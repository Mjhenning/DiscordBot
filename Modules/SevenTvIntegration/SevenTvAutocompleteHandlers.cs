using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules.SevenTv;

public class EmoteAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var focused = autocompleteInteraction.Data.Current.Value as string ?? "";

        if (string.IsNullOrWhiteSpace(focused))
            return AutocompletionResult.FromSuccess();

        try
        {
            var prefs = await SevenTvPreferencesStore.GetPreferencesAsync(context.User.Id);
            var setId = prefs.EmoteSetId;

            if (string.IsNullOrEmpty(setId))
            {
                var defaults = await SevenTvDefaults.ResolveAsync();
                setId = defaults?.SetId;
            }

            var emotes = await SevenTvApi.SearchEmotesForAutocompleteAsync(focused, setId, context.User.Id.ToString());

            if (emotes.Count == 0)
            {
                return AutocompletionResult.FromSuccess(new[]
                {
                    new AutocompleteResult($"❌ No emotes found matching \"{focused}\"", "no_emotes")
                });
            }

            var choices = emotes.Take(25).Select(emote =>
            {
                var score = emote.Scores.TopAllTime;
                var scoreEmoji = score > 1000 ? "🔥" : score > 500 ? "⭐" : "";
                var name = $"{scoreEmoji} {emote.DisplayName} ({score:N0} uses)".Trim();
                return new AutocompleteResult(name.Length > 100 ? name[..100] : name, emote.DisplayName);
            });

            return AutocompletionResult.FromSuccess(choices);
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error handling emote autocomplete: {ex.Message}");
            return AutocompletionResult.FromSuccess();
        }
    }
}

public class ChannelAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var focused = autocompleteInteraction.Data.Current.Value as string ?? "";

        if (focused.Length < 2)
            return AutocompletionResult.FromSuccess();

        try
        {
            var channels = await SevenTvApi.SearchChannelsAsync(focused);

            var choices = channels
                .Where(c => c.MainConnection != null)
                .Take(25)
                .Select(c =>
                {
                    var displayName = string.IsNullOrEmpty(c.MainConnection!.PlatformDisplayName)
                        ? c.MainConnection.PlatformUsername
                        : c.MainConnection.PlatformDisplayName;
                    var username = c.MainConnection.PlatformUsername;
                    var platform = c.MainConnection.Platform.ToUpperInvariant();
                    var platformEmoji = platform == "TWITCH" ? "🟣" : platform == "YOUTUBE" ? "🔴" : "📺";
                    var name = $"{platformEmoji} {displayName} (@{username}) [{platform}]";

                    return new AutocompleteResult(name.Length > 100 ? name[..100] : name, username);
                });

            return AutocompletionResult.FromSuccess(choices);
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error handling channel autocomplete: {ex.Message}");
            return AutocompletionResult.FromSuccess();
        }
    }
}

public class EmoteSetAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var prefs = await SevenTvPreferencesStore.GetPreferencesAsync(context.User.Id);
            var channelId = prefs.EmoteChannelId;

            if (string.IsNullOrEmpty(channelId))
            {
                var defaults = await SevenTvDefaults.ResolveAsync();
                channelId = defaults?.ChannelId;
            }

            if (string.IsNullOrEmpty(channelId))
            {
                return AutocompletionResult.FromSuccess(new[]
                {
                    new AutocompleteResult("⚠️ Please select a channel first using /channel", "no_channel")
                });
            }

            var sets = await SevenTvApi.GetUserEmoteSetsAsync(channelId);

            if (sets.Count == 0)
            {
                return AutocompletionResult.FromSuccess(new[]
                {
                    new AutocompleteResult("❌ No emote sets found for this channel", "no_sets")
                });
            }

            var sorted = sets.OrderByDescending(s => s.UpdatedAt).ToList();

            var choices = sorted.Take(25).Select(set =>
            {
                var name = $"{set.Name} - ({set.Capacity} emotes) - {set.UpdatedAt:d}";
                return new AutocompleteResult(name.Length > 100 ? name[..100] : name, $"{set.Id}|{set.Name}");
            });

            return AutocompletionResult.FromSuccess(choices);
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Error handling emote-set autocomplete: {ex.Message}");
            return AutocompletionResult.FromSuccess(new[] { new AutocompleteResult("❌ Error loading emote sets", "error") });
        }
    }
}