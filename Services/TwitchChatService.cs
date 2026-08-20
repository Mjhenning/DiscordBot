using DiscordBot.Modules;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;

namespace DiscordBot.Services;

public class TwitchChatService
{
    readonly TwitchLib.Client.TwitchClient _client;
    readonly TokenManager _tokens;

    public event Action<string, string, string, string>? OnMessageReceived;

    public TwitchChatService(TokenManager tokens)
    {
        _tokens = tokens;
        _client = new TwitchLib.Client.TwitchClient();
    }

    public async Task ConnectAsync()
    {
        var token = await _tokens.GetValidAccessTokenAsync(TwitchProfile.Bot);
        var credentials = new ConnectionCredentials(
            twitchUsername: Config.TwitchBotName,
            twitchOAuth: "oauth:" + token
        );

        _client.Initialize(credentials);
        _client.OnMessageReceived += OnIrcMessageReceived;

        _client.Connect();
        await Task.Delay(2000);
        _client.JoinChannel(Config.TwitchChannelName);
        Logger.Log($"[TwitchChat] Connected and joined #{Config.TwitchChannelName} as {Config.TwitchBotName}");
    }

    void OnIrcMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        var msg = e.ChatMessage;
        OnMessageReceived?.Invoke(msg.UserId, msg.Username, msg.Message, msg.Id);
    }

    public void SendMessage(string message)
    {
        _client.SendMessage(Config.TwitchChannelName, message);
    }

    public void DeleteMessage(string messageId)
    {
        _client.DeleteMessage(Config.TwitchChannelName, messageId);
    }
}
