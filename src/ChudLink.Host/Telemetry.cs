using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ChudLink.Host;

public sealed class Telemetry : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    static readonly uint MemoryStatusSize = (uint)Marshal.SizeOf<MemoryStatus>();
    long lastIdle, lastTotal, lastNetwork;
    TimeSpan? lastCpuSample, lastNetworkSample;
    TimeSpan nextStorage, nextInterfaces, nextVolume;
    NetworkInterface[] adapters = [];
    DriveInfo? systemDrive;
    Process? currentProcess;
    (string Text, double Value)? storage, volume;
    bool disposed;
    readonly Stopwatch uptime = Stopwatch.StartNew();
    public long SampleCount { get; private set; }
    public long CpuProbeCount { get; private set; }
    public long MemoryProbeCount { get; private set; }
    public long ProcessProbeCount { get; private set; }
    public long StorageProbeCount { get; private set; }
    public long NetworkEnumerationCount { get; private set; }
    public long NetworkProbeCount { get; private set; }
    public long VolumeProbeCount { get; private set; }

    public Dictionary<string, (string Text, double Value)> Sample(IReadOnlySet<string>? requiredKeys = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SampleCount++;
        var result = new Dictionary<string, (string, double)>(Math.Min(requiredKeys?.Count ?? 8, 8));
        bool Needed(string key) => requiredKeys == null || requiredKeys.Contains(key);
        var now = uptime.Elapsed;
        if (Needed("host.cpu"))
        {
            CpuProbeCount++;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var total = kernel + user; var delta = total - lastTotal;
                // Do not average a fresh CPU tile over a long period when it was not requested.
                if (lastCpuSample.HasValue && now - lastCpuSample.Value <= TimeSpan.FromSeconds(15) && delta > 0)
                {
                    var cpu = Math.Clamp(100.0 * (delta - (idle - lastIdle)) / delta, 0, 100);
                    result["host.cpu"] = ($"{cpu:F0}%", cpu);
                }
                lastTotal = total; lastIdle = idle; lastCpuSample = now;
            }
        }
        if (Needed("host.ram"))
        {
            MemoryProbeCount++;
            var m = new MemoryStatus { Length = MemoryStatusSize };
            if (GlobalMemoryStatusEx(ref m)) result["host.ram"] = ($"{(m.TotalPhys - m.AvailPhys) / 1073741824.0:F1} / {m.TotalPhys / 1073741824.0:F1} GB", m.Load);
        }
        if (Needed("host.storage"))
        {
            if (now >= nextStorage)
            {
                nextStorage = now + TimeSpan.FromSeconds(30); StorageProbeCount++; storage = null;
                try
                {
                    systemDrive ??= new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                    if (systemDrive.IsReady)
                    {
                        var total = systemDrive.TotalSize; var free = systemDrive.AvailableFreeSpace;
                        if (total > 0) { var used = 100.0 * (total - free) / total; storage = ($"{used:F0}% used", used); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (storage is { } stored) result["host.storage"] = stored;
        }
        if (Needed("host.network")) SampleNetwork(result, now);
        if (Needed("host.processMemory"))
        {
            ProcessProbeCount++; currentProcess ??= Process.GetCurrentProcess(); currentProcess.Refresh();
            var bytes = currentProcess.WorkingSet64;
            result["host.processMemory"] = ($"{bytes / 1048576.0:F1} MB", bytes / 1048576.0);
        }
        if (Needed("host.uptime")) result["host.uptime"] = (now.ToString(@"dd\.hh\:mm\:ss"), now.TotalSeconds);
        if (Needed("audio.volume"))
        {
            if (now >= nextVolume)
            {
                VolumeProbeCount++; volume = null;
                try { var v = WindowsActions.GetVolume(); volume = ($"{v * 100:F0}%", v * 100); nextVolume = now + TimeSpan.FromSeconds(1); }
                catch (Exception) { nextVolume = now + TimeSpan.FromSeconds(10); }
            }
            if (volume is { } actualVolume) result["audio.volume"] = actualVolume;
        }
        return result;
    }

    void SampleNetwork(Dictionary<string, (string Text, double Value)> result, TimeSpan now)
    {
        if (now >= nextInterfaces)
        {
            nextInterfaces = now + TimeSpan.FromSeconds(10); NetworkEnumerationCount++;
            try
            {
                var found = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).OrderBy(n => n.Id).ToArray();
                if (!adapters.Select(n => n.Id).SequenceEqual(found.Select(n => n.Id))) lastNetworkSample = null;
                adapters = found;
            }
            catch (NetworkInformationException) { }
        }
        NetworkProbeCount++;
        long network = 0; var measured = false;
        foreach (var adapter in adapters)
        {
            try { var sample = adapter.GetIPStatistics(); network += sample.BytesReceived + sample.BytesSent; measured = true; }
            catch (NetworkInformationException) { }
        }
        if (!measured) { lastNetworkSample = null; return; }
        if (lastNetworkSample is { } last && (now - last).TotalSeconds is > 0 and <= 15 && network >= lastNetwork)
        {
            var speed = (network - lastNetwork) / (now - last).TotalSeconds;
            result["host.network"] = ($"{speed / 1024:F1} KB/s", speed);
        }
        lastNetwork = network; lastNetworkSample = now;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; currentProcess?.Dispose(); currentProcess = null;
    }
}
