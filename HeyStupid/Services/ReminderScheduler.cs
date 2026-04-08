namespace HeyStupid.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using HeyStupid.Models;
    using Microsoft.UI.Dispatching;
    using Microsoft.UI.Xaml;

    public class ReminderScheduler
    {
        private readonly JsonReminderStore _store;
        private readonly DispatcherQueueTimer _timer;

        public event Action<Reminder>? ReminderFired;
        public event Action? RemindersChanged;

        public ReminderScheduler(JsonReminderStore store, DispatcherQueue dispatcherQueue)
        {
            _store = store;
            _timer = dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(15);
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public List<Reminder> GetMissedReminders()
        {
            var now = DateTime.Now;
            return _store.GetAll()
                .Where(r => r.IsActive
                    && r.NextDue.HasValue
                    && r.NextDue.Value < now
                    && r.IsWaitingForAcknowledgment == false)
                .OrderBy(r => r.NextDue)
                .ToList();
        }

        public async Task AcknowledgeAsync(Guid reminderId)
        {
            var reminder = _store.GetById(reminderId);
            if (reminder == null)
            {
                return;
            }

            reminder.IsWaitingForAcknowledgment = false;
            reminder.CurrentRetryCount = 0;
            reminder.NextRetry = null;
            reminder.NextDue = CalculateNextDue(reminder, DateTime.Now);

            if (reminder.RecurrenceType == RecurrenceType.Once)
            {
                reminder.IsActive = false;
            }

            await _store.SaveAsync(reminder).ConfigureAwait(false);
            RemindersChanged?.Invoke();
        }

        public async Task CalculateAndSetNextDueAsync(Reminder reminder)
        {
            if (reminder.NextDue.HasValue == false)
            {
                reminder.NextDue = CalculateNextDue(reminder, DateTime.Now);
                await _store.SaveAsync(reminder).ConfigureAwait(false);
            }
        }

        public static DateTime? CalculateNextDue(Reminder reminder, DateTime from)
        {
            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.Once:
                    return null;

                case RecurrenceType.EveryNMinutes:
                    return from.AddMinutes(reminder.RecurrenceInterval);

                case RecurrenceType.Hourly:
                    return from.AddHours(reminder.RecurrenceInterval);

                case RecurrenceType.Daily:
                {
                    var next = from.Date
                        .AddDays(reminder.RecurrenceInterval)
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);
                    return next;
                }

                case RecurrenceType.Weekly:
                {
                    var interval = Math.Max(1, reminder.RecurrenceInterval);
                    var weeksToSkip = interval - 1;

                    // Start searching from tomorrow within the current week cycle
                    var candidate = from.Date.AddDays(1)
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

                    // Check remaining days this week first
                    for (int i = 0; i < 7; i++)
                    {
                        var checkDate = candidate.AddDays(i);
                        var dayFlag = ToDaysOfWeek(checkDate.DayOfWeek);
                        if (reminder.RecurrenceDays.HasFlag(dayFlag) && checkDate > from)
                        {
                            // If interval is 1, return the next matching day
                            // If interval > 1, only return if we're still in the same week as the last fire
                            if (weeksToSkip == 0)
                            {
                                return checkDate;
                            }
                            break;
                        }
                    }

                    // Jump ahead by the interval in weeks, then find the first matching day
                    var weekStart = from.Date.AddDays(7 * interval)
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

                    // Find first matching day starting from that Monday-ish area
                    var startOfWeek = weekStart.AddDays(-(int)weekStart.DayOfWeek);
                    startOfWeek = startOfWeek.Date
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

                    for (int i = 0; i < 7; i++)
                    {
                        var checkDate = startOfWeek.AddDays(i);
                        var dayFlag = ToDaysOfWeek(checkDate.DayOfWeek);
                        if (reminder.RecurrenceDays.HasFlag(dayFlag))
                        {
                            return checkDate;
                        }
                    }

                    return weekStart;
                }

                case RecurrenceType.Monthly:
                {
                    var interval = Math.Max(1, reminder.RecurrenceInterval);
                    var nextMonth = from.AddMonths(interval);
                    var day = Math.Min(reminder.ReminderDayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                    return new DateTime(nextMonth.Year, nextMonth.Month, day,
                        reminder.ReminderHour, reminder.ReminderMinute, 0);
                }

                default:
                    return null;
            }
        }

        public static DateTime CalculateInitialDue(Reminder reminder)
        {
            var now = DateTime.Now;

            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.EveryNMinutes:
                    return now.AddMinutes(reminder.RecurrenceInterval);

                case RecurrenceType.Hourly:
                    return now.AddHours(reminder.RecurrenceInterval);

                case RecurrenceType.Once:
                case RecurrenceType.Daily:
                {
                    var today = now.Date
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);
                    return today > now ? today : today.AddDays(reminder.RecurrenceInterval);
                }

                case RecurrenceType.Weekly:
                {
                    var todayAtTime = now.Date
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

                    for (int i = 0; i < 8; i++)
                    {
                        var candidate = todayAtTime.AddDays(i);
                        var dayFlag = ToDaysOfWeek(candidate.DayOfWeek);
                        if (reminder.RecurrenceDays.HasFlag(dayFlag) && candidate > now)
                        {
                            return candidate;
                        }
                    }
                    return todayAtTime.AddDays(7);
                }

                case RecurrenceType.Monthly:
                {
                    var thisMonth = new DateTime(now.Year, now.Month,
                        Math.Min(reminder.ReminderDayOfMonth, DateTime.DaysInMonth(now.Year, now.Month)),
                        reminder.ReminderHour, reminder.ReminderMinute, 0);

                    if (thisMonth > now)
                    {
                        return thisMonth;
                    }

                    var nextMonth = now.AddMonths(reminder.RecurrenceInterval);
                    var day = Math.Min(reminder.ReminderDayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                    return new DateTime(nextMonth.Year, nextMonth.Month, day,
                        reminder.ReminderHour, reminder.ReminderMinute, 0);
                }

                default:
                    return now.AddHours(1);
            }
        }

        private void OnTimerTick(DispatcherQueueTimer sender, object args)
        {
            _ = CheckRemindersAsync();
        }

        private async Task CheckRemindersAsync()
        {
            var now = DateTime.Now;
            var reminders = _store.GetAll();
            var anyChanged = false;

            foreach (var reminder in reminders.Where(r => r.IsActive))
            {
                if (reminder.IsWaitingForAcknowledgment)
                {
                    if (reminder.NextRetry.HasValue
                        && reminder.NextRetry.Value <= now
                        && reminder.CurrentRetryCount < reminder.MaxRetries)
                    {
                        reminder.CurrentRetryCount++;
                        reminder.NextRetry = now.AddMinutes(reminder.RetryIntervalMinutes);
                        reminder.LastFired = now;
                        await _store.SaveAsync(reminder).ConfigureAwait(false);
                        ReminderFired?.Invoke(reminder);
                        anyChanged = true;
                    }
                }
                else if (reminder.NextDue.HasValue && reminder.NextDue.Value <= now)
                {
                    reminder.LastFired = now;

                    if (reminder.RequireAcknowledgment)
                    {
                        reminder.IsWaitingForAcknowledgment = true;
                        reminder.CurrentRetryCount = 1;
                        reminder.NextRetry = now.AddMinutes(reminder.RetryIntervalMinutes);
                    }
                    else
                    {
                        reminder.NextDue = CalculateNextDue(reminder, now);
                        if (reminder.RecurrenceType == RecurrenceType.Once)
                        {
                            reminder.IsActive = false;
                        }
                    }

                    await _store.SaveAsync(reminder).ConfigureAwait(false);
                    ReminderFired?.Invoke(reminder);
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                RemindersChanged?.Invoke();
            }
        }

        private static DaysOfWeek ToDaysOfWeek(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Sunday => DaysOfWeek.Sunday,
                DayOfWeek.Monday => DaysOfWeek.Monday,
                DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
                DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
                DayOfWeek.Thursday => DaysOfWeek.Thursday,
                DayOfWeek.Friday => DaysOfWeek.Friday,
                DayOfWeek.Saturday => DaysOfWeek.Saturday,
                _ => DaysOfWeek.None
            };
        }
    }
}