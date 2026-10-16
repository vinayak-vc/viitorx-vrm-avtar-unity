using NUnit.Framework;
using UnityEngine;

using VirtualMirror.Retargeting;

namespace VirtualMirror.Tests {
    /// <summary>
    /// P0-1 (audit F-01) unit tests for <see cref="LimbGate"/> — the confidence gate that prevents an
    /// invalid/occluded joint from driving a limb rotation toward the origin. Proves the core requirement:
    /// a low/invalid limb HOLDS its last valid rotation and NEVER returns zero.
    /// </summary>
    public sealed class LimbGateTests {
        private const float Thr = 0.3f;
        private static readonly Vector3 U = new Vector3(0.5f, 0.2f, -0.1f);
        private static readonly Vector3 L = new Vector3(0.3f, 0.1f, 0.0f);
        private static readonly Vector3 Zero = Vector3.zero;

        [Test]
        public void HighConfidence_AppliesFreshSolve() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool transitioned;
            bool applied = gate.Resolve(0.9f, Thr, U, L, out tu, out tl, out transitioned);
            Assert.IsTrue(applied);
            Assert.AreEqual(U, tu);
            Assert.AreEqual(L, tl);
            Assert.AreEqual(LimbGate.State.Valid, gate.CurrentState);
        }

        [Test]
        public void ShortDropout_HoldsLastValid_NeverZero() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool tr;
            gate.Resolve(0.9f, Thr, U, L, out tu, out tl, out tr); // establish valid
            for (int i = 0; i < 5; i++) {
                bool applied = gate.Resolve(0.0f, Thr, Zero, Zero, out tu, out tl, out tr);
                Assert.IsTrue(applied, "held frame must still apply a rotation");
                Assert.AreEqual(U, tu, "must hold the last valid upper rotation");
                Assert.AreEqual(L, tl, "must hold the last valid lower rotation");
                Assert.AreNotEqual(Zero, tu, "must NEVER collapse to zero (the bug being fixed)");
            }
            Assert.AreEqual(LimbGate.State.Held, gate.CurrentState);
            Assert.AreEqual(1, gate.HoldEvents, "exactly one VALID→HELD transition");
        }

        [Test]
        public void LongDropout_StaysHeld_NoCollapse() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool tr;
            gate.Resolve(0.9f, Thr, U, L, out tu, out tl, out tr);
            for (int i = 0; i < 20; i++) {
                gate.Resolve(0.0f, Thr, Zero, Zero, out tu, out tl, out tr);
                Assert.AreEqual(U, tu, "P0 holds indefinitely rather than collapsing");
            }
        }

        [Test]
        public void Reacquire_FlagsTransition_AppliesNew() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool tr;
            gate.Resolve(0.9f, Thr, U, L, out tu, out tl, out tr);
            for (int i = 0; i < 4; i++) {
                gate.Resolve(0.0f, Thr, Zero, Zero, out tu, out tl, out tr);
            }
            Vector3 newU = new Vector3(0.6f, 0.25f, -0.05f);
            Vector3 newL = new Vector3(0.35f, 0.12f, 0.0f);
            bool applied = gate.Resolve(0.9f, Thr, newU, newL, out tu, out tl, out tr);
            Assert.IsTrue(applied);
            Assert.AreEqual(newU, tu, "re-acquire applies the fresh solve (slerp downstream avoids a snap)");
            Assert.IsTrue(tr, "HELD→VALID must flag a transition");
            Assert.AreEqual(1, gate.ReacquireEvents);
        }

        [Test]
        public void InvalidAtStartup_DoesNotApply_NoZero() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool tr;
            bool applied = gate.Resolve(0.0f, Thr, Zero, Zero, out tu, out tl, out tr);
            Assert.IsFalse(applied, "with no valid state yet, the bone must be left at rest (not zero-driven)");
        }

        [Test]
        public void MediumBelowThreshold_Holds() {
            LimbGate gate = new LimbGate();
            Vector3 tu, tl;
            bool tr;
            gate.Resolve(0.9f, Thr, U, L, out tu, out tl, out tr);
            bool applied = gate.Resolve(0.2f, Thr, new Vector3(9f, 9f, 9f), new Vector3(9f, 9f, 9f), out tu, out tl, out tr);
            Assert.IsTrue(applied);
            Assert.AreEqual(U, tu, "0.2 < 0.3 threshold → hold, do not apply the low-confidence solve");
        }
    }
}
