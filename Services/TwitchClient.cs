using TwitchLib.Api;

namespace DiscordBot.Modules;

public class TwitchClient
{
    private readonly TokenManager _tokenManager;
    private readonly TwitchAPI _api;

    public TwitchClient(TokenManager tokenManager, TwitchAPI api)
    {
        _tokenManager = tokenManager;
        _api = api;
    }

    public async Task<T> ExecuteAsync<T>(
        TwitchProfile profile,
        Func<TwitchAPI, Task<T>> action)
    {
        return await ExecuteInternal(profile, action, retry: true);
    }

    private async Task<T> ExecuteInternal<T>(
        TwitchProfile profile,
        Func<TwitchAPI, Task<T>> action,
        bool retry)
    {
        var token = await _tokenManager.GetValidAccessTokenAsync(profile);

        try
        {
            // IMPORTANT: clone API per call (prevents race conditions)
            var api = CloneApi(token);

            return await action(api);
        }
        catch (Exception ex) when (retry && IsAuthError(ex))
        {
            Logger.Log("[TwitchClient] auth failure, refreshing token...");

            await _tokenManager.ForceRefreshAsync(profile);

            return await ExecuteInternal(profile, action, retry: false);
        }
    }

    private TwitchAPI CloneApi(string token)
    {
        var api = new TwitchAPI
        {
            Settings =
            {
                ClientId = Config.TwitchClientId,
                AccessToken = token
            }
        };

        return api;
    }

    private bool IsAuthError(Exception ex)
    {
        // still simple, but safer fallback
        return ex.Message.Contains("401") ||
               ex.Message.Contains("Unauthorized") ||
               ex.Message.Contains("invalid token");
    }
}