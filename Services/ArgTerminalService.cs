using Discord;
using Discord.WebSocket;
using DiscordBot.Data;
using System.Text;
using DiscordBot.Modules;

namespace DiscordBot.Services;

public class ArgTerminalService
{
    readonly DiscordSocketClient _client;
    readonly ArgTerminalData _data;
    readonly ArgFilesystem _fs;
    readonly StreamWriter _log;

    public ArgTerminalService(DiscordSocketClient client, ArgTerminalData data, ArgFilesystem fs, StreamWriter log)
    {
        _client = client;
        _data = data;
        _fs = fs;
        _log = log;
    }

    // ─── HELPERS ────────────────────────────────────────────────────
    void Log(string msg)
    {
        Console.WriteLine(msg);
        _log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }
    public async Task ResetSession()
    {
        _data.LoggedInUsers.Clear();
        _data.activeUsers = 0;

        SocketGuild? guild = _client.GetGuild(Config.GuildId);
        
        if (guild == null)
        {
            Log("[Warning] Guild not found");
            return;
        }

        ITextChannel? channel =
            guild.GetChannel(_data.PublishedChannelId) as ITextChannel;

        if (channel == null)
            return;

        Embed terminalEmbed = BuildTerminalEmbed();
        Embed readEmbed     = BuildReadEmbed();

        await UpdateExistingEmbed(
            channel,
            terminalEmbed,
            _data.PublishedTMessageId);

        await UpdateExistingEmbed(
            channel,
            readEmbed,
            _data.PublishedRMessageId);
    }
    string RenderDirectory(FsNode node, int coherence)
    {
        StringBuilder sb = new();

        foreach (FsNode child in node.Children.Values)
        {
            if (child.IsDirectory)
            {
                sb.AppendLine($"├📁 /{child.Name}/");
            }
            else
            {
                if (child.UnlockedAtCoherence.HasValue &&
                    coherence < child.UnlockedAtCoherence.Value)
                    continue;

                sb.AppendLine(child.Corrupted
                    ? $"📄 {child.Filename} [CORRUPTED]"
                    : $"📄 {child.Filename}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "*empty*";
    }

    // ─── EMBEDS ────────────────────────────────────────────────────
    public Embed BuildTerminalEmbed()
    {
        int coherence = _data.GetCoherence();

        FsNode currentNode;

        if (string.IsNullOrEmpty(_data.Cwd) || _data.Cwd == "/")
        {
            currentNode = _fs.Root;
        }
        else
        {
            string fullPath =
                Path.Combine(_fs.RootPath, _data.Cwd.TrimStart('/'));

            _fs.PathIndex.TryGetValue(fullPath, out FsNode? found);

            currentNode = found ?? _fs.Root;
        }

        string listing = RenderDirectory(currentNode, coherence);

        return new EmbedBuilder()
            .WithAuthor(
                "AETHER-OS // TERMINAL SESSION",
                "https://images.icon-icons.com/213/PNG/256/Mac_Terminal-01_25118.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                $"📂 {(_data.Cwd == "" ? "/" : _data.Cwd)}\n" +
                listing +
                $"\n**------------------------------------------**" +
                $"\n🔌 Active connections: {_data.activeUsers}" +
                $"\n⚡ Last action: {_data.LastAction ?? "none"}" +
                $"\n💾 Coherence: {coherence}%" +
                $"\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .Build();
    }
    public Embed BuildReadEmbed()
    {
        int coherence = _data.GetCoherence();

        string description = string.IsNullOrEmpty(_data.ReadMessageContent)
            ? "*no file loaded*"
            : _data.ReadMessageContent.Replace(
                "{coherence}",
                coherence.ToString());

        return new EmbedBuilder()
            .WithAuthor(
                $"AETHER-OS // {(string.IsNullOrEmpty(_data.ReadMessageFile)
                    ? "IDLE"
                    : _data.ReadMessageFile)}",
                "https://images.icon-icons.com/54/PNG/256/windowviewdetailscreen_ventana_vista_detall_10768.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                description +
                "\n\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .Build();
    }
    public async Task UpdateExistingEmbed(ITextChannel? channel, Embed embed, ulong messageId) {
        if (channel == null)
            return;

        IUserMessage? message =
            await channel.GetMessageAsync(messageId) as IUserMessage;

        if (message == null)
            return;

        await message.ModifyAsync(props => props.Embed = embed);
    }
    public async Task<IUserMessage> SendNewEmbed(ITextChannel? channel, Embed embed)
    {
        return await channel.SendMessageAsync(embed: embed);
    }
    
    // ─── BUTTONS ────────────────────────────────────────────────────
    // [ComponentInteraction("terminal_btn_nav",     ignoreGroupNames: true)]
    // public Task OnBtnNavigate()     => NavigateToFolder();
    //
    // [ComponentInteraction("terminal_btn_read", ignoreGroupNames: true)]
    // public Task OnBtnRead() => ReadFile();
    //
    // [ComponentInteraction("terminal_read_exit", ignoreGroupNames: true)]
    // public Task OnBtnReadExit() => ReadExit();
    
}