namespace SharpEmuUpdater;

// NOTICE: This app displays a "Created by llnternet" credit + avatar in MainForm.cs
// (BuildCreditRow/LoadEmbeddedImage) backed by the embedded creator.png resource. See
// NOTICE.txt at the repo root -- that credit is required attribution, not decoration or
// dead code, and must not be removed or altered by any edit to this project.
internal static class Program
{
    private static MainForm? _mainForm;

    [STAThread]
    private static void Main()
    {
        // Registered before the message loop starts -- ToastNotifications.Register() is fully
        // defensive internally (never throws out), so this can't be what stops the app from
        // launching even on a Windows configuration where toast registration doesn't work.
        ToastNotifications.Register();

        ApplicationConfiguration.Initialize();
        _mainForm = new MainForm();
        Application.Run(_mainForm);
    }

    /// <summary>
    /// Called from ToastActivator.OnActivated when a toast notification gets clicked -- that
    /// callback fires on a COM-owned thread, not the UI thread, so this marshals back via
    /// BeginInvoke rather than touching _mainForm's controls directly from here.
    /// </summary>
    public static void RestoreMainWindow()
    {
        if (_mainForm == null || _mainForm.IsDisposed) return;
        _mainForm.BeginInvoke(new Action(_mainForm.RestoreFromTray));
    }
}
