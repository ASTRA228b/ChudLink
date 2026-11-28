using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace ChudLink.Phone;

/// <summary>Keep controls outside visible system bars and camera cutouts, including edge-to-edge Android.</summary>
public sealed class SafeAreaFrame : FrameLayout
{
    public SafeAreaFrame(Context context) : base(context) => SetBackgroundColor(Color.Rgb(10, 14, 11));
    public override WindowInsets? OnApplyWindowInsets(WindowInsets? insets)
    {
        if (insets == null) return null;
        int left, top, right, bottom;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            left = safe.Left; top = safe.Top; right = safe.Right; bottom = safe.Bottom;
        }
        else
        {
#pragma warning disable CA1422 // API 26–29 use the legacy inset accessors.
            left = insets.SystemWindowInsetLeft; top = insets.SystemWindowInsetTop; right = insets.SystemWindowInsetRight; bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && insets.DisplayCutout is { } cutout)
            {
                left = Math.Max(left, cutout.SafeInsetLeft); top = Math.Max(top, cutout.SafeInsetTop);
                right = Math.Max(right, cutout.SafeInsetRight); bottom = Math.Max(bottom, cutout.SafeInsetBottom);
            }
        }
        SetPadding(left, top, right, bottom);
        return insets;
    }
}
