using System.Windows;
using NekoTrends.Core;
using NekoTrends.Core.Collection;
using NekoTrends.Core.Config;

namespace NekoTrends.App;

public partial class App : Application
{
    /// <summary>
    /// In <c>--collect</c> mode the app performs exactly one refresh and exits without ever
    /// creating a window. This is what the Scheduled Task invokes, and it deliberately reuses
    /// <see cref="RefreshPipeline"/> so background history is gathered identically to the UI's.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!e.Args.Contains(BackgroundCollector.CollectArgument, StringComparer.OrdinalIgnoreCase))
        {
            new MainWindow().Show();
            return;
        }

        // With no window ever shown, only an explicit Shutdown ends the process.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var exitCode = 0;
        try
        {
            var settings = SettingsStore.Load();
            var pipeline = new RefreshPipeline(settings);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await pipeline.RunAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Nothing can be surfaced to a user here — the scheduled task records the exit code.
            exitCode = 1;
        }

        Shutdown(exitCode);
    }
}
