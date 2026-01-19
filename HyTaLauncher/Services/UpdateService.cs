using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class UpdateInfo
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = "";

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonProperty("body")]
        public string Body { get; set; } = "";

        [JsonProperty("assets")]
        public List<ReleaseAsset> Assets { get; set; } = new();

        public string Version => TagName.TrimStart('v', 'V');

        public string? PortableDownloadUrl => Assets
            .FirstOrDefault(a => a.Name.Contains("Portable") && a.Name.EndsWith(".zip"))?.DownloadUrl;
    }

    public class ReleaseAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("browser_download_url")]
        public string DownloadUrl { get; set; } = "";

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    public class UpdateService : IDisposable
    {
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        private readonly HttpRetryService _httpService;
        private readonly HttpClient _apiClient;
        private bool _disposed;
        private const string RepoOwner = "MerryJoyKey-Studio";
        private const string RepoName = "HyTaLauncher";
        private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        // Retry config for update downloads
        private static readonly RetryConfig UpdateDownloadConfig = new()
        {
            MaxRetries = 3,
            Timeout = TimeSpan.FromMinutes(15),
            EnableResume = true,
            InitialRetryDelay = TimeSpan.FromSeconds(2)
        };

        public static string CurrentVersion => "1.0.7";

        public UpdateService()
        {
            _httpService = new HttpRetryService(UpdateDownloadConfig);
            _httpService.ProgressChanged += p => ProgressChanged?.Invoke(p);
            _httpService.StatusChanged += s => StatusChanged?.Invoke(s);

            _apiClient = new HttpClient();
            _apiClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher");
            _apiClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var maxRetries = 2;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    LogService.LogGameVerbose($"Checking for updates (attempt {attempt}/{maxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    var json = await _apiClient.GetStringAsync(ApiUrl, cts.Token);
                    var release = JsonConvert.DeserializeObject<UpdateInfo>(json);

                    if (release != null && IsNewerVersion(release.Version, CurrentVersion))
                    {
                        LogService.LogGame($"Update available: {release.Version}");
                        return release;
                    }

                    LogService.LogGameVerbose("No updates available");
                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // User cancelled
                }
                catch (Exception ex)
                {
                    LogService.LogGameVerbose($"Update check failed (attempt {attempt}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            }

            LogService.LogGameVerbose("Update check failed after all retries");
            return null;
        }

        /// <summary>
        /// Скачивает и применяет обновление
        /// </summary>
        public async Task<bool> DownloadAndApplyUpdateAsync(UpdateInfo update, LocalizationService localization, CancellationToken cancellationToken = default)
        {
            var downloadUrl = update.PortableDownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("Portable version not found in release");
            }

            // Get expected file size from release assets
            var expectedSize = update.Assets
                .FirstOrDefault(a => a.DownloadUrl == downloadUrl)?.Size ?? -1;

            var launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "HyTaLauncher_Update");
            var zipPath = Path.Combine(tempDir, $"HyTaLauncher_Portable_{update.Version}.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            try
            {
                // Очищаем временную папку
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(extractDir);

                // Скачиваем zip с поддержкой докачки и повторов
                StatusChanged?.Invoke(localization.Get("update.downloading"));
                LogService.LogGame($"Downloading update from: {downloadUrl}");

                var downloadResult = await _httpService.DownloadFileAsync(
                    downloadUrl,
                    zipPath,
                    UpdateDownloadConfig,
                    expectedSize,
                    cancellationToken: cancellationToken);

                if (!downloadResult.Success)
                {
                    LogService.LogError($"Update download failed: {downloadResult.ErrorMessage}");
                    throw new Exception($"Download failed: {downloadResult.ErrorMessage}");
                }

                LogService.LogGame($"Update downloaded: {downloadResult.BytesDownloaded} bytes, attempts: {downloadResult.AttemptsUsed}, resumed: {downloadResult.WasResumed}");

                // Распаковываем
                StatusChanged?.Invoke(localization.Get("update.extracting"));
                ProgressChanged?.Invoke(-1);
                ZipFile.ExtractToDirectory(zipPath, extractDir, true);

                // Ищем папку с файлами (может быть вложенная папка)
                var sourceDir = extractDir;
                var subDirs = Directory.GetDirectories(extractDir);
                if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0)
                {
                    sourceDir = subDirs[0];
                }

                // Создаём PowerShell скрипт для обновления (менее подозрительный для AV)
                StatusChanged?.Invoke(localization.Get("update.preparing"));
                var updateScriptPath = Path.Combine(tempDir, "update.ps1");
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ??
                    Path.Combine(launcherDir, "HyTaLauncher.exe");

                // PowerShell скрипт вместо batch - меньше детектов
                var psContent = $@"
# HyTaLauncher Update Script
$ErrorActionPreference = 'SilentlyContinue'

# Wait for launcher to close
Start-Sleep -Seconds 2

# Copy new files
$source = '{sourceDir.Replace("'", "''")}'
$dest = '{launcherDir.Replace("'", "''")}'
Copy-Item -Path ""$source\*"" -Destination $dest -Recurse -Force

# Cleanup temp files
Remove-Item -Path '{tempDir.Replace("'", "''")}' -Recurse -Force

# Start updated launcher
Start-Process -FilePath '{exePath.Replace("'", "''")}'
";
                File.WriteAllText(updateScriptPath, psContent, System.Text.Encoding.UTF8);

                // Запускаем PowerShell скрипт
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{updateScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                return true;
            }
            catch (Exception ex)
            {
                // Очищаем при ошибке
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }

                throw new Exception($"Update failed: {ex.Message}");
            }
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

        private bool IsNewerVersion(string remote, string current)
        {
            try
            {
                var remoteParts = remote.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(remoteParts.Length, currentParts.Length); i++)
                {
                    var r = i < remoteParts.Length ? remoteParts[i] : 0;
                    var c = i < currentParts.Length ? currentParts[i] : 0;

                    if (r > c) return true;
                    if (r < c) return false;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Disposes resources used by UpdateService
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _httpService.Dispose();
                _apiClient.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
