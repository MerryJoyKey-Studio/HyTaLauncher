using System.IO;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class LocalizationService
    {
        private Dictionary<string, string> _translations = new();
        private readonly string _langDir;
        private string _currentLanguage = "en";

        public event Action? LanguageChanged;

        public LocalizationService()
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _langDir = Path.Combine(roaming, "HyTaLauncher", "languages");
            Directory.CreateDirectory(_langDir);
            
            CreateDefaultLanguages();
            LoadLanguage("en");
        }

        public string CurrentLanguage => _currentLanguage;

        public List<string> GetAvailableLanguages()
        {
            var languages = new List<string>();
            if (Directory.Exists(_langDir))
            {
                foreach (var file in Directory.GetFiles(_langDir, "*.json"))
                {
                    languages.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            return languages;
        }

        public void LoadLanguage(string language)
        {
            var langFile = Path.Combine(_langDir, $"{language}.json");
            if (File.Exists(langFile))
            {
                try
                {
                    var json = File.ReadAllText(langFile);
                    _translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) 
                        ?? new Dictionary<string, string>();
                    _currentLanguage = language;
                    LanguageChanged?.Invoke();
                }
                catch
                {
                    _translations = new Dictionary<string, string>();
                }
            }
        }

        public string Get(string key)
        {
            return _translations.TryGetValue(key, out var value) ? value : key;
        }

        private void CreateDefaultLanguages()
        {
            // English - всегда обновляем недостающие ключи
            var enFile = Path.Combine(_langDir, "en.json");
            var enDefaults = GetEnglishDefaults();
            MergeLanguageFile(enFile, enDefaults);

            // Russian - всегда обновляем недостающие ключи
            var ruFile = Path.Combine(_langDir, "ru.json");
            var ruDefaults = GetRussianDefaults();
            MergeLanguageFile(ruFile, ruDefaults);
        }

        private void MergeLanguageFile(string filePath, Dictionary<string, string> defaults)
        {
            Dictionary<string, string> existing = new();
            
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    existing = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
                }
                catch { }
            }

            // Добавляем недостающие ключи
            bool updated = false;
            foreach (var kvp in defaults)
            {
                if (!existing.ContainsKey(kvp.Key))
                {
                    existing[kvp.Key] = kvp.Value;
                    updated = true;
                }
            }

            // Сохраняем если были изменения или файл не существовал
            if (updated || !File.Exists(filePath))
            {
                File.WriteAllText(filePath, JsonConvert.SerializeObject(existing, Formatting.Indented));
            }
        }

        private Dictionary<string, string> GetEnglishDefaults()
        {
            return new Dictionary<string, string>
            {
                ["app.title"] = "HyTaLauncher",
                ["main.news"] = "HYTALE NEWS",
                ["main.nickname"] = "NICKNAME",
                ["main.version"] = "VERSION",
                ["main.branch"] = "BRANCH",
                ["main.play"] = "PLAY",
                ["main.settings"] = "⚙ Settings",
                ["main.mods"] = "🧩 Mods",
                ["main.preparing"] = "Preparing...",
                ["main.footer"] = "HyTaLauncher v1.0.0 • Unofficial launcher",
                ["main.disclaimer"] = "This is a non-commercial fan project. After trying the game, please purchase it at",
                ["main.versions_found"] = "Versions found: {0}",
                ["main.latest"] = "Latest (latest)",
                ["main.version_num"] = "Version {0}",
                
                ["error.title"] = "Error",
                ["error.nickname_empty"] = "Please enter a nickname!",
                ["error.nickname_length"] = "Nickname must be 3-16 characters!",
                ["error.version_select"] = "Please select a version!",
                ["error.launch"] = "Launch error: {0}",
                
                ["status.checking_java"] = "Checking Java...",
                ["status.checking_game"] = "Checking game...",
                ["status.launching"] = "Launching game...",
                ["status.downloading_jre"] = "Downloading Java Runtime...",
                ["status.extracting_java"] = "Extracting Java...",
                ["status.system_java"] = "Using system Java",
                ["status.game_installed"] = "Game already installed",
                ["status.pwr_cached"] = "PWR file already downloaded",
                ["status.downloading"] = "Downloading {0}...",
                ["status.installing"] = "Installing game...",
                ["status.downloading_butler"] = "Downloading Butler...",
                ["status.extracting_butler"] = "Extracting Butler...",
                ["status.applying_patch"] = "Applying patch...",
                ["status.game_installed_done"] = "Game installed!",
                ["status.checking_versions"] = "Checking available versions...",
                
                ["settings.title"] = "⚙ Settings",
                ["settings.game_folder"] = "GAME FOLDER",
                ["settings.api_key"] = "CURSEFORGE API KEY",
                ["settings.api_key_hint"] = "Get your API key at console.curseforge.com",
                ["settings.info"] = "HyTaLauncher v1.0.0",
                ["settings.info_desc"] = "Unofficial launcher for Hytale",
                ["settings.cancel"] = "Cancel",
                ["settings.save"] = "Save",
                ["settings.saved"] = "Settings saved!",
                ["settings.success"] = "Success",
                ["settings.select_folder"] = "Select game folder",
                
                ["update.available"] = "Update available!",
                ["update.message"] = "New version {0} is available.\nCurrent version: {1}\n\nOpen download page?",
                ["update.checking"] = "Checking for updates...",
                
                ["mods.title"] = "Mods Manager",
                ["mods.installed"] = "INSTALLED MODS",
                ["mods.browse"] = "CURSEFORGE",
                ["mods.ready"] = "Ready",
                ["mods.loading"] = "Loading mods...",
                ["mods.searching"] = "Searching...",
                ["mods.found"] = "Found: {0} mods",
                ["mods.count"] = "{0} mods installed",
                ["mods.delete_confirm"] = "Delete mod \"{0}\"?",
                ["mods.delete_title"] = "Delete mod",
                ["mods.deleted"] = "Mod \"{0}\" deleted",
                ["mods.no_api_key"] = "CurseForge API key not set. Add it in Settings.",
                ["mods.search_placeholder"] = "Search mods..."
            };
        }

        private Dictionary<string, string> GetRussianDefaults()
        {
            return new Dictionary<string, string>
            {
                ["app.title"] = "HyTaLauncher",
                ["main.news"] = "НОВОСТИ HYTALE",
                ["main.nickname"] = "НИКНЕЙМ",
                ["main.version"] = "ВЕРСИЯ",
                ["main.branch"] = "ВЕТКА",
                ["main.play"] = "ИГРАТЬ",
                ["main.settings"] = "⚙ Настройки",
                ["main.mods"] = "🧩 Моды",
                ["main.preparing"] = "Подготовка...",
                ["main.footer"] = "HyTaLauncher v1.0.0 • Неофициальный лаунчер",
                ["main.disclaimer"] = "Это некоммерческий фан-проект. После ознакомления приобретите игру на",
                ["main.versions_found"] = "Найдено версий: {0}",
                ["main.latest"] = "Последняя (latest)",
                ["main.version_num"] = "Версия {0}",
                
                ["error.title"] = "Ошибка",
                ["error.nickname_empty"] = "Пожалуйста, введите никнейм!",
                ["error.nickname_length"] = "Никнейм должен быть от 3 до 16 символов!",
                ["error.version_select"] = "Пожалуйста, выберите версию!",
                ["error.launch"] = "Ошибка запуска: {0}",
                
                ["status.checking_java"] = "Проверка Java...",
                ["status.checking_game"] = "Проверка игры...",
                ["status.launching"] = "Запуск игры...",
                ["status.downloading_jre"] = "Загрузка Java Runtime...",
                ["status.extracting_java"] = "Распаковка Java...",
                ["status.system_java"] = "Используется системная Java",
                ["status.game_installed"] = "Игра уже установлена",
                ["status.pwr_cached"] = "PWR файл уже скачан",
                ["status.downloading"] = "Загрузка {0}...",
                ["status.installing"] = "Установка игры...",
                ["status.downloading_butler"] = "Загрузка Butler...",
                ["status.extracting_butler"] = "Распаковка Butler...",
                ["status.applying_patch"] = "Применение патча...",
                ["status.game_installed_done"] = "Игра установлена!",
                ["status.checking_versions"] = "Проверка доступных версий...",
                
                ["settings.title"] = "⚙ Настройки",
                ["settings.game_folder"] = "ПАПКА ИГРЫ",
                ["settings.api_key"] = "CURSEFORGE API КЛЮЧ",
                ["settings.api_key_hint"] = "Получите API ключ на console.curseforge.com",
                ["settings.info"] = "HyTaLauncher v1.0.0",
                ["settings.info_desc"] = "Неофициальный лаунчер для Hytale",
                ["settings.cancel"] = "Отмена",
                ["settings.save"] = "Сохранить",
                ["settings.saved"] = "Настройки сохранены!",
                ["settings.success"] = "Успех",
                ["settings.select_folder"] = "Выберите папку для игры",
                
                ["update.available"] = "Доступно обновление!",
                ["update.message"] = "Доступна новая версия {0}.\nТекущая версия: {1}\n\nОткрыть страницу загрузки?",
                ["update.checking"] = "Проверка обновлений...",
                
                ["mods.title"] = "Менеджер модов",
                ["mods.installed"] = "УСТАНОВЛЕННЫЕ МОДЫ",
                ["mods.browse"] = "CURSEFORGE",
                ["mods.ready"] = "Готово",
                ["mods.loading"] = "Загрузка модов...",
                ["mods.searching"] = "Поиск...",
                ["mods.found"] = "Найдено: {0} модов",
                ["mods.count"] = "{0} модов установлено",
                ["mods.delete_confirm"] = "Удалить мод \"{0}\"?",
                ["mods.delete_title"] = "Удаление мода",
                ["mods.deleted"] = "Мод \"{0}\" удалён",
                ["mods.no_api_key"] = "API ключ CurseForge не указан. Добавьте его в Настройках.",
                ["mods.search_placeholder"] = "Поиск модов..."
            };
        }
    }
}
