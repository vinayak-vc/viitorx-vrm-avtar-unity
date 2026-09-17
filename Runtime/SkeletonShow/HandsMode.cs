using System.Text;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-29 MODE 5 — THE HANDS. All 21 landmarks per hand, the palm's orientation, and the gestures
    /// read from them.
    ///
    /// THE POINT. The sidecar has been streaming 21 landmarks per hand since the whole-body sender
    /// shipped. The Unity provider read all 21, reduced them to five curl scalars for the avatar's
    /// finger bones, and dropped the positions — so nothing in the project could draw a hand, measure
    /// a pinch, or hit-test a fingertip, and the richest channel on the wire was invisible. Nothing
    /// new is being sensed here. It is being shown for the first time.
    ///
    /// WHAT THE MODE DELIBERATELY REFUSES TO DRAW. When a hand's shape fails
    /// <see cref="HandPose"/>'s plausibility gate the skeleton is NOT drawn — a marker is shown
    /// instead. On the seven-person regression clip 40% of hand observations fail that gate - not
    /// because of distance but because the single-person crop is migrating between dancers (F-32),
    /// which is the same thing a genuinely out-of-range hand looks like from here. A 36 cm hand
    /// rendered confidently is worse than no hand at all: it invites a viewer to conclude the
    /// tracking is broken in some interesting way rather than that it is out of range.
    ///
    /// The body skeleton stays drawn, dimmed, behind the hands. Without it the hands float with no
    /// indication of whose they are or where the arms went.
    /// </summary>
    public sealed class HandsMode : ISkeletonMode {
        private const float HandBoneWidth = 0.007f;
        private const float TipSize = 0.018f;
        private const float KnuckleSize = 0.012f;
        private const float BodyBoneWidth = 0.008f;

        private static readonly Color Open = new Color(0.25f, 1.30f, 2.20f, 1f);
        private static readonly Color Pinched = new Color(2.40f, 0.90f, 0.20f, 1f);
        private static readonly Color Rejected = new Color(2.20f, 0.25f, 0.35f, 1f);
        private static readonly Color BodyDim = new Color(0.08f, 0.26f, 0.60f, 1f);

        private Transform root;
        private SkeletonShowPalette palette;

        private LineRenderer[] bodyBones;
        private Material bodyMaterial;

        private readonly HandVisual[] hands = new HandVisual[2];
        private readonly StringBuilder readout = new StringBuilder(256);

        // One hand's renderers, so the two hands cannot accidentally share state.
        private sealed class HandVisual {
            public LineRenderer[] Bones;
            public Material[] BoneMaterials;
            public Transform[] Points;
            public Material[] PointMaterials;
            public LineRenderer PinchLink;
            public Material PinchMaterial;
            public Transform PalmDisc;
            public Material PalmMaterial;
            public Transform RejectMark;
            public Material RejectMaterial;
        }

        public string Name {
            get {
                return "5  HANDS";
            }
        }

        public string Description {
            get {
                return "21 landmarks per hand, palm orientation, pinch and finger count";
            }
        }

        public void Activate(Transform parent, SkeletonShowPalette sharedPalette) {
            root = new GameObject("Mode_Hands").transform;
            root.SetParent(parent, false);
            palette = sharedPalette;

            bodyMaterial = palette.NewAdditive(BodyDim);
            int boneCount = SkeletonPose.Bones.Length / 2;
            bodyBones = new LineRenderer[boneCount];
            int i = 0;
            while (i < boneCount) {
                bodyBones[i] = MakeLine("body_" + i, BodyBoneWidth, bodyMaterial);
                i = i + 1;
            }

            i = 0;
            while (i < 2) {
                hands[i] = BuildHand(i == 0 ? "left" : "right");
                i = i + 1;
            }
        }

        private HandVisual BuildHand(string label) {
            HandVisual visual = new HandVisual();
            int boneCount = RawHandFrame.Bones.Length / 2;
            visual.Bones = new LineRenderer[boneCount];
            visual.BoneMaterials = new Material[boneCount];
            int i = 0;
            while (i < boneCount) {
                visual.BoneMaterials[i] = palette.NewAdditive(Open);
                visual.Bones[i] = MakeLine(label + "_bone_" + i, HandBoneWidth, visual.BoneMaterials[i]);
                i = i + 1;
            }

            visual.Points = new Transform[RawHandFrame.LandmarkCount];
            visual.PointMaterials = new Material[RawHandFrame.LandmarkCount];
            i = 0;
            while (i < RawHandFrame.LandmarkCount) {
                // Fingertips are drawn larger than the rest: they are what an interaction uses, and a
                // uniform dot cloud makes it impossible to tell which point is the one that matters.
                bool isTip = System.Array.IndexOf(RawHandFrame.Fingertips, i) >= 0;
                visual.Points[i] = MakeSphere(label + "_p" + i, isTip ? TipSize : KnuckleSize,
                                              out visual.PointMaterials[i], Open, false);
                i = i + 1;
            }

            visual.PinchMaterial = palette.NewAdditive(Pinched);
            visual.PinchLink = MakeLine(label + "_pinch", 0.006f, visual.PinchMaterial);

            // A flat disc on the palm, oriented by the palm normal. Orientation is invisible on a
            // point cloud - a surface is the only way a viewer sees the hand turn over.
            visual.PalmDisc = MakeCylinder(label + "_palm", out visual.PalmMaterial);
            visual.RejectMark = MakeSphere(label + "_rejected", 0.06f, out visual.RejectMaterial,
                                           Rejected, true);
            return visual;
        }

        private LineRenderer MakeLine(string name, float width, Material material) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = material;
            return lr;
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

        private Transform MakeCylinder(string name, out Material material) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Object.Destroy(collider);
            }
            go.transform.SetParent(root, false);
            go.transform.localScale = new Vector3(0.075f, 0.002f, 0.075f);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            material = palette.NewAdditive(new Color(Open.r * 0.35f, Open.g * 0.35f, Open.b * 0.35f, 0.5f));
            mr.sharedMaterial = material;
            return go.transform;
        }

        public void Deactivate() {
            if (root != null) {
                Object.Destroy(root.gameObject);
                root = null;
            }
            bodyBones = null;
            bodyMaterial = null;
            hands[0] = null;
            hands[1] = null;
        }

        public void Render(SkeletonPose pose, float deltaSeconds) {
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(pose.Valid);
            if (!pose.Valid) {
                return;
            }

            int i = 0;
            while (i < bodyBones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = pose.BoneVisible(a, b);
                bodyBones[i].enabled = visible;
                if (visible) {
                    bodyBones[i].SetPosition(0, pose.World[a]);
                    bodyBones[i].SetPosition(1, pose.World[b]);
                }
                i = i + 1;
            }
        }

        /// <summary>
        /// Hands are rendered separately from <see cref="Render"/> because they arrive on their own
        /// channel: the sidecar omits a hand entirely when it is not confidently tracked, so hand
        /// validity does not follow body validity and the bootstrap supplies them per frame.
        /// </summary>
        public void RenderHands(HandPose left, HandPose right) {
            if (root == null) {
                return;
            }
            DrawHand(hands[0], left);
            DrawHand(hands[1], right);
        }

        private void DrawHand(HandVisual visual, HandPose hand) {
            if (visual == null) {
                return;
            }
            bool draw = hand != null && hand.Valid;
            // The rejection marker replaces the hand rather than accompanying it: a hand that failed
            // the gate has no trustworthy geometry to draw, so there is nothing to put beside it.
            bool reject = hand != null && hand.Implausible;

            int i = 0;
            while (i < visual.Bones.Length) {
                visual.Bones[i].enabled = draw;
                i = i + 1;
            }
            i = 0;
            while (i < visual.Points.Length) {
                visual.Points[i].gameObject.SetActive(draw);
                i = i + 1;
            }
            visual.PinchLink.enabled = draw;
            visual.PalmDisc.gameObject.SetActive(draw);
            visual.RejectMark.gameObject.SetActive(reject);

            if (reject) {
                visual.RejectMark.position = hand.World[RawHandFrame.Wrist];
            }
            if (!draw) {
                return;
            }

            Color colour = hand.Pinching ? Pinched : Open;
            i = 0;
            while (i < visual.Bones.Length) {
                int a = RawHandFrame.Bones[i * 2];
                int b = RawHandFrame.Bones[i * 2 + 1];
                visual.Bones[i].SetPosition(0, hand.World[a]);
                visual.Bones[i].SetPosition(1, hand.World[b]);
                SkeletonShowPalette.SetColour(visual.BoneMaterials[i], colour);
                i = i + 1;
            }

            i = 0;
            while (i < visual.Points.Length) {
                visual.Points[i].position = hand.World[i];
                // An extended finger's tip brightens. That makes the finger COUNT legible on the hand
                // itself rather than only as a number in the corner, which is the difference between
                // a viewer believing it and taking it on trust.
                int finger = System.Array.IndexOf(RawHandFrame.Fingertips, i);
                Color pointColour = colour;
                if (finger >= 0 && !hand.Extended[finger]) {
                    pointColour = new Color(colour.r * 0.22f, colour.g * 0.22f, colour.b * 0.22f, 1f);
                }
                SkeletonShowPalette.SetColour(visual.PointMaterials[i], pointColour);
                i = i + 1;
            }

            visual.PinchLink.SetPosition(0, hand.World[4]);
            visual.PinchLink.SetPosition(1, hand.World[8]);
            SkeletonShowPalette.SetColour(visual.PinchMaterial, colour);

            // The disc sits at the palm centre and lies IN the palm plane. A Unity cylinder's axis is
            // its local Y, so the palm normal must become Y - hence FromToRotation rather than
            // LookRotation, which would align the normal to Z and stand the disc on edge.
            visual.PalmDisc.position = 0.5f * (hand.World[RawHandFrame.Wrist] + hand.World[9]);
            visual.PalmDisc.rotation = Quaternion.FromToRotation(Vector3.up, hand.PalmNormal);
            SkeletonShowPalette.SetColour(visual.PalmMaterial,
                                          new Color(colour.r * 0.30f, colour.g * 0.30f, colour.b * 0.30f, 0.5f));
        }

        /// <summary>One line per hand for the bootstrap's HUD.</summary>
        public string BuildReadout(HandPose left, HandPose right) {
            readout.Length = 0;
            AppendHand(left, "LEFT ");
            readout.Append('\n');
            AppendHand(right, "RIGHT");
            return readout.ToString();
        }

        private void AppendHand(HandPose hand, string label) {
            readout.Append(label).Append("  ");
            if (hand == null || (!hand.Valid && !hand.Implausible)) {
                readout.Append("not tracked (sidecar sends no hand below its confidence gate)");
                return;
            }
            if (hand.Implausible) {
                readout.Append("REJECTED - palm ").Append((hand.PalmMetres * 1000f).ToString("F0"));
                readout.Append(" mm is outside ").Append((HandPose.MinPalmMetres * 1000f).ToString("F0"));
                readout.Append("-").Append((HandPose.MaxPalmMetres * 1000f).ToString("F0"));
                readout.Append(" mm; this is out of range, not a gesture");
                return;
            }
            readout.Append("palm ").Append((hand.PalmMetres * 1000f).ToString("F0")).Append(" mm");
            readout.Append("   thumb-index ").Append((hand.ThumbIndexMetres * 1000f).ToString("F0"));
            readout.Append(" mm (").Append(hand.PinchNormalised.ToString("F2")).Append(" palms)");
            readout.Append("   ").Append(hand.Pinching ? "PINCH" : "open ");
            readout.Append("   fingers ").Append(hand.FingerCount).Append("  [");
            int f = 0;
            while (f < hand.Extended.Length) {
                readout.Append(hand.Extended[f] ? "|" : ".");
                f = f + 1;
            }
            readout.Append("]");
        }
    }
}
