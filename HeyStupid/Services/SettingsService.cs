namespace HeyStupid.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using HeyStupid.Models;
    using Microsoft.Win32;

    public class SettingsService
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeyStupid");

        private static readonly string SettingsFilePath = Path.Combine(
            SettingsDirectory,
            "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "HeyStupid";

        private AppSettings _settings = new();

        public AppSettings Settings => _settings;

        public async Task LoadAsync()
        {
            if (File.Exists(SettingsFilePath) == false)
            {
                _settings = new AppSettings();
                _settings.EnsureDefaultSource();
                await SaveAsync().ConfigureAwait(false);
                return;
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            _settings.EnsureDefaultSource();
        }

        public async Task SaveAsync()
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            await File.WriteAllTextAsync(SettingsFilePath, json).ConfigureAwait(false);
        }

        public async Task AddSourceAsync(ReminderSource source)
        {
            _settings.ReminderSources.Add(source);
            await SaveAsync().ConfigureAwait(false);
        }

        public async Task RemoveSourceAsync(Guid sourceId)
        {
            _settings.ReminderSources.RemoveAll(s => s.Id == sourceId);
            _settings.EnsureDefaultSource();
            await SaveAsync().ConfigureAwait(false);
        }

        public async Task UpdateSourceAsync(ReminderSource source)
        {
            var index = _settings.ReminderSources.FindIndex(s => s.Id == source.Id);
            if (index >= 0)
            {
                _settings.ReminderSources[index] = source;
                await SaveAsync().ConfigureAwait(false);
            }
        }

        public void ApplyStartupSetting()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
            {
                if (key == null)
                {
                    return;
                }

                if (_settings.StartWithWindows)
                {
                    var exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath) == false)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }

        public async Task SetStartWithWindowsAsync(bool enabled)
        {
            _settings.StartWithWindows = enabled;
            ApplyStartupSetting();
            await SaveAsync().ConfigureAwait(false);
        }
    }
}