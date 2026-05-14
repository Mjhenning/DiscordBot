using TwitchLib.Api.Helix.Models.Videos.GetVideos;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

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
    
    static readonly HttpClient HttpClient = new HttpClient(){BaseAddress = new Uri("https://id.twitch.tv/oauth2/token")};

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
        
        _eventSubClient.WebsocketConnected += OnWebsocketConnected;
        
        _eventSubClient.StreamOnline  += OnStreamOnline; 
        _eventSubClient.StreamOffline += OnStreamOffline;
        _eventSubClient.ChannelUpdate += OnChannelUpdate;

        _discordSocket = discordSocket;
        
        _eventSubClient.WebsocketConnected    += (s, e) => Log("[Info] Websocket connected");
        _eventSubClient.WebsocketDisconnected += (s, e) => Log("[Warning] Websocket disconnected");
        _eventSubClient.ErrorOccurred         += (s, e) => Log($"[Error] Websocket error: {e.Message}");
        
        _eventSubClient.ErrorOccurred += (s, e) => 
        {
            Log($"[Error] Websocket error type: {e.GetType().FullName}");
            foreach (var prop in e.GetType().GetProperties())
            {
                Log($"[Error] {prop.Name}: {prop.GetValue(e)}");
            }
        };
    }
    
    async void OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (!e.IsRequestedReconnect)
        {
            await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "stream.online",
                "1",
                new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId
            );
        
            await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "stream.offline",           
                "1",                       
                new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId  
            );
            
            await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.update",           
                "2",                       
                new Dictionary<string, string> { { "broadcaster_user_id", Config.TwitchUserId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId  
            );
        }
    }
    
    public async Task<string?> GetAppToken()
    {
        FormUrlEncodedContent tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id",     Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("grant_type",    "client_credentials"),
        });

        TwitchTokenResponse response = new TwitchTokenResponse();
        try
        {
            var asyncResponse = await HttpClient.PostAsync(HttpClient.BaseAddress, tokenRequest);
            string json = await asyncResponse.Content.ReadAsStringAsync();
            response = JsonConvert.DeserializeObject<TwitchTokenResponse>(json) ?? new TwitchTokenResponse();
        }
        catch (Exception e)
        {
            Log($"[Error] {e.GetType().Name}: {e.Message}");
            Log($"[Error] Inner: {e.InnerException?.Message}");
        }
        return response?.AccessToken != "" ? response?.AccessToken : null;
    }

    public async Task StartAsync()
    {
        string? _token = await GetAppToken();
        Log($"[Info] Twitch app token fetched: {(_token != null ? "success" : "failed")}");

        TwitchApi.Settings.ClientId    = Config.TwitchClientId;
        TwitchApi.Settings.AccessToken = _token;
        
        await _eventSubClient.ConnectAsync();
    }

    // ─── ONLINE ──────────────────────────────────────────────────────────────

    async void OnStreamOnline(object? sender, StreamOnlineArgs args)
    {
        GetStreamsResponse? result = await TwitchApi.Helix.Streams.GetStreamsAsync(
            null, 1, null, null, null,
            new List<string>() { Config.TwitchChannelName }
        );

        GetUsersResponse? userResult = await TwitchApi.Helix.Users.GetUsersAsync(
            null,
            new List<string>() { Config.TwitchChannelName }
        );
        
        TwitchSession.TwitchAvatarUrl = userResult.Users[0].ProfileImageUrl;
        TwitchSession.UserId          = result.Streams[0].UserId;
        TwitchSession.CurrentlyLive   = true;
        TwitchSession.Title           = result.Streams[0].Title;
        TwitchSession.GameName        = result.Streams[0].GameName;
        TwitchSession.ThumbnailUrl    = result.Streams[0].ThumbnailUrl
            .Replace("{width}", "1920")
            .Replace("{height}", "1080");
        TwitchSession.ViewerCount  = result.Streams[0].ViewerCount;
        TwitchSession.StartedAt    = new DateTimeOffset(result.Streams[0].StartedAt, TimeSpan.Zero);
        
        await OnStreamReceived();
    }
    
    async Task OnStreamReceived()
    {
        ITextChannel? channel = _discordSocket.GetChannel(Config.TwitchNotifyChannelId) as ITextChannel;
        
        if (channel == null)
        {
            Log("[Warning] Could not find Twitch notify channel");
            return;
        }

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
        string roleMention = MentionUtils.MentionRole(Config.LiveRoleId);
        
        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);
        TwitchSession.PublishedChannelId = channel.Id;
        TwitchSession.PublishedMessageId = posted.Id;

        Log($"[Info] Notification published to #{channel.Name} (msg: {posted.Id})");
        
        _ = Task.Run(async () => await StartLiveUpdates());
    }
    
    // ─── OFFLINE ─────────────────────────────────────────────────────────────
    
    async void OnStreamOffline(object? sender, StreamOfflineArgs args)
    {
        TwitchSession.OfflineAt      = DateTimeOffset.UtcNow;
        TwitchSession.CurrentlyLive  = false;

        await CheckIfVodUp();
        await UpdateEmbed();

        TwitchSession = new StreamSession();
        TwitchVOD     = new TwitchVOD();
    }

    async Task CheckIfVodUp()
    {
        while (!TwitchVOD.Viewable.Contains("public"))
        {
            DateTimeOffset startTime = DateTimeOffset.UtcNow;

            GetVideosResponse? result = await TwitchApi.Helix.Videos.GetVideosAsync(
                null, TwitchSession.UserId, null, null, null, 1
            );

            if (result?.Videos == null || result.Videos.Length == 0)
            {
                Log("[Info] VOD not available yet, retrying...");
            }
            else
            {
                TwitchVOD.Url      = result.Videos[0].Url;
                TwitchVOD.Duration = result.Videos[0].Duration;
                TwitchVOD.Viewable = result.Videos[0].Viewable;
            }
        
            TimeSpan elapsed = DateTime.UtcNow - startTime;
            TimeSpan delay   = TimeSpan.FromMinutes(1) - elapsed;

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
        }
    }

    // ─── UPDATE ───────────────────────────────────────────────────────────────
    
    async void OnChannelUpdate(object? sender, ChannelUpdateArgs args)
    {
        TwitchSession.GameName = args.Notification.Payload.Event.CategoryName;
        TwitchSession.Title    = args.Notification.Payload.Event.Title;
        
        await UpdateEmbed();
    }
    
    // ─── HELPERS ──────────────────────────────────────────────────────────────
    
    async Task StartLiveUpdates()
    {
        while (TwitchSession.CurrentlyLive)
        {
            DateTimeOffset startTime = DateTimeOffset.UtcNow;

            GetStreamsResponse? result = await TwitchApi.Helix.Streams.GetStreamsAsync(
                null, 1, null, null, null,
                new List<string>() { Config.TwitchChannelName }
            );
            
            TwitchSession.ViewerCount  = result.Streams[0].ViewerCount;
            TwitchSession.ThumbnailUrl = result.Streams[0].ThumbnailUrl
                .Replace("{width}", "1920")
                .Replace("{height}", "1080");
            
            await UpdateEmbed();
            
            TimeSpan elapsed = DateTime.UtcNow - startTime;
            TimeSpan delay   = TimeSpan.FromMinutes(1) - elapsed;

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
        }
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
        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor($"{userName} {(live ? "is" : "was")} live on Twitch!", pfp, Config.TwitchChannelUrl)
            .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
            .AddField("Game", $"> {game}", true)
            .AddField($"{(live ? "Viewers" : "VOD")}", $"> {(live ? viewerCount : $"[{vod.Duration}]({vod.Url})")}", true)
            .WithColor(new Color(0x5865F2))
            .WithImageUrl(live ? $"{thumbnailUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" : null)
            .WithFooter(
                $"{(live ? streamDuration : $"{streamDuration} | Offline at {timeOffline}")}",
                "https://static.vecteezy.com/system/resources/previews/010/992/697/large_2x/social-media-twitch-realistic-icon-free-free-png.png"
            );

        return builder.Build();
    }

    async Task UpdateEmbed()
    {
        TimeSpan duration      = DateTimeOffset.UtcNow - TwitchSession.StartedAt.ToUniversalTime();
        string onlineDuration  = "Just started.";
        
        if (duration != TimeSpan.Zero)
        {
            List<string> parts = new();

            if (duration.Days > 0)    parts.Add($"{duration.Days} day{(duration.Days == 1 ? "" : "s")}");
            if (duration.Hours > 0)   parts.Add($"{duration.Hours} hour{(duration.Hours == 1 ? "" : "s")}");
            if (duration.Minutes > 0) parts.Add($"{duration.Minutes} minute{(duration.Minutes == 1 ? "" : "s")}");

            onlineDuration = parts.Count > 0 ? "Online for " + string.Join(", ", parts) + "." : "Just started.";
        }
        
        try
        {
            ITextChannel? channel = _discordSocket.GetChannel(TwitchSession.PublishedChannelId) as ITextChannel;
            Log($"[Debug] Channel: {channel?.Name ?? "NULL"}");
            if (channel == null) return;

            IUserMessage? message = await channel.GetMessageAsync(TwitchSession.PublishedMessageId) as IUserMessage;
            Log($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");
            if (message == null) return;

            Embed updated = BuildTwitchEmbed(
                Config.TwitchChannelName,
                TwitchSession.CurrentlyLive,
                TwitchSession.TwitchAvatarUrl,
                TwitchSession.Title,
                TwitchSession.GameName,
                TwitchSession.ThumbnailUrl,
                onlineDuration,
                TwitchSession.ViewerCount,
                TwitchSession.CurrentlyLive ? null : TwitchVOD,
                TwitchSession.CurrentlyLive ? null : TwitchSession.OfflineAt
            );
            await message.ModifyAsync(props => props.Embed = updated);
            Log($"[Info] Published twitch embed updated");
        }
        catch (Exception ex)
        {
            Log($"[Warning] TryUpdatePublishedEmbed failed: {ex.Message}");
            Log($"[Warning] {ex.StackTrace}");
        }
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