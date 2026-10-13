using UnityEngine;

using VirtualMirror.App;
using VirtualMirror.SkeletonShow;

namespace HandsFaceShowcase {
    /// <summary>
    /// What both sample scenes share: finding the vision host that <see cref="AppBootstrap"/> owns, asking it for the stream this scene shows
    /// (which is what starts the Python sender in Auto mode), and letting go of it when the scene ends. A scene never opens a socket or a process
    /// of its own: one UDP port has one reader, and the sender is part of the bootstrap's settings and control panel (F6).
    ///
    /// The bootstrap may not exist yet when this component wakes (script order inside a scene is not defined), so the host is looked up each frame
    /// until it is there.
    /// </summary>
    public abstract class VisionShowcaseBase : MonoBehaviour {
        private VirtualMirror.App.VisionHost host;

        /// <summary>The stream this scene shows.</summary>
        protected abstract VisionStream Stream { get; }

        /// <summary>The host, or null until the bootstrap is up.</summary>
        protected VirtualMirror.App.VisionHost Host {
            get {
                return host;
            }
        }

        /// <summary>Called once, on the main thread, when the host is found. Subscribe to its events here.</summary>
        protected abstract void OnBound(VirtualMirror.App.VisionHost visionHost);

        /// <summary>Called once when the host is let go (this component is disabled). Unsubscribe here.</summary>
        protected abstract void OnUnbound(VirtualMirror.App.VisionHost visionHost);

        protected virtual void Update() {
            if (host == null) {
                TryBind();
            }
            // ESC returns to the Launcher, as in every experience scene; nothing happens when the scene was opened on its own.
            if (LauncherScene.IsAvailable && LauncherScene.ReturnRequested()) {
                LauncherScene.Load();
            }
        }

        protected virtual void OnDisable() {
            Unbind();
        }

        private void TryBind() {
            AppBootstrap bootstrap = AppBootstrap.Instance;
            if (bootstrap == null || bootstrap.Vision == null) {
                return;
            }
            host = bootstrap.Vision;
            host.AddDemand(Stream);
            OnBound(host);
        }

        private void Unbind() {
            if (host == null) {
                return;
            }
            OnUnbound(host);
            host.RemoveDemand(Stream);
            host = null;
        }

        /// <summary>The camera the scene is seen through: the one given, else the main camera.</summary>
        protected static Camera ResolveCamera(Camera given) {
            return given != null ? given : Camera.main;
        }
    }
}
