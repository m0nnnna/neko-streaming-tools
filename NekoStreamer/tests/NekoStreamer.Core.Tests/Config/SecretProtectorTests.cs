using NekoStreamer.Core.Config;

namespace NekoStreamer.Core.Tests.Config;

public class SecretProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        const string secret = "sk_live_super_secret_stream_key";

        var protectedValue = SecretProtector.Protect(secret);
        var recovered = SecretProtector.Unprotect(protectedValue);

        Assert.Equal(secret, recovered);
        Assert.NotEqual(secret, protectedValue);
    }
}
