namespace DiscordBot.Modules.SevenTv;

// fallback 7tv channel + emote set used when a user hasn't run /channel or
// /emote-set themselves. resolved once (channel name to 7tv ids) and cached
// for the lifetime of the process.
public static class SevenTvDefaults
{
     static (string ChannelId, string SetId, string SetName)? _resolved;
     static readonly SemaphoreSlim Lock = new(1, 1);

    // returns the default channel id + emote set id, resolving and caching
    // them on first call. returns null if the channel can't be found on 7tv
    // or has no emote sets. callers should treat that the same as "no default
    // configured" and fall back to a global search.
    public static async Task<(string ChannelId, string SetId, string SetName)?> ResolveAsync()
    {
        if (_resolved != null) return _resolved;

        await Lock.WaitAsync();
        try
        {
            if (_resolved != null) return _resolved;

            var channels = await SevenTvApi.SearchChannelsAsync(Config.TwitchChannelName);

            // case insensitive match, since 7tv's stored username casing doesn't
            // necessarily match how the channel name is cased in config. twitch
            // handles are case insensitive everywhere else too. falls back to
            // the top search result if nothing matches exactly, since we're
            // specifically searching for this name and the first hit should
            // almost always be the right channel.
            var channel = channels.FirstOrDefault(c =>
                    string.Equals(c.MainConnection?.PlatformUsername, Config.TwitchChannelName, StringComparison.OrdinalIgnoreCase))
                ?? channels.FirstOrDefault(c => c.MainConnection != null);

            if (channel == null)
            {
                Logger.Log($"[7TV] Default channel \"{Config.TwitchChannelName}\" not found on 7TV. " +
                           $"Search returned {channels.Count} result(s): " +
                           string.Join(", ", channels.Select(c => c.MainConnection?.PlatformUsername ?? "(no connection)")));
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