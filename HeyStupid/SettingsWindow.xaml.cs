namespace HeyStupid
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Microsoft.UI.Xaml.Media;
    using Windows.Graphics;
    using Windows.Storage.Pickers;

    public sealed partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private bool _suppressCategoryFolderChange;

        public bool SourcesChanged { get; private set; }

        public SettingsWindow(SettingsService settingsService)
        {
            _settingsService = settingsService;
            InitializeComponent();
            ConfigureWindow();
            LoadCurrentSettings();
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(560, 650));
            Title = "Settings - Hey Stupid";

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
            }
        }

        private void LoadCurrentSettings()
        {
            var settings = _settingsService.Settings;
            StartupToggle.IsOn = settings.StartWithWindows;
            DefaultRetriesBox.Value = settings.DefaultMaxRetries;
            DefaultIntervalBox.Value = settings.DefaultRetryIntervalMinutes;
            RefreshSourcesList();
            RefreshCategoriesList();
        }

        private void RefreshSourcesList()
        {
            var sources = _settingsService.Settings.ReminderSources.ToList();
            SourcesList.ItemsSource = null;
            SourcesList.ItemsSource = sources;

            DefaultFolderBox.ItemsSource = sources;
            var defaultSource = _settingsService.Settings.GetDefaultSource();
            DefaultFolderBox.SelectedItem = sources.FirstOrDefault(s => s.Id == defaultSource.Id);
        }

        private void RefreshCategoriesList()
        {
            CategoriesList.ItemsSource = null;
            CategoriesList.ItemsSource = _settingsService.Settings.Categories.ToList();
        }

        private void CategoriesList_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }

            if (args.Item is ReminderCategory category)
            {
                var comboBox = FindChildByName<ComboBox>(args.ItemContainer, "FolderCombo");
                if (comboBox != null)
                {
                    _suppressCategoryFolderChange = true;
                    comboBox.Tag = category.Id;
                    comboBox.ItemsSource = _settingsService.Settings.ReminderSources;

                    if (category.FolderId.HasValue)
                    {
                        comboBox.SelectedItem = _settingsService.Settings.ReminderSources
                            .FirstOrDefault(s => s.Id == category.FolderId.Value);
                    }
                    else
                    {
                        comboBox.SelectedItem = null;
                    }
                    _suppressCategoryFolderChange = false;
                }
            }
        }

        private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && typed.Name.Equals(name, StringComparison.Ordinal))
                {
                    return typed;
                }
                var found = FindChildByName<T>(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private async void AddSource_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox { PlaceholderText = "e.g., IT Team, Finance, Personal" };
            var nameDialog = new ContentDialog
            {
                Title = "New Reminder Folder",
                Content = nameBox,
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var nameResult = await nameDialog.ShowAsync();
            if (nameResult != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            var source = new ReminderSource
            {
                Name = nameBox.Text.Trim(),
                FolderPath = folder.Path
            };

            await _settingsService.AddSourceAsync(source).ConfigureAwait(true);
            SourcesChanged = true;
            RefreshSourcesList();
            RefreshCategoriesList();
        }

        private async void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ReminderSource source)
            {
                if (_settingsService.Settings.ReminderSources.Count <= 1)
                {
                    var warn = new ContentDialog
                    {
                        Title = "Cannot Remove",
                        Content = "You must have at least one reminder folder.",
                        CloseButtonText = "OK",
                        XamlRoot = Content.XamlRoot
                    };
                    await warn.ShowAsync();
                    return;
                }

                await _settingsService.RemoveSourceAsync(source.Id).ConfigureAwait(true);
                SourcesChanged = true;
                RefreshSourcesList();
                RefreshCategoriesList();
            }
        }

        private async void DefaultFolder_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (DefaultFolderBox.SelectedItem is ReminderSource selected)
            {
                foreach (var s in _settingsService.Settings.ReminderSources)
                {
                    s.IsDefault = s.Id == selected.Id;
                }
                await _settingsService.SaveAsync().ConfigureAwait(true);
                SourcesChanged = true;
            }
        }

        private async void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox { PlaceholderText = "e.g., Sprint Tasks, Compliance, Meetings" };
            var nameDialog = new ContentDialog
            {
                Title = "New Category",
                Content = nameBox,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await nameDialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            var category = new ReminderCategory
            {
                Name = nameBox.Text.Trim(),
                FolderId = _settingsService.Settings.GetDefaultSource().Id
            };

            _settingsService.Settings.Categories.Add(category);
            await _settingsService.SaveAsync().ConfigureAwait(true);
            RefreshCategoriesList();
        }

        private async void RemoveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ReminderCategory category)
            {
                _settingsService.Settings.Categories.RemoveAll(c => c.Id == category.Id);
                await _settingsService.SaveAsync().ConfigureAwait(true);
                RefreshCategoriesList();
            }
        }

        private async void CategoryFolder_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCategoryFolderChange)
            {
                return;
            }

            if (sender is ComboBox combo && combo.Tag is Guid categoryId
                && combo.SelectedItem is ReminderSource source)
            {
                var category = _settingsService.Settings.Categories.FirstOrDefault(c => c.Id == categoryId);
                if (category != null)
                {
                    category.FolderId = source.Id;
                    await _settingsService.SaveAsync().ConfigureAwait(true);
                    SourcesChanged = true;
                }
            }
        }

        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            await _settingsService.SetStartWithWindowsAsync(StartupToggle.IsOn).ConfigureAwait(true);
        }

        private async void DefaultRetriesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(args.NewValue) == false)
            {
                _settingsService.Settings.DefaultMaxRetries = (int)args.NewValue;
                await _settingsService.SaveAsync().ConfigureAwait(true);
            }
        }

        private async void DefaultIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (double.IsNaN(args.NewValue) == false)
            {
                _settingsService.Settings.DefaultRetryIntervalMinutes = (int)args.NewValue;
                await _settingsService.SaveAsync().ConfigureAwait(true);
            }
        }
    }
}