namespace DiscordBot.Modules.SevenTv;

public class SevenTvImage
{
    public string Url { get; set; } = "";
    public int Size { get; set; }
    public string Mime { get; set; } = "";
}

public class SevenTvEmoteScores
{
    public long TopAllTime { get; set; }
}

public class SevenTvEmote
{
    public string Id { get; set; } = "";
    public string DefaultName { get; set; } = "";
    public SevenTvEmoteScores Scores { get; set; } = new();
    public List<SevenTvImage> Images { get; set; } = new();
}

public class SevenTvMainConnection
{
    public string Platform { get; set; } = "";
    public string PlatformId { get; set; } = "";
    public string PlatformUsername { get; set; } = "";
    public string PlatformDisplayName { get; set; } = "";
}

public class SevenTvUser
{
    public string Id { get; set; } = "";
    public SevenTvMainConnection? MainConnection { get; set; }
}

public class SevenTvEmoteSet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Capacity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// the resolved, ready-to-download image for an emote at a given size,
// matches the format detection (animated emotes come
// through as avif, static ones as png)
public class SevenTvEmoteInfo
{
    public string Url { get; set; } = "";
    public string Format { get; set; } = ""; // "avif" or "png"
    public bool IsAnimated { get; set; }
    public string Name { get; set; } = "";
}

public class SevenTvUserPreferences
{
    public string ImageSize { get; set; } = "2x";
    public string? EmoteChannelId { get; set; }
    public string? EmoteChannelName { get; set; }
    public string? EmoteSetId { get; set; }
    public string? EmoteSetName { get; set; }
}
