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
    // deletes the prompt, adds check/cross reactions to the original message, tracks it
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

        try
        {
            await original.AddReactionAsync(new Emoji("\u2705"));
            await original.AddReactionAsync(new Emoji("\u274C"));
        }
        catch (Exception ex)
        {
            Logger.Log($"[TodoCapture] Failed to add reactions to message {id}: {ex.Message}");
            return;
        }

        _data.Track(id);
        Logger.Log($"[TodoCapture] Tracked message {id} for {Context.User.Username}");
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
