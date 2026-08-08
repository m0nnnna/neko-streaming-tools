using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;

namespace NekoChat.Core.YouTube;

/// <summary>
/// OAuth2 Authorization Code + PKCE via Google.Apis.Auth's built-in installed-app
/// helper — it opens the user's browser and runs a local loopback listener for the
/// redirect itself, no manual PKCE/redirect-server code needed on our end. If a
/// valid token is already in the data store, this returns near-instantly with no
/// browser popup at all.
/// </summary>
public static class YouTubeAuthenticator
{
    public static readonly string[] Scopes = [YouTubeService.Scope.YoutubeReadonly];

    public static Task<UserCredential> AuthorizeAsync(string clientId, string clientSecret, IDataStore dataStore, CancellationToken ct = default)
    {
        var clientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret };
        return GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets, Scopes, "user", ct, dataStore);
    }
}
