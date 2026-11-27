using System.Runtime.InteropServices;

namespace ChudLink.Host;

public static class WindowsActions
{
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern bool PlaySound(string? sound, nint module, uint flags);
    public static string? ForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); return process.ProcessName; } catch (ArgumentException) { return null; }
    }
    public static void Media(string key)
    {
        byte code = key switch { "play" => 0xB3, "next" => 0xB0, "previous" => 0xB1, "mute" => 0xAD, "up" => 0xAF, "down" => 0xAE, _ => throw new InvalidDataException("Unknown media control") };
        keybd_event(code, 0, 0, 0); keybd_event(code, 0, 2, 0);
    }
    public static void Shortcut(string keys)
    {
        var codes = keys.Split('+', StringSplitOptions.TrimEntries).Select(k => k.ToUpperInvariant() switch
        { "CTRL" => (byte)0x11, "SHIFT" => (byte)0x10, "ALT" => (byte)0x12, "WIN" => (byte)0x5B, "ENTER" => (byte)0x0D, "SPACE" => (byte)0x20, _ when k.Length == 1 && char.IsAsciiLetterOrDigit(k[0]) => (byte)char.ToUpperInvariant(k[0]), _ => throw new InvalidDataException("Use CTRL/ALT/SHIFT/WIN and a letter, digit, ENTER or SPACE") }).ToArray();
        if (codes.Length is < 1 or > 5) throw new InvalidDataException("Invalid shortcut");
        foreach (var c in codes) keybd_event(c, 0, 0, 0);
        foreach (var c in codes.Reverse()) keybd_event(c, 0, 2, 0);
    }
    public static void Sound(string path) { if (path.Length == 0) PlaySound(null, 0, 0); else if (!PlaySound(path, 0, 0x00020000 | 0x0001 | 0x0002)) throw new IOException("Unable to play WAV"); }
    static IAudioEndpointVolume VolumeEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var endpoint));
        Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator);
        return (IAudioEndpointVolume)endpoint;
    }
    public static float GetVolume() { var endpoint = VolumeEndpoint(); try { Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var volume)); return volume; } finally { Marshal.ReleaseComObject(endpoint); } }
    public static void SetVolume(float volume) { var endpoint = VolumeEndpoint(); try { Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0, 1), Guid.Empty)); } finally { Marshal.ReleaseComObject(endpoint); } }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator
    { [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint devices); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, nint activation, [MarshalAs(UnmanagedType.IUnknown)] out object value); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify); [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count); [PreserveSig] int SetMasterVolumeLevel(float level, Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid context); [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }
}
