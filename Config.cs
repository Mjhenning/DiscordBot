namespace DiscordBot;

public static class Config
{
    // -----------------------------
    // Discord
    // -----------------------------
    public const string BotToken = "OTU4Nzg0NDM3NTg2MzYyNDY4.GXcn9i.5i96_vW_2rbGNU0keiIvDPB3fBZ_FryHPU_iKs";
    public const ulong GuildId = 1487638187764351219;

    // -----------------------------
    // Twitch Integration
    // -----------------------------
    public const string TwitchClientId = "7effvglei7e59m6u5mx9ri7ubxuw56";
    public const string TwitchClientSecret = "6ozfvadu9ug7ycd7d3bn2fq4lvvbo8";
    
    public const string TwitchChannelName = "F0XTA1L";
    public const string TwitchUserId = "202188873";

    public const string TwitchChannelUrl = "https://www.twitch.tv/f0xta1l";
    
    // Channels / IDs

    public const ulong TwitchNotifyChannelId = 1487776641248792778;
    public const ulong SuggestionChannelId = 1504438238058905732;
    public const ulong WelcomeChannelId = 1487775542106390599;
    public const ulong FavouritesNotifyChannelId = 1502655175809171648;
    public const ulong ModLogChannelId = 1522619256804737064;
    
    public const ulong AutoRoleId = 1487777765003497482;
    public const ulong TwitchNotifyRoleId = 1516099121474306148;
    public const ulong ScheduleNotiRoleId = 1516085795738353836;
    public const ulong LiveGuestRoleId = 1517654897615568987;
    
    //Rewards
    
    //public const string BroadcasterRefreshToken = "txymqu9mmqlb1cn0alfmma7q5uqalcu5rvg2uy8mcbvx6yojeo";
    public const string SuggestRewardId = "f379d2f4-202d-48fd-929a-bb830a2f3a32";
    public const string QuoteRewardId = "5e6b9d0d-6ae6-4f98-91bc-f9c925ab1ae6";
    
    //Welcome messages

    public static readonly string[] WelcomeMessages =
    [
        "New connection detected… welcome {user}. The proxy feels a little less quiet now 🔆",
        "{user} has connected to {server}. Stability increasing…✨",
        "Incoming connection: {user}. Environment holding steady — welcome 🫧",
        "Connection established with {user}. System activity increasing…🔋",
        "Connection established. Welcome, {user} 🦊",
        "Incoming connection accepted: {user} 🦊💬",
        "{user} is now part of the system. Monitoring connection... 🟢",
        "{user} connected successfully. Active connections increasing 🟢",
        "{user} has joined... Fox should be happy 🫧"
    ];

    // -----------------------------
    // Data Files
    // -----------------------------
    public const string ReactionRolesFile = "Data/reaction_roles.json";
    public const string ScheduleFile = "Data/schedule.json";
    public const string QuoteDirectory ="../../Overlay/Scripts";
}