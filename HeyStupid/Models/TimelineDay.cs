namespace HeyStupid.Models
{
    using System;
    using System.Collections.Generic;

    public class TimelineDay
    {
        public DateTime Date { get; set; }
        public string Header { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool IsToday { get; set; }
        public List<TimelineEntry> PastDueEntries { get; set; } = new();
        public List<TimelineEntry> Entries { get; set; } = new();

        public bool HasPastDue => PastDueEntries.Count > 0;
        public bool HasEntries => Entries.Count > 0;
        public bool IsEmpty => HasPastDue == false && HasEntries == false;
    }
}