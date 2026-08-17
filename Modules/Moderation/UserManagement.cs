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

        await RespondAsync(
            embed: BuildEmbed(),
            components: BuildComponents(),
            ephemeral: true
        );
    }

    // ==========================================================
    // User Selection
    // ==========================================================

    [ComponentInteraction("um:users")]
    public async Task UsersSelected(string[] users)
    {
        await DeferAsync(ephemeral: true);

        Session.SelectedUsers.Clear();
        foreach (var id in users)
            Session.SelectedUsers.Add(ulong.Parse(id));

        Session.Page = UmPage.Main;
        await Refresh();
    }

    // ==========================================================
    // Navigation
    // ==========================================================

    [ComponentInteraction("um:warn")]
    public async Task Warn()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Warn;
        await Refresh();
    }

    [ComponentInteraction("um:ban")]
    public async Task Ban()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Ban;
        await Refresh();
    }

    [ComponentInteraction("um:kick")]
    public async Task Kick()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Kick;
        await Refresh();
    }

    [ComponentInteraction("um:roles")]
    public async Task Roles()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Roles;
        await Refresh();
    }

    [ComponentInteraction("um:liveguest")]
    public async Task LiveGuest()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.LiveGuest;
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
    // Role Management
    // ==========================================================

    [ComponentInteraction("um:add_roles")]
    public async Task AddRoles()
    {
        await DeferAsync(ephemeral: true);
        var roles = Context.Guild.Roles
            .Where(r => r.Id != Context.Guild.Id && !r.IsManaged)
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
                .WithMaxValues(roles.Count))
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

        Session.Page = UmPage.Main;
        await Refresh();
    }

    [ComponentInteraction("um:remove_roles")]
    public async Task RemoveRoles()
    {
        await DeferAsync(ephemeral: true);
        var roles = Context.Guild.Roles
            .Where(r => r.Id != Context.Guild.Id && !r.IsManaged)
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
                .WithMaxValues(roles.Count))
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

        Session.Page = UmPage.Main;
        await Refresh();
    }

    [ComponentInteraction("um:back_roles")]
    public async Task BackFromRoles()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    // ==========================================================
    // Live Guest Management
    // ==========================================================

    [ComponentInteraction("um:lg_add")]
    public async Task LiveGuestAdd()
    {
        await DeferAsync(ephemeral: true);

        SocketRole? role = Context.Guild.GetRole(Config.LiveGuestRoleId);
        if (role == null)
        {
            await FollowupAsync("Live guest role is not configured.", ephemeral: true);
            return;
        }

        int success = 0;
        int skipped = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            if (user.Roles.Any(r => r.Id == role.Id)) { skipped++; continue; }

            try
            {
                await user.AddRoleAsync(role,
                    new RequestOptions { AuditLogReason = $"Live guest added by {Context.User.Username}" });
                success++;
            }
            catch { failed++; }
        }

        string summary = $"Added **{role.Name}** to {success} user(s).";
        if (skipped > 0) summary += $"\nAlready had role: {skipped}";
        if (failed > 0) summary += $"\nFailed: {failed}";

        await FollowupAsync(summary, ephemeral: true);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    [ComponentInteraction("um:lg_remove")]
    public async Task LiveGuestRemove()
    {
        await DeferAsync(ephemeral: true);

        SocketRole? role = Context.Guild.GetRole(Config.LiveGuestRoleId);
        if (role == null)
        {
            await FollowupAsync("Live guest role is not configured.", ephemeral: true);
            return;
        }

        int success = 0;
        int skipped = 0;
        int failed = 0;

        foreach (ulong userId in Session.SelectedUsers)
        {
            var user = Context.Guild.GetUser(userId);
            if (user == null) { failed++; continue; }

            if (!user.Roles.Any(r => r.Id == role.Id)) { skipped++; continue; }

            try
            {
                await user.RemoveRoleAsync(role,
                    new RequestOptions { AuditLogReason = $"Live guest removed by {Context.User.Username}" });
                success++;
            }
            catch { failed++; }
        }

        string summary = $"Removed **{role.Name}** from {success} user(s).";
        if (skipped > 0) summary += $"\nDidn't have role: {skipped}";
        if (failed > 0) summary += $"\nFailed: {failed}";

        await FollowupAsync(summary, ephemeral: true);
        Session.Page = UmPage.Main;
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
            case UmPage.SelectUsers:
                embed
                    .WithTitle("User Management")
                    .WithDescription("Select one or more users to begin.");
                break;

            case UmPage.Main:
                embed
                    .WithTitle("User Management")
                    .WithDescription(BuildUserList());
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

            case UmPage.LiveGuest:
                embed
                    .WithTitle("Live Guest")
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
            case UmPage.SelectUsers:
                builder.WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId("um:users")
                    .WithType(ComponentType.UserSelect)
                    .WithPlaceholder("Select users...")
                    .WithMinValues(1)
                    .WithMaxValues(25));
                break;

            case UmPage.Main:
                builder
                    .WithButton("❗ Warn", "um:reason_warn", ButtonStyle.Primary)
                    .WithButton("🔨 Ban", "um:reason_ban", ButtonStyle.Danger)
                    .WithButton("👢 Kick", "um:reason_kick", ButtonStyle.Danger)
                    .WithButton("⚙ Roles", "um:roles", ButtonStyle.Secondary)
                    .WithButton("🎙 Live Guest", "um:liveguest", ButtonStyle.Secondary);
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

            case UmPage.LiveGuest:
                builder
                    .WithButton("➕ Add", "um:lg_add", ButtonStyle.Primary)
                    .WithButton("➖ Remove", "um:lg_remove", ButtonStyle.Danger)
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

    // ==========================================================
    // Models
    // ==========================================================

     enum UmPage
    {
        SelectUsers,
        Main,
        Warn,
        Ban,
        Kick,
        Roles,
        LiveGuest
    }

     sealed class UmSession
    {
        public HashSet<ulong> SelectedUsers { get; } = new();
        public UmPage Page { get; set; }
        public string? Reason { get; set; }

        public void Reset()
        {
            SelectedUsers.Clear();
            Reason = null;
            Page = UmPage.SelectUsers;
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
