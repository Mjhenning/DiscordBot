using Discord;
using Discord.WebSocket;
using System.Timers;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using DiscordBot.Data;
using Timer = System.Timers.Timer;

namespace DiscordBot.Modules;

public class FavouritesLiveNoti
{
    // polls the api instead of eventsub subscriptions. each stream.online
    // subscription costs one point of the websocket's 10-point budget, and
    // the watchlist has outgrown it. one GetStreams call covers the whole
    // list at once, so the list can keep growing for free.

    const double PollIntervalMs = 60_000;

    readonly DiscordSocketClient _discordSocket;
    readonly TwitchApiService _twitchClient;
    readonly FavouritesData _data;

    // logins known to be live right now, case-insensitive. seeded on the
    // first poll so a streamer already live at boot doesn't instantly fire
    // a notification.
    HashSet<string> _live = new(StringComparer.OrdinalIgnoreCase);
    bool _seeded;

    readonly SemaphoreSlim _pollLock = new(1, 1);
    Timer? _timer;

    public FavouritesLiveNoti(
        DiscordSocketClient discordSocket,
        TwitchApiService twitchClient,
        FavouritesData data)
    {
        _discordSocket = discordSocket;
        _twitchClient  = twitchClient;
        _data          = data;

        Logger.Log("[FavNoti] Constructed. watching for: " + string.Join(", ", _data.Entries.Keys));

        // sweep stream.online subs left by the old watcher so the websocket
        // budget stays clear for the main notifier
        _ = CleanupStaleSubscriptionsAsync();

        _timer = new Timer(PollIntervalMs)
        {
            AutoReset = true
        };
        _timer.Elapsed += async (_, _) => await PollAsync();
        _timer.Start();
    }


    //-----STALE SUB CLEANUP-----
    // deletes fav stream.online subscriptions created by the pre-polling
    // era of this service. runs once on construction, best effort.
    async Task CleanupStaleSubscriptionsAsync()
    {
        try
        {
            var existing = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.EventSub.GetEventSubSubscriptionsAsync(
                    type: "stream.online"
                )
            );

            foreach (var sub in existing.Subscriptions)
            {
                // only delete stream.online subs that aren't for your own
                // channel, those belong to the old FavNoti watcher
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
    }


    //-----POLL-----
    // one GetStreams call fetches live state for every favourite at once,
    // then diffs against the last known state to detect new go-lives.
    async Task PollAsync()
    {
        // skip if the previous poll is still running
        if (!_pollLock.Wait(0)) return;

        try
        {
            GetStreamsResponse? streamResult = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.Streams.GetStreamsAsync(
                    null, 100, null, null, null,
                    _data.Entries.Keys.ToList()
                )
            );

            HashSet<string> nowLive = new(StringComparer.OrdinalIgnoreCase);
            if (streamResult?.Streams != null)
            {
                foreach (var stream in streamResult.Streams)
                    nowLive.Add(stream.UserLogin);
            }

            // first poll records who is already live and notifies for them,
            // otherwise a reboot silently swallows everyone mid-stream
            if (!_seeded)
            {
                _live = new HashSet<string>(nowLive, StringComparer.OrdinalIgnoreCase);
                _seeded = true;
                Logger.Log($"[FavNoti] Seeded with {_live.Count} favourite(s) already live");

                foreach (string login in nowLive)
                    await PostLiveNotificationAsync(login);

                return;
            }

            // diff: anyone live now but not before just came online
            foreach (string login in nowLive)
            {
                if (!_live.Contains(login))
                    await PostLiveNotificationAsync(login);
            }

            // replace the tracked set with this poll's snapshot so the users
            // who just posted are remembered and offline ones are dropped
            _live = nowLive;
        }
        catch (Exception ex)
        {
            Logger.Log($"[FavNoti] Poll failed: {ex.Message}");
        }
        finally
        {
            _pollLock.Release();
        }
    }


    //-----POST LIVE NOTIFICATION-----
    // builds and sends the themed go-live embed for a broadcaster.
    async Task PostLiveNotificationAsync(string broadcasterLogin)
    {
        if (!_data.Entries.TryGetValue(broadcasterLogin, out string? messageTemplate))
            return;

        Logger.Log($"[FavNoti] {broadcasterLogin} went live. fetching stream info");

        // give twitch a moment to finish registering the stream start
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
    // builds the themed go-live embed.
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