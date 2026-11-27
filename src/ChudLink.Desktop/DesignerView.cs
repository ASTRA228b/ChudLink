using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using ChudLink.Core;
using Microsoft.Win32;

namespace ChudLink.Desktop;

public sealed class DesignerView : UserControl
{
    readonly HostRuntime host;
    readonly Action<string> notice;
    readonly DashboardHistory history = new();
    readonly HashSet<string> selected = [];
    readonly Canvas canvas = new() { ClipToBounds = true, Focusable = true, AllowDrop = true };
    readonly StackPanel inspector = new();
    readonly ListBox layers = new() { Height = 130 };
    readonly ComboBox pages = new() { Width = 140, DisplayMemberPath = "Name" };
    readonly CheckBox snap = new() { Content = "Snap 12 px", IsChecked = true };
    Dashboard document;
    int pageIndex;
    string? clipboard;
    Point start;
    Dictionary<string, Point> initial = [];
    bool moving, loading;
    public Dashboard Document => document;
    DashboardPage Page => document.Pages[Math.Clamp(pageIndex, 0, document.Pages.Count - 1)];
    public DesignerView(HostRuntime host, Dashboard document, Action<string> notice)
    {
        this.host = host; this.document = document; this.notice = notice;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        var tools = new WrapPanel();
        void Tool(string title, Action action) => tools.Children.Add(MainWindow.Btn(title, () => Safe(action)));
        Tool("Save + send live", () => _ = PublishAsync()); Tool("Import", Import); Tool("Export", Export);
        Tool("Undo", () => { this.document = history.Undo(this.document); Draw(); }); Tool("Redo", () => { this.document = history.Redo(this.document); Draw(); });
        Tool("Copy", Copy); Tool("Paste", Paste); Tool("Duplicate", () => { Copy(); Paste(); }); Tool("Delete", Delete);
        Tool("Align left", () => Align(false)); Tool("Align top", () => Align(true)); Tool("Center", Center); Tool("Container", Group);
        Tool("Layer +", () => Layer(1)); Tool("Layer −", () => Layer(-1)); tools.Children.Add(snap);
        tools.Children.Add(pages); Tool("+ Page", () => { history.Save(this.document); this.document.Pages.Add(new() { Name = "Page " + (this.document.Pages.Count + 1) }); pageIndex = this.document.Pages.Count - 1; Draw(); });
        var sizes = new ComboBox { Width = 150, ItemsSource = new[] { "720 × 1280", "1080 × 1920", "1280 × 720", "1920 × 1080" }, SelectedIndex = 0 };
        sizes.SelectionChanged += (_, _) => { if (loading) return; var dimensions = sizes.SelectedItem!.ToString()!.Split('×').Select(s => double.Parse(s.Trim())).ToArray(); history.Save(this.document); var sx = dimensions[0] / this.document.Width; var sy = dimensions[1] / this.document.Height; foreach (var w in this.document.Pages.SelectMany(p => p.Widgets)) { w.X *= sx; w.Y *= sy; w.Width *= sx; w.Height *= sy; } this.document.Width = dimensions[0]; this.document.Height = dimensions[1]; Draw(); };
        tools.Children.Add(sizes); root.Children.Add(tools);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(130) }); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new(245) }); Grid.SetRow(body, 1); root.Children.Add(body);
        var palette = new StackPanel(); palette.Children.Add(MainWindow.Label("WIDGETS", 10, "#C6FF2E"));
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var b = MainWindow.Btn(kind.ToString(), () => Add(kind, 24, 24)); b.Padding = new(9, 6, 9, 6); b.HorizontalContentAlignment = HorizontalAlignment.Left;
            Point paletteStart = default;
            b.PreviewMouseLeftButtonDown += (_, e) => paletteStart = e.GetPosition(b);
            b.PreviewMouseMove += (_, e) => { var p = e.GetPosition(b); if (e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - paletteStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - paletteStart.Y) > SystemParameters.MinimumVerticalDragDistance)) DragDrop.DoDragDrop(b, new DataObject("ChudLink.Widget", kind.ToString()), DragDropEffects.Copy); }; palette.Children.Add(b);
        }
        body.Children.Add(new ScrollViewer { Content = palette, Margin = new(0, 0, 10, 0) });
        var viewbox = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Margin = new(16), MaxHeight = 1100 };
        var preview = MainWindow.Card(viewbox, 0); preview.Background = B("#060A07"); Grid.SetColumn(preview, 1); body.Children.Add(preview);
        canvas.Drop += (_, e) => { if (e.Data.GetData("ChudLink.Widget") is string kind && Enum.TryParse<WidgetKind>(kind, out var type)) { var p = e.GetPosition(canvas); Add(type, p.X, p.Y); } };
        canvas.MouseLeftButtonDown += (_, e) => { if (e.Source == canvas) { selected.Clear(); Inspector(); DrawCanvas(); canvas.Focus(); } };
        pages.SelectionChanged += (_, _) => { if (loading) return; pageIndex = Math.Max(0, pages.SelectedIndex); selected.Clear(); DrawCanvas(); Inspector(); };
        layers.SelectionChanged += (_, _) => { if (loading || layers.SelectedItem is not LayerItem item) return; selected.Clear(); selected.Add(item.Id); DrawCanvas(); Inspector(); };
        var right = new StackPanel(); right.Children.Add(MainWindow.Label("PROPERTIES / EVENTS", 10, "#C6FF2E")); right.Children.Add(inspector); right.Children.Add(MainWindow.Label("LAYERS", 10, "#C6FF2E")); right.Children.Add(layers);
        var scroll = new ScrollViewer { Content = right, Padding = new(0, 0, 3, 0) }; Grid.SetColumn(scroll, 2); body.Children.Add(scroll);
        Content = root; PreviewKeyDown += KeyPressed; Draw();
    }
    static SolidColorBrush B(string color) { try { return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; } catch { return new(Colors.LimeGreen); } }
    void Safe(Action action) { try { action(); } catch (Exception ex) { notice(ex.Message); } }
    double Snap(double value) => snap.IsChecked == true ? Math.Round(value / 12) * 12 : value;
    async Task PublishAsync() { try { document.Validate(); await host.PublishAsync(document); notice("Saved dashboard and sent it to assigned devices."); } catch (Exception ex) { notice(ex.Message); } }
    void Add(WidgetKind kind, double x, double y)
    {
        history.Save(document);
        var w = new Widget { Kind = kind, Label = kind.ToString().ToUpperInvariant(), X = Math.Clamp(Snap(x), 0, document.Width - 240), Y = Math.Clamp(Snap(y), 0, document.Height - 144), Width = 240, Height = 144, Layer = Page.Widgets.Count };
        Page.Widgets.Add(w); selected.Clear(); selected.Add(w.Id); Draw();
    }
    void Draw()
    {
        loading = true; pages.ItemsSource = document.Pages.ToArray(); pages.SelectedIndex = pageIndex; loading = false;
        DrawCanvas(); Inspector();
    }
    void DrawCanvas()
    {
        canvas.Width = document.Width; canvas.Height = document.Height; canvas.Background = B(document.Background); canvas.Children.Clear();
        if (snap.IsChecked == true)
        {
            var geometry = new GeometryGroup(); geometry.Children.Add(new EllipseGeometry(new Point(1, 1), .8, .8));
            var brush = new DrawingBrush(new GeometryDrawing(B("#29372C"), null, geometry)) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 12, 12), ViewboxUnits = BrushMappingMode.Absolute };
            canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = document.Width, Height = document.Height, Fill = brush, IsHitTestVisible = false });
        }
        foreach (var w in Page.Widgets.OrderBy(w => w.Layer))
        {
            var content = new Grid { Margin = new(14) };
            var title = MainWindow.Label(w.Label, Math.Min(w.FontSize, 22), w.Color); content.Children.Add(title);
            var display = MainWindow.Label(PreviewText(w), w.Kind == WidgetKind.Clock ? 34 : 24, w.Color); display.VerticalAlignment = VerticalAlignment.Center; display.Margin = new(0, 22, 0, 0); content.Children.Add(display);
            if (w.Kind == WidgetKind.Image && w.Source?.StartsWith("data:image/") == true)
            {
                try { using var stream = new MemoryStream(Convert.FromBase64String(w.Source[(w.Source.IndexOf(',') + 1)..])); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); content.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform }); } catch (Exception) { }
            }
            var border = new Border { Width = w.Width, Height = w.Height, Background = B(w.Background), BorderBrush = B(selected.Contains(w.Id) ? "#C6FF2E" : "#344238"), BorderThickness = new(selected.Contains(w.Id) ? 2 : 1), CornerRadius = new(12), Child = content, Tag = w, Cursor = Cursors.SizeAll };
            if (w.GradientEnd != null) border.Background = new LinearGradientBrush(B(w.Background).Color, B(w.GradientEnd).Color, 45);
            Canvas.SetLeft(border, w.X); Canvas.SetTop(border, w.Y); canvas.Children.Add(border);
            border.MouseLeftButtonDown += (_, e) =>
            {
                canvas.Focus();
                if (!selected.Contains(w.Id)) { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) selected.Clear(); selected.Add(w.Id); }
                history.Save(document); start = e.GetPosition(canvas); initial = Page.Widgets.Where(v => selected.Contains(v.Id) || selected.Contains(v.ParentId ?? "")).ToDictionary(v => v.Id, v => new Point(v.X, v.Y));
                moving = true; border.CaptureMouse(); Inspector(); e.Handled = true;
            };
            border.MouseMove += (_, e) =>
            {
                if (!moving || !border.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                var pos = e.GetPosition(canvas); var dx = Snap(pos.X - start.X); var dy = Snap(pos.Y - start.Y);
                dx = Math.Clamp(dx, -initial.Values.Min(p => p.X), document.Width - initial.Max(kv => kv.Value.X + Page.Widgets.First(z => z.Id == kv.Key).Width));
                dy = Math.Clamp(dy, -initial.Values.Min(p => p.Y), document.Height - initial.Max(kv => kv.Value.Y + Page.Widgets.First(z => z.Id == kv.Key).Height));
                foreach (var kv in initial) { var item = Page.Widgets.First(z => z.Id == kv.Key); item.X = kv.Value.X + dx; item.Y = kv.Value.Y + dy; }
                foreach (var element in canvas.Children.OfType<Border>()) if (element.Tag is Widget item) { Canvas.SetLeft(element, item.X); Canvas.SetTop(element, item.Y); }
            };
            border.MouseLeftButtonUp += (_, e) => { if (moving) { moving = false; border.ReleaseMouseCapture(); Draw(); } e.Handled = true; };
            if (selected.Contains(w.Id))
            {
                var resize = new Thumb { Width = 18, Height = 18, Background = B("#C6FF2E"), Cursor = Cursors.SizeNWSE };
                Canvas.SetLeft(resize, w.X + w.Width - 18); Canvas.SetTop(resize, w.Y + w.Height - 18); canvas.Children.Add(resize);
                resize.DragStarted += (_, _) => history.Save(document);
                resize.DragDelta += (_, e) => { w.Width = Math.Clamp(Snap(w.Width + e.HorizontalChange), 24, document.Width - w.X); w.Height = Math.Clamp(Snap(w.Height + e.VerticalChange), 24, document.Height - w.Y); border.Width = w.Width; border.Height = w.Height; Canvas.SetLeft(resize, w.X + w.Width - 18); Canvas.SetTop(resize, w.Y + w.Height - 18); };
                resize.DragCompleted += (_, _) => Draw();
            }
        }
        loading = true; layers.ItemsSource = Page.Widgets.OrderByDescending(w => w.Layer).Select(w => new LayerItem(w.Id, $"{w.Layer} · {w.Label}")).ToArray(); loading = false;
    }
    string PreviewText(Widget w)
    {
        if (w.DataKey != null && host.Latest.TryGetValue(w.DataKey, out var sample)) return sample.Text;
        return w.Kind switch { WidgetKind.Clock => DateTime.Now.ToString("HH:mm:ss"), WidgetKind.Button or WidgetKind.Navigation => "↗", WidgetKind.Timer => "05:00", WidgetKind.Toggle => w.Value > 0 ? "ON" : "OFF", _ => string.IsNullOrEmpty(w.Text) ? w.Kind is WidgetKind.Gauge or WidgetKind.Progress or WidgetKind.Counter or WidgetKind.Slider ? w.Value.ToString("F0") : w.DataKey ?? "" : w.Text };
    }
    void Inspector()
    {
        inspector.Children.Clear();
        TextBox Field(string title, string text) { inspector.Children.Add(MainWindow.Label(title, 10)); var field = new TextBox { Text = text, FontSize = 12, Padding = new(7, 5, 7, 5), Margin = new(0, 0, 0, 8) }; inspector.Children.Add(field); return field; }
        if (selected.Count != 1)
        {
            inspector.Children.Add(MainWindow.Label(selected.Count == 0 ? "Drag a widget onto the canvas, or click to add. Ctrl-click selects several widgets." : $"{selected.Count} widgets selected. Use alignment and container tools.", 12));
            var name = Field("Dashboard name", document.Name); var pageName = Field("Page name", Page.Name); var background = Field("Background", document.Background);
            inspector.Children.Add(MainWindow.Btn("Apply dashboard", () => { history.Save(document); document.Name = name.Text; Page.Name = pageName.Text; document.Background = background.Text; Draw(); }));
            if (document.Pages.Count > 1) inspector.Children.Add(MainWindow.Btn("Remove this page", () => { history.Save(document); document.Pages.RemoveAt(pageIndex); pageIndex = Math.Max(0, pageIndex - 1); selected.Clear(); Draw(); })); return;
        }
        var w = Page.Widgets.FirstOrDefault(w => selected.Contains(w.Id)); if (w == null) { selected.Clear(); Inspector(); return; }
        inspector.Children.Add(MainWindow.Label(w.Kind + " / " + w.Id[..Math.Min(8, w.Id.Length)], 12, "#C6FF2E"));
        var label = Field("Label", w.Label); var text = Field("Text", w.Text); var x = Field("X", w.X.ToString("F0")); var y = Field("Y", w.Y.ToString("F0"));
        var width = Field("Width", w.Width.ToString("F0")); var height = Field("Height", w.Height.ToString("F0"));
        var color = Field("Accent color", w.Color); var bg = Field("Background", w.Background); var gradient = Field("Gradient end (optional)", w.GradientEnd ?? "");
        var font = Field("Font family", w.FontFamily); var fontSize = Field("Font size", w.FontSize.ToString("F0"));
        var data = Field("Data key (e.g. host.cpu)", w.DataKey ?? ""); var value = Field("Initial value", w.Value.ToString()); var maximum = Field("Maximum", w.Maximum.ToString());
        var source = Field("Image data URI / source", w.Source ?? "");
        if (w.Kind == WidgetKind.Image) inspector.Children.Add(MainWindow.Btn("Embed image…", () => { var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" }; if (dialog.ShowDialog() == true) { var bytes = File.ReadAllBytes(dialog.FileName); if (bytes.Length > 256 * 1024) { notice("Use an image smaller than 256 KB. The full dashboard must fit in 950 KB."); return; } source.Text = "data:image/" + (Path.GetExtension(dialog.FileName) == ".png" ? "png" : "jpeg") + ";base64," + Convert.ToBase64String(bytes); } }));
        inspector.Children.Add(MainWindow.Label("EVENT → ACTION", 10, "#C6FF2E"));
        var eventBox = new ComboBox { ItemsSource = new[] { "press", "change" }, SelectedItem = w.Bindings.FirstOrDefault()?.Event ?? "press" }; inspector.Children.Add(eventBox);
        var action = new ComboBox { DisplayMemberPath = "Name", MinWidth = 0 };
        var options = new List<ActionOption> { new("No action", "", null), new("Increment shared counter", "counter", null), new("Media: play / pause", "media", "play"), new("Media: next", "media", "next"), new("Media: previous", "media", "previous"), new("Media: mute", "media", "mute"), new("Master volume", "volume", null), new("Cross-device switch", "mesh", null), new("Custom extension action", "custom", null) };
        options.AddRange(host.Store.Settings.Actions.Select(a => new ActionOption("Approved: " + a.Name, "approved", a.Id)));
        options.AddRange(host.Store.Dashboards.Values.Select(d => new ActionOption("Dashboard: " + d.Name, "navigate", d.Id)));
        var binding = w.Bindings.FirstOrDefault(); action.ItemsSource = options; action.SelectedItem = options.FirstOrDefault(o => o.Action == binding?.Action && o.Argument == binding?.Argument) ?? (binding == null ? options[0] : options.First(o => o.Action == "custom")); inspector.Children.Add(action);
        var customId = Field("Custom action ID", binding?.Action ?? ""); var arg = Field("Argument / targetId|dashboardId", binding?.Argument ?? "");
        var properties = Field("Extra properties (JSON string values)", Wire.Serialize(w.Properties)); properties.TextWrapping = TextWrapping.Wrap; properties.AcceptsReturn = true; properties.MinHeight = 65;
        inspector.Children.Add(MainWindow.Btn("Apply widget", () => Safe(() =>
        {
            var copy = document.Clone(); var changed = copy.Pages.SelectMany(p => p.Widgets).First(z => z.Id == w.Id);
            changed.Label = label.Text; changed.Text = text.Text; changed.X = double.Parse(x.Text); changed.Y = double.Parse(y.Text); changed.Width = double.Parse(width.Text); changed.Height = double.Parse(height.Text);
            changed.Color = color.Text; changed.Background = bg.Text; changed.GradientEnd = string.IsNullOrWhiteSpace(gradient.Text) ? null : gradient.Text; changed.FontFamily = font.Text; changed.FontSize = double.Parse(fontSize.Text);
            changed.DataKey = string.IsNullOrWhiteSpace(data.Text) ? null : data.Text; changed.Value = double.Parse(value.Text); changed.Maximum = double.Parse(maximum.Text); changed.Source = source.Text; changed.Properties = Wire.Parse<Dictionary<string, string>>(properties.Text);
            if (action.SelectedItem is ActionOption a && a.Action.Length > 0) changed.Bindings = [new(a.Action == "custom" ? customId.Text : a.Action, a.Argument ?? (arg.Text.Length > 0 ? arg.Text : null), eventBox.SelectedItem!.ToString()!)]; else changed.Bindings.Clear();
            copy.Validate(); history.Save(document); document = copy; Draw();
        })));
        inspector.Children.Add(MainWindow.Label("Phone rendering uses installed fonts and declarative widget types. Bindings run only if the device has the required permission.", 11));
    }
    void Copy() => clipboard = Wire.Serialize(Page.Widgets.Where(w => selected.Contains(w.Id) || selected.Contains(w.ParentId ?? "")).ToArray());
    void Paste()
    {
        if (clipboard == null) return; history.Save(document); selected.Clear();
        var items = Wire.Parse<Widget[]>(clipboard); var ids = items.ToDictionary(w => w.Id, _ => Guid.NewGuid().ToString("N"));
        foreach (var w in items) { w.Id = ids[w.Id]; w.ParentId = w.ParentId != null && ids.TryGetValue(w.ParentId, out var parent) ? parent : null; w.X = Math.Min(document.Width - w.Width, w.X + 24); w.Y = Math.Min(document.Height - w.Height, w.Y + 24); w.Layer = Page.Widgets.Count; Page.Widgets.Add(w); selected.Add(w.Id); } Draw();
    }
    void Delete() { history.Save(document); Page.Widgets.RemoveAll(w => selected.Contains(w.Id)); foreach (var w in Page.Widgets) if (selected.Contains(w.ParentId ?? "")) w.ParentId = null; selected.Clear(); Draw(); }
    void Align(bool vertical) { var items = Page.Widgets.Where(w => selected.Contains(w.Id)).ToArray(); if (items.Length < 2) return; history.Save(document); var min = items.Min(w => vertical ? w.Y : w.X); foreach (var w in items) { if (vertical) w.Y = min; else w.X = min; } Draw(); }
    void Center() { history.Save(document); foreach (var w in Page.Widgets.Where(w => selected.Contains(w.Id))) w.X = (document.Width - w.Width) / 2; Draw(); }
    void Layer(int delta) { history.Save(document); foreach (var w in Page.Widgets.Where(w => selected.Contains(w.Id))) w.Layer += delta; Draw(); }
    void Group()
    {
        var widgets = Page.Widgets.Where(w => selected.Contains(w.Id)).ToArray(); if (widgets.Length == 0) return; history.Save(document);
        var x = Math.Max(0, widgets.Min(w => w.X) - 12); var y = Math.Max(0, widgets.Min(w => w.Y) - 12);
        var container = new Widget { Kind = WidgetKind.Container, Label = "Container", X = x, Y = y, Width = Math.Min(document.Width - x, widgets.Max(w => w.X + w.Width) - x + 12), Height = Math.Min(document.Height - y, widgets.Max(w => w.Y + w.Height) - y + 12), Layer = widgets.Min(w => w.Layer) - 1, Background = "#0B110D" };
        foreach (var w in widgets) w.ParentId = container.Id; Page.Widgets.Add(container); selected.Clear(); selected.Add(container.Id); Draw();
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Filter = "ChudLink dashboards|*.chudlink" }; if (dialog.ShowDialog() != true) return;
        var imported = Wire.Parse<Dashboard>(File.ReadAllText(dialog.FileName)); imported.Validate(); history.Save(document); document = imported; pageIndex = 0; selected.Clear(); Draw();
    }
    void Export()
    {
        document.Validate(); var dialog = new SaveFileDialog { Filter = "ChudLink dashboards|*.chudlink", FileName = "Dashboard.chudlink" };
        if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, Wire.Serialize(document));
    }
    void KeyPressed(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key) { case Key.C: Copy(); break; case Key.V: Paste(); break; case Key.D: Copy(); Paste(); break; case Key.Z: document = history.Undo(document); Draw(); break; case Key.Y: document = history.Redo(document); Draw(); break; default: return; } e.Handled = true;
        }
        else if (e.Key == Key.Delete) { Delete(); e.Handled = true; }
    }
    sealed record LayerItem(string Id, string Name) { public override string ToString() => Name; }
    sealed record ActionOption(string Name, string Action, string? Argument);
}

