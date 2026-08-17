using Newtonsoft.Json;
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
    // LOAD FROM ENV (INITIAL TOKENS)
    // ─────────────────────────────

    public void LoadFromEnvironment()
    {
        LoadTokenFromEnv(TwitchProfile.Broadcaster, "TWITCH_BROADCASTER_TOKEN");
        LoadTokenFromEnv(TwitchProfile.Bot, "TWITCH_BOT_TOKEN");
    }

    private void LoadTokenFromEnv(TwitchProfile profile, string envKey)
    {
        var key = Key(profile);
        string? token = Environment.GetEnvironmentVariable(envKey);

        if (string.IsNullOrWhiteSpace(token))
            return;

        if (_tokens.TryGetValue(key, out var existing) && !IsExpired(existing))
            return;

        _tokens[key] = new TwitchTokenSet
        {
            AccessToken = token,
            RefreshToken = "",
            ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
        };

        SaveToFile();

        if (profile == TwitchProfile.Broadcaster)
            _twitchApi.Settings.AccessToken = token;

        Logger.Log($"[TokenManager] Loaded {profile} token from environment");
    }
}