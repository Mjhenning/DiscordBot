using Newtonsoft.Json;

namespace DiscordBot.Redeems;

public static class QuoteRedeem
{
    public record QuoteEntry(
        string Quote,
        string Source,
        string Year,
        string Submitter
    );

    public static async Task Handle(RedemptionContext ctx)
    {
        if (!IsValidInput(ctx.UserInput))
        {
            Logger.Log($"[Warning] QuoteRedeem: invalid input from {ctx.UserName} — \"{ctx.UserInput}\"");
            return;
        }

        try
        {
            QuoteEntry entry = Parse(ctx.UserInput, ctx.UserName);
            await AppendToJson(entry);
            Logger.Log($"[Info] Quote saved from {ctx.UserName}: \"{entry.Quote}\"");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Error] QuoteRedeem.Handle failed: {ex.Message}");
        }
    }

    static bool IsValidInput(string input)
    {
        return !string.IsNullOrWhiteSpace(input);
    }

    static QuoteEntry Parse(string input, string submitter)
    {
        input = input.Replace("\"", "");
        
        if (!input.Contains('-'))
            return new QuoteEntry(input.Trim(), "", "", submitter);

        int dash     = input.IndexOf('-');
        string left  = input[..dash].Trim();
        string right = input[(dash + 1)..].Trim();

        string quotePart;
        string metaPart;

        if (left.Contains(',') && !right.Contains(','))
        {
            metaPart  = left;
            quotePart = right;
        }
        else
        {
            quotePart = left;
            metaPart  = right;
        }

        string source = "";
        string year   = "";

        if (metaPart.Contains(','))
        {
            int comma = metaPart.LastIndexOf(',');
            source    = metaPart[..comma].Trim();
            year      = metaPart[(comma + 1)..].Trim();
        }
        else
        {
            source = metaPart.Trim();
        }

        return new QuoteEntry(quotePart, source, year, submitter);
    }

    static async Task AppendToJson(QuoteEntry entry)
    {
        string path = Path.Combine(Config.QuoteDirectory, "quotes.json");
        List<QuoteEntry> quotes = new();

        if (File.Exists(path))
        {
            string existing = await File.ReadAllTextAsync(path);
            quotes = JsonConvert.DeserializeObject<List<QuoteEntry>>(existing) ?? new();
        }

        quotes.Add(entry);

        string json = JsonConvert.SerializeObject(quotes, Formatting.Indented);
        await File.WriteAllTextAsync(path, json);
    }
}