using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Represents a mod within a modpack
    /// </summary>
    public class ModpackMod
    {
        public string FileName { get; set; } = "";
        public string? CurseForgeId { get; set; }
        public string? Version { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj is ModpackMod other)
            {
                return FileName == other.FileName &&
                       CurseForgeId == other.CurseForgeId &&
                       Version == other.Version;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FileName, CurseForgeId, Version);
        }
    }

    /// <summary>
    /// Represents a modpack with isolated UserData directory
    /// </summary>
    public class Modpack
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<ModpackMod> Mods { get; set; } = new();

        public override bool Equals(object? obj)
        {
            if (obj is Modpack other)
            {
                if (Id != other.Id || Name != other.Name)
                    return false;

                // Compare CreatedAt with tolerance for serialization precision
                if (Math.Abs((CreatedAt - other.CreatedAt).TotalSeconds) > 1)
                    return false;

                if (Mods.Count != other.Mods.Count)
                    return false;

                for (int i = 0; i < Mods.Count; i++)
                {
                    if (!Mods[i].Equals(other.Mods[i]))
                        return false;
                }

                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, CreatedAt, Mods.Count);
        }
    }

    /// <summary>
    /// Configuration class for JSON storage of modpacks
    /// </summary>
    public class ModpackConfig
    {
        public List<Modpack> Modpacks { get; set; } = new();
        public string? SelectedModpackId { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj is ModpackConfig other)
            {
                if (SelectedModpackId != other.SelectedModpackId)
                    return false;

                if (Modpacks.Count != other.Modpacks.Count)
                    return false;

                for (int i = 0; i < Modpacks.Count; i++)
                {
                    if (!Modpacks[i].Equals(other.Modpacks[i]))
                        return false;
                }

                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Modpacks.Count, SelectedModpackId);
        }
    }

    /// <summary>
    /// Manifest format for modpack export/import ZIP files
    /// </summary>
    public class ModpackManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string Name { get; set; } = "";
        public string? Author { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<ModpackMod> Mods { get; set; } = new();
    }
}
