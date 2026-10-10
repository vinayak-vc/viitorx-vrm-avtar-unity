using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Experiences;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-31 unit tests for <see cref="EchoBuffer"/>, the ring behind Time Echo.
    ///
    /// WHY THIS IS THE PART WORTH TESTING. A mis-indexed ring does not crash and does not look
    /// broken: it shows a plausible body at slightly the wrong time, and no viewer on earth can tell.
    /// That is exactly the class of defect that survives a visual check, so it is pinned here instead.
    /// The clock is injected, so every case below is deterministic.
    /// </summary>
    public sealed class EchoBufferTests {
        private static SkeletonPose PoseAt(float x, float confidence = 0.9f) {
            PoseFrame frame = new PoseFrame();
            for (int i = 0; i < PoseFrame.LandmarkCount; i++) {
                frame.SetLandmark(i, new PoseLandmark(new Vector3(x, 0f, 0f), confidence));
            }
            frame.SetMeta(0.0, true);
            SkeletonPose pose = new SkeletonPose();
            pose.Fill(frame, Vector3.zero, 1f, 1f / 60f);
            return pose;
        }

        [Test]
        public void EmptyBuffer_SamplesNothing() {
            EchoBuffer buffer = new EchoBuffer(10);
            Vector3[] p;
            float[] c;
            Assert.IsFalse(buffer.Sample(0f, out p, out c));
            Assert.AreEqual(0f, buffer.OldestAge(5f));
        }

        [Test]
        public void RequestOlderThanTheBufferReaches_IsRefused() {
            // The trail is still filling. Refusing is what lets Time Echo HIDE that echo rather than
            // showing the oldest frame it happens to have, which would pin a copy in place until the
            // buffer caught up.
            EchoBuffer buffer = new EchoBuffer(60);
            buffer.Push(PoseAt(0f), 10f);
            Vector3[] p;
            float[] c;
            Assert.IsFalse(buffer.Sample(9.0f, out p, out c), "1 s before the oldest frame");
            Assert.IsTrue(buffer.Sample(10f, out p, out c), "exactly the oldest frame");
        }

        [Test]
        public void SamplesTheNearestFrameInTime() {
            EchoBuffer buffer = new EchoBuffer(120);
            for (int i = 0; i < 100; i++) {
                buffer.Push(PoseAt(i), i * 0.1f);   // x encodes the frame index
            }
            Vector3[] p;
            float[] c;
            // 5.0 s is frame 50 exactly.
            Assert.IsTrue(buffer.Sample(5.0f, out p, out c));
            Assert.AreEqual(50f, p[0].x, 0.001f);
            // 5.04 s is nearer frame 50 than 51.
            Assert.IsTrue(buffer.Sample(5.04f, out p, out c));
            Assert.AreEqual(50f, p[0].x, 0.001f);
            // 5.06 s is nearer frame 51.
            Assert.IsTrue(buffer.Sample(5.06f, out p, out c));
            Assert.AreEqual(51f, p[0].x, 0.001f);
        }

        [Test]
        public void SamplesCorrectlyAfterTheRingHasWrapped() {
            // The case that a nested private implementation would never have been exercised on.
            EchoBuffer buffer = new EchoBuffer(50);
            for (int i = 0; i < 500; i++) {
                buffer.Push(PoseAt(i), i * 0.1f);
            }
            Assert.AreEqual(50, buffer.Count, "ring must cap at capacity, not grow");
            Vector3[] p;
            float[] c;
            // Newest is frame 499 at 49.9 s; the ring holds the last 50, so 45.0 s = frame 450.
            Assert.IsTrue(buffer.Sample(45.0f, out p, out c));
            Assert.AreEqual(450f, p[0].x, 0.001f);
            Assert.IsTrue(buffer.Sample(49.9f, out p, out c));
            Assert.AreEqual(499f, p[0].x, 0.001f, "the newest frame is still reachable after wrapping");
            Assert.IsFalse(buffer.Sample(44.0f, out p, out c), "anything older has been overwritten");
        }

        [Test]
        public void OldestAge_ReportsHowFarBackTheTrailReaches() {
            EchoBuffer buffer = new EchoBuffer(50);
            for (int i = 0; i < 10; i++) {
                buffer.Push(PoseAt(i), i * 0.1f);
            }
            // Oldest frame is at 0.0 s; "now" is 0.9 s.
            Assert.AreEqual(0.9f, buffer.OldestAge(0.9f), 0.001f);
            for (int i = 10; i < 500; i++) {
                buffer.Push(PoseAt(i), i * 0.1f);
            }
            // Full ring of 50 frames at 0.1 s apart spans 4.9 s.
            Assert.AreEqual(4.9f, buffer.OldestAge(49.9f), 0.01f);
        }

        [Test]
        public void UnobservedJointsAreStoredAsZeroConfidence() {
            // Presence is folded into the stored confidence so a replayed frame needs one test, not
            // two. A joint that was not observed must not come back as a drawable point.
            EchoBuffer buffer = new EchoBuffer(10);
            buffer.Push(PoseAt(1f, 0.0f), 0f);
            Vector3[] p;
            float[] c;
            Assert.IsTrue(buffer.Sample(0f, out p, out c));
            Assert.AreEqual(0f, c[0], 1e-6f);
        }

        [Test]
        public void Clear_EmptiesTheRing() {
            EchoBuffer buffer = new EchoBuffer(10);
            for (int i = 0; i < 5; i++) {
                buffer.Push(PoseAt(i), i * 0.1f);
            }
            buffer.Clear();
            Vector3[] p;
            float[] c;
            Assert.AreEqual(0, buffer.Count);
            Assert.IsFalse(buffer.Sample(0f, out p, out c));
        }

        [Test]
        public void CapacityIsNeverBelowTwo() {
            // A one-frame ring cannot represent a delay at all; clamping is quieter than throwing in
            // a scene whose serialized delay somebody set to zero.
            Assert.AreEqual(2, new EchoBuffer(0).Capacity);
            Assert.AreEqual(2, new EchoBuffer(-5).Capacity);
        }
    }
}
