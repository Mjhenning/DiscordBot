using DiscordBot.Models;

namespace DiscordBot.Services;

public class CollabRequestCache
{
     readonly Dictionary<ulong, PendingCollabRequest> _pending = new();

    public void Add(PendingCollabRequest request)
    {
        _pending[request.Id] = request;
    }

    public PendingCollabRequest? Get(ulong id)
    {
        _pending.TryGetValue(id, out var request);
        return request;
    }

    public void Remove(ulong id)
    {
        _pending.Remove(id);
    }
}