# DiscordBot - AETHER-OS

A feature-rich Discord bot built in C# for the channel **F0XTA1L**. Built with [Discord.Net](https://discordnet.dev/) and [TwitchLib](https://github.com/TwitchLib), this bot manages community interaction, Twitch live notifications, stream scheduling, moderation logging, collaboration requests, and an in-character alternate reality game - all themed around the "AETHER-OS" lore.

---

## Table of Contents

- [Features](#features)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Running](#running)
- [Slash Commands](#slash-commands)
- [Module Overview](#module-overview)
- [Project Structure](#project-structure)
- [Configuration](#configuration)
- [Architecture Notes](#architecture-notes)

---

## Features

- **Live Notifications** - Automatic go-live, offline, and mid-stream update embeds via Twitch EventSub websocket. Polls viewer count every 60s, updates thumbnails, and appends VOD links after stream ends.
- **Favourite Streamer Alerts** - Watches a curated list of followed streamers and posts go-live notifications with custom messages to a dedicated channel.
- **Channel Point Redemptions** - Routes Twitch channel point rewards to Discord: viewer suggestions with an admin "Mark Complete" button, and quote submissions that save to a shared file for OBS overlay integration.
- **Stream Schedule** - Manage your weekly stream schedule from Discord with add/remove/view/publish. Entries are automatically pushed to your Twitch channel schedule. Auto-resets weekly.
- **Reaction Roles** - Interactive multi-step wizard for setting up toggle-based reaction roles on any message. Supports multiple roles per emoji with add/remove behavior. Uses a real Discord reaction capture step.
- **Live Guest Role** - Quickly grant or revoke a "live guest" voice channel role for stream collaborators, managed via the `/user` moderation menu. Automatically removed when a user leaves any voice channel.
- **Collaboration Requests** - Create multi-person stream/event proposals with DM invitations, accept/decline flow, decline reasons, and live status tracking. Hosts and participants receive persistent DM updates.
- **Moderation Logging** - Comprehensive audit logging: message edits/deletes (with before/after, attachment tracking, and moderator attribution), member joins/leaves/kicks/bans, nickname/role changes, and voice session summaries with duration tracking.
- **7TV Emote Integration** - Search and send 7TV emotes directly in Discord with autocomplete. Per-user preferences for channel, emote set, and image size (1x–4x). Supports animated and static formats.
- **AETHER-OS Terminal** - An in-character ARG terminal interface with a virtual filesystem, file reading, directory navigation, and a "coherence" mechanic. Corrupted files become readable as coherence increases. All users share a single terminal session. Includes a **Network Handshake** gambling system where users send Glossels into unknown network nodes for a chance to win multiplied rewards, drain the shared network cache, or lose packets to hostile nodes.
- **Twitch-Discord Account Linking** - A persistent embed with a "Link Twitch" button lets users link their accounts. Clicking generates a code, the user types it in Twitch chat within 20 seconds, and the bot matches and writes the link to the shared user data file.

---

## Prerequisites

| Dependency | Purpose | Notes |
|---|---|---|
| **.NET 10.0 SDK** | Runtime and build tooling | `dotnet build` / `dotnet run` |
| **Discord Bot Token** | Bot authentication | Requires intents: `Guilds`, `GuildMessages`, `GuildMessageReactions`, `GuildMembers`, `MessageContent`, `DirectMessages`, `GuildVoiceStates` |
| **Twitch Application** | EventSub, schedule sync, redemptions | Requires Client ID and Client Secret |
| **Newtonsoft.Json** | JSON serialization for data files | Bundled via `DiscordBot.csproj` |
| **DotNetEnv** | Loads `.env` files at startup | Bundled via `DiscordBot.csproj` |

---

## Setup

1. **Clone the repository:**
   ```bash
   git clone <repo-url>
   cd DiscordBot
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Configure environment variables:**
   
   Copy the example env file and fill in your values:
   ```bash
   cp .env.example .env
   ```
   
   Edit `.env` and set the following:
   - `DISCORD_BOT_TOKEN` - Your Discord bot token
   - `DISCORD_GUILD_ID` - Your Discord server ID
    - `TWITCH_CLIENT_ID` / `TWITCH_CLIENT_SECRET` - Twitch API credentials
    - `TWITCH_CHANNEL_NAME` / `TWITCH_USER_ID` - Your Twitch channel name and numeric ID
    - `TWITCH_BOT_NAME` - Your Twitch bot account username (used for IRC chat connection)
   - `FOX_DISCORD_ID` - Your Discord user ID (used for collab view filtering)
   
   Channel and role IDs (all numeric):
   - `TWITCH_NOTIFY_CHANNEL_ID` - Where go-live notifications post
   - `SUGGESTION_CHANNEL_ID` - Where Twitch suggestion redemptions appear
   - `WELCOME_CHANNEL_ID` - Where welcome messages are sent
   - `FAVOURITES_NOTIFY_CHANNEL_ID` - Where favourite streamer alerts post
   - `MOD_LOG_CHANNEL_ID` - Where moderation logs are posted
   - `AUTO_ROLE_ID` - Role assigned to new members on join
   - `TWITCH_NOTIFY_ROLE_ID` - Role pinged on go-live notifications
   - `SCHEDULE_NOTI_ROLE_ID` - Role pinged when schedule is published
    - `LIVE_GUEST_ROLE_ID` - Voice channel guest role managed by `/user`
   
   Twitch reward IDs:
   - `SUGGEST_REWARD_ID` - Channel point reward for suggestions
   - `QUOTE_REWARD_ID` - Channel point reward for quotes

4. **Build and run:**
   ```bash
   dotnet build
   dotnet run
   ```
   
   On first run, the bot starts a local HTTP server on port 17563 and prints an authorization URL. Open it in your browser, authorize the app, and Twitch redirects to `localhost:17563` to complete the flow. Tokens are saved to `Data/twitch_tokens.json` and auto-refreshed thereafter.
   
   For headless servers, use SSH port forwarding before starting the bot:
   ```bash
   ssh -L 17563:localhost:17563 your-server
   ```

---

## Running

```bash
dotnet run
```

The bot will:

1. Construct the `DiscordSocketClient` with all required gateway intents and initialize the logger.
2. Build the DI container, registering all singletons (Discord services, data stores, Twitch services, ARG services, moderation logger).
3. Check for valid Twitch tokens. If a refresh token exists but the access token is expired, silently refresh. If no tokens exist at all, print an authorization URL and start a local callback server on port 17563 to complete the OAuth flow.
4. Log in to Discord and start the client.
5. On the `Ready` event:
   - Register all slash command modules from the assembly.
   - Register commands to the configured guild (instant propagation). For production, switch to `RegisterCommandsGloballyAsync()` (up to 1 hour propagation).
   - Reset the AETHER-OS terminal session.
   - Initialize Twitch services: `TwitchRedeemHandler`, `FavouritesLiveNoti`, `EventSubReconnectService`, `TwitchChatService` (IRC connection for account linking), and start the `Twitch_Notifier` (connects EventSub websocket, subscribes to `stream.online`, `stream.offline`, and `channel.update`).
6. Begin listening for Discord events (interactions, reactions, joins) and Twitch EventSub events.
7. The bot blocks indefinitely with `Task.Delay(Timeout.Infinite)`.

---

## Slash Commands

### Stream Management (Requires "🔧 Processes" role)

| Command | Description |
|---|---|
| `/streamschedule` | Open the schedule menu: Add, Remove, View, or Publish the weekly stream schedule |
| `/resetschedule` | Force-clear the published schedule and all entries |
| `/reactionrole` | Open the reaction role wizard: Add or Remove reaction roles on any message |
| `/user` | Open the user management menu: warn, ban, kick, manage roles, or assign live guest role (testing) |
| `/collab` | Start a collaboration request: pick date, fill modal, invite collaborators, confirm and send DMs (requires "Proxy Hosts" role) |
| `/postlink` | Post the account linking embed with a "Link Twitch" button to the current channel |

### AETHER-OS Terminal

| Command | Description |
|---|---|
| `/system login` | Log in to the AETHER-OS terminal. Posts four persistent embeds: logs, terminal, file viewer, and network handshake |
| `/system logout` | End your terminal session |

### 7TV Emotes

| Command | Description |
|---|---|
| `/7tv emote <name>` | Search for and send a 7TV emote image (autocomplete-enabled) |
| `/7tv channel` | Select a 7TV channel (Twitch/YouTube) as your emote source |
| `/7tv image-size` | Set preferred emote image size: 1x, 2x, 3x, or 4x |
| `/7tv emote-set` | Select a specific emote set from your chosen channel |
| `/7tv reset` | Clear all 7TV preferences, reverting to defaults |

---

## Module Overview

### Reaction Roles (`Modules/Commands/ReactionRoles.cs`)
Multi-step interactive wizard. The add flow: select channel → select message (last 5 shown) → capture emoji via real Discord reaction → choose roles to add/remove → confirm. The remove flow: multi-select from configured entries → confirm deletion. Uses an in-memory session dictionary keyed by user ID. Data persists to `Data/reaction_roles.json`.

### Stream Schedule (`Modules/Commands/Schedule.cs` + `Modules/Data/ScheduleData.cs` + `Services/TwitchScheduleService.cs`)
Weekly schedule manager with four actions: Add (day picker → modal with title, time, optional game name that resolves a Twitch category ID), Remove (multi-select), View (ephemeral embed), and Publish (posts styled embed with role mention, or refreshes existing message). Auto-resets weekly via a timer (Sunday 23:30). Entries are synced to the Twitch channel schedule API.

### Collaboration System (`Modules/Collabs/` + `Services/CollabService.cs` + `Services/CollabRequestCache.cs`)
Full collaboration request pipeline: date picker → modal (title, time, game, external collaborators) → user-select menu → confirmation embed. On confirm, DMs are sent to all participants with Accept/Decline buttons. Responses update the DM embed in-place. Data persists to `Data/collabs.json`. An in-memory `CollabRequestCache` holds pending requests between modal submission and confirmation.

### Twitch Live Notifications (`Modules/Twitch_Automation/TwitchNotifier.cs`)
EventSub websocket handler for `stream.online`, `stream.offline`, and `channel.update`. On live: fetches stream info and avatar, posts a rich embed with role ping, starts a 60s polling loop that updates viewer count and thumbnail. On offline: updates embed with duration and VOD link. Thread-safe via `SemaphoreSlim`.

### Favourite Streamer Alerts (`Modules/Twitch_Automation/FavouritesLiveNoti.cs`)
Watches 9 hardcoded streamers via EventSub `stream.online` subscriptions. Posts themed go-live embeds with custom notification messages to a dedicated channel.

### Twitch Redemption Handler (`Modules/Twitch_Automation/TwitchRedeemHandler.cs` + `Redeems/`)
Routes fulfilled channel point redemptions by reward ID: suggestions post an embed with a "Mark Complete" button, quotes parse input (`Quote text - Source, Year`) and append to a shared JSON file for OBS overlay integration.

### 7TV Integration (`Modules/SevenTvIntegration/`)
Per-user emote preferences stored in `Data/sevenTvPreferences.json`. Emote search uses 7TV's GraphQL v4 API with autocomplete caching (5 min TTL, 1000 entry cap). Supports animated (avif) and static (png) formats. Default channel/set auto-resolves from the configured Twitch channel.

### AETHER-OS Terminal (`Modules/ARG/` + `Services/ArgTerminalService.cs` + `Services/CoherenceWatcher.cs`)
In-character terminal with four persistent embeds: action log, terminal view (directory listing + buttons for Navigate, Read, Ping, Handshake), file viewer, and network handshake results. A virtual filesystem with a coherence percentage (0–100) gates file access - corrupted files show garbled content below 60% coherence. The Ping button increases coherence by 2%. The Handshake button initiates a weighted gambling system (ported from the TwitchBot) where users send Glossels into unknown network nodes. All users share a single session. A `CoherenceWatcher` monitors the state file on disk for external changes (e.g. from an overlay or external tool).

### Network Handshake (`Modules/ARG/HandshakeModule.cs` + `Services/HandshakeService.cs`)
Gambling system shared with the TwitchBot via `../../TwitchBot/data/user_data.json` and `network_cache.json`. The Handshake button on the terminal embed opens an ephemeral choice screen with "Unknown Network" and "Other Connection". Unknown Network presents a modal where the user enters an amount of Glossels to gamble. Six weighted outcomes: accepted (35%, 2x return), unstable (30%, no change), rejected (25%, loss to cache), amplified (8%, 3x return), captured (2%, half lost to cache), drained (1%, drains entire network cache). Other Connection presents a native user picker of your guild; selecting a user who is linked to Twitch opens an amount modal to transfer Glossels, while unlinked users get an error explaining they're not linked yet. Results update the persistent Handshake embed and log to the action history. Requires a linked Twitch-Discord account with a positive Glossel balance. `LinkedAccountsData` re-reads `user_data.json` from disk before every balance lookup and mutating write, and also watches the data directory for the atomic rename the TwitchBot uses, so Glossel balances stay in sync with the TwitchBot without a restart.

### Moderation Logging (`Modules/Moderation/ModerationLogs.cs`)
Auto-logs audit events to a Discord channel. Message logs include before/after content, attachment changes, and reply context. Member logs track joins (with account age), leaves, kicks (via audit log), bans/unbans, nickname/role changes. Reason fields are only shown when a reason was actually supplied, across all log types. Voice logs track sessions: when a channel empties, posts a summary with total duration and all participants. Each category independently toggleable via `Config` flags.

### User Management (`Modules/Moderation/UserManagement.cs`) - Testing
`/user` slash command (requires "🔧 Processes" role). Opens an ephemeral menu with a user select picker, then offers: Warn (DMs the user), Ban, Kick, role management (add/remove any server role), and live guest role management. Includes a reason modal for moderation actions: set reasons appear in the target's DM and in audit-log entries, and the Reason field is omitted from logs when left empty. Automatically removes the live guest role when a user leaves any voice channel. This module is experimental and may have issues.

### Token Management (`Services/TokenManager.cs`)
Manages Twitch OAuth2 tokens (Bot and Broadcaster profiles) with automatic refresh 5 minutes before expiry. On startup, if a refresh token exists but the access token is expired, it silently refreshes without user interaction. On first run (no tokens at all), starts a local HTTP callback server on port 17563 and prints an authorization URL for the user to complete the OAuth flow. Persists tokens to `Data/twitch_tokens.json`. Thread-safe via `SemaphoreSlim`. A retry wrapper detects 401 responses, refreshes, and retries once.

### Twitch-Discord Account Linking (`Modules/Linking/` + `Services/TwitchChatService.cs` + `Services/LinkedAccountsData.cs`)
A persistent embed posted via `/postlink` with a "Link Twitch" button. Clicking the button (ephemeral) generates a 5-character code valid for 20 seconds. The user types the code in Twitch chat, the bot matches it, deletes the Twitch message, looks up the user in the shared data file (`../../TwitchBot/data/user_data.json`), and writes their Discord user ID into the entry. Sends a DM on success, updates the ephemeral message and DM on expiry. Requires a configured Twitch bot account for IRC connection. `LinkedAccountsData` resolves the path relative to `AppContext.BaseDirectory`.

---

## Project Structure

```
DiscordBot/
├── Config.cs                       # Env var loader (gitignored, see .env.example)
├── Program.cs                      # Entrypoint - DI container, event handlers, command registration
├── DiscordBot.csproj               # .NET 10.0 project file with package references
├── .env.example                    # Environment variable template
├── Modules/
│   ├── Commands/
│   │   ├── ReactionRoles.cs        # /reactionrole wizard (add/remove)
│   │   └── Schedule.cs             # /streamschedule and /resetschedule
│   ├── Moderation/
│   │   ├── ModerationLogs.cs       # Audit event handler (messages, members, voice)
│   │   └── UserManagement.cs       # /user moderation menu (testing)
│   ├── Collabs/
│   │   ├── CollabModule.cs         # /collab slash command and UI flow
│   │   ├── CollabData.cs           # JSON-backed collab persistence
│   │   ├── CollabEntry.cs          # Data model for a collaboration
│   │   ├── CollabParticipant.cs    # Data model for a participant
│   │   ├── CollabDmReference.cs    # DM message tracking for owner/participants
│   │   ├── CollabRequestModal.cs   # Modal for collab details
│   │   └── DeclineModal.cs         # Modal for decline reason
│   ├── Twitch_Automation/
│   │   ├── Twitch_Notifier.cs      # EventSub live/offline/update handler
│   │   ├── FavouritesLiveNoti.cs   # Favourite streamer go-live alerts
│   │   ├── TwitchRedeemHandler.cs  # Channel point redemption router
│   │   └── SuggestionModule.cs     # "Mark Complete" button handler
│   ├── Linking/
│   │   └── LinkModule.cs           # /postlink and Link Twitch button handler
│   ├── SevenTvIntegration/
│   │   ├── SevenTvModule.cs        # /7tv command group
│   │   ├── SevenTvApi.cs           # 7TV GraphQL API client
│   │   ├── SevenTvAutocompleteHandlers.cs  # Autocomplete providers
│   │   ├── SevenTvPreferencesStore.cs      # Per-user preference persistence
│   │   ├── SevenTvTypes.cs         # API response types
│   │   └── SevenTvDefaults.cs      # Default channel/set resolution
│   ├── ARG/
│   │   ├── ARG.cs                  # /system login/logout and terminal UI
│   │   └── HandshakeModule.cs      # Handshake button, Unknown Network choice, gamble modal
│   ├── Data/
│   │   ├── ReactionsData.cs        # Reaction role data model and persistence
│   │   ├── ScheduleData.cs         # Schedule data model with weekly reset timer
│   │   ├── ARGTerminalData.cs      # ARG state persistence
│   │   └── ARG_Helper.cs           # Virtual filesystem tree
├── Services/
│   ├── Logger.cs                   # Static logger (console + bot-log.txt + optional DM)
│   ├── TokenManager.cs             # Twitch OAuth2 token management with auto-refresh
│   ├── TwitchApiService.cs         # TwitchAPI wrapper with token pre-flight
│   ├── TwitchChatService.cs        # TwitchLib.Client IRC connection for account linking
│   ├── TwitchScheduleService.cs    # Twitch schedule segment CRUD
│   ├── EventSubReconnectService.cs # EventSub websocket reconnect with backoff
│   ├── CollabService.cs            # DM delivery and status updates for collabs
│   ├── CollabRequestCache.cs       # In-memory pending collab request cache
│   ├── LinkedAccountsData.cs       # User data read/write for account linking
│   ├── LiveGuestService.cs         # Auto-remove live guest role on voice leave
│   ├── ArgTerminalService.cs       # Terminal embed builder and renderer
│   ├── HandshakeService.cs         # Network handshake gambling logic and cache
│   └── CoherenceWatcher.cs         # Filesystem watcher for external ARG state changes
├── Redeems/
│   ├── RedemptionContext.cs        # Context record for redemption handlers
│   ├── SuggestionRedeem.cs         # Suggestion reward -> Discord embed
│   └── QuoteRedeem.cs             # Quote reward -> shared JSON file
└── Data/                           # (gitignored) Runtime state: reactions, schedule, tokens, etc.
```

---

## Configuration

All secrets and IDs are read from environment variables in `Config.cs` (static readonly fields). See `.env.example` for the full list. `Config.cs` is gitignored and not committed to the repository.

### Data Files (gitignored)

The `Data/` directory holds all runtime state and is fully gitignored. On first run, the bot creates these files automatically:

| File | Contents |
|---|---|
| `Data/reactions.json` | Reaction role configurations (message ID, channel, emoji, roles) |
| `Data/schedule.json` | Stream schedule entries, published message state, week tracking |
| `Data/collabs.json` | Collaboration requests with participant statuses |
| `Data/twitch_tokens.json` | Twitch OAuth access/refresh tokens (auto-refreshed at runtime) |
| `Data/sevenTvPreferences.json` | Per-user 7TV channel, emote set, and image size preferences |
| `Data/argData.json` | AETHER-OS terminal state (cwd, coherence, action history) |
| `bot-log.txt` | Timestamped log output (append mode) |

### External File

| File | Purpose |
|---|---|
| `../../Overlay/Scripts/quotes.json` | Quote redemptions appended here for OBS overlay integration (outside repo) |

---

## Architecture Notes

- **Top-level statements**: `Program.cs` uses C# 9+ top-level statements - no `Main()` class or wrapper. DI, event handlers, and command routing are all wired in one file.
- **Singleton DI**: All services are registered as singletons. Modules receive dependencies via constructor injection from `InteractionService`.
- **Guild-only command registration**: Commands are registered per-guild in `RegisterCommandsToGuildAsync()` for instant propagation during development. Switch to `RegisterCommandsGloballyAsync()` for production (up to 1 hour propagation).
- **Reaction handler ordering**: The `ReactionAdded` event handler in `Program.cs` runs the setup wizard flow before normal reaction role logic. The setup flow must exit early with `return` to prevent normal role logic from firing.
- **Single EventSub websocket**: The `Twitch_Notifier` service manages the EventSub websocket connection. Multiple services (`FavouritesLiveNoti`, `TwitchRedeemHandler`, `EventSubReconnectService`) share this connection.
- **Weekly schedule reset**: A `Timer` in `ScheduleData` fires on a configurable day/time to auto-clear the schedule. On startup, `EnsureCurrentWeek()` detects if the saved week is stale and clears it.
- **AETHER-OS shared state**: All Discord users share a single terminal session (one current directory, one open file, one action history). The `CoherenceWatcher` uses a `FileSystemWatcher` with debouncing to detect external state changes.
- **No build step**: Standard .NET CLI. `dotnet build` and `dotnet run`. No Makefile, no Docker, no CI pipeline.
