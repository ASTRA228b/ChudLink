using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using ChudLink.Core;
using ChudLink.Extensions;
using ChudLink.SDK;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;

namespace ChudLink.Desktop;

public partial class MainWindow : Window
{
    readonly HostRuntime host = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly System.Windows.Forms.NotifyIcon tray;
    bool quitting, busy;
    TimeSpan lastCpu;
    DateTimeOffset lastDiagnostics = DateTimeOffset.UtcNow;
    long lastUpdates, lastBytes;
    string deviceSignature = "";
    string current = "Overview";
    readonly Dictionary<string, TextBlock> stats = new();
    ListBox? deviceList, pendingList;
    TextBlock? pairingText;
    TextBlock? usbText;
    TextBlock? networkText;
    TextBox? diagnostics;
    DesignerView? designer;
    Dashboard? editing;
    TextEditor? editor;
    TextBox? compileOutput;
    CheckBox? trustCode;
    string? builtAssembly;
    string? activeStudioId;
    readonly string studioId = "local.studio";
    public MainWindow()
    {
        InitializeComponent();
        tray = new() { Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "ChudLink.ico")), Text = "ChudLink · local PC companion", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open ChudLink", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("Quit ChudLink", null, (_, _) => Dispatcher.Invoke(async () => await QuitAsync())); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        Closing += OnClosing; timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => UpdateRefreshActivity(); StateChanged += (_, _) => UpdateRefreshActivity();
        Loaded += (_, _) => UpdateRefreshActivity(); Navigation.SelectedIndex = 0;
        var smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");
        if (smokeTest) Loaded += (_, _) => { Notice("Desktop smoke-test mode; host remains stopped."); };
        else if (host.Store.Settings.AutoStartHost || host.Store.Settings.UsbAutoConnect) Loaded += async (_, _) =>
        {
            try { await host.StartAsync(PreferredAddress()); host.NewPairingCode(); Notice("PC host ready. Open ChudLink on your phone, or choose Connect by USB for a cable connection."); }
            catch (Exception ex) { Notice("Could not start the PC host: " + ex.Message); }
        };
    }
    static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public static TextBlock Label(string text, double size = 13, string color = "#B3C3B8") => new() { Text = text, FontSize = size, Foreground = Brush(color), Margin = new(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    public static Button Btn(string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => action(); return b; }
    Button AsyncBtn(string text, Func<Task> action) => Btn(text, async () => { try { await action(); } catch (Exception ex) { Notice(ex.Message); } });
    public static StackPanel Row(params UIElement[] elements) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var e in elements) p.Children.Add(e); return p; }
    public static Border Card(UIElement child, double padding = 20) => new() { Child = child, Background = Brush("#111913"), BorderBrush = Brush("#29392D"), BorderThickness = new(1), CornerRadius = new(9), Padding = new(padding), Margin = new(0, 0, 14, 16) };
    static StackPanel Stack(params UIElement[] elements) { var p = new StackPanel(); foreach (var e in elements) p.Children.Add(e); return p; }
    public void Notice(string text) { NoticeText.Text = text; NoticeBorder.Visibility = Visibility.Visible; }
    void UpdateRefreshActivity()
    {
        host.OverviewTelemetryRequested = !quitting && IsVisible && WindowState != WindowState.Minimized && current == "Overview";
        var needsLiveUpdates = current is "Overview" or "Devices" or "Diagnostics";
        if (!quitting && IsVisible && WindowState != WindowState.Minimized && needsLiveUpdates)
        { Refresh(); timer.Start(); }
        else timer.Stop();
    }
    static void UpdateText(TextBlock? label, string value) { if (label != null && label.Text != value) label.Text = value; }
    static T[] SnapshotList<T>(List<T> values) { lock (values) return values.ToArray(); }
    void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is not ListBoxItem item || PageContent == null) return;
        if (designer != null && current == "Designer") editing = designer.Document;
        current = item.Content.ToString()!; Breadcrumb.Text = "WORKSPACE / " + current.ToUpperInvariant(); PageTitle.Text = current == "Overview" ? "Mission control" : current;
        stats.Clear(); deviceList = null; pendingList = null; pairingText = null; usbText = null; networkText = null; diagnostics = null; deviceSignature = "";
        PageContent.Content = current switch
        {
            "Overview" => Overview(), "Devices" => DevicesPage(), "Presets" => PresetsPage(), "Designer" => DesignerPage(),
            "Developer Studio" => Studio(), "Extensions" => ExtensionsPage(), "Settings" => SettingsPage(), "Diagnostics" => DiagnosticsPage(), "Connection guide" => ConnectionGuide(), _ => AboutPage()
        };
        UpdateRefreshActivity();
        if (!timer.IsEnabled) Refresh();
    }
    UIElement Overview()
    {
        var page = new StackPanel();
        var intro = Stack(Label("BUILD YOUR NEXT WORKSPACE", 11, "#C6FF2E"), Label("One PC. Any number of possibilities.", 30, "#F0F5F1"), Label("Turn your phones into programmable companions. Everything runs on your local network.", 14));
        intro.MaxWidth = 500; intro.HorizontalAlignment = HorizontalAlignment.Left;
        var hero = new Grid { Height = 232 };
        hero.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Companions.png")), Stretch = Stretch.UniformToFill, ClipToBounds = true });
        hero.Children.Add(new Border { Background = new LinearGradientBrush(new GradientStopCollection { new(System.Windows.Media.Color.FromArgb(245, 10, 14, 11), 0), new(System.Windows.Media.Color.FromArgb(180, 10, 14, 11), .5), new(System.Windows.Media.Color.FromArgb(0, 10, 14, 11), 1) }, new Point(0, .5), new Point(1, .5)) });
        intro.Margin = new(26); intro.VerticalAlignment = VerticalAlignment.Center; hero.Children.Add(intro);
        page.Children.Add(Card(hero, 0));
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var (id, title) in new[] { ("devices", "CONNECTED DEVICES"), ("cpu", "SYSTEM CPU"), ("ram", "SYSTEM MEMORY"), ("traffic", "DATA SENT") })
        {
            var value = Label("—", 28, "#C6FF2E"); stats[id] = value; cards.Children.Add(Card(Stack(Label(title, 10), value), 18));
        }
        page.Children.Add(cards);
        page.Children.Add(UsbPanel());
        var addresses = new ComboBox { ItemsSource = LocalNetwork.Addresses().Select(a => a.ToString()).ToArray(), Width = 220 };
        addresses.SelectedItem = PreferredAddress();
        networkText = Label("Starting the PC host…", 15, "#C6FF2E");
        var connect = Stack(Label("WI-FI CONNECTION", 11, "#C6FF2E"), networkText, Label("The host starts automatically on your active network. Use the same Wi-Fi on your phone. Allow ChudLink on private networks if Windows Firewall asks. USB does not need Wi-Fi discovery.", 13),
            Row(addresses, AsyncBtn("Start host", async () => { await host.StartAsync(addresses.SelectedItem?.ToString() ?? "127.0.0.1"); if (host.PairingCode.Length != 6) host.NewPairingCode(); Notice("Host ready. Open Devices for the Wi-Fi code, or plug in a USB phone."); })),
            Label("Close the window to keep the host in the system tray. Use Quit ChudLink to stop it.", 12));
        page.Children.Add(Card(connect));
        page.Children.Add(Row(Btn("Pair a phone  ↗", () => Navigation.SelectedIndex = 1), Btn("Connection guide", () => Navigation.SelectedIndex = 8), Btn("Explore presets  ↗", () => Navigation.SelectedIndex = 2), Btn("Open Designer  ↗", () => Navigation.SelectedIndex = 3)));
        return new ScrollViewer { Content = page };
    }
    UIElement DevicesPage()
    {
        var page = new StackPanel();
        page.Children.Add(UsbPanel());
        if (host.Running && host.PairingCode.Length != 6) host.NewPairingCode();
        var fingerprint = new TextBox { Text = host.Fingerprint, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new("Consolas") };
        pairingText = Label(host.Running ? host.PairingCode : "Start the host in Overview first", 27, "#C6FF2E");
        page.Children.Add(Card(Stack(Label("CONNECT OVER WI-FI", 11, "#C6FF2E"), Label(host.Running ? host.Address : "Start the Wi-Fi host in Overview first", 19), Label("Open ChudLink on the same Wi-Fi. Select this PC and enter the code below. Confirm this fingerprint when the phone asks.", 13), fingerprint,
            Row(Btn("Generate 2-minute code", () => { if (host.Running) pairingText.Text = host.NewPairingCode(); else Notice("Start the host in Overview first."); }), pairingText),
            Row(AsyncBtn("Allow Wi-Fi connection", ConfigureWifiAsync), Btn("Step-by-step guide", () => Navigation.SelectedIndex = 8)))));
        pendingList = new ListBox { Height = 84 };
        page.Children.Add(Card(Stack(Label("AWAITING YOUR APPROVAL", 11), pendingList, Row(Btn("Approve selected phone", () => { if (pendingList.SelectedItem is PendingPair p) host.Approve(p.Ticket, true); }), Btn("Decline", () => { if (pendingList.SelectedItem is PendingPair p) host.Approve(p.Ticket, false); })))));
        deviceList = new ListBox { MinHeight = 130, MaxHeight = 210 };
        var rename = new TextBox { Width = 230, ToolTip = "Device name" }; var group = new TextBox { Width = 180, ToolTip = "Mesh group" };
        var assigned = new ComboBox { ItemsSource = host.Store.Dashboards.Values.OrderBy(d => d.Name).ToArray(), DisplayMemberPath = "Name", Width = 230 };
        CheckBox Perm(string title, string id) => new() { Content = title, Tag = id };
        var permissions = new[] { Perm("Dashboards", "dashboard"), Perm("Counter", "counter"), Perm("Media", "media"), Perm("Approved PC actions", "actions"), Perm("Mesh", "mesh"), Perm("Sensors", "sensors") };
        var permissionRow = new WrapPanel(); foreach (var c in permissions) permissionRow.Children.Add(c);
        var details = Label("Select a phone to inspect its capabilities, software and latency.", 12);
        deviceList.SelectionChanged += (_, _) =>
        {
            if (deviceList.SelectedItem is not DeviceRecord d) return;
            rename.Text = d.Name; group.Text = d.Group; assigned.SelectedItem = host.Store.Dashboards.GetValueOrDefault(d.DashboardId);
            foreach (var p in permissions) p.IsChecked = d.Permissions.Contains((string)p.Tag);
            details.Text = $"{d.Id}\n{d.Info?.Platform} · {d.Info?.Version} · {d.Info?.Orientation} · {d.LatencyMs:F0} ms RTT\nCapabilities: {string.Join(", ", d.Info?.Capabilities ?? [])}";
        };
        page.Children.Add(Card(Stack(Label("PAIRED DEVICES", 11, "#C6FF2E"), deviceList, details, Label("Name / mesh group", 11), Row(rename, group), permissionRow,
            Row(Btn("Save device", () => { if (deviceList.SelectedItem is DeviceRecord d) { host.ConfigureDevice(d.Id, rename.Text, group.Text, permissions.Where(p => p.IsChecked == true).Select(p => (string)p.Tag).ToHashSet()); Notice("Device configuration saved."); } }),
            assigned, AsyncBtn("Assign dashboard", async () => { if (deviceList.SelectedItem is DeviceRecord d && assigned.SelectedItem is Dashboard board) await host.AssignAsync(d.Id, board.Id); }),
            Btn("Revoke credential", () => { if (deviceList.SelectedItem is DeviceRecord d) { host.Revoke(d.Id); Notice("Device revoked. It must pair again to reconnect."); } })))));
        return new ScrollViewer { Content = page };
    }
    UIElement UsbPanel()
    {
        usbText = Label(host.Usb.Status, 14, "#C6FF2E");
        return Card(Stack(Label("PLUG IN. CONNECT.", 11, "#C6FF2E"), Label("Connect your phone by USB", 23, "#F0F5F1"),
            Label("1. Click Connect by USB below.\n2. Install the latest ChudLink APK on your phone.\n3. Enable USB debugging in Android Developer options.\n4. Plug in a data cable, unlock the phone, and tap Allow.\n5. Confirm the PC on the phone and approve it in Devices once.", 13),
            Label("Next time: plug in your phone while this PC app is running. ChudLink opens and reconnects automatically. Open Connection guide for detailed setup and cable help.", 13),
            Row(AsyncBtn("Connect by USB", async () => { await EnableUsbAsync(); Notice("USB auto-connect enabled. Plug in and unlock your phone. First-time approvals appear in Devices."); }),
                AsyncBtn("Retry USB", async () => { await EnableUsbAsync(); await host.Usb.ScanAsync(retry: true); }),
                AsyncBtn("Turn off USB", async () => { host.Store.Settings.UsbAutoConnect = false; host.Store.Save(); await host.Usb.StopAsync(); })), usbText));
    }
    async Task EnableUsbAsync()
    {
        host.Store.Settings.UsbAutoConnect = true; host.Store.Save();
        if (!host.Running)
        {
            await host.StartAsync(PreferredAddress());
        }
        host.Usb.Start();
    }
    string PreferredAddress()
    {
        var addresses = LocalNetwork.Addresses();
        return addresses.FirstOrDefault(ip => !System.Net.IPAddress.IsLoopback(ip) && ip.ToString() == host.Store.Settings.Address)?.ToString() ?? addresses[0].ToString();
    }
    async Task ConfigureWifiAsync()
    {
        // Windows handles elevation. The user initiates and approves this narrowly scoped setup.
        var program = (Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ChudLink.Desktop.exe")).Replace("'", "''");
        var command = "$ErrorActionPreference='Stop'; ";
        foreach (var rule in new[] { ("HTTPS", "TCP", host.Store.Settings.Port), ("Discovery", "UDP", Wire.DiscoveryPort) })
        {
            var name = "ChudLink.Local." + rule.Item1;
            var options = $"-Enabled True -Direction Inbound -Action Allow -Profile Private -Program '{program}' -Protocol {rule.Item2} -LocalPort {rule.Item3} -RemoteAddress LocalSubnet";
            command += $"if(Get-NetFirewallRule -Name '{name}' -ErrorAction SilentlyContinue){{Set-NetFirewallRule -Name '{name}' {options} | Out-Null}}else{{New-NetFirewallRule -Name '{name}' -DisplayName 'ChudLink private {rule.Item1}' {options} | Out-Null}}; ";
        }
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", command }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Windows did not start Wi-Fi setup.");
            await process.WaitForExitAsync();
            Notice(process.ExitCode == 0 ? "ChudLink is allowed on private local networks. Tap Find my PC again on your phone." : "Wi-Fi setup did not finish. Try again and approve the Windows administrator prompt, or use USB.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Notice("Wi-Fi setup was cancelled. You can retry whenever you're ready."); }
    }
    UIElement ConnectionGuide()
    {
        var page = new StackPanel();
        Border Steps(string title, params (string Title, string Body)[] steps)
        {
            var content = Stack(Label(title, 24, "#C6FF2E"));
            for (var i = 0; i < steps.Length; i++) { content.Children.Add(Label($"{i + 1:00}  {steps[i].Title}", 17, "#F0F5F1")); content.Children.Add(Label(steps[i].Body, 13)); }
            return Card(content);
        }
        page.Children.Add(Label("Pick USB or Wi-Fi. Pair once; reconnect automatically after that. Install the new phone APK when updating connection features.", 15));
        page.Children.Add(Steps("Connect with a USB cable",
            ("Turn on USB mode here", "Open Devices and click Connect by USB. Keep ChudLink running; closing its window leaves it in the tray."),
            ("Enable Android Developer options once", "On the phone, open Settings > About phone > Build number (sometimes under Software information). Tap Build number seven times and follow the phone's instructions."),
            ("Enable USB debugging", "Open Settings > Developer options and enable USB debugging. This is required for this alpha's USB mode. Wi-Fi pairing does not require developer options."),
            ("Plug in and allow your PC", "Use a USB data cable, unlock the phone, and tap Allow on the USB debugging prompt. Install the latest ChudLink APK first. If your phone offers Always allow, choose it only for your own trusted PC."),
            ("Approve once", "ChudLink opens on the phone. Compare its fingerprint with Devices here, confirm, then select the pending phone and click Approve selected phone."),
            ("Reconnect by plugging in", "Leave USB mode enabled. Future cable connections open the app and reconnect with the saved credential. No Wi-Fi address, discovery, or repeated code entry is needed.")));
        page.Children.Add(Steps("Connect over Wi-Fi",
            ("Open ChudLink on your PC", "The host starts on your active private network automatically. Open Devices to see your PC address and two-minute pairing code."),
            ("Use the same network", "Connect the phone to the same home Wi-Fi as the PC. A wired PC also works when its LAN can reach that Wi-Fi."),
            ("Tap your PC on the phone", "Open ChudLink. It searches automatically. Select your PC if more than one appears, then enter the six-digit code."),
            ("Confirm and approve", "Compare the fingerprint when asked, confirm it matches, then click Approve selected phone on the PC. Your dashboard appears automatically."),
            ("Open the app next time", "The phone remembers this PC and reconnects automatically. If the PC address changes, it searches for the same saved certificate.")));
        page.Children.Add(Steps("Can't connect?",
            ("Connection refused", "Check the PC host is active. Use the Phone connection address in Overview. 127.0.0.1 is reserved for the USB tunnel and cannot reach the PC over Wi-Fi."),
            ("No local hosts found", "In Devices, click Allow Wi-Fi connection and approve the Windows administrator prompt. This adds rules only for ChudLink on private local networks. Then tap Find my PC again on the phone. Guest Wi-Fi, VPNs, and router client isolation can block discovery; try USB or Manual connection."),
            ("USB phone not detected", "Unlock the phone, check USB debugging and its Allow prompt, try another data cable/USB port, then click Retry USB. Some phones also need their manufacturer's Android USB driver."),
            ("Code expired or connection declined", "Generate a new code for Wi-Fi. For USB, click Retry USB. Confirm the phone's request in Devices before the two-minute approval expires."),
            ("Changed PC identity or revoked phone", "On the phone, open Settings > Forget this host, then reconnect and approve again. Always compare a changed fingerprint with your own PC.")));
        page.Children.Add(Steps("After your phone connects",
            ("Choose a dashboard", "Open Devices, select the phone, choose a dashboard, and click Assign dashboard."),
            ("Make it your own", "Open Presets to clone a station, or open Designer. Save + send live updates the assigned phone."),
            ("Adjust the phone", "Use the menu on the phone for brightness, keep-awake, rendering profile, notes, or the connection guide.")));
        page.Children.Add(Row(Btn("Open Devices", () => Navigation.SelectedIndex = 1), AsyncBtn("Allow Wi-Fi connection", ConfigureWifiAsync)));
        return new ScrollViewer { Content = page };
    }
    UIElement PresetsPage()
    {
        var wrap = new WrapPanel();
        foreach (var d in host.Store.Dashboards.Values.OrderBy(d => d.Name))
        {
            var p = Stack(Label(d.Name, 21, "#EAF5EB"), Label(d.Description, 12), Label($"{d.Pages.Count} page(s)  /  {d.Pages.Sum(p => p.Widgets.Count)} widgets", 10, "#C6FF2E"),
                Row(Btn("Open in Designer", () => { editing = d.Clone(); Navigation.SelectedIndex = 3; }), Btn("Clone", () => { editing = d.Clone(); editing.Id = Guid.NewGuid().ToString("N"); editing.Name += " copy"; Navigation.SelectedIndex = 3; })));
            var c = Card(p); c.Width = 315; c.MinHeight = 220; wrap.Children.Add(c);
        }
        return new ScrollViewer { Content = wrap };
    }
    UIElement DesignerPage()
    {
        designer = new DesignerView(host, editing ?? host.Store.Dashboards["blank"].Clone(), Notice); return designer;
    }
    UIElement Studio()
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = new(170) });
        editor ??= new TextEditor { ShowLineNumbers = true, SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"), FontFamily = new("Cascadia Code, Consolas"), FontSize = 13, Background = Brush("#0E1510"), Foreground = Brush("#D7E6DB"), Text = DefaultSource, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        foreach (var c in editor.SyntaxHighlighting.NamedHighlightingColors)
        {
            var color = c.Name.Contains("Comment") ? "#809988" : c.Name.Contains("String") || c.Name.Contains("Char") ? "#D5E99B" : c.Name.Contains("Keyword") ? "#BAA7F5" : c.Name.Contains("Number") ? "#F3BD83" : "#82D3C3";
            c.Foreground = new SimpleHighlightingBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
        }
        editor.TextChanged -= StudioTextChanged; editor.TextChanged += StudioTextChanged;
        compileOutput = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas"), Text = "Build with Roslyn. Inspect diagnostics here.\nWorker stdout is available in Diagnostics. Running code has the same Windows account access as ChudLink." };
        if (editor.Parent is Panel oldParent) oldParent.Children.Remove(editor);
        trustCode = new CheckBox { Content = "I trust this C# code and its requested permissions" };
        var templates = new ComboBox { Width = 220, ItemsSource = new[] { "Hello World", "Custom counter", "Custom system monitor", "Custom clock", "Cross-device dashboard switcher", "JSON API status monitor", "Custom audio controls", "Sensor-controlled dashboard", "Developer build monitor" }, SelectedIndex = 0 };
        var bar = new WrapPanel();
        bar.Children.Add(templates); bar.Children.Add(Btn("Load template", () => { var file = Path.Combine(AppContext.BaseDirectory, "examples", TemplateFolder(templates.SelectedIndex), "Extension.cs"); editor.Text = File.Exists(file) ? File.ReadAllText(file) : DefaultSource; builtAssembly = null; }));
        bar.Children.Add(Btn("Build", BuildStudio)); bar.Children.Add(AsyncBtn("Run", RunStudioAsync)); bar.Children.Add(AsyncBtn("Stop", async () => { if (activeStudioId != null) await host.Workers.StopAsync(activeStudioId); })); bar.Children.Add(AsyncBtn("Reload", async () => { BuildStudio(); await RunStudioAsync(); }));
        bar.Children.Add(Btn("Import .cs", () => { var dialog = new OpenFileDialog { Filter = "C# source|*.cs" }; if (dialog.ShowDialog() == true) { editor.Text = File.ReadAllText(dialog.FileName); builtAssembly = null; } }));
        bar.Children.Add(Btn("Export project", ExportStudio));
        var top = Stack(Label("TRUSTED LOCAL DEVELOPMENT", 11, "#C6FF2E"), bar, trustCode); grid.Children.Add(top); Grid.SetRow(editor, 1); grid.Children.Add(editor); Grid.SetRow(compileOutput, 2); grid.Children.Add(compileOutput);
        return grid;
    }
    static string TemplateFolder(int index) => new[] { "HelloWorld", "CustomCounter", "SystemMonitor", "CustomClock", "CrossDevice", "JsonStatus", "AudioControls", "SensorDashboard", "BuildMonitor" }[Math.Clamp(index, 0, 8)];
    void StudioTextChanged(object? sender, EventArgs e) => builtAssembly = null;
    void BuildStudio()
    {
        if (editor == null) return;
        var result = ExtensionCompiler.Build(editor.Text, Path.Combine(host.Store.Root, "studio", Guid.NewGuid().ToString("N")));
        builtAssembly = result.AssemblyPath; compileOutput!.Text = (result.Success ? "Build succeeded" : "Build failed") + "\n" + string.Join("\n", result.Diagnostics);
        if (result.Success) Notice("Compiled successfully. Run starts a separate trusted-code worker.");
    }
    async Task RunStudioAsync()
    {
        if (builtAssembly == null) { BuildStudio(); if (builtAssembly == null) return; }
        if (trustCode?.IsChecked != true) { Notice("Read the source and select the trust checkbox before running executable C# code."); return; }
        var id = ExtractId(editor!.Text);
        if (activeStudioId != null && activeStudioId != id) await host.Workers.StopAsync(activeStudioId);
        var manifest = new ExtensionManifest(id, "Local development", "Local developer", "0.1.0", "0.1.0", ["dashboard", "actions", "notifications", "mesh", "providers", "sensors"]);
        await host.Workers.StartAsync(builtAssembly, manifest, true); activeStudioId = id; Notice("Worker started. Open Presets to edit its dashboard, or assign it in Devices.");
    }
    string ExtractId(string source)
    {
        var match = System.Text.RegularExpressions.Regex.Match(source, "\\[ChudExtension\\(\"([^\"]+)\""); return match.Success ? match.Groups[1].Value : studioId;
    }
    void ExportStudio()
    {
        if (editor == null) return;
        var dialog = new SaveFileDialog { Filter = "ChudLink extension package|*.chudext", FileName = "MyExtension.chudext" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            BuildStudio(); if (builtAssembly == null) return;
            var folder = Path.GetDirectoryName(builtAssembly)!;
            File.WriteAllText(Path.Combine(folder, "Extension.cs"), editor.Text);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), Wire.Serialize(new ExtensionManifest(ExtractId(editor.Text), "Local extension", "Local developer", "0.1.0", "0.1.0", ["dashboard", "actions", "notifications", "mesh", "providers", "sensors"])));
            File.WriteAllText(Path.Combine(folder, "settings.schema.json"), "{\"type\":\"object\",\"properties\":{}}");
            ExtensionPackage.Pack(folder, dialog.FileName); Notice("Exported compiled extension package with source and settings schema.");
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
    public const string DefaultSource = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing ChudLink.SDK;\nusing ChudLink.Protocol;\n\n[ChudExtension(\"local.studio\", \"Hello ChudLink\")]\npublic sealed class Hello : IChudExtension\n{\n    public Task StartAsync(IChudContext ctx, CancellationToken ct)\n    {\n        var board = new DashboardBuilder(\"Hello ChudLink\")\n            .Id(\"local.studio.home\")\n            .Button(\"hello\", \"SAY HELLO\", \"local.studio.hello\")\n            .Text(\"message\", \"Made with C#\").Build();\n        ctx.RegisterDashboard(board);\n        ctx.RegisterAction(\"local.studio.hello\", (device, arg) =>\n        {\n            ctx.Update(board.Id, new WidgetPatch(\"message\", \"Hello from your PC!\"));\n            return Task.CompletedTask;\n        });\n        return Task.CompletedTask;\n    }\n}\n";
    UIElement ExtensionsPage()
    {
        var page = new StackPanel();
        page.Children.Add(Label("Extensions are executable C# code. A worker process and resource limits do not form a security sandbox. Install packages for review; enable only authors and code you trust.", 14));
        var root = Path.Combine(host.Store.Root, "extensions"); Directory.CreateDirectory(root);
        page.Children.Add(Btn("Install .chudext for review", () =>
        {
            var dialog = new OpenFileDialog { Filter = "ChudLink extensions|*.chudext" }; if (dialog.ShowDialog() != true) return;
            try { var m = ExtensionPackage.Inspect(dialog.FileName); var inspected = System.Windows.MessageBox.Show($"{m.Name} {m.Version}\nAuthor: {m.Author}\nPermissions: {string.Join(", ", m.Permissions)}\n\nInstall for review? This will not run code.", "Install extension", MessageBoxButton.OKCancel); if (inspected != MessageBoxResult.OK) return; ExtensionPackage.Install(dialog.FileName, root); PageContent.Content = ExtensionsPage(); } catch (Exception ex) { Notice(ex.Message); }
        }));
        foreach (var folder in Directory.GetDirectories(root))
        {
            try
            {
                var m = Wire.Parse<ExtensionManifest>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
                var trust = new CheckBox { Content = "I reviewed and trust this extension" };
                var config = new TextBox { Text = Wire.Serialize(host.Store.Read<Dictionary<string, string>>("extension-" + m.Id + ".json") ?? []), Height = 65, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
                var p = Stack(Label($"{m.Name}  ·  {m.Version}", 20), Label($"{m.Author} / SDK {m.SdkVersion}\nRequested: {string.Join(", ", m.Permissions)}", 12), trust,
                    Row(AsyncBtn("Enable trusted code", async () => { await host.Workers.StartAsync(Path.Combine(folder, m.Assembly), m, trust.IsChecked == true); Notice("Extension enabled for this session."); }), AsyncBtn("Disable", () => host.Workers.StopAsync(m.Id)),
                    AsyncBtn("Uninstall", async () => { await host.Workers.StopAsync(m.Id); Directory.Delete(folder, true); PageContent.Content = ExtensionsPage(); })),
                    Label("Extension settings · JSON string values", 11), config, Btn("Save settings", () => { try { host.Store.Write("extension-" + m.Id + ".json", Wire.Parse<Dictionary<string, string>>(config.Text)); Notice("Saved. Reload extension to apply."); } catch (Exception ex) { Notice(ex.Message); } })); page.Children.Add(Card(p));
            }
            catch (Exception ex) { page.Children.Add(Label("Invalid package: " + Path.GetFileName(folder) + " · " + ex.Message)); }
        }
        return new ScrollViewer { Content = page };
    }
    UIElement SettingsPage()
    {
        var page = new StackPanel();
        var autoStart = new CheckBox { Content = "Start the local host automatically when ChudLink opens", IsChecked = host.Store.Settings.AutoStartHost };
        autoStart.Checked += (_, _) => { host.Store.Settings.AutoStartHost = true; host.Store.Save(); };
        autoStart.Unchecked += (_, _) => { host.Store.Settings.AutoStartHost = false; host.Store.Save(); };
        page.Children.Add(Card(Stack(Label("CONNECTION", 11, "#C6FF2E"), autoStart, Label("USB auto-connect also starts the host when enabled. Quit ChudLink to stop the host and USB connections.", 12))));
        page.Children.Add(Label("Configure PC actions locally. Phone events can reference only these IDs, and the phone also needs Approved PC actions permission.", 13));
        var name = new TextBox { Text = "My action", Width = 190 }; var kind = new ComboBox { ItemsSource = new[] { "launch", "shortcut", "sound", "build" }, SelectedIndex = 0, Width = 150 };
        var target = new TextBox { ToolTip = "Executable path, WAV path or shortcut such as CTRL+SHIFT+S" }; var arguments = new TextBox { ToolTip = "Fixed arguments (never supplied by the phone)" };
        var actions = new ListBox { Height = 100, ItemsSource = host.Store.Settings.Actions.ToArray(), DisplayMemberPath = "Name" };
        page.Children.Add(Card(Stack(Label("APPROVED ACTIONS", 11, "#C6FF2E"), actions, Row(name, kind), Label("Target / fixed arguments", 11), target, arguments,
            Row(Btn("Browse file", () => { var dialog = new OpenFileDialog { Filter = "Executable, sound or project|*.exe;*.wav;*.sln;*.csproj|All files|*.*" }; if (dialog.ShowDialog() == true) { target.Text = dialog.FileName; if (name.Text == "My action") name.Text = Path.GetFileNameWithoutExtension(dialog.FileName); } }), Btn("Approve action", () =>
            {
                if (target.Text.Length == 0) return;
                var id = Guid.NewGuid().ToString("N"); host.Store.Settings.Actions.Add(new(id, name.Text, kind.SelectedItem!.ToString()!, target.Text, arguments.Text)); host.Store.Save(); actions.ItemsSource = host.Store.Settings.Actions.ToArray(); Notice("Action approved. Bind ID " + id + " in Designer.");
            }), Btn("Remove action", () => { if (actions.SelectedItem is ApprovedAction a) { host.Store.Settings.Actions.Remove(a); host.Store.Save(); actions.ItemsSource = host.Store.Settings.Actions.ToArray(); } })))));
        var endpointName = new TextBox { Text = "Local service", Width = 200 }; var url = new TextBox { ToolTip = "Actual HTTP/HTTPS health URL" }; var property = new TextBox { ToolTip = "Optional top-level JSON property" };
        var endpoints = new ListBox { Height = 80, ItemsSource = SnapshotList(host.Store.Settings.Endpoints), DisplayMemberPath = "Name" };
        page.Children.Add(Card(Stack(Label("CONFIGURED SERVICES", 11, "#C6FF2E"), endpoints, endpointName, Label("Health URL / optional JSON property", 11), url, property,
            Row(Btn("Add health check", () =>
            {
                if (!Uri.TryCreate(url.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) { Notice("Enter an HTTP/HTTPS URL."); return; }
                lock (host.Store.Settings.Endpoints)
                {
                    if (host.Store.Settings.Endpoints.Count >= 16) { Notice("Limit: 16 configured checks."); return; }
                    host.Store.Settings.Endpoints.Add(new(Guid.NewGuid().ToString("N"), endpointName.Text, url.Text, property.Text));
                }
                host.Store.Save(); endpoints.ItemsSource = SnapshotList(host.Store.Settings.Endpoints);
            }), Btn("Remove check", () => { if (endpoints.SelectedItem is ServiceEndpoint endpoint) { lock (host.Store.Settings.Endpoints) host.Store.Settings.Endpoints.Remove(endpoint); host.Store.Save(); endpoints.ItemsSource = SnapshotList(host.Store.Settings.Endpoints); } })))));
        var process = new TextBox { Text = "devenv", Width = 200 }; var dashboards = new ComboBox { ItemsSource = host.Store.Dashboards.Values.ToArray(), DisplayMemberPath = "Name", Width = 220 }; dashboards.SelectedItem = host.Store.Dashboards["developer"];
        var rules = new ListBox { Height = 80, ItemsSource = SnapshotList(host.Store.Settings.ContextRules) };
        page.Children.Add(Card(Stack(Label("CONTEXT SWITCHING", 11, "#C6FF2E"), Label("Match a foreground process name without .exe. Manual assignment pauses automation for 15 minutes.", 12), rules, Row(process, dashboards), Row(Btn("Add rule", () => { if (dashboards.SelectedItem is Dashboard d) { lock (host.Store.Settings.ContextRules) host.Store.Settings.ContextRules.Add(new(process.Text.Trim(), d.Id)); host.Store.Save(); rules.ItemsSource = SnapshotList(host.Store.Settings.ContextRules); } }), Btn("Remove rule", () => { if (rules.SelectedItem is ContextRule r) { lock (host.Store.Settings.ContextRules) host.Store.Settings.ContextRules.Remove(r); host.Store.Save(); rules.ItemsSource = SnapshotList(host.Store.Settings.ContextRules); } })))));
        var personal = new CheckBox { Content = "Enable ChudLink Personal integration templates", IsChecked = host.Store.Settings.PersonalEdition };
        var integrationTemplate = new ComboBox { ItemsSource = ChudLink.Personal.Templates.Names, SelectedIndex = 0 };
        var templateDetails = Stack(integrationTemplate, Btn("Configure selected integration", () => { endpointName.Text = integrationTemplate.SelectedItem!.ToString(); url.Text = ""; property.Text = ""; Notice("Template selected. Enter its actual health URL in Configured services, then Add health check."); }), Label(ChudLink.Personal.Templates.Instructions, 12));
        templateDetails.Visibility = host.Store.Settings.PersonalEdition ? Visibility.Visible : Visibility.Collapsed;
        personal.Checked += (_, _) => { host.Store.Settings.PersonalEdition = true; templateDetails.Visibility = Visibility.Visible; host.Store.Save(); }; personal.Unchecked += (_, _) => { host.Store.Settings.PersonalEdition = false; templateDetails.Visibility = Visibility.Collapsed; host.Store.Save(); };
        page.Children.Add(Card(Stack(personal, templateDetails)));
        return new ScrollViewer { Content = page };
    }
    UIElement DiagnosticsPage()
    {
        using var process = Process.GetCurrentProcess(); lastCpu = process.TotalProcessorTime; lastDiagnostics = DateTimeOffset.UtcNow;
        lastUpdates = host.UpdatesSent; lastBytes = host.BytesSent;
        diagnostics = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return diagnostics;
    }
    UIElement AboutPage() => new ScrollViewer { Content = Stack(Card(Stack(Label("CHUDLINK", 42, "#C6FF2E"), Label("By ChudAI  /  0.1.1 Alpha", 19), Label("A programmable PC companion platform.\nC# core · native WPF · native .NET Android · local first", 15))),
        Label("This alpha includes secure local pairing, multiple devices, a portable dashboard format, visual editing, C# development and explicitly enabled extension workers.", 14),
        Label("Known limitations: no OBS adapter, GPU/VRAM/temperature provider, per-application audio routing, calendar integration or native phone plugins. Custom widgets use declarative fallbacks. See docs/STATUS.md for the complete feature and test matrix.", 14),
        Btn("Open data folder", () => Process.Start(new ProcessStartInfo(host.Store.Root) { UseShellExecute = true })),
        Label("No cloud account required. Personal settings and device credentials stay in your Windows profile.", 12)) };
    void Refresh()
    {
        if (busy) return; busy = true;
        try
        {
            var snapshot = host.Devices(); var connected = snapshot.Count(d => d.Connected);
            UpdateText(SidebarStatus, host.Running ? "●  LOCAL HOST ACTIVE" : "○  HOST STOPPED");
            UpdateText(FooterStatus, host.Running ? host.Address + "  /  " + connected + " connected" : "Host stopped · choose an interface in Overview");
            if (stats.TryGetValue("devices", out var devices)) UpdateText(devices, connected.ToString("00"));
            var anyConnected = connected > 0;
            if (stats.TryGetValue("cpu", out var cpu)) UpdateText(cpu, anyConnected ? host.Latest.GetValueOrDefault("host.cpu").Text ?? "—" : "Paused");
            if (stats.TryGetValue("ram", out var ram)) UpdateText(ram, anyConnected ? host.Latest.GetValueOrDefault("host.ram").Text ?? "—" : "Paused");
            if (stats.TryGetValue("traffic", out var traffic)) UpdateText(traffic, $"{host.BytesSent / 1024.0:F1} KB");
            if (deviceList != null)
            {
                var selected = (deviceList.SelectedItem as DeviceRecord)?.Id;
                var signature = string.Join('\n', snapshot.Select(d => d.Id + ":" + d));
                if (deviceList.Items.Count != snapshot.Length || deviceList.Items.Cast<DeviceRecord>().Select(d => d.Id).SequenceEqual(snapshot.Select(d => d.Id)) == false)
                { deviceList.ItemsSource = snapshot; deviceList.SelectedItem = snapshot.FirstOrDefault(d => d.Id == selected); }
                else if (signature != deviceSignature) deviceList.Items.Refresh();
                deviceSignature = signature;
            }
            if (pendingList != null)
            {
                var selected = pendingList.SelectedItem; var values = host.Pairings.Values.Where(p => p.Status == "pending" && p.Expires > DateTimeOffset.UtcNow).ToArray();
                if (!pendingList.Items.Cast<PendingPair>().SequenceEqual(values)) { pendingList.ItemsSource = values; pendingList.SelectedItem = selected ?? values.FirstOrDefault(); }
            }
            if (host.Running) UpdateText(pairingText, host.PairingCode);
            UpdateText(usbText, host.Usb.Status);
            UpdateText(networkText, host.Running ? (new Uri(host.Address).IsLoopback ? "USB / PC-only host · Wi-Fi is unavailable on this address" : "Phone connection address: " + host.Address) : "Host stopped · choose Start host");
            if (diagnostics != null)
            {
                using var p = Process.GetCurrentProcess();
                var now = DateTimeOffset.UtcNow; var seconds = (now - lastDiagnostics).TotalSeconds;
                var cpuUsage = seconds > 0 ? 100 * (p.TotalProcessorTime - lastCpu).TotalSeconds / seconds / Environment.ProcessorCount : 0;
                var updateRate = seconds > 0 ? (host.UpdatesSent - lastUpdates) / seconds : 0; var networkRate = seconds > 0 ? (host.BytesSent - lastBytes) / seconds : 0;
                var offset = diagnostics.VerticalOffset;
                var followLog = offset + diagnostics.ViewportHeight >= diagnostics.ExtentHeight - 2;
                diagnostics.Text = $"CHUDLINK / LIVE DIAGNOSTICS\nWorking set: {p.WorkingSet64 / 1048576.0:F1} MB\nProcess CPU: {cpuUsage:F2}% across {Environment.ProcessorCount} logical CPUs (sample interval {seconds:F1}s)\nProcess CPU time: {p.TotalProcessorTime}\nSent: {host.BytesSent} bytes / Received: {host.BytesReceived} bytes\nWidget patches: {host.UpdatesSent} / {updateRate:F1} per second\nOutbound rate: {networkRate / 1024:F2} KB/s\nProfiles: Legacy 0.2 Hz / Balanced 0.5 Hz / Performance 1 Hz\nWorkers: {string.Join(", ", host.Workers.Active)}\n\n" + string.Join("\n", host.Logs.Snapshot());
                if (followLog) diagnostics.ScrollToEnd(); else diagnostics.ScrollToVerticalOffset(offset);
                lastDiagnostics = now; lastCpu = p.TotalProcessorTime; lastUpdates = host.UpdatesSent; lastBytes = host.BytesSent;
            }
        }
        catch (Exception ex) { FooterStatus.Text = ex.Message; }
        finally { busy = false; }
    }
    void OnClosing(object? sender, CancelEventArgs e) { if (!quitting) { e.Cancel = true; Hide(); tray.ShowBalloonTip(2500, "ChudLink is still running", "Open ChudLink from the tray. Choose Quit to stop the host.", System.Windows.Forms.ToolTipIcon.Info); } }
    async void Quit_Click(object sender, RoutedEventArgs e) => await QuitAsync();
    async Task QuitAsync() { if (quitting) return; quitting = true; timer.Stop(); host.OverviewTelemetryRequested = false; await host.DisposeAsync(); tray.Visible = false; tray.Dispose(); System.Windows.Application.Current.Shutdown(); }
}
