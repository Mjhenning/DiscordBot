using Discord;
using Discord.Interactions;
using DiscordBot.Data;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class HandshakeModule : InteractionModuleBase<SocketInteractionContext>
{
    readonly HandshakeService _handshake;
    readonly ArgTerminalData _data;
    readonly ArgTerminalService _terminal;

    public HandshakeModule(HandshakeService handshake, ArgTerminalData data, ArgTerminalService terminal)
    {
        _handshake = handshake;
        _data = data;
        _terminal = terminal;
    }

    bool IsLoggedIn() => _data.LoggedInUsers.Contains(Context.User.Id);

    [ComponentInteraction("terminal_btn_handshake", ignoreGroupNames: true)]
    public async Task OnBtnHandshake()
    {
        if (!IsLoggedIn())
        {
            await RespondAsync(
                "You're not currently logged in! Please call /system login first to interact with the AETHER-OS.",
                ephemeral: true);
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Unknown Network", "handshake_unknown", ButtonStyle.Secondary)
            .Build();

        await RespondAsync(
            "Initiating network handshake protocol. Select a target.",
            components: components,
            ephemeral: true);
    }

    [ComponentInteraction("handshake_unknown", ignoreGroupNames: true)]
    public async Task OnUnknownNetwork()
    {
        if (!IsLoggedIn())
        {
            await RespondAsync(
                "Session expired. Please call /system login again.",
                ephemeral: true);
            return;
        }

        int balance = _handshake.GetBalance(Context.User.Id);

        await RespondWithModalAsync<HandshakeAmountModal>(
            "handshake_amount",
            modifyModal: m => m.WithTitle($"Network Handshake — Balance: {balance} Glossels"));
    }

    [ModalInteraction("handshake_amount", ignoreGroupNames: true)]
    public async Task OnHandshakeAmount(HandshakeAmountModal modal)
    {
        await DeferAsync(ephemeral: true);

        if (!int.TryParse(modal.AmountInput, out int amount) || amount <= 0)
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Invalid amount. Enter a positive number.";
            });
            return;
        }

        _data.AddHistory($"{Context.User.Username} initiated handshake ({amount} Glossels)");
        _data.Save();

        var result = _handshake.SoloGamble(Context.User.Id, Context.User.Username, amount);

        if (result.Type == "error")
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = result.Message;
            });
            return;
        }

        string response = $"**>> NETWORK HANDSHAKE - SENDING {amount} GLOSSELS**\n" +
                           $"{result.Message}\n\n" +
                           $"{Context.User.Username} Balance: {result.NewBalance} Glossels.";

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = response;
        });

        string handshakeContent = $"**>> NETWORK HANDSHAKE - {amount} GLOSSELS**\n" +
                                   $"{result.Message}\n\n" +
                                   $"{Context.User.Username} Balance: {result.NewBalance} Glossels.";

        _data.HandshakeContent = handshakeContent;
        _data.Save();

        await _terminal.RefreshEmbeds(ARGEmbed_Type.Handshake, ARGEmbed_Type.Logs, ARGEmbed_Type.Terminal);
    }
}

public class HandshakeAmountModal : IModal
{
    public string Title => "Network Handshake";

    [InputLabel("Glossels to send")]
    [ModalTextInput("handshake_amount_input", TextInputStyle.Short,
        placeholder: "Enter amount...", minLength: 1, maxLength: 10)]
    public string AmountInput { get; set; } = "";
}
