using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;
using DiscordBot.Data;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Modules;
using DiscordBot.Modules.Moderation;
using DiscordBot.Services;
using TwitchLib.Api;
using TwitchLib.EventSub.Websockets.Extensions;

DotNetEnv.Env.Load();

bool _ready = false;


//------------------------------TOP-LEVEL STATEMENTS------------------------------
// c# version 9+ does not use main class void structure.
// top level statements run directly without a class wrapper.

DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds
                     | GatewayIntents.GuildMessages
                     | GatewayIntents.GuildMessageReactions
                     | GatewayIntents.GuildMembers
                     | GatewayIntents.MessageContent
                     | GatewayIntents.DirectMessages
                     | GatewayIntents.GuildVoiceStates,

    MessageCacheSize = 1000,

    LogLevel = LogSeverity.Info
});

    Logger.Init(client);

//----------------------------SERVICE PROVIDER----------------------------
// service provider: registers singletons for DI injection into modules.

ServiceProvider services = new ServiceCollection()
    
    //-----base discord-----
    .AddSingleton(client)
    .AddSingleton(x => new InteractionService(
        x.GetRequiredService<DiscordSocketClient>()
    ))
    
    //-----data related-----
    .AddSingleton<ReactionsData>()
    .AddSingleton<ScheduleData>()
    .AddSingleton<CollabData>()
    .AddSingleton<FavouritesData>()
    .AddSingleton<TodoCaptureData>()
    
    //-----twitch related-----
    .AddSingleton<TwitchAPI>()
    .AddTwitchLibEventSubWebsockets()
    .AddSingleton<TokenManager>(sp => new TokenManager(
        sp.GetRequiredService<TwitchAPI>(),
        "Data/twitch_tokens.json"
    ))
    .AddSingleton<TwitchApiService>()
    .AddSingleton<Twitch_Notifier>()
    .AddSingleton<TwitchRedeemHandler>()
    .AddSingleton<FavouritesLiveNoti>()
    .AddSingleton<TwitchScheduleService>()
    .AddSingleton<CollabRequestCache>()
    .AddSingleton<CollabService>()
    .AddSingleton<CollabCleanupService>()
    
    //-----twitch reconnect handler-----
    .AddSingleton<EventSubReconnectService>()
    
    //-----arg related-----
    .AddSingleton<ArgFilesystem>()
    .AddSingleton<ArgTerminalData>()
    .AddSingleton<ArgTerminalService>()
    .AddSingleton(sp =>
    {
        var terminal = sp.GetRequiredService<ArgTerminalService>();
        var data = sp.GetRequiredService<ArgTerminalData>();

        return new CoherenceWatcher(
            terminal,
            data
        );
    })
    
    //-----mod logging-----
    .AddSingleton<ModerationLogs>()

    //-----live guest auto-remove-----
    .AddSingleton<LiveGuestService>()

    //-----linking-----
    .AddSingleton<TwitchChatService>()
    .AddSingleton<LinkedAccountsData>()
    
    //-----handshake gambling-----
    .AddSingleton<HandshakeService>()
    
    //-----logger-----
    .AddLogging()
    
    //-----builds service provider-----
    .BuildServiceProvider();

ModerationLogs modLogs = services.GetRequiredService<ModerationLogs>();
InteractionService interactions = services.GetRequiredService<InteractionService>();
ReactionsData reactionsData    = services.GetRequiredService<ReactionsData>();
CoherenceWatcher watcher = services.GetRequiredService<CoherenceWatcher>();
TokenManager tokenManager = services.GetRequiredService<TokenManager>();
CollabCleanupService cleanup = services.GetRequiredService<CollabCleanupService>();
TodoCaptureData todoCaptureData = services.GetRequiredService<TodoCaptureData>();

//--------------------------------TWITCH TOKEN AUTH--------------------------------
// refresh if expired, full OAuth if no refresh token.

if (!tokenManager.HasValidTokens(TwitchProfile.Broadcaster))
{
    if (tokenManager.HasRefreshToken(TwitchProfile.Broadcaster))
    {
        Logger.Log("[Info] Broadcaster token expired, refreshing...");
        await tokenManager.ForceRefreshAsync(TwitchProfile.Broadcaster);
    }
    else
    {
        Logger.Log("[Info] No Broadcaster token found. Starting Twitch authorization...");
        await tokenManager.AuthorizeAsync(TwitchProfile.Broadcaster);
    }
}

if (!tokenManager.HasValidTokens(TwitchProfile.Bot))
{
    if (tokenManager.HasRefreshToken(TwitchProfile.Bot))
    {
        Logger.Log("[Info] Bot token expired, refreshing...");
        await tokenManager.ForceRefreshAsync(TwitchProfile.Bot);
    }
    else
    {
        Logger.Log("[Info] No Bot token found. Starting Twitch authorization...");
        await tokenManager.AuthorizeAsync(TwitchProfile.Bot);
    }
}

//------------------------------------LOGGING------------------------------------
// routes both client and interaction service logs to the console.
// => {} is a lambda meaning an anonymous function.

client.Log += log =>
{
    Logger.Log($"[{log.Severity}] {log.Source}: {log.Message}");
    if (log.Exception != null) Logger.Log($"    Exception: {log.Exception}");
    return Task.CompletedTask;
};

interactions.Log += log =>
{
    Logger.Log($"[{log.Severity}] Interactions/{log.Source}: {log.Message}");
    if (log.Exception != null) Logger.Log($"    Exception: {log.Exception}");
    return Task.CompletedTask;
};

//------------------------------------READY------------------------------------
// registers slash commands and logs startup info.

client.Ready += async () =>
{
    //TODO: uncomment to clear out commands
    //
    // add this once, run the bot, then remove it
    //      await client.Rest.DeleteAllGlobalCommandsAsync();
    //
    //  // also clear guild-specific commands
    //      foreach (var guild in client.Guilds)
    //      {
    //          await guild.DeleteApplicationCommandsAsync();
    //          Logger.Log($"[Info] Cleared commands for {guild.Name}");
    //      }
    
    if (_ready) return; // prevent re-running on reconnect
    _ready = true;
    
    // initialize todo capture store
    todoCaptureData.Initialize();

    try
    {
        // load all interaction modules from this assembly
        await interactions.AddModulesAsync(System.Reflection.Assembly.GetExecutingAssembly(), services);
    }
    catch (Exception ex)
    {
        Logger.Log($"[Warning] Failed to add modules: {ex.Message}");
    }
    
    // // use RegisterCommandsToGuildAsync(guildId) during development for instant updates
    // // switch to RegisterCommandsGloballyAsync() for production, up to 1hr propagation
    await interactions.RegisterCommandsToGuildAsync(Config.GuildId, deleteMissing: true);
    
    Logger.Log($"[Info] Bot is ready — logged in as {client.CurrentUser.Username}#{client.CurrentUser.Discriminator}");
    Logger.Log($"[Info] Serving {client.Guilds.Count} guild(s)");
    foreach (var guild in client.Guilds)
    {
        Logger.Log($"[Info] Serving {guild.Name} (ID: {guild.Id}) — {guild.MemberCount} members");
    }
    
    Logger.Log($"[Info] Registered {interactions.SlashCommands.Count} slash command(s)");
    
    ArgTerminalService terminal =
        services.GetRequiredService<ArgTerminalService>();

    await terminal.ResetSession();
    Logger.Log("[Info] Terminal session reset");

    // rebuild any previously sent collab dms into the current format
    CollabService collabService =
        services.GetRequiredService<CollabService>();

    await collabService.RefreshAllDmsAsync(client);
    Logger.Log("[Collab] Live collab dms refreshed");

    try
    {
        Logger.Log("[Info] Initializing Twitch services...");

        services.GetRequiredService<TwitchRedeemHandler>();
        services.GetRequiredService<FavouritesLiveNoti>();
        services.GetRequiredService<EventSubReconnectService>();

        var notifier =
            services.GetRequiredService<Twitch_Notifier>();

        await notifier.StartAsync();

        Logger.Log("[Info] Twitch services started.");

        var chatService = services.GetRequiredService<TwitchChatService>();
        await chatService.ConnectAsync();
    }
    catch (Exception ex)
    {
        Logger.Log(
            $"[Error] Twitch initialization failed: {ex}");
    }
};

//------------------------------INTERACTION ROUTING------------------------------
// forwards all interactions to the interaction service.

client.InteractionCreated += async interaction =>
{
    SocketInteractionContext ctx = new SocketInteractionContext(client, interaction);
    
    // log what's coming in to confirm routing
    Logger.Log($"[Debug] Interaction received: {interaction.Type} — {(interaction is SocketMessageComponent c ? c.Data.CustomId : "N/A")}");
    
    var result = await interactions.ExecuteCommandAsync(ctx, services); // routes to correct module based on called slash command

    if (!result.IsSuccess)
        Logger.Log($"[Warning] Interaction failed: {result.Error} — {result.ErrorReason}");
};

//------------------------WELCOME AND AUTO ROLE------------------------
// fires when a new member joins the guild.

client.UserJoined += async member =>
{
    Logger.Log($"[Info] {member.Username} joined {member.Guild.Name}");

    SocketRole role = member.Guild.GetRole(Config.AutoRoleId);
    if (role != null)
    {
        await member.AddRoleAsync(role, new RequestOptions { AuditLogReason = "Auto role for new member" });
        Logger.Log($"[Info] Auto role '{role.Name}' assigned to {member.Username}");
    }
    else
    {
        Logger.Log($"[Warning] Auto role ID {Config.AutoRoleId} not found in {member.Guild.Name}");
    }

    if (member.Guild.GetChannel(Config.WelcomeChannelId) is IMessageChannel channel)
    {
        string msg = Config.WelcomeMessages[Random.Shared.Next(Config.WelcomeMessages.Length)]
            .Replace("{user}", member.Mention)
            .Replace("{server}", member.Guild.Name);
        await channel.SendMessageAsync(msg);
        Logger.Log($"[Info] Welcome message sent for {member.Username}");
    }
    else
    {
        Logger.Log($"[Warning] Welcome channel ID {Config.WelcomeChannelId} not found");
    }
};

//------------------------------TODO CAPTURE------------------------------
// posts a public confirm/cancel prompt when a message appears in the capture channel

client.MessageReceived += async message =>
{
    if (message.Author.IsBot) return;
    if (message.Channel.Id != Config.TodoCaptureChannelId) return;
    if (string.IsNullOrWhiteSpace(message.Content)) return;

    try
    {
        var components = new ComponentBuilder()
            .WithButton("✅ Create Todo", $"todo_capture_confirm:{message.Id}", ButtonStyle.Success)
            .WithButton("❌ Cancel", $"todo_capture_cancel:{message.Id}", ButtonStyle.Danger)
            .Build();

        var embed = new EmbedBuilder()
            .WithAuthor(message.Author.Username, message.Author.GetAvatarUrl())
            .WithTitle("Create todo from this message?")
            .WithDescription(message.Content.Length > 200 ? message.Content.Substring(0, 200) + "..." : message.Content)
            .WithColor(Color.Gold)
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .WithCurrentTimestamp()
            .Build();

        await message.Channel.SendMessageAsync(embed: embed, components: components);
    }
    catch (Exception ex)
    {
        Logger.Log($"[Warning] TodoCapture prompt failed: {ex.Message}");
    }
};

//------------------------------REACTION ADDED------------------------------
//
// handles two systems.
// 1. setup wizard flow, step 3 to step 4 emoji capture
// 2. live reaction role system, normal users triggering roles
//
// order matters: setup flow must run before normal role logic.

client.ReactionAdded += async (msgRef, channelRef, reaction) =>
{
    // ignore bot's own reactions, prevents infinite loops
    if (reaction.UserId == client.CurrentUser.Id) return;

    //---------------resolve channel and message context---------------

    IMessageChannel? resolvedChannel = await channelRef.GetOrDownloadAsync();
    if (resolvedChannel is not IGuildChannel guildChannel)
    {
        Logger.Log("[Warning] ReactionAdded: reaction was not in a guild channel");
        return;
    }

    IUserMessage msg = await msgRef.GetOrDownloadAsync();

    var guild = (guildChannel as SocketGuildChannel)?.Guild;
    if (guild == null) return;

    //---------------todo capture reaction handling---------------
    if (todoCaptureData.IsTracked(msg.Id))
    {
        // use emoji Name property to compare unicode emojis reliably
        var capEmoji = reaction.Emote;
        if (capEmoji != null && capEmoji.Name == "\u2705")
        {
            // strike through the todo embed/message and remove reactions
            try
            {
                if (msg.Embeds != null && msg.Embeds.Count > 0)
                {
                    var oldEmbed = msg.Embeds.First();
                    string desc = oldEmbed.Description ?? "";
                    string struck = desc.StartsWith("~~") ? desc : $"~~{desc}~~";
                    if (struck.Length > 4096) struck = struck.Substring(0, 4093) + "...";

                    var builder = new EmbedBuilder()
                        .WithTitle(oldEmbed.Title ?? "Todo")
                        .WithDescription(struck)
                        .WithColor(oldEmbed.Color ?? new Color(0x5865F2))
                        .WithFooter(oldEmbed.Footer?.Text ?? "System Active • 4/30/03, 3:00 AM");

                    if (oldEmbed.Author.HasValue)
                        builder.WithAuthor(oldEmbed.Author.Value.Name, oldEmbed.Author.Value.IconUrl, oldEmbed.Author.Value.Url);

                    await msg.ModifyAsync(m => { m.Embed = builder.Build(); m.Content = ""; });
                }
                else
                {
                    string content = msg.Content ?? "";
                    string struck = content.StartsWith("~~") ? content : $"~~{content}~~";
                    if (struck.Length > 2000) struck = struck.Substring(0, 1997) + "...";
                    await msg.ModifyAsync(m => m.Content = struck);
                }

                await msg.RemoveAllReactionsAsync();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] TodoCapture strike failed: {ex.Message}");
            }

            todoCaptureData.Untrack(msg.Id);
            return;
        }
        else if (capEmoji != null && capEmoji.Name == "\u274C")
        {
            // delete the message
            try { await msg.DeleteAsync(); } catch { }
            todoCaptureData.Untrack(msg.Id);
            return;
        }
        // ignore other reactions on tracked messages
        return;
    }

    //-----------------setup flow bridge, step 3 to step 4-----------------
    // capture emoji directly from real discord reaction.
    // exit early, do not run normal role logic.

    if (ReactionRolesModule.Sessions.TryGetValue(reaction.UserId, out var session))
    {
        if (session.WaitingForEmoji && session.MessageId == msg.Id)
        {
            // capture exact emoji, unicode or custom
            session.Emoji = reaction.Emote?.ToString();
            session.WaitingForEmoji = false;

            Logger.Log($"[Setup] Captured emoji: {session.Emoji} for user {reaction.UserId}");

            // clean up user's setup reaction for cleaner ux
            try
            {
                var guildUser = guild.GetUser(reaction.UserId);
                if (guildUser != null)
                    await msg.RemoveReactionAsync(reaction.Emote, guildUser);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] Failed to remove setup reaction: {ex.Message}");
            }

            //-------------continue wizard to step 4, role selection ui-------------

            var roles = guild.Roles
                .Where(r => r.Id != guild.Id && !r.IsManaged)
                .OrderByDescending(r => r.Position)
                .Take(25)
                .Select(r => new SelectMenuOptionBuilder()
                    .WithLabel(r.Name)
                    .WithValue(r.Id.ToString()))
                .ToList();

            var components = new ComponentBuilder()
                .WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId("rr_roles_add")
                    .WithPlaceholder("Roles to ADD when reacted")
                    .WithOptions(roles)
                    .WithMinValues(0)
                    .WithMaxValues(roles.Count))
                .WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId("rr_roles_remove")
                    .WithPlaceholder("Roles to REMOVE when reacted")
                    .WithOptions(roles)
                    .WithMinValues(0)
                    .WithMaxValues(roles.Count))
                .WithButton("Confirm & Save", "rr_confirm", ButtonStyle.Success)
                .Build();

            await msg.Channel.SendMessageAsync(
                $"✅ Emoji captured: {session.Emoji}\n**Step 4:** Choose roles and confirm.",
                components: components
            );

            return; // stop here so normal role logic does not run
        }
    }

    //---------------normal reaction role logic, production users---------------

    string? emoji = reaction.Emote?.ToString();
    if (string.IsNullOrWhiteSpace(emoji))
        return;

    ReactionEntry? entry = reactionsData.GetEntry(msg.Id, emoji);
    if (entry == null) return;

    SocketGuildUser? member = guild.GetUser(reaction.UserId);
    if (member == null)
    {
        Logger.Log($"[Warning] ReactionAdded: could not resolve member {reaction.UserId}");
        return;
    }

    Logger.Log($"[Info] Reaction role triggered by {member.Username} on message {msg.Id} with {reaction.Emote}");

    //----------role handling, toggle behavior----------
    // if member has all roles-to-add, remove them, toggle off
    // otherwise add them, toggle on.

    if (entry.RolesToAdd.Count > 0)
    {
        bool alreadyHasAll = entry.RolesToAdd.All(id => member.Roles.Any(r => r.Id == id));

        if (alreadyHasAll)
        {
            await member.RemoveRolesAsync(entry.RolesToAdd);
            Logger.Log($"[Info] Toggled OFF — removed {entry.RolesToAdd.Count} role(s) from {member.Username}");
        }
        else
        {
            await member.AddRolesAsync(entry.RolesToAdd);
            Logger.Log($"[Info] Toggled ON — added {entry.RolesToAdd.Count} role(s) to {member.Username}");
        }
    }

    if (entry.RolesToRemove.Count > 0)
    {
        await member.RemoveRolesAsync(entry.RolesToRemove);
        Logger.Log($"[Info] Removed {entry.RolesToRemove.Count} role(s) from {member.Username}");
    }

    //----------ux cleanup: remove user's reaction after processing----------
    // keeps only bot reaction visible, button-like behavior.

    try
    {
        await msg.RemoveReactionAsync(reaction.Emote, member);
    }
    catch (Exception ex)
    {
        Logger.Log($"[Warning] Failed to remove reaction: {ex.Message}");
    }
};

//--------------------------------LOGIN AND RUN--------------------------------
// verifies token and blocks forever.

Logger.Log("[Info] Logging in...");
await client.LoginAsync(TokenType.Bot, Config.BotToken);
await client.StartAsync();
Logger.Log("[Info] Bot started — waiting for events");
await Task.Delay(Timeout.Infinite);