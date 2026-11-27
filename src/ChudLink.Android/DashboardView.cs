using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using ChudLink.Protocol;
using Color = Android.Graphics.Color;

namespace ChudLink.Phone;

public sealed class DashboardView : View
{
    readonly Paint paint = new(PaintFlags.AntiAlias);
    readonly Handler handler = new(Looper.MainLooper!);
    readonly Dictionary<string, Queue<double>> graphs = new();
    readonly Dictionary<string, DateTimeOffset> timers = new();
    readonly Dictionary<string, double> elapsed = new();
    readonly Dictionary<string, Bitmap> images = new();
    readonly Dictionary<string, Typeface> fonts = new();
    readonly Action tick;
    Dashboard dashboard = new();
    int page;
    float scale = 1, ox, oy;
    Widget? pressed;
    long lastChange;
    readonly RenderProfile profile;
    public event Action<TouchEvent>? TouchAction;
    public event Action<Widget>? EditRequested;
    public DashboardView(Context context, RenderProfile profile) : base(context)
    {
        this.profile = profile; Focusable = true; ContentDescription = "ChudLink dashboard. Tap widgets to activate.";
        tick = () => { if (IsAttachedToWindow) { if (dashboard.Pages[page].Widgets.Any(w => w.Kind is WidgetKind.Clock or WidgetKind.Timer)) Invalidate(); handler.PostDelayed(tick!, 1000); } };
    }
    protected override void OnAttachedToWindow() { base.OnAttachedToWindow(); handler.PostDelayed(tick, 1000); }
    protected override void OnDetachedFromWindow() { handler.RemoveCallbacks(tick); base.OnDetachedFromWindow(); }
    public void Load(Dashboard value)
    {
        dashboard = value; page = Math.Min(page, value.Pages.Count - 1); graphs.Clear();
        foreach (var bitmap in images.Values) bitmap.Dispose(); images.Clear();
        long pixels = 0;
        foreach (var w in value.Pages.SelectMany(p => p.Widgets).Where(w => w.Kind == WidgetKind.Image && w.Source?.StartsWith("data:image/") == true).Take(16))
        {
            try
            {
                var source = w.Source!; if (source.Length > 1400000) continue;
                var bytes = Convert.FromBase64String(source[(source.IndexOf(',') + 1)..]);
                using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true }; BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
                if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0 || bounds.OutWidth > 4096 || bounds.OutHeight > 4096) continue;
                using var options = new BitmapFactory.Options { InSampleSize = Math.Max(1, Math.Max(bounds.OutWidth, bounds.OutHeight) / 1024) };
                if (pixels + (long)bounds.OutWidth * bounds.OutHeight / (options.InSampleSize * options.InSampleSize) > 4000000) continue;
                var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
                if (bitmap != null) { images[w.Id] = bitmap; pixels += (long)bitmap.Width * bitmap.Height; }
            }
            catch (Exception) { }
        }
        Invalidate();
    }
    public void Patch(WidgetPatch[] updates)
    {
        foreach (var p in updates)
        {
            var w = dashboard.Pages.SelectMany(p => p.Widgets).FirstOrDefault(w => w.Id == p.Id); if (w == null) continue;
            if (p.Text != null) w.Text = p.Text; if (p.Value is { } value && double.IsFinite(value)) w.Value = value; if (p.Color != null) w.Color = p.Color;
            if (w.Kind == WidgetKind.Graph && p.Value.HasValue)
            { if (!graphs.TryGetValue(w.Id, out var q)) graphs[w.Id] = q = new(); q.Enqueue(p.Value.Value); while (q.Count > (profile == RenderProfile.Legacy ? 30 : 60)) q.Dequeue(); }
        }
        Invalidate();
    }
    static Color C(string? text, string fallback = "#C6FF2E") { try { return Color.ParseColor(text ?? fallback); } catch { return Color.ParseColor(fallback); } }
    void Fill(Canvas c, RectF rect, Color color, float radius = 14)
    { paint.SetShader(null); paint.Color = color; paint.SetStyle(Paint.Style.Fill); c.DrawRoundRect(rect, radius, radius, paint); }
    void Text(Canvas c, string text, float x, float y, float size, Color color, string? font = null)
    {
        font ??= "sans-serif";
        if (!fonts.TryGetValue(font, out var face)) { face = Typeface.Create(font, TypefaceStyle.Normal)!; if (fonts.Count < 32) fonts[font] = face; else { face.Dispose(); face = Typeface.Default!; } }
        paint.SetShader(null); paint.Color = color; paint.TextSize = size; paint.SetStyle(Paint.Style.Fill); paint.SetTypeface(face); c.DrawText(text, x, y, paint);
    }
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas); canvas.DrawColor(C(dashboard.Background, "#0A0E0B"));
        scale = Math.Min(Width / (float)dashboard.Width, (Height - 40) / (float)dashboard.Height); if (scale <= 0) return;
        ox = (Width - (float)dashboard.Width * scale) / 2; oy = (Height - 40 - (float)dashboard.Height * scale) / 2;
        canvas.Save(); canvas.Translate(ox, oy); canvas.Scale(scale, scale);
        foreach (var w in dashboard.Pages[page].Widgets.OrderBy(w => w.Layer)) DrawWidget(canvas, w);
        canvas.Restore();
        Text(canvas, $"{dashboard.Name}  ·  {page + 1}/{dashboard.Pages.Count}" + (dashboard.Pages.Count > 1 ? "  › TAP TO CHANGE PAGE" : ""), 16, Height - 13, 12, C("#8B9C8F"));
    }
    void DrawWidget(Canvas c, Widget w)
    {
        var x = (float)w.X; var y = (float)w.Y; var width = (float)w.Width; var height = (float)w.Height;
        var r = new RectF(x, y, x + width, y + height); var accent = C(w.Color);
        Fill(c, r, C(w.Background, "#161D18"));
        if (w.GradientEnd != null)
        {
            using var shader = new LinearGradient(x, y, x + width, y + height, C(w.Background), C(w.GradientEnd), Shader.TileMode.Clamp!);
            paint.SetShader(shader); c.DrawRoundRect(r, 14, 14, paint); paint.SetShader(null);
        }
        paint.Color = C(pressed == w ? w.Color : "#2A382D"); paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 1.5f; c.DrawRoundRect(r, 14, 14, paint); paint.SetStyle(Paint.Style.Fill);
        c.Save(); c.ClipRect(x + 12, y + 8, x + width - 12, y + height - 8);
        Text(c, w.Label, x + 18, y + 31, 20, C("#A3B5A7"), w.FontFamily);
        var value = Math.Clamp(w.Value / w.Maximum, 0, 1); var text = string.IsNullOrEmpty(w.Text) ? w.Kind == WidgetKind.Counter ? w.Value.ToString("F0") : "" : w.Text;
        var size = (float)Math.Clamp(w.FontSize, 10, 96);
        switch (w.Kind)
        {
            case WidgetKind.Button: case WidgetKind.Navigation:
                Text(c, string.IsNullOrEmpty(text) ? "↗" : text, x + 20, y + height - 28, Math.Max(30, size), accent, w.FontFamily); break;
            case WidgetKind.Clock:
                Text(c, DateTime.Now.ToString("HH:mm:ss"), x + 18, y + 92, Math.Min(48, width / 5), accent, w.FontFamily);
                Text(c, DateTime.Now.ToString("ddd, dd MMM yyyy"), x + 18, y + 127, 18, C("#92A597")); break;
            case WidgetKind.Progress: case WidgetKind.Slider:
                Fill(c, new(x + 18, y + height - 40, x + width - 18, y + height - 22), C("#2A382D"), 8);
                Fill(c, new(x + 18, y + height - 40, x + 18 + (width - 36) * (float)value, y + height - 22), accent, 8);
                Text(c, string.IsNullOrEmpty(text) ? $"{w.Value:F0}%" : text, x + 18, y + 85, size, accent, w.FontFamily); break;
            case WidgetKind.Toggle:
                Fill(c, new(x + width - 90, y + 65, x + width - 20, y + 102), w.Value > 0 ? accent : C("#344238"), 20);
                paint.Color = Color.White; c.DrawCircle(x + width - (w.Value > 0 ? 38 : 72), y + 84, 12, paint); Text(c, w.Value > 0 ? "ON" : "OFF", x + 18, y + 90, size, accent); break;
            case WidgetKind.Gauge: case WidgetKind.Dial:
                var arc = new RectF(x + width / 2 - 60, y + 52, x + width / 2 + 60, y + 160);
                paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 10; paint.Color = C("#2A382D"); c.DrawArc(arc, 150, 240, false, paint); paint.Color = accent; c.DrawArc(arc, 150, (float)value * 240, false, paint); paint.SetStyle(Paint.Style.Fill);
                Text(c, string.IsNullOrEmpty(text) ? $"{w.Value:F0}" : text, x + width / 2 - 38, y + 122, 26, accent); break;
            case WidgetKind.Graph:
                if (graphs.TryGetValue(w.Id, out var history) && history.Count > 1)
                {
                    var samples = history.ToArray(); var max = Math.Max(w.Maximum, samples.Max()); using var path = new global::Android.Graphics.Path();
                    for (var i = 0; i < samples.Length; i++) { var gx = x + 18 + (width - 36) * i / (samples.Length - 1); var gy = y + height - 20 - (height - 70) * (float)(samples[i] / max); if (i == 0) path.MoveTo(gx, gy); else path.LineTo(gx, gy); }
                    paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 3; paint.Color = accent; c.DrawPath(path, paint); paint.SetStyle(Paint.Style.Fill);
                }
                Text(c, text, x + 18, y + 57, 17, accent); break;
            case WidgetKind.Status:
                paint.Color = text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || text.StartsWith("No services") ? C("#F6BC64") : accent;
                c.DrawCircle(x + 24, y + 62, 6, paint); Wrap(c, text, x + 40, y + 70, width - 56, 19, accent, w.FontFamily); break;
            case WidgetKind.Timer:
                var isStopwatch = w.Id == "stopwatch" || w.Properties.GetValueOrDefault("mode") == "stopwatch";
                var seconds = elapsed.GetValueOrDefault(w.Id) + (timers.TryGetValue(w.Id, out var start) ? (DateTimeOffset.UtcNow - start).TotalSeconds : 0);
                var remaining = isStopwatch ? seconds : Math.Max(0, (w.Properties.TryGetValue("seconds", out var configured) && double.TryParse(configured, out var v) ? v : 300) - seconds);
                Text(c, TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss"), x + 18, y + 98, 46, accent); Text(c, "TAP TO START / PAUSE", x + 18, y + 137, 14, C("#92A597")); break;
            case WidgetKind.Image:
                if (images.TryGetValue(w.Id, out var bitmap)) c.DrawBitmap(bitmap, null, new RectF(x + 18, y + 42, x + width - 18, y + height - 18), paint); else Text(c, "Embed an image in Designer", x + 18, y + 75, 17, accent); break;
            case WidgetKind.Container: break;
            case WidgetKind.Terminal: Wrap(c, text, x + 18, y + 64, width - 36, 16, accent, "monospace"); break;
            default: Wrap(c, text, x + 18, y + 75, width - 36, size, accent, w.FontFamily); break;
        }
        c.Restore();
    }
    void Wrap(Canvas c, string text, float x, float y, float width, float size, Color color, string font)
    {
        foreach (var paragraph in text.Split('\n').Take(20))
        {
            string line = "";
            foreach (var word in paragraph.Split(' '))
            {
                paint.TextSize = size;
                if (paint.MeasureText(line + word) > width && line.Length > 0) { Text(c, line, x, y, size, color, font); y += size * 1.4f; line = ""; }
                line += word + " ";
            }
            Text(c, line, x, y, size, color, font); y += size * 1.4f;
        }
    }
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        if (e.Action == MotionEventActions.Down)
        {
            if (e.GetY() > Height - 40 && dashboard.Pages.Count > 1) { page = (page + 1) % dashboard.Pages.Count; Invalidate(); return true; }
            var x = (e.GetX() - ox) / scale; var y = (e.GetY() - oy) / scale;
            pressed = dashboard.Pages[page].Widgets.OrderBy(w => w.Layer).Reverse().FirstOrDefault(w => w.Kind != WidgetKind.Container && x >= w.X && x <= w.X + w.Width && y >= w.Y && y <= w.Y + w.Height);
            if (pressed != null) { PerformHapticFeedback(FeedbackConstants.VirtualKey); Invalidate(); } return true;
        }
        if (pressed is { } w && e.Action is MotionEventActions.Move or MotionEventActions.Up)
        {
            if (w.Kind is WidgetKind.Slider or WidgetKind.Dial)
            {
                w.Value = Math.Clamp(((e.GetX() - ox) / scale - w.X - 18) / (w.Width - 36), 0, 1) * w.Maximum;
                var now = SystemClock.ElapsedRealtime(); if (e.Action == MotionEventActions.Up || now - lastChange >= 100) { TouchAction?.Invoke(new(w.Id, "change", w.Value)); lastChange = now; }
            }
            else if (e.Action == MotionEventActions.Up)
            {
                if (w.Kind == WidgetKind.Toggle) w.Value = w.Value == 0 ? 1 : 0;
                if (w.Kind == WidgetKind.Timer)
                { if (timers.Remove(w.Id, out var started)) elapsed[w.Id] = elapsed.GetValueOrDefault(w.Id) + (DateTimeOffset.UtcNow - started).TotalSeconds; else timers[w.Id] = DateTimeOffset.UtcNow; }
                if (w.Id is "notes" or "tasks" || w.Properties.GetValueOrDefault("editable") == "true") EditRequested?.Invoke(w);
                if (w.Kind == WidgetKind.Navigation && w.Properties.TryGetValue("page", out var pageId)) { var index = dashboard.Pages.FindIndex(p => p.Id == pageId || p.Name == pageId); if (index >= 0) page = index; }
                TouchAction?.Invoke(new(w.Id, w.Kind == WidgetKind.Toggle ? "change" : "press", w.Value)); PerformClick();
            }
            if (e.Action == MotionEventActions.Up) pressed = null; Invalidate(); return true;
        }
        if (e.Action == MotionEventActions.Cancel) { pressed = null; Invalidate(); }
        return true;
    }
    public override bool PerformClick() { base.PerformClick(); return true; }
    protected override void Dispose(bool disposing) { if (disposing) { handler.RemoveCallbacks(tick); paint.Dispose(); foreach (var image in images.Values) image.Dispose(); images.Clear(); foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); } base.Dispose(disposing); }
}
