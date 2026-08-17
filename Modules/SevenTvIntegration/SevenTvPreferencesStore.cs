using System.Text.Json;

namespace DiscordBot.Modules.SevenTv;

/// <summary>
/// Same public API as the SQLite version, backed by a single JSON file instead —
/// no NuGet package needed, just System.Text.Json. All user preferences live in
/// memory once loaded and get rewritten to disk (atomically, via a temp file +
/// move) on every change, so reads never touch disk after the first load.
/// </summary>
public static class SevenTvPreferencesStore
{
     static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "data", "sevenTvPreferences.json");
     static readonly string[] ValidSizes = { "1x", "2x", "3x", "4x" };

     static Dictionary<string, SevenTvUserPreferences>? _cache;
     static readonly SemaphoreSlim Lock = new(1, 1);

     static async Task<Dictionary<string, SevenTvUserPreferences>> LoadAsync()
    {
        if (_cache != null) return _cache;

        await Lock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            if (!File.Exists(FilePath))
            {
                _cache = new Dictionary<string, SevenTvUserPreferences>();
                return _cache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(FilePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, SevenTvUserPreferences>>(json)
                          ?? new Dictionary<string, SevenTvUserPreferences>();
            }
            catch (Exception ex)
            {
                Logger.Log($"[7TV] Failed to read preferences file, starting fresh: {ex.Message}");
                _cache = new Dictionary<string, SevenTvUserPreferences>();
            }

            return _cache;
        }
        finally
        {
            Lock.Release();
        }
    }

     static async Task SaveAsync(Dictionary<string, SevenTvUserPreferences> data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        // Write to a temp file then move over the real one, so a crash mid-write
        // can't leave you with a corrupted/truncated preferences file.
        var tempPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public static async Task<SevenTvUserPreferences> GetPreferencesAsync(ulong userId)
    {
        var data = await LoadAsync();

        await Lock.WaitAsync();
        try
        {
            if (!data.TryGetValue(userId.ToString(), out var prefs))
                return new SevenTvUserPreferences();

            if (!ValidSizes.Contains(prefs.ImageSize))
                prefs.ImageSize = "2x";

            return prefs;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Loads the current preferences, applies `mutate`, and saves the result —
    /// so callers only need to set the one or two fields they care about.
    /// </summary>
    public static async Task<bool> SetPreferencesAsync(ulong userId, Action<SevenTvUserPreferences> mutate)
    {
        var data = await LoadAsync();

        await Lock.WaitAsync();
        try
        {
            var key = userId.ToString();
            var current = data.TryGetValue(key, out var existing) ? existing : new SevenTvUserPreferences();

            mutate(current);

            if (!ValidSizes.Contains(current.ImageSize))
            {
                Logger.Log($"[7TV] Refused to save invalid image size: {current.ImageSize}");
                return false;
            }

            data[key] = current;
            await SaveAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to save preferences: {ex.Message}");
            return false;
        }
        finally
        {
            Lock.Release();
        }
    }

    public static async Task<bool> DeletePreferencesAsync(ulong userId)
    {
        var data = await LoadAsync();

        await Lock.WaitAsync();
        try
        {
            if (data.Remove(userId.ToString()))
                await SaveAsync(data);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[7TV] Failed to delete preferences: {ex.Message}");
            return false;
        }
        finally
        {
            Lock.Release();
        }
    }
}
