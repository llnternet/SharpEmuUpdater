namespace SharpEmuUpdater;

/// <summary>This app's own current version -- bump this alongside every new GitHub release
/// published to llnternet/SharpEmuUpdater (tagged "vX.Y" there). Compared against that repo's
/// own releases by SelfUpdateChecker so a running copy can notice when a newer version of
/// SharpEmu Updater itself has been published.</summary>
public static class AppVersion
{
    public const string Current = "1.6";
}
