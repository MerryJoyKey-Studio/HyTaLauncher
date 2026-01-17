namespace HyTaLauncher.Services
{
    /// <summary>
    /// Search filters for CurseForge mod search
    /// </summary>
    public class SearchFilters
    {
        /// <summary>
        /// Category ID to filter by (null = all categories)
        /// </summary>
        public int? CategoryId { get; set; }

        /// <summary>
        /// Game version to filter by (null = all versions)
        /// </summary>
        public string? GameVersion { get; set; }

        /// <summary>
        /// Release type filter: 1=Release, 2=Beta, 3=Alpha (null = all types)
        /// </summary>
        public int? ReleaseType { get; set; }
    }

    /// <summary>
    /// Sort options for CurseForge mod search, values map to CurseForge API sortField
    /// </summary>
    public enum SortOption
    {
        /// <summary>Featured/Popularity (sortField=1)</summary>
        Featured = 1,
        /// <summary>Popularity (sortField=2)</summary>
        Popularity = 2,
        /// <summary>Last Updated (sortField=3)</summary>
        LastUpdated = 3,
        /// <summary>Name (sortField=4)</summary>
        Name = 4,
        /// <summary>Author (sortField=5)</summary>
        Author = 5,
        /// <summary>Total Downloads (sortField=6)</summary>
        TotalDownloads = 6,
        /// <summary>Category (sortField=7)</summary>
        Category = 7,
        /// <summary>Game Version (sortField=8)</summary>
        GameVersion = 8
    }

    /// <summary>
    /// Sort order direction
    /// </summary>
    public enum SortOrder
    {
        /// <summary>Ascending order</summary>
        Ascending,
        /// <summary>Descending order</summary>
        Descending
    }

    public class ModManifest
    {
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public List<ModAuthor> Authors { get; set; } = new();
        public string Website { get; set; } = "";
        public string ServerVersion { get; set; } = "*";
        public Dictionary<string, string> Dependencies { get; set; } = new();
        public Dictionary<string, string> OptionalDependencies { get; set; } = new();
        public bool DisabledByDefault { get; set; }
        public string Main { get; set; } = "";
        public bool IncludesAssetPack { get; set; }
    }

    public class ModAuthor
    {
        public string Name { get; set; } = "";
    }

    public class InstalledMod
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public ModManifest? Manifest { get; set; }
        public bool IsEnabled { get; set; } = true;
        
        // Update info
        public bool HasUpdate { get; set; }
        public CurseForgeSearchResult? UpdateInfo { get; set; }
        public string? LatestVersion { get; set; }
        
        // Icon URL from CurseForge (if matched)
        public string? IconUrl { get; set; }
        
        public string DisplayName => Manifest?.Name ?? FileName;
        public string DisplayVersion => Manifest?.Version ?? "?";
        public string DisplayAuthor => Manifest?.Authors?.FirstOrDefault()?.Name ?? "Unknown";
        public string DisplayDescription => Manifest?.Description ?? "";
    }

    // CurseForge API models для Hytale
    public class CurseForgeSearchResult
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Slug { get; set; } = "";
        public long DownloadCount { get; set; }
        public CurseForgeLogo? Logo { get; set; }
        public List<CurseForgeAuthor> Authors { get; set; } = new();
        public List<CurseForgeFile> LatestFiles { get; set; } = new();
        
        public string AuthorName => Authors?.FirstOrDefault()?.Name ?? "Unknown";
        public string? ThumbnailUrl => Logo?.ThumbnailUrl;
        public string DownloadCountFormatted => DownloadCount > 1000000 
            ? $"{DownloadCount / 1000000.0:F1}M" 
            : DownloadCount > 1000 
                ? $"{DownloadCount / 1000.0:F1}K" 
                : DownloadCount.ToString();
    }

    public class CurseForgeLogo
    {
        public int Id { get; set; }
        public string ThumbnailUrl { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public class CurseForgeAuthor
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public class CurseForgeFile
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? DownloadUrl { get; set; }
        public long FileLength { get; set; }
        public List<string> GameVersions { get; set; } = new();
        public int ReleaseType { get; set; } // 1 = Release, 2 = Beta, 3 = Alpha
    }

    /// <summary>
    /// CurseForge mod category
    /// </summary>
    public class ModCategory
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
    }
}
