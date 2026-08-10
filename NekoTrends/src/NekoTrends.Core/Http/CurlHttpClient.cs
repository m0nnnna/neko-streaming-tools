using System.Diagnostics;

namespace NekoTrends.Core.Http;

/// <summary>
/// Shells out to Windows' bundled curl.exe instead of .NET's HttpClient — carried over
/// from NekoChat, where this was confirmed empirically against Cloudflare-protected
/// endpoints: HttpClient gets reliably 403'd while curl succeeds seconds apart on the
/// same machine, a TLS-fingerprint-level distinction rather than a header check.
/// curl.exe ships with Windows 10 1803+ and Windows 11, so this needs no extra install.
///
/// The right User-Agent is endpoint-specific and NOT simply "pretend to be a browser":
/// Kick's endpoints want an explicit browser UA. Pass null to use curl's default.
/// </summary>
internal static class CurlHttpClient
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public static async Task<string> GetStringAsync(
        string url, CancellationToken ct, string? userAgent = null, int timeoutSeconds = 20)
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
        startInfo.ArgumentList.Add("--max-time");
        startInfo.ArgumentList.Add(timeoutSeconds.ToString());
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
