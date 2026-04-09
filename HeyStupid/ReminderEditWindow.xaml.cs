namespace HeyStupid
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Windows.Graphics;

    public sealed partial class ReminderEditWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly Reminder? _existing;

        public Reminder? Result { get; private set; }

        public ReminderEditWindow(AppSettings settings, Reminder? existing = null)
        {
            _settings = settings;
            _existing = existing;

            InitializeComponent();
            ConfigureWindow();
            LoadCategories();
            LoadDefaults();

            if (_existing != null)
            {
                Title = "Edit Reminder";
                PopulateFrom(_existing);
            }
        }

        private void ConfigureWindow()
        {
            AppWindow.Resize(new SizeInt32(500, 680));
            Title = "New Reminder";

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

        private void LoadCategories()
        {
            CategoryBox.ItemsSource = _settings.Categories;

            if (_settings.Categories.Count == 0)
            {
                CategoryBox.Visibility = Visibility.Collapsed;
            }

            if (_existing != null && _existing.CategoryId.HasValue)
            {
                var match = _settings.Categories.FirstOrDefault(c => c.Id == _existing.CategoryId.Value);
                CategoryBox.SelectedItem = match;
            }
        }

        private void LoadDefaults()
        {
            MaxRetriesBox.Value = _settings.DefaultMaxRetries;
            RetryIntervalBox.Value = _settings.DefaultRetryIntervalMinutes;
            RecurrenceBox.SelectedIndex = 0;
            StartDatePicker.Date = DateTimeOffset.Now;
            TimePicker.Time = new TimeSpan(9, 0, 0);
            UpdateFieldVisibility();
        }

        private void PopulateFrom(Reminder r)
        {
            TitleBox.Text = r.Title;
            MessageBox.Text = r.Message;

            RecurrenceBox.SelectedIndex = r.RecurrenceType switch
            {
                RecurrenceType.Once => 0,
                RecurrenceType.EveryNMinutes => 1,
                RecurrenceType.Hourly => 2,
                RecurrenceType.Daily => 3,
                RecurrenceType.Weekly => 4,
                RecurrenceType.Monthly => 5,
                _ => 0
            };

            IntervalBox.Value = r.RecurrenceInterval;
            DayOfMonthBox.Value = r.ReminderDayOfMonth;
            TimePicker.Time = new TimeSpan(r.ReminderHour, r.ReminderMinute, 0);

            if (r.NextDue.HasValue)
            {
                StartDatePicker.Date = new DateTimeOffset(r.NextDue.Value.Date);
            }
            else
            {
                StartDatePicker.Date = DateTimeOffset.Now;
            }

            SunToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Sunday);
            MonToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Monday);
            TueToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Tuesday);
            WedToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Wednesday);
            ThuToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Thursday);
            FriToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Friday);
            SatToggle.IsChecked = r.RecurrenceDays.HasFlag(DaysOfWeek.Saturday);

            AckToggle.IsOn = r.RequireAcknowledgment;
            MaxRetriesBox.Value = r.MaxRetries;
            RetryIntervalBox.Value = r.RetryIntervalMinutes;

            UpdateFieldVisibility();
        }

        private RecurrenceType GetSelectedRecurrence()
        {
            if (RecurrenceBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return tag switch
                {
                    "Once" => RecurrenceType.Once,
                    "EveryNMinutes" => RecurrenceType.EveryNMinutes,
                    "Hourly" => RecurrenceType.Hourly,
                    "Daily" => RecurrenceType.Daily,
                    "Weekly" => RecurrenceType.Weekly,
                    "Monthly" => RecurrenceType.Monthly,
                    _ => RecurrenceType.Once
                };
            }
            return RecurrenceType.Once;
        }

        private void UpdateFieldVisibility()
        {
            var recurrence = GetSelectedRecurrence();

            var showInterval = recurrence == RecurrenceType.EveryNMinutes
                || recurrence == RecurrenceType.Hourly
                || recurrence == RecurrenceType.Daily
                || recurrence == RecurrenceType.Weekly
                || recurrence == RecurrenceType.Monthly;
            IntervalBox.Visibility = showInterval ? Visibility.Visible : Visibility.Collapsed;

            IntervalBox.Header = recurrence switch
            {
                RecurrenceType.EveryNMinutes => "Every N minutes",
                RecurrenceType.Hourly => "Every N hours",
                RecurrenceType.Daily => "Every N days",
                RecurrenceType.Weekly => "Every N weeks",
                RecurrenceType.Monthly => "Every N months",
                _ => "Interval"
            };

            DaysPanel.Visibility = recurrence == RecurrenceType.Weekly
                ? Visibility.Visible
                : Visibility.Collapsed;

            DayOfMonthBox.Visibility = recurrence == RecurrenceType.Monthly
                ? Visibility.Visible
                : Visibility.Collapsed;

            DateTimePanel.Visibility = Visibility.Visible;
        }

        private void RecurrenceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IntervalBox != null)
            {
                UpdateFieldVisibility();
            }
        }

        private void AckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AckSettings != null)
            {
                AckSettings.Visibility = AckToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private DaysOfWeek GetSelectedDays()
        {
            var days = DaysOfWeek.None;
            if (SunToggle.IsChecked == true) { days |= DaysOfWeek.Sunday; }
            if (MonToggle.IsChecked == true) { days |= DaysOfWeek.Monday; }
            if (TueToggle.IsChecked == true) { days |= DaysOfWeek.Tuesday; }
            if (WedToggle.IsChecked == true) { days |= DaysOfWeek.Wednesday; }
            if (ThuToggle.IsChecked == true) { days |= DaysOfWeek.Thursday; }
            if (FriToggle.IsChecked == true) { days |= DaysOfWeek.Friday; }
            if (SatToggle.IsChecked == true) { days |= DaysOfWeek.Saturday; }
            return days == DaysOfWeek.None ? DaysOfWeek.All : days;
        }

        private DateTime CalculateNextDueFromInputs(RecurrenceType recurrence)
        {
            var now = DateTime.Now;
            var selectedDate = StartDatePicker.Date?.Date ?? now.Date;
            var selectedTime = TimePicker.Time;
            var nextDue = selectedDate.Add(selectedTime);

            if (nextDue > now)
            {
                return nextDue;
            }

            if (recurrence == RecurrenceType.Once)
            {
                return nextDue;
            }

            return RecurrenceCalculator.CalculateInitialDue(new Reminder
            {
                RecurrenceType = recurrence,
                RecurrenceInterval = (int)IntervalBox.Value,
                RecurrenceDays = GetSelectedDays(),
                ReminderHour = selectedTime.Hours,
                ReminderMinute = selectedTime.Minutes,
                ReminderDayOfMonth = (int)DayOfMonthBox.Value
            });
        }

        private Guid ResolveSourceId()
        {
            if (CategoryBox.SelectedItem is ReminderCategory category && category.FolderId.HasValue)
            {
                var source = _settings.ReminderSources.FirstOrDefault(s => s.Id == category.FolderId.Value);
                if (source != null)
                {
                    return source.Id;
                }
            }

            if (_existing != null)
            {
                return _existing.SourceId;
            }

            return _settings.GetDefaultSource().Id;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Header = "Title (required)";
                return;
            }

            var recurrence = GetSelectedRecurrence();
            var selectedCategory = CategoryBox.SelectedItem as ReminderCategory;

            Result = new Reminder
            {
                Id = _existing?.Id ?? Guid.NewGuid(),
                Title = TitleBox.Text.Trim(),
                Message = MessageBox.Text?.Trim() ?? string.Empty,
                IsActive = _existing?.IsActive ?? true,
                RecurrenceType = recurrence,
                RecurrenceInterval = (int)IntervalBox.Value,
                RecurrenceDays = GetSelectedDays(),
                ReminderHour = TimePicker.Time.Hours,
                ReminderMinute = TimePicker.Time.Minutes,
                ReminderDayOfMonth = (int)DayOfMonthBox.Value,
                CategoryId = selectedCategory?.Id,
                CategoryName = selectedCategory?.Name ?? string.Empty,
                RequireAcknowledgment = AckToggle.IsOn,
                MaxRetries = (int)MaxRetriesBox.Value,
                RetryIntervalMinutes = (int)RetryIntervalBox.Value,
                Created = _existing?.Created ?? DateTime.Now,
                SourceId = ResolveSourceId(),
                NextDue = CalculateNextDueFromInputs(recurrence)
            };

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}