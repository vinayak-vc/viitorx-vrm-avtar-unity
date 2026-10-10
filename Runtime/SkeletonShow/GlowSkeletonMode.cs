using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 MODE 1 — THE SKELETON IS THE CHARACTER. Glowing bone lines, joint cores, and trails off
    /// the hands and feet, coloured by how fast each part is moving.
    ///
    /// THE POINT. The debug skeleton already tracks the user correctly (F-26 §2); what makes it read
    /// as a debug overlay rather than a character is entirely presentation — uniform dots, flat lines,
    /// no response to motion. Nothing about the DATA needs to change. This mode takes the identical
    /// joint positions and dresses them, and it is the cheapest of the three because it adds no
    /// geometry at all: 16 line renderers, 33 spheres, 4 trails.
    ///
    /// Line WIDTH carries confidence and speed together: a joint the tracker is unsure of thins out
    /// rather than vanishing, so a dropout reads as the body fading rather than a limb snapping off.
    /// That is the same event P0-1 handles downstream by holding a rotation — here it is simply made
    /// legible instead of hidden.
    /// </summary>
    public sealed class GlowSkeletonMode : ISkeletonMode {
        private const float BaseWidth = 0.018f;
        private const float JointSize = 0.035f;

        private Transform root;
        private SkeletonShowPalette palette;
        private LineRenderer[] bones;
        private Transform[] joints;
        private Material[] boneMaterials;
        private Material[] jointMaterials;
        private TrailRenderer[] trails;
        private Transform groundPool;
        private Material groundPoolMaterial;

        /// <summary>Floor-pool radius in metres, and how far below the body's brightness it
        /// sits. See <see cref="ShowStage.GroundPool"/> for why depth gets its own object.</summary>
        private const float GroundPoolRadius = 0.26f;
        private const float GroundPoolDim = 0.25f;

        public string Name {
            get {
                return "1  GLOW SKELETON";
            }
        }

        public string Description {
            get {
                return "the skeleton as the character - lines, cores and trails, coloured by speed";
            }
        }

        public void Activate(Transform parent, SkeletonShowPalette sharedPalette) {
            root = new GameObject("Mode_GlowSkeleton").transform;
            root.SetParent(parent, false);
            palette = sharedPalette;
            groundPool = ShowStage.GroundPool(root, palette, out groundPoolMaterial);

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
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                boneMaterials[i] = palette.NewAdditive(palette.Cool);
                lr.sharedMaterial = boneMaterials[i];
                bones[i] = lr;
                i = i + 1;
            }

            joints = new Transform[PoseFrame.LandmarkCount];
            jointMaterials = new Material[PoseFrame.LandmarkCount];
            i = 0;
            while (i < PoseFrame.LandmarkCount) {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "joint_" + i;
                Collider collider = dot.GetComponent<Collider>();
                if (collider != null) {
                    Object.Destroy(collider);
                }
                dot.transform.SetParent(root, false);
                dot.transform.localScale = Vector3.one * JointSize;
                MeshRenderer mr = dot.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                jointMaterials[i] = palette.NewOpaque(palette.Cool);
                mr.sharedMaterial = jointMaterials[i];
                joints[i] = dot.transform;
                i = i + 1;
            }

            trails = new TrailRenderer[SkeletonPose.Extremities.Length];
            i = 0;
            while (i < trails.Length) {
                GameObject go = new GameObject("trail_" + SkeletonPose.Extremities[i]);
                go.transform.SetParent(root, false);
                TrailRenderer tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.45f;
                tr.startWidth = 0.05f;
                tr.endWidth = 0f;
                tr.minVertexDistance = 0.01f;
                tr.autodestruct = false;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.sharedMaterial = palette.NewAdditive(palette.Warm);
                trails[i] = tr;
                i = i + 1;
            }
        }

        public void Deactivate() {
            if (root != null) {
                Object.Destroy(root.gameObject);
                root = null;
            }
            bones = null;
            joints = null;
            trails = null;
            groundPool = null;
            groundPoolMaterial = null;
            boneMaterials = null;
            jointMaterials = null;
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
                    // Width carries trust AND motion: an uncertain limb thins instead of disappearing,
                    // so a dropout reads as fading rather than as a limb being torn off.
                    float width = BaseWidth * (0.45f + 0.55f * Mathf.Clamp01(trust)) * (1f + heat * 0.9f);
                    bones[i].startWidth = width;
                    bones[i].endWidth = width;
                    SkeletonShowPalette.SetColour(boneMaterials[i], palette.Heat(heat));
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
                    float size = JointSize * (0.8f + 0.9f * heat) * (0.5f + 0.5f * pose.Confidence[i]);
                    joints[i].localScale = Vector3.one * size;
                    SkeletonShowPalette.SetColour(jointMaterials[i], palette.Heat(heat));
                }
                i = i + 1;
            }

            i = 0;
            while (i < trails.Length) {
                int joint = SkeletonPose.Extremities[i];
                bool present = pose.Present[joint];
                trails[i].gameObject.SetActive(present);
                if (present) {
                    trails[i].transform.position = pose.World[joint];
                    float heat = pose.Heat(joint);
                    trails[i].startWidth = 0.02f + 0.06f * heat;
                    trails[i].time = 0.25f + 0.35f * heat;
                    SkeletonShowPalette.SetColour(trails[i].sharedMaterial, palette.Heat(heat));
                } else {
                    // Clear rather than leave the ribbon hanging in space: a trail whose owner vanished
                    // is the one artefact that makes this look broken rather than stylised.
                    trails[i].Clear();
                }
                i = i + 1;
            }

            // WHERE THIS BODY IS STANDING. Anchored on the mid-hip rather than the feet: the feet are
            // the truer contact point and much the noisier one, and a pool that jumps when an ankle
            // drops out draws the eye to the failure instead of to the person.
            if (groundPool != null) {
                bool standing = pose.Present[(int)JointId.LeftHip]
                                && pose.Present[(int)JointId.RightHip];
                groundPool.gameObject.SetActive(standing);
                if (standing) {
                    Vector3 ground = 0.5f * (pose.World[(int)JointId.LeftHip]
                                             + pose.World[(int)JointId.RightHip]);
                    ShowStage.PlaceGroundPool(groundPool, ground, GroundPoolRadius,
                                              pose.HasFloor ? pose.FloorY : 0f);
                    SkeletonShowPalette.SetColour(groundPoolMaterial,
                                                  palette.Cool * GroundPoolDim);
                }
            }
        }
    }
}
