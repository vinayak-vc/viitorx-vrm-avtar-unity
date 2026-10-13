using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Drives a character from the face stream: the 52 blendshape scores onto the ARKit-named blendshapes of one or more meshes, and, optionally, the
    /// head bone's rotation from the head pose. Add it next to a <see cref="FaceReceiverBehaviour"/> (or point it at one).
    ///
    /// Blendshapes are matched by NAME (<see cref="ArkitBlendshapeMap"/>); a mesh with none of the names is reported once in the console. Scores are
    /// 0..1 and Unity's blendshape weights 0..100, so <c>gain</c> 1 maps exactly. The sidecar already smooths the scores, so none is added here.
    ///
    /// The head: <see cref="Quaternion"/> identity from the sidecar means a frontal face looking at the camera. <c>headRotationOffset</c> (Euler degrees)
    /// is applied after it to fit your rig's bone axes (a character that faces +z needs 180 about y). <c>referenceSpace</c> is the transform the head
    /// pose is relative to: the camera that watches the user, or the character's root. NOT verified against a particular rig in the Editor.
    /// </summary>
    [AddComponentMenu("Vision/ARKit Blendshape Driver")]
    public sealed class ArkitBlendshapeDriver : MonoBehaviour {
        [Header("Source")]
        [SerializeField] private FaceReceiverBehaviour source;

        [Header("Blendshapes")]
        [SerializeField] private SkinnedMeshRenderer[] targets;
        [Tooltip("Multiplier on every score before it becomes a 0-100 weight.")]
        [SerializeField] private float gain = 1f;
        [Tooltip("Swap Left and Right: the character is a mirror of the user. The wire's sides are the user's own.")]
        [SerializeField] private bool mirror;
        [Tooltip("Set every shape to zero when no face is in view, instead of holding the last expression.")]
        [SerializeField] private bool resetWhenNoFace = true;

        [Header("Head")]
        [SerializeField] private Transform head;
        [Tooltip("The transform the head pose is relative to (a camera, or the character root). Empty = world space.")]
        [SerializeField] private Transform referenceSpace;
        [SerializeField] private Vector3 headRotationOffset = Vector3.zero;
        [Range(0f, 1f)]
        [SerializeField] private float headWeight = 1f;

        private int[][] maps;
        private bool subscribed;

        private void OnEnable() {
            if (source == null) {
                source = GetComponent<FaceReceiverBehaviour>();
            }
            BuildMaps();
            if (source != null) {
                source.Updated += OnFace;
                subscribed = true;
            }
        }

        private void OnDisable() {
            if (subscribed && source != null) {
                source.Updated -= OnFace;
            }
            subscribed = false;
        }

        /// <summary>Rebuilds the name matching. Call after changing <c>targets</c> or <c>mirror</c> at run time.</summary>
        public void BuildMaps() {
            int count = targets == null ? 0 : targets.Length;
            maps = new int[count][];
            int i = 0;
            while (i < count) {
                maps[i] = BuildMap(targets[i]);
                i = i + 1;
            }
        }

        private int[] BuildMap(SkinnedMeshRenderer renderer) {
            if (renderer == null || renderer.sharedMesh == null) {
                return ArkitBlendshapeMap.Build(null, mirror);
            }
            Mesh mesh = renderer.sharedMesh;
            string[] names = new string[mesh.blendShapeCount];
            int i = 0;
            while (i < names.Length) {
                names[i] = mesh.GetBlendShapeName(i);
                i = i + 1;
            }
            int[] map = ArkitBlendshapeMap.Build(names, mirror);
            if (ArkitBlendshapeMap.CountMapped(map) == 0) {
                Debug.LogWarning("[Vision] " + renderer.name + " has no ARKit-named blendshapes (eyeBlinkLeft, jawOpen, ...); the face stream cannot drive it.", renderer);
            }
            return map;
        }

        private void OnFace(FaceMessage message) {
            if (message.Count < 1) {
                if (resetWhenNoFace) {
                    Apply(null);
                }
                return;
            }
            FaceData face = message.Face(0);
            Apply(face.HasBlendshapes ? face.Blendshapes : null);
            if (head != null && face.PoseValid && headWeight > 0f) {
                Quaternion pose = face.HeadRotation * Quaternion.Euler(headRotationOffset);
                Quaternion world = referenceSpace != null ? referenceSpace.rotation * pose : pose;
                head.rotation = headWeight >= 1f ? world : Quaternion.Slerp(head.rotation, world, headWeight);
            }
        }

        private void Apply(float[] scores) {
            if (maps == null || targets == null) {
                return;
            }
            int t = 0;
            while (t < maps.Length) {
                SkinnedMeshRenderer renderer = targets[t];
                if (renderer != null) {
                    int[] map = maps[t];
                    int i = 1;
                    while (i < map.Length) {
                        if (map[i] != ArkitBlendshapeMap.NoShape) {
                            float score = scores == null ? 0f : scores[i];
                            renderer.SetBlendShapeWeight(map[i], Mathf.Clamp01(score * gain) * 100f);
                        }
                        i = i + 1;
                    }
                }
                t = t + 1;
            }
        }
    }
}
