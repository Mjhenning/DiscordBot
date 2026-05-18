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
    
    await DeferAsync(ephemeral: true);
    
    Log($"[Info] /login called by {Context.User.Username}");

    bool loggedIn = _data.Login(Context.User.Id);
    if (!loggedIn)
    {
        Log($"[Debug] {Context.User.Username} already logged in");
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = "You're already logged in to the terminal. 🫧";
        });
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
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Terminal channel unavailable.";
            });
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
            await _terminal.UpdateExistingEmbedWButtons(channel, terminalEmbed, _data.PublishedTMessageId, new Dictionary<string, string>()
            {
                {"Navigate", "terminal_btn_nav"},
                {"Read", "terminal_btn_read"},
                {"Ping", "terminal_btn_ping"}
            });
        }
        else
        {
            Log($"[Debug] No existing terminal embed — posting new");
            postedTerminal = await _terminal.SendNewEmbedWButtons(channel, terminalEmbed, new Dictionary<string, string>()
            {
                {"Navigate", "terminal_btn_nav"},
                {"Read", "terminal_btn_read"},
                {"Ping", "terminal_btn_ping"}
            });
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
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Content =
                $"🫧 {Context.User.Username} has successfully logged into the AETHER-OS. Don't do anything rash ⚠️";
        });
    }
    else
    {
        Log($"[Warning] No published channel set — cannot post terminal embeds");
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = "No published channel set.";
        });
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
    
    
    // ─── BUTTONS ────────────────────────────────────────────────────
    [ComponentInteraction("terminal_btn_nav",     ignoreGroupNames: true)]
    public Task OnBtnNavigate()     => NavigateToFolder();
    
    [ComponentInteraction("terminal_btn_read", ignoreGroupNames: true)]
    public Task OnBtnRead() => ReadFile();
    
    [ComponentInteraction("terminal_btn_read_exit", ignoreGroupNames: true)]
    public Task OnBtnReadExit() => ReadExit();
    
    [ComponentInteraction("terminal_btn_ping", ignoreGroupNames: true)]
    public Task OnBtnPing() => Ping();

    public async Task NavigateToFolder()
    {
        //open menu to enter directory / file structure and navigate to their and update embed
    }

    public async Task ReadFile()
    {
        // Log($"[Debug] Building Read file embed...");
        //
        // Embed readEmbed = _terminal.BuildReadEmbed();
        // ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
        //
        // Log($"[Debug] Updating existing read embed (ID: {_data.PublishedRMessageId})");
        // await _terminal.UpdateExistingEmbedWButtons(channel, readEmbed, _data.PublishedRMessageId, new Dictionary<string, string>()
        // {
        //     {"Exit", "terminal_btn_read_exit"}
        // });
        
        //open menu to enter filename and update read embed
    }

    public async Task ReadExit()
    {
        //blanks out read embed to idle state and removes button
        
        Log($"[Debug] Building Read file embed...");
        
        Embed readEmbed = _terminal.BuildReadEmbed();
        ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
        
        Log($"[Debug] Updating existing read embed (ID: {_data.PublishedRMessageId})");
        await _terminal.UpdateExistingEmbed(channel, readEmbed, _data.PublishedRMessageId);
    }

    public async Task Ping()
    {
        //bumps coherence with 2%
        _data.BumpCoherence(2);
        
        ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
        
        Embed terminalEmbed = _terminal.BuildTerminalEmbed();
        
        Log($"[Debug] Updating existing terminal embed (ID: {_data.PublishedTMessageId})");
        await _terminal.UpdateExistingEmbedWButtons(channel, terminalEmbed, _data.PublishedTMessageId, new Dictionary<string, string>()
        {
            {"Navigate", "terminal_btn_nav"},
            {"Read", "terminal_btn_read"},
            {"Ping", "terminal_btn_ping"}
        });

        await RespondAsync($"{Context.User.Username} has successfully broken down built up bitrot by 2%! 🫧", ephemeral: true);
    }
}