namespace HeyStupid
{
    using System;
    using System.IO;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI;
    using Microsoft.UI.Input;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Windows.Graphics;

    public sealed partial class ReminderPopupWindow : Window
    {
        private readonly Reminder _reminder;
        private readonly ReminderScheduler _scheduler;

        public event Action<Guid>? Acknowledged;

        public Guid ReminderId => _reminder.Id;

        public ReminderPopupWindow(Reminder reminder, ReminderScheduler scheduler)
        {
            _reminder = reminder;
            _scheduler = scheduler;

            InitializeComponent();
            ConfigureWindow();
            PopulateContent();

            // Cursor set via HandCursorButton in XAML
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(400, 340));
            Title = "Hey Stupid!";

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trayicon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }

            // Center on screen
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                var x = (workArea.Width - 400) / 2;
                var y = (workArea.Height - 340) / 2;
                AppWindow.Move(new PointInt32(x, y));
            }
        }

        private void PopulateContent()
        {
            ReminderTitle.Text = _reminder.Title;

            if (string.IsNullOrWhiteSpace(_reminder.Message) == false)
            {
                ReminderMessage.Text = _reminder.Message;
            }
            else
            {
                ReminderMessage.Visibility = Visibility.Collapsed;
            }

            if (_reminder.CurrentRetryCount > 1)
            {
                AttemptText.Text = $"Attempt {_reminder.CurrentRetryCount} of {_reminder.MaxRetries}";
                AttemptText.Visibility = Visibility.Visible;
            }

            if (_reminder.LastFired.HasValue)
            {
                TimeText.Text = $"Fired at {_reminder.LastFired.Value:h:mm tt}";
            }
        }

        private async void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
        {
            await _scheduler.AcknowledgeAsync(_reminder.Id).ConfigureAwait(true);
            Acknowledged?.Invoke(_reminder.Id);
            Close();
        }
    }
}