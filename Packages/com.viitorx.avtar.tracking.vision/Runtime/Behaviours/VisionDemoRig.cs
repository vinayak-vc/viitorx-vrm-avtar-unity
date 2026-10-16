using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// One component that builds the whole demo: add it to an empty GameObject in any scene and press Play. It creates a hands receiver and a face
    /// receiver (ports 8901 and 8902), a skeleton view for each, and the status HUD. Start the Python senders
    /// (<c>python-sidecar~/scripts/run_hands_demo.bat</c>, <c>run_face_demo.bat</c>) and the hands and the face appear at their estimated place in
    /// front of the main camera.
    ///
    /// Everything it makes is a child of this object, so removing the component removes the demo. NOT verified in the Editor.
    /// </summary>
    [AddComponentMenu("Vision/Demo Rig")]
    public sealed class VisionDemoRig : MonoBehaviour {
        [SerializeField] private bool showHands = true;
        [SerializeField] private bool showFace = true;
        [SerializeField] private bool showHud = true;
        [Tooltip("Camera space is relative to this transform. Empty = the main camera.")]
        [SerializeField] private Transform cameraSpace;

        private void Awake() {
            HandsReceiverBehaviour handsReceiver = null;
            FaceReceiverBehaviour faceReceiver = null;
            if (showHands) {
                GameObject hands = new GameObject("Hands");
                hands.transform.SetParent(transform, false);
                handsReceiver = hands.AddComponent<HandsReceiverBehaviour>();
                hands.AddComponent<HandsGizmoView>().Bind(handsReceiver, cameraSpace);
            }
            if (showFace) {
                GameObject face = new GameObject("Face");
                face.transform.SetParent(transform, false);
                faceReceiver = face.AddComponent<FaceReceiverBehaviour>();
                face.AddComponent<FaceGizmoView>().Bind(faceReceiver, cameraSpace);
            }
            if (showHud) {
                GameObject hud = new GameObject("Status HUD");
                hud.transform.SetParent(transform, false);
                hud.AddComponent<VisionStatusHud>().Bind(handsReceiver, faceReceiver);
            }
        }
    }
}
