using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HyTaLauncher.Services
{
    public class ModService : IDisposable
    {
        private readonly HttpRetryService _httpService;
        private readonly HttpClient _apiClient; // For API calls with custom headers
        private readonly string _modsFolder;
        private readonly bool _hasApiKey;
        private bool _disposed;

        // CurseForge API для Hytale
        private const string CF_API_BASE = "https://api.curseforge.com/v1";
        private const int HYTALE_GAME_ID = 70216;
        private const int MODS_CLASS_ID = 9137;

        // Retry configuration for mod downloads
        private static readonly RetryConfig ModDownloadConfig = new()
        {
            MaxRetries = 3,
            Timeout = TimeSpan.FromMinutes(10),
            EnableResume = true,
            InitialRetryDelay = TimeSpan.FromSeconds(2)
        };

        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        public bool HasApiKey => _hasApiKey;

        public ModService(string gameDirectory)
        {
            _httpService = new HttpRetryService(ModDownloadConfig);
            _httpService.ProgressChanged += p => ProgressChanged?.Invoke(p);
            _httpService.StatusChanged += s => StatusChanged?.Invoke(s);

            _apiClient = new HttpClient();
            _apiClient.Timeout = TimeSpan.FromSeconds(30);
            _apiClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher/1.0");

            // Используем встроенный API ключ
            _hasApiKey = !string.IsNullOrEmpty(Config.CurseForgeApiKey);
            if (_hasApiKey)
            {
                _apiClient.DefaultRequestHeaders.Add("x-api-key", Config.CurseForgeApiKey);
                LogService.LogMods("API key configured");
            }
            else
            {
                LogService.LogMods("No API key provided");
            }

            // Общая папка UserData рядом с install
            var baseDir = string.IsNullOrEmpty(gameDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hytale")
                : gameDirectory;

            _modsFolder = Path.Combine(baseDir, "UserData", "Mods");

            LogService.LogMods($"Mods folder: {_modsFolder}");
        }

        public string GetModsFolder() => _modsFolder;

        public async Task<List<InstalledMod>> GetInstalledModsAsync()
        {
            LogService.LogMods($"Loading installed mods from: {_modsFolder}");
            var mods = new List<InstalledMod>();

            if (!Directory.Exists(_modsFolder))
            {
                Directory.CreateDirectory(_modsFolder);
                LogService.LogMods("Mods folder created (was empty)");
                return mods;
            }

            var files = Directory.GetFiles(_modsFolder, "*.*")
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                var mod = new InstalledMod
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    IsEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                };

                try
                {
                    mod.Manifest = await ReadManifestFromArchiveAsync(file);
                }
                catch { }

                // Read icon URL from .icons folder if exists
                var iconsFolder = Path.Combine(_modsFolder, ".icons");
                var iconFilePath = Path.Combine(iconsFolder, Path.GetFileName(file) + ".icon");
                if (File.Exists(iconFilePath))
                {
                    try
                    {
                        mod.IconUrl = await File.ReadAllTextAsync(iconFilePath);
                    }
                    catch { }
                }

                mods.Add(mod);
            }

            LogService.LogMods($"Found {mods.Count} installed mods");
            return mods.OrderBy(m => m.DisplayName).ToList();
        }

        private async Task<ModManifest?> ReadManifestFromArchiveAsync(string archivePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var archive = ZipFile.OpenRead(archivePath);
                    var manifestEntry = archive.GetEntry("manifest.json");

                    if (manifestEntry == null) return null;

                    using var stream = manifestEntry.Open();
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();

                    return JsonConvert.DeserializeObject<ModManifest>(json);
                }
                catch
                {
                    return null;
                }
            });
        }

        public async Task<List<CurseForgeSearchResult>> SearchModsAsync(
            string query,
            SearchFilters? filters = null,
            SortOption sort = SortOption.Popularity,
            SortOrder sortOrder = SortOrder.Descending,
            int page = 0,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            LogService.LogMods($"Searching mods: query=\"{query}\", page={page}, sort={sort}, sortOrder={sortOrder}");

            var maxRetries = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var url = BuildSearchUrl(query, filters, sort, sortOrder, page, pageSize);

                    LogService.LogMods($"Request URL: {url} (attempt {attempt}/{maxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));

                    var response = await _apiClient.GetStringAsync(url, cts.Token);
                    LogService.LogMods($"Response received: {response.Length} chars");

                    var json = JObject.Parse(response);
                    var data = json["data"]?.ToObject<List<CurseForgeSearchResult>>();

                    LogService.LogMods($"Search results: {data?.Count ?? 0} mods found");
                    return data ?? new List<CurseForgeSearchResult>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // User cancelled
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogMods($"Search error (attempt {attempt}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            LogService.LogError($"ModService.SearchModsAsync failed after {maxRetries} attempts", lastException!);
            StatusChanged?.Invoke($"Search error: {lastException?.Message}");
            return new List<CurseForgeSearchResult>();
        }

        /// <summary>
        /// Builds the CurseForge API search URL with filters and sort parameters
        /// </summary>
        internal string BuildSearchUrl(
            string query,
            SearchFilters? filters,
            SortOption sort,
            SortOrder sortOrder,
            int page,
            int pageSize)
        {
            var url = $"{CF_API_BASE}/mods/search?gameId={HYTALE_GAME_ID}&classId={MODS_CLASS_ID}";

            // Add search query if provided
            if (!string.IsNullOrWhiteSpace(query))
            {
                url += $"&searchFilter={Uri.EscapeDataString(query)}";
            }

            // Add pagination
            url += $"&index={page * pageSize}&pageSize={pageSize}";

            // Add sort parameters
            url += $"&sortField={(int)sort}";
            url += $"&sortOrder={GetSortOrderString(sortOrder)}";

            // Add filters if provided
            if (filters != null)
            {
                if (filters.CategoryId.HasValue)
                {
                    url += $"&categoryId={filters.CategoryId.Value}";
                }

                if (!string.IsNullOrWhiteSpace(filters.GameVersion))
                {
                    url += $"&gameVersion={Uri.EscapeDataString(filters.GameVersion)}";
                }

                if (filters.ReleaseType.HasValue)
                {
                    // CurseForge uses modLoaderType for some filtering, but for release type
                    // we need to filter client-side or use the appropriate parameter
                    // The API doesn't have a direct releaseType filter, so we'll handle this
                    // by filtering results after fetching
                }
            }

            return url;
        }

        /// <summary>
        /// Converts SortOrder enum to CurseForge API string
        /// </summary>
        private static string GetSortOrderString(SortOrder sortOrder)
        {
            return sortOrder == SortOrder.Ascending ? "asc" : "desc";
        }

        /// <summary>
        /// Filters search results by release type (client-side filtering)
        /// </summary>
        public List<CurseForgeSearchResult> FilterByReleaseType(List<CurseForgeSearchResult> results, int releaseType)
        {
            return results.Where(r =>
                r.LatestFiles?.Any(f => f.ReleaseType == releaseType) == true
            ).ToList();
        }

        public async Task<List<CurseForgeSearchResult>> GetPopularModsAsync(
            SearchFilters? filters = null,
            SortOption sort = SortOption.Popularity,
            SortOrder sortOrder = SortOrder.Descending,
            int page = 0,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            LogService.LogMods($"Loading popular mods, page={page}, sort={sort}");

            var maxRetries = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var url = BuildSearchUrl("", filters, sort, sortOrder, page, pageSize);

                    LogService.LogMods($"Request URL: {url} (attempt {attempt}/{maxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));

                    var response = await _apiClient.GetStringAsync(url, cts.Token);
                    LogService.LogMods($"Response received: {response.Length} chars");

                    var json = JObject.Parse(response);
                    var data = json["data"]?.ToObject<List<CurseForgeSearchResult>>();

                    // Apply client-side release type filter if specified
                    if (filters?.ReleaseType.HasValue == true && data != null)
                    {
                        data = FilterByReleaseType(data, filters.ReleaseType.Value);
                    }

                    LogService.LogMods($"Popular mods: {data?.Count ?? 0} found");
                    return data ?? new List<CurseForgeSearchResult>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogMods($"GetPopularMods error (attempt {attempt}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            LogService.LogError($"ModService.GetPopularModsAsync failed after {maxRetries} attempts", lastException!);
            return new List<CurseForgeSearchResult>();
        }

        /// <summary>
        /// Gets available mod categories from CurseForge API
        /// </summary>
        public async Task<List<ModCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            LogService.LogMods("Loading mod categories from API");

            var maxRetries = 2;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var url = $"{CF_API_BASE}/categories?gameId={HYTALE_GAME_ID}&classId={MODS_CLASS_ID}";
                    LogService.LogModsVerbose($"Categories URL: {url}");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    var response = await _apiClient.GetStringAsync(url, cts.Token);
                    var json = JObject.Parse(response);
                    var data = json["data"]?.ToObject<List<ModCategory>>();

                    LogService.LogMods($"Categories loaded: {data?.Count ?? 0}");
                    return data ?? new List<ModCategory>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogService.LogMods($"GetCategories error (attempt {attempt}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            }

            LogService.LogError("ModService.GetCategoriesAsync failed");
            return new List<ModCategory>();
        }

        /// <summary>
        /// Gets available game versions from CurseForge API
        /// </summary>
        public async Task<List<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
        {
            LogService.LogMods("Loading game versions from API");

            var maxRetries = 2;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var url = $"{CF_API_BASE}/games/{HYTALE_GAME_ID}/versions";
                    LogService.LogModsVerbose($"Versions URL: {url}");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    var response = await _apiClient.GetStringAsync(url, cts.Token);
                    var json = JObject.Parse(response);
                    var data = json["data"] as JArray;

                    var versions = new List<string>();
                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            var versionArray = item["versions"] as JArray;
                            if (versionArray != null)
                            {
                                foreach (var v in versionArray)
                                {
                                    versions.Add(v.ToString());
                                }
                            }
                        }
                    }

                    LogService.LogMods($"Game versions loaded: {versions.Count}");
                    return versions;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogService.LogMods($"GetGameVersions error (attempt {attempt}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            }

            LogService.LogError("ModService.GetGameVersionsAsync failed");
            return new List<string>();
        }

        public async Task<bool> InstallModAsync(CurseForgeSearchResult mod, string? targetFolder = null, CancellationToken cancellationToken = default)
        {
            LogService.LogMods($"Installing mod: {mod.Name} (ID: {mod.Id})");
            try
            {
                var latestFile = mod.LatestFiles?.FirstOrDefault();
                if (latestFile == null || string.IsNullOrEmpty(latestFile.DownloadUrl))
                {
                    LogService.LogMods($"No download URL for mod {mod.Name}");
                    StatusChanged?.Invoke("No download available");
                    return false;
                }

                var installFolder = targetFolder ?? _modsFolder;
                Directory.CreateDirectory(installFolder);

                var fileName = latestFile.FileName;
                var filePath = Path.Combine(installFolder, fileName);

                // Check if already installed
                if (File.Exists(filePath))
                {
                    LogService.LogMods($"Mod already exists: {filePath}");
                    StatusChanged?.Invoke("Mod already installed");
                    return true;
                }

                LogService.LogMods($"Downloading from: {latestFile.DownloadUrl}");
                StatusChanged?.Invoke($"Downloading {mod.Name}...");
                ProgressChanged?.Invoke(0);

                // Use HttpRetryService for download with retry and resume support
                var downloadResult = await _httpService.DownloadFileAsync(
                    latestFile.DownloadUrl,
                    filePath,
                    ModDownloadConfig,
                    latestFile.FileLength,
                    cancellationToken: cancellationToken);

                if (!downloadResult.Success)
                {
                    LogService.LogMods($"Download failed: {downloadResult.ErrorMessage}");
                    StatusChanged?.Invoke($"Download failed: {downloadResult.ErrorMessage}");

                    // Clean up partial file
                    SafeDeleteFile(filePath);
                    SafeDeleteFile(filePath + ".tmp");

                    return false;
                }

                LogService.LogMods($"Download completed: {downloadResult.BytesDownloaded} bytes, attempts: {downloadResult.AttemptsUsed}, resumed: {downloadResult.WasResumed}");

                // Save icon URL to .icons folder
                if (!string.IsNullOrEmpty(mod.ThumbnailUrl))
                {
                    var iconsFolder = Path.Combine(installFolder, ".icons");
                    Directory.CreateDirectory(iconsFolder);
                    var iconFilePath = Path.Combine(iconsFolder, Path.GetFileName(filePath) + ".icon");
                    await File.WriteAllTextAsync(iconFilePath, mod.ThumbnailUrl, cancellationToken);
                    LogService.LogModsVerbose($"Saved icon URL to: {iconFilePath}");
                }

                ProgressChanged?.Invoke(100);
                LogService.LogMods($"Mod installed: {filePath}");
                StatusChanged?.Invoke($"{mod.Name} installed!");
                return true;
            }
            catch (OperationCanceledException)
            {
                LogService.LogMods($"Mod installation cancelled: {mod.Name}");
                StatusChanged?.Invoke("Installation cancelled");
                return false;
            }
            catch (Exception ex)
            {
                LogService.LogMods($"Install error: {ex.Message}");
                LogService.LogError($"ModService.InstallModAsync: {ex}");
                StatusChanged?.Invoke($"Install error: {ex.Message}");
                return false;
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

        public async Task<bool> InstallModFromFileAsync(int modId, int fileId, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"{CF_API_BASE}/mods/{modId}/files/{fileId}/download-url";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                var response = await _apiClient.GetStringAsync(url, cts.Token);
                var json = JObject.Parse(response);
                var downloadUrl = json["data"]?.ToString();

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    StatusChanged?.Invoke("Download URL not available");
                    return false;
                }

                // Get file info
                var fileInfoUrl = $"{CF_API_BASE}/mods/{modId}/files/{fileId}";
                var fileInfoResponse = await _apiClient.GetStringAsync(fileInfoUrl, cts.Token);
                var fileInfoJson = JObject.Parse(fileInfoResponse);
                var fileName = fileInfoJson["data"]?["fileName"]?.ToString() ?? $"mod_{fileId}.jar";
                var fileSize = fileInfoJson["data"]?["fileLength"]?.Value<long>() ?? -1;

                Directory.CreateDirectory(_modsFolder);
                var filePath = Path.Combine(_modsFolder, fileName);

                StatusChanged?.Invoke($"Downloading...");
                ProgressChanged?.Invoke(0);

                // Use HttpRetryService for download with retry and resume support
                var downloadResult = await _httpService.DownloadFileAsync(
                    downloadUrl,
                    filePath,
                    ModDownloadConfig,
                    fileSize,
                    cancellationToken: cancellationToken);

                if (!downloadResult.Success)
                {
                    StatusChanged?.Invoke($"Download failed: {downloadResult.ErrorMessage}");
                    SafeDeleteFile(filePath);
                    SafeDeleteFile(filePath + ".tmp");
                    return false;
                }

                ProgressChanged?.Invoke(100);
                StatusChanged?.Invoke("Mod installed!");
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke("Download cancelled");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error: {ex.Message}");
                LogService.LogError($"ModService.InstallModFromFileAsync: {ex}");
                return false;
            }
        }

        public bool DeleteMod(InstalledMod mod)
        {
            try
            {
                if (File.Exists(mod.FilePath))
                {
                    File.Delete(mod.FilePath);
                    LogService.LogMods($"Deleted mod: {mod.FilePath}");

                    // Delete icon file from .icons folder
                    var modsFolder = Path.GetDirectoryName(mod.FilePath);
                    if (!string.IsNullOrEmpty(modsFolder))
                    {
                        var iconFilePath = Path.Combine(modsFolder, ".icons", mod.FileName + ".icon");
                        if (File.Exists(iconFilePath))
                        {
                            File.Delete(iconFilePath);
                            LogService.LogModsVerbose($"Deleted icon: {iconFilePath}");
                        }
                    }

                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.LogMods($"Delete error: {ex.Message}");
                return false;
            }
        }

        public bool ToggleMod(InstalledMod mod)
        {
            try
            {
                var newPath = mod.IsEnabled
                    ? mod.FilePath + ".disabled"
                    : mod.FilePath.Replace(".disabled", "");

                File.Move(mod.FilePath, newPath);
                mod.FilePath = newPath;
                mod.IsEnabled = !mod.IsEnabled;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void OpenModsFolder()
        {
            Directory.CreateDirectory(_modsFolder);
            System.Diagnostics.Process.Start("explorer.exe", _modsFolder);
        }

        public async Task<List<InstalledMod>> CheckForUpdatesAsync(List<InstalledMod> installedMods)
        {
            LogService.LogMods("Checking for mod updates...");

            foreach (var mod in installedMods)
            {
                mod.HasUpdate = false;
                mod.UpdateInfo = null;
                mod.IconUrl = null;

                if (mod.Manifest == null || string.IsNullOrEmpty(mod.Manifest.Name))
                    continue;

                try
                {
                    // Поиск мода на CurseForge по имени
                    var searchResults = await SearchModsAsync(mod.Manifest.Name, null, SortOption.Popularity, SortOrder.Descending, 0, 5);

                    // Ищем точное совпадение по имени
                    var match = searchResults.FirstOrDefault(r =>
                        r.Name.Equals(mod.Manifest.Name, StringComparison.OrdinalIgnoreCase) ||
                        r.Slug.Equals(mod.Manifest.Name, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        // Set icon URL from CurseForge
                        mod.IconUrl = match.ThumbnailUrl;

                        if (match.LatestFiles?.Any() == true)
                        {
                            var latestFile = match.LatestFiles.FirstOrDefault();
                            if (latestFile != null)
                            {
                                // Сравниваем версии
                                var latestVersion = ExtractVersionFromFileName(latestFile.FileName);
                                var currentVersion = mod.Manifest.Version;

                                if (!string.IsNullOrEmpty(latestVersion) &&
                                    !string.IsNullOrEmpty(currentVersion) &&
                                    CompareVersions(latestVersion, currentVersion) > 0)
                                {
                                    mod.HasUpdate = true;
                                    mod.UpdateInfo = match;
                                    mod.LatestVersion = latestVersion;
                                    LogService.LogMods($"Update available for {mod.DisplayName}: {currentVersion} -> {latestVersion}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogMods($"Error checking update for {mod.DisplayName}: {ex.Message}");
                }
            }

            var updatesCount = installedMods.Count(m => m.HasUpdate);
            LogService.LogMods($"Update check complete. {updatesCount} updates available.");

            return installedMods;
        }

        public async Task<bool> UpdateModAsync(InstalledMod mod)
        {
            if (mod.UpdateInfo == null)
                return false;

            LogService.LogMods($"Updating mod: {mod.DisplayName}");

            // Удаляем старый файл
            if (File.Exists(mod.FilePath))
            {
                File.Delete(mod.FilePath);
                LogService.LogMods($"Deleted old version: {mod.FilePath}");
            }

            // Устанавливаем новую версию
            return await InstallModAsync(mod.UpdateInfo);
        }

        private string ExtractVersionFromFileName(string fileName)
        {
            // Пытаемся извлечь версию из имени файла
            // Примеры: ModName-1.2.3.jar, ModName_v1.2.3.zip
            var patterns = new[]
            {
                @"[-_]v?(\d+\.\d+(?:\.\d+)?)",
                @"(\d+\.\d+(?:\.\d+)?)"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return "";
        }

        private int CompareVersions(string v1, string v2)
        {
            try
            {
                var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
                var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

                var maxLen = Math.Max(parts1.Length, parts2.Length);

                for (int i = 0; i < maxLen; i++)
                {
                    var p1 = i < parts1.Length ? parts1[i] : 0;
                    var p2 = i < parts2.Length ? parts2[i] : 0;

                    if (p1 > p2) return 1;
                    if (p1 < p2) return -1;
                }

                return 0;
            }
            catch
            {
                return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Disposes resources used by ModService
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
