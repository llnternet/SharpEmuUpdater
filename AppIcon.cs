namespace SharpEmuUpdater;

/// <summary>
/// Caches the exe's own icon -- Icon.ExtractAssociatedIcon does real disk I/O and icon-resource
/// parsing, and every Form in this app (MainForm, plus every dialog it opens: ForkPickerForm,
/// BuildPickerForm, ChangelogForm) was independently re-extracting the exact same icon from the
/// exact same path, on every single open. Dialogs especially can be opened and closed many times
/// in one session.
///
/// Each caller gets its own Clone() rather than sharing one Icon instance directly -- it's
/// ambiguous whether Form disposes its own Icon on Form.Dispose(), and dialogs have independent
/// lifetimes from MainForm (a picker's `using`-scoped disposal when it closes must never be able
/// to invalidate MainForm's still-live icon, or a later dialog's). A Clone() is cheap; the actual
/// expensive part (disk read + icon parsing) still only ever happens once.
/// </summary>
internal static class AppIcon
{
    private static Icon? _cached;
    private static bool _attempted;

    public static Icon? TryGetClone()
    {
        if (!_attempted)
        {
            _attempted = true;
            try { _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _cached = null; }
        }
        return _cached == null ? null : (Icon)_cached.Clone();
    }
}
