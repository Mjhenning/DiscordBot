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
    readonly TokenManager _tokenManager;
    readonly TwitchClient _twitchClient;

    readonly Dictionary<string, Func<RedemptionContext, Task>> _handlers;

    public TwitchRedeemHandler(
        EventSubWebsocketClient eventSubClient,
        DiscordSocketClient discordSocket,
        TokenManager tokenManager,
        TwitchClient twitchClient)
    {
        _tokenManager   = tokenManager;
        _twitchClient   = twitchClient;
        _eventSubClient = eventSubClient;
        _discordSocket  = discordSocket;

        _handlers = new Dictionary<string, Func<RedemptionContext, Task>>
        {
            { Config.SuggestRewardId, SuggestionRedeem.Handle },
            { Config.QuoteRewardId,   QuoteRedeem.Handle },
        };

        _eventSubClient.WebsocketConnected                        += OnWebsocketConnected;
        _eventSubClient.ChannelPointsCustomRewardRedemptionUpdate += OnRedemptionUpdated;
        // OnInteractionCreated hook REMOVED — suggestion_complete is now a proper [ComponentInteraction]
    }

    // ── Subscriptions ──────────────────────────────────────────────────────

    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (e.IsRequestedReconnect) return;

        // Ensure token is valid before subscribing
        await _tokenManager.GetValidAccessTokenAsync(TwitchProfile.Broadcaster);

        foreach (string rewardId in _handlers.Keys)
        {
            try
            {
                var result = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                        "channel.channel_points_custom_reward_redemption.update",
                        "1",
                        new Dictionary<string, string>
                        {
                            { "broadcaster_user_id", Config.TwitchUserId },
                            { "reward_id",           rewardId            }
                        },
                        TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                        _eventSubClient.SessionId
                    )
                );

                Logger.Log(
                    $"[Info] Redeem subscription created: " +
                    $"{result.Subscriptions[0].Id}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[Error] Subscription failed for reward {rewardId}: {ex.Message}");
            }
        }
    }
    

    // ── Redemption updated → route to handler ─────────────────────────────

    async Task OnRedemptionUpdated(object? sender, ChannelPointsCustomRewardRedemptionArgs args)
    {
        var redemption = args.Payload.Event;

        if (redemption.Status != "fulfilled") return;
        if (!_handlers.TryGetValue(redemption.Reward.Id, out Func<RedemptionContext, Task>? handler)) return;

        Logger.Log($"[Info] Routing fulfilled redemption — Reward: {redemption.Reward.Id}, User: {redemption.UserName}");

        try
        {
            string avatarUrl = "";

            try
            {
                var userResult = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.Users.GetUsersAsync(null, new List<string> { redemption.UserName })
                );
                avatarUrl = userResult?.Users?.Length > 0 ? userResult.Users[0].ProfileImageUrl : "";
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] Could not fetch avatar for {redemption.UserName}: {ex.Message}");
            }

            RedemptionContext ctx = new(
                redemption.UserName,
                redemption.UserInput,
                avatarUrl,
                $"https://www.twitch.tv/{redemption.UserName}",
                _discordSocket
            );

            await handler(ctx);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Error] Handler for reward {redemption.Reward.Id} failed: {ex.Message}");
        }
    }
}