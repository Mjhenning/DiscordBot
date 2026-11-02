using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules.Todo;

public class TodoCreateModal : IModal
{
    public string Title => "Create Todo";

    [InputLabel("Task Title")]
    [ModalTextInput("todo_title_input", TextInputStyle.Short,
        placeholder: "What needs to be done?", maxLength: 200)]
    public string TaskTitle { get; set; } = "";

    [InputLabel("Description (optional)")]
    [ModalTextInput("todo_desc_input", TextInputStyle.Paragraph,
        placeholder: "Details...")]
    public string Description { get; set; } = "";

    [InputLabel("Priority (low / med / high)")]
    [ModalTextInput("todo_priority_input", TextInputStyle.Short,
        placeholder: "low", maxLength: 10)]
    public string Priority { get; set; } = "low";

    [InputLabel("Due date (optional, YYYY-MM-DD)")]
    [ModalTextInput("todo_due_input", TextInputStyle.Short,
        placeholder: "2026-11-05")]
    public string DueDate { get; set; } = "";

    [InputLabel("Assignee (optional, mention or username)")]
    [ModalTextInput("todo_assign_input", TextInputStyle.Short,
        placeholder: "@user or leave blank for you")]
    public string Assignee { get; set; } = "";
}

public class TodoPostponeModal : IModal
{
    public string Title => "Postpone Todo";

    [InputLabel("New due date (YYYY-MM-DD)")]
    [ModalTextInput("todo_postpone_due", TextInputStyle.Short,
        placeholder: "2026-11-05")]
    public string NewDueDate { get; set; } = "";

    [InputLabel("Todo ID (hidden)")]
    [ModalTextInput("todo_id_hidden", TextInputStyle.Short,
        placeholder: "")]
    public string TodoId { get; set; } = "";
}