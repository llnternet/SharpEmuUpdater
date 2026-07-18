namespace SharpEmuUpdater;

/// <summary>
/// Always dark -- this app used to also follow Windows' own light/dark "Choose your mode"
/// setting (or a manually pinned override, cycled via a header button), but that was removed by
/// explicit request in favor of a single, fixed dark theme. Accent and status colors were always
/// fixed regardless of light/dark: the purple accent matches the real SharpEmu GUI's own branding
/// (src/SharpEmu.GUI/App.axaml).
/// </summary>
public static class Theme
{
    public static readonly Color BgTop = ColorTranslator.FromHtml("#12151F");
    public static readonly Color BgMid = ColorTranslator.FromHtml("#0D1017");
    public static readonly Color BgBottom = ColorTranslator.FromHtml("#0B0D14");

    public static readonly Color Chrome = ColorTranslator.FromHtml("#090C12");
    public static readonly Color Card = ColorTranslator.FromHtml("#141924");
    public static readonly Color CardBorder = ColorTranslator.FromHtml("#232B3A");
    public static readonly Color Elevated = ColorTranslator.FromHtml("#1B2230");

    public static readonly Color Text = ColorTranslator.FromHtml("#E8ECF4");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8B94A7");
    public static readonly Color Faint = ColorTranslator.FromHtml("#5A6478");

    public static readonly Color Console = ColorTranslator.FromHtml("#0B0E14");

    public static readonly Color Accent = ColorTranslator.FromHtml("#7C5CFC");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#8F73FF");
    public static readonly Color Danger = ColorTranslator.FromHtml("#E5484D");
    public static readonly Color DangerHover = ColorTranslator.FromHtml("#F2555A");
    public static readonly Color Success = ColorTranslator.FromHtml("#46C46B");
    public static readonly Color Regression = ColorTranslator.FromHtml("#F5A524");

    // GitHub's own in-progress indicator color (the animated yellow ring on the Actions tab).
    // Queued reuses Muted -- GitHub shows queued/waiting runs as a plain gray dot, not amber;
    // amber/spinning is reserved for something actually running.
    public static readonly Color InProgress = ColorTranslator.FromHtml("#DBAB0A");

    // Kept as a constant (rather than removed outright) so DwmHelper.ApplyDarkTitleBar and
    // MainForm.ApplyWindowChrome's border-color pick don't need their own call sites changed --
    // both just always get the dark-mode answer now.
    public const bool IsDark = true;

    // Font wraps a native GDI font handle and is IDisposable, but a Control never takes ownership
    // of (or disposes) a Font assigned to it -- by design, since more than one control can share
    // the same instance. UiFont/MonoFont used to hand back a brand-new Font on every single call,
    // and every Build* method in MainForm/the picker dialogs calls one of these at least once --
    // RebuildUi() alone (every DPI change) fires 20-30+ of them, none ever released. The actual
    // set of distinct (family, size, style) combinations used across the
    // whole app is small and fixed (a handful of sizes, regular or bold), so caching by that key
    // and handing back the same instance every time turns an unbounded leak into a fixed, tiny,
    // one-time allocation -- safe to never dispose these, since nothing else in the app disposes
    // a Font it was merely assigned, either.
    private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> _fontCache = new();

    private static Font GetOrCreateFont(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (!_fontCache.TryGetValue(key, out var font))
        {
            font = new Font(family, size, style);
            _fontCache[key] = font;
        }
        return font;
    }

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => GetOrCreateFont("Segoe UI", size, style);
    public static Font MonoFont(float size, FontStyle style = FontStyle.Regular) => GetOrCreateFont("Consolas", size, style);

    public static Color OutcomeColor(BuildOutcome outcome) => outcome switch
    {
        BuildOutcome.Success => Success,
        BuildOutcome.Regression => Danger,
        // Matches GitHub's own Actions-tab convention -- a red X for any failure, whether it's a
        // fresh regression or one that's been broken a while. The "just broke vs. still broken"
        // distinction still comes through in the outcome label text (Regression / Still failing).
        BuildOutcome.Failed => Danger,
        BuildOutcome.InProgress => InProgress,
        BuildOutcome.Queued => Muted,
        BuildOutcome.Pending => Regression,
        // GitHub shows both of these as plain gray, same as Queued -- neither is a verdict on
        // the code, just "this run never actually produced one."
        BuildOutcome.Cancelled => Muted,
        BuildOutcome.Skipped => Muted,
        _ => Muted,
    };

    public static string OutcomeLabel(BuildOutcome outcome) => outcome switch
    {
        BuildOutcome.Success => "Success",
        BuildOutcome.Regression => "Regression",
        BuildOutcome.Failed => "Still failing",
        BuildOutcome.InProgress => "In progress",
        BuildOutcome.Queued => "Queued",
        BuildOutcome.Pending => "Needs approval",
        BuildOutcome.Cancelled => "Cancelled",
        BuildOutcome.Skipped => "Skipped",
        _ => "Unknown",
    };
}
