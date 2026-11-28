using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using ChudLink.Core;
using ChudLink.Extensions;
using ChudLink.Host;
using ChudLink.Phone;
using ChudLink.Protocol;
using ChudLink.SDK;

var results = new List<object>(); var failures = 0;
var root = FindRoot();
if (args.Contains("--pack-examples")) { PackExamples(); return 0; }
if (args.Contains("--emulator-host") || args.Contains("--usb-emulator-host"))
{
    // Explicit test-only mode. This data directory and auto-approval never ship in the host app.
    await using var emulatorHost = new HostRuntime(Path.Combine(root, "artifacts", "emulator-host-data"));
    var usbTest = args.Contains("--usb-emulator-host");
    await emulatorHost.StartAsync("127.0.0.1", usbTest ? 47841 : Wire.Port);
    await using var emulatorUsb = new UsbAutoConnect(emulatorHost, includeEmulators: true);
    if (usbTest) emulatorUsb.Start();
    var setup = new { address = "https://10.0.2.2:" + Wire.Port, fingerprint = emulatorHost.Fingerprint, code = emulatorHost.NewPairingCode() };
    File.WriteAllText(Path.Combine(root, "artifacts", "emulator-setup.json"), Wire.Serialize(setup));
    Console.WriteLine("Emulator test host ready; auto-approves emulator test identities only. Ctrl+C to stop.");
    var assigned = new HashSet<string>();
    while (true)
    {
        var codeRequest = Path.Combine(root, "artifacts", "emulator-new-code");
        if (emulatorHost.PairingCode.Length != 6 || File.Exists(codeRequest))
        {
            if (File.Exists(codeRequest)) File.Delete(codeRequest);
            File.WriteAllText(Path.Combine(root, "artifacts", "emulator-setup.json"), Wire.Serialize(new { address = "https://10.0.2.2:" + Wire.Port, fingerprint = emulatorHost.Fingerprint, code = emulatorHost.NewPairingCode() }));
        }
        foreach (var p in emulatorHost.Pairings.Values.Where(p => p.Status == "pending" && p.Request.Name.Contains("sdk", StringComparison.OrdinalIgnoreCase))) emulatorHost.Approve(p.Ticket, true);
        foreach (var d in emulatorHost.Devices().Where(d => d.Connected && !assigned.Contains(d.Id)))
        { emulatorHost.ConfigureDevice(d.Id, d.Name, "test", ["dashboard", "counter", "media", "actions", "sensors"]); assigned.Add(d.Id); }
        var assignFile = Path.Combine(root, "artifacts", "emulator-assign.txt");
        var extensionRequest = Path.Combine(root, "artifacts", "emulator-extension.request");
        if (File.Exists(extensionRequest))
        {
            File.Delete(extensionRequest);
            var build = ExtensionCompiler.Build(File.ReadAllText(Path.Combine(root, "examples", "HelloWorld", "Extension.cs")), Path.Combine(root, "artifacts", "emulator-extension"));
            if (!build.Success) throw new Exception(string.Join("\n", build.Diagnostics));
            await emulatorHost.Workers.StartAsync(build.AssemblyPath!, new("example.hello", "Hello", "Tests", "0.1.0", "0.1.0", ["dashboard", "actions", "notifications"]), true);
        }
        if (File.Exists(assignFile))
        {
            var dashboardId = File.ReadAllText(assignFile).Trim();
            if (dashboardId.Length > 0 && emulatorHost.Store.Dashboards.ContainsKey(dashboardId))
                foreach (var d in emulatorHost.Devices().Where(d => d.Connected && d.DashboardId != dashboardId)) await emulatorHost.AssignAsync(d.Id, dashboardId);
        }
        File.WriteAllText(Path.Combine(root, "artifacts", "emulator-host-status.json"), Wire.Serialize(new { devices = emulatorHost.Devices().Select(d => new { d.Id, d.Name, d.Connected, d.DashboardId, d.Info, d.LatencyMs }), shared = emulatorHost.Shared, emulatorHost.BytesReceived, emulatorHost.BytesSent, logs = emulatorHost.Logs.Snapshot() }));
        await Task.Delay(500);
    }
}
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + "\n" + e); results.Add(new { name, passed = false, error = e.ToString() }); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Until(Func<bool> predicate, string why, int seconds = 10)
{
    var end = DateTime.UtcNow.AddSeconds(seconds); while (DateTime.UtcNow < end) { if (predicate()) return; await Task.Delay(100); } throw new TimeoutException(why);
}
await Test("Every built-in preset validates and round-trips", () =>
{
    var presets = ChudLink.Presets.Library.Create(); Check(presets.Count == 11, "11 presets required");
    foreach (var d in presets) { d.Validate(); var restored = Wire.Parse<Dashboard>(Wire.Serialize(d)); restored.Validate(); Check(restored.Id == d.Id, "ID must persist"); } return Task.CompletedTask;
});
await Test("Dashboard rejects future format, duplicate identities, invalid geometry and NaN", () =>
{
    var d = new Dashboard(); d.FormatVersion = 2; Throws(d.Validate); d.FormatVersion = 1;
    var w = new Widget(); d.Pages[0].Widgets.Add(w); d.Pages[0].Widgets.Add(w); Throws(d.Validate);
    d.Pages[0].Widgets.RemoveAt(1); w.Width = double.NaN; Throws(d.Validate); w.Width = 2000; Throws(d.Validate); return Task.CompletedTask;
});
await Test("Undo, redo and clone isolation", () =>
{
    var d = new Dashboard(); var h = new DashboardHistory(); h.Save(d); d.Name = "changed"; d = h.Undo(d); Check(d.Name != "changed", "Undo"); d = h.Redo(d); Check(d.Name == "changed", "Redo"); var clone = d.Clone(); clone.Name = "different"; Check(d.Name == "changed", "Clone isolation"); return Task.CompletedTask;
});
await Test("Credential hashing and private-interface validation", () =>
{
    var token = Credentials.NewToken(); var hash = Credentials.Hash(token); Check(token != hash && Credentials.Verify(token, hash) && !Credentials.Verify("wrong", hash), "Token verification");
    Check(LocalNetwork.IsPrivate(IPAddress.Parse("192.168.2.3")) && !LocalNetwork.IsPrivate(IPAddress.Parse("8.8.8.8")), "Private binding"); return Task.CompletedTask;
});
await Test("Phone rejects HTTP, public addresses and malformed certificate pins", () =>
{
    Throws(() => PhoneConnection.NormalizeAddress("http://192.168.1.2")); Throws(() => PhoneConnection.NormalizeAddress("8.8.8.8")); Throws(() => new PhoneConnection("id", "127.0.0.1", "bad", "")); return Task.CompletedTask;
});
await Test("Phone dashboards use readable screen-width cards across phone and tablet sizes", () =>
{
    var dashboard = ChudLink.Presets.Library.Create().Single(d => d.Id == "hardware");
    foreach (var width in new[] { 320d, 360, 412, 640, 800, 1200 })
    {
        var layout = PhoneLayout.Arrange(dashboard, 0, width, 560);
        Check(layout.Cards.Length == 8, "Every control remains available");
        foreach (var card in layout.Cards)
        {
            Check(card.Bounds.X >= 0 && card.Bounds.X + card.Bounds.Width <= width && card.Bounds.Height >= 132, "Cards stay inside screen with large touch targets");
            Check(20 * card.Bounds.Width / card.LogicalWidth >= 16, "Widget labels stay readable in dp rather than shrinking an entire canvas");
        }
        if (width < 600) Check(layout.Cards.All(c => c.Bounds.X == 12), "Narrow phones use one full-width column");
    }
    return Task.CompletedTask;
});
await Test("Responsive layouts preserve widgets and resolve touches after scrolling", () =>
{
    foreach (var dashboard in ChudLink.Presets.Library.Create())
    {
        var original = Wire.Serialize(dashboard); var layout = PhoneLayout.Arrange(dashboard, 0, 360, 500);
        foreach (var card in layout.Cards)
            Check(layout.HitTest(card.Bounds.X + card.Bounds.Width / 2, card.Bounds.Y + card.Bounds.Height / 2)?.Widget.Id == card.Widget.Id, "Touch resolves the displayed widget at its scrolled content position");
        for (var i = 0; i < layout.Cards.Length; i++) for (var j = i + 1; j < layout.Cards.Length; j++)
        {
            var a = layout.Cards[i].Bounds; var b = layout.Cards[j].Bounds;
            Check(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y, "Responsive cards do not overlap");
        }
        Check(Wire.Serialize(dashboard) == original, "Phone fitting never rewrites Designer geometry");
    }
    return Task.CompletedTask;
});
await Test("Landscape and larger Android fonts scroll without squeezing controls", () =>
{
    var dashboard = ChudLink.Presets.Library.Create().Single(d => d.Id == "hardware");
    var landscape = PhoneLayout.Arrange(dashboard, 0, 740, 260);
    Check(landscape.Cards.Select(c => c.Bounds.X).Distinct().Count() == 2 && landscape.ContentHeight > 260, "Landscape fits two readable columns and scrolls the remaining rows");
    var normal = PhoneLayout.Arrange(dashboard, 0, 740, 560); var large = PhoneLayout.Arrange(dashboard, 0, 740, 560, fontScale: 1.8);
    Check(large.Cards.Select(c => c.Bounds.X).Distinct().Count() == 1 && large.Cards[0].Bounds.Height > normal.Cards[0].Bounds.Height, "Larger system text gets more space rather than clipping");
    Check(PhoneLayout.Arrange(new Dashboard(), 0, 360, 560).Cards.Length == 0 && PhoneLayout.Arrange(dashboard, 0, 0, 0).ContentHeight == 0, "Blank and not-yet-measured views are safe");
    return Task.CompletedTask;
});
await Test("Original Designer layout remains available with layer-aware hit testing", () =>
{
    var d = new Dashboard(); d.Pages[0].Widgets.AddRange([new() { Id = "back", X = 24, Y = 24, Layer = 0 }, new() { Id = "front", X = 24, Y = 24, Layer = 1 }]);
    var original = PhoneLayout.Arrange(d, 0, 720, 1280, autoFit: false);
    Check(original.HitTest(30, 30)?.Widget.Id == "front" && original.Cards[0].Bounds.X == 24, "Authored positions and top layer are preserved");
    return Task.CompletedTask;
});
var data = Path.Combine(root, "artifacts", "test-data", Guid.NewGuid().ToString("N"));
await Test("Telemetry probes only requested values and reuses slow snapshots", () =>
{
    using var sampler = new Telemetry();
    Check(sampler.Sample(new HashSet<string>()).Count == 0 && sampler.CpuProbeCount == 0 && sampler.VolumeProbeCount == 0 && sampler.NetworkEnumerationCount == 0, "Empty demand does no OS probes");
    for (var i = 0; i < 50; i++) sampler.Sample(new HashSet<string> { "host.storage" });
    Check(sampler.StorageProbeCount == 1 && sampler.MemoryProbeCount == 0 && sampler.VolumeProbeCount == 0, "Disk snapshot reused and unrelated probes absent");
    for (var i = 0; i < 3; i++) sampler.Sample(new HashSet<string> { "host.network" });
    Check(sampler.NetworkEnumerationCount == 1 && sampler.NetworkProbeCount == 3, "Adapter list is cached while counters stay real");
    Check(sampler.Sample(new HashSet<string> { "host.processMemory" })["host.processMemory"].Value > 0, "Process reading is actual working set");
    return Task.CompletedTask;
});
await Test("Buffered extension IPC preserves line framing and the payload bound", async () =>
{
    var longLine = new string('x', 20000); var lines = new BoundedLineReader(new StringReader("first\r\n\n" + longLine + "\nlast"));
    Check(await lines.ReadAsync() == "first" && await lines.ReadAsync() == "" && await lines.ReadAsync() == longLine && await lines.ReadAsync() == "last" && await lines.ReadAsync() == null, "Multiple lines, buffer splits, CRLF and final EOF");
    var oversized = new BoundedLineReader(new StringReader(new string('x', 1025)), 1024); var rejected = false;
    try { await oversized.ReadAsync(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "IPC bound enforced before unbounded buffering");
});
await using var host = new HostRuntime(data);
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
await Test("Host starts on one selected interface with persistent certificate", async () =>
{
    await host.StartAsync("127.0.0.1", port); Check(host.Fingerprint.Length == 64, "SHA256 fingerprint"); using var cert = host.Store.Certificate(); Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)) == host.Fingerprint, "Persistent identity");
});
PhoneConnection Client(string token = "", string? id = null) => new(id ?? Guid.NewGuid().ToString("N"), host.Address, host.Fingerprint, token);
await Test("Pinned HTTPS health and wrong-pin rejection", async () =>
{
    using var good = Client(); using var http = good.Client(); Check((await http.GetStringAsync("/health")).Contains("ChudLink"), "HTTPS health");
    using var bad = new PhoneConnection("id", host.Address, new string('0', 64), ""); using var wrong = bad.Client();
    var failed = false; try { await wrong.GetAsync("/health"); } catch (HttpRequestException) { failed = true; } Check(failed, "Wrong fingerprint must fail");
});
await Test("Pairing requires valid code and protocol", async () =>
{
    using var c = Client(); using var http = c.Client();
    var bad = await http.PostAsync("/pair", new StringContent(Wire.Serialize(new PairRequest("wrong", c.DeviceId, "test", "test", Wire.Version)), System.Text.Encoding.UTF8, "application/json")); Check(bad.StatusCode == HttpStatusCode.BadRequest, "Wrong code rejected");
    host.NewPairingCode(); var version = await http.PostAsync("/pair", new StringContent(Wire.Serialize(new PairRequest(host.PairingCode, c.DeviceId, "test", "test", 99)), System.Text.Encoding.UTF8, "application/json")); Check(version.StatusCode == HttpStatusCode.BadRequest, "Version rejected");
});
await Test("USB bootstrap is one-use, preserves the Wi-Fi code, and still requires approval", async () =>
{
    using var c = Client(); using var http = c.Client();
    var manual = host.NewPairingCode(); var usb = host.NewUsbPairingCode();
    var body = Wire.Serialize(new PairRequest(usb, c.DeviceId, "USB test", "Android", Wire.Version));
    using var first = await http.PostAsync("/pair", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    Check(first.IsSuccessStatusCode, "USB bootstrap accepted over loopback");
    var ticket = Wire.Parse<PairTicket>(await first.Content.ReadAsStringAsync());
    Check(host.Pairings[ticket.Ticket].Token == null && ticket.Status == "pending", "USB does not bypass PC approval");
    using var second = await http.PostAsync("/pair", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    Check(second.StatusCode == HttpStatusCode.BadRequest && host.PairingCode == manual, "USB secret cannot replay or consume Wi-Fi code");
    host.Approve(ticket.Ticket, false);
});
await Test("USB bootstrap is rejected over LAN while pinned loopback remains available", async () =>
{
    var lan = LocalNetwork.Addresses().FirstOrDefault(ip => !IPAddress.IsLoopback(ip));
    if (lan == null) throw new Exception("This integration check needs a private network adapter");
    var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var lanPort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
    await using var lanHost = new HostRuntime(Path.Combine(data, "lan")); await lanHost.StartAsync(lan.ToString(), lanPort);
    using var phone = new PhoneConnection(Guid.NewGuid().ToString("N"), lanHost.Address, lanHost.Fingerprint, ""); using var http = phone.Client();
    var body = Wire.Serialize(new PairRequest(lanHost.NewUsbPairingCode(), phone.DeviceId, "LAN USB attempt", "Android", Wire.Version));
    using var rejected = await http.PostAsync("/pair", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    Check(rejected.StatusCode == HttpStatusCode.BadRequest, "LAN cannot use USB bootstrap");
    using var loopback = new PhoneConnection(phone.DeviceId, $"https://127.0.0.1:{lanPort}", lanHost.Fingerprint, ""); using var local = loopback.Client();
    using var accepted = await local.PostAsync("/pair", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    Check(accepted.IsSuccessStatusCode, "A LAN host also serves the pinned USB loopback endpoint");
});
await Test("USB reconnect repairs tunnels without repeatedly opening the app", async () =>
{
    var adb = new FakeUsbAdb(); await using var usb = new UsbAutoConnect(host, adb.Run);
    await usb.ScanAsync(); await usb.ScanAsync(); Check(adb.Opens == 1 && adb.Mapped, "One app launch per connection");
    adb.Mapped = false; await usb.ScanAsync(); Check(adb.Opens == 2 && adb.Mapped, "Lost tunnel repaired");
    adb.Attached = false; await usb.ScanAsync(); adb.Attached = true; await usb.ScanAsync(); Check(adb.Opens == 3, "Replug launches and reconnects");
    await usb.StopAsync(); Check(!adb.Mapped && adb.Removed == 1, "Stop removes only ChudLink's tunnel");
});
await Test("USB handles unauthorized devices, missing APKs, and ignores wireless ADB", async () =>
{
    var adb = new FakeUsbAdb { State = "unauthorized" }; await using var usb = new UsbAutoConnect(host, adb.Run);
    await usb.ScanAsync(); Check(adb.Opens == 0 && usb.Status.Contains("Allow USB debugging"), "Phone approval stays with user");
    adb.State = "device"; adb.MissingApp = true; await usb.ScanAsync(); Check(usb.Status.Contains("latest ChudLink APK"), "Missing APK is actionable");
    adb.MissingApp = false; await usb.ScanAsync(retry: true); Check(adb.Opens == 2 && usb.Status.Contains("USB ready"), "Retry can launch an installed APK");
    adb.Attached = false; adb.Wireless = true; var before = adb.Opens; await usb.ScanAsync(); Check(adb.Opens == before && usb.Status.Contains("Waiting"), "Wireless ADB never treated as USB");
});
await Test("Stable USB scans avoid repeated device and tunnel child processes", () => UsbPerformanceChecks.StableScansAsync(host));
await Test("USB smart-socket framing accepts split packets and rejects malformed lengths", UsbPerformanceChecks.SmartSocketAsync);
await Test("Rapid USB stop/start and disposal leave no background watcher or tunnel", () => UsbPerformanceChecks.LifecycleAsync(host));
await Test("Phone falls back to another address without changing the certificate pin", async () =>
{
    using var phone = Client(); var code = host.NewPairingCode(); var pairing = phone.PairAsync(code, "Fallback test", "Android", CancellationToken.None);
    await Until(() => host.Pairings.Values.Any(p => p.Request.DeviceId == phone.DeviceId), "Fallback test pair pending");
    host.Approve(host.Pairings.Values.Single(p => p.Request.DeviceId == phone.DeviceId).Ticket, true); await pairing;
    var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
    using var fallback = new PhoneConnection(phone.DeviceId, $"https://127.0.0.1:{closedPort}", phone.Fingerprint, phone.Token, [host.Address]);
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var run = fallback.RunAsync(() => new("Fallback", "Android", "test", 90, "Portrait", ["canvas"], RenderProfile.Balanced), cancel.Token);
    try { await Until(() => fallback.Connected && fallback.Address == host.Address, "Saved LAN/USB fallback did not connect"); }
    finally { await cancel.CancelAsync(); fallback.Dispose(); try { await run; } catch (OperationCanceledException) { } host.Revoke(phone.DeviceId); }
});
async Task<PhoneConnection> Pair(string name)
{
    var phone = Client(); var code = host.NewPairingCode();
    var pending = phone.PairAsync(code, name, "test Android client", CancellationToken.None);
    await Until(() => host.Pairings.Values.Any(p => p.Request.DeviceId == phone.DeviceId), "Pair request not received");
    var pair = host.Pairings.Values.First(p => p.Request.DeviceId == phone.DeviceId);
    Check(pair.Token == null && pair.Status == "pending", "No credential before local approval"); host.Approve(pair.Ticket, true);
    await pending; Check(phone.Token.Length == 64, "Strong issued token"); return phone;
}
PhoneConnection? a = null, b = null;
await Test("Phone pairing with explicit local approval", async () => { a = await Pair("Phone A"); });
await Test("A second independently authenticated device", async () => { b = await Pair("Phone B"); Check(a!.Token != b.Token && host.Devices().Length == 2, "Independent identities"); });
await Test("No remote management or unauthenticated socket access", async () =>
{
    using var c = Client(); using var http = c.Client(); Check((await http.GetAsync("/connect")).StatusCode == HttpStatusCode.Unauthorized, "Socket auth"); Check((await http.PostAsync("/approve", null)).StatusCode == HttpStatusCode.NotFound, "No remote admin route");
});
using var stop = new CancellationTokenSource(); var aMessages = Channel.CreateUnbounded<Envelope>(); var bMessages = Channel.CreateUnbounded<Envelope>();
var tasks = new List<Task>();
DeviceHello Hello() => new("Tests", "Android protocol simulation", "0.1.0", 87, "Portrait", ["canvas", "touch"], RenderProfile.Performance);
async Task<Envelope> Read(Channel<Envelope> channel, string type, Func<Envelope, bool>? filter = null)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (await channel.Reader.WaitToReadAsync(timeout.Token)) while (channel.Reader.TryRead(out var envelope)) if (envelope.Type == type && (filter == null || filter(envelope))) return envelope;
    throw new Exception("Missing " + type);
}
if (a != null && b != null)
{
    a.Message += e => aMessages.Writer.TryWrite(e); b.Message += e => bMessages.Writer.TryWrite(e);
    tasks.Add(a.RunAsync(Hello, stop.Token)); tasks.Add(b.RunAsync(Hello, stop.Token));
    await Test("Both phones receive assigned dashboards", async () => { await Read(aMessages, "dashboard"); await Read(bMessages, "dashboard"); await Until(() => host.Devices().All(d => d.Connected), "Both online"); });
    await Test("Live real telemetry uses incremental patches", async () => { var e = await Read(aMessages, "patch"); Check(e.Read<WidgetPatch[]>().Any(p => p.Id == "ram"), "Real memory patch"); });
    await Test("Slow HTTP integrations cannot stall dashboard telemetry", async () =>
    {
        using var slow = new TcpListener(IPAddress.Loopback, 0); slow.Start(); var slowPort = ((IPEndPoint)slow.LocalEndpoint).Port;
        var ids = new[] { "perf-slow-a", "perf-slow-b", "perf-slow-c" };
        lock (host.Store.Settings.Endpoints) foreach (var id in ids) host.Store.Settings.Endpoints.Add(new(id, "Slow integration", $"http://127.0.0.1:{slowPort}/"));
        try
        {
            using var accepted = await slow.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15));
            while (aMessages.Reader.TryRead(out _)) { }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while ((await aMessages.Reader.ReadAsync(deadline.Token)).Type != "patch") { }
        }
        finally { lock (host.Store.Settings.Endpoints) host.Store.Settings.Endpoints.RemoveAll(e => ids.Contains(e.Id)); }
    });
    await Test("Unapproved PC actions cannot be invoked", async () => { var blocked = false; try { await host.RunApprovedActionAsync("not-approved", a.DeviceId); } catch (UnauthorizedAccessException) { blocked = true; } Check(blocked, "No actions permission"); });
    await Test("Shared counter and independent dashboard assignments", async () =>
    {
        await host.AssignAsync(a.DeviceId, "control"); await Read(aMessages, "dashboard", e => e.Read<Dashboard>().Id == "control"); await a.SendAsync("touch", new TouchEvent("count"));
        await Until(() => host.Shared.GetValueOrDefault("counter") == "1", "Counter action"); Check(host.Devices().First(d => d.Id == b.DeviceId).DashboardId == "hardware", "Other device stays independent");
    });
    await Test("Host-routed mesh respects explicit permission and group", async () =>
    {
        host.ConfigureDevice(a.DeviceId, "Phone A", "desk", ["dashboard", "counter", "mesh", "actions"]); host.ConfigureDevice(b.DeviceId, "Phone B", "desk", ["dashboard", "counter"]);
        var board = new DashboardBuilder("Mesh test").Id("mesh-test").Add(new Widget { Id = "switch", Kind = WidgetKind.Button, Bindings = [new("mesh", b.DeviceId + "|ambient")] }).Build(); await host.PublishAsync(board); await host.AssignAsync(a.DeviceId, board.Id);
        await Read(aMessages, "dashboard", e => e.Read<Dashboard>().Id == board.Id); await a.SendAsync("touch", new TouchEvent("switch"));
        await Read(bMessages, "dashboard", e => e.Read<Dashboard>().Id == "ambient");
    });
    await Test("Roslyn source builds with diagnostics", () =>
    {
        var bad = ExtensionCompiler.Build("not valid csharp", Path.Combine(data, "bad-build")); Check(!bad.Success && bad.Diagnostics.Length > 0, "Compiler diagnostics");
        var good = ExtensionCompiler.Build(File.ReadAllText(Path.Combine(root, "examples", "HelloWorld", "Extension.cs")), Path.Combine(data, "extension")); Check(good.Success, string.Join("\n", good.Diagnostics)); return Task.CompletedTask;
    });
    await Test("Trusted extension runs in limited worker and handles phone events", async () =>
    {
        var manifest = new ExtensionManifest("example.hello", "Hello", "Tests", "0.1.0", "0.1.0", ["dashboard", "actions", "notifications"]);
        await host.Workers.StartAsync(Path.Combine(data, "extension", "Extension.dll"), manifest, true);
        await Until(() => host.Store.Dashboards.ContainsKey("example.hello.home"), "Extension dashboard missing. " + string.Join("\n", host.Logs.Snapshot()));
        await host.AssignAsync(a.DeviceId, "example.hello.home"); await Read(aMessages, "dashboard", e => e.Read<Dashboard>().Id == "example.hello.home");
        await a.SendAsync("touch", new TouchEvent("hello"));
        await Read(aMessages, "patch", e => e.Read<WidgetPatch[]>().Any(p => p.Text == "Hello from your PC!"));
        await host.Workers.StopAsync("example.hello"); Check(host.Workers.Active.Length == 0, "Worker stopped");
    });
    await Test("Untrusted extension cannot be enabled automatically", async () =>
    {
        var denied = false; try { await host.Workers.StartAsync("unused", new("example.untrusted", "Unknown", "Unknown", "0.1.0", "0.1.0", []), false); } catch (UnauthorizedAccessException) { denied = true; } Check(denied, "Trust opt-in");
    });
    await Test("Revoking a credential closes the socket and prevents reconnect", async () =>
    {
        host.Revoke(b.DeviceId); await Until(() => !b.Connected, "Revoked socket closed"); using var http = b.Client(); using var request = new HttpRequestMessage(HttpMethod.Get, "/connect"); request.Headers.Authorization = new("Bearer", b.Token); Check((await http.SendAsync(request)).StatusCode == HttpStatusCode.Unauthorized, "Revoked credential rejected");
    });
}
await Test("Extension ZIP rejects path traversal and unknown permissions", () =>
{
    var path = Path.Combine(data, "bad.chudext"); using (var z = ZipFile.Open(path, ZipArchiveMode.Create)) { using var manifest = new StreamWriter(z.CreateEntry("manifest.json").Open()); manifest.Write(Wire.Serialize(new ExtensionManifest("bad.test", "Bad", "Unknown", "0.1.0", "0.1.0", []))); manifest.Dispose(); z.CreateEntry("Extension.dll"); z.CreateEntry("../escape"); }
    Throws(() => ExtensionPackage.Inspect(path)); Throws(() => ExtensionPackage.ValidateManifest(new("valid.id", "Name", "Author", "0.1.0", "0.1.0", ["shell"]))); return Task.CompletedTask;
});
await Test("Custom widget registration materializes its portable fallback", async () =>
{
    host.CustomWidgets["test.custom"] = new() { Kind = WidgetKind.Progress, Maximum = 256 };
    var dashboard = new DashboardBuilder("Custom").Id("test.custom.home").Add(new Widget { Id = "custom", Kind = WidgetKind.Custom, Label = "MY WIDGET", Properties = { ["type"] = "test.custom" } }).Build();
    await host.PublishAsync(dashboard); var w = host.Store.Dashboards[dashboard.Id].Pages[0].Widgets[0];
    Check(w.Kind == WidgetKind.Progress && w.Maximum == 256 && w.Id == "custom", "Portable fallback keeps identity and uses registered rendering");
});
await stop.CancelAsync(); a?.Dispose(); b?.Dispose(); foreach (var task in tasks) try { await task; } catch (OperationCanceledException) { }
var report = Path.Combine(root, "artifacts", "test-results.json"); File.WriteAllText(report, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, failures, tests = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count - failures}/{results.Count} tests passed. Report: {report}"); return failures == 0 ? 0 : 1;

static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent; return d?.FullName ?? throw new DirectoryNotFoundException("Repository root"); }
void PackExamples()
{
    var output = Path.Combine(root, "artifacts", "extensions"); Directory.CreateDirectory(output);
    foreach (var source in Directory.GetFiles(Path.Combine(root, "examples"), "Extension.cs", SearchOption.AllDirectories))
    {
        var name = new DirectoryInfo(Path.GetDirectoryName(source)!).Name;
        var folder = Path.Combine(root, "artifacts", "example-builds", name); Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        var code = File.ReadAllText(source); var result = ExtensionCompiler.Build(code, folder);
        if (!result.Success) throw new Exception(name + ": " + string.Join("\n", result.Diagnostics));
        var id = System.Text.RegularExpressions.Regex.Match(code, "\\[ChudExtension\\(\"([^\"]+)\"").Groups[1].Value;
        string[] permissions = name switch
        {
            "HelloWorld" or "BuildMonitor" => ["dashboard", "actions", "notifications"],
            "CustomCounter" => ["dashboard", "actions"],
            "CrossDevice" => ["dashboard", "actions", "mesh", "notifications"],
            "JsonStatus" => ["dashboard", "providers"],
            "SensorDashboard" => ["dashboard", "sensors"],
            _ => ["dashboard"]
        };
        File.WriteAllText(Path.Combine(folder, "manifest.json"), Wire.Serialize(new ExtensionManifest(id, name, "ChudAI", "0.1.0", "0.1.0", permissions)));
        File.WriteAllText(Path.Combine(folder, "Extension.cs"), code);
        File.WriteAllText(Path.Combine(folder, "settings.schema.json"), "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"},\"property\":{\"type\":\"string\"},\"buildActionId\":{\"type\":\"string\"},\"targetDeviceId\":{\"type\":\"string\"}}}");
        ExtensionPackage.Pack(folder, Path.Combine(output, name + ".chudext")); Console.WriteLine("Packaged " + name);
    }
}
