using System;
using System.Net;
using System.Net.Sockets;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// The two checks every code path that starts a sidecar runs first, written once: is somebody already
    /// sending on the pose port, and does a LAN camera source have the PIN it cannot join without.
    /// </summary>
    public static class SidecarGuards {
        public const int DefaultProbeMilliseconds = 400;

        /// <summary>
        /// Listens briefly on <paramref name="port"/> for a datagram. Binding and waiting tests the thing that
        /// matters (is somebody sending) rather than a proxy such as a lock file, which a hand-started sender
        /// does not create. Returns false on ANY failure, including the port being unavailable: the caller then
        /// tries to start, and a real conflict surfaces as the sidecar's own error instead of this silently
        /// deciding not to start.
        /// </summary>
        public static bool ProducerAlreadyRunning(int port, int timeoutMilliseconds) {
            UdpClient probe = null;
            try {
                probe = new UdpClient();
                probe.Client.ExclusiveAddressUse = false;
                probe.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                probe.Client.ReceiveTimeout = timeoutMilliseconds;
                probe.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = probe.Receive(ref from);
                return data != null && data.Length > 0;
            } catch (SocketException) {
                return false;
            } catch (ObjectDisposedException) {
                return false;
            } finally {
                if (probe != null) {
                    probe.Close();
                }
            }
        }

        public static bool ProducerAlreadyRunning(int port) {
            return ProducerAlreadyRunning(port, DefaultProbeMilliseconds);
        }

        /// <summary>
        /// True when <paramref name="resolvedSource"/> is a LAN camera and neither the typed PIN nor the
        /// <see cref="SidecarProcessLauncher.LanCameraPinEnvironmentVariable"/> environment variable holds one.
        /// A sidecar started without it exits at once and is then restarted by the watchdog forever, so callers
        /// say so in one line instead.
        /// </summary>
        public static bool LanCameraNeedsPin(string resolvedSource, string typedPin) {
            if (VideoSourceSpec.KindOf(resolvedSource) != VideoSourceKind.LanCamera) {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(typedPin)) {
                return false;
            }
            return string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(SidecarProcessLauncher.LanCameraPinEnvironmentVariable));
        }
    }
}
