using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChudLink.Protocol;

public static class Wire
{
    public const int Version = 1;
    public const int Port = 47831;
    public const int DiscoveryPort = 47832;
    public const string DiscoveryQuery = "CHUDLINK_DISCOVER_V1";
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false, Converters = { new JsonStringEnumConverter() }, MaxDepth = 32
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("Empty document");
}

public enum WidgetKind { Button, Toggle, Slider, Dial, Progress, Text, Image, Clock, Graph, Gauge, Terminal, Status, Timer, Counter, Container, Navigation, Notification, Custom }
public enum RenderProfile { Legacy, Balanced, Performance }
public sealed record ActionBinding(string Action, string? Argument = null, string Event = "press");
public sealed class Widget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    public string Label { get; set; } = "Widget";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
    public double Width { get; set; } = 312;
    public double Height { get; set; } = 150;
    public int Layer { get; set; }
    public string? ParentId { get; set; }
    public string Color { get; set; } = "#C6FF2E";
    public string Background { get; set; } = "#161D18";
    public string? GradientEnd { get; set; }
    public string FontFamily { get; set; } = "sans-serif";
    public double FontSize { get; set; } = 24;
    public string? Source { get; set; }
    public string? DataKey { get; set; }
    public string Text { get; set; } = "";
    public double Value { get; set; }
    public double Maximum { get; set; } = 100;
    public List<ActionBinding> Bindings { get; set; } = [];
    public Dictionary<string, string> Properties { get; set; } = [];
}
public sealed class DashboardPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Main";
    public List<Widget> Widgets { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class Dashboard
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled workspace";
    public string Description { get; set; } = "";
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 1280;
    public string Background { get; set; } = "#0A0E0B";
    public List<DashboardPage> Pages { get; set; } = [new()];
    public Dashboard Clone() => Wire.Parse<Dashboard>(Wire.Serialize(this));
    public override string ToString() => Name;
    public void Validate()
    {
        if (FormatVersion != 1 || string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || Name.Length > 120 || Width is < 240 or > 8192 || Height is < 240 or > 8192 || !double.IsFinite(Width) || !double.IsFinite(Height) || Pages.Count is < 1 or > 32)
            throw new InvalidDataException("Unsupported or invalid dashboard");
        var ids = new HashSet<string>();
        foreach (var p in Pages)
        {
            if (p.Widgets.Count > 256 || !ids.Add(p.Id)) throw new InvalidDataException("Too many widgets or duplicate ID");
            foreach (var w in p.Widgets)
            {
                if (!ids.Add(w.Id) || string.IsNullOrWhiteSpace(w.Id) || w.Label.Length > 512 || w.Text.Length > 16384 || !Enum.IsDefined(w.Kind) ||
                    !double.IsFinite(w.X) || !double.IsFinite(w.Y) || !double.IsFinite(w.Width) || !double.IsFinite(w.Height) ||
                    !double.IsFinite(w.Maximum) || !double.IsFinite(w.Value) || !double.IsFinite(w.FontSize) || w.FontSize is < 8 or > 256 || w.Maximum <= 0 || w.Width is < 12 or > 8192 || w.Height is < 12 or > 8192 ||
                    w.X < 0 || w.Y < 0 || w.X + w.Width > Width + 1 || w.Y + w.Height > Height + 1 || w.Bindings.Count > 16)
                    throw new InvalidDataException("Invalid widget geometry, value or identity");
            }
        }
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Serialize(this)) > 950000) throw new InvalidDataException("Dashboard must fit in 950 KB, including embedded images");
    }
}
public sealed record DiscoveryReply(string Name, string Address, string Fingerprint, int Protocol = Wire.Version);
public sealed record PairRequest(string Code, string DeviceId, string Name, string Platform, int Protocol);
public sealed record PairTicket(string Ticket, string Status);
public sealed record PairResult(string Status, string? Token = null, string? DeviceId = null);
public sealed record WidgetPatch(string Id, string? Text = null, double? Value = null, string? Color = null);
public sealed record DeviceHello(string Name, string Platform, string Version, int Battery, string Orientation, string[] Capabilities, RenderProfile Profile);
public sealed record TouchEvent(string WidgetId, string Event = "press", double Value = 0);
public sealed record SensorEvent(string Type, float X, float Y, float Z);
public sealed record Envelope(string Type, JsonElement Payload)
{
    public static Envelope Create<T>(string type, T payload) => new(type, JsonSerializer.SerializeToElement(payload, Wire.Json));
    public T Read<T>() => Payload.Deserialize<T>(Wire.Json) ?? throw new InvalidDataException("Missing payload");
}
