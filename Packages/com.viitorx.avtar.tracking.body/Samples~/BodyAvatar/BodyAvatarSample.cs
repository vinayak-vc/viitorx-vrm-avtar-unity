using UnityEngine;

using VirtualMirror.Body;

namespace Viitorx.Tracking.Samples {
    /// <summary>
    /// The body avatar sample: a status box (the engine, the body stream, the avatar, and why the avatar is held when the person is out of range) and one button,
    /// beside a <see cref="BodyBootstrap"/> that does the work: it starts the tracking engine's body sender for the camera, loads the VRM that is its Avatar Asset
    /// with the control rig, and drives it from the person in front of the camera. With the legacy Input Manager the C key does what the button does.
    ///
    /// Needs UniVRM (see the body package's README), the engine with its body model, and a camera in front of where the avatar stands. The source (a webcam by
    /// default, or a clip, a LAN camera, a stream), the engine path and every following option are on the BodyBootstrap.
    /// </summary>
    [AddComponentMenu("Viitorx Tracking Samples/Body Avatar Sample")]
    public sealed class BodyAvatarSample : MonoBehaviour {
        [SerializeField] private bool showControls = true;

        private GUIStyle style;

        private void OnGUI() {
            if (!showControls) {
                return;
            }
            if (style == null) {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                style.wordWrap = true;
                style.fontSize = 13;
            }
            BodyBootstrap bootstrap = BodyBootstrap.Instance;
            if (bootstrap == null) {
                GUI.Box(new Rect(12f, 12f, 640f, 44f), "No BodyBootstrap is running: add one to a GameObject in this scene.", style);
                return;
            }
            GUI.Box(new Rect(12f, 12f, 640f, 96f), bootstrap.StatusText, style);
            if (GUI.Button(new Rect(12f, 114f, 280f, 28f), "Take the neutral again (stand upright)")) {
                bootstrap.Recalibrate();
            }
        }
    }
}
