namespace SharpEmuUpdater;

/// <summary>
/// Font sizes set in points (the WinForms default) already scale correctly with DPI on their
/// own -- that's the whole point of points vs. pixels, GDI+ converts them using the target
/// device's real DPI automatically. Everything else in this app (window size, button heights,
/// padding, corner radii, icon sizes) is a raw pixel count with no inherent DPI-awareness, and
/// AutoScaleMode.Dpi does not reliably rescale those when set imperatively in code rather than
/// through the WinForms Designer's InitializeComponent pattern -- confirmed by direct diagnostic
/// (ClientSize stayed at its literal coded value even at 125% DPI). So every raw pixel constant
/// in this app is scaled explicitly through this instead.
/// </summary>
public static class UiScale
{
    public static float Factor { get; private set; } = 1f;

    public static void Update(int dpi) => Factor = Math.Max(1, dpi) / 96f;

    public static int S(int value) => (int)Math.Round(value * Factor);
}
