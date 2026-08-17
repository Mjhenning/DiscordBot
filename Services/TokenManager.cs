using Newtonsoft.Json;
using System.Net;
using TwitchLib.Api;

namespace DiscordBot.Modules;

public enum TwitchProfile
{
    Bot,
    Broadcaster
}

public class TwitchTokenSet
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public long ExpiresAt { get; set; }
}

public class TwitchTokenResponse
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
}

public class TokenManager
{
    private readonly string _path;
    private readonly TwitchAPI _twitchApi;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Dictionary<string, TwitchTokenSet> _tokens = new();

    public TokenManager(TwitchAPI api, string tokenFilePath)
    {
        _twitchApi = api;
        _path = tokenFilePath;

        _http = new HttpClient
        {
            BaseAddress = new Uri("https://id.twitch.tv/oauth2/token")
        };

        LoadFromFile();
    }

    // ─────────────────────────────
    // CORE PUBLIC API (THIS IS ALL YOU USE NOW)
    // ─────────────────────────────

    public async Task<string> GetAccessTokenAsync(TwitchProfile profile)
    {
        await EnsureValid(profile);

        return _tokens[Key(profile)].AccessToken;
    }

    // ─────────────────────────────
    // VALIDATION CORE
    // ─────────────────────────────

    private async Task EnsureValid(TwitchProfile profile)
    {
        var key = Key(profile);

        if (!_tokens.TryGetValue(key, out var token))
        {
            token = new TwitchTokenSet();
            _tokens[key] = token;
        }

        if (!IsExpired(token))
            return;

        await Refresh(profile);
    }

    private bool IsExpired(TwitchTokenSet token)
    {
        if (token.ExpiresAt == 0)
            return true;

        // refresh 5 min early
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() > token.ExpiresAt - 300;
    }

    // ─────────────────────────────
    // REFRESH (ONLY WHEN NEEDED)
    // ─────────────────────────────

    internal async Task Refresh(TwitchProfile profile)
    {
        await _lock.WaitAsync();

        try
        {
            var key = Key(profile);

            if (!_tokens.TryGetValue(key, out var token))
            {
                token = new TwitchTokenSet();
                _tokens[key] = token;
            }

            var request = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", Config.TwitchClientId),
                new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", token.RefreshToken),
            });

            var response = await _http.PostAsync("", request);
            var json = await response.Content.ReadAsStringAsync();

            var data = JsonConvert.DeserializeObject<TwitchTokenResponse>(json);

            if (string.IsNullOrWhiteSpace(data?.AccessToken))
            {
                Logger.Log($"[TokenManager] refresh failed: {profile}");
                return;
            }

            token.AccessToken = data.AccessToken;
            token.RefreshToken = data.RefreshToken;
            token.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + data.ExpiresIn;

            SaveToFile();

            if (profile == TwitchProfile.Broadcaster)
                _twitchApi.Settings.AccessToken = data.AccessToken;

            Logger.Log($"[TokenManager] refreshed: {profile}");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─────────────────────────────
    // AUTO-RECOVERY WRAPPER (IMPORTANT)
    // ─────────────────────────────

    public async Task<T> WithTokenRetryAsync<T>(
        TwitchProfile profile,
        Func<string, Task<T>> action)
    {
        var token = await GetAccessTokenAsync(profile);

        try
        {
            return await action(token);
        }
        catch (Exception ex) when (IsAuthError(ex))
        {
            Logger.Log("[TokenManager] token expired, retrying...");

            await Refresh(profile);

            token = await GetAccessTokenAsync(profile);

            return await action(token);
        }
    }

    private bool IsAuthError(Exception ex)
    {
        return ex.Message.Contains("401") ||
               ex.Message.Contains("Unauthorized");
    }

    // ─────────────────────────────
    // FILE IO
    // ─────────────────────────────

    private static string Key(TwitchProfile profile) => profile.ToString();

    private void LoadFromFile()
    {
        if (!File.Exists(_path))
        {
            _tokens = new();
            return;
        }

        var json = File.ReadAllText(_path);

        _tokens = JsonConvert.DeserializeObject<Dictionary<string, TwitchTokenSet>>(json)
                  ?? new();
    }

    private void SaveToFile()
    {
        File.WriteAllText(
            _path,
            JsonConvert.SerializeObject(_tokens, Formatting.Indented)
        );
    }
    
    // ─────────────────────────────
    // HELPERS
    // ─────────────────────────────
    
    public async Task<string> GetValidAccessTokenAsync(TwitchProfile profile)
    {
        await EnsureValid(profile);
        return _tokens[Key(profile)].AccessToken;
    }

    public async Task ForceRefreshAsync(TwitchProfile profile)
    {
        await Refresh(profile);
    }

    // ─────────────────────────────
    // INITIAL OAUTH AUTHORIZATION
    // ─────────────────────────────

    private const int AuthPort = 17563;
    private const string RedirectUri = "http://localhost:17563/callback";

    private static readonly string[] RequiredScopes =
    [
        "analytics:read:extensions",
        "analytics:read:games",
        "bits:read",
        "channel:edit:commercial",
        "channel:manage:broadcast",
        "channel:manage:moderators",
        "channel:manage:polls",
        "channel:manage:predictions",
        "channel:manage:redemptions",
        "channel:manage:schedule",
        "channel:manage:videos",
        "channel:moderate",
        "channel:read:editors",
        "channel:read:goals",
        "channel:read:hype_train",
        "channel:read:polls",
        "channel:read:predictions",
        "channel:read:redemptions",
        "channel:read:stream_key",
        "channel:read:subscriptions",
        "chat:edit",
        "chat:read",
        "clips:edit",
        "moderation:read",
        "moderator:manage:announcements",
        "moderator:manage:automod",
        "moderator:manage:banned_users",
        "moderator:manage:blocked_terms",
        "moderator:manage:chat_messages",
        "moderator:read:chatters",
        "moderator:read:followers",
        "user:edit",
        "user:edit:broadcast",
        "user:manage:blocked_users",
        "user:manage:whispers",
        "user:read:blocked_users",
        "user:read:broadcast",
        "user:read:email",
        "user:read:follows",
        "user:read:subscriptions",
        "whispers:read"
    ];

    public bool HasValidTokens(TwitchProfile profile)
    {
        var key = Key(profile);
        if (!_tokens.TryGetValue(key, out var token))
            return false;
        return !IsExpired(token) && !string.IsNullOrWhiteSpace(token.RefreshToken);
    }

    public async Task AuthorizeAsync(TwitchProfile profile)
    {
        string scope = string.Join("+", RequiredScopes);
        string state = Guid.NewGuid().ToString("N");

        string authUrl =
            $"https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={Config.TwitchClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&response_type=code" +
            $"&scope={scope}" +
            $"&state={state}";

        Logger.Log($"[TokenManager] Open this URL to authorize the {profile} account:");
        Logger.Log($"[TokenManager] {authUrl}");

        string? authCode = null;

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{AuthPort}/");
        listener.Start();

        Logger.Log($"[TokenManager] Waiting for authorization on port {AuthPort}...");

        try
        {
            var context = await listener.GetContextAsync();
            var code = context.Request.QueryString["code"];
            var returnedState = context.Request.QueryString["state"];

            string responseHtml;
            if (code != null && returnedState == state)
            {
                authCode = code;
                responseHtml = "<html><body><h2>Authorized! You can close this tab.</h2></body></html>";
                Logger.Log($"[TokenManager] Authorization code received for {profile}");
            }
            else
            {
                responseHtml = "<html><body><h2>Authorization failed. Close this tab and try again.</h2></body></html>";
                Logger.Log($"[TokenManager] Authorization failed for {profile}");
            }

            var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }

        if (authCode == null)
        {
            Logger.Log($"[TokenManager] No authorization code received for {profile}. Skipping.");
            return;
        }

        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("code", authCode),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
        });

        var response = await _http.PostAsync("", request);
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonConvert.DeserializeObject<TwitchTokenResponse>(json);

        if (string.IsNullOrWhiteSpace(data?.AccessToken))
        {
            Logger.Log($"[TokenManager] Token exchange failed for {profile}: {json}");
            return;
        }

        var key = Key(profile);
        _tokens[key] = new TwitchTokenSet
        {
            AccessToken = data.AccessToken,
            RefreshToken = data.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + data.ExpiresIn
        };

        SaveToFile();

        if (profile == TwitchProfile.Broadcaster)
            _twitchApi.Settings.AccessToken = data.AccessToken;

        Logger.Log($"[TokenManager] Authorized and saved: {profile}");
    }
}