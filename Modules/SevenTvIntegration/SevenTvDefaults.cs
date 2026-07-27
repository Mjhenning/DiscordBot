namespace DiscordBot.Modules.SevenTv;

/// <summary>
/// Fallback 7TV channel + emote set used when a user hasn't run /channel and
/// /emote-set themselves. Resolved once (channel name -> 7TV IDs) and cached
/// for the lifetime of the process.
/// </summary>
public static class SevenTvDefaults
{
    private static (string ChannelId, string SetId, string SetName)? _resolved;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Returns the default channel ID + emote set ID, resolving and caching
    /// them on first call. Returns null if the channel can't be found on 7TV
    /// or has no emote sets — callers should treat that the same as "no default
    /// configured" and fall back to a global search.
    /// </summary>
    public static async Task<(string ChannelId, string SetId, string SetName)?> ResolveAsync()
    {
        if (_resolved != null) return _resolved;

        await Lock.WaitAsync();
        try
        {
            if (_resolved != null) return _resolved;

            var channels = await SevenTvApi.SearchChannelsAsync(Config.TwitchChannelName);
            var channel = channels.FirstOrDefault(c => c.MainConnection?.PlatformUsername == Config.TwitchChannelName);

            if (channel == null)
            {
                Logger.Log($"[7TV] Default channel \"{Config.TwitchChannelName}\" not found on 7TV.");
                return null;
            }

            var sets = await SevenTvApi.GetUserEmoteSetsAsync(channel.Id);
            var primarySet = sets.FirstOrDefault();

            if (primarySet == null)
            {
                Logger.Log($"[7TV] Default channel \"{Config.TwitchChannelName}\" has no emote sets on 7TV.");
                return null;
            }

            _resolved = (channel.Id, primarySet.Id, primarySet.Name);
            Logger.Log($"[7TV] Default channel resolved: {Config.TwitchChannelName} -> emote set \"{primarySet.Name}\"");
            return _resolved;
        }
        finally
        {
            Lock.Release();
        }
    }
}