using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace DiscordBot
{
    //-----------------------------------------------------------------------
    // logger, static utility for writing timestamped messages to console
    // and a persistent log file. call Logger.Log() from anywhere.
    //
    // logs are written to logs/bot-log-YYYY-MM-DD-N.txt where N is the
    // iteration count for that day. a new file is created each time the
    // bot starts.
    //
    // pass dm: true to also DM the bot owner, use it for events you
    // actually want to be notified about, not routine debug spam.
    //-----------------------------------------------------------------------
    public static class Logger
    {
        static readonly string LogDir =
            Path.Combine(Environment.CurrentDirectory, "logs");

        static StreamWriter? _logFile;
        static DiscordSocketClient? _client;
        static IDMChannel? _ownerDm;

        // call once, right after constructing the client, before client.LoginAsync
        public static void Init(DiscordSocketClient client)
        {
            _client = client;

            Directory.CreateDirectory(LogDir);

            string today = DateTime.Now.ToString("yyyy-MM-dd");

            string[] existing = Directory.GetFiles(
                LogDir, $"bot-log-{today}-*.txt");

            int iteration = existing.Length + 1;

            string path = Path.Combine(
                LogDir, $"bot-log-{today}-{iteration}.txt");

            _logFile = new StreamWriter(path, append: true) { AutoFlush = true };
        }

        public static void Log(string msg, bool dm = false)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logFile?.WriteLine(line);
            _logFile?.Flush();
            Console.WriteLine(line);

            if (dm)
                _ = SendDmAsync(msg); // fire and forget so callers stay sync
        }

         static async Task SendDmAsync(string msg)
        {
            if (_client == null)
            {
                // note: don't call Log(..., dm:true) here, avoids infinite recursion
                Log("[Warning] Logger.Log(dm:true) called before Logger.Init, DM not sent");
                return;
            }

            try
            {
                if (_ownerDm == null)
                {
                    IApplication appInfo = await _client.GetApplicationInfoAsync();
                    _ownerDm = await appInfo.Owner.CreateDMChannelAsync();
                }

                await _ownerDm.SendMessageAsync(msg);
            }
            catch (Exception ex)
            {
                Log($"[Warning] Failed to send log DM: {ex.Message}");
                _ownerDm = null; // drop the cache in case the channel went stale
            }
        }
    }
}
