using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace DiscordBot.Modules;

using Discord.WebSocket;
using Discord;

using TwitchLib.Api;
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
        
        TwitchSession.CurrentStream = result.Streams[0];
        TwitchSession.TwitchAvatarUrl = userResult.Users[0].ProfileImageUrl;

        await OnStreamReceived();
    }
    
    async void OnStreamOffline(object? sender, StreamOfflineArgs args)
    {
        DateTimeOffset offlineAt = DateTimeOffset.UtcNow;
        
    }

    async void OnChannelUpdate(object? sender, ChannelUpdateArgs args)
    {
        
    }


    async Task OnStreamReceived()
    {
        ITextChannel? channel = _discordSocket.GetChannel(Config.TwitchNotifyChannelId) as ITextChannel;
        
        if (channel == null) //if no channel log and return
        {
            Console.WriteLine("[Warning] Could not find Twitch notify channel");
            return;
        }

        Embed embed = BuildTwitchEmbed();
        string roleMention = MentionUtils.MentionRole(Config.LiveRoleId);
        
        // Send and store the message ID + channel ID for future edits
        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);
        TwitchSession.PublishedChannelId = channel.Id;
        TwitchSession.PublishedMessageId = posted.Id;

        Console.WriteLine($"[Info] Notification published to #{channel.Name} (msg: {posted.Id})");
    }
    
    
    Embed BuildTwitchEmbed()
    {
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor($"{TwitchSession.CurrentStream.UserName} is {TwitchSession.CurrentStream.Type} on Twitch!", TwitchSession.TwitchAvatarUrl, Config.TwitchChannelUrl)
            .WithTitle(TwitchSession.CurrentStream.Title)
            
            .AddField("GAME", $"> {TwitchSession.CurrentStream.GameName}", true)
            .AddField("VOD", "> REDACTED", true)
            
            .WithColor(new Color(0x5865F2))
            .WithFooter($"Online for 1 second.", "https://static.vecteezy.com/system/resources/previews/010/992/697/large_2x/social-media-twitch-realistic-icon-free-free-png.png");

        return builder.Build();
    }

    public string CalcTime()
    {
        TimeSpan duration = DateTimeOffset.UtcNow - TwitchSession.CurrentStream.StartedAt.ToUniversalTime();

        string onlineDuration = "just started.";
        
        if (duration != TimeSpan.Zero)
        {
            List<string> parts = new();

            if (duration.Days > 0)    parts.Add($"{duration.Days} day{(duration.Days == 1 ? "" : "s")}");
            if (duration.Hours > 0)   parts.Add($"{duration.Hours} hour{(duration.Hours == 1 ? "" : "s")}");
            if (duration.Minutes > 0) parts.Add($"{duration.Minutes} minute{(duration.Minutes == 1 ? "" : "s")}");

            onlineDuration = parts.Count > 0 ? string.Join(", ", parts) + "." : "just started.";
        }
        
        return onlineDuration;
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

    public Stream? CurrentStream { get; set; } = null;
}