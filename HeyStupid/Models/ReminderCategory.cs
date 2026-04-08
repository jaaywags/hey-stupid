namespace HeyStupid.Models
{
    using System;

    public class ReminderCategory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public Guid? FolderId { get; set; }
    }
}