using System.Security.Cryptography;
using System.Text;

namespace NekoChat.Core.Config;

/// <summary>Encrypts OAuth tokens at rest using Windows DPAPI, scoped to the current user account.</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NekoChat.Secret.v1");

    public static string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string protectedBase64)
    {
        var encrypted = Convert.FromBase64String(protectedBase64);
        var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
