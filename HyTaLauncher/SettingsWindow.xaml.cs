using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
        private string RussifierUrl => Config.RussifierUrl;
        private string OnlineFixUrl => Config.OnlineFixUrl;

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
            CheckGameInstalled();
        }

        private void UpdateUI()
        {
            Title = _localization.Get("settings.title");
            GameDirLabel.Text = _localization.Get("settings.game_folder");
            //InfoText.Text = _localization.Get("settings.info");
            //InfoDescText.Text = _localization.Get("settings.info_desc");
            CancelBtn.Content = _localization.Get("settings.cancel");
            SaveBtn.Content = _localization.Get("settings.save");
            MirrorLabel.Text = _localization.Get("settings.mirror");
            UseMirrorText.Text = _localization.Get("settings.use_mirror");
            MirrorWarningText.Text = _localization.Get("settings.mirror_warning");
            RussifierLabel.Text = _localization.Get("settings.russifier");
            RussifierBtnText.Text = _localization.Get("settings.install_russifier");
            OnlineFixLabel.Text = _localization.Get("settings.onlinefix");
            OnlineFixBtnText.Text = _localization.Get("settings.install_onlinefix");
        }

        private void CheckGameInstalled()
        {
            var gameDir = GetGameDirectory();
            var isInstalled = IsGameInstalled(gameDir);
            
            RussifierBtn.IsEnabled = isInstalled;
            OnlineFixBtn.IsEnabled = isInstalled;
            
            RussifierStatusText.Text = isInstalled 
                ? "" 
                : _localization.Get("settings.russifier_no_game");
            OnlineFixStatusText.Text = isInstalled 
                ? "" 
                : _localization.Get("settings.onlinefix_no_game");
        }

        private string GetGameDirectory()
        {
            var settings = _settingsManager.Load();
            return string.IsNullOrEmpty(settings.GameDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hytale")
                : settings.GameDirectory;
        }

        private bool IsGameInstalled(string gameDir)
        {
            var installDir = Path.Combine(gameDir, "install", "release", "package", "game");
            if (!Directory.Exists(installDir))
                return false;

            // Проверяем есть ли хотя бы одна версия с HytaleClient.exe
            foreach (var dir in Directory.GetDirectories(installDir))
            {
                var clientPath = Path.Combine(dir, "Client", "HytaleClient.exe");
                if (File.Exists(clientPath))
                    return true;
            }
            return false;
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
            
            UseMirrorCheckBox.IsChecked = settings.UseMirror;
            MirrorWarningText.Visibility = settings.UseMirror ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UseMirrorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var isChecked = UseMirrorCheckBox.IsChecked == true;
            MirrorWarningText.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
            
            if (isChecked)
            {
                MessageBox.Show(
                    _localization.Get("settings.mirror_confirm"),
                    _localization.Get("settings.mirror"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
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
            settings.UseMirror = UseMirrorCheckBox.IsChecked == true;
            _settingsManager.Save(settings);
            
            MessageBox.Show(_localization.Get("settings.saved"), 
                _localization.Get("settings.success"), 
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private async void RussifierButton_Click(object sender, RoutedEventArgs e)
        {
            RussifierBtn.IsEnabled = false;
            RussifierStatusText.Text = _localization.Get("settings.russifier_downloading");
            RussifierStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);

            try
            {
                var gameDir = GetGameDirectory();
                var installDir = Path.Combine(gameDir, "install", "release", "package", "game");
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HyTaLauncher", "cache"
                );
                Directory.CreateDirectory(cacheDir);

                var zipPath = Path.Combine(cacheDir, "ru.zip");
                var extractPath = Path.Combine(cacheDir, "ru_temp");

                // Скачиваем архив
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 YaBrowser/25.12.0.0 Safari/537.36");
                
                var response = await httpClient.GetAsync(RussifierUrl);
                response.EnsureSuccessStatusCode();
                
                await using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                RussifierStatusText.Text = _localization.Get("settings.russifier_installing");

                // Распаковываем
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Копируем Client в каждую версию игры
                var clientSourceDir = Path.Combine(extractPath, "Client");
                if (!Directory.Exists(clientSourceDir))
                {
                    // Может быть вложенная папка
                    var dirs = Directory.GetDirectories(extractPath);
                    if (dirs.Length > 0)
                    {
                        clientSourceDir = Path.Combine(dirs[0], "Client");
                    }
                }

                if (!Directory.Exists(clientSourceDir))
                {
                    throw new Exception("Client folder not found in archive");
                }

                int installedCount = 0;
                foreach (var versionDir in Directory.GetDirectories(installDir))
                {
                    var clientDestDir = Path.Combine(versionDir, "Client");
                    if (Directory.Exists(clientDestDir))
                    {
                        CopyDirectory(clientSourceDir, clientDestDir);
                        installedCount++;
                    }
                }

                // Очистка
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                RussifierStatusText.Text = string.Format(_localization.Get("settings.russifier_done"), installedCount);
                RussifierStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x2e, 0xa0, 0x43));
            }
            catch (Exception ex)
            {
                RussifierStatusText.Text = $"{_localization.Get("settings.russifier_error")}: {ex.Message}";
                RussifierStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xcc, 0x33, 0x33));
            }
            finally
            {
                RussifierBtn.IsEnabled = true;
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private async void OnlineFixButton_Click(object sender, RoutedEventArgs e)
        {
            OnlineFixBtn.IsEnabled = false;
            OnlineFixStatusText.Text = _localization.Get("settings.onlinefix_downloading");
            OnlineFixStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);

            try
            {
                var gameDir = GetGameDirectory();
                var installDir = Path.Combine(gameDir, "install", "release", "package", "game");
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HyTaLauncher", "cache"
                );
                Directory.CreateDirectory(cacheDir);

                var zipPath = Path.Combine(cacheDir, "online.zip");
                var extractPath = Path.Combine(cacheDir, "online_temp");

                // Скачиваем архив
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 YaBrowser/25.12.0.0 Safari/537.36");
                
                var response = await httpClient.GetAsync(OnlineFixUrl);
                response.EnsureSuccessStatusCode();
                
                await using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                OnlineFixStatusText.Text = _localization.Get("settings.onlinefix_installing");

                // Распаковываем
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Копируем содержимое в каждую версию игры
                int installedCount = 0;
                foreach (var versionDir in Directory.GetDirectories(installDir))
                {
                    // Копируем всё содержимое архива в папку версии
                    CopyDirectory(extractPath, versionDir);
                    installedCount++;
                }

                // Очистка
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                OnlineFixStatusText.Text = string.Format(_localization.Get("settings.onlinefix_done"), installedCount);
                OnlineFixStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x2e, 0xa0, 0x43));
            }
            catch (Exception ex)
            {
                OnlineFixStatusText.Text = $"{_localization.Get("settings.onlinefix_error")}: {ex.Message}";
                OnlineFixStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xcc, 0x33, 0x33));
            }
            finally
            {
                OnlineFixBtn.IsEnabled = true;
            }
        }
    }
}
