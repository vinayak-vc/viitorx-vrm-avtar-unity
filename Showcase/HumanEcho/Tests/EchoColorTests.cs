using NUnit.Framework;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// EditMode tests for the numbers each echo copy is drawn with: the hue ramp (and how it wraps past 1),
    /// the opacity ramp from the
    /// newest to the oldest copy, the shift and size per age, the stipple grid shift. Pure logic on <see
    /// cref="EchoSettings"/>: no GPU,
    /// no scene. Parameterless [Test] methods only (the self-test runner of this assembly does not run
    /// [TestCase]).
    /// </summary>
    public sealed class EchoColorTests {
        private const float Tolerance = 1e-4f;

        private static EchoSettings NewSettings() {
            EchoSettings settings = new EchoSettings();
            settings.copies = 8;
            settings.colorMode = EchoColorMode.HueRamp;
            settings.hueStart = 0.1f;
            settings.hueStep = 0.09f;
            settings.hueDrift = 0.05f;
            settings.saturation = 1f;
            settings.value = 1f;
            settings.opacityNewest = 0.85f;
            settings.opacityOldest = 0.12f;
            settings.offsetPerCopy = new Vector2(0.05f, -0.02f);
            settings.scaleStep = 0.02f;
            return settings;
        }

        // ------------------------------------------------------------------ hue ramp

        [Test]
        public void Hue_NewestCopyStartsAtHueStart() {
            EchoSettings settings = NewSettings();
            Assert.AreEqual(0.1f, EchoSchedule.HueFor(settings, 0, 0f), Tolerance);
        }

        [Test]
        public void Hue_StepsOncePerCopy() {
            EchoSettings settings = NewSettings();
            Assert.AreEqual(0.1f + 0.09f, EchoSchedule.HueFor(settings, 1, 0f), Tolerance);
            Assert.AreEqual(0.1f + 3f * 0.09f, EchoSchedule.HueFor(settings, 3, 0f), Tolerance);
        }

        [Test]
        public void Hue_DriftsWithTimeInTurnsPerSecond() {
            EchoSettings settings = NewSettings();
            // 0.05 turns per second for 4 s is 0.2 of a turn.
            Assert.AreEqual(0.3f, EchoSchedule.HueFor(settings, 0, 4f), Tolerance);
            settings.hueDrift = 0f;
            Assert.AreEqual(0.1f, EchoSchedule.HueFor(settings, 0, 1234f), Tolerance, "no drift: the ramp stands still");
            settings.hueDrift = -0.05f;
            Assert.AreEqual(0.9f, EchoSchedule.HueFor(settings, 0, 4f), Tolerance, "a negative drift turns the other way");
        }

        [Test]
        public void Hue_WrapsPastOne() {
            EchoSettings settings = NewSettings();
            settings.hueStart = 0.9f;
            settings.hueDrift = 0f;
            // 0.9 + 2 * 0.09 = 1.08, which is 0.08 of the next turn.
            float hue = EchoSchedule.HueFor(settings, 2, 0f);
            Assert.AreEqual(0.08f, hue, Tolerance);
            Assert.GreaterOrEqual(hue, 0f);
            Assert.LessOrEqual(hue, 1f);
        }

        [Test]
        public void Hue_ANegativeStepWrapsBelowZero() {
            EchoSettings settings = NewSettings();
            settings.hueStart = 0.1f;
            settings.hueStep = -0.2f;
            settings.hueDrift = 0f;
            // 0.1 - 0.2 = -0.1, which is 0.9.
            Assert.AreEqual(0.9f, EchoSchedule.HueFor(settings, 1, 0f), Tolerance);
        }

        [Test]
        public void Hue_StaysInTheUnitRangeForEveryCopyAndALongRun() {
            EchoSettings settings = NewSettings();
            float[] times = new float[] { 0f, 1f, 59.7f, 3600f, 86400f, 100000f };
            float[] steps = new float[] { 0.09f, -0.13f, 0.5f, 1.7f };
            for (int s = 0; s < steps.Length; s++) {
                settings.hueStep = steps[s];
                for (int t = 0; t < times.Length; t++) {
                    for (int copy = 0; copy < EchoSchedule.MaxCopies; copy++) {
                        float hue = EchoSchedule.HueFor(settings, copy, times[t]);
                        Assert.IsFalse(float.IsNaN(hue), "NaN at copy " + copy + " time " + times[t]);
                        Assert.GreaterOrEqual(hue, 0f, "copy " + copy + " time " + times[t]);
                        Assert.LessOrEqual(hue, 1f, "copy " + copy + " time " + times[t]);
                    }
                }
            }
        }

        [Test]
        public void Hue_ALongRunKeepsItsPrecision() {
            // 0.05 turns per second for ten million seconds (115 days) is 500000 whole turns: the hue is back
            // at the start, to a
            // five hundredth of a turn. Adding hueStart to the unwrapped drift first would round the sum to
            // 1/32 and miss by 0.006.
            EchoSettings settings = NewSettings();
            float hue = EchoSchedule.HueFor(settings, 0, 10000000f);
            float distance = Mathf.Abs(hue - 0.1f);
            Assert.Less(Mathf.Min(distance, 1f - distance), 0.002f);
        }

        // ------------------------------------------------------------------ colour

        [Test]
        public void Color_SolidModeIgnoresTheHueRamp() {
            EchoSettings settings = NewSettings();
            settings.colorMode = EchoColorMode.Solid;
            settings.solidColor = new Color(1f, 0.05f, 0.05f, 1f);
            for (int copy = 0; copy < EchoSchedule.MaxCopies; copy++) {
                Assert.AreEqual(settings.solidColor, EchoSchedule.ColorFor(settings, copy, copy * 3.7f), "copy " + copy);
            }
        }

        [Test]
        public void Color_HueRampFollowsHueSaturationAndValue() {
            EchoSettings settings = NewSettings();
            settings.hueStart = 0f;
            settings.hueStep = 0f;
            settings.hueDrift = 0f;
            Color red = EchoSchedule.ColorFor(settings, 0, 0f);
            Assert.AreEqual(1f, red.r, 1e-3f);
            Assert.AreEqual(0f, red.g, 1e-3f);
            Assert.AreEqual(0f, red.b, 1e-3f);

            settings.hueStart = 1f / 3f;
            Color green = EchoSchedule.ColorFor(settings, 0, 0f);
            Assert.AreEqual(0f, green.r, 1e-3f);
            Assert.AreEqual(1f, green.g, 1e-3f);
            Assert.AreEqual(0f, green.b, 1e-3f);

            settings.saturation = 0f;
            settings.value = 0.6f;
            Color grey = EchoSchedule.ColorFor(settings, 0, 0f);
            Assert.AreEqual(0.6f, grey.r, 1e-3f, "no saturation: a grey of the given value");
            Assert.AreEqual(0.6f, grey.g, 1e-3f);
            Assert.AreEqual(0.6f, grey.b, 1e-3f);

            settings.value = 0f;
            Color black = EchoSchedule.ColorFor(settings, 0, 0f);
            Assert.AreEqual(0f, black.r + black.g + black.b, 1e-3f, "no value: black");
        }

        [Test]
        public void Color_HueRampDiffersBetweenCopies() {
            EchoSettings settings = NewSettings();
            Color newest = EchoSchedule.ColorFor(settings, 0, 0f);
            Color older = EchoSchedule.ColorFor(settings, 3, 0f);
            Assert.AreNotEqual(newest, older);
        }

        // ------------------------------------------------------------------ opacity ramp

        [Test]
        public void Opacity_EndsAreTheNewestAndTheOldestSetting() {
            EchoSettings settings = NewSettings();
            Assert.AreEqual(0.85f, EchoSchedule.OpacityFor(settings, 0, 8), Tolerance);
            Assert.AreEqual(0.12f, EchoSchedule.OpacityFor(settings, 7, 8), Tolerance);
        }

        [Test]
        public void Opacity_MiddleOfTheRampIsTheAverage() {
            EchoSettings settings = NewSettings();
            // Five copies: copy 2 is exactly halfway.
            Assert.AreEqual((0.85f + 0.12f) * 0.5f, EchoSchedule.OpacityFor(settings, 2, 5), Tolerance);
        }

        [Test]
        public void Opacity_FadesStrictlyFromNewestToOldest() {
            EchoSettings settings = NewSettings();
            float previous = float.MaxValue;
            for (int copy = 0; copy < 16; copy++) {
                float opacity = EchoSchedule.OpacityFor(settings, copy, 16);
                Assert.Less(opacity, previous, "copy " + copy + " must be fainter than copy " + (copy - 1));
                previous = opacity;
            }
        }

        [Test]
        public void Opacity_OneCopyIsTheNewestAndNotNaN() {
            EchoSettings settings = NewSettings();
            float opacity = EchoSchedule.OpacityFor(settings, 0, 1);
            Assert.IsFalse(float.IsNaN(opacity));
            Assert.AreEqual(0.85f, opacity, Tolerance);
        }

        [Test]
        public void Opacity_AnOutOfRangeCountOrCopyIsClamped() {
            EchoSettings settings = NewSettings();
            Assert.AreEqual(0.85f, EchoSchedule.OpacityFor(settings, 0, 0), Tolerance, "a count of 0 is treated as 1 copy");
            Assert.AreEqual(0.12f, EchoSchedule.OpacityFor(settings, 15, 99), Tolerance, "a count of 99 is treated as 16 copies");
            Assert.AreEqual(0.12f, EchoSchedule.OpacityFor(settings, 20, 8), Tolerance, "a copy past the last is the oldest");
            Assert.AreEqual(0.85f, EchoSchedule.OpacityFor(settings, -3, 8), Tolerance, "a negative copy is the newest");
        }

        [Test]
        public void Opacity_IsKeptInsideZeroToOne() {
            EchoSettings settings = NewSettings();
            settings.opacityNewest = 1.6f;
            settings.opacityOldest = -0.4f;
            Assert.AreEqual(1f, EchoSchedule.OpacityFor(settings, 0, 8), Tolerance);
            Assert.AreEqual(0f, EchoSchedule.OpacityFor(settings, 7, 8), Tolerance);
        }

        [Test]
        public void Opacity_RisesWhenTheOldestIsBrighterThanTheNewest() {
            EchoSettings settings = NewSettings();
            settings.opacityNewest = 0.1f;
            settings.opacityOldest = 0.9f;
            Assert.AreEqual(0.1f, EchoSchedule.OpacityFor(settings, 0, 4), Tolerance);
            Assert.AreEqual(0.9f, EchoSchedule.OpacityFor(settings, 3, 4), Tolerance);
            Assert.Greater(EchoSchedule.OpacityFor(settings, 2, 4), EchoSchedule.OpacityFor(settings, 1, 4));
        }

        // ------------------------------------------------------------------ shift and size

        [Test]
        public void Offset_IsOneStepForTheNewestCopyAndGrowsOneStepPerAge() {
            EchoSettings settings = NewSettings();
            Vector2 first = EchoSchedule.OffsetFor(settings, 0);
            Assert.AreEqual(0.05f, first.x, Tolerance, "the newest echo is already one step away from the person");
            Assert.AreEqual(-0.02f, first.y, Tolerance);
            Vector2 fourth = EchoSchedule.OffsetFor(settings, 3);
            Assert.AreEqual(0.20f, fourth.x, Tolerance);
            Assert.AreEqual(-0.08f, fourth.y, Tolerance);
        }

        [Test]
        public void Offset_ZeroStepStacksTheCopies() {
            EchoSettings settings = NewSettings();
            settings.offsetPerCopy = Vector2.zero;
            for (int copy = 0; copy < EchoSchedule.MaxCopies; copy++) {
                Assert.AreEqual(0f, EchoSchedule.OffsetFor(settings, copy).sqrMagnitude, 1e-12f);
            }
        }

        [Test]
        public void Scale_CompoundsPerAge() {
            EchoSettings settings = NewSettings();
            Assert.AreEqual(1.02f, EchoSchedule.ScaleFor(settings, 0), 1e-5f);
            // Age 3: 1.02 ^ 3, not 1 + 3 * 0.02.
            Assert.AreEqual(1.061208f, EchoSchedule.ScaleFor(settings, 2), 1e-5f);
        }

        [Test]
        public void Scale_ZeroStepKeepsTheSize() {
            EchoSettings settings = NewSettings();
            settings.scaleStep = 0f;
            for (int copy = 0; copy < EchoSchedule.MaxCopies; copy++) {
                Assert.AreEqual(1f, EchoSchedule.ScaleFor(settings, copy), 1e-6f);
            }
        }

        [Test]
        public void Scale_StaysPositiveForTheStrongestShrinkAndNonsense() {
            EchoSettings settings = NewSettings();
            settings.scaleStep = -0.1f;
            Assert.Greater(EchoSchedule.ScaleFor(settings, 15), 0.1f, "0.9 ^ 16 is about 0.185");
            settings.scaleStep = -2f;
            float scale = EchoSchedule.ScaleFor(settings, 15);
            Assert.IsFalse(float.IsNaN(scale));
            Assert.Greater(scale, 0f, "a nonsense step must not flip a copy inside out");
        }

        // ------------------------------------------------------------------ stipple grid shift

        [Test]
        public void GridShift_StaysInOneCellAndDiffersPerCopy() {
            for (int a = 0; a < EchoSchedule.MaxCopies; a++) {
                Vector2 shiftA = EchoSchedule.GridShiftFor(a);
                Assert.GreaterOrEqual(shiftA.x, 0f);
                Assert.LessOrEqual(shiftA.x, 1f);
                Assert.GreaterOrEqual(shiftA.y, 0f);
                Assert.LessOrEqual(shiftA.y, 1f);
                for (int b = a + 1; b < EchoSchedule.MaxCopies; b++) {
                    Vector2 shiftB = EchoSchedule.GridShiftFor(b);
                    Assert.Greater((shiftA - shiftB).magnitude, 0.01f, "copies " + a + " and " + b + " share a grid shift");
                }
            }
        }
    }
}
