// using Discord;
// using Discord.Interactions;
// using Discord.WebSocket;
//
// namespace DiscordBot.Modules;
//
// public class UserManagement : InteractionModuleBase<SocketInteractionContext>
// {
//     // ==========================================================
//     // Session Storage
//     // ==========================================================
//
//     private static readonly Dictionary<ulong, UserManagementSession> Sessions = new();
//
//     private UserManagementSession Session
//     {
//         get
//         {
//             if (!Sessions.TryGetValue(Context.User.Id, out var session))
//             {
//                 session = new UserManagementSession(Context.User.Id);
//                 Sessions[Context.User.Id] = session;
//             }
//
//             return session;
//         }
//     }
//
//     // ==========================================================
//     // Slash Command
//     // ==========================================================
//
//     [SlashCommand("user", "Open the user management menu")]
//     [RequireRole("🔧 Processes")]
//     public async Task UserMenu()
//     {
//         Session.Reset();
//
//         await RespondAsync(
//             embed: BuildEmbed(),
//             components: BuildComponents(),
//             ephemeral: true
//         );
//     }
//
//     // ==========================================================
//     // User Selection
//     // ==========================================================
//
//     [ComponentInteraction("um:users")]
//     public async Task UsersSelected(string[] users)
//     {
//         foreach (var id in users)
//             Session.SelectedUsers.Add(ulong.Parse(id));
//
//         Session.Page = UserManagementPage.Main;
//
//         await Refresh();
//     }
//
//     // ==========================================================
//     // Navigation
//     // ==========================================================
//
//     [ComponentInteraction("um:warn")]
//     public async Task Warn()
//     {
//         Session.Page = UserManagementPage.Warn;
//         await Refresh();
//     }
//
//     [ComponentInteraction("um:ban")]
//     public async Task Ban()
//     {
//         Session.Page = UserManagementPage.Ban;
//         await Refresh();
//     }
//
//     [ComponentInteraction("um:roles")]
//     public async Task Roles()
//     {
//         Session.Page = UserManagementPage.Roles;
//         await Refresh();
//     }
//
//     [ComponentInteraction("um:back")]
//     public async Task Back()
//     {
//         Session.Page = UserManagementPage.Main;
//         await Refresh();
//     }
//
//     // ==========================================================
//     // Rendering
//     // ==========================================================
//
//     private async Task Refresh()
//     {
//         await ModifyOriginalResponseAsync(msg =>
//         {
//             msg.Embed = BuildEmbed();
//             msg.Components = BuildComponents();
//         });
//     }
//
//     private Embed BuildEmbed()
//     {
//         var embed = new EmbedBuilder()
//             .WithColor(Color.Blue);
//
//         switch (Session.Page)
//         {
//             case UserManagementPage.SelectUsers:
//                 embed
//                     .WithTitle("User Management")
//                     .WithDescription(
//                         "Select one or more users to begin."
//                     );
//                 break;
//
//             case UserManagementPage.Main:
//                 embed
//                     .WithTitle("User Management")
//                     .WithDescription(BuildUserList());
//                 break;
//
//             case UserManagementPage.Warn:
//                 embed
//                     .WithTitle("Warn Users")
//                     .WithDescription(BuildUserList());
//                 break;
//
//             case UserManagementPage.Ban:
//                 embed
//                     .WithTitle("Ban Users")
//                     .WithDescription(BuildUserList());
//                 break;
//
//             case UserManagementPage.Roles:
//                 embed
//                     .WithTitle("Manage Roles")
//                     .WithDescription(BuildUserList());
//                 break;
//         }
//
//         return embed.Build();
//     }
//
//     private MessageComponent BuildComponents()
//     {
//         var builder = new ComponentBuilder();
//
//         switch (Session.Page)
//         {
//             case UserManagementPage.SelectUsers:
//
//                 builder.WithUserSelectMenu(
//                     "um:users",
//                     "Select users...",
//                     minValues: 1,
//                     maxValues: 25);
//
//                 break;
//
//             case UserManagementPage.Main:
//
//                 builder
//                     .WithButton("❗ Warn", "um:warn", ButtonStyle.Primary)
//                     .WithButton("🗑 Ban", "um:ban", ButtonStyle.Danger)
//                     .WithButton("⚙ Roles", "um:roles");
//
//                 break;
//
//             case UserManagementPage.Warn:
//
//                 builder
//                     .WithButton("Back", "um:back")
//                     .WithButton("Confirm", "um:confirm_warn",
//                         ButtonStyle.Danger);
//
//                 break;
//
//             case UserManagementPage.Ban:
//
//                 builder
//                     .WithButton("Back", "um:back")
//                     .WithButton("Confirm", "um:confirm_ban",
//                         ButtonStyle.Danger);
//
//                 break;
//
//             case UserManagementPage.Roles:
//
//                 builder
//                     .WithButton("➕ Add", "um:add_roles")
//                     .WithButton("➖ Remove", "um:remove_roles")
//                     .WithButton("Back", "um:back");
//
//                 break;
//         }
//
//         return builder.Build();
//     }
//
//     // ==========================================================
//     // Helpers
//     // ==========================================================
//
//     private string BuildUserList()
//     {
//         if (Session.SelectedUsers.Count == 0)
//             return "No users selected.";
//
//         return string.Join(
//             "\n",
//             Session.SelectedUsers.Select(x => $"• <@{x}>")
//         );
//     }
//
//     // ==========================================================
//     // Models
//     // ==========================================================
//
//     private enum UserManagementPage
//     {
//         SelectUsers,
//         Main,
//         Warn,
//         Ban,
//         Roles
//     }
//
//     private sealed class UserManagementSession
//     {
//         public ulong ModeratorId { get; }
//
//         public HashSet<ulong> SelectedUsers { get; } = new();
//
//         public UserManagementPage Page { get; set; }
//
//         public string? Reason { get; set; }
//
//         public UserManagementSession(ulong moderatorId)
//         {
//             ModeratorId = moderatorId;
//             Reset();
//         }
//
//         public void Reset()
//         {
//             SelectedUsers.Clear();
//             Reason = null;
//             Page = UserManagementPage.SelectUsers;
//         }
//     }
// }