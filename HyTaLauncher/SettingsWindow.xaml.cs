using System.IO;
using System.Windows;
using System.Windows.Input;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;
using Microsoft.Win32;

namespace HyTaLauncher
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsManager _settingsManager;
        private readonly LocalizationService _localization;

        public SettingsWindow(SettingsManager settingsManager, LocalizationService localization)
        {
            FontHelper.Initialize();
            InitializeComponent();
            
            // Применяем шрифт
            if (FontHelper.CinzelFont != null)
            {
                FontFamily = FontHelper.CinzelFont;
            }
            
            _settingsManager = settingsManager;
            _localization = localization;
            LoadSettings();
            UpdateUI();
        }

        private void UpdateUI()
        {
            Title = _localization.Get("settings.title");
            GameDirLabel.Text = _localization.Get("settings.game_folder");
            InfoText.Text = _localization.Get("settings.info");
            InfoDescText.Text = _localization.Get("settings.info_desc");
            CancelBtn.Content = _localization.Get("settings.cancel");
            SaveBtn.Content = _localization.Get("settings.save");
        }

        private void LoadSettings()
        {
            var settings = _settingsManager.Load();
            
            var defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hytale"
            );
            GameDirTextBox.Text = string.IsNullOrEmpty(settings.GameDirectory) 
                ? defaultDir 
                : settings.GameDirectory;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = _localization.Get("settings.select_folder")
            };

            if (dialog.ShowDialog() == true)
            {
                GameDirTextBox.Text = dialog.FolderName;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsManager.Load();
            settings.GameDirectory = GameDirTextBox.Text;
            _settingsManager.Save(settings);
            
            MessageBox.Show(_localization.Get("settings.saved"), 
                _localization.Get("settings.success"), 
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
