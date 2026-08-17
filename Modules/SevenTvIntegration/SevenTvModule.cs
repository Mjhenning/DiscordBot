using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules.SevenTv;

/// <summary>
/// /emote, /channel, /image-size, /emote-set, /reset — ported from atmahana/7emotes.
/// Written as a proper Discord.Interactions module so it's auto-discovered by
/// the existing `interactions.AddModulesAsync(typeof(ReactionRolesModule).Assembly, services)`
/// call in Program.cs. No manual wiring needed — just needs to live in the
/// same project/assembly.
/// </summary>
[Group("7tv", "7TV emote commands")]
public class SevenTvModule : InteractionModuleBase<SocketInteractionContext>
{
     static readonly HttpClient Http = new();

    [SlashCommand("emote", "Send a 7TV emote")]
    public async Task EmoteCommand(
        [Summary("name", "The name of the emote")]
        [Autocomplete(typeof(EmoteAutocompleteHandler))]
        string name)
    {
        await DeferAsync();

        try
        {
            var prefs = await SevenTvPreferencesStore.GetPreferencesAsync(Context.User.Id);
            var setId = prefs.EmoteSetId;

            if (string.IsNullOrEmpty(setId))
            {
                var defaults = await SevenTvDefaults.ResolveAsync();
                setId = defaults?.SetId;
            }

            var emote = await SevenTvApi.SearchEmoteAsync(name, setId, Context.User.Id.ToString());

            if (emote == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = $"❌ Emote \"{name}\" not found on 7TV.");
                return;
            }

            var emoteInfo = SevenTvApi.GetEmoteUrl(emote, prefs.ImageSize);
            var imageBytes = await Http.GetByteArrayAsync(emoteInfo.Url);

            using var stream = new MemoryStream(imageBytes);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "\u200B"; // zero-width space — satisfies Discord.Net's "Content can't be empty" check, renders as nothing visible
                m.Attachments = new[] { new FileAttachment(stream, $"{emoteInfo.Name}.{emoteInfo.Format}") };
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error fetching emote: {ex.Message}");
            await ModifyOriginalResponseAsync(m => m.Content = "❌ An error occurred while fetching the emote. Please try again.");
        }
    }

    [SlashCommand("channel", "Select 7TV channel")]
    public async Task ChannelCommand(
        [Summary("channel", "The name of the channel")]
        [Autocomplete(typeof(ChannelAutocompleteHandler))]
        string channel)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var channels = await SevenTvApi.SearchChannelsAsync(channel);
            var matched = channels.FirstOrDefault(c => c.MainConnection?.PlatformUsername == channel);

            if (matched?.MainConnection == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = $"❌ Channel \"{channel}\" not found on 7TV.");
                return;
            }

            var success = await SevenTvPreferencesStore.SetPreferencesAsync(Context.User.Id, prefs =>
            {
                prefs.EmoteChannelId = matched.Id;
                prefs.EmoteChannelName = matched.MainConnection.PlatformUsername;
            });

            if (success)
            {
                var displayName = string.IsNullOrEmpty(matched.MainConnection.PlatformDisplayName)
                    ? matched.MainConnection.PlatformUsername
                    : matched.MainConnection.PlatformDisplayName;
                var platform = matched.MainConnection.Platform.ToUpperInvariant();
                var platformEmoji = platform == "TWITCH" ? "🟣" : platform == "YOUTUBE" ? "🔴" : "📺";

                await ModifyOriginalResponseAsync(m =>
                    m.Content = $"✅ Selected channel: {platformEmoji} **{displayName}** (@{channel})");
            }
            else
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ Failed to save your channel preference. Please try again.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error handling channel selection: {ex.Message}");
            await ModifyOriginalResponseAsync(m => m.Content = "❌ An error occurred while saving the channel. Please try again.");
        }
    }

    [SlashCommand("image-size", "Set your preferred emote image size")]
    public async Task ImageSizeCommand(
        [Summary("size", "Select image size")]
        [Choice("1x (Small)", "1x")]
        [Choice("2x (Medium)", "2x")]
        [Choice("3x (Large)", "3x")]
        [Choice("4x (Extra Large)", "4x")]
        string size)
    {
        await DeferAsync(ephemeral: true);

        var success = await SevenTvPreferencesStore.SetPreferencesAsync(Context.User.Id, prefs => prefs.ImageSize = size);

        await ModifyOriginalResponseAsync(m => m.Content = success
            ? $"✅ Image size set to **{size}**! All emotes will now be sent in this size."
            : "❌ Failed to save your preference. Please try again.");
    }

    [SlashCommand("emote-set", "Select an emote set from your chosen channel")]
    public async Task EmoteSetCommand(
        [Summary("set", "The emote set to use")]
        [Autocomplete(typeof(EmoteSetAutocompleteHandler))]
        string set)
    {
        if (set == "no_channel")
        {
            await RespondAsync("⚠️ Please select a channel first using `/channel` command.", ephemeral: true);
            return;
        }

        if (set is "no_sets" or "error")
        {
            await RespondAsync("❌ Unable to load emote sets. Please try again later.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var parts = set.Split('|', 2);
            if (parts.Length != 2)
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ Invalid emote set data. Please try again.");
                return;
            }

            var (setId, setName) = (parts[0], parts[1]);

            var success = await SevenTvPreferencesStore.SetPreferencesAsync(Context.User.Id, prefs =>
            {
                prefs.EmoteSetId = setId;
                prefs.EmoteSetName = setName;
            });

            await ModifyOriginalResponseAsync(m => m.Content = success
                ? $"✅ Selected emote set: **{setName}**"
                : "❌ Failed to save your emote set preference. Please try again.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error handling emote-set selection: {ex.Message}");
            await ModifyOriginalResponseAsync(m => m.Content = "❌ An error occurred while saving the emote set. Please try again.");
        }
    }

    [SlashCommand("reset", "Reset all your preferences to defaults")]
    public async Task ResetCommand()
    {
        await DeferAsync(ephemeral: true);

        var success = await SevenTvPreferencesStore.DeletePreferencesAsync(Context.User.Id);

        await ModifyOriginalResponseAsync(m => m.Content = success
            ? "✅ Your preferences have been reset! All settings have been cleared and defaults will be used."
            : "❌ Failed to reset your preferences. Please try again.");
    }
}