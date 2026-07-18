namespace SharpEmuUpdater;

public static class Logger
{
    public static event Action<string>? OnLog;

    private static readonly object Lock = new();

    public static void Log(string message)
    {
        // DateTime.Now is already the machine's local time zone. The date/time FORMAT used to be
        // a hardcoded "yyyy-MM-dd h:mm:ss tt" (always US-style date order, always 12-hour/AM-PM)
        // regardless of the user's actual Windows region settings -- someone using, say, a
        // dd/MM/yyyy or 24-hour-clock region would see a format that doesn't match anything else
        // on their system. "G" is .NET's culture-sensitive general date/long-time pattern --
        // ToString()'s own default -- so this now reads however dates/times normally read on
        // this machine, whatever that is, while still including seconds like the old format did.
        string line = $"[{DateTime.Now:G}] {message}";
        try
        {
            lock (Lock)
            {
                AppPaths.EnsureDataDir();
                // File.AppendAllText opens with a fairly restrictive share mode -- if another
                // process has the log file open at the same moment (most plausibly a second,
                // leftover instance of this same app), every write from here throws and used to
                // get silently swallowed below with no trace anywhere, even though the on-screen
                // Activity Log (driven by OnLog below, independent of whether this disk write
                // succeeds) kept looking completely normal. FileShare.ReadWrite makes that
                // specific conflict far less likely to happen in the first place.
                using var stream = new FileStream(AppPaths.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            // Logging must never crash the app -- but a silent failure here is exactly what made
            // a prior occurrence of this (the disk file going stale for 45 minutes while the
            // on-screen log kept updating normally) impossible to diagnose after the fact. Debug
            // output costs nothing when nobody's attached to it, and gives something to look at
            // (via DebugView or an attached debugger) if this ever happens again.
            System.Diagnostics.Debug.WriteLine($"Logger: failed to write to {AppPaths.LogFile}: {ex}");
        }
        OnLog?.Invoke(line);
    }

    /// <summary>
    /// This app is meant to be left running (or restarted and left running again) for days or
    /// weeks at a time, and Log() only ever appends -- with nothing capping it, activity.log would
    /// grow without bound for as long as the app has ever existed on a given machine. Called once
    /// at startup rather than checked on every Log() call, so normal logging never pays the cost
    /// of a filesystem stat just to check whether trimming is needed.
    /// </summary>
    public static void TrimLogFileIfTooLarge(long maxBytes = 5 * 1024 * 1024)
    {
        try
        {
            var info = new FileInfo(AppPaths.LogFile);
            if (!info.Exists || info.Length <= maxBytes) return;

            // Keeping roughly the newer half is simple and cheap -- a log file that's grown this
            // large only matters for "what happened recently," not preserving every line back to
            // whenever it was first created.
            string[] lines = File.ReadAllLines(AppPaths.LogFile);
            File.WriteAllLines(AppPaths.LogFile, lines[(lines.Length / 2)..]);
        }
        catch
        {
            // Same reasoning as Log() itself -- trimming the log must never be the thing that
            // crashes the app.
        }
    }
}
