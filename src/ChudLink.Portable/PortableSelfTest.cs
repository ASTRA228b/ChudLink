using System.Net;
using System.Net.Sockets;
using ChudLink.Host;
using ChudLink.Phone;
using ChudLink.Protocol;

namespace ChudLink.Portable;

static class PortableSelfTest
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "ChudLink-Portable-SelfTest", Guid.NewGuid().ToString("N"));
            await using var host = DraftHost.Create(root);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            await host.StartAsync("127.0.0.1", port);
            using var phone = new PhoneConnection(Guid.NewGuid().ToString("N"), host.Address, host.Fingerprint, "");
            using var http = phone.Client();
            if (!(await http.GetStringAsync("/health")).Contains("ChudLink")) throw new Exception("Pinned health failed");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var paired = phone.PairAsync(host.NewPairingCode(), "Portable self-test", "Protocol self-test", deadline.Token);
            while (host.Pairings.IsEmpty) await Task.Delay(50, deadline.Token);
            var pending = host.Pairings.Values.Single();
            if (pending.Token != null) throw new Exception("Credential issued before approval");
            host.Approve(pending.Ticket, true); await paired;
            var received = new TaskCompletionSource<Dashboard>(TaskCreationOptions.RunContinuationsAsynchronously);
            phone.Message += e => { if (e.Type == "dashboard") received.TrySetResult(e.Read<Dashboard>()); };
            var running = phone.RunAsync(() => new("Portable self-test", "Simulation", "draft", 100, "Portrait", ["canvas"], RenderProfile.Balanced), deadline.Token);
            try
            {
                var dashboard = await received.Task.WaitAsync(deadline.Token);
                if (dashboard.Id != "hardware" || dashboard.Pages[0].Widgets.Any(w => w.DataKey is "host.cpu" or "host.ram")) throw new Exception("Draft dashboard contains unsupported metrics");
                using var telemetry = new Telemetry();
                var sample = telemetry.Sample(new HashSet<string> { "host.processMemory", "host.uptime" });
                if (!sample.ContainsKey("host.processMemory") || !sample.ContainsKey("host.uptime")) throw new Exception("Real portable telemetry missing");
                var failed = false; try { WindowsActions.Media("play"); } catch (PlatformNotSupportedException) { failed = true; }
                if (!failed) throw new Exception("Unsupported media action did not reject");
                host.Revoke(phone.DeviceId);
                using var revoked = new HttpRequestMessage(HttpMethod.Get, "/connect"); revoked.Headers.Authorization = new("Bearer", phone.Token);
                using var rejected = await http.SendAsync(revoked, deadline.Token);
                if (rejected.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Revoked portable credential was not rejected");
            }
            finally { await deadline.CancelAsync(); phone.Dispose(); try { await running; } catch (OperationCanceledException) { } }
            Console.WriteLine("PASS: portable host pinned TLS, explicit pairing approval, WebSocket dashboard, supported telemetry, unavailable-action rejection, revocation. Native Linux/macOS GUI execution remains untested.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
