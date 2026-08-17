using Newtonsoft.Json;

namespace DiscordBot.Data;

public enum ARGEmbed_Type
{
    Logs,
    Terminal,
    ReadOutput
}

public enum TerminalInteractionMode
{
    None,
    Navigating,
    Reading
}

public class ArgTerminalData
{
    const string FilePath = "Data/argData.json";
    
    public readonly string StateFilePath =
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "TwitchBot",
                "ARG",
                "data",
                "state.json"));
    public ulong PublishedChannelId  { get;  set; } = 0;
    public ulong PublishedTMessageId  { get;  set; } = 0;
    public ulong PublishedRMessageId  { get; set; } = 0;
    public ulong PublishedLMessageId  { get; set; } = 0;

    public string Cwd { get; set; } = "/";
    public string ReadMessageFile{ get; set; } = "";
    public string ReadMessageContent { get; set; } = "";
    
    public int activeUsers = 0;
    
    public HashSet<ulong> LoggedInUsers { get;  set; } = new();

    public Queue<string> ActionHistory { get; set; } = new Queue<string>();
    
    public TerminalInteractionMode InteractionMode { get; set; }
    
    
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
            PublishedLMessageId = store.LogsMessageId;
            PublishedTMessageId = store.TerminalMessageId;
            PublishedChannelId = store.ChannelId;
            PublishedRMessageId = store.ReadMessageId;
            
            Cwd = store.Cwd;
            ReadMessageFile = store.ReadMessageFile;
            ReadMessageContent = store.ReadMessage;

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
            case ARGEmbed_Type.Logs:
                PublishedLMessageId = messageId;
                break;
        }
        Save();
    }
    
    public void Save()
    {
        TerminalStore store = new()
        {
            LogsMessageId = PublishedLMessageId,
            TerminalMessageId = PublishedTMessageId,
            ChannelId = PublishedChannelId,
            ReadMessageId = PublishedRMessageId,
            
            Cwd = Cwd,
            ReadMessageFile = ReadMessageFile,
            ReadMessage = ReadMessageContent,
            
            ActionHistory = ActionHistory
        };

        string json = JsonConvert.SerializeObject(store, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }
    
    // ─── HELPERS ────────────────────────────────────────────────────

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
    
    public int BumpCoherence(int amount)
    {
        if (amount == 0) return 0;

        string json   = File.ReadAllText(StateFilePath);
        dynamic state = JsonConvert.DeserializeObject<dynamic>(json)!;
        int current   = (int)(state.coherence ?? 0);
        int updated   = Math.Min(100, current + amount);
        state.coherence = updated;
    
        string written = JsonConvert.SerializeObject(state, Formatting.Indented);
        Logger.Log($"[ARG] Writing coherence: {written}"); // temp debug
        File.WriteAllText(StateFilePath, written);
        return updated;
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
    
    public void AddHistory(string action)
    {
        long unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ActionHistory.Enqueue(action + $" <t:{unixTimestamp}:R>");

        while (ActionHistory.Count > 10)
        {
            ActionHistory.Dequeue();
        }
    }

    public Queue<string> GetHistory()
    {
        return ActionHistory;
    }
    
    public void Reload()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            string json = File.ReadAllText(FilePath);
            TerminalStore? store =
                JsonConvert.DeserializeObject<TerminalStore>(json);

            if (store == null)
                return;

            PublishedLMessageId = store.LogsMessageId;
            PublishedTMessageId = store.TerminalMessageId;
            PublishedChannelId  = store.ChannelId;
            PublishedRMessageId = store.ReadMessageId;

            Cwd = store.Cwd ?? "/";
            ReadMessageFile = store.ReadMessageFile ?? "";
            ReadMessageContent = store.ReadMessage ?? "";

            ActionHistory = store.ActionHistory ?? new Queue<string>();

            // keep runtime-only state intact unless you want resets:
            activeUsers = LoggedInUsers.Count;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ARG Reload Error] {ex.Message}");
        }
    }
}

public class TerminalStore
{
    public ulong ChannelId  { get; set; } = 0;
    public ulong TerminalMessageId { get; set; } = 0;
    public ulong ReadMessageId { get; set; } = 0;
    public ulong LogsMessageId { get; set; } = 0;

    public string Cwd { get; set; } = "/";
    public string ReadMessageFile { get; set; } = "";
    public string ReadMessage { get; set; } = "";
    
    public Queue<string> ActionHistory { get; set; }
}