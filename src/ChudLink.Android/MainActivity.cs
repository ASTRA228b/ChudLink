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

[Activity(Label = "ChudLink", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity, ISensorEventListener
{
    global::Android.Content.ISharedPreferences prefs = null!;
    LinearLayout root = null!;
    TextView status = null!;
    DashboardView? view;
    PhoneConnection? connection;
    CancellationTokenSource? session;
    readonly CancellationTokenSource lifetime = new();
    bool dashboardVisible, resumed;
    bool thermalLimited;
    PowerManager? power;
    ThermalListener? thermal;
    SensorManager? sensors;
    long lastSensor;
    public RenderProfile Profile => thermalLimited ? RenderProfile.Legacy : Enum.TryParse<RenderProfile>(prefs.GetString("profile", "Balanced"), out var p) ? p : RenderProfile.Balanced;
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        prefs = GetSharedPreferences("chudlink", FileCreationMode.Private)!;
        if (!prefs.Contains("id")) prefs.Edit()!.PutString("id", Guid.NewGuid().ToString("N"))!.Commit();
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
        if (prefs.Contains("token")) ShowDashboard(); else ShowConnection();
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
    protected override void OnDestroy() { lifetime.Cancel(); session?.Cancel(); connection?.Dispose(); view?.Dispose(); base.OnDestroy(); }
    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration configuration) { base.OnConfigurationChanged(configuration); view?.Invalidate(); }
    LinearLayout Shell(bool scroll = false)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical }; layout.SetBackgroundColor(Color.Rgb(10, 14, 11));
        layout.SetPadding(24, 24, 24, 16);
        if (scroll) { var scroller = new ScrollView(this) { FillViewport = true }; scroller.SetBackgroundColor(Color.Rgb(10, 14, 11)); scroller.AddView(layout); SetContentView(scroller); } else SetContentView(layout);
        return root = layout;
    }
    TextView Text(string text, float size = 16, string color = "#DCE8DE")
    {
        var t = new TextView(this) { Text = text, TextSize = size }; t.SetTextColor(Color.ParseColor(color)); t.SetPadding(0, 8, 0, 12); return t;
    }
    EditText Entry(string hint, string value = "")
    {
        var edit = new EditText(this) { Hint = hint, Text = value, TextSize = 15 }; edit.SetSingleLine(true);
        edit.SetTextColor(Color.White); edit.SetHintTextColor(Color.Rgb(110, 128, 116)); root.AddView(edit); return edit;
    }
    Button Button(string title, Action click)
    {
        var b = new Button(this) { Text = title, TextSize = 14 }; b.SetTextColor(Color.ParseColor("#C6FF2E"));
        b.SetBackgroundColor(Color.ParseColor("#1C2A1F")); var lp = new LinearLayout.LayoutParams(-1, -2) { TopMargin = 12 }; root.AddView(b, lp); b.Click += (_, _) => click(); return b;
    }
    void ShowConnection()
    {
        dashboardVisible = false; session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this);
        Shell(true);
        var brand = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var mark = new ImageView(this); mark.SetImageResource(Resource.Drawable.chudlink_mark); brand.AddView(mark, new LinearLayout.LayoutParams(64, 64)); brand.AddView(Text("CHUDLINK", 30, "#C6FF2E")); root.AddView(brand);
        var family = new LinearLayout(this) { Orientation = Orientation.Horizontal }; var familyMark = new ImageView(this); familyMark.SetImageResource(Resource.Drawable.chudgpt_mark); family.AddView(familyMark, new LinearLayout.LayoutParams(28, 28)); family.AddView(Text("  A ChudGPT app · By ChudAI · 0.1.0 Alpha", 12)); root.AddView(family);
        root.AddView(Text("Connect to your PC", 24));
        var address = Entry("PC IP address, e.g. 192.168.1.20", prefs.GetString("address", "")!);
        var fingerprint = Entry("PC SHA-256 fingerprint", prefs.GetString("fingerprint", "")!);
        fingerprint.SetSingleLine(false); fingerprint.SetMaxLines(3);
        var code = Entry("6-digit pairing code"); code.InputType = global::Android.Text.InputTypes.ClassNumber;
        root.AddView(Text("Compare the entire fingerprint with the PC before pairing. Discovery is a suggestion; verify its identity.", 13, "#97A79B"));
        status = Text("Same private Wi-Fi or LAN as your PC", 13); root.AddView(status);
        Button("DISCOVER LOCAL HOSTS", async () =>
        {
            try
            {
                status.Text = "Searching…"; var found = await PhoneConnection.DiscoverAsync(lifetime.Token);
                if (found.Length == 0) { status.Text = "No hosts found. Enter the PC address manually."; return; }
                new AlertDialog.Builder(this)!.SetTitle("Select host · verify fingerprint")!.SetItems(found.Select(f => f.Name + " · " + f.Address).ToArray(), (_, e) => { address.Text = found[e.Which].Address; fingerprint.Text = found[e.Which].Fingerprint; })!.Show();
                status.Text = "Verify fingerprint against the desktop";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        });
        var pair = Button("PAIR WITH PC", () => { });
        pair.Click += async (_, _) =>
        {
            pair.Enabled = false;
            try
            {
                using var candidate = new PhoneConnection(prefs.GetString("id", "")!, address.Text ?? "", fingerprint.Text ?? "", "");
                candidate.Status += s => RunOnUiThread(() => status.Text = s);
                var token = await candidate.PairAsync(code.Text ?? "", Build.Model ?? "Android", "Android " + Build.VERSION.Release, lifetime.Token);
                prefs.Edit()!.PutString("address", candidate.Address)!.PutString("fingerprint", candidate.Fingerprint)!.PutString("token", token)!.Commit();
                ShowDashboard(); StartConnection();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { pair.Enabled = true; }
        };
        Button("PHONE SETTINGS", ShowSettings);
    }
    void ShowDashboard()
    {
        dashboardVisible = true; Shell(); root.SetPadding(0, 0, 0, 0);
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal }; bar.SetPadding(16, 4, 12, 4);
        status = Text("CHUDLINK · CONNECTING", 11, "#C6FF2E"); bar.AddView(status, new LinearLayout.LayoutParams(0, -2, 1));
        var settings = new Button(this) { Text = "☰", TextSize = 18 }; settings.SetTextColor(Color.ParseColor("#C6FF2E")); settings.SetBackgroundColor(Color.Transparent); settings.Click += (_, _) => ShowSettings(); bar.AddView(settings, new LinearLayout.LayoutParams(56, 48)); root.AddView(bar);
        view = new DashboardView(this, Profile);
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
            connection = new(prefs.GetString("id", "")!, prefs.GetString("address", "")!, prefs.GetString("fingerprint", "")!, prefs.GetString("token", "")!);
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
        return new(Build.Model ?? "Android", "Android " + Build.VERSION.Release, "0.1.0-alpha", level < 0 ? -1 : level * 100 / Math.Max(1, scale), Resources!.Configuration!.Orientation.ToString(), ["canvas", "touch", "accelerometer", "brightness"], Profile);
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
        dashboardVisible = false; session?.Cancel(); connection?.Dispose(); sensors?.UnregisterListener(this); Shell(true);
        root.AddView(Text("Phone settings", 28, "#C6FF2E")); root.AddView(Text($"{Build.Manufacturer} {Build.Model}\nAndroid {Build.VERSION.Release} · API {(int)Build.VERSION.SdkInt}\n{(connection?.Received ?? 0) / 1024.0:F1} KB received · {(connection?.Sent ?? 0) / 1024.0:F1} KB sent", 14));
        var awake = new Switch(this) { Text = "Keep screen awake", Checked = prefs.GetBoolean("awake", true) }; root.AddView(awake); awake.CheckedChange += (_, e) => prefs.Edit()!.PutBoolean("awake", e.IsChecked)!.Apply();
        var sensor = new Switch(this) { Text = "Share accelerometer (host permission required)", Checked = prefs.GetBoolean("sensor", false) }; root.AddView(sensor); sensor.CheckedChange += (_, e) => prefs.Edit()!.PutBoolean("sensor", e.IsChecked)!.Apply();
        root.AddView(Text("Rendering profile", 16));
        foreach (var p in Enum.GetValues<RenderProfile>()) Button(p + (Profile == p ? " · selected" : ""), () => { prefs.Edit()!.PutString("profile", p.ToString())!.Apply(); ShowSettings(); });
        root.AddView(Text("Screen brightness", 16)); var brightness = new SeekBar(this) { Max = 100, Progress = prefs.GetInt("brightness", 70) }; root.AddView(brightness);
        brightness.ProgressChanged += (_, e) => { prefs.Edit()!.PutInt("brightness", Math.Max(5, e.Progress))!.Apply(); ApplyDisplay(); };
        Button(prefs.Contains("token") ? "BACK TO DASHBOARD" : "BACK TO CONNECTION", () => { if (prefs.Contains("token")) { ShowDashboard(); StartConnection(); ConfigureSensors(); } else ShowConnection(); });
        if (prefs.Contains("token")) Button("FORGET THIS HOST", () => { prefs.Edit()!.Remove("token")!.Commit(); ShowConnection(); });
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

