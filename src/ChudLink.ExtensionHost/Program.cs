using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ChudLink.Protocol;
using ChudLink.SDK;

namespace ChudLink.ExtensionHost;

public static class Worker
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var output = Console.Out;
        // Reserve stdout for the bounded JSON protocol; extension Console writes become log lines.
        Console.SetOut(Console.Error);
        using var stop = new CancellationTokenSource();
        var context = new WorkerContext(output, stop.Token);
        try
        {
            // The host sends settings and the device snapshot before startup.
            for (var i = 0; i < 2; i++)
            {
                var initial = Wire.Parse<Envelope>(await Console.In.ReadLineAsync(stop.Token) ?? throw new EndOfStreamException());
                if (initial.Type == "settings") context.Configuration = initial.Read<Dictionary<string, string>>();
                if (initial.Type == "devices") context.DeviceList = initial.Read<DeviceInfo[]>();
            }
            var asm = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var type = asm.GetTypes().First(t => !t.IsAbstract && typeof(IChudExtension).IsAssignableFrom(t));
            var extension = (IChudExtension)Activator.CreateInstance(type)!;
            var start = extension.StartAsync(context, stop.Token);
            while (await Console.In.ReadLineAsync(stop.Token) is { } line)
            {
                if (line.Length > 256 * 1024) break;
                var message = Wire.Parse<Envelope>(line);
                switch (message.Type)
                {
                    case "devices": context.DeviceList = message.Read<DeviceInfo[]>(); break;
                    case "settings": context.Configuration = message.Read<Dictionary<string, string>>(); break;
                    case "touch":
                        var t = message.Payload; await extension.OnTouchAsync(t.GetProperty("deviceId").GetString()!, t.GetProperty("touch").Deserialize<TouchEvent>(Wire.Json)!, stop.Token); break;
                    case "sensor":
                        var s = message.Payload; await extension.OnSensorAsync(s.GetProperty("deviceId").GetString()!, s.GetProperty("sensor").Deserialize<SensorEvent>(Wire.Json)!, stop.Token); break;
                    case "action":
                        var a = message.Payload;
                        if (context.Actions.TryGetValue(a.GetProperty("id").GetString()!, out var handler)) await handler(a.GetProperty("deviceId").GetString()!, a.TryGetProperty("argument", out var arg) ? arg.GetString() : null);
                        break;
                    case "stop": stop.Cancel(); return 0;
                }
                if (start.IsFaulted) await start;
            }
            stop.Cancel(); return 0;
        }
        catch (Exception ex) { context.Log(ex.ToString()); return 1; }
    }
}
sealed class WorkerContext(TextWriter output, CancellationToken stop) : IChudContext
{
    public DeviceInfo[] DeviceList { get; set; } = [];
    public Dictionary<string, string> Configuration { get; set; } = [];
    public IReadOnlyList<DeviceInfo> Devices => DeviceList;
    public IReadOnlyDictionary<string, string> Settings => Configuration;
    public ConcurrentDictionary<string, Func<string, string?, Task>> Actions { get; } = new();
    void Send<T>(string type, T data) { lock (output) { output.WriteLine(Wire.Serialize(Envelope.Create(type, data))); output.Flush(); } }
    public void RegisterDashboard(Dashboard dashboard) { dashboard.Validate(); Send("dashboard", dashboard); }
    public void RegisterWidget(string type, Widget fallback) => Send("widget", new { type, fallback });
    public void RegisterAction(string id, Func<string, string?, Task> handler) { Actions[id] = handler; Send("registerAction", new { id }); }
    public void RegisterProvider(string key, Func<CancellationToken, Task<string>> provider)
    {
        _ = Task.Run(async () => { try { while (!stop.IsCancellationRequested) { if (DeviceList.Length > 0) Send("provider", new { key, value = await provider(stop) }); await Task.Delay(2000, stop); } } catch (OperationCanceledException) { } catch (Exception e) { Log(e.Message); } });
    }
    public void Update(string dashboardId, params WidgetPatch[] updates) => Send("patch", new { dashboardId, updates });
    public void AssignDashboard(string deviceId, string dashboardId) => Send("assign", new { deviceId, dashboardId });
    public void Notify(string deviceId, string message) => Send("notify", new { deviceId, message });
    public void SetShared(string key, string value) => Send("shared", new { key, value });
    public void Log(string message) => Send("log", new { message });
    public void RunApprovedAction(string actionId, string deviceId) => Send("approvedAction", new { actionId, deviceId });
}
