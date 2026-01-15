using System.Globalization;
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
        public string Language { get; set; } = ""; // Пустая строка = определить по системе
        public bool UseMirror { get; set; } = false; // Использовать зеркало для скачивания
        public bool ServerInfoShown { get; set; } = false; // Показана ли инструкция по серверу
    }

    public class SettingsManager
    {
        private readonly string _settingsPath;
        private readonly string _langDir;

        public SettingsManager()
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher"
            );
            Directory.CreateDirectory(appDir);
            _settingsPath = Path.Combine(appDir, "settings.json");
            _langDir = Path.Combine(appDir, "languages");
        }

        public LauncherSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<LauncherSettings>(json) ?? new LauncherSettings();
                    
                    // Если язык не задан - определяем по системе
                    if (string.IsNullOrEmpty(settings.Language))
                    {
                        settings.Language = GetSystemLanguage();
                    }
                    
                    return settings;
                }
            }
            catch
            {
                // Return default settings on error
            }
            
            var defaultSettings = new LauncherSettings();
            defaultSettings.Language = GetSystemLanguage();
            return defaultSettings;
        }

        private string GetSystemLanguage()
        {
            var langCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
            
            // Проверяем, есть ли такой язык
            if (Directory.Exists(_langDir))
            {
                var langFile = Path.Combine(_langDir, $"{langCode}.json");
                if (File.Exists(langFile))
                {
                    return langCode;
                }
            }
            
            // Поддерживаемые языки
            if (langCode == "ru") return "ru";
            
            return "en";
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
