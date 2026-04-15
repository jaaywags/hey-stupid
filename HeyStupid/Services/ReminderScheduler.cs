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
            reminder.NextDue = RecurrenceCalculator.CalculateNextFutureDue(reminder);

            if (reminder.RecurrenceType == RecurrenceType.Once)
            {
                reminder.IsActive = false;
            }

            await _store.SaveAsync(reminder).ConfigureAwait(false);
            RemindersChanged?.Invoke();
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
                // A recurring reminder that's still marked "waiting for ack" when its next
                // scheduled occurrence has come due shouldn't stay stuck forever (which can
                // happen when the user closes the popup without acking, especially with
                // MaxRetries=0).  Auto-advance past the stuck occurrence to the most recent
                // past one so the fire branch below picks it up as a fresh occurrence.
                if (reminder.IsWaitingForAcknowledgment)
                {
                    var advanced = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);
                    if (advanced.HasValue)
                    {
                        reminder.IsWaitingForAcknowledgment = false;
                        reminder.CurrentRetryCount = 0;
                        reminder.NextRetry = null;
                        reminder.NextDue = advanced.Value;
                    }
                }

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
                        reminder.NextDue = RecurrenceCalculator.CalculateNextDue(reminder, now);
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
    }
}