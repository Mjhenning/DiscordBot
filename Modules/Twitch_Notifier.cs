using TwitchLib.Api.Helix.Models.Videos.GetVideos;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.EventArgs.Stream;

namespace DiscordBot.Modules;
using Discord.WebSocket;
using Discord;

using Newtonsoft.Json;

public class TwitchTokenResponse
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = "";
}

public class Twitch_Notifier
{
    readonly EventSubWebsocketClient _eventSubClient;
    private readonly DiscordSocketClient _discordSocket;
    private readonly StreamWriter _log;
    
    static StreamSession TwitchSession = new();
    static TwitchVOD TwitchVOD = new();
    readonly TwitchAPI TwitchApi;

    // int instead of bool so Interlocked can atomically check-and-set it, preventing two updater loops from starting simultaneously
    private int _liveUpdaterRunning = 0;

    // Ensures only one thread reads or writes TwitchSession/TwitchVOD at a time
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    
    static readonly HttpClient HttpClient = new();

    void Log(string msg)
    {
        Console.WriteLine(msg);
        _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }

    public Twitch_Notifier(EventSubWebsocketClient eventSubClient, DiscordSocketClient discordSocket, StreamWriter log, TwitchAPI twitchApi)
    {
        TwitchApi = twitchApi;
        _eventSubClient = eventSubClient;
        _log = log;
        
        Log("[Info] Twitch_Notifier constructor called");
        Log($"[Info] Notifier EventSubClient hash: {_eventSubClient.GetHashCode()}");
        
        _eventSubClient.WebsocketConnected    += OnWebsocketConnected;
        _eventSubClient.WebsocketDisconnected += (s, e) =>
        {
            Log("[Warning] Notifier websocket disconnected");
            return Task.CompletedTask;
        };

        _eventSubClient.ErrorOccurred += (s, e) =>
        {
            Log("[Error] Notifier websocket error occurred");
            foreach (var prop in e.GetType().GetProperties())
                Log($"[Error]   {prop.Name}: {prop.GetValue(e)}");
            return Task.CompletedTask;
        };
        
        _eventSubClient.StreamOnline  += OnStreamOnline; 
        _eventSubClient.StreamOffline += OnStreamOffline;
        _eventSubClient.ChannelUpdate += OnChannelUpdate;

        _discordSocket = discordSocket;
        
        Log("[Info] Twitch_Notifier constructor complete — all handlers attached");
    }
    
    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        Log($"[Info] Notifier OnWebsocketConnected fired — IsReconnect: {e.IsRequestedReconnect}, SessionId: {_eventSubClient.SessionId}");
        
        if (!e.IsRequestedReconnect)
        {
            try
            {
                Log("[Info] Creating stream.online subscription...");
                await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "stream.online",
                    "1",
                    new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId
                );
                Log("[Info] stream.online subscription created");
            }
            catch (Exception ex) { Log($"[Error] stream.online subscription failed: {ex.Message}"); }
        
            try
            {
                Log("[Info] Creating stream.offline subscription...");
                await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "stream.offline",           
                    "1",                       
                    new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId  
                );
                Log("[Info] stream.offline subscription created");
            }
            catch (Exception ex) { Log($"[Error] stream.offline subscription failed: {ex.Message}"); }
            
            try
            {
                Log("[Info] Creating channel.update subscription...");
                await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "channel.update",           
                    "2",                       
                    new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId  
                );
                Log("[Info] channel.update subscription created");
            }
            catch (Exception ex) { Log($"[Error] channel.update subscription failed: {ex.Message}"); }
            
            Log("[Info] Notifier OnWebsocketConnected complete");
        }
        else
        {
            Log("[Info] Reconnect detected — skipping subscription creation");
        }
    }
    
    public async Task<string?> GetUserToken()
    {
        Log("[Info] Fetching user token...");

        FormUrlEncodedContent tokenRequest = new(new[]
        {
            new KeyValuePair<string, string>("client_id",     Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("grant_type",    "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", Config.BroadcasterRefreshToken),
        });

        try
        {
            HttpResponseMessage response = await HttpClient.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                tokenRequest
            );

            string json = await response.Content.ReadAsStringAsync();

            Log($"[Info] Token HTTP status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Log($"[Error] Token response body: {json}");
                return null;
            }

            TwitchTokenResponse? tokenResponse =
                JsonConvert.DeserializeObject<TwitchTokenResponse>(json);

            return tokenResponse?.AccessToken;
        }
        catch (Exception ex)
        {
            Log($"[Error] GetUserToken failed: {ex}");
            return null;
        }
    }

    public async Task StartAsync()
    {
        Log("[Info] Notifier StartAsync called");
    
        string? _token = await GetUserToken();
        Log($"[Info] Twitch user token fetched: {(_token != null ? "success" : "failed")}");

        TwitchApi.Settings.ClientId    = Config.TwitchClientId;
        if (string.IsNullOrWhiteSpace(_token))
        {
            Log("[Error] Could not obtain Twitch token");
            return;
        }

        TwitchApi.Settings.AccessToken = _token;
    
        Log("[Info] TwitchApi credentials set — calling ConnectAsync");
        await _eventSubClient.ConnectAsync();
        Log("[Info] ConnectAsync returned");
    }

    // ─── ONLINE ──────────────────────────────────────────────────────────────

    async Task OnStreamOnline(object? sender, StreamOnlineArgs args)
{
    Log("[Info] OnStreamOnline fired");

    try
    {
        GetStreamsResponse? result = null;

        try
        {
            result = await TwitchApi.Helix.Streams.GetStreamsAsync(
                null, 1, null, null, null,
                new List<string>() { Config.TwitchChannelName }
            );
        }
        catch (Exception ex) when (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            Log("[Warning] Twitch token expired during GetStreamsAsync");

            bool refreshed = await RefreshAccessToken();

            if (!refreshed)
                throw;

            result = await TwitchApi.Helix.Streams.GetStreamsAsync(
                null, 1, null, null, null,
                new List<string>() { Config.TwitchChannelName }
            );
        }

        Log($"[Info] GetStreams returned {result?.Streams?.Length ?? 0} stream(s)");

        if (result?.Streams == null || result.Streams.Length == 0)
        {
            Log("[Warning] OnStreamOnline: no streams returned");
            return;
        }

        GetUsersResponse? userResult = null;

        try
        {
            userResult = await TwitchApi.Helix.Users.GetUsersAsync(
                null,
                new List<string>() { Config.TwitchChannelName }
            );
        }
        catch (Exception ex) when (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            Log("[Warning] Twitch token expired during GetUsersAsync");

            bool refreshed = await RefreshAccessToken();

            if (!refreshed)
                throw;

            userResult = await TwitchApi.Helix.Users.GetUsersAsync(
                null,
                new List<string>() { Config.TwitchChannelName }
            );
        }

        Log($"[Info] GetUsers returned {userResult?.Users?.Length ?? 0} user(s)");

        if (userResult?.Users == null || userResult.Users.Length == 0)
        {
            Log("[Warning] OnStreamOnline: no users returned");
            return;
        }

        await _sessionLock.WaitAsync();

        try
        {
            TwitchSession.TwitchAvatarUrl = userResult.Users[0].ProfileImageUrl;
            TwitchSession.UserId          = result.Streams[0].UserId;
            TwitchSession.CurrentlyLive   = true;
            TwitchSession.Title           = result.Streams[0].Title;
            TwitchSession.GameName        = result.Streams[0].GameName;

            TwitchSession.ThumbnailUrl =
                result.Streams[0].ThumbnailUrl
                    .Replace("{width}", "1920")
                    .Replace("{height}", "1080");

            TwitchSession.ViewerCount = result.Streams[0].ViewerCount;

            TwitchSession.StartedAt =
                new DateTimeOffset(result.Streams[0].StartedAt, TimeSpan.Zero);
        }
        finally
        {
            _sessionLock.Release();
        }

        Log(
            $"[Info] Session populated — " +
            $"Title: {TwitchSession.Title}, " +
            $"Game: {TwitchSession.GameName}, " +
            $"Viewers: {TwitchSession.ViewerCount}"
        );

        await OnStreamReceived();
    }
    catch (Exception ex)
    {
        Log($"[Error] OnStreamOnline failed: {ex}");
    }
}
    
    async Task OnStreamReceived()
    {
        Log("[Info] OnStreamReceived called");
        ITextChannel? channel =
            _discordSocket.GetChannel(Config.TwitchNotifyChannelId) as ITextChannel
            ?? await _discordSocket.GetChannelAsync(Config.TwitchNotifyChannelId) as ITextChannel;
        
        if (channel == null)
        {
            Log($"[Warning] Could not find Twitch notify channel (ID: {Config.TwitchNotifyChannelId})");
            return;
        }

        Log($"[Info] Posting to #{channel.Name}");

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
        string roleMention = "@everyone";
        
        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);

        await _sessionLock.WaitAsync();
        try
        {
            TwitchSession.PublishedChannelId = channel.Id;
            TwitchSession.PublishedMessageId = posted.Id;
        }
        finally { _sessionLock.Release(); }

        Log($"[Info] Notification published to #{channel.Name} (msg: {posted.Id})");
        
        // Atomically set _liveUpdaterRunning to 1 only if it's currently 0, so a second stream.online event can't spawn a duplicate loop
        if (Interlocked.CompareExchange(ref _liveUpdaterRunning, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try   { await StartLiveUpdates(); }
                finally { Interlocked.Exchange(ref _liveUpdaterRunning, 0); }
            });
        }
    }
    
    // ─── OFFLINE ─────────────────────────────────────────────────────────────
    
    async Task OnStreamOffline(object? sender, StreamOfflineArgs args)
    {
        Log("[Info] OnStreamOffline fired");

        await _sessionLock.WaitAsync();
        try
        {
            TwitchSession.OfflineAt     = DateTimeOffset.UtcNow;
            TwitchSession.CurrentlyLive = false;
        }
        finally { _sessionLock.Release(); }

        Log("[Info] Updating embed to offline state...");
        await UpdateEmbed();

        _ = Task.Run(async () =>
        {
            Log("[Info] Starting background VOD check...");
            await CheckIfVodUp();

            await _sessionLock.WaitAsync();
            try
            {
                TwitchSession = new StreamSession();
                TwitchVOD     = new TwitchVOD();
            }
            finally { _sessionLock.Release(); }

            Log("[Info] Session reset");
        });
    }

    async Task CheckIfVodUp()
    {
        string userId;

        await _sessionLock.WaitAsync();
        try   { userId = TwitchSession.UserId; }
        finally { _sessionLock.Release(); }

        GetVideosResponse? result = null;

        try
        {
            result = await TwitchApi.Helix.Videos.GetVideosAsync(
                null, userId, null, null, null, 1
            );
        }
        catch (Exception ex) when (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            Log("[Warning] Twitch token expired during GetVideosAsync");

            bool refreshed = await RefreshAccessToken();

            if (!refreshed)
                throw;

            result = await TwitchApi.Helix.Videos.GetVideosAsync(
                null, userId, null, null, null, 1
            );
        }

        if (result?.Videos != null && result.Videos.Length > 0)
        {
            await _sessionLock.WaitAsync();
            try
            {
                TwitchVOD.Url      = result.Videos[0].Url;
                TwitchVOD.Duration = result.Videos[0].Duration;
            }
            finally { _sessionLock.Release(); }

            Log($"[Info] VOD found: {result.Videos[0].Url}");

            await UpdateEmbed();
        }
    }

    // ─── UPDATE ───────────────────────────────────────────────────────────────
    
    async Task OnChannelUpdate(object? sender, ChannelUpdateArgs args)
    {
        Log($"[Info] OnChannelUpdate fired — Title: {args.Payload.Event.Title}, Game: {args.Payload.Event.CategoryName}");

        await _sessionLock.WaitAsync();
        try
        {
            TwitchSession.GameName = args.Payload.Event.CategoryName;
            TwitchSession.Title    = args.Payload.Event.Title;
        }
        finally { _sessionLock.Release(); }
        
        await UpdateEmbed();
    }
    
    // ─── HELPERS ──────────────────────────────────────────────────────────────
    
    async Task StartLiveUpdates()
    {
        Log("[Info] StartLiveUpdates loop started");

        while (true)
        {
            bool live;

            await _sessionLock.WaitAsync();

            try
            {
                live = TwitchSession.CurrentlyLive;
            }
            finally
            {
                _sessionLock.Release();
            }

            if (!live)
                break;

            DateTimeOffset startTime = DateTimeOffset.UtcNow;

            try
            {
                GetStreamsResponse? result = null;

                try
                {
                    result = await TwitchApi.Helix.Streams.GetStreamsAsync(
                        null,
                        1,
                        null,
                        null,
                        null,
                        new List<string>() { Config.TwitchChannelName }
                    );
                }
                catch (Exception ex) when (
                    ex.Message.Contains(
                        "Unauthorized",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    Log("[Warning] Twitch token expired during StartLiveUpdates");

                    bool refreshed = await RefreshAccessToken();

                    if (!refreshed)
                        throw;

                    result = await TwitchApi.Helix.Streams.GetStreamsAsync(
                        null,
                        1,
                        null,
                        null,
                        null,
                        new List<string>() { Config.TwitchChannelName }
                    );
                }

                if (result?.Streams == null || result.Streams.Length == 0)
                {
                    Log("[Warning] StartLiveUpdates: GetStreams returned no results");
                }
                else
                {
                    await _sessionLock.WaitAsync();

                    try
                    {
                        TwitchSession.ViewerCount =
                            result.Streams[0].ViewerCount;

                        TwitchSession.ThumbnailUrl =
                            result.Streams[0].ThumbnailUrl
                                .Replace("{width}", "1920")
                                .Replace("{height}", "1080");
                    }
                    finally
                    {
                        _sessionLock.Release();
                    }
                }

                await UpdateEmbed();
            }
            catch (Exception ex)
            {
                Log($"[Error] StartLiveUpdates failed: {ex}");
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - startTime;

            TimeSpan delay = TimeSpan.FromMinutes(1) - elapsed;

            try
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }
            catch (TaskCanceledException)
            {
                Log("[Info] StartLiveUpdates delay cancelled");
                break;
            }
        }

        Log("[Info] StartLiveUpdates loop ended — stream no longer live");
    }
    
    Embed BuildTwitchEmbed(
        string userName,
        bool live,
        string pfp,
        string title,
        string game,
        string thumbnailUrl,
        string streamDuration,
        int viewerCount      = 0,
        TwitchVOD? vod       = null,
        DateTimeOffset? timeOffline = null
    )
    {
        EmbedBuilder builder = new EmbedBuilder();
        
        if (live)
        {
            builder
                .WithAuthor($"{userName} is live on Twitch!", pfp, Config.TwitchChannelUrl)
                .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
                .AddField("Game", $"> {game}", true)
                .AddField("Viewers", $"> {viewerCount}", true)
                .WithColor(new Color(0x5865F2))
                .WithFooter(
                    $"{streamDuration}",
                    "https://static.vecteezy.com/system/resources/previews/010/992/697/large_2x/social-media-twitch-realistic-icon-free-free-png.png"
                );
            
                if (!string.IsNullOrWhiteSpace(thumbnailUrl))
                {
                    builder.WithImageUrl($"{thumbnailUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                }
        }
        else
        {
            string vodText = "> Processing VOD...";

            if (vod != null && !string.IsNullOrWhiteSpace(vod.Url))
            {
                vodText = $"> [{vod.Duration}]({vod.Url})";
            }

            string offlineText = timeOffline.HasValue
                ? $"<t:{timeOffline.Value.ToUnixTimeSeconds()}:f>"
                : "Unknown";

            builder
                .WithAuthor($"{userName} was live on Twitch!", pfp, Config.TwitchChannelUrl)
                .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
                .AddField("Game", $"> {game}", true)
                .AddField("VOD", vodText, true)
                .WithColor(new Color(0x5865F2))
                .WithFooter(
                    $"{streamDuration} • Offline at {offlineText}",
                    "https://static.vecteezy.com/system/resources/previews/010/992/697/large_2x/social-media-twitch-realistic-icon-free-free-png.png"
                );
        }
        
        return builder.Build();
    }

    async Task UpdateEmbed()
    {
        try
        {
            ulong channelId;
            ulong messageId;

            bool currentlyLive;

            string avatarUrl;
            string title;
            string gameName;
            string thumbnailUrl;

            int viewerCount;

            DateTimeOffset startedAt;
            DateTimeOffset offlineAt;

            TwitchVOD vod;

            await _sessionLock.WaitAsync();

            try
            {
                startedAt      = TwitchSession.StartedAt;
                channelId      = TwitchSession.PublishedChannelId;
                messageId      = TwitchSession.PublishedMessageId;

                currentlyLive  = TwitchSession.CurrentlyLive;

                avatarUrl      = TwitchSession.TwitchAvatarUrl;
                title          = TwitchSession.Title;
                gameName       = TwitchSession.GameName;
                thumbnailUrl   = TwitchSession.ThumbnailUrl;

                viewerCount    = TwitchSession.ViewerCount;

                offlineAt      = TwitchSession.OfflineAt;

                // Clone instead of sharing same reference object
                vod = new TwitchVOD
                {
                    Url      = TwitchVOD.Url,
                    Duration = TwitchVOD.Duration,
                    Viewable = TwitchVOD.Viewable
                };
            }
            finally
            {
                _sessionLock.Release();
            }

            // TotalSeconds > 0 rather than != TimeSpan.Zero so sub-minute durations
            // don't incorrectly show "Just started." for the whole first minute
            TimeSpan duration = DateTimeOffset.UtcNow - startedAt;

            string onlineDuration = "Just started.";

            if (duration.TotalSeconds > 0)
            {
                List<string> parts = new();

                if (duration.Days > 0)
                    parts.Add($"{duration.Days} day{(duration.Days == 1 ? "" : "s")}");

                if (duration.Hours > 0)
                    parts.Add($"{duration.Hours} hour{(duration.Hours == 1 ? "" : "s")}");

                if (duration.Minutes > 0)
                    parts.Add($"{duration.Minutes} minute{(duration.Minutes == 1 ? "" : "s")}");

                if (duration.Seconds > 0 && parts.Count == 0)
                    parts.Add($"{duration.Seconds} second{(duration.Seconds == 1 ? "" : "s")}");

                onlineDuration =
                    parts.Count > 0
                        ? "Online for " + string.Join(", ", parts) + "."
                        : "Just started.";
            }

            ITextChannel? channel =
                _discordSocket.GetChannel(channelId) as ITextChannel
                ?? await _discordSocket.GetChannelAsync(channelId) as ITextChannel;

            Log($"[Debug] Channel: {channel?.Name ?? "NULL"}");

            if (channel == null)
                return;

            IUserMessage? message =
                await channel.GetMessageAsync(messageId) as IUserMessage;

            Log($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");

            if (message == null)
                return;

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

            Log("[Info] Published twitch embed updated");
        }
        catch (Exception ex)
        {
            Log($"[Warning] TryUpdatePublishedEmbed failed: {ex}");
        }
    }
    
    async Task<bool> RefreshAccessToken()
    {
        string? token = await GetUserToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            Log("[Error] Failed to refresh Twitch token");
            return false;
        }

        TwitchApi.Settings.AccessToken = token;

        Log("[Info] Twitch access token refreshed");
        return true;
    }
}

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
    public DateTimeOffset StartedAt  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset OfflineAt  { get; set; } = DateTimeOffset.MinValue;
}