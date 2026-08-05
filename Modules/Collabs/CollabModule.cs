using Discord;
using Discord.Interactions;
using DiscordBot.Data;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class CollabModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly CollabData _data;
    readonly CollabService _collabService;

    public CollabModule(
        CollabData data,
        CollabService collabService)
    {
        _data = data;
        _collabService = collabService;
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
                "❌ Couldn't parse that time. Use something like `20:00` or `8 PM`.",
                ephemeral: true);

            return;
        }

        CollabEntry request = new()
        {
            Id = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

            OwnerId = Context.User.Id,

            Description = modal.Description.Trim(),

            ScheduledAt = scheduled.ToUniversalTime().ToString("o"),

            GameName = string.IsNullOrWhiteSpace(modal.Game)
                ? null
                : modal.Game.Trim()
        };

        // Owner is automatically accepted
        request.Participants.Add(new CollabParticipant
        {
            UserId = Context.User.Id,
            Status = ParticipantStatus.Accepted
        });

        foreach (ulong id in ParseUsers(modal.Collaborators))
        {
            if (id == Context.User.Id)
                continue;

            // Don't add duplicates
            if (request.Participants.Any(p => p.UserId == id))
                continue;

            request.Participants.Add(new CollabParticipant
            {
                UserId = id
            });
        }

        _data.Add(request);

        await RespondAsync(
            $"✅ Collaboration request created with **{request.Participants.Count}** participant(s).",
            ephemeral: true);

        await _collabService.SendRequestAsync(request, Context.Client);
    }
    
    [ComponentInteraction("collab_accept:*", ignoreGroupNames: true)]
    public async Task Accept(string idString)
    {
        if (!ulong.TryParse(idString, out ulong id))
            return;

        CollabEntry? request = _data.Get(id);

        if (request == null)
        {
            await RespondAsync(
                "This collaboration request no longer exists.",
                ephemeral: true);

            return;
        }

        CollabParticipant? participant =
            request.Participants
                .FirstOrDefault(x => x.UserId == Context.User.Id);

        if (participant == null)
        {
            await RespondAsync(
                "You are not part of this collaboration.",
                ephemeral: true);

            return;
        }

        participant.Status = ParticipantStatus.Accepted;
        participant.DeclineReason = null;

        _data.Update(request);

        await _collabService.UpdateMessagesAsync(
            request,
            Context.Client);

        await RespondAsync(
            "✅ You've accepted the collaboration!",
            ephemeral: true);
    }
    
    [ModalInteraction("collab_decline_modal:*", ignoreGroupNames: true)]
    public async Task DeclineSubmitted(
        string idString,
        DeclineModal modal)
    {
        if (!ulong.TryParse(idString, out ulong id))
            return;

        CollabEntry? request = _data.Get(id);

        if (request == null)
        {
            await RespondAsync(
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

        participant.DeclineReason =
            string.IsNullOrWhiteSpace(modal.Reason)
                ? null
                : modal.Reason.Trim();

        _data.Update(request);

        await _collabService.UpdateMessagesAsync(
            request,
            Context.Client);

        await RespondAsync(
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
    
    // ─── REQUESTS ──────────────────────────────────────────────────────────────────
    public async Task RequestStart()
    {
        List<SelectMenuOptionBuilder> options = new();

        DateTimeOffset now = DateTimeOffset.Now;

        for (int i = 0; i < 30; i++)
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
    
    // ─── VIEW ──────────────────────────────────────────────────────────────────
    
    public async Task View()
    {
        var collabs = _data.Accepted().ToList();

        if (collabs.Count == 0)
        {
            await RespondAsync(
                "There are no confirmed collaborations.",
                ephemeral: true);

            return;
        }

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Accessing Collaboration Schedule...")
            .WithDescription("Confirmed collaborations")
            .WithColor(new Color(0x5865F2));

        foreach (var collab in collabs)
        {
            string collaborators = string.Join(
                "\n",
                collab.Participants
                    .Select(p => $"<@{p.UserId}>"));

            builder.AddField(
                $"🎮 {collab.Description}",
                $"""
                 <t:{collab.ScheduledAtParsed.ToUnixTimeSeconds()}:F>

                 **Host**
                 <@{collab.OwnerId}>

                 **Collaborators**
                 {collaborators}
                 """);
        }

        await RespondAsync(
            embed: builder.Build(),
            ephemeral: true);
    }
    
    
    
    // ─── HELPERS ──────────────────────────────────────────────────────────────────
    
    private IEnumerable<ulong> ParseUsers(string input)
    {
        HashSet<ulong> ids = new();

        foreach (string word in input.Split(' ', '\n', ',', ';'))
        {
            if (MentionUtils.TryParseUser(word, out ulong id))
            {
                ids.Add(id);
                continue;
            }

            if (ulong.TryParse(word, out id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}