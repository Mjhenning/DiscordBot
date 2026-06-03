using Discord.WebSocket;

namespace DiscordBot.Redeems;

public record RedemptionContext(
    string              UserName,
    string              UserInput,
    string              AvatarUrl,
    string              UserUrl,
    DiscordSocketClient Discord
);