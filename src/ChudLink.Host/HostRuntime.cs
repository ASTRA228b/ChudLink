using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using ChudLink.Core;
using ChudLink.Protocol;
using Microsoft.AspNetCore.RateLimiting;

namespace ChudLink.Host;

public sealed class PendingPair
{
    public required PairRequest Request { get; init; }
    public string Ticket { get; } = Credentials.NewToken();
    public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(2);
    public string Status { get; set; } = "pending";
    public string? Token { get; set; }
    public override string ToString() => $"{Request.Name} · {Request.Platform} · {Status}";
}
sealed class Connection(WebSocket socket)
{
    public WebSocket Socket { get; } = socket;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
    public int Messages;
    public DateTimeOffset Window = DateTimeOffset.UtcNow;
}
public sealed class HostRuntime : IAsyncDisposable
{
    public Store Store { get; }
    public History<string> Logs { get; } = new(300);
    public ConcurrentDictionary<string, PendingPair> Pairings { get; } = new();
    public ConcurrentDictionary<string, string> Shared { get; } = new();
    public ConcurrentDictionary<string, string> ProviderValues { get; } = new();
    public ConcurrentDictionary<string, Widget> CustomWidgets { get; } = new();
    public WorkerManager Workers { get; }
    readonly ConcurrentDictionary<string, Connection> connections = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> overrides = new();
    readonly ConcurrentDictionary<string, string> activeContexts = new();
    readonly ConcurrentDictionary<string, string> patchCache = new();
    readonly CancellationTokenSource stop = new();
    readonly object sync = new();
    readonly SemaphoreSlim buildGate = new(1, 1);
    WebApplication? app;
    FileStream? instanceLock;
    System.Security.Cryptography.X509Certificates.X509Certificate2? certificate;
    Task? discovery, telemetry;
    string code = "";
    DateTimeOffset codeExpires;
    public string PairingCode => DateTimeOffset.UtcNow < codeExpires ? code : "Expired — generate a new code";
    public string Fingerprint { get; private set; } = "";
    public string Address { get; private set; } = "";
    public bool Running => app != null;
    public long BytesSent, BytesReceived, UpdatesSent;
    public Dictionary<string, (string Text, double Value)> Latest { get; private set; } = new();
    public HostRuntime(string? dataRoot = null) { Store = new(dataRoot); Workers = new(this); }
    public void Log(string message) => Logs.Add($"{DateTime.Now:HH:mm:ss}  {message[..Math.Min(message.Length, 4000)]}");
    public DeviceRecord[] Devices() { lock (sync) return Store.Devices.ToArray(); }
    public string NewPairingCode()
    {
        lock (sync) { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); codeExpires = DateTimeOffset.UtcNow.AddMinutes(2); return code; }
    }
    public async Task StartAsync(string address, int? port = null)
    {
        if (Running) return;
        var ip = IPAddress.Parse(address);
        if (!LocalNetwork.Addresses().Contains(ip) || !LocalNetwork.IsPrivate(ip)) throw new InvalidOperationException("Select a local private IPv4 interface");
        instanceLock = new FileStream(Path.Combine(Store.Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var cert = certificate = Store.Certificate();
            Fingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData));
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = 16 * 1024; k.Listen(ip, port ?? Store.Settings.Port, l => l.UseHttps(cert)); });
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = 429;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
            var web = builder.Build();
            web.Use(async (ctx, next) =>
            {
                if (ctx.Connection.RemoteIpAddress is not { } remote || !LocalNetwork.IsPrivate(remote)) { ctx.Response.StatusCode = 403; return; }
                ctx.Response.Headers.CacheControl = "no-store";
                try { await next(); }
                catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException) { ctx.Response.StatusCode = 400; }
            });
            web.UseRateLimiter(); web.UseWebSockets(new() { KeepAliveInterval = TimeSpan.FromSeconds(20) });
            web.MapGet("/health", () => new { app = "ChudLink", version = "0.1.0-alpha", protocol = Wire.Version });
            web.MapPost("/pair", (PairRequest request) =>
            {
                lock (sync)
                {
                    if (request.Protocol != Wire.Version) return Results.BadRequest(new { error = "Unsupported protocol" });
                    if (DateTimeOffset.UtcNow >= codeExpires || request.Code != code || request.DeviceId.Length is < 16 or > 100 || request.Name.Length is < 1 or > 80 || request.Platform.Length > 80 || Pairings.Count > 16)
                        return Results.BadRequest(new { error = "Invalid or expired pairing request" });
                    var pending = new PendingPair { Request = request };
                    Pairings[pending.Ticket] = pending; codeExpires = DateTimeOffset.MinValue; // one use
                    Log($"Pair request: {request.Name}. Awaiting local approval.");
                    return Results.Json(new PairTicket(pending.Ticket, pending.Status), Wire.Json);
                }
            });
            web.MapGet("/pair/status", (HttpContext context) =>
            {
                var ticket = Bearer(context);
                lock (sync)
                {
                    if (ticket == null || !Pairings.TryGetValue(ticket, out var p) || p.Expires < DateTimeOffset.UtcNow) return Results.NotFound();
                    var result = new PairResult(p.Status, p.Token, p.Request.DeviceId);
                    if (p.Status != "pending") Pairings.TryRemove(ticket, out _);
                    return Results.Json(result, Wire.Json);
                }
            });
            web.Map("/connect", ConnectAsync);
            await web.StartAsync(); app = web;
            Address = $"https://{ip}:{port ?? Store.Settings.Port}";
            Store.Settings.Address = address; Store.Save();
            discovery = DiscoverAsync(ip, stop.Token); telemetry = TelemetryLoopAsync(stop.Token);
            Log($"Listening on {Address}");
        }
        catch { certificate?.Dispose(); certificate = null; instanceLock.Dispose(); instanceLock = null; throw; }
    }
    static string? Bearer(HttpContext context)
    {
        var h = context.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.Ordinal) && h.Length <= 160 ? h[7..] : null;
    }
    public void Approve(string ticket, bool approve)
    {
        lock (sync)
        {
            if (!Pairings.TryGetValue(ticket, out var p) || p.Status != "pending" || p.Expires < DateTimeOffset.UtcNow) return;
            p.Status = approve ? "approved" : "denied";
            if (!approve) return;
            var old = Store.Devices.FirstOrDefault(d => d.Id == p.Request.DeviceId);
            if (old != null) { Store.Devices.Remove(old); if (connections.TryRemove(old.Id, out var connection)) connection.Socket.Abort(); }
            p.Token = Credentials.NewToken();
            Store.Devices.Add(new() { Id = p.Request.DeviceId, Name = p.Request.Name, TokenHash = Credentials.Hash(p.Token) }); Store.Save();
            Log($"Paired {p.Request.Name}");
        }
    }
    public void Revoke(string id)
    {
        lock (sync) { Store.Devices.RemoveAll(d => d.Id == id); Store.Save(); }
        if (connections.TryRemove(id, out var connection)) connection.Socket.Abort();
        Log($"Revoked device {id}");
    }
    public void ConfigureDevice(string id, string name, string group, HashSet<string> permissions)
    {
        lock (sync) { var d = Store.Devices.First(x => x.Id == id); d.Name = name[..Math.Min(name.Length, 80)]; d.Group = group[..Math.Min(group.Length, 80)]; d.Permissions = permissions; Store.Save(); }
    }
    public async Task AssignAsync(string deviceId, string dashboardId, bool manual = true)
    {
        if (!Store.Dashboards.TryGetValue(dashboardId, out var d)) throw new InvalidDataException("Unknown dashboard");
        lock (sync) { var device = Store.Devices.FirstOrDefault(x => x.Id == deviceId) ?? throw new InvalidDataException("Unknown device"); device.DashboardId = dashboardId; Store.Save(); }
        if (manual) overrides[deviceId] = DateTimeOffset.UtcNow.AddMinutes(15);
        ClearPatchCache(deviceId);
        await SendAsync(deviceId, "dashboard", d);
    }
    public async Task PublishAsync(Dashboard dashboard)
    {
        dashboard = dashboard.Clone();
        foreach (var p in dashboard.Pages)
        {
            for (var i = 0; i < p.Widgets.Count; i++)
            {
                var custom = p.Widgets[i];
                if (custom.Kind != WidgetKind.Custom || !custom.Properties.TryGetValue("type", out var type) || !CustomWidgets.TryGetValue(type, out var fallback)) continue;
                var rendered = Wire.Parse<Widget>(Wire.Serialize(fallback));
                rendered.Id = custom.Id; rendered.Label = custom.Label; rendered.X = custom.X; rendered.Y = custom.Y; rendered.Width = custom.Width; rendered.Height = custom.Height;
                rendered.Layer = custom.Layer; rendered.ParentId = custom.ParentId; rendered.Properties = new(custom.Properties);
                if (custom.Text.Length > 0) rendered.Text = custom.Text;
                if (custom.DataKey != null) rendered.DataKey = custom.DataKey;
                if (custom.Bindings.Count > 0) rendered.Bindings = custom.Bindings;
                p.Widgets[i] = rendered;
            }
        }
        Store.SaveDashboard(dashboard);
        foreach (var d in Devices().Where(d => d.DashboardId == dashboard.Id)) { ClearPatchCache(d.Id); await SendAsync(d.Id, "dashboard", dashboard); }
    }
    void ClearPatchCache(string id) { foreach (var key in patchCache.Keys.Where(k => k.StartsWith(id + ":", StringComparison.Ordinal))) patchCache.TryRemove(key, out _); }
    async Task ConnectAsync(HttpContext ctx)
    {
        var token = Bearer(ctx); DeviceRecord? device;
        lock (sync) device = token == null ? null : Store.Devices.FirstOrDefault(d => Credentials.Verify(token, d.TokenHash));
        if (device == null) { ctx.Response.StatusCode = 401; return; }
        if (ctx.Request.Headers["X-ChudLink-Protocol"] != Wire.Version.ToString()) { ctx.Response.StatusCode = 426; return; }
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(ws);
        if (connections.TryRemove(device.Id, out var old)) old.Socket.Abort();
        connections[device.Id] = connection;
        device.Connected = true; device.LastSeen = DateTimeOffset.UtcNow; ClearPatchCache(device.Id);
        try
        {
            if (Store.Dashboards.TryGetValue(device.DashboardId, out var dashboard)) await SendAsync(device.Id, "dashboard", dashboard);
            await Workers.DevicesChangedAsync();
            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var length = 0; WebSocketReceiveResult received;
                do
                {
                    if (length >= buffer.Length) throw new InvalidDataException("Message too large");
                    received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), stop.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    if (received.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Text messages required");
                    length += received.Count;
                } while (!received.EndOfMessage);
                Interlocked.Add(ref BytesReceived, length);
                if ((DateTimeOffset.UtcNow - connection.Window).TotalSeconds >= 1) { connection.Window = DateTimeOffset.UtcNow; connection.Messages = 0; }
                if (++connection.Messages > 40) throw new InvalidDataException("Device rate limit");
                var envelope = Wire.Parse<Envelope>(Encoding.UTF8.GetString(buffer, 0, length));
                device.LastSeen = DateTimeOffset.UtcNow;
                switch (envelope.Type)
                {
                    case "hello":
                        var hello = envelope.Read<DeviceHello>();
                        if (hello.Name.Length > 80 || hello.Platform.Length > 80 || hello.Version.Length > 40 || hello.Capabilities.Length > 20 || hello.Battery is < -1 or > 100 || !Enum.IsDefined(hello.Profile)) throw new InvalidDataException("Invalid device information");
                        device.Info = hello; break;
                    case "heartbeat":
                        connection.LastHeartbeat = DateTimeOffset.UtcNow;
                        await SendAsync(device.Id, "heartbeat", envelope.Payload); break;
                    case "pong":
                        var ticks = envelope.Payload.GetProperty("sent").GetInt64();
                        device.LatencyMs = Math.Clamp((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ticks)).TotalMilliseconds, 0, 60000); break;
                    case "touch": await HandleTouchAsync(device, envelope.Read<TouchEvent>()); break;
                    case "sensor":
                        if (device.Permissions.Contains("sensors")) await Workers.BroadcastAsync("sensor", new { deviceId = device.Id, sensor = envelope.Read<SensorEvent>() }, "sensors"); break;
                    default: throw new InvalidDataException("Unknown message");
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log($"{device.Name}: {ex.Message}"); }
        finally
        {
            if (connections.TryGetValue(device.Id, out var current) && ReferenceEquals(current, connection)) { connections.TryRemove(device.Id, out _); device.Connected = false; }
            await Workers.DevicesChangedAsync();
        }
    }
    async Task HandleTouchAsync(DeviceRecord device, TouchEvent touch)
    {
        if (touch.Event.Length > 32 || !double.IsFinite(touch.Value) || !Store.Dashboards.TryGetValue(device.DashboardId, out var dashboard)) return;
        var widget = dashboard.Pages.SelectMany(p => p.Widgets).FirstOrDefault(w => w.Id == touch.WidgetId);
        if (widget == null) return;
        foreach (var binding in widget.Bindings.Where(b => b.Event == touch.Event || b.Event == "press" && touch.Event == "change"))
        {
            try
            {
                switch (binding.Action)
                {
                    case "counter" when device.Permissions.Contains("counter"):
                        Shared.AddOrUpdate("counter", "1", (_, v) => (long.TryParse(v, out var n) && n < long.MaxValue ? n + 1 : 1).ToString()); break;
                    case "media" when device.Permissions.Contains("media"): WindowsActions.Media(binding.Argument ?? "play"); break;
                    case "volume" when device.Permissions.Contains("media"): WindowsActions.SetVolume((float)Math.Clamp(touch.Value / 100, 0, 1)); break;
                    case "navigate" when device.Permissions.Contains("dashboard"): await AssignAsync(device.Id, binding.Argument ?? "hardware"); break;
                    case "mesh" when device.Permissions.Contains("mesh"):
                        var parts = (binding.Argument ?? "").Split('|', 2);
                        if (parts.Length == 2 && Devices().Any(d => d.Id == parts[0] && d.Group == device.Group && d.Group.Length > 0)) await AssignAsync(parts[0], parts[1]); break;
                    case "approved" when device.Permissions.Contains("actions"):
                        _ = Task.Run(async () => { try { await RunApprovedActionAsync(binding.Argument ?? "", device.Id); } catch (Exception ex) { Log("Approved action: " + ex.Message); } }); break;
                    default: await Workers.InvokeActionAsync(binding.Action, device, binding.Argument); break;
                }
            }
            catch (Exception ex) { Log($"Action {binding.Action}: {ex.Message}"); await SendAsync(device.Id, "notification", "Action failed. Check host diagnostics."); }
        }
        await Workers.BroadcastAsync("touch", new { deviceId = device.Id, touch }, "dashboard");
    }
    public async Task RunApprovedActionAsync(string id, string deviceId)
    {
        var device = Devices().FirstOrDefault(d => d.Id == deviceId);
        if (device == null || !device.Permissions.Contains("actions")) throw new UnauthorizedAccessException("Device action permission required");
        var a = Store.Settings.Actions.FirstOrDefault(x => x.Id == id) ?? throw new InvalidDataException("Action is not approved on the host");
        switch (a.Kind)
        {
            case "shortcut": WindowsActions.Shortcut(a.Target); break;
            case "sound": WindowsActions.Sound(a.Target); break;
            case "launch": System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(a.Target, a.Arguments) { UseShellExecute = true }); break;
            case "build":
                if (!await buildGate.WaitAsync(0, stop.Token)) throw new InvalidOperationException("Another approved build is already running");
                try
                {
                var start = new System.Diagnostics.ProcessStartInfo(a.Target, a.Arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(a.Target)! };
                using (var p = System.Diagnostics.Process.Start(start) ?? throw new IOException("Build failed to start"))
                {
                    void Line(object _, System.Diagnostics.DataReceivedEventArgs e) { if (e.Data is { } text) { ProviderValues["build.output"] = text[..Math.Min(text.Length, 1000)]; Log(text[..Math.Min(text.Length, 1000)]); } }
                    p.OutputDataReceived += Line; p.ErrorDataReceived += Line; p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    try { await p.WaitForExitAsync(stop.Token).WaitAsync(TimeSpan.FromMinutes(5), stop.Token); }
                    catch { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
                    ProviderValues["build.output"] = $"Build exited {p.ExitCode}";
                }
                }
                finally { buildGate.Release(); }
                break;
            default: throw new InvalidDataException("Unsupported approved action kind");
        }
    }
    public async Task SendAsync<T>(string deviceId, string type, T payload)
    {
        if (!connections.TryGetValue(deviceId, out var c) || c.Socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(Wire.Serialize(Envelope.Create(type, payload)));
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Outbound message too large");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await c.Gate.WaitAsync(timeout.Token);
            try { await c.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token); Interlocked.Add(ref BytesSent, bytes.Length); }
            finally { c.Gate.Release(); }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { c.Socket.Abort(); }
    }
    async Task DiscoverAsync(IPAddress address, CancellationToken ct)
    {
        if (IPAddress.IsLoopback(address)) return;
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Wire.DiscoveryPort));
            var response = Encoding.UTF8.GetBytes(Wire.Serialize(new DiscoveryReply(Environment.MachineName, Address, Fingerprint)));
            var recent = new Dictionary<IPAddress, DateTimeOffset>();
            while (!ct.IsCancellationRequested)
            {
                var received = await udp.ReceiveAsync(ct);
                if (!LocalNetwork.IsPrivate(received.RemoteEndPoint.Address) || Encoding.UTF8.GetString(received.Buffer) != Wire.DiscoveryQuery) continue;
                if (recent.TryGetValue(received.RemoteEndPoint.Address, out var time) && (DateTimeOffset.UtcNow - time).TotalSeconds < 2) continue;
                if (recent.Count > 100) recent.Clear(); recent[received.RemoteEndPoint.Address] = DateTimeOffset.UtcNow;
                await udp.SendAsync(response, received.RemoteEndPoint, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("Discovery unavailable: " + ex.Message + ". Manual address still works."); }
    }
    async Task TelemetryLoopAsync(CancellationToken ct)
    {
        var sampler = new Telemetry(); int iteration = 0;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var p in Pairings.Where(p => p.Value.Expires < DateTimeOffset.UtcNow)) Pairings.TryRemove(p.Key, out _);
                if (iteration % 10 == 0) await Workers.CheckLimitsAsync();
                if (connections.Count > 0)
                {
                    Latest = sampler.Sample();
                    if (iteration % 10 == 0)
                    {
                        foreach (var endpoint in Store.Settings.Endpoints.Take(16).ToArray())
                        {
                            try
                            {
                                if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) continue;
                                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                                var value = $"{endpoint.Name}: {(int)response.StatusCode}";
                                if (response.IsSuccessStatusCode && endpoint.JsonProperty is { Length: > 0 })
                                {
                                    await using var input = await response.Content.ReadAsStreamAsync(ct); var bytes = new byte[65537]; var count = 0;
                                    while (count < bytes.Length) { var n = await input.ReadAsync(bytes.AsMemory(count), ct); if (n == 0) break; count += n; }
                                    if (count > 65536) throw new InvalidDataException("JSON response exceeds 64 KB");
                                    using var json = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(0, count));
                                    if (json.RootElement.TryGetProperty(endpoint.JsonProperty, out var property)) value = $"{endpoint.Name}: {property}";
                                }
                                ProviderValues["service." + endpoint.Id] = value;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { ProviderValues["service." + endpoint.Id] = endpoint.Name + ": unavailable"; }
                        }
                        ProviderValues["service.summary"] = Store.Settings.Endpoints.Count == 0 ? "No services configured" : string.Join("\n", ProviderValues.Where(p => p.Key.StartsWith("service.") && p.Key != "service.summary").Select(p => p.Value));
                    }
                    foreach (var device in Devices().Where(d => d.Connected))
                    {
                        if (!connections.TryGetValue(device.Id, out var connection)) continue;
                        if ((DateTimeOffset.UtcNow - connection.LastHeartbeat).TotalSeconds > 65) { connection.Socket.Abort(); continue; }
                        var every = device.Info?.Profile switch { RenderProfile.Legacy => 5, RenderProfile.Performance => 1, _ => 2 };
                        if (iteration % every != 0) continue;
                        if (Store.Dashboards.TryGetValue(device.DashboardId, out var dashboard))
                        {
                            List<WidgetPatch> patches = [];
                            foreach (var w in dashboard.Pages.SelectMany(p => p.Widgets).Where(w => w.DataKey != null))
                            {
                                var key = w.DataKey!; string? text = null; double? value = null;
                                if (Latest.TryGetValue(key, out var sample)) { text = sample.Text; value = sample.Value; }
                                else if (key == "device.latency") { text = $"{device.LatencyMs:F0} ms"; value = device.LatencyMs; }
                                else if (key.StartsWith("shared.") && Shared.TryGetValue(key[7..], out var shared)) { text = shared; if (double.TryParse(shared, out var number)) value = number; }
                                else if (ProviderValues.TryGetValue(key, out var supplied)) text = supplied;
                                if (text == null) continue;
                                var cacheKey = device.Id + ":" + w.Id; var signature = text + "|" + value;
                                if (w.Kind == WidgetKind.Graph || !patchCache.TryGetValue(cacheKey, out var previous) || previous != signature) { patches.Add(new(w.Id, text, value)); patchCache[cacheKey] = signature; }
                            }
                            if (patches.Count > 0) { await SendAsync(device.Id, "patch", patches); Interlocked.Add(ref UpdatesSent, patches.Count); }
                        }
                        if (iteration % 10 == 0) await SendAsync(device.Id, "ping", new { sent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                        var process = WindowsActions.ForegroundProcess();
                        if (process != null && (!overrides.TryGetValue(device.Id, out var until) || until < DateTimeOffset.UtcNow))
                        {
                            var rule = Store.Settings.ContextRules.FirstOrDefault(r => string.Equals(r.Process, process, StringComparison.OrdinalIgnoreCase) && (r.DeviceId == null || r.DeviceId == device.Id));
                            if (rule != null && (!activeContexts.TryGetValue(device.Id, out var context) || context != process)) { await AssignAsync(device.Id, rule.DashboardId, false); activeContexts[device.Id] = process; }
                            if (rule == null) activeContexts.TryRemove(device.Id, out _);
                        }
                    }
                }
                iteration++; await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("Telemetry stopped: " + ex); }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        foreach (var c in connections.Values) c.Socket.Abort();
        await Workers.DisposeAsync();
        if (app != null) { await app.StopAsync(); await app.DisposeAsync(); app = null; }
        if (telemetry != null) await telemetry; if (discovery != null) await discovery;
        Store.Save(); instanceLock?.Dispose(); certificate?.Dispose(); stop.Dispose();
    }
}
