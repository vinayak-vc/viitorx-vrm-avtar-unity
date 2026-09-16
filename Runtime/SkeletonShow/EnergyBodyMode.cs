using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 MODE 2 — A VISUAL SKIN OVER THE SAME SKELETON. Each bone becomes a volume, each joint a
    /// blob, and where they overlap they merge into a single luminous body with a hologram scan
    /// running up it.
    ///
    /// WHY THIS AVOIDS THE HARD PROBLEM. A VRM has to deform a fixed-proportion mesh from ~16 bone
    /// ROTATIONS, which is where F-26 located every avatar failure: the mesh's bone lengths are its
    /// own, so the retarget can only aim a limb and never place it. Here the visual is BUILT from the
    /// joint positions each frame, so there is no skinning, no proportion mismatch and no retarget —
    /// the body cannot be in a pose the tracker did not report. That is the entire trade: a stylised
    /// body instead of a human-looking one, in exchange for the class of error disappearing.
    ///
    /// EACH SEGMENT IS DRAWN TWICE: a tight bright core and a wider dim shell. Two additive layers are
    /// what turn a row of capsules into something that reads as volume rather than as plumbing, and it
    /// costs one extra draw per bone instead of a custom shader.
    /// </summary>
    public sealed class EnergyBodyMode : ISkeletonMode {
        private const float CoreRadius = 0.045f;
        private const float ShellRadius = 0.105f;
        private const float ScanSpeed = 0.45f;      // metres/second up the body
        private const float ScanWidth = 0.22f;      // metres of body the bright band covers

        private Transform root;
        private SkeletonShowPalette palette;
        private Transform[] cores;
        private Transform[] shells;
        private Material[] coreMaterials;
        private Material[] shellMaterials;
        private Transform[] blobs;
        private Material[] blobMaterials;
        private float scan;

        public string Name {
            get {
                return "2  ENERGY BODY";
            }
        }

        public string Description {
            get {
                return "a hologram skin built from the joints - no mesh, no skinning, no retarget";
            }
        }

        public void Activate(Transform parent, SkeletonShowPalette sharedPalette) {
            root = new GameObject("Mode_EnergyBody").transform;
            root.SetParent(parent, false);
            palette = sharedPalette;

            int boneCount = SkeletonPose.Bones.Length / 2;
            cores = new Transform[boneCount];
            shells = new Transform[boneCount];
            coreMaterials = new Material[boneCount];
            shellMaterials = new Material[boneCount];
            int i = 0;
            while (i < boneCount) {
                cores[i] = MakeCapsule("core_" + i, CoreRadius, out coreMaterials[i], palette.Warm, 1f);
                shells[i] = MakeCapsule("shell_" + i, ShellRadius, out shellMaterials[i], palette.Cool, 0.22f);
                i = i + 1;
            }

            // Blobs only on the joints a body actually bulges at. Putting one on all 33 would pack ten
            // spheres into the face and give the head a lumpy halo.
            blobs = new Transform[BlobJoints.Length];
            blobMaterials = new Material[BlobJoints.Length];
            i = 0;
            while (i < BlobJoints.Length) {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "blob_" + BlobJoints[i];
                Collider c = go.GetComponent<Collider>();
                if (c != null) {
                    Object.Destroy(c);
                }
                go.transform.SetParent(root, false);
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                blobMaterials[i] = palette.NewAdditive(Fade(palette.Warm, 0.6f));
                mr.sharedMaterial = blobMaterials[i];
                blobs[i] = go.transform;
                i = i + 1;
            }
        }

        private static readonly int[] BlobJoints = { 0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 };

        private Transform MakeCapsule(string name, float radius, out Material material,
                                      Color colour, float alpha) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) {
                Object.Destroy(c);
            }
            go.transform.SetParent(root, false);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            material = palette.NewAdditive(Fade(colour, alpha));
            mr.sharedMaterial = material;
            go.transform.localScale = new Vector3(radius * 2f, 0.1f, radius * 2f);
            return go.transform;
        }

        private static Color Fade(Color c, float alpha) {
            return new Color(c.r * alpha, c.g * alpha, c.b * alpha, alpha);
        }

        public void Deactivate() {
            if (root != null) {
                Object.Destroy(root.gameObject);
                root = null;
            }
            cores = null;
            shells = null;
            blobs = null;
            coreMaterials = null;
            shellMaterials = null;
            blobMaterials = null;
        }

        public void Render(SkeletonPose pose, float deltaSeconds) {
            if (root == null) {
                return;
            }
            root.gameObject.SetActive(pose.Valid);
            if (!pose.Valid) {
                return;
            }
            // The scan sweeps up the body and wraps. Tied to the body's own height rather than to world
            // space so it looks the same on a tall subject and a short one, and at any distance.
            float bodyHeight = Mathf.Max(0.4f, Vector3.Distance(pose.World[0], pose.Centre) * 1.3f);
            scan = scan + deltaSeconds * ScanSpeed;
            if (scan > bodyHeight) {
                scan = scan - bodyHeight;
            }
            float baseY = pose.Centre.y - bodyHeight * 0.55f;

            int i = 0;
            while (i < cores.Length) {
                int a = SkeletonPose.Bones[i * 2];
                int b = SkeletonPose.Bones[i * 2 + 1];
                bool visible = pose.BoneVisible(a, b);
                cores[i].gameObject.SetActive(visible);
                shells[i].gameObject.SetActive(visible);
                if (visible) {
                    Vector3 pa = pose.World[a];
                    Vector3 pb = pose.World[b];
                    PlaceCapsule(cores[i], pa, pb, CoreRadius);
                    PlaceCapsule(shells[i], pa, pb, ShellRadius);
                    float heat = Mathf.Max(pose.Heat(a), pose.Heat(b));
                    float mid = 0.5f * (pa.y + pb.y);
                    float band = ScanBand(mid - baseY, bodyHeight);
                    Color colour = palette.Heat(heat);
                    SkeletonShowPalette.SetColour(coreMaterials[i], Fade(colour, 0.65f + 0.35f * band));
                    SkeletonShowPalette.SetColour(shellMaterials[i], Fade(colour, 0.10f + 0.28f * band));
                }
                i = i + 1;
            }

            i = 0;
            while (i < blobs.Length) {
                int joint = BlobJoints[i];
                bool present = pose.Present[joint];
                blobs[i].gameObject.SetActive(present);
                if (present) {
                    blobs[i].position = pose.World[joint];
                    float heat = pose.Heat(joint);
                    float r = (joint == 0 ? 0.13f : ShellRadius * 0.95f) * (1f + 0.25f * heat);
                    blobs[i].localScale = Vector3.one * r * 2f;
                    float band = ScanBand(pose.World[joint].y - baseY, bodyHeight);
                    SkeletonShowPalette.SetColour(blobMaterials[i],
                        Fade(palette.Heat(heat), 0.18f + 0.35f * band));
                }
                i = i + 1;
            }
        }

        // 1 inside the travelling band, falling to 0 outside it. Wraps, so the band re-enters at the
        // feet the instant it leaves the head instead of blinking out.
        private float ScanBand(float height, float bodyHeight) {
            float d = Mathf.Abs(height - scan);
            float wrapped = bodyHeight - d;
            float distance = Mathf.Min(d, wrapped);
            return Mathf.Clamp01(1f - distance / ScanWidth);
        }

        // A Unity capsule is 2 units tall with its axis on local Y, so half the distance is the Y
        // scale and the rotation puts local up along the bone.
        private static void PlaceCapsule(Transform t, Vector3 a, Vector3 b, float radius) {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            t.position = a + delta * 0.5f;
            if (length > 1e-5f) {
                t.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            }
            t.localScale = new Vector3(radius * 2f, Mathf.Max(radius, length * 0.5f), radius * 2f);
        }
    }
}
