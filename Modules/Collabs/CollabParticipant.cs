namespace DiscordBot.Models;

public enum ParticipantStatus
{
    Pending,
    Accepted,
    Declined
}

public class CollabParticipant
{
    public ulong UserId { get; set; }

    public ParticipantStatus Status { get; set; } = ParticipantStatus.Pending;

    public string? DeclineReason { get; set; }
}