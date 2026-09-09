using Discord.WebSocket;
using DiscordBot.Data;
using DiscordBot.Models;

namespace DiscordBot.Services;

public class CollabCleanupService
{
    // how often we sweep for collabs whose date has passed
    const int SweepIntervalHours = 1;

    readonly CollabData _data;
    readonly CollabService _collabService;
    readonly DiscordSocketClient _client;

    Timer? _timer;

    public CollabCleanupService(
        CollabData data,
        CollabService collabService,
        DiscordSocketClient client)
    {
        _data = data;
        _collabService = collabService;
        _client = client;

        StartSweep();

        Logger.Log("[Collab] Expiry sweep scheduled");
    }

    void StartSweep()
    {
        // run once now, then on the interval
        _ = SweepExpiredAsync();

        _timer = new Timer(
            async _ => await SweepExpiredAsync(),
            null,
            TimeSpan.FromHours(SweepIntervalHours),
            TimeSpan.FromHours(SweepIntervalHours));
    }

    async Task SweepExpiredAsync()
    {
        try
        {
            List<CollabEntry> expired = _data.RemoveExpired();

            if (expired.Count == 0)
                return;

            Logger.Log($"[Collab] Removing {expired.Count} expired collaboration(s)");

            await _collabService.DeleteDmMessagesAsync(expired, _client);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Error] Collab expiry sweep failed: {ex.Message}");
        }
    }
}
