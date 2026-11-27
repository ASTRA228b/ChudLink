using ChudLink.Protocol;

namespace ChudLink.SDK;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ChudExtensionAttribute(string id, string name) : Attribute
{
    public string Id { get; } = id;
    public string Name { get; } = name;
}
public interface IChudExtension
{
    Task StartAsync(IChudContext context, CancellationToken cancellationToken);
    Task OnTouchAsync(string deviceId, TouchEvent touch, CancellationToken cancellationToken) => Task.CompletedTask;
    Task OnSensorAsync(string deviceId, SensorEvent sensor, CancellationToken cancellationToken) => Task.CompletedTask;
}
public interface IChudContext
{
    IReadOnlyList<DeviceInfo> Devices { get; }
    IReadOnlyDictionary<string, string> Settings { get; }
    void RegisterDashboard(Dashboard dashboard);
    void RegisterWidget(string type, Widget fallback);
    void RegisterAction(string id, Func<string, string?, Task> handler);
    void RegisterProvider(string key, Func<CancellationToken, Task<string>> provider);
    void Update(string dashboardId, params WidgetPatch[] updates);
    void AssignDashboard(string deviceId, string dashboardId);
    void Notify(string deviceId, string message);
    void SetShared(string key, string value);
    void Log(string message);
    void RunApprovedAction(string actionId, string deviceId);
}
public sealed record DeviceInfo(string Id, string Name, string Platform, string[] Capabilities);
public sealed record ExtensionManifest(string Id, string Name, string Author, string Version, string SdkVersion, string[] Permissions, string Assembly = "Extension.dll", string? EntryType = null);
public sealed class DashboardBuilder(string name)
{
    readonly Dashboard dashboard = new() { Name = name };
    public DashboardBuilder Id(string id) { dashboard.Id = id; return this; }
    public DashboardBuilder Size(double width, double height) { dashboard.Width = width; dashboard.Height = height; return this; }
    public DashboardBuilder Add(Widget widget) { dashboard.Pages[^1].Widgets.Add(widget); return this; }
    public DashboardBuilder Button(string id, string label, string action, double x = 24, double y = 24) => Add(new() { Id = id, Kind = WidgetKind.Button, Label = label, X = x, Y = y, Bindings = [new(action)] });
    public DashboardBuilder Text(string id, string text, double x = 24, double y = 200) => Add(new() { Id = id, Kind = WidgetKind.Text, Label = text, Text = text, X = x, Y = y });
    public DashboardBuilder Page(string name) { dashboard.Pages.Add(new() { Name = name }); return this; }
    public Dashboard Build() { dashboard.Validate(); return dashboard; }
}
