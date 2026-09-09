using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class CollabModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly CollabData _data;
    readonly CollabService _collabService;
    readonly CollabRequestCache _cache;

    public CollabModule(
        CollabData data,
        CollabService collabService,
        CollabRequestCache cache)
    {
        _data = data;
        _collabService = collabService;
        _cache = cache;
    }

    [SlashCommand("collab", "Manage Collabs")]
    [RequireRole("🌐 Proxy Hosts")] [RequireRole("🔧 Processes")] 
    public async Task CollabMenu()
    {
        MessageComponent buttons = new ComponentBuilder()
            .WithButton("➕ Start Request",    "c_btn_request",    ButtonStyle.Primary)
            .WithButton("👁️ View Collabs", "c_btn_view", ButtonStyle.Secondary)
            .Build();
        
        await RespondAsync(
            "**Collabs** — what would you like to do?",
            components: buttons,
            ephemeral: true
        );
    }

    [ComponentInteraction("c_btn_request", ignoreGroupNames: true)]
    public Task OnBtnRequest() => RequestStart();

    [ComponentInteraction("c_btn_view", ignoreGroupNames: true)]
    public Task OnBtnView() => View();
    
    [ComponentInteraction("collab_day_select")]
    public async Task OnDaySelected(string[] values)
    {
        string date = values[0];

        await Context.Interaction
            .RespondWithModalAsync<CollabRequestModal>(
                $"collab_modal:{date}");
    }
    
    [ModalInteraction("collab_modal:*", ignoreGroupNames: true)]
    public async Task OnModalSubmitted(
        string date,
        CollabRequestModal modal)
    {
        string combined = $"{date} {modal.Time}";

        if (!DateTimeOffset.TryParse(combined, out DateTimeOffset scheduled))
        {
            await RespondAsync(
                "❌ Couldn't parse that time.",
                ephemeral: true);

            return;
        }

        PendingCollabRequest pending = new()
        {
            Id = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

            OwnerId = Context.User.Id,

            Description = modal.Description.Trim(),

            ScheduledAt = scheduled.ToUniversalTime().ToString("o"),

            GameName = string.IsNullOrWhiteSpace(modal.Game)
                ? null
                : modal.Game.Trim(),
            
            ExternalCollaborators = modal.ExternalCollaborators
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList()
        };

        _cache.Add(pending);

        var menu = new SelectMenuBuilder()
            .WithCustomId($"collab_users:{pending.Id}")
            .WithPlaceholder("Select collaborators")
            .WithMinValues(0)
            .WithMaxValues(10)
            .WithType(ComponentType.UserSelect);

        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .Build();

        await RespondAsync(
            "Select everyone you'd like to invite.",
            components: components,
            ephemeral: true);
    }
    
    [ComponentInteraction("collab_accept:*", ignoreGroupNames: true)]
    public async Task Accept(string idString)
    {
        await DeferAsync(ephemeral: true);

        if (!ulong.TryParse(idString, out ulong id))
            return;

        CollabEntry? request = _data.Get(id);

        if (request == null)
        {
            await FollowupAsync(
                "This collaboration request no longer exists.",
                ephemeral: true);

            return;
        }

        CollabParticipant? participant =
            request.Participants
                .FirstOrDefault(x => x.UserId == Context.User.Id);

        if (participant == null)
        {
            await FollowupAsync(
                "You are not part of this collaboration.",
                ephemeral: true);

            return;
        }

        participant.Status = ParticipantStatus.Accepted;
        participant.DeclineReason = null;

        request.LastUpdated = DateTimeOffset.UtcNow;

        _data.Update(request);

        await _collabService.UpdateMessagesAsync(
            request,
            Context.Client);

        await FollowupAsync(
            "✅ You've accepted the collaboration!",
            ephemeral: true);
    }
    
    [ModalInteraction("collab_decline_modal:*", ignoreGroupNames: true)]
    public async Task DeclineSubmitted(
        string idString,
        DeclineModal modal)
    {
        await DeferAsync(ephemeral: true);

        if (!ulong.TryParse(idString, out ulong id))
            return;

        CollabEntry? request = _data.Get(id);

        if (request == null)
        {
            await FollowupAsync(
                "Request no longer exists.",
                ephemeral: true);

            return;
        }

        CollabParticipant? participant =
            request.Participants
                .FirstOrDefault(x => x.UserId == Context.User.Id);

        if (participant == null)
            return;

        participant.Status = ParticipantStatus.Declined;

        request.LastUpdated = DateTimeOffset.UtcNow;

        participant.DeclineReason =
            string.IsNullOrWhiteSpace(modal.Reason)
                ? null
                : modal.Reason.Trim();

        _data.Update(request);

        await _collabService.UpdateMessagesAsync(
            request,
            Context.Client);

        await FollowupAsync(
            "You've declined the collaboration.",
            ephemeral: true);
    }
    
    [ComponentInteraction("collab_decline:*", ignoreGroupNames: true)]
    public async Task Decline(string id)
    {
        await Context.Interaction
            .RespondWithModalAsync<DeclineModal>(
                $"collab_decline_modal:{id}");
    }
    
    [ComponentInteraction("collab_users:*", ignoreGroupNames: true)]
    public async Task OnUsersSelected(
        string requestId,
        string[] selectedUsers)
    {
        if (!ulong.TryParse(requestId, out ulong id))
            return;

        PendingCollabRequest? pending = _cache.Get(id);

        if (pending == null)
        {
            await RespondAsync(
                "This request expired.",
                ephemeral: true);

            return;
        }

        pending.Collaborators = selectedUsers
            .Select(ulong.Parse)
            .Where(id => id != pending.OwnerId)
            .Distinct()
            .ToList();
        
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("🌏 Proxy Collaboration Request")
            .WithColor(new Color(0x5865F2))
            .WithDescription("Please confirm the details before invitations are sent.");

        builder.AddField(
            "📝 Description",
            pending.Description,
            false);

        if (!string.IsNullOrWhiteSpace(pending.GameName))
        {
            builder.AddField(
                "🎮 Game",
                pending.GameName,
                true);
        }

        builder.AddField(
            "📅 Scheduled",
            $"<t:{DateTimeOffset.Parse(pending.ScheduledAt).ToUnixTimeSeconds()}:F>",
            true);

        builder.AddField(
            "👤 Host",
            $"<@{pending.OwnerId}>",
            true);

        builder.AddField(
            "👥 Invited Collaborators",
            pending.Collaborators.Any()
                ? string.Join(
                    "\n",
                    pending.Collaborators.Select(x => $"• <@{x}>"))
                : "*None*",
            false);
        
        if (pending.ExternalCollaborators.Any())
        {
            builder.AddField(
                "🌐 External Collaborators",
                string.Join(
                    "\n",
                    pending.ExternalCollaborators.Select(x => $"• {x}")),
                false);
        }

        string count =
            pending.Collaborators.Count == 1
                ? "1 collaborator"
                : $"{pending.Collaborators.Count} collaborators";

        builder.AddField(
            "Confirmation",
            $"⚠️ Invitations will be sent immediately to **{count}**.",
            false);
        
        var buttons = new ComponentBuilder()
            .WithButton(
                "✅ Create Request",
                $"collab_create:{pending.Id}",
                ButtonStyle.Success)

            .WithButton(
                "✖ Cancel",
                $"collab_cancel:{pending.Id}",
                ButtonStyle.Danger)
            .Build();
        
        await RespondAsync(
            embed: builder.Build(),
            components: buttons,
            ephemeral: true);
    }
    
    [ComponentInteraction("collab_create:*", ignoreGroupNames: true)]
    public async Task CreateRequest(string idString)
    {
        await DeferAsync(ephemeral: true);
        
        if (!ulong.TryParse(idString, out ulong id))
            return;

        PendingCollabRequest? pending = _cache.Get(id);

        if (pending == null)
        {
            await FollowupAsync(
                "This request expired.",
                ephemeral: true);

            return;
        }

        CollabEntry request = new()
        {
            Id = pending.Id,
            OwnerId = pending.OwnerId,
            Description = pending.Description,
            ScheduledAt = pending.ScheduledAt,
            GameName = pending.GameName,
            ExternalCollaborators = pending.ExternalCollaborators.ToList(),
            LastUpdated = DateTimeOffset.UtcNow
        };

        request.Participants.Add(new CollabParticipant
        {
            UserId = pending.OwnerId,
            Status = ParticipantStatus.Accepted
        });

        foreach (ulong collaborator in pending.Collaborators)
        {
            request.Participants.Add(new CollabParticipant
            {
                UserId = collaborator
            });
        }

        _data.Add(request);

        _cache.Remove(id);

        await _collabService.SendRequestAsync(
            request,
            Context.Client);

        await FollowupAsync(
            "✅ Collaboration request sent!",
            ephemeral: true);
    }
    
    [ComponentInteraction("collab_cancel:*", ignoreGroupNames: true)]
    public async Task CancelRequest(string idString)
    {
        if (ulong.TryParse(idString, out ulong id))
        {
            _cache.Remove(id);
        }

        await RespondAsync(
            "❌ Collaboration request cancelled.",
            ephemeral: true);
    }
    
    //-----REQUESTS-----
    public async Task RequestStart()
    {
        List<SelectMenuOptionBuilder> options = new();

        DateTimeOffset now = DateTimeOffset.Now;

        for (int i = 0; i < 25; i++)
        {
            DateTimeOffset day = now.Date.AddDays(i);

            options.Add(new SelectMenuOptionBuilder()
                .WithLabel(day.ToString("dddd, MMMM d"))
                .WithValue(day.ToString("yyyy-MM-dd")));
        }

        var menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("collab_day_select")
                .WithPlaceholder("Choose a date")
                .WithOptions(options))
            .Build();

        await RespondAsync(
            "Pick the collaboration date:",
            components: menu,
            ephemeral: true);
    }
    
    //-----VIEW-----

    const int PageSize = 3;

    List<CollabEntry> FoxCollabs()
    {
        return _data.GetFoxCollabs(Config.FoxId)
            .OrderBy(x => x.ScheduledAtParsed)
            .ToList();
    }

    public async Task View()
    {
        await ShowPage(0);
    }

    [ComponentInteraction("collab_page_prev:*", ignoreGroupNames: true)]
    public async Task OnPagePrev(string pageString)
    {
        if (int.TryParse(pageString, out int page) && page > 0)
            await ShowPage(page - 1);
    }

    [ComponentInteraction("collab_page_next:*", ignoreGroupNames: true)]
    public async Task OnPageNext(string pageString)
    {
        if (int.TryParse(pageString, out int page))
            await ShowPage(page + 1);
    }

    async Task ShowPage(int page)
    {
        List<CollabEntry> collabs = FoxCollabs();

        if (collabs.Count == 0)
        {
            await RespondAsync(
                "There are no confirmed collaborations involving Fox.",
                ephemeral: true);

            return;
        }

        int pageCount = (collabs.Count + PageSize - 1) / PageSize;

        if (page < 0)
            page = 0;

        if (page > pageCount - 1)
            page = pageCount - 1;

        Embed embed = BuildCollabEmbed(
            collabs.Skip(page * PageSize).Take(PageSize).ToList(),
            page,
            pageCount);

        MessageComponent nav = BuildNavButtons(page, pageCount);

        // page nav buttons edit the collab list message in place;
        // the /collab menu button posts a fresh ephemeral message.
        bool isNav = Context.Interaction is SocketMessageComponent c
            && c.Data.CustomId.StartsWith("collab_page");

        if (isNav)
        {
            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = nav;
            });

            return;
        }

        await RespondAsync(
            embed: embed,
            components: nav,
            ephemeral: true);
    }

    MessageComponent BuildNavButtons(int page, int pageCount)
    {
        ComponentBuilder builder = new();

        if (pageCount <= 1)
            return builder.Build();

        if (page > 0)
        {
            builder.WithButton(
                "◀ Previous",
                $"collab_page_prev:{page}",
                ButtonStyle.Secondary);
        }

        if (page < pageCount - 1)
        {
            builder.WithButton(
                "Next ▶",
                $"collab_page_next:{page}",
                ButtonStyle.Secondary);
        }

        return builder.Build();
    }

    Embed BuildCollabEmbed(
        List<CollabEntry> collabs,
        int page,
        int pageCount)
    {
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Accessing Collaboration Schedule...")
            .WithDescription("Status: active\nCollab queries detected...\n\n—")
            .WithColor(new Color(0x5865F2))
            .WithFooter($"System Active • Page {page + 1} of {pageCount}");

        foreach (CollabEntry collab in collabs)
        {
            builder.AddField(
                $"🌏 {collab.Description}",
                BuildCollabField(collab),
                false);
        }

        return builder.Build();
    }

    string BuildCollabField(CollabEntry collab)
    {
        long unixSeconds = collab.ScheduledAtParsed.ToUnixTimeSeconds();

        string value = $"> <t:{unixSeconds}:F>\n";

        value += $"> Host: <@{collab.OwnerId}>\n";

        if (!string.IsNullOrWhiteSpace(collab.GameName))
            value += $"> Game: {collab.GameName}\n";

        if (collab.Participants.Any())
        {
            value += "> \n> Collaborators:\n";

            foreach (CollabParticipant p in collab.Participants)
            {
                string icon = p.Status switch
                {
                    ParticipantStatus.Accepted => "🟢",
                    ParticipantStatus.Pending => "🟡",
                    ParticipantStatus.Declined => "🔴",
                    _ => "⚪"
                };

                value += $"> {icon} <@{p.UserId}>\n";
            }
        }

        if (collab.ExternalCollaborators.Any())
        {
            value += "> \n> External: ";

            value += string.Join(", ", collab.ExternalCollaborators);
        }

        return value.TrimEnd();
    }
}