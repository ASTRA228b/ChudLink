using ChudLink.Protocol;

namespace ChudLink.Phone;

public readonly record struct PhoneBounds(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}
public sealed record PhoneCard(Widget Widget, PhoneBounds Bounds, double LogicalWidth);
public sealed record PhonePage(PhoneCard[] Cards, double ContentHeight)
{
    public PhoneCard? HitTest(double x, double y) => Cards.LastOrDefault(card => card.Widget.Kind != WidgetKind.Container && card.Bounds.Contains(x, y));
}

/// <summary>All coordinates here are density-independent pixels, never physical screen pixels.</summary>
public static class PhoneLayout
{
    public static PhonePage Arrange(Dashboard dashboard, int page, double width, double height, bool autoFit = true, double fontScale = 1)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return new([], 0);
        fontScale = double.IsFinite(fontScale) && fontScale > 0 ? fontScale : 1;
        var widgets = dashboard.Pages[page].Widgets;
        if (!autoFit)
        {
            var scale = Math.Min(width / dashboard.Width, height / dashboard.Height);
            var ox = (width - dashboard.Width * scale) / 2; var oy = (height - dashboard.Height * scale) / 2;
            return new(widgets.OrderBy(w => w.Layer).Select(w => new PhoneCard(w, new(ox + w.X * scale, oy + w.Y * scale, w.Width * scale, w.Height * scale), w.Width)).ToArray(), height);
        }

        const double padding = 12, gap = 12;
        var usable = Math.Max(1, width - padding * 2);
        var columns = Math.Clamp((int)Math.Floor((usable + gap) / (280 * Math.Max(1, fontScale) + gap)), 1, 4);
        var cardWidth = (usable - gap * (columns - 1)) / columns;
        var ordered = widgets.Where(w => w.Kind != WidgetKind.Container).OrderBy(w => w.Y).ThenBy(w => w.X).ThenBy(w => w.Layer).ToArray();
        var cards = new List<PhoneCard>(); var y = padding;
        for (var row = 0; row * columns < ordered.Length; row++)
        {
            var rowWidgets = ordered.Skip(row * columns).Take(columns).ToArray();
            var rowHeight = rowWidgets.Max(w =>
            {
                var authoredHeight = Math.Clamp(w.Height * 324 / w.Width, 166, 648);
                var lines = w.Kind is WidgetKind.Text or WidgetKind.Terminal or WidgetKind.Status ? Math.Min(8, Math.Max(1, (int)Math.Ceiling(w.Text.Length / (28 / fontScale)))) : 1;
                var textHeight = 64 + lines * Math.Max(24, w.FontSize) * 1.4;
                return Math.Max(132 * fontScale, Math.Max(authoredHeight, textHeight) * fontScale * cardWidth / 324);
            });
            for (var col = 0; col < rowWidgets.Length; col++) cards.Add(new(rowWidgets[col], new(padding + col * (cardWidth + gap), y, cardWidth, rowHeight), 324));
            y += rowHeight + gap;
        }
        return new(cards.ToArray(), Math.Max(height, cards.Count == 0 ? height : y));
    }
}
