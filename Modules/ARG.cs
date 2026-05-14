using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class ARG : InteractionModuleBase<SocketInteractionContext>
{
    private readonly StreamWriter _log;

    public ARG(StreamWriter log)
    {
        _log = log;
    }

    void Log(string msg) { Console.WriteLine(msg); _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

    [SlashCommand("login", "Login to continue with or start a terminal session")]
    public async Task TerminalStart()
    {
        Log($"[Info] /system login called by {Context.User.Username}");
        // your logic here
    }
}