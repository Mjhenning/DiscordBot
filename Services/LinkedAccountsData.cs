using Newtonsoft.Json;

namespace DiscordBot.Services;

public class UserDataEntry
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
    List<UserDataEntry> _entries = new();
    readonly object _lock = new();

    public LinkedAccountsData()
    {
        _filePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "TwitchBot", "data", "user_data.json"));
        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _entries = JsonConvert.DeserializeObject<List<UserDataEntry>>(json) ?? new();
                Logger.Log($"[LinkedAccounts] Loaded {_entries.Count} entries from {_filePath}");
            }
            else
            {
                Logger.Log($"[LinkedAccounts] User data file not found at {_filePath}, starting empty");
                _entries = new();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LinkedAccounts] Failed to load user data: {ex.Message}");
            _entries = new();
        }
    }

    public void Save()
    {
        var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
        File.WriteAllText(_filePath, json);
    }

    public UserDataEntry? FindByTwitchId(string twitchUserId)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.UsrId == twitchUserId);
        }
    }

    public UserDataEntry? FindByDiscordId(ulong discordUserId)
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

    public bool UpdateAmount(string twitchUserId, int newAmount)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.UsrId == twitchUserId);
            if (entry == null) return false;

            entry.Amount = newAmount;
            Save();
            return true;
        }
    }

    public bool AddAmountByDiscordId(ulong discordUserId, int delta)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.DiscordUserId == discordUserId.ToString());
            if (entry == null) return false;

            entry.Amount += delta;
            if (entry.Amount < 0) entry.Amount = 0;
            Save();
            return true;
        }
    }

    public bool TransferAmount(string fromTwitchId, string toTwitchId, int amount)
    {
        lock (_lock)
        {
            var from = _entries.FirstOrDefault(e => e.UsrId == fromTwitchId);
            var to = _entries.FirstOrDefault(e => e.UsrId == toTwitchId);
            if (from == null || to == null) return false;
            if (from.Amount < amount) return false;

            from.Amount -= amount;
            to.Amount += amount;
            Save();
            return true;
        }
    }
}
