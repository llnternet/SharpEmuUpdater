using DiscordRPC;

namespace SharpEmuUpdater;

/// <summary>
/// Optional Discord Rich Presence integration -- shows the actively tracked fork/branch and
/// currently installed build (with platforms) on the user's Discord profile, if they've entered
/// their own Discord Application Client ID in the app. A Client ID is a public identifier (not a
/// secret), obtained by registering a free Application at discord.com/developers/applications --
/// this app never talks to Discord's web API directly, only the local IPC connection to a
/// running Discord client, which the DiscordRichPresence NuGet package (MIT-licensed, independent
/// of SharpEmu's own GPL-licensed Discord integration) implements.
///
/// Entirely opt-in and silent: no connection is attempted at all while the Client ID is blank,
/// and any failure (Discord not running, IPC hiccup, etc.) is swallowed -- this is a cosmetic
/// feature with no bearing on the app's actual function, so it must never surface an error.
/// </summary>
public sealed class DiscordPresenceManager : IDisposable
{
    private DiscordRpcClient? _client;
    private string _activeClientId = "";

    /// <summary>Reconnects to Discord under a new Client ID, or disconnects entirely if the given
    /// id is blank. No-ops if the id hasn't actually changed, so this is safe to call on every
    /// keystroke of a Settings text box without reconnecting on every character typed.</summary>
    public void UpdateClientId(string clientId)
    {
        clientId = clientId.Trim();
        if (clientId == _activeClientId) return;
        _activeClientId = clientId;

        _client?.Dispose();
        _client = null;

        if (clientId.Length == 0) return;

        try
        {
            _client = new DiscordRpcClient(clientId);
            _client.Initialize();
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord Rich Presence failed to initialize: {ex.Message}");
            _client = null;
        }
    }

    /// <summary>Updates the presence shown on Discord. buildLabel/platforms are null when no
    /// build has been installed yet for the currently tracked fork/branch.</summary>
    public void SetPresence(string forkLabel, string? buildLabel, BuildPlatforms? platforms)
    {
        if (_client is not { IsInitialized: true }) return;

        try
        {
            _client.SetPresence(new RichPresence
            {
                Details = forkLabel,
                State = buildLabel == null ? "No build installed yet" : $"{buildLabel}{FormatPlatformsSuffix(platforms)}",
                Timestamps = Timestamps.Now,
                Assets = new Assets
                {
                    LargeImageKey = "logo",
                    LargeImageText = "SharpEmu Updater",
                },
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Discord Rich Presence update failed: {ex.Message}");
        }
    }

    // Short 3-letter codes (matches BuildListRenderer's own list formatting), not full platform
    // names -- Discord's own profile card renders this at a fairly narrow fixed width and
    // truncates with "..." well before any of Discord's actual character limits kick in;
    // confirmed live against the user's own Discord profile ("Windows/macOS/..." was getting cut
    // off), not a hypothetical concern.
    private static string FormatPlatformsSuffix(BuildPlatforms? platforms)
    {
        if (platforms is not { } p || p == BuildPlatforms.None) return "";
        var parts = new List<string>();
        if (p.HasFlag(BuildPlatforms.Windows)) parts.Add("Win");
        if (p.HasFlag(BuildPlatforms.MacOS)) parts.Add("Mac");
        if (p.HasFlag(BuildPlatforms.Linux)) parts.Add("Lnx");
        return parts.Count > 0 ? $" · {string.Join("/", parts)}" : "";
    }

    public void Dispose() => _client?.Dispose();
}
