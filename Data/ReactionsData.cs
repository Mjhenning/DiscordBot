namespace DiscordBot.Data;
using Newtonsoft.Json;
public class ReactionsData
{
    const string FilePath = "Data/reactions.json";
    
    public List<ReactionEntry> ReactionMessages { get; private set; } = new();

    public ReactionsData()
    {
        Initialize();
    }
    
    void Initialize() // on ReactionsData create
    {
        if (!File.Exists(FilePath)) //check if file doesn't exist and create file + save data
        {
            ReactionMessages = new();
            Save();
            return;
        }
        
        string json = File.ReadAllText(FilePath); //else json = readalltext in file

        ReactionMessages = JsonConvert.DeserializeObject<List<ReactionEntry>>(json) ?? new List<ReactionEntry>(); //list of reactentry gets populated by deserialized json
    }

    public void Save() //on save searlize json string and write to file
    {
        string json = JsonConvert.SerializeObject(ReactionMessages, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public ReactionEntry? GetEntry(ulong messageId, string emoji) //used to retrieve an entry based on message connected to and emoji
    {
        
        return ReactionMessages.FirstOrDefault(x =>
            x.Message == messageId &&
            x.Emoji == emoji
        );
    }

    public void AddEntry(ReactionEntry entry) //adds an entry and saves json
    {
        ReactionMessages.Add(entry);
        Save();
    }
    
    public void RemoveEntry(ulong messageId, string emoji) //removes an entry and saves json
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