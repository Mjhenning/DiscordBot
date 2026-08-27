using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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
            .WithButton("Other Connection", "handshake_other", ButtonStyle.Secondary)
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

    [ComponentInteraction("handshake_other", ignoreGroupNames: true)]
    public async Task OnOtherConnection()
    {
        if (!IsLoggedIn())
        {
            await RespondAsync(
                "Session expired. Please call /system login again.",
                ephemeral: true);
            return;
        }

        if (!_handshake.IsLinked(Context.User.Id))
        {
            await RespondAsync(
                "You don't have a linked account to transfer from. Please link your Twitch account first.",
                ephemeral: true);
            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId("handshake_other_pick")
            .WithPlaceholder("Select a user to transfer to")
            .WithMinValues(1)
            .WithMaxValues(1)
            .WithType(ComponentType.UserSelect);

        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .Build();

        await RespondAsync(
            "Select another connection to route Glossels to.",
            components: components,
            ephemeral: true);
    }

    [ComponentInteraction("handshake_other_pick", ignoreGroupNames: true)]
    public async Task OnOtherConnectionPick(string[] selectedUsers)
    {
        if (!IsLoggedIn())
        {
            await RespondAsync(
                "Session expired. Please call /system login again.",
                ephemeral: true);
            return;
        }

        if (selectedUsers.Length == 0 ||
            !ulong.TryParse(selectedUsers[0], out ulong targetId))
        {
            await RespondAsync("Invalid selection.", ephemeral: true);
            return;
        }

        if (targetId == Context.User.Id)
        {
            await RespondAsync("You can't transfer to yourself.", ephemeral: true);
            return;
        }

        var target = Context.Guild.GetUser(targetId);

        // Confirm the chosen user is actually reachable on the network.
        // If they're not linked to Twitch yet, they can't hold Glossels.
        if (!_handshake.IsLinked(targetId))
        {
            await RespondAsync(
                $"{target?.Mention ?? "That user"} isn't linked to Twitch yet, so Glossels can't be routed to them.",
                ephemeral: true);
            return;
        }

        int balance = _handshake.GetBalance(Context.User.Id);

        await RespondWithModalAsync<HandshakeTransferModal>(
            $"handshake_transfer:{targetId}",
            modifyModal: m => m.WithTitle($"Transfer to {target?.Username ?? "user"} — Balance: {balance} Glossels"));
    }

    [ModalInteraction("handshake_transfer:*", ignoreGroupNames: true)]
    public async Task OnHandshakeTransfer(string targetIdStr, HandshakeTransferModal modal)
    {
        await DeferAsync(ephemeral: true);

        if (!ulong.TryParse(targetIdStr, out ulong targetId))
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Invalid recipient.";
            });
            return;
        }

        if (!int.TryParse(modal.AmountInput, out int amount) || amount <= 0)
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Invalid amount. Enter a positive number.";
            });
            return;
        }

        if (targetId == Context.User.Id)
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "You can't transfer to yourself.";
            });
            return;
        }

        var target = Context.Guild.GetUser(targetId);
        string targetName = target?.Username ?? "Unknown connection";

        var result = _handshake.Transfer(Context.User.Id, targetId, targetName, amount);

        if (!result.Success)
        {
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = result.Message;
            });
            return;
        }

        _data.AddHistory(
            $"{Context.User.Username} transferred {amount} Glossels to {result.ToName}");
        _data.Save();

        string response = $"**>> NETWORK TRANSFER - {amount} GLOSSELS**\n" +
                          $"Routed {amount} Glossels to **{result.ToName}**.\n\n" +
                          $"{Context.User.Username} Balance: {result.FromBalance} Glossels.\n" +
                          $"{result.ToName} Balance: {result.ToBalance} Glossels.";

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = response;
        });

        string handshakeContent = $"**>> NETWORK TRANSFER - {amount} GLOSSELS**\n" +
                                  $"{Context.User.Username} routed {amount} Glossels to {result.ToName}.";

        _data.HandshakeContent = handshakeContent;
        _data.Save();

        await _terminal.RefreshEmbeds(ARGEmbed_Type.Handshake, ARGEmbed_Type.Logs, ARGEmbed_Type.Terminal);
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

public class HandshakeTransferModal : IModal
{
    public string Title => "Network Transfer";

    [InputLabel("Glossels to send")]
    [ModalTextInput("handshake_transfer_input", TextInputStyle.Short,
        placeholder: "Enter amount...", minLength: 1, maxLength: 10)]
    public string AmountInput { get; set; } = "";
}
