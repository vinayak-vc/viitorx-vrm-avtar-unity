using UnityEngine;

using VirtualMirror.App;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Base of a component that SHOWS or USES the streams of the running host: it finds the host in <see cref="VisionHostRegistry"/> (the bootstrap may
    /// wake later than this component, so it keeps looking every frame), asks it for the streams this component needs (in mode Auto that is what starts
    /// the engine for them) and lets go of them when it is disabled or the host goes away. A client never opens a socket or a process of its own: one
    /// UDP port has one reader, and the engine belongs to the host's settings and control panel.
    /// </summary>
    public abstract class VisionHostClient : MonoBehaviour {
        private VisionHost host;
        private VisionStream[] bound = new VisionStream[0];

        /// <summary>The streams this component needs.</summary>
        protected abstract VisionStream[] Streams { get; }

        /// <summary>The host, or null until the bootstrap is up.</summary>
        protected VisionHost Host {
            get {
                return host;
            }
        }

        /// <summary>Called once, on the main thread, when the host is found. Subscribe to its events here.</summary>
        protected abstract void OnBound(VisionHost visionHost);

        /// <summary>Called once when the host is let go. Unsubscribe here.</summary>
        protected abstract void OnUnbound(VisionHost visionHost);

        protected virtual void Update() {
            if (host != null && VisionHostRegistry.Current != host) {
                Unbind();
            }
            if (host == null) {
                TryBind();
            }
        }

        protected virtual void OnDisable() {
            Unbind();
        }

        private void TryBind() {
            VisionHost found = VisionHostRegistry.Current;
            if (found == null) {
                return;
            }
            host = found;
            bound = Streams;
            for (int i = 0; i < bound.Length; i++) {
                host.AddDemand(bound[i]);
            }
            OnBound(host);
        }

        private void Unbind() {
            if (host == null) {
                return;
            }
            VisionHost leaving = host;
            host = null;
            OnUnbound(leaving);
            for (int i = 0; i < bound.Length; i++) {
                leaving.RemoveDemand(bound[i]);
            }
            bound = new VisionStream[0];
        }
    }
}
