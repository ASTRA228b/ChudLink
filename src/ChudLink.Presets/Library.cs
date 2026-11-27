using ChudLink.Protocol;

namespace ChudLink.Presets;

public static class Library
{
    static Widget W(string id, WidgetKind kind, string label, double x, double y, string? data = null, string? action = null, string? arg = null) => new()
    { Id = id, Kind = kind, Label = label, X = x, Y = y, Width = 324, Height = 166, DataKey = data, Text = data == null ? "" : "Awaiting host", Bindings = action == null ? [] : [new(action, arg)] };
    static Dashboard Deck(string id, string name, string description, params Widget[] widgets) => new()
    { Id = id, Name = name, Description = description, Pages = [new() { Id = id + "-main", Widgets = widgets.ToList() }] };
    public static IReadOnlyList<Dashboard> Create() =>
    [
        Deck("control", "Control Deck", "Media transport and a shared counter. Add approved launchers and shortcuts in Settings and Designer.",
            W("play",WidgetKind.Button,"PLAY / PAUSE",24,24, action:"media",arg:"play"), W("next",WidgetKind.Button,"NEXT TRACK",372,24,action:"media",arg:"next"),
            W("prev",WidgetKind.Button,"PREVIOUS",24,214,action:"media",arg:"previous"),W("mute",WidgetKind.Button,"MUTE",372,214,action:"media",arg:"mute"),
            W("count",WidgetKind.Counter,"SHARED COUNTER",24,404,"shared.counter","counter"),W("time",WidgetKind.Clock,"LOCAL TIME",372,404)),
        Deck("hardware", "Hardware Station", "Real Windows CPU, memory, storage and network measurements. Unavailable GPU, VRAM and thermal data are omitted.",
            W("cpu",WidgetKind.Gauge,"CPU",24,24,"host.cpu"),W("ram",WidgetKind.Progress,"RAM",372,24,"host.ram"),
            W("cpu-history",WidgetKind.Graph,"CPU HISTORY",24,214,"host.cpu"),W("net",WidgetKind.Text,"NETWORK",372,214,"host.network"),
            W("disk",WidgetKind.Progress,"SYSTEM DRIVE",24,404,"host.storage"),W("mem",WidgetKind.Text,"HOST MEMORY",372,404,"host.processMemory"),
            W("up",WidgetKind.Text,"HOST UPTIME",24,594,"host.uptime"),W("clock",WidgetKind.Clock,"LOCAL TIME",372,594)),
        Deck("audio", "Audio Station", "Windows master volume and media controls. Per-application routing is not yet available.",
            W("volume",WidgetKind.Slider,"MASTER VOLUME",24,24,"audio.volume","volume"),W("mute-a",WidgetKind.Button,"MUTE",372,24,action:"media",arg:"mute"),
            W("play-a",WidgetKind.Button,"PLAY / PAUSE",24,214,action:"media",arg:"play"),W("next-a",WidgetKind.Button,"NEXT",372,214,action:"media",arg:"next")),
        Deck("soundboard", "Soundboard Station", "Import WAV audio as an approved sound action in Settings, then bind a button in Designer.",
            W("sound-help",WidgetKind.Text,"YOUR SOUND LIBRARY",24,24),W("sound-volume",WidgetKind.Slider,"MASTER VOLUME",372,24,"audio.volume","volume")),
        Deck("developer", "Developer Station", "Host process telemetry. Add an approved build executable with fixed arguments in Settings; bind it in Designer.",
            W("build-output",WidgetKind.Terminal,"BUILD OUTPUT",24,24,"build.output"),W("dev-ram",WidgetKind.Text,"HOST MEMORY",372,24,"host.processMemory"), W("dev-clock",WidgetKind.Clock,"TIME",24,214)),
        Deck("ai", "AI Station", "Configure your own HTTP/HTTPS health endpoints in Settings. No credentials or assumed service URLs.",
            W("api",WidgetKind.Status,"SERVICE HEALTH",24,24,"service.summary"),W("ai-up",WidgetKind.Text,"HOST UPTIME",372,24,"host.uptime")),
        Deck("network", "Network Station", "Network interface throughput and measured connection round-trip time.",
            W("network-rate",WidgetKind.Text,"NETWORK RATE",24,24,"host.network"),W("latency",WidgetKind.Graph,"ROUND TRIP · MS",372,24,"device.latency"),W("health",WidgetKind.Status,"SERVICES",24,214,"service.summary")),
        Deck("productivity", "Productivity Station", "Local clock, timer, stopwatch, shared counter, notes and task list.",
            W("prod-clock",WidgetKind.Clock,"LOCAL TIME",24,24),W("timer",WidgetKind.Timer,"5 MINUTE TIMER",372,24),W("stopwatch",WidgetKind.Timer,"STOPWATCH",24,214),
            W("prod-count",WidgetKind.Counter,"SHARED COUNTER",372,214,"shared.counter","counter"),W("notes",WidgetKind.Text,"NOTES · TAP TO EDIT",24,404),W("tasks",WidgetKind.Text,"TASKS · TAP TO EDIT",372,404)),
        Deck("gaming", "Gaming Station", "System monitoring and media transport without game injection. FPS requires an external supported provider.",
            W("game-cpu",WidgetKind.Gauge,"CPU",24,24,"host.cpu"),W("game-ram",WidgetKind.Progress,"RAM",372,24,"host.ram"),W("game-music",WidgetKind.Button,"PLAY / PAUSE",24,214,action:"media",arg:"play")),
        Deck("ambient", "Ambient Station", "Minimal local clock and system status. Customize the theme in Designer.",
            W("ambient-clock",WidgetKind.Clock,"CHUDLINK",24,24),W("ambient-ram",WidgetKind.Text,"MEMORY",372,24,"host.ram")),
        Deck("blank", "Blank Workspace", "A clean canvas. Build your own device.")
    ];
}
