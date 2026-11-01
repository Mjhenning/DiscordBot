using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text.RegularExpressions;

namespace DiscordBot.Modules.Moderation;

public class UserManagementModule : InteractionModuleBase<SocketInteractionContext>
{
     static readonly Dictionary<ulong, UmSession> Sessions = new();
     static readonly Regex MentionRegex = new(@"<@(?:!|&)?(\d+)>", RegexOptions.Compiled);

     const string FooterText = "System Active \u2022 4/30/03, 3:00 AM";
     const string AuthorIcon = "https://images.icon-icons.com/183/PNG/256/Windows_Messenger_22559.png";

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

    //-------------------------------------SLASH COMMAND-------------------------------------

    [SlashCommand("moderate", "Warn, ban, kick, or list members by role")]
    [RequireRole("\U0001f527 Processes")]
    public async Task UserMenu(string targets)
    {
        Session.Reset();

        var ids = new HashSet<ulong>();
        foreach (Match m in MentionRegex.Matches(targets))
            if (ulong.TryParse(m.Groups[1].Value, out ulong id)) ids.Add(id);
        foreach (string part in targets.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (ulong.TryParse(part, out ulong id)) ids.Add(id);

        if (ids.Count == 0)
        {
            await RespondAsync("No valid users or roles found.", ephemeral: true);
            return;
        }

        var userIds = new HashSet<ulong>();
        var roleIds = new HashSet<ulong>();

        foreach (ulong id in ids)
        {
            if (Context.Guild.GetUser(id) != null) userIds.Add(id);
            else if (Context.Guild.GetRole(id) != null) roleIds.Add(id);
        }

        if (userIds.Count > 0 && roleIds.Count > 0)
        {
            await RespondAsync("Don't mix users and roles in one call. Use `/moderate` separately for each.", ephemeral: true);
            return;
        }

        if (roleIds.Count > 0)
        {
            if (roleIds.Count > 1)
            {
                await RespondAsync("Only one role at a time.", ephemeral: true);
                return;
            }
            ulong roleId = roleIds.First();
            Session.IsRoleMode = true;
            Session.RoleId = roleId;
            Session.Page = UmPage.Main;
            await RespondAsync(
                embed: new EmbedBuilder()
                    .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                    .WithTitle("Role Members")
                    .WithDescription($"Role: <@&{roleId}>\n\nPress the button to list members with this role.")
                    .WithColor(Color.Blue)
                    .WithFooter(FooterText)
                    .WithCurrentTimestamp()
                    .Build(),
                components: new ComponentBuilder()
                    .WithButton("\u2691\ufe0f List Members", "um:role_members", ButtonStyle.Primary)
                    .WithButton("Back", "um:back")
                    .Build(),
                ephemeral: true);
            return;
        }

        Session.SelectedUsers = userIds;

        if (Session.SelectedUsers.Count == 0)
        {
            await RespondAsync(
                "No valid users found. Mention them or provide their IDs.",
                ephemeral: true);
            return;
        }

        Session.Page = UmPage.Main;
        await RespondAsync(
            embed: BuildEmbed(),
            components: BuildComponents(),
            ephemeral: true
        );
    }

    //-------------------------------------NAVIGATION FROM MAIN MENU-------------------------------------

    [ComponentInteraction("um:warn")]
    public async Task Warn()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Warn;
        Session.Page = UmPage.Warn;
        await Refresh();
    }

    [ComponentInteraction("um:ban")]
    public async Task Ban()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Ban;
        Session.Page = UmPage.Ban;
        await Refresh();
    }

    [ComponentInteraction("um:kick")]
    public async Task Kick()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.Kick;
        Session.Page = UmPage.Kick;
        await Refresh();
    }

    [ComponentInteraction("um:role_members")]
    public async Task RoleMembersEntry()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.RoleMembers;

        if (Session.IsRoleMode && Session.RoleId != 0)
        {
            await RoleMembersSubmittedFromId(Session.RoleId);
            return;
        }

        await ShowRoleMembersModal();
    }

    [ComponentInteraction("um:add_roles")]
    public async Task AddRolesEntry()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.AddRoles;
        await ShowAddRoles();
    }

    [ComponentInteraction("um:remove_roles")]
    public async Task RemoveRolesEntry()
    {
        await DeferAsync(ephemeral: true);
        Session.PendingAction = UmAction.RemoveRoles;
        await ShowRemoveRoles();
    }

    [ComponentInteraction("um:back")]
    public async Task Back()
    {
        await DeferAsync(ephemeral: true);
        Session.Page = UmPage.Main;
        await Refresh();
    }

    //-------------------------------------CONFIRM ACTIONS-------------------------------------

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
                var embed = new EmbedBuilder()
                    .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                    .WithTitle($"\u26a0 Warning from {Context.Guild.Name}")
                    .WithDescription("You have been warned by a moderator.")
                    .WithColor(Color.Orange)
                    .WithFooter(FooterText)
                    .WithCurrentTimestamp();

                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");

                await dm.SendMessageAsync(embed: embed.Build());
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
                var dm = await user.CreateDMChannelAsync();
                var embed = new EmbedBuilder()
                    .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                    .WithTitle($"\U0001f52b Banned from {Context.Guild.Name}")
                    .WithDescription("You have been banned by a moderator.")
                    .WithColor(Color.Red)
                    .WithFooter(FooterText)
                    .WithCurrentTimestamp();

                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");

                await dm.SendMessageAsync(embed: embed.Build());
            }
            catch { }

            try
            {
                await user.BanAsync(
                    pruneDays: 0,
                    reason: AuditReason("Banned")
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
                var dm = await user.CreateDMChannelAsync();
                var embed = new EmbedBuilder()
                    .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                    .WithTitle($"\U0001f462 Kicked from {Context.Guild.Name}")
                    .WithDescription("You have been kicked by a moderator.")
                    .WithColor(Color.DarkOrange)
                    .WithFooter(FooterText)
                    .WithCurrentTimestamp();

                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");

                await dm.SendMessageAsync(embed: embed.Build());
            }
            catch { }

            try
            {
                await user.KickAsync(
                    reason: AuditReason("Kicked")
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

    //-------------------------------------REASON MODAL-------------------------------------

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

    //-------------------------------------ADD ROLES DISPLAY-------------------------------------

     async Task ShowAddRoles()
    {
        var rolePool = Context.Guild.Roles
            .Where(r => r.Id != Context.Guild.Id && !r.IsManaged);

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
            .WithButton("Back", "um:back")
            .Build();

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = new EmbedBuilder()
                .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                .WithTitle("Add Roles")
                .WithDescription($"Select roles to add to {BuildUserList()}")
                .WithColor(Color.Blue)
                .WithFooter(FooterText)
                .WithCurrentTimestamp()
                .Build();
            msg.Components = menu;
        });
    }

    //-------------------------------------REMOVE ROLES DISPLAY-------------------------------------

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
            .WithButton("Back", "um:back")
            .Build();

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Embed = new EmbedBuilder()
                .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                .WithTitle("Remove Roles")
                .WithDescription($"Select roles to remove from {BuildUserList()}")
                .WithColor(Color.Red)
                .WithFooter(FooterText)
                .WithCurrentTimestamp()
                .Build();
            msg.Components = menu;
        });
    }

    //-------------------------------------ROLE MANAGEMENT: APPLY-------------------------------------

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
                var existingRoleIds = user.Roles.Select(r => r.Id).ToHashSet();
                var rolesToAdd = roleIds
                    .Select(id => Context.Guild.GetRole(ulong.Parse(id)))
                    .Where(r => r != null && !existingRoleIds.Contains(r.Id))
                    .ToList();

                if (rolesToAdd.Count > 0)
                {
                    await user.AddRolesAsync(rolesToAdd,
                        new RequestOptions { AuditLogReason = $"Roles added by {Context.User.Username}" });
                }
                success++;
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Added roles to {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        await LogModChannel("Role add", Color.Green, Session.SelectedUsers, null);
        Session.Reset();
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
                var userRoleIds = user.Roles.Select(r => r.Id).ToHashSet();
                var rolesToRemove = roleIds
                    .Select(id => Context.Guild.GetRole(ulong.Parse(id)))
                    .Where(r => r != null && userRoleIds.Contains(r.Id))
                    .ToList();

                if (rolesToRemove.Count > 0)
                {
                    await user.RemoveRolesAsync(rolesToRemove,
                        new RequestOptions { AuditLogReason = $"Roles removed by {Context.User.Username}" });
                }
                success++;
            }
            catch { failed++; }
        }

        await FollowupAsync(
            $"Removed roles from {success} user(s)." + (failed > 0 ? $" Failed: {failed}" : ""),
            ephemeral: true
        );

        await LogModChannel("Role remove", Color.Red, Session.SelectedUsers, null);
        Session.Reset();
    }

    //-------------------------------------RENDERING-------------------------------------

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
            .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
            .WithFooter(FooterText)
            .WithCurrentTimestamp();

        switch (Session.Page)
        {
            case UmPage.Main:
                embed
                    .WithTitle("User Management")
                    .WithDescription(BuildUserList() +
                        "\n\nPick an action below.")
                    .WithColor(Color.Blue);
                break;

            case UmPage.Warn:
                embed
                    .WithTitle("Warn Users")
                    .WithDescription(BuildUserList())
                    .WithColor(Color.Orange);
                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");
                break;

            case UmPage.Ban:
                embed
                    .WithTitle("Ban Users")
                    .WithDescription(BuildUserList())
                    .WithColor(Color.Red);
                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");
                break;

            case UmPage.Kick:
                embed
                    .WithTitle("Kick Users")
                    .WithDescription(BuildUserList())
                    .WithColor(Color.DarkOrange);
                if (!string.IsNullOrWhiteSpace(Session.Reason))
                    embed.AddField("Reason", $"> {Session.Reason}");
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
                    .WithButton("\u2757 Warn", "um:warn", ButtonStyle.Primary)
                    .WithButton("\U0001f528 Ban", "um:ban", ButtonStyle.Danger)
                    .WithButton("\U0001f462 Kick", "um:kick", ButtonStyle.Danger)
                    .WithButton("\u2699\uFE0F Add Roles", "um:add_roles", ButtonStyle.Secondary)
                    .WithButton("\u2699\uFE0F Remove Roles", "um:remove_roles", ButtonStyle.Secondary);

                break;

            case UmPage.Warn:
                builder
                    .WithButton("Set Reason", "um:reason_warn")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm \u26a0\ufe0f", "um:confirm_warn", ButtonStyle.Danger);
                break;

            case UmPage.Ban:
                builder
                    .WithButton("Set Reason", "um:reason_ban")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm \U0001f52b", "um:confirm_ban", ButtonStyle.Danger);
                break;

            case UmPage.Kick:
                builder
                    .WithButton("Set Reason", "um:reason_kick")
                    .WithButton("Back", "um:back")
                    .WithButton("Confirm \U0001f462", "um:confirm_kick", ButtonStyle.Danger);
                break;
        }

        return builder.Build();
    }

    //-------------------------------------HELPERS-------------------------------------

     static HashSet<ulong> ParseUserIds(string input)
    {
        var ids = new HashSet<ulong>();

        foreach (Match match in MentionRegex.Matches(input))
        {
            if (ulong.TryParse(match.Groups[1].Value, out ulong id))
                ids.Add(id);
        }

        foreach (string part in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ulong.TryParse(part, out ulong id))
                ids.Add(id);
        }

        return ids;
    }

     string BuildUserList()
    {
        if (Session.SelectedUsers.Count == 0)
            return "No users selected.";

        return string.Join("\n", Session.SelectedUsers.Select(x => $"\u2022 <@{x}>"));
    }

     void LogModAction(string action, HashSet<ulong> users, string? reason)
    {
        string userList = string.Join(", ", users.Select(id => $"<@{id}>"));
        Logger.Log($"[UserMgmt] {action} by {Context.User.Username}: {userList} | Reason: {reason ?? "none"}");
    }

    // audit log reason for kick/ban, omits the ": reason" suffix when
    // none was set so the mod log doesn't end in a dangling colon
     string AuditReason(string action)
    {
        return string.IsNullOrWhiteSpace(Session.Reason)
            ? $"{action} by {Context.User.Username}"
            : $"{action} by {Context.User.Username}: {Session.Reason}";
    }

     async Task LogModChannel(string action, Color color, HashSet<ulong> users, string? reason)
    {
        try
        {
            var channel = Context.Client.GetChannel(Config.ModLogChannelId) as IMessageChannel;
            if (channel == null) return;

            string userList = string.Join("\n", users.Select(id => $"\u2022 <@{id}>"));

            var builder = new EmbedBuilder()
                .WithTitle($"{action} (via /moderate)")
                .WithColor(color)
                .WithCurrentTimestamp()
                .AddField("Target(s)", userList)
                .AddField("Moderator", Context.User.Mention, true);

            if (!string.IsNullOrWhiteSpace(reason))
                builder.AddField("Reason", reason, true);

            var embed = builder.Build();

            await channel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserMgmt] Failed to send mod log: {ex.Message}");
        }
    }

    //-------------------------------------MODELS-------------------------------------

     enum UmPage
    {
        Main,
        Warn,
        Ban,
        Kick
    }

     enum UmAction
    {
        None,
        Warn,
        Ban,
        Kick,
        AddRoles,
        RemoveRoles,
        RoleMembers
    }

     sealed class UmSession
    {
        public HashSet<ulong> SelectedUsers { get; set; } = new();
        public UmPage Page { get; set; }
        public UmAction PendingAction { get; set; }
        public bool IsRoleMode { get; set; }
        public ulong RoleId { get; set; }
        public string? Reason { get; set; }

        public void Reset()
        {
            SelectedUsers.Clear();
            Reason = null;
            PendingAction = UmAction.None;
            Page = UmPage.Main;
            IsRoleMode = false;
            RoleId = 0;
        }
    }

    async Task RoleMembersSubmittedFromId(ulong roleId)
    {
        var role = Context.Guild.GetRole(roleId);
        if (role == null)
        {
            await FollowupAsync("Role not found.", ephemeral: true);
            Session.Reset();
            return;
        }

        var members = new List<IGuildUser>();
        await foreach (var batch in Context.Guild.GetUsersAsync())
            members.AddRange(batch);
        members = members
            .Where(u => u.RoleIds.Contains(roleId))
            .OrderBy(u => u.Username)
            .ToList();

        string list = members.Count > 0
            ? string.Join("\n", members.Take(30).Select(m => $"\u2022 <@{m.Id}>"))
            + (members.Count > 30 ? $"\n... and {members.Count - 30} more" : "")
            : "No members found with this role.";

        await FollowupAsync(
            embed: new EmbedBuilder()
                .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                .WithTitle($"Members with {role.Name}")
                .WithDescription(list)
                .WithColor(Color.Blue)
                .WithFooter(FooterText)
                .WithCurrentTimestamp()
                .Build(),
            ephemeral: true);
        Session.Reset();
    }

    async Task ShowRoleMembersModal()
    {
        await RespondWithModalAsync<UmRoleModal>("um:role_submit");
    }

    [ModalInteraction("um:role_submit")]
    public async Task RoleMembersSubmitted(UmRoleModal modal)
    {
        await DeferAsync(ephemeral: true);

        var match = MentionRegex.Match(modal.RoleMention);
        if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out ulong roleId))
        {
            await FollowupAsync("No valid role mention found. Mention it like `@RoleName`.", ephemeral: true);
            Session.Reset();
            return;
        }

        var role = Context.Guild.GetRole(roleId);
        if (role == null)
        {
            await FollowupAsync("Role not found in this server.", ephemeral: true);
            Session.Reset();
            return;
        }

        var members = new List<IGuildUser>();
        await foreach (var batch in Context.Guild.GetUsersAsync())
            members.AddRange(batch);
        members = members
            .Where(u => u.RoleIds.Contains(roleId))
            .OrderBy(u => u.Username)
            .ToList();

        string list = members.Count > 0
            ? string.Join("\n", members.Take(30).Select(m => $"\u2022 <@{m.Id}>"))
            + (members.Count > 30 ? $"\n... and {members.Count - 30} more" : "")
            : "No members found with this role.";

        await FollowupAsync(
            embed: new EmbedBuilder()
                .WithAuthor("AETHER-OS // MODERATION", AuthorIcon)
                .WithTitle($"Members with {role.Name}")
                .WithDescription(list)
                .WithColor(Color.Blue)
                .WithFooter(FooterText)
                .WithCurrentTimestamp()
                .Build(),
            ephemeral: true);

        Session.Reset();
    }
}

public class UmRoleModal : IModal
{
    public string Title => "List Members by Role";

    [InputLabel("Role (mention it)")]
    [ModalTextInput("um_role_input", TextInputStyle.Short,
        placeholder: "@RoleName", maxLength: 100)]
    public string RoleMention { get; set; } = "";
}

public class UmReasonModal : IModal
{
    public string Title => "Moderation Reason";

    [InputLabel("Reason (optional)")]
    [ModalTextInput("um_reason_input", TextInputStyle.Paragraph,
        placeholder: "Enter a reason...", maxLength: 500)]
    public string Reason { get; set; } = "";
}
