using System;
using System.Net;
using System.Net.Sockets;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// F-36 — "SOMEBODY ALREADY OWNS THE SIDECAR", as one definition.
    ///
    /// THE FAILURE THIS PREVENTS. Two producers on one UDP port interleave poses from two different
    /// sessions. That does not look like a configuration error — it looks like violent jitter, and
    /// the obvious suspects are the filters, the camera and the tracker, none of which are at fault.
    /// It costs an afternoon every time.
    ///
    /// `sidecar_supervisor.py` already arbitrates this by holding a TCP port for its lifetime, and
    /// <see cref="SidecarProcessLauncher"/> already probes it before spawning. F-36 added a SECOND
    /// thing that can own a sidecar — <c>SidecarBootstrap</c>, which runs the multi-person sender
    /// directly and so never starts a supervisor — and that left a hole: the boot scene held no
    /// lock, so `AppBootstrap` saw a free port and spawned its own supervised sidecar on top.
    /// Launching the avatar app from the demonstration menu produced exactly the two-producer case
    /// the probe exists to prevent.
    ///
    /// So the lock became a thing anyone can HOLD, not only something to probe. Whoever owns a
    /// sidecar holds it; everyone else sees it held and attaches.
    ///
    /// LOOPBACK ONLY, and a bind is the whole mechanism — nothing ever connects or accepts. A TCP
    /// bind is atomic, is released by the kernel even if the process is killed, and needs no file to
    /// go stale. A lock FILE would survive a crash and block the next run, which is the failure mode
    /// this is meant to remove rather than reproduce.
    /// </summary>
    public sealed class SidecarSingleInstanceLock : IDisposable {
        /// <summary>The port `sidecar_supervisor.py` uses for its own single-instance check. Anything
        /// that owns a sidecar must hold THIS one, or the two owners cannot see each other.</summary>
        public const int DefaultPort = 8897;

        private TcpListener listener;

        /// <summary>True while this instance holds the lock.</summary>
        public bool Held {
            get {
                return listener != null;
            }
        }

        /// <summary>
        /// Take the lock. Returns false when somebody else already has it — which is the answer
        /// "there is already a sidecar owner", not an error.
        /// </summary>
        public bool TryAcquire(int port) {
            if (listener != null) {
                return true;
            }
            try {
                TcpListener candidate = new TcpListener(IPAddress.Loopback, port);
                candidate.Start();
                listener = candidate;
                return true;
            } catch (SocketException) {
                return false;
            } catch (Exception) {
                // Unknown failure. Report NOT held so the caller carries on and the supervisor
                // arbitrates - it aborts cleanly and says so on stderr if it loses the race.
                return false;
            }
        }

        /// <summary>Give the lock up. Safe to call twice.</summary>
        public void Release() {
            TcpListener held = listener;
            listener = null;
            if (held == null) {
                return;
            }
            try {
                held.Stop();
            } catch (Exception) {
                // Nothing useful to do; the kernel releases the port with the process regardless.
            }
        }

        public void Dispose() {
            Release();
        }

        /// <summary>
        /// Is anybody holding it? Tested by binding and immediately releasing, which is the same
        /// thing the supervisor does — so the answer means the same on both sides.
        ///
        /// Inherently racy and deliberately so: it answers "was it held a moment ago". The caller
        /// uses it to decide whether to spawn, and a lost race is settled by the supervisor's own
        /// single-instance check.
        /// </summary>
        public static bool IsHeldByAnyone(int port) {
            SidecarSingleInstanceLock probe = new SidecarSingleInstanceLock();
            bool acquired = probe.TryAcquire(port);
            probe.Release();
            return !acquired;
        }
    }
}
