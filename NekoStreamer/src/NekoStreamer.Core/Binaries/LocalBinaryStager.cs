using System.Security.Cryptography;

namespace NekoStreamer.Core.Binaries;

/// <summary>
/// Vendored tool binaries (mediamtx.exe, etc.) can live on a network-mapped project
/// drive, but Windows/SMB frequently refuses to execute binaries from network shares.
/// This copies a binary to a local, writable directory before it's ever launched.
/// </summary>
public static class LocalBinaryStager
{
    public static string StageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NekoStreamer", "bin");

    public static string Stage(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Vendored binary not found.", sourcePath);

        Directory.CreateDirectory(StageDirectory);

        var destPath = Path.Combine(StageDirectory, Path.GetFileName(sourcePath));

        if (File.Exists(destPath) && SameContent(sourcePath, destPath))
            return destPath;

        var tempPath = destPath + ".tmp";
        File.Copy(sourcePath, tempPath, overwrite: true);
        File.Move(tempPath, destPath, overwrite: true);

        return destPath;
    }

    private static bool SameContent(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);
        if (infoA.Length != infoB.Length)
            return false;

        return Hash(a).SequenceEqual(Hash(b));
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(stream);
    }
}
