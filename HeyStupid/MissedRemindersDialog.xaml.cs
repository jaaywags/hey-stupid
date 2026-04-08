namespace HeyStupid
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Microsoft.UI.Xaml.Controls;

    public sealed partial class MissedRemindersDialog : ContentDialog
    {
        private readonly List<Reminder> _missedReminders;
        private readonly ReminderScheduler _scheduler;

        public MissedRemindersDialog(List<Reminder> missedReminders, ReminderScheduler scheduler)
        {
            _missedReminders = missedReminders;
            _scheduler = scheduler;
            InitializeComponent();
            MissedList.ItemsSource = _missedReminders;
        }

        private async void AcknowledgeAll_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                foreach (var reminder in _missedReminders)
                {
                    await _scheduler.AcknowledgeAsync(reminder.Id).ConfigureAwait(true);
                }
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}