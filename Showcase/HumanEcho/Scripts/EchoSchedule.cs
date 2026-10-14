using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The CPU side of the echo: WHEN the ring takes a mask, WHICH ring slot holds the mask of "copy k", and
    /// the numbers (hue, opacity,
    /// shift, size) each copy is drawn with. Nothing here touches the GPU or creates a Unity object, so every
    /// rule is covered by plain
    /// EditMode tests.
    ///
    /// Vocabulary. The ring keeps the last <see cref="Capacity"/> captures. AGE counts captures back from the
    /// newest one: age 0 is the
    /// mask captured last (about the live person, which the person layer already draws), age 1 the capture
    /// before it, and so on.
    /// COPY k (0-based) is the k-th echo drawn: copy 0 is the NEWEST echo and shows age 1, copy 15 shows age
    /// 16. That is why the ring
    /// needs <see cref="MaxCopies"/> + 1 slots.
    /// </summary>
    public sealed class EchoSchedule {
        /// <summary>The most echo copies one frame draws (the size of the shader's per-copy
        /// arrays).</summary>
        public const int MaxCopies = 16;

        /// <summary>Age shown by copy 0. Age 0 is the newest capture, which is the live person's pose, so no
        /// echo draws it.</summary>
        public const int FirstCopyAge = 1;

        /// <summary>Slots the ring needs so that <see cref="MaxCopies"/> copies all have a mask.</summary>
        public const int DefaultCapacity = MaxCopies + FirstCopyAge;

        // A history older than this is not an echo of "a moment ago" any more (the layer was off, or the mask
        // stream paused).
        private const float MinStaleSeconds = 1f;
        private const float StaleSpacings = 4f;

        // Plastic-number constants: successive copies get well spread grid shifts, so stacked dot ghosts
        // never line up into moire.
        private const float GridShiftX = 0.7548776662f;
        private const float GridShiftY = 0.5698402910f;

        private readonly int capacity;
        private int head = -1;
        private int validCount;
        private float lastCaptureTime;

        public EchoSchedule(int capacity) {
            this.capacity = Mathf.Max(1, capacity);
        }

        /// <summary>How many captures the ring can hold.</summary>
        public int Capacity {
            get {
                return capacity;
            }
        }

        /// <summary>Ring slot of the newest capture; -1 before the first one.</summary>
        public int Head {
            get {
                return head;
            }
        }

        /// <summary>How many captures exist (at most <see cref="Capacity"/>): ages 0 .. ValidCount - 1 have a
        /// mask.</summary>
        public int ValidCount {
            get {
                return validCount;
            }
        }

        /// <summary>The time passed to the last <see cref="Capture"/>.</summary>
        public float LastCaptureTime {
            get {
                return lastCaptureTime;
            }
        }

        /// <summary>
        /// True when the ring should take the current mask now. A capture needs a NEW mask, and at least
        /// <paramref name="spacingSeconds"/>
        /// minus half a mask interval since the last capture. The half interval matters because masks arrive
        /// on a grid of about
        /// <paramref name="maskIntervalSeconds"/> with jitter: demanding the full spacing would skip a whole
        /// mask (a whole slot's worth
        /// of time) every time one arrives a little early, while half an interval always lands on the grid
        /// point nearest to the spacing.
        /// </summary>
        public bool ShouldCapture(bool newMask, float now, float spacingSeconds, float maskIntervalSeconds) {
            if (!newMask) {
                return false;
            }
            if (validCount == 0) {
                return true;
            }
            float elapsed = now - lastCaptureTime;
            if (elapsed < 0f) {
                // The clock restarted (a new scene time); the last capture is not "before" now.
                return true;
            }
            float spacing = spacingSeconds > 0f ? spacingSeconds : 0f;
            float interval = maskIntervalSeconds > 0f ? maskIntervalSeconds : 0f;
            float needed = spacing - 0.5f * interval;
            if (needed < 0f) {
                needed = 0f;
            }
            return elapsed >= needed;
        }

        /// <summary>
        /// Books a capture made at <paramref name="now"/> and returns the ring slot the mask must be written
        /// to. The slot of age k
        /// moves on by one (the newest becomes age 0, what was age 0 becomes age 1).
        /// </summary>
        public int Capture(float now) {
            head = (head + 1) % capacity;
            if (validCount < capacity) {
                validCount = validCount + 1;
            }
            lastCaptureTime = now;
            return head;
        }

        /// <summary>True when a capture of that age exists (0 = newest).</summary>
        public bool IsValidAge(int age) {
            return age >= 0 && age < validCount;
        }

        /// <summary>The ring slot that holds the capture of <paramref name="age"/>, or -1 when no such
        /// capture exists yet.</summary>
        public int SlotForAge(int age) {
            if (!IsValidAge(age)) {
                return -1;
            }
            int slot = head - age;
            if (slot < 0) {
                slot = slot + capacity;
            }
            return slot;
        }

        /// <summary>True when the history is too old to be an echo, or lies in the future of <paramref
        /// name="now"/>.</summary>
        public bool IsStale(float now, float spacingSeconds) {
            if (validCount == 0) {
                return false;
            }
            float elapsed = now - lastCaptureTime;
            if (elapsed < 0f) {
                return true;
            }
            float spacing = spacingSeconds > 0f ? spacingSeconds : 0f;
            float limit = Mathf.Max(MinStaleSeconds, StaleSpacings * spacing);
            return elapsed > limit;
        }

        /// <summary>Forgets a stale history (see <see cref="IsStale"/>). Returns true when it did.</summary>
        public bool ResetIfStale(float now, float spacingSeconds) {
            if (!IsStale(now, spacingSeconds)) {
                return false;
            }
            Reset();
            return true;
        }

        /// <summary>Forgets every capture. The slots keep their pixels, but no age maps to them any
        /// more.</summary>
        public void Reset() {
            head = -1;
            validCount = 0;
            lastCaptureTime = 0f;
        }

        /// <summary>The copy count the layer draws: <see cref="EchoSettings.copies"/> kept inside 1 .. <see
        /// cref="MaxCopies"/>.</summary>
        public static int ClampCopies(int copies) {
            return Mathf.Clamp(copies, 1, MaxCopies);
        }

        /// <summary>The ring age shown by copy <paramref name="copy"/> (0 = the newest echo).</summary>
        public static int AgeOfCopy(int copy) {
            return copy + FirstCopyAge;
        }

        /// <summary>
        /// Hue (0..1) of copy <paramref name="copy"/> at <paramref name="time"/> seconds: the newest copy has
        /// hueStart, every older copy
        /// is hueStep further, and the whole ramp rotates by hueDrift turns per second. The drift is wrapped
        /// on its own first so a long
        /// running scene keeps its float precision.
        /// </summary>
        public static float HueFor(EchoSettings settings, int copy, float time) {
            float drift = Mathf.Repeat(settings.hueDrift * time, 1f);
            return Mathf.Repeat(settings.hueStart + drift + settings.hueStep * copy, 1f);
        }

        /// <summary>The colour of copy <paramref name="copy"/> before gain and linear conversion (the fixed
        /// colour, or the hue ramp).</summary>
        public static Color ColorFor(EchoSettings settings, int copy, float time) {
            if (settings.colorMode == EchoColorMode.Solid) {
                return settings.solidColor;
            }
            return Color.HSVToRGB(HueFor(settings, copy, time), settings.saturation, settings.value);
        }

        /// <summary>
        /// Opacity of copy <paramref name="copy"/> out of <paramref name="copies"/>: opacityNewest for copy
        /// 0, opacityOldest for the last
        /// one, linear in between. A single copy is the newest. The ramp always runs over the configured
        /// count, not over the copies that
        /// exist yet, so a copy does not change opacity when a farther one appears.
        /// </summary>
        public static float OpacityFor(EchoSettings settings, int copy, int copies) {
            int count = ClampCopies(copies);
            float t = 0f;
            if (count > 1) {
                t = Mathf.Clamp01((float)copy / (count - 1));
            }
            return Mathf.Clamp01(Mathf.Lerp(settings.opacityNewest, settings.opacityOldest, t));
        }

        /// <summary>How far copy <paramref name="copy"/> is shifted from the person, in screen heights: one
        /// step per age.</summary>
        public static Vector2 OffsetFor(EchoSettings settings, int copy) {
            return settings.offsetPerCopy * (float)AgeOfCopy(copy);
        }

        /// <summary>
        /// Size factor of copy <paramref name="copy"/>. It compounds per age (+2 % per step is 1.02 ^ age),
        /// so even -10 % per step over
        /// 16 copies stays positive instead of flipping the copy inside out.
        /// </summary>
        public static float ScaleFor(EchoSettings settings, int copy) {
            float step = Mathf.Max(0.01f, 1f + settings.scaleStep);
            return Mathf.Pow(step, (float)AgeOfCopy(copy));
        }

        /// <summary>A shift of the stipple grid for copy <paramref name="copy"/>, in cells (0..1 on each
        /// axis).</summary>
        public static Vector2 GridShiftFor(int copy) {
            float n = (float)(copy + 1);
            return new Vector2(Mathf.Repeat(GridShiftX * n, 1f), Mathf.Repeat(GridShiftY * n, 1f));
        }
    }
}
