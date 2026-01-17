using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HyTaLauncher.Helpers
{
    public static class FontHelper
    {
        private static bool _initialized = false;
        private static string? _fontDir;
        private static List<string>? _availableFonts;
        
        public static FontFamily? CurrentFont { get; private set; }
        public static FontFamily? CinzelFont { get; private set; }
        
        // Рекомендуемые шрифты (показываются первыми)
        private static readonly string[] RecommendedFonts = { "Inter", "Cinzel", "Segoe UI", "Arial", "Consolas", "Verdana", "Tahoma" };
        
        public static string CurrentFontName { get; private set; } = "Inter";

        /// <summary>
        /// Получает список всех доступных шрифтов (рекомендуемые + системные)
        /// </summary>
        public static List<string> AvailableFonts
        {
            get
            {
                if (_availableFonts == null)
                {
                    _availableFonts = new List<string>();
                    
                    // Сначала добавляем рекомендуемые
                    _availableFonts.AddRange(RecommendedFonts);
                    
                    // Затем все системные шрифты (кроме уже добавленных)
                    var systemFonts = Fonts.SystemFontFamilies
                        .Select(f => f.Source)
                        .Where(name => !RecommendedFonts.Contains(name))
                        .OrderBy(name => name)
                        .ToList();
                    
                    _availableFonts.AddRange(systemFonts);
                }
                return _availableFonts;
            }
        }

        public static void Initialize(string fontName = "Inter")
        {
            if (!_initialized)
            {
                _initialized = true;
                
                try
                {
                    // Папка для шрифтов в AppData
                    _fontDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "HyTaLauncher", "fonts"
                    );
                    Directory.CreateDirectory(_fontDir);

                    // Извлекаем шрифты Cinzel
                    ExtractFont("cinzel_regular.ttf");
                    ExtractFont("cinzel_bold.ttf");
                    
                    // Извлекаем Inter
                    ExtractFont("inter_regular.ttf");
                    ExtractFont("inter_bold.ttf");

                    // Создаём FontFamily для Cinzel
                    CinzelFont = new FontFamily(new Uri(_fontDir + "/"), "./#Cinzel(RUS BY LYAJKA)");
                }
                catch
                {
                    CinzelFont = new FontFamily("Segoe UI");
                }
            }
            
            SetFont(fontName);
        }

        public static void SetFont(string fontName)
        {
            CurrentFontName = fontName;
            
            try
            {
                // Специальная обработка для встроенных шрифтов
                if (fontName == "Inter" && _fontDir != null)
                {
                    CurrentFont = new FontFamily(new Uri(_fontDir + "/"), "./#Inter");
                }
                else if (fontName == "Cinzel")
                {
                    CurrentFont = CinzelFont ?? new FontFamily("Segoe UI");
                }
                else
                {
                    // Любой системный шрифт
                    CurrentFont = new FontFamily(fontName);
                }
            }
            catch
            {
                CurrentFont = new FontFamily("Segoe UI");
            }
        }

        private static void ExtractFont(string fontName)
        {
            if (_fontDir == null) return;
            
            var fontPath = Path.Combine(_fontDir, fontName);
            
            if (!File.Exists(fontPath))
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Fonts/{fontName}");
                    var streamInfo = Application.GetResourceStream(uri);
                    
                    if (streamInfo?.Stream != null)
                    {
                        using var fileStream = File.Create(fontPath);
                        streamInfo.Stream.CopyTo(fileStream);
                        streamInfo.Stream.Close();
                    }
                }
                catch
                {
                    // Шрифт не найден в ресурсах - используем системный
                }
            }
        }
    }
}
