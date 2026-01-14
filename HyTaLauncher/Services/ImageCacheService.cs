using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace HyTaLauncher.Services
{
    public static class ImageCacheService
    {
        private static readonly HttpClient _httpClient = new();
        private static readonly string _cacheDir;
        private static readonly Dictionary<string, BitmapImage> _memoryCache = new();
        private static readonly object _lock = new();

        static ImageCacheService()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher", "cache", "images"
            );
            Directory.CreateDirectory(_cacheDir);
        }

        public static async Task<BitmapImage?> GetImageAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // Проверяем memory cache
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(url, out var cached))
                    return cached;
            }

            try
            {
                var fileName = GetCacheFileName(url);
                var filePath = Path.Combine(_cacheDir, fileName);

                // Проверяем disk cache
                if (File.Exists(filePath))
                {
                    var image = LoadImageFromFile(filePath);
                    if (image != null)
                    {
                        lock (_lock) { _memoryCache[url] = image; }
                        return image;
                    }
                }

                // Скачиваем
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);

                var downloadedImage = LoadImageFromBytes(bytes);
                if (downloadedImage != null)
                {
                    lock (_lock) { _memoryCache[url] = downloadedImage; }
                }
                return downloadedImage;
            }
            catch
            {
                return null;
            }
        }

        private static string GetCacheFileName(string url)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            return hashString + ext;
        }

        private static BitmapImage? LoadImageFromFile(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }

        private static BitmapImage? LoadImageFromBytes(byte[] bytes)
        {
            try
            {
                var image = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }

        public static void ClearCache()
        {
            lock (_lock) { _memoryCache.Clear(); }
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    foreach (var file in Directory.GetFiles(_cacheDir))
                        File.Delete(file);
                }
            }
            catch { }
        }
    }
}
