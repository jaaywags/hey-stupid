namespace HeyStupid
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Microsoft.UI.Xaml.Media;
    using Windows.Graphics;

    public sealed partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly JsonReminderStore _store;
        private readonly ReminderScheduler _scheduler;
        private bool _isRefreshing;

        public MainWindow(SettingsService settingsService, JsonReminderStore store, ReminderScheduler scheduler)
        {
            _settingsService = settingsService;
            _store = store;
            _scheduler = scheduler;

            InitializeComponent();

            ConfigureWindow();
            RefreshList();

            _scheduler.RemindersChanged += () =>
            {
                DispatcherQueue.TryEnqueue(RefreshList);
            };
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(720, 600));
            Title = "Hey Stupid - Reminders";

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMinimizable = true;
                presenter.IsMaximizable = false;
            }
        }

        public void RefreshList()
        {
            _isRefreshing = true;
            try
            {
                var reminders = _store.GetAll()
                    .OrderByDescending(r => r.IsActive)
                    .ThenBy(r => r.NextDue ?? DateTime.MaxValue)
                    .ToList();

                ReminderListView.ItemsSource = null;
                ReminderListView.ItemsSource = reminders;

            var hasReminders = reminders.Count > 0;
            EmptyState.Visibility = hasReminders ? Visibility.Collapsed : Visibility.Visible;
            ReminderListView.Visibility = hasReminders ? Visibility.Visible : Visibility.Collapsed;

            var activeCount = reminders.Count(r => r.IsActive);
            var waitingCount = reminders.Count(r => r.IsWaitingForAcknowledgment);

            if (activeCount == 0)
            {
                SubtitleText.Text = "No active reminders";
            }
            else if (waitingCount > 0)
            {
                SubtitleText.Text = $"{activeCount} active, {waitingCount} waiting for acknowledgment";
            }
            else
            {
                SubtitleText.Text = $"{activeCount} active reminder{(activeCount == 1 ? "" : "s")}";
            }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ReminderEditDialog(_settingsService.Settings);
            dialog.XamlRoot = Content.XamlRoot;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.Reminder != null)
            {
                await _store.SaveAsync(dialog.Reminder).ConfigureAwait(true);
                RefreshList();
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Reminder reminder)
            {
                var dialog = new ReminderEditDialog(_settingsService.Settings, reminder);
                dialog.XamlRoot = Content.XamlRoot;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && dialog.Reminder != null)
                {
                    var edited = dialog.Reminder;
                    edited.IsWaitingForAcknowledgment = false;
                    edited.CurrentRetryCount = 0;
                    edited.NextRetry = null;
                    await _store.SaveAsync(edited).ConfigureAwait(true);
                    RefreshList();
                }
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Reminder reminder)
            {
                var confirm = new ContentDialog
                {
                    Title = "Delete Reminder",
                    Content = $"Delete \"{reminder.Title}\"?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };

                var result = await confirm.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await _store.DeleteAsync(reminder.Id).ConfigureAwait(true);
                    RefreshList();
                }
            }
        }

        private async void AckButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Reminder reminder)
            {
                await _scheduler.AcknowledgeAsync(reminder.Id).ConfigureAwait(true);
                RefreshList();
            }
        }

        private async void ReminderToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            if (sender is ToggleSwitch toggle && toggle.DataContext is Reminder reminder)
            {
                reminder.IsActive = toggle.IsOn;
                if (toggle.IsOn && reminder.NextDue.HasValue == false)
                {
                    reminder.NextDue = RecurrenceCalculator.CalculateInitialDue(reminder);
                }
                if (toggle.IsOn == false)
                {
                    reminder.IsWaitingForAcknowledgment = false;
                    reminder.CurrentRetryCount = 0;
                    reminder.NextRetry = null;
                }
                await _store.SaveAsync(reminder).ConfigureAwait(true);
                RefreshList();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settingsService);
            settingsWindow.Closed += async (s, args) =>
            {
                if (settingsWindow.SourcesChanged)
                {
                    _store.SetSources(_settingsService.Settings.ReminderSources);
                    await _store.LoadAsync().ConfigureAwait(true);
                    RefreshList();
                }
            };
            settingsWindow.Activate();
        }
    }
}