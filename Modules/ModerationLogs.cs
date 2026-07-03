using Discord;
using Discord.WebSocket;

namespace DiscordBot.Modules;

public class ModerationLogs
{
    private readonly DiscordSocketClient _client;

    public ModerationLogs(DiscordSocketClient client)
    {
        _client = client;

        Logger.Log("[Info] Initializing moderation logger...");

        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;

        _client.UserJoined += OnUserJoined;
        _client.UserLeft += OnUserLeft;

        _client.GuildMemberUpdated += OnGuildMemberUpdated;
        _client.UserUpdated += OnUserUpdated;

        Logger.Log("[Info] Moderation logger initialized.");
    }

    private async Task<IMessageChannel?> GetLogChannel()
    {
        return _client.GetChannel(Config.ModLogChannelId) as IMessageChannel;
    }

    private EmbedBuilder CreateEmbed(string title, Color color)
    {
        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .WithCurrentTimestamp();
    }

    private async Task LogAsync(Embed embed)
    {
        try
        {
            var channel = await GetLogChannel();

            if (channel == null)
            {
                Logger.Log($"[Warning] Moderation log channel ({Config.ModLogChannelId}) could not be found.");
                return;
            }

            await channel.SendMessageAsync(embed: embed);

            Logger.Log($"[Debug] Sent moderation log: {embed.Title}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Error] Failed to send moderation log: {ex}");
        }
    }

    // =====================================================
    // MESSAGE LOGS
    // =====================================================

    private async Task OnMessageUpdated(
        Cacheable<IMessage, ulong> beforeCache,
        SocketMessage after,
        ISocketMessageChannel channel)
    {
        var before = await beforeCache.GetOrDownloadAsync();

        if (before == null)
            return;

        if (before.Author.IsBot)
            return;

        if (before.Content == after.Content)
            return;

        Logger.Log($"[Log] Message edited by {before.Author.Username} in #{channel.Name}");

        var embed = CreateEmbed("✏️ Message Edited", Color.Orange)
            .AddField("User", before.Author.Mention, true)
            .AddField("Channel", channel.Name, true)
            .AddField("Before",
                string.IsNullOrWhiteSpace(before.Content) ? "*No text*" : before.Content)
            .AddField("After",
                string.IsNullOrWhiteSpace(after.Content) ? "*No text*" : after.Content);

        await LogAsync(embed.Build());
    }

    private async Task OnMessageDeleted(
        Cacheable<IMessage, ulong> cache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var message = await cache.GetOrDownloadAsync();

        if (message == null)
            return;

        if (message.Author.IsBot)
            return;

        var channel = await channelCache.GetOrDownloadAsync();

        Logger.Log($"[Log] Message deleted by {message.Author.Username} in #{channel?.Name ?? "Unknown"}");

        var embed = CreateEmbed("🗑️ Message Deleted", Color.Red)
            .AddField("User", message.Author.Mention, true)
            .AddField("Channel", channel?.Name ?? "Unknown", true)
            .AddField("Content",
                string.IsNullOrWhiteSpace(message.Content) ? "*No text*" : message.Content);

        await LogAsync(embed.Build());
    }

    // =====================================================
    // MEMBER LOGS
    // =====================================================

    private async Task OnUserJoined(SocketGuildUser user)
    {
        Logger.Log($"[Log] {user.Username} joined {user.Guild.Name}");

        var embed = CreateEmbed("📥 Member Joined", Color.Green)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true)
            .AddField("Account Created", $"<t:{user.CreatedAt.ToUnixTimeSeconds()}:F>");

        await LogAsync(embed.Build());
    }

    private async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        Logger.Log($"[Log] {user.Username} left {guild.Name}");

        var embed = CreateEmbed("📤 Member Left", Color.DarkGrey)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true);

        await LogAsync(embed.Build());
    }

    // =====================================================
    // MEMBER / ROLE CHANGES
    // =====================================================

    private async Task OnGuildMemberUpdated(
        Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        var before = await beforeCache.GetOrDownloadAsync();

        if (before == null)
            return;

        // Nickname changed
        if (before.Nickname != after.Nickname)
        {
            Logger.Log($"[Log] {after.Username} changed nickname from '{before.Nickname ?? "None"}' to '{after.Nickname ?? "None"}'");

            var embed = CreateEmbed("📝 Nickname Changed", Color.Blue)
                .AddField("User", after.Mention)
                .AddField("Before", before.Nickname ?? "*None*")
                .AddField("After", after.Nickname ?? "*None*");

            await LogAsync(embed.Build());
        }

        // Roles Added
        foreach (var role in after.Roles.Except(before.Roles))
        {
            Logger.Log($"[Log] Role '{role.Name}' added to {after.Username}");

            var embed = CreateEmbed("➕ Role Added", Color.Green)
                .AddField("User", after.Mention, true)
                .AddField("Role", role.Mention, true);

            await LogAsync(embed.Build());
        }

        // Roles Removed
        foreach (var role in before.Roles.Except(after.Roles))
        {
            Logger.Log($"[Log] Role '{role.Name}' removed from {after.Username}");

            var embed = CreateEmbed("➖ Role Removed", Color.Red)
                .AddField("User", after.Mention, true)
                .AddField("Role", role.Mention, true);

            await LogAsync(embed.Build());
        }
    }

    // =====================================================
    // USER PROFILE CHANGES
    // =====================================================

    private async Task OnUserUpdated(SocketUser before, SocketUser after)
    {
        // Username changed
        if (before.Username != after.Username)
        {
            Logger.Log($"[Log] Username changed from '{before.Username}' to '{after.Username}'");

            var embed = CreateEmbed("👤 Username Changed", Color.Purple)
                .AddField("User", after.Mention)
                .AddField("Before", before.Username)
                .AddField("After", after.Username);

            await LogAsync(embed.Build());
        }

        // Avatar changed
        if (before.GetAvatarUrl() != after.GetAvatarUrl())
        {
            Logger.Log($"[Log] {after.Username} changed their avatar.");

            var embed = CreateEmbed("🖼️ Avatar Changed", Color.Teal)
                .AddField("User", after.Mention);

            embed.WithThumbnailUrl(after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl());

            await LogAsync(embed.Build());
        }
    }
}