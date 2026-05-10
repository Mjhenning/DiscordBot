namespace DiscordBot.Modules;

using Discord.WebSocket;
using Discord;

using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;

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
    
    static StreamSession TwitchSession = new();
    static TwitchVOD TwitchVOD = new();
    static readonly TwitchAPI TwitchApi = new();
    
    static readonly HttpClient HttpClient = new HttpClient(){BaseAddress = new Uri("https://id.twitch.tv/oauth2/token")}; //gives it a base-address to access

    public Twitch_Notifier(EventSubWebsocketClient eventSubClient, DiscordSocketClient discordSocket) //constructs webhook event
    {
        _eventSubClient = eventSubClient;
        _eventSubClient.StreamOnline += OnStreamOnline; 
        _eventSubClient.StreamOffline += OnStreamOffline;
        _eventSubClient.ChannelUpdate += OnChannelUpdate;

        _discordSocket = discordSocket;
    }

    public async Task<string?> GetAppToken() //api call via post to get access token
    {
        FormUrlEncodedContent tokenRequest = new FormUrlEncodedContent(new[] //sets up the structure to feed post request
        {
            new KeyValuePair<string, string>("client_id", Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
        });

        TwitchTokenResponse response = new TwitchTokenResponse();
        try //try this
        {
            var asyncResponse = await HttpClient.PostAsync(HttpClient.BaseAddress, tokenRequest);
            string json = await asyncResponse.Content.ReadAsStringAsync();
       
            response = JsonConvert.DeserializeObject<TwitchTokenResponse>(json) ?? new TwitchTokenResponse();
        }
        catch (Exception e) //else catch exception and log
        {
            Console.WriteLine($"[Error] {e.GetType().Name}: {e.Message}");
            Console.WriteLine($"[Error] Inner: {e.InnerException?.Message}");
        }
        return response?.AccessToken != "" ? response?.AccessToken : null;
    }

    public async Task StartAsync() //connects to twitch
    {
        string? _token = await GetAppToken();
        Console.WriteLine($"[Info] Twitch app token fetched: {(_token != null ? "success" : "failed")}");

        TwitchApi.Settings.ClientId = Config.TwitchClientId;
        TwitchApi.Settings.AccessToken = _token;
        
        await _eventSubClient.ConnectAsync();
    }

    //------------------------------ONLINE----------------------------------------
    
    async void OnStreamOnline(object? sender, StreamOnlineArgs args)
    {
        GetStreamsResponse? result = await TwitchApi.Helix.Streams.GetStreamsAsync(
            null,
            1,
            null,
            null,
            null,
            new List<string>() { Config.TwitchChannelName },
            TwitchApi.Settings.AccessToken
        );

        GetUsersResponse? userResult = await TwitchApi.Helix.Users.GetUsersAsync(
            null,
            new List<string>(){Config.TwitchChannelName},
            TwitchApi.Settings.AccessToken
        );
        
        //TwitchSession.CurrentStream = result.Streams[0];
        
        
        TwitchSession.TwitchAvatarUrl = userResult.Users[0].ProfileImageUrl;
        TwitchSession.CurrentlyLive = true;
        TwitchSession.Title = result.Streams[0].Title;
        TwitchSession.GameName = result.Streams[0].GameName;
        TwitchSession.ThumbnailUrl = result.Streams[0].ThumbnailUrl;
        TwitchSession.ViewerCount  = result.Streams[0].ViewerCount;
        TwitchSession.StartedAt = new DateTimeOffset(result.Streams[0].StartedAt, TimeSpan.Zero);
        
        await OnStreamReceived();
    }
    
    async Task OnStreamReceived()
    {
        ITextChannel? channel = _discordSocket.GetChannel(Config.TwitchNotifyChannelId) as ITextChannel;
        
        if (channel == null) //if no channel log and return
        {
            Console.WriteLine("[Warning] Could not find Twitch notify channel");
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
        
        // Send and store the message ID + channel ID for future edits
        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);
        TwitchSession.PublishedChannelId = channel.Id;
        TwitchSession.PublishedMessageId = posted.Id;

        Console.WriteLine($"[Info] Notification published to #{channel.Name} (msg: {posted.Id})");
        
        _ = Task.Run(async () => await StartLiveUpdates());
    }
    
    //------------------------------OFFLINE----------------------------------------
    
    async void OnStreamOffline(object? sender, StreamOfflineArgs args)
    {
        TwitchSession.CurrentlyLive = false;
        await UpdateEmbed();
    }

    //------------------------------UPDATE----------------------------------------
    
    async void OnChannelUpdate(object? sender, ChannelUpdateArgs args)
    {
        TwitchSession.GameName = args.Notification.Payload.Event.CategoryName;
        TwitchSession.Title = args.Notification.Payload.Event.Title;
        
        await UpdateEmbed();
    }
    
    
    //------------------------------HELPERS----------------------------------------
    
    async Task StartLiveUpdates()
    {
        while (TwitchSession.CurrentlyLive)
        {
            DateTimeOffset startTime = DateTimeOffset.UtcNow;

            await UpdateEmbed();
            
            TimeSpan elapsed = DateTime.UtcNow - startTime;
            TimeSpan delay = TimeSpan.FromMinutes(1) - elapsed;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }
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
        int viewerCount = 0,
        TwitchVOD? vod = null,
        DateTimeOffset? timeOffline = null
    )
    {
        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor($"{userName} {(live? "is" : "was")} live on Twitch!", pfp, Config.TwitchChannelUrl)
            .WithTitle(title).WithUrl(Config.TwitchChannelUrl)
            .AddField("Game", $"> {game}", true)
            .AddField($"{(live? "Viewers" : "VOD")}", $"> {(live? viewerCount : $"<a href=\"{vod.Url}\">{vod.Duration}</a>")}", true)
            .WithColor(new Color(0x5865F2))
            .WithImageUrl(thumbnailUrl)
            .WithFooter($"{(live? $"{streamDuration}" : $"{streamDuration} | Offline at {timeOffline.ToString()}")}", "https://static.vecteezy.com/system/resources/previews/010/992/697/large_2x/social-media-twitch-realistic-icon-free-free-png.png");

        return builder.Build();
    }

    async Task UpdateEmbed()
    {
        TimeSpan duration = DateTimeOffset.UtcNow - TwitchSession.StartedAt.ToUniversalTime();

        string onlineDuration = "Just started.";
        
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
            Console.WriteLine($"[Debug] Channel: {channel?.Name ?? "NULL"}");
            if (channel == null) return;

            IUserMessage? message = await channel.GetMessageAsync(TwitchSession.PublishedMessageId) as IUserMessage;
            Console.WriteLine($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");
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
                TwitchSession.CurrentlyLive? null : TwitchVOD,
                TwitchSession.CurrentlyLive? null : DateTimeOffset.UtcNow
            );
            await message.ModifyAsync(props => props.Embed = updated);
            Console.WriteLine($"[Info] Published twitch embed updated");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] TryUpdatePublishedEmbed failed: {ex.Message}");
            Console.WriteLine($"[Warning] {ex.StackTrace}");
        }
        
    }
}


public class TwitchVOD
{
    public string Url { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Viewable { get; set; } = "";
}

public class StreamSession //persistent until on ofline after on online and then it clears out for next stream
{
    public ulong PublishedMessageId  { get; set; } = 0;
    public ulong PublishedChannelId  { get; set; } = 0;
    
    public string TwitchAvatarUrl { get; set; } = "";
    public bool CurrentlyLive { get; set; } = false;
    public string Title { get; set; } = "";
    public string GameName { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public int ViewerCount { get; set; } = 0;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}