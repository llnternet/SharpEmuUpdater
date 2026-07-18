using System.Security.Cryptography;
using System.Text;

namespace SharpEmuUpdater;

/// <summary>
/// Loads the GitHub token, encrypted at rest via Windows DPAPI. token.txt (next to the exe, then
/// in the app data folder) is a plain-text DROP-IN interface only -- the first time Load() finds
/// one, it's encrypted into TokenFileEncrypted (DataProtectionScope.CurrentUser -- decryptable
/// only by this same Windows user account on this same machine) and the plaintext copy is deleted
/// immediately. Every later Load() (including a token *rotation*: edit token.txt again, click
/// "Reload Token from File") goes through that exact same one-time encrypt-then-delete step, so a
/// plaintext token.txt never lingers on disk for longer than it takes to notice and encrypt it.
/// </summary>
public static class TokenFileStore
{
    public static string? Load()
    {
        foreach (string plainPath in new[] { AppPaths.TokenFileNextToExe, AppPaths.TokenFileInAppData })
        {
            if (!File.Exists(plainPath)) continue;
            string token = File.ReadAllText(plainPath).Trim();
            if (string.IsNullOrEmpty(token)) continue;

            EncryptAndStore(token);
            // Deleted only after the encrypted copy is successfully written -- if EncryptAndStore
            // itself threw, this line never runs, so a failed encryption attempt can't destroy the
            // only copy of the token.
            File.Delete(plainPath);
            Logger.Log($"Encrypted the plain-text token found at {plainPath} and deleted the plain-text copy.");
            return token;
        }

        if (!File.Exists(AppPaths.TokenFileEncrypted)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(AppPaths.TokenFileEncrypted);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string token = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (Exception ex)
        {
            // Most likely cause: token.dat was copied from a different machine or a different
            // Windows user account -- DPAPI ties the encryption to exactly one (user, machine)
            // pair, so it's not decryptable anywhere else by design, not a bug. Logged rather than
            // silently swallowed so that specific, fixable case doesn't look like "no token found"
            // with no explanation.
            Logger.Log($"Could not decrypt the stored token ({ex.Message}). Drop a fresh token.txt " +
                "next to the exe to re-encrypt it for this machine/account.");
            return null;
        }
    }

    private static void EncryptAndStore(string token)
    {
        AppPaths.EnsureDataDir();
        byte[] plain = Encoding.UTF8.GetBytes(token);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.TokenFileEncrypted, encrypted);
    }

    /// <summary>Classic tokens (`ghp_...`, or a bare 40-hex-char legacy token predating GitHub's
    /// 2021 prefix scheme) and fine-grained tokens (`github_pat_...`) both authenticate exactly
    /// the same way here (a plain Bearer header, see GitHubUpdaterService's constructor) -- this
    /// is purely for the startup log line, so whichever type got loaded is visible without the
    /// user having to remember which kind they generated.</summary>
    public static string DescribeTokenType(string token) => token switch
    {
        _ when token.StartsWith("github_pat_", StringComparison.Ordinal) => "fine-grained personal access token",
        _ when token.StartsWith("ghp_", StringComparison.Ordinal) => "classic personal access token",
        _ when token.StartsWith("gho_", StringComparison.Ordinal) => "OAuth token",
        _ when token.StartsWith("ghu_", StringComparison.Ordinal) => "GitHub App user-to-server token",
        _ when token.StartsWith("ghs_", StringComparison.Ordinal) => "GitHub App server-to-server token",
        _ when token.Length == 40 && token.All(Uri.IsHexDigit) => "classic personal access token (legacy format)",
        _ => "token of an unrecognized format",
    };

    public static string MissingTokenMessage() =>
        $"No token found. Create one of these files with your GitHub token as plain text:\n" +
        $"  {AppPaths.TokenFileNextToExe}\n" +
        $"  {AppPaths.TokenFileInAppData}\n" +
        "It's encrypted automatically and the plain-text copy deleted the next time this app starts " +
        "(or you click 'Reload Token from File') -- see HOW TO SET UP YOUR TOKEN.txt.\n" +
        "Either a classic Personal Access Token (needs the 'repo' scope) or a fine-grained token " +
        "(needs 'Actions: Read-only' and 'Contents: Read-only' permissions, with this repository -- " +
        "and any fork you might Switch Fork to -- included in its repository access list) will work. " +
        "Listing builds works with almost any token, but downloading artifact zips is rejected (403) " +
        "without that specific access, even on public repos.";
}
