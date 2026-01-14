using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HyTaLauncher.Helpers
{
    public static class FontHelper
    {
        private static bool _initialized = false;
        private static string? _fontDir;
        
        public static FontFamily? CinzelFont { get; private set; }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Папка для шрифтов в AppData
                _fontDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HyTaLauncher", "fonts"
                );
                Directory.CreateDirectory(_fontDir);

                // Извлекаем шрифты
                ExtractFont("cinzel_regular.ttf");
                ExtractFont("cinzel_bold.ttf");

                // Создаём FontFamily из папки с шрифтами
                // WPF автоматически найдёт Regular и Bold варианты
                CinzelFont = new FontFamily(new Uri(_fontDir + "/"), "./#Cinzel(RUS BY LYAJKA)");
            }
            catch
            {
                // Fallback на системный шрифт
                CinzelFont = new FontFamily("Segoe UI");
            }
        }

        private static void ExtractFont(string fontName)
        {
            if (_fontDir == null) return;
            
            var fontPath = Path.Combine(_fontDir, fontName);
            
            // Извлекаем если не существует
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
                    // Игнорируем
                }
            }
        }
    }
}
