using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Platform detection helper
    /// </summary>
    public static class PlatformHelper
    {
        public enum Platform { Windows, Linux, MacOS }
        public enum Architecture { Amd64, Arm64, X86 }
        
        public static Platform CurrentPlatform
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Platform.Windows;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return Platform.Linux;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Platform.MacOS;
                return Platform.Windows; // Default fallback
            }
        }
        
        public static Architecture CurrentArchitecture
        {
            get
            {
                return RuntimeInformation.OSArchitecture switch
                {
                    System.Runtime.InteropServices.Architecture.X64 => Architecture.Amd64,
                    System.Runtime.InteropServices.Architecture.Arm64 => Architecture.Arm64,
                    System.Runtime.InteropServices.Architecture.X86 => Architecture.X86,
                    _ => Architecture.Amd64
                };
            }
        }
        
        public static string GetPlatformString()
        {
            return CurrentPlatform switch
            {
                Platform.Linux => "linux",
                Platform.MacOS => "darwin",
                _ => "windows"
            };
        }
        
        public static string GetArchitectureString()
        {
            return CurrentArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "amd64"
            };
        }
        
        public static string GetExecutableExtension()
        {
            return CurrentPlatform == Platform.Windows ? ".exe" : "";
        }
        
        public static string GetJavaExecutable()
        {
            return CurrentPlatform == Platform.Windows ? "java.exe" : "java";
        }
        
        public static string GetGameExecutable()
        {
            return CurrentPlatform switch
            {
                Platform.Linux => "HytaleClient",
                Platform.MacOS => "HytaleClient",
                _ => "HytaleClient.exe"
            };
        }
        
        public static bool IsOnlinefixSupported()
        {
            // Online fix only works on Windows
            return CurrentPlatform == Platform.Windows;
        }
    }

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
        
        // URL для скачивания (зависит от платформы)
        private string GetOfficialBaseUrl() => 
            $"https://game-patches.hytale.com/patches/{PlatformHelper.GetPlatformString()}/{PlatformHelper.GetArchitectureString()}";
        private string MirrorBaseUrl => Config.MirrorUrl;
        
        // Использовать зеркало
        public bool UseMirror { get; set; } = false;
        
        // Всегда скачивать полную версию (без инкрементальных патчей)
        public bool AlwaysFullDownload { get; set; } = true;
        
        // Кастомные аргументы запуска игры
        public string CustomGameArgs { get; set; } = "";
        
        // Selected modpack ID for game launch (null = use default UserData)
        public string? SelectedModpackId { get; set; }
        
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
        
        private string GetPatchBaseUrl() => UseMirror && !string.IsNullOrEmpty(MirrorBaseUrl) ? MirrorBaseUrl : GetOfficialBaseUrl();

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
            
            // Папки игры для всех веток: %AppData%\Hytale\install\{branch}\package\...
            foreach (var branch in AvailableBranches)
            {
                Directory.CreateDirectory(Path.Combine(_gameDir, branch, "package", "jre", "latest"));
                Directory.CreateDirectory(Path.Combine(_gameDir, branch, "package", "game", "latest"));
            }
        }

        public async Task LaunchGameAsync(string playerName, GameVersion version, LocalizationService localization)
        {
            LogService.LogGame($"Starting launch: player={playerName}, version={version.Version}, branch={version.Branch}");
            LogService.LogGameVerbose($"Version details: PwrFile={version.PwrFile}, PrevVersion={version.PrevVersion}, IsLatest={version.IsLatest}");
            
            StatusChanged?.Invoke(localization.Get("status.checking_java"));
            await DownloadJreAsync(version.Branch, localization);

            StatusChanged?.Invoke(localization.Get("status.checking_game"));
            await DownloadGameAsync(version, localization);

            // Проверяем целостность exe перед запуском
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", PlatformHelper.GetGameExecutable());
            
            LogService.LogGameVerbose($"Checking executable: {clientPath}");
            
            if (!IsValidExecutable(clientPath))
            {
                LogService.LogError($"Invalid executable detected: {clientPath}");
                throw new Exception(localization.Get("error.corrupted_files"));
            }

            LogService.LogGameVerbose("Executable validation passed");
            StatusChanged?.Invoke(localization.Get("status.launching"));
            
            try
            {
                LaunchGame(playerName, version);
            }
            catch (Exception ex)
            {
                // Если ошибка запуска связана с повреждёнными файлами - предлагаем переустановку
                if (ex.Message.Contains("параллельная конфигурация") || 
                    ex.Message.Contains("side-by-side configuration") ||
                    ex.Message.Contains("VCRUNTIME") ||
                    ex.Message.Contains("MSVCP") ||
                    ex.Message.Contains("not a valid application") ||
                    ex.Message.Contains("не является приложением") ||
                    ex.Message.Contains("BadImageFormatException"))
                {
                    throw new Exception(localization.Get("error.corrupted_files"));
                }
                throw;
            }
        }

        /// <summary>
        /// Проверяет что исполняемый файл является валидным
        /// Windows: PE header check, Linux/macOS: ELF/Mach-O header check
        /// </summary>
        private bool IsValidExecutable(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                var fileInfo = new FileInfo(path);
                
                // Минимальный размер для валидного исполняемого файла
                if (fileInfo.Length < 4096)
                    return false;

                using var stream = File.OpenRead(path);
                var buffer = new byte[4];
                stream.Read(buffer, 0, 4);

                // Platform-specific validation
                switch (PlatformHelper.CurrentPlatform)
                {
                    case PlatformHelper.Platform.Windows:
                        // PE header check (MZ signature)
                        if (buffer[0] != 0x4D || buffer[1] != 0x5A)
                            return false;

                        // Читаем смещение PE header (offset 0x3C)
                        stream.Seek(0x3C, SeekOrigin.Begin);
                        var peOffsetBuffer = new byte[4];
                        stream.Read(peOffsetBuffer, 0, 4);
                        var peOffset = BitConverter.ToInt32(peOffsetBuffer, 0);

                        // Проверяем PE signature
                        if (peOffset > 0 && peOffset < fileInfo.Length - 4)
                        {
                            stream.Seek(peOffset, SeekOrigin.Begin);
                            var peBuffer = new byte[4];
                            stream.Read(peBuffer, 0, 4);
                            
                            // PE\0\0 = 0x50 0x45 0x00 0x00
                            if (peBuffer[0] != 0x50 || peBuffer[1] != 0x45 || peBuffer[2] != 0x00 || peBuffer[3] != 0x00)
                                return false;
                        }
                        else
                        {
                            return false;
                        }
                        break;

                    case PlatformHelper.Platform.Linux:
                        // ELF header check: 0x7F 'E' 'L' 'F'
                        if (buffer[0] != 0x7F || buffer[1] != 0x45 || buffer[2] != 0x4C || buffer[3] != 0x46)
                            return false;
                        break;

                    case PlatformHelper.Platform.MacOS:
                        // Mach-O header check
                        // 64-bit: 0xFEEDFACF (little-endian: CF FA ED FE)
                        // 32-bit: 0xFEEDFACE (little-endian: CE FA ED FE)
                        // Universal: 0xCAFEBABE (big-endian: CA FE BA BE)
                        bool isMachO64 = buffer[0] == 0xCF && buffer[1] == 0xFA && buffer[2] == 0xED && buffer[3] == 0xFE;
                        bool isMachO32 = buffer[0] == 0xCE && buffer[1] == 0xFA && buffer[2] == 0xED && buffer[3] == 0xFE;
                        bool isUniversal = buffer[0] == 0xCA && buffer[1] == 0xFE && buffer[2] == 0xBA && buffer[3] == 0xBE;
                        if (!isMachO64 && !isMachO32 && !isUniversal)
                            return false;
                        break;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadJreAsync(string branch, LocalizationService localization)
        {
            var jreDir = Path.Combine(_gameDir, branch, "package", "jre", "latest");
            var javaExe = Path.Combine(jreDir, "bin", PlatformHelper.GetJavaExecutable());

            LogService.LogGameVerbose($"Checking JRE at: {javaExe}");

            if (File.Exists(javaExe))
            {
                LogService.LogGameVerbose("JRE already installed");
                ProgressChanged?.Invoke(100);
                return;
            }

            LogService.LogGame($"Downloading JRE for branch: {branch}");
            StatusChanged?.Invoke(localization.Get("status.downloading_jre"));

            try
            {
                var response = await _httpClient.GetStringAsync(
                    $"https://launcher.hytale.com/version/{branch}/jre.json");
                var jreData = JsonConvert.DeserializeObject<JreData>(response);

                if (jreData?.DownloadUrl == null)
                    throw new Exception("Failed to get JRE info");

                var osKey = PlatformHelper.GetPlatformString();
                var archKey = PlatformHelper.GetArchitectureString();

                if (!jreData.DownloadUrl.TryGetValue(osKey, out var osData) ||
                    !osData.TryGetValue(archKey, out var platform))
                {
                    throw new Exception($"JRE not available for {osKey}/{archKey}");
                }

                var cacheDir = Path.Combine(_launcherDir, "cache");
                var fileName = Path.GetFileName(platform.Url);
                var cachePath = Path.Combine(cacheDir, fileName);

                LogService.LogGameVerbose($"JRE download URL: {platform.Url}");
                LogService.LogGameVerbose($"JRE cache path: {cachePath}");

                await DownloadFileAsync(platform.Url, cachePath);

                StatusChanged?.Invoke(localization.Get("status.extracting_java"));
                LogService.LogGameVerbose($"Extracting JRE to: {jreDir}");
                await ExtractArchiveAsync(cachePath, jreDir);

                FlattenDirectory(jreDir);

                File.Delete(cachePath);
                LogService.LogGame("JRE installation completed");
            }
            catch (HttpRequestException ex)
            {
                LogService.LogGameVerbose($"JRE download failed, using system Java: {ex.Message}");
                StatusChanged?.Invoke(localization.Get("status.system_java"));
            }
        }

        private async Task DownloadGameAsync(GameVersion version, LocalizationService localization)
        {
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", PlatformHelper.GetGameExecutable());
            var versionFile = Path.Combine(gameDir, ".version");

            LogService.LogGame($"DownloadGameAsync: target version={version.Version}, branch={version.Branch}");
            LogService.LogGameVerbose($"Game directory: {gameDir}");
            LogService.LogGameVerbose($"Client path: {clientPath}");

            // Читаем текущую установленную версию
            int installedVersion = 0;
            if (File.Exists(versionFile))
            {
                int.TryParse(File.ReadAllText(versionFile).Trim(), out installedVersion);
            }
            
            LogService.LogGameVerbose($"Installed version: {installedVersion}, target version: {version.Version}");

            int targetVersion = version.Version;

            // Если уже установлена нужная версия
            if (installedVersion == targetVersion && File.Exists(clientPath))
            {
                LogService.LogGameVerbose("Game already installed at target version");
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
            var updatePath = new List<(int prev, int target)>();
            
            // Если включено "Всегда полная версия" или нет установленной версии - качаем полную
            if (AlwaysFullDownload || installedVersion == 0)
            {
                LogService.LogGameVerbose("Using full download (AlwaysFullDownload or fresh install)");
                updatePath.Add((0, targetVersion));
            }
            else
            {
                updatePath = FindUpdatePath(installedVersion, targetVersion, pwrSet);
                LogService.LogGameVerbose($"Update path: {string.Join(" -> ", updatePath.Select(p => $"{p.prev}/{p.target}"))}");

                if (updatePath.Count == 0)
                {
                    // Нет пути — качаем полную версию
                    LogService.LogGameVerbose("No update path found, using full install");
                    updatePath.Add((0, targetVersion));
                }
            }

            // Применяем патчи по порядку
            foreach (var (prevVer, targetVer) in updatePath)
            {
                var pwrFile = $"{targetVer}.pwr";
                var pwrPath = Path.Combine(cacheDir, $"{version.Branch}_{prevVer}_{pwrFile}");
                var pwrUrl = $"{GetPatchBaseUrl()}/{version.Branch}/{prevVer}/{pwrFile}";

                LogService.LogGame($"Processing patch: {prevVer} -> {targetVer}");
                LogService.LogGameVerbose($"PWR URL: {pwrUrl}");
                LogService.LogGameVerbose($"PWR local path: {pwrPath}");

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
                    LogService.LogGameVerbose($"Existing PWR file size: {fileInfo.Length}, expected: {expectedSize}");
                    if (fileInfo.Length == expectedSize)
                    {
                        needsDownload = false;
                        LogService.LogGameVerbose("PWR file already cached and valid");
                        StatusChanged?.Invoke(localization.Get("status.pwr_cached"));
                    }
                    else
                    {
                        // Файл неполный — удаляем и качаем заново
                        LogService.LogGameVerbose("PWR file incomplete, re-downloading");
                        StatusChanged?.Invoke(localization.Get("status.redownloading"));
                        File.Delete(pwrPath);
                    }
                }
                else if (File.Exists(pwrPath) && expectedSize == 0)
                {
                    // Не смогли получить размер — используем существующий файл
                    LogService.LogGameVerbose("Using existing PWR file (couldn't verify size)");
                    needsDownload = false;
                }

                if (needsDownload)
                {
                    LogService.LogGame($"Downloading PWR file: {pwrUrl}");
                    await DownloadFileAsync(pwrUrl, pwrPath, expectedSize);
                    
                    // Проверяем размер после скачивания
                    if (expectedSize > 0)
                    {
                        var downloadedSize = new FileInfo(pwrPath).Length;
                        LogService.LogGameVerbose($"Downloaded size: {downloadedSize}, expected: {expectedSize}");
                        if (downloadedSize != expectedSize)
                        {
                            LogService.LogError($"Download incomplete: {downloadedSize}/{expectedSize} bytes");
                            File.Delete(pwrPath);
                            throw new Exception($"Download incomplete: {downloadedSize}/{expectedSize} bytes");
                        }
                    }
                    LogService.LogGameVerbose("PWR download completed successfully");
                }

                StatusChanged?.Invoke(localization.Get("status.applying_patch"));
                
                try
                {
                    LogService.LogGame($"Applying patch: {pwrPath}");
                    await ApplyPwrAsync(pwrPath, gameDir, localization);
                    LogService.LogGameVerbose("Patch applied successfully");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Patch failed: {prevVer}->{targetVer}", ex);
                    
                    // Если это инкрементальный патч и он не сработал - пробуем полную установку
                    if (prevVer > 0)
                    {
                        var logPath = Path.Combine(_launcherDir, "butler.log");
                        File.AppendAllText(logPath, $"[{DateTime.Now}] Patch {prevVer}->{targetVer} failed: {ex.Message}\n");
                        
                        StatusChanged?.Invoke(localization.Get("status.patch_failed_full"));
                        
                        // Удаляем повреждённую установку
                        if (Directory.Exists(gameDir))
                        {
                            try { Directory.Delete(gameDir, true); } catch { }
                        }
                        
                        // Удаляем файл версии
                        if (File.Exists(versionFile))
                        {
                            try { File.Delete(versionFile); } catch { }
                        }
                        
                        // Рекурсивно вызываем установку с нуля (будет использована полная версия)
                        await DownloadGameAsync(version, localization);
                        return;
                    }
                    
                    // Если это была полная установка - пробрасываем ошибку
                    throw;
                }
                
                // Проверяем целостность после патча (валидный PE файл)
                LogService.LogGameVerbose($"Validating executable after patch: {clientPath}");
                if (!IsValidExecutable(clientPath))
                {
                    LogService.LogError($"Corrupted executable after patch {prevVer}->{targetVer}");
                    
                    // Если это инкрементальный патч - пробуем полную установку
                    if (prevVer > 0)
                    {
                        var logPath = Path.Combine(_launcherDir, "butler.log");
                        File.AppendAllText(logPath, $"[{DateTime.Now}] Corrupted exe after patch {prevVer}->{targetVer}, retrying with full install\n");
                        
                        StatusChanged?.Invoke(localization.Get("status.corrupted_retry_full"));
                        
                        // Удаляем повреждённую установку
                        if (Directory.Exists(gameDir))
                        {
                            try { Directory.Delete(gameDir, true); } catch { }
                        }
                        
                        if (File.Exists(versionFile))
                        {
                            try { File.Delete(versionFile); } catch { }
                        }
                        
                        // Рекурсивно вызываем установку с нуля
                        await DownloadGameAsync(version, localization);
                        return;
                    }
                    
                    // Если это была полная установка - ошибка
                    if (File.Exists(versionFile))
                        File.Delete(versionFile);
                    throw new Exception("Game files corrupted after patch. Please try reinstalling.");
                }
                
                // Обновляем версию после каждого патча
                File.WriteAllText(versionFile, targetVer.ToString());
                LogService.LogGameVerbose($"Version file updated to: {targetVer}");
            }

            LogService.LogGame("Game installation completed successfully");
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
            LogService.LogNetworkVerbose($"Starting download: {url}");
            LogService.LogNetworkVerbose($"Destination: {destPath}");
            
            var tempPath = destPath + ".tmp";
            long existingBytes = 0;
            
            // Проверяем есть ли частично скачанный файл
            if (File.Exists(tempPath))
            {
                existingBytes = new FileInfo(tempPath).Length;
                LogService.LogNetworkVerbose($"Resuming from existing temp file: {existingBytes} bytes");
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
                LogService.LogNetworkVerbose("Server doesn't support resume, starting fresh download");
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
            
            LogService.LogNetworkVerbose($"Content length: {contentLength}, total expected: {totalBytes}");

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
            
            LogService.LogNetworkVerbose($"Download completed: {downloadedBytes} bytes");
        }

        private async Task ExtractArchiveAsync(string archivePath, string destDir)
        {
            await Task.Run(() =>
            {
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir, true);
                }
                else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || 
                         archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                {
                    // Use system tar on Linux/macOS
                    if (PlatformHelper.CurrentPlatform != PlatformHelper.Platform.Windows)
                    {
                        Directory.CreateDirectory(destDir);
                        var tar = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "tar",
                                Arguments = $"-xzf \"{archivePath}\" -C \"{destDir}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true
                            }
                        };
                        tar.Start();
                        tar.WaitForExit(120000); // 2 min timeout
                        if (tar.ExitCode != 0)
                        {
                            var error = tar.StandardError.ReadToEnd();
                            throw new Exception($"tar extraction failed: {error}");
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("tar.gz extraction not supported on Windows");
                    }
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
            
            // ВАЖНО: Очищаем staging директорию перед использованием
            // Остатки от предыдущих патчей могут вызвать повреждение
            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, true);
                }
                catch
                {
                    // Если не удалось удалить - пробуем очистить содержимое
                    try
                    {
                        foreach (var file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
                        {
                            File.Delete(file);
                        }
                        foreach (var dir in Directory.GetDirectories(stagingDir))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch { }
                }
            }
            
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(gameDir);

            StatusChanged?.Invoke(localization.Get("status.applying_patch"));
            
            ProgressChanged?.Invoke(-1);

            var arguments = $"apply --staging-dir \"{stagingDir}\" \"{pwrPath}\" \"{gameDir}\"";
            
            // Логируем команду
            var logPath = Path.Combine(_launcherDir, "butler.log");
            File.AppendAllText(logPath, $"\n[{DateTime.Now}] Running: {butlerPath} {arguments}\n");
            File.AppendAllText(logPath, $"PWR file size: {new FileInfo(pwrPath).Length} bytes\n");
            File.AppendAllText(logPath, $"Game dir exists: {Directory.Exists(gameDir)}\n");

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
                
                // Очищаем staging при таймауте
                try
                {
                    if (Directory.Exists(stagingDir))
                        Directory.Delete(stagingDir, true);
                }
                catch { }
                
                throw new Exception("Butler timeout (10 min)");
            }

            if (process.ExitCode != 0)
            {
                var errorMsg = stderr.ToString().Trim();
                if (string.IsNullOrEmpty(errorMsg))
                    errorMsg = stdout.ToString().Trim();
                if (string.IsNullOrEmpty(errorMsg))
                    errorMsg = "Unknown error";
                
                // Очищаем staging при ошибке
                try
                {
                    if (Directory.Exists(stagingDir))
                        Directory.Delete(stagingDir, true);
                }
                catch { }
                    
                throw new Exception($"Butler error (code {process.ExitCode}): {errorMsg}");
            }

            // Очищаем staging после успешного применения
            try
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
            catch
            {
                // Не критично если не удалось удалить
            }
            
            ProgressChanged?.Invoke(100);
            StatusChanged?.Invoke(localization.Get("status.game_installed_done"));
        }

        private async Task<string> EnsureButlerAsync(LocalizationService localization)
        {
            var butlerDir = Path.Combine(_launcherDir, "butler");
            var butlerExeName = "butler" + PlatformHelper.GetExecutableExtension();
            var butlerExe = Path.Combine(butlerDir, butlerExeName);

            if (File.Exists(butlerExe))
                return butlerExe;

            Directory.CreateDirectory(butlerDir);

            StatusChanged?.Invoke(localization.Get("status.downloading_butler"));

            // Butler URL pattern: https://broth.itch.zone/butler/{platform}-{arch}/LATEST/archive/default
            var butlerPlatform = PlatformHelper.CurrentPlatform switch
            {
                PlatformHelper.Platform.Linux => "linux",
                PlatformHelper.Platform.MacOS => "darwin",
                _ => "windows"
            };
            var butlerArch = PlatformHelper.GetArchitectureString();
            var butlerUrl = $"https://broth.itch.zone/butler/{butlerPlatform}-{butlerArch}/LATEST/archive/default";
            var zipPath = Path.Combine(_launcherDir, "cache", "butler.zip");

            await DownloadFileAsync(butlerUrl, zipPath);

            StatusChanged?.Invoke(localization.Get("status.extracting_butler"));
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, butlerDir, true);

            File.Delete(zipPath);

            // On Linux/macOS, make butler executable
            if (PlatformHelper.CurrentPlatform != PlatformHelper.Platform.Windows)
            {
                try
                {
                    var chmod = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{butlerExe}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    chmod.Start();
                    chmod.WaitForExit(5000);
                }
                catch { /* Ignore chmod errors */ }
            }

            return butlerExe;
        }

        private void LaunchGame(string playerName, GameVersion version)
        {
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", PlatformHelper.GetGameExecutable());
            var javaExe = GetJavaPath(version.Branch);
            
            // Get UserData path - use modpack path if selected, otherwise default
            var userDataDir = GetUserDataPath();
            Directory.CreateDirectory(userDataDir);

            LogService.LogGame($"Launching game: {clientPath}");
            LogService.LogGameVerbose($"Java path: {javaExe}");
            LogService.LogGameVerbose($"User data dir: {userDataDir}");
            LogService.LogGameVerbose($"Selected modpack: {SelectedModpackId ?? "default"}");

            if (!File.Exists(clientPath))
            {
                LogService.LogError($"Game client not found: {clientPath}");
                throw new FileNotFoundException("Game client not found");
            }

            var uuid = GetOrCreateUuid(playerName);
            LogService.LogGameVerbose($"Player UUID: {uuid}");

            // Формируем аргументы - используем кастомные если заданы
            string arguments;
            if (!string.IsNullOrEmpty(CustomGameArgs))
            {
                // Заменяем переменные в кастомных аргументах
                arguments = CustomGameArgs
                    .Replace("{app-dir}", gameDir)
                    .Replace("{java-exec}", javaExe)
                    .Replace("{user-dir}", userDataDir)
                    .Replace("{uuid}", uuid)
                    .Replace("{name}", playerName);
                LogService.LogGameVerbose($"Using custom arguments");
            }
            else
            {
                arguments = $"--app-dir \"{gameDir}\" --java-exec \"{javaExe}\" --user-dir \"{userDataDir}\" --auth-mode offline --uuid {uuid} --name {playerName}";
            }
            
            LogService.LogGameVerbose($"Launch arguments: {arguments}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = clientPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(clientPath)
                }
            };

            process.Start();
            LogService.LogGame($"Game process started: PID={process.Id}");
        }

        /// <summary>
        /// Gets the UserData path based on selected modpack
        /// </summary>
        /// <returns>Path to UserData directory (modpack-specific or default)</returns>
        private string GetUserDataPath()
        {
            var hytaleDir = Path.GetDirectoryName(_gameDir); // %AppData%\Hytale
            
            if (!string.IsNullOrEmpty(SelectedModpackId))
            {
                // Use modpack-specific UserData directory
                var modpackService = new ModpackService(hytaleDir);
                var modpack = modpackService.GetModpack(SelectedModpackId);
                
                if (modpack != null)
                {
                    return modpackService.GetModpackUserDataPath(SelectedModpackId);
                }
                
                // Modpack not found, fall back to default
                LogService.LogMods($"Selected modpack {SelectedModpackId} not found, using default UserData");
            }
            
            // Default UserData directory
            return Path.Combine(hytaleDir!, "UserData");
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
            
            // Ищем или создаём UUID для никнейма (используем lowercase для стабильности)
            var key = playerName.ToLowerInvariant();
            if (!players.TryGetValue(key, out var uuid))
            {
                // Генерируем UUID БЕЗ дефисов (формат для Minecraft/Hytale)
                uuid = Guid.NewGuid().ToString("N"); // "N" = 32 символа без дефисов
                players[key] = uuid;
                
                // Сохраняем
                try
                {
                    File.WriteAllText(uuidFile, JsonConvert.SerializeObject(players, Formatting.Indented));
                }
                catch { }
            }
            else
            {
                // Если UUID был сохранён со старым форматом (с дефисами) - конвертируем
                if (uuid.Contains("-"))
                {
                    uuid = uuid.Replace("-", "");
                    players[key] = uuid;
                    
                    // Сохраняем обновлённый формат
                    try
                    {
                        File.WriteAllText(uuidFile, JsonConvert.SerializeObject(players, Formatting.Indented));
                    }
                    catch { }
                }
            }
            
            return uuid;
        }

        /// <summary>
        /// Мигрирует данные из старых папок UserData в новую общую папку
        /// Старый путь: Hytale\install\{branch}\package\game\{version}\Client\UserData
        /// Новый путь: Hytale\UserData
        /// </summary>
        public void MigrateUserData()
        {
            try
            {
                var hytaleDir = Path.GetDirectoryName(_gameDir); // %AppData%\Hytale
                if (string.IsNullOrEmpty(hytaleDir)) return;
                
                var newUserDataDir = Path.Combine(hytaleDir, "UserData");
                var migrationMarker = Path.Combine(newUserDataDir, ".migrated");
                
                // Если уже мигрировали - пропускаем
                if (File.Exists(migrationMarker)) return;
                
                var oldUserDataDirs = new List<string>();
                
                // Ищем старые папки UserData во всех ветках и версиях
                foreach (var branch in AvailableBranches)
                {
                    var branchDir = Path.Combine(_gameDir, branch, "package", "game");
                    if (!Directory.Exists(branchDir)) continue;
                    
                    // Проверяем все версии (latest, 1, 2, 3...)
                    foreach (var versionDir in Directory.GetDirectories(branchDir))
                    {
                        var oldUserData = Path.Combine(versionDir, "Client", "UserData");
                        if (Directory.Exists(oldUserData))
                        {
                            oldUserDataDirs.Add(oldUserData);
                        }
                    }
                }
                
                if (oldUserDataDirs.Count == 0) return;
                
                // Создаём новую папку
                Directory.CreateDirectory(newUserDataDir);
                
                // Копируем данные из всех найденных папок (более новые перезаписывают старые)
                foreach (var oldDir in oldUserDataDirs)
                {
                    CopyDirectoryContents(oldDir, newUserDataDir);
                }
                
                // Создаём маркер миграции
                File.WriteAllText(migrationMarker, $"Migrated on {DateTime.Now}\nFrom: {string.Join("\n", oldUserDataDirs)}");
            }
            catch
            {
                // Игнорируем ошибки миграции - не критично
            }
        }
        
        private void CopyDirectoryContents(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            
            // Копируем файлы
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                // Копируем только если файл новее или не существует
                if (!File.Exists(destFile) || File.GetLastWriteTime(file) > File.GetLastWriteTime(destFile))
                {
                    File.Copy(file, destFile, true);
                }
            }
            
            // Рекурсивно копируем подпапки
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectoryContents(dir, destSubDir);
            }
        }

        private string GetJavaPath(string branch)
        {
            var javaExe = Path.Combine(_gameDir, branch, "package", "jre", "latest", "bin", PlatformHelper.GetJavaExecutable());
            return File.Exists(javaExe) ? javaExe : "java";
        }

        /// <summary>
        /// Переустанавливает игру: удаляет текущую установку и скачивает заново
        /// </summary>
        public async Task ReinstallGameAsync(GameVersion version, LocalizationService localization)
        {
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var versionFile = Path.Combine(gameDir, ".version");

            LogService.LogGame($"Reinstalling game: version={version.Version}, branch={version.Branch}");
            LogService.LogGameVerbose($"Game directory to delete: {gameDir}");

            StatusChanged?.Invoke(localization.Get("status.reinstalling"));

            // Удаляем папку игры (кроме UserData, которая теперь отдельно)
            if (Directory.Exists(gameDir))
            {
                try
                {
                    LogService.LogGameVerbose("Deleting game directory...");
                    Directory.Delete(gameDir, true);
                    LogService.LogGameVerbose("Game directory deleted");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Failed to delete game folder: {ex.Message}");
                    throw new Exception($"Failed to delete game folder: {ex.Message}");
                }
            }

            // Удаляем кэшированные PWR файлы для этой ветки/версии
            var cacheDir = Path.Combine(_launcherDir, "cache");
            if (Directory.Exists(cacheDir))
            {
                var pwrFiles = Directory.GetFiles(cacheDir, $"{version.Branch}_*_{version.Version}.pwr");
                LogService.LogGameVerbose($"Deleting {pwrFiles.Length} cached PWR files");
                foreach (var file in pwrFiles)
                {
                    try { File.Delete(file); } catch { }
                }
            }

            // Скачиваем и устанавливаем заново
            LogService.LogGame("Starting fresh installation...");
            await DownloadGameAsync(version, localization);
        }

        /// <summary>
        /// Проверяет, установлена ли указанная версия игры
        /// </summary>
        public bool IsGameInstalled(GameVersion version)
        {
            var gameDir = Path.Combine(_gameDir, version.Branch, "package", "game", "latest");
            var clientPath = Path.Combine(gameDir, "Client", PlatformHelper.GetGameExecutable());
            var versionFile = Path.Combine(gameDir, ".version");

            if (!File.Exists(clientPath) || !File.Exists(versionFile))
                return false;

            if (int.TryParse(File.ReadAllText(versionFile).Trim(), out int installedVersion))
            {
                return installedVersion == version.Version;
            }

            return false;
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
