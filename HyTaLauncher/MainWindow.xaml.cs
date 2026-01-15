using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class MainWindow : Window
    {
        private readonly GameLauncher _gameLauncher;
        private readonly SettingsManager _settings;
        private readonly NewsFeedService _newsFeed;
        private readonly LocalizationService _localization;
        private readonly UpdateService _updateService;
        private List<GameVersion> _versions = new List<GameVersion>();

        public MainWindow()
        {
            // Загружаем шрифты из файлов
            FontHelper.Initialize();
            
            InitializeComponent();
            
            // Применяем шрифт ко всему окну
            if (FontHelper.CinzelFont != null)
            {
                FontFamily = FontHelper.CinzelFont;
            }
            
            _settings = new SettingsManager();
            _gameLauncher = new GameLauncher();
            _newsFeed = new NewsFeedService();
            _localization = new LocalizationService();
            _updateService = new UpdateService();
            
            _localization.LanguageChanged += UpdateUI;
            
            LoadSettings();
            InitializeLanguages();
            UpdateUI();
            
            _gameLauncher.ProgressChanged += OnProgressChanged;
            _gameLauncher.StatusChanged += OnStatusChanged;
            
            Loaded += async (s, e) =>
            {
                await LoadVersionsAsync();
                await LoadNewsAsync();
                await CheckForUpdatesAsync();
            };
        }

        private void InitializeLanguages()
        {
            var languages = _localization.GetAvailableLanguages();
            LanguageComboBox.ItemsSource = languages;
            LanguageComboBox.SelectedItem = _localization.CurrentLanguage;
        }

        private void UpdateUI()
        {
            NewsTitle.Text = _localization.Get("main.news");
            NicknameLabel.Text = _localization.Get("main.nickname");
            VersionLabel.Text = _localization.Get("main.version");
            BranchLabel.Text = _localization.Get("main.branch");
            PlayButton.Content = _localization.Get("main.play");
            SettingsLink.Text = _localization.Get("main.settings");
            ModsLink.Text = _localization.Get("main.mods");
            FooterText.Text = _localization.Get("main.footer");
            DisclaimerText.Text = _localization.Get("main.disclaimer");
            StatusText.Text = _localization.Get("main.preparing");
            StartServerText.Text = _localization.Get("main.start_server");
            VpnHintText.Text = _localization.Get("main.vpn_hint");
            
            CheckServerAvailable();
        }

        private void LoadSettings()
        {
            var settings = _settings.Load();
            NicknameTextBox.Text = settings.Nickname;
            BranchComboBox.SelectedIndex = settings.VersionIndex;
            _localization.LoadLanguage(settings.Language);
            _gameLauncher.UseMirror = settings.UseMirror;
            
            // Устанавливаем папку игры
            if (!string.IsNullOrEmpty(settings.GameDirectory))
            {
                _gameLauncher.GameDirectory = settings.GameDirectory;
            }
        }

        private void SaveSettings()
        {
            var settings = _settings.Load();
            settings.Nickname = NicknameTextBox.Text;
            settings.VersionIndex = BranchComboBox.SelectedIndex;
            settings.Language = _localization.CurrentLanguage;
            _settings.Save(settings);
        }

        private async Task LoadNewsAsync()
        {
            var articles = await _newsFeed.GetNewsAsync();
            NewsItemsControl.ItemsSource = articles;
        }

        private async Task CheckForUpdatesAsync()
        {
            var update = await _updateService.CheckForUpdatesAsync();
            if (update != null)
            {
                var message = string.Format(
                    _localization.Get("update.message"),
                    update.Version,
                    UpdateService.CurrentVersion
                );

                var result = MessageBox.Show(
                    message,
                    _localization.Get("update.available"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = update.HtmlUrl,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private async Task LoadVersionsAsync()
        {
            VersionComboBox.IsEnabled = false;
            PlayButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                var branch = GetSelectedBranch();
                _versions = await _gameLauncher.GetAvailableVersionsAsync(branch, _localization);
                
                // Сохраняем версии для определения базы при установке
                _gameLauncher.SetVersionsCache(_versions);
                
                VersionComboBox.ItemsSource = _versions;
                if (_versions.Count > 0)
                {
                    VersionComboBox.SelectedIndex = 0;
                    VersionComboBox.IsEnabled = true;
                }
                
                StatusText.Text = string.Format(_localization.Get("main.versions_found"), _versions.Count);
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(_localization.Get("error.launch"), ex.Message);
            }
            finally
            {
                PlayButton.IsEnabled = true;
                RefreshButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                ResetProgress();
            }
        }

        private string GetSelectedBranch()
        {
            return BranchComboBox.SelectedIndex switch
            {
                0 => "release",
                1 => "pre-release",
                2 => "beta",
                3 => "alpha",
                _ => "release"
            };
        }

        private async void BranchComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await LoadVersionsAsync();
        }

        private async void RefreshVersions_Click(object sender, RoutedEventArgs e)
        {
            await LoadVersionsAsync();
        }

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded && LanguageComboBox.SelectedItem is string lang)
            {
                _localization.LoadLanguage(lang);
                SaveSettings();
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var nickname = NicknameTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(nickname))
            {
                MessageBox.Show(_localization.Get("error.nickname_empty"), 
                    _localization.Get("error.title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (nickname.Length < 3 || nickname.Length > 16)
            {
                MessageBox.Show(_localization.Get("error.nickname_length"), 
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedVersion = VersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion == null)
            {
                MessageBox.Show(_localization.Get("error.version_select"), 
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettings();
            
            PlayButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                await _gameLauncher.LaunchGameAsync(nickname, selectedVersion, _localization);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(_localization.Get("error.launch"), ex.Message), 
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PlayButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                ResetProgress();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            Close();
        }

        private void OnProgressChanged(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (progress < 0)
                {
                    // Неопределённый прогресс (мерцание)
                    DownloadProgress.IsIndeterminate = true;
                    ProgressPercent.Text = "...";
                }
                else
                {
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = progress;
                    ProgressPercent.Text = $"{progress:F1}%";
                }
            });
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        private void ResetProgress()
        {
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = 0;
            ProgressPercent.Text = "0%";
            StatusText.Text = _localization.Get("main.preparing");
        }

        private void Settings_Click(object sender, MouseButtonEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings, _localization);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
            
            // Обновляем настройки после закрытия окна настроек
            var settings = _settings.Load();
            _gameLauncher.UseMirror = settings.UseMirror;
            
            // Обновляем папку игры
            if (!string.IsNullOrEmpty(settings.GameDirectory))
            {
                _gameLauncher.GameDirectory = settings.GameDirectory;
            }
            
            // Проверяем доступность сервера (мог быть установлен онлайн фикс)
            CheckServerAvailable();
        }

        private void Mods_Click(object sender, MouseButtonEventArgs e)
        {
            var modsWindow = new ModsWindow(_localization, _settings);
            modsWindow.Owner = this;
            modsWindow.ShowDialog();
        }

        private void NewsItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is NewsArticle article)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = article.DestUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void Store_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://store.hytale.com",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void CheckServerAvailable()
        {
            var serverBatPath = GetServerBatPath();
            StartServerButton.Visibility = !string.IsNullOrEmpty(serverBatPath) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private string? GetServerBatPath()
        {
            var settings = _settings.Load();
            var gameDir = string.IsNullOrEmpty(settings.GameDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hytale")
                : settings.GameDirectory;

            var installDir = Path.Combine(gameDir, "install", "release", "package", "game");
            if (!Directory.Exists(installDir))
                return null;

            // Проверяем каждую версию на наличие start-server.bat
            foreach (var versionDir in Directory.GetDirectories(installDir))
            {
                var serverBat = Path.Combine(versionDir, "Server", "start-server.bat");
                if (File.Exists(serverBat))
                    return serverBat;
            }

            return null;
        }

        private void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            var serverBatPath = GetServerBatPath();
            if (string.IsNullOrEmpty(serverBatPath))
                return;

            // Показываем инструкции при первом запуске
            var settings = _settings.Load();
            if (!settings.ServerInfoShown)
            {
                MessageBox.Show(
                    _localization.Get("main.server_info"),
                    _localization.Get("main.server_info_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                
                settings.ServerInfoShown = true;
                _settings.Save(settings);
            }

            try
            {
                var serverDir = Path.GetDirectoryName(serverBatPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = serverBatPath,
                    WorkingDirectory = serverDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(_localization.Get("error.launch"), ex.Message),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
