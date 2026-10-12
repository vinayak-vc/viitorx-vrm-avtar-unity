using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace ChromaticRain {
    /// <summary>
    /// Thin adapter between the EXISTING tracking data (a world-space <see cref="SkeletonPose"/>,
    /// the two <see cref="HandPose"/>s and the optional trust channel) and the showcase's view-space
    /// <see cref="ShowcaseBody"/>. It owns no socket and decodes no packet.
    ///
    /// What it adds, and nothing else: mapping to view space, light velocity-adaptive smoothing,
    /// per-joint validity from confidence and trust state, a grace period and fade-out for joints
    /// that drop out, and a snap for the moment a new silhouette starts. A joint that is not seen
    /// is HELD, never moved to the origin and never connected to it.
    /// </summary>
    public sealed class ShowcasePoseAdapter {
        private static readonly int[] BodyJoints = {
            Joint.Nose, Joint.LeftEar, Joint.RightEar,
            Joint.LeftShoulder, Joint.RightShoulder, Joint.LeftElbow, Joint.RightElbow,
            Joint.LeftWrist, Joint.RightWrist, Joint.LeftHip, Joint.RightHip,
            Joint.LeftKnee, Joint.RightKnee, Joint.LeftAnkle, Joint.RightAnkle,
            Joint.LeftHeel, Joint.RightHeel, Joint.LeftToe, Joint.RightToe
        };

        /// <summary>A target further than this from the smoothed joint in one frame is a jump, not
        /// a step, so the joint snaps instead of smearing across the screen.</summary>
        private const float JumpMetres = 1.5f;

        /// <summary>A weight above this counts as a fresh measurement.</summary>
        private const float FreshWeight = 0.35f;

        private readonly float[] lostFor = new float[Joint.SlotCount];
        private readonly bool[] seen = new bool[Joint.SlotCount];
        private readonly Vector2[] rawTarget = new Vector2[Joint.SlotCount];
        private readonly float[] rawWeight = new float[Joint.SlotCount];
        private bool snapPending = true;
        private Vector3 lastCentreWorld = new Vector3(0f, 0.95f, 0f);
        private bool hasCentre;
        private Vector2 previousHip;
        private bool hasPreviousHip;

        /// <summary>The next update with a measurable body places every joint exactly, with no
        /// smoothing from wherever the previous person stood.</summary>
        public void RequestSnap() {
            snapPending = true;
        }

        public void Reset(ShowcaseBody body) {
            body.Clear();
            for (int i = 0; i < Joint.SlotCount; i++) {
                lostFor[i] = 0f;
                seen[i] = false;
            }
            snapPending = true;
            hasCentre = false;
            hasPreviousHip = false;
        }

        public void Update(ShowcaseBody body, SkeletonPose pose, HandPose left, HandPose right,
                           TrackingTelemetry telemetry, bool sourceFresh, float dt,
                           ITrackingTuning s, ScreenSpaceMapper mapper) {
            if (dt <= 0f) {
                return;
            }
            bool usable = sourceFresh && pose != null && pose.Valid;

            // Where the body is, for this frame. A frame without both hips keeps the last good
            // centre rather than inventing one.
            if (usable && pose.Present[Joint.LeftHip] && pose.Present[Joint.RightHip]
                && IsFinite(pose.Centre)) {
                lastCentreWorld = pose.Centre;
                hasCentre = true;
            }
            Vector3 centre = lastCentreWorld;
            bool canMap = usable && hasCentre;

            for (int i = 0; i < Joint.SlotCount; i++) {
                rawWeight[i] = 0f;
            }

            if (canMap) {
                for (int k = 0; k < BodyJoints.Length; k++) {
                    int j = BodyJoints[k];
                    Vector3 world = pose.World[j];
                    if (!pose.Present[j] || !IsFinite(world)) {
                        continue;
                    }
                    float conf = Mathf.InverseLerp(s.MinJointConfidence, s.MinJointConfidence + 0.2f,
                                                   pose.Confidence[j]);
                    rawWeight[j] = conf * TrustFactor(telemetry, j);
                    rawTarget[j] = mapper.Map(world, centre);
                }
                SetHand(Joint.LeftHandEnd, left, pose, centre, mapper);
                SetHand(Joint.RightHandEnd, right, pose, centre, mapper);
            }

            bool snapping = snapPending && canMap;
            float jointFade = Mathf.Max(0.05f, s.JointFadeSeconds);

            // Is the torso still being measured? If it is, a joint that drops out is a SINGLE-JOINT
            // loss: held for the grace period, then its segment shrinks away. If it is not, the whole
            // body is lost: every joint is held exactly as it was and the state machine fades the
            // whole silhouette out, so the shape never collapses limb by limb.
            bool coreNow = canMap && rawWeight[Joint.LeftShoulder] > FreshWeight
                           && rawWeight[Joint.RightShoulder] > FreshWeight
                           && (rawWeight[Joint.LeftHip] > FreshWeight || rawWeight[Joint.RightHip] > FreshWeight);

            for (int j = 0; j < Joint.SlotCount; j++) {
                bool fresh = rawWeight[j] > FreshWeight;
                body.Fresh[j] = fresh;
                if (fresh) {
                    Vector2 target = rawTarget[j];
                    if (snapping || !seen[j] || (target - body.Position[j]).magnitude > JumpMetres) {
                        body.Position[j] = target;
                        body.Velocity[j] = Vector2.zero;
                        body.Weight[j] = snapping ? 1f : body.Weight[j];
                    } else {
                        Vector2 delta = target - body.Position[j];
                        float speed = delta.magnitude / dt;
                        float fast = Mathf.Clamp01(speed / Mathf.Max(0.1f, s.SmoothingFastSpeed));
                        float tau = Mathf.Lerp(s.SmoothingSlowTau, s.SmoothingFastTau, fast);
                        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, tau));
                        Vector2 next = body.Position[j] + delta * a;
                        Vector2 velocity = (next - body.Position[j]) / dt;
                        body.Velocity[j] = Vector2.Lerp(body.Velocity[j], velocity,
                                                        1f - Mathf.Exp(-dt / 0.05f));
                        body.Position[j] = next;
                    }
                    seen[j] = true;
                    lostFor[j] = 0f;
                    body.Weight[j] = Mathf.Min(1f, body.Weight[j] + dt / jointFade);
                } else if (snapping) {
                    // A new person: nothing of the previous one may survive in a joint that this
                    // person has not shown yet.
                    body.Weight[j] = 0f;
                    body.Velocity[j] = Vector2.zero;
                    seen[j] = false;
                    lostFor[j] = 0f;
                } else {
                    lostFor[j] += dt;
                    body.Velocity[j] = body.Velocity[j] * Mathf.Exp(-dt / 0.1f);
                    if (coreNow && lostFor[j] > s.JointGraceSeconds) {
                        body.Weight[j] = Mathf.Max(0f, body.Weight[j] - dt / jointFade);
                    }
                }
            }

            if (snapping) {
                snapPending = false;
                hasPreviousHip = false;
                body.HipVelocity = Vector2.zero;
                body.ShoulderWidth = 0f;
            }

            body.CoreFresh = coreNow;

            UpdateDerived(body, pose, dt);
        }

        private void SetHand(int slot, HandPose hand, SkeletonPose pose, Vector3 centre,
                             ScreenSpaceMapper mapper) {
            if (hand == null || !hand.Valid || hand.Implausible) {
                return;
            }
            Vector3 mcp = hand.World[9];
            Vector3 tip = hand.World[12];
            Vector3 end = Vector3.Lerp(mcp, tip, 0.35f);
            if (!IsFinite(end)) {
                return;
            }
            rawWeight[slot] = 1f;
            rawTarget[slot] = mapper.Map(end, centre);
        }

        private void UpdateDerived(ShowcaseBody body, SkeletonPose pose, float dt) {
            float wl = body.Weight[Joint.LeftHip];
            float wr = body.Weight[Joint.RightHip];
            if (wl > 0.05f || wr > 0.05f) {
                Vector2 hip = (body.Position[Joint.LeftHip] * wl + body.Position[Joint.RightHip] * wr)
                              / (wl + wr);
                body.HipCentre = hip;
                if (hasPreviousHip) {
                    Vector2 v = (hip - previousHip) / dt;
                    body.HipVelocity = Vector2.Lerp(body.HipVelocity, v, 1f - Mathf.Exp(-dt / 0.1f));
                }
                previousHip = hip;
                hasPreviousHip = true;
            } else {
                hasPreviousHip = false;
                body.HipVelocity = body.HipVelocity * Mathf.Exp(-dt / 0.1f);
            }

            if (body.Weight[Joint.LeftShoulder] > 0.5f && body.Weight[Joint.RightShoulder] > 0.5f) {
                float width = Vector2.Distance(body.Position[Joint.LeftShoulder],
                                               body.Position[Joint.RightShoulder]);
                body.ShoulderWidth = body.ShoulderWidth <= 0f
                    ? width
                    : Mathf.Lerp(body.ShoulderWidth, width, 1f - Mathf.Exp(-dt / 0.5f));
            }
            body.Energy = pose != null ? pose.Energy : 0f;
        }

        /// <summary>How far to trust a joint given the pipeline's own verdict. No trust channel
        /// means confidence alone decides.</summary>
        public static float TrustFactor(TrackingTelemetry telemetry, int joint) {
            if (telemetry == null || !telemetry.HasTrustChannel) {
                return 1f;
            }
            JointTrackState state = telemetry.State(joint);
            switch (state) {
                case JointTrackState.Lost:
                    return 0f;
                case JointTrackState.Predicted:
                    return 0.45f;
                case JointTrackState.Weak:
                    return 0.75f;
                case JointTrackState.Recovering:
                    return 0.7f;
                default:
                    return 1f;
            }
        }

        private static bool IsFinite(Vector3 v) {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                     || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
