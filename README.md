# Hey Stupid

A persistent, no-nonsense reminder app for Windows that won't let you forget things. Built with WinUI 3 and .NET 10.

<img width="706" height="593" alt="image" src="https://github.com/user-attachments/assets/92fc8cef-7fb4-4fc1-848f-c63616969866" />

## Features

- **Recurring reminders** — one-time, every N minutes/hours/days/weeks/months, with day-of-week and day-of-month support
- **Must-acknowledge mode** — configurable nag count and interval. If you don't click "I Got It", it keeps coming back
- **Always-on-top popups** — reminder windows demand your attention, centered on screen and pinned above everything else
- **Missed reminders on startup** — if the app wasn't running when reminders were due, you get a summary of everything you missed
- **Categories** — organize reminders by category (e.g., Sprint Tasks, Compliance, Meetings) and assign categories to storage folders
- **Multi-folder storage** — store reminders in multiple folders. Point them at OneDrive for automatic backup and cross-machine sync
- **System tray** — runs quietly in the background. No popups, no notifications, completely silent until a reminder fires
- **Start with Windows** — optional auto-start so you never miss a reminder

## Getting Started

### Prerequisites

- Windows 10 (1809+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022/18+ with the **Desktop development with C++** workload (required for Windows App SDK native compilation)

### Build & Run

```
git clone <repo-url>
cd HeyStupid
dotnet restore
dotnet run --project HeyStupid
```

Or open `HeyStupid.sln` in Visual Studio and hit F5.

### First Launch

1. The app starts with a default "Personal" storage folder in `%LOCALAPPDATA%\HeyStupid\data`
2. Open **Settings** to point the storage folder at a OneDrive directory for backup
3. Create categories and assign them to folders if you want to organize reminders by team or topic
4. Click **New Reminder** to create your first reminder

## How It Works

### Reminders

Each reminder has:
- A **title** and optional **message**
- A **recurrence schedule** (once, every N minutes/hours/days/weeks/months)
- A **start date and time** — pick exactly when the first occurrence fires
- An **acknowledgment setting** — require acknowledgment with configurable retry count and interval

### Acknowledgment Flow

1. Reminder comes due — an always-on-top popup appears with "I Got It"
2. Click "I Got It" — acknowledged, next occurrence is scheduled
3. Ignore it — the app nags you again after N minutes, up to N times
4. You can also acknowledge directly from the main window's reminder list

### Categories & Folders

- **Folders** are physical directories where `reminders.json` files are stored
- **Categories** are labels you assign to reminders
- Each category maps to a folder — when you save a reminder with a category, it goes to that category's folder
- Uncategorized reminders go to the **default folder**
- Share a folder via OneDrive to sync reminders across machines or with teammates

## Auto-Versioning

The project uses [MinVer](https://github.com/adamralph/minver) for automatic versioning based on git tags.

```
git tag v1.0.0
git push origin v1.0.0
```

This triggers the GitHub Actions release workflow.

## CI/CD

The GitHub Actions workflow (`.github/workflows/release.yml`) runs on tag push:

1. **Test** — runs all unit tests
2. **Build MSI** — publishes a self-contained x64 build and packages it with WiX
3. **Release** — creates a GitHub release with the MSI installer attached

Tags containing `-` (e.g., `v1.0.0-beta`) are marked as pre-releases.

## Project Structure

```
HeyStupid/
  Models/               Data models (Reminder, AppSettings, ReminderSource, ReminderCategory)
  Services/             Business logic (JsonReminderStore, ReminderScheduler, SettingsService)
  MainWindow            Reminder list with add/edit/delete/toggle/acknowledge
  ReminderEditDialog    Full editor for reminders (recurrence, date/time, categories, ack settings)
  ReminderPopupWindow   Always-on-top "HEY STUPID!" acknowledgment popup
  MissedRemindersDialog Startup dialog showing all missed reminders
  SettingsWindow        Folder management, categories, startup toggle, default nag settings
  App.xaml.cs           Application entry point, tray icon, popup management

HeyStupid.Tests/        Unit tests for scheduler recurrence logic

installer/              WiX MSI installer source
.github/workflows/      CI/CD pipeline
```

## License

This project is licensed under the GNU General Public License v3.0 — see [LICENSE](LICENSE) for details.
