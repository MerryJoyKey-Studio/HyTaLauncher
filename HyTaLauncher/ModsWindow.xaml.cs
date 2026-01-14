using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class ModsWindow : Window
    {
        private readonly ModService _modService;
        private readonly LocalizationService _localization;
        
        private int _currentPage = 0;
        private string _currentSearchQuery = "";
        private const int PAGE_SIZE = 20;

        public ModsWindow(LocalizationService localization, SettingsManager settingsManager)
        {
            FontHelper.Initialize();
            InitializeComponent();
            
            if (FontHelper.CinzelFont != null)
            {
                FontFamily = FontHelper.CinzelFont;
            }

            _localization = localization;
            
            var settings = settingsManager.Load();
            _modService = new ModService(settings.GameDirectory);
            
            _modService.ProgressChanged += OnProgressChanged;
            _modService.StatusChanged += OnStatusChanged;
            
            UpdateUI();
            
            Loaded += async (s, e) =>
            {
                await LoadInstalledModsAsync();
                await LoadPopularModsAsync();
            };
        }

        private void UpdateUI()
        {
            TitleText.Text = _localization.Get("mods.title");
            InstalledTitle.Text = _localization.Get("mods.installed");
            BrowseTitle.Text = _localization.Get("mods.browse");
            StatusText.Text = _localization.Get("mods.ready");
        }

        private async Task LoadInstalledModsAsync()
        {
            StatusText.Text = _localization.Get("mods.loading");
            
            var mods = await _modService.GetInstalledModsAsync();
            InstalledModsList.ItemsSource = mods;
            
            ModsCountText.Text = string.Format(_localization.Get("mods.count"), mods.Count);
            StatusText.Text = _localization.Get("mods.ready");
        }

        private async Task LoadPopularModsAsync()
        {
            var mods = await _modService.GetPopularModsAsync(_currentPage, PAGE_SIZE);
            SearchResultsList.ItemsSource = mods;
            UpdatePagination(mods.Count);
        }

        private async Task SearchModsAsync(string query)
        {
            _currentSearchQuery = query;
            
            if (string.IsNullOrWhiteSpace(query))
            {
                await LoadPopularModsAsync();
                return;
            }

            StatusText.Text = _localization.Get("mods.searching");
            var results = await _modService.SearchModsAsync(query, _currentPage, PAGE_SIZE);
            SearchResultsList.ItemsSource = results;
            StatusText.Text = string.Format(_localization.Get("mods.found"), results.Count);
            UpdatePagination(results.Count);
        }
        
        private void UpdatePagination(int resultsCount)
        {
            PageText.Text = $"Page {_currentPage + 1}";
            PrevPageBtn.IsEnabled = _currentPage > 0;
            NextPageBtn.IsEnabled = resultsCount >= PAGE_SIZE;
        }
        
        private async void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                await LoadCurrentPageAsync();
            }
        }
        
        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await LoadCurrentPageAsync();
        }
        
        private async Task LoadCurrentPageAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentSearchQuery))
            {
                await LoadPopularModsAsync();
            }
            else
            {
                await SearchModsAsync(_currentSearchQuery);
            }
        }

        private async void ModIcon_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Image image && image.Tag is string url)
            {
                var bitmap = await ImageCacheService.GetImageAsync(url);
                if (bitmap != null)
                {
                    image.Source = bitmap;
                }
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

        private async void RefreshInstalled_Click(object sender, RoutedEventArgs e)
        {
            await LoadInstalledModsAsync();
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = _localization.Get("mods.checking_updates");
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
            
            var mods = await _modService.GetInstalledModsAsync();
            mods = await _modService.CheckForUpdatesAsync(mods);
            
            InstalledModsList.ItemsSource = mods;
            
            var updatesCount = mods.Count(m => m.HasUpdate);
            StatusText.Text = updatesCount > 0 
                ? string.Format(_localization.Get("mods.updates_available"), updatesCount)
                : _localization.Get("mods.no_updates");
            
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Visibility = Visibility.Collapsed;
        }

        private async void UpdateMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is InstalledMod mod)
            {
                element.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                
                StatusText.Text = string.Format(_localization.Get("mods.updating"), mod.DisplayName);
                
                var success = await _modService.UpdateModAsync(mod);
                
                ProgressBar.Visibility = Visibility.Collapsed;
                element.IsEnabled = true;
                
                if (success)
                {
                    StatusText.Text = string.Format(_localization.Get("mods.updated"), mod.DisplayName);
                    await LoadInstalledModsAsync();
                }
                else
                {
                    StatusText.Text = _localization.Get("mods.update_failed");
                }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            _modService.OpenModsFolder();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 0;
            await SearchModsAsync(SearchBox.Text);
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _currentPage = 0;
                await SearchModsAsync(SearchBox.Text);
            }
        }

        private async void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is CurseForgeSearchResult mod)
            {
                element.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                
                var success = await _modService.InstallModAsync(mod);
                
                ProgressBar.Visibility = Visibility.Collapsed;
                element.IsEnabled = true;
                
                if (success)
                {
                    await LoadInstalledModsAsync();
                }
            }
        }

        private async void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is InstalledMod mod)
            {
                var result = MessageBox.Show(
                    string.Format(_localization.Get("mods.delete_confirm"), mod.DisplayName),
                    _localization.Get("mods.delete_title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    if (_modService.DeleteMod(mod))
                    {
                        StatusText.Text = string.Format(_localization.Get("mods.deleted"), mod.DisplayName);
                        await LoadInstalledModsAsync();
                    }
                }
            }
        }

        private void OnProgressChanged(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = progress;
            });
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }
    }
}
