using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;

namespace DiscordBot.Modules.Linking;

public class LinkModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly TwitchChatService _chatService;
    readonly LinkedAccountsData _linkedData;
    readonly DiscordSocketClient _discord;

    static readonly ConcurrentDictionary<ulong, PendingLink> _pendingLinks = new();
    static readonly Random _rng = new();

    public LinkModule(TwitchChatService chatService, LinkedAccountsData linkedData, DiscordSocketClient discord)
    {
        _chatService = chatService;
        _linkedData = linkedData;
        _discord = discord;

        _chatService.OnMessageReceived += OnTwitchMessage;
    }

    [SlashCommand("postlink", "Post the account linking embed to this channel")]
    [RequireRole("🔧 Processes")]
    public async Task PostLinkEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle("Account Linking")
            .WithDescription(
                "Link your Twitch and Discord accounts.\n\n" +
                $"Click the button below, then type the generated code in [Twitch chat]({Config.TwitchChatUrl}) within 20 seconds.")
            .WithColor(Color.Teal)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("Link Twitch", "link.twitch", ButtonStyle.Success)
            .Build();

        await RespondAsync(embed: embed, components: components, ephemeral: false);
    }

    [ComponentInteraction("link.twitch")]
    public async Task LinkButtonClicked()
    {
        await DeferAsync(ephemeral: true);

        ulong discordId = Context.User.Id;

        var existing = _linkedData.FindByDiscordId(discordId);
        if (existing != null)
        {
            await FollowupAsync(
                $"You are already linked to Twitch user **{existing.UsrName}**.",
                ephemeral: true
            );
            return;
        }

        if (_pendingLinks.ContainsKey(discordId))
        {
            await FollowupAsync(
                "You already have a pending link code. Wait for it to expire or check your DMs.",
                ephemeral: true
            );
            return;
        }

        string code = GenerateCode();

        var pending = new PendingLink
        {
            DiscordUserId = discordId,
            DiscordUsername = Context.User.Username,
            Code = code,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(20)
        };

        _pendingLinks[discordId] = pending;
        _ = ScheduleExpiry(discordId, pending);

        var msg = await FollowupAsync(
            $"Your link code is: **{code}**\n" +
            $"Type this code in [Twitch chat]({Config.TwitchChatUrl}) within 20 seconds to link your account.",
            ephemeral: true
        );

        pending.FollowupMessage = msg;

        Logger.Log($"[Link] Generated code {code} for {Context.User.Username} (Discord {discordId})");
    }

    void OnTwitchMessage(string twitchUserId, string twitchUsername, string message, string messageId)
    {
        string trimmed = message.Trim();

        foreach (var kvp in _pendingLinks)
        {
            if (DateTimeOffset.UtcNow > kvp.Value.ExpiresAt)
                continue;

            if (!string.Equals(kvp.Value.Code, trimmed, StringComparison.OrdinalIgnoreCase))
                continue;

            var pending = kvp.Value;
            _pendingLinks.TryRemove(kvp.Key, out _);

            _ = CompleteLink(pending, twitchUserId, twitchUsername, messageId);
            break;
        }
    }

    async Task CompleteLink(PendingLink pending, string twitchUserId, string twitchUsername, string messageId)
    {
        try
        {
            _chatService.DeleteMessage(messageId);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Link] Failed to delete Twitch message: {ex.Message}");
        }

        var glosselEntry = _linkedData.FindByTwitchId(twitchUserId);
        if (glosselEntry == null)
        {
            Logger.Log($"[Link] Twitch user {twitchUsername} ({twitchUserId}) not found in GlosselDB");
            await NotifyDiscord(pending.DiscordUserId,
                $"Twitch user **{twitchUsername}** was not found in the database. Contact a mod.");
            return;
        }

        bool linked = _linkedData.Link(twitchUserId, pending.DiscordUserId);
        if (!linked)
        {
            await NotifyDiscord(pending.DiscordUserId, "Link failed. Try again.");
            return;
        }

        Logger.Log($"[Link] Successfully linked Discord {pending.DiscordUsername} ({pending.DiscordUserId}) to Twitch {glosselEntry.UsrName} ({twitchUserId})");

        await NotifyDiscord(pending.DiscordUserId,
            $"Linked to Twitch user **{glosselEntry.UsrName}**!");

        await LogModChannel(pending.DiscordUsername, pending.DiscordUserId, glosselEntry.UsrName, twitchUserId);
    }

    async Task ScheduleExpiry(ulong key, PendingLink pending)
    {
        await Task.Delay(TimeSpan.FromSeconds(25));

        if (_pendingLinks.TryRemove(pending.DiscordUserId, out var stillPending) && stillPending == pending)
        {
            Logger.Log($"[Link] Code {pending.Code} expired for {pending.DiscordUsername}");

            if (pending.FollowupMessage != null)
            {
                try
                {
                    await pending.FollowupMessage.ModifyAsync(x =>
                        x.Content = "Link code expired. Click the button again to get a new code.");
                }
                catch { }
            }

            await NotifyDiscord(pending.DiscordUserId,
                "Link code expired. Click the button again to get a new code.");
        }
    }

    async Task NotifyDiscord(ulong userId, string message)
    {
        try
        {
            var user = await ((IDiscordClient)_discord).GetUserAsync(userId, CacheMode.AllowDownload);
            if (user != null)
            {
                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Link] Failed to DM Discord user {userId}: {ex.Message}");
        }
    }

    async Task LogModChannel(string discordUsername, ulong discordId, string twitchName, string twitchId)
    {
        try
        {
            var channel = _discord.GetChannel(Config.ModLogChannelId) as IMessageChannel;
            if (channel == null) return;

            var embed = new EmbedBuilder()
                .WithTitle("Account Linked")
                .WithColor(Color.Green)
                .AddField("Discord", $"@{discordUsername} (<@{discordId}>)", true)
                .AddField("Twitch", $"@{twitchName} (ID: {twitchId})", true)
                .WithCurrentTimestamp()
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Link] Failed to send mod log: {ex.Message}");
        }
    }

    static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 5).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
    }

    class PendingLink
    {
        public ulong DiscordUserId { get; init; }
        public string DiscordUsername { get; init; } = "";
        public string Code { get; init; } = "";
        public DateTimeOffset ExpiresAt { get; init; }
        public IUserMessage? FollowupMessage { get; set; }
    }
}
