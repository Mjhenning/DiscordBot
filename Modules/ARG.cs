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
    
    public ARG(ArgTerminalData data, ArgFilesystem fs, StreamWriter log, ArgTerminalService terminal)
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
        
        ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
        Embed terminalEmbed   = _terminal.BuildTerminalEmbed();

        await _terminal.UpdateExistingEmbedWButtons(channel, terminalEmbed, _data.PublishedTMessageId,
            new Dictionary<string, string>
            {
                { "Navigate", "terminal_btn_nav" },
                { "Read",     "terminal_btn_read" },
                { "Ping",     "terminal_btn_ping" }
            });

        Log($"[Debug] activeUsers after logout: {_data.activeUsers}");
        await RespondAsync($"{Context.User.Username} has successfully logged out of the AETHER-OS. Sad to see you go! 🫧", ephemeral: true);
    }
    
    bool IsLoggedIn() => _data.LoggedInUsers.Contains(Context.User.Id);
    
    
    // ─── BUTTONS ────────────────────────────────────────────────────
    [ComponentInteraction("terminal_btn_nav",     ignoreGroupNames: true)]
    public Task OnBtnNavigate()     => NavigateToFolder();
    
    [ComponentInteraction("terminal_btn_read", ignoreGroupNames: true)]
    public Task OnBtnRead() => ReadFile();
    
    [ComponentInteraction("terminal_btn_ping", ignoreGroupNames: true)]
    public Task OnBtnPing() => Ping();
    
    
    

    public async Task NavigateToFolder()
    {
        if (!IsLoggedIn()) //if logged in
        {
            await RespondAsync(
                "You must login first.",
                ephemeral: true);

            return;
        }

        _data.InteractionMode = TerminalInteractionMode.Navigating; //so select menu is for folder navigation

        //Buuilds directory list
        List<FsNode> directories =
            _fs.GetDirectories(_data.Cwd);
        
        List<SelectMenuOptionBuilder> options = new();

        // parent directory option
        if (!string.IsNullOrWhiteSpace(_data.Cwd) &&
            _data.Cwd != "/")
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel("📁 ..")
                .WithDescription("Go to parent directory")
                .WithValue("PARENT"));
        }

        foreach (FsNode dir in directories.Take(24))
        {
            options.Add(new SelectMenuOptionBuilder()
                .WithLabel($"📁 {dir.Name}")
                .WithDescription($"Navigate to {dir.Name}")
                .WithValue(dir.FullPath));
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("terminal_nav_select")
                .WithPlaceholder("Select a directory")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            $"Current Directory: `{_data.Cwd}`",
            components: menu,
            ephemeral: true);
    }
    
    [ComponentInteraction("terminal_nav_select", ignoreGroupNames: true)]
    public async Task OnNavigationSelected(string selected)
    {
        string newPath;

        if (selected == "PARENT")
        {
            FsNode current = _fs.GetCurrentNode(_data.Cwd);
            newPath = current.Parent == null || current.Parent == _fs.Root
                ? "/"
                : current.Parent.FullPath
                    .Replace(_fs.RootPath, "")
                    .Replace("\\", "/");
        }
        else
        {
            newPath = selected
                .Replace(_fs.RootPath, "")
                .Replace("\\", "/");
        }

        _data.Cwd       = newPath;
        _data.AddHistory($"{Context.User.Username} navigated to {newPath}");
        _data.Save();

        ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
        Embed terminalEmbed   = _terminal.BuildTerminalEmbed();

        await _terminal.UpdateExistingEmbedWButtons(channel, terminalEmbed, _data.PublishedTMessageId,
            new Dictionary<string, string>
            {
                { "Navigate", "terminal_btn_nav" },
                { "Read",     "terminal_btn_read" },
                { "Ping",     "terminal_btn_ping" }
            });

        await RespondAsync($"Moved to `{newPath}`", ephemeral: true);
    }

    
    
    
    public async Task ReadFile()
    {
        if (!IsLoggedIn())
        {
            await RespondAsync(
                "You must login first.",
                ephemeral: true);

            return;
        }

        _data.InteractionMode =
            TerminalInteractionMode.Reading;

        int coherence = _data.GetCoherence();

        List<FsNode> files =
            _fs.GetReadableFiles(_data.Cwd, coherence);

        if (files.Count == 0)
        {
            await RespondAsync(
                "No readable files found in this directory.",
                ephemeral: true);

            return;
        }

        List<SelectMenuOptionBuilder> options = new();

        foreach (FsNode file in files.Take(25))
        {
            string label = file.Corrupted
                ? $"📄 {file.Filename} [CORRUPTED]"
                : $"📄 {file.Filename}";

            options.Add(new SelectMenuOptionBuilder()
                .WithLabel(label)
                .WithDescription("Open file")
                .WithValue(file.FullPath));
        }

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("terminal_read_select")
                .WithPlaceholder("Select a file to read")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1))
            .Build();

        await RespondAsync(
            $"Current Directory: `{_data.Cwd}`",
            components: menu,
            ephemeral: true);
    }
    
    [ComponentInteraction("terminal_read_select", ignoreGroupNames: true)]
    public async Task OnReadFileSelected(string selected)
    {
        if (!_fs.PathIndex.TryGetValue(selected, out FsNode? node))
        {
            await RespondAsync(
                "File no longer exists.",
                ephemeral: true);

            return;
        }

        _data.ReadMessageFile =
            node.Filename ?? "unknown_file";

        string content = node.Content == null
            ? "*empty file*"
            : string.Join("\n", node.Content);

        if (content.Length > 4000)
        {
            content = content[..4000] +
                      "\n\n[FILE TRUNCATED]";
        }

        _data.ReadMessageContent = content;

        _data.AddHistory($"{Context.User.Username} read {_data.ReadMessageFile}");

        _data.Save();

        ITextChannel? channel =
            Context.Guild.GetChannel(
                    _data.PublishedChannelId)
                as ITextChannel;

        Embed readEmbed =
            _terminal.BuildReadEmbed();

        await _terminal.UpdateExistingEmbedWButtons(
            channel,
            readEmbed,
            _data.PublishedRMessageId,
            new Dictionary<string, string>()
            {
                {"Close File", "terminal_btn_close_file"}
            });

        await RespondAsync(
            $"Opened `{_data.ReadMessageFile}`",
            ephemeral: true);
    }
    
    [ComponentInteraction("terminal_btn_close_file", ignoreGroupNames: true)]
    public async Task CloseFile()
    {
        _data.ReadMessageFile = "";
        _data.ReadMessageContent = "";

        _data.AddHistory("closed active file");

        _data.Save();

        ITextChannel? channel =
            Context.Guild.GetChannel(
                    _data.PublishedChannelId)
                as ITextChannel;

        Embed readEmbed =
            _terminal.BuildReadEmbed();

        await _terminal.UpdateExistingEmbedNoComponents(
            channel,
            readEmbed,
            _data.PublishedRMessageId);

        await RespondAsync(
            "File closed.",
            ephemeral: true);
    }
    
    

    public async Task Ping()
    {
        int coherenceBefore = _data.GetCoherence();

        int coherenceAfter =
            _data.BumpCoherence(2);

        _data.AddHistory("stabilized filesystem integrity");

        _data.Save();

        ITextChannel? channel =
            Context.Guild.GetChannel(
                    _data.PublishedChannelId)
                as ITextChannel;

        Embed terminalEmbed =
            _terminal.BuildTerminalEmbed();

        await _terminal.UpdateExistingEmbedWButtons(
            channel,
            terminalEmbed,
            _data.PublishedTMessageId,
            new Dictionary<string, string>()
            {
                {"Navigate", "terminal_btn_nav"},
                {"Read", "terminal_btn_read"},
                {"Ping", "terminal_btn_ping"}
            });

        int restored =
            coherenceAfter - coherenceBefore;

        await RespondAsync(
            $"🫧 Filesystem integrity restored by {restored}%\n" +
            $"Current coherence: {coherenceAfter}%",
            ephemeral: true);
    }
}