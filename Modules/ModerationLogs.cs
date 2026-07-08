using System.Collections.Concurrent;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace DiscordBot.Modules;

public class ModerationLogs
{
    enum LogCategory
    {
        Message,
        Member,
        Voice,
        Channel,
        Role,
        Invite,
        Thread,
        AutoMod,
        ScheduledEvent,
        Webhook,
        Integration
    }
    
    
    readonly DiscordSocketClient _client;

    // Tracks in-progress voice channel sessions: channelId -> session info
    readonly ConcurrentDictionary<ulong, VoiceSessionInfo> _voiceSessions = new();

    class VoiceSessionInfo
    {
        public DateTimeOffset StartTime { get; init; }
        public Dictionary<ulong, string> Participants { get; } = new();
    }

    public ModerationLogs(DiscordSocketClient client)
    {
        _client = client;

        Logger.Log("[ModLogs] Initializing moderation logger...");
        
        RegisterEvents();

        Logger.Log("[ModLogs] Moderation logger initialized.");
    }
    
    static bool IsEnabled(LogCategory category)
    {
        return category switch
        {
            LogCategory.Message => Config.LogMessages,
            LogCategory.Member => Config.LogMembers,
            LogCategory.Voice => Config.LogVoice,
            LogCategory.Channel => Config.LogChannels,
            LogCategory.Role => Config.LogRoles,
            LogCategory.Invite => Config.LogInvites,
            LogCategory.Thread => Config.LogThreads,
            LogCategory.AutoMod => Config.LogAutoMod,
            LogCategory.ScheduledEvent => Config.LogEvents,
            LogCategory.Webhook => Config.LogWebhooks,
            LogCategory.Integration => Config.LogIntegrations,
            _ => true
        };
    }

    void RegisterEvents()
    {
        RegisterMessageEvents();
        RegisterMemberEvents();
        RegisterVoiceEvents();
        RegisterChannelEvents();
        RegisterRoleEvents();
        RegisterInviteEvents();
        RegisterThreadEvents();
        RegisterAutoModEvents();
        RegisterScheduledEventEvents();
        RegisterWebhookEvents();
        RegisterIntegrationEvents();
    }

    void RegisterMessageEvents()
    {
        if (!IsEnabled(LogCategory.Message))
            return;

        _client.MessageUpdated += OnMessageUpdated;
        _client.MessageDeleted += OnMessageDeleted;
        // _client.MessagesBulkDeleted += OnMessagesBulkDeleted;
        // _client.ReactionAdded += OnReactionAdded;
        // _client.ReactionRemoved += OnReactionRemoved;
        // _client.ReactionsCleared += OnReactionsCleared;
        // _client.ReactionsRemovedForEmote += OnReactionsRemovedForEmote;
    }

    void RegisterMemberEvents()
    {
        if (!IsEnabled(LogCategory.Member))
            return;

        _client.UserJoined += OnUserJoined;
        _client.UserLeft += OnUserLeft;
        _client.UserBanned += OnUserBanned;
        _client.UserUnbanned += OnUserUnbanned;
        _client.GuildMemberUpdated += OnGuildMemberUpdated;
        _client.UserUpdated += OnUserUpdated;
    }

    void RegisterVoiceEvents()
    {
        if (!IsEnabled(LogCategory.Voice))
            return;

        _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
        // _client.VoiceChannelStatusUpdated += OnVoiceChannelStatusUpdated;
    }

    void RegisterChannelEvents()
    {
        if (!IsEnabled(LogCategory.Channel))
            return;

        // _client.ChannelCreated += OnChannelCreated;
        // _client.ChannelDestroyed += OnChannelDestroyed;
        // _client.ChannelUpdated += OnChannelUpdated;
    }

    void RegisterRoleEvents()
    {
        if (!IsEnabled(LogCategory.Role))
            return;

        // _client.RoleCreated += OnRoleCreated;
        // _client.RoleDeleted += OnRoleDeleted;
        // _client.RoleUpdated += OnRoleUpdated;
    }

    void RegisterInviteEvents()
    {
        if (!IsEnabled(LogCategory.Invite))
            return;

        // _client.InviteCreated += OnInviteCreated;
        // _client.InviteDeleted += OnInviteDeleted;
    }

    void RegisterThreadEvents()
    {
        if (!IsEnabled(LogCategory.Thread))
            return;

        // _client.ThreadCreated += OnThreadCreated;
        // _client.ThreadUpdated += OnThreadUpdated;
        // _client.ThreadDeleted += OnThreadDeleted;
        // _client.ThreadMemberJoined += OnThreadMemberJoined;
        // _client.ThreadMemberLeft += OnThreadMemberLeft;
    }

    void RegisterAutoModEvents() { }
    void RegisterScheduledEventEvents() { }
    void RegisterWebhookEvents() { }
    void RegisterIntegrationEvents() { }

    async Task<IMessageChannel?> GetLogChannel()
    {
        return _client.GetChannel(Config.ModLogChannelId) as IMessageChannel;
    }

    EmbedBuilder CreateEmbed(string title, Color color)
    {
        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .WithCurrentTimestamp();
    }

    async Task LogAsync(Embed embed)
    {
        try
        {
            var channel = await GetLogChannel();

            if (channel == null)
            {
                Logger.Log($"[ModLogs] Moderation log channel ({Config.ModLogChannelId}) could not be found.");
                return;
            }

            await channel.SendMessageAsync(embed: embed);

            Logger.Log($"[ModLogs] Sent moderation log: {embed.Title}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[ModLogs] Failed to send moderation log: {ex}");
        }
    }

    // =====================================================
    // AUDIT LOG HELPER
    // =====================================================
    // Every "who did this" lookup below shares this one method.
    // Discord doesn't push audit log entries to us directly, so
    // whenever we see an event that could've been caused by a mod
    // action (kick, ban, role change, message delete) we pull the
    // most recent matching audit log entry and check it happened
    // just now (within `maxAge`) so we don't misattribute an old
    // unrelated action to a fresh event.

     async Task<(IUser? Moderator, string? Reason)> TryGetAuditLogModeratorAsync(
        SocketGuild guild,
        ActionType actionType,
        Func<Discord.Rest.RestAuditLogEntry, bool> matches,
        TimeSpan maxAge)
    {
        try
        {
            await foreach (var page in guild.GetAuditLogsAsync(10, actionType: actionType))
            {
                foreach (var entry in page)
                {
                    if (DateTimeOffset.UtcNow - entry.CreatedAt > maxAge)
                        continue;

                    if (matches(entry))
                        return (entry.User, entry.Reason);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ModLogs] Failed to query audit logs ({actionType}): {ex.Message}");
        }

        return (null, null);
    }

     static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }

    // =====================================================
    // MESSAGE LOGS
    // =====================================================

     async Task OnMessageUpdated(
        Cacheable<IMessage, ulong> beforeCache,
        SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (!IsEnabled(LogCategory.Message))
            return;
        
        var before = await beforeCache.GetOrDownloadAsync();

        if (before == null)
            return;

        if (before.Author.IsBot)
            return;

        if (before.Content == after.Content)
            return;

        Logger.Log($"[ModLogs] Message edited by {before.Author.Username} in #{channel.Name}");

        var embed = CreateEmbed("✏️ Message Edited", Color.Orange)
            .AddField("User", before.Author.Mention, true)
            .AddField("Channel", channel.Name, true)
            .AddField("Before",
                string.IsNullOrWhiteSpace(before.Content) ? "*No text*" : before.Content)
            .AddField("After",
                string.IsNullOrWhiteSpace(after.Content) ? "*No text*" : after.Content);

        await LogAsync(embed.Build());
    }

     async Task OnMessageDeleted(
        Cacheable<IMessage, ulong> cache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        if (!IsEnabled(LogCategory.Message))
            return;
        
        var message = await cache.GetOrDownloadAsync();

        if (message == null)
            return;

        if (message.Author.IsBot)
            return;

        var channel = await channelCache.GetOrDownloadAsync();

        Logger.Log($"[ModLogs] Message deleted by {message.Author.Username} in #{channel?.Name ?? "Unknown"}");

        var embed = CreateEmbed("🗑️ Message Deleted", Color.Red)
            .AddField("User", message.Author.Mention, true)
            .AddField("Channel", channel?.Name ?? "Unknown", true)
            .AddField("Content",
                string.IsNullOrWhiteSpace(message.Content) ? "*No text*" : message.Content);

        // Try to attribute the deletion to a moderator (if it wasn't the author deleting their own message).
        if (channel is SocketGuildChannel guildChannel)
        {
            var (moderator, reason) = await TryGetAuditLogModeratorAsync(
                guildChannel.Guild,
                ActionType.MessageDeleted,
                entry => entry.Data is MessageDeleteAuditLogData data
                    && data.Target.Id == message.Author.Id
                    && data.ChannelId == channel.Id,
                TimeSpan.FromSeconds(10));

            if (moderator != null)
            {
                embed.AddField("Deleted By", moderator.Mention, true);
                if (!string.IsNullOrWhiteSpace(reason))
                    embed.AddField("Reason", reason);
            }
        }

        await LogAsync(embed.Build());
    }

    // =====================================================
    // MEMBER LOGS
    // =====================================================

     async Task OnUserJoined(SocketGuildUser user)
    {
        if (!IsEnabled(LogCategory.Member))
            return;
        
        Logger.Log($"[ModLogs] {user.Username} joined {user.Guild.Name}");

        var embed = CreateEmbed("📥 Member Joined", Color.Green)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true)
            .AddField("Account Created", $"<t:{user.CreatedAt.ToUnixTimeSeconds()}:F>");

        await LogAsync(embed.Build());
    }

     async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        // A kick looks identical to a normal leave from Discord's gateway perspective —
        // the only way to tell them apart is checking the audit log for a very recent
        // Kick entry targeting this user.
        
        if (!IsEnabled(LogCategory.Member))
            return;
        
        var (moderator, reason) = await TryGetAuditLogModeratorAsync(
            guild,
            ActionType.Kick,
            entry => entry.Data is KickAuditLogData data && data.Target.Id == user.Id,
            TimeSpan.FromSeconds(5));

        if (moderator != null)
        {
            Logger.Log($"[ModLogs] {user.Username} was kicked from {guild.Name} by {moderator.Username}");

            var kickEmbed = CreateEmbed("👢 Member Kicked", Color.DarkOrange)
                .AddField("User", user.Mention, true)
                .AddField("Username", user.Username, true)
                .AddField("Kicked By", moderator.Mention, true);

            if (!string.IsNullOrWhiteSpace(reason))
                kickEmbed.AddField("Reason", reason);

            await LogAsync(kickEmbed.Build());
            return;
        }

        Logger.Log($"[ModLogs] {user.Username} left {guild.Name}");

        var embed = CreateEmbed("📤 Member Left", Color.DarkGrey)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true);

        await LogAsync(embed.Build());
    }

    // =====================================================
    // BAN / UNBAN
    // =====================================================

     async Task OnUserBanned(SocketUser user, SocketGuild guild)
    {
        if (!IsEnabled(LogCategory.Member))
            return;
        
        var (moderator, reason) = await TryGetAuditLogModeratorAsync(
            guild,
            ActionType.Ban,
            entry => entry.Data is BanAuditLogData data && data.Target.Id == user.Id,
            TimeSpan.FromSeconds(5));

        Logger.Log($"[ModLogs] {user.Username} was banned from {guild.Name}" +
                   (moderator != null ? $" by {moderator.Username}" : ""));

        var embed = CreateEmbed("🔨 Member Banned", Color.Red)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true);

        if (moderator != null)
            embed.AddField("Banned By", moderator.Mention, true);

        if (!string.IsNullOrWhiteSpace(reason))
            embed.AddField("Reason", reason);

        await LogAsync(embed.Build());
    }

     async Task OnUserUnbanned(SocketUser user, SocketGuild guild)
    {
        if (!IsEnabled(LogCategory.Member))
            return;
        
        var (moderator, reason) = await TryGetAuditLogModeratorAsync(
            guild,
            ActionType.Unban,
            entry => entry.Data is UnbanAuditLogData data && data.Target.Id == user.Id,
            TimeSpan.FromSeconds(5));

        Logger.Log($"[ModLogs] {user.Username} was unbanned from {guild.Name}" +
                   (moderator != null ? $" by {moderator.Username}" : ""));

        var embed = CreateEmbed("🕊️ Member Unbanned", Color.Teal)
            .AddField("User", user.Mention, true)
            .AddField("Username", user.Username, true);

        if (moderator != null)
            embed.AddField("Unbanned By", moderator.Mention, true);

        if (!string.IsNullOrWhiteSpace(reason))
            embed.AddField("Reason", reason);

        await LogAsync(embed.Build());
    }

    // =====================================================
    // MEMBER / ROLE CHANGES
    // =====================================================

     async Task OnGuildMemberUpdated(
        Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        if (!IsEnabled(LogCategory.Member))
            return;
        
        var before = await beforeCache.GetOrDownloadAsync();

        if (before == null)
            return;

        // Nickname changed
        if (before.Nickname != after.Nickname)
        {
            Logger.Log($"[ModLogs] {after.Username} changed nickname from '{before.Nickname ?? "None"}' to '{after.Nickname ?? "None"}'");

            var embed = CreateEmbed("📝 Nickname Changed", Color.Blue)
                .AddField("User", after.Mention)
                .AddField("Before", before.Nickname ?? "*None*")
                .AddField("After", after.Nickname ?? "*None*");

            await LogAsync(embed.Build());
        }

        // Roles Added / Removed — look up who made the change once, reuse for both.
        var rolesAdded = after.Roles.Except(before.Roles).ToList();
        var rolesRemoved = before.Roles.Except(after.Roles).ToList();

        if (rolesAdded.Count > 0 || rolesRemoved.Count > 0)
        {
            var (moderator, reason) = await TryGetAuditLogModeratorAsync(
                after.Guild,
                ActionType.MemberRoleUpdated,
                entry => entry.Data is MemberRoleAuditLogData data && data.Target.Id == after.Id,
                TimeSpan.FromSeconds(5));

            foreach (var role in rolesAdded)
            {
                Logger.Log($"[ModLogs] Role '{role.Name}' added to {after.Username}" +
                           (moderator != null ? $" by {moderator.Username}" : ""));

                var embed = CreateEmbed("➕ Role Added", Color.Green)
                    .AddField("User", after.Mention, true)
                    .AddField("Role", role.Mention, true);

                if (moderator != null)
                    embed.AddField("Changed By", moderator.Mention, true);
                if (!string.IsNullOrWhiteSpace(reason))
                    embed.AddField("Reason", reason);

                await LogAsync(embed.Build());
            }

            foreach (var role in rolesRemoved)
            {
                Logger.Log($"[ModLogs] Role '{role.Name}' removed from {after.Username}" +
                           (moderator != null ? $" by {moderator.Username}" : ""));

                var embed = CreateEmbed("➖ Role Removed", Color.Red)
                    .AddField("User", after.Mention, true)
                    .AddField("Role", role.Mention, true);

                if (moderator != null)
                    embed.AddField("Changed By", moderator.Mention, true);
                if (!string.IsNullOrWhiteSpace(reason))
                    embed.AddField("Reason", reason);

                await LogAsync(embed.Build());
            }
        }
    }

    // =====================================================
    // USER PROFILE CHANGES
    // =====================================================

     async Task OnUserUpdated(SocketUser before, SocketUser after)
    {
        if (!IsEnabled(LogCategory.Member))
            return;
        
        // Username changed
        if (before.Username != after.Username)
        {
            Logger.Log($"[ModLogs] Username changed from '{before.Username}' to '{after.Username}'");

            var embed = CreateEmbed("👤 Username Changed", Color.Purple)
                .AddField("User", after.Mention)
                .AddField("Before", before.Username)
                .AddField("After", after.Username);

            await LogAsync(embed.Build());
        }

        // Avatar changed
        if (before.GetAvatarUrl() != after.GetAvatarUrl())
        {
            Logger.Log($"[ModLogs] {after.Username} changed their avatar.");

            var embed = CreateEmbed("🖼️ Avatar Changed", Color.Teal)
                .AddField("User", after.Mention);

            embed.WithThumbnailUrl(after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl());

            await LogAsync(embed.Build());
        }
    }

    // =====================================================
    // VOICE CHANNEL ACTIVITY
    // =====================================================
    // Tracks who's been in a voice channel since it first became non-empty.
    // When the last person leaves, posts a summary: how long the channel
    // was active, who left last, and everyone who passed through it during
    // that session (not just who happened to be there at the end).

     async Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (!IsEnabled(LogCategory.Voice))
            return;
        
        var leftChannel = before.VoiceChannel;
        var joinedChannel = after.VoiceChannel;

        if (leftChannel?.Id == joinedChannel?.Id)
            return; // mute/deafen/etc. toggle, no channel change

        if (joinedChannel != null)
        {
            var session = _voiceSessions.GetOrAdd(joinedChannel.Id,
                _ => new VoiceSessionInfo { StartTime = DateTimeOffset.UtcNow });

            lock (session.Participants)
            {
                session.Participants[user.Id] = user.Username;
            }

            Logger.Log($"[ModLogs] {user.Username} joined voice channel '{joinedChannel.Name}'");
        }

        if (leftChannel != null)
        {
            Logger.Log($"[ModLogs] {user.Username} left voice channel '{leftChannel.Name}'");

            // Only fire the summary once the channel is actually empty.
            if (leftChannel.ConnectedUsers.Count == 0 && _voiceSessions.TryRemove(leftChannel.Id, out var session))
            {
                var duration = DateTimeOffset.UtcNow - session.StartTime;

                string participantList;
                lock (session.Participants)
                {
                    participantList = session.Participants.Count > 0
                        ? string.Join("\n", session.Participants.Values)
                        : "*Unknown*";
                }

                var embed = CreateEmbed("🔇 Voice Channel Emptied", Color.DarkGrey)
                    .AddField("Channel", leftChannel.Name, true)
                    .AddField("Active For", FormatDuration(duration), true)
                    .AddField("Last To Leave", user.Username, true)
                    .AddField($"All Participants ({session.Participants.Count})", participantList);

                await LogAsync(embed.Build());
            }
        }
    }
}