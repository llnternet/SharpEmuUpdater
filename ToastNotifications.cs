using Microsoft.Toolkit.Uwp.Notifications;

namespace SharpEmuUpdater;

/// <summary>
/// Real Windows Action Center toast notifications for new builds. NotifyIcon.ShowBalloonTip
/// (still used elsewhere in this app) already renders with the same visual toast style on
/// Windows 10+, but never gets logged to Action Center history -- it just disappears once
/// dismissed or timed out, with no way to find it again if you missed it. A real toast via this
/// package persists there until you clear it.
///
/// Uses the newer ToastNotificationManagerCompat.OnActivated event rather than the older
/// NotificationActivator/COM-GUID pattern -- simpler, and for a plain Win32 app like this one it
/// needs no Start Menu shortcut to work.
///
/// Fully defensive by design: registration or display failing (a locked-down Windows
/// configuration, a missing component, whatever) must never surface as a crash or even a visible
/// error -- the existing tray balloon already carries the same information independently, so
/// toast support here is purely additive.
/// </summary>
public static class ToastNotifications
{
    private static bool _registered;

    public static void Register()
    {
        try
        {
            // Fires on a COM-owned thread, not the UI thread -- RestoreMainWindow marshals back
            // via BeginInvoke itself, so nothing extra is needed here.
            ToastNotificationManagerCompat.OnActivated += _ => Program.RestoreMainWindow();
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public static void ShowNewBuild(string title, string message)
    {
        if (!_registered) return;
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            // See the class doc comment -- a toast failing to show is not something the rest of
            // the app needs to know or care about.
        }
    }
}
