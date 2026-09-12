using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using NekoStreamer.Core;
using NekoStreamer.Core.Config;
using NekoStreamer.Remote.Obs;
using NekoStreamer.Remote.VTubeStudio;
using QRCoder;

// The QR banner uses Unicode block characters — Windows consoles default to a legacy
// codepage that mangles them into "?" boxes unless we opt into UTF-8 explicitly.
Console.OutputEncoding = Encoding.UTF8;

var settings = RemoteSettingsStore.Load();
RemoteSettingsStore.Save(settings); // persist a freshly-generated AccessToken on first run

var obs = new ObsClient();
var vts = new VtsClient();

// Content root must not depend on the launching process's working directory
// (double-click from Explorer, a shortcut, Task Scheduler, etc. all differ).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();
app.UseStaticFiles(new StaticFileOptions
{
    // This UI gets iterated on; a phone caching a stale app.js is a worse problem than
    // re-fetching a few KB on every load. This only stops FUTURE caching though — see
    // the versioned index.html below for busting whatever a phone already cached under
    // the old, header-less responses from before this existed.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-store",
});

// Serve index.html with a cache-busting ?v= on its script/style tags instead of as a
// plain static file. Cache-Control alone can't force a phone to re-fetch app.js under
// the exact same URL it already cached — the URL has to actually change.
var assetVersion = File.GetLastWriteTimeUtc(Path.Combine(app.Environment.WebRootPath, "app.js")).Ticks;
var indexHtml = File.ReadAllText(Path.Combine(app.Environment.WebRootPath, "index.html"))
    .Replace("app.js\"", $"app.js?v={assetVersion}\"")
    .Replace("style.css\"", $"style.css?v={assetVersion}\"");
app.MapGet("/", (HttpResponse res) =>
{
    res.Headers.CacheControl = "no-store";
    return Results.Text(indexHtml, "text/html");
});

bool Authorized(HttpRequest req) =>
    !settings.RequireAccessToken ||
    (req.Headers.TryGetValue("X-Access-Token", out var token) && token == settings.AccessToken);

app.MapGet("/api/status", (HttpRequest req) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    return Results.Ok(new
    {
        obsConnected = obs.IsConnected,
        currentScene = obs.CurrentScene,
        vtsConnected = vts.IsConnected,
        vtsAuthenticated = vts.IsAuthenticated,
    });
});

app.MapGet("/api/obs/scenes", async (HttpRequest req, CancellationToken ct) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    if (!obs.IsConnected) return Results.Json(new { error = "obs-disconnected" }, statusCode: 503);
    try
    {
        var (current, scenes) = await obs.GetSceneListAsync(ct);
        return Results.Ok(new { current, scenes });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapPost("/api/obs/scenes/{name}", async (string name, HttpRequest req, CancellationToken ct) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    if (!obs.IsConnected) return Results.Json(new { error = "obs-disconnected" }, statusCode: 503);
    try
    {
        await obs.SetCurrentSceneAsync(name, ct);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapGet("/api/vts/hotkeys", async (HttpRequest req, CancellationToken ct) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    if (!vts.IsConnected) return Results.Json(new { error = "vts-disconnected" }, statusCode: 503);
    if (!vts.IsAuthenticated) return Results.Json(new { error = "vts-not-authenticated" }, statusCode: 503);
    try
    {
        var hotkeys = await vts.GetHotkeysAsync(ct);
        return Results.Ok(hotkeys);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapPost("/api/vts/hotkeys/{id}", async (string id, HttpRequest req, CancellationToken ct) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    if (!vts.IsConnected || !vts.IsAuthenticated) return Results.Json(new { error = "vts-unavailable" }, statusCode: 503);
    try
    {
        await vts.TriggerHotkeyAsync(id, ct);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

var supervisorCts = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
_ = Task.Run(() => ObsSupervisorLoop(obs, settings, supervisorCts.Token));
_ = Task.Run(() => VtsSupervisorLoop(vts, settings, supervisorCts.Token));

// Only announce a token/QR once Kestrel has actually bound the port — otherwise, if
// another instance is already holding it, this one prints a QR for a token that never
// goes live, while the phone silently keeps talking to the older instance instead.
try
{
    await app.StartAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Could not start on port {settings.Port}: {ex.Message}");
    Console.WriteLine("Is another copy of NekoStreamer Remote already running? Close it (check for");
    Console.WriteLine("other NekoStreamer.Remote.exe windows) and try again, or change \"Port\" in");
    Console.WriteLine($"{LocalPaths.RemoteSettingsFilePath} and restart.");
    Environment.Exit(1);
    return;
}

PrintStartupBanner(settings);
await app.WaitForShutdownAsync();
return;

static async Task ObsSupervisorLoop(ObsClient obs, RemoteSettings settings, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        if (!obs.IsConnected)
        {
            try
            {
                var password = string.IsNullOrEmpty(settings.EncryptedObsPassword)
                    ? null
                    : SecretProtector.Unprotect(settings.EncryptedObsPassword);
                await obs.ConnectAsync(new Uri(settings.ObsUrl), password, ct);
                Console.WriteLine("[OBS] connected");
            }
            catch
            {
                // OBS probably isn't running yet — retry on the next tick
            }
        }

        try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { break; }
    }
}

static async Task VtsSupervisorLoop(VtsClient vts, RemoteSettings settings, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        if (!vts.IsConnected)
        {
            try
            {
                await vts.ConnectAsync(new Uri(settings.VtsUrl), ct);
                Console.WriteLine("[VTS] connected");
            }
            catch
            {
                // VTube Studio probably isn't running yet — retry on the next tick
            }
        }

        if (vts.IsConnected && !vts.IsAuthenticated)
        {
            try
            {
                var storedToken = string.IsNullOrEmpty(settings.EncryptedVtsToken)
                    ? null
                    : SecretProtector.Unprotect(settings.EncryptedVtsToken);

                if (storedToken is not null && await vts.AuthenticateAsync(storedToken, ct))
                {
                    Console.WriteLine("[VTS] authenticated with stored token");
                }
                else
                {
                    Console.WriteLine("[VTS] requesting plugin access — approve the popup inside VTube Studio");
                    var newToken = await vts.RequestAuthTokenAsync(ct);
                    settings.EncryptedVtsToken = SecretProtector.Protect(newToken);
                    RemoteSettingsStore.Save(settings);
                    await vts.AuthenticateAsync(newToken, ct);
                    Console.WriteLine("[VTS] authenticated with new token");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VTS] auth failed: {ex.Message}");
            }
        }

        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
    }
}

static void PrintStartupBanner(RemoteSettings settings)
{
    var detectedIps = GetLocalIPv4Addresses().ToList();
    var announceIp = string.IsNullOrWhiteSpace(settings.AnnounceIp) ? detectedIps.FirstOrDefault() : settings.AnnounceIp;

    Console.WriteLine("=== NekoStreamer Remote ===");
    Console.WriteLine($"Running: {Environment.ProcessPath}");
    Console.WriteLine($"Built:   {File.GetLastWriteTime(Environment.ProcessPath!):yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine($"PID:     {Environment.ProcessId}");
    Console.WriteLine();
    if (!settings.RequireAccessToken)
        Console.WriteLine("Pairing disabled (RequireAccessToken = false) — anyone on your LAN can use this.");
    else
        Console.WriteLine($"Pairing token: {settings.AccessToken}");
    Console.WriteLine();
    Console.WriteLine("Open this on your phone (same Wi-Fi as this PC):");
    foreach (var ip in detectedIps)
        Console.WriteLine(ip == announceIp ? $"  http://{ip}:{settings.Port}/  <- using this one" : $"  http://{ip}:{settings.Port}/");
    if (detectedIps.Count > 1)
    {
        Console.WriteLine($"Wrong one? Set \"AnnounceIp\": \"<correct-ip>\" in {LocalPaths.RemoteSettingsFilePath} and restart.");
    }
    Console.WriteLine();

    // The QR encodes the token in the URL so scanning it pairs the phone directly —
    // no typing or copy/paste needed. app.js picks up ?token= on first load.
    //
    // The trailing "_=<nonce>" is a cache-buster on the PAGE URL itself (not just
    // app.js) — a phone that already has *this exact address* cached from way earlier
    // in testing (before any Cache-Control header existed on it) would otherwise keep
    // serving that stale document forever, script tag and all, no matter what headers
    // the server sends now. A nonce guarantees a URL that has never been requested
    // before, so there's nothing to have cached.
    if (announceIp is not null)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var pairingUrl = settings.RequireAccessToken
            ? $"http://{announceIp}:{settings.Port}/?token={settings.AccessToken}&_={nonce}"
            : $"http://{announceIp}:{settings.Port}/?_={nonce}";
        Console.WriteLine($"Scan this from your phone's camera ({pairingUrl}):");
        var qrData = QRCodeGenerator.GenerateQrCode(pairingUrl, QRCodeGenerator.ECCLevel.Q);
        Console.WriteLine(new AsciiQRCode(qrData).GetGraphicSmall());
        Console.WriteLine();
    }
}

static IEnumerable<string> GetLocalIPv4Addresses()
{
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (ni.OperationalStatus != OperationalStatus.Up) continue;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

        foreach (var addr in ni.GetIPProperties().UnicastAddresses)
        {
            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                yield return addr.Address.ToString();
        }
    }
}
