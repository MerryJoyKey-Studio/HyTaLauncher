using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;
using Process = System.Diagnostics.Process;

namespace HyTaLauncher
{
    public partial class MainWindow : Window
    {
        private readonly GameLauncher _gameLauncher;
        private readonly SettingsManager _settings;
        private readonly NewsFeedService _newsFeed;
        private readonly LocalizationService _localization;
        private readonly UpdateService _updateService;
        private readonly ModpackService _modpackService;
        private List<GameVersion> _versions = new List<GameVersion>();
        private CancellationTokenSource? _currentOperationCts;

        public MainWindow()
        {
            _settings = new SettingsManager();

            // Загружаем шрифты из файлов с учётом настроек
            var settings = _settings.Load();

            // Устанавливаем verbose logging из настроек
            LogService.VerboseLogging = settings.VerboseLogging;
            LogService.LogGame("=== HyTaLauncher starting ===");
            LogService.LogGameVerbose($"Verbose logging: {settings.VerboseLogging}");
            LogService.LogGameVerbose($"Game directory: {settings.GameDirectory}");
            LogService.LogGameVerbose($"Language: {settings.Language}");

            FontHelper.Initialize(settings.FontName ?? "Inter");

            InitializeComponent();

            // Применяем шрифт ко всему окну
            if (FontHelper.CurrentFont != null)
            {
                FontFamily = FontHelper.CurrentFont;
            }
            _gameLauncher = new GameLauncher();
            _newsFeed = new NewsFeedService();
            _localization = new LocalizationService();
            _updateService = new UpdateService();
            _modpackService = new ModpackService();

            _localization.LanguageChanged += UpdateUI;
            _modpackService.ModpacksChanged += LoadModpacks;

            LoadSettings();

            // Миграция данных из старых папок UserData в новую общую папку (v1.0.7)
            LogService.LogGameVerbose("Checking for UserData migration...");
            _gameLauncher.MigrateUserData();
            InitializeLanguages();
            UpdateUI();

            _gameLauncher.ProgressChanged += OnProgressChanged;
            _gameLauncher.StatusChanged += OnStatusChanged;

            Loaded += async (s, e) =>
            {
                LogService.LogGameVerbose("MainWindow loaded, starting initialization...");
                await LoadVersionsAsync();
                await LoadNewsAsync();
                await CheckForUpdatesAsync();
                SetupLogoFallback();
                LoadModpacks();
                LogService.LogGameVerbose("MainWindow initialization complete");
            };
        }

        private void LoadModpacks()
        {
            var modpacks = _modpackService.GetAllModpacks();
            var selectedId = _settings.Load().SelectedModpackId;

            // Create list with "Default" option first
            var items = new List<ModpackComboItem>
            {
                new ModpackComboItem { Id = null, Name = _localization.Get("main.modpack_default") }
            };

            foreach (var modpack in modpacks)
            {
                items.Add(new ModpackComboItem { Id = modpack.Id, Name = modpack.Name });
            }

            ModpackComboBox.ItemsSource = items;

            // Select the saved modpack or default
            var selectedIndex = 0;
            if (!string.IsNullOrEmpty(selectedId))
            {
                var index = items.FindIndex(m => m.Id == selectedId);
                if (index >= 0)
                {
                    selectedIndex = index;
                }
                else
                {
                    // Selected modpack was deleted, reset to default
                    var settings = _settings.Load();
                    settings.SelectedModpackId = null;
                    _settings.Save(settings);
                }
            }

            ModpackComboBox.SelectedIndex = selectedIndex;

            // Update GameLauncher with selected modpack
            UpdateGameLauncherModpack();
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
            ModpackLabel.Text = _localization.Get("main.modpack");
            OpenModsButton.ToolTip = _localization.Get("main.manage_modpacks");
            PlayButton.Content = _localization.Get("main.play");
            SettingsLink.Text = _localization.Get("main.settings");
            ModsLink.Text = _localization.Get("main.mods");
            FooterText.Text = _localization.Get("main.footer");
            DisclaimerText.Text = _localization.Get("main.disclaimer");
            StatusText.Text = _localization.Get("main.preparing");
            StartServerText.Text = _localization.Get("main.start_server");
            VpnHintText.Text = _localization.Get("main.vpn_hint");
            ReinstallButton.ToolTip = _localization.Get("main.reinstall");
            WebsiteText.Text = _localization.Get("main.website");
            DiscordText.Text = _localization.Get("main.discord");

            // Refresh modpack list to update "Default" text
            LoadModpacks();

            CheckServerAvailable();
            UpdateReinstallButtonVisibility();
        }

        private void LoadSettings()
        {
            var settings = _settings.Load();
            NicknameTextBox.Text = settings.Nickname;
            BranchComboBox.SelectedIndex = settings.VersionIndex;
            _localization.LoadLanguage(settings.Language);
            _gameLauncher.UseMirror = settings.UseMirror;
            _gameLauncher.AlwaysFullDownload = settings.AlwaysFullDownload;
            _gameLauncher.CustomGameArgs = settings.CustomGameArgs;

            // Инициализируем подробное логирование
            LogService.VerboseLogging = settings.VerboseLogging;

            // Устанавливаем папку игры
            if (!string.IsNullOrEmpty(settings.GameDirectory))
            {
                _gameLauncher.GameDirectory = settings.GameDirectory;
            }

            // Устанавливаем запуск от админа
            _gameLauncher.RunGameAsAdmin = settings.RunGameAsAdmin;

            // Устанавливаем настройку SSL
            _gameLauncher.BypassSslValidation = settings.BypassSslValidation;
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
                // Проверяем есть ли portable версия для автообновления
                var hasPortable = !string.IsNullOrEmpty(update.PortableDownloadUrl);

                var message = hasPortable
                    ? string.Format(_localization.Get("update.message_auto"), update.Version, UpdateService.CurrentVersion)
                    : string.Format(_localization.Get("update.message"), update.Version, UpdateService.CurrentVersion);

                var result = MessageBox.Show(
                    message,
                    _localization.Get("update.available"),
                    hasPortable ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes && hasPortable)
                {
                    // Автообновление
                    await PerformAutoUpdateAsync(update);
                }
                else if (result == MessageBoxResult.No && hasPortable)
                {
                    // Открыть страницу загрузки вручную
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

        private async Task PerformAutoUpdateAsync(UpdateInfo update)
        {
            PlayButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            _updateService.ProgressChanged += OnUpdateProgressChanged;
            _updateService.StatusChanged += OnStatusChanged;

            try
            {
                var success = await _updateService.DownloadAndApplyUpdateAsync(update, _localization);
                if (success)
                {
                    // Показываем сообщение перед закрытием
                    MessageBox.Show(
                        _localization.Get("update.restarting"),
                        _localization.Get("update.restarting_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    // Закрываем приложение - скрипт перезапустит
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(_localization.Get("update.error"), ex.Message),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                _updateService.ProgressChanged -= OnUpdateProgressChanged;
                _updateService.StatusChanged -= OnStatusChanged;
                PlayButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                ResetProgress();
            }
        }

        private void OnUpdateProgressChanged(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (progress < 0)
                {
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

        private async Task LoadVersionsAsync()
        {
            var branch = GetSelectedBranch();
            LogService.LogGame($"Loading versions for branch: {branch}");
            LogService.LogGameVerbose($"Mirror enabled: {_gameLauncher.UseMirror}");

            VersionComboBox.IsEnabled = false;
            PlayButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                _versions = await _gameLauncher.GetAvailableVersionsAsync(branch, _localization);

                LogService.LogGame($"Found {_versions.Count} versions for branch {branch}");
                LogService.LogGameVerbose($"Versions: {string.Join(", ", _versions.Select(v => v.Version))}");

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
                LogService.LogError($"Failed to load versions: {ex.Message}");
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
            {
                await LoadVersionsAsync();
                UpdateReinstallButtonVisibility();
                CheckServerAvailable();
            }
        }

        private async void RefreshVersions_Click(object sender, RoutedEventArgs e)
        {
            await LoadVersionsAsync();
        }

        private void VersionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                UpdateReinstallButtonVisibility();
                CheckServerAvailable();
            }
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
            LogService.LogGameVerbose($"Play button clicked, nickname: {nickname}");

            if (string.IsNullOrEmpty(nickname))
            {
                LogService.LogGameVerbose("Nickname is empty, showing error");
                MessageBox.Show(_localization.Get("error.nickname_empty"),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (nickname.Length < 3 || nickname.Length > 16)
            {
                LogService.LogGameVerbose($"Nickname length invalid: {nickname.Length}");
                MessageBox.Show(_localization.Get("error.nickname_length"),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedVersion = VersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion == null)
            {
                LogService.LogGameVerbose("No version selected");
                MessageBox.Show(_localization.Get("error.version_select"),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogService.LogGame($"Starting game launch: player={nickname}, version={selectedVersion.Version}, branch={selectedVersion.Branch}");
            LogService.LogGameVerbose($"Selected modpack: {_gameLauncher.SelectedModpackId ?? "default"}");

            SaveSettings();

            PlayButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;

            // Create new cancellation token for this operation
            _currentOperationCts?.Cancel();
            _currentOperationCts?.Dispose();
            _currentOperationCts = new CancellationTokenSource();
            var cancellationToken = _currentOperationCts.Token;

            try
            {
                await _gameLauncher.LaunchGameAsync(nickname, selectedVersion, _localization);
                LogService.LogGame("Game launch completed successfully");
            }
            catch (Exception ex)
            {
                LogService.LogError($"Game launch failed: {ex.Message}");

                // Check for SSL errors first
                if (HttpRetryService.IsSslError(ex))
                {
                    LogService.LogError("SSL error detected during game launch");
                    var settings = _settings.Load();

                    var sslMessage = _localization.Get("settings.ssl_error_message");
                    if (!settings.BypassSslValidation)
                    {
                        sslMessage += "\n\n" + _localization.Get("settings.ssl_enable_bypass_hint");
                    }

                    var result = MessageBox.Show(
                        sslMessage,
                        _localization.Get("settings.ssl_error_title"),
                        settings.BypassSslValidation ? MessageBoxButton.OK : MessageBoxButton.YesNo,
                        MessageBoxImage.Error
                    );

                    // Offer to enable SSL bypass
                    if (!settings.BypassSslValidation && result == MessageBoxResult.Yes)
                    {
                        settings.BypassSslValidation = true;
                        _settings.Save(settings);
                        _gameLauncher.BypassSslValidation = true;

                        MessageBox.Show(
                            _localization.Get("settings.ssl_bypass_enabled"),
                            _localization.Get("settings.success"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                }
                // Если ошибка связана с повреждёнными файлами - предлагаем переустановку
                else if (ex.Message.Contains(_localization.Get("error.corrupted_files")) ||
                    ex.Message.Contains("corrupted") ||
                    ex.Message.Contains("повреждён"))
                {
                    LogService.LogGameVerbose("Corrupted files detected, offering reinstall");
                    var result = MessageBox.Show(
                        _localization.Get("error.corrupted_reinstall"),
                        _localization.Get("error.title"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        // Запускаем переустановку
                        LogService.LogGame("User accepted reinstall");
                        try
                        {
                            await _gameLauncher.ReinstallGameAsync(selectedVersion, _localization);
                            LogService.LogGame("Reinstall completed");
                        }
                        catch (Exception reinstallEx)
                        {
                            LogService.LogError($"Reinstall failed: {reinstallEx.Message}");
                            MessageBox.Show(
                                string.Format(_localization.Get("error.launch"), reinstallEx.Message),
                                _localization.Get("error.title"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                        }
                    }
                }
                else
                {
                    // Get friendly error message
                    var friendlyMessage = HttpRetryServiceExtensions.GetFriendlyErrorMessage(ex);
                    var errorMsg = string.Format(_localization.Get("error.launch"), friendlyMessage);

                    MessageBox.Show(errorMsg,
                        _localization.Get("error.title"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                PlayButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                ResetProgress();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LogService.LogGame("User requested cancellation of current operation");

            if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
            {
                _currentOperationCts.Cancel();
                _gameLauncher.CancelCurrentOperation();
                StatusText.Text = _localization.Get("status.cancelling");
                CancelButton.IsEnabled = false;
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
            _gameLauncher.AlwaysFullDownload = settings.AlwaysFullDownload;
            _gameLauncher.CustomGameArgs = settings.CustomGameArgs;
            _gameLauncher.RunGameAsAdmin = settings.RunGameAsAdmin;
            _gameLauncher.BypassSslValidation = settings.BypassSslValidation;
            LogService.VerboseLogging = settings.VerboseLogging;

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

            // Refresh modpacks after mods window closes (user might have created/deleted modpacks)
            LoadModpacks();
        }

        private void OpenMods_Click(object sender, RoutedEventArgs e)
        {
            var modsWindow = new ModsWindow(_localization, _settings);
            modsWindow.Owner = this;
            modsWindow.ShowDialog();

            // Refresh modpacks after mods window closes
            LoadModpacks();
        }

        private void ModpackComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            var selectedItem = ModpackComboBox.SelectedItem as ModpackComboItem;
            var settings = _settings.Load();
            settings.SelectedModpackId = selectedItem?.Id;
            _settings.Save(settings);

            UpdateGameLauncherModpack();
        }

        private void UpdateGameLauncherModpack()
        {
            var settings = _settings.Load();
            _gameLauncher.SelectedModpackId = settings.SelectedModpackId;
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

        private void Website_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://hytalauncher.ru",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void Discord_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/Hwtew6UfQw",
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

            // Получаем текущую выбранную ветку
            var selectedVersion = VersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion == null)
                return null;

            var installDir = Path.Combine(gameDir, "install", selectedVersion.Branch, "package", "game");
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
            LogService.LogGameVerbose($"Starting server, bat path: {serverBatPath}");

            if (string.IsNullOrEmpty(serverBatPath))
            {
                LogService.LogGameVerbose("Server bat path is empty, aborting");
                return;
            }

            // Показываем инструкции при первом запуске
            var settings = _settings.Load();
            if (!settings.ServerInfoShown)
            {
                LogService.LogGameVerbose("Showing server info dialog (first run)");
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
                LogService.LogGame($"Launching server: {serverBatPath}");
                LogService.LogGameVerbose($"Server working directory: {serverDir}");
                LogService.LogGameVerbose($"Run server as admin: {settings.RunServerAsAdmin}");

                Process? serverProcess;
                if (settings.RunServerAsAdmin)
                {
                    LogService.LogGame("Starting server as administrator...");
                    serverProcess = AdminHelper.StartAsAdmin(serverBatPath, null, serverDir);

                    if (serverProcess == null)
                    {
                        // User cancelled UAC or error occurred, try normal launch
                        LogService.LogGame("Admin launch cancelled or failed, trying normal launch");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = serverBatPath,
                            WorkingDirectory = serverDir,
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = serverBatPath,
                        WorkingDirectory = serverDir,
                        UseShellExecute = true
                    });
                }

                LogService.LogGame("Server process started");
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to start server: {ex.Message}");
                MessageBox.Show(string.Format(_localization.Get("error.launch"), ex.Message),
                    _localization.Get("error.title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateReinstallButtonVisibility()
        {
            var selectedVersion = VersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion != null && _gameLauncher.IsGameInstalled(selectedVersion))
            {
                ReinstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                ReinstallButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void ReinstallButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedVersion = VersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion == null)
                return;

            var result = MessageBox.Show(
                _localization.Get("main.reinstall_confirm"),
                _localization.Get("main.reinstall_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
                return;

            PlayButton.IsEnabled = false;
            ReinstallButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                await _gameLauncher.ReinstallGameAsync(selectedVersion, _localization);
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
                ReinstallButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                ResetProgress();
                UpdateReinstallButtonVisibility();
            }
        }

        private void SetupLogoFallback()
        {
            // Если картинка не загрузилась - показываем текст
            LogoImage.ImageFailed += (s, e) =>
            {
                LogoImage.Visibility = Visibility.Collapsed;
                LogoText.Visibility = Visibility.Visible;
            };

            // Проверяем загрузку через таймер (на случай если ImageFailed не сработал)
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (LogoImage.Source == null || !LogoImage.Source.CanFreeze)
                {
                    LogoImage.Visibility = Visibility.Collapsed;
                    LogoText.Visibility = Visibility.Visible;
                }
            };
            timer.Start();
        }
    }

    /// <summary>
    /// Helper class for modpack combo box items
    /// </summary>
    public class ModpackComboItem
    {
        public string? Id { get; set; }
        public string Name { get; set; } = "";

        public override string ToString() => Name;
    }
}
