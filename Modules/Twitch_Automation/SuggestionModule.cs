using Discord.WebSocket;
namespace DiscordBot.Modules;
using Discord.Interactions;

public class SuggestionModule : InteractionModuleBase<SocketInteractionContext>
{
    // This replaces the manual OnInteractionCreated hook in TwitchRedeemHandler
    [ComponentInteraction("suggestion_complete", ignoreGroupNames: true)]
    public async Task OnMarkComplete()
    {
        SocketGuildUser? user = Context.User as SocketGuildUser;

        if (user == null || !user.GuildPermissions.Administrator)
        {
            await RespondAsync("You don't have permission to do this.", ephemeral: true);
            return;
        }

        // Context.Interaction is the button click — the message it's on is the suggestion embed
        if (Context.Interaction is SocketMessageComponent component)
        {
            await component.Message.DeleteAsync();
        }

        await RespondAsync("Suggestion marked as complete.", ephemeral: true);
    }
}