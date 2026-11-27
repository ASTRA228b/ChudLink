# ChudLink SDK 0.1.0 Alpha

Use C# to make a dashboard without implementing discovery, TLS or phone rendering. Build against ChudLink.SDK and ChudLink.Protocol; generated NuGet packages are in artifacts/sdk. The repo examples reference the SDK project directly and build with the Windows solution.

```csharp
using ChudLink.SDK;
using ChudLink.Protocol;

[ChudExtension("my.counter", "My counter")]
public sealed class Counter : IChudExtension
{
    public Task StartAsync(IChudContext ctx, CancellationToken ct)
    {
        var count = 0;
        var board = new DashboardBuilder("My workspace")
            .Id("my.counter.home")
            .Button("count", "COUNT +1", "my.counter.increment")
            .Text("value", "0")
            .Build();
        ctx.RegisterDashboard(board);
        ctx.RegisterAction("my.counter.increment", (device, argument) =>
        {
            ctx.Update(board.Id, new WidgetPatch("value", (++count).ToString()));
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
```

Copy into Developer Studio, Build, inspect diagnostics, trust and Run. Enable the phone's Approved PC actions permission, then assign the registered dashboard. Dashboard IDs and custom action IDs must start with the extension ID followed by a period. Worker assembly names are explicit in manifest.json. An attribute labels the source; the actual package manifest is what the host inspects.

## Context methods

| API | Behavior / required manifest permission |
| --- | --- |
| RegisterDashboard | Validate and persist a namespaced dashboard / dashboard |
| RegisterWidget | Register a declarative standard-widget fallback / dashboard |
| RegisterAction | Receive event bindings as device ID and argument / actions |
| RegisterProvider | Poll custom string values every 2 seconds / providers |
| Update | Incremental text, numeric or color updates to owned dashboards / dashboard |
| Devices | Current connected identity/platform/capability snapshots / dashboard |
| Settings | Configuration string dictionary supplied before StartAsync |
| OnTouchAsync | Receive supported widget press/change events / dashboard |
| OnSensorAsync | Receive opt-in accelerometer events / sensors |
| AssignDashboard | Change an individual paired device's dashboard / mesh |
| SetShared | Set a variable under extensionId.key / mesh |
| Notify | Show a local phone notification / notifications |
| RunApprovedAction | Invoke a fixed host action by ID / actions, plus device actions permission |
| Log | Write to host Diagnostics |

Read a shared variable on a widget through `DataKey = "shared.my.extension.variable"`. Read a custom provider through `DataKey = "my.extension.status"`. Built-in telemetry keys include host.cpu, host.ram, host.storage, host.network, host.processMemory, host.uptime, device.latency, audio.volume and service.summary. Unknown values are never fabricated. For arrays or structured JSON, parse data in trusted host C# and publish display values; widgets remain declarative.

Worker startup can return immediately or run an async loop. Honor the supplied cancellation token. Touch/action handlers should be short; use a background Task for lengthy work and explicitly handle cancellation/errors. All updates to phones route through the host. Dashboard changes and native widget fallback definitions work with the existing APK. The SDK does not currently offer arbitrary phone-native code execution or a public custom renderer registry beyond fallback declarations.

## Packages

A .chudext is a ZIP containing manifest.json, a compiled assembly, optional source, assets and settings.schema.json. Example manifest:

```json
{
  "id": "my.counter",
  "name": "My counter",
  "author": "Your name",
  "version": "0.1.0",
  "sdkVersion": "0.1.0",
  "permissions": ["dashboard", "actions"],
  "assembly": "Extension.dll"
}
```

Developer Studio Export project compiles and exports this format with source. The Extensions screen can import it for review and explicitly enable it. Configuration currently uses a JSON string dictionary; schemas travel with the package but automatic schema-generated forms are not implemented. Local dependencies must be included alongside the extension assembly and compatible with .NET 10. Framework code uses the worker runtime. Packages must not contain secrets or installed-user configuration.

## Working examples

All nine projects under examples build independently in Visual Studio/Rider/VS Code or with dotnet build. The build script compiles and packages them into artifacts/extensions:

- HelloWorld: dashboard, registered action, live text and notification.
- SystemMonitor: actual host CPU/RAM bindings and worker process memory.
- CustomCounter: C# event handler and incremental count.
- CustomClock: host UTC clock with a cancellation-aware update loop.
- CrossDevice: switch another phone; optional targetDeviceId setting.
- JsonStatus: configured HTTP/HTTPS JSON health request; url and optional property settings.
- AudioControls: built-in authorized master volume and media controls.
- SensorDashboard: receive opt-in accelerometer events and update a dashboard.
- BuildMonitor: launch a locally approved buildActionId and show output.

Extensions are trusted executable code. Read ../SECURITY.md before enabling third-party assemblies. IPC permissions and worker resource limits do not provide an OS security sandbox.
