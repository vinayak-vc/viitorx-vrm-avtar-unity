using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Rendering {
    /// <summary>
    /// Live, line-rendered debug skeleton of the tracked <see cref="PoseFrame"/>, drawn as LineRenderer
    /// bones + joint dots in this GameObject's local space so it can be placed beside the avatar. It lets
    /// the RAW tracking data be validated by eye against the retargeted VRM: if the skeleton tracks the
    /// user cleanly but the avatar does not, the retarget is at fault; if the skeleton itself is wrong, the
    /// tracking/sidecar is. Purely diagnostic — no gameplay logic. Driven each frame by AppBootstrap via
    /// <see cref="Render"/>; drop it (or toggle showDebugSkeleton off) to disable.
    /// </summary>
    public sealed class PoseDebugSkeleton : MonoBehaviour {
        [SerializeField] private float scale = 1f;
        [SerializeField] private float boneWidth = 0.02f;
        [SerializeField] private float jointSize = 0.04f;
        [SerializeField] private Color boneColor = new Color(0.2f, 1f, 0.4f);
        [SerializeField] private Color jointColor = new Color(0.3f, 0.8f, 1f);
        [SerializeField] private Color handColor = new Color(1f, 0.3f, 0.8f);

        // BlazePose-33 bone connections by JointId index (flattened pairs): shoulders, both arms, spine
        // sides, hips, both legs, and nose->shoulders. Matches the topology the retarget consumes.
        private static readonly int[] BonePairs = {
            11, 12, 11, 13, 13, 15, 12, 14, 14, 16, 11, 23, 12, 24, 23, 24,
            23, 25, 25, 27, 27, 31, 24, 26, 26, 28, 28, 32, 0, 11, 0, 12
        };

        private Transform[] joints;
        private LineRenderer[] bones;
        private bool built;

        /// <summary>Update the skeleton from a tracked frame. Hides everything on a null/invalid frame.</summary>
        public void Render(PoseFrame frame) {
            if (frame == null || !frame.IsValid) {
                if (built) {
                    SetAllVisible(false);
                }
                return;
            }
            if (!built) {
                Build();
            }

            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                PoseLandmark landmark = frame.GetLandmark((JointId)index);
                bool present = landmark.Confidence > 0.001f;
                joints[index].gameObject.SetActive(present);
                if (present) {
                    joints[index].localPosition = landmark.Position * scale;
                }
                index = index + 1;
            }

            int boneIndex = 0;
            int pair = 0;
            while (pair < BonePairs.Length) {
                int a = BonePairs[pair];
                int b = BonePairs[pair + 1];
                PoseLandmark la = frame.GetLandmark((JointId)a);
                PoseLandmark lb = frame.GetLandmark((JointId)b);
                bool present = la.Confidence > 0.001f && lb.Confidence > 0.001f;
                bones[boneIndex].enabled = present;
                if (present) {
                    bones[boneIndex].SetPosition(0, la.Position * scale);
                    bones[boneIndex].SetPosition(1, lb.Position * scale);
                }
                pair = pair + 2;
                boneIndex = boneIndex + 1;
            }
        }

        private void SetAllVisible(bool visible) {
            int i = 0;
            while (i < joints.Length) {
                if (joints[i] != null) {
                    joints[i].gameObject.SetActive(visible);
                }
                i = i + 1;
            }
            i = 0;
            while (i < bones.Length) {
                if (bones[i] != null) {
                    bones[i].enabled = visible;
                }
                i = i + 1;
            }
        }

        private void Build() {
            Shader shader = Shader.Find("Sprites/Default");
            joints = new Transform[PoseFrame.LandmarkCount];
            int i = 0;
            while (i < joints.Length) {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Collider collider = dot.GetComponent<Collider>();
                if (collider != null) {
                    Destroy(collider);
                }
                dot.name = "joint_" + i;
                dot.transform.SetParent(transform, false);
                dot.transform.localScale = Vector3.one * jointSize;
                MeshRenderer meshRenderer = dot.GetComponent<MeshRenderer>();
                Material material = new Material(shader);
                material.color = (i >= 15 && i <= 22) ? handColor : jointColor; // wrists + hand points stand out
                meshRenderer.sharedMaterial = material;
                dot.SetActive(false);
                joints[i] = dot.transform;
                i = i + 1;
            }

            int boneCount = BonePairs.Length / 2;
            bones = new LineRenderer[boneCount];
            i = 0;
            while (i < boneCount) {
                GameObject boneObject = new GameObject("bone_" + i);
                boneObject.transform.SetParent(transform, false);
                LineRenderer lineRenderer = boneObject.AddComponent<LineRenderer>();
                Material material = new Material(shader);
                material.color = boneColor;
                lineRenderer.material = material;
                lineRenderer.startColor = boneColor;
                lineRenderer.endColor = boneColor;
                lineRenderer.widthMultiplier = boneWidth;
                lineRenderer.positionCount = 2;
                lineRenderer.useWorldSpace = false; // local space -> follows this transform (place beside avatar)
                lineRenderer.numCapVertices = 2;
                lineRenderer.enabled = false;
                bones[i] = lineRenderer;
                i = i + 1;
            }
            built = true;
        }
    }
}
