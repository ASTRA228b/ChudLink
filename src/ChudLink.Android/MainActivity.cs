using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.Graphics;
using global::Android.Hardware;
using global::Android.OS;
using global::Android.Views;
using global::Android.Widget;
using ChudLink.Protocol;
using System.Text.Json;
using Color = global::Android.Graphics.Color;
using Path = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

namespace ChudLink.Phone;

[Activity(Name = "ai.chud.chudlink.MainActivity", Label = "ChudLink", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity, ISensorEventListener
{
    global::Android.Content.ISharedPreferences prefs = null!;
    LinearLayout root = null!;
    TextView status = null!;
    DashboardView? view;
    PhoneConnection? connection;
    CancellationTokenSource? session;
    CancellationTokenSource? setup;
    bool pairing;
    readonly CancellationTokenSource lifetime = new();
    bool dashboardVisible, resumed;
    bool thermalLimited;
    PowerManager? power;
    ThermalListener? thermal;
    SensorManager? sensors;
    long lastSensor;
    int Dp(float value) => (int)Math.Ceiling(value * (Resources?.DisplayMetrics?.Density ?? 1));
    public RenderProfile Profile => thermalLimited ? RenderProfile.Legacy : Enum.TryParse<RenderProfile>(prefs.GetString("profile", "Balanced"), out var p) ? p : RenderProfile.Balanced;
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        prefs = GetSharedPreferences("chudlink", FileCreationMode.Private)!;
        if (!prefs.Contains("id")) prefs.Edit()!.PutString("id", Guid.NewGuid().ToString("N"))!.Commit();
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
        if (!HandleUsbIntent(Intent)) { if (prefs.Contains("token")) ShowDashboard(); else ShowConnection(); }
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent); Intent = intent;
        HandleUsbIntent(intent);
    }
    bool HandleUsbIntent(Intent? intent)
    {
        var fingerprint = intent?.GetStringExtra("chudlink.usbFingerprint");
        var code = intent?.GetStringExtra("chudlink.usbCode");
        var lan = intent?.GetStringExtra("chudlink.lanAddress");
        intent?.RemoveExtra("chudlink.usbFingerprint"); intent?.RemoveExtra("chudlink.usbCode"); intent?.RemoveExtra("chudlink.lanAddress");
        if (fingerprint == null || code == null || code.Length != 64 || !code.All(Uri.IsHexDigit)) return false;
        try { using var check = new PhoneConnection("usb", Wire.UsbAddress, fingerprint, ""); fingerprint = check.Fingerprint; }
        catch (Exception) { return false; }
        string? fallback = null;
        try { if (!string.IsNullOrWhiteSpace(lan) && !new Uri(PhoneConnection.NormalizeAddress(lan)).IsLoopback) fallback = PhoneConnection.NormalizeAddress(lan); } catch (Exception) { }
        if (prefs.Contains("token") && fingerprint == prefs.GetString("fingerprint", ""))
        {
            prefs.Edit()!.PutString("address", Wire.UsbAddress)!.Apply();
            if (fallback != null) prefs.Edit()!.PutString("lanAddress", fallback)!.Apply();
            if (!dashboardVisible) ShowDashboard(); StartConnection(); return true;
        }
        ShowConnection(); var ct = setup!.Token;
        new AlertDialog.Builder(this)!.SetTitle("Connect to this PC over USB?")!
            .SetMessage("Confirm this fingerprint matches ChudLink on your PC:\n\n" + fingerprint + "\n\n" + (prefs.Contains("token") ? "This replaces your saved PC after you approve the connection." : "Approve this phone on the PC once. Future USB connections are automatic."))!
            .SetPositiveButton("Connect", async (_, _) =>
            {
                await PairAndConnectAsync(Wire.UsbAddress, fingerprint, code, ct, fallback);
            })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
        return true;
    }
    protected override void OnResume()
    {
        base.OnResume(); resumed = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            power ??= (PowerManager?)GetSystemService(PowerService);
            thermal ??= new ThermalListener(value => RunOnUiThread(() => { thermalLimited = value >= ThermalStatus.Severe; if (dashboardVisible) { ApplyDisplay(); if (thermalLimited) status.Text = "THERMAL WARNING · reduced updates and brightness"; } }));
            power?.AddThermalStatusListener(thermal);
        }
        if (dashboardVisible) { StartConnection(); ConfigureSensors(); }
    }
    protected override void OnPause()
    {
        resumed = false; session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this);
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && thermal != null) power?.RemoveThermalStatusListener(thermal);
        base.OnPause();
    }
    protected override void OnDestroy() { lifetime.Cancel(); setup?.Cancel(); session?.Cancel(); connection?.Dispose(); base.OnDestroy(); }
    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration configuration) { base.OnConfigurationChanged(configuration); view?.Invalidate(); }
    LinearLayout Shell(bool scroll = false)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; layout.SetBackgroundColor(Color.Rgb(10, 14, 11));
        layout.SetPadding(Dp(20), Dp(20), Dp(20), Dp(16));
        var safe = new SafeAreaFrame(this);
        if (scroll) { var scroller = new ScrollView(this) { FillViewport = true }; scroller.SetBackgroundColor(Color.Rgb(10, 14, 11)); scroller.AddView(layout); safe.AddView(scroller); } else safe.AddView(layout);
        SetContentView(safe); safe.RequestApplyInsets();
        // Android can still deliver queued native callbacks after SetContentView. Let its peer
        // lifetime machinery release the detached view rather than disposing a live JNI peer.
        view = null;
        return root = layout;
    }
    TextView Text(string text, float size = 16, string color = "#DCE8DE")
    {
        var t = new TextView(this) { Text = text, TextSize = size }; t.SetTextColor(Color.ParseColor(color)); t.SetPadding(0, Dp(8), 0, Dp(12)); return t;
    }
    EditText Entry(string hint, string value = "", LinearLayout? parent = null)
    {
        var edit = new EditText(this) { Hint = hint, Text = value, TextSize = 15 }; edit.SetSingleLine(true);
        edit.SetTextColor(Color.White); edit.SetHintTextColor(Color.Rgb(110, 128, 116)); (parent ?? root).AddView(edit); return edit;
    }
    Button Button(string title, Action click)
    {
        var b = new Button(this) { Text = title, TextSize = 14 }; b.SetTextColor(Color.ParseColor("#C6FF2E"));
        b.SetBackgroundColor(Color.ParseColor("#1C2A1F")); var lp = new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) }; root.AddView(b, lp); b.Click += (_, _) => click(); return b;
    }
    void ShowConnection()
    {
        setup?.Cancel(); setup?.Dispose(); setup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var ct = setup.Token;
        dashboardVisible = false; session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this);
        Shell(true);
        var brand = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var mark = new ImageView(this); mark.SetImageResource(Resource.Drawable.chudlink_mark); brand.AddView(mark, new LinearLayout.LayoutParams(Dp(48), Dp(48))); brand.AddView(Text("CHUDLINK", 30, "#C6FF2E")); root.AddView(brand);
        var family = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var familyMark = new ImageView(this); familyMark.SetImageResource(Resource.Drawable.chudgpt_mark); family.AddView(familyMark, new LinearLayout.LayoutParams(28, 28)); family.AddView(Text("  A ChudGPT app · By ChudAI · 0.1.1 Alpha", 12)); root.AddView(family);
        root.AddView(Text("Let's connect", 26));
        root.AddView(Text("USB: click Connect by USB in the PC app, plug in a data cable, and allow this PC on your phone.\n\nWi-Fi: open ChudLink on your PC and choose Start host. We'll find it here.", 14, "#97A79B"));
        var hosts = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(hosts);
        var code = Entry("Enter the 6-digit code from your PC"); code.InputType = global::Android.Text.InputTypes.ClassNumber; code.SetFilters([new global::Android.Text.InputFilterLengthFilter(6)]);
        status = Text("Looking for your PC…", 13); root.AddView(status);
        var advanced = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        var address = Entry("PC IP address, e.g. 192.168.1.20", prefs.GetString("lanAddress", prefs.GetString("address", ""))!, advanced);
        var fingerprint = Entry("PC SHA-256 fingerprint", prefs.GetString("fingerprint", "")!, advanced);
        fingerprint.SetSingleLine(false); fingerprint.SetMaxLines(3);
        var pair = Button("CONNECT TO PC", () =>
        {
            if (pairing) return;
            if (code.Text?.Length != 6) { status.Text = "Enter the 6-digit code shown on the PC's Devices page."; return; }
            try { using var check = new PhoneConnection("check", address.Text ?? "", fingerprint.Text ?? "", ""); }
            catch (Exception) { advanced.Visibility = ViewStates.Visible; status.Text = "Choose a PC below, or enter its details in Manual connection."; return; }
            new AlertDialog.Builder(this)!.SetTitle("Confirm your PC")!.SetMessage("Compare this fingerprint with Devices in the PC app:\n\n" + fingerprint.Text)!
                .SetPositiveButton("It matches · Connect", async (_, _) => await PairAndConnectAsync(address.Text ?? "", fingerprint.Text ?? "", code.Text ?? "", ct))!
                .SetNegativeButton("Cancel", (_, _) => { })!.Show();
        });
        async Task FindHostsAsync()
        {
            if (ct.IsCancellationRequested || pairing) return;
            try
            {
                status.Text = "Looking for your PC…"; var found = await PhoneConnection.DiscoverAsync(ct);
                if (ct.IsCancellationRequested || pairing) return;
                hosts.RemoveAllViews();
                if (found.Length == 0) { status.Text = "No PC found yet. Start the PC host on the same Wi-Fi, try USB, or open Manual connection."; return; }
                void Select(DiscoveryReply host) { address.Text = host.Address; fingerprint.Text = host.Fingerprint; status.Text = host.Name + " selected · enter the PC's pairing code above."; }
                foreach (var host in found)
                {
                    var choice = new Button(this) { Text = "PC · " + host.Name + "\n" + host.Address }; choice.SetTextColor(Color.ParseColor("#C6FF2E"));
                    choice.Click += (_, _) => Select(host); hosts.AddView(choice);
                }
                if (found.Length == 1) Select(found[0]); else status.Text = "Choose your PC, then enter its pairing code.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception) { if (!ct.IsCancellationRequested) status.Text = "Couldn't search this network. Try USB or Manual connection."; }
        }
        Button("FIND MY PC AGAIN", async () => await FindHostsAsync());
        Button("MANUAL CONNECTION", () => advanced.Visibility = advanced.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible);
        root.AddView(advanced);
        Button("CONNECTION GUIDE · WI-FI & USB", ShowConnectionHelp);
        Button("PHONE SETTINGS", ShowSettings);
        _ = FindHostsAsync();
    }
    async Task PairAndConnectAsync(string address, string fingerprint, string code, CancellationToken ct, string? fallback = null)
    {
        if (pairing || ct.IsCancellationRequested) return;
        pairing = true;
        try
        {
            using var candidate = new PhoneConnection(prefs.GetString("id", "")!, address, fingerprint, "");
            candidate.Status += s => RunOnUiThread(() => { if (!ct.IsCancellationRequested) status.Text = s; });
            status.Text = "Connecting… Then approve this phone on your PC.";
            var token = await candidate.PairAsync(code, Build.Model ?? "Android", "Android " + Build.VERSION.Release, ct);
            ct.ThrowIfCancellationRequested();
            var saved = prefs.Edit()!.PutString("address", candidate.Address)!.PutString("fingerprint", candidate.Fingerprint)!.PutString("token", token)!;
            if (!new Uri(candidate.Address).IsLoopback) saved.PutString("lanAddress", candidate.Address);
            else if (fallback != null) saved.PutString("lanAddress", fallback);
            else saved.Remove("lanAddress");
            saved.Commit(); ShowDashboard(); StartConnection();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) status.Text = ex.Message + " For USB, click Retry USB on the PC."; }
        finally { pairing = false; }
    }
    void ShowDashboard()
    {
        setup?.Cancel();
        dashboardVisible = true; Shell(); root.SetPadding(0, 0, 0, 0);
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal }; bar.SetGravity(GravityFlags.CenterVertical); bar.SetPadding(Dp(16), Dp(4), Dp(8), Dp(4));
        status = Text("CHUDLINK · CONNECTING", 14, "#C6FF2E"); status.Gravity = GravityFlags.CenterVertical; status.SetPadding(0, Dp(6), Dp(8), Dp(6)); status.SetMinHeight(Dp(48)); status.SetMaxLines(3); status.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        bar.AddView(status, new LinearLayout.LayoutParams(0, -2, 1));
        var settings = new Button(this) { Text = "☰", TextSize = 22, ContentDescription = "Phone settings" }; settings.SetPadding(0, 0, 0, 0); settings.SetMinWidth(0); settings.SetMinimumWidth(0); settings.SetTextColor(Color.ParseColor("#C6FF2E")); settings.SetBackgroundColor(Color.Transparent); settings.Click += (_, _) => ShowSettings(); bar.AddView(settings, new LinearLayout.LayoutParams(Dp(48), Dp(48))); root.AddView(bar);
        view = new DashboardView(this, Profile, prefs.GetBoolean("autofit", true));
        view.TouchAction += touch => _ = SafeSendAsync("touch", touch);
        view.EditRequested += w =>
        {
            var input = new EditText(this) { Text = prefs.GetString("note." + w.Id, w.Text) }; input.SetMinLines(3);
            new AlertDialog.Builder(this)!.SetTitle(w.Label)!.SetView(input)!.SetPositiveButton("Save", (_, _) => { w.Text = input.Text ?? ""; prefs.Edit()!.PutString("note." + w.Id, w.Text)!.Apply(); view.Invalidate(); })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
        };
        root.AddView(view, new LinearLayout.LayoutParams(-1, 0, 1));
        var cache = Path.Combine(FilesDir!.AbsolutePath, "dashboard.chudlink");
        if (File.Exists(cache)) try { LoadDashboard(Wire.Parse<Dashboard>(File.ReadAllText(cache))); } catch (Exception) { }
        ApplyDisplay();
    }
    void LoadDashboard(Dashboard dashboard)
    {
        dashboard.Validate();
        foreach (var w in dashboard.Pages.SelectMany(p => p.Widgets).Where(w => w.Id is "notes" or "tasks")) w.Text = prefs.GetString("note." + w.Id, w.Text) ?? "";
        view?.Load(dashboard);
    }
    void StartConnection()
    {
        if (!dashboardVisible || !resumed || !prefs.Contains("token")) return;
        session?.Cancel(); connection?.Dispose(); session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            connection = new(prefs.GetString("id", "")!, prefs.GetString("address", "")!, prefs.GetString("fingerprint", "")!, prefs.GetString("token", "")!, [prefs.GetString("lanAddress", "")!]);
            connection.Status += s => RunOnUiThread(() => { if (dashboardVisible) status.Text = s; });
            connection.Message += e => RunOnUiThread(() =>
            {
                try
                {
                    switch (e.Type)
                    {
                        case "dashboard":
                            var dashboard = e.Read<Dashboard>(); LoadDashboard(dashboard); File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "dashboard.chudlink"), Wire.Serialize(dashboard)); break;
                        case "patch": view?.Patch(e.Read<WidgetPatch[]>()); break;
                        case "notification": Toast.MakeText(this, e.Read<string>(), ToastLength.Long)!.Show(); break;
                    }
                }
                catch (Exception ex) { status.Text = "Dashboard error: " + ex.Message; }
            });
            var ct = session.Token; var active = connection; _ = Task.Run(async () => { try { await active.RunAsync(Hello, ct); } catch (OperationCanceledException) { } catch (Exception ex) { RunOnUiThread(() => status.Text = ex.Message); } }, ct);
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    DeviceHello Hello()
    {
        using var battery = RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        var level = battery?.GetIntExtra(BatteryManager.ExtraLevel, -1) ?? -1;
        var scale = battery?.GetIntExtra(BatteryManager.ExtraScale, 100) ?? 100;
        return new(Build.Model ?? "Android", "Android " + Build.VERSION.Release, Wire.AppVersion, level < 0 ? -1 : level * 100 / Math.Max(1, scale), Resources!.Configuration!.Orientation.ToString(), ["canvas", "touch", "accelerometer", "brightness"], Profile);
    }
    async Task SafeSendAsync<T>(string type, T payload) { try { if (connection != null) await connection.SendAsync(type, payload, session?.Token ?? default); } catch (Exception) { } }
    void ApplyDisplay()
    {
        if (prefs.GetBoolean("awake", true) && !thermalLimited) Window!.AddFlags(WindowManagerFlags.KeepScreenOn); else Window!.ClearFlags(WindowManagerFlags.KeepScreenOn);
        var attributes = Window!.Attributes!; attributes.ScreenBrightness = thermalLimited ? .15f : prefs.GetInt("brightness", 70) / 100f; Window.Attributes = attributes;
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) Window.InsetsController?.Hide(WindowInsets.Type.SystemBars());
        else
        {
#pragma warning disable CA1422 // Legacy fullscreen fallback is used only below API 30.
            Window.DecorView!.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation;
#pragma warning restore CA1422
        }
    }
    void ShowSettings()
    {
        setup?.Cancel();
        dashboardVisible = false; session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this); Shell(true);
        root.AddView(Text("Phone settings", 28, "#C6FF2E")); root.AddView(Text($"{Build.Manufacturer} {Build.Model}\nAndroid {Build.VERSION.Release} · API {(int)Build.VERSION.SdkInt}\n{(connection?.Received ?? 0) / 1024.0:F1} KB received · {(connection?.Sent ?? 0) / 1024.0:F1} KB sent", 14));
        Button("CONNECTION GUIDE · WI-FI & USB", ShowConnectionHelp);
        var fit = new Switch(this) { Text = "Fit dashboard controls to this screen", Checked = prefs.GetBoolean("autofit", true) }; root.AddView(fit); fit.CheckedChange += (_, e) => prefs.Edit()!.PutBoolean("autofit", e.IsChecked)!.Apply();
        root.AddView(Text("Fits readable cards to your screen. Swipe up or down for more controls. Turn off to use the original Designer canvas.", 14));
        var awake = new Switch(this) { Text = "Keep screen awake", Checked = prefs.GetBoolean("awake", true) }; root.AddView(awake); awake.CheckedChange += (_, e) => prefs.Edit()!.PutBoolean("awake", e.IsChecked)!.Apply();
        var sensor = new Switch(this) { Text = "Share accelerometer (host permission required)", Checked = prefs.GetBoolean("sensor", false) }; root.AddView(sensor); sensor.CheckedChange += (_, e) => prefs.Edit()!.PutBoolean("sensor", e.IsChecked)!.Apply();
        root.AddView(Text("Rendering profile", 16));
        foreach (var p in Enum.GetValues<RenderProfile>()) Button(p + (Profile == p ? " · selected" : ""), () => { prefs.Edit()!.PutString("profile", p.ToString())!.Apply(); ShowSettings(); });
        root.AddView(Text("Screen brightness", 16)); var brightness = new SeekBar(this) { Max = 100, Progress = prefs.GetInt("brightness", 70) }; root.AddView(brightness);
        brightness.ProgressChanged += (_, e) => { prefs.Edit()!.PutInt("brightness", Math.Max(5, e.Progress))!.Apply(); ApplyDisplay(); };
        Button(prefs.Contains("token") ? "BACK TO DASHBOARD" : "BACK TO CONNECTION", () => { if (prefs.Contains("token")) { ShowDashboard(); StartConnection(); ConfigureSensors(); } else ShowConnection(); });
        if (prefs.Contains("token")) Button("FORGET THIS HOST", () => { prefs.Edit()!.Remove("token")!.Commit(); ShowConnection(); });
    }
    void ShowConnectionHelp()
    {
        dashboardVisible = false; setup?.Cancel(); session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this); Shell(true);
        root.AddView(Text("Connection guide", 28, "#C6FF2E"));
        void Steps(string heading, params string[] steps)
        {
            root.AddView(Text(heading, 23, "#C6FF2E"));
            for (var i = 0; i < steps.Length; i++) root.AddView(Text($"{i + 1}. {steps[i]}", 15));
        }
        Steps("Connect by USB",
            "Install the latest ChudLink APK on this phone. On the PC, open ChudLink > Devices > Connect by USB.",
            "On this phone, open Settings > About phone > Build number (sometimes under Software information). Tap Build number seven times to enable Developer options.",
            "Open Settings > Developer options and turn on USB debugging. This alpha's USB mode needs it; Wi-Fi pairing works without it.",
            "Plug in a USB data cable and unlock the phone. Tap Allow when Android asks to allow USB debugging from your PC.",
            "ChudLink opens automatically. Compare the fingerprint with Devices on your PC and tap Connect. Approve selected phone on the PC once.",
            "Next time, leave the PC app running and plug in the cable. The app opens and reconnects automatically. No Wi-Fi or address entry needed.");
        Steps("Connect over Wi-Fi",
            "Open ChudLink on your PC. It starts the host automatically. Open Devices to see the six-digit code.",
            "Use the same home Wi-Fi or reachable local network on both devices.",
            "Return to Let's connect. Your PC is found automatically. Tap it if several PCs appear and enter the six-digit code.",
            "Compare the fingerprint with your PC, confirm it matches, then approve the phone in the PC's Devices page.",
            "Your dashboard appears. Next time, open ChudLink and it reconnects automatically.");
        Steps("If it won't connect",
            "No PC found: on the PC, click Devices > Allow Wi-Fi connection, approve the Windows administrator prompt, and tap Find my PC again here. Guest networks, VPNs, or router isolation can block local connections.",
            "Connection refused: make sure the PC host is active and use its Phone connection address. Do not enter 127.0.0.1 for a Wi-Fi connection; that address is used automatically for USB only.",
            "USB not detected: unlock the phone, check USB debugging and its Allow prompt, try another data cable or port, and click Retry USB on the PC. Some phones need their maker's Android USB driver.",
            "Expired code: generate a new Wi-Fi code on the PC. For USB, click Retry USB. Approve the phone before the two-minute request expires.",
            "Changed or revoked PC: open Phone settings > Forget this host and pair again. Compare the new fingerprint with your own PC.");
        Button(prefs.Contains("token") ? "BACK TO DASHBOARD" : "BACK TO CONNECTION", () => { if (prefs.Contains("token")) { ShowDashboard(); StartConnection(); } else ShowConnection(); });
        Button("PHONE SETTINGS", ShowSettings);
    }
    void ConfigureSensors()
    {
        sensors ??= (SensorManager?)GetSystemService(SensorService); sensors?.UnregisterListener(this);
        if (prefs.GetBoolean("sensor", false) && dashboardVisible) { var sensor = sensors?.GetDefaultSensor(SensorType.Accelerometer); if (sensor != null) sensors!.RegisterListener(this, sensor, SensorDelay.Normal); }
    }
    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy) { }
    public void OnSensorChanged(global::Android.Hardware.SensorEvent? e)
    {
        var now = SystemClock.ElapsedRealtime(); if (now - lastSensor < 500 || e?.Values == null || e.Values.Count < 3) return;
        lastSensor = now; _ = SafeSendAsync("sensor", new ChudLink.Protocol.SensorEvent("accelerometer", e.Values[0], e.Values[1], e.Values[2]));
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
sealed class ThermalListener(Action<ThermalStatus> changed) : Java.Lang.Object, PowerManager.IOnThermalStatusChangedListener
{
    public void OnThermalStatusChanged(ThermalStatus status) => changed(status);
}

