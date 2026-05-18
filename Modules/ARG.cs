using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class ARG : InteractionModuleBase<SocketInteractionContext>
{
    readonly ArgTerminalData _data;
    readonly ArgFilesystem _fs;
    readonly StreamWriter _log;
    
    public ARG(ArgTerminalData data, ArgFilesystem fs, StreamWriter log)
    {
        _data = data;
        _fs   = fs;
        _log = log;
    }

    void Log(string msg) { Console.WriteLine(msg); _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}"); }

    [SlashCommand("login", "Login to continue with or start a terminal session")]
    public async Task TerminalStart()
    {
        Log($"[Info] /system login called by {Context.User.Username}");

        _data.activeUsers++;

        if (_data.PublishedChannelId != 0) //if assigned channel
        {
            ITextChannel? channel = Context.Guild.GetChannel(_data.PublishedChannelId) as ITextChannel;
            
            Embed terminalEmbed = BuildTerminalEmbed();
            Embed readEmbed       = BuildReadEmbed();

            IUserMessage postedTerminal;
            IUserMessage postedRead;

            if (_data.PublishedRMessageId != 0) 
                await UpdateExistingEmbed(channel, readEmbed, _data.PublishedRMessageId);
            else
            {
                postedRead = await SendNewEmbed(channel, readEmbed);
                _data.SetPublished(postedRead.Id, channel.Id, ARGEmbed_Type.ReadOutput);
            }

            if (_data.PublishedTMessageId != 0) 
                await UpdateExistingEmbed(channel, terminalEmbed, _data.PublishedTMessageId);
            else
            {
                postedTerminal = await SendNewEmbed(channel, terminalEmbed);
                _data.SetPublished(postedTerminal.Id, channel.Id, ARGEmbed_Type.Terminal);
            }
        }
        
    }

    [SlashCommand("logout", "logout to stop interacting with terminal")]
    public async Task Disconnect()
    {
        Log($"[Info] /disconnect called by {Context.User.Username}");
        _data.activeUsers--;
    }
    
    // ─── EMBED BUILDER ────────────────────────────────────────────────────────

    Embed BuildTerminalEmbed()
    {
        int coherence = _data.GetCoherence();

        // resolve current directory node
        FsNode currentNode;
        if (string.IsNullOrEmpty(_data.Cwd) || _data.Cwd == "/")
        {
            currentNode = _fs.Root;
        }
        else
        {
            string fullPath = Path.Combine(_fs.RootPath, _data.Cwd.TrimStart('/'));
            _fs.PathIndex.TryGetValue(fullPath, out FsNode? found);
            currentNode = found ?? _fs.Root;
        }

        string listing = RenderDirectory(currentNode, coherence);

        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor("AETHER-OS // TERMINAL SESSION",
                "https://images.icon-icons.com/213/PNG/256/Mac_Terminal-01_25118.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                $"📁 {(_data.Cwd == "" ? "/" : _data.Cwd)}\n" +
                listing +
                $"\n**------------------------------------------**" +
                $"\n🔌 Active connections: {_data.activeUsers}" +
                $"\n⚡ Last action: {_data.LastAction ?? "none"}" +
                $"\n💾 Coherence: {coherence}%" +
                $"\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter("System Active • 4/30/03, 3:00 AM");

        return builder.Build();
    }

    Embed BuildReadEmbed()
    {
        int coherence = _data.GetCoherence();

        string description = string.IsNullOrEmpty(_data.ReadMessageContent)
            ? "*no file loaded*"
            : _data.ReadMessageContent.Replace("{coherence}", coherence.ToString());

        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor(
                $"AETHER-OS // {(string.IsNullOrEmpty(_data.ReadMessageFile) ? "IDLE" : _data.ReadMessageFile)}",
                "https://images.icon-icons.com/54/PNG/256/windowviewdetailscreen_ventana_vista_detall_10768.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                description +
                $"\n\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter("System Active • 4/30/03, 3:00 AM");

        return builder.Build();
    }

    async Task UpdateExistingEmbed(ITextChannel? channel, Embed embed, ulong messageId)
    {
        IUserMessage? message = await channel.GetMessageAsync(messageId) as IUserMessage;
        Log($"[Debug] Message: {message?.Id.ToString() ?? "NULL"}");
        if (message == null) return;
        
        await message.ModifyAsync(props => props.Embed = embed);
    }

    async Task<IUserMessage> SendNewEmbed(ITextChannel? channel, Embed embed)
    {
        return await channel.SendMessageAsync(embed: embed);
    }
    
    // ─── DIRECTORY HELPERS ────────────────────────────────────────────────────────
    
    string RenderDirectory(FsNode node, int coherence)
    {
        StringBuilder sb = new();

        foreach (FsNode child in node.Children.Values)
        {
            if (child.IsDirectory)
            {
                sb.AppendLine($"📁 /{child.Name}/");
            }
            else
            {
                if (child.UnlockedAtCoherence.HasValue && coherence < child.UnlockedAtCoherence.Value)
                    continue;

                sb.AppendLine(child.Corrupted
                    ? $"📄 {child.Filename} [CORRUPTED]"
                    : $"📄 {child.Filename}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "*empty*";
    }
    
    string RenderFileContent(FsNode node, int coherence)
    {
        if (node.Content == null) return "*no content*";

        return string.Join("\n", node.Content)
            .Replace("{coherence}", coherence.ToString());
    }
}