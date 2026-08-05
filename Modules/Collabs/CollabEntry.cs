using Newtonsoft.Json;

namespace DiscordBot.Models;

public class CollabEntry
{
    public ulong Id { get; set; }

    public ulong OwnerId { get; set; }

    public string Description { get; set; } = "";

    public string ScheduledAt { get; set; } = "";

    public string? GameName { get; set; }

    public List<CollabParticipant> Participants { get; set; } = new();

    // Owner DM so we can keep updating it.
    public ulong OwnerDmChannelId { get; set; }

    public ulong OwnerDmMessageId { get; set; }

    // participantId -> messageId
    public Dictionary<ulong, CollabDmReference> ParticipantDmMessages { get; set; } = new();

    [JsonIgnore]
    public DateTimeOffset ScheduledAtParsed
        => DateTimeOffset.Parse(ScheduledAt);

    [JsonIgnore]
    public string ScheduledDisplay
        => ScheduledAtParsed.ToLocalTime().ToString("dddd, MMMM d • HH:mm");

    [JsonIgnore]
    public bool FullyAccepted =>
        Participants.All(x => x.Status == ParticipantStatus.Accepted);
}

public class PendingCollabRequest
{
    public ulong Id { get; set; }

    public ulong OwnerId { get; set; }

    public string Description { get; set; } = "";

    public string ScheduledAt { get; set; } = "";

    public string? GameName { get; set; }

    public List<ulong> Collaborators { get; set; } = new();
}