namespace HeyStupid.Models
{
    using System;

    public class ReminderSource
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }
}