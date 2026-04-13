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
        private const int TimelineWeekChunk = 3;
        private const int MaxEntriesPerReminderPerDay = 6;

        private readonly SettingsService _settingsService;
        private readonly JsonReminderStore _store;
        private readonly ReminderScheduler _scheduler;
        private bool _isRefreshing;
        private bool _initialized;
        private SettingsWindow? _settingsWindow;
        private readonly Dictionary<Guid, ReminderEditWindow> _editWindows = new();
        private DateTime _timelineRangeEnd;
        private ScrollViewer? _timelineScrollViewer;
        private bool _timelineScrollHooked;
        private bool _timelineRebuilding;
        private bool _timelineRebuildPending;

        public MainWindow(SettingsService settingsService, JsonReminderStore store, ReminderScheduler scheduler)
        {
            _settingsService = settingsService;
            _store = store;
            _scheduler = scheduler;

            InitializeComponent();

            ConfigureWindow();
            ApplyHomeView(settingsService.Settings.HomeView, persist: false);
            _initialized = true;
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
                var isTimeline = _settingsService.Settings.HomeView == HomeView.Timeline;

                if (isTimeline)
                {
                    SetVisibility(EmptyState, Visibility.Collapsed);
                    SetVisibility(ReminderListView, Visibility.Collapsed);
                    SetVisibility(TimelineView, Visibility.Visible);
                    QueueTimelineRebuild();
                }
                else
                {
                    SetVisibility(TimelineView, Visibility.Collapsed);
                    SetVisibility(EmptyState, hasReminders ? Visibility.Collapsed : Visibility.Visible);
                    SetVisibility(ReminderListView, hasReminders ? Visibility.Visible : Visibility.Collapsed);
                }

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

        private void ViewSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (_initialized == false)
            {
                return;
            }

            var selected = sender.SelectedItem == TimelineViewItem
                ? HomeView.Timeline
                : HomeView.List;

            if (_settingsService.Settings.HomeView == selected)
            {
                return;
            }

            _settingsService.Settings.HomeView = selected;
            if (selected == HomeView.Timeline)
            {
                _timelineRangeEnd = DateTime.Today.AddDays(7 * TimelineWeekChunk);
            }
            _ = _settingsService.SaveAsync();
            RefreshList();
        }

        private void ApplyHomeView(HomeView view, bool persist)
        {
            _settingsService.Settings.HomeView = view;
            ViewSelector.SelectedItem = view == HomeView.Timeline ? TimelineViewItem : ListViewItem;

            if (view == HomeView.Timeline)
            {
                _timelineRangeEnd = DateTime.Today.AddDays(7 * TimelineWeekChunk);
            }

            if (persist)
            {
                _ = _settingsService.SaveAsync();
            }
        }

        private static void SetVisibility(UIElement element, Visibility value)
        {
            if (element.Visibility != value)
            {
                element.Visibility = value;
            }
        }

        private void QueueTimelineRebuild()
        {
            if (_timelineRebuildPending)
            {
                return;
            }

            _timelineRebuildPending = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _timelineRebuildPending = false;
                RebuildTimeline();
            });
        }

        private void RebuildTimeline()
        {
            if (TimelineListView == null || _timelineRebuilding)
            {
                return;
            }

            _timelineRebuilding = true;
            try
            {
                var today = DateTime.Today;
                if (_timelineRangeEnd < today.AddDays(7 * TimelineWeekChunk))
                {
                    _timelineRangeEnd = today.AddDays(7 * TimelineWeekChunk);
                }

                var days = BuildTimelineDays(today, _timelineRangeEnd);
                TimelineListView.ItemsSource = days;

                HookTimelineScroll();
            }
            finally
            {
                _timelineRebuilding = false;
            }
        }

        private List<TimelineDay> BuildTimelineDays(DateTime rangeStart, DateTime rangeEnd)
        {
            var now = DateTime.Now;
            var reminders = _store.GetAll().Where(r => r.IsActive).ToList();

            // Index occurrences by local date so we can bucket them by day.
            var perReminderPerDay = new Dictionary<Guid, Dictionary<DateTime, List<DateTime>>>();

            foreach (var reminder in reminders)
            {
                // Enumerate from today through the end of the requested range; we don't show
                // past days on the timeline, so rangeStart = today 00:00.
                foreach (var occurrence in RecurrenceCalculator.EnumerateOccurrences(reminder, rangeStart, rangeEnd.AddDays(1).AddTicks(-1)))
                {
                    if (occurrence < now && reminder.IsWaitingForAcknowledgment == false)
                    {
                        // Fired-and-done occurrences in the past don't belong in the timeline.
                        continue;
                    }

                    if (perReminderPerDay.TryGetValue(reminder.Id, out var byDate) == false)
                    {
                        byDate = new Dictionary<DateTime, List<DateTime>>();
                        perReminderPerDay[reminder.Id] = byDate;
                    }

                    var dateKey = occurrence.Date;
                    if (byDate.TryGetValue(dateKey, out var list) == false)
                    {
                        list = new List<DateTime>();
                        byDate[dateKey] = list;
                    }
                    list.Add(occurrence);
                }
            }

            var days = new List<TimelineDay>();
            for (var date = rangeStart.Date; date <= rangeEnd.Date; date = date.AddDays(1))
            {
                var day = new TimelineDay
                {
                    Date = date,
                    Header = FormatDayHeader(date, now.Date),
                    IsToday = date == now.Date
                };

                // Past-due (today only). Each waiting reminder contributes one entry even if
                // it has fired multiple times without acknowledgment — we dedupe by Reminder.Id.
                if (day.IsToday)
                {
                    foreach (var reminder in reminders.Where(r => r.IsWaitingForAcknowledgment && r.NextDue.HasValue))
                    {
                        day.PastDueEntries.Add(new TimelineEntry
                        {
                            Reminder = reminder,
                            OccurrenceTime = reminder.NextDue!.Value,
                            IsPastDue = true,
                            TimeText = FormatPastDue(now - reminder.NextDue.Value),
                            DetailText = FormatDetail(reminder, reminder.NextDue.Value)
                        });
                    }
                }

                // Regular entries for this day, collapsing per-reminder bursts.
                foreach (var reminder in reminders)
                {
                    if (perReminderPerDay.TryGetValue(reminder.Id, out var byDate) == false)
                    {
                        continue;
                    }

                    if (byDate.TryGetValue(date, out var occurrences) == false || occurrences.Count == 0)
                    {
                        continue;
                    }

                    // Don't render the same reminder twice on today if it's waiting — the
                    // past-due section already covers it. Future same-day occurrences still render.
                    var threshold = day.IsToday && reminder.IsWaitingForAcknowledgment
                        ? reminder.NextDue ?? DateTime.MinValue
                        : DateTime.MinValue;
                    var filtered = occurrences.Where(o => o > threshold).OrderBy(o => o).ToList();
                    if (filtered.Count == 0)
                    {
                        continue;
                    }

                    if (filtered.Count > MaxEntriesPerReminderPerDay)
                    {
                        var first = filtered[0];
                        var last = filtered[^1];
                        day.Entries.Add(new TimelineEntry
                        {
                            Reminder = reminder,
                            OccurrenceTime = first,
                            IsCollapsed = true,
                            TimeText = $"{first:h:mm tt} – {last:h:mm tt}",
                            DetailText = $"{reminder.RecurrenceSummary} · {filtered.Count} times"
                        });
                        continue;
                    }

                    foreach (var occurrence in filtered)
                    {
                        day.Entries.Add(new TimelineEntry
                        {
                            Reminder = reminder,
                            OccurrenceTime = occurrence,
                            TimeText = occurrence.ToString("h:mm tt"),
                            DetailText = FormatDetail(reminder, occurrence)
                        });
                    }
                }

                day.Entries = day.Entries.OrderBy(e => e.OccurrenceTime).ToList();
                day.Summary = BuildDaySummary(day);
                days.Add(day);
            }

            return days;
        }

        private static string FormatDayHeader(DateTime date, DateTime today)
        {
            if (date == today)
            {
                return $"Today · {date:dddd, MMM d}";
            }
            if (date == today.AddDays(1))
            {
                return $"Tomorrow · {date:dddd, MMM d}";
            }
            return date.ToString("dddd, MMM d");
        }

        private static string BuildDaySummary(TimelineDay day)
        {
            var total = day.PastDueEntries.Count + day.Entries.Count;
            if (total == 0)
            {
                return "No reminders";
            }
            return total == 1 ? "1 reminder" : $"{total} reminders";
        }

        private static string FormatDetail(Reminder reminder, DateTime occurrence)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(reminder.CategoryName) == false)
            {
                parts.Add(reminder.CategoryName);
            }
            parts.Add(reminder.RecurrenceSummary);
            return string.Join(" · ", parts);
        }

        private static string FormatPastDue(TimeSpan overdue)
        {
            if (overdue.TotalMinutes < 1)
            {
                return "Just now";
            }
            if (overdue.TotalHours < 1)
            {
                return $"{(int)overdue.TotalMinutes}m past due";
            }
            if (overdue.TotalDays < 1)
            {
                return $"{(int)overdue.TotalHours}h past due";
            }
            return $"{(int)overdue.TotalDays}d past due";
        }

        private void HookTimelineScroll()
        {
            if (_timelineScrollHooked)
            {
                return;
            }

            _timelineScrollViewer = FindDescendant<ScrollViewer>(TimelineListView);
            if (_timelineScrollViewer == null)
            {
                // ItemsPanel isn't realized yet; retry after layout.
                TimelineListView.Loaded += (s, e) =>
                {
                    if (_timelineScrollHooked == false)
                    {
                        HookTimelineScroll();
                    }
                };
                return;
            }

            _timelineScrollViewer.ViewChanged += TimelineScroller_ViewChanged;
            _timelineScrollHooked = true;
        }

        private void TimelineScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate || _timelineRebuilding || _initialized == false)
            {
                return;
            }

            if (_timelineScrollViewer == null || _settingsService.Settings.HomeView != HomeView.Timeline)
            {
                return;
            }

            if (_timelineScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            var threshold = _timelineScrollViewer.ScrollableHeight - 200;
            if (_timelineScrollViewer.VerticalOffset >= threshold)
            {
                _timelineRangeEnd = _timelineRangeEnd.AddDays(7 * TimelineWeekChunk);
                QueueTimelineRebuild();
            }
        }

        private void JumpToTodayButton_Click(object sender, RoutedEventArgs e)
        {
            if (TimelineListView.Items.Count == 0)
            {
                return;
            }

            var target = TimelineListView.Items[0];
            TimelineListView.ScrollIntoView(target);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    return typed;
                }
                var found = FindDescendant<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private async void TimelineToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            if (sender is ToggleSwitch toggle && toggle.DataContext is TimelineEntry entry)
            {
                var reminder = entry.Reminder;
                // Binding-initialized Toggled events (row realized during refresh) arrive with
                // the toggle already matching the reminder's state. Treat those as no-ops.
                if (reminder.IsActive == toggle.IsOn)
                {
                    return;
                }
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

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var editWindow = new ReminderEditWindow(_settingsService.Settings);
            editWindow.Closed += async (s, args) =>
            {
                if (editWindow.Result != null)
                {
                    await _store.SaveAsync(editWindow.Result).ConfigureAwait(true);
                    RefreshList();
                }
            };
            editWindow.Activate();
        }

        private static Reminder? ExtractReminder(object context)
        {
            return context switch
            {
                Reminder r => r,
                TimelineEntry entry => entry.Reminder,
                _ => null
            };
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && ExtractReminder(button.DataContext) is Reminder reminder)
            {
                if (_editWindows.ContainsKey(reminder.Id))
                {
                    _editWindows[reminder.Id].Activate();
                    return;
                }

                var editWindow = new ReminderEditWindow(_settingsService.Settings, reminder);
                _editWindows[reminder.Id] = editWindow;
                editWindow.Closed += async (s, args) =>
                {
                    _editWindows.Remove(reminder.Id);
                    if (editWindow.Result != null)
                    {
                        var edited = editWindow.Result;
                        edited.IsWaitingForAcknowledgment = false;
                        edited.CurrentRetryCount = 0;
                        edited.NextRetry = null;
                        await _store.SaveAsync(edited).ConfigureAwait(true);
                        RefreshList();
                    }
                };
                editWindow.Activate();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && ExtractReminder(button.DataContext) is Reminder reminder)
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
            if (sender is FrameworkElement element && ExtractReminder(element.DataContext) is Reminder reminder)
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
                if (reminder.IsActive == toggle.IsOn)
                {
                    return;
                }
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
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(_settingsService);
            _settingsWindow.Closed += async (s, args) =>
            {
                var changed = _settingsWindow.SourcesChanged;
                _settingsWindow = null;
                if (changed)
                {
                    _store.SetSources(_settingsService.Settings.ReminderSources);
                    await _store.LoadAsync().ConfigureAwait(true);
                    RefreshList();
                }
            };
            _settingsWindow.Activate();
        }
    }
}