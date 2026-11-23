using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules.TodoCapture;

// handles the public confirm/cancel prompt buttons posted in the capture channel
public class TodoCaptureModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly TodoCaptureData _data;

    public TodoCaptureModule(TodoCaptureData data)
    {
        _data = data;
    }

    //-----CONFIRM-----
    // deletes the prompt, reposts the message as a bot-authored todo, adds reactions, tracks new message
    [ComponentInteraction("todo_capture_confirm:*", ignoreGroupNames: true)]
    public async Task Confirm(string messageId)
    {
        await DeferAsync();

        if (Context.Interaction is SocketMessageComponent component && component.Message != null)
        {
            try { await component.Message.DeleteAsync(); } catch { }
        }

        if (!ulong.TryParse(messageId, out ulong id)) return;

        var original = await Context.Channel.GetMessageAsync(id) as IUserMessage;
        if (original == null) return;

        SocketGuild guild = (Context.Guild as SocketGuild) ?? (Context.Channel as SocketGuildChannel)?.Guild!;
        SocketGuildUser? guildUser = guild?.GetUser(original.Author.Id);
        string displayName = guildUser?.DisplayName ?? original.Author.GlobalName ?? original.Author.Username;
        string avatarUrl = guildUser?.GetDisplayAvatarUrl() ?? original.Author.GetDisplayAvatarUrl() ?? original.Author.GetAvatarUrl();

        string content = original.Content ?? "";
        if (content.Length > 4000) content = content.Substring(0, 3997) + "...";

        Embed embed = new EmbedBuilder()
            .WithAuthor(displayName, avatarUrl)
            .WithTitle("Todo")
            .WithColor(new Color(0x5865F2))
            .WithDescription(content)
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .Build();

        IUserMessage newMsg;
        try
        {
            newMsg = await Context.Channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Logger.Log($"[TodoCapture] Failed to repost todo message {id}: {ex.Message}");
            return;
        }

        try
        {
            await newMsg.AddReactionAsync(new Emoji("\u2705"));
            await newMsg.AddReactionAsync(new Emoji("\u274C"));
        }
        catch (Exception ex)
        {
            Logger.Log($"[TodoCapture] Failed to add reactions to reposted message {newMsg.Id}: {ex.Message}");
            try { await newMsg.DeleteAsync(); } catch { }
            return;
        }

        try { await original.DeleteAsync(); } catch (Exception ex) { Logger.Log($"[TodoCapture] Failed to delete original message {id}: {ex.Message}"); }

        _data.Track(newMsg.Id);
        Logger.Log($"[TodoCapture] Tracked reposted message {newMsg.Id} (from {id}) for {Context.User.Username}");
    }

    //-----CANCEL-----
    // deletes the prompt only, deletion is the confirmation
    [ComponentInteraction("todo_capture_cancel:*", ignoreGroupNames: true)]
    public async Task Cancel(string messageId)
    {
        await DeferAsync();

        if (Context.Interaction is SocketMessageComponent component && component.Message != null)
        {
            try { await component.Message.DeleteAsync(); } catch { }
        }
    }
}
