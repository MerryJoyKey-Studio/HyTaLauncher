using System.IO;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Represents a user-defined tag for categorizing mods
    /// </summary>
    public class ModTag
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#4CAF50";

        public override bool Equals(object? obj)
        {
            if (obj is ModTag other)
            {
                return Id == other.Id && Name == other.Name && Color == other.Color;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, Color);
        }
    }

    /// <summary>
    /// Represents the association between a mod file and its assigned tags
    /// </summary>
    public class TagAssignment
    {
        public string ModFilePath { get; set; } = "";
        public List<string> TagIds { get; set; } = new();

        public override bool Equals(object? obj)
        {
            if (obj is TagAssignment other)
            {
                return ModFilePath == other.ModFilePath && 
                       TagIds.SequenceEqual(other.TagIds);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ModFilePath, TagIds.Count);
        }
    }

    /// <summary>
    /// Configuration class for JSON serialization of tags and assignments
    /// </summary>
    public class TagConfig
    {
        public List<ModTag> Tags { get; set; } = new();
        public List<TagAssignment> Assignments { get; set; } = new();

        public override bool Equals(object? obj)
        {
            if (obj is TagConfig other)
            {
                if (Tags.Count != other.Tags.Count || Assignments.Count != other.Assignments.Count)
                    return false;

                for (int i = 0; i < Tags.Count; i++)
                {
                    if (!Tags[i].Equals(other.Tags[i]))
                        return false;
                }

                for (int i = 0; i < Assignments.Count; i++)
                {
                    if (!Assignments[i].Equals(other.Assignments[i]))
                        return false;
                }

                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Tags.Count, Assignments.Count);
        }
    }

    /// <summary>
    /// Service for managing mod tags - creation, assignment, filtering, and persistence
    /// </summary>
    public class TagService
    {
        private readonly string _configPath;
        private TagConfig _config;

        /// <summary>
        /// Event raised when tags or assignments change
        /// </summary>
        public event Action? TagsChanged;

        public TagService()
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher"
            );
            Directory.CreateDirectory(appDir);
            _configPath = Path.Combine(appDir, "tags.json");
            _config = new TagConfig();
            Load();
        }

        /// <summary>
        /// Constructor for testing with custom config path
        /// </summary>
        internal TagService(string configPath)
        {
            _configPath = configPath;
            _config = new TagConfig();
            
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            Load();
        }

        /// <summary>
        /// Gets all defined tags
        /// </summary>
        public List<ModTag> GetAllTags()
        {
            return _config.Tags.ToList();
        }

        /// <summary>
        /// Creates a new tag with the specified name and color
        /// </summary>
        /// <param name="name">Tag display name</param>
        /// <param name="color">Tag color in hex format (e.g., "#4CAF50")</param>
        /// <returns>The created tag</returns>
        public ModTag CreateTag(string name, string color = "#4CAF50")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tag name cannot be empty", nameof(name));
            }

            var tag = new ModTag
            {
                Id = Guid.NewGuid().ToString(),
                Name = name.Trim(),
                Color = color
            };

            _config.Tags.Add(tag);
            Save();
            TagsChanged?.Invoke();

            LogService.LogMods($"Created tag: {tag.Name} ({tag.Id})");
            return tag;
        }

        /// <summary>
        /// Deletes a tag and removes it from all mod assignments
        /// </summary>
        /// <param name="tagId">The ID of the tag to delete</param>
        public void DeleteTag(string tagId)
        {
            var tag = _config.Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag == null)
            {
                return;
            }

            // Remove tag from all assignments
            foreach (var assignment in _config.Assignments)
            {
                assignment.TagIds.Remove(tagId);
            }

            // Remove empty assignments
            _config.Assignments.RemoveAll(a => a.TagIds.Count == 0);

            // Remove the tag itself
            _config.Tags.Remove(tag);
            Save();
            TagsChanged?.Invoke();

            LogService.LogMods($"Deleted tag: {tag.Name} ({tagId})");
        }

        /// <summary>
        /// Assigns a tag to a mod
        /// </summary>
        /// <param name="modFilePath">The file path or name of the mod</param>
        /// <param name="tagId">The ID of the tag to assign</param>
        public void AssignTag(string modFilePath, string tagId)
        {
            if (string.IsNullOrWhiteSpace(modFilePath))
            {
                throw new ArgumentException("Mod file path cannot be empty", nameof(modFilePath));
            }

            // Verify tag exists
            if (!_config.Tags.Any(t => t.Id == tagId))
            {
                throw new ArgumentException($"Tag with ID {tagId} does not exist", nameof(tagId));
            }

            var assignment = _config.Assignments.FirstOrDefault(a => a.ModFilePath == modFilePath);
            
            if (assignment == null)
            {
                assignment = new TagAssignment { ModFilePath = modFilePath };
                _config.Assignments.Add(assignment);
            }

            if (!assignment.TagIds.Contains(tagId))
            {
                assignment.TagIds.Add(tagId);
                Save();
                TagsChanged?.Invoke();
                LogService.LogMods($"Assigned tag {tagId} to mod: {modFilePath}");
            }
        }

        /// <summary>
        /// Removes a tag from a mod
        /// </summary>
        /// <param name="modFilePath">The file path or name of the mod</param>
        /// <param name="tagId">The ID of the tag to remove</param>
        public void RemoveTag(string modFilePath, string tagId)
        {
            var assignment = _config.Assignments.FirstOrDefault(a => a.ModFilePath == modFilePath);
            
            if (assignment == null)
            {
                return;
            }

            if (assignment.TagIds.Remove(tagId))
            {
                // Remove empty assignments
                if (assignment.TagIds.Count == 0)
                {
                    _config.Assignments.Remove(assignment);
                }

                Save();
                TagsChanged?.Invoke();
                LogService.LogMods($"Removed tag {tagId} from mod: {modFilePath}");
            }
        }

        /// <summary>
        /// Gets all tag IDs assigned to a mod
        /// </summary>
        /// <param name="modFilePath">The file path or name of the mod</param>
        /// <returns>List of tag IDs assigned to the mod</returns>
        public List<string> GetTagsForMod(string modFilePath)
        {
            var assignment = _config.Assignments.FirstOrDefault(a => a.ModFilePath == modFilePath);
            return assignment?.TagIds.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Gets all ModTag objects assigned to a mod
        /// </summary>
        /// <param name="modFilePath">The file path or name of the mod</param>
        /// <returns>List of ModTag objects assigned to the mod</returns>
        public List<ModTag> GetTagObjectsForMod(string modFilePath)
        {
            var tagIds = GetTagsForMod(modFilePath);
            return _config.Tags.Where(t => tagIds.Contains(t.Id)).ToList();
        }

        /// <summary>
        /// Filters a list of installed mods to only those with a specific tag
        /// </summary>
        /// <param name="mods">The list of mods to filter</param>
        /// <param name="tagId">The tag ID to filter by</param>
        /// <returns>Filtered list containing only mods with the specified tag</returns>
        public List<InstalledMod> FilterByTag(List<InstalledMod> mods, string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId))
            {
                return mods.ToList();
            }

            return mods.Where(mod =>
            {
                var assignment = _config.Assignments.FirstOrDefault(a => 
                    a.ModFilePath == mod.FilePath || a.ModFilePath == mod.FileName);
                return assignment?.TagIds.Contains(tagId) == true;
            }).ToList();
        }

        /// <summary>
        /// Saves the tag configuration to disk
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                LogService.LogMods($"Tags saved to: {_configPath}");
            }
            catch (Exception ex)
            {
                LogService.LogError($"TagService.Save: {ex}");
            }
        }

        /// <summary>
        /// Loads the tag configuration from disk
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<TagConfig>(json) ?? new TagConfig();
                    LogService.LogMods($"Tags loaded from: {_configPath}");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"TagService.Load: {ex}");
                _config = new TagConfig();
            }
        }

        /// <summary>
        /// Gets the current configuration (for testing/serialization purposes)
        /// </summary>
        internal TagConfig GetConfig()
        {
            return _config;
        }

        /// <summary>
        /// Sets the configuration (for testing purposes)
        /// </summary>
        internal void SetConfig(TagConfig config)
        {
            _config = config;
        }
    }
}
