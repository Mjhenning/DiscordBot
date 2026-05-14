using Discord;
using Discord.WebSocket;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomRewardRedemption;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace DiscordBot.Modules;

public class TwitchSuggestionsPoster
{
    readonly EventSubWebsocketClient _eventSubClient;
    private readonly DiscordSocketClient _discordSocket;
    private readonly TwitchAPI TwitchApi;
    
    private readonly StreamWriter _log;
    
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
    
    async void OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (!e.IsRequestedReconnect)
        {
            await TwitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.channel_points_custom_reward_redemption.update",
                "1",
                new Dictionary<string, string>
                {
                    { "broadcaster_user_id", Config.TwitchUserId },
                    { "reward_id", Config.SuggestRewardId}
                },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Webhook,
                _eventSubClient.SessionId
            );
        }
    }

    async void OnRewardRedemptionUpdated(object? sender, ChannelPointsCustomRewardRedemptionArgs args)
    {
        var redemption = args.Notification.Payload.Event;

        if (redemption.Status != "fulfilled") return;
        
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

        Embed embed = BuildTwitchEmbed(user, input, avatarUrl, userUrl);
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