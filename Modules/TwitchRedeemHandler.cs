using Discord;
using Discord.WebSocket;
using DiscordBot.Redeems;
using Newtonsoft.Json;
using TwitchLib.Api;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace DiscordBot.Modules;

public class TwitchRedeemHandler
{
    readonly EventSubWebsocketClient _eventSubClient;
    readonly DiscordSocketClient _discordSocket;
    readonly TwitchAPI _twitchApi;
    readonly StreamWriter _log;
    readonly HttpClient _http = new() { BaseAddress = new Uri("https://id.twitch.tv/oauth2/token") };

    readonly Dictionary<string, Func<RedemptionContext, Task>> _handlers;

    void Log(string msg)
    {
        Console.WriteLine(msg);
        _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }

    public TwitchRedeemHandler(EventSubWebsocketClient eventSubClient, DiscordSocketClient discordSocket, StreamWriter log, TwitchAPI twitchApi)
    {
        _twitchApi      = twitchApi;
        _eventSubClient = eventSubClient;
        _discordSocket  = discordSocket;
        _log            = log;

        // ── Register redeems here ──────────────────────────────────────────
        _handlers = new Dictionary<string, Func<RedemptionContext, Task>>
        {
            { Config.SuggestRewardId, SuggestionRedeem.Handle },
            { Config.QuoteRewardId, QuoteRedeem.Handle },
        };

        _eventSubClient.WebsocketConnected                          += OnWebsocketConnected;
        _discordSocket.InteractionCreated                           += OnInteractionCreated;
    }

    public async Task StartAsync()
    {
        FormUrlEncodedContent request = new(new[]
        {
            new KeyValuePair<string, string>("client_id",     Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("grant_type",    "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", Config.BroadcasterRefreshToken),
        });

        try
        {
            HttpResponseMessage response = await _http.PostAsync(_http.BaseAddress, request);
            string json = await response.Content.ReadAsStringAsync();
            TwitchTokenResponse? parsed = JsonConvert.DeserializeObject<TwitchTokenResponse>(json);
            string token = parsed?.AccessToken ?? "";

            if (string.IsNullOrWhiteSpace(token))
            {
                Log("[Error] TwitchRedeemHandler: failed to fetch token");
                return;
            }

            _twitchApi.Settings.AccessToken = token;
            Log("[Info] TwitchRedeemHandler token fetched: success");
        }
        catch (Exception ex)
        {
            Log($"[Error] TwitchRedeemHandler token fetch failed: {ex.Message}");
        }
    }

    // ── Subscriptions ──────────────────────────────────────────────────────

    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (e.IsRequestedReconnect) return;

        foreach (string rewardId in _handlers.Keys)
        {
            try
            {
                // Subscribe to redemption updated (fulfilled/cancelled)
                await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "channel.channel_points_custom_reward_redemption.update",
                    "1",
                    new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", Config.TwitchUserId },
                        { "reward_id",           rewardId            }
                    },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId
                );

                Log($"[Info] Subscribed to redemption events for reward {rewardId}");
            }
            catch (Exception ex)
            {
                Log($"[Error] Subscription failed for reward {rewardId}: {ex.Message}");
            }
        }
    }
    

    // ── Redemption updated → route to handler ─────────────────────────────

    async Task OnRedemptionUpdated(object? sender, ChannelPointsCustomRewardRedemptionArgs args)
    {
        var redemption = args.Payload.Event;

        if (redemption.Status != "fulfilled") return;
        if (!_handlers.TryGetValue(redemption.Reward.Id, out Func<RedemptionContext, Task>? handler)) return;

        Log($"[Info] Routing fulfilled redemption — Reward: {redemption.Reward.Id}, User: {redemption.UserName}");

        try
        {
            string avatarUrl = "";

            try
            {
                var userResult = await _twitchApi.Helix.Users.GetUsersAsync(null, new List<string> { redemption.UserName });
                avatarUrl = userResult?.Users?.Length > 0 ? userResult.Users[0].ProfileImageUrl : "";
            }
            catch (Exception ex)
            {
                Log($"[Warning] Could not fetch avatar for {redemption.UserName}: {ex.Message}");
            }

            RedemptionContext ctx = new(
                redemption.UserName,
                redemption.UserInput,
                avatarUrl,
                $"https://www.twitch.tv/{redemption.UserName}",
                _discordSocket,
                _log
            );

            await handler(ctx);
        }
        catch (Exception ex)
        {
            Log($"[Error] Handler for reward {redemption.Reward.Id} failed: {ex.Message}");
        }
    }

    // ── Shared interaction handling ────────────────────────────────────────

    async Task OnInteractionCreated(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent component) return;

        if (component.Data.CustomId == "suggestion_complete")
            await SuggestionRedeem.OnMarkComplete(component);
    }
}