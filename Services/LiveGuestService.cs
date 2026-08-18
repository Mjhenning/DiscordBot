using Discord;
using Discord.WebSocket;

namespace DiscordBot.Services;

public class LiveGuestService
{
    readonly DiscordSocketClient _client;

    public LiveGuestService(DiscordSocketClient client)
    {
        _client = client;
        _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
        Logger.Log("[LiveGuest] Service initialized.");
    }

    async Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
    {
        if (oldState.VoiceChannel == null) return;
        if (newState.VoiceChannel != null) return;

        if (user is not SocketGuildUser guildUser) return;

        SocketRole? role = guildUser.Guild.GetRole(Config.LiveGuestRoleId);
        if (role == null) return;

        if (!guildUser.Roles.Any(r => r.Id == role.Id)) return;

        try
        {
            await guildUser.RemoveRoleAsync(role,
                new RequestOptions { AuditLogReason = "Auto-removed: Left-over relay.guest role" });
            Logger.Log($"[LiveGuest] Auto-removed role from {guildUser.Username} (left voice)");
        }
        catch (Exception ex)
        {
            Logger.Log($"[LiveGuest] Failed to auto-remove role from {guildUser.Username}: {ex.Message}");
        }
    }
}
