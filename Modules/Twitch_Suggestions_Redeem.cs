using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace DiscordBot.Modules;

public class TwitchSuggestionsPoster
{
    readonly EventSubWebsocketClient _eventSubClient;
    readonly DiscordSocketClient _discordSocket;
    readonly TwitchAPI TwitchApi;
    
    readonly StreamWriter _log;
    
    readonly HttpClient _http = new() { BaseAddress = new Uri("https://id.twitch.tv/oauth2/token") };
    string _userAccessToken = "";
    
    void Log(string msg)
    {
        Console.WriteLine(msg);
        _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }

    public TwitchSuggestionsPoster(EventSubWebsocketClient eventSubClient, DiscordSocketClient discordSocket, StreamWriter log, TwitchAPI twitchApi)
    {
        TwitchApi = twitchApi;
        _eventSubClient = eventSubClient;
        _log = log;
        
        _eventSubClient.WebsocketConnected += OnWebsocketConnected;
        _eventSubClient.ChannelPointsCustomRewardRedemptionUpdate += OnRewardRedemptionUpdated;
        
        _discordSocket = discordSocket;
        _discordSocket.InteractionCreated += OnInteractionCreated;
    }
    
    public async Task StartAsync()
    {
        FormUrlEncodedContent request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id",     Config.TwitchClientId),
            new KeyValuePair<string, string>("client_secret", Config.TwitchClientSecret),
            new KeyValuePair<string, string>("grant_type",    "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", Config.BroadcasterRefreshToken),
        });

        try
        {
            var response = await _http.PostAsync(_http.BaseAddress, request);
            string json  = await response.Content.ReadAsStringAsync();
            var parsed   = JsonConvert.DeserializeObject<TwitchTokenResponse>(json);
            _userAccessToken = parsed?.AccessToken ?? "";
            Log($"[Info] SuggestionsPoster user token fetched: {(_userAccessToken != "" ? "success" : "failed")}");
        }
        catch (Exception e)
        {
            Log($"[Error] SuggestionsPoster token fetch failed: {e.Message}");
        }
    }
    
    async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (!e.IsRequestedReconnect)
        {
            try
            {
                var userApi = new TwitchAPI();
                userApi.Settings.ClientId    = Config.TwitchClientId;
                userApi.Settings.AccessToken = _userAccessToken;

                await userApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "channel.channel_points_custom_reward_redemption.update",
                    "1",
                    new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", Config.TwitchUserId },
                        { "reward_id", Config.SuggestRewardId }
                    },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId
                );
                Log("[Info] SuggestionsPoster EventSub subscription created successfully");
            }
            catch (Exception ex)
            {
                Log($"[Error] SuggestionsPoster subscription failed: {ex.Message}");
                Log($"[Error] Inner: {ex.InnerException?.Message}");
            }
        }
    }

    async Task OnRewardRedemptionUpdated(object? sender, ChannelPointsCustomRewardRedemptionArgs args)
    {
        var redemption = args.Payload.Event;
        Log($"[Info] Redemption event received — Status: {redemption.Status}, User: {redemption.UserName}");

        if (redemption.Status != "fulfilled")
        {
            Log($"[Info] Skipping — status is '{redemption.Status}', not fulfilled");
            return;
        }
        
        string userName  = redemption.UserName;
        
        GetUsersResponse? userResult = await TwitchApi.Helix.Users.GetUsersAsync(
            null,
            new List<string>() { userName }
        );

        await PostToDiscord(userName, redemption.UserInput, userResult.Users[0].ProfileImageUrl, $"www.twitch.tv/{userName}");
    }

    async Task PostToDiscord(string user, string input, string avatarUrl, string userUrl = "")
    {
        ITextChannel? channel = _discordSocket.GetChannel(Config.SuggestionChannelId) as ITextChannel;
        if (channel == null) { Log("[Warning] Could not find Suggestion channel"); return; }

        var components = new ComponentBuilder()
            .WithButton("Mark Complete", "suggestion_complete", ButtonStyle.Success)
            .Build();

        Log($"[Info] Componenets sucessfully setup for embed");

        Embed embed = BuildTwitchEmbed(user, input, avatarUrl, userUrl);

        Log($"[Info] EMbed created");
        IUserMessage posted = await channel.SendMessageAsync(embed: embed, components: components);

        Log($"[Info] Suggestion published to #{channel.Name} (msg: {posted.Id})");
    }
    
    Embed BuildTwitchEmbed(
        string userName,
        string userInput,
        string userAvatarUrl,
        string userUrl = ""
    )
    {
        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor($"{userName}", userAvatarUrl, userUrl)
            .WithTitle("Suggestion / Request module activated...")
            .WithColor(new Color(0x6441a5))
            .WithDescription(userInput)
            .WithFooter("System Active • 4/30/03, 3:00 AM");

        return builder.Build();
    }
    
    async Task OnInteractionCreated(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent component) return;
        if (component.Data.CustomId != "suggestion_complete") return;

        var user = interaction.User as SocketGuildUser;

        if (user == null || !user.GuildPermissions.Administrator)
        {
            await interaction.RespondAsync("You don't have permission to do this.", ephemeral: true);
            return;
        }

        await component.Message.DeleteAsync();
        await interaction.RespondAsync("Suggestion marked as complete.", ephemeral: true);
    }
    
}