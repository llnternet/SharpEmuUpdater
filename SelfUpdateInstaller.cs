using System.Diagnostics;
using System.IO.Compression;

namespace SharpEmuUpdater;

/// <summary>
/// Downloads a new SharpEmu Updater release and installs it over the currently running exe.
/// Windows won't let a running exe overwrite itself, so this follows the standard self-update
/// pattern: download the new exe to a temp location, hand off to a small detached PowerShell
/// script that waits for this process to actually exit, swaps the file, and relaunches it, then
/// this process exits normally. The handoff script and downloaded files clean up after
/// themselves once the swap completes.
/// </summary>
public static class SelfUpdateInstaller
{
    /// <summary>Downloads the release zip, extracts the exe from it, and stages the relaunch
    /// script -- everything that can still fail with a clear error message shown to the user.
    /// Returns the path to the staged new exe; call LaunchSwapAndRestart with it once the caller
    /// is ready to actually exit and hand off.</summary>
    public static async Task<string> DownloadAndStageAsync(SelfUpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"SharpEmuUpdaterUpdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, "update.zip");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SharpEmuUpdater/1.0");
            using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength ?? (info.DownloadSizeBytes > 0 ? info.DownloadSizeBytes : null);
            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(zipPath);

            if (progress == null || totalBytes is not > 0)
            {
                await httpStream.CopyToAsync(fileStream, ct);
            }
            else
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    progress.Report((double)totalRead / totalBytes.Value);
                }
            }
        }

        ZipFile.ExtractToDirectory(zipPath, tempDir);
        string newExePath = Path.Combine(tempDir, "SharpEmuUpdater.exe");
        if (!File.Exists(newExePath))
            throw new InvalidOperationException("Downloaded update did not contain SharpEmuUpdater.exe.");

        return newExePath;
    }

    /// <summary>Launches the detached swap-and-relaunch script, then the caller should exit
    /// immediately (ExitApplication) so the script's wait for this process to end doesn't sit
    /// there for its full timeout. The script deletes itself and the downloaded temp files once
    /// the swap completes -- nothing is left behind on a successful update.</summary>
    public static void LaunchSwapAndRestart(string newExePath)
    {
        string currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the currently running exe's path.");
        int currentPid = Environment.ProcessId;

        string scriptPath = Path.Combine(Path.GetTempPath(), $"SharpEmuUpdaterSwap_{Guid.NewGuid():N}.ps1");
        string tempDir = Path.GetDirectoryName(newExePath)!;

        // Waits for the current process to actually exit (up to 30s), then copies the new exe
        // over the old one and relaunches it. -Confirm:$false / -Force everywhere since this runs
        // fully unattended -- there is no user present to answer a prompt. Cleans up the
        // downloaded temp folder and its own script file afterward regardless of outcome, so a
        // failed swap doesn't leave stray files behind either.
        string script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            try { Wait-Process -Id {{currentPid}} -Timeout 30 } catch {}
            Start-Sleep -Milliseconds 500
            Copy-Item -Path "{{newExePath}}" -Destination "{{currentExePath}}" -Force
            Start-Process -FilePath "{{currentExePath}}"
            Start-Sleep -Milliseconds 500
            Remove-Item -Path "{{tempDir}}" -Recurse -Force
            Remove-Item -Path "{{scriptPath}}" -Force
            """;
        File.WriteAllText(scriptPath, script);

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath },
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }
}
