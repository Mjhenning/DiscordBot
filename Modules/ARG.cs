using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class ARG : InteractionModuleBase<SocketInteractionContext>
{
    readonly ArgTerminalData _data;
    readonly ArgFilesystem _fs;
    readonly StreamWriter _log;
    readonly ArgTerminalService _terminal;
    
    public ARG(
        ArgTerminalData data,
        ArgFilesystem fs,
        StreamWriter log,
        ArgTerminalService terminal)
    {
        _data = data;
        _fs = fs;
        _log = log;
        _terminal = terminal;
    }

    void Log(string msg) { Console.WriteLine(msg); _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

    [SlashCommand("login", "Login to continue with or start a terminal session")]
    public async Task TerminalStart()
{
    Log($"[Info] /login called by {Context.User.Username}");

    bool loggedIn = _data.Login(Context.User.Id);
    if (!loggedIn)
    {
        Log($"[Debug] {Context.User.Username} already logged in");
        await RespondAsync("You're already logged in to the terminal. 🫧", ephemeral: true);
        return;
    }

    Log($"[Debug] activeUsers after login: {_data.activeUsers}");

    if (_data.PublishedChannelId != 0)
    {
        Log($"[Debug] Published channel found: {_data.PublishedChannelId}");
        ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;

        if (channel == null)
        {
            Log($"[Warning] Could not resolve channel {_data.PublishedChannelId} as ITextChannel");
            await RespondAsync("Terminal channel unavailable.", ephemeral: true);
            return;
        }

        Log($"[Debug] Building embeds...");
        Embed terminalEmbed = _terminal.BuildTerminalEmbed();
        Embed readEmbed = _terminal.BuildReadEmbed();
        Log($"[Debug] Embeds built successfully");

        IUserMessage postedTerminal;
        IUserMessage postedRead;

        // ── Terminal embed first ────────────────────────────────────────
        if (_data.PublishedTMessageId != 0)
        {
            Log($"[Debug] Updating existing terminal embed (ID: {_data.PublishedTMessageId})");
            await _terminal.UpdateExistingEmbed(channel, terminalEmbed, _data.PublishedTMessageId);
        }
        else
        {
            Log($"[Debug] No existing terminal embed — posting new");
            postedTerminal = await _terminal.SendNewEmbed(channel, terminalEmbed);
            Log($"[Debug] Terminal embed posted with ID: {postedTerminal.Id}");
            _data.SetPublished(postedTerminal.Id, channel.Id, ARGEmbed_Type.Terminal);
        }

        // ── Read embed second ───────────────────────────────────────────
        if (_data.PublishedRMessageId != 0)
        {
            Log($"[Debug] Updating existing read embed (ID: {_data.PublishedRMessageId})");
            await _terminal.UpdateExistingEmbed(channel, readEmbed, _data.PublishedRMessageId);
        }
        else
        {
            Log($"[Debug] No existing read embed — posting new");
            postedRead = await _terminal.SendNewEmbed(channel, readEmbed);
            Log($"[Debug] Read embed posted with ID: {postedRead.Id}");
            _data.SetPublished(postedRead.Id, channel.Id, ARGEmbed_Type.ReadOutput);
        }

        Log($"[Info] /login complete for {Context.User.Username} — terminal session active");
        await RespondAsync($"🫧 {Context.User.Username} has successfully logged into the AETHER-OS. Don't do anything rash ⚠️", ephemeral: true);
    }
    else
    {
        Log($"[Warning] No published channel set — cannot post terminal embeds");
        await RespondAsync("No published channel set.", ephemeral: true);
    }
}

    [SlashCommand("logout", "logout to stop interacting with terminal")]
    public async Task Disconnect()
    {
        Log($"[Info] /logout called by {Context.User.Username}");

        bool loggedOut = _data.Logout(Context.User.Id);
        if (!loggedOut)
        {
            Log($"[Debug] {Context.User.Username} was not logged in");
            await RespondAsync($"{Context.User.Username} you haven't connected yet!", ephemeral: true);
            return;
        }

        Log($"[Debug] activeUsers after logout: {_data.activeUsers}");
        await RespondAsync($"{Context.User.Username} has successfully logged out of the AETHER-OS. Sad to see you go! 🫧", ephemeral: true);
    }
    
    bool IsLoggedIn() => _data.LoggedInUsers.Contains(Context.User.Id);
}