namespace HeyStupid.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using HeyStupid.Models;

    public class JsonReminderStore : IReminderStore
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Dictionary<Guid, SourceData> _sources = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public void SetSources(IEnumerable<ReminderSource> sources)
        {
            _sources.Clear();
            foreach (var source in sources)
            {
                _sources[source.Id] = new SourceData
                {
                    Source = source,
                    FilePath = Path.Combine(source.FolderPath, "reminders.json"),
                    Reminders = new List<Reminder>()
                };
            }
        }

        public List<ReminderSource> GetSources()
        {
            return _sources.Values.Select(s => s.Source).ToList();
        }

        public async Task LoadAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var entry in _sources.Values)
                {
                    if (File.Exists(entry.FilePath) == false)
                    {
                        entry.Reminders = new List<Reminder>();
                        continue;
                    }

                    var json = await File.ReadAllTextAsync(entry.FilePath).ConfigureAwait(false);
                    entry.Reminders = JsonSerializer.Deserialize<List<Reminder>>(json, JsonOptions)
                        ?? new List<Reminder>();

                    foreach (var reminder in entry.Reminders)
                    {
                        reminder.SourceId = entry.Source.Id;
                        reminder.SourceName = entry.Source.Name;
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public List<Reminder> GetAll()
        {
            _lock.Wait();
            try
            {
                return _sources.Values
                    .SelectMany(s => s.Reminders)
                    .ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public Reminder? GetById(Guid id)
        {
            _lock.Wait();
            try
            {
                return _sources.Values
                    .SelectMany(s => s.Reminders)
                    .FirstOrDefault(r => r.Id == id);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAsync(Reminder reminder)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Find which source this reminder belongs to
                var sourceData = FindSourceForReminder(reminder);
                if (sourceData == null)
                {
                    return;
                }

                var index = sourceData.Reminders.FindIndex(r => r.Id == reminder.Id);
                if (index >= 0)
                {
                    sourceData.Reminders[index] = reminder;
                }
                else
                {
                    sourceData.Reminders.Add(reminder);
                }

                await PersistSourceAsync(sourceData).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var entry in _sources.Values)
                {
                    var removed = entry.Reminders.RemoveAll(r => r.Id == id);
                    if (removed > 0)
                    {
                        await PersistSourceAsync(entry).ConfigureAwait(false);
                        break;
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private SourceData? FindSourceForReminder(Reminder reminder)
        {
            // Check if reminder already exists in a source
            foreach (var entry in _sources.Values)
            {
                if (entry.Reminders.Any(r => r.Id == reminder.Id))
                {
                    return entry;
                }
            }

            // New reminder — use the SourceId on the reminder
            if (_sources.TryGetValue(reminder.SourceId, out var target))
            {
                return target;
            }

            // Fallback to first non-readonly source
            return _sources.Values.FirstOrDefault();
        }

        private static async Task PersistSourceAsync(SourceData sourceData)
        {
            var directory = Path.GetDirectoryName(sourceData.FilePath);
            if (string.IsNullOrEmpty(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(sourceData.Reminders, JsonOptions);
            await File.WriteAllTextAsync(sourceData.FilePath, json).ConfigureAwait(false);
        }

        private sealed class SourceData
        {
            public ReminderSource Source { get; set; } = null!;
            public string FilePath { get; set; } = string.Empty;
            public List<Reminder> Reminders { get; set; } = new();
        }
    }
}