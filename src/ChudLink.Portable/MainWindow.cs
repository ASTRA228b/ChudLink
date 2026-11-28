using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChudLink.Core;
using ChudLink.Host;
using ChudLink.Protocol;

namespace ChudLink.Portable;

sealed record DeviceChoice(string Id, string Display) { public override string ToString() => Display; }
sealed record PairChoice(string Ticket, string Display) { public override string ToString() => Display; }

public sealed class MainWindow : Window
{
    readonly HostRuntime host = DraftHost.Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChudAI", "ChudLink-Portable-Draft"));
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly TextBlock notice = Text("Choose your private network and start the draft host.", 14);
    readonly TextBlock state = Text("HOST STOPPED", 14, "#C6FF2E");
    readonly TextBlock code = Text("—", 30, "#C6FF2E");
    readonly TextBox fingerprint = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
    readonly ListBox pending = new() { MinHeight = 80, MaxHeight = 160 };
    readonly ListBox devices = new() { MinHeight = 150, MaxHeight = 260 };
    readonly ComboBox boards = new() { MinWidth = 270 };
    readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220 };
    readonly TextBox rename = new() { Watermark = "Device name", MinWidth = 180 };
    readonly TextBox group = new() { Watermark = "Mesh group (optional)", MinWidth = 180 };
    string deviceStamp = "", pendingStamp = "", logStamp = "";
    bool closing;

    public MainWindow()
    {
        Title = "ChudLink — Linux / macOS Draft"; Width = 1040; Height = 820; MinWidth = 760; MinHeight = 600;
        Background = Brush("#090D0A"); Foreground = Brush("#E7EDE8");
        var banner = new StackPanel { Spacing = 8 };
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var logo = Path.Combine(AppContext.BaseDirectory, "Assets", "ChudLink.png");
        if (File.Exists(logo)) identity.Children.Add(new Image { Source = new Bitmap(logo), Width = 56, Height = 56 });
        identity.Children.Add(Text("ChudLink", 32, "#C6FF2E")); banner.Children.Add(identity);
        banner.Children.Add(Text("BY CHUDAI / PORTABLE DRAFT", 12, "#B9A3EE"));
        banner.Children.Add(Text("Linux and macOS development build · Cross-published on Windows · Not tested on either native OS · Unsigned", 14, "#F2C078"));
        banner.Children.Add(state); banner.Children.Add(notice);
        var tabs = new TabControl { ItemsSource = new[]
        {
            new TabItem { Header = "Connect phones", Content = ConnectionPage() },
            new TabItem { Header = "Dashboards", Content = DashboardsPage() },
            new TabItem { Header = "Guide / limitations", Content = GuidePage() },
            new TabItem { Header = "Diagnostics", Content = log }
        }};
        tabs.SelectionChanged += (_, _) => Refresh();
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(24), RowSpacing = 18 };
        grid.Children.Add(banner); Grid.SetRow(tabs, 1); grid.Children.Add(tabs); Content = grid;
        timer.Tick += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) Refresh(); }; timer.Start();
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; timer.Stop();
            try { await host.DisposeAsync(); } finally { Close(); }
        };
    }

    static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    static TextBlock Text(string value, double size = 14, string color = "#B3C3B8") => new() { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
    static StackPanel Stack(params Control[] children) { var panel = new StackPanel { Spacing = 12 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    static WrapPanel Row(params Control[] children) { var panel = new WrapPanel(); foreach (var child in children) { child.Margin = new Thickness(0, 0, 10, 10); panel.Children.Add(child); } return panel; }
    Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 9), Foreground = Brush("#C6FF2E") };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); Refresh(); } catch (Exception ex) { notice.Text = ex.Message; } finally { button.IsEnabled = true; } };
        return button;
    }
    Button Button(string label, Action action) => Button(label, () => { action(); return Task.CompletedTask; });
    static Border Card(Control content) => new() { Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), Background = Brush("#111913"), BorderBrush = Brush("#29392D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = content };
    static ScrollViewer Scroll(Control content) => new() { Content = content };

    Control ConnectionPage()
    {
        var addresses = new ComboBox { ItemsSource = LocalNetwork.Addresses().Select(a => a.ToString()).ToArray(), SelectedIndex = 0, MinWidth = 220 };
        boards.ItemsSource = host.Store.Dashboards.Values.OrderBy(d => d.Name).ToArray(); boards.SelectedIndex = 0;
        devices.SelectionChanged += (_, _) =>
        {
            if (devices.SelectedItem is not DeviceChoice choice || host.Devices().FirstOrDefault(d => d.Id == choice.Id) is not { } phone) return;
            rename.Text = phone.Name; group.Text = phone.Group;
        };
        return Scroll(Stack(
            Card(Stack(Text("1. Start the local host", 22, "#E7EDE8"), Text("Choose the private LAN address your phone can reach. Firewall configuration is manual in this draft."),
                Row(addresses, Button("Start host", async () => { await host.StartAsync(addresses.SelectedItem?.ToString() ?? "127.0.0.1"); host.NewPairingCode(); notice.Text = "Host ready. Open ChudLink on the same Wi-Fi, enter the code and compare the fingerprint."; })))),
            Card(Stack(Text("2. Pair your Android phone", 22, "#E7EDE8"), code,
                Button("New two-minute code", () => { if (!host.Running) throw new InvalidOperationException("Start the host first."); host.NewPairingCode(); }),
                Text("Compare the entire SHA-256 fingerprint on both screens. Discovery is not proof of identity."), fingerprint)),
            Card(Stack(Text("3. Approve the phone here", 22, "#E7EDE8"), pending,
                Row(Button("Approve selected phone", () => { if (pending.SelectedItem is PairChoice pair && host.Pairings.TryGetValue(pair.Ticket, out var request)) { host.Approve(pair.Ticket, true); host.ConfigureDevice(request.Request.DeviceId, request.Request.Name, "", ["dashboard", "counter", "mesh"]); } }),
                    Button("Decline", () => { if (pending.SelectedItem is PairChoice pair) host.Approve(pair.Ticket, false); })))),
            Card(Stack(Text("4. Assign a dashboard", 22, "#E7EDE8"), devices, Row(rename, group,
                Button("Save phone", () => { if (devices.SelectedItem is DeviceChoice choice) host.ConfigureDevice(choice.Id, rename.Text ?? "Android", group.Text ?? "", ["dashboard", "counter", "mesh"]); })),
                Row(boards, Button("Assign dashboard", async () => { if (devices.SelectedItem is DeviceChoice choice && boards.SelectedItem is Dashboard board) await host.AssignAsync(choice.Id, board.Id); }),
                    Button("Revoke selected phone", () => { if (devices.SelectedItem is DeviceChoice choice) host.Revoke(choice.Id); }))))));
    }

    Control DashboardsPage()
    {
        var list = new ListBox { ItemsSource = host.Store.Dashboards.Values.OrderBy(d => d.Name).ToArray(), MinHeight = 250 };
        var description = Text("Select a dashboard. Full visual Designer and C# Studio are Windows-only in this draft.");
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is Dashboard d) description.Text = d.Description + "\n" + d.Pages.Count + " page(s), " + d.Pages.Sum(p => p.Widgets.Count) + " widgets."; };
        return Scroll(Card(Stack(Text("Portable dashboard library", 24, "#E7EDE8"), list, description,
            Row(Button("Import .chudlink", async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import dashboard", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("ChudLink dashboard") { Patterns = ["*.chudlink"] }] });
                if (files.Count == 0) return;
                await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
                var chars = new char[1000001]; var count = await reader.ReadBlockAsync(chars.AsMemory());
                if (count > 1000000) throw new InvalidDataException("Dashboard exceeds import limit.");
                var json = new string(chars, 0, count);
                var board = Wire.Parse<Dashboard>(json); board.Validate();
                if (new[] { "hardware", "control", "audio", "soundboard", "developer", "ai", "network", "productivity", "gaming", "ambient", "blank" }.Contains(board.Id))
                {
                    board.Id = "portable.import." + Guid.NewGuid().ToString("N");
                    board.Name += " (imported)";
                }
                await host.PublishAsync(board);
                list.ItemsSource = host.Store.Dashboards.Values.OrderBy(d => d.Name).ToArray(); boards.ItemsSource = host.Store.Dashboards.Values.OrderBy(d => d.Name).ToArray(); notice.Text = "Dashboard imported.";
            }), Button("Export selected", async () =>
            {
                if (list.SelectedItem is not Dashboard board) return;
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export dashboard", SuggestedFileName = "Dashboard.chudlink", DefaultExtension = "chudlink" });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(Wire.Serialize(board));
            })))));
    }

    Control GuidePage() => Scroll(Card(Stack(Text("Draft scope", 24, "#E7EDE8"),
        Text("Available: local private-network TLS/WSS host, SHA-256 pin verification, expiring pairing code, explicit approval, stored/revoked device credentials, dashboard assignment/import/export, native Android rendering, shared counter, and host-routed mesh."),
        Text("Telemetry: real host-process memory/uptime, network throughput and filesystem usage. System CPU/RAM, GPU, temperature, master volume and media controls are not provided in this draft; unsupported values stay absent."),
        Text("Not included: WPF Designer, C# Studio/extension workers, Windows Job Objects, USB/ADB automation, global shortcuts, media/audio routing, background tray service, OS startup registration, macOS signing/notarization and native installers.", 14, "#F2C078"),
        Text("Connection steps", 22, "#C6FF2E"),
        Text("1. Start this host on its private LAN address.\n2. Put the Android phone on the same network and open ChudLink.\n3. Select this host, enter its code and compare the complete fingerprint.\n4. Approve the phone here.\n5. Assign a supported dashboard. The phone reconnects with its saved identity."),
        Text("If discovery is blocked, configure the host firewall for TCP 47831 and UDP 47832 on the intended private local network, or enter its address/fingerprint under Manual connection on the phone. Guest/client-isolated Wi-Fi may prevent access."),
        Text("Build and test status", 22, "#C6FF2E"),
        Text("These Linux and macOS packages were cross-published on Windows. They have NOT been executed or visually tested on Linux or macOS. The transport self-test runs independently of the native GUI. Native dependencies, local-network permissions, graphics, installer behavior and signing must be validated on each OS."),
        Text("Data folder: " + host.Store.Root, 12))));

    void Refresh()
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        state.Text = host.Running ? host.Address + " · " + host.Devices().Count(d => d.Connected) + " connected" : "HOST STOPPED";
        code.Text = host.Running ? host.PairingCode : "Start the host first"; fingerprint.Text = host.Fingerprint;
        var pairings = host.Pairings.Values.Where(p => p.Status == "pending" && p.Expires > DateTimeOffset.UtcNow).Select(p => new PairChoice(p.Ticket, p.ToString())).ToArray();
        var pairStamp = string.Join('|', pairings.Select(p => p.Ticket));
        if (pairStamp != pendingStamp) { var id = (pending.SelectedItem as PairChoice)?.Ticket; pending.ItemsSource = pairings; pending.SelectedItem = pairings.FirstOrDefault(p => p.Ticket == id) ?? pairings.FirstOrDefault(); pendingStamp = pairStamp; }
        var phones = host.Devices().Select(d => new DeviceChoice(d.Id, d.ToString())).ToArray(); var stamp = string.Join('|', phones.Select(p => p.Id + p.Display));
        if (stamp != deviceStamp) { var id = (devices.SelectedItem as DeviceChoice)?.Id; devices.ItemsSource = phones; devices.SelectedItem = phones.FirstOrDefault(p => p.Id == id); deviceStamp = stamp; }
        var lines = string.Join('\n', host.Logs.Snapshot()); if (lines != logStamp) { log.Text = lines; logStamp = lines; }
    }
}
