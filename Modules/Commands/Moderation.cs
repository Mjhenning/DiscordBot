using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Data;

namespace DiscordBot.Modules;

public class Moderation
{

}

//SLASH COMMAND FOR MANGING USERS ALLOWED INTO LIVE VOICE CHANNEL
public class LiveGuestsModule : InteractionModuleBase<SocketInteractionContext>
{
    //SLASH COMMAND START
    [SlashCommand("liveguests", "Manage your live guests")]
    [RequireRole("🔧 Processes")] 
    public async Task LiveGuestsMenu()
    {
        MessageComponent buttons = new ComponentBuilder()
            .WithButton("➕ Add",    "lg_btn_add",    ButtonStyle.Primary)
            .WithButton("🗑️ Remove", "lg_btn_remove", ButtonStyle.Danger)
            .Build();

        await RespondAsync(
            "**Live Guests** — what would you like to do?",
            components: buttons,
            ephemeral: true
        );
    }

    [ComponentInteraction("lg_btn_add",    ignoreGroupNames: true)]
    public Task OnBtnAdd()    => AddStart();

    [ComponentInteraction("lg_btn_remove", ignoreGroupNames: true)]
    public Task OnBtnRemove() => RemoveStart();


    // ADD FLOW

    public async Task AddStart()
    {
        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("lg_users")
                .WithType(ComponentType.UserSelect)
                .WithPlaceholder("Select the guests")
                .WithMinValues(1)
                .WithMaxValues(25))
            .Build();

        await RespondAsync(
            "**Step 1:** Choose the guests to allow into the live vc for this session.",
            components: menu,
            ephemeral: true
        );
    }

    [ComponentInteraction("lg_users", ignoreGroupNames: true)]
    public async Task OnUsersSelected(string[] selectedValues)
    {
        SocketRole? role = GetGuestRoleOrNull();
        if (role == null)
        {
            await RespondAsync("⚠️ Live guest role is not configured correctly — couldn't find it on this server.", ephemeral: true);
            return;
        }

        var result = await ApplyRoleChangeAsync(
            selectedValues,
            role,
            add: true,
            auditReason: $"Live guest added by {Context.User.Username}"
        );

        await RespondAsync(result.BuildSummary(role, "Added", "Failed"), ephemeral: true);
    }


    // REMOVE FLOW

    public async Task RemoveStart()
    {
        SocketRole? role = GetGuestRoleOrNull();
        if (role == null)
        {
            await RespondAsync("⚠️ Live guest role is not configured correctly — couldn't find it on this server.", ephemeral: true);
            return;
        }

        List<SocketGuildUser> currentGuests = Context.Guild.Users
            .Where(u => u.Roles.Any(r => r.Id == role.Id))
            .ToList();

        if (currentGuests.Count == 0)
        {
            await RespondAsync("Nobody currently has the live guest role.", ephemeral: true);
            return;
        }

        bool truncated = currentGuests.Count > 25;
        if (truncated)
            currentGuests = currentGuests.Take(25).ToList();

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("lg_remove_users")
            .WithPlaceholder($"Select guests to remove ({currentGuests.Count} have the role)")
            .WithMinValues(1)
            .WithMaxValues(currentGuests.Count);

        foreach (SocketGuildUser user in currentGuests)
            selectMenu.AddOption(user.DisplayName, user.Id.ToString());

        MessageComponent menu = new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .Build();

        string prompt = "**Remove guests:** Select who should lose the live guest role.";
        if (truncated)
            prompt += "\n⚠️ More than 25 users have this role — showing the first 25 only.";

        await RespondAsync(prompt, components: menu, ephemeral: true);
    }

    [ComponentInteraction("lg_remove_users", ignoreGroupNames: true)]
    public async Task OnRemoveUsersSelected(string[] selectedValues)
    {
        SocketRole? role = GetGuestRoleOrNull();
        if (role == null)
        {
            await RespondAsync("⚠️ Live guest role is not configured correctly — couldn't find it on this server.", ephemeral: true);
            return;
        }

        var result = await ApplyRoleChangeAsync(
            selectedValues,
            role,
            add: false,
            auditReason: $"Live guest removed by {Context.User.Username}"
        );

        await RespondAsync(result.BuildSummary(role, "Removed", "Failed"), ephemeral: true);
    }


    // SHARED HELPERS

    private SocketRole? GetGuestRoleOrNull() => Context.Guild.GetRole(Config.LiveGuestRoleId);

    /// <summary>
    /// Adds or removes <paramref name="role"/> for each user ID in <paramref name="userIds"/>.
    /// Skips users who already have/lack the role (so the summary reflects real changes only)
    /// and tolerates per-user failures without aborting the batch.
    /// </summary>
    private async Task<RoleChangeResult> ApplyRoleChangeAsync(
        IEnumerable<string> userIds,
        SocketRole role,
        bool add,
        string auditReason)
    {
        var result = new RoleChangeResult();

        foreach (string idStr in userIds)
        {
            ulong userId = ulong.Parse(idStr);
            SocketGuildUser? guildUser = Context.Guild.GetUser(userId);

            if (guildUser == null)
            {
                result.Failed.Add($"<@{userId}> (not found in guild)");
                continue;
            }

            bool hasRole = guildUser.Roles.Any(r => r.Id == role.Id);
            if (add && hasRole)
            {
                result.Skipped.Add(guildUser.Mention);
                continue;
            }
            if (!add && !hasRole)
            {
                result.Skipped.Add(guildUser.Mention);
                continue;
            }

            try
            {
                var options = new RequestOptions { AuditLogReason = auditReason };
                if (add)
                    await guildUser.AddRoleAsync(role, options);
                else
                    await guildUser.RemoveRoleAsync(role, options);

                result.Changed.Add(guildUser.Mention);
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{guildUser.Mention} ({ex.Message})");
            }
        }

        return result;
    }

    private class RoleChangeResult
    {
        public List<string> Changed = new();
        public List<string> Skipped = new();
        public List<string> Failed  = new();

        public string BuildSummary(SocketRole role, string changedVerb, string failedLabel)
        {
            string summary = Changed.Count > 0
                ? $"✅ {changedVerb} **{role.Name}** for: {string.Join(", ", Changed)}"
                : $"No roles were changed.";

            if (Skipped.Count > 0)
                summary += $"\nℹ️ Already up to date: {string.Join(", ", Skipped)}";

            if (Failed.Count > 0)
                summary += $"\n⚠️ {failedLabel}: {string.Join(", ", Failed)}";

            return summary;
        }
    }
}