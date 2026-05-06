using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

[Group("schedule", "Manage stream schedule")]
public class ScheduleModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly ScheduleData _data;

    public ScheduleModule(ScheduleData data)
    {
        _data = data;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule add — pick a day from remaining days this week, then modal
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("add", "Add a stream day to this week's schedule")]
    public async Task AddStart()
    {
        List<SelectMenuOptionBuilder> options = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Get remaining days of the current week (Monday–Sunday)
        DateTimeOffset weekStart = ScheduleData.GetCurrentWeekStart();
        DateTimeOffset weekEnd   = weekStart.AddDays(7);

        for (DateTimeOffset day = now.Date; day < weekEnd; day = day.AddDays(1))
        {
            // Skip days that already have an entry
            string dayStr = day.ToString("yyyy-MM-dd");
            bool alreadyAdded = _data.ScheduleEntries
                .Any(e => e.ScheduledAtParsed.ToString("yyyy-MM-dd") == dayStr);

            if (alreadyAdded) continue;

            options.Add(new SelectMenuOptionBuilder()
                .WithLabel(day.ToString("dddd, MMMM d"))
                .WithValue(dayStr));
        }

        if (options.Count == 0)
        {
            await RespondAsync(
                "All days this week already have entries, or the week is over. Use `/schedule remove` to free up a day.",
                ephemeral: true
            );
            return;
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("schedule_day_select")
                .WithPlaceholder("Pick the day you'll be live")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            "Pick the day you'll be streaming this week:",
            components: menu,
            ephemeral: true
        );
    }

    [ComponentInteraction("schedule_day_select", ignoreGroupNames: true)]
    public async Task OnDaySelected(string[] selectedValues)
    {
        string selectedDate = selectedValues[0];
        DateTimeOffset day  = DateTimeOffset.Parse(selectedDate);
        string dayLabel     = day.ToString("dddd, MMMM d");

        await Context.Interaction.RespondWithModalAsync<ScheduleEntryModal>(
            $"schedule_entry_modal:{selectedDate}",
            modifyModal: m => m.WithTitle($"Stream on {dayLabel}")
        );
    }

    [ModalInteraction("schedule_entry_modal:*", ignoreGroupNames: true)]
    public async Task OnEntrySubmitted(string dateStr, ScheduleEntryModal modal)
    {
        string combinedStr = $"{dateStr} {modal.Time.Trim()}";

        if (!DateTimeOffset.TryParse(combinedStr, out DateTimeOffset scheduledAt))
        {
            await RespondAsync(
                $"❌ Couldn't parse **{modal.Time}**. Use a format like `10:00 PM` or `22:00`.",
                ephemeral: true
            );
            return;
        }

        ScheduleEntry entry = new()
        {
            Id          = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Description = modal.Description.Trim(),
            ScheduledAt = scheduledAt.ToUniversalTime().ToString("o")
        };

        _data.AddEntry(entry);

        Console.WriteLine($"[Info] Schedule entry added: {entry.Description} — {entry.ScheduledAtDisplay}");

        // Live-update published embed if one exists this week
        await TryUpdatePublishedEmbed();

        await RespondAsync(
            $"✅ Added **{entry.Description}** — <t:{entry.ScheduledAtParsed.ToUnixTimeSeconds()}:F>.",
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule remove
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("remove", "Remove one or more schedule entries")]
    public async Task RemoveStart()
    {
        var entries = _data.ScheduleEntries;

        if (entries.Count == 0)
        {
            await RespondAsync("There are no schedule entries to remove.", ephemeral: true);
            return;
        }

        List<SelectMenuOptionBuilder> options = entries
            .OrderBy(e => e.ScheduledAtParsed)
            .Take(25)
            .Select(e => new SelectMenuOptionBuilder()
                .WithLabel(e.Description.Length > 50 ? e.Description[..50] : e.Description)
                .WithDescription(e.ScheduledAtDisplay)
                .WithValue(e.Id.ToString()))
            .ToList();

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("schedule_remove_select")
                .WithPlaceholder("Select entries to remove")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(options.Count))
            .Build();

        await RespondAsync(
            "Select the entries you want to remove:",
            components: menu,
            ephemeral: true
        );
    }

    [ComponentInteraction("schedule_remove_select", ignoreGroupNames: true)]
    public async Task OnRemoveSelected(string[] selectedValues)
    {
        int removed = 0;

        foreach (string idStr in selectedValues)
        {
            if (ulong.TryParse(idStr, out ulong id))
            {
                _data.RemoveEntry(id);
                removed++;
            }
        }

        // Live-update published embed if one exists this week
        await TryUpdatePublishedEmbed();

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Removed **{removed}** entry/entries. Published schedule updated.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Removed **{removed}** entry/entries.", ephemeral: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule view
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("view", "Preview the schedule embed")]
    public async Task View()
    {
        if (_data.ScheduleEntries.Count == 0)
        {
            await RespondAsync("No entries yet. Use `/schedule add` to add some.", ephemeral: true);
            return;
        }

        Embed embed = BuildScheduleEmbed();
        await RespondAsync(embed: embed, ephemeral: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule publish
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("publish", "Post the schedule embed to a channel")]
    public async Task PublishStart()
    {
        if (_data.ScheduleEntries.Count == 0)
        {
            await RespondAsync("There are no schedule entries to publish.", ephemeral: true);
            return;
        }

        // Block re-publish if already published this week
        if (_data.IsPublishedThisWeek())
        {
            await RespondAsync(
                "📌 The schedule for this week is already published.\nUse `/schedule add` or `/schedule remove` to update it — the posted embed will update automatically.",
                ephemeral: true
            );
            return;
        }

        // New week — clear out last week's entries and message ref before publishing
        if (_data.PublishedMessageId != 0)
        {
            _data.ClearPublished(); // clears entries + message ref
            await RespondAsync(
                "🔄 New week detected — last week's schedule has been cleared. Use `/schedule add` to build this week's schedule first, then publish.",
                ephemeral: true
            );
            return;
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("schedule_publish_channel")
                .WithType(ComponentType.ChannelSelect)
                .WithPlaceholder("Pick the channel to post in")
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            "Pick the channel to publish the schedule to:",
            components: menu,
            ephemeral: true
        );
    }

    [ComponentInteraction("schedule_publish_channel", ignoreGroupNames: true)]
    public async Task OnPublishChannelSelected(string[] selectedValues)
    {
        ulong channelId = ulong.Parse(selectedValues[0]);
        ITextChannel? channel = Context.Guild.GetChannel(channelId) as ITextChannel;

        if (channel == null)
        {
            await RespondAsync("That isn't a text channel.", ephemeral: true);
            return;
        }

        Embed embed = BuildScheduleEmbed();
        string roleMention = MentionUtils.MentionRole(Config.LiveRoleId);

        // Send and store the message ID + channel ID for future edits
        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);
        _data.SetPublished(posted.Id, channelId, ScheduleData.GetCurrentWeekStart());

        Console.WriteLine($"[Info] Schedule published to #{channel.Name} (msg: {posted.Id}) by {Context.User.Username}");

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Schedule published to <#{channelId}>. Future add/remove changes will update it automatically.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Published to <#{channelId}>.", ephemeral: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Live-edit helper — silently updates the published embed after any change
    // ─────────────────────────────────────────────────────────────────────────

    async Task TryUpdatePublishedEmbed()
    {
        if (!_data.IsPublishedThisWeek()) return;
        if (_data.PublishedMessageId == 0 || _data.PublishedChannelId == 0) return;

        try
        {
            ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
            if (channel == null) return;

            IUserMessage? message = await channel.GetMessageAsync(_data.PublishedMessageId) as IUserMessage;
            if (message == null) return;

            Embed updated = BuildScheduleEmbed();
            await message.ModifyAsync(props => props.Embed = updated);

            Console.WriteLine($"[Info] Published schedule embed updated (msg: {_data.PublishedMessageId})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] Failed to update published embed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Embed builder
    // ─────────────────────────────────────────────────────────────────────────

    Embed BuildScheduleEmbed()
    {
        var entries = _data.ScheduleEntries
            .OrderBy(e => e.ScheduledAtParsed)
            .ToList();

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Accessing Stream-schedule.txt...")
            .WithDescription("Status: active\nStream queries detected...\n\n—")
            .WithColor(new Color(0x5865F2))
            .WithFooter($"System Active • 4/30/03, 3:00 AM");

        foreach (ScheduleEntry entry in entries)
        {
            long unixSeconds = entry.ScheduledAtParsed.ToUnixTimeSeconds();
            builder.AddField(
                $"⚠️ {entry.Description}",
                $"> <t:{unixSeconds}:F>",
                inline: false
            );
        }

        return builder.Build();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Modal
// ─────────────────────────────────────────────────────────────────────────────

public class ScheduleEntryModal : IModal
{
    public string Title => "Add Stream Entry";

    [InputLabel("Game / Stream Title")]
    [ModalTextInput("schedule_description",
        placeholder: "Atomic Heart Part 3!! Let's go find some bunkers!",
        maxLength: 100)]
    public string Description { get; set; } = "";

    [InputLabel("Start Time (e.g. 10 PM or 22:00)")]
    [ModalTextInput("schedule_time", placeholder: "10 PM")]
    public string Time { get; set; } = "";
}