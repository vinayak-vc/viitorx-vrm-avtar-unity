using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>
    /// Puts the cutout in any scene that has an <see cref="AppBootstrap"/>: it asks the vision host for the cutout stream (which is what starts the
    /// Python sender in Auto mode, and stops it again when this component is disabled) and gives a <see cref="CutoutView"/> the host's textures.
    /// Nothing else: no HUD, no controls, no knowledge of the VRM avatar or of skeletal tracking, so a scene about something else entirely can
    /// show the person in front of the camera next to whatever it already does, or ignore the view and read <see cref="VisionHost.CutoutTextures"/>
    /// itself (a particle emitter, an echo trail, a transition).
    ///
    /// The bootstrap may not exist yet when this component wakes (script order inside a scene is not defined), so it is looked up each frame until
    /// it is there. The camera, and every segmentation setting, are the bootstrap's "Hands and Face" settings or its F6 panel.
    /// </summary>
    [AddComponentMenu("Virtual Mirror/Cutout Stream Binder")]
    public sealed class CutoutStreamBinder : MonoBehaviour {
        [Tooltip("The view that draws the person. Empty: the stream is still requested and the textures are there for any other system to read.")]
        [SerializeField] private CutoutView view;

        private VisionHost host;

        /// <summary>True once the vision host is found and the stream is requested.</summary>
        public bool IsBound {
            get {
                return host != null;
            }
        }

        public CutoutView View {
            get {
                return view;
            }
            set {
                view = value;
            }
        }

        /// <summary>Requests the cutout stream from <paramref name="visionHost"/> and hands its textures to the view. A second call without <see cref="Unbind"/> does nothing.</summary>
        public void Bind(VisionHost visionHost) {
            if (visionHost == null || host != null) {
                return;
            }
            host = visionHost;
            host.AddDemand(VisionStream.Cutout);
            if (view != null) {
                view.Bind(host.CutoutTextures);
            }
        }

        /// <summary>Lets go of the stream (the sender stops when nobody else wants it) and empties the view.</summary>
        public void Unbind() {
            if (host == null) {
                return;
            }
            host.RemoveDemand(VisionStream.Cutout);
            host = null;
            if (view != null) {
                view.Bind(null);
            }
        }

        private void Update() {
            if (host != null) {
                return;
            }
            AppBootstrap bootstrap = AppBootstrap.Instance;
            if (bootstrap != null && bootstrap.Vision != null) {
                Bind(bootstrap.Vision);
            }
        }

        private void OnDisable() {
            Unbind();
        }
    }
}
