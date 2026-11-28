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
    PhonePage layout = new([], 0);
    int page;
    float scroll, downX, downY, downScroll, viewportHeight;
    bool dragging;
    PhoneCard? pressedCard;
    Widget? pressed;
    long lastChange;
    readonly RenderProfile profile;
    readonly bool autoFit;
    float Density => Resources?.DisplayMetrics?.Density ?? 1;
    float FontScale => Resources?.Configuration?.FontScale ?? 1;
    float FooterHeight => Math.Max(44, 24 * FontScale + 16);
    public event Action<TouchEvent>? TouchAction;
    public event Action<Widget>? EditRequested;
    public DashboardView(Context context, RenderProfile profile, bool autoFit = true) : base(context)
    {
        this.profile = profile; this.autoFit = autoFit; Focusable = true; ContentDescription = "ChudLink dashboard. Swipe vertically for more controls. Tap widgets to activate.";
        tick = () => { if (IsAttachedToWindow) { if (dashboard.Pages[page].Widgets.Any(w => w.Kind is WidgetKind.Clock or WidgetKind.Timer)) Invalidate(); handler.PostDelayed(tick!, 1000); } };
    }
    protected override void OnAttachedToWindow() { base.OnAttachedToWindow(); handler.PostDelayed(tick, 1000); }
    protected override void OnDetachedFromWindow() { handler.RemoveCallbacks(tick); base.OnDetachedFromWindow(); }
    public void Load(Dashboard value)
    {
        var changed = dashboard.Id != value.Id; dashboard = value; page = Math.Min(page, value.Pages.Count - 1); graphs.Clear(); if (changed) scroll = 0;
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
    void TextStyle(float size, string? font = null)
    {
        font ??= "sans-serif";
        if (!fonts.TryGetValue(font, out var face)) { face = Typeface.Create(font, TypefaceStyle.Normal)!; if (fonts.Count < 32) fonts[font] = face; else { face.Dispose(); face = Typeface.Default!; } }
        paint.SetShader(null); paint.TextSize = size * FontScale; paint.SetStyle(Paint.Style.Fill); paint.SetTypeface(face);
    }
    void Text(Canvas c, string text, float x, float y, float size, Color color, string? font = null)
    {
        TextStyle(size, font); paint.Color = color; c.DrawText(text, x, y, paint);
    }
    string FitText(string text, float width, float size, string? font = null)
    {
        TextStyle(size, font);
        if (paint.MeasureText(text) <= width) return text;
        var length = text.Length;
        while (length > 0 && paint.MeasureText(text[..length] + "…") > width) length--;
        return length == 0 ? "" : text[..length] + "…";
    }
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas); canvas.DrawColor(C(dashboard.Background, "#0A0E0B"));
        var density = Density; var width = Width / density; viewportHeight = Height / density - FooterHeight;
        if (viewportHeight <= 0 || width <= 0) return;
        layout = PhoneLayout.Arrange(dashboard, page, width, viewportHeight, autoFit, FontScale);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, (float)layout.ContentHeight - viewportHeight));
        canvas.Save(); canvas.Scale(density, density); canvas.Save(); canvas.ClipRect(0, 0, width, viewportHeight); canvas.Translate(0, -scroll);
        foreach (var card in layout.Cards)
        {
            var bounds = card.Bounds;
            if (bounds.Y + bounds.Height < scroll || bounds.Y > scroll + viewportHeight) continue;
            var scale = (float)(bounds.Width / card.LogicalWidth);
            canvas.Save(); canvas.Translate((float)bounds.X, (float)bounds.Y); canvas.Scale(scale, scale);
            DrawWidget(canvas, card.Widget, (float)card.LogicalWidth, (float)bounds.Height / scale); canvas.Restore();
        }
        canvas.Restore();
        if (layout.ContentHeight > viewportHeight)
        {
            var thumb = Math.Max(24, viewportHeight * viewportHeight / (float)layout.ContentHeight);
            var top = (viewportHeight - thumb) * scroll / ((float)layout.ContentHeight - viewportHeight);
            Fill(canvas, new(width - 5, top, width - 2, top + thumb), C("#718773"), 2);
        }
        Fill(canvas, new(0, viewportHeight, width, Height / density), C("#0A0E0B"), 0);
        var pages = dashboard.Pages.Count > 1 ? $"{page + 1}/{dashboard.Pages.Count}  ›" : "";
        TextStyle(14); var pageWidth = paint.MeasureText(pages); var baseline = viewportHeight + (FooterHeight - paint.Descent() - paint.Ascent()) / 2;
        Text(canvas, FitText(dashboard.Name, Math.Max(0, width - pageWidth - 40), 14), 12, baseline, 14, C("#A3B5A7"));
        if (pages.Length > 0) Text(canvas, pages, width - pageWidth - 12, baseline, 14, C("#C6FF2E"));
        canvas.Restore();
    }
    void DrawWidget(Canvas c, Widget w, float width, float height)
    {
        const float x = 0, y = 0;
        var r = new RectF(x, y, x + width, y + height); var accent = C(w.Color);
        Fill(c, r, C(w.Background, "#161D18"));
        if (w.GradientEnd != null)
        {
            using var shader = new LinearGradient(x, y, x + width, y + height, C(w.Background), C(w.GradientEnd), Shader.TileMode.Clamp!);
            paint.SetShader(shader); c.DrawRoundRect(r, 14, 14, paint); paint.SetShader(null);
        }
        paint.Color = C(pressed == w ? w.Color : "#2A382D"); paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 1.5f; c.DrawRoundRect(r, 14, 14, paint); paint.SetStyle(Paint.Style.Fill);
        c.Save(); c.ClipRect(x + 12, y + 8, x + width - 12, y + height - 8);
        var labelBaseline = 11 + 20 * FontScale; var contentTop = labelBaseline + 16;
        Text(c, FitText(w.Label, width - 36, 20, w.FontFamily), x + 18, y + labelBaseline, 20, C("#A3B5A7"), w.FontFamily);
        var value = Math.Clamp(w.Value / w.Maximum, 0, 1); var text = string.IsNullOrEmpty(w.Text) ? w.Kind == WidgetKind.Counter ? w.Value.ToString("F0") : "" : w.Text;
        var size = (float)Math.Clamp(w.FontSize, 10, 96);
        switch (w.Kind)
        {
            case WidgetKind.Button: case WidgetKind.Navigation:
                Text(c, FitText(string.IsNullOrEmpty(text) ? "↗" : text, width - 40, Math.Max(30, size), w.FontFamily), x + 20, y + height - 28, Math.Max(30, size), accent, w.FontFamily); break;
            case WidgetKind.Clock:
                var clockSize = Math.Min(48, (width - 36) / (5 * FontScale));
                Text(c, DateTime.Now.ToString("HH:mm:ss"), x + 18, y + contentTop + clockSize * FontScale, clockSize, accent, w.FontFamily);
                Text(c, FitText(DateTime.Now.ToString("ddd, dd MMM yyyy"), width - 36, 18), x + 18, y + contentTop + (clockSize + 30) * FontScale, 18, C("#92A597")); break;
            case WidgetKind.Progress: case WidgetKind.Slider:
                Fill(c, new(x + 18, y + height - 40, x + width - 18, y + height - 22), C("#2A382D"), 8);
                Fill(c, new(x + 18, y + height - 40, x + 18 + (width - 36) * (float)value, y + height - 22), accent, 8);
                Text(c, FitText(string.IsNullOrEmpty(text) ? $"{w.Value:F0}%" : text, width - 36, size, w.FontFamily), x + 18, y + contentTop + size * FontScale, size, accent, w.FontFamily); break;
            case WidgetKind.Toggle:
                Fill(c, new(x + width - 90, y + contentTop + 16, x + width - 20, y + contentTop + 53), w.Value > 0 ? accent : C("#344238"), 20);
                paint.Color = Color.White; c.DrawCircle(x + width - (w.Value > 0 ? 38 : 72), y + contentTop + 35, 12, paint); Text(c, w.Value > 0 ? "ON" : "OFF", x + 18, y + contentTop + size * FontScale, size, accent); break;
            case WidgetKind.Gauge: case WidgetKind.Dial:
                var arc = new RectF(x + width / 2 - 60, y + contentTop + 4, x + width / 2 + 60, y + contentTop + 112);
                paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 10; paint.Color = C("#2A382D"); c.DrawArc(arc, 150, 240, false, paint); paint.Color = accent; c.DrawArc(arc, 150, (float)value * 240, false, paint); paint.SetStyle(Paint.Style.Fill);
                Text(c, FitText(string.IsNullOrEmpty(text) ? $"{w.Value:F0}" : text, width - 36, 26), x + width / 2 - 38, y + contentTop + 72, 26, accent); break;
            case WidgetKind.Graph:
                if (graphs.TryGetValue(w.Id, out var history) && history.Count > 1)
                {
                    var samples = history.ToArray(); var max = Math.Max(w.Maximum, samples.Max()); using var path = new global::Android.Graphics.Path();
                    for (var i = 0; i < samples.Length; i++) { var gx = x + 18 + (width - 36) * i / (samples.Length - 1); var gy = y + height - 20 - Math.Max(1, height - contentTop - 48) * (float)(samples[i] / max); if (i == 0) path.MoveTo(gx, gy); else path.LineTo(gx, gy); }
                    paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = 3; paint.Color = accent; c.DrawPath(path, paint); paint.SetStyle(Paint.Style.Fill);
                }
                Text(c, FitText(text, width - 36, 17), x + 18, y + contentTop + 17 * FontScale, 17, accent); break;
            case WidgetKind.Status:
                paint.Color = text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || text.StartsWith("No services") ? C("#F6BC64") : accent;
                c.DrawCircle(x + 24, y + contentTop + 12, 6, paint); Wrap(c, text, x + 40, y + contentTop + 19 * FontScale, width - 56, 19, accent, w.FontFamily); break;
            case WidgetKind.Timer:
                var isStopwatch = w.Id == "stopwatch" || w.Properties.GetValueOrDefault("mode") == "stopwatch";
                var seconds = elapsed.GetValueOrDefault(w.Id) + (timers.TryGetValue(w.Id, out var start) ? (DateTimeOffset.UtcNow - start).TotalSeconds : 0);
                var remaining = isStopwatch ? seconds : Math.Max(0, (w.Properties.TryGetValue("seconds", out var configured) && double.TryParse(configured, out var v) ? v : 300) - seconds);
                Text(c, TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss"), x + 18, y + contentTop + 46 * FontScale, 46, accent); Text(c, "TAP TO START / PAUSE", x + 18, y + contentTop + 76 * FontScale, 14, C("#92A597")); break;
            case WidgetKind.Image:
                if (images.TryGetValue(w.Id, out var bitmap)) c.DrawBitmap(bitmap, null, new RectF(x + 18, y + contentTop, x + width - 18, y + height - 18), paint); else Wrap(c, "Embed an image in Designer", x + 18, y + contentTop + 17 * FontScale, width - 36, 17, accent, w.FontFamily); break;
            case WidgetKind.Container: break;
            case WidgetKind.Terminal: Wrap(c, text, x + 18, y + contentTop + 18 * FontScale, width - 36, 18, accent, "monospace"); break;
            default: Wrap(c, text, x + 18, y + contentTop + size * FontScale, width - 36, size, accent, w.FontFamily); break;
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
                TextStyle(size, font);
                if (paint.MeasureText(line + word) > width && line.Length > 0) { Text(c, line, x, y, size, color, font); y += size * FontScale * 1.4f; line = ""; }
                line += word + " ";
            }
            Text(c, line, x, y, size, color, font); y += size * FontScale * 1.4f;
        }
    }
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        var px = e.GetX() / Density; var py = e.GetY() / Density;
        if (e.Action == MotionEventActions.Down)
        {
            downX = px; downY = py; downScroll = scroll; dragging = false; pressed = null; pressedCard = null;
            if (py >= viewportHeight) { if (dashboard.Pages.Count > 1) { page = (page + 1) % dashboard.Pages.Count; scroll = 0; Invalidate(); } return true; }
            pressedCard = layout.HitTest(px, py + scroll); pressed = pressedCard?.Widget;
            Parent?.RequestDisallowInterceptTouchEvent(true);
            if (pressed != null) { PerformHapticFeedback(FeedbackConstants.VirtualKey); Invalidate(); } return true;
        }
        if (e.Action == MotionEventActions.Move)
        {
            var dy = py - downY; var dx = px - downX;
            var canScroll = layout.ContentHeight > viewportHeight;
            var slop = (ViewConfiguration.Get(Context!)?.ScaledTouchSlop ?? 8) / Density;
            if (canScroll && (dragging || Math.Abs(dy) > slop && (pressed?.Kind is not (WidgetKind.Slider or WidgetKind.Dial) || Math.Abs(dy) > Math.Abs(dx))))
            {
                dragging = true; pressed = null; pressedCard = null;
                scroll = Math.Clamp(downScroll - dy, 0, Math.Max(0, (float)layout.ContentHeight - viewportHeight)); Invalidate(); return true;
            }
        }
        if (e.Action == MotionEventActions.Up && dragging) { dragging = false; Parent?.RequestDisallowInterceptTouchEvent(false); Invalidate(); return true; }
        if (pressed is { } w && pressedCard is { } card && e.Action is MotionEventActions.Move or MotionEventActions.Up)
        {
            if (w.Kind is WidgetKind.Slider or WidgetKind.Dial)
            {
                var localX = (px - card.Bounds.X) * card.LogicalWidth / card.Bounds.Width;
                w.Value = Math.Clamp((localX - 18) / (card.LogicalWidth - 36), 0, 1) * w.Maximum;
                var now = SystemClock.ElapsedRealtime(); if (e.Action == MotionEventActions.Up || now - lastChange >= 100) { TouchAction?.Invoke(new(w.Id, "change", w.Value)); lastChange = now; }
            }
            else if (e.Action == MotionEventActions.Up && card.Bounds.Contains(px, py + scroll))
            {
                if (w.Kind == WidgetKind.Toggle) w.Value = w.Value == 0 ? 1 : 0;
                if (w.Kind == WidgetKind.Timer)
                { if (timers.Remove(w.Id, out var started)) elapsed[w.Id] = elapsed.GetValueOrDefault(w.Id) + (DateTimeOffset.UtcNow - started).TotalSeconds; else timers[w.Id] = DateTimeOffset.UtcNow; }
                if (w.Id is "notes" or "tasks" || w.Properties.GetValueOrDefault("editable") == "true") EditRequested?.Invoke(w);
                if (w.Kind == WidgetKind.Navigation && w.Properties.TryGetValue("page", out var pageId)) { var index = dashboard.Pages.FindIndex(p => p.Id == pageId || p.Name == pageId); if (index >= 0) { page = index; scroll = 0; } }
                TouchAction?.Invoke(new(w.Id, w.Kind == WidgetKind.Toggle ? "change" : "press", w.Value)); PerformClick();
            }
            if (e.Action == MotionEventActions.Up) { pressed = null; pressedCard = null; Parent?.RequestDisallowInterceptTouchEvent(false); } Invalidate(); return true;
        }
        if (e.Action == MotionEventActions.Cancel) { pressed = null; pressedCard = null; dragging = false; Parent?.RequestDisallowInterceptTouchEvent(false); Invalidate(); }
        return true;
    }
    public override bool PerformClick() { base.PerformClick(); return true; }
    protected override void Dispose(bool disposing) { if (disposing) { handler.RemoveCallbacks(tick); paint.Dispose(); foreach (var image in images.Values) image.Dispose(); images.Clear(); foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); } base.Dispose(disposing); }
}
