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
    // /schedule add — pick a day, then fill in details via modal
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("add", "Add a stream day to the schedule")]
    public async Task AddStart()
    {
        // Build next 14 days as selectable options
        List<SelectMenuOptionBuilder> options = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i <= (7 - (int)DateTime.Today.DayOfWeek); i++)
        {
            DateTimeOffset day = now.AddDays(i);
            string label = day.ToString("dddd, MMMM d"); // "Monday, April 14"
            string value = day.ToString("yyyy-MM-dd");   // stored value
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel(label)
                .WithValue(value));
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
            "**Step 1:** Pick the day you'll be streaming.",
            components: menu,
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Day selected — open modal for title + time
    // ─────────────────────────────────────────────────────────────────────────

    [ComponentInteraction("schedule_day_select", ignoreGroupNames: true)]
    public async Task OnDaySelected(string[] selectedValues)
    {
        string selectedDate = selectedValues[0]; // "yyyy-MM-dd"

        // Parse the date to show in modal title
        DateTimeOffset day = DateTimeOffset.Parse(selectedDate);
        string dayLabel = day.ToString("dddd, MMMM d");

        await Context.Interaction.RespondWithModalAsync<ScheduleEntryModal>(
            $"schedule_entry_modal:{selectedDate}",
            modifyModal: m => m.WithTitle($"Stream on {dayLabel}")
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Modal submitted — save entry
    // ─────────────────────────────────────────────────────────────────────────

    [ModalInteraction("schedule_entry_modal:*", ignoreGroupNames: true)]
    public async Task OnEntrySubmitted(string dateStr, ScheduleEntryModal modal)
    {
        // Parse time from modal (expected: "10:00 PM")
        // Combine with selected date into a full DateTimeOffset
        string combinedStr = $"{dateStr} {modal.Time.Trim()}";

        if (!DateTimeOffset.TryParse(combinedStr, out DateTimeOffset scheduledAt))
        {
            await RespondAsync(
                $"❌ Couldn't parse the time **{modal.Time}**. Use a format like `10:00 PM` or `22:00`.",
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

        Console.WriteLine($"[Info] Schedule entry added: {entry.Description} on {entry.ScheduledAtDisplay}");

        await RespondAsync(
            $"✅ Added **{entry.Description}** on <t:{entry.ScheduledAtParsed.ToUnixTimeSeconds()}:F>.",
            ephemeral: true
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule remove — select from existing entries
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

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Removed **{removed}** schedule entry/entries.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Removed **{removed}** entry/entries.", ephemeral: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule view — preview the embed
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("view", "Preview the schedule embed")]
    public async Task View()
    {
        Embed embed = BuildScheduleEmbed();
        await RespondAsync(embed: embed, ephemeral: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // /schedule publish — pick channel, then post with role ping
    // ─────────────────────────────────────────────────────────────────────────

    [SlashCommand("publish", "Post the schedule embed to a channel")]
    public async Task PublishStart()
    {
        if (_data.ScheduleEntries.Count == 0)
        {
            await RespondAsync("There are no schedule entries to publish.", ephemeral: true);
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

        // Ping the configured role — add ScheduleRoleId to your Config
        string roleMention = MentionUtils.MentionRole(Config.LiveRoleId);

        await channel.SendMessageAsync(text: roleMention, embed: embed);

        Console.WriteLine($"[Info] Schedule published to #{channel.Name} by {Context.User.Username}");

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content = $"✅ Schedule published to <#{channelId}>.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Schedule published to <#{channelId}>.", ephemeral: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Embed builder — shared by view and publish
    // ─────────────────────────────────────────────────────────────────────────

    Embed BuildScheduleEmbed()
    {
        var entries = _data.ScheduleEntries
            .OrderBy(e => e.ScheduledAtParsed)
            .ToList();

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Accessing Stream-schedule.txt...")
            .WithDescription("Status: active\nStream queries detected...\n\n—")
            .WithColor(new Color(0x5865F2))  // Discord blurple — swap to your theme color
            .WithFooter($"System Active • 4/30/03, 3:00 AM");

        foreach (ScheduleEntry entry in entries)
        {
            long unixSeconds = entry.ScheduledAtParsed.ToUnixTimeSeconds();
            builder.AddField(
                $"⚠️ {entry.Description}",
                $"<t:{unixSeconds}:F>",
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
    [ModalTextInput("schedule_description", placeholder: "Atomic Heart Part 3!! Let's go find some bunkers!")]
    public string Description { get; set; } = "";

    [InputLabel("Start Time (e.g. 10:00 PM)")]
    [ModalTextInput("schedule_time", placeholder: "10:00 PM")]
    public string Time { get; set; } = "";
}