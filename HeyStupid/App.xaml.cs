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
        private bool _isExiting;

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
                if (_isExiting == false)
                {
                    e.Cancel = true;
                    MainWindow.AppWindow.Hide();
                }
            };

            // Check for missed reminders, then start the scheduler
            await CheckMissedRemindersAsync();
            _scheduler.Start();
        }

        private void SetupTrayIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Hey Stupid - Reminders"
            };

            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new Icon(iconPath);
            }

            _trayIcon.NoLeftClickDelay = true;

            _trayIcon.LeftClickCommand = new RelayCommand(() =>
            {
                MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    MainWindow.AppWindow.Show();
                    MainWindow.Activate();
                });
            });

            _trayIcon.RightClickCommand = new RelayCommand(() =>
            {
                ShowTrayContextMenu();
            });

            _trayIcon.ForceCreate();
        }

        private void ShowTrayContextMenu()
        {
            const uint TPM_RIGHTALIGN = 0x0008;
            const uint TPM_BOTTOMALIGN = 0x0020;
            const uint TPM_RETURNCMD = 0x0100;
            const uint MF_STRING = 0x0000;
            const uint MF_SEPARATOR = 0x0800;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);

            var hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, 1, "Show");
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
            AppendMenu(hMenu, MF_STRING, 2, "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);

            var cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD,
                point.X, point.Y, hwnd, IntPtr.Zero);

            DestroyMenu(hMenu);

            if (cmd == 1)
            {
                MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    MainWindow.AppWindow.Show();
                    MainWindow.Activate();
                });
            }
            else if (cmd == 2)
            {
                ExitApplication();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

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

        private void ExitApplication()
        {
            _isExiting = true;
            _scheduler.Stop();

            try { _trayIcon?.Dispose(); } catch { }

            foreach (var popup in _openPopups.Values.ToList())
            {
                try { popup.Close(); } catch { }
            }

            try { MainWindow.Close(); } catch { }

            System.Diagnostics.Process.GetCurrentProcess().Kill();
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