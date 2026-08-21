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


// ─────────────────────────────────────────────────────────────────────────────
// c# version 9+ does not use main class void structure, allows writing top level
// statements - essentially code that runs directly without a class wrapper
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Service provider — registers singletons for DI injection into modules
// ─────────────────────────────────────────────────────────────────────────────

ServiceProvider services = new ServiceCollection()
    
    //BASE DISCORD STUFF
    .AddSingleton(client)
    .AddSingleton(x => new InteractionService(
        x.GetRequiredService<DiscordSocketClient>()
    ))
    
    //DATA RELATED
    .AddSingleton<ReactionsData>()
    .AddSingleton<ScheduleData>()
    .AddSingleton<CollabData>()
    
    //TWITCH RELATED
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
    
    //TWITCH RECONNECT HANDLER
    .AddSingleton<EventSubReconnectService>()
    
    //ARG RELATED
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
    
    //Mod Logging
    .AddSingleton<ModerationLogs>()

    //Live Guest auto-remove
    .AddSingleton<LiveGuestService>()

    //Linking
    .AddSingleton<TwitchChatService>()
    .AddSingleton<LinkedAccountsData>()
    
    //Handshake gambling
    .AddSingleton<HandshakeService>()
    
    //LOGGER
    .AddLogging()
    
    //CONSTRUCTS SERVICE PROVIDER
    .BuildServiceProvider();

ModerationLogs modLogs = services.GetRequiredService<ModerationLogs>();
InteractionService interactions = services.GetRequiredService<InteractionService>();
ReactionsData reactionsData    = services.GetRequiredService<ReactionsData>();
CoherenceWatcher watcher = services.GetRequiredService<CoherenceWatcher>();
TokenManager tokenManager = services.GetRequiredService<TokenManager>();

// ─────────────────────────────────────────────────────────────────────────────
// Twitch token auth - refresh if expired, full OAuth if no refresh token
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Logging — routes both client and interaction service logs to the console
// => {} is a lambda meaning an anonymous function
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Ready — registers slash commands and logs startup info
// ─────────────────────────────────────────────────────────────────────────────

client.Ready += async () =>
{
    // //UNCOMMENT TO CLEAR OUT COMMANDS
    //
    // // Add this ONCE, run the bot, then remove it
    //      await client.Rest.DeleteAllGlobalCommandsAsync();
    //
    //  // Also clear guild-specific commands
    //      foreach (var guild in client.Guilds)
    //      {
    //          await guild.DeleteApplicationCommandsAsync();
    //          Logger.Log($"[Info] Cleared commands for {guild.Name}");
    //      }
    
    if (_ready) return; // prevent re-running on reconnect
    _ready = true;
    
    await interactions.AddModulesAsync(typeof(ReactionRolesModule).Assembly, services); //adds Schedule and ReactionROle because both derive from IInteractionModuleBase
    
    // // Use RegisterCommandsToGuildAsync(guildId) during development for instant updates
    // // Switch to RegisterCommandsGloballyAsync() for production (up to 1hr propagation)
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

// ─────────────────────────────────────────────────────────────────────────────
// Interaction routing — forwards all interactions to the interaction service
// ─────────────────────────────────────────────────────────────────────────────

client.InteractionCreated += async interaction =>
{
    SocketInteractionContext ctx = new SocketInteractionContext(client, interaction);
    
    // Log what's coming in to confirm routing
    Logger.Log($"[Debug] Interaction received: {interaction.Type} — {(interaction is SocketMessageComponent c ? c.Data.CustomId : "N/A")}");
    
    var result = await interactions.ExecuteCommandAsync(ctx, services); //routes to correct module based on called slash command

    if (!result.IsSuccess)
        Logger.Log($"[Warning] Interaction failed: {result.Error} — {result.ErrorReason}");
};

// ─────────────────────────────────────────────────────────────────────────────
// Welcome + auto role — fires when a new member joins the guild
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// ReactionAdded Event Handler
//
// Handles TWO systems:
// 1. Setup Wizard Flow (Step 3 → Step 4 emoji capture)
// 2. Live Reaction Role System (normal users triggering roles)
//
// IMPORTANT: Order matters — setup flow MUST run before normal role logic
// ─────────────────────────────────────────────────────────────────────────────

client.ReactionAdded += async (msgRef, channelRef, reaction) =>
{
    // Ignore bot's own reactions (prevents infinite loops / self-triggers)
    if (reaction.UserId == client.CurrentUser.Id) return;

    // ─────────────────────────────────────────────────────────────
    // Resolve channel + message context
    // ─────────────────────────────────────────────────────────────

    IMessageChannel? resolvedChannel = await channelRef.GetOrDownloadAsync();
    if (resolvedChannel is not IGuildChannel guildChannel)
    {
        Logger.Log("[Warning] ReactionAdded: reaction was not in a guild channel");
        return;
    }

    IUserMessage msg = await msgRef.GetOrDownloadAsync();

    var guild = (guildChannel as SocketGuildChannel)?.Guild;
    if (guild == null) return;

    // ─────────────────────────────────────────────────────────────
    // SETUP FLOW BRIDGE (STEP 3 → STEP 4)
    //
    // If user is currently configuring a reaction role:
    // - capture emoji directly from real Discord reaction
    // - exit early (do NOT run normal role logic)
    // ─────────────────────────────────────────────────────────────

    if (ReactionRolesModule.Sessions.TryGetValue(reaction.UserId, out var session))
    {
        if (session.WaitingForEmoji && session.MessageId == msg.Id)
        {
            // Capture exact emoji (unicode or custom)
            session.Emoji = reaction.Emote?.ToString();
            session.WaitingForEmoji = false;

            Logger.Log($"[Setup] Captured emoji: {session.Emoji} for user {reaction.UserId}");

            // Clean up user's setup reaction for cleaner UX
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

            // ─────────────────────────────────────────────────────────────
            // CONTINUE WIZARD → STEP 4 (ROLE SELECTION UI)
            // ─────────────────────────────────────────────────────────────

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

            return; // IMPORTANT: stop here so normal role logic does not run
        }
    }

    // ─────────────────────────────────────────────────────────────
    // NORMAL REACTION ROLE LOGIC (production users)
    // ─────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────
    // ROLE HANDLING — toggle behavior
    // If member already has ALL roles-to-add, remove them (toggle off)
    // Otherwise add them (toggle on)
    // ─────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────
    // UX CLEANUP: remove user's reaction after processing
    // (keeps only bot reaction visible → button-like behavior)
    // ─────────────────────────────────────────────────────────────

    try
    {
        await msg.RemoveReactionAsync(reaction.Emote, member);
    }
    catch (Exception ex)
    {
        Logger.Log($"[Warning] Failed to remove reaction: {ex.Message}");
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Login and run — verifies token and blocks forever
// ─────────────────────────────────────────────────────────────────────────────

Logger.Log("[Info] Logging in...");
await client.LoginAsync(TokenType.Bot, Config.BotToken);
await client.StartAsync();
Logger.Log("[Info] Bot started — waiting for events");
await Task.Delay(Timeout.Infinite);