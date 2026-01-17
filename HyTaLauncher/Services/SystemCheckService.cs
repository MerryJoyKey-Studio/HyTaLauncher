using System.IO;
using System.Net.Http;
using Microsoft.Win32;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Service to check system requirements and dependencies on first launch
    /// </summary>
    public class SystemCheckService
    {
        private readonly string _settingsPath;
        
        public SystemCheckService()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HyTaLauncher"
            );
        }
        
        /// <summary>
        /// Check if this is the first launch
        /// </summary>
        public bool IsFirstLaunch()
        {
            var checkFile = Path.Combine(_settingsPath, ".system_checked");
            return !File.Exists(checkFile);
        }
        
        /// <summary>
        /// Mark system check as completed
        /// </summary>
        public void MarkCheckCompleted()
        {
            Directory.CreateDirectory(_settingsPath);
            var checkFile = Path.Combine(_settingsPath, ".system_checked");
            File.WriteAllText(checkFile, DateTime.Now.ToString("o"));
        }
        
        /// <summary>
        /// Run all system checks and return results
        /// </summary>
        public async Task<SystemCheckResult> RunChecksAsync()
        {
            var result = new SystemCheckResult();
            
            // Check .NET Desktop Runtime
            result.DotNetInstalled = CheckDotNetRuntime();
            
            // Check WebView2 Runtime (for potential future use)
            result.WebView2Installed = CheckWebView2();
            
            // Check VC++ Redistributable
            result.VCRedistInstalled = CheckVCRedist();
            
            // Check internet connectivity
            result.InternetAvailable = await CheckInternetAsync();
            
            // Check disk space (at least 5GB free)
            result.SufficientDiskSpace = CheckDiskSpace(5L * 1024 * 1024 * 1024);
            
            // Check write permissions
            result.WritePermissions = CheckWritePermissions();
            
            // Check if Languages folder exists
            result.LanguagesPresent = CheckLanguagesFolder();
            
            // Check if Fonts folder exists
            result.FontsPresent = CheckFontsFolder();
            
            return result;
        }
        
        private bool CheckDotNetRuntime()
        {
            // If we're running, .NET is installed
            // But check for Desktop runtime specifically
            try
            {
                var version = Environment.Version;
                return version.Major >= 8;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckWebView2()
        {
            try
            {
                // Check registry for WebView2
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (key != null)
                {
                    var version = key.GetValue("pv") as string;
                    return !string.IsNullOrEmpty(version);
                }
                
                // Try alternative path
                using var key2 = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (key2 != null)
                {
                    var version = key2.GetValue("pv") as string;
                    return !string.IsNullOrEmpty(version);
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckVCRedist()
        {
            try
            {
                // Check for VC++ 2015-2022 Redistributable
                string[] registryPaths = {
                    @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                    @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
                };
                
                foreach (var path in registryPaths)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        var installed = key.GetValue("Installed");
                        if (installed != null && (int)installed == 1)
                            return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private async Task<bool> CheckInternetAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("https://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckDiskSpace(long requiredBytes)
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var drive = new DriveInfo(Path.GetPathRoot(appDataPath)!);
                return drive.AvailableFreeSpace >= requiredBytes;
            }
            catch
            {
                return true; // Assume OK if can't check
            }
        }
        
        private bool CheckWritePermissions()
        {
            try
            {
                Directory.CreateDirectory(_settingsPath);
                var testFile = Path.Combine(_settingsPath, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private bool CheckLanguagesFolder()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var langDir = Path.Combine(exeDir, "Languages");
            return Directory.Exists(langDir) && Directory.GetFiles(langDir, "*.json").Length > 0;
        }
        
        private bool CheckFontsFolder()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var fontsDir = Path.Combine(exeDir, "Fonts");
            return Directory.Exists(fontsDir) && Directory.GetFiles(fontsDir, "*.ttf").Length > 0;
        }
    }
    
    /// <summary>
    /// Result of system checks
    /// </summary>
    public class SystemCheckResult
    {
        public bool DotNetInstalled { get; set; }
        public bool WebView2Installed { get; set; }
        public bool VCRedistInstalled { get; set; }
        public bool InternetAvailable { get; set; }
        public bool SufficientDiskSpace { get; set; }
        public bool WritePermissions { get; set; }
        public bool LanguagesPresent { get; set; }
        public bool FontsPresent { get; set; }
        
        /// <summary>
        /// Returns true if all critical checks passed
        /// </summary>
        public bool AllCriticalPassed => 
            DotNetInstalled && 
            WritePermissions && 
            LanguagesPresent;
        
        /// <summary>
        /// Returns true if all checks passed
        /// </summary>
        public bool AllPassed => 
            DotNetInstalled && 
            VCRedistInstalled && 
            InternetAvailable && 
            SufficientDiskSpace && 
            WritePermissions && 
            LanguagesPresent && 
            FontsPresent;
        
        /// <summary>
        /// Get list of warnings (non-critical issues)
        /// </summary>
        public List<string> GetWarnings()
        {
            var warnings = new List<string>();
            
            if (!VCRedistInstalled)
                warnings.Add("VC++ Redistributable not found. Some features may not work.");
            
            if (!InternetAvailable)
                warnings.Add("No internet connection. Game download and mods will not work.");
            
            if (!SufficientDiskSpace)
                warnings.Add("Low disk space. At least 5GB recommended for game installation.");
            
            if (!FontsPresent)
                warnings.Add("Fonts folder not found. Using system fonts.");
            
            if (!WebView2Installed)
                warnings.Add("WebView2 not installed. Some features may be limited.");
            
            return warnings;
        }
        
        /// <summary>
        /// Get list of critical errors
        /// </summary>
        public List<string> GetErrors()
        {
            var errors = new List<string>();
            
            if (!DotNetInstalled)
                errors.Add(".NET 8 Runtime not properly installed.");
            
            if (!WritePermissions)
                errors.Add("Cannot write to AppData folder. Check permissions.");
            
            if (!LanguagesPresent)
                errors.Add("Languages folder not found. Reinstall the application.");
            
            return errors;
        }
    }
}
