namespace HyTaLauncher.Services
{
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
}
