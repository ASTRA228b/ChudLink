using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using ChudLink.Protocol;

namespace ChudLink.Phone;

public sealed class PhoneConnection : IDisposable
{
    readonly SemaphoreSlim sendGate = new(1, 1);
    ClientWebSocket? socket;
    long lastReceive;
    public string DeviceId { get; }
    public string Address { get; set; }
    public string Fingerprint { get; }
    public string Token { get; set; }
    public long Sent, Received;
    public bool Connected => socket?.State == WebSocketState.Open;
    public event Action<Envelope>? Message;
    public event Action<string>? Status;
    public PhoneConnection(string id, string address, string fingerprint, string token)
    {
        DeviceId = id; Address = NormalizeAddress(address); Fingerprint = NormalizeFingerprint(fingerprint); Token = token;
        if (Fingerprint.Length != 64 || !Fingerprint.All(Uri.IsHexDigit)) throw new InvalidDataException("Enter the complete SHA-256 fingerprint shown on the PC");
    }
    public static string NormalizeAddress(string value)
    {
        if (!value.Contains("://")) value = "https://" + value;
        var builder = new UriBuilder(value);
        if (builder.Scheme != "https" || !IPAddress.TryParse(builder.Host, out var ip) || !Private(ip) || builder.UserName.Length > 0 || builder.Password.Length > 0)
            throw new InvalidDataException("Use the PC's private IPv4 address with HTTPS");
        if (builder.Port == 443) builder.Port = Wire.Port;
        builder.Path = ""; builder.Query = ""; builder.Fragment = "";
        return builder.Uri.ToString().TrimEnd('/');
    }
    static bool Private(IPAddress ip) { var b = ip.GetAddressBytes(); return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 127 || b[0] == 169 && b[1] == 254); }
    public static string NormalizeFingerprint(string text) => text.Replace(":", "").Replace(" ", "").Replace("\n", "").Replace("\r", "").ToUpperInvariant();
    bool Validate(System.Security.Cryptography.X509Certificates.X509Certificate? cert) => cert != null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), Convert.FromHexString(Fingerprint));
    public HttpClient Client()
    {
        var handler = new SocketsHttpHandler { SslOptions = new() { RemoteCertificateValidationCallback = (_, certificate, _, _) => Validate(certificate) } };
        return new(handler) { BaseAddress = new Uri(Address), Timeout = TimeSpan.FromSeconds(10) };
    }
    public async Task<string> PairAsync(string code, string name, string platform, CancellationToken ct)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/pair", new PairRequest(code, DeviceId, name, platform, Wire.Version), Wire.Json, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("Pairing code expired or rejected. Generate a new code on the PC.");
        var ticket = Wire.Parse<PairTicket>(await response.Content.ReadAsStringAsync(ct));
        Status?.Invoke("Approve this phone in the PC's Devices page…");
        for (var i = 0; i < 55; i++)
        {
            await Task.Delay(2200, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/pair/status");
            request.Headers.Authorization = new("Bearer", ticket.Ticket);
            using var poll = await client.SendAsync(request, ct);
            if (!poll.IsSuccessStatusCode) throw new InvalidDataException("Pairing expired; try again");
            var result = Wire.Parse<PairResult>(await poll.Content.ReadAsStringAsync(ct));
            if (result.Status == "denied") throw new InvalidDataException("The PC declined this phone");
            if (result.Status == "approved" && result.Token != null) return Token = result.Token;
        }
        throw new TimeoutException("Host approval timed out");
    }
    public async Task RunAsync(Func<DeviceHello> hello, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket(); socket = ws;
                ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => Validate(cert);
                ws.Options.SetRequestHeader("Authorization", "Bearer " + Token); ws.Options.SetRequestHeader("X-ChudLink-Protocol", Wire.Version.ToString());
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                Status?.Invoke("Connecting securely…");
                using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connecting.CancelAfter(TimeSpan.FromSeconds(12));
                    await ws.ConnectAsync(new Uri(Address.Replace("https://", "wss://") + "/connect"), connecting.Token);
                }
                Interlocked.Exchange(ref lastReceive, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                attempt = 0; Status?.Invoke("CONNECTED"); await SendAsync("hello", hello(), ct);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeat = HeartbeatsAsync(hello, session.Token);
                try
                {
                    var buffer = new byte[1024 * 1024];
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        int length = 0; WebSocketReceiveResult frame;
                        do
                        {
                            if (length == buffer.Length) throw new InvalidDataException("Host message exceeds 1 MB");
                            frame = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
                            if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("Host disconnected");
                            length += frame.Count;
                        } while (!frame.EndOfMessage);
                        Interlocked.Add(ref Received, length);
                        Interlocked.Exchange(ref lastReceive, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        var message = Wire.Parse<Envelope>(Encoding.UTF8.GetString(buffer, 0, length));
                        if (message.Type == "ping") await SendAsync("pong", message.Payload, ct);
                        else Message?.Invoke(message);
                    }
                }
                finally { session.Cancel(); try { await heartbeat; } catch (OperationCanceledException) { } }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Status?.Invoke("OFFLINE · " + ex.Message); }
            finally { socket = null; }
            await Task.Delay(Math.Min(15000, 1000 * (1 << Math.Min(attempt++, 4))), ct);
        }
    }
    async Task HeartbeatsAsync(Func<DeviceHello> hello, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(20000, ct);
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Interlocked.Read(ref lastReceive) > 65000) { socket?.Abort(); return; }
            try { await SendAsync("heartbeat", new { sent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct); await SendAsync("hello", hello(), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Status?.Invoke(ex.Message); socket?.Abort(); return; }
        }
    }
    public async Task SendAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        var ws = socket;
        if (ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(Envelope.Create(type, payload)));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await sendGate.WaitAsync(timeout.Token);
        try { if (ws.State == WebSocketState.Open) { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token); Interlocked.Add(ref Sent, bytes.Length); } }
        finally { sendGate.Release(); }
    }
    public static async Task<DiscoveryReply[]> DiscoverAsync(CancellationToken ct)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var query = Encoding.UTF8.GetBytes(Wire.DiscoveryQuery);
        await udp.SendAsync(query, new IPEndPoint(IPAddress.Broadcast, Wire.DiscoveryPort), ct);
        List<DiscoveryReply> results = [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(3000);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                if (packet.Buffer.Length > 2048) continue;
                var reply = Wire.Parse<DiscoveryReply>(Encoding.UTF8.GetString(packet.Buffer));
                if (reply.Protocol == Wire.Version && !results.Any(r => r.Address == reply.Address)) results.Add(reply);
            }
        }
        catch (OperationCanceledException) { }
        return results.ToArray();
    }
    public void Dispose() { socket?.Abort(); }
}
