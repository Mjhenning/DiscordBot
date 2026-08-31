using System.Net.Http.Json;
using System.Text.Json;

namespace DiscordBot.Modules.SevenTv;

// talks to 7TV's GraphQL v4 API.
public static class SevenTvApi
{
     const string GraphQlEndpoint = "https://7tv.io/v4/gql";
     static readonly HttpClient Http = new();

    //-------------GRAPHQL QUERIES--------------

     const string EmoteInSetQuery = @"
      query getEmoteInEmoteSet($setId:Id!, $emoteName:String){
        emoteSets{
          emoteSet(id:$setId){
            emotes(query:$emoteName){
              items{
                id
                emote{
                  id
                  defaultName
                  scores{ topAllTime }
                  images{ url size mime }
                }
              }
            }
          }
        }
      }";

     const string EmoteQuery = @"
      query GetEmoteByName($emoteName:String){
        emotes{
          search(query:$emoteName, sort:{sortBy:TOP_ALL_TIME, order:DESCENDING}){
            items{
              id
              defaultName
              ownerId
              scores{ topAllTime }
              images{ url size mime }
            }
          }
        }
      }";

     const string ChannelQuery = @"
      query GetChannel($channelName:String!){
        users{
          search(query:$channelName){
            items{
              id
              mainConnection{ platform platformId platformUsername platformDisplayName }
            }
          }
        }
      }";

     const string EmoteSetQuery = @"
      query GetEmoteSetByChannelID($userId:Id!){
        users{
          user(id:$userId){
            emoteSets{ id name capacity updatedAt }
          }
        }
      }";

     static async Task<JsonDocument?> PostGraphQlAsync(string query, object variables)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(GraphQlEndpoint, new { query, variables });
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] GraphQL request failed: {ex.Message}");
            return null;
        }
    }

    //---------------------CHANNELS---------------------

    public static async Task<List<SevenTvUser>> SearchChannelsAsync(string channelName)
    {
        var doc = await PostGraphQlAsync(ChannelQuery, new { channelName });
        if (doc == null) return new();

        try
        {
            var items = doc.RootElement.GetProperty("data").GetProperty("users").GetProperty("search").GetProperty("items");
            var results = new List<SevenTvUser>();

            foreach (var item in items.EnumerateArray())
            {
                var user = new SevenTvUser { Id = item.GetProperty("id").GetString() ?? "" };

                if (item.TryGetProperty("mainConnection", out var mc) && mc.ValueKind != JsonValueKind.Null)
                {
                    user.MainConnection = new SevenTvMainConnection
                    {
                        Platform = mc.GetProperty("platform").GetString() ?? "",
                        PlatformId = mc.GetProperty("platformId").GetString() ?? "",
                        PlatformUsername = mc.GetProperty("platformUsername").GetString() ?? "",
                        PlatformDisplayName = mc.GetProperty("platformDisplayName").GetString() ?? ""
                    };
                }

                results.Add(user);
            }

            return results;
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to parse channel search response: {ex.Message}");
            return new();
        }
    }

    //--------------------EMOTE SETS--------------------

    public static async Task<List<SevenTvEmoteSet>> GetUserEmoteSetsAsync(string sevenTvUserId)
    {
        var doc = await PostGraphQlAsync(EmoteSetQuery, new { userId = sevenTvUserId });
        if (doc == null) return new();

        try
        {
            var sets = doc.RootElement.GetProperty("data").GetProperty("users").GetProperty("user").GetProperty("emoteSets");
            var results = new List<SevenTvEmoteSet>();

            foreach (var set in sets.EnumerateArray())
            {
                results.Add(new SevenTvEmoteSet
                {
                    Id = set.GetProperty("id").GetString() ?? "",
                    Name = set.GetProperty("name").GetString() ?? "",
                    Capacity = set.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 0,
                    UpdatedAt = set.TryGetProperty("updatedAt", out var updated)
                                && DateTimeOffset.TryParse(updated.GetString(), out var parsed)
                        ? parsed
                        : DateTimeOffset.MinValue
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to parse emote set response: {ex.Message}");
            return new();
        }
    }

    //----------------------EMOTES----------------------

     static SevenTvEmote ParseEmote(JsonElement el)
    {
        var emote = new SevenTvEmote
        {
            Id = el.GetProperty("id").GetString() ?? "",
            DefaultName = el.GetProperty("defaultName").GetString() ?? ""
        };

        if (el.TryGetProperty("scores", out var scores) && scores.TryGetProperty("topAllTime", out var top))
            emote.Scores.TopAllTime = top.GetInt64();

        if (el.TryGetProperty("images", out var images))
        {
            foreach (var img in images.EnumerateArray())
            {
                emote.Images.Add(new SevenTvImage
                {
                    Url = img.GetProperty("url").GetString() ?? "",
                    Size = img.TryGetProperty("size", out var sz) ? sz.GetInt32() : 0,
                    Mime = img.GetProperty("mime").GetString() ?? ""
                });
            }
        }

        return emote;
    }

     static List<SevenTvEmote> ParseEmoteSearchResponse(JsonDocument doc, bool isSetQuery)
    {
        var data = doc.RootElement.GetProperty("data");
        var results = new List<SevenTvEmote>();

        if (isSetQuery)
        {
            if (!data.TryGetProperty("emoteSets", out var emoteSets)) return results;
            if (!emoteSets.TryGetProperty("emoteSet", out var emoteSet) || emoteSet.ValueKind == JsonValueKind.Null) return results;
            if (!emoteSet.TryGetProperty("emotes", out var emotesContainer)) return results;

            foreach (var item in emotesContainer.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("emote", out var emoteEl))
                    results.Add(ParseEmote(emoteEl));
            }
        }
        else
        {
            if (!data.TryGetProperty("emotes", out var emotes)) return results;
            if (!emotes.TryGetProperty("search", out var search)) return results;

            foreach (var item in search.GetProperty("items").EnumerateArray())
                results.Add(ParseEmote(item));
        }

        return results;
    }

    // resolve a single emote by exact name.
    // checks the user's last autocomplete results first,
    // then falls back to a fresh GraphQL search.
    public static async Task<SevenTvEmote?> SearchEmoteAsync(string emoteName, string? setId, string userId)
    {
        var lastResults = GetLastAutocompleteResults(userId, setId);
        if (lastResults != null)
        {
            var exactMatch = lastResults.Value.Emotes.FirstOrDefault(e => e.DefaultName == emoteName);
            if (exactMatch != null) return exactMatch;
        }

        var isSetQuery = !string.IsNullOrEmpty(setId);
        var doc = await PostGraphQlAsync(
            isSetQuery ? EmoteInSetQuery : EmoteQuery,
            new { setId = setId ?? "", emoteName });

        if (doc == null) return null;

        try
        {
            var emotes = ParseEmoteSearchResponse(doc, isSetQuery);
            return emotes.FirstOrDefault(e => e.DefaultName == emoteName);
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to parse emote search response: {ex.Message}");
            return null;
        }
    }

    // search for emotes matching a partial name, for autocomplete.
    // sorted by popularity (topAllTime), cached 5 min per user+query+set,
    // so repeated keystrokes don't hammer 7TV.
    public static async Task<List<SevenTvEmote>> SearchEmotesForAutocompleteAsync(string partialName, string? setId, string userId)
    {
        var cached = GetCachedAutocompleteSearch(userId, partialName, setId);
        if (cached != null)
        {
            SetLastAutocompleteResults(userId, partialName, cached, setId);
            return cached;
        }

        var isSetQuery = !string.IsNullOrEmpty(setId);
        var doc = await PostGraphQlAsync(
            isSetQuery ? EmoteInSetQuery : EmoteQuery,
            new { setId, emoteName = partialName });

        if (doc == null) return new();

        try
        {
            var emotes = ParseEmoteSearchResponse(doc, isSetQuery)
                .OrderByDescending(e => e.Scores.TopAllTime)
                .ToList();

            if (emotes.Count > 0)
            {
                SetCachedAutocompleteSearch(userId, partialName, emotes, setId);
                SetLastAutocompleteResults(userId, partialName, emotes, setId);
            }

            return emotes;
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to parse autocomplete response: {ex.Message}");
            return new();
        }
    }

    // pick the right image URL/format for an emote at the given size.
    // prefers animated (avif) over static (png).
    public static SevenTvEmoteInfo GetEmoteUrl(SevenTvEmote emote, string size = "2x")
    {
        var filtered = emote.Images
            .Where(img => (img.Mime == "image/avif" || img.Mime == "image/png") && !img.Url.Contains("_static."))
            .ToList();

        var format = filtered.Count > 0 && filtered[0].Mime == "image/avif" ? "avif" : "png";

        var selected = filtered.Where(img => img.Url.Contains($"{size}.{format}")).ToList();

        var final = selected.FirstOrDefault()
            ?? filtered.FirstOrDefault(img => img.Url.Contains($"2x.{format}"))
            ?? filtered.FirstOrDefault();

        return new SevenTvEmoteInfo
        {
            Url = final?.Url ?? "",
            Format = format,
            IsAnimated = final?.Mime == "image/avif",
            Name = emote.DefaultName
        };
    }

    //----------------------CACHES----------------------
    // two caches, one for raw autocomplete search results,
    // keyed by user+query+set, 5 min TTL.
    // one tracking each user's last autocomplete results,
    // so /emote can resolve an exact pick instantly.

     static readonly Dictionary<string, (List<SevenTvEmote> Emotes, DateTime Timestamp)> AutocompleteCache = new();
     static readonly Dictionary<string, (List<SevenTvEmote> Emotes, string Query, DateTime Timestamp)> LastAutocompleteCache = new();
     static readonly object CacheLock = new();
     static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

     static List<SevenTvEmote>? GetCachedAutocompleteSearch(string userId, string query, string? setId)
    {
        var key = $"{userId}:{query.ToLowerInvariant()}:{setId ?? "global"}";
        lock (CacheLock)
        {
            if (AutocompleteCache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.Timestamp < CacheDuration)
                    return entry.Emotes;

                AutocompleteCache.Remove(key);
            }
        }
        return null;
    }

     static void SetCachedAutocompleteSearch(string userId, string query, List<SevenTvEmote> emotes, string? setId)
    {
        var key = $"{userId}:{query.ToLowerInvariant()}:{setId ?? "global"}";
        lock (CacheLock)
        {
            AutocompleteCache[key] = (emotes, DateTime.UtcNow);

            if (AutocompleteCache.Count > 1000)
            {
                var oldest = AutocompleteCache.OrderBy(kv => kv.Value.Timestamp).First().Key;
                AutocompleteCache.Remove(oldest);
            }
        }
    }

     static (List<SevenTvEmote> Emotes, string Query, DateTime Timestamp)? GetLastAutocompleteResults(string userId, string? setId)
    {
        var key = $"{userId}:{setId ?? "global"}";
        lock (CacheLock)
        {
            if (LastAutocompleteCache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.Timestamp < CacheDuration)
                    return entry;

                LastAutocompleteCache.Remove(key);
            }
        }
        return null;
    }

     static void SetLastAutocompleteResults(string userId, string query, List<SevenTvEmote> emotes, string? setId)
    {
        var key = $"{userId}:{setId ?? "global"}";
        lock (CacheLock)
        {
            LastAutocompleteCache[key] = (emotes, query, DateTime.UtcNow);

            if (LastAutocompleteCache.Count > 500)
            {
                var oldest = LastAutocompleteCache.OrderBy(kv => kv.Value.Timestamp).First().Key;
                LastAutocompleteCache.Remove(oldest);
            }
        }
    }
}
