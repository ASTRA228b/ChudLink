using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ChudLink.Protocol;

namespace ChudLink.Host;

public sealed record AdbResult(int ExitCode, string Output);

/// <summary>Optional USB transport. Never enables debugging, authorizes a PC, or installs an APK.</summary>
public sealed class UsbAutoConnect : IAsyncDisposable
{
    readonly HostRuntime host;
    readonly Func<string[], CancellationToken, Task<AdbResult>> command;
    readonly bool includeEmulators;
    readonly bool useSmartSocket;
    readonly TimeProvider timeProvider;
    readonly object lifecycle = new();
    readonly SemaphoreSlim gate = new(1, 1);
    readonly HashSet<string> opened = [];
    readonly HashSet<string> tunnels = [];
    readonly Dictionary<string, bool> wired = [];
    readonly Dictionary<string, string> attachments = [];
    readonly Dictionary<string, DateTimeOffset> nextTunnelCheck = [];
    readonly Dictionary<string, DateTimeOffset> nextLaunchAttempt = [];
    HashSet<string> connectedDevices = [];
    CancellationTokenSource? stop;
    Task? loop;
    Task? stoppingTask;
    bool restartRequested, disposed;
    string status = "USB auto-connect is off";
    public string Status => Volatile.Read(ref status);
    public bool Enabled => stop is { IsCancellationRequested: false };

    public UsbAutoConnect(HostRuntime host, Func<string[], CancellationToken, Task<AdbResult>>? command = null, bool includeEmulators = false, TimeProvider? timeProvider = null)
    { this.host = host; this.command = command ?? RunAdbAsync; this.includeEmulators = includeEmulators; useSmartSocket = command == null; this.timeProvider = timeProvider ?? TimeProvider.System; }

    void Report(string value)
    {
        if (Status == value) return;
        Volatile.Write(ref status, value); host.Log(value);
    }

    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stoppingTask is { IsCompleted: false }) { restartRequested = true; return; }
            StartLoop();
        }
    }
    void StartLoop()
    {
        if (Enabled) return;
        if (!host.Running) throw new InvalidOperationException("Start the host first.");
        stop = new(); var ct = stop.Token;
        loop = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await ScanAsync(ct, background: true); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception ex) { Report(ex is FileNotFoundException ? "USB tools missing. Run bootstrap.ps1 -Android and rebuild, or install Google Android Platform Tools." : "USB connection unavailable. Check the cable, phone approval, and Android USB driver."); }
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }, ct);
    }

    public async Task ScanAsync(CancellationToken ct = default, bool retry = false, bool background = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (retry) { opened.Clear(); wired.Clear(); nextTunnelCheck.Clear(); nextLaunchAttempt.Clear(); }
            var now = timeProvider.GetUtcNow();
            var currentConnections = host.Devices().Where(d => d.Connected).Select(d => d.Id).ToHashSet();
            if (connectedDevices.Any(id => !currentConnections.Contains(id))) nextTunnelCheck.Clear();
            connectedDevices = currentConnections;
            AdbResult list;
            if (background && useSmartSocket)
            {
                try { list = await ReadDeviceListAsync(ct: ct); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    // The normal scan is a tiny local request. Launch adb only to start/recover its daemon.
                    list = await command(["devices", "-l"], ct);
                }
            }
            else list = await command(["devices", "-l"], ct);
            if (list.ExitCode != 0) { Report("USB tools could not list phones. Check Android Platform Tools and the USB driver."); return; }
            var seen = new HashSet<string>(); var ready = 0; var needsApproval = false; var offline = false; var failed = false;
            foreach (var line in list.Output.Split('\n'))
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !Regex.IsMatch(fields[0], "^[A-Za-z0-9._:-]{1,160}$") || fields[1] is not ("device" or "unauthorized" or "offline")) continue;
                var serial = fields[0];
                var emulator = serial.StartsWith("emulator-", StringComparison.Ordinal);
                // Network ADB transports never count as a plugged-in phone.
                if (emulator && !includeEmulators || !emulator && (serial.Contains(':') || serial.Contains("._adb-", StringComparison.Ordinal))) continue;
                seen.Add(serial);
                var attachment = fields.FirstOrDefault(f => f.StartsWith("transport_id:", StringComparison.Ordinal)) ?? "";
                if (attachments.TryGetValue(serial, out var previousAttachment) && previousAttachment != attachment)
                { opened.Remove(serial); wired.Remove(serial); nextTunnelCheck.Remove(serial); nextLaunchAttempt.Remove(serial); }
                attachments[serial] = attachment;
                if (fields[1] == "unauthorized") { opened.Remove(serial); wired.Remove(serial); nextTunnelCheck.Remove(serial); needsApproval = true; continue; }
                if (fields[1] == "offline") { opened.Remove(serial); wired.Remove(serial); nextTunnelCheck.Remove(serial); offline = true; continue; }
                if (!emulator)
                {
                    if (!wired.TryGetValue(serial, out var isWired))
                    {
                        var path = await command(["-s", serial, "get-devpath"], ct);
                        if (path.ExitCode != 0) { failed = true; continue; }
                        isWired = path.Output.Trim().StartsWith("usb:", StringComparison.OrdinalIgnoreCase);
                        wired[serial] = isWired;
                    }
                    if (!isWired) continue;
                }
                ready++;
                if (background && opened.Contains(serial) && nextTunnelCheck.TryGetValue(serial, out var next) && next > now) continue;
                var local = "tcp:" + new Uri(host.Address).Port;
                var remote = "tcp:" + Wire.Port;
                var mappings = await command(["-s", serial, "reverse", "--list"], ct);
                var mapped = HasMapping(mappings.Output, remote, local);
                if (!mapped)
                {
                    opened.Remove(serial);
                    var reverse = await command(["-s", serial, "reverse", "--no-rebind", remote, local], ct);
                    if (reverse.ExitCode != 0) { failed = true; continue; }
                }
                tunnels.Add(serial);
                nextTunnelCheck[serial] = now.AddSeconds(15);
                if (opened.Contains(serial)) continue;
                if (background && nextLaunchAttempt.TryGetValue(serial, out var attemptAt) && attemptAt > now) { failed = true; continue; }
                nextLaunchAttempt[serial] = now.AddSeconds(15);
                // The random bootstrap secret is one-use, expires in two minutes, and is loopback-only.
                var secret = host.NewUsbPairingCode();
                var launch = await command(["-s", serial, "shell", "am", "start", "-W", "-n", "ai.chud.chudlink/.MainActivity", "--es", "chudlink.usbFingerprint", host.Fingerprint, "--es", "chudlink.usbCode", secret, "--es", "chudlink.lanAddress", host.Address], ct);
                if (launch.ExitCode != 0 || launch.Output.Contains("Error:", StringComparison.OrdinalIgnoreCase) || launch.Output.Contains("Exception", StringComparison.OrdinalIgnoreCase)) { failed = true; continue; }
                opened.Add(serial);
                nextLaunchAttempt.Remove(serial);
            }
            opened.RemoveWhere(serial => !seen.Contains(serial));
            tunnels.RemoveWhere(serial => !seen.Contains(serial));
            foreach (var serial in attachments.Keys.Where(serial => !seen.Contains(serial)).ToArray())
            { attachments.Remove(serial); wired.Remove(serial); nextTunnelCheck.Remove(serial); nextLaunchAttempt.Remove(serial); }
            Report(needsApproval ? "Unlock the phone and tap Allow USB debugging for this PC." : failed ? "USB phone found. Install the latest ChudLink APK, then click Retry USB." : ready > 0 ? $"USB ready · {ready} phone(s). First connection: confirm on the phone, then approve it in Devices." : offline ? "USB phone is offline. Unlock it or reconnect the data cable." : "Waiting for a USB phone · use a data cable, enable USB debugging once, and unlock the phone.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Read the documented ADB smart-socket list service; never forwards shell commands or credentials.</summary>
    public static async Task<AdbResult> ReadDeviceListAsync(int port = 5037, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await using var stream = client.GetStream();
        const string service = "host:devices-l";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(service.Length.ToString("X4", CultureInfo.InvariantCulture) + service), timeout.Token);
        var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
        if (Encoding.ASCII.GetString(header) != "OKAY") throw new IOException("ADB list service is unavailable");
        await stream.ReadExactlyAsync(header, timeout.Token);
        if (!int.TryParse(Encoding.ASCII.GetString(header), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size is < 0 or > 65535)
            throw new IOException("Invalid ADB device-list length");
        var payload = new byte[size]; await stream.ReadExactlyAsync(payload, timeout.Token);
        return new(0, Encoding.UTF8.GetString(payload));
    }

    static bool HasMapping(string output, string remote, string local) => output.Split('\n').Any(line =>
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[^2] == remote && parts[^1] == local;
    });

    public Task StopAsync()
    {
        lock (lifecycle)
        {
            restartRequested = false;
            if (stoppingTask is { IsCompleted: false }) return stoppingTask;
            var previousStop = stop; var previousLoop = loop; stop = null; loop = null;
            return stoppingTask = StopCoreAsync(previousStop, previousLoop);
        }
    }
    async Task StopCoreAsync(CancellationTokenSource? previousStop, Task? previousLoop)
    {
        if (previousStop != null) { await previousStop.CancelAsync(); if (previousLoop != null) await previousLoop; previousStop.Dispose(); }
        await gate.WaitAsync();
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var serial in tunnels.ToArray())
            {
                try
                {
                    var mappings = await command(["-s", serial, "reverse", "--list"], cleanup.Token);
                    if (HasMapping(mappings.Output, "tcp:" + Wire.Port, "tcp:" + new Uri(host.Address).Port))
                        await command(["-s", serial, "reverse", "--remove", "tcp:" + Wire.Port], cleanup.Token);
                }
                catch (Exception) { /* A disconnected phone has already lost its USB tunnel. */ }
            }
            tunnels.Clear(); opened.Clear(); wired.Clear(); attachments.Clear(); nextTunnelCheck.Clear(); nextLaunchAttempt.Clear(); connectedDevices.Clear(); Report("USB auto-connect is off");
        }
        finally { gate.Release(); }
        lock (lifecycle)
        {
            if (restartRequested && !disposed && host.Running) { restartRequested = false; StartLoop(); }
        }
    }

    static string FindAdb()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "adb", "adb.exe");
        if (File.Exists(bundled)) return bundled;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".tools", "android-sdk", "platform-tools", "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var sdk = Environment.GetEnvironmentVariable(variable);
            if (sdk != null && File.Exists(Path.Combine(sdk, "platform-tools", "adb.exe"))) return Path.Combine(sdk, "platform-tools", "adb.exe");
        }
        throw new FileNotFoundException("Android Platform Tools are missing");
    }

    static async Task<AdbResult> RunAdbAsync(string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(FindAdb()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start Android Platform Tools");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); return new(process.ExitCode, await output + "\n" + await error); }
        catch { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } throw; }
    }

    public async ValueTask DisposeAsync()
    {
        lock (lifecycle) disposed = true;
        await StopAsync();
    }
}
