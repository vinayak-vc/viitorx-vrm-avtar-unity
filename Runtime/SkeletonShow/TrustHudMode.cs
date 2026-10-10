using System.Text;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-29 MODE 4 — THE SYSTEM'S OWN REASONING, MADE VISIBLE.
    ///
    /// THE POINT. P1-1's joint tracker, P1-4's kinematic recovery, F-22's pose validation and F-21's
    /// target ownership decide something about every joint of every frame. All of that reached a log
    /// file and nothing else. On screen a confidently measured elbow and one being extrapolated
    /// through an occlusion were the same white dot — so a viewer could not tell a working system
    /// from a lucky one, and neither could an operator standing next to it. This mode draws the
    /// difference.
    ///
    /// THE COLOUR RULE IS DELIBERATELY BROKEN HERE, and this is the one mode allowed to break it.
    /// Everywhere else in the show colour means SPEED (see <see cref="SkeletonShowPalette"/>). Here
    /// colour means TRUST, because the whole subject of the mode is what the pipeline believes. Two
    /// meanings for one channel in one mode would be unreadable, so speed is dropped entirely rather
    /// than encoded somewhere else and half-noticed. The legend is always on screen for that reason —
    /// a viewer arriving at this mode must be told the mapping changed.
    ///
    /// WHAT IS REAL AND WHAT IS DRAWN. Every state shown here is the sidecar's own, echoed on the
    /// wire; nothing is inferred in Unity and nothing here changes what is tracked or drawn. If this
    /// mode shows a joint as PREDICTED it is because P1-1 said so on the frame that produced that
    /// geometry.
    ///
    /// HOW TO DEMONSTRATE IT: have the subject occlude one arm behind their back. The occluded
    /// joints go WEAK, then PREDICTED as P1-1 extrapolates, then LOST if the occlusion outlasts the
    /// prediction horizon; on re-exposure P1-4 blends them back as RECOVERING. That sequence is the
    /// most expensive engineering in the project and it was, until now, completely invisible.
    /// </summary>
    public sealed class TrustHudMode : ISkeletonMode {
        // Trust colours, HDR like the rest of the show so they bloom. Chosen to survive the grader:
        // green/amber/blue/red read as distinct hues even after ACES tonemapping compresses the top
        // end, which a green/yellow ramp would not.
        private static readonly Color Trusted = new Color(0.25f, 2.10f, 0.60f, 1f);
        private static readonly Color Uncertain = new Color(2.30f, 1.30f, 0.15f, 1f);
        private static readonly Color Extrapolated = new Color(0.30f, 1.20f, 2.40f, 1f);
        private static readonly Color Gone = new Color(2.40f, 0.30f, 0.30f, 1f);
        private static readonly Color Reacquiring = new Color(1.60f, 0.60f, 2.40f, 1f);
        private static readonly Color Unmonitored = new Color(0.35f, 0.42f, 0.60f, 1f);

        private const float BoneWidth = 0.016f;
        private const float JointSize = 0.045f;

        /// <summary>Above this the newest pose is reported as an AGE rather than a latency. Set to the
        /// provider's own StaleHold threshold (500 ms) so this HUD and the tracking layer's watchdog
        /// agree about when a stream has stopped, instead of acquiring a second definition of stale.</summary>
        private const float StaleLatencyMs = 500f;

        // Joints whose absolute depth is worth printing. The wrists are where an operator looks to
        // judge whether the sensor resolved the arms in front of or behind the body, which is the
        // failure the OAK-D exists to prevent; the hips are the root everything else is measured from.
        private static readonly int[] DepthProbes = { 15, 16, 23, 24 };
        private static readonly string[] DepthProbeNames = { "L.wrist", "R.wrist", "L.hip", "R.hip" };

        private Transform root;
        private SkeletonShowPalette palette;
        private LineRenderer[] bones;
        private Transform[] joints;
        private Material[] boneMaterials;
        private Material[] jointMaterials;
        private Transform[] fallbackRings;
        private Material[] fallbackMaterials;

        private readonly StringBuilder readout = new StringBuilder(512);

        public string Name {
            get {
                return "4  TRUST HUD";
            }
        }

        public string Description {
            get {
                return "colour means TRUST, not speed - what the pipeline believes about every joint";
            }
        }

        public void Activate(Transform parent, SkeletonShowPalette sharedPalette) {
            root = new GameObject("Mode_TrustHud").transform;
            root.SetParent(parent, false);
            palette = sharedPalette;

            int boneCount = SkeletonPose.Bones.Length / 2;
            bones = new LineRenderer[boneCount];
            boneMaterials = new Material[boneCount];
            int i = 0;
            while (i < boneCount) {
                GameObject go = new GameObject("bone_" + i);
                go.transform.SetParent(root, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 4;
                lr.startWidth = BoneWidth;
                lr.endWidth = BoneWidth;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                boneMaterials[i] = palette.NewAdditive(Unmonitored);
                lr.sharedMaterial = boneMaterials[i];
                bones[i] = lr;
                i = i + 1;
            }

            joints = new Transform[PoseFrame.LandmarkCount];
            jointMaterials = new Material[PoseFrame.LandmarkCount];
            fallbackRings = new Transform[PoseFrame.LandmarkCount];
            fallbackMaterials = new Material[PoseFrame.LandmarkCount];
            i = 0;
            while (i < PoseFrame.LandmarkCount) {
                joints[i] = MakeSphere("joint_" + i, JointSize, out jointMaterials[i], Unmonitored, false);
                // A second, larger, hollow-looking shell drawn only when this joint's depth was NOT
                // measured. Depth provenance is a genuinely separate axis from tracking state - a
                // joint can be perfectly TRACKED on a monocular guess - and encoding it in the same
                // colour would conflate the two. A halo is readable at a glance and does not compete.
                fallbackRings[i] = MakeSphere("fallback_" + i, JointSize * 1.9f,
                                              out fallbackMaterials[i], Unmonitored, true);
                i = i + 1;
            }
        }

        private Transform MakeSphere(string name, float size, out Material material, Color colour, bool additive) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Object.Destroy(collider);
            }
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * size;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            material = additive ? palette.NewAdditive(colour) : palette.NewOpaque(colour);
            mr.sharedMaterial = material;
            return go.transform;
        }

        public void Deactivate() {
            if (root != null) {
                Object.Destroy(root.gameObject);
                root = null;
            }
            bones = null;
            joints = null;
            boneMaterials = null;
            jointMaterials = null;
            fallbackRings = null;
            fallbackMaterials = null;
        }

        public void Render(SkeletonPose pose, float deltaSeconds) {
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(pose.Valid);
            if (!pose.Valid) {
                return;
            }
            TrackingTelemetry telemetry = pose.Telemetry;

            int i = 0;
            while (i < bones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = pose.BoneVisible(a, b);
                bones[i].enabled = visible;
                if (visible) {
                    bones[i].SetPosition(0, pose.World[a]);
                    bones[i].SetPosition(1, pose.World[b]);
                    // The LESS trusted of the two ends wins. A bone is only as good as its worse
                    // endpoint, and showing the average would hide exactly the joint worth seeing.
                    Color colour = Worse(StateColour(telemetry, a), StateColour(telemetry, b));
                    SkeletonShowPalette.SetColour(boneMaterials[i], colour);
                }
                i = i + 1;
            }

            i = 0;
            while (i < joints.Length) {
                bool present = pose.Present[i];
                joints[i].gameObject.SetActive(present);
                if (present) {
                    joints[i].position = pose.World[i];
                    SkeletonShowPalette.SetColour(jointMaterials[i], StateColour(telemetry, i));
                }
                // The fallback halo needs the depth channel to mean anything. Without it every joint
                // would wear one, which would read as "the sensor failed" when the truth is "this
                // sender does not say".
                bool fallback = present && telemetry != null && telemetry.HasDepthChannel
                                && !telemetry.DepthMeasured(i);
                fallbackRings[i].gameObject.SetActive(fallback);
                if (fallback) {
                    fallbackRings[i].position = pose.World[i];
                    SkeletonShowPalette.SetColour(fallbackMaterials[i],
                                                  new Color(0.55f, 0.30f, 0.05f, 0.30f));
                }
                i = i + 1;
            }
        }

        private static Color StateColour(TrackingTelemetry telemetry, int joint) {
            if (telemetry == null || !telemetry.HasTrustChannel) {
                return Unmonitored;
            }
            JointTrackState state = telemetry.State(joint);
            if (state == JointTrackState.Tracked) {
                return Trusted;
            }
            if (state == JointTrackState.Weak) {
                return Uncertain;
            }
            if (state == JointTrackState.Predicted) {
                return Extrapolated;
            }
            if (state == JointTrackState.Lost) {
                return Gone;
            }
            if (state == JointTrackState.Recovering) {
                return Reacquiring;
            }
            return Unmonitored;
        }

        // Rank by how much the pipeline distrusts the joint, so the worse of two endpoints wins.
        private static Color Worse(Color a, Color b) {
            return Rank(a) >= Rank(b) ? a : b;
        }

        private static int Rank(Color c) {
            if (c == Gone) {
                return 5;
            }
            if (c == Extrapolated) {
                return 4;
            }
            if (c == Reacquiring) {
                return 3;
            }
            if (c == Uncertain) {
                return 2;
            }
            if (c == Unmonitored) {
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// The numeric panel, built by the mode rather than the bootstrap because these are this
        /// mode's subject rather than the scene's status. Returned as text for the bootstrap's IMGUI
        /// pass so there is still exactly one place that draws GUI.
        /// </summary>
        public string BuildReadout(SkeletonPose pose, float endToEndLatencyMs) {
            readout.Length = 0;
            TrackingTelemetry telemetry = pose.Telemetry;
            if (telemetry == null || !telemetry.HasTrustChannel) {
                readout.Append("TRUST CHANNEL UNREPORTED - this sidecar does not send 'st'. ");
                readout.Append("Per-joint state is unavailable; nothing below is inferred to fill the gap.");
                return readout.ToString();
            }

            int tracked = 0, weak = 0, predicted = 0, lost = 0, recovering = 0, untracked = 0;
            int i = 0;
            while (i < TrackingTelemetry.JointCount) {
                JointTrackState s = telemetry.State(i);
                if (s == JointTrackState.Tracked) {
                    tracked = tracked + 1;
                } else if (s == JointTrackState.Weak) {
                    weak = weak + 1;
                } else if (s == JointTrackState.Predicted) {
                    predicted = predicted + 1;
                } else if (s == JointTrackState.Lost) {
                    lost = lost + 1;
                } else if (s == JointTrackState.Recovering) {
                    recovering = recovering + 1;
                } else {
                    untracked = untracked + 1;
                }
                i = i + 1;
            }

            readout.Append("JOINTS   trusted ").Append(tracked);
            readout.Append("   weak ").Append(weak);
            readout.Append("   predicted ").Append(predicted);
            readout.Append("   lost ").Append(lost);
            readout.Append("   recovering ").Append(recovering);
            readout.Append("   no-tracker ").Append(untracked);
            readout.Append("   |  valid ").Append(telemetry.ValidJointCount(0.05f)).Append("/33");

            readout.Append("\nDEPTH    ");
            if (telemetry.HasDepthChannel) {
                int measured = telemetry.MeasuredDepthCount();
                readout.Append(measured).Append("/33 MEASURED");
                if (measured == 0) {
                    // Said explicitly because it is the normal state on a video source and would
                    // otherwise look like a broken sensor.
                    readout.Append("  (monocular source - no stereo pair, every joint is inferred)");
                }
            } else {
                readout.Append("UNREPORTED");
            }

            readout.Append("\nPERSON   ");
            readout.Append(telemetry.OwnershipState != null
                           ? telemetry.OwnershipState
                           : "ownership not running (--no-ownership)");

            readout.Append("\nLATENCY  ");
            // A stalled stream makes the newest datagram older every frame, so a latency number would
            // climb forever and read as "the pipeline got slow" when the truth is "the pipeline
            // stopped". Past the threshold the age is reported as an AGE, which is the honest
            // description of what is on screen: a held pose, not a late one.
            if (endToEndLatencyMs > StaleLatencyMs) {
                readout.Append("STREAM STALE - newest pose is ");
                readout.Append((endToEndLatencyMs / 1000f).ToString("F1")).Append(" s old. ");
                readout.Append("The body on screen is being held, not tracked.");
            } else if (telemetry.SidecarLatencyMs >= 0f && endToEndLatencyMs >= 0f) {
                readout.Append("sidecar ").Append(telemetry.SidecarLatencyMs.ToString("F0")).Append(" ms");
                readout.Append("  +  wire/present ").Append(endToEndLatencyMs.ToString("F0")).Append(" ms");
                readout.Append("  =  ").Append((telemetry.SidecarLatencyMs + endToEndLatencyMs).ToString("F0"));
                readout.Append(" ms end to end");
            } else if (endToEndLatencyMs >= 0f) {
                readout.Append("sidecar UNREPORTED   +  wire/present ");
                readout.Append(endToEndLatencyMs.ToString("F0")).Append(" ms");
            } else {
                // The sender omits `t`, so there is no common clock and no age can be computed. Said
                // rather than shown as zero, which would look like a perfect pipeline.
                readout.Append("UNMEASURABLE - this sender stamps no send time");
            }

            readout.Append("\nFLOOR    ");
            if (pose.HasFloor) {
                readout.Append("y=").Append(pose.FloorY.ToString("F3")).Append(" m   planted ");
                int p = 0;
                while (p < pose.Planted.Length) {
                    readout.Append(pose.Planted[p] ? "#" : ".");
                    p = p + 1;
                }
                readout.Append(" (L.heel R.heel L.toe R.toe)");
            } else {
                readout.Append("no foot contact seen yet");
            }

            readout.Append("\nDEPTH@   ");
            i = 0;
            while (i < DepthProbes.Length) {
                int joint = DepthProbes[i];
                readout.Append(DepthProbeNames[i]).Append(' ');
                if (pose.Present[joint]) {
                    // Absolute camera depth, reconstructed the only way it can be: the landmarks are
                    // hip-relative and `xyz` carries the measured mid-hip, so joint depth is the sum.
                    // Printed in metres because that is the unit an operator can check against a tape
                    // measure, which is the entire point of showing it.
                    readout.Append(pose.CameraDepth(joint).ToString("F2")).Append("m");
                    if (telemetry.HasDepthChannel && !telemetry.DepthMeasured(joint)) {
                        readout.Append("~");   // inferred, not measured
                    }
                } else {
                    readout.Append("--");
                }
                readout.Append("   ");
                i = i + 1;
            }
            readout.Append(" (~ = inferred, not measured)");
            return readout.ToString();
        }
    }
}
