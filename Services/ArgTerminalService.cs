using Discord;
using Discord.WebSocket;
using DiscordBot.Data;
using System.Text;
using Discord.Interactions;
using DiscordBot.Modules;

namespace DiscordBot.Services;

public class ArgTerminalService
{
    readonly DiscordSocketClient _client;
    readonly ArgTerminalData _data;
    readonly ArgFilesystem _fs;

    public ArgTerminalService(DiscordSocketClient client, ArgTerminalData data, ArgFilesystem fs)
    {
        _client = client;
        _data = data;
        _fs = fs;
    }

    // ─── HELPERS ────────────────────────────────────────────────────
    public async Task ResetSession()
    {
        _data.LoggedInUsers.Clear();
        _data.activeUsers = 0;

        SocketGuild? guild = _client.GetGuild(Config.GuildId);

        if (guild == null)
        {
            Logger.Log("[Warning] Guild not found");
            return;
        }

        if (_data.PublishedChannelId == 0)
        {
            Logger.Log("[Debug] No published channel set");
            return;
        }

        ITextChannel? channel =
            guild.GetChannel(_data.PublishedChannelId) as ITextChannel;

        if (channel == null)
        {
            Logger.Log("[Warning] Published channel not found");
            return;
        }

        Embed logEmbed = BuildLogHistoryEmbed();
        Embed terminalEmbed = BuildTerminalEmbed();
        Embed readEmbed     = BuildReadEmbed();


        if (_data.PublishedLMessageId != 0)
        {
            await UpdateExistingEmbed(
                channel,
                logEmbed,
                _data.PublishedLMessageId);
        }

        if (_data.PublishedTMessageId != 0)
        {
            await UpdateExistingEmbed(
                channel,
                terminalEmbed,
                _data.PublishedTMessageId);
        }

        if (_data.PublishedRMessageId != 0)
        {
            await UpdateExistingEmbed(
                channel,
                readEmbed,
                _data.PublishedRMessageId);
        }
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
                    ? $"├📄 {child.Filename} [CORRUPTED]"
                    : $"├📄 {child.Filename}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "*empty*";
    }

    string RenderHistory()
    {
        StringBuilder sb = new();

        foreach (string action in _data.GetHistory().Reverse())
        {
            sb.AppendLine($"├⚡ {action}");
        }
        
        return sb.Length > 0 ? sb.ToString() : "*No Logs Found*";
    }

    // ─── EMBEDS ────────────────────────────────────────────────────
    public Embed BuildTerminalEmbed()
    {
        int coherence    = _data.GetCoherence();
        FsNode currentNode = _fs.GetCurrentNode(_data.Cwd); // use the fixed method

        string listing = RenderDirectory(currentNode, coherence);
        
        string lastAction = _data.ActionHistory.Any()
            ? _data.ActionHistory.Last()
            : "none";

        return new EmbedBuilder()
            .WithAuthor(
                "AETHER-OS // TERMINAL SESSION",
                "https://images.icon-icons.com/213/PNG/256/Mac_Terminal-01_25118.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                $"📂 {(string.IsNullOrEmpty(_data.Cwd) ? "/" : _data.Cwd)}\n" +
                listing +
                $"\n**------------------------------------------**" +
                $"\n🔌 Active connections: {_data.activeUsers}" +
                $"\n⚡ Last action: {lastAction}" +
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
    public Embed BuildLogHistoryEmbed()
    {
        string history = RenderHistory();
        
        return new EmbedBuilder()
            .WithAuthor(
                "AETHER-OS // ACTION LOGS",
                "https://images.icon-icons.com/41/PNG/128/cab_history_archive_archives_7220.png")
            .WithTitle("---------------------------------------")
            .WithDescription(
                $"📂 {(string.IsNullOrEmpty(_data.Cwd) ? "/" : _data.Cwd)}\n" +
                history +
                $"\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .Build(); 
    }
    
    public async Task RefreshEmbeds(params ARGEmbed_Type[] embedTypes)
    {
        foreach (ARGEmbed_Type type in embedTypes.Distinct())
        {
            await RefreshEmbeds(type);
        }
    }
    
    public async Task RefreshEmbeds(ARGEmbed_Type embedType)
    {
        if (_data.PublishedChannelId == 0) return;

        SocketGuild? guild = _client.GetGuild(Config.GuildId);

        if (guild == null) return;

        ITextChannel? channel = guild.GetChannel(_data.PublishedChannelId) as ITextChannel;

        if (channel == null) return;

        switch (embedType)
        {
            case ARGEmbed_Type.Logs:
                // ─── LOGS ───────────────────────────────

                if (_data.PublishedLMessageId != 0)
                {
                    await UpdateExistingEmbed(
                        channel,
                        BuildLogHistoryEmbed(),
                        _data.PublishedLMessageId);
                }
                
                break;
            case ARGEmbed_Type.Terminal:
                // ─── TERMINAL ───────────────────────────

                if (_data.PublishedTMessageId != 0)
                {
                    await UpdateExistingEmbedWButtons(
                        channel,
                        BuildTerminalEmbed(),
                        _data.PublishedTMessageId,
                        new Dictionary<string, string>()
                        {
                            {"Navigate", "terminal_btn_nav"},
                            {"Read", "terminal_btn_read"},
                            {"Ping", "terminal_btn_ping"}
                        });
                }
                
                break;
            case ARGEmbed_Type.ReadOutput:
                // ─── READ ───────────────────────────────

                if (_data.PublishedRMessageId != 0)
                {
                    bool hasFile =
                        !string.IsNullOrWhiteSpace(_data.ReadMessageFile);

                    if (hasFile)
                    {
                        await UpdateExistingEmbedWButtons(
                            channel,
                            BuildReadEmbed(),
                            _data.PublishedRMessageId,
                            new Dictionary<string, string>()
                            {
                                {"Close File", "terminal_btn_close_file"}
                            });
                    }
                    else
                    {
                        await UpdateExistingEmbedNoComponents(
                            channel,
                            BuildReadEmbed(),
                            _data.PublishedRMessageId);
                    }
                }
                
                break;
            
        }
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
    public async Task UpdateExistingEmbedWButtons(ITextChannel? channel, Embed embed, ulong messageId, Dictionary<String, String> Btns) {
        if (channel == null)
            return;

        IUserMessage? message =
            await channel.GetMessageAsync(messageId) as IUserMessage;

        if (message == null)
            return;
        
        ComponentBuilder builder = new();

        foreach (KeyValuePair<string, string> btn in Btns)
        {
            builder.WithButton(
                label: btn.Key,
                customId: btn.Value,
                style: ButtonStyle.Secondary
            );
        }

        MessageComponent buttons = builder.Build();

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = buttons;
        });
    }
    public async Task UpdateExistingEmbedNoComponents(ITextChannel? channel, Embed embed, ulong messageId)
    {
        if (channel == null)
            return;

        IUserMessage? message =
            await channel.GetMessageAsync(messageId)
                as IUserMessage;

        if (message == null)
            return;

        await message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = new ComponentBuilder().Build();
        });
    }
    
    
    public async Task<IUserMessage> SendNewEmbed(ITextChannel? channel, Embed embed)
    {
        return await channel.SendMessageAsync(embed: embed);
    }
    public async Task<IUserMessage> SendNewEmbedWButtons (ITextChannel? channel, Embed embed, Dictionary<String, String> Btns)
    {
        ComponentBuilder builder = new();

        foreach (KeyValuePair<string, string> btn in Btns)
        {
            builder.WithButton(
                label: btn.Key,
                customId: btn.Value,
                style: ButtonStyle.Secondary
            );
        }

        MessageComponent buttons = builder.Build();
        return await channel.SendMessageAsync(embed: embed, components: buttons);
    }
}