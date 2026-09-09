using Discord;
using Discord.WebSocket;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class FavouritesLiveNoti
{
    // the watchlist itself lives in Data/favourites.json (seeded on first run),
    // edited there to add or remove streamers. the Twitch bot reads the same
    // file to auto-shoutout favourited streamers when they first type in chat.

    readonly EventSubWebsocketClient _eventSubClient;
    readonly DiscordSocketClient _discordSocket;
    readonly TwitchApiService _twitchClient;
    readonly FavouritesData _data;

    public FavouritesLiveNoti(
        EventSubWebsocketClient eventSubClient,
        DiscordSocketClient discordSocket,
        TwitchApiService twitchClient,
        FavouritesData data)
    {
        _eventSubClient = eventSubClient;
        _discordSocket  = discordSocket;
        _twitchClient   = twitchClient;
        _data           = data;

        // hook into the shared EventSub websocket, same connection Twitch_Notifier uses
        _eventSubClient.WebsocketConnected += OnWebsocketConnected;
        _eventSubClient.StreamOnline        += OnStreamOnline;

        Logger.Log("[FavNoti] Constructed — watching for: " + string.Join(", ", _data.Entries.Keys));
    }


    //-----WEBSOCKET CONNECTED-----
    // subscribe to stream.online for every streamer in the watchlist.
    // fires on initial connect only, reconnects reuse existing subscriptions.
    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (e.IsRequestedReconnect) return;

        // clean up any leftover subscriptions from the previous session
        // before creating new ones, otherwise we'll hit the 10-sub limit
        try
        {
            var existing = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.EventSub.GetEventSubSubscriptionsAsync(
                    type: "stream.online"  // only fetch the type we care about
                )
            );

            foreach (var sub in existing.Subscriptions)
            {
                // only delete stream.online subs that belong to FavNoti
                // identified by broadcaster_user_id not being your own channel
                if (sub.Type == "stream.online" && 
                    sub.Condition.TryGetValue("broadcaster_user_id", out string? uid) &&
                    uid != Config.TwitchUserId)
                {
                    await _twitchClient.ExecuteAsync(
                        TwitchProfile.Broadcaster,
                        api => api.Helix.EventSub.DeleteEventSubSubscriptionAsync(sub.Id)
                    );
                    Logger.Log($"[FavNoti] Cleaned up stale subscription: {sub.Id}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavNoti] Failed to clean up old subscriptions: {ex.Message}");
        }
        
        // fetch broadcaster user ids for each name in the watchlist
        foreach (string username in _data.Entries.Keys)
        {
            try
            {
                // look up the Twitch user id for this username
                var userResult = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.Users.GetUsersAsync(
                        null,
                        new List<string> { username }
                    )
                );

                if (userResult?.Users == null || userResult.Users.Length == 0)
                {
                    Logger.Log($"[FavNoti] Could not find Twitch user: {username}");
                    continue;
                }

                string userId = userResult.Users[0].Id;

                var result = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                        "stream.online",
                        "1",
                        new Dictionary<string, string> { { "broadcaster_user_id", userId } },
                        TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                        _eventSubClient.SessionId
                    )
                );

                Logger.Log(
                    $"[FavNoti] Subscription created: " +
                    $"{result.Subscriptions[0].Id}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[FavNoti] Failed to subscribe for {username}: {ex.Message}");
            }
        }
    }


    //-----STREAM ONLINE-----
    // fires when any subscribed channel goes live.
    // we match the broadcaster login name against the watchlist and post
    // the configured message if found.
    async Task OnStreamOnline(object? sender, StreamOnlineArgs args)
    {
        string broadcasterLogin = args.Payload.Event.BroadcasterUserLogin;

        // check if this broadcaster is in the watchlist
        if (!_data.Entries.TryGetValue(broadcasterLogin, out string? messageTemplate))
            return;

        Logger.Log($"[FavNoti] {broadcasterLogin} went live — fetching stream info");
        
        await Task.Delay(3000);
        
        try
        {
            string userName = "";
            string gameName = "";
            string thumbnail = "";
            string pfp = "";
            
           
            
            // fetch stream info so we can fill in game, thumbnail, username
            GetStreamsResponse? streamResult = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.Streams.GetStreamsAsync(
                    null, 1, null, null, null,
                    new List<string> { broadcasterLogin }
                )
            );
            
            if (streamResult?.Streams?.Length > 0)
            {
                gameName = streamResult.Streams[0].GameName;
                thumbnail = streamResult.Streams[0].ThumbnailUrl
                                .Replace("{width}", "1920")
                                .Replace("{height}", "1080")
                            + $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                
            }

            GetUsersResponse? usersResponse = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.Users.GetUsersAsync(
                    null, new List<string> { broadcasterLogin }
                )
            );

            if (usersResponse?.Users?.Length > 0)
            {
                userName = usersResponse.Users[0].DisplayName;
                pfp = usersResponse.Users[0].ProfileImageUrl;
            }
            
            // build the message and replace placeholders
            string message = messageTemplate.Replace("{game}", gameName, StringComparison.OrdinalIgnoreCase);

            // resolve the notification channel
            ITextChannel? channel = _discordSocket.GetChannel(Config.FavouritesNotifyChannelId) as ITextChannel;

            if (channel == null)
            {
                Logger.Log($"[FavNoti] Could not find channel ID {Config.FavouritesNotifyChannelId}");
                return;
            }

            Embed embed = BuildLiveEmbed(userName, pfp, message, $"https://www.twitch.tv/{broadcasterLogin}", thumbnail);

            await channel.SendMessageAsync(embed: embed);
            Logger.Log($"[FavNoti] Posted notification for {broadcasterLogin} to #{channel.Name}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavNoti] Failed to post notification for {broadcasterLogin}: {ex.Message}");
        }
    }
    
    
    //-----EMBED BUILDER-----
    // builds either a live embed or an offline embed depending on stream state.
    Embed BuildLiveEmbed(
        string userName,
        string pfp,
        string customMsg,
        string url,
        string thumbnailUrl
    )
    {
        EmbedBuilder builder = new EmbedBuilder();
        
            builder
                .WithAuthor($"AETHER-OS // {userName}'s Proxy is Active", pfp, url)
                .WithDescription("**---------------------------------------------------------------------** \n\n" +customMsg + "\n\n" + $"[Click here to go spread the love 🫧]({url})" + "\n\n**---------------------------------------------------------------------**")
                .WithColor(new Color(0x5865F2))
                .WithFooter("System Active • 4/30/03, 3:00 AM")
                .WithThumbnailUrl(thumbnailUrl);
        

        return builder.Build();
    }
}