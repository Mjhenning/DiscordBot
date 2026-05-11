// ─────────────────────────────────────────────────────────────────────────────
// ScheduleData.cs — updated
// ─────────────────────────────────────────────────────────────────────────────

namespace DiscordBot.Data;
using Newtonsoft.Json;

public class ScheduleData
{
    const string FilePath = "Data/schedule.json";

    public List<ScheduleEntry> ScheduleEntries { get; private set; } = new();

    // Persistent published message reference
    public ulong PublishedMessageId  { get; private set; } = 0;
    public ulong PublishedChannelId  { get; private set; } = 0;
    public string WeekStart          { get; private set; } = ""; // ISO 8601 UTC — Monday of published week

    public ScheduleData() => Initialize();

    void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);
        ScheduleStore? store = JsonConvert.DeserializeObject<ScheduleStore>(json);

        if (store != null)
        {
            ScheduleEntries    = store.Entries    ?? new();
            PublishedMessageId = store.MessageId;
            PublishedChannelId = store.ChannelId;
            WeekStart          = store.WeekStart  ?? "";
        }
    }

    public void Save()
    {
        ScheduleStore store = new()
        {
            Entries   = ScheduleEntries,
            MessageId = PublishedMessageId,
            ChannelId = PublishedChannelId,
            WeekStart = WeekStart
        };

        string json = JsonConvert.SerializeObject(store, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public ScheduleEntry? GetEntry(ulong id)
    {
        EnsureCurrentWeek();
        
        return ScheduleEntries.FirstOrDefault(x => x.Id == id);
    }

    public void AddEntry(ScheduleEntry entry)
    {
        EnsureCurrentWeek();
        
        ScheduleEntries.Add(entry);
        Save();
    }

    public void RemoveEntry(ulong id)
    {
        EnsureCurrentWeek();
        
        ScheduleEntries.RemoveAll(x => x.Id == id);
        Save();
    }

    public void SetPublished(ulong messageId, ulong channelId, DateTimeOffset weekStart)
    {
        PublishedMessageId = messageId;
        PublishedChannelId = channelId;
        WeekStart          = weekStart.Date.ToString("yyyy-MM-dd");
        Save();
    }

    public void ClearPublished()
    {
        PublishedMessageId = 0;
        PublishedChannelId = 0;
        WeekStart          = "";
        ScheduleEntries.Clear();
        Save();
    }

    // Returns true if a message is published for the current week
    public bool IsPublishedThisWeek()
    {
        EnsureCurrentWeek();
        
        if (PublishedMessageId == 0 || string.IsNullOrWhiteSpace(WeekStart))
            return false;
        
        string currentWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");
        return WeekStart == currentWeekStart;
    }

    // Monday of the current UTC week
    public static DateTimeOffset GetCurrentWeekStart()
    {
        DateTimeOffset today = DateTimeOffset.UtcNow;
        int daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-daysFromMonday).Date;
    }
    
    public void EnsureCurrentWeek()
    {
        string currentWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");

        // No active week stored
        if (string.IsNullOrWhiteSpace(WeekStart))
            return;

        // Same week → nothing to do
        if (WeekStart == currentWeekStart)
            return;

        Console.WriteLine("[Info] Week rollover detected — clearing previous schedule");

        ClearPublished();
    }
}

// Wrapper so we can store entries + metadata in one JSON object
public class ScheduleStore
{
    public List<ScheduleEntry> Entries   { get; set; } = new();
    public ulong MessageId               { get; set; } = 0;
    public ulong ChannelId               { get; set; } = 0;
    public string WeekStart              { get; set; } = "";
}

public class ScheduleEntry
{
    public ulong  Id          { get; set; } = 0;
    public string Description { get; set; } = "";
    public string ScheduledAt { get; set; } = "";

    [JsonIgnore]
    public DateTimeOffset ScheduledAtParsed => DateTimeOffset.Parse(ScheduledAt);

    [JsonIgnore]
    public string ScheduledAtDisplay => ScheduledAtParsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}