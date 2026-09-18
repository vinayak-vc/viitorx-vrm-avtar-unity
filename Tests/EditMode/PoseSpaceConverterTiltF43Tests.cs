using NUnit.Framework;
using UnityEngine;
using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-43 — MOUNT TILT COMPENSATION.
    ///
    /// The thing being tested is not "does a rotation matrix rotate". It is the specific failure a
    /// tilted mount causes downstream: <see cref="SkeletonPose"/> learns the floor as a SINGLE
    /// SCALAR from foot contacts, and a camera pitched by θ makes the floor appear at a different
    /// height at every distance. One scalar cannot represent a sloped plane, so the skeleton sinks
    /// as someone walks in and rises as they walk out.
    ///
    /// So the central test here (<see cref="FloorSampledAtTwoDepths_LevelsToASingleHeight"/>) is
    /// written as the geometry actually is: take a real floor, work out what a pitched camera would
    /// measure looking at it, and require that the converter turns those two different measurements
    /// back into one height. The rest guard the things that would silently make that correct number
    /// arrive wrong — the identity case, the mirror ordering, and the two entry points agreeing.
    /// </summary>
    [TestFixture]
    public class PoseSpaceConverterTiltF43Tests {
        private const float Tol = 1e-4f;

        /// <summary>
        /// What a camera pitched DOWN by <paramref name="tiltDeg"/> measures for a point that really
        /// sits at (0, height, depth) in a world-levelled frame — X right, Y DOWN, Z forward, the
        /// frame the sidecar emits. This is the inverse of the correction, derived independently of
        /// it so a sign error in the production code cannot be cancelled by the same error here.
        /// </summary>
        private static Vector2 AsMeasuredByTiltedCamera(float height, float depth, float tiltDeg) {
            float r = tiltDeg * Mathf.Deg2Rad;
            float y = height * Mathf.Cos(r) - depth * Mathf.Sin(r);
            float z = height * Mathf.Sin(r) + depth * Mathf.Cos(r);
            return new Vector2(y, z);
        }

        [Test]
        public void ZeroTilt_IsExactIdentity_SoALevelMountIsUnaffected() {
            // The regression guard that matters most: every existing install runs at 0.
            PoseSpaceConverter level = new PoseSpaceConverter(false, false, false, 0f);
            PoseSpaceConverter legacy = new PoseSpaceConverter(false, false, false);

            Vector3 a = level.ToUnity(0.3f, 1.1f, 2.7f);
            Vector3 b = legacy.ToUnity(0.3f, 1.1f, 2.7f);
            Assert.AreEqual(b.x, a.x, 0f, "x must be bit-identical at zero tilt");
            Assert.AreEqual(b.y, a.y, 0f, "y must be bit-identical at zero tilt");
            Assert.AreEqual(b.z, a.z, 0f, "z must be bit-identical at zero tilt");

            Vector3 pa = level.ToUnityPosition(0.3f, 1.1f, 2.7f);
            Vector3 pb = legacy.ToUnityPosition(0.3f, 1.1f, 2.7f);
            Assert.AreEqual(pb.y, pa.y, 0f);
            Assert.AreEqual(pb.z, pa.z, 0f);
        }

        [Test]
        public void ThreeArgumentConstructor_DefaultsToLevel() {
            Assert.AreEqual(0f, new PoseSpaceConverter(false, true, false).TiltDegrees, 0f);
        }

        [Test]
        public void PointOnTheOpticalAxis_ResolvesBelowTheHorizon() {
            // A down-pitched camera looking along its own axis is looking at the floor, not at the
            // horizon. At 3 m down 20°, the axis point is 3*sin20 = 1.026 m below camera height and
            // only 3*cos20 = 2.819 m away horizontally. Y is DOWN, so "below" is positive.
            PoseSpaceConverter c = new PoseSpaceConverter(false, false, false, 20f);
            Vector3 r = c.ToUnity(0f, 0f, 3f);

            Assert.AreEqual(3f * Mathf.Sin(20f * Mathf.Deg2Rad), r.y, Tol);
            Assert.AreEqual(3f * Mathf.Cos(20f * Mathf.Deg2Rad), r.z, Tol);
            Assert.AreEqual(0f, r.x, Tol);
        }

        [Test]
        public void FloorSampledAtTwoDepths_LevelsToASingleHeight() {
            // THE POINT OF THE FEATURE. A camera 1.2 m up, pitched down 20°, looking at its own
            // floor at 1.5 m and at 2.5 m. Uncorrected those two samples disagree about where the
            // floor is; corrected they must not, because it is one floor.
            const float tilt = 20f;
            const float height = 1.2f;
            PoseSpaceConverter c = new PoseSpaceConverter(false, false, false, tilt);

            Vector2 near = AsMeasuredByTiltedCamera(height, 1.5f, tilt);
            Vector2 far = AsMeasuredByTiltedCamera(height, 2.5f, tilt);

            // What the tilted camera reported, with no correction, differs by a third of a metre.
            Assert.Greater(Mathf.Abs(near.x - far.x), 0.3f,
                           "the uncorrected samples must genuinely disagree, or this test proves nothing");

            Vector3 rn = c.ToUnity(0f, near.x, near.y);
            Vector3 rf = c.ToUnity(0f, far.x, far.y);

            Assert.AreEqual(height, rn.y, Tol, "near floor sample");
            Assert.AreEqual(height, rf.y, Tol, "far floor sample");
            Assert.AreEqual(rn.y, rf.y, Tol, "one floor, one height");

            // And the horizontal distances survive as themselves rather than as slant ranges.
            Assert.AreEqual(1.5f, rn.z, Tol);
            Assert.AreEqual(2.5f, rf.z, Tol);
        }

        [Test]
        public void UncorrectedFloorDrift_IsTanTiltPerMetreOfDepth() {
            // Pins the number quoted in CameraMount and in the setup guidance: 15° is 0.27 m/m, which
            // crosses SkeletonPose.PlantedBandMetres (0.08 m) within 30 cm of walking. If this ever
            // stops holding, the justification for the whole feature has moved.
            const float tilt = 15f;
            Vector2 a = AsMeasuredByTiltedCamera(1.2f, 1.0f, tilt);
            Vector2 b = AsMeasuredByTiltedCamera(1.2f, 2.0f, tilt);

            float driftPerMetre = Mathf.Abs((a.x - b.x) / (b.y - a.y));
            Assert.AreEqual(Mathf.Tan(tilt * Mathf.Deg2Rad), driftPerMetre, 1e-3f);
            Assert.AreEqual(0.268f, driftPerMetre, 5e-3f, "0.27 m per metre at 15 degrees");

            float walkToCrossPlantedBand = SkeletonPose.PlantedBandMetres / driftPerMetre;
            Assert.Less(walkToCrossPlantedBand, 0.31f,
                        "15 degrees must still blow the planted band inside a third of a metre");
        }

        [Test]
        public void Levelling_IsARotation_SoItPreservesLength() {
            PoseSpaceConverter c = new PoseSpaceConverter(false, false, false, 27.5f);
            Vector3 r = c.ToUnity(0.4f, 1.3f, 2.2f);
            Assert.AreEqual(new Vector3(0.4f, 1.3f, 2.2f).magnitude, r.magnitude, Tol);
        }

        [Test]
        public void TiltIsAppliedBeforeTheMirrorSigns() {
            // Mirroring is a display convention applied to already-levelled data. If the order were
            // reversed the rotation would run in the mirrored frame and lean the body the wrong way,
            // which shows up here as the floor no longer resolving to its true height.
            const float tilt = 20f;
            const float height = 1.2f;
            Vector2 m = AsMeasuredByTiltedCamera(height, 2.0f, tilt);

            PoseSpaceConverter mirrored = new PoseSpaceConverter(true, false, true, tilt);
            Vector3 r = mirrored.ToUnity(0.5f, m.x, m.y);

            Assert.AreEqual(height, r.y, Tol, "height must survive a Z mirror");
            Assert.AreEqual(-2.0f, r.z, Tol, "depth is levelled first, then mirrored");
            Assert.AreEqual(-0.5f, r.x, Tol);
        }

        [Test]
        public void BothEntryPoints_LevelTheSameFrame() {
            // The per-joint landmarks and the mid-hip "xyz" come from one back-projection in the
            // sender, so a correction that applied to only one of them would tear the root away
            // from the body it belongs to.
            const float tilt = 18f;
            PoseSpaceConverter c = new PoseSpaceConverter(false, false, false, tilt);
            Vector2 m = AsMeasuredByTiltedCamera(1.0f, 2.0f, tilt);

            Vector3 landmark = c.ToUnity(0.2f, m.x, m.y);
            Vector3 root = c.ToUnityPosition(0.2f, m.x, m.y);

            Assert.AreEqual(landmark.y, root.y, Tol);
            Assert.AreEqual(landmark.z, root.z, Tol);
            Assert.AreEqual(1.0f, root.y, Tol);
            Assert.AreEqual(2.0f, root.z, Tol);
        }

        [Test]
        public void SetTiltDegrees_TakesEffectOnTheNextConversion() {
            PoseSpaceConverter c = new PoseSpaceConverter(false, false, false);
            Assert.AreEqual(3f, c.ToUnity(0f, 0f, 3f).z, Tol, "level to begin with");

            c.SetTiltDegrees(20f);
            Assert.AreEqual(20f, c.TiltDegrees, 0f);
            Assert.AreEqual(3f * Mathf.Cos(20f * Mathf.Deg2Rad), c.ToUnity(0f, 0f, 3f).z, Tol);

            c.SetTiltDegrees(0f);
            Assert.AreEqual(3f, c.ToUnity(0f, 0f, 3f).z, Tol, "and back to level");
        }

        [Test]
        public void NegativeTilt_HandlesAnUpwardPitch() {
            // Rare but real — a camera below the subject looking up. The correction is the mirror
            // image, which is worth pinning so nobody "fixes" the sign convention by clamping it.
            PoseSpaceConverter down = new PoseSpaceConverter(false, false, false, 12f);
            PoseSpaceConverter up = new PoseSpaceConverter(false, false, false, -12f);

            Vector3 d = down.ToUnity(0f, 0f, 2f);
            Vector3 u = up.ToUnity(0f, 0f, 2f);

            Assert.AreEqual(-d.y, u.y, Tol);
            Assert.AreEqual(d.z, u.z, Tol);
        }

        [Test]
        public void CameraMount_IsLevelByDefault() {
            float saved = CameraMount.TiltDegrees;
            try {
                CameraMount.TiltDegrees = 0f;
                Assert.AreEqual(0f, CameraMount.TiltDegrees, 0f);
                CameraMount.TiltDegrees = 14.5f;
                Assert.AreEqual(14.5f, CameraMount.TiltDegrees, 0f);
            } finally {
                CameraMount.TiltDegrees = saved;
            }
        }
    }
}
