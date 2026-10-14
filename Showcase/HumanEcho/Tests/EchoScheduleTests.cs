using NUnit.Framework;

namespace HumanEcho.Tests {
    /// <summary>
    /// EditMode tests for the echo ring's schedule: when a mask is captured, which ring slot holds the mask
    /// of a given age, how many
    /// captures exist, and when a history is too old to be an echo. Pure logic: no GPU, no scene, no Unity
    /// object is created.
    /// Parameterless [Test] methods only (the self-test runner of this assembly does not run [TestCase]).
    /// </summary>
    public sealed class EchoScheduleTests {
        private const float Spacing = 0.14f;
        private const float MaskInterval = 0.055f;

        private static EchoSchedule NewSchedule() {
            return new EchoSchedule(EchoSchedule.DefaultCapacity);
        }

        private static void CaptureMany(EchoSchedule schedule, int count) {
            for (int i = 0; i < count; i++) {
                schedule.Capture((float)i);
            }
        }

        // A tiny deterministic generator (a 32-bit linear congruential one): the same jitter on every machine
        // and every run.
        private static float NextUnit(ref uint state) {
            state = unchecked(state * 1664525u + 1013904223u);
            return (float)(state >> 8) / 16777216f;
        }

        // ------------------------------------------------------------------ a new schedule

        [Test]
        public void New_HasNoCapturesAndNoNewestSlot() {
            EchoSchedule schedule = NewSchedule();
            Assert.AreEqual(0, schedule.ValidCount);
            Assert.AreEqual(-1, schedule.Head);
            Assert.AreEqual(-1, schedule.SlotForAge(0));
            Assert.IsFalse(schedule.IsValidAge(0));
            Assert.AreEqual(EchoSchedule.DefaultCapacity, schedule.Capacity);
        }

        [Test]
        public void DefaultCapacity_ReachesTheOldestCopysAge() {
            // Copy k shows age k + 1; the sixteenth copy shows age 16, so the ring needs 17 slots (age 0 is
            // the live pose).
            Assert.AreEqual(17, EchoSchedule.DefaultCapacity);
            int oldestAge = EchoSchedule.AgeOfCopy(EchoSchedule.MaxCopies - 1);
            Assert.AreEqual(16, oldestAge);
            Assert.Less(oldestAge, EchoSchedule.DefaultCapacity);
        }

        [Test]
        public void AgeOfCopy_SkipsTheLiveAge() {
            Assert.AreEqual(1, EchoSchedule.AgeOfCopy(0));
            Assert.AreEqual(5, EchoSchedule.AgeOfCopy(4));
        }

        [Test]
        public void ClampCopies_KeepsOneToSixteen() {
            Assert.AreEqual(1, EchoSchedule.ClampCopies(-5));
            Assert.AreEqual(1, EchoSchedule.ClampCopies(0));
            Assert.AreEqual(1, EchoSchedule.ClampCopies(1));
            Assert.AreEqual(8, EchoSchedule.ClampCopies(8));
            Assert.AreEqual(16, EchoSchedule.ClampCopies(16));
            Assert.AreEqual(16, EchoSchedule.ClampCopies(17));
            Assert.AreEqual(16, EchoSchedule.ClampCopies(99));
        }

        // ------------------------------------------------------------------ when to capture

        [Test]
        public void FirstNewMask_AlwaysCapturesWhateverTheClockSays() {
            EchoSchedule schedule = NewSchedule();
            Assert.IsTrue(schedule.ShouldCapture(true, 0.01f, Spacing, MaskInterval));
            Assert.IsTrue(schedule.ShouldCapture(true, 12.5f, Spacing, MaskInterval));
        }

        [Test]
        public void NoNewMask_NeverCaptures() {
            EchoSchedule schedule = NewSchedule();
            Assert.IsFalse(schedule.ShouldCapture(false, 5f, Spacing, MaskInterval), "nothing to capture before any mask");
            schedule.Capture(1f);
            Assert.IsFalse(schedule.ShouldCapture(false, 100f, Spacing, MaskInterval), "an old frame is not captured twice");
        }

        [Test]
        public void Capture_WaitsForTheSpacingMinusHalfAMaskInterval() {
            // 0.14 s spacing, 0.055 s mask interval: the next capture is due 0.1125 s after the last one.
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(1f);
            Assert.IsFalse(schedule.ShouldCapture(true, 1.055f, Spacing, MaskInterval), "one mask later is far too early");
            Assert.IsFalse(schedule.ShouldCapture(true, 1.10f, Spacing, MaskInterval), "0.10 s is still short of 0.1125 s");
            Assert.IsTrue(schedule.ShouldCapture(true, 1.12f, Spacing, MaskInterval), "0.12 s is past 0.1125 s: do not wait for 0.14 s");
            Assert.IsTrue(schedule.ShouldCapture(true, 1.30f, Spacing, MaskInterval), "a late mask captures too");
        }

        [Test]
        public void Capture_JitteryMaskTimingDoesNotSkipSlots() {
            // The spacing is exactly two mask intervals, but every mask arrives up to 10 ms early or late.
            // With the half-interval
            // allowance every capture is two masks after the previous one (never three), so no slot's worth
            // of time is skipped.
            EchoSchedule schedule = NewSchedule();
            const float spacing = 0.11f;
            const float interval = 0.055f;
            uint state = 12345u;
            float now = 0f;
            float lastCapture = -1f;
            float maxGap = 0f;
            float gapSum = 0f;
            int gaps = 0;
            for (int i = 0; i < 400; i++) {
                now += interval + (NextUnit(ref state) - 0.5f) * 0.02f;
                if (!schedule.ShouldCapture(true, now, spacing, interval)) {
                    continue;
                }
                if (lastCapture >= 0f) {
                    float gap = now - lastCapture;
                    maxGap = UnityEngine.Mathf.Max(maxGap, gap);
                    gapSum += gap;
                    gaps++;
                }
                schedule.Capture(now);
                lastCapture = now;
            }
            Assert.Greater(gaps, 150, "about every second mask is captured");
            Assert.LessOrEqual(maxGap, 0.1301f, "no gap is longer than two (jittered) mask intervals");
            Assert.AreEqual(spacing, gapSum / gaps, 0.01f, "the average spacing is the configured one");
        }

        [Test]
        public void Capture_SpacingShorterThanTheMaskIntervalTakesEveryMask() {
            EchoSchedule schedule = NewSchedule();
            float now = 3f;
            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(schedule.ShouldCapture(true, now, 0.02f, MaskInterval), "mask " + i);
                schedule.Capture(now);
                now += MaskInterval;
            }
            Assert.AreEqual(10, schedule.ValidCount);
        }

        [Test]
        public void Capture_ABackwardsClockCapturesInsteadOfWaitingForever() {
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(100f);
            Assert.IsTrue(schedule.ShouldCapture(true, 1f, Spacing, MaskInterval));
        }

        [Test]
        public void Capture_NonsenseIntervalsCannotBlockIt() {
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(1f);
            // NaN is treated as "no interval": the full spacing is needed, and it is enough.
            Assert.IsFalse(schedule.ShouldCapture(true, 1.05f, Spacing, float.NaN));
            Assert.IsTrue(schedule.ShouldCapture(true, 1.20f, Spacing, float.NaN), "a NaN interval must not stop captures");
            // A huge interval (a stalled stream) drives the needed time to zero, never below: the next mask
            // is taken.
            Assert.IsTrue(schedule.ShouldCapture(true, 1.001f, Spacing, 10f));
            // A zero or negative spacing means "every mask".
            Assert.IsTrue(schedule.ShouldCapture(true, 1.001f, 0f, MaskInterval));
            Assert.IsTrue(schedule.ShouldCapture(true, 1.001f, -1f, MaskInterval));
        }

        [Test]
        public void Capture_TimeZeroIsAnOrdinaryCaptureTime() {
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(0f);
            Assert.IsFalse(schedule.ShouldCapture(true, 0.05f, Spacing, MaskInterval));
            Assert.IsTrue(schedule.ShouldCapture(true, 0.2f, Spacing, MaskInterval));
        }

        // ------------------------------------------------------------------ slots and ages

        [Test]
        public void Capture_BooksSlotsInOrderFromZero() {
            EchoSchedule schedule = NewSchedule();
            Assert.AreEqual(0, schedule.Capture(0.5f));
            Assert.AreEqual(1, schedule.Capture(0.7f));
            Assert.AreEqual(2, schedule.Capture(0.9f));
            Assert.AreEqual(2, schedule.Head);
            Assert.AreEqual(3, schedule.ValidCount);
            Assert.AreEqual(0.9f, schedule.LastCaptureTime, 1e-6f);
        }

        [Test]
        public void SlotForAge_NewestIsAgeZeroAndOlderAgesCountBack() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 5);
            Assert.AreEqual(4, schedule.SlotForAge(0));
            Assert.AreEqual(3, schedule.SlotForAge(1));
            Assert.AreEqual(2, schedule.SlotForAge(2));
            Assert.AreEqual(1, schedule.SlotForAge(3));
            Assert.AreEqual(0, schedule.SlotForAge(4));
        }

        [Test]
        public void SlotForAge_AgesThatDoNotExistYetAreSkipped() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 3);
            Assert.IsTrue(schedule.IsValidAge(0));
            Assert.IsTrue(schedule.IsValidAge(2));
            Assert.IsFalse(schedule.IsValidAge(3));
            Assert.AreEqual(-1, schedule.SlotForAge(3));
            Assert.AreEqual(-1, schedule.SlotForAge(16));
            Assert.IsFalse(schedule.IsValidAge(-1));
            Assert.AreEqual(-1, schedule.SlotForAge(-1));
        }

        [Test]
        public void ValidCount_StopsAtCapacity() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 16);
            Assert.AreEqual(16, schedule.ValidCount);
            CaptureMany(schedule, 17);
            Assert.AreEqual(17, schedule.ValidCount);
            CaptureMany(schedule, 40);
            Assert.AreEqual(17, schedule.ValidCount);
            Assert.IsFalse(schedule.IsValidAge(17), "the ring has no age beyond its capacity");
            Assert.AreEqual(-1, schedule.SlotForAge(17));
        }

        [Test]
        public void Ring_WrapsAroundAndKeepsAgesInOrder() {
            // 20 captures into 17 slots: the writes went to slots 0..16, then 0, 1, 2 again, so the newest is
            // in slot 2.
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 20);
            Assert.AreEqual(2, schedule.Head);
            Assert.AreEqual(2, schedule.SlotForAge(0));
            Assert.AreEqual(1, schedule.SlotForAge(1));
            Assert.AreEqual(0, schedule.SlotForAge(2));
            Assert.AreEqual(16, schedule.SlotForAge(3), "age 3 wrapped below slot 0 back to the last slot");
            Assert.AreEqual(15, schedule.SlotForAge(4));
            Assert.AreEqual(3, schedule.SlotForAge(16), "the oldest age sits in the slot the next capture overwrites");
        }

        [Test]
        public void Ring_EveryAgeMapsToItsOwnSlotAfterManyLaps() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 17 * 5 + 4);
            Assert.AreEqual(3, schedule.Head, "the head wraps with the capacity, it does not just count up");
            bool[] used = new bool[schedule.Capacity];
            for (int age = 0; age < schedule.Capacity; age++) {
                int slot = schedule.SlotForAge(age);
                Assert.GreaterOrEqual(slot, 0, "age " + age);
                Assert.Less(slot, schedule.Capacity, "age " + age);
                Assert.IsFalse(used[slot], "two ages share slot " + slot);
                used[slot] = true;
            }
        }

        [Test]
        public void Ring_ANewCaptureShiftsEveryAgeByOne() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 20);
            int before0 = schedule.SlotForAge(0);
            int before1 = schedule.SlotForAge(1);
            int before9 = schedule.SlotForAge(9);
            schedule.Capture(50f);
            Assert.AreEqual(before0, schedule.SlotForAge(1), "what was newest is now one older");
            Assert.AreEqual(before1, schedule.SlotForAge(2));
            Assert.AreEqual(before9, schedule.SlotForAge(10));
        }

        // ------------------------------------------------------------------ reset and staleness

        [Test]
        public void Reset_ForgetsEverythingAndRestartsAtSlotZero() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 10);
            schedule.Reset();
            Assert.AreEqual(0, schedule.ValidCount);
            Assert.AreEqual(-1, schedule.Head);
            Assert.AreEqual(-1, schedule.SlotForAge(0));
            Assert.IsTrue(schedule.ShouldCapture(true, 0.01f, Spacing, MaskInterval), "an earlier time than before the reset is fine");
            Assert.AreEqual(0, schedule.Capture(0.01f), "the first capture after a reset goes to slot zero");
            Assert.AreEqual(1, schedule.ValidCount);
        }

        [Test]
        public void IsStale_AnEmptyHistoryIsNeverStale() {
            EchoSchedule schedule = NewSchedule();
            Assert.IsFalse(schedule.IsStale(1000f, Spacing));
            Assert.IsFalse(schedule.ResetIfStale(1000f, Spacing));
        }

        [Test]
        public void IsStale_AHistoryOlderThanAboutASecondIsTooOldToBeAnEcho() {
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(10f);
            Assert.IsFalse(schedule.IsStale(10.14f, Spacing));
            Assert.IsFalse(schedule.IsStale(10.9f, Spacing));
            Assert.IsTrue(schedule.IsStale(11.1f, Spacing));
        }

        [Test]
        public void IsStale_TheLimitGrowsWithAVeryLongSpacing() {
            // A 2 s spacing is legitimate: gaps of 2 s are normal, so staleness starts at 4 spacings = 8 s,
            // not at 1 s.
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(10f);
            Assert.IsFalse(schedule.IsStale(17f, 2f));
            Assert.IsTrue(schedule.IsStale(19f, 2f));
        }

        [Test]
        public void IsStale_AHistoryFromTheFutureIsStale() {
            EchoSchedule schedule = NewSchedule();
            schedule.Capture(100f);
            Assert.IsTrue(schedule.IsStale(1f, Spacing));
        }

        [Test]
        public void ResetIfStale_ForgetsAStaleHistoryAndKeepsAFreshOne() {
            EchoSchedule schedule = NewSchedule();
            CaptureMany(schedule, 6);
            float last = schedule.LastCaptureTime;
            Assert.IsFalse(schedule.ResetIfStale(last + 0.2f, Spacing));
            Assert.AreEqual(6, schedule.ValidCount, "a fresh history is kept");
            Assert.IsTrue(schedule.ResetIfStale(last + 5f, Spacing));
            Assert.AreEqual(0, schedule.ValidCount, "a stale history is forgotten");
            Assert.AreEqual(-1, schedule.Head);
        }
    }
}
