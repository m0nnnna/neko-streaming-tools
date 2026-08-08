using System.Diagnostics;

namespace NekoStreamer.Core.Processes;

/// <summary>
/// Thin wrapper around a long-running child process (mediamtx, ffmpeg) with
/// redirected output capture and a graceful-then-forceful stop sequence.
/// </summary>
public sealed class ManagedProcess : IDisposable
{
    // Shared across every ManagedProcess so a crash of our own process can never
    // leave an ffmpeg still connected to Twitch/Kick, or mediamtx still squatting
    // on the RTMP/API ports and blocking the next launch.
    private static readonly ChildProcessJob SharedJob = new();

    private readonly Process _process;
    private readonly object _logLock = new();
    private readonly List<string> _recentLog = new();
    private const int MaxRecentLogLines = 200;

    public event Action<string>? OutputReceived;

    /// <summary>Fires when the process exits for any reason, including on its own
    /// (e.g. ffmpeg losing its RTMP source) — not just when we asked it to stop.</summary>
    public event Action? Exited;

    public bool IsRunning => !_process.HasExited;

    public int Id => _process.Id;

    private ManagedProcess(Process process)
    {
        _process = process;
    }

    public static ManagedProcess Start(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var managed = new ManagedProcess(process);

        process.OutputDataReceived += (_, e) => managed.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => managed.OnLine(e.Data);
        process.Exited += (_, _) => SafeInvoke(managed.Exited);

        process.Start();
        SharedJob.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return managed;
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    /// <summary>
    /// Signals EOF on the process's stdin without touching the process itself.
    /// Some ffmpeg input modes (e.g. reading pipe:0) never finalize/flush output
    /// until they see EOF, so a graceful stop needs this before waiting on exit.
    /// </summary>
    public void CloseStandardInput()
    {
        if (!_process.HasExited)
            _process.StandardInput.Close();
    }

    public IReadOnlyList<string> RecentLog
    {
        get { lock (_logLock) return _recentLog.ToList(); }
    }

    public async Task StopAsync(TimeSpan gracePeriod)
    {
        if (_process.HasExited)
            return;

        try
        {
            _process.CloseMainWindow();
        }
        catch
        {
            // process may not have a message loop; fall through to kill on timeout
        }

        using var cts = new CancellationTokenSource(gracePeriod);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>Terminates immediately, no grace period. For processes with nothing
    /// meaningful to flush on exit (the ingest writer, mediamtx) — waiting around
    /// for them just stalls Stop with no benefit.</summary>
    public void Kill()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
    }

    private void OnLine(string? line)
    {
        if (line is null)
            return;

        lock (_logLock)
        {
            _recentLog.Add(line);
            if (_recentLog.Count > MaxRecentLogLines)
                _recentLog.RemoveAt(0);
        }

        SafeInvoke(() => OutputReceived?.Invoke(line));
    }

    /// <summary>
    /// Process output/exit events fire on the .NET thread pool. An unhandled
    /// exception there — e.g. from a subscriber further up the chain marshaling
    /// onto a WPF Dispatcher that's mid-shutdown — crashes the entire app, not
    /// just the callback. Nothing a downstream handler does should be able to
    /// take the whole program down.
    /// </summary>
    private static void SafeInvoke(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch
        {
            // deliberately swallowed — see remarks above
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
