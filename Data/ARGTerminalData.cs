using Newtonsoft.Json;

namespace DiscordBot.Data;

public enum ARGEmbed_Type
{
    Terminal,
    ReadOutput
}

public class ArgTerminalData
{
    private const string FilePath = "Data/argData.json";
    
    const string StateFilePath = "/home/mjhenning/OBS/Twitch_Bot/ARG/data/state.json";
    public ulong PublishedChannelId  { get; private set; } = 0;
    public ulong PublishedTMessageId  { get; private set; } = 0;
    public ulong PublishedRMessageId  { get; set; } = 0;

    public string Cwd { get; set; } = "";
    public string ReadMessageFile{ get; set; } = "";
    public string ReadMessageContent { get; set; } = "";

    public string LastAction { get; set; } = "";
    
    public int activeUsers = 0;
    
    public HashSet<ulong> LoggedInUsers { get; private set; } = new();

    public List<string> ActionHistory { get; set; } = new List<string>();
    
    
    public ArgTerminalData() => Initialize();
    
    void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);
        TerminalStore? store = JsonConvert.DeserializeObject<TerminalStore>(json);
        

        if (store != null)
        {
            PublishedTMessageId = store.MessageId;
            PublishedChannelId = store.ChannelId;
            PublishedRMessageId = store.ContentMessageId;
            
            Cwd = store.Cwd;
            ReadMessageFile = store.ReadMessageFile;
            ReadMessageContent = store.ReadMessage;
            LastAction = store.LastAction;

            ActionHistory = store.ActionHistory ?? new();
        }
    }
    
    public void SetPublished(ulong messageId, ulong channelId, ARGEmbed_Type type)
    {
        if (PublishedChannelId == 0) PublishedChannelId = channelId;

        switch (type)
        {
            case ARGEmbed_Type.Terminal:
                PublishedTMessageId = messageId;
                break;
            case ARGEmbed_Type.ReadOutput:
                PublishedRMessageId = messageId;
                break;
        }
        Save();
    }
    
    public void Save()
    {
        TerminalStore store = new()
        {
            MessageId = PublishedTMessageId,
            ChannelId = PublishedChannelId,
            ContentMessageId = PublishedRMessageId,
            
            Cwd = Cwd,
            ReadMessage = ReadMessageContent,
            LastAction = LastAction,
            
            ActionHistory = ActionHistory
        };

        string json = JsonConvert.SerializeObject(store, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public int GetCoherence()
    {
        try
        {
            string json = File.ReadAllText(StateFilePath);
            dynamic state = JsonConvert.DeserializeObject<dynamic>(json)!;
            return (int)(state.coherence ?? 0);
        }
        catch
        {
            return 0;
        }
    }
    
    public bool Login(ulong userId)
    {
        if (LoggedInUsers.Contains(userId)) return false;
        LoggedInUsers.Add(userId);
        activeUsers = LoggedInUsers.Count;
        return true;
    }

    public bool Logout(ulong userId)
    {
        if (!LoggedInUsers.Contains(userId)) return false;
        LoggedInUsers.Remove(userId);
        activeUsers = LoggedInUsers.Count;
        return true;
    }

}

public class TerminalStore
{
    public ulong ChannelId  { get; set; } = 0;
    public ulong MessageId  { get; set; } = 0;
    public ulong ContentMessageId { get; set; } = 0;

    public string Cwd { get; set; } = "";
    public string ReadMessageFile { get; set; } = "";
    public string ReadMessage { get; set; } = "";
    
    public string LastAction { get; set; }
    
    public List<string> ActionHistory { get; set; }
}