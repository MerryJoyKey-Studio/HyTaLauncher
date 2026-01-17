using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HyTaLauncher.Services
{
    public class ModService
    {
        private readonly HttpClient _httpClient;
        private readonly string _modsFolder;
        private readonly bool _hasApiKey;
        
        // CurseForge API для Hytale
        private const string CF_API_BASE = "https://api.curseforge.com/v1";
        private const int HYTALE_GAME_ID = 70216;
        private const int MODS_CLASS_ID = 9137;
        
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        public bool HasApiKey => _hasApiKey;

        public ModService(string gameDirectory)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher/1.0");
            
            // Используем встроенный API ключ
            _hasApiKey = !string.IsNullOrEmpty(Config.CurseForgeApiKey);
            if (_hasApiKey)
            {
                _httpClient.DefaultRequestHeaders.Add("x-api-key", Config.CurseForgeApiKey);
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
            int pageSize = 20)
        {
            LogService.LogMods($"Searching mods: query=\"{query}\", page={page}, sort={sort}, sortOrder={sortOrder}");
            try
            {
                var url = BuildSearchUrl(query, filters, sort, sortOrder, page, pageSize);

                LogService.LogMods($"Request URL: {url}");
                var response = await _httpClient.GetStringAsync(url);
                LogService.LogMods($"Response received: {response.Length} chars");
                
                var json = JObject.Parse(response);
                var data = json["data"]?.ToObject<List<CurseForgeSearchResult>>();
                
                LogService.LogMods($"Search results: {data?.Count ?? 0} mods found");
                return data ?? new List<CurseForgeSearchResult>();
            }
            catch (Exception ex)
            {
                LogService.LogMods($"Search error: {ex.Message}");
                LogService.LogError($"ModService.SearchModsAsync: {ex}");
                StatusChanged?.Invoke($"Search error: {ex.Message}");
                return new List<CurseForgeSearchResult>();
            }
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
            int pageSize = 20)
        {
            LogService.LogMods($"Loading popular mods, page={page}, sort={sort}");
            try
            {
                var url = BuildSearchUrl("", filters, sort, sortOrder, page, pageSize);

                LogService.LogMods($"Request URL: {url}");
                var response = await _httpClient.GetStringAsync(url);
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
            catch (Exception ex)
            {
                LogService.LogMods($"GetPopularMods error: {ex.Message}");
                LogService.LogError($"ModService.GetPopularModsAsync: {ex}");
                return new List<CurseForgeSearchResult>();
            }
        }

        /// <summary>
        /// Gets available mod categories from CurseForge API
        /// </summary>
        public async Task<List<ModCategory>> GetCategoriesAsync()
        {
            LogService.LogMods("Loading mod categories from API");
            try
            {
                var url = $"{CF_API_BASE}/categories?gameId={HYTALE_GAME_ID}&classId={MODS_CLASS_ID}";
                LogService.LogModsVerbose($"Categories URL: {url}");
                
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var data = json["data"]?.ToObject<List<ModCategory>>();
                
                LogService.LogMods($"Categories loaded: {data?.Count ?? 0}");
                return data ?? new List<ModCategory>();
            }
            catch (Exception ex)
            {
                LogService.LogMods($"GetCategories error: {ex.Message}");
                LogService.LogError($"ModService.GetCategoriesAsync: {ex}");
                return new List<ModCategory>();
            }
        }

        /// <summary>
        /// Gets available game versions from CurseForge API
        /// </summary>
        public async Task<List<string>> GetGameVersionsAsync()
        {
            LogService.LogMods("Loading game versions from API");
            try
            {
                var url = $"{CF_API_BASE}/games/{HYTALE_GAME_ID}/versions";
                LogService.LogModsVerbose($"Versions URL: {url}");
                
                var response = await _httpClient.GetStringAsync(url);
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
            catch (Exception ex)
            {
                LogService.LogMods($"GetGameVersions error: {ex.Message}");
                LogService.LogError($"ModService.GetGameVersionsAsync: {ex}");
                return new List<string>();
            }
        }

        public async Task<bool> InstallModAsync(CurseForgeSearchResult mod, string? targetFolder = null)
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

                using var response = await _httpClient.GetAsync(latestFile.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;
                LogService.LogMods($"Download size: {totalBytes} bytes");

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(filePath);
                
                var buffer = new byte[8192];
                int bytesRead;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;
                    
                    if (totalBytes > 0)
                    {
                        var progress = (double)downloadedBytes / totalBytes * 100;
                        ProgressChanged?.Invoke(progress);
                    }
                }

                // Save icon URL to .icons folder
                if (!string.IsNullOrEmpty(mod.ThumbnailUrl))
                {
                    var iconsFolder = Path.Combine(installFolder, ".icons");
                    Directory.CreateDirectory(iconsFolder);
                    var iconFilePath = Path.Combine(iconsFolder, Path.GetFileName(filePath) + ".icon");
                    await File.WriteAllTextAsync(iconFilePath, mod.ThumbnailUrl);
                    LogService.LogModsVerbose($"Saved icon URL to: {iconFilePath}");
                }

                ProgressChanged?.Invoke(100);
                LogService.LogMods($"Mod installed: {filePath}");
                StatusChanged?.Invoke($"{mod.Name} installed!");
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogMods($"Install error: {ex.Message}");
                LogService.LogError($"ModService.InstallModAsync: {ex}");
                StatusChanged?.Invoke($"Install error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InstallModFromFileAsync(int modId, int fileId)
        {
            try
            {
                var url = $"{CF_API_BASE}/mods/{modId}/files/{fileId}/download-url";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var downloadUrl = json["data"]?.ToString();

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    StatusChanged?.Invoke("Download URL not available");
                    return false;
                }

                // Get file info
                var fileInfoUrl = $"{CF_API_BASE}/mods/{modId}/files/{fileId}";
                var fileInfoResponse = await _httpClient.GetStringAsync(fileInfoUrl);
                var fileInfoJson = JObject.Parse(fileInfoResponse);
                var fileName = fileInfoJson["data"]?["fileName"]?.ToString() ?? $"mod_{fileId}.jar";

                Directory.CreateDirectory(_modsFolder);
                var filePath = Path.Combine(_modsFolder, fileName);

                StatusChanged?.Invoke($"Downloading...");
                ProgressChanged?.Invoke(0);

                using var downloadResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                downloadResponse.EnsureSuccessStatusCode();

                var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(filePath);
                
                var buffer = new byte[8192];
                int bytesRead;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;
                    
                    if (totalBytes > 0)
                    {
                        ProgressChanged?.Invoke((double)downloadedBytes / totalBytes * 100);
                    }
                }

                ProgressChanged?.Invoke(100);
                StatusChanged?.Invoke("Mod installed!");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error: {ex.Message}");
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
    }
}
