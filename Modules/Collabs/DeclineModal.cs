using Discord;
using Discord.Interactions;

public class DeclineModal : IModal
{
    public string Title => "Decline Collaboration";

    [InputLabel("Reason (optional)")]
    [ModalTextInput(
        "reason",
        TextInputStyle.Paragraph,
        placeholder: "Busy that evening...",
        maxLength: 250)]
    public string Reason { get; set; } = "";
}