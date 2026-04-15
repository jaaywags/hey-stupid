namespace HeyStupid.Services
{
    using System;
    using System.Collections.Generic;
    using HeyStupid.Models;

    public static class RecurrenceCalculator
    {
        public static DateTime? CalculateNextDue(Reminder reminder, DateTime from)
        {
            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.Once:
                    return null;

                case RecurrenceType.EveryNMinutes:
                    return ClampToActiveHours(reminder, TruncateSeconds(from.AddMinutes(reminder.RecurrenceInterval)));

                case RecurrenceType.Hourly:
                    return ClampToActiveHours(reminder, TruncateSeconds(from.AddHours(reminder.RecurrenceInterval)));

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

                    var candidate = from.Date.AddDays(1)
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

                    for (int i = 0; i < 7; i++)
                    {
                        var checkDate = candidate.AddDays(i);
                        var dayFlag = ToDaysOfWeek(checkDate.DayOfWeek);
                        if (reminder.RecurrenceDays.HasFlag(dayFlag) && checkDate > from)
                        {
                            if (weeksToSkip == 0)
                            {
                                return checkDate;
                            }
                            break;
                        }
                    }

                    var weekStart = from.Date.AddDays(7 * interval)
                        .AddHours(reminder.ReminderHour)
                        .AddMinutes(reminder.ReminderMinute);

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
                    return ClampToActiveHours(reminder, TruncateSeconds(now.AddMinutes(reminder.RecurrenceInterval)));

                case RecurrenceType.Hourly:
                    return ClampToActiveHours(reminder, TruncateSeconds(now.AddHours(reminder.RecurrenceInterval)));

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

        /// <summary>
        /// Walks a reminder's recurrence forward and yields every fire time that falls within
        /// [rangeStart, rangeEnd]. Enumeration is capped at maxOccurrences so high-frequency
        /// recurrences can't run unbounded.
        /// </summary>
        public static IEnumerable<DateTime> EnumerateOccurrences(
            Reminder reminder,
            DateTime rangeStart,
            DateTime rangeEnd,
            int maxOccurrences = 5000)
        {
            if (rangeEnd < rangeStart)
            {
                yield break;
            }

            if (reminder.RecurrenceType == RecurrenceType.Once)
            {
                if (reminder.NextDue.HasValue
                    && reminder.NextDue.Value >= rangeStart
                    && reminder.NextDue.Value <= rangeEnd)
                {
                    yield return reminder.NextDue.Value;
                }
                yield break;
            }

            var cursor = reminder.NextDue ?? rangeStart;
            var produced = 0;
            var safety = 0;

            // Fast-forward past rangeStart without emitting, using CalculateNextDue so active
            // hours / weekly day masks / etc. are honored exactly like the scheduler does.
            while (cursor < rangeStart && safety++ < maxOccurrences)
            {
                var next = CalculateNextDue(reminder, cursor);
                if (next == null || next.Value <= cursor)
                {
                    yield break;
                }
                cursor = next.Value;
            }

            while (cursor <= rangeEnd && produced < maxOccurrences)
            {
                if (cursor >= rangeStart)
                {
                    yield return cursor;
                    produced++;
                }

                var next = CalculateNextDue(reminder, cursor);
                if (next == null || next.Value <= cursor)
                {
                    yield break;
                }
                cursor = next.Value;
            }
        }

        public static DateTime ClampToActiveHours(Reminder reminder, DateTime candidate)
        {
            if (reminder.ActiveHoursEnabled == false)
            {
                return candidate;
            }

            var startMinutes = NormalizeMinuteOfDay(reminder.ActiveHoursStartHour, reminder.ActiveHoursStartMinute);
            var endMinutes = NormalizeMinuteOfDay(reminder.ActiveHoursEndHour, reminder.ActiveHoursEndMinute);

            // Degenerate window: treat as "always active" so we never block forever.
            if (startMinutes == endMinutes)
            {
                return candidate;
            }

            var candidateMinutes = candidate.Hour * 60 + candidate.Minute;
            var overnight = startMinutes > endMinutes;

            bool inWindow = overnight
                ? candidateMinutes >= startMinutes || candidateMinutes < endMinutes
                : candidateMinutes >= startMinutes && candidateMinutes < endMinutes;

            if (inWindow)
            {
                return candidate;
            }

            // Push forward to the next start-of-window.
            var startToday = candidate.Date
                .AddHours(reminder.ActiveHoursStartHour)
                .AddMinutes(reminder.ActiveHoursStartMinute);

            if (overnight)
            {
                // Overnight window: only reachable hole is [endMinutes, startMinutes) on the same day.
                return startToday;
            }

            return candidateMinutes < startMinutes ? startToday : startToday.AddDays(1);
        }

        private static DateTime TruncateSeconds(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);
        }

        private static int NormalizeMinuteOfDay(int hour, int minute)
        {
            var h = Math.Clamp(hour, 0, 23);
            var m = Math.Clamp(minute, 0, 59);
            return h * 60 + m;
        }

        /// <summary>
        /// Advances from the reminder's last scheduled time (NextDue) rather than from "now",
        /// stepping forward through any occurrences that have already lapsed, until a future
        /// occurrence is found.  This keeps recurring reminders on their original cadence even
        /// when acknowledgment is delayed.
        /// </summary>
        public static DateTime? CalculateNextFutureDue(Reminder reminder)
        {
            return CalculateNextFutureDue(reminder, DateTime.Now);
        }

        /// <summary>
        /// Walks forward from NextDue and returns the most recent occurrence that is still
        /// at or before "now" — used to auto-advance a recurring reminder that got stuck
        /// waiting for an acknowledgment the user never gave.  Returns null if no newer
        /// occurrence has come due yet (or the reminder is non-recurring).
        /// </summary>
        public static DateTime? CalculateMostRecentPastDue(Reminder reminder, DateTime now)
        {
            if (reminder.RecurrenceType == RecurrenceType.Once || reminder.NextDue.HasValue == false)
            {
                return null;
            }

            var cursor = reminder.NextDue.Value;
            DateTime? mostRecent = null;

            for (int i = 0; i < 5000; i++)
            {
                var next = CalculateNextDue(reminder, cursor);
                if (next == null || next.Value > now)
                {
                    break;
                }
                mostRecent = next;
                cursor = next.Value;
            }

            return mostRecent;
        }

        /// <summary>
        /// Overload that accepts "now" explicitly so callers (and tests) can control the clock.
        /// </summary>
        public static DateTime? CalculateNextFutureDue(Reminder reminder, DateTime now)
        {
            var cursor = reminder.NextDue ?? now;

            for (int i = 0; i < 5000; i++)
            {
                var next = CalculateNextDue(reminder, cursor);
                if (next == null)
                {
                    return null;
                }
                if (next.Value > now)
                {
                    return next.Value;
                }
                cursor = next.Value;
            }

            // Safety fallback — should never be reached for reasonable intervals.
            return CalculateNextDue(reminder, now);
        }

        public static DaysOfWeek ToDaysOfWeek(DayOfWeek day)
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