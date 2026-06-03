using System;
using System.IO;

namespace DiscordBot
{
    // ─────────────────────────────────────────────────────────────────────────
    // Logger — static utility for writing timestamped messages to console
    // and a persistent log file. Call Logger.Log() from anywhere.
    // ─────────────────────────────────────────────────────────────────────────
    public static class Logger
    {
        private static readonly StreamWriter _logFile =
            new StreamWriter("bot-log.txt", append: true) { AutoFlush = true };

        public static void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logFile.WriteLine(line);
            _logFile.Flush();
            Console.WriteLine(line);
        }
    }
}