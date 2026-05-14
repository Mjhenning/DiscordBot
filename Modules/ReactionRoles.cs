using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class ReactionRolesModule : InteractionModuleBase<SocketInteractionContext>
{
    public static readonly Dictionary<ulong, ReactionSetupSession> Sessions = new();

    readonly ReactionsData _data;
    readonly StreamWriter  _log;

    void Log(string msg)
    {
        Console.WriteLine(msg);
        _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }

    public ReactionRolesModule(ReactionsData data, StreamWriter log)
    {
        _data = data;
        _log  = log;
    }
    
    [SlashCommand("reactionrole", "Manage your Reaction Roles")]
    public async Task ScheduleMenu()
    {
        MessageComponent buttons = new ComponentBuilder()
            .WithButton("➕ Add",    "rr_btn_add",    ButtonStyle.Primary)
            .WithButton("🗑️ Remove", "rr_btn_remove", ButtonStyle.Danger)
            .Build();

        await RespondAsync(
            "**Reaction Roles** — what would you like to do?",
            components: buttons,
            ephemeral: true
        );
    }

    [ComponentInteraction("rr_btn_add",    ignoreGroupNames: true)]
    public Task OnBtnAdd()    => SetupStart();

    [ComponentInteraction("rr_btn_remove", ignoreGroupNames: true)]
    public Task OnBtnRemove() => DeleteStart();
    
    // ─── STEP 1 ───────────────────────────────────────────────────────────────

    public async Task SetupStart()
    {
        Sessions[Context.User.Id] = new ReactionSetupSession();

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("rr_channel")
                .WithType(ComponentType.ChannelSelect)
                .WithPlaceholder("Select the channel")
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            "**Step 1:** Choose the channel that contains your target message.",
            components: menu,
            ephemeral: true
        );
    }

    // ─── STEP 2 ───────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_channel", ignoreGroupNames: true)]
    public async Task OnChannelSelected(string[] selectedValues)
    {
        ulong channelId = ulong.Parse(selectedValues[0]);

        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        session.ChannelId = channelId;

        ITextChannel? channel = Context.Guild.GetChannel(channelId) as ITextChannel;
        if (channel == null)
        {
            await RespondAsync("That channel isn't a text channel.", ephemeral: true);
            return;
        }

        var messages = await channel.GetMessagesAsync(5).FlattenAsync();

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

    // ─── STEP 3 ───────────────────────────────────────────────────────────────

    [ComponentInteraction("rr_message", ignoreGroupNames: true)]
    public async Task OnMessageSelected(string[] selectedValues)
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        session.MessageId      = ulong.Parse(selectedValues[0]);
        session.WaitingForEmoji = true;

        await DeferAsync(ephemeral: true);

        await FollowupAsync(
            "**Step 3:** React to the target message with the emoji you want to use.\nAfter you react, setup will continue automatically.",
            ephemeral: true
        );
    }

    // ─── STEP 4 ───────────────────────────────────────────────────────────────

    [ModalInteraction("rr_emoji_modal")]
    public async Task OnEmojiSubmitted(ReactionEmojiModal modal)
    {
        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            await RespondAsync("Session expired. Please run `/reactionrole create` again.", ephemeral: true);
            return;
        }

        session.Emoji = modal.Emoji.Trim();

        List<SelectMenuOptionBuilder> roles = Context.Guild.Roles
            .Where(r => r.Id != Context.Guild.Id && !r.IsManaged)
            .OrderByDescending(r => r.Position)
            .Take(25)
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
                .WithMaxValues(roles.Count))
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

    // ─── ROLE SELECTION ───────────────────────────────────────────────────────

    [ComponentInteraction("rr_roles_add", ignoreGroupNames: true)]
    public async Task OnRolesAddSelected(string[] selectedValues)
    {
        if (Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
            session.RolesToAdd = selectedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(ulong.Parse)
                .ToList();

        await DeferAsync(ephemeral: true);
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

    // ─── STEP 5 — CONFIRM ─────────────────────────────────────────────────────

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

        _data.AddEntry(new ReactionEntry
        {
            Message       = session.MessageId,
            Channel       = session.ChannelId,
            Emoji         = session.Emoji,
            RolesToAdd    = session.RolesToAdd,
            RolesToRemove = session.RolesToRemove
        });

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

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content    = $"✅ Reaction role saved!\nReact with {session.Emoji} on the message to trigger it.";
                props.Components = null;
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(4000);
                try
                {
                    await component.DeleteOriginalResponseAsync();
                }
                catch (Exception ex)
                {
                    Log($"[Warning] Failed to delete confirmation message: {ex.Message}");
                }
            });
        }
        else
        {
            await RespondAsync(
                $"✅ Reaction role saved!\nReact with {session.Emoji} on the message to trigger it.",
                ephemeral: true
            );
        }
    }
    
    // ─── DELETE START ─────────────────────────────────────────────────────────
    
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
            Log("[Warning] More than 25 reaction role entries — only showing first 25 in delete menu");
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

    // ─── DELETE SELECT ────────────────────────────────────────────────────────

    [ComponentInteraction("rr_delete_select", ignoreGroupNames: true)]
    public async Task OnDeleteSelected(string[] selectedValues)
    {
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

        string summary = string.Join("\n", toDelete.Select(e => $"• **{e.emoji}** on message `{e.messageId}`"));

        if (!Sessions.TryGetValue(Context.User.Id, out ReactionSetupSession? session))
        {
            session = new ReactionSetupSession();
            Sessions[Context.User.Id] = session;
        }
        session.PendingDeletes = toDelete;

        MessageComponent components = new ComponentBuilder()
            .WithButton("Yes, delete all", "rr_delete_confirm_multi", ButtonStyle.Danger)
            .WithButton("Cancel",          "rr_delete_cancel",        ButtonStyle.Secondary)
            .Build();

        await RespondAsync(
            $"Are you sure you want to delete **{toDelete.Count}** reaction role(s)?\n{summary}",
            components: components,
            ephemeral: true
        );
    }

    // ─── DELETE CONFIRM ───────────────────────────────────────────────────────

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

            Log($"[Info] Reaction role deleted — message {messageId}, emoji {emoji} by {Context.User.Username}");

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
                Log($"[Warning] Could not remove bot reaction during delete: {ex.Message}");
            }
        }

        session.PendingDeletes = null;

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content    = $"✅ Deleted **{deleted}** reaction role(s).";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Deleted **{deleted}** reaction role(s).", ephemeral: true);
        }
    }

    // ─── DELETE CANCEL ────────────────────────────────────────────────────────

    [ComponentInteraction("rr_delete_cancel", ignoreGroupNames: true)]
    public async Task OnDeleteCancelled()
    {
        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content    = "Deletion cancelled.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync("Deletion cancelled.", ephemeral: true);
        }
    }
}

public class ReactionEmojiModal : IModal
{
    public string Title => "Reaction Role — Emoji";

    [InputLabel("Emoji")]
    [ModalTextInput("rr_emoji_input")]
    public string Emoji { get; set; } = "";
}

public class ReactionSetupSession
{
    public List<(ulong messageId, string emoji)>? PendingDeletes { get; set; }
    public bool   WaitingForEmoji { get; set; }
    public ulong  ChannelId       { get; set; }
    public ulong  MessageId       { get; set; }
    public string? Emoji          { get; set; } = "";
    public List<ulong> RolesToAdd    { get; set; } = new();
    public List<ulong> RolesToRemove { get; set; } = new();
}