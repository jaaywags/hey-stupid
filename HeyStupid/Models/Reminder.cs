namespace HeyStupid.Models
{
    using System;
    using System.Text.Json.Serialization;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecurrenceType
    {
        Once,
        EveryNMinutes,
        Hourly,
        Daily,
        Weekly,
        Monthly
    }

    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DaysOfWeek
    {
        None = 0,
        Sunday = 1,
        Monday = 2,
        Tuesday = 4,
        Wednesday = 8,
        Thursday = 16,
        Friday = 32,
        Saturday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekend = Saturday | Sunday,
        All = Weekdays | Weekend
    }

    public class Reminder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        // Recurrence
        public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.Once;
        public int RecurrenceInterval { get; set; } = 1;
        public DaysOfWeek RecurrenceDays { get; set; } = DaysOfWeek.All;
        public int ReminderHour { get; set; } = 9;
        public int ReminderMinute { get; set; }
        public int ReminderDayOfMonth { get; set; } = 1;
        public DateTime? NextDue { get; set; }

        // Category
        public Guid? CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        // Active hours window (applies to EveryNMinutes / Hourly recurrences).
        // When ActiveHoursEnabled is true, reminders only fire if the time-of-day
        // is within [ActiveHoursStart, ActiveHoursEnd). A start greater than the end
        // means the window spans midnight (e.g., 22:00-06:00).
        public bool ActiveHoursEnabled { get; set; }
        public int ActiveHoursStartHour { get; set; } = 8;
        public int ActiveHoursStartMinute { get; set; }
        public int ActiveHoursEndHour { get; set; } = 17;
        public int ActiveHoursEndMinute { get; set; }

        // Acknowledgment
        public bool RequireAcknowledgment { get; set; } = true;
        public int MaxRetries { get; set; } = 3;
        public int RetryIntervalMinutes { get; set; } = 5;

        // Runtime state
        public int CurrentRetryCount { get; set; }
        public bool IsWaitingForAcknowledgment { get; set; }
        public DateTime? NextRetry { get; set; }
        public DateTime? LastFired { get; set; }
        public DateTime Created { get; set; } = DateTime.Now;

        // Source tracking (not persisted to JSON)
        [JsonIgnore]
        public Guid SourceId { get; set; }

        [JsonIgnore]
        public string SourceName { get; set; } = string.Empty;



        public string RecurrenceSummary
        {
            get
            {
                return RecurrenceType switch
                {
                    RecurrenceType.Once => "One time",
                    RecurrenceType.EveryNMinutes => RecurrenceInterval == 1
                        ? "Every minute"
                        : $"Every {RecurrenceInterval} minutes",
                    RecurrenceType.Hourly => RecurrenceInterval == 1
                        ? "Every hour"
                        : $"Every {RecurrenceInterval} hours",
                    RecurrenceType.Daily => RecurrenceInterval == 1
                        ? "Daily"
                        : $"Every {RecurrenceInterval} days",
                    RecurrenceType.Weekly => RecurrenceInterval == 1
                        ? $"Weekly ({FormatDays(RecurrenceDays)})"
                        : $"Every {RecurrenceInterval} weeks ({FormatDays(RecurrenceDays)})",
                    RecurrenceType.Monthly => RecurrenceInterval == 1
                        ? $"Monthly (day {ReminderDayOfMonth})"
                        : $"Every {RecurrenceInterval} months (day {ReminderDayOfMonth})",
                    _ => "Unknown"
                };
            }
        }

        public string NextDueSummary
        {
            get
            {
                if (NextDue.HasValue == false)
                {
                    return IsWaitingForAcknowledgment ? "Waiting for acknowledgment" : "Not scheduled";
                }

                var diff = NextDue.Value - DateTime.Now;
                if (diff.TotalMinutes < 1)
                {
                    return "Due now";
                }
                if (diff.TotalHours < 1)
                {
                    return $"In {(int)diff.TotalMinutes}m";
                }
                if (diff.TotalDays < 1)
                {
                    return $"Today at {NextDue.Value:h:mm tt}";
                }
                if (diff.TotalDays < 2)
                {
                    return $"Tomorrow at {NextDue.Value:h:mm tt}";
                }
                return NextDue.Value.ToString("MMM d 'at' h:mm tt");
            }
        }

        private static string FormatDays(DaysOfWeek days)
        {
            if (days == DaysOfWeek.All)
            {
                return "All days";
            }
            if (days == DaysOfWeek.Weekdays)
            {
                return "Weekdays";
            }
            if (days == DaysOfWeek.Weekend)
            {
                return "Weekends";
            }

            var parts = new System.Collections.Generic.List<string>();
            if (days.HasFlag(DaysOfWeek.Sunday)) { parts.Add("Sun"); }
            if (days.HasFlag(DaysOfWeek.Monday)) { parts.Add("Mon"); }
            if (days.HasFlag(DaysOfWeek.Tuesday)) { parts.Add("Tue"); }
            if (days.HasFlag(DaysOfWeek.Wednesday)) { parts.Add("Wed"); }
            if (days.HasFlag(DaysOfWeek.Thursday)) { parts.Add("Thu"); }
            if (days.HasFlag(DaysOfWeek.Friday)) { parts.Add("Fri"); }
            if (days.HasFlag(DaysOfWeek.Saturday)) { parts.Add("Sat"); }
            return string.Join(", ", parts);
        }
    }
}