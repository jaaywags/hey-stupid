namespace HeyStupid
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using H.NotifyIcon;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI.Dispatching;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;

    public partial class App : Application
    {
        private SettingsService _settingsService = null!;
        private JsonReminderStore _store = null!;
        private ReminderScheduler _scheduler = null!;
        private TaskbarIcon? _trayIcon;
        private readonly Dictionary<Guid, ReminderPopupWindow> _openPopups = new();

        public static MainWindow MainWindow { get; private set; } = null!;

        public App()
        {
            InitializeComponent();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            _settingsService = new SettingsService();
            await _settingsService.LoadAsync().ConfigureAwait(true);
            _settingsService.ApplyStartupSetting();

            _store = new JsonReminderStore();
            _store.SetSources(_settingsService.Settings.ReminderSources);
            await _store.LoadAsync().ConfigureAwait(true);

            _scheduler = new ReminderScheduler(_store, DispatcherQueue.GetForCurrentThread());
            _scheduler.ReminderFired += OnReminderFired;

            MainWindow = new MainWindow(_settingsService, _store, _scheduler);
            MainWindow.Activate();

            SetupTrayIcon();

            MainWindow.AppWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                MainWindow.AppWindow.Hide();
            };

            // Check for missed reminders, then start the scheduler
            await CheckMissedRemindersAsync();
            _scheduler.Start();
        }

        private void SetupTrayIcon()
        {
            var showItem = new MenuFlyoutItem { Text = "Show" };
            showItem.Click += (s, e) =>
            {
                MainWindow.AppWindow.Show();
                MainWindow.Activate();
            };

            var exitItem = new MenuFlyoutItem { Text = "Exit" };
            exitItem.Click += (s, e) =>
            {
                _scheduler.Stop();
                _trayIcon?.Dispose();
                MainWindow.Close();
                Environment.Exit(0);
            };

            var flyout = new MenuFlyout();
            flyout.Items.Add(showItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(exitItem);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Hey Stupid - Reminders",
                ContextFlyout = flyout
            };

            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new Icon(iconPath);
            }

            _trayIcon.LeftClickCommand = new RelayCommand(() =>
            {
                MainWindow.AppWindow.Show();
                MainWindow.Activate();
            });

            _trayIcon.ForceCreate();
        }

        private async Task CheckMissedRemindersAsync()
        {
            var missed = _scheduler.GetMissedReminders();
            if (missed.Count == 0)
            {
                return;
            }

            // Wait briefly for the UI to fully load
            await Task.Delay(500).ConfigureAwait(false);

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new MissedRemindersDialog(missed, _scheduler);
                dialog.XamlRoot = MainWindow.Content.XamlRoot;
                await dialog.ShowAsync();
                MainWindow.RefreshList();
            });
        }

        private void OnReminderFired(Reminder reminder)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                // Close existing popup for this reminder if open
                if (_openPopups.TryGetValue(reminder.Id, out var existing))
                {
                    try { existing.Close(); } catch { }
                    _openPopups.Remove(reminder.Id);
                }

                var popup = new ReminderPopupWindow(reminder, _scheduler);
                popup.Acknowledged += OnPopupAcknowledged;
                popup.AppWindow.Closing += (s, e) =>
                {
                    _openPopups.Remove(reminder.Id);
                };
                _openPopups[reminder.Id] = popup;
                popup.Activate();
            });
        }

        private void OnPopupAcknowledged(Guid reminderId)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_openPopups.TryGetValue(reminderId, out var popup))
                {
                    _openPopups.Remove(reminderId);
                }
                MainWindow.RefreshList();
            });
        }

        /// <summary>
        /// Simple relay command for tray icon click.
        /// </summary>
        private sealed class RelayCommand : System.Windows.Input.ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute;
            }

#pragma warning disable CS0067
            public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => _execute();
        }
    }
}