using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

[Group("system", "Access AETHER-OS filesystem")]
public class ARG : InteractionModuleBase<SocketInteractionContext>
{
    
    int activeUsers {get; set;}


    [SlashCommand("login", "Login to continue with or start a terminal session")]
    public async Task TerminalStart()
    {
        
    }
}