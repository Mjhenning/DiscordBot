using Newtonsoft.Json;

namespace DiscordBot.Data;

// tracks which messages are pending todo capture
// persists message ids to Data/todoCapture.json so restart keeps state
public class TodoCaptureData
{
    const string FilePath = "Data/todoCapture.json";

    HashSet<ulong> _tracked = new();

    public void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);
        var ids = JsonConvert.DeserializeObject<List<ulong>>(json) ?? new List<ulong>();
        _tracked = new HashSet<ulong>(ids);
    }

    public bool IsTracked(ulong messageId) => _tracked.Contains(messageId);

    public void Track(ulong messageId)
    {
        _tracked.Add(messageId);
        Save();
    }

    public void Untrack(ulong messageId)
    {
        _tracked.Remove(messageId);
        Save();
    }

    void Save()
    {
        string json = JsonConvert.SerializeObject(_tracked.ToList(), Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }
}
