// ─────────────────────────────────────────────────────────────────────────────
// ScheduleData.cs
// ─────────────────────────────────────────────────────────────────────────────

namespace DiscordBot.Data;
using Newtonsoft.Json;

public class ScheduleData
{
    const string FilePath = "Data/schedule.json";
    
    // ── Tweak these to change when the week resets ────────────────────────
    const DayOfWeek ResetDay    = DayOfWeek.Sunday;
    const int       ResetHour   = 23;   // 24-hour local time
    const int       ResetMinute = 30;
    // ─────────────────────────────────────────────────────────────────────
    
    Timer _resetTimer;

    public List<ScheduleEntry> ScheduleEntries { get; private set; } = new();

    // Persistent published message reference
    public ulong PublishedMessageId  { get; private set; } = 0;
    public ulong PublishedChannelId  { get; private set; } = 0;
    public string WeekStart          { get; private set; } = ""; // ISO 8601 UTC — Monday of published week
    public string EntriesWeekStart   { get; private set; } = ""; // ISO 8601 UTC — Monday of the week current entries belong to

    public ScheduleData() => Initialize();

    void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            EntriesWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");
            Save();
            StartResetTimer();
            return;
        }

        string json = File.ReadAllText(FilePath);
        ScheduleStore? store = JsonConvert.DeserializeObject<ScheduleStore>(json);

        if (store != null)
        {
            ScheduleEntries    = store.Entries          ?? new();
            PublishedMessageId = store.MessageId;
            PublishedChannelId = store.ChannelId;
            WeekStart          = store.WeekStart        ?? "";
            EntriesWeekStart   = store.EntriesWeekStart  ?? "";
        }

        // Back-compat: older saves won't have EntriesWeekStart set.
        // Fall back to WeekStart if we have it, otherwise assume "now".
        if (string.IsNullOrWhiteSpace(EntriesWeekStart))
        {
            EntriesWeekStart = !string.IsNullOrWhiteSpace(WeekStart)
                ? WeekStart
                : GetCurrentWeekStart().ToString("yyyy-MM-dd");
        }
        
        EnsureCurrentWeek();
        StartResetTimer();
    }

    public void Save()
    {
        ScheduleStore store = new()
        {
            Entries          = ScheduleEntries,
            MessageId        = PublishedMessageId,
            ChannelId        = PublishedChannelId,
            WeekStart        = WeekStart,
            EntriesWeekStart = EntriesWeekStart
        };

        string json = JsonConvert.SerializeObject(store, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public ScheduleEntry? GetEntry(ulong id)
    {
        return ScheduleEntries.FirstOrDefault(x => x.Id == id);
    }

    public void AddEntry(ScheduleEntry entry)
    {
        // Stamp the entries-week if it's somehow unset (e.g. list was empty going in)
        if (string.IsNullOrWhiteSpace(EntriesWeekStart))
            EntriesWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");

        ScheduleEntries.Add(entry);
        Save();
    }

    public void RemoveEntry(ulong id)
    {
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

    public event Func<Task>? OnWeekReset;
    
    public void ClearPublished()
    {
        PublishedMessageId = 0;
        PublishedChannelId = 0;
        WeekStart          = "";
        EntriesWeekStart   = GetCurrentWeekStart().ToString("yyyy-MM-dd");
        ScheduleEntries.Clear();
        Save();
        
        OnWeekReset?.Invoke();
    }

    // Returns true if a message is published for the current week
    public bool IsPublishedThisWeek()
    {
        
        if (PublishedMessageId == 0 || string.IsNullOrWhiteSpace(WeekStart))
            return false;
        
        string currentWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");
        return WeekStart == currentWeekStart;
    }

    // Monday of the current UTC week
    public static DateTimeOffset GetCurrentWeekStart()
    {
        DateTime now = DateTime.Now;
        int daysFromMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateTime weekStart = now.AddDays(-daysFromMonday).Date;
        return new DateTimeOffset(weekStart);
    }
    
    void StartResetTimer()
    {
        DateTime now          = DateTime.Now;
        DateTime nextReset    = GetNextResetTime(now);
        TimeSpan initialDelay = nextReset - now;

        _resetTimer = new Timer(_ =>
        {
            Logger.Log("[Info] Scheduled week reset triggered");
            EnsureCurrentWeek();

            // Reschedule for next week
            TimeSpan nextInterval = TimeSpan.FromDays(7);
            _resetTimer.Change(nextInterval, TimeSpan.FromDays(7));
        }, null, initialDelay, TimeSpan.FromDays(7));

        Logger.Log($"[Info] Reset timer scheduled — next reset at {nextReset:f}");
    }
    
    static DateTime GetNextResetTime(DateTime from)
    {
        DateTime candidate = from.Date
            .AddDays(((int)ResetDay - (int)from.DayOfWeek + 7) % 7)
            .AddHours(ResetHour)
            .AddMinutes(ResetMinute);

        // If that time has already passed this week, jump to next week
        if (candidate <= from)
            candidate = candidate.AddDays(7);

        return candidate;
    }
    
    public void EnsureCurrentWeek()
    {
        string currentWeekStart = GetCurrentWeekStart().ToString("yyyy-MM-dd");

        // No entries-week tracked yet — stamp it and bail, nothing to clear
        if (string.IsNullOrWhiteSpace(EntriesWeekStart))
        {
            EntriesWeekStart = currentWeekStart;
            Save();
            return;
        }

        // Entries belong to the current week → nothing to do
        if (EntriesWeekStart == currentWeekStart)
            return;

        Logger.Log("[Info] Week rollover detected — clearing previous schedule");

        ClearPublished();
    }
}

// Wrapper so we can store entries + metadata in one JSON object
public class ScheduleStore
{
    public List<ScheduleEntry> Entries          { get; set; } = new();
    public ulong MessageId                      { get; set; } = 0;
    public ulong ChannelId                      { get; set; } = 0;
    public string WeekStart                     { get; set; } = "";
    public string EntriesWeekStart              { get; set; } = "";
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