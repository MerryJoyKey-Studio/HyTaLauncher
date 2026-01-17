using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Service for managing modpacks - creation, deletion, export/import, and persistence
    /// </summary>
    public class ModpackService
    {
        private readonly string _configPath;
        private readonly string _baseUserDataPath;
        private ModpackConfig _config;

        /// <summary>
        /// Characters that are invalid in filesystem names
        /// </summary>
        private static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// Event raised when modpacks change
        /// </summary>
        public event Action? ModpacksChanged;

        public ModpackService(string? gameDirectory = null)
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher"
            );
            Directory.CreateDirectory(appDir);
            _configPath = Path.Combine(appDir, "modpacks.json");

            // Base path for UserData directories
            var hytaleDir = string.IsNullOrEmpty(gameDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hytale")
                : gameDirectory;
            _baseUserDataPath = hytaleDir;

            _config = new ModpackConfig();
            Load();
        }

        /// <summary>
        /// Constructor for testing with custom paths
        /// </summary>
        internal ModpackService(string configPath, string baseUserDataPath)
        {
            _configPath = configPath;
            _baseUserDataPath = baseUserDataPath;
            _config = new ModpackConfig();

            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            Directory.CreateDirectory(baseUserDataPath);

            Load();
        }


        /// <summary>
        /// Validates a modpack name for filesystem compatibility
        /// </summary>
        /// <param name="name">The name to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidModpackName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            foreach (var c in InvalidNameChars)
            {
                if (name.Contains(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets all modpacks
        /// </summary>
        public List<Modpack> GetAllModpacks()
        {
            return _config.Modpacks.ToList();
        }

        /// <summary>
        /// Gets a modpack by ID
        /// </summary>
        public Modpack? GetModpack(string id)
        {
            return _config.Modpacks.FirstOrDefault(m => m.Id == id);
        }

        /// <summary>
        /// Gets the currently selected modpack ID
        /// </summary>
        public string? GetSelectedModpackId()
        {
            return _config.SelectedModpackId;
        }

        /// <summary>
        /// Sets the currently selected modpack
        /// </summary>
        public void SetSelectedModpack(string? modpackId)
        {
            if (modpackId != null && !_config.Modpacks.Any(m => m.Id == modpackId))
            {
                throw new ArgumentException($"Modpack with ID {modpackId} does not exist");
            }

            _config.SelectedModpackId = modpackId;
            Save();
            LogService.LogMods($"Selected modpack: {modpackId ?? "default"}");
        }

        /// <summary>
        /// Creates a new modpack with the specified mods
        /// </summary>
        /// <param name="name">Modpack name</param>
        /// <param name="modFilePaths">List of mod file paths to include</param>
        /// <returns>The created modpack</returns>
        public Modpack CreateModpack(string name, List<string> modFilePaths)
        {
            if (!IsValidModpackName(name))
            {
                throw new ArgumentException("Invalid modpack name. Name cannot be empty or contain: \\ / : * ? \" < > |", nameof(name));
            }

            var modpack = new Modpack
            {
                Id = Guid.NewGuid().ToString(),
                Name = name.Trim(),
                CreatedAt = DateTime.UtcNow,
                Mods = new List<ModpackMod>()
            };

            // Create the modpack's UserData directory
            var userDataPath = GetModpackUserDataPath(modpack.Id);
            var modsPath = Path.Combine(userDataPath, "Mods");
            Directory.CreateDirectory(modsPath);

            // Copy mods to the modpack directory
            foreach (var sourcePath in modFilePaths)
            {
                if (File.Exists(sourcePath))
                {
                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(modsPath, fileName);
                    File.Copy(sourcePath, destPath, overwrite: true);

                    modpack.Mods.Add(new ModpackMod
                    {
                        FileName = fileName,
                        Version = ExtractVersionFromFileName(fileName)
                    });
                }
            }

            _config.Modpacks.Add(modpack);
            Save();
            ModpacksChanged?.Invoke();

            LogService.LogMods($"Created modpack: {modpack.Name} ({modpack.Id}) with {modpack.Mods.Count} mods");
            return modpack;
        }


        /// <summary>
        /// Deletes a modpack and its UserData directory
        /// </summary>
        /// <param name="id">Modpack ID</param>
        public void DeleteModpack(string id)
        {
            var modpack = _config.Modpacks.FirstOrDefault(m => m.Id == id);
            if (modpack == null)
            {
                return;
            }

            // Delete the UserData directory
            var userDataPath = GetModpackUserDataPath(id);
            if (Directory.Exists(userDataPath))
            {
                try
                {
                    Directory.Delete(userDataPath, recursive: true);
                    LogService.LogMods($"Deleted modpack directory: {userDataPath}");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"ModpackService.DeleteModpack: Failed to delete directory: {ex}");
                }
            }

            // Reset selection if this modpack was selected
            if (_config.SelectedModpackId == id)
            {
                _config.SelectedModpackId = null;
            }

            _config.Modpacks.Remove(modpack);
            Save();
            ModpacksChanged?.Invoke();

            LogService.LogMods($"Deleted modpack: {modpack.Name} ({id})");
        }

        /// <summary>
        /// Renames a modpack
        /// </summary>
        /// <param name="id">Modpack ID</param>
        /// <param name="newName">New name</param>
        public void RenameModpack(string id, string newName)
        {
            if (!IsValidModpackName(newName))
            {
                throw new ArgumentException("Invalid modpack name. Name cannot be empty or contain: \\ / : * ? \" < > |", nameof(newName));
            }

            var modpack = _config.Modpacks.FirstOrDefault(m => m.Id == id);
            if (modpack == null)
            {
                throw new ArgumentException($"Modpack with ID {id} does not exist");
            }

            modpack.Name = newName.Trim();
            Save();
            ModpacksChanged?.Invoke();

            LogService.LogMods($"Renamed modpack {id} to: {newName}");
        }

        /// <summary>
        /// Adds a mod to a modpack
        /// </summary>
        /// <param name="modpackId">Modpack ID</param>
        /// <param name="modFilePath">Path to the mod file to add</param>
        public void AddModToModpack(string modpackId, string modFilePath)
        {
            var modpack = _config.Modpacks.FirstOrDefault(m => m.Id == modpackId);
            if (modpack == null)
            {
                throw new ArgumentException($"Modpack with ID {modpackId} does not exist");
            }

            if (!File.Exists(modFilePath))
            {
                throw new FileNotFoundException("Mod file not found", modFilePath);
            }

            var fileName = Path.GetFileName(modFilePath);
            var modsPath = Path.Combine(GetModpackUserDataPath(modpackId), "Mods");
            Directory.CreateDirectory(modsPath);

            var destPath = Path.Combine(modsPath, fileName);
            File.Copy(modFilePath, destPath, overwrite: true);

            // Update modpack metadata if not already present
            if (!modpack.Mods.Any(m => m.FileName == fileName))
            {
                modpack.Mods.Add(new ModpackMod
                {
                    FileName = fileName,
                    Version = ExtractVersionFromFileName(fileName)
                });
                Save();
                ModpacksChanged?.Invoke();
            }

            LogService.LogMods($"Added mod {fileName} to modpack {modpack.Name}");
        }


        /// <summary>
        /// Removes a mod from a modpack
        /// </summary>
        /// <param name="modpackId">Modpack ID</param>
        /// <param name="modFileName">Name of the mod file to remove</param>
        public void RemoveModFromModpack(string modpackId, string modFileName)
        {
            var modpack = _config.Modpacks.FirstOrDefault(m => m.Id == modpackId);
            if (modpack == null)
            {
                throw new ArgumentException($"Modpack with ID {modpackId} does not exist");
            }

            // Delete the file from the modpack directory
            var modPath = Path.Combine(GetModpackUserDataPath(modpackId), "Mods", modFileName);
            if (File.Exists(modPath))
            {
                File.Delete(modPath);
            }

            // Remove from metadata
            var mod = modpack.Mods.FirstOrDefault(m => m.FileName == modFileName);
            if (mod != null)
            {
                modpack.Mods.Remove(mod);
                Save();
                ModpacksChanged?.Invoke();
            }

            LogService.LogMods($"Removed mod {modFileName} from modpack {modpack.Name}");
        }

        /// <summary>
        /// Gets the UserData path for a modpack
        /// </summary>
        /// <param name="modpackId">Modpack ID</param>
        /// <returns>Full path to the modpack's UserData directory</returns>
        public string GetModpackUserDataPath(string modpackId)
        {
            return Path.Combine(_baseUserDataPath, $"UserData_{modpackId}");
        }

        /// <summary>
        /// Gets the default UserData path (when no modpack is selected)
        /// </summary>
        public string GetDefaultUserDataPath()
        {
            return Path.Combine(_baseUserDataPath, "UserData");
        }

        /// <summary>
        /// Gets the mods folder path for a modpack
        /// </summary>
        public string GetModpackModsPath(string modpackId)
        {
            return Path.Combine(GetModpackUserDataPath(modpackId), "Mods");
        }

        /// <summary>
        /// Gets the list of installed mods in a modpack by scanning the directory
        /// </summary>
        public List<InstalledMod> GetModpackInstalledMods(string modpackId)
        {
            var mods = new List<InstalledMod>();
            var modsPath = GetModpackModsPath(modpackId);

            if (!Directory.Exists(modsPath))
            {
                return mods;
            }

            var files = Directory.GetFiles(modsPath, "*.*")
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
                
                // Read manifest from archive
                try
                {
                    mod.Manifest = ReadManifestFromArchive(file);
                }
                catch { }
                
                // Read icon URL from .icons folder if exists
                var iconsFolder = Path.Combine(modsPath, ".icons");
                var iconFilePath = Path.Combine(iconsFolder, Path.GetFileName(file) + ".icon");
                if (File.Exists(iconFilePath))
                {
                    try
                    {
                        mod.IconUrl = File.ReadAllText(iconFilePath);
                    }
                    catch { }
                }
                
                mods.Add(mod);
            }

            return mods.OrderBy(m => m.DisplayName).ToList();
        }
        
        private ModManifest? ReadManifestFromArchive(string archivePath)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
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
        }

        /// <summary>
        /// Saves the modpack configuration to disk
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                LogService.LogMods($"Modpacks saved to: {_configPath}");
            }
            catch (Exception ex)
            {
                LogService.LogError($"ModpackService.Save: {ex}");
            }
        }

        /// <summary>
        /// Loads the modpack configuration from disk
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<ModpackConfig>(json) ?? new ModpackConfig();
                    LogService.LogMods($"Modpacks loaded from: {_configPath}");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"ModpackService.Load: {ex}");
                _config = new ModpackConfig();
            }
        }


        /// <summary>
        /// Gets the current configuration (for testing/serialization purposes)
        /// </summary>
        internal ModpackConfig GetConfig()
        {
            return _config;
        }

        /// <summary>
        /// Sets the configuration (for testing purposes)
        /// </summary>
        internal void SetConfig(ModpackConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Extracts version from a mod filename
        /// </summary>
        private static string? ExtractVersionFromFileName(string fileName)
        {
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

            return null;
        }

        /// <summary>
        /// Checks if a modpack's UserData directory exists
        /// </summary>
        public bool ModpackDirectoryExists(string modpackId)
        {
            return Directory.Exists(GetModpackUserDataPath(modpackId));
        }

        /// <summary>
        /// Exports a modpack to a ZIP archive
        /// </summary>
        /// <param name="id">Modpack ID</param>
        /// <param name="zipPath">Path for the output ZIP file</param>
        public async Task ExportModpackAsync(string id, string zipPath)
        {
            var modpack = _config.Modpacks.FirstOrDefault(m => m.Id == id);
            if (modpack == null)
            {
                throw new ArgumentException($"Modpack with ID {id} does not exist");
            }

            await Task.Run(() =>
            {
                // Delete existing file if present
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                // Create manifest
                var manifest = new ModpackManifest
                {
                    FormatVersion = 1,
                    Name = modpack.Name,
                    CreatedAt = modpack.CreatedAt,
                    Mods = modpack.Mods.ToList()
                };

                var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                var manifestEntry = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(manifestJson);
                }

                // Add mod files
                var modsPath = GetModpackModsPath(id);
                if (Directory.Exists(modsPath))
                {
                    foreach (var modFile in Directory.GetFiles(modsPath))
                    {
                        var fileName = Path.GetFileName(modFile);
                        archive.CreateEntryFromFile(modFile, $"mods/{fileName}");
                    }
                }
            });

            LogService.LogMods($"Exported modpack {modpack.Name} to: {zipPath}");
        }

        /// <summary>
        /// Imports a modpack from a ZIP archive
        /// </summary>
        /// <param name="zipPath">Path to the ZIP file</param>
        /// <returns>The imported modpack, or null if import failed</returns>
        public async Task<Modpack?> ImportModpackAsync(string zipPath)
        {
            if (!File.Exists(zipPath))
            {
                throw new FileNotFoundException("ZIP file not found", zipPath);
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);

                    // Read manifest
                    var manifestEntry = archive.GetEntry("manifest.json");
                    if (manifestEntry == null)
                    {
                        LogService.LogMods("Import failed: manifest.json not found in ZIP");
                        return null;
                    }

                    ModpackManifest? manifest;
                    using (var reader = new StreamReader(manifestEntry.Open()))
                    {
                        var json = reader.ReadToEnd();
                        manifest = JsonConvert.DeserializeObject<ModpackManifest>(json);
                    }

                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Name))
                    {
                        LogService.LogMods("Import failed: invalid manifest");
                        return null;
                    }

                    // Create new modpack
                    var modpack = new Modpack
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = manifest.Name,
                        CreatedAt = DateTime.UtcNow,
                        Mods = new List<ModpackMod>()
                    };

                    // Create UserData directory
                    var userDataPath = GetModpackUserDataPath(modpack.Id);
                    var modsPath = Path.Combine(userDataPath, "Mods");
                    Directory.CreateDirectory(modsPath);

                    // Extract mod files
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(entry.Name))
                        {
                            var destPath = Path.Combine(modsPath, entry.Name);
                            entry.ExtractToFile(destPath, overwrite: true);

                            modpack.Mods.Add(new ModpackMod
                            {
                                FileName = entry.Name,
                                Version = ExtractVersionFromFileName(entry.Name)
                            });
                        }
                    }

                    // Also add mods from manifest that might not be in the archive
                    foreach (var manifestMod in manifest.Mods)
                    {
                        if (!modpack.Mods.Any(m => m.FileName == manifestMod.FileName))
                        {
                            // Mod was in manifest but not in archive - add metadata only
                            modpack.Mods.Add(manifestMod);
                        }
                        else
                        {
                            // Update with CurseForge info from manifest
                            var existingMod = modpack.Mods.First(m => m.FileName == manifestMod.FileName);
                            existingMod.CurseForgeId = manifestMod.CurseForgeId;
                            if (!string.IsNullOrEmpty(manifestMod.Version))
                            {
                                existingMod.Version = manifestMod.Version;
                            }
                        }
                    }

                    _config.Modpacks.Add(modpack);
                    Save();
                    ModpacksChanged?.Invoke();

                    LogService.LogMods($"Imported modpack: {modpack.Name} ({modpack.Id}) with {modpack.Mods.Count} mods");
                    return modpack;
                }
                catch (InvalidDataException ex)
                {
                    LogService.LogError($"ModpackService.ImportModpackAsync: Invalid ZIP file: {ex}");
                    return null;
                }
                catch (Exception ex)
                {
                    LogService.LogError($"ModpackService.ImportModpackAsync: {ex}");
                    return null;
                }
            });
        }
    }
}
