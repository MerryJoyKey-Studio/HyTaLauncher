using System;
using System.IO;

namespace HyTaLauncher
{
    public static class Config
    {
        // API key - loaded from .env file or environment variable
        private static string? _curseForgeApiKey;
        private static string? _mirrorUrl;
        private static string? _russifierUrl;
        
        public static string CurseForgeApiKey
        {
            get
            {
                if (_curseForgeApiKey == null)
                {
                    _curseForgeApiKey = LoadSecret("CURSEFORGE_API_KEY");
                }
                return _curseForgeApiKey;
            }
        }
        
        public static string MirrorUrl
        {
            get
            {
                if (_mirrorUrl == null)
                {
                    _mirrorUrl = LoadSecret("MIRROR_URL");
                }
                return _mirrorUrl;
            }
        }
        
        public static string RussifierUrl
        {
            get
            {
                if (_russifierUrl == null)
                {
                    _russifierUrl = LoadSecret("RUSSIFIER_URL");
                }
                return _russifierUrl;
            }
        }
        
        private static string? _onlineFixUrl;
        
        public static string OnlineFixUrl
        {
            get
            {
                if (_onlineFixUrl == null)
                {
                    _onlineFixUrl = LoadSecret("ONLINEFIX_URL");
                }
                return _onlineFixUrl;
            }
        }
        
        private static string LoadSecret(string key)
        {
            // 1. Try environment variable first
            var envValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(envValue))
                return envValue;
            
            // 2. Try .env file in app directory
            var envPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".env"), // Debug from VS
                ".env"
            };
            
            foreach (var envPath in envPaths)
            {
                if (File.Exists(envPath))
                {
                    foreach (var line in File.ReadAllLines(envPath))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith($"{key}="))
                        {
                            return trimmed.Substring($"{key}=".Length).Trim();
                        }
                    }
                }
            }
            
            // 3. Fallback - hardcoded placeholders (will be replaced at build time for releases)
            // These strings are replaced by build.ps1 or GitHub Actions
            return key switch
            {
                "CURSEFORGE_API_KEY" => "#{CURSEFORGE_API_KEY}#",
                "MIRROR_URL" => "#{MIRROR_URL}#",
                "RUSSIFIER_URL" => "#{RUSSIFIER_URL}#",
                "ONLINEFIX_URL" => "#{ONLINEFIX_URL}#",
                _ => ""
            };
        }
    }
}






















































