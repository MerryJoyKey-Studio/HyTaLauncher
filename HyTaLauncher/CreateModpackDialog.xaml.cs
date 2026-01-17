using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HyTaLauncher.Helpers;
using HyTaLauncher.Services;

namespace HyTaLauncher
{
    public partial class CreateModpackDialog : Window
    {
        private readonly LocalizationService _localization;
        private readonly ObservableCollection<SelectableMod> _selectableMods;
        
        public string? ModpackName { get; private set; }
        public List<string> SelectedModPaths { get; private set; } = new();
        public new bool DialogResult { get; private set; }

        public CreateModpackDialog(LocalizationService localization, List<InstalledMod> availableMods)
        {
            InitializeComponent();
            
            if (FontHelper.CurrentFont != null)
            {
                FontFamily = FontHelper.CurrentFont;
            }

            _localization = localization;
            
            // Create selectable mod items
            _selectableMods = new ObservableCollection<SelectableMod>(
                availableMods.Select(m => new SelectableMod(m))
            );
            
            foreach (var mod in _selectableMods)
            {
                mod.PropertyChanged += OnModSelectionChanged;
            }
            
            ModsListBox.ItemsSource = _selectableMods;
            
            UpdateUI();
            UpdateSelectionSummary();
        }

        private void UpdateUI()
        {
            TitleText.Text = _localization.Get("modpack.create.title");
            NameLabel.Text = _localization.Get("modpack.create.name_label");
            SelectModsLabel.Text = _localization.Get("modpack.create.select_mods");
            CancelBtn.Content = _localization.Get("settings.cancel");
            CreateBtn.Content = _localization.Get("modpack.create.button");
        }

        private void OnModSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableMod.IsSelected))
            {
                UpdateSelectionSummary();
            }
        }

        private void UpdateSelectionSummary()
        {
            var count = _selectableMods.Count(m => m.IsSelected);
            SelectionSummary.Text = string.Format(_localization.Get("modpack.create.selected_count"), count);
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

        private void CreateButton_Click(object sender, RoutedEventArgs e)
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
            
            ModpackName = name;
            SelectedModPaths = _selectableMods
                .Where(m => m.IsSelected)
                .Select(m => m.Mod.FilePath)
                .ToList();
            
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Wrapper class for mods with selection state
    /// </summary>
    public class SelectableMod : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public InstalledMod Mod { get; }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
        
        public SelectableMod(InstalledMod mod)
        {
            Mod = mod;
            _isSelected = false;
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
