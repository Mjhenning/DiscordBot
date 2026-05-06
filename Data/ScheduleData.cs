namespace DiscordBot.Data;
using Newtonsoft.Json;

public class ScheduleData
{
    const string FilePath = "Data/schedule.json";

    public List<ScheduleEntry> ScheduleEntries { get; private set; } = new();

    public ScheduleData()
    {
        Initialize();
    }

    void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            ScheduleEntries = new();
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);
        ScheduleEntries = JsonConvert.DeserializeObject<List<ScheduleEntry>>(json) ?? new();
    }

    public void Save()
    {
        string json = JsonConvert.SerializeObject(ScheduleEntries, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public ScheduleEntry? GetEntry(ulong id)
    {
        return ScheduleEntries.FirstOrDefault(x => x.Id == id);
    }

    public void AddEntry(ScheduleEntry entry)
    {
        ScheduleEntries.Add(entry);
        Save();
    }

    public void RemoveEntry(ulong id)
    {
        ScheduleEntries.RemoveAll(x => x.Id == id);
        Save();
    }
}

public class ScheduleEntry
{
    public ulong Id           { get; set; } = 0;  // message ID or your own generated ID
    public string Description { get; set; } = "";
    public string ScheduledAt { get; set; } = "";  // stored as UTC ISO 8601

    // Convenience property — not serialized, just for use in code
    [JsonIgnore]
    public DateTimeOffset ScheduledAtParsed => DateTimeOffset.Parse(ScheduledAt);

    // Convenience property for display
    [JsonIgnore]
    public string ScheduledAtDisplay => ScheduledAtParsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}