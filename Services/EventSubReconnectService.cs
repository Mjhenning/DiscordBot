using DiscordBot.Modules;
using TwitchLib.EventSub.Websockets;

namespace DiscordBot.Services;

public class EventSubReconnectService
{
    readonly EventSubWebsocketClient _eventSubClient;
    readonly TokenManager _tokenManager;

    public EventSubReconnectService(EventSubWebsocketClient eventSubClient, TokenManager tokenManager)
    {
        _eventSubClient = eventSubClient;
        _tokenManager = tokenManager;

        _eventSubClient.WebsocketDisconnected += OnDisconnected;
    }

    async Task OnDisconnected(object? sender, EventArgs e)
    {
        Logger.Log("[Warning] EventSub disconnected — attempting reconnect...");

        int attempts = 0;

        while (attempts < 5)
        {
            attempts++;
            await Task.Delay(TimeSpan.FromSeconds(attempts * 3)); // back off: 3s, 6s, 9s...

            try
            {
                // Refresh token before reconnecting in case that was the cause
                await _tokenManager.ForceRefreshAsync(TwitchProfile.Broadcaster);
                await _eventSubClient.ReconnectAsync();
                Logger.Log("[Info] EventSub reconnected successfully");
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] EventSub reconnect attempt {attempts}/5 failed: {ex.Message}");
            }
        }

        Logger.Log("[Error] EventSub failed to reconnect after 5 attempts");
    }
}