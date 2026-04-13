namespace HeyStupid.Models
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class AppSettings
    {
        private static readonly string DefaultStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeyStupid",
            "data");

        public List<ReminderSource> ReminderSources { get; set; } = new();
        public List<ReminderCategory> Categories { get; set; } = new();
        public bool StartWithWindows { get; set; } = true;
        public int DefaultMaxRetries { get; set; } = 3;
        public int DefaultRetryIntervalMinutes { get; set; } = 5;
        public HomeView HomeView { get; set; } = HomeView.List;

        public void EnsureDefaultSource()
        {
            if (ReminderSources.Count == 0)
            {
                ReminderSources.Add(new ReminderSource
                {
                    Name = "Personal",
                    FolderPath = DefaultStoragePath,
                    IsDefault = true
                });
            }

            if (ReminderSources.Exists(s => s.IsDefault) == false)
            {
                ReminderSources[0].IsDefault = true;
            }
        }

        public ReminderSource GetDefaultSource()
        {
            return ReminderSources.Find(s => s.IsDefault) ?? ReminderSources[0];
        }

        public Guid? GetFolderForCategory(Guid categoryId)
        {
            var category = Categories.Find(c => c.Id == categoryId);
            return category?.FolderId;
        }
    }
}