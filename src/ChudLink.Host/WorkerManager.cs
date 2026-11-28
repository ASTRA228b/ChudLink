using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ChudLink.Core;
using ChudLink.Protocol;
using ChudLink.SDK;

namespace ChudLink.Host;

public sealed class WorkerManager(HostRuntime host) : IAsyncDisposable
{
    sealed class Running(Process process, ExtensionManifest manifest, ProcessLimits limits)
    {
        public Process Process { get; } = process;
        public ExtensionManifest Manifest { get; } = manifest;
        public ProcessLimits Limits { get; } = limits;
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public HashSet<string> Dashboards { get; } = [];
        public HashSet<string> Actions { get; } = [];
        public SemaphoreSlim InputGate { get; } = new(1, 1);
        public int MessageCount;
        public DateTimeOffset Window = DateTimeOffset.UtcNow;
    }
    readonly ConcurrentDictionary<string, Running> running = new();
    public string[] Active => running.Keys.ToArray();
    public async Task StartAsync(string assembly, ExtensionManifest manifest, bool trusted)
    {
        if (!trusted) throw new UnauthorizedAccessException("Only run C# code you trust. Workers are not a security sandbox.");
        ChudLink.Extensions.ExtensionPackage.ValidateManifest(manifest);
        await StopAsync(manifest.Id);
        var worker = Path.Combine(AppContext.BaseDirectory, "worker", "ChudLink.ExtensionHost.exe");
        if (!File.Exists(worker)) throw new FileNotFoundException("Build/package the extension worker with scripts/build.ps1", worker);
        var start = new ProcessStartInfo(worker) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(assembly))! };
        start.ArgumentList.Add(Path.GetFullPath(assembly));
        var process = Process.Start(start) ?? throw new IOException("Cannot start worker");
        ProcessLimits limits;
        try { limits = new(process); } catch { process.Kill(true); process.Dispose(); throw; }
        var instance = new Running(process, manifest, limits); running[manifest.Id] = instance;
        _ = ReadAsync(instance); _ = ReadErrorsAsync(instance);
        await InputAsync(instance, "settings", host.Store.Read<Dictionary<string, string>>("extension-" + manifest.Id + ".json") ?? []);
        await DevicesChangedAsync(); host.Log($"Started trusted worker {manifest.Name} (256 MB, 20% CPU, 4 h timeout; NOT sandboxed)");
    }
    async Task ReadErrorsAsync(Running worker)
    {
        try { var lines = new BoundedLineReader(worker.Process.StandardError); while (await lines.ReadAsync() is { } line) host.Log($"[{worker.Manifest.Id}] {line[..Math.Min(line.Length, 2000)]}"); }
        catch (Exception ex) { host.Log("Worker console: " + ex.Message); }
    }
    async Task ReadAsync(Running w)
    {
        try
        {
            var lines = new BoundedLineReader(w.Process.StandardOutput);
            while (await lines.ReadAsync() is { } line)
            {
                if ((DateTimeOffset.UtcNow - w.Window).TotalSeconds > 1) { w.Window = DateTimeOffset.UtcNow; w.MessageCount = 0; }
                if (++w.MessageCount > 100) throw new InvalidDataException("Worker message rate exceeded");
                var e = Wire.Parse<Envelope>(line); var p = e.Payload;
                bool Has(string permission) => w.Manifest.Permissions.Contains(permission);
                switch (e.Type)
                {
                    case "dashboard" when Has("dashboard"):
                        var dashboard = e.Read<Dashboard>(); dashboard.Validate();
                        if (!dashboard.Id.StartsWith(w.Manifest.Id + ".", StringComparison.Ordinal)) throw new InvalidDataException("Extension dashboard IDs must begin with extension ID + '.'");
                        w.Dashboards.Add(dashboard.Id); await host.PublishAsync(dashboard); break;
                    case "patch" when Has("dashboard"):
                        var id = p.GetProperty("dashboardId").GetString()!;
                        if (!w.Dashboards.Contains(id)) throw new UnauthorizedAccessException("Dashboard ownership required");
                        var patches = p.GetProperty("updates").Deserialize<WidgetPatch[]>(Wire.Json)!;
                        if (patches.Length > 256 || patches.Any(p => p.Text?.Length > 16384 || p.Color?.Length > 40 || p.Value.HasValue && !double.IsFinite(p.Value.Value))) throw new InvalidDataException("Invalid widget updates");
                        foreach (var d in host.Devices().Where(d => d.DashboardId == id)) await host.SendAsync(d.Id, "patch", patches);
                        if (host.Store.Dashboards.TryGetValue(id, out var stored))
                        {
                            var updated = stored.Clone();
                            foreach (var patch in patches)
                            {
                                var widget = updated.Pages.SelectMany(x => x.Widgets).FirstOrDefault(x => x.Id == patch.Id);
                                if (widget == null) continue;
                                if (patch.Text is { Length: <= 16384 } text) widget.Text = text;
                                if (patch.Value is { } value && double.IsFinite(value)) widget.Value = value;
                                if (patch.Color is { Length: <= 40 } color) widget.Color = color;
                            }
                            host.Store.Dashboards[id] = updated;
                        }
                        break;
                    case "registerAction" when Has("actions"):
                        var actionId = p.GetProperty("id").GetString()!;
                        if (!actionId.StartsWith(w.Manifest.Id + ".", StringComparison.Ordinal)) throw new InvalidDataException("Action namespace required");
                        w.Actions.Add(actionId); break;
                    case "assign" when Has("mesh"): await host.AssignAsync(p.GetProperty("deviceId").GetString()!, p.GetProperty("dashboardId").GetString()!); break;
                    case "notify" when Has("notifications"): await host.SendAsync(p.GetProperty("deviceId").GetString()!, "notification", p.GetProperty("message").GetString()![..Math.Min(p.GetProperty("message").GetString()!.Length, 1000)]); break;
                    case "shared" when Has("mesh"):
                        var key = p.GetProperty("key").GetString()!; var val = p.GetProperty("value").GetString()!;
                        if (key.Length > 100 || val.Length > 4096 || host.Shared.Count > 256) throw new InvalidDataException("Shared variable limit");
                        host.Shared[w.Manifest.Id + "." + key] = val; break;
                    case "approvedAction" when Has("actions"): await host.RunApprovedActionAsync(p.GetProperty("actionId").GetString()!, p.GetProperty("deviceId").GetString()!); break;
                    case "provider" when Has("providers"):
                        var providerKey = p.GetProperty("key").GetString()!;
                        if (!providerKey.StartsWith(w.Manifest.Id + ".", StringComparison.Ordinal) || host.ProviderValues.Count > 512) throw new InvalidDataException("Provider namespace/limit");
                        var v = p.GetProperty("value").GetString()!; host.ProviderValues[providerKey] = v[..Math.Min(v.Length, 4096)]; break;
                    case "widget" when Has("dashboard"):
                        var widgetType = p.GetProperty("type").GetString()!;
                        var fallback = p.GetProperty("fallback").Deserialize<Widget>(Wire.Json)!;
                        if (!widgetType.StartsWith(w.Manifest.Id + ".", StringComparison.Ordinal) || widgetType.Length > 160 || fallback.Kind == WidgetKind.Custom || host.CustomWidgets.Count > 128) throw new InvalidDataException("Invalid custom widget type/fallback");
                        new Dashboard { Pages = [new() { Widgets = [fallback] }] }.Validate();
                        host.CustomWidgets[widgetType] = fallback;
                        host.Log($"Registered declarative widget {widgetType}"); break;
                    case "log": host.Log($"[{w.Manifest.Id}] {p.GetProperty("message").GetString()}"); break;
                    default: throw new UnauthorizedAccessException($"Extension lacks permission for {e.Type}");
                }
            }
        }
        catch (Exception ex) { host.Log($"Worker {w.Manifest.Id}: {ex.Message}"); }
        finally { if (running.TryGetValue(w.Manifest.Id, out var current) && ReferenceEquals(w, current)) await StopAsync(w.Manifest.Id); }
    }
    async Task InputAsync<T>(Running w, string type, T payload)
    {
        await w.InputGate.WaitAsync();
        try { await w.Process.StandardInput.WriteLineAsync(Wire.Serialize(Envelope.Create(type, payload))).WaitAsync(TimeSpan.FromSeconds(3)); await w.Process.StandardInput.FlushAsync(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { host.Log("Worker input: " + ex.Message); }
        finally { w.InputGate.Release(); }
    }
    public Task BroadcastAsync<T>(string type, T payload, string permission) => Task.WhenAll(running.Values.Where(w => w.Manifest.Permissions.Contains(permission)).Select(w => InputAsync(w, type, payload)));
    public Task DevicesChangedAsync() => BroadcastAsync("devices", host.Devices().Where(d => d.Connected).Select(d => new DeviceInfo(d.Id, d.Name, d.Info?.Platform ?? "Android", d.Info?.Capabilities ?? [])).ToArray(), "dashboard");
    public async Task InvokeActionAsync(string action, DeviceRecord device, string? argument)
    {
        if (!device.Permissions.Contains("actions")) return;
        foreach (var w in running.Values.Where(w => w.Actions.Contains(action))) await InputAsync(w, "action", new { id = action, deviceId = device.Id, argument });
    }
    public async Task CheckLimitsAsync()
    {
        foreach (var w in running.Values)
            try { if (w.Process.HasExited || DateTimeOffset.UtcNow - w.Started > TimeSpan.FromHours(4) || w.Process.WorkingSet64 > 256L * 1024 * 1024) await StopAsync(w.Manifest.Id); }
            catch (InvalidOperationException) { /* Another shutdown path has already disposed this worker. */ }
    }
    public async Task StopAsync(string id)
    {
        if (!running.TryRemove(id, out var w)) return;
        try
        {
            await InputAsync(w, "stop", new { });
            if (!w.Process.HasExited) { try { await w.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { w.Process.Kill(true); } }
        }
        catch (InvalidOperationException) { }
        finally { w.Limits.Dispose(); w.Process.Dispose(); host.Log("Stopped " + id); }
    }
    public async ValueTask DisposeAsync() { foreach (var id in Active) await StopAsync(id); }
}
