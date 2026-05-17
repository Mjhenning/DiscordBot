using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class ARG : InteractionModuleBase<SocketInteractionContext>
{
    readonly ArgTerminalData _data;
    readonly StreamWriter _log;
    
    public ARG(ArgTerminalData data, StreamWriter log)
    {
        _data = data;
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
        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor("AETHER-OS // TERMINAL SESSION", "https://images.icon-icons.com/213/PNG/256/Mac_Terminal-01_25118.png")
            .WithTitle("---------------------------------------")
            .WithDescription($"📁{_data.Cwd}" +
                             $"\n├ chatroom_2003.log" + //unsure how to setup these properly to be acessed
                             $"\n├ chatroom_2005.log" +
                             $"\n├ ~~/scrt/~~ [RESTRICTED]" +
                             $"\n\n**------------------------------------------**" +
                             $"\n🔌 Active connections: {_data.activeUsers}" +
                             $"\n⚡ Last action: {_data.LastAction}" +
                             $"\n💾 Coherence: {_data.GetCoherence()}%" +
                             $"\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter($"System Active • 4/30/03, 3:00 AM");

        return builder.Build();
    }

    Embed BuildReadEmbed()
    {
        EmbedBuilder builder = new EmbedBuilder()
            .WithAuthor($"AETHER-OS // {_data.ReadMessageFile}", "https://images.icon-icons.com/54/PNG/256/windowviewdetailscreen_ventana_vista_detall_10768.png")
            .WithTitle("---------------------------------------")
            .WithDescription($"📁{_data.ReadMessageContent}" +
                             $"\n\n**------------------------------------------**")
            .WithColor(new Color(0xffffff))
            .WithFooter($"System Active • 4/30/03, 3:00 AM");
        
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
}