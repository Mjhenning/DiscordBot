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
            ctx.Log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [Warning] QuoteRedeem: invalid input from {ctx.UserName} — \"{ctx.UserInput}\"");
            return;
        }

        try
        {
            QuoteEntry entry = Parse(ctx.UserInput, ctx.UserName);
            await AppendToJson(entry);
            ctx.Log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [Info] Quote saved from {ctx.UserName}: \"{entry.Quote}\"");
        }
        catch (Exception ex)
        {
            ctx.Log.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [Error] QuoteRedeem.Handle failed: {ex.Message}");
        }
    }

    static bool IsValidInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (!input.Contains('-')) return false;

        int dash    = input.IndexOf('-');
        string left  = input[..dash].Trim();
        string right = input[(dash + 1)..].Trim();

        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right);
    }

    static QuoteEntry Parse(string input, string submitter)
    {
        int dash     = input.IndexOf('-');
        string left  = input[..dash].Trim();
        string right = input[(dash + 1)..].Trim();

        string quotePart;
        string metaPart;

        if (left.Contains(',') && !right.Contains(','))
        {
            // "Source, Year - Quote"
            metaPart  = left;
            quotePart = right;
        }
        else
        {
            // "Quote - Source, Year" or "Quote - Source"
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