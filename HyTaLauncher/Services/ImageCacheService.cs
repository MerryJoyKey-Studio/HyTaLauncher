using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;

namespace HyTaLauncher.Services
{
    public static class ImageCacheService
    {
        private static readonly HttpClient _httpClient;
        private static readonly string _cacheDir;
        private static readonly Dictionary<string, BitmapImage> _memoryCache = new();
        private static readonly LinkedList<string> _cacheOrder = new(); // LRU tracking
        private static readonly object _lock = new();

        // Configuration
        private const int MaxMemoryCacheSize = 100; // Maximum number of images in memory
        private const int MaxRetries = 2;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromDays(7);

        static ImageCacheService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = RequestTimeout;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher/1.0");

            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher", "cache", "images"
            );
            Directory.CreateDirectory(_cacheDir);

            // Clean up old cache files on startup
            CleanupExpiredCache();
        }

        public static async Task<BitmapImage?> GetImageAsync(string? url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // Check memory cache first
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(url, out var cached))
                {
                    // Move to end of LRU list (most recently used)
                    _cacheOrder.Remove(url);
                    _cacheOrder.AddLast(url);
                    return cached;
                }
            }

            try
            {
                var fileName = GetCacheFileName(url);
                var filePath = Path.Combine(_cacheDir, fileName);

                // Check disk cache
                if (File.Exists(filePath))
                {
                    // Check if file is not too old
                    var fileInfo = new FileInfo(filePath);
                    if (DateTime.Now - fileInfo.LastWriteTime < CacheExpiration)
                    {
                        var image = await LoadImageFromFileAsync(filePath);
                        if (image != null)
                        {
                            AddToMemoryCache(url, image);
                            return image;
                        }
                    }
                    else
                    {
                        // Expired, delete it
                        SafeDeleteFile(filePath);
                    }
                }

                // Download with retry
                var bytes = await DownloadWithRetryAsync(url, cancellationToken);
                if (bytes == null) return null;

                // Save to disk cache
                try
                {
                    await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
                }
                catch
                {
                    // Ignore disk cache errors
                }

                var downloadedImage = LoadImageFromBytes(bytes);
                if (downloadedImage != null)
                {
                    AddToMemoryCache(url, downloadedImage);
                }
                return downloadedImage;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogService.LogNetworkVerbose($"Image cache error for {url}: {ex.Message}");
                return null;
            }
        }

        private static async Task<byte[]?> DownloadWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(RequestTimeout);

                    var bytes = await _httpClient.GetByteArrayAsync(url, cts.Token);
                    return bytes;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // User cancelled, don't retry
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogNetworkVerbose($"Image download failed (attempt {attempt}): {url} - {ex.Message}");

                    if (attempt < MaxRetries)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            return null;
                        }
                    }
                }
            }

            LogService.LogNetworkVerbose($"Image download failed after {MaxRetries} attempts: {url}");
            return null;
        }

        private static void AddToMemoryCache(string url, BitmapImage image)
        {
            lock (_lock)
            {
                // Evict oldest entries if cache is full
                while (_memoryCache.Count >= MaxMemoryCacheSize && _cacheOrder.First != null)
                {
                    var oldest = _cacheOrder.First.Value;
                    _cacheOrder.RemoveFirst();
                    _memoryCache.Remove(oldest);
                }

                _memoryCache[url] = image;
                _cacheOrder.AddLast(url);
            }
        }

        private static string GetCacheFileName(string url)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();

            // Try to get extension from URL
            string ext = ".png";
            try
            {
                var uri = new Uri(url);
                var urlExt = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(urlExt) && urlExt.Length <= 5)
                {
                    ext = urlExt;
                }
            }
            catch { }

            return hashString + ext;
        }

        private static async Task<BitmapImage?> LoadImageFromFileAsync(string path)
        {
            return await Task.Run(() => LoadImageFromFile(path));
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
            catch
            {
                return null;
            }
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
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Clears both memory and disk cache
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
                _cacheOrder.Clear();
            }

            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    foreach (var file in Directory.GetFiles(_cacheDir))
                    {
                        SafeDeleteFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to clear image cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears only memory cache (keeps disk cache)
        /// </summary>
        public static void ClearMemoryCache()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
                _cacheOrder.Clear();
            }
        }

        /// <summary>
        /// Gets current memory cache size
        /// </summary>
        public static int GetMemoryCacheSize()
        {
            lock (_lock)
            {
                return _memoryCache.Count;
            }
        }

        /// <summary>
        /// Gets disk cache size in bytes
        /// </summary>
        public static long GetDiskCacheSize()
        {
            try
            {
                if (!Directory.Exists(_cacheDir))
                    return 0;

                return Directory.GetFiles(_cacheDir)
                    .Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Removes expired cache files
        /// </summary>
        private static void CleanupExpiredCache()
        {
            try
            {
                if (!Directory.Exists(_cacheDir))
                    return;

                var cutoff = DateTime.Now - CacheExpiration;
                foreach (var file in Directory.GetFiles(_cacheDir))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Safely deletes a file without throwing exceptions
        /// </summary>
        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
