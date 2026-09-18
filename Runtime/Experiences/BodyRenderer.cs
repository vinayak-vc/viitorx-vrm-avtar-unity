using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 — the player's body, drawn the same way in every experience.
    ///
    /// Every experience needs a visible body and none of them is ABOUT the body, so drawing it is
    /// factored out here rather than reinvented four times with four slightly different bone widths.
    /// A player who moves between scenes should recognise themselves.
    ///
    /// It keeps F-28's rule that colour means SPEED, and its reason for putting confidence into line
    /// WIDTH: a joint the tracker is unsure of thins out instead of vanishing, so a dropout reads as
    /// the body fading rather than a limb being torn off.
    ///
    /// <see cref="Dim"/> exists because most experiences want the body present but not competing —
    /// in Bubble Pop the bubbles are the subject, and a full-brightness skeleton in front of them is
    /// just noise.
    /// </summary>
    public sealed class BodyRenderer {
        private const float BaseWidth = 0.016f;
        private const float JointSize = 0.032f;

        /// <summary>Floor-pool radius in metres. Roughly a person's stance, so it reads as where
        /// they are standing rather than as a target painted on the floor.</summary>
        private const float GroundPoolRadius = 0.26f;

        /// <summary>How far below the body's own brightness the pool sits.</summary>
        private const float GroundPoolDim = 0.25f;

        private readonly SkeletonShowPalette palette;
        private Transform root;
        private LineRenderer[] bones;
        private Material[] boneMaterials;
        private Transform[] joints;
        private Material[] jointMaterials;

        /// <summary>Multiplies every colour. 1 = the full glow skeleton, ~0.35 = present but quiet.</summary>
        public float Dim = 1f;

        /// <summary>Multiplied into every colour alongside <see cref="Dim"/>. White is a no-op, which
        /// is the default, so an experience that never asks for a tint is unaffected.</summary>
        private Color activeTint = Color.white;

        /// <summary>Draw the hands' 21 landmarks too, when they are tracked.</summary>
        public bool ShowHands;

        /// <summary>Draw the floor pool that shows where this body is STANDING. See
        /// <see cref="ShowStage.GroundPool"/> for why depth needs its own object rather than a
        /// colour or a width. Off for a body that is not a person standing on the floor — an echo,
        /// a ghost, a recorded replay — because a pool asserts "somebody is here".</summary>
        public bool ShowGroundPool = true;

        private Transform groundPool;
        private Material groundPoolMaterial;

        private LineRenderer[] leftHandBones;
        private LineRenderer[] rightHandBones;
        private Material handMaterial;

        public BodyRenderer(SkeletonShowPalette sharedPalette) {
            palette = sharedPalette;
        }

        public void Build(Transform parent, bool withHands = false) {
            root = new GameObject("Body").transform;
            root.SetParent(parent, false);
            ShowHands = withHands;
            groundPool = ShowStage.GroundPool(root, palette, out groundPoolMaterial);

            int boneCount = SkeletonPose.Bones.Length / 2;
            bones = new LineRenderer[boneCount];
            boneMaterials = new Material[boneCount];
            int i = 0;
            while (i < boneCount) {
                boneMaterials[i] = palette.NewAdditive(palette.Cool);
                bones[i] = ShowStage.Line(root, "bone_" + i, BaseWidth, boneMaterials[i]);
                i = i + 1;
            }

            joints = new Transform[PoseFrame.LandmarkCount];
            jointMaterials = new Material[PoseFrame.LandmarkCount];
            i = 0;
            while (i < joints.Length) {
                joints[i] = ShowStage.Sphere(root, "joint_" + i, JointSize, palette, palette.Cool,
                                             false, out jointMaterials[i]);
                i = i + 1;
            }

            if (withHands) {
                handMaterial = palette.NewAdditive(palette.Warm);
                leftHandBones = BuildHandBones("lhand");
                rightHandBones = BuildHandBones("rhand");
            }
        }

        private LineRenderer[] BuildHandBones(string label) {
            int count = RawHandFrame.Bones.Length / 2;
            LineRenderer[] lines = new LineRenderer[count];
            int i = 0;
            while (i < count) {
                lines[i] = ShowStage.Line(root, label + "_" + i, 0.006f, handMaterial);
                i = i + 1;
            }
            return lines;
        }

        public void Render(SkeletonPose pose) {
            Render(pose, Color.white);
        }

        /// <summary>
        /// F-34 - the same body, every colour multiplied by <paramref name="tint"/>.
        ///
        /// WHY THIS EXISTS AND WHY IT IS AN OVERLOAD RATHER THAN THE ONLY FORM. In a crowd, a body
        /// has to be identifiable as a PERSON, and until now the only way to colour one was
        /// <see cref="RenderRaw"/>, which throws away the speed palette, the confidence-in-line-width
        /// rule and the per-joint heat - so Bonds draws flat bodies purely because that was the only
        /// tinted path available.
        ///
        /// READ THIS BEFORE USING IT. Colour already means SPEED, everywhere, in every mode - that is
        /// the one rule that makes the scenes read as one piece of work. Tinting spends part of that
        /// same channel on identity, and two meanings on one channel is unreadable; Time Echo hit
        /// this and chose to drop speed entirely rather than blend them. So a scene should pick ONE:
        /// either bodies are tinted per person and the heat palette is muted toward white, or the
        /// heat palette is left alone and identity is carried by something else (a line, a marker, a
        /// footprint). A tint near white leaves the palette untouched, which is the default.
        ///
        /// Hands drawn by <see cref="RenderHands"/> inherit the tint most recently passed here, so a
        /// person's fingers match their body rather than being the only untinted part of them.
        /// </summary>
        public void Render(SkeletonPose pose, Color tint) {
            activeTint = tint;
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(pose.Valid);
            if (!pose.Valid) {
                return;
            }

            int i = 0;
            while (i < bones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = pose.BoneVisible(a, b);
                bones[i].enabled = visible;
                if (visible) {
                    bones[i].SetPosition(0, pose.World[a]);
                    bones[i].SetPosition(1, pose.World[b]);
                    float heat = Mathf.Max(pose.Heat(a), pose.Heat(b));
                    float trust = Mathf.Min(pose.Confidence[a], pose.Confidence[b]);
                    float width = BaseWidth * (0.45f + 0.55f * Mathf.Clamp01(trust)) * (1f + heat * 0.7f);
                    bones[i].startWidth = width;
                    bones[i].endWidth = width;
                    SkeletonShowPalette.SetColour(boneMaterials[i], Scale(palette.Heat(heat)));
                }
                i = i + 1;
            }

            i = 0;
            while (i < joints.Length) {
                bool present = pose.Present[i];
                joints[i].gameObject.SetActive(present);
                if (present) {
                    joints[i].position = pose.World[i];
                    float heat = pose.Heat(i);
                    joints[i].localScale = Vector3.one * JointSize * (0.8f + 0.7f * heat);
                    SkeletonShowPalette.SetColour(jointMaterials[i], Scale(palette.Heat(heat)));
                }
                i = i + 1;
            }

            UpdateGroundPool(pose);
        }

        /// <summary>
        /// Put the floor pool where this body is standing.
        ///
        /// ANCHORED ON THE MID-HIP, not on the feet. The feet are the truer contact point and much
        /// the noisier one - they leave the frame, they get occluded by furniture, and a pool that
        /// jumps when an ankle drops out draws the eye to the failure rather than to the person. The
        /// hips are the most reliably tracked pair on the body and are what the whole pose is
        /// authored around, so the pool sits under the body's own origin.
        /// </summary>
        private void UpdateGroundPool(SkeletonPose pose) {
            if (groundPool == null) {
                return;
            }
            bool show = ShowGroundPool
                        && pose.Present[(int)JointId.LeftHip] && pose.Present[(int)JointId.RightHip];
            groundPool.gameObject.SetActive(show);
            if (!show) {
                return;
            }
            Vector3 ground = 0.5f * (pose.World[(int)JointId.LeftHip]
                                     + pose.World[(int)JointId.RightHip]);
            ShowStage.PlaceGroundPool(groundPool, ground, GroundPoolRadius,
                                      pose.HasFloor ? pose.FloorY : 0f);
            // A quarter of the body's brightness: an anchor the eye finds when it looks for it, and
            // never the thing it lands on first.
            SkeletonShowPalette.SetColour(groundPoolMaterial, Scale(palette.Cool) * GroundPoolDim);
        }

        /// <summary>
        /// F-31 — draw a body from RAW arrays with one flat colour, for a recorded pose that is not
        /// the live one.
        ///
        /// WHY A FLAT TINT RATHER THAN THE SPEED PALETTE. An echo's subject is its AGE, and the show
        /// already spends colour on speed. Two meanings on one channel is unreadable, so an echo
        /// spends colour on age and drops speed entirely — the same trade the trust HUD makes, for
        /// the same reason. It also avoids having to store and replay a velocity history purely to
        /// colour something the viewer is meant to read as "the past".
        /// </summary>
        /// <summary>Raw bodies get NO floor pool. A pool says "a person is standing here", and the
        /// callers of this overload are echoes, ghosts and recorded replays - things that are
        /// deliberately not a person standing anywhere.</summary>
        public void RenderRaw(Vector3[] world, float[] confidence, Color tint, float widthScale) {
            if (groundPool != null) {
                groundPool.gameObject.SetActive(false);
            }
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(true);
            int i = 0;
            while (i < bones.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = confidence[a] > 0.05f && confidence[b] > 0.05f;
                bones[i].enabled = visible;
                if (visible) {
                    bones[i].SetPosition(0, world[a]);
                    bones[i].SetPosition(1, world[b]);
                    float width = BaseWidth * widthScale;
                    bones[i].startWidth = width;
                    bones[i].endWidth = width;
                    SkeletonShowPalette.SetColour(boneMaterials[i], tint);
                }
                i = i + 1;
            }
            i = 0;
            while (i < joints.Length) {
                bool present = confidence[i] > 0.05f;
                joints[i].gameObject.SetActive(present);
                if (present) {
                    joints[i].position = world[i];
                    joints[i].localScale = Vector3.one * JointSize * widthScale;
                    SkeletonShowPalette.SetColour(jointMaterials[i], tint);
                }
                i = i + 1;
            }
        }

        /// <summary>Hide everything this renderer owns, without destroying it. An echo whose delay
        /// has not been filled yet must show nothing rather than a body frozen at the origin.</summary>
        public void Hide() {
            if (root != null) {
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Draw the fingers. Separate from <see cref="Render"/> because hands arrive on
        /// their own channel and their validity does not follow the body's.</summary>
        public void RenderHands(HandPose left, HandPose right) {
            if (root == null || !ShowHands) {
                return;
            }
            SkeletonShowPalette.SetColour(handMaterial, Scale(palette.Warm));
            DrawHand(leftHandBones, left);
            DrawHand(rightHandBones, right);
        }

        private static void DrawHand(LineRenderer[] lines, HandPose hand) {
            bool draw = hand != null && hand.Valid;
            int i = 0;
            while (i < lines.Length) {
                lines[i].enabled = draw;
                if (draw) {
                    lines[i].SetPosition(0, hand.World[RawHandFrame.Bones[i * 2]]);
                    lines[i].SetPosition(1, hand.World[RawHandFrame.Bones[i * 2 + 1]]);
                }
                i = i + 1;
            }
        }

        private Color Scale(Color c) {
            return new Color(c.r * Dim * activeTint.r,
                             c.g * Dim * activeTint.g,
                             c.b * Dim * activeTint.b,
                             c.a);
        }
    }
}
