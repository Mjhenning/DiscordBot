using DiscordBot.Data;

namespace DiscordBot.Services;

public class HandshakeOutcome
{
    public string Type { get; init; } = "";
    public string Message { get; init; } = "";
    public int DisplayAmount { get; init; }
    public int NewBalance { get; init; }
    public int CacheBalance { get; init; }
}

public class HandshakeService
{
    readonly LinkedAccountsData _linked;

    readonly string _cachePath;

    static readonly Random _rng = new();

    readonly record struct OutcomeDef(string Type, int Weight, int Multiplier, string[] Messages);

    static readonly OutcomeDef[] Outcomes =
    {
        new("accepted",  35, 2,  new[]
        {
            "Node accepted {user} handshake. Packets returned doubled.",
            "Connection established. Node amplified {user} signal. +{amount} Glossels.",
            "Handshake successful. Network node returned {user} data with interest."
        }),
        new("unstable",  30, 1,  new[]
        {
            "Signal unstable. Packets retained, no change.",
            "Node acknowledged but refused to route. Glossels unchanged.",
            "Connection flickered. Data returned as sent. No loss, no gain."
        }),
        new("rejected",  25, 0,  new[]
        {
            "Node rejected transmission. Packets corrupted. -{amount} Glossels.",
            "Handshake failed. Network firewall severed {user} connection. Data lost.",
            "Unknown node dropped {user} signal. Glossels absorbed into the void."
        }),
        new("amplified",  8, 3,  new[]
        {
            ">> UNKNOWN NODE AMPLIFYING SIGNAL. 3x recovery. +{amount} Glossels.",
            ">> CRITICAL: Node running unknown protocol. Packets tripled. This should not be possible.",
            ">> ANOMALY DETECTED. Node returned 3x {user} original transmission."
        }),
        new("captured",   2, -1, new[]
        {
            "Node partially captured {user} packets. Half recovered. -{amount} Glossels.",
            "WARNING: Intercepted mid-transfer. Partial data salvage.",
            "Hostile node detected. Packet capture partial. What remains has been returned."
        }),
        new("drained",    1, 1,  new[]
        {
            ">> NETWORK CACHE DRAINED. All buffered packets recovered. +{amount} Glossels.",
            ">> CENTRAL CACHE SIPHONED. {user} retrieved every lost packet from the buffer.",
            ">> CACHE BREACH. Buffered data extracted. {user} recovered {amount} Glossels from the network cache."
        })
    };

    public HandshakeService(LinkedAccountsData linked)
    {
        _linked = linked;

        _cachePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "TwitchBot", "data", "network_cache.json"));
    }

    public HandshakeOutcome SoloGamble(ulong discordUserId, string username, int amount)
    {
        var entry = _linked.FindByDiscordId(discordUserId);
        if (entry == null)
            return new HandshakeOutcome { Type = "error", Message = "No linked account found." };

        if (amount <= 0)
            return new HandshakeOutcome { Type = "error", Message = "Invalid amount." };

        if (entry.Amount < amount)
            return new HandshakeOutcome { Type = "error", Message = $"Insufficient Glossels. Balance: {entry.Amount}" };

        var outcome = RollOutcome();
        string msgTemplate = outcome.Messages[_rng.Next(outcome.Messages.Length)];
        int displayAmount;
        int cacheBalance = ReadCache();

        if (outcome.Type == "drained")
        {
            int cacheDrained = cacheBalance;
            _linked.AddAmountByDiscordId(discordUserId, amount + cacheDrained);
            WriteCache(0);
            displayAmount = amount + cacheDrained;
        }
        else if (outcome.Type == "captured")
        {
            int lost = Math.Max(1, (int)Math.Ceiling(amount * 0.5));
            _linked.AddAmountByDiscordId(discordUserId, -lost);
            WriteCache(cacheBalance + lost);
            displayAmount = lost;
        }
        else if (outcome.Multiplier > 1)
        {
            int returnAmount = (int)Math.Floor(amount * (double)outcome.Multiplier);
            _linked.AddAmountByDiscordId(discordUserId, -amount);
            _linked.AddAmountByDiscordId(discordUserId, returnAmount);
            displayAmount = returnAmount - amount;
        }
        else if (outcome.Multiplier == 0)
        {
            _linked.AddAmountByDiscordId(discordUserId, -amount);
            WriteCache(cacheBalance + amount);
            displayAmount = amount;
        }
        else
        {
            displayAmount = 0;
        }

        string flavor = msgTemplate
            .Replace("{amount}", displayAmount.ToString())
            .Replace("{user}", username);

        int newBalance = _linked.FindByDiscordId(discordUserId)?.Amount ?? 0;

        return new HandshakeOutcome
        {
            Type = outcome.Type,
            Message = flavor,
            DisplayAmount = displayAmount,
            NewBalance = newBalance,
            CacheBalance = ReadCache()
        };
    }

    public int GetBalance(ulong discordUserId)
    {
        return _linked.FindByDiscordId(discordUserId)?.Amount ?? 0;
    }

    public class TransferOutcome
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int FromBalance { get; init; }
        public int ToBalance { get; init; }
        public string ToName { get; init; } = "";
    }

    // Transfers Glossels between two linked Discord users. Atomic: the
    // recipient is credited and the sender debited together, so both
    // sides can never drift out of sync.
    public TransferOutcome Transfer(ulong fromDiscordId, ulong toDiscordId, string toUsername, int amount)
    {
        if (amount <= 0)
            return new TransferOutcome { Message = "Invalid amount." };

        var from = _linked.FindByDiscordId(fromDiscordId);
        if (from == null)
            return new TransferOutcome { Message = "No linked account found." };

        if (from.Amount < amount)
            return new TransferOutcome
            {
                Message = $"Insufficient Glossels. Balance: {from.Amount}"
            };

        if (_linked.TransferAmountByDiscordId(fromDiscordId, toDiscordId, amount))
            return new TransferOutcome
            {
                Success = true,
                FromBalance = _linked.FindByDiscordId(fromDiscordId)?.Amount ?? 0,
                ToBalance = _linked.FindByDiscordId(toDiscordId)?.Amount ?? amount,
                ToName = toUsername,
                Message = ""
            };

        return new TransferOutcome { Message = "Recipient has no linked account." };
    }

    public bool IsLinked(ulong discordUserId)
    {
        return _linked.FindByDiscordId(discordUserId) != null;
    }

    public int GetCacheBalance() => ReadCache();

    OutcomeDef RollOutcome()
    {
        int totalWeight = Outcomes.Sum(o => o.Weight);
        int roll = _rng.Next(totalWeight);

        foreach (var outcome in Outcomes)
        {
            if (roll < outcome.Weight)
                return outcome;
            roll -= outcome.Weight;
        }

        return Outcomes[0];
    }

    int ReadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                string json = File.ReadAllText(_cachePath);
                var cache = Newtonsoft.Json.JsonConvert.DeserializeObject<CacheStore>(json);
                return cache?.Balance ?? 0;
            }
        }
        catch { }
        return 0;
    }

    void WriteCache(int balance)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_cachePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                new CacheStore { Balance = balance }, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_cachePath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"[HandshakeService] Failed to write cache: {ex.Message}");
        }
    }

    class CacheStore
    {
        public int Balance { get; set; }
    }
}
