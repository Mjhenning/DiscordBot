using Newtonsoft.Json;

namespace DiscordBot.Services;

public class GlosselEntry
{
    [JsonProperty("usrName")]
    public string UsrName { get; set; } = "";

    [JsonProperty("usrId")]
    public string UsrId { get; set; } = "";

    [JsonProperty("amount")]
    public int Amount { get; set; }

    [JsonProperty("lastCheckin")]
    public string LastCheckin { get; set; } = "";

    [JsonProperty("discordUserId")]
    public string? DiscordUserId { get; set; }
}

public class LinkedAccountsData
{
    readonly string _filePath;
    List<GlosselEntry> _entries = new();
    readonly object _lock = new();

    public LinkedAccountsData()
    {
        _filePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "TwitchBot", "data", "glossels_db.json"));
        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _entries = JsonConvert.DeserializeObject<List<GlosselEntry>>(json) ?? new();
                Logger.Log($"[LinkedAccounts] Loaded {_entries.Count} entries from {_filePath}");
            }
            else
            {
                Logger.Log($"[LinkedAccounts] Glossels file not found at {_filePath}, starting empty");
                _entries = new();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LinkedAccounts] Failed to load glossels: {ex.Message}");
            _entries = new();
        }
    }

    void Save()
    {
        var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
        File.WriteAllText(_filePath, json);
    }

    public GlosselEntry? FindByTwitchId(string twitchUserId)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.UsrId == twitchUserId);
        }
    }

    public GlosselEntry? FindByDiscordId(ulong discordUserId)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.DiscordUserId == discordUserId.ToString());
        }
    }

    public bool Link(string twitchUserId, ulong discordUserId)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.UsrId == twitchUserId);
            if (entry == null) return false;

            entry.DiscordUserId = discordUserId.ToString();
            Save();
            Logger.Log($"[LinkedAccounts] Linked Twitch {twitchUserId} ({entry.UsrName}) to Discord {discordUserId}");
            return true;
        }
    }
}
