using Discord;
using Discord.WebSocket;

namespace DiscordBot.Redeems;

public static class SuggestionRedeem
{
    public static async Task Handle(RedemptionContext ctx)
    {
        ITextChannel? channel = ctx.Discord.GetChannel(Config.SuggestionChannelId) as ITextChannel;
        if (channel == null)
        {
            Logger.Log("[Warning] SuggestionRedeem: could not find channel");
            return;
        }

        MessageComponent components = new ComponentBuilder()
            .WithButton("Mark Complete", "suggestion_complete", ButtonStyle.Success)
            .Build();

        Embed embed = new EmbedBuilder()
            .WithAuthor(ctx.UserName, ctx.AvatarUrl, ctx.UserUrl)
            .WithTitle("Suggestion / Request module activated...")
            .WithColor(new Color(0x6441a5))
            .WithDescription(ctx.UserInput)
            .WithFooter("System Active • 4/30/03, 3:00 AM")
            .Build();

        IUserMessage posted = await channel.SendMessageAsync(embed: embed, components: components);
        Logger.Log($"[Info] Suggestion published to #{channel.Name} (msg: {posted.Id})");
    }
}