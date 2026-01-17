using System.Text;
using System.Windows;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            var systemCheck = new SystemCheckService();
            
            // Run system check on first launch
            if (systemCheck.IsFirstLaunch())
            {
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
            }
        }
    }
}
