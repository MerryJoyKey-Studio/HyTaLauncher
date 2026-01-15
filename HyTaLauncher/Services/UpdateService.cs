using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
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

    public class UpdateService
    {
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        private readonly HttpClient _httpClient;
        private const string RepoOwner = "MerryJoyKey-Studio";
        private const string RepoName = "HyTaLauncher";
        private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        public static string CurrentVersion => "1.0.5";

        public UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher");
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(ApiUrl);
                var release = JsonConvert.DeserializeObject<UpdateInfo>(json);

                if (release != null && IsNewerVersion(release.Version, CurrentVersion))
                {
                    return release;
                }
            }
            catch
            {
                // Игнорируем ошибки проверки обновлений
            }

            return null;
        }

        /// <summary>
        /// Скачивает и применяет обновление
        /// </summary>
        public async Task<bool> DownloadAndApplyUpdateAsync(UpdateInfo update, LocalizationService localization)
        {
            var downloadUrl = update.PortableDownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("Portable version not found in release");
            }

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

                // Скачиваем zip
                StatusChanged?.Invoke(localization.Get("update.downloading"));
                await DownloadFileAsync(downloadUrl, zipPath);

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

                // Создаём батник для обновления
                StatusChanged?.Invoke(localization.Get("update.preparing"));
                var updateBatPath = Path.Combine(tempDir, "update.bat");
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? 
                    Path.Combine(launcherDir, "HyTaLauncher.exe");

                var batContent = $@"@echo off
chcp 65001 >nul
echo Updating HyTaLauncher...
echo.

:: Ждём завершения лаунчера
timeout /t 2 /nobreak >nul

:: Копируем новые файлы
echo Copying new files...
xcopy /s /y /q ""{sourceDir}\*"" ""{launcherDir}""

:: Удаляем временные файлы
echo Cleaning up...
rmdir /s /q ""{tempDir}""

:: Запускаем обновлённый лаунчер
echo Starting launcher...
start """" ""{exePath}""

exit
";
                File.WriteAllText(updateBatPath, batContent, System.Text.Encoding.UTF8);

                // Запускаем батник и закрываем лаунчер
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateBatPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
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

        private async Task DownloadFileAsync(string url, string destPath)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            long downloadedBytes = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    ProgressChanged?.Invoke(progress);
                }
            }
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
    }
}
