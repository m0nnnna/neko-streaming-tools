using System.Diagnostics;

namespace NekoChat.Core.Http;

/// <summary>
/// Shells out to Windows' bundled curl.exe instead of .NET's HttpClient. Confirmed
/// empirically against two unrelated Cloudflare/bot-protected endpoints that
/// HttpClient gets reliably 403'd while curl succeeds consistently seconds apart
/// on the same machine — a TLS-fingerprint-level distinction, not a header check.
/// curl.exe ships with Windows 10 1803+ and Windows 11, so this needs no extra
/// install.
///
/// The right User-Agent is endpoint-specific and NOT just "pretend to be a
/// browser": Kick's endpoint needs an explicit browser UA, while Liberapay's
/// Cloudflare specifically flags a browser UA arriving over curl's fingerprint as
/// MORE suspicious than curl's own honest default UA — confirmed empirically for
/// both. Pass null to use curl's default UA.
/// </summary>
internal static class CurlHttpClient
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public static async Task<string> GetStringAsync(string url, CancellationToken ct, string? userAgent = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-s");
        if (userAgent is not null)
        {
            startInfo.ArgumentList.Add("-H");
            startInfo.ArgumentList.Add($"User-Agent: {userAgent}");
        }
        startInfo.ArgumentList.Add(url);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("curl.exe did not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not run curl.exe. It ships with Windows 10/11 by default — check it's on PATH.", ex);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask;
                throw new InvalidOperationException($"curl failed calling '{url}' (exit {process.ExitCode}): {stderr}");
            }

            return stdout;
        }
    }
}
