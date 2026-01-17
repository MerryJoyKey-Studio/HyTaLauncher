using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class ModsWindow : Window
    {
        private readonly ModService _modService;
        private readonly TagService _tagService;
        private readonly ModpackService _modpackService;
        private readonly LocalizationService _localization;
        private readonly SettingsManager _settingsManager;
        
        private int _currentPage = 0;
        private string _currentSearchQuery = "";
        private const int PAGE_SIZE = 20;
        
        // Filter and sort state
        private SearchFilters _currentFilters = new();
        private SortOption _currentSort = SortOption.Popularity;
        private SortOrder _currentSortOrder = SortOrder.Descending;
        
        // Tag filter state
        private string? _selectedTagId = null;
        private List<InstalledMod> _allInstalledMods = new();
        
        // Modpack state
        private string? _selectedModpackId = null;
        private readonly List<ModpackItem> _modpackItems = new();
        private bool _isModpackOperationInProgress = false;
        
        // Filter data items
        private readonly List<FilterItem> _categoryItems = new();
        private readonly List<FilterItem> _gameVersionItems = new();
        private readonly List<SortItem> _sortItems = new();
        
        // Multi-select state
        private bool _isSelectModeActive = false;
        private readonly HashSet<InstalledMod> _selectedMods = new();

        public ModsWindow(LocalizationService localization, SettingsManager settingsManager)
        {
            InitializeComponent();
            
            if (FontHelper.CurrentFont != null)
            {
                FontFamily = FontHelper.CurrentFont;
            }

            _localization = localization;
            _settingsManager = settingsManager;
            
            var settings = settingsManager.Load();
            _modService = new ModService(settings.GameDirectory);
            _tagService = new TagService();
            _modpackService = new ModpackService(settings.GameDirectory);
            
            // Load saved sort preference
            _currentSort = (SortOption)settings.ModsSortOption;
            _currentSortOrder = (SortOrder)settings.ModsSortOrder;
            
            _modService.ProgressChanged += OnProgressChanged;
            _modService.StatusChanged += OnStatusChanged;
            _tagService.TagsChanged += OnTagsChanged;
            _modpackService.ModpacksChanged += OnModpacksChanged;
            
            InitializeFilterDropdowns();
            InitializeModpackDropdown();
            UpdateUI();
            RefreshTagFilterPanel();
            
            Loaded += async (s, e) =>
            {
                await LoadInstalledModsForCurrentModpackAsync();
                await LoadPopularModsAsync();
            };
        }

        private async void InitializeFilterDropdowns()
        {
            // Initialize Category filter items - load from API
            _categoryItems.Clear();
            _categoryItems.Add(new FilterItem { DisplayName = _localization.Get("mods.filter.all_categories"), Value = null });
            
            // Load categories from CurseForge API
            var categories = await _modService.GetCategoriesAsync();
            foreach (var cat in categories.OrderBy(c => c.Name))
            {
                _categoryItems.Add(new FilterItem { DisplayName = cat.Name, Value = cat.Id });
            }
            
            CategoryFilter.ItemsSource = _categoryItems;
            CategoryFilter.SelectedIndex = 0;
            
            // Initialize Game Version filter items - load from API
            _gameVersionItems.Clear();
            _gameVersionItems.Add(new FilterItem { DisplayName = _localization.Get("mods.filter.all_versions"), StringValue = null });
            
            var versions = await _modService.GetGameVersionsAsync();
            foreach (var ver in versions)
            {
                _gameVersionItems.Add(new FilterItem { DisplayName = ver, StringValue = ver });
            }
            
            GameVersionFilter.ItemsSource = _gameVersionItems;
            GameVersionFilter.SelectedIndex = 0;
            
            // Initialize Sort dropdown items
            _sortItems.Clear();
            _sortItems.Add(new SortItem { DisplayName = _localization.Get("mods.sort.popularity"), Sort = SortOption.Popularity, Order = SortOrder.Descending });
            _sortItems.Add(new SortItem { DisplayName = _localization.Get("mods.sort.downloads"), Sort = SortOption.TotalDownloads, Order = SortOrder.Descending });
            _sortItems.Add(new SortItem { DisplayName = _localization.Get("mods.sort.updated"), Sort = SortOption.LastUpdated, Order = SortOrder.Descending });
            _sortItems.Add(new SortItem { DisplayName = _localization.Get("mods.sort.name_az"), Sort = SortOption.Name, Order = SortOrder.Ascending });
            _sortItems.Add(new SortItem { DisplayName = _localization.Get("mods.sort.name_za"), Sort = SortOption.Name, Order = SortOrder.Descending });
            
            SortDropdown.ItemsSource = _sortItems;
            
            // Select saved sort preference
            var savedSortIndex = _sortItems.FindIndex(s => s.Sort == _currentSort && s.Order == _currentSortOrder);
            SortDropdown.SelectedIndex = savedSortIndex >= 0 ? savedSortIndex : 0;
        }

        private void UpdateUI()
        {
            TitleText.Text = _localization.Get("mods.title");
            InstalledTitle.Text = _localization.Get("mods.installed");
            BrowseTitle.Text = _localization.Get("mods.browse");
            StatusText.Text = _localization.Get("mods.ready");
            ModpackLabel.Text = _localization.Get("modpack.label");
            
            // Update filter dropdown labels if needed
            if (_categoryItems.Count > 0)
            {
                _categoryItems[0].DisplayName = _localization.Get("mods.filter.all_categories");
            }
            if (_gameVersionItems.Count > 0)
            {
                _gameVersionItems[0].DisplayName = _localization.Get("mods.filter.all_versions");
            }
            
            // Update modpack dropdown default item
            if (_modpackItems.Count > 0)
            {
                _modpackItems[0].DisplayName = _localization.Get("modpack.default");
            }
        }

        private async Task LoadInstalledModsAsync()
        {
            StatusText.Text = _localization.Get("mods.loading");
            
            _allInstalledMods = await _modService.GetInstalledModsAsync();
            
            // Apply tag filter if selected
            var displayMods = _selectedTagId != null 
                ? _tagService.FilterByTag(_allInstalledMods, _selectedTagId)
                : _allInstalledMods;
            
            InstalledModsList.ItemsSource = displayMods;
            
            ModsCountText.Text = string.Format(_localization.Get("mods.count"), _allInstalledMods.Count);
            StatusText.Text = _localization.Get("mods.ready");
        }

        private async Task LoadPopularModsAsync()
        {
            var mods = await _modService.GetPopularModsAsync(_currentFilters, _currentSort, _currentSortOrder, _currentPage, PAGE_SIZE);
            
            // Apply client-side release type filter if needed
            if (_currentFilters.ReleaseType.HasValue)
            {
                mods = _modService.FilterByReleaseType(mods, _currentFilters.ReleaseType.Value);
            }
            
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
            var results = await _modService.SearchModsAsync(query, _currentFilters, _currentSort, _currentSortOrder, _currentPage, PAGE_SIZE);
            
            // Apply client-side release type filter if needed
            if (_currentFilters.ReleaseType.HasValue)
            {
                results = _modService.FilterByReleaseType(results, _currentFilters.ReleaseType.Value);
            }
            
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
        
        // Filter event handlers
        private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryFilter.SelectedItem is FilterItem item)
            {
                _currentFilters.CategoryId = item.Value;
                _currentPage = 0;
                await LoadCurrentPageAsync();
            }
        }
        
        private async void GameVersionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameVersionFilter.SelectedItem is FilterItem item)
            {
                _currentFilters.GameVersion = item.StringValue;
                _currentPage = 0;
                await LoadCurrentPageAsync();
            }
        }
        
        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            // Reset all filters
            _currentFilters = new SearchFilters();
            CategoryFilter.SelectedIndex = 0;
            GameVersionFilter.SelectedIndex = 0;
            _currentPage = 0;
            await LoadCurrentPageAsync();
        }
        
        // Sort event handler
        private async void SortDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortDropdown.SelectedItem is SortItem item)
            {
                _currentSort = item.Sort;
                _currentSortOrder = item.Order;
                _currentPage = 0;
                
                // Persist sort preference
                var settings = _settingsManager.Load();
                settings.ModsSortOption = (int)_currentSort;
                settings.ModsSortOrder = (int)_currentSortOrder;
                _settingsManager.Save(settings);
                
                await LoadCurrentPageAsync();
            }
        }

        private async void ModIcon_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Image image)
            {
                var url = image.Tag as string;
                LogService.LogModsVerbose($"ModIcon_Loaded: Tag={url ?? "null"}");
                
                if (!string.IsNullOrEmpty(url))
                {
                    var bitmap = await ImageCacheService.GetImageAsync(url);
                    if (bitmap != null)
                    {
                        image.Source = bitmap;
                        LogService.LogModsVerbose($"ModIcon_Loaded: Image set for {url}");
                    }
                    else
                    {
                        LogService.LogModsVerbose($"ModIcon_Loaded: Failed to load image for {url}");
                    }
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
                
                // Determine target folder based on selected modpack
                string? targetFolder = null;
                if (_selectedModpackId != null)
                {
                    targetFolder = _modpackService.GetModpackModsPath(_selectedModpackId);
                }
                
                var success = await _modService.InstallModAsync(mod, targetFolder);
                
                ProgressBar.Visibility = Visibility.Collapsed;
                element.IsEnabled = true;
                
                if (success)
                {
                    await LoadInstalledModsForCurrentModpackAsync();
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
                        await LoadInstalledModsForCurrentModpackAsync();
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
        
        // Tag management methods
        
        private void OnTagsChanged()
        {
            Dispatcher.Invoke(() =>
            {
                RefreshTagFilterPanel();
                ApplyTagFilter();
            });
        }
        
        private void RefreshTagFilterPanel()
        {
            var tags = _tagService.GetAllTags();
            TagFilterList.ItemsSource = tags;
            
            // Show/hide tag filter panel based on whether there are tags
            TagFilterPanel.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            
            // Update "All" chip text
            AllTagsText.Text = _localization.Get("mods.tags.all");
            
            // Update "All" chip selection state
            UpdateTagChipSelection();
        }
        
        private void UpdateTagChipSelection()
        {
            // Update "All" chip appearance
            AllTagsChip.Background = _selectedTagId == null 
                ? (Brush)FindResource("AccentBrush") 
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3d444d"));
        }
        
        private void ApplyTagFilter()
        {
            if (_allInstalledMods == null || _allInstalledMods.Count == 0)
                return;
                
            var displayMods = _selectedTagId != null 
                ? _tagService.FilterByTag(_allInstalledMods, _selectedTagId)
                : _allInstalledMods;
            
            InstalledModsList.ItemsSource = displayMods;
            
            // Update checkbox visibility for filtered items
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in InstalledModsList.Items)
                {
                    var container = InstalledModsList.ItemContainerGenerator.ContainerFromItem(item);
                    UpdateModItemCheckboxVisibility(container as FrameworkElement);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        
        private void AllTagsChip_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedTagId = null;
            UpdateTagChipSelection();
            ApplyTagFilter();
        }
        
        private void TagChip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ModTag tag)
            {
                _selectedTagId = tag.Id;
                UpdateTagChipSelection();
                ApplyTagFilter();
            }
        }
        
        private void InstalledMod_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Context menu will be shown automatically
        }
        
        private void ModContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu && contextMenu.PlacementTarget is FrameworkElement element)
            {
                var mod = element.Tag as InstalledMod;
                if (mod == null) return;
                
                // Find menu items
                var addTagMenuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == _localization.Get("mods.tags.add") || m.Header?.ToString() == "Add Tag");
                var removeTagMenuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == _localization.Get("mods.tags.remove") || m.Header?.ToString() == "Remove Tag");
                var createTagMenuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString()?.Contains("Create") == true || m.Header?.ToString()?.Contains("New") == true);
                
                // Update headers with localized text
                if (addTagMenuItem != null)
                {
                    addTagMenuItem.Header = _localization.Get("mods.tags.add");
                    addTagMenuItem.Items.Clear();
                    
                    var allTags = _tagService.GetAllTags();
                    var modTags = _tagService.GetTagsForMod(mod.FileName);
                    
                    // Add tags that are not already assigned
                    foreach (var tag in allTags.Where(t => !modTags.Contains(t.Id)))
                    {
                        var tagItem = new MenuItem
                        {
                            Header = tag.Name,
                            Tag = new TagActionData { Mod = mod, TagId = tag.Id },
                            Style = (Style)FindResource("ModernMenuItem"),
                            Icon = new Border
                            {
                                Width = 12,
                                Height = 12,
                                CornerRadius = new CornerRadius(2),
                                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color))
                            }
                        };
                        tagItem.Click += AddTagToMod_Click;
                        addTagMenuItem.Items.Add(tagItem);
                    }
                    
                    // Disable if no tags available to add
                    addTagMenuItem.IsEnabled = addTagMenuItem.Items.Count > 0;
                }
                
                if (removeTagMenuItem != null)
                {
                    removeTagMenuItem.Header = _localization.Get("mods.tags.remove");
                    removeTagMenuItem.Items.Clear();
                    
                    var modTagIds = _tagService.GetTagsForMod(mod.FileName);
                    var modTags = _tagService.GetTagObjectsForMod(mod.FileName);
                    
                    // Add tags that are assigned to this mod
                    foreach (var tag in modTags)
                    {
                        var tagItem = new MenuItem
                        {
                            Header = tag.Name,
                            Tag = new TagActionData { Mod = mod, TagId = tag.Id },
                            Style = (Style)FindResource("ModernMenuItem"),
                            Icon = new Border
                            {
                                Width = 12,
                                Height = 12,
                                CornerRadius = new CornerRadius(2),
                                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color))
                            }
                        };
                        tagItem.Click += RemoveTagFromMod_Click;
                        removeTagMenuItem.Items.Add(tagItem);
                    }
                    
                    // Disable if no tags to remove
                    removeTagMenuItem.IsEnabled = removeTagMenuItem.Items.Count > 0;
                }
                
                if (createTagMenuItem != null)
                {
                    createTagMenuItem.Header = _localization.Get("mods.tags.create");
                    createTagMenuItem.Tag = mod;
                }
            }
        }
        
        private void AddTagToMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is TagActionData data)
            {
                _tagService.AssignTag(data.Mod.FileName, data.TagId);
                StatusText.Text = string.Format(_localization.Get("mods.tags.added_to_mod"), data.Mod.DisplayName);
            }
        }
        
        private void RemoveTagFromMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is TagActionData data)
            {
                _tagService.RemoveTag(data.Mod.FileName, data.TagId);
                StatusText.Text = string.Format(_localization.Get("mods.tags.removed_from_mod"), data.Mod.DisplayName);
            }
        }
        
        private void CreateNewTag_Click(object sender, RoutedEventArgs e)
        {
            var mod = (sender as FrameworkElement)?.Tag as InstalledMod;
            
            // Show a simple input dialog for tag name
            var dialog = new Window
            {
                Title = _localization.Get("mods.tags.create_title"),
                Width = 300,
                Height = 180,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            
            var border = new Border
            {
                Background = (Brush)FindResource("CardBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20)
            };
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var titleText = new TextBlock
            {
                Text = _localization.Get("mods.tags.create_title"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(titleText, 0);
            
            var nameBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                Height = 32,
                FontSize = 12
            };
            Grid.SetRow(nameBox, 1);
            
            // Color picker (simple predefined colors)
            var colorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 15)
            };
            Grid.SetRow(colorPanel, 2);
            
            var colors = new[] { "#4CAF50", "#2196F3", "#FF9800", "#E91E63", "#9C27B0", "#00BCD4", "#FF5722", "#607D8B" };
            string selectedColor = colors[0];
            Border? selectedColorBorder = null;
            
            foreach (var color in colors)
            {
                var colorBorder = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Tag = color
                };
                
                if (color == selectedColor)
                {
                    colorBorder.BorderBrush = Brushes.White;
                    selectedColorBorder = colorBorder;
                }
                
                colorBorder.MouseLeftButtonDown += (s, args) =>
                {
                    if (selectedColorBorder != null)
                        selectedColorBorder.BorderBrush = Brushes.Transparent;
                    
                    selectedColor = (string)((Border)s).Tag;
                    ((Border)s).BorderBrush = Brushes.White;
                    selectedColorBorder = (Border)s;
                };
                
                colorPanel.Children.Add(colorBorder);
            }
            
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 3);
            
            var cancelBtn = new Button
            {
                Content = _localization.Get("settings.cancel"),
                Style = (Style)FindResource("ModernButton"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3d444d")),
                Padding = new Thickness(15, 8, 15, 8),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, args) => dialog.Close();
            
            var createBtn = new Button
            {
                Content = _localization.Get("mods.tags.create_title"),
                Style = (Style)FindResource("ModernButton"),
                Padding = new Thickness(15, 8, 15, 8)
            };
            createBtn.Click += (s, args) =>
            {
                var tagName = nameBox.Text.Trim();
                if (!string.IsNullOrEmpty(tagName))
                {
                    var newTag = _tagService.CreateTag(tagName, selectedColor);
                    
                    // If a mod was selected, assign the new tag to it
                    if (mod != null)
                    {
                        _tagService.AssignTag(mod.FileName, newTag.Id);
                    }
                    
                    dialog.Close();
                    StatusText.Text = string.Format(_localization.Get("mods.tags.created"), tagName);
                }
            };
            
            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(createBtn);
            
            grid.Children.Add(titleText);
            grid.Children.Add(nameBox);
            grid.Children.Add(colorPanel);
            grid.Children.Add(buttonPanel);
            
            border.Child = grid;
            dialog.Content = border;
            
            dialog.ShowDialog();
        }
        
        // Modpack management methods
        
        private void InitializeModpackDropdown()
        {
            RefreshModpackDropdown();
            
            // Select saved modpack
            var selectedId = _modpackService.GetSelectedModpackId();
            _selectedModpackId = selectedId;
            
            var selectedIndex = _modpackItems.FindIndex(m => m.ModpackId == selectedId);
            ModpackSelector.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            
            UpdateModpackButtonStates();
        }
        
        private void RefreshModpackDropdown()
        {
            _modpackItems.Clear();
            _modpackItems.Add(new ModpackItem { DisplayName = _localization.Get("modpack.default"), ModpackId = null });
            
            foreach (var modpack in _modpackService.GetAllModpacks())
            {
                _modpackItems.Add(new ModpackItem { DisplayName = modpack.Name, ModpackId = modpack.Id });
            }
            
            ModpackSelector.DisplayMemberPath = "DisplayName";
            ModpackSelector.ItemsSource = null;
            ModpackSelector.ItemsSource = _modpackItems;
        }
        
        private void UpdateModpackButtonStates()
        {
            var hasModpackSelected = _selectedModpackId != null;
            EditModpackBtn.IsEnabled = hasModpackSelected;
            DeleteModpackBtn.IsEnabled = hasModpackSelected;
            ExportModpackBtn.IsEnabled = hasModpackSelected;
        }
        
        private void OnModpacksChanged()
        {
            if (_isModpackOperationInProgress) return;
            
            Dispatcher.Invoke(() =>
            {
                RefreshModpackDropdown();
                
                // Re-select current modpack if it still exists
                var selectedIndex = _modpackItems.FindIndex(m => m.ModpackId == _selectedModpackId);
                if (selectedIndex < 0)
                {
                    _selectedModpackId = null;
                    selectedIndex = 0;
                }
                ModpackSelector.SelectedIndex = selectedIndex;
                
                UpdateModpackButtonStates();
            });
        }
        
        private async void ModpackSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModpackSelector.SelectedItem is ModpackItem item)
            {
                _selectedModpackId = item.ModpackId;
                _modpackService.SetSelectedModpack(_selectedModpackId);
                UpdateModpackButtonStates();
                
                // Reload installed mods for the selected modpack
                await LoadInstalledModsForCurrentModpackAsync();
            }
        }
        
        private async Task LoadInstalledModsForCurrentModpackAsync()
        {
            StatusText.Text = _localization.Get("mods.loading");
            
            if (_selectedModpackId != null)
            {
                // Load mods from modpack directory
                _allInstalledMods = _modpackService.GetModpackInstalledMods(_selectedModpackId);
            }
            else
            {
                // Load mods from default directory
                _allInstalledMods = await _modService.GetInstalledModsAsync();
            }
            
            // Apply tag filter if selected
            var displayMods = _selectedTagId != null 
                ? _tagService.FilterByTag(_allInstalledMods, _selectedTagId)
                : _allInstalledMods;
            
            InstalledModsList.ItemsSource = displayMods;
            
            ModsCountText.Text = string.Format(_localization.Get("mods.count"), _allInstalledMods.Count);
            StatusText.Text = _localization.Get("mods.ready");
            
            // Update checkbox visibility for newly loaded items
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in InstalledModsList.Items)
                {
                    var container = InstalledModsList.ItemContainerGenerator.ContainerFromItem(item);
                    UpdateModItemCheckboxVisibility(container as FrameworkElement);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
            
            // Load icons in background
            _ = LoadModIconsAsync(_allInstalledMods);
        }
        
        private async Task LoadModIconsAsync(List<InstalledMod> mods)
        {
            var modsWithoutIcons = mods.Where(m => string.IsNullOrEmpty(m.IconUrl) && m.Manifest != null && !string.IsNullOrEmpty(m.Manifest.Name)).ToList();
            
            if (modsWithoutIcons.Count == 0)
            {
                LogService.LogModsVerbose("All mods have icons, skipping API search");
                return;
            }
            
            LogService.LogModsVerbose($"Loading icons for {modsWithoutIcons.Count} mods without cached icons");
            
            foreach (var mod in modsWithoutIcons)
            {
                try
                {
                    LogService.LogModsVerbose($"Searching icon for: {mod.Manifest!.Name}");
                    var searchResults = await _modService.SearchModsAsync(mod.Manifest.Name, null, SortOption.Popularity, SortOrder.Descending, 0, 3);
                    var match = searchResults.FirstOrDefault(r => 
                        r.Name.Equals(mod.Manifest.Name, StringComparison.OrdinalIgnoreCase) ||
                        r.Slug.Equals(mod.Manifest.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (match != null && !string.IsNullOrEmpty(match.ThumbnailUrl))
                    {
                        mod.IconUrl = match.ThumbnailUrl;
                        LogService.LogModsVerbose($"Found icon for {mod.Manifest.Name}: {mod.IconUrl}");
                        
                        // Save icon URL to .icons folder for future use
                        var modsFolder = Path.GetDirectoryName(mod.FilePath);
                        if (!string.IsNullOrEmpty(modsFolder))
                        {
                            var iconsFolder = Path.Combine(modsFolder, ".icons");
                            Directory.CreateDirectory(iconsFolder);
                            var iconFilePath = Path.Combine(iconsFolder, mod.FileName + ".icon");
                            try
                            {
                                await File.WriteAllTextAsync(iconFilePath, mod.IconUrl);
                            }
                            catch { }
                        }
                        
                        // Pre-load the image into cache
                        await ImageCacheService.GetImageAsync(mod.IconUrl);
                    }
                    else
                    {
                        LogService.LogModsVerbose($"No match found for {mod.Manifest.Name}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogModsVerbose($"Error loading icon for {mod.Manifest!.Name}: {ex.Message}");
                }
            }
            
            // Refresh the list after icons loaded
            if (modsWithoutIcons.Any(m => !string.IsNullOrEmpty(m.IconUrl)))
            {
                LogService.LogModsVerbose("Refreshing installed mods list with new icons");
                Dispatcher.Invoke(() =>
                {
                    var displayMods = _selectedTagId != null 
                        ? _tagService.FilterByTag(_allInstalledMods, _selectedTagId)
                        : _allInstalledMods;
                    InstalledModsList.ItemsSource = null;
                    InstalledModsList.ItemsSource = displayMods;
                });
            }
        }
        
        private async void CreateModpack_Click(object sender, RoutedEventArgs e)
        {
            // Get mods from default directory for selection
            var defaultMods = await _modService.GetInstalledModsAsync();
            
            var dialog = new CreateModpackDialog(_localization, defaultMods)
            {
                Owner = this
            };
            
            dialog.ShowDialog();
            
            if (dialog.DialogResult)
            {
                try
                {
                    _isModpackOperationInProgress = true;
                    
                    var modpack = _modpackService.CreateModpack(dialog.ModpackName!, dialog.SelectedModPaths);
                    StatusText.Text = string.Format(_localization.Get("modpack.created"), modpack.Name);
                    
                    // Set selected modpack ID before refreshing dropdown
                    _selectedModpackId = modpack.Id;
                    
                    // Select the newly created modpack
                    RefreshModpackDropdown();
                    var newIndex = _modpackItems.FindIndex(m => m.ModpackId == modpack.Id);
                    if (newIndex >= 0)
                    {
                        ModpackSelector.SelectedIndex = newIndex;
                    }
                    
                    _isModpackOperationInProgress = false;
                    
                    UpdateModpackButtonStates();
                    await LoadInstalledModsForCurrentModpackAsync();
                }
                catch (Exception ex)
                {
                    _isModpackOperationInProgress = false;
                    MessageBox.Show(
                        ex.Message,
                        _localization.Get("error.title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
        
        private async void EditModpack_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpackId == null) return;
            
            var modpack = _modpackService.GetModpack(_selectedModpackId);
            if (modpack == null) return;
            
            // Get mods from default directory for adding
            var defaultMods = await _modService.GetInstalledModsAsync();
            
            var dialog = new EditModpackDialog(_localization, _modpackService, modpack, defaultMods)
            {
                Owner = this
            };
            
            dialog.ShowDialog();
            
            if (dialog.DialogResult)
            {
                if (dialog.NewModpackName != null)
                {
                    StatusText.Text = string.Format(_localization.Get("modpack.renamed"), dialog.NewModpackName);
                    RefreshModpackDropdown();
                    
                    // Re-select the modpack
                    var index = _modpackItems.FindIndex(m => m.ModpackId == _selectedModpackId);
                    if (index >= 0)
                    {
                        ModpackSelector.SelectedIndex = index;
                    }
                }
                
                if (dialog.ModsChanged)
                {
                    await LoadInstalledModsForCurrentModpackAsync();
                }
            }
        }
        
        private async void DeleteModpack_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpackId == null) return;
            
            var modpack = _modpackService.GetModpack(_selectedModpackId);
            if (modpack == null) return;
            
            var result = MessageBox.Show(
                string.Format(_localization.Get("modpack.delete_confirm"), modpack.Name),
                _localization.Get("modpack.delete_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _modpackService.DeleteModpack(_selectedModpackId);
                    StatusText.Text = string.Format(_localization.Get("modpack.deleted"), modpack.Name);
                    
                    // Reset to default
                    _selectedModpackId = null;
                    RefreshModpackDropdown();
                    ModpackSelector.SelectedIndex = 0;
                    
                    await LoadInstalledModsForCurrentModpackAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        _localization.Get("error.title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
        
        private async void ExportModpack_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpackId == null) return;
            
            var modpack = _modpackService.GetModpack(_selectedModpackId);
            if (modpack == null) return;
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = _localization.Get("modpack.export_title"),
                Filter = "ZIP files (*.zip)|*.zip",
                FileName = $"{modpack.Name}.zip"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressBar.IsIndeterminate = true;
                    StatusText.Text = _localization.Get("modpack.exporting");
                    
                    await _modpackService.ExportModpackAsync(_selectedModpackId, dialog.FileName);
                    
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    StatusText.Text = string.Format(_localization.Get("modpack.exported"), modpack.Name);
                }
                catch (Exception ex)
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    
                    MessageBox.Show(
                        ex.Message,
                        _localization.Get("error.title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
        
        private async void ImportModpack_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = _localization.Get("modpack.import_title"),
                Filter = "ZIP files (*.zip)|*.zip"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressBar.IsIndeterminate = true;
                    StatusText.Text = _localization.Get("modpack.importing");
                    
                    var modpack = await _modpackService.ImportModpackAsync(dialog.FileName);
                    
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    
                    if (modpack != null)
                    {
                        StatusText.Text = string.Format(_localization.Get("modpack.imported"), modpack.Name);
                        
                        // Select the imported modpack
                        RefreshModpackDropdown();
                        var newIndex = _modpackItems.FindIndex(m => m.ModpackId == modpack.Id);
                        if (newIndex >= 0)
                        {
                            ModpackSelector.SelectedIndex = newIndex;
                        }
                    }
                    else
                    {
                        StatusText.Text = _localization.Get("modpack.import_failed");
                        MessageBox.Show(
                            _localization.Get("modpack.import_failed"),
                            _localization.Get("error.title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                catch (Exception ex)
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    
                    MessageBox.Show(
                        ex.Message,
                        _localization.Get("error.title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
        
        // Multi-select methods
        
        private void SelectMode_Click(object sender, RoutedEventArgs e)
        {
            _isSelectModeActive = !_isSelectModeActive;
            _selectedMods.Clear();
            
            UpdateSelectModeUI();
        }
        
        private void UpdateSelectModeUI()
        {
            // Update button appearance
            SelectModeBtn.Background = _isSelectModeActive 
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3d444d"));
            
            // Show/hide checkboxes and related UI
            SelectAllCheckBox.Visibility = _isSelectModeActive ? Visibility.Visible : Visibility.Collapsed;
            DeleteSelectedBtn.Visibility = _isSelectModeActive && _selectedMods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            
            // Update checkboxes in the list
            if (InstalledModsList.ItemsSource != null)
            {
                foreach (var item in InstalledModsList.Items)
                {
                    var container = InstalledModsList.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null)
                    {
                        UpdateModItemCheckboxVisibility(container as FrameworkElement);
                    }
                }
            }
            
            // Reset select all checkbox
            SelectAllCheckBox.IsChecked = false;
        }
        
        private void UpdateModItemCheckboxVisibility(FrameworkElement? container)
        {
            if (container == null) return;
            
            var checkbox = FindVisualChild<CheckBox>(container);
            if (checkbox != null)
            {
                checkbox.Visibility = _isSelectModeActive ? Visibility.Visible : Visibility.Collapsed;
                
                // Update checked state based on selection
                if (checkbox.Tag is InstalledMod mod)
                {
                    checkbox.IsChecked = _selectedMods.Contains(mod);
                }
            }
        }
        
        private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var isChecked = SelectAllCheckBox.IsChecked == true;
            
            _selectedMods.Clear();
            
            if (isChecked && InstalledModsList.ItemsSource != null)
            {
                foreach (var item in InstalledModsList.ItemsSource)
                {
                    if (item is InstalledMod mod)
                    {
                        _selectedMods.Add(mod);
                    }
                }
            }
            
            // Update all checkboxes
            if (InstalledModsList.ItemsSource != null)
            {
                foreach (var item in InstalledModsList.Items)
                {
                    var container = InstalledModsList.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null)
                    {
                        var checkbox = FindVisualChild<CheckBox>(container as FrameworkElement);
                        if (checkbox != null)
                        {
                            checkbox.IsChecked = isChecked;
                        }
                    }
                }
            }
            
            UpdateDeleteSelectedButtonVisibility();
        }
        
        private void ModCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.Tag is InstalledMod mod)
            {
                if (checkbox.IsChecked == true)
                {
                    _selectedMods.Add(mod);
                }
                else
                {
                    _selectedMods.Remove(mod);
                }
                
                // Update select all checkbox state
                var totalMods = InstalledModsList.Items.Count;
                SelectAllCheckBox.IsChecked = _selectedMods.Count == totalMods && totalMods > 0;
                
                UpdateDeleteSelectedButtonVisibility();
            }
        }
        
        private void UpdateDeleteSelectedButtonVisibility()
        {
            DeleteSelectedBtn.Visibility = _isSelectModeActive && _selectedMods.Count > 0 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        
        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMods.Count == 0) return;
            
            var result = MessageBox.Show(
                string.Format(_localization.Get("mods.delete_selected_confirm"), _selectedMods.Count),
                _localization.Get("mods.delete_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            
            if (result == MessageBoxResult.Yes)
            {
                var modsToDelete = _selectedMods.ToList();
                var deletedCount = 0;
                
                foreach (var mod in modsToDelete)
                {
                    if (_modService.DeleteMod(mod))
                    {
                        deletedCount++;
                    }
                }
                
                StatusText.Text = string.Format(_localization.Get("mods.deleted_count"), deletedCount);
                
                _selectedMods.Clear();
                _isSelectModeActive = false;
                UpdateSelectModeUI();
                
                await LoadInstalledModsForCurrentModpackAsync();
            }
        }
        
        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                {
                    return typedChild;
                }
                
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Helper class for tag action data
    /// </summary>
    public class TagActionData
    {
        public InstalledMod Mod { get; set; } = null!;
        public string TagId { get; set; } = "";
    }
    
    /// <summary>
    /// Helper class for filter dropdown items
    /// </summary>
    public class FilterItem
    {
        public string DisplayName { get; set; } = "";
        public int? Value { get; set; }
        public string? StringValue { get; set; }
        
        public override string ToString() => DisplayName;
    }
    
    /// <summary>
    /// Helper class for sort dropdown items
    /// </summary>
    public class SortItem
    {
        public string DisplayName { get; set; } = "";
        public SortOption Sort { get; set; }
        public SortOrder Order { get; set; }
        
        public override string ToString() => DisplayName;
    }
    
    /// <summary>
    /// Helper class for modpack dropdown items
    /// </summary>
    public class ModpackItem
    {
        public string DisplayName { get; set; } = "";
        public string? ModpackId { get; set; }
        
        public override string ToString() => DisplayName;
    }
}
