namespace HeyStupid
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using HeyStupid.Models;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Windows.Graphics;
    using Windows.Storage.Pickers;

    public sealed partial class FolderEditWindow : Window
    {
        private readonly ReminderSource _source;
        private readonly IReadOnlyList<ReminderSource> _otherSources;

        public bool Saved { get; private set; }
        public string NewName { get; private set; } = string.Empty;
        public string NewPath { get; private set; } = string.Empty;

        public FolderEditWindow(ReminderSource source, IEnumerable<ReminderSource> allSources)
        {
            _source = source;
            _otherSources = allSources.Where(s => s.Id != source.Id).ToList();

            InitializeComponent();
            ConfigureWindow();

            NameBox.Text = source.Name;
            PathBox.Text = source.FolderPath;
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(500, 360));
            Title = "Edit Folder - Hey Stupid";

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

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var picked = await picker.PickSingleFolderAsync();
            if (picked != null)
            {
                PathBox.Text = picked.Path;
                HideError();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? string.Empty;
            var path = PathBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Name is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                ShowError("Folder is required.");
                return;
            }

            if (PathsEqual(path, _source.FolderPath) == false)
            {
                var conflict = _otherSources.FirstOrDefault(s => PathsEqual(s.FolderPath, path));
                if (conflict != null)
                {
                    ShowError($"The folder \"{path}\" is already used by \"{conflict.Name}\". Pick a different folder.");
                    return;
                }
            }

            NewName = name;
            NewPath = path;
            Saved = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                var fullA = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
                var fullB = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
                return string.Equals(fullA, fullB, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}