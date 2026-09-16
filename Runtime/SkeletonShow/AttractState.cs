using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-29 — WHAT THE INSTALLATION DOES WITH NOBODY IN FRONT OF IT.
    ///
    /// F-28 §7.4 named this as missing and it is the gap that matters most in a room: with no person
    /// tracked, all of the modes drew NOTHING. A black screen is indistinguishable from a crashed
    /// machine, so the first thing any visitor saw was something that looked broken, and nobody
    /// approaches a broken exhibit.
    ///
    /// THE DESIGN DECISION WORTH KNOWING: attract is a synthetic POSE SOURCE, not a separate
    /// renderer. It emits an ordinary <see cref="PoseFrame"/> that flows through the identical path a
    /// tracked person's does — <see cref="SkeletonPose"/>, the humanized layer, whichever mode is
    /// active. So the attract loop demonstrates the REAL modes rather than a bespoke screensaver that
    /// would drift away from them, every mode gets attract behaviour for free including modes not yet
    /// written, and a visitor sees exactly what they are about to control.
    ///
    /// The one thing it must never do is look like a tracked person, or an operator could not tell a
    /// live failure from the attract loop. It is marked in the HUD, it moves more slowly and more
    /// regularly than a human does, and <see cref="PoseFrame.HasRootPosition"/> is left false so no
    /// depth readout invents a distance for a body that is not there.
    /// </summary>
    public sealed class AttractSkeleton {
        // Hip-centred rest pose in UNITY-space metres (X right, Y up, Z forward, camera at -Z looking
        // toward +Z, so the figure faces -Z). Ordinary adult proportions: 1.70 m standing, hip centre
        // at 0.93 m, which puts the head near 1.65 m and the feet at the floor when the show stages
        // the origin at 0.95 m.
        private static readonly Vector3[] Rest = BuildRest();

        private readonly PoseFrame frame = new PoseFrame();
        private float clock;

        /// <summary>Advance and return the synthetic pose. The returned instance is reused.</summary>
        public PoseFrame Tick(float deltaSeconds) {
            clock = clock + deltaSeconds;

            // Three slow, incommensurate periods so the loop never visibly repeats: a viewer who can
            // see the cycle decides the exhibit is a video rather than something that will respond.
            float breathe = Mathf.Sin(clock * 0.9f);
            float sway = Mathf.Sin(clock * 0.37f);
            float drift = Mathf.Sin(clock * 0.23f);
            float armPhase = clock * 0.55f;

            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 p = Rest[i];
                bool used = p.sqrMagnitude > 0f || i == 23 || i == 24;
                if (!used) {
                    // Slots the body model does not fill stay explicitly unobserved. Emitting them at
                    // the origin with confidence is the exact fault the humanized layer exists to
                    // reject, and attract must not be the thing that produces it.
                    frame.SetLandmark(i, new PoseLandmark(Vector3.zero, 0f));
                    i = i + 1;
                    continue;
                }

                // Height above the hips scales the sway, so the figure leans from the feet like a
                // body rather than sliding sideways like a cut-out.
                float lever = Mathf.Max(0f, p.y + 0.9f) / 1.5f;
                p.x = p.x + sway * 0.055f * lever;
                p.z = p.z + drift * 0.030f * lever;
                p.y = p.y + breathe * 0.012f * lever;
                frame.SetLandmark(i, new PoseLandmark(p, 0.95f));
                i = i + 1;
            }

            // Arms float up and down in a slow, offset pair. Done after the base pass so it overrides
            // the swayed positions rather than fighting them.
            RaiseArm(true, Mathf.Sin(armPhase));
            RaiseArm(false, Mathf.Sin(armPhase + 1.9f));

            // Timestamped from this object's OWN accumulated clock rather than Time.realtimeSinceStartup.
            // It is the same number for any caller driving it with real deltas, and it keeps the class
            // free of the player loop so it can be exercised headlessly.
            frame.SetMeta(clock, true);
            // No measured mid-hip: there is no body and therefore no distance to it. Leaving this
            // false is what stops the HUD printing a confident depth for a figure that is not real.
            frame.ClearRootPosition();
            return frame;
        }

        // Swing one arm about its shoulder. Positions rather than rotations, because positions are
        // what this whole show consumes - going through an angle and back would be a retarget, which
        // is precisely the stage F-26 measured as lossy.
        private void RaiseArm(bool left, float phase) {
            int shoulder = left ? 11 : 12;
            int elbow = left ? 13 : 14;
            int wrist = left ? 15 : 16;
            float side = left ? -1f : 1f;
            float lift = 0.5f + 0.5f * phase;              // 0 = by the side, 1 = raised
            Vector3 s = frame.GetLandmark((JointId)shoulder).Position;

            float upper = 0.28f;
            float fore = 0.26f;
            // Sweep the upper arm from hanging (-80 deg) to out and up (+35 deg) measured from
            // straight down, in the shoulder's own side plane.
            float angle = Mathf.Lerp(-80f, 35f, lift) * Mathf.Deg2Rad;
            Vector3 upperDir = new Vector3(side * Mathf.Sin(angle + Mathf.PI * 0.5f) * 0.55f,
                                           -Mathf.Cos(angle), 0f).normalized;
            Vector3 e = s + upperDir * upper;
            // The forearm trails the upper arm, so the elbow reads as bending rather than the arm
            // being one rigid stick. A normalised LERP rather than Vector3.Slerp: the two directions
            // are never near-opposite here so the paths are visually identical, and Slerp is a native
            // engine call, which would tie this class to a running player and make it untestable.
            Vector3 target = new Vector3(side * 0.25f, 0.95f, -0.18f).normalized;
            float blend = 0.35f + 0.35f * lift;
            Vector3 foreDir = (upperDir * (1f - blend) + target * blend).normalized;
            Vector3 w = e + foreDir * fore;

            frame.SetLandmark(elbow, new PoseLandmark(e, 0.95f));
            frame.SetLandmark(wrist, new PoseLandmark(w, 0.95f));
        }

        private static Vector3[] BuildRest() {
            Vector3[] r = new Vector3[PoseFrame.LandmarkCount];
            r[0] = new Vector3(0f, 0.72f, -0.04f);        // nose
            r[11] = new Vector3(-0.18f, 0.50f, 0f);       // left shoulder
            r[12] = new Vector3(0.18f, 0.50f, 0f);        // right shoulder
            r[13] = new Vector3(-0.24f, 0.24f, 0f);       // left elbow
            r[14] = new Vector3(0.24f, 0.24f, 0f);        // right elbow
            r[15] = new Vector3(-0.26f, 0.02f, 0f);       // left wrist
            r[16] = new Vector3(0.26f, 0.02f, 0f);        // right wrist
            r[23] = new Vector3(-0.09f, 0f, 0f);          // left hip
            r[24] = new Vector3(0.09f, 0f, 0f);           // right hip
            r[25] = new Vector3(-0.10f, -0.45f, 0f);      // left knee
            r[26] = new Vector3(0.10f, -0.45f, 0f);       // right knee
            r[27] = new Vector3(-0.10f, -0.88f, 0f);      // left ankle
            r[28] = new Vector3(0.10f, -0.88f, 0f);       // right ankle
            // Heel behind, toe in front: the figure faces -Z, so the toes take the negative offset.
            // Both sit below the ankle, which is what the F-29 foot measurements say a real foot does.
            r[29] = new Vector3(-0.10f, -0.94f, 0.05f);   // left heel
            r[30] = new Vector3(0.10f, -0.94f, 0.05f);    // right heel
            r[31] = new Vector3(-0.10f, -0.95f, -0.13f);  // left toe
            r[32] = new Vector3(0.10f, -0.95f, -0.13f);   // right toe
            return r;
        }
    }

    /// <summary>
    /// Is there a person in front of the installation? Hysteretic, because the raw answer flickers
    /// and a switch driven by it would strobe the whole scene between attract and live.
    ///
    /// The two delays are deliberately very different. ENTERING live is fast — a visitor who steps up
    /// and waits a beat before anything happens concludes it is not working, so the cost of a false
    /// positive (a moment of attract-to-live flicker) is far lower than the cost of hesitation.
    /// LEAVING is slow, because the stream drops poses for ordinary reasons — an occlusion, a turn,
    /// F-21 briefly losing its lock — and dumping a present visitor back to attract mid-interaction
    /// is the worst failure this class can produce.
    /// </summary>
    public sealed class PresenceGate {
        /// <summary>Continuous tracking needed before the installation goes live.</summary>
        public const float EnterSeconds = 0.4f;

        /// <summary>Continuous absence before it gives up and returns to attract. Comfortably longer
        /// than the provider's own 2 s stale failsafe, so this never fires first and re-decides
        /// something the tracking layer is already handling.</summary>
        public const float LeaveSeconds = 3.0f;

        private float trackedFor;
        private float absentFor;

        /// <summary>True when a real person is considered present.</summary>
        public bool Present { get; private set; }

        /// <summary>Set for the single frame on which <see cref="Present"/> changed, so the caller can
        /// log a transition without diffing the flag itself.</summary>
        public bool Changed { get; private set; }

        /// <summary>Seconds until the pending transition, for the HUD. Zero when settled.</summary>
        public float Countdown {
            get {
                if (Present) {
                    return absentFor > 0f ? Mathf.Max(0f, LeaveSeconds - absentFor) : 0f;
                }
                return trackedFor > 0f ? Mathf.Max(0f, EnterSeconds - trackedFor) : 0f;
            }
        }

        public void Update(bool tracked, float deltaSeconds) {
            Changed = false;
            if (tracked) {
                trackedFor = trackedFor + deltaSeconds;
                absentFor = 0f;
                if (!Present && trackedFor >= EnterSeconds) {
                    Present = true;
                    Changed = true;
                }
            } else {
                absentFor = absentFor + deltaSeconds;
                trackedFor = 0f;
                if (Present && absentFor >= LeaveSeconds) {
                    Present = false;
                    Changed = true;
                }
            }
        }

        public void Reset() {
            trackedFor = 0f;
            absentFor = 0f;
            Present = false;
            Changed = false;
        }
    }
}
