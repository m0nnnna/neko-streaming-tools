namespace NekoStreamer.Core.Binaries;

/// <summary>
/// Locates vendored tool files (mediamtx.exe, its config) relative to wherever the
/// app is actually running from. Walks upward from the app's base directory to
/// support running via `dotnet run` from within the repo during development, and
/// falls back to a path alongside the executable for a packaged deployment where
/// tools/config are copied next to the app.
/// </summary>
public static class VendoredTools
{
    public static string FindMediaMtxExe() => Find(Path.Combine("tools", "mediamtx", "mediamtx.exe"));

    public static string FindMediaMtxConfig() => Find(Path.Combine("config", "mediamtx.yml"));

    private static string Find(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }
}
