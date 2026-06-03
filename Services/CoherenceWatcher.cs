using System.IO;
using DiscordBot;
using DiscordBot.Data;
using DiscordBot.Services;

public class CoherenceWatcher : IDisposable
{
    readonly ArgTerminalService _terminal;
    readonly ArgTerminalData _data;
    readonly FileSystemWatcher _watcher;
    
    DateTime _lastFileEvent = DateTime.MinValue;
    DateTime _lastEmbedRefresh = DateTime.MinValue;

    public CoherenceWatcher(
        ArgTerminalService terminal,
        ArgTerminalData data)
    {
        _terminal = terminal;
        _data = data;

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_data.StateFilePath)!)
        {
            Filter = Path.GetFileName(_data.StateFilePath),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        _ = HandleChangeAsync();
    }

    async Task HandleChangeAsync()
    {
        // 1. file-system debounce
        if (DateTime.UtcNow - _lastFileEvent < TimeSpan.FromMilliseconds(300))
            return;

        _lastFileEvent = DateTime.UtcNow;

        await Task.Delay(50); // allow write to finish

        try
        {
            _data.Reload();

            // 2. embed refresh throttle
            if (DateTime.UtcNow - _lastEmbedRefresh < TimeSpan.FromMilliseconds(800))
                return;

            _lastEmbedRefresh = DateTime.UtcNow;

            await _terminal.RefreshEmbeds(ARGEmbed_Type.Terminal);
        }
        catch (Exception ex)
        {
            Logger.Log($"[CoherenceWatcher] Error: {ex}");
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}