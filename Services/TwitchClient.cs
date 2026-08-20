using TwitchLib.Api;

namespace DiscordBot.Modules;

public class TwitchApiService
{
    readonly TwitchAPI _api;
    readonly TokenManager _tokens;

    public TwitchApiService(TwitchAPI api, TokenManager tokens)
    {
        _api = api;
        _tokens = tokens;

        _api.Settings.ClientId = Config.TwitchClientId;
    }

    public async Task<T> ExecuteAsync<T>(
        TwitchProfile profile,
        Func<TwitchAPI, Task<T>> action)
    {
        _api.Settings.AccessToken =
            await _tokens.GetValidAccessTokenAsync(profile);

        return await action(_api);
    }
}