using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
        public int PrevVersion { get; set; } = 0;  // Предыдущая версия (0 = полная установка)
        public int Version { get; set; } = 0;      // Целевая версия
        public bool IsLatest { get; set; } = false;
        public bool IsFullInstall => PrevVersion == 0; // 0/X.pwr = полная версия
        
        public override string ToString() => Name;
    }

    public class GameLauncher
    {
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        private readonly HttpClient _httpClient;
        private readonly string _launcherDir;  // Папка лаунчера: %AppData%\HyTaLauncher
        private string _gameDir;               // Папка игры: по умолчанию %AppData%\Hytale
        private const int ConsecutiveMissesToStop = 5; // Прекращаем поиск после 5 подряд отсутствующих версий
        
        // Доступные ветки
        public static readonly string[] AvailableBranches = { "release", "pre-release", "beta", "alpha" };
        
        // Кэш всех найденных PWR файлов: branch -> (prevVer, targetVer) -> exists
        private Dictionary<string, HashSet<(int prev, int target)>> _pwrCache = new();
        
        // Отдельный клиент для быстрых проверок
        private readonly HttpClient _quickClient;
        
        // URL зеркала для скачивания
        private const string OFFICIAL_BASE_URL = "https://game-patches.hytale.com/patches/windows/amd64";
        private string MirrorBaseUrl => Config.MirrorUrl;
        
        // Использовать зеркало
        public bool UseMirror { get; set; } = false;
        
        // Папка игры (можно изменить из настроек)
        public string GameDirectory
        {
            get => _gameDir;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _gameDir = Path.Combine(value, "install");
                    EnsureDirectories();
                }
            }
        }
        
        private string GetPatchBaseUrl() => UseMirror && !string.IsNullOrEmpty(MirrorBaseUrl) ? MirrorBaseUrl : OFFICIAL_BASE_URL;

        public GameLauncher()
        {
            // Включаем поддержку TLS 1.2 и 1.3 для старых систем
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            
            var handler = new HttpClientHandler
            {
                // Разрешаем автоматическую декомпрессию
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Увеличиваем таймаут для больших файлов
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HytaleLauncher/1.0");
            
            // Быстрый клиент для HEAD запросов
            _quickClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            _quickClient.Timeout = TimeSpan.FromSeconds(5);
            _quickClient.DefaultRequestHeaders.Add("User-Agent", "HytaleLauncher/1.0");
            
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
            var pwrSet = new HashSet<(int prev, int target)>();
            
            StatusChanged?.Invoke(localization.Get("status.checking_versions"));

            int maxVersion = 0;
            int consecutiveMisses = 0;
            int ver = 1;
            
            // Ищем полные версии (0/X.pwr) пока не будет 5 подряд отсутствующих
            while (consecutiveMisses < ConsecutiveMissesToStop)
            {
                var url = $"{GetPatchBaseUrl()}/{branch}/0/{ver}.pwr";
                
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, url);
                    using var response = await _quickClient.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        maxVersion = ver;
                        pwrSet.Add((0, ver));
                        consecutiveMisses = 0;
                    }
                    else
                    {
                        consecutiveMisses++;
                    }
                }
                catch
                {
                    consecutiveMisses++;
                }
                
                ProgressChanged?.Invoke(Math.Min(ver * 5, 50));
                ver++;
            }

            // Ищем инкрементальные патчи (X/Y.pwr где X < Y)
            for (int prevVer = 1; prevVer < maxVersion; prevVer++)
            {
                consecutiveMisses = 0;
                int targetVer = prevVer + 1;
                
                while (consecutiveMisses < ConsecutiveMissesToStop && targetVer <= maxVersion + ConsecutiveMissesToStop)
                {
                    var url = $"{GetPatchBaseUrl()}/{branch}/{prevVer}/{targetVer}.pwr";
                    
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Head, url);
                        using var response = await _quickClient.SendAsync(request);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            pwrSet.Add((prevVer, targetVer));
                            consecutiveMisses = 0;
                        }
                        else
                        {
                            consecutiveMisses++;
                        }
                    }
                    catch
                    {
                        consecutiveMisses++;
                    }
                    
                    targetVer++;
                }
                
                var progress = 50 + (double)prevVer / Math.Max(maxVersion, 1) * 50;
                ProgressChanged?.Invoke(Math.Min(progress, 100));
            }

            // Сохраняем кэш для использования при установке
            _pwrCache[branch] = pwrSet;

            ProgressChanged?.Invoke(100);
            
            // Создаём список версий для UI (только целевые версии)
            var targetVersions = pwrSet.Select(p => p.target).Distinct().OrderBy(v => v).ToList();
            
            foreach (var v in targetVersions)
            {
                versions.Add(new GameVersion
                {
                    Name = string.Format(localization.Get("main.version_num"), v),
                    PwrFile = $"{v}.pwr",
                    Branch = branch,
                    PrevVersion = 0,
                    Version = v
                });
            }
            
            if (versions.Count == 0)
            {
                versions.Add(new GameVersion
                {
                    Name = localization.Get("main.latest"),
                    PwrFile = "1.pwr",
                    Branch = branch,
                    PrevVersion = 0,
                    Version = 1,
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
                    PrevVersion = 0,
                    Version = latestVersion.Version,
                    IsLatest = true
                });
            }

            return versions;
        }
        
        // Хранит все найденные версии для определения базы
        private List<GameVersion> _allVersions = new();
        
        /// <summary>
        /// Сохраняет версии для использования при установке
        /// </summary>
        public void SetVersionsCache(List<GameVersion> versions)
        {
            _allVersions = versions;
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
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", "HytaleClient.exe");
            var versionFile = Path.Combine(gameDir, ".version");

            // Читаем текущую установленную версию
            int installedVersion = 0;
            if (File.Exists(versionFile))
            {
                int.TryParse(File.ReadAllText(versionFile).Trim(), out installedVersion);
            }

            int targetVersion = version.Version;

            // Если уже установлена нужная версия
            if (installedVersion == targetVersion && File.Exists(clientPath))
            {
                StatusChanged?.Invoke(localization.Get("status.game_installed"));
                ProgressChanged?.Invoke(100);
                return;
            }

            Directory.CreateDirectory(gameDir);
            var cacheDir = Path.Combine(_launcherDir, "cache");

            // Получаем кэш PWR файлов для этой ветки
            if (!_pwrCache.TryGetValue(version.Branch, out var pwrSet))
            {
                pwrSet = new HashSet<(int prev, int target)>();
            }

            // Определяем путь обновления
            var updatePath = FindUpdatePath(installedVersion, targetVersion, pwrSet);

            if (updatePath.Count == 0)
            {
                // Нет пути — качаем полную версию
                updatePath.Add((0, targetVersion));
            }

            // Применяем патчи по порядку
            foreach (var (prevVer, targetVer) in updatePath)
            {
                var pwrFile = $"{targetVer}.pwr";
                var pwrPath = Path.Combine(cacheDir, $"{version.Branch}_{prevVer}_{pwrFile}");
                var pwrUrl = $"{GetPatchBaseUrl()}/{version.Branch}/{prevVer}/{pwrFile}";

                if (prevVer == 0)
                {
                    StatusChanged?.Invoke(string.Format(localization.Get("status.downloading"), $"v{targetVer}"));
                }
                else
                {
                    StatusChanged?.Invoke(string.Format(localization.Get("status.downloading_patch"), prevVer, targetVer));
                }

                // Проверяем размер файла на сервере
                long expectedSize = 0;
                try
                {
                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, pwrUrl);
                    using var headResponse = await _quickClient.SendAsync(headRequest);
                    if (headResponse.IsSuccessStatusCode)
                    {
                        expectedSize = headResponse.Content.Headers.ContentLength ?? 0;
                    }
                }
                catch { }

                // Проверяем существующий файл
                bool needsDownload = true;
                if (File.Exists(pwrPath) && expectedSize > 0)
                {
                    var fileInfo = new FileInfo(pwrPath);
                    if (fileInfo.Length == expectedSize)
                    {
                        needsDownload = false;
                        StatusChanged?.Invoke(localization.Get("status.pwr_cached"));
                    }
                    else
                    {
                        // Файл неполный — удаляем и качаем заново
                        StatusChanged?.Invoke(localization.Get("status.redownloading"));
                        File.Delete(pwrPath);
                    }
                }
                else if (File.Exists(pwrPath) && expectedSize == 0)
                {
                    // Не смогли получить размер — используем существующий файл
                    needsDownload = false;
                }

                if (needsDownload)
                {
                    await DownloadFileAsync(pwrUrl, pwrPath, expectedSize);
                    
                    // Проверяем размер после скачивания
                    if (expectedSize > 0)
                    {
                        var downloadedSize = new FileInfo(pwrPath).Length;
                        if (downloadedSize != expectedSize)
                        {
                            File.Delete(pwrPath);
                            throw new Exception($"Download incomplete: {downloadedSize}/{expectedSize} bytes");
                        }
                    }
                }

                StatusChanged?.Invoke(localization.Get("status.applying_patch"));
                await ApplyPwrAsync(pwrPath, gameDir, localization);
                
                // Обновляем версию после каждого патча
                File.WriteAllText(versionFile, targetVer.ToString());
            }

            StatusChanged?.Invoke(localization.Get("status.game_installed_done"));
        }

        /// <summary>
        /// Находит оптимальный путь обновления от текущей версии до целевой
        /// </summary>
        private List<(int prev, int target)> FindUpdatePath(int fromVersion, int toVersion, HashSet<(int prev, int target)> available)
        {
            if (fromVersion >= toVersion)
                return new List<(int, int)>();

            // Пробуем найти прямой патч
            if (available.Contains((fromVersion, toVersion)))
            {
                return new List<(int, int)> { (fromVersion, toVersion) };
            }

            // Если нет прямого патча, ищем цепочку
            // Простой жадный алгоритм: ищем патч с максимальным шагом
            var path = new List<(int prev, int target)>();
            int currentVersion = fromVersion;

            while (currentVersion < toVersion)
            {
                // Ищем патч от текущей версии с максимальным целевым значением <= toVersion
                var bestPatch = available
                    .Where(p => p.prev == currentVersion && p.target <= toVersion)
                    .OrderByDescending(p => p.target)
                    .FirstOrDefault();

                if (bestPatch == default)
                {
                    // Нет патча — нужна полная установка
                    return new List<(int, int)> { (0, toVersion) };
                }

                path.Add(bestPatch);
                currentVersion = bestPatch.target;
            }

            return path;
        }

        private async Task DownloadFileAsync(string url, string destPath, long expectedSize = -1)
        {
            var tempPath = destPath + ".tmp";
            long existingBytes = 0;
            
            // Проверяем есть ли частично скачанный файл
            if (File.Exists(tempPath))
            {
                existingBytes = new FileInfo(tempPath).Length;
            }

            // Создаём запрос с поддержкой докачки
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            if (existingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
                StatusChanged?.Invoke($"Resuming download from {existingBytes / 1024 / 1024}MB...");
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            // Если сервер не поддерживает Range или файл изменился — качаем заново
            if (existingBytes > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            {
                existingBytes = 0;
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength ?? -1;
            var totalBytes = existingBytes + contentLength;
            
            if (expectedSize > 0)
            {
                totalBytes = expectedSize;
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, 
                existingBytes > 0 ? FileMode.Append : FileMode.Create, 
                FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            var downloadedBytes = existingBytes;

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
            
            // Закрываем поток перед переименованием
            await fileStream.FlushAsync();
            fileStream.Close();
            
            // Переименовываем .tmp в финальный файл
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempPath, destPath);
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

            // Проверяем существование файлов
            if (!File.Exists(pwrPath))
            {
                throw new FileNotFoundException($"PWR file not found: {pwrPath}");
            }
            
            if (!File.Exists(butlerPath))
            {
                throw new FileNotFoundException($"Butler not found: {butlerPath}");
            }

            var stagingDir = Path.Combine(gameDir, "staging-temp");
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(gameDir);

            StatusChanged?.Invoke(localization.Get("status.applying_patch"));
            
            ProgressChanged?.Invoke(-1);

            var arguments = $"apply --staging-dir \"{stagingDir}\" \"{pwrPath}\" \"{gameDir}\"";
            
            // Логируем команду
            var logPath = Path.Combine(_launcherDir, "butler.log");
            File.AppendAllText(logPath, $"\n[{DateTime.Now}] Running: {butlerPath} {arguments}\n");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = butlerPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _launcherDir
                }
            };

            var stdout = new System.Text.StringBuilder();
            var stderr = new System.Text.StringBuilder();
            
            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(600000));
            
            // Логируем результат
            File.AppendAllText(logPath, $"Exit code: {process.ExitCode}\n");
            File.AppendAllText(logPath, $"Stdout: {stdout}\n");
            File.AppendAllText(logPath, $"Stderr: {stderr}\n");
            
            if (!completed)
            {
                process.Kill();
                throw new Exception("Butler timeout (10 min)");
            }

            if (process.ExitCode != 0)
            {
                var errorMsg = stderr.ToString().Trim();
                if (string.IsNullOrEmpty(errorMsg))
                    errorMsg = stdout.ToString().Trim();
                if (string.IsNullOrEmpty(errorMsg))
                    errorMsg = "Unknown error";
                    
                throw new Exception($"Butler error (code {process.ExitCode}): {errorMsg}");
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
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", "HytaleClient.exe");
            var javaExe = GetJavaPath();

            if (!File.Exists(clientPath))
                throw new FileNotFoundException("Game client not found");

            var uuid = GetOrCreateUuid(playerName);

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

        private string GetOrCreateUuid(string playerName)
        {
            var uuidFile = Path.Combine(_launcherDir, "players.json");
            var players = new Dictionary<string, string>();
            
            // Загружаем существующие UUID
            if (File.Exists(uuidFile))
            {
                try
                {
                    var json = File.ReadAllText(uuidFile);
                    players = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
                }
                catch { }
            }
            
            // Ищем или создаём UUID для никнейма
            var key = playerName.ToLowerInvariant();
            if (!players.TryGetValue(key, out var uuid))
            {
                uuid = Guid.NewGuid().ToString();
                players[key] = uuid;
                
                // Сохраняем
                try
                {
                    File.WriteAllText(uuidFile, JsonConvert.SerializeObject(players, Formatting.Indented));
                }
                catch { }
            }
            
            return uuid;
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
