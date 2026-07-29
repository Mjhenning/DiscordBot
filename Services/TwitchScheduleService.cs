using DiscordBot.Data;
using DiscordBot.Modules;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Schedule.CreateChannelStreamSegment;

namespace DiscordBot.Services;

public class TwitchScheduleService
{
    readonly TwitchAPI _api;
    readonly TokenManager _tokens;

    // ── Tweak these ─────────────────────────────────────────────
    const int    DefaultDurationMinutes = 240; // 4 hours
    const string Timezone               = "Africa/Johannesburg"; // IANA tz name
    // ───────────────────────────────────────────────────────────

    public TwitchScheduleService(TwitchAPI api, TokenManager tokens)
    {
        _api    = api;
        _tokens = tokens;
    }

    public async Task<string?> CreateSegmentAsync(ScheduleEntry entry)
    {
        CreateChannelStreamSegmentRequest payload = new()
        {
            StartTime   = entry.ScheduledAtParsed.UtcDateTime,
            Timezone    = Timezone,
            Duration    = DefaultDurationMinutes.ToString(),
            IsRecurring = false,
            Title       = entry.Description
        };

        var response = await _tokens.WithTokenRetryAsync(TwitchProfile.Broadcaster, token =>
            _api.Helix.Schedule.CreateChannelStreamScheduleSegmentAsync(Config.TwitchUserId, payload, token));

        string? segmentId = response?.Schedule?.Segments?.FirstOrDefault()?.Id;

        if (segmentId == null)
            Logger.Log($"[Warning] Twitch returned no segment ID for '{entry.Description}'");
        else
            Logger.Log($"[Info] Twitch schedule segment created for '{entry.Description}' ({segmentId})");

        return segmentId;
    }

    public async Task DeleteSegmentAsync(string segmentId)
    {
        await _tokens.WithTokenRetryAsync(TwitchProfile.Broadcaster, async token =>
        {
            await _api.Helix.Schedule.DeleteChannelStreamScheduleSegmentAsync(Config.TwitchUserId, segmentId, token);
            return true; // WithTokenRetryAsync is generic; Delete has no return value, so dummy it
        });

        Logger.Log($"[Info] Twitch schedule segment deleted ({segmentId})");
    }
}