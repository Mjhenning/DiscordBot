using Discord;
using Discord.WebSocket;
using DiscordBot.Data;
using DiscordBot.Models;

namespace DiscordBot.Services;

public class CollabService
{
     readonly CollabData _data;

    public CollabService(CollabData data)
    {
        _data = data;
    }
    
    public async Task SendRequestAsync(
        CollabEntry request,
        DiscordSocketClient client)
    {
        await SendOwnerStatus(request, client);

        foreach (CollabParticipant participant in request.Participants)
        {
            if (participant.UserId == request.OwnerId)
                continue;

            await SendParticipantInvite(
                request,
                participant,
                client);
        }

        _data.Update(request);
    }
    
    async Task SendOwnerStatus(
        CollabEntry request,
        DiscordSocketClient client)
    {
        SocketUser? owner = client.GetUser(request.OwnerId);

        if (owner == null)
            return;

        IDMChannel dm = await owner.CreateDMChannelAsync();

        IUserMessage msg = await dm.SendMessageAsync(
            embed: BuildOwnerEmbed(request));

        request.OwnerDmChannelId = dm.Id;
        request.OwnerDmMessageId = msg.Id;
    }
    
    async Task SendParticipantInvite(
        CollabEntry request,
        CollabParticipant participant,
        DiscordSocketClient client)
    {
        SocketUser? user = client.GetUser(participant.UserId);

        if (user == null)
            return;

        IDMChannel dm = await user.CreateDMChannelAsync();

        MessageComponent buttons = BuildInviteButtons(request.Id);

        IUserMessage message =
            await dm.SendMessageAsync(
                embed: BuildInviteEmbed(
                    request,
                    participant.UserId),
                components: buttons);

        request.ParticipantDmMessages[participant.UserId] =
            new CollabDmReference
            {
                ChannelId = dm.Id,
                MessageId = message.Id
            };
    }
    
    public async Task UpdateMessagesAsync(
        CollabEntry request,
        DiscordSocketClient client)
    {
        await UpdateOwnerMessage(request, client);

        foreach (CollabParticipant participant in request.Participants)
        {
            if (participant.UserId == request.OwnerId)
                continue;

            await UpdateParticipantMessage(
                request,
                participant,
                client);
        }
    }
    
    // rebuild every live collab dm once at boot so old embeds adopt the
    // current format, skips collabs that never got their dms sent
    public async Task RefreshAllDmsAsync(DiscordSocketClient client)
    {
        foreach (CollabEntry request in _data.Collabs)
        {
            if (request.OwnerDmChannelId == 0 &&
                request.ParticipantDmMessages.Count == 0)
                continue;

            try
            {
                await UpdateMessagesAsync(request, client);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] Couldn't refresh collab DM {request.Id}: {ex.Message}");
            }
        }
    }

    async Task UpdateOwnerMessage(
        CollabEntry request,
        DiscordSocketClient client)
    {
        // go through rest so the refresh works even before the socket
        // user cache is populated at boot
        IDMChannel? dm =
            await client.Rest.GetChannelAsync(
                request.OwnerDmChannelId) as IDMChannel;

        if (dm == null)
            return;

        IMessage message =
            await dm.GetMessageAsync(
                request.OwnerDmMessageId);

        if (message is IUserMessage userMessage)
        {
            await userMessage.ModifyAsync(props =>
            {
                props.Embed = BuildOwnerEmbed(request);
            });
        }
    }
    
    async Task UpdateParticipantMessage(
        CollabEntry request,
        CollabParticipant participant,
        DiscordSocketClient client)
    {
        if (!request.ParticipantDmMessages.TryGetValue(
                participant.UserId,
                out CollabDmReference? reference))
            return;

        IDMChannel? dm =
            await client.Rest.GetChannelAsync(reference.ChannelId)
                as IDMChannel;

        if (dm == null)
            return;

        IUserMessage? message =
            await dm.GetMessageAsync(reference.MessageId)
                as IUserMessage;

        if (message == null)
            return;

        Embed embed;
        MessageComponent components = new ComponentBuilder().Build();

        if (participant.Status == ParticipantStatus.Pending)
        {
            // still deciding, refresh invite so the live list stays current
            embed = BuildInviteEmbed(request, participant.UserId);
            components = BuildInviteButtons(request.Id);
        }
        else
        {
            EmbedBuilder builder = new();

            if (participant.Status == ParticipantStatus.Accepted)
            {
                builder
                    .WithColor(Color.Green)
                    .WithTitle("✅ Collaboration Accepted")
                    .WithDescription(
                        $"You accepted **{request.Description}**.");
            }
            else
            {
                builder
                    .WithColor(Color.Red)
                    .WithTitle("❌ Collaboration Declined")
                    .WithDescription(
                        $"You declined **{request.Description}**.");

                // echo only the participant's own reason
                if (!string.IsNullOrWhiteSpace(participant.DeclineReason))
                {
                    builder.AddField(
                        "Reason",
                        participant.DeclineReason);
                }
            }

            builder.AddField(
                "Time",
                $"<t:{request.ScheduledAtParsed.ToUnixTimeSeconds()}:F>");

            builder.AddField(
                "Collaborators",
                BuildParticipantsValue(
                    request,
                    participant.UserId,
                    includeDeclineReason: false));

            embed = builder.Build();
        }

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

    public async Task DeleteDmMessagesAsync(
        IEnumerable<CollabEntry> collabs,
        DiscordSocketClient client)
    {
        foreach (CollabEntry collab in collabs)
        {
            await DeleteDmMessageAsync(
                collab.OwnerDmChannelId,
                collab.OwnerDmMessageId,
                client);

            foreach (CollabDmReference reference in collab.ParticipantDmMessages.Values)
            {
                await DeleteDmMessageAsync(
                    reference.ChannelId,
                    reference.MessageId,
                    client);
            }
        }
    }

    async Task DeleteDmMessageAsync(
        ulong channelId,
        ulong messageId,
        DiscordSocketClient client)
    {
        if (channelId == 0 || messageId == 0)
            return;

        try
        {
            IDMChannel? dm =
                await client.Rest.GetChannelAsync(channelId)
                    as IDMChannel;

            if (dm == null)
                return;

            IMessage message =
                await dm.GetMessageAsync(messageId);

            await message.DeleteAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Warning] Couldn't delete collab DM ({channelId}/{messageId}): {ex.Message}");
        }
    }

    Embed BuildOwnerEmbed(CollabEntry request)
    {
        EmbedBuilder builder = new();

        builder
            .WithTitle("🌏 Collaboration Request")
            .WithColor(Color.Blue);

        builder.AddField(
            "Description",
            request.Description);

        builder.AddField(
            "Time",
            $"<t:{request.ScheduledAtParsed.ToUnixTimeSeconds()}:F>");

        if (!string.IsNullOrWhiteSpace(request.GameName))
        {
            builder.AddField(
                "Game",
                request.GameName,
                true);
        }

        string participants = BuildParticipantsValue(
            request,
            request.OwnerId,
            includeDeclineReason: true);

        builder.AddField(
            "Participants",
            participants);
        
        builder.WithDescription(
            $"Last updated <t:{request.LastUpdated.ToUnixTimeSeconds()}:R>");

        builder.WithFooter("This message updates automatically.");

        return builder.Build();
    }
    
    Embed BuildInviteEmbed(
        CollabEntry request,
        ulong viewerId)
    {
        EmbedBuilder builder = new();

        builder
            .WithTitle("🌏 Collaboration Invitation")
            .WithColor(Color.Green);

        builder.AddField(
            "Host",
            $"<@{request.OwnerId}>");

        builder.AddField(
            "Description",
            request.Description);

        builder.AddField(
            "Time",
            $"<t:{request.ScheduledAtParsed.ToUnixTimeSeconds()}:F>");

        if (!string.IsNullOrWhiteSpace(request.GameName))
        {
            builder.AddField(
                "Game",
                request.GameName,
                true);
        }

        builder.AddField(
            "Collaborators",
            BuildParticipantsValue(
                request,
                viewerId,
                includeDeclineReason: false));

        builder.WithFooter(
            "Choose Accept or Decline below.");

        return builder.Build();
    }

    MessageComponent BuildInviteButtons(ulong requestId)
    {
        return new ComponentBuilder()

            .WithButton(
                "✅ Accept",
                $"collab_accept:{requestId}",
                ButtonStyle.Success)

            .WithButton(
                "❌ Decline",
                $"collab_decline:{requestId}",
                ButtonStyle.Danger)

            .Build();
    }

    // status list shared by every collab dm, externals drop in when present
    string BuildParticipantsValue(
        CollabEntry request,
        ulong viewerId,
        bool includeDeclineReason)
    {
        string value = "";

        foreach (CollabParticipant p in request.Participants)
        {
            string icon = p.Status switch
            {
                ParticipantStatus.Accepted => "🟢",
                ParticipantStatus.Pending => "🟡",
                ParticipantStatus.Declined => "🔴",
                _ => "⚪"
            };

            value += $"{icon} <@{p.UserId}>";

            if (p.UserId == viewerId)
                value += " (you)";

            // decline reasons are reserved for the host
            if (includeDeclineReason && !string.IsNullOrWhiteSpace(p.DeclineReason))
                value += $" — {p.DeclineReason}";

            value += "\n";
        }

        if (request.ExternalCollaborators.Any())
        {
            value += "\n🌐 External Collaborators\n";

            foreach (string name in request.ExternalCollaborators)
                value += $"• {name}\n";
        }

        return value.TrimEnd();
    }
}