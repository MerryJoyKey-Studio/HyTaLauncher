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
        public bool VerboseLogging { get; set; } = false; // Подробное логирование
        public bool AlwaysFullDownload { get; set; } = true; // Всегда скачивать полную версию (по умолчанию вкл)
        public string CustomGameArgs { get; set; } = ""; // Кастомные аргументы запуска игры
        public string FontName { get; set; } = "Inter"; // Шрифт лаунчера

        // Mods browser preferences
        public int ModsSortOption { get; set; } = 2; // Default: Popularity (SortOption.Popularity = 2)
        public int ModsSortOrder { get; set; } = 1; // Default: Descending (1 = Descending, 0 = Ascending)

        // Selected modpack for game launch
        public string? SelectedModpackId { get; set; } = null; // null = use default UserData

        // Run as Administrator settings
        public bool RunGameAsAdmin { get; set; } = false; // Запуск игры от имени администратора
        public bool RunServerAsAdmin { get; set; } = false; // Запуск сервера от имени администратора
        public bool RunLauncherAsAdmin { get; set; } = false; // Запуск лаунчера от имени администратора

        // SSL/Network settings
        public bool BypassSslValidation { get; set; } = false; // Обход проверки SSL сертификатов (для корпоративных прокси)
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
