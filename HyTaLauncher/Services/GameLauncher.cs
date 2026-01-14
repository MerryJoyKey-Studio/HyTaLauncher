using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class GameVersion
    {
        public string Name { get; set; } = "";
        public string PwrFile { get; set; } = "";
        public string Branch { get; set; } = "release";
        public bool IsLatest { get; set; } = false;
        
        public override string ToString() => Name;
    }

    public class GameLauncher
    {
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        private readonly HttpClient _httpClient;
        private readonly string _launcherDir;  // Папка лаунчера: %AppData%\HyTaLauncher
        private readonly string _gameDir;      // Папка игры: %AppData%\Hytale
        private const int MaxPwrCheck = 20;

        public GameLauncher()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _launcherDir = Path.Combine(roaming, "HyTaLauncher");
            _gameDir = Path.Combine(roaming, "Hytale", "install");
            
            EnsureDirectories();
        }

        /// <summary>
        /// Получает список доступных версий игры
        /// </summary>
        public async Task<List<GameVersion>> GetAvailableVersionsAsync(string branch, LocalizationService localization)
        {
            var versions = new List<GameVersion>();
            
            StatusChanged?.Invoke(localization.Get("status.checking_versions"));

            for (int i = 1; i <= MaxPwrCheck; i++)
            {
                var pwrFile = $"{i}.pwr";
                var url = $"https://game-patches.hytale.com/patches/windows/amd64/{branch}/0/{pwrFile}";
                
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, url);
                    using var response = await _httpClient.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        versions.Add(new GameVersion
                        {
                            Name = string.Format(localization.Get("main.version_num"), i),
                            PwrFile = pwrFile,
                            Branch = branch
                        });
                        
                        var progress = (double)i / MaxPwrCheck * 100;
                        ProgressChanged?.Invoke(progress);
                    }
                    else
                    {
                        break;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (HttpRequestException)
                {
                    continue;
                }
            }

            ProgressChanged?.Invoke(100);
            
            if (versions.Count == 0)
            {
                versions.Add(new GameVersion
                {
                    Name = localization.Get("main.latest"),
                    PwrFile = "1.pwr",
                    Branch = branch,
                    IsLatest = true
                });
            }
            else
            {
                var latestVersion = versions.Last();
                versions.Insert(0, new GameVersion
                {
                    Name = localization.Get("main.latest"),
                    PwrFile = latestVersion.PwrFile,
                    Branch = branch,
                    IsLatest = true
                });
            }

            return versions;
        }

        private void EnsureDirectories()
        {
            // Папки лаунчера
            Directory.CreateDirectory(_launcherDir);
            Directory.CreateDirectory(Path.Combine(_launcherDir, "cache"));
            Directory.CreateDirectory(Path.Combine(_launcherDir, "butler"));
            
            // Папки игры: %AppData%\Hytale\install\release\package\...
            Directory.CreateDirectory(Path.Combine(_gameDir, "release", "package", "jre", "latest"));
            Directory.CreateDirectory(Path.Combine(_gameDir, "release", "package", "game", "latest"));
        }

        public async Task LaunchGameAsync(string playerName, GameVersion version, LocalizationService localization)
        {
            StatusChanged?.Invoke(localization.Get("status.checking_java"));
            await DownloadJreAsync(localization);

            StatusChanged?.Invoke(localization.Get("status.checking_game"));
            await DownloadGameAsync(version, localization);

            StatusChanged?.Invoke(localization.Get("status.launching"));
            LaunchGame(playerName, version);
        }

        private async Task DownloadJreAsync(LocalizationService localization)
        {
            var jreDir = Path.Combine(_gameDir, "release", "package", "jre", "latest");
            var javaExe = Path.Combine(jreDir, "bin", "java.exe");

            if (File.Exists(javaExe))
            {
                ProgressChanged?.Invoke(100);
                return;
            }

            StatusChanged?.Invoke(localization.Get("status.downloading_jre"));

            try
            {
                var response = await _httpClient.GetStringAsync(
                    "https://launcher.hytale.com/version/release/jre.json");
                var jreData = JsonConvert.DeserializeObject<JreData>(response);

                if (jreData?.DownloadUrl == null)
                    throw new Exception("Failed to get JRE info");

                var osKey = "windows";
                var archKey = Environment.Is64BitOperatingSystem ? "amd64" : "x86";

                if (!jreData.DownloadUrl.TryGetValue(osKey, out var osData) ||
                    !osData.TryGetValue(archKey, out var platform))
                {
                    throw new Exception($"JRE not available for {osKey}/{archKey}");
                }

                var cacheDir = Path.Combine(_launcherDir, "cache");
                var fileName = Path.GetFileName(platform.Url);
                var cachePath = Path.Combine(cacheDir, fileName);

                await DownloadFileAsync(platform.Url, cachePath);

                StatusChanged?.Invoke(localization.Get("status.extracting_java"));
                await ExtractArchiveAsync(cachePath, jreDir);

                FlattenDirectory(jreDir);

                File.Delete(cachePath);
            }
            catch (HttpRequestException)
            {
                StatusChanged?.Invoke(localization.Get("status.system_java"));
            }
        }

        private async Task DownloadGameAsync(GameVersion version, LocalizationService localization)
        {
            var folderName = version.IsLatest ? "latest" : version.PwrFile.Replace(".pwr", "");
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", folderName);
            var clientPath = Path.Combine(gameDir, "Client", "HytaleClient.exe");

            if (File.Exists(clientPath))
            {
                StatusChanged?.Invoke(localization.Get("status.game_installed"));
                ProgressChanged?.Invoke(100);
                return;
            }

            Directory.CreateDirectory(gameDir);
            
            var cacheDir = Path.Combine(_launcherDir, "cache");
            var pwrPath = Path.Combine(cacheDir, $"{version.Branch}_{version.PwrFile}");

            if (!File.Exists(pwrPath))
            {
                StatusChanged?.Invoke(string.Format(localization.Get("status.downloading"), version.Name));
                var pwrUrl = $"https://game-patches.hytale.com/patches/windows/amd64/{version.Branch}/0/{version.PwrFile}";
                await DownloadFileAsync(pwrUrl, pwrPath);
            }
            else
            {
                StatusChanged?.Invoke(localization.Get("status.pwr_cached"));
                ProgressChanged?.Invoke(100);
            }

            StatusChanged?.Invoke(localization.Get("status.installing"));
            await ApplyPwrAsync(pwrPath, gameDir, localization);
        }

        private async Task DownloadFileAsync(string url, string destPath)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;

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

        private async Task ExtractArchiveAsync(string archivePath, string destDir)
        {
            await Task.Run(() =>
            {
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir, true);
                }
                else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    // For tar.gz, we'd need additional library or use system tools
                    // For simplicity, assuming Windows uses .zip
                    throw new NotSupportedException("tar.gz extraction not implemented");
                }
            });
        }

        private void FlattenDirectory(string dir)
        {
            var subdirs = Directory.GetDirectories(dir);
            if (subdirs.Length == 1)
            {
                var subdir = subdirs[0];
                foreach (var file in Directory.GetFiles(subdir))
                {
                    var destFile = Path.Combine(dir, Path.GetFileName(file));
                    File.Move(file, destFile, true);
                }
                foreach (var folder in Directory.GetDirectories(subdir))
                {
                    var destFolder = Path.Combine(dir, Path.GetFileName(folder));
                    Directory.Move(folder, destFolder);
                }
                Directory.Delete(subdir, true);
            }
        }

        private async Task ApplyPwrAsync(string pwrPath, string gameDir, LocalizationService localization)
        {
            var butlerPath = await EnsureButlerAsync(localization);

            var stagingDir = Path.Combine(gameDir, "staging-temp");
            Directory.CreateDirectory(stagingDir);

            StatusChanged?.Invoke(localization.Get("status.applying_patch"));
            
            ProgressChanged?.Invoke(-1);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = butlerPath,
                    Arguments = $"apply --staging-dir \"{stagingDir}\" \"{pwrPath}\" \"{gameDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(600000));
            
            if (!completed)
            {
                process.Kill();
                throw new Exception("Butler timeout");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"Butler error (code {process.ExitCode})");
            }

            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            
            ProgressChanged?.Invoke(100);
            StatusChanged?.Invoke(localization.Get("status.game_installed_done"));
        }

        private async Task<string> EnsureButlerAsync(LocalizationService localization)
        {
            var butlerDir = Path.Combine(_launcherDir, "butler");
            var butlerExe = Path.Combine(butlerDir, "butler.exe");

            if (File.Exists(butlerExe))
                return butlerExe;

            Directory.CreateDirectory(butlerDir);

            StatusChanged?.Invoke(localization.Get("status.downloading_butler"));

            var butlerUrl = "https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default";
            var zipPath = Path.Combine(_launcherDir, "cache", "butler.zip");

            await DownloadFileAsync(butlerUrl, zipPath);

            StatusChanged?.Invoke(localization.Get("status.extracting_butler"));
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, butlerDir, true);

            File.Delete(zipPath);

            return butlerExe;
        }

        private void LaunchGame(string playerName, GameVersion version)
        {
            var folderName = version.IsLatest ? "latest" : version.PwrFile.Replace(".pwr", "");
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", folderName);
            var clientPath = Path.Combine(gameDir, "Client", "HytaleClient.exe");
            var javaExe = GetJavaPath();

            if (!File.Exists(clientPath))
                throw new FileNotFoundException("Клиент игры не найден");

            var uuid = Guid.NewGuid().ToString();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = clientPath,
                    Arguments = $"--app-dir \"{gameDir}\" --java-exec \"{javaExe}\" --auth-mode offline --uuid {uuid} --name {playerName}",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(clientPath)
                }
            };

            process.Start();
        }

        private string GetJavaPath()
        {
            var javaExe = Path.Combine(_gameDir, "release", "package", "jre", "latest", "bin", "java.exe");
            return File.Exists(javaExe) ? javaExe : "java";
        }
    }

    public class JreData
    {
        [JsonProperty("version")]
        public string? Version { get; set; }

        [JsonProperty("download_url")]
        public Dictionary<string, Dictionary<string, JrePlatform>>? DownloadUrl { get; set; }
    }

    public class JrePlatform
    {
        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = "";
    }
}
