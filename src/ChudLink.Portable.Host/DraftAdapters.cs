using System.Diagnostics;
using System.Net.NetworkInformation;
using ChudLink.Protocol;

namespace ChudLink.Host;

// These adapters deliberately expose the draft's limitations rather than pretend a Windows
// implementation or a security-equivalent extension worker exists on another OS.
public static class WindowsActions
{
    static PlatformNotSupportedException Unsupported() => new("This native PC action is not implemented in the Linux/macOS draft.");
    public static void Media(string action) => throw Unsupported();
    public static void SetVolume(float value) => throw Unsupported();
    public static void Shortcut(string keys) => throw Unsupported();
    public static void Sound(string path) => throw Unsupported();
    public static string? ForegroundProcess() => null;
}

public sealed class WorkerManager : IAsyncDisposable
{
    public WorkerManager(HostRuntime host) { }
    public string[] Active => [];
    public Task CheckLimitsAsync() => Task.CompletedTask;
    public Task DevicesChangedAsync() => Task.CompletedTask;
    public Task BroadcastAsync<T>(string type, T payload, string? permission = null) => Task.CompletedTask;
    public Task InvokeActionAsync(string action, ChudLink.Core.DeviceRecord device, string? argument)
        => Task.FromException(new PlatformNotSupportedException("C# extension workers are not available in the portable draft."));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class UsbAutoConnect : IAsyncDisposable
{
    public UsbAutoConnect(HostRuntime host) { }
    public string Status => "USB is not implemented in the portable draft. Use local Wi-Fi/LAN pairing.";
    public bool Enabled => false;
    public void Start() => throw new PlatformNotSupportedException(Status);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Only real cross-platform values; unsupported system CPU/RAM/audio are absent.</summary>
public sealed class Telemetry : IDisposable
{
    readonly Stopwatch uptime = Stopwatch.StartNew();
    readonly Process process = Process.GetCurrentProcess();
    DateTimeOffset lastNetworkSample;
    long lastNetwork;
    DateTimeOffset slowSample;
    readonly Dictionary<string, (string Text, double Value)> slowValues = [];
    public long SampleCount { get; private set; }
    public Dictionary<string, (string Text, double Value)> Sample(IReadOnlySet<string>? requiredKeys = null)
    {
        SampleCount++;
        var result = new Dictionary<string, (string Text, double Value)>();
        bool Needs(string key) => requiredKeys == null || requiredKeys.Contains(key);
        if (Needs("host.processMemory"))
        {
            process.Refresh(); var memory = process.WorkingSet64 / 1048576d;
            result["host.processMemory"] = ($"{memory:F1} MB", memory);
        }
        if (Needs("host.uptime")) result["host.uptime"] = (uptime.Elapsed.ToString(@"dd\.hh\:mm\:ss"), uptime.Elapsed.TotalSeconds);
        var now = DateTimeOffset.UtcNow;
        if (Needs("host.network") && (now - lastNetworkSample).TotalSeconds >= 1)
        {
            long bytes = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                try { var info = nic.GetIPStatistics(); bytes += info.BytesReceived + info.BytesSent; } catch (NetworkInformationException) { }
            var seconds = (now - lastNetworkSample).TotalSeconds;
            if (lastNetworkSample != default && seconds > 0)
            {
                var rate = Math.Max(0, bytes - lastNetwork) / seconds;
                slowValues["host.network"] = ($"{rate / 1024:F1} KB/s", rate);
            }
            lastNetwork = bytes; lastNetworkSample = now;
        }
        if (Needs("host.storage") && (now - slowSample).TotalSeconds >= 15)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? "/");
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    var used = 100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                    slowValues["host.storage"] = ($"{used:F0}% used", used);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            slowSample = now;
        }
        foreach (var (key, sample) in slowValues) if (Needs(key)) result[key] = sample;
        return result;
    }
    public void Dispose() => process.Dispose();
}
