using TwitchLib.Api.Helix.Models.Videos.GetVideos;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.EventArgs.Stream;

namespace DiscordBot.Modules;
using Discord.WebSocket;
using Discord;

public class Twitch_Notifier
{
    readonly EventSubWebsocketClient _eventSubClient;
    readonly DiscordSocketClient _discordSocket;
    readonly TokenManager _tokenManager;
    readonly TwitchClient _twitchClient;

    static StreamSession TwitchSession = new();
    static TwitchVOD TwitchVOD = new();

    // int instead of bool so Interlocked can atomically check-and-set it,
    // preventing two updater loops from starting simultaneously
    private int _liveUpdaterRunning = 0;

    // Ensures only one thread reads or writes TwitchSession/TwitchVOD at a time
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    static readonly HttpClient HttpClient = new();


    // ── CONSTRUCTOR ──────────────────────────────────────────────────────────
    // Inject TokenManager + TwitchClient instead of raw TwitchAPI.
    // DI resolves these automatically because they're registered in Program.cs.
    public Twitch_Notifier(
        EventSubWebsocketClient eventSubClient,
        DiscordSocketClient discordSocket,
        TokenManager tokenManager,
        TwitchClient twitchClient)
    {
        _tokenManager   = tokenManager;
        _twitchClient   = twitchClient;
        _eventSubClient = eventSubClient;
        _discordSocket  = discordSocket;

        Logger.Log("[Info] Twitch_Notifier constructor called");
        Logger.Log($"[Info] Notifier EventSubClient hash: {_eventSubClient.GetHashCode()}");

        // ── Wire up EventSub lifecycle events ─────────────────────────────
        _eventSubClient.WebsocketConnected    += OnWebsocketConnected;
        _eventSubClient.WebsocketDisconnected += (s, e) =>
        {
            Logger.Log("[Warning] Notifier websocket disconnected");
            return Task.CompletedTask;
        };

        _eventSubClient.ErrorOccurred += (s, e) =>
        {
            Logger.Log("[Error] Notifier websocket error occurred");
            foreach (var prop in e.GetType().GetProperties())
                Logger.Log($"[Error]   {prop.Name}: {prop.GetValue(e)}");
            return Task.CompletedTask;
        };

        // ── Wire up stream event handlers ─────────────────────────────────
        _eventSubClient.StreamOnline  += OnStreamOnline;
        _eventSubClient.StreamOffline += OnStreamOffline;
        _eventSubClient.ChannelUpdate += OnChannelUpdate;

        Logger.Log("[Info] Twitch_Notifier constructor complete — all handlers attached");
    }


    // ── START ────────────────────────────────────────────────────────────────
    // Simplified: we just ask TokenManager for a valid token (it fetches +
    // stores it automatically), then connect the EventSub websocket.
    // The old GetUserToken() and manual token assignment are gone.
    public async Task StartAsync()
    {
        Logger.Log("[Info] Notifier StartAsync called");

        // Trigger initial token fetch/validation. TokenManager saves the token
        // to disk and updates it automatically before it expires from now on.
        var token = await _tokenManager.GetValidAccessTokenAsync(TwitchProfile.Broadcaster);

        Logger.Log($"[Info] Token ready ({token[..10]}...)");

        await _eventSubClient.ConnectAsync();
        Logger.Log("[Info] ConnectAsync returned");
    }


    // ── WEBSOCKET CONNECTED ──────────────────────────────────────────────────
    // Fires when the EventSub websocket connects (or reconnects).
    // We use TwitchClient.ExecuteAsync here so if the token happens to be
    // expired at subscription time, it's refreshed and retried automatically.
    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        Logger.Log($"[Info] Notifier OnWebsocketConnected fired — IsReconnect: {e.IsRequestedReconnect}, SessionId: {_eventSubClient.SessionId}");

        // On reconnect, Twitch re-uses existing subscriptions — no need to re-register
        if (!e.IsRequestedReconnect)
        {
            // ── stream.online ──────────────────────────────────────────────
            try
            {
                Logger.Log("[Info] Creating stream.online subscription...");

                await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                        "stream.online",
                        "1",
                        new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                        TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                        _eventSubClient.SessionId
                    )
                );

                Logger.Log("[Info] stream.online subscription created");
            }
            catch (Exception ex) { Logger.Log($"[Error] stream.online subscription failed: {ex.Message}"); }

            // ── stream.offline ─────────────────────────────────────────────
            try
            {
                Logger.Log("[Info] Creating stream.offline subscription...");

                var result = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                        "stream.offline",
                        "1",
                        new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                        TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                        _eventSubClient.SessionId
                    )
                );

                Logger.Log(
                    $"[Info] Subscription created: " +
                    $"{result.Subscriptions[0].Id}");
            }
            catch (Exception ex) { Logger.Log($"[Error] stream.offline subscription failed: {ex.Message}"); }

            // ── channel.update ─────────────────────────────────────────────
            try
            {
                Logger.Log("[Info] Creating channel.update subscription...");

                var result = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                        "channel.update",
                        "2",
                        new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                        TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                        _eventSubClient.SessionId
                    )
                );

                Logger.Log(
                    $"[Info] Subscription created: " +
                    $"{result.Subscriptions[0].Id}");
            }
            catch (Exception ex) { Logger.Log($"[Error] channel.update subscription failed: {ex.Message}"); }

            Logger.Log("[Info] Notifier OnWebsocketConnected complete");
        }
        else
        {
            Logger.Log("[Info] Reconnect detected — skipping subscription creation");
        }
    }


    // ── STREAM ONLINE ────────────────────────────────────────────────────────
    // Fires when Twitch detects the channel goes live.
    // TwitchClient.ExecuteAsync replaces the old try/catch Unauthorized blocks —
    // if the token is stale it refreshes once and retries transparently.
    async Task OnStreamOnline(object? sender, StreamOnlineArgs args)
    {
        Logger.Log("[Info] OnStreamOnline fired");

        try
        {
            // Fetch current stream info (title, game, viewer count, thumbnail)
            GetStreamsResponse? result = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.Streams.GetStreamsAsync(
                    null, 1, null, null, null,
                    new List<string> { Config.TwitchChannelName }
                )
            );

            Logger.Log($"[Info] GetStreams returned {result?.Streams?.Length ?? 0} stream(s)");

            if (result?.Streams == null || result.Streams.Length == 0)
            {
                Logger.Log("[Warning] OnStreamOnline: no streams returned");
                return;
            }

            // Fetch user info (avatar URL)
            GetUsersResponse? userResult = await _twitchClient.ExecuteAsync(
                TwitchProfile.Broadcaster,
                api => api.Helix.Users.GetUsersAsync(
                    null,
                    new List<string> { Config.TwitchChannelName }
                )
            );

            Logger.Log($"[Info] GetUsers returned {userResult?.Users?.Length ?? 0} user(s)");

            if (userResult?.Users == null || userResult.Users.Length == 0)
            {
                Logger.Log("[Warning] OnStreamOnline: no users returned");
                return;
            }

            // Lock session and populate it with fresh stream data
            await _sessionLock.WaitAsync();
            try
            {
                TwitchSession.TwitchAvatarUrl = userResult.Users[0].ProfileImageUrl;
                TwitchSession.UserId          = result.Streams[0].UserId;
                TwitchSession.CurrentlyLive   = true;
                TwitchSession.Title           = result.Streams[0].Title;
                TwitchSession.GameName        = result.Streams[0].GameName;
                TwitchSession.ThumbnailUrl    = result.Streams[0].ThumbnailUrl
                    .Replace("{width}", "1920")
                    .Replace("{height}", "1080");
                TwitchSession.ViewerCount     = result.Streams[0].ViewerCount;
                TwitchSession.StartedAt       = new DateTimeOffset(result.Streams[0].StartedAt, TimeSpan.Zero);
            }
            finally
            {
                _sessionLock.Release();
            }

            Logger.Log(
                $"[Info] Session populated — " +
                $"Title: {TwitchSession.Title}, " +
                $"Game: {TwitchSession.GameName}, " +
                $"Viewers: {TwitchSession.ViewerCount}"
            );

            await OnStreamReceived();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Error] OnStreamOnline failed: {ex}");
        }
    }


    // ── STREAM RECEIVED ──────────────────────────────────────────────────────
    // Posts the go-live embed to Discord and starts the live updater loop.
    async Task OnStreamReceived()
    {
        Logger.Log("[Info] OnStreamReceived called");

        ITextChannel? channel =
            _discordSocket.GetChannel(Config.TwitchNotifyChannelId) as ITextChannel
            ?? await _discordSocket.GetChannelAsync(Config.TwitchNotifyChannelId) as ITextChannel;

        if (channel == null)
        {
            Logger.Log($"[Warning] Could not find Twitch notify channel (ID: {Config.TwitchNotifyChannelId})");
            return;
        }

        Logger.Log($"[Info] Posting to #{channel.Name}");

        Embed embed = BuildTwitchEmbed(
            Config.TwitchChannelName,
            TwitchSession.CurrentlyLive,
            TwitchSession.TwitchAvatarUrl,
            TwitchSession.Title,
            TwitchSession.GameName,
            TwitchSession.ThumbnailUrl,
            "Just started.",
            TwitchSession.ViewerCount
        );

        IUserMessage posted = await channel.SendMessageAsync(text: "@everyone", embed: embed);

        await _sessionLock.WaitAsync();
        try
        {
            TwitchSession.PublishedChannelId = channel.Id;
            TwitchSession.PublishedMessageId = posted.Id;
        }
        finally { _sessionLock.Release(); }

        Logger.Log($"[Info] Notification published to #{channel.Name} (msg: {posted.Id})");

        // Atomically start the live updater loop — Interlocked prevents a
        // second stream.online event from spawning a duplicate loop
        if (Interlocked.CompareExchange(ref _liveUpdaterRunning, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try   { await StartLiveUpdates(); }
                finally { Interlocked.Exchange(ref _liveUpdaterRunning, 0); }
            });
        }
    }


    // ── STREAM OFFLINE ───────────────────────────────────────────────────────
    async Task OnStreamOffline(object? sender, StreamOfflineArgs args)
    {
        Logger.Log("[Info] OnStreamOffline fired");

        await _sessionLock.WaitAsync();
        try
        {
            if (!TwitchSession.CurrentlyLive)
            {
                Logger.Log("[Info] StreamOffline ignored — stream already offline");
                return;
            }

            TwitchSession.OfflineAt     = DateTimeOffset.UtcNow;
            TwitchSession.CurrentlyLive = false;
        }
        finally { _sessionLock.Release(); }

        Logger.Log("[Info] Updating embed to offline state...");
        await UpdateEmbed();

        // Wait for the live updater loop to fully exit before checking for VOD
        _ = Task.Run(async () =>
        {
            while (Interlocked.CompareExchange(ref _liveUpdaterRunning, 0, 0) == 1)
            {
                Logger.Log("[Info] Waiting for live updater to stop before resetting session...");
                await Task.Delay(500);
            }

            Logger.Log("[Info] Starting background VOD check...");
            await CheckIfVodUp();

            await _sessionLock.WaitAsync();
            try
            {
                TwitchSession = new StreamSession();
                TwitchVOD     = new TwitchVOD();
            }
            finally { _sessionLock.Release(); }

            Logger.Log("[Info] Session reset");
        });
    }


    // ── VOD CHECK ────────────────────────────────────────────────────────────
    // Called after stream goes offline. Tries to find the VOD and update the embed.
    async Task CheckIfVodUp()
    {
        string userId;

        await _sessionLock.WaitAsync();
        try   { userId = TwitchSession.UserId; }
        finally { _sessionLock.Release(); }

        // TwitchClient handles token refresh automatically if the call fails with 401
        GetVideosResponse? result = await _twitchClient.ExecuteAsync(
            TwitchProfile.Broadcaster,
            api => api.Helix.Videos.GetVideosAsync(null, userId, null, null, null, 1)
        );

        if (result?.Videos != null && result.Videos.Length > 0)
        {
            await _sessionLock.WaitAsync();
            try
            {
                TwitchVOD.Url      = result.Videos[0].Url;
                TwitchVOD.Duration = result.Videos[0].Duration;
            }
            finally { _sessionLock.Release(); }

            Logger.Log($"[Info] VOD found: {result.Videos[0].Url}");

            // Update the embed so viewers can see the VOD link
            await UpdateEmbed();
        }
    }


    // ── CHANNEL UPDATE ───────────────────────────────────────────────────────
    // Fires when the broadcaster changes their title or game mid-stream.
    async Task OnChannelUpdate(object? sender, ChannelUpdateArgs args)
    {
        Logger.Log($"[Info] OnChannelUpdate fired — Title: {args.Payload.Event.Title}, Game: {args.Payload.Event.CategoryName}");

        await _sessionLock.WaitAsync();
        try
        {
            TwitchSession.GameName = args.Payload.Event.CategoryName;
            TwitchSession.Title    = args.Payload.Event.Title;
        }
        finally { _sessionLock.Release(); }

        // Reflect the title/game change in the Discord embed immediately
        await UpdateEmbed();
    }


    // ── LIVE UPDATER LOOP ────────────────────────────────────────────────────
    // Polls every minute while the stream is live to update viewer count +
    // thumbnail. Also detects if the stream has gone offline unexpectedly.
    async Task StartLiveUpdates()
    {
        Logger.Log("[Info] StartLiveUpdates loop started");

        int missedStreamChecks = 0;

        while (true)
        {
            // Check if stream is still marked live before each poll
            bool live;
            await _sessionLock.WaitAsync();
            try   { live = TwitchSession.CurrentlyLive; }
            finally { _sessionLock.Release(); }

            if (!live) break;

            DateTimeOffset startTime = DateTimeOffset.UtcNow;

            try
            {
                // TwitchClient handles 401 retry automatically — no manual catch needed
                GetStreamsResponse? result = await _twitchClient.ExecuteAsync(
                    TwitchProfile.Broadcaster,
                    api => api.Helix.Streams.GetStreamsAsync(
                        null, 1, null, null, null,
                        new List<string> { Config.TwitchChannelName }
                    )
                );

                if (result?.Streams == null || result.Streams.Length == 0)
                {
                    // Stream may have just ended — give it 3 missed checks before forcing offline
                    missedStreamChecks++;
                    Logger.Log($"[Warning] StartLiveUpdates: GetStreams returned no results ({missedStreamChecks}/3)");

                    if (missedStreamChecks >= 3)
                    {
                        Logger.Log("[Warning] Stream assumed offline after 3 failed checks");

                        bool shouldTriggerOffline;
                        await _sessionLock.WaitAsync();
                        try   { shouldTriggerOffline = TwitchSession.CurrentlyLive; }
                        finally { _sessionLock.Release(); }

                        if (shouldTriggerOffline)
                        {
                            Logger.Log("[Info] Forcing offline event from StartLiveUpdates");
                            await OnStreamOffline(this, null!);
                        }

                        break;
                    }
                }
                else
                {
                    // Stream still live — update viewer count + thumbnail
                    missedStreamChecks = 0;

                    await _sessionLock.WaitAsync();
                    try
                    {
                        TwitchSession.ViewerCount  = result.Streams[0].ViewerCount;
                        TwitchSession.ThumbnailUrl = result.Streams[0].ThumbnailUrl
                            .Replace("{width}", "1920")
                            .Replace("{height}", "1080");
                    }
                    finally { _sessionLock.Release(); }
                }

                await UpdateEmbed();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Error] StartLiveUpdates failed: {ex}");
            }

            // Pace the loop to ~1 update per minute, accounting for elapsed time
            TimeSpan elapsed = DateTimeOffset.UtcNow - startTime;
            TimeSpan delay   = TimeSpan.FromMinutes(1) - elapsed;

            try
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }
            catch (TaskCanceledException)
            {
                Logger.Log("[Info] StartLiveUpdates delay cancelled");
                break;
            }
        }

        Logger.Log("[Info] StartLiveUpdates loop ended — stream no longer live");
    }


    // ── EMBED BUILDER ────────────────────────────────────────────────────────
    // Builds either a live embed or an offline embed depending on stream state.
    Embed BuildTwitchEmbed(
        string userName,
        bool live,
        string pfp,
        string title,
        string game,
        string thumbnailUrl,
        string streamDuration,
        int viewerCount           = 0,
        TwitchVOD? vod            = null,
        DateTimeOffset? timeOffline = null
    )
    {
        EmbedBuilder builder = new EmbedBuilder();

        if (live)
        {
            builder
                .WithAuthor($"{userName} is live on Twitch!", pfp, Config.TwitchChannelUrl)
                .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
                .AddField("Game",    $"> {game}",        true)
                .AddField("Viewers", $"> {viewerCount}", true)
                .WithColor(new Color(0x5865F2))
                .WithFooter(
                    streamDuration,
                    "https://images.icon-icons.com/4401/PNG/256/269414_twitch-icon.png"
                );

            // Add cache-busted thumbnail so Discord doesn't show a stale image
            if (!string.IsNullOrWhiteSpace(thumbnailUrl))
                builder.WithImageUrl($"{thumbnailUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        }
        else
        {
            // Show VOD link once it's available, or "Processing VOD..." while waiting
            string vodText = "> Processing VOD...";
            if (vod != null && !string.IsNullOrWhiteSpace(vod.Url))
                vodText = $"> [{vod.Duration}]({vod.Url})";

            string offlineText = timeOffline.HasValue
                ? timeOffline.Value.ToUniversalTime().ToString("d MMMM yyyy HH:mm")
                : "Unknown";

            builder
                .WithAuthor($"{userName} was live on Twitch!", pfp, Config.TwitchChannelUrl)
                .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
                .AddField("Game", $"> {game}", true)
                .AddField("VOD",  vodText,     true)
                .WithColor(new Color(0x5865F2))
                .WithFooter(
                    $"{streamDuration} • Offline at {offlineText}",
                    "https://images.icon-icons.com/4401/PNG/256/269414_twitch-icon.png"
                );
        }

        return builder.Build();
    }


    // ── UPDATE EMBED ─────────────────────────────────────────────────────────
    // Edits the already-posted Discord message with the latest stream state.
    // Called by the live updater loop, channel update handler, and offline handler.
    async Task UpdateEmbed()
    {
        // Don't attempt to update if no message has been published yet
        if (TwitchSession.PublishedChannelId == 0 || TwitchSession.PublishedMessageId == 0)
        {
            Logger.Log("[Debug] UpdateEmbed skipped — no published message");
            return;
        }
        
        try
        {
            // Snapshot all session state under the lock to avoid race conditions
            ulong channelId;
            ulong messageId;
            bool currentlyLive;
            string avatarUrl, title, gameName, thumbnailUrl;
            int viewerCount;
            DateTimeOffset startedAt, offlineAt;
            TwitchVOD vod;

            await _sessionLock.WaitAsync();
            try
            {
                channelId     = TwitchSession.PublishedChannelId;
                messageId     = TwitchSession.PublishedMessageId;
                currentlyLive = TwitchSession.CurrentlyLive;
                avatarUrl     = TwitchSession.TwitchAvatarUrl;
                title         = TwitchSession.Title;
                gameName      = TwitchSession.GameName;
                thumbnailUrl  = TwitchSession.ThumbnailUrl;
                viewerCount   = TwitchSession.ViewerCount;
                startedAt     = TwitchSession.StartedAt;
                offlineAt     = TwitchSession.OfflineAt;

                // Clone the VOD object to avoid modifying shared state outside the lock
                vod = new TwitchVOD
                {
                    Url      = TwitchVOD.Url,
                    Duration = TwitchVOD.Duration,
                    Viewable = TwitchVOD.Viewable
                };
            }
            finally { _sessionLock.Release(); }

            // Build a human-readable stream duration string
            TimeSpan duration = (currentlyLive ? DateTimeOffset.UtcNow : offlineAt) - startedAt;
            string onlineDuration = "Just started.";

            if (duration.TotalSeconds > 0)
            {
                List<string> parts = new();

                if (duration.Days    > 0) parts.Add($"{duration.Days} day{(duration.Days == 1 ? "" : "s")}");
                if (duration.Hours   > 0) parts.Add($"{duration.Hours} hour{(duration.Hours == 1 ? "" : "s")}");
                if (duration.Minutes > 0) parts.Add($"{duration.Minutes} minute{(duration.Minutes == 1 ? "" : "s")}");
                if (duration.Seconds > 0 && parts.Count == 0)
                    parts.Add($"{duration.Seconds} second{(duration.Seconds == 1 ? "" : "s")}");

                onlineDuration = parts.Count > 0
                    ? "Online for " + string.Join(", ", parts) + "."
                    : "Just started.";
            }

            // Resolve the Discord channel and message to edit
            ITextChannel? channel =
                _discordSocket.GetChannel(channelId) as ITextChannel
                ?? await _discordSocket.GetChannelAsync(channelId) as ITextChannel;

            Logger.Log($"[Debug] Channel: {channel?.Name ?? "NULL"}");
            if (channel == null) return;

            IUserMessage? message = await channel.GetMessageAsync(messageId) as IUserMessage;

            Logger.Log($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");
            if (message == null) return;

            Embed updated = BuildTwitchEmbed(
                Config.TwitchChannelName,
                currentlyLive,
                avatarUrl,
                title,
                gameName,
                thumbnailUrl,
                onlineDuration,
                viewerCount,
                currentlyLive ? null : vod,
                currentlyLive ? null : offlineAt
            );

            await message.ModifyAsync(props => props.Embed = updated);
            Logger.Log("[Info] Published twitch embed updated");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Warning] UpdateEmbed failed: {ex}");
        }
    }

    // NOTE: GetUserToken() and RefreshAccessToken() have been removed.
    // TokenManager + TwitchClient handle all of that transparently now.
}


// ── DATA MODELS ──────────────────────────────────────────────────────────────

public class TwitchVOD
{
    public string Url      { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Viewable { get; set; } = "";
}

public class StreamSession
{
    public ulong PublishedMessageId { get; set; } = 0;
    public ulong PublishedChannelId { get; set; } = 0;

    public string TwitchAvatarUrl { get; set; } = "";
    public string UserId          { get; set; } = "";
    public bool   CurrentlyLive   { get; set; } = false;
    public string Title           { get; set; } = "";
    public string GameName        { get; set; } = "";
    public string ThumbnailUrl    { get; set; } = "";
    public int    ViewerCount     { get; set; } = 0;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset OfflineAt { get; set; } = DateTimeOffset.MinValue;
}