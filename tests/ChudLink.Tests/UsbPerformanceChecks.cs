using System.Net;
using System.Net.Sockets;
using System.Text;
using ChudLink.Host;

static class UsbPerformanceChecks
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static async Task StableScansAsync(HostRuntime host)
    {
        var fake = new FakeUsbAdb(); var clock = new Clock(); var pathQueries = 0; var reverseQueries = 0;
        Task<AdbResult> Command(string[] args, CancellationToken ct)
        {
            if (args[^1] == "get-devpath") pathQueries++;
            if (args.Length >= 4 && args[^2] == "reverse" && args[^1] == "--list") reverseQueries++;
            return fake.Run(args, ct);
        }
        await using var usb = new UsbAutoConnect(host, Command, timeProvider: clock);
        await usb.ScanAsync(background: true);
        for (var i = 0; i < 10; i++) { clock.Now = clock.Now.AddMilliseconds(500); await usb.ScanAsync(background: true); }
        if (pathQueries != 1 || reverseQueries != 1 || fake.Opens != 1) throw new Exception("Stable scans repeated device-path, tunnel, or activity processes");
        fake.Mapped = false; clock.Now = clock.Now.AddSeconds(16); await usb.ScanAsync(background: true);
        if (!fake.Mapped || fake.Opens != 2 || reverseQueries != 2 || pathQueries != 1) throw new Exception("Periodic health check did not repair the lost tunnel");
        fake.Attached = false; await usb.ScanAsync(background: true); fake.Attached = true; await usb.ScanAsync(background: true);
        if (fake.Opens != 3 || pathQueries != 2) throw new Exception("Replug did not invalidate the per-attachment cache");
        await usb.ScanAsync(retry: true, background: true);
        if (fake.Opens != 4) throw new Exception("Explicit retry did not bypass the cache");
        Console.WriteLine("USB stable-scan proof: ten background scans used zero additional path/tunnel/activity commands; repair, replug and retry remained functional.");
    }

    public static async Task SmartSocketAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(); await using var stream = accepted.GetStream();
            var request = new byte[18]; await stream.ReadExactlyAsync(request);
            if (Encoding.ASCII.GetString(request) != "000Ehost:devices-l") throw new Exception("Unexpected ADB request");
            const string devices = "TEST_USB\tdevice model:Test transport_id:1\n";
            var reply = Encoding.ASCII.GetBytes("OKAY" + devices.Length.ToString("X4") + devices);
            // Split packets to exercise exact-length framing, rather than relying on one TCP read.
            await stream.WriteAsync(reply.AsMemory(0, 3)); await stream.WriteAsync(reply.AsMemory(3));
        });
        var list = await UsbAutoConnect.ReadDeviceListAsync(port);
        await server;
        if (list.ExitCode != 0 || !list.Output.Contains("TEST_USB\tdevice")) throw new Exception("Smart socket did not return the device list");

        var malformed = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(); await using var stream = accepted.GetStream();
            var request = new byte[18]; await stream.ReadExactlyAsync(request); await stream.WriteAsync(Encoding.ASCII.GetBytes("OKAYZZZZ"));
        });
        var rejected = false;
        try { await UsbAutoConnect.ReadDeviceListAsync(port); } catch (IOException) { rejected = true; }
        await malformed;
        if (!rejected) throw new Exception("Malformed ADB framing must fail closed");
    }

    public static async Task LifecycleAsync(HostRuntime host)
    {
        var fake = new FakeUsbAdb(); var firstPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var polls = 0;
        async Task<AdbResult> Command(string[] args, CancellationToken ct)
        {
            if (args.SequenceEqual(new[] { "devices", "-l" }) && Interlocked.Increment(ref polls) == 1)
            {
                firstPoll.TrySetResult(); await Task.Delay(Timeout.Infinite, ct);
            }
            var result = await fake.Run(args, ct);
            if (args.Contains("start")) restarted.TrySetResult();
            return result;
        }
        await using var usb = new UsbAutoConnect(host, Command);
        usb.Start(); await firstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = usb.StopAsync(); usb.Start();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5)); await restarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!usb.Enabled || fake.Opens != 1) throw new Exception("A rapid reconnect did not restart exactly one USB watcher after cleanup");
        await usb.DisposeAsync();
        if (usb.Enabled || fake.Mapped) throw new Exception("Disposal left a USB watcher or mapping alive");
        var rejected = false; try { usb.Start(); } catch (ObjectDisposedException) { rejected = true; }
        if (!rejected) throw new Exception("A disposed watcher restarted");
    }
}
