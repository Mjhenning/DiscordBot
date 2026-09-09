// FavouritesData.cs
// drives Data/favourites.json: the curated list of favourite streamers shared
// between the Discord bot (go-live embeds) and the Twitch bot (first-chat shoutouts).

namespace DiscordBot.Data;
using Newtonsoft.Json;

public class FavouritesData
{
    const string FilePath = "Data/favourites.json";

    // usernames seeded on first run, edited afterwards by hand or future commands
    static readonly Dictionary<string, string> DefaultFavourites = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Siigynn",   "Fox's favourite matcha obsessed herbalist is live!! Whether it's {game} or karaoke, she's always a blast to have around! 💚" },
        { "its_livinabox", "Go catch our favourite australian goober Livy, whether it's {game} or anything else, there's always a giggle to be shared! 🩷" },
        { "InnocentOfSin", "Definitely not a cult, but brother can this owl yap! 🧡 Go checkout the amazing sin and his sussy but lovely community!" },
        { "BaxxyCH", "Go catch our lovely family from next door, the baxxidents!!! 💜 Make sure to keep up with their chaotic energy on {game}!" },
        { "LaeliaTheCat", "Fox's favourite chef star kitty is live with {game}!!! 🌟 Make sure to go pop in and say hi!!" },
        { "Juliuskat", "Our Finnish Feline from next door is live with {game}! Go show the katpack some love! 🤍" },
        { "violenciakurayami", "Fox's favourite sharkie is busy with {game}, go say hi to our bubbly family, the fishies! 🫧" },
        { "pathetic_softpaw", "You love art right?! Go check out fox's favourite vibe artist, paw paw! Maybe she's up to {game} and not art this time? 🩷" },
        { "Silbers_", "Scug?! The only one I know is Sticky! Go check out this amazing scug and her community (maybe drop a wawa in chat) whether it's {game} or something else!🩶" }
    };

    // username -> live notification message template
    public Dictionary<string, string> Entries { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public FavouritesData()
    {
        Initialize();
    }

    void Initialize() // runs on construction
    {
        if (!File.Exists(FilePath)) // first run, seed the curated default list
        {
            Entries = new Dictionary<string, string>(DefaultFavourites, StringComparer.OrdinalIgnoreCase);
            Save();
            return;
        }

        string json = File.ReadAllText(FilePath);
        Entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save() // serialize entries and write to file
    {
        string json = JsonConvert.SerializeObject(Entries, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    // case-insensitive lookup, Twitch logins come through lowercase
    public bool Contains(string username) => Entries.ContainsKey(username);
}