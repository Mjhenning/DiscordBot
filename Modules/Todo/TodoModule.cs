using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using DiscordBot.Data;

namespace DiscordBot.Modules.Todo;

public class TodoModule : InteractionModuleBase<SocketInteractionContext>
{
    //-------------------------------------SLASH COMMAND-------------------------------------

    [SlashCommand("todo", "Create or view a todo")]
    public async Task TodoMenu()
    {
        await RespondAsync(
            "Open the menu to create or view todos.",
            components: BuildTodoMenu(),
            ephemeral: true);
    }

    //-------------------------------------NAVIGATION FROM MAIN MENU-------------------------------------

    [ComponentInteraction("todo_create")]
    public async Task CreateTodoEntry()
    {
        await DeferAsync(ephemeral: true);
        await RespondWithModalAsync<TodoCreateModal>("todo_create_submit");
    }

    [ComponentInteraction("todo_view")]
    public async Task ViewTodoEntries()
    {
        var entries = TodoService.GetAllEntries().Where(e => e.Status == "pending").ToList();

        if (entries.Count == 0)
        {
            await FollowupAsync("No pending todos yet. Create one!", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithAuthor("AETHER-OS // TODO")
            .WithTitle("Pending Todos")
            .WithColor(Color.Gold);

        foreach (var e in entries.Take(30))
        {
            var due = e.DueDate != null ? $" | Due: {e.DueDate.Substring(0, 10)}" : "";
            embed.AddField(e.Title, $"{e.Status}" + due + $"\nPriority: {e.Priority}", inline: true);
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    //-------------------------------------CREATE MODAL -------------------------------------

    [ModalInteraction("todo_create_submit")]
    public async Task TodoCreateSubmitted(TodoCreateModal modal)
    {
        await DeferAsync(ephemeral: true);

        var entry = new TodoEntry
        {
            Title = modal.TaskTitle,
            Description = modal.Description ?? "",
            Assignee = modal.Assignee ?? Context.User.Mention,
            Priority = modal.Priority ?? "low",
            Source = "discord",
            Status = "pending"
        };

        if (!string.IsNullOrWhiteSpace(modal.DueDate))
            entry.DueDate = modal.DueDate;

        TodoService.AddEntry(entry);

        // Post the todo embed to the todo channel
        await PostTodoEmbed(entry, Config.TodoChannelId, Context);

        // Announcement to mod channel
        await PostTodoAnnouncement(entry, action: "created", ctx: Context);

        // DM to server owner
        try
        {
            var owner = Context.Guild.Owner;
            await owner.SendMessageAsync($"New todo created:\n**{entry.Title}**\nAssignee: {entry.Assignee}");
        }
        catch
        {
            Logger.Log($"[Todo] Failed to DM owner for notification");
        }

        await FollowupAsync($"Todo created: **{entry.Title}**", ephemeral: true);
    }

    //-------------------------------------COMPLETE -------------------------------------

    [ComponentInteraction("todo_complete:{todoId}", ignoreGroupNames: true)]
    public async Task TodoCompleteHandler(string todoId)
    {
        if (!TodoService.UpdateEntry(todoId, e =>
        {
            e.Status = "completed";
            e.CompletedAt = DateTime.UtcNow.ToString("o");
        }))
        {
            await FollowupAsync("Todo not found.", ephemeral: true);
            return;
        }

        var entry = TodoService.GetAllEntries().FirstOrDefault(e => e.Id == todoId);
        if (entry != null)
            await PostTodoAnnouncement(entry, action: "completed", ctx: Context);

        await FollowupAsync("Todo completed.", ephemeral: true);
    }

    //-------------------------------------DECLINE -------------------------------------

    [ComponentInteraction("todo_decline:{todoId}", ignoreGroupNames: true)]
    public async Task TodoDeclineHandler(string todoId)
    {
        if (!TodoService.UpdateEntry(todoId, e =>
        {
            e.Status = "declined";
            e.CompletedAt = DateTime.UtcNow.ToString("o");
        }))
        {
            await FollowupAsync("Todo not found.", ephemeral: true);
            return;
        }

        var entry = TodoService.GetAllEntries().FirstOrDefault(e => e.Id == todoId);
        if (entry != null)
            await PostTodoAnnouncement(entry, action: "declined", ctx: Context);

        await FollowupAsync("Todo declined.", ephemeral: true);
    }

    //-------------------------------------POSTPONE -------------------------------------

    [ComponentInteraction("todo_postpone:{todoId}", ignoreGroupNames: true)]
    public async Task TodoPostponeHandler(string todoId)
    {
        await DeferAsync(ephemeral: true);
        await RespondWithModalAsync<TodoPostponeModal>($"todo_postpone_submit:{todoId}");
    }

    [ModalInteraction("todo_postpone_submit:{todoId}")]
    public async Task TodoPostponeSubmitted(TodoPostponeModal modal)
    {
        await DeferAsync(ephemeral: true);

        TodoService.UpdateEntry(modal.TodoId, e =>
        {
            e.Status = "postponed";
            e.PostponedUntil = modal.NewDueDate;
        });

        var entry = TodoService.GetAllEntries().FirstOrDefault(e => e.Id == modal.TodoId);
        if (entry != null)
            await PostTodoAnnouncement(entry, action: "postponed", ctx: Context);

        await FollowupAsync($"Todo postponed. New due date set.", ephemeral: true);
    }

    //-------------------------------------HELPERS -------------------------------------

    static MessageComponent BuildTodoMenu()
    {
        return new ComponentBuilder()
            .WithButton("\u270E\ufe0F Create", "todo_create", ButtonStyle.Primary)
            .WithButton("\u2665 View", "todo_view", ButtonStyle.Secondary)
            .Build();
    }

    static async Task PostTodoEmbed(TodoEntry entry, ulong postChannelId, SocketInteractionContext ctx)
    {
        ITextChannel? channel = null;

        try
        {
            channel = await ctx.Client.GetChannelAsync(postChannelId) as ITextChannel;
        }
        catch { }

        if (channel == null) return;

        string status = entry.Status switch
        {
            "completed" => $"Completed at {entry.CompletedAt?.Substring(0, 10) ?? "N/A"}",
            "declined" => "Declined",
            "postponed" => $"Postponed until {entry.PostponedUntil?.Substring(0, 10) ?? "N/A"}",
            _ => "Pending"
        };

        string description = $"{status}" + (entry.DueDate != null ? $" | Due: {entry.DueDate.Substring(0, 10)}" : "");

        var embed = new EmbedBuilder()
            .WithAuthor($"{entry.Assignee}", "@Ninja", $"https://www.twitch.tv/{entry.Assignee}")
            .WithTitle(entry.Title)
            .WithDescription(description)
            .WithFooter($"Priority: {entry.Priority}\nSystem Active • {DateTimeOffset.UtcNow.Year}/{DateTimeOffset.UtcNow.Month}/{DateTimeOffset.UtcNow.Day}")
            .WithCurrentTimestamp()
            .WithColor(entry.Status switch
            {
                "pending" => Color.Gold,
                "completed" => Color.Green,
                "declined" => Color.Red,
                "postponed" => Color.Orange,
                _ => Color.Gold
            });

        if (!string.IsNullOrEmpty(entry.Description))
            embed.AddField("Details", entry.Description, inline: false);

        await channel.SendMessageAsync(embed: embed.Build());
    }

    static async Task PostTodoAnnouncement(TodoEntry entry, string action, SocketInteractionContext ctx)
    {
        ITextChannel? channel = null;

        try
        {
            channel = await ctx.Client.GetChannelAsync(Config.ModChannelId) as ITextChannel;
        }
        catch { }

        if (channel == null) return;

        string prefix = action switch
        {
            "created" => "\u2705 Todo Created",
            "completed" => "\u2705 Todo Completed",
            "declined" => "\u274C Todo Declined",
            "postponed" => "\u269F\ufe0F Todo Postponed",
            _ => ""
        };

        string suffix = entry.Status == "postponed" && entry.PostponedUntil != null
            ? $" - New Date: {entry.PostponedUntil.Substring(0, 10)}"
            : "";

        var embed = new EmbedBuilder()
            .WithAuthor("AETHER-OS // TODO")
            .WithTitle($"{prefix}: {entry.Title}{suffix}")
            .WithDescription($"From {entry.Source}\nAssignee: {entry.Assignee}")
            .WithColor(entry.Status switch
            {
                "pending" => Color.Gold,
                "completed" => Color.Green,
                "declined" => Color.Red,
                "postponed" => Color.Orange,
                _ => Color.Gold
            })
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }
}