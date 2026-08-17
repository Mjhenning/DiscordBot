using DiscordBot.Models;
using Newtonsoft.Json;

namespace DiscordBot.Data;

public class CollabData
{
     const string FilePath = "Data/collabs.json";

    public List<CollabEntry> Collabs { get;  set; } = new();

    public CollabData()
    {
        Load();
    }

     void Load()
    {
        if (!File.Exists(FilePath))
        {
            Directory.CreateDirectory("Data");
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);

        Collabs =
            JsonConvert.DeserializeObject<List<CollabEntry>>(json)
            ?? new();
    }

    public void Save()
    {
        string json = JsonConvert.SerializeObject(
            Collabs,
            Formatting.Indented);

        File.WriteAllText(FilePath, json);
    }

    public void Add(CollabEntry entry)
    {
        Collabs.Add(entry);
        Save();
    }

    public void Update(CollabEntry entry)
    {
        int index = Collabs.FindIndex(x => x.Id == entry.Id);

        if (index == -1)
            return;

        Collabs[index] = entry;

        Save();
    }

    public void Remove(ulong id)
    {
        Collabs.RemoveAll(x => x.Id == id);

        Save();
    }

    public CollabEntry? Get(ulong id)
    {
        return Collabs.FirstOrDefault(x => x.Id == id);
    }

    public IEnumerable<CollabEntry> Confirmed()
    {
        return Collabs.Where(x =>
            x.Participants.Any(p =>
                p.UserId != x.OwnerId &&
                p.Status == ParticipantStatus.Accepted));
    }
    
    public IEnumerable<CollabEntry> GetFoxCollabs(ulong foxId)
    {
        return Confirmed().Where(x =>
            x.OwnerId == foxId ||
            x.Participants.Any(p =>
                p.UserId == foxId &&
                p.Status == ParticipantStatus.Accepted));
    }
}