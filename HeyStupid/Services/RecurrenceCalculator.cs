namespace HeyStupid.Services
{
    using System;
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