using System.IO;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class LauncherSettings
    {
        public string Nickname { get; set; } = "";
        public int VersionIndex { get; set; } = 0;
        public string GameDirectory { get; set; } = "";
        public int MemoryMb { get; set; } = 4096;
        public string Language { get; set; } = "en";
    }

    public class SettingsManager
    {
        private readonly string _settingsPath;

        public SettingsManager()
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher"
            );
            Directory.CreateDirectory(appDir);
            _settingsPath = Path.Combine(appDir, "settings.json");
        }

        public LauncherSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonConvert.DeserializeObject<LauncherSettings>(json) ?? new LauncherSettings();
                }
            }
            catch
            {
                // Return default settings on error
            }
            return new LauncherSettings();
        }

        public void Save(LauncherSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
