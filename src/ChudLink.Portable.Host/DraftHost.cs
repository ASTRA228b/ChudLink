using ChudLink.Protocol;

namespace ChudLink.Host;

public static class DraftHost
{
    public static HostRuntime Create(string root)
    {
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var host = new HostRuntime(root);
        host.Shared.TryAdd("counter", "0");
        host.Store.Settings.UsbAutoConnect = false;
        // Do not let migrated draft settings grant unsupported PC actions or carry old shell commands.
        host.Store.Settings.Actions.Clear(); host.Store.Settings.ContextRules.Clear();
        foreach (var device in host.Devices()) device.Permissions.IntersectWith(["dashboard", "counter", "mesh"]);
        foreach (var id in new[] { "audio", "soundboard", "developer", "ai", "gaming", "ambient" }) host.Store.Dashboards.TryRemove(id, out _);
        Widget Widget(string id, string title, double x, double y, string key) => new() { Id = id, Kind = WidgetKind.Text, Label = title, X = x, Y = y, Width = 324, Height = 166, DataKey = key, Text = "Waiting for the first sample" };
        host.Store.Dashboards["hardware"] = new()
        {
            Id = "hardware", Name = "Portable Host Station", Description = "Real process memory, host uptime, filesystem usage and network throughput. System CPU/RAM are not provided by the draft.",
            Pages = [new() { Id = "portable-hardware-main", Widgets = [Widget("mem", "HOST PROCESS MEMORY", 24, 24, "host.processMemory"), Widget("up", "HOST UPTIME", 372, 24, "host.uptime"), Widget("disk", "FILESYSTEM USAGE", 24, 214, "host.storage"), Widget("net", "NETWORK RATE", 372, 214, "host.network")] }]
        };
        if (host.Store.Dashboards.TryGetValue("control", out var control))
        {
            control.Name = "Counter and Clock"; control.Description = "Shared counter and phone-local clock; native media buttons are excluded from this draft.";
            foreach (var page in control.Pages) page.Widgets.RemoveAll(w => w.Bindings.Any(b => b.Action == "media"));
        }
        return host;
    }
}
