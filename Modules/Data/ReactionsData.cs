namespace DiscordBot.Data;
using Newtonsoft.Json;
public class ReactionsData
{
    const string FilePath = "Data/reactions.json";
    
    public List<ReactionEntry> ReactionMessages { get;  set; } = new();

    public ReactionsData()
    {
        Initialize();
    }
    
    void Initialize() // runs on construction
    {
        if (!File.Exists(FilePath)) // first run, no stored entries
        {
            ReactionMessages = new();
            Save();
            return;
        }
        
        string json = File.ReadAllText(FilePath); // load existing entries

        ReactionMessages = JsonConvert.DeserializeObject<List<ReactionEntry>>(json) ?? new List<ReactionEntry>(); // deserialize stored entries
    }

    public void Save() // serialize entries and write to file
    {
        string json = JsonConvert.SerializeObject(ReactionMessages, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public ReactionEntry? GetEntry(ulong messageId, string emoji) // find an entry by message id and emoji
    {
        
        return ReactionMessages.FirstOrDefault(x =>
            x.Message == messageId &&
            x.Emoji == emoji
        );
    }

    public void AddEntry(ReactionEntry entry) // add an entry and save
    {
        ReactionMessages.Add(entry);
        Save();
    }
    
    public void RemoveEntry(ulong messageId, string emoji) // remove an entry and save
    {
        ReactionMessages.RemoveAll(x => 
        x.Message == messageId &&
        x.Emoji == emoji
        );
        
        Save();
    }
}

public class ReactionEntry
{
    public ulong Message { get; set; }
    public ulong Channel { get; set; }

    public string Emoji { get; set; } = "";

    public List<ulong> RolesToAdd { get; set; } = new();
    public List<ulong> RolesToRemove { get; set; } = new();
}