using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChudLink.Host;

// Windows Job Objects enforce resource/lifetime bounds, not filesystem/network isolation.
public sealed class ProcessLimits : IDisposable
{
    nint job;
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcessTime, PerJobTime; public uint Flags; public nuint MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] struct Cpu { public uint Flags, Rate; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(nint job, int type, nint data, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    public ProcessLimits(Process process)
    {
        job = CreateJobObject(0, null);
        if (job == 0) throw new Win32Exception();
        try
        {
            var limits = new Extended { Basic = new Basic { Flags = 0x2000 | 0x100 | 0x8, ActiveProcesses = 1 }, ProcessMemory = 256u * 1024 * 1024 };
            Set(9, limits); Set(15, new Cpu { Flags = 0x1 | 0x4, Rate = 2000 });
            if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception();
        }
        catch { Dispose(); throw; }
    }
    void Set<T>(int type, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>(); var memory = Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(value, memory, false); if (!SetInformationJobObject(job, type, memory, (uint)size)) throw new Win32Exception(); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Dispose() { if (job != 0) { CloseHandle(job); job = 0; } }
}
