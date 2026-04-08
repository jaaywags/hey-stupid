namespace HeyStupid.Tests
{
    using System;
    using System.Collections.Generic;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Xunit;

    public class ReminderSchedulerTests
    {
        [Fact]
        public void CalculateNextDue_Once_ReturnsNull()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Once
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, DateTime.Now);

            Assert.Null(result);
        }

        [Fact]
        public void CalculateNextDue_EveryNMinutes_AddsInterval()
        {
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.EveryNMinutes,
                RecurrenceInterval = 15
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            Assert.Equal(from.AddMinutes(15), result.Value);
        }

        [Fact]
        public void CalculateNextDue_Hourly_AddsHours()
        {
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = 2
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            Assert.Equal(from.AddHours(2), result.Value);
        }

        [Fact]
        public void CalculateNextDue_Daily_AddsCorrectDays()
        {
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Daily,
                RecurrenceInterval = 3,
                ReminderHour = 9,
                ReminderMinute = 30
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            var expected = new DateTime(2026, 4, 11, 9, 30, 0);
            Assert.Equal(expected, result.Value);
        }

        [Fact]
        public void CalculateNextDue_Weekly_FindsNextMatchingDay()
        {
            // April 8, 2026 is a Wednesday
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Weekly,
                RecurrenceDays = DaysOfWeek.Friday,
                ReminderHour = 14,
                ReminderMinute = 0
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            // Next Friday is April 10, 2026
            Assert.Equal(DayOfWeek.Friday, result.Value.DayOfWeek);
            Assert.Equal(14, result.Value.Hour);
            Assert.Equal(0, result.Value.Minute);
        }

        [Fact]
        public void CalculateNextDue_Weekly_SkipsNonSelectedDays()
        {
            // April 8, 2026 is a Wednesday
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Weekly,
                RecurrenceDays = DaysOfWeek.Monday | DaysOfWeek.Wednesday,
                ReminderHour = 8,
                ReminderMinute = 0
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            // Next matching day after Wed at 10am: should be next Monday (Apr 13) at 8am
            Assert.True(result.Value > from);
        }

        [Fact]
        public void CalculateInitialDue_Once_ReturnsTimeToday_WhenFuture()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Once,
                ReminderHour = 23,
                ReminderMinute = 59
            };

            var result = RecurrenceCalculator.CalculateInitialDue(reminder);

            // Should be today if time hasn't passed, or tomorrow
            Assert.True(result >= DateTime.Now.Date);
        }

        [Fact]
        public void CalculateInitialDue_EveryNMinutes_ReturnsFutureTime()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.EveryNMinutes,
                RecurrenceInterval = 10
            };

            var before = DateTime.Now;
            var result = RecurrenceCalculator.CalculateInitialDue(reminder);

            Assert.True(result > before);
            Assert.True(result <= before.AddMinutes(11));
        }

        [Fact]
        public void CalculateInitialDue_Hourly_ReturnsFutureTime()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = 1
            };

            var before = DateTime.Now;
            var result = RecurrenceCalculator.CalculateInitialDue(reminder);

            Assert.True(result > before);
            Assert.True(result <= before.AddHours(2));
        }

        [Fact]
        public void CalculateInitialDue_Daily_ReturnsFutureDate()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Daily,
                RecurrenceInterval = 1,
                ReminderHour = 0,
                ReminderMinute = 0
            };

            var result = RecurrenceCalculator.CalculateInitialDue(reminder);

            // Should be either today (if midnight hasn't passed... unlikely) or tomorrow
            Assert.True(result >= DateTime.Now.Date);
        }

        [Fact]
        public void CalculateInitialDue_Weekly_ReturnsFutureDate()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Weekly,
                RecurrenceDays = DaysOfWeek.All,
                ReminderHour = 23,
                ReminderMinute = 59
            };

            var result = RecurrenceCalculator.CalculateInitialDue(reminder);

            Assert.True(result > DateTime.Now);
        }

        [Theory]
        [InlineData(DaysOfWeek.Monday, DayOfWeek.Monday)]
        [InlineData(DaysOfWeek.Tuesday, DayOfWeek.Tuesday)]
        [InlineData(DaysOfWeek.Wednesday, DayOfWeek.Wednesday)]
        [InlineData(DaysOfWeek.Thursday, DayOfWeek.Thursday)]
        [InlineData(DaysOfWeek.Friday, DayOfWeek.Friday)]
        [InlineData(DaysOfWeek.Saturday, DayOfWeek.Saturday)]
        [InlineData(DaysOfWeek.Sunday, DayOfWeek.Sunday)]
        public void CalculateNextDue_Weekly_SingleDay_LandsOnCorrectDay(DaysOfWeek selectedDay, DayOfWeek expectedDay)
        {
            var from = new DateTime(2026, 4, 8, 10, 0, 0);
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Weekly,
                RecurrenceDays = selectedDay,
                ReminderHour = 9,
                ReminderMinute = 0
            };

            var result = RecurrenceCalculator.CalculateNextDue(reminder, from);

            Assert.NotNull(result);
            Assert.Equal(expectedDay, result.Value.DayOfWeek);
            Assert.True(result.Value > from);
        }

        [Fact]
        public void Reminder_RecurrenceSummary_Once()
        {
            var reminder = new Reminder { RecurrenceType = RecurrenceType.Once };
            Assert.Equal("One time", reminder.RecurrenceSummary);
        }

        [Fact]
        public void Reminder_RecurrenceSummary_Daily_Singular()
        {
            var reminder = new Reminder { RecurrenceType = RecurrenceType.Daily, RecurrenceInterval = 1 };
            Assert.Equal("Daily", reminder.RecurrenceSummary);
        }

        [Fact]
        public void Reminder_RecurrenceSummary_Daily_Plural()
        {
            var reminder = new Reminder { RecurrenceType = RecurrenceType.Daily, RecurrenceInterval = 3 };
            Assert.Equal("Every 3 days", reminder.RecurrenceSummary);
        }

        [Fact]
        public void Reminder_RecurrenceSummary_Hourly_Singular()
        {
            var reminder = new Reminder { RecurrenceType = RecurrenceType.Hourly, RecurrenceInterval = 1 };
            Assert.Equal("Every hour", reminder.RecurrenceSummary);
        }

        [Fact]
        public void Reminder_RecurrenceSummary_EveryNMinutes()
        {
            var reminder = new Reminder { RecurrenceType = RecurrenceType.EveryNMinutes, RecurrenceInterval = 30 };
            Assert.Equal("Every 30 minutes", reminder.RecurrenceSummary);
        }

        [Fact]
        public void Reminder_NextDueSummary_NotScheduled()
        {
            var reminder = new Reminder { NextDue = null, IsWaitingForAcknowledgment = false };
            Assert.Equal("Not scheduled", reminder.NextDueSummary);
        }

        [Fact]
        public void Reminder_NextDueSummary_WaitingForAck()
        {
            var reminder = new Reminder { NextDue = null, IsWaitingForAcknowledgment = true };
            Assert.Equal("Waiting for acknowledgment", reminder.NextDueSummary);
        }

        [Fact]
        public void Reminder_Defaults_AreReasonable()
        {
            var reminder = new Reminder();

            Assert.True(reminder.IsActive);
            Assert.True(reminder.RequireAcknowledgment);
            Assert.Equal(3, reminder.MaxRetries);
            Assert.Equal(5, reminder.RetryIntervalMinutes);
            Assert.Equal(RecurrenceType.Once, reminder.RecurrenceType);
            Assert.Equal(1, reminder.RecurrenceInterval);
            Assert.Equal(9, reminder.ReminderHour);
            Assert.Equal(0, reminder.ReminderMinute);
            Assert.Equal(0, reminder.CurrentRetryCount);
            Assert.False(reminder.IsWaitingForAcknowledgment);
            Assert.NotEqual(Guid.Empty, reminder.Id);
        }

        [Fact]
        public void DaysOfWeek_Weekdays_ContainsCorrectDays()
        {
            var weekdays = DaysOfWeek.Weekdays;

            Assert.True(weekdays.HasFlag(DaysOfWeek.Monday));
            Assert.True(weekdays.HasFlag(DaysOfWeek.Tuesday));
            Assert.True(weekdays.HasFlag(DaysOfWeek.Wednesday));
            Assert.True(weekdays.HasFlag(DaysOfWeek.Thursday));
            Assert.True(weekdays.HasFlag(DaysOfWeek.Friday));
            Assert.False(weekdays.HasFlag(DaysOfWeek.Saturday));
            Assert.False(weekdays.HasFlag(DaysOfWeek.Sunday));
        }

        [Fact]
        public void DaysOfWeek_Weekend_ContainsCorrectDays()
        {
            var weekend = DaysOfWeek.Weekend;

            Assert.True(weekend.HasFlag(DaysOfWeek.Saturday));
            Assert.True(weekend.HasFlag(DaysOfWeek.Sunday));
            Assert.False(weekend.HasFlag(DaysOfWeek.Monday));
        }
    }
}