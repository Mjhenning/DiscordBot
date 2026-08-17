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

        MessageComponent buttons =
            new ComponentBuilder()

                .WithButton(
                    "✅ Accept",
                    $"collab_accept:{request.Id}",
                    ButtonStyle.Success)

                .WithButton(
                    "❌ Decline",
                    $"collab_decline:{request.Id}",
                    ButtonStyle.Danger)

                .Build();

        IUserMessage message =
            await dm.SendMessageAsync(
                embed: BuildInviteEmbed(request),
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
    
    async Task UpdateOwnerMessage(
        CollabEntry request,
        DiscordSocketClient client)
    {
        SocketUser? owner =
            client.GetUser(request.OwnerId);

        if (owner == null)
            return;

        IDMChannel dm =
            await owner.CreateDMChannelAsync();

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

        if (participant.Status == ParticipantStatus.Pending)
            return;

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

        await message.ModifyAsync(props =>
        {
            props.Embed = builder.Build();

            // Removes the Accept / Decline buttons
            props.Components = new ComponentBuilder().Build();
        });
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

        string participants = "";

        foreach (CollabParticipant p in request.Participants)
        {
            string icon = p.Status switch
            {
                ParticipantStatus.Accepted => "🟢",
                ParticipantStatus.Pending => "🟡",
                ParticipantStatus.Declined => "🔴",
                _ => "⚪"
            };

            participants +=
                $"{icon} <@{p.UserId}>";

            if (!string.IsNullOrWhiteSpace(p.DeclineReason))
                participants += $" — {p.DeclineReason}";

            participants += "\n";
        }

        builder.AddField(
            "Participants",
            participants);
        
        builder.WithDescription(
            $"Last updated <t:{request.LastUpdated.ToUnixTimeSeconds()}:R>");

        builder.WithFooter("This message updates automatically.");

        return builder.Build();
    }
    
    Embed BuildInviteEmbed(CollabEntry request)
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

        builder.WithFooter(
            "Choose Accept or Decline below.");

        return builder.Build();
    }
}