using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class EditModpackDialog : Window
    {
        private readonly LocalizationService _localization;
        private readonly ModpackService _modpackService;
        private readonly Modpack _modpack;
        private readonly ObservableCollection<InstalledMod> _currentMods;
        private readonly ObservableCollection<InstalledMod> _availableMods;
        
        public string? NewModpackName { get; private set; }
        public new bool DialogResult { get; private set; }
        public bool ModsChanged { get; private set; }

        public EditModpackDialog(
            LocalizationService localization, 
            ModpackService modpackService,
            Modpack modpack,
            List<InstalledMod> defaultMods)
        {
            InitializeComponent();
            
            if (FontHelper.CurrentFont != null)
            {
                FontFamily = FontHelper.CurrentFont;
            }

            _localization = localization;
            _modpackService = modpackService;
            _modpack = modpack;
            
            // Get current mods in modpack
            var modpackMods = _modpackService.GetModpackInstalledMods(modpack.Id);
            _currentMods = new ObservableCollection<InstalledMod>(modpackMods);
            
            // Get available mods (from default that are not in modpack)
            var currentFileNames = _currentMods.Select(m => m.FileName).ToHashSet();
            _availableMods = new ObservableCollection<InstalledMod>(
                defaultMods.Where(m => !currentFileNames.Contains(m.FileName))
            );
            
            ModpackNameBox.Text = modpack.Name;
            CurrentModsList.ItemsSource = _currentMods;
            AvailableModsList.ItemsSource = _availableMods;
            
            UpdateUI();
        }

        private void UpdateUI()
        {
            TitleText.Text = _localization.Get("modpack.edit.title");
            NameLabel.Text = _localization.Get("modpack.create.name_label");
            CurrentModsLabel.Text = _localization.Get("modpack.edit.current_mods");
            AddModsLabel.Text = _localization.Get("modpack.edit.add_mods");
            CancelBtn.Content = _localization.Get("settings.cancel");
            SaveBtn.Content = _localization.Get("modpack.edit.save");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = ModpackNameBox.Text.Trim();
            
            // Validate name
            if (string.IsNullOrWhiteSpace(name))
            {
                NameErrorText.Text = _localization.Get("modpack.create.error_empty_name");
                NameErrorText.Visibility = Visibility.Visible;
                return;
            }
            
            if (!ModpackService.IsValidModpackName(name))
            {
                NameErrorText.Text = _localization.Get("modpack.create.error_invalid_name");
                NameErrorText.Visibility = Visibility.Visible;
                return;
            }
            
            // Rename if name changed
            if (name != _modpack.Name)
            {
                try
                {
                    _modpackService.RenameModpack(_modpack.Id, name);
                    NewModpackName = name;
                }
                catch (Exception ex)
                {
                    NameErrorText.Text = ex.Message;
                    NameErrorText.Visibility = Visibility.Visible;
                    return;
                }
            }
            
            DialogResult = true;
            Close();
        }

        private void RemoveModFromModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is InstalledMod mod)
            {
                try
                {
                    _modpackService.RemoveModFromModpack(_modpack.Id, mod.FileName);
                    _currentMods.Remove(mod);
                    _availableMods.Add(mod);
                    ModsChanged = true;
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

        private void AddModToModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is InstalledMod mod)
            {
                try
                {
                    _modpackService.AddModToModpack(_modpack.Id, mod.FilePath);
                    _availableMods.Remove(mod);
                    
                    // Refresh current mods from disk
                    var updatedMods = _modpackService.GetModpackInstalledMods(_modpack.Id);
                    var addedMod = updatedMods.FirstOrDefault(m => m.FileName == mod.FileName);
                    if (addedMod != null)
                    {
                        _currentMods.Add(addedMod);
                    }
                    
                    ModsChanged = true;
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
    }
}
