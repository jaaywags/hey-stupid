namespace HeyStupid.Models
{
    using System;

    public class TimelineEntry
    {
        public Reminder Reminder { get; set; } = null!;
        public DateTime OccurrenceTime { get; set; }
        public string TimeText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public bool IsCollapsed { get; set; }
        public bool IsPastDue { get; set; }
    }
}