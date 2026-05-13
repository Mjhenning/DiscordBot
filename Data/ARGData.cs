namespace DiscordBot.Data;

public class ArgTerminalSession
{
    public string UIMessageID { get; set; }
    public string ContentMessageID { get; set; }
    public string Cwd { get; set; }
    public string LastAction { get; set; }
    
    public List<string> ActionHistory { get; set; }
}

public class ARGReadSession
{
    
}