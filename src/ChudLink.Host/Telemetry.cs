using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ChudLink.Host;

public sealed class Telemetry
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    long lastIdle, lastTotal, lastNetwork;
    DateTimeOffset lastSample = DateTimeOffset.UtcNow;
    readonly Stopwatch uptime = Stopwatch.StartNew();
    public Dictionary<string, (string Text, double Value)> Sample()
    {
        var result = new Dictionary<string, (string, double)>();
        var now = DateTimeOffset.UtcNow;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user; var delta = total - lastTotal;
            if (lastTotal > 0 && delta > 0) { var cpu = Math.Clamp(100.0 * (delta - (idle - lastIdle)) / delta, 0, 100); result["host.cpu"] = ($"{cpu:F0}%", cpu); }
            lastTotal = total; lastIdle = idle;
        }
        var m = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref m)) result["host.ram"] = ($"{(m.TotalPhys - m.AvailPhys) / 1073741824.0:F1} / {m.TotalPhys / 1073741824.0:F1} GB", m.Load);
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        if (drive.IsReady) { var used = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize; result["host.storage"] = ($"{used:F0}% used", used); }
        long network = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            try { var s = n.GetIPStatistics(); network += s.BytesReceived + s.BytesSent; } catch (NetworkInformationException) { }
        var elapsed = (now - lastSample).TotalSeconds;
        if (lastNetwork > 0 && elapsed > 0) { var speed = Math.Max(0, (network - lastNetwork) / elapsed); result["host.network"] = ($"{speed / 1024:F1} KB/s", speed); }
        lastNetwork = network; lastSample = now;
        using var process = Process.GetCurrentProcess();
        result["host.processMemory"] = ($"{process.WorkingSet64 / 1048576.0:F1} MB", process.WorkingSet64 / 1048576.0);
        result["host.uptime"] = (uptime.Elapsed.ToString(@"dd\.hh\:mm\:ss"), uptime.Elapsed.TotalSeconds);
        try { var v = WindowsActions.GetVolume(); result["audio.volume"] = ($"{v * 100:F0}%", v * 100); } catch (Exception) { }
        return result;
    }
}
