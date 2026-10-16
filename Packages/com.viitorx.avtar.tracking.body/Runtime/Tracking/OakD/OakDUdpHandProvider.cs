using System;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// <see cref="IHandTrackingProvider"/> facade over a running <see cref="OakDUdpPoseProvider"/>. The
    /// OAK-D sidecar streams body + hands (lh/rh) in ONE UDP datagram, so a single socket (owned by the
    /// pose provider) feeds both. This facade just forwards the hand frame the pose provider already
    /// parsed — it does NOT own the socket or thread, so lifecycle calls are no-ops (the pose provider is
    /// started/stopped/disposed by AppBootstrap as the body provider).
    /// </summary>
    public sealed class OakDUdpHandProvider : IHandTrackingProvider {
        private readonly OakDUdpPoseProvider source;

        public OakDUdpHandProvider(OakDUdpPoseProvider source) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }
            this.source = source;
        }

        public bool IsTracking {
            get {
                return source.IsRunning;
            }
        }

        // The pose provider owns the UDP socket + receive thread; nothing to start/stop/dispose here.
        public bool StartTracking() {
            return source.IsRunning;
        }

        public void Tick(float deltaSeconds) {
        }

        public void StopTracking() {
        }

        public bool TryGetFrame(out HandFrame frame) {
            return source.TryGetHandFrame(out frame);
        }

        public void Dispose() {
        }
    }
}
