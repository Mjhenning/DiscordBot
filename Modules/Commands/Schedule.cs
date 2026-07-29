using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class ScheduleModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly ScheduleData _data;

    public ScheduleModule(ScheduleData data)
    {
        _data = data;
    }
    
    [SlashCommand("resetschedule", "Force reset the schedule — delete after use")]
    public async Task ForceReset()
    {
        await _data.ClearPublishedAsync();
        Logger.Log("[Info] Schedule force reset by command");
        await RespondAsync("🗑️ Schedule has been force reset.", ephemeral: true);
    }
    
    [SlashCommand("streamschedule", "Manage the stream schedule")]
    [RequireRole("🔧 Processes")] 
    public async Task ScheduleMenu()
    {
        MessageComponent buttons = new ComponentBuilder()
            .WithButton("➕ Add",     "schedule_btn_add",     ButtonStyle.Primary)
            .WithButton("🗑️ Remove",  "schedule_btn_remove",  ButtonStyle.Danger)
            .WithButton("👁️ View",    "schedule_btn_view",    ButtonStyle.Secondary)
            .WithButton("📢 Publish", "schedule_btn_publish", ButtonStyle.Success)
            .Build();

        await RespondAsync(
            "**Stream Schedule** — what would you like to do?",
            components: buttons,
            ephemeral: true
        );
    }

    [ComponentInteraction("schedule_btn_add",     ignoreGroupNames: true)]
    public Task OnBtnAdd()     => AddStart();

    [ComponentInteraction("schedule_btn_remove",  ignoreGroupNames: true)]
    public Task OnBtnRemove()  => RemoveStart();

    [ComponentInteraction("schedule_btn_view",    ignoreGroupNames: true)]
    public Task OnBtnView()    => View();

    [ComponentInteraction("schedule_btn_publish", ignoreGroupNames: true)]
    public Task OnBtnPublish() => PublishStart();

    // ─── ADD ──────────────────────────────────────────────────────────────────
    
    public async Task AddStart()
    {
        List<SelectMenuOptionBuilder> options = new();
        DateTimeOffset now = DateTimeOffset.Now;
        int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
        DateTimeOffset weekEnd = now.Date.AddDays(daysUntilSunday == 0 ? 7 : daysUntilSunday + 1);

        for (DateTimeOffset day = now.Date; day < weekEnd; day = day.AddDays(1))
        {
            string dayStr      = day.ToString("yyyy-MM-dd");
            bool alreadyAdded = _data.ScheduleEntries
                .Any(e => e.ScheduledAtParsed.ToLocalTime().ToString("yyyy-MM-dd") == dayStr);

            if (alreadyAdded) continue;

            options.Add(new SelectMenuOptionBuilder()
                .WithLabel(day.ToString("dddd, MMMM d"))
                .WithValue(dayStr));
        }

        if (options.Count == 0)
        {
            await RespondAsync(
                "Use the 🗑️ Remove button in /schedule to free up a day.",
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

        await _data.AddEntryAsync(entry);

        Logger.Log($"[Info] Schedule entry added: {entry.Description} — {entry.ScheduledAtDisplay}");

        await TryUpdatePublishedEmbed();

        await RespondAsync(
            $"✅ Added **{entry.Description}** — <t:{entry.ScheduledAtParsed.ToUnixTimeSeconds()}:F>.",
            ephemeral: true
        );
    }

    // ─── REMOVE ───────────────────────────────────────────────────────────────
    
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
                await _data.RemoveEntryAsync(id);
                removed++;
            }
        }

        await TryUpdatePublishedEmbed();

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content    = $"✅ Removed **{removed}** entry/entries. Published schedule updated.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Removed **{removed}** entry/entries.", ephemeral: true);
        }
    }

    // ─── VIEW ─────────────────────────────────────────────────────────────────
    
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

// ─── PUBLISH ──────────────────────────────────────────────────────────────
    
    public async Task PublishStart()
    {
        if (_data.ScheduleEntries.Count == 0)
        {
            await RespondAsync("There are no schedule entries to publish.", ephemeral: true);
            return;
        }

        if (_data.IsPublishedThisWeek())
        {
            bool updated = await TryUpdatePublishedEmbed();

            await RespondAsync(
                updated
                    ? "🔄 Schedule already published — I've refreshed the existing message with the latest entries."
                    : "⚠️ Couldn't find the previously published message to update (it may have been deleted). Use `/resetschedule` to start fresh, then publish again.",
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

        Embed embed        = BuildScheduleEmbed();
        string roleMention = $"<@&{Config.ScheduleNotiRoleId}>";

        IUserMessage posted = await channel.SendMessageAsync(text: roleMention, embed: embed);
        _data.SetPublished(posted.Id, channelId, ScheduleData.GetCurrentWeekStart());

        Logger.Log($"[Info] Schedule published to #{channel.Name} (msg: {posted.Id}) by {Context.User.Username}");

        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.UpdateAsync(props =>
            {
                props.Content    = $"✅ Schedule published to <#{channelId}>. Future add/remove changes will update it automatically.";
                props.Components = null;
            });
        }
        else
        {
            await RespondAsync($"✅ Published to <#{channelId}>.", ephemeral: true);
        }
    }

    // ─── LIVE EDIT HELPER ─────────────────────────────────────────────────────

    async Task<bool> TryUpdatePublishedEmbed()
    {
        Logger.Log($"[Debug] TryUpdatePublishedEmbed called");
        Logger.Log($"[Debug] IsPublishedThisWeek: {_data.IsPublishedThisWeek()}");
        Logger.Log($"[Debug] MessageId: {_data.PublishedMessageId}, ChannelId: {_data.PublishedChannelId}");

        if (!_data.IsPublishedThisWeek()) { Logger.Log("[Debug] Bailing — not published this week"); return false; }
        if (_data.PublishedMessageId == 0 || _data.PublishedChannelId == 0) { Logger.Log("[Debug] Bailing — missing IDs"); return false; }

        try
        {
            ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
            Logger.Log($"[Debug] Channel: {channel?.Name ?? "NULL"}");
            if (channel == null) return false;

            IUserMessage? message = await channel.GetMessageAsync(_data.PublishedMessageId) as IUserMessage;
            Logger.Log($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");
            if (message == null) return false;

            Embed updated = BuildScheduleEmbed();
            await message.ModifyAsync(props => props.Embed = updated);
            Logger.Log($"[Info] Published schedule embed updated");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Warning] TryUpdatePublishedEmbed failed: {ex.Message}");
            Logger.Log($"[Warning] {ex.StackTrace}");
            return false;
        }
    }

    // ─── EMBED BUILDER ────────────────────────────────────────────────────────

    Embed BuildScheduleEmbed()
    {
        var entries = _data.ScheduleEntries
            .OrderBy(e => e.ScheduledAtParsed)
            .ToList();

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