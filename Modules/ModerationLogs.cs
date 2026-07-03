using Discord;
using Discord.WebSocket;

namespace DiscordBot.Modules;

public class ModerationLogs
{
    private readonly DiscordSocketClient _client;

    public ModerationLogs(DiscordSocketClient client)
    {
        _client = client;

        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;

        _client.UserJoined += OnUserJoined;
        _client.UserLeft += OnUserLeft;

        _client.GuildMemberUpdated += OnGuildMemberUpdated;
        _client.UserUpdated += OnUserUpdated;
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

        var log = await GetLogChannel();
        if (log == null)
            return;

        var embed = CreateEmbed("✏️ Message Edited", Color.Orange)
            .AddField("User", before.Author.Mention, true)
            .AddField("Channel", channel.Name, true)
            .AddField("Before",
                string.IsNullOrWhiteSpace(before.Content) ? "*No text*" : before.Content)
            .AddField("After",
                string.IsNullOrWhiteSpace(after.Content) ? "*No text*" : after.Content);

        await log.SendMessageAsync(embed: embed.Build());
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

        var log = await GetLogChannel();
        if (log == null)
            return;

        var channel = await channelCache.GetOrDownloadAsync();

        var embed = CreateEmbed("🗑️ Message Deleted", Color.Red)
            .AddField("User", message.Author.Mention, true)
            .AddField("Channel", channel?.Name ?? "Unknown", true)
            .AddField("Content",
                string.IsNullOrWhiteSpace(message.Content) ? "*No text*" : message.Content);

        await log.SendMessageAsync(embed: embed.Build());
    }

    // =====================================================
    // MEMBER LOGS
    // =====================================================

    private async Task OnUserJoined(SocketGuildUser user)
    {
        var log = await GetLogChannel();
        if (log == null)
            return;

        var embed = CreateEmbed("📥 Member Joined", Color.Green)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true)
            .AddField("Account Created", $"<t:{user.CreatedAt.ToUnixTimeSeconds()}:F>");

        await log.SendMessageAsync(embed: embed.Build());
    }

    private async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        var log = await GetLogChannel();
        if (log == null)
            return;

        var embed = CreateEmbed("📤 Member Left", Color.DarkGrey)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true);

        await log.SendMessageAsync(embed: embed.Build());
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

        var log = await GetLogChannel();
        if (log == null)
            return;

        // Nickname changed
        if (before.Nickname != after.Nickname)
        {
            var embed = CreateEmbed("📝 Nickname Changed", Color.Blue)
                .AddField("User", after.Mention)
                .AddField("Before", before.Nickname ?? "*None*")
                .AddField("After", after.Nickname ?? "*None*");

            await log.SendMessageAsync(embed: embed.Build());
        }

        // Roles Added
        foreach (var role in after.Roles.Except(before.Roles))
        {
            var embed = CreateEmbed("➕ Role Added", Color.Green)
                .AddField("User", after.Mention, true)
                .AddField("Role", role.Mention, true);

            await log.SendMessageAsync(embed: embed.Build());
        }

        // Roles Removed
        foreach (var role in before.Roles.Except(after.Roles))
        {
            var embed = CreateEmbed("➖ Role Removed", Color.Red)
                .AddField("User", after.Mention, true)
                .AddField("Role", role.Mention, true);

            await log.SendMessageAsync(embed: embed.Build());
        }
    }

    // =====================================================
    // USER PROFILE CHANGES
    // =====================================================

    private async Task OnUserUpdated(SocketUser before, SocketUser after)
    {
        var log = await GetLogChannel();
        if (log == null)
            return;

        // Username changed
        if (before.Username != after.Username)
        {
            var embed = CreateEmbed("👤 Username Changed", Color.Purple)
                .AddField("User", after.Mention)
                .AddField("Before", before.Username)
                .AddField("After", after.Username);

            await log.SendMessageAsync(embed: embed.Build());
        }

        // Avatar changed
        if (before.GetAvatarUrl() != after.GetAvatarUrl())
        {
            var embed = CreateEmbed("🖼️ Avatar Changed", Color.Teal)
                .AddField("User", after.Mention);

            embed.WithThumbnailUrl(after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl());

            await log.SendMessageAsync(embed: embed.Build());
        }
    }
}