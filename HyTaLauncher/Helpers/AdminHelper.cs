using System.Diagnostics;
using System.Security.Principal;
using HyTaLauncher.Services;

namespace HyTaLauncher.Helpers
{
    /// <summary>
    /// Helper class for administrator privilege management
    /// </summary>
    public static class AdminHelper
    {
        /// <summary>
        /// Checks if the current process is running with administrator privileges
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to check admin status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restarts the current application with administrator privileges
        /// </summary>
        /// <returns>True if restart was initiated, false otherwise</returns>
        public static bool RestartAsAdmin()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    LogService.LogError("Failed to get current executable path for admin restart");
                    return false;
                }

                LogService.LogGame($"Restarting as administrator: {exePath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas" // This triggers UAC prompt
                };

                Process.Start(startInfo);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                LogService.LogGameVerbose("User cancelled UAC prompt");
                return false;
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to restart as admin: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Starts a process with administrator privileges
        /// </summary>
        /// <param name="fileName">Path to the executable</param>
        /// <param name="arguments">Command line arguments</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <returns>Started process or null if failed</returns>
        public static Process? StartAsAdmin(string fileName, string? arguments = null, string? workingDirectory = null)
        {
            try
            {
                LogService.LogGame($"Starting as administrator: {fileName}");
                LogService.LogGameVerbose($"Arguments: {arguments ?? "(none)"}");
                LogService.LogGameVerbose($"Working directory: {workingDirectory ?? "(default)"}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                if (!string.IsNullOrEmpty(arguments))
                {
                    startInfo.Arguments = arguments;
                }

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                var process = Process.Start(startInfo);
                LogService.LogGame($"Process started as admin: PID={process?.Id}");
                return process;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                LogService.LogGameVerbose("User cancelled UAC prompt for process start");
                return null;
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to start process as admin: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Starts a process normally (without elevation)
        /// </summary>
        /// <param name="fileName">Path to the executable</param>
        /// <param name="arguments">Command line arguments</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <returns>Started process or null if failed</returns>
        public static Process? StartNormal(string fileName, string? arguments = null, string? workingDirectory = null)
        {
            try
            {
                LogService.LogGame($"Starting process: {fileName}");
                LogService.LogGameVerbose($"Arguments: {arguments ?? "(none)"}");
                LogService.LogGameVerbose($"Working directory: {workingDirectory ?? "(default)"}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false
                };

                if (!string.IsNullOrEmpty(arguments))
                {
                    startInfo.Arguments = arguments;
                }

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                var process = Process.Start(startInfo);
                LogService.LogGame($"Process started: PID={process?.Id}");
                return process;
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to start process: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Starts a process, optionally as administrator
        /// </summary>
        /// <param name="fileName">Path to the executable</param>
        /// <param name="arguments">Command line arguments</param>
        /// <param name="workingDirectory">Working directory for the process</param>
        /// <param name="runAsAdmin">Whether to run as administrator</param>
        /// <returns>Started process or null if failed</returns>
        public static Process? Start(string fileName, string? arguments = null, string? workingDirectory = null, bool runAsAdmin = false)
        {
            if (runAsAdmin)
            {
                return StartAsAdmin(fileName, arguments, workingDirectory);
            }
            else
            {
                return StartNormal(fileName, arguments, workingDirectory);
            }
        }

        /// <summary>
        /// Gets a display string for the current admin status
        /// </summary>
        public static string GetAdminStatusString()
        {
            return IsRunningAsAdmin() ? "Running as Administrator" : "Running as Standard User";
        }

        /// <summary>
        /// Checks if UAC is enabled on the system
        /// </summary>
        public static bool IsUacEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");

                if (key != null)
                {
                    var enableLua = key.GetValue("EnableLUA");
                    return enableLua != null && (int)enableLua == 1;
                }

                return true; // Assume UAC is enabled if we can't check
            }
            catch
            {
                return true; // Assume UAC is enabled if we can't check
            }
        }
    }
}
