using Discord.WebSocket;
namespace DiscordBot.Modules;
using Discord.Interactions;

public class SuggestionModule : InteractionModuleBase<SocketInteractionContext>
{
    // handles the suggestion_complete button click
    [ComponentInteraction("suggestion_complete", ignoreGroupNames: true)]
    public async Task OnMarkComplete()
    {
        SocketGuildUser? user = Context.User as SocketGuildUser;

        if (user == null || !user.GuildPermissions.Administrator)
        {
            await RespondAsync("You don't have permission to do this.", ephemeral: true);
            return;
        }

        // the button click is the message the suggestion embed is on
        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.Message.DeleteAsync();
        }

        await RespondAsync("Suggestion marked as complete.", ephemeral: true);
    }
}