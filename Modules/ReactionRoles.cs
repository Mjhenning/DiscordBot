using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;
namespace DiscordBot.Modules;

[Group("reactionrole", "Manage reaction roles")]
public class ReactionRolesModule : InteractionModuleBase<SocketInteractionContext>
{
    // In-memory setup session per user — keyed by userId
    // Stores partial data while the user works through the UI steps
    public static readonly Dictionary<ulong, ReactionSetupSession> Sessions = new();

    readonly ReactionsData _data;

    // Discord.NET's DI system injects ReactionsData for you (register it in your service provider)
    public ReactionRolesModule(ReactionsData data)
    {
        _data = data;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 1 — Slash command entry point: show a channel select menu
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("create", "Set up a reaction role on a message")]
    public async Task SetupStart()
    {
        // Create (or reset) a session for this user
        Sessions[Context.User.Id] = new ReactionSetupSession();

        // ChannelSelectMenuBuilder lets the user pick any text channel in the guild
        // Docs: SelectMenuBuilder / ChannelSelectMenuBuilder under Discord.ComponentBuilder
        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_channel")               // ID we listen for in ComponentInteraction below
                .WithType(ComponentType.ChannelSelect)    // Renders as a channel picker — no manual population needed
                .WithPlaceholder("Select the channel")
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            "**Step 1:** Choose the channel that contains your target message.",
            components: menu,
            ephemeral: true // ✅ this locks the whole interaction into ephemeral mode
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 2 — User picked a channel; fetch 5 most recent messages & show them
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_channel", ignoreGroupNames: true)]  // Fires when the channel select menu is submitted
    public async Task OnChannelSelected(string[] selectedValues)
    {
        // selectedValues[0] is the channel ID as a string
        ulong channelId = ulong.Parse(selectedValues[0]);

        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        session.ChannelId = channelId;

        // Retrieve the channel from the guild — cast to ITextChannel to access messages
        // Docs: SocketGuild.GetChannel → cast to ITextChannel → GetMessagesAsync
        ITextChannel? channel = Context.Guild.GetChannel(channelId) as ITextChannel;
        if (channel == null)
        {
            await RespondAsync("That channel isn't a text channel.", ephemeral: true);
            return;
        }

        // GetMessagesAsync(limit) — returns the most recent N messages, newest first
        var messages = await channel.GetMessagesAsync(5).FlattenAsync();

        // Build a select menu where each option is a message, labelled by its timestamp
        // The Value we store is the message's ID (ulong) as a string — we parse it back later
        List<SelectMenuOptionBuilder> options = messages.Select(m => new SelectMenuOptionBuilder()
            .WithLabel($"{m.Author.Username} • {m.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}")
            .WithValue(m.Id.ToString()))
            .ToList();

        if (!options.Any())
        {
            await RespondAsync("No messages found in that channel.", ephemeral: true);
            return;
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_message")
                .WithPlaceholder("Select the message")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await DeferAsync(ephemeral: true);

        await FollowupAsync(
            "**Step 2:** Choose the message to attach the reaction role to.",
            components: menu,
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 3 — User picked a message; ask user to react with specific emoji
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_message", ignoreGroupNames: true)]  // Fires when the message select menu is submitted
    public async Task OnMessageSelected(string[] selectedValues)
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        // Save chosen target message
        session.MessageId = ulong.Parse(selectedValues[0]);

        // Mark session as waiting for emoji reaction input
        session.WaitingForEmoji = true;

        await DeferAsync(ephemeral: true);

        await FollowupAsync(
            "**Step 3:** React to the target message with the emoji you want to use.\nAfter you react, setup will continue automatically.",
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 4 — Modal submitted; show role multi-selects (add & remove)
    // ─────────────────────────────────────────────────────────────────────────

    [ModalInteraction("rr_emoji_modal")]  // Fires when the modal is submitted
    public async Task OnEmojiSubmitted(ReactionEmojiModal modal)
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        session.Emoji = modal.Emoji.Trim();

        // Build role options from the guild's role list
        // Filter out @everyone (Id == GuildId) and bot-managed roles
        List<SelectMenuOptionBuilder> roles = Context.Guild.Roles
            .Where(r => r.Id != Context.Guild.Id && !r.IsManaged)
            .OrderByDescending(r => r.Position)
            .Take(25)  // SelectMenu max options is 25 — Discord API hard limit
            .Select(r => new SelectMenuOptionBuilder()
                .WithLabel(r.Name)
                .WithValue(r.Id.ToString()))
            .ToList();

        MessageComponent components = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_roles_add")
                .WithPlaceholder("Roles to ADD when reacted")
                .WithOptions(roles)
                .WithMinValues(0)
                .WithMaxValues(roles.Count))  // Allow selecting multiple
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_roles_remove")
                .WithPlaceholder("Roles to REMOVE when reacted")
                .WithOptions(roles)
                .WithMinValues(0)
                .WithMaxValues(roles.Count))
            .WithButton("Confirm & Save", "rr_confirm", ButtonStyle.Success)
            .Build();

        await DeferAsync(ephemeral: true);

        await FollowupAsync(
            "**Step 4:** Choose roles to add/remove, then confirm.",
            components: components,
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Role selection handlers — just update session, no response yet
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_roles_add", ignoreGroupNames: true)]
    public async Task OnRolesAddSelected(string[] selectedValues)
    {
        if (Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
            session.RolesToAdd = selectedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(ulong.Parse)
                .ToList();

        await DeferAsync(ephemeral: true);  // Acknowledge without sending a new message
    }

    [ComponentInteraction("rr_roles_remove", ignoreGroupNames: true)]
    public async Task OnRolesRemoveSelected(string[] selectedValues)
    {
        if (Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
            session.RolesToRemove = selectedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(ulong.Parse)
                .ToList();

        await DeferAsync(ephemeral: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STEP 5 — Confirm button: validate, save, react to message
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_confirm", ignoreGroupNames: true)]
    public async Task OnConfirm()
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        if (session.MessageId == 0 || session.ChannelId == 0 || string.IsNullOrWhiteSpace(session.Emoji))
        {
            await RespondAsync("Something went wrong — missing data. Please start again.", ephemeral: true);
            return;
        }

        // Save entry
        _data.AddEntry(new ReactionEntry
        {
            Message = session.MessageId,
            Channel = session.ChannelId,
            Emoji = session.Emoji,
            RolesToAdd = session.RolesToAdd,
            RolesToRemove = session.RolesToRemove
        });

        // Add bot reaction to target message
        ITextChannel? channel = Context.Guild.GetChannel(session.ChannelId) as ITextChannel;
        if (channel != null)
        {
            IUserMessage? message = await channel.GetMessageAsync(session.MessageId) as IUserMessage;
            if (message != null)
            {
                IEmote emote = Emote.TryParse(session.Emoji, out Emote customEmote)
                    ? customEmote
                    : new Emoji(session.Emoji);

                await message.AddReactionAsync(emote);
            }
        }

        Sessions.Remove(Context.User.Id);

        // IMPORTANT: this MUST be UpdateAsync for button interactions
        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content =
                    $"✅ Reaction role saved!\nReact with {session.Emoji} on the message to trigger it.";
                props.Components = null; // removes Step 4 UI cleanly
            });

            // 🔥 auto-cleanup after user sees it
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000);

                try
                {
                    await component.DeleteOriginalResponseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Failed to delete confirmation message: {ex.Message}");
                }
            });
        }
        else
        {
            // fallback safety (rare case)
            await RespondAsync(
                $"✅ Reaction role saved!\nReact with {session.Emoji} on the message to trigger it.",
                ephemeral: true
            );
        }
    }
    
    
    
    
    // ─────────────────────────────────────────────────────────────────────────
    // /reactionrole delete — show a select menu of all saved entries to remove
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("delete", "Remove a reaction role entry")]
    public async Task DeleteStart()
    {
        var entries = _data.ReactionMessages;

        if (entries.Count == 0)
        {
            await RespondAsync("There are no reaction roles configured.", ephemeral: true);
            return;
        }

        List<SelectMenuOptionBuilder> options = new();

        foreach (ReactionEntry entry in entries)
        {
            string channelName = Context.Guild.GetChannel(entry.Channel)?.Name ?? entry.Channel.ToString();

            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"#{channelName} — {entry.Emoji}")
                .WithDescription($"Message ID: {entry.Message}")
                .WithValue($"{entry.Message}|{entry.Emoji}"));
        }

        if (options.Count > 25)
        {
            options = options.Take(25).ToList();
            Console.WriteLine("[Warning] More than 25 reaction role entries — only showing first 25 in delete menu");
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_delete_select")
                .WithPlaceholder("Select the reaction role to remove")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(options.Count))
            .Build();

        await RespondAsync(
            "Select the reaction role entry you want to delete:",
            components: menu,
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete — user picked an entry; confirm before removing
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_delete_select", ignoreGroupNames: true)]
    public async Task OnDeleteSelected(string[] selectedValues)
    {
        // Parse all selected entries
        List<(ulong messageId, string emoji)> toDelete = new();

        foreach (string value in selectedValues)
        {
            string[] parts = value.Split('|');
            if (parts.Length == 2 && ulong.TryParse(parts[0], out ulong messageId))
                toDelete.Add((messageId, parts[1]));
        }

        if (toDelete.Count == 0)
        {
            await RespondAsync("Invalid selection. Please try again.", ephemeral: true);
            return;
        }

        // Build a summary of what will be deleted
        string summary = string.Join("\n", toDelete.Select(e => $"• **{e.emoji}** on message `{e.messageId}`"));

        // Encode all selections into the confirm button's custom ID
        // Format: rr_delete_confirm_multi — actual data passed via session
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            session = new ReactionSetupSession();
            Sessions[Context.User.Id] = session;
        }
        session.PendingDeletes = toDelete;

        MessageComponent components = new ComponentBuilder()
            .WithButton("Yes, delete all", "rr_delete_confirm_multi", ButtonStyle.Danger)
            .WithButton("Cancel", "rr_delete_cancel", ButtonStyle.Secondary)
            .Build();

        await RespondAsync(
            $"Are you sure you want to delete **{toDelete.Count}** reaction role(s)?\n{summary}",
            components: components,
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete — confirmed; remove from data and clean up bot reaction
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_delete_confirm_multi", ignoreGroupNames: true)]
    public async Task OnDeleteConfirmedMulti()
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session) 
            || session.PendingDeletes == null || session.PendingDeletes.Count == 0)
        {
            await RespondAsync("Session expired. Please run `/reactionrole delete` again.", ephemeral: true);
            return;
        }

        int deleted = 0;

        foreach ((ulong messageId, string emoji) in session.PendingDeletes)
        {
            ReactionEntry? entry = _data.GetEntry(messageId, emoji);
            if (entry == null) continue;

            _data.RemoveEntry(messageId, emoji);
            deleted++;

            Console.WriteLine($"[Info] Reaction role deleted — message {messageId}, emoji {emoji} by {Context.User.Username}");

            try
            {
                ITextChannel? channel = Context.Guild.GetChannel(entry.Channel) as ITextChannel;
                if (channel != null)
                {
                    IUserMessage? message = await channel.GetMessageAsync(messageId) as IUserMessage;
                    if (message != null)
                    {
                        IEmote emote = Emote.TryParse(emoji, out Emote customEmote)
                            ? customEmote
                            : new Emoji(emoji);

                        await message.RemoveReactionAsync(emote, Context.Guild.CurrentUser);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Could not remove bot reaction during delete: {ex.Message}");
            }
        }

        session.PendingDeletes = null;

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Deleted **{deleted}** reaction role(s).";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Deleted **{deleted}** reaction role(s).", ephemeral: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete — cancelled
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_delete_cancel", ignoreGroupNames: true)]
    public async Task OnDeleteCancelled()
    {
        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = "Deletion cancelled.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync("Deletion cancelled.", ephemeral: true);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Modal data class — Discord.NET maps modal fields to properties automatically
// Property name must match the TextInput CustomId
// ─────────────────────────────────────────────────────────────────────────────
public class ReactionEmojiModal : IModal
{
    public string Title => "Reaction Role — Emoji";

    [InputLabel("Emoji")]
    [ModalTextInput("rr_emoji_input")]
    public string Emoji { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// Session model — holds state between UI steps for one user
// ─────────────────────────────────────────────────────────────────────────────
public class ReactionSetupSession
{
    public List<(ulong messageId, string emoji)>? PendingDeletes { get; set; }
    public bool WaitingForEmoji { get; set; } //reaction capture state
    public ulong ChannelId  { get; set; }
    public ulong MessageId  { get; set; }
    public string? Emoji     { get; set; } = "";
    public List<ulong> RolesToAdd    { get; set; } = new();
    public List<ulong> RolesToRemove { get; set; } = new();
}