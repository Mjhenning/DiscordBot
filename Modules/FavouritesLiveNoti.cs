using Discord;
using Discord.WebSocket;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Stream;

namespace DiscordBot.Modules;

public class FavouritesLiveNoti
{
    // ─────────────────────────────────────────────────────────────────────────
    // CONFIGURATION — edit these to add/remove streamers
    //
    // Key:   Twitch username (lowercase)
    // Value: Message to post when they go live.
    //        Use {user} for their name and {game} for their current game.
    //        A link to their stream is always appended automatically.
    // ─────────────────────────────────────────────────────────────────────────
    
    static readonly Dictionary<string, string> WatchList = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Siigynn",   "Fox's favourite matcha obsessed herbalist is live!! Whether it's {game} or karaoke, she's always a blast to have around! 💚" },
        { "its_livinabox", "Go catch our favourite australian goober Livy, whether it's {game} or anything else, there's always a giggle to be shared! 🩷" },
        { "InnocentOfSin", "Definitely not a cult, but brother can this owl yap! 🧡 Go checkout the amazing sin and his sussy but lovely community!"},
        { "BaxxyCH", "Go catch our lovely family from next door, the baxxidents!!! 💜 Make sure to keep up with their chaotic energy on {game}!"},
        { "LaeliaTheCat", "Fox's favourite chef star kitty is live with {game}!!! 🌟 Make sure to go pop in and say hi!!"},
        {"Juliuskat", "Our Finish Feline from next door is live with {game}! Go show the katpack some love! 🤍"}
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Channel to post notifications in — add this to Config.cs:
    //     public const ulong FavouritesNotifyChannelId = YOUR_CHANNEL_ID;
    // ─────────────────────────────────────────────────────────────────────────

    readonly EventSubWebsocketClient _eventSubClient;
    readonly DiscordSocketClient _discordSocket;
    readonly TokenManager _tokenManager;
    readonly TwitchClient _twitchClient;

    public FavouritesLiveNoti(
        EventSubWebsocketClient eventSubClient,
        DiscordSocketClient discordSocket,
        TokenManager tokenManager,
        TwitchClient twitchClient)
    {
        _eventSubClient = eventSubClient;
        _discordSocket  = discordSocket;
        _tokenManager   = tokenManager;
        _twitchClient   = twitchClient;

        // Hook into the shared EventSub websocket — same connection Twitch_Notifier uses
        _eventSubClient.WebsocketConnected += OnWebsocketConnected;
        _eventSubClient.StreamOnline        += OnStreamOnline;

        Logger.Log("[FavNoti] Constructed — watching for: " + string.Join(", ", WatchList.Keys));
    }


    // ─── WEBSOCKET CONNECTED ─────────────────────────────────────────────────
    // Subscribe to stream.online for every streamer in the watchlist.
    // Fires on initial connect only — reconnects reuse existing subscriptions.
    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (e.IsRequestedReconnect) return;

        // Fetch broadcaster user IDs for each name in the watchlist
        foreach (string username in WatchList.Keys)
        {
            try
            {
                // Look up the Twitch user ID for this username
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


    // ─── STREAM ONLINE ───────────────────────────────────────────────────────
    // Fires when any subscribed channel goes live.
    // We match the broadcaster login name against our watchlist and post
    // the configured message if found.
    async Task OnStreamOnline(object? sender, StreamOnlineArgs args)
    {
        string broadcasterLogin = args.Payload.Event.BroadcasterUserLogin;

        // Check if this broadcaster is in our watchlist
        if (!WatchList.TryGetValue(broadcasterLogin, out string? messageTemplate))
            return;

        Logger.Log($"[FavNoti] {broadcasterLogin} went live — fetching stream info");
        
        
        try
        {
            string userName = "";
            string gameName = "";
            string thumbnail = "";
            string pfp = "";
            
           
            
            // Fetch stream info so we can fill in game, thumbail, username
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
                thumbnail = streamResult.Streams[0].ThumbnailUrl;
                
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
            
            // Build the message — replace placeholders
            string message = messageTemplate.Replace("{game}", gameName, StringComparison.OrdinalIgnoreCase);

            // Resolve the notification channel
            ITextChannel? channel = _discordSocket.GetChannel(Config.FavouritesNotifyChannelId) as ITextChannel;

            if (channel == null)
            {
                Logger.Log($"[FavNoti] Could not find channel ID {Config.FavouritesNotifyChannelId}");
                return;
            }

            Embed embed = BuildLiveEmbed(userName, pfp, message, $"https://www.twitch.tv/{userName}", thumbnail);

            await channel.SendMessageAsync(embed: embed);
            Logger.Log($"[FavNoti] Posted notification for {broadcasterLogin} to #{channel.Name}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavNoti] Failed to post notification for {broadcasterLogin}: {ex.Message}");
        }
    }
    
    
        // ── EMBED BUILDER ────────────────────────────────────────────────────────
    // Builds either a live embed or an offline embed depending on stream state.
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