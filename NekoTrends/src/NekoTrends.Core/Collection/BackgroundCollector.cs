using System.Diagnostics;
using System.Text;

namespace NekoTrends.Core.Collection;

/// <summary>
/// Registers a Windows Scheduled Task that runs the app in <c>--collect</c> mode on the refresh
/// interval, so viewer history keeps accumulating while the app is closed.
///
/// This matters more than it looks: momentum compares a channel against its own recent baseline,
/// and a baseline built only from the hours the app happened to be open is both sparse and biased
/// toward the user's own timezone. Sampling on a schedule is what makes the Trending tab trustworthy.
///
/// Driven through schtasks.exe rather than the TaskScheduler COM API to avoid taking a dependency
/// on the interop assembly for what is three commands.
/// </summary>
public static class BackgroundCollector
{
    public const string TaskName = "NekoTrends Collector";

    /// <summary>The switch that puts the app into headless single-refresh mode.</summary>
    public const string CollectArgument = "--collect";

    public static bool IsRegistered() => RunSchtasks($"/query /tn \"{TaskName}\"").Success;

    /// <summary>
    /// Registers (or replaces) the task. Runs only when the user is logged on, which is what
    /// keeps DPAPI able to decrypt the stored tokens — they're scoped to the current user.
    /// </summary>
    public static (bool Success, string Message) Register(int intervalMinutes)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return (false, "Could not determine the running executable's path.");

        // schtasks caps /mo for MINUTE at 1439 (just under a day).
        var minutes = Math.Clamp(intervalMinutes, 10, 1439);

        var arguments = new StringBuilder()
            .Append("/create /f ")
            .Append($"/tn \"{TaskName}\" ")
            .Append($"/tr \"\\\"{exePath}\\\" {CollectArgument}\" ")
            .Append("/sc MINUTE ")
            .Append($"/mo {minutes} ")
            .Append("/it")
            .ToString();

        var result = RunSchtasks(arguments);

        return result.Success
            ? (true, $"Collecting every {minutes} minutes in the background.")
            : (false, $"Could not register the task: {result.Output}");
    }

    public static (bool Success, string Message) Unregister()
    {
        var result = RunSchtasks($"/delete /f /tn \"{TaskName}\"");

        return result.Success
            ? (true, "Background collection stopped.")
            : (false, $"Could not remove the task: {result.Output}");
    }

    private static (bool Success, string Output) RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return (false, "schtasks.exe did not start.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (process.ExitCode == 0, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
