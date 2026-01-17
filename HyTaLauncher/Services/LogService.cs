using System.IO;

namespace HyTaLauncher.Services
{
    public static class LogService
    {
        private static readonly string _logDir;
        private static readonly object _lock = new();
        
        /// <summary>
        /// Включить подробное логирование (устанавливается из настроек)
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

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

        /// <summary>
        /// Логирует только если включено подробное логирование
        /// </summary>
        public static void LogVerbose(string category, string message)
        {
            if (VerboseLogging)
            {
                Log(category, $"[VERBOSE] {message}");
            }
        }

        public static void LogMods(string message) => Log("mods", message);
        public static void LogGame(string message) => Log("game", message);
        public static void LogError(string message) => Log("error", message);
        public static void LogError(string message, Exception ex)
        {
            Log("error", $"{message}: {ex.Message}");
            if (VerboseLogging)
            {
                Log("error", $"StackTrace: {ex.StackTrace}");
            }
        }

        // Verbose методы
        public static void LogGameVerbose(string message) => LogVerbose("game", message);
        public static void LogModsVerbose(string message) => LogVerbose("mods", message);
        public static void LogNetworkVerbose(string message) => LogVerbose("network", message);

        public static string GetLogsFolder() => _logDir;
        
        /// <summary>
        /// Очищает старые логи (старше 7 дней)
        /// </summary>
        public static void CleanOldLogs(int daysToKeep = 7)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                foreach (var file in Directory.GetFiles(_logDir, "*.log"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch { }
        }
    }
}
