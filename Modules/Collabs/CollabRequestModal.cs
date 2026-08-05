using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules;

public class CollabRequestModal : IModal
{
    public string Title => "Create Collaboration";

    [InputLabel("Stream / Collaboration Title")]
    [ModalTextInput(
        "collab_description",
        placeholder: "Minecraft Proxy SMP",
        maxLength: 100)]
    public string Description { get; set; } = "";

    [InputLabel("Start Time (22:00 or 10 PM)")]
    [ModalTextInput(
        "collab_time",
        placeholder: "20:00")]
    public string Time { get; set; } = "";

    [InputLabel("Game (optional)")]
    [ModalTextInput(
        "collab_game",
        placeholder: "Minecraft",
        maxLength: 50)]
    public string Game { get; set; } = "";
    
    [InputLabel("🌐 External Collaborators (optional)")]
    [ModalTextInput(
        "external_collaborators",
        TextInputStyle.Paragraph,
        placeholder: "One name per line",
        maxLength: 300)]
    public string ExternalCollaborators { get; set; } = "";
}