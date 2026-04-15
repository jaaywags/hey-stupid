namespace HeyStupid.Tests
{
    using System;
    using HeyStupid.Models;
    using HeyStupid.Services;
    using Xunit;

    /// <summary>
    /// Tests specifically targeting the acknowledgment → next-fire timing logic.
    /// The scenarios here mirror real-world ack behavior: on-time, slightly late,
    /// very late (past multiple occurrences), seconds carried forward, and active-hours
    /// clamping after a late ack.  All cases use an explicit "now" so the assertions
    /// are deterministic.
    /// </summary>
    public class AcknowledgmentTimingTests
    {
        private static Reminder EveryNMinutes(int interval, DateTime nextDue)
        {
            return new Reminder
            {
                RecurrenceType = RecurrenceType.EveryNMinutes,
                RecurrenceInterval = interval,
                NextDue = nextDue
            };
        }

        private static Reminder Hourly(int interval, DateTime nextDue)
        {
            return new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = interval,
                NextDue = nextDue
            };
        }

        // ---------------------------------------------------------------------
        // EveryNMinutes — on-time ack
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_AckImmediately_NextFireIsOneIntervalLater()
        {
            // Scheduled 4:49, acked 30 seconds after.
            // Expected: 4:49 + 3min = 4:52:00, seconds stripped.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 16, 49, 0));
            var now = new DateTime(2026, 4, 14, 16, 49, 30);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 16, 52, 0), next);
        }

        // ---------------------------------------------------------------------
        // EveryNMinutes — slightly late ack (still within first interval)
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_AckLate_ButWithinFirstInterval_SkipsNoOccurrences()
        {
            // Scheduled 4:49, acked at 4:51 (2min late). Next tick (4:52) is still future.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 16, 49, 0));
            var now = new DateTime(2026, 4, 14, 16, 51, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 16, 52, 0), next);
        }

        // ---------------------------------------------------------------------
        // EveryNMinutes — ack right on the next boundary (past == now)
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_AckExactlyAtNextBoundary_SkipsThatBoundary()
        {
            // Scheduled 4:49, acked at exactly 4:52:00. The strictly-greater-than
            // check should skip 4:52 and return 4:55.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 16, 49, 0));
            var now = new DateTime(2026, 4, 14, 16, 52, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 16, 55, 0), next);
        }

        // ---------------------------------------------------------------------
        // EveryNMinutes — very late ack, past multiple intervals
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_AckVeryLate_SkipsAllLapsedOccurrences()
        {
            // The exact scenario the user reported: 4:49 schedule, acked 10 minutes later.
            // Occurrences: 4:52, 4:55, 4:58, 5:01. First future one at 4:59 is 5:01.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 16, 49, 0));
            var now = new DateTime(2026, 4, 14, 16, 59, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 17, 1, 0), next);
        }

        [Fact]
        public void EveryNMinutes_AckAcrossHourBoundary_RemainsOnCadence()
        {
            // 4:49 → 4:52 → 4:55 → 4:58 → 5:01 → 5:04. Ack at 5:03.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 16, 49, 0));
            var now = new DateTime(2026, 4, 14, 17, 3, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 17, 4, 0), next);
        }

        // ---------------------------------------------------------------------
        // Seconds must be stripped from the produced NextDue
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_NextDueHasSeconds_ResultHasZeroSeconds()
        {
            // This is the real-world bug: the stored NextDue carried fractional seconds
            // from a previous ack.  The returned time must land on a clean minute.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 17, 2, 25, 660));
            var now = new DateTime(2026, 4, 14, 17, 2, 30);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.NotNull(next);
            Assert.Equal(0, next!.Value.Second);
            Assert.Equal(0, next.Value.Millisecond);
            // 17:02:25 + 3min = 17:05:25, truncated to 17:05:00 and > now → use directly.
            Assert.Equal(new DateTime(2026, 4, 14, 17, 5, 0), next);
        }

        [Fact]
        public void EveryNMinutes_NextDueHasSeconds_MultipleStepsStillCleanMinutes()
        {
            // 17:02:25 → (trunc +3) 17:05:00 → 17:08 → 17:11 → 17:14 → 17:17.
            // Ack at 17:13 — expect 17:14 (because chain starts from 17:02:25 trunc = 17:05).
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 17, 2, 25, 660));
            var now = new DateTime(2026, 4, 14, 17, 13, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 17, 14, 0), next);
        }

        // ---------------------------------------------------------------------
        // Hourly — very late ack (user's 9/12/3 scenario)
        // ---------------------------------------------------------------------

        [Fact]
        public void Hourly_AckVeryLate_StaysOnOriginalAnchor()
        {
            // Every 3 hours starting 9am. Previous fire 12:00, stuck waiting for ack
            // until 1:43pm.  Expected next: 3:00pm, NOT 4:43pm (which is what the bug did).
            var reminder = Hourly(3, new DateTime(2026, 4, 14, 12, 0, 0));
            var now = new DateTime(2026, 4, 14, 13, 43, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 15, 0, 0), next);
        }

        [Fact]
        public void Hourly_AckAcrossMultipleOccurrences_JumpsToFirstFutureOne()
        {
            // 9:00 → 12:00 → 15:00 → 18:00. Previous fire was 9:00, user acks at 5pm.
            var reminder = Hourly(3, new DateTime(2026, 4, 14, 9, 0, 0));
            var now = new DateTime(2026, 4, 14, 17, 0, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 18, 0, 0), next);
        }

        // ---------------------------------------------------------------------
        // Active hours — late ack pushes outside the window
        // ---------------------------------------------------------------------

        [Fact]
        public void Hourly_WithActiveHours_AckLateAtEndOfWindow_ClampsToNextDay()
        {
            // Every 3h, 9am-5pm.  Previous fire 3:00pm, acked at 3:30pm.
            // Next would be 6:00pm which is OUTSIDE the 9-5 window → clamp to next day 9am.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = 3,
                NextDue = new DateTime(2026, 4, 14, 15, 0, 0),
                ActiveHoursEnabled = true,
                ActiveHoursStartHour = 9,
                ActiveHoursEndHour = 17
            };
            var now = new DateTime(2026, 4, 14, 15, 30, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 9, 0, 0), next);
        }

        [Fact]
        public void Hourly_WithActiveHours_AckOnTime_StaysInWindow()
        {
            // Every 3h, 9am-5pm. Previous fire 12:00, acked 12:05.  Expect 3:00pm.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = 3,
                NextDue = new DateTime(2026, 4, 14, 12, 0, 0),
                ActiveHoursEnabled = true,
                ActiveHoursStartHour = 9,
                ActiveHoursEndHour = 17
            };
            var now = new DateTime(2026, 4, 14, 12, 5, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 15, 0, 0), next);
        }

        [Fact]
        public void EveryNMinutes_WithActiveHours_AckAfterWindowClose_JumpsToNextMorning()
        {
            // Every 15min, 9am-5pm. Previous fire 4:45pm, acked 5:10pm.
            // 4:45 → 5:00 (outside [9,17)) → clamp to next day 9:00.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.EveryNMinutes,
                RecurrenceInterval = 15,
                NextDue = new DateTime(2026, 4, 14, 16, 45, 0),
                ActiveHoursEnabled = true,
                ActiveHoursStartHour = 9,
                ActiveHoursEndHour = 17
            };
            var now = new DateTime(2026, 4, 14, 17, 10, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 9, 0, 0), next);
        }

        // ---------------------------------------------------------------------
        // Daily, Weekly, Monthly — ack-timing with longer cadences
        // ---------------------------------------------------------------------

        [Fact]
        public void Daily_AckLateSameDay_StillAdvancesByOneDay()
        {
            // Daily at 9am. NextDue = today 9am. User acks at 11am.
            // CalculateNextDue(Daily) adds interval days from the start date, landing tomorrow 9am.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Daily,
                RecurrenceInterval = 1,
                ReminderHour = 9,
                ReminderMinute = 0,
                NextDue = new DateTime(2026, 4, 14, 9, 0, 0)
            };
            var now = new DateTime(2026, 4, 14, 11, 0, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 9, 0, 0), next);
        }

        [Fact]
        public void Daily_AckNextDayLate_JumpsToCorrectFutureDay()
        {
            // Daily at 9am. Stuck waiting from yesterday, user acks today at 10am.
            // Yesterday 9am → today 9am (past) → tomorrow 9am (future).
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Daily,
                RecurrenceInterval = 1,
                ReminderHour = 9,
                ReminderMinute = 0,
                NextDue = new DateTime(2026, 4, 13, 9, 0, 0)
            };
            var now = new DateTime(2026, 4, 14, 10, 0, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 9, 0, 0), next);
        }

        // ---------------------------------------------------------------------
        // Once — ack clears the schedule (returns null)
        // ---------------------------------------------------------------------

        [Fact]
        public void Once_AckReturnsNull_NoFurtherOccurrence()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Once,
                NextDue = new DateTime(2026, 4, 14, 10, 0, 0)
            };
            var now = new DateTime(2026, 4, 14, 10, 5, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Null(next);
        }

        // ---------------------------------------------------------------------
        // Edge cases / safety
        // ---------------------------------------------------------------------

        [Fact]
        public void EveryNMinutes_NoNextDueSet_FallsBackToAdvancingFromNow()
        {
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.EveryNMinutes,
                RecurrenceInterval = 5,
                NextDue = null
            };
            var now = new DateTime(2026, 4, 14, 16, 49, 30);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            // From null, cursor starts at now; first tick = now+5min, truncated.
            Assert.Equal(new DateTime(2026, 4, 14, 16, 54, 0), next);
        }

        [Fact]
        public void EveryNMinutes_SmallIntervalVeryLateAck_DoesNotHangForever()
        {
            // 1-minute reminder, last fired a week ago. Safety cap should kick in and return.
            var reminder = EveryNMinutes(1, new DateTime(2026, 4, 7, 9, 0, 0));
            var now = new DateTime(2026, 4, 14, 9, 0, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            // We don't care exactly where it lands — just that it returns in reasonable time
            // (not hang, not null).  And the result must be in the future.
            Assert.NotNull(next);
            Assert.True(next!.Value > now);
        }

        // ---------------------------------------------------------------------
        // Full simulated cycle — mirrors the exact scheduler behavior
        // ---------------------------------------------------------------------

        [Fact]
        public void UserScenario_EveryThreeMinutesFrom9_AckAt919_ReturnsTwentyOne()
        {
            // Literal user scenario: every 3 min starting 9:00, reminder never acked,
            // user finally clicks ack at 9:19.  The expected next fire is 9:21, not 9:24.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 9, 0, 0));
            var now = new DateTime(2026, 4, 14, 9, 19, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 14, 9, 21, 0), next);
            Assert.NotEqual(new DateTime(2026, 4, 14, 9, 24, 0), next);
        }

        [Fact]
        public void UserScenario_NextDueHasFractionalSeconds_AckAt919_StillReturns921()
        {
            // Same scenario but with the fractional seconds the user's JSON actually stores
            // (e.g., Created at 9:00:00.4567 means NextDue may have sub-second precision).
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 14, 9, 0, 0, 456).AddTicks(7000));
            var now = new DateTime(2026, 4, 14, 9, 19, 0);

            var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);

            // First step truncates seconds: 9:00:00.456 + 3min = 9:03:00.456 → 9:03:00.
            // Subsequent: 9:06, 9:09, 9:12, 9:15, 9:18, 9:21.  9:21 > 9:19 → return.
            Assert.Equal(new DateTime(2026, 4, 14, 9, 21, 0), next);
        }

        [Fact]
        public void FullCycle_FireWaitAckRepeat_StaysOnOriginalCadence()
        {
            // Simulate: reminder fires at 9:00, user acks at 9:07, fires at 12:00 (should),
            // acks at 1:43, fires at 15:00 (should), acks at 15:05, fires at 18:00 (should).
            var reminder = Hourly(3, new DateTime(2026, 4, 14, 9, 0, 0));

            // First ack — slightly late at 9:07
            reminder.NextDue = RecurrenceCalculator.CalculateNextFutureDue(reminder, new DateTime(2026, 4, 14, 9, 7, 0));
            Assert.Equal(new DateTime(2026, 4, 14, 12, 0, 0), reminder.NextDue);

            // Second ack — very late at 1:43pm
            reminder.NextDue = RecurrenceCalculator.CalculateNextFutureDue(reminder, new DateTime(2026, 4, 14, 13, 43, 0));
            Assert.Equal(new DateTime(2026, 4, 14, 15, 0, 0), reminder.NextDue);

            // Third ack — on time at 3:05pm
            reminder.NextDue = RecurrenceCalculator.CalculateNextFutureDue(reminder, new DateTime(2026, 4, 14, 15, 5, 0));
            Assert.Equal(new DateTime(2026, 4, 14, 18, 0, 0), reminder.NextDue);
        }

        // ---------------------------------------------------------------------
        // CalculateMostRecentPastDue — used by the scheduler to unstick reminders
        // whose popup was closed without acknowledgment.
        // ---------------------------------------------------------------------

        [Fact]
        public void MostRecentPastDue_WithinFirstInterval_ReturnsNull()
        {
            // NextDue is 9:45, now is 9:46 — next occurrence (9:48) is still future.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 15, 9, 45, 0));
            var now = new DateTime(2026, 4, 15, 9, 46, 0);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            Assert.Null(result);
        }

        [Fact]
        public void MostRecentPastDue_OneIntervalPast_ReturnsThatOccurrence()
        {
            // Stuck at 9:45, now is 9:48:30 — the 9:48 occurrence has just come due.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 15, 9, 45, 0));
            var now = new DateTime(2026, 4, 15, 9, 48, 30);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 9, 48, 0), result);
        }

        [Fact]
        public void MostRecentPastDue_ManyIntervalsPast_ReturnsMostRecent()
        {
            // User scenario: stuck at 9:45, comes back at 11:01 (1h 16m later).
            // Occurrences: 9:48, 9:51, ..., 10:57, 11:00.  Most recent <= 11:01 is 11:00.
            var reminder = EveryNMinutes(3, new DateTime(2026, 4, 15, 9, 45, 0));
            var now = new DateTime(2026, 4, 15, 11, 1, 0);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            Assert.Equal(new DateTime(2026, 4, 15, 11, 0, 0), result);
        }

        [Fact]
        public void MostRecentPastDue_OnceReminder_ReturnsNull()
        {
            // Once reminders have no "next occurrence" to advance to.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Once,
                NextDue = new DateTime(2026, 4, 15, 9, 0, 0)
            };
            var now = new DateTime(2026, 4, 15, 11, 0, 0);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            Assert.Null(result);
        }

        [Fact]
        public void MostRecentPastDue_Hourly_LongAway_ReturnsMostRecent()
        {
            // Every 3h from 9:00.  User gone for a day — should find the latest occurrence.
            var reminder = Hourly(3, new DateTime(2026, 4, 14, 9, 0, 0));
            var now = new DateTime(2026, 4, 15, 10, 30, 0);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            // Chain: 12, 15, 18, 21, 00, 03, 06, 09 (next day).  10:30 → most recent is 9:00.
            Assert.Equal(new DateTime(2026, 4, 15, 9, 0, 0), result);
        }

        [Fact]
        public void MostRecentPastDue_Hourly_WithActiveHours_RespectsWindow()
        {
            // Every 3h 9am-5pm. Stuck at 3pm, now 8pm. Next occurrence (6pm) is clamped
            // to next morning 9am, which is after now → no newer occurrence yet.
            var reminder = new Reminder
            {
                RecurrenceType = RecurrenceType.Hourly,
                RecurrenceInterval = 3,
                NextDue = new DateTime(2026, 4, 15, 15, 0, 0),
                ActiveHoursEnabled = true,
                ActiveHoursStartHour = 9,
                ActiveHoursEndHour = 17
            };
            var now = new DateTime(2026, 4, 15, 20, 0, 0);

            var result = RecurrenceCalculator.CalculateMostRecentPastDue(reminder, now);

            Assert.Null(result);
        }

        [Fact]
        public void FullCycle_EveryThreeMinutes_CleanMinutesPreservedAcrossManyCycles()
        {
            // Start at 4:49. Simulate 10 ack cycles with varying lateness.
            // Every resulting NextDue must land on a clean minute that's a multiple
            // of 3 minutes from 4:49.
            var start = new DateTime(2026, 4, 14, 16, 49, 0);
            var reminder = EveryNMinutes(3, start);

            for (int cycle = 0; cycle < 10; cycle++)
            {
                // Ack a random-ish amount late each time (0, 30, 47, 60, 90, 120... seconds)
                var ackDelay = TimeSpan.FromSeconds((cycle * 17) % 90);
                var now = reminder.NextDue!.Value + ackDelay;

                var next = RecurrenceCalculator.CalculateNextFutureDue(reminder, now);
                Assert.NotNull(next);

                // Must be future
                Assert.True(next!.Value > now);

                // Must have zero seconds (user's requirement)
                Assert.Equal(0, next.Value.Second);
                Assert.Equal(0, next.Value.Millisecond);

                // Must be aligned to the 3-minute grid anchored at 4:49
                var minutesFromStart = (int)(next.Value - start).TotalMinutes;
                Assert.Equal(0, minutesFromStart % 3);

                reminder.NextDue = next;
            }
        }
    }
}