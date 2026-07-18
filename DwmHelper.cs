using System.Runtime.InteropServices;

namespace SharpEmuUpdater;

/// <summary>
/// Shared DWM helper so every top-level window in this app -- not just MainForm's own
/// custom-chrome one -- matches Theme.IsDark. ForkPickerForm/BuildPickerForm use a normal
/// FormBorderStyle.Sizable window with a native OS-drawn title bar, which otherwise just follows
/// Windows' own default (usually light) regardless of how dark the dialog's own body content is
/// -- exactly the mismatched light-title-bar-on-a-dark-dialog look that prompted this.
/// </summary>
internal static class DwmHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void ApplyDarkTitleBar(IntPtr handle, bool isDark)
    {
        int value = isDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
