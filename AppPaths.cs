namespace SharpEmuUpdater;

public static class AppPaths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SharpEmuUpdater");

    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string LogFile => Path.Combine(DataDir, "activity.log");

    // Plain-text token file: checked next to the exe first, then in the app data folder. This is
    // the drop-in/onboarding interface only -- TokenFileStore.Load() encrypts whichever of these
    // it finds into TokenFileEncrypted and deletes the plaintext copy, so neither of these paths
    // should exist on disk for long in normal use.
    public static string TokenFileNextToExe => Path.Combine(AppContext.BaseDirectory, "token.txt");
    public static string TokenFileInAppData => Path.Combine(DataDir, "token.txt");

    // Windows-DPAPI-encrypted token, current-user-scoped -- see TokenFileStore. Only ever
    // decryptable by the same Windows user account on the same machine it was encrypted on;
    // copying this file elsewhere (a different PC, a different user account) is useless to an
    // attacker without also compromising that specific account's own Windows login.
    public static string TokenFileEncrypted => Path.Combine(DataDir, "token.dat");

    // Default install root, used the first time the app runs. The user can change this
    // via AppState.InstallDir (see MainForm's "Install location" picker) -- everywhere else
    // in the app reads _state.InstallDir instead of this constant directly.
    public static readonly string DefaultInstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "SharpEmu");

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);

    /// <summary>
    /// Filesystem-safe folder name for a given (owner, branch) pair -- e.g. "galvesribeiro-gpu-
    /// backend-abstraction". Owner is included because branch names collide across contributors
    /// far more often than not (lots of people's own-fork default branch is just "main" too), so
    /// branch alone isn't unique enough to be a folder name on its own.
    /// </summary>
    public static string BranchFolderName(string owner, string branch)
    {
        string raw = $"{owner}-{branch}";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '-' : c).ToArray();
        return new string(chars);
    }
}
