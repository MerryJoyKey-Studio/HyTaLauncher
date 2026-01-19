using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handlers
            SetupExceptionHandling();

            // Clean old logs on startup
            LogService.CleanOldLogs(7);
            LogService.LogGame("=== Application starting ===");

            // Check if we should restart as admin
            if (ShouldRestartAsAdmin())
            {
                LogService.LogGame("Restarting as administrator per user settings...");
                if (AdminHelper.RestartAsAdmin())
                {
                    Shutdown();
                    return;
                }
                else
                {
                    LogService.LogGame("Failed to restart as admin, continuing normally");
                }
            }

            // Log current admin status
            LogService.LogGameVerbose($"Admin status: {AdminHelper.GetAdminStatusString()}");

            var systemCheck = new SystemCheckService();

            // Run system check on first launch
            if (systemCheck.IsFirstLaunch())
            {
                LogService.LogGameVerbose("Running first launch system check...");
                var result = await systemCheck.RunChecksAsync();

                // Show errors if critical checks failed
                var errors = result.GetErrors();
                if (errors.Count > 0)
                {
                    var errorMsg = new StringBuilder();
                    errorMsg.AppendLine("Critical issues detected:\n");
                    foreach (var error in errors)
                    {
                        errorMsg.AppendLine($"• {error}");
                        LogService.LogError($"System check error: {error}");
                    }
                    errorMsg.AppendLine("\nThe launcher may not work correctly.");

                    MessageBox.Show(
                        errorMsg.ToString(),
                        "HyTaLauncher - System Check",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }

                // Show warnings if non-critical issues found
                var warnings = result.GetWarnings();
                if (warnings.Count > 0 && errors.Count == 0)
                {
                    var warnMsg = new StringBuilder();
                    warnMsg.AppendLine("Some issues were detected:\n");
                    foreach (var warning in warnings)
                    {
                        warnMsg.AppendLine($"• {warning}");
                        LogService.LogGameVerbose($"System check warning: {warning}");
                    }
                    warnMsg.AppendLine("\nThe launcher will still work, but some features may be limited.");

                    MessageBox.Show(
                        warnMsg.ToString(),
                        "HyTaLauncher - System Check",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }

                // Mark check as completed
                systemCheck.MarkCheckCompleted();
                LogService.LogGameVerbose("System check completed");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.LogGame("=== Application exiting ===");
            base.OnExit(e);
        }

        /// <summary>
        /// Checks if the application should restart as administrator
        /// </summary>
        private bool ShouldRestartAsAdmin()
        {
            try
            {
                // Already running as admin - no need to restart
                if (AdminHelper.IsRunningAsAdmin())
                {
                    return false;
                }

                // Check settings
                var settingsManager = new SettingsManager();
                var settings = settingsManager.Load();

                // If user wants to run as admin, restart
                return settings.RunLauncherAsAdmin;
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to check admin settings: {ex.Message}");
                return false;
            }
        }

        private void SetupExceptionHandling()
        {
            // Handle exceptions on the UI thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Handle exceptions on background threads
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Handle exceptions in async tasks
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("Unhandled UI exception", e.Exception);
            e.Handled = ShowErrorAndContinue(e.Exception);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogException("Unhandled domain exception", exception);

            if (e.IsTerminating)
            {
                ShowFatalError(exception);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("Unobserved task exception", e.Exception);
            e.SetObserved(); // Prevent the process from crashing

            // Show error on UI thread if possible
            Dispatcher?.BeginInvoke(() => ShowErrorAndContinue(e.Exception));
        }

        private static void LogException(string context, Exception? exception)
        {
            if (exception == null)
            {
                LogService.LogError($"{context}: Unknown exception (null)");
                return;
            }

            var logMessage = new StringBuilder();
            logMessage.AppendLine($"{context}:");
            logMessage.AppendLine($"Type: {exception.GetType().FullName}");
            logMessage.AppendLine($"Message: {exception.Message}");
            logMessage.AppendLine($"Source: {exception.Source}");
            logMessage.AppendLine($"StackTrace: {exception.StackTrace}");

            // Log inner exceptions
            var innerException = exception.InnerException;
            var depth = 0;
            while (innerException != null && depth < 5)
            {
                logMessage.AppendLine($"--- Inner Exception {++depth} ---");
                logMessage.AppendLine($"Type: {innerException.GetType().FullName}");
                logMessage.AppendLine($"Message: {innerException.Message}");
                logMessage.AppendLine($"StackTrace: {innerException.StackTrace}");
                innerException = innerException.InnerException;
            }

            LogService.LogError(logMessage.ToString());

            // Also write to a crash log file
            try
            {
                var crashLogPath = Path.Combine(LogService.GetLogsFolder(), $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                File.WriteAllText(crashLogPath, logMessage.ToString());
            }
            catch
            {
                // Ignore errors writing crash log
            }
        }

        private static bool ShowErrorAndContinue(Exception exception)
        {
            try
            {
                var message = GetUserFriendlyMessage(exception);

                var result = MessageBox.Show(
                    $"{message}\n\nWould you like to continue using the launcher?\n\nClick 'No' to exit and check the logs folder for details.",
                    "HyTaLauncher - Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error
                );

                return result == MessageBoxResult.Yes;
            }
            catch
            {
                return false;
            }
        }

        private static void ShowFatalError(Exception? exception)
        {
            try
            {
                var message = exception != null
                    ? GetUserFriendlyMessage(exception)
                    : "An unknown fatal error occurred.";

                MessageBox.Show(
                    $"{message}\n\nThe application will now close.\n\nPlease check the logs folder for more details:\n{LogService.GetLogsFolder()}",
                    "HyTaLauncher - Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            catch
            {
                // Last resort - can't show message box
            }
        }

        private static string GetUserFriendlyMessage(Exception exception)
        {
            // Provide user-friendly messages for common exceptions
            return exception switch
            {
                OutOfMemoryException => "The application ran out of memory. Try closing some other programs.",
                IOException io when io.Message.Contains("disk") => "A disk error occurred. Please check your available disk space.",
                IOException => "A file access error occurred. Please check that you have permission to access the game folder.",
                UnauthorizedAccessException => "Access denied. Try running the launcher as administrator.",
                System.Net.Http.HttpRequestException => "A network error occurred. Please check your internet connection.",
                System.Net.WebException => "A network error occurred. Please check your internet connection.",
                TaskCanceledException => "An operation timed out. Please try again.",
                OperationCanceledException => "The operation was cancelled.",
                System.Security.SecurityException => "A security error occurred. Try running the launcher as administrator.",
                _ => $"An unexpected error occurred: {exception.Message}"
            };
        }
    }
}
