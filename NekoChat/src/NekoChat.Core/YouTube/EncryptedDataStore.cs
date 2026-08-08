using System.Text.Json;
using Google.Apis.Util.Store;
using NekoChat.Core.Config;

namespace NekoChat.Core.YouTube;

/// <summary>
/// Google's auth library persists OAuth tokens itself via IDataStore — its default
/// FileDataStore writes plaintext JSON to disk. This backs it with the same DPAPI
/// encryption every other saved credential in NekoChat uses instead.
/// </summary>
public sealed class EncryptedDataStore : IDataStore
{
    private readonly string _directory;

    public EncryptedDataStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(LocalPaths.DataDirectory, "youtube-tokens");
        Directory.CreateDirectory(_directory);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var encrypted = SecretProtector.Protect(json);
        File.WriteAllText(PathFor(key), encrypted);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return Task.FromResult(default(T)!);

        var encrypted = File.ReadAllText(path);
        var json = SecretProtector.Unprotect(encrypted);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
    }

    public Task ClearAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_directory))
            File.Delete(file);
        return Task.CompletedTask;
    }

    private string PathFor(string key) => Path.Combine(_directory, $"{Uri.EscapeDataString(key)}.dat");
}
