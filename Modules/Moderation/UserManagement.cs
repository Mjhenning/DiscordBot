using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordBot.Modules.Moderation;

public class UserManagementModule : InteractionModuleBase<SocketInteractionContext>
{
     static readonly Dictionary<ulong, UmSession> Sessions = new();

     UmSession Session
    {
        get
        {
            if (!Sessions.TryGetValue(Context.User.Id, out var session))
            {
                session = new UmSession();
                Sessions[Context.User.Id] = session;
            }
            return session;
        }
    }

    // ==========================================================
    // Slash Command
    // ==========================================================

    [SlashCommand("user", "Open the user management menu")]
    [RequireRole("🔧 Processes")]
    public async Task UserMenu()
    {
        Session.Reset();
        Session.Page = UmPage.Main;

        await RespondAsync(
            embed: BuildEmbed(),
            components: BuildComponents(),
            ephemeral: true
        );
    }

    // ==========================================================
    // User Selection → routes to pending action
    // ==========================================================

    [ComponentInteraction("um:users")]
    public async Task UsersSelected(string[] users)
    {
        await DeferAsync(ephemeral: true);

        Session.SelectedUsers.Clear();
        foreach (var id in users)
            Session.SelectedUsers.Add(ulong.Parse(id));

        switch (Session.PendingAction)
        {
            case UmAction.Warn:
            case UmAction.Ban:
            case UmAction.Kick:
                Session.Page = Session.PendingAction switch
                {
                    UmAction.Warn => UmPage.Warn,
                    UmAction.Ban => UmPage.Ban,
                    UmAction.Kick => UmPage.Kick,
                    _ => UmPage.Main
                };
                break;

            case UmAction.AddRoles:
                await ShowAddRoles();
                return;

            case UmAction.RemoveRoles:
                await ShowRemoveRoles();
                return;

            default:
                Session.Page = UmPage.Main;
                break;
        }

        await Refresh();
    }

    // ==========================================================
    // Navigation from main menu
    // ==========================================================

    [ComponentInteraction("um:warn")]
    public async Task Warn()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Warn;
        Session.Page = UmPage.SelectUsers;
        await Refresh();
    }

    [ComponentInteraction("um:ban")]
    public async Task Ban()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Ban;
        Session.Page = UmPage.SelectUsers;
        await Refresh();
    }

    [ComponentInteraction("um:kick")]
    public async Task Kick()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Kick;
        Session.Page = UmPage.SelectUsers;
        await Refresh();
    }

    [ComponentInteraction("um:roles")]
    public async Task Roles()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Roles;
        await Refresh();
    }

    [ComponentInteraction("um:back")]
    public async Task Back()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    // ==========================================================
    // Confirm Actions
    // ==========================================================

    [ComponentInteraction("um:confirm_warn")]
    public async Task ConfirmWarn()
    {
        await DeferAsync(ephemeral: true);

        int success = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            try
            {
                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"**Warning from {Context.Guild.Name}**\n" +
                    $"You have been warned by a moderator." +
                    (string.IsNullOrWhiteSpace(Session.Reason) ? "" : $"\n**Reason:** {Session.Reason}")
                );
                success++;
                Logger.Log($"[UserMgmt] Warned {user.Username} by {Context.User.Username}: {Session.Reason}");
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Warned {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        LogModAction("Warn", Session.SelectedUsers, Session.Reason);
        await LogModChannel("Warn", Color.Orange, Session.SelectedUsers, Session.Reason);
        Session.Reset();
    }

    [ComponentInteraction("um:confirm_ban")]
    public async Task ConfirmBan()
    {
        await DeferAsync(ephemeral: true);

        int success = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            try
            {
                await user.BanAsync(
                    pruneDays: 0,
                    reason: $"Banned by {Context.User.Username}: {Session.Reason}"
                );
                success++;
                Logger.Log($"[UserMgmt] Banned {user.Username} by {Context.User.Username}: {Session.Reason}");
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Banned {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        LogModAction("Ban", Session.SelectedUsers, Session.Reason);
        await LogModChannel("Ban", Color.Red, Session.SelectedUsers, Session.Reason);
        Session.Reset();
    }

    [ComponentInteraction("um:confirm_kick")]
    public async Task ConfirmKick()
    {
        await DeferAsync(ephemeral: true);

        int success = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            try
            {
                await user.KickAsync(
                    reason: $"Kicked by {Context.User.Username}: {Session.Reason}"
                );
                success++;
                Logger.Log($"[UserMgmt] Kicked {user.Username} by {Context.User.Username}: {Session.Reason}");
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Kicked {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        LogModAction("Kick", Session.SelectedUsers, Session.Reason);
        await LogModChannel("Kick", Color.DarkOrange, Session.SelectedUsers, Session.Reason);
        Session.Reset();
    }

    // ==========================================================
    // Reason Modal
    // ==========================================================

    [ComponentInteraction("um:reason_warn", ignoreGroupNames: true)]
    public async Task OpenReasonModalWarn() => await OpenReasonModal();

    [ComponentInteraction("um:reason_ban", ignoreGroupNames: true)]
    public async Task OpenReasonModalBan() => await OpenReasonModal();

    [ComponentInteraction("um:reason_kick", ignoreGroupNames: true)]
    public async Task OpenReasonModalKick() => await OpenReasonModal();

     async Task OpenReasonModal()
    {
        await RespondWithModalAsync<UmReasonModal>("um:reason_submit");
    }

    [ModalInteraction("um:reason_submit")]
    public async Task ReasonSubmitted(UmReasonModal modal)
    {
        await DeferAsync(ephemeral: true);
        Session.Reason = modal.Reason;
        await Refresh();
    }

    // ==========================================================
    // Role Management - entry points
    // ==========================================================

    [ComponentInteraction("um:add_roles")]
    public async Task AddRolesEntry()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.AddRoles;
        Session.Page = UmPage.SelectUsers;
        await Refresh();
    }

    [ComponentInteraction("um:remove_roles")]
    public async Task RemoveRolesEntry()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.RemoveRoles;
        Session.Page = UmPage.SelectUsers;
        await Refresh();
    }

    // ==========================================================
    // Role Management - display
    // ==========================================================

     async Task ShowAddRoles()
    {
        IEnumerable<SocketRole> rolePool;

        if (Session.SelectedUsers.Count == 1)
        {
            var user = Context.Guild.GetUser(Session.SelectedUsers.First());
            var userRoleIds = user?.Roles.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();

            rolePool = Context.Guild.Roles
                .Where(r => r.Id != Context.Guild.Id && !r.IsManaged && !userRoleIds.Contains(r.Id));
        }
        else
        {
            rolePool = Context.Guild.Roles
                .Where(r => r.Id != Context.Guild.Id && !r.IsManaged);
        }

        var roles = rolePool
            .OrderByDescending(r => r.Position)
            .Take(25)
            .Select(r => new SelectMenuOptionBuilder()
                .WithLabel(r.Name)
                .WithValue(r.Id.ToString()))
            .ToList();

        var menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("um:pick_add_roles")
                .WithPlaceholder("Select roles to ADD")
                .WithOptions(roles)
                .WithMinValues(1)
                .WithMaxValues(Math.Max(1, roles.Count)))
            .WithButton("Back", "um:back_roles")
            .Build();

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = new EmbedBuilder()
                .WithTitle("Add Roles")
                .WithDescription($"Select roles to add to {BuildUserList()}")
                .WithColor(Color.Blue)
                .Build();
            msg.Components = menu;
        });
    }

    async Task ShowRemoveRoles()
    {
        IEnumerable<SocketRole> rolePool;

        if (Session.SelectedUsers.Count == 1)
        {
            var user = Context.Guild.GetUser(Session.SelectedUsers.First());
            var userRoleIds = user?.Roles.Select(r => r.Id).ToHashSet() ?? new HashSet<ulong>();

            rolePool = Context.Guild.Roles
                .Where(r => userRoleIds.Contains(r.Id) && !r.IsManaged);
        }
        else
        {
            rolePool = Context.Guild.Roles
                .Where(r => r.Id != Context.Guild.Id && !r.IsManaged);
        }

        var roles = rolePool
            .OrderByDescending(r => r.Position)
            .Take(25)
            .Select(r => new SelectMenuOptionBuilder()
                .WithLabel(r.Name)
                .WithValue(r.Id.ToString()))
            .ToList();

        var menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("um:pick_remove_roles")
                .WithPlaceholder("Select roles to REMOVE")
                .WithOptions(roles)
                .WithMinValues(1)
                .WithMaxValues(Math.Max(1, roles.Count)))
            .WithButton("Back", "um:back_roles")
            .Build();

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = new EmbedBuilder()
                .WithTitle("Remove Roles")
                .WithDescription($"Select roles to remove from {BuildUserList()}")
                .WithColor(Color.Blue)
                .Build();
            msg.Components = menu;
        });
    }

    // ==========================================================
    // Role Management - apply
    // ==========================================================

    [ComponentInteraction("um:pick_add_roles")]
    public async Task PickAddRoles(string[] roleIds)
    {
        await DeferAsync(ephemeral: true);

        int success = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            try
            {
                var roles = roleIds.Select(id => Context.Guild.GetRole(ulong.Parse(id)))
                    .Where(r => r != null).ToList();
                await user.AddRolesAsync(roles,
                    new RequestOptions { AuditLogReason = $"Roles added by {Context.User.Username}" });
                success++;
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Added roles to {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        await LogModChannel("Role add", Color.Green, Session.SelectedUsers, null);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    [ComponentInteraction("um:pick_remove_roles")]
    public async Task PickRemoveRoles(string[] roleIds)
    {
        await DeferAsync(ephemeral: true);

        int success = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            try
            {
                var roles = roleIds.Select(id => Context.Guild.GetRole(ulong.Parse(id)))
                    .Where(r => r != null).ToList();
                await user.RemoveRolesAsync(roles,
                    new RequestOptions { AuditLogReason = $"Roles removed by {Context.User.Username}" });
                success++;
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Removed roles from {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        await LogModChannel("Role remove", Color.Red, Session.SelectedUsers, null);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    [ComponentInteraction("um:back_roles")]
    public async Task BackFromRoles()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Roles;
        await Refresh();
    }

    // ==========================================================
    // Rendering
    // ==========================================================

     async Task Refresh()
    {
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = BuildEmbed();
            msg.Components = BuildComponents();
        });
    }

     Embed BuildEmbed()
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Blue);

        switch (Session.Page)
        {
            case UmPage.Main:
                embed
                    .WithTitle("User Management")
                    .WithDescription("Pick an action, then select the target user(s).");
                break;

            case UmPage.SelectUsers:
                embed
                    .WithTitle("User Management")
                    .WithDescription($"**{Session.PendingAction}** - Select target user(s).");
                break;

            case UmPage.Warn:
                embed
                    .WithTitle("Warn Users")
                    .WithDescription(BuildUserList() +
                        (string.IsNullOrWhiteSpace(Session.Reason) ? "" : $"\n**Reason:** {Session.Reason}"));
                break;

            case UmPage.Ban:
                embed
                    .WithTitle("Ban Users")
                    .WithDescription(BuildUserList() +
                        (string.IsNullOrWhiteSpace(Session.Reason) ? "" : $"\n**Reason:** {Session.Reason}"));
                break;

            case UmPage.Kick:
                embed
                    .WithTitle("Kick Users")
                    .WithDescription(BuildUserList() +
                        (string.IsNullOrWhiteSpace(Session.Reason) ? "" : $"\n**Reason:** {Session.Reason}"));
                break;

            case UmPage.Roles:
                embed
                    .WithTitle("Manage Roles")
                    .WithDescription(BuildUserList());
                break;
        }

        return embed.Build();
    }

     MessageComponent BuildComponents()
    {
        var builder = new ComponentBuilder();

        switch (Session.Page)
        {
            case UmPage.Main:
                builder
                    .WithButton("❗ Warn", "um:warn", ButtonStyle.Primary)
                    .WithButton("🔨 Ban", "um:ban", ButtonStyle.Danger)
                    .WithButton("👢 Kick", "um:kick", ButtonStyle.Danger)
                    .WithButton("⚙ Roles", "um:roles", ButtonStyle.Secondary);
                break;

            case UmPage.SelectUsers:
                builder.WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId("um:users")
                    .WithType(ComponentType.UserSelect)
                    .WithPlaceholder("Select users...")
                    .WithMinValues(1)
                    .WithMaxValues(25));
                break;

            case UmPage.Warn:
                builder
                    .WithButton("Set Reason", "um:reason_warn")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm ⚠️", "um:confirm_warn", ButtonStyle.Danger);
                break;

            case UmPage.Ban:
                builder
                    .WithButton("Set Reason", "um:reason_ban")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm 🗑", "um:confirm_ban", ButtonStyle.Danger);
                break;

            case UmPage.Kick:
                builder
                    .WithButton("Set Reason", "um:reason_kick")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm 🗑", "um:confirm_kick", ButtonStyle.Danger);
                break;

            case UmPage.Roles:
                builder
                    .WithButton("➕ Add", "um:add_roles", ButtonStyle.Primary)
                    .WithButton("➖ Remove", "um:remove_roles", ButtonStyle.Danger)
                    .WithButton("Back", "um:back");
                break;
        }

        return builder.Build();
    }

    // ==========================================================
    // Helpers
    // ==========================================================

     string BuildUserList()
    {
        if (Session.SelectedUsers.Count == 0)
            return "No users selected.";

        return string.Join("\n", Session.SelectedUsers.Select(x => $"• <@{x}>"));
    }

     void LogModAction(string action, HashSet<ulong> users, string? reason)
    {
        string userList = string.Join(", ", users.Select(id => $"<@{id}>"));
        Logger.Log($"[UserMgmt] {action} by {Context.User.Username}: {userList} | Reason: {reason ?? "none"}");
    }

     async Task LogModChannel(string action, Color color, HashSet<ulong> users, string? reason)
    {
        try
        {
            var channel = Context.Client.GetChannel(Config.ModLogChannelId) as IMessageChannel;
            if (channel == null) return;

            string userList = string.Join("\n", users.Select(id => $"• <@{id}>"));

            var embed = new EmbedBuilder()
                .WithTitle($"{action} (via /user)")
                .WithColor(color)
                .AddField("Target(s)", userList)
                .AddField("Moderator", Context.User.Mention, true)
                .AddField("Reason", string.IsNullOrWhiteSpace(reason) ? "*No reason provided*" : reason, true)
                .WithCurrentTimestamp()
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserMgmt] Failed to send mod log: {ex.Message}");
        }
    }

    // ==========================================================
    // Models
    // ==========================================================

     enum UmPage
    {
        Main,
        SelectUsers,
        Warn,
        Ban,
        Kick,
        Roles
    }

     enum UmAction
    {
        None,
        Warn,
        Ban,
        Kick,
        AddRoles,
        RemoveRoles
    }

     sealed class UmSession
    {
        public HashSet<ulong> SelectedUsers { get; } = new();
        public UmPage Page { get; set; }
        public UmAction PendingAction { get; set; }
        public string? Reason { get; set; }

        public void Reset()
        {
            SelectedUsers.Clear();
            Reason = null;
            PendingAction = UmAction.None;
            Page = UmPage.Main;
        }
    }
}

public class UmReasonModal : IModal
{
    public string Title => "Moderation Reason";

    [InputLabel("Reason (optional)")]
    [ModalTextInput("um_reason_input", TextInputStyle.Paragraph,
        placeholder: "Enter a reason...", maxLength: 500)]
    public string Reason { get; set; } = "";
}
