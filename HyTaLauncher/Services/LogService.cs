using System.IO;

namespace HyTaLauncher.Services
{
    public static class LogService
    {
        private static readonly string _logDir;
        private static readonly object _lock = new();

        static LogService()
        {
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher", "logs"
            );
            Directory.CreateDirectory(_logDir);
        }

        public static void Log(string category, string message)
        {
            var logFile = Path.Combine(_logDir, $"{category}_{DateTime.Now:yyyy-MM-dd}.log");
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(logFile, logLine + Environment.NewLine);
                }
                catch { }
            }
        }

        public static void LogMods(string message) => Log("mods", message);
        public static void LogGame(string message) => Log("game", message);
        public static void LogError(string message) => Log("error", message);

        public static string GetLogsFolder() => _logDir;
    }
}
