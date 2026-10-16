using System;
using System.Diagnostics;
using System.Threading;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Receives the cutout wire (cutout_wire.py) on UDP <see cref="DefaultPort"/> from <c>tools/cutout/cutout_udp_sender.py</c>: a camera picture
    /// and its person mask per frame, each frame arriving as a burst of datagrams. The receive thread reassembles with <see cref="CutoutAssembler"/>;
    /// the main thread takes the newest whole frame with <see cref="TryGetNew"/> once per Update.
    ///
    /// Newest wins, like the hands and face receivers, but a frame is megabytes, so it is handed over by SWAPPING buffers under a lock rather than
    /// copying: the assembler, the hand-over slot and the consumer's frame each keep one buffer set and they rotate. A frame the consumer did not
    /// take before the next one completed is dropped and counted in <see cref="Overwritten"/> (the main thread is slower than the sender; nothing
    /// is queued, because a picture that is late is worse than one that is missing).
    ///
    /// The OS receive queue is 16 MB (<see cref="SocketBufferBytes"/>): a 1280x720 frame is ~62 datagrams of 60 KB that arrive back to back and
    /// the default 64 KB queue would drop most of them.
    /// </summary>
    public sealed class CutoutReceiver : UdpDatagramReceiver {
        public const int DefaultPort = CutoutWire.DefaultPort;
        public const double DefaultStaleSeconds = 0.5;
        public const int SocketBufferBytes = 16 * 1024 * 1024;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly CutoutAssembler assembler = new CutoutAssembler();
        private readonly CutoutFrame readyFrame = new CutoutFrame();
        private readonly Func<double> clockSeconds;
        private readonly Func<double> unixSeconds;
        private readonly double staleSeconds;
        private readonly bool[] rejectWarned = new bool[CutoutWire.RejectKindCount];
        private readonly object gate = new object();

        private bool hasAny;
        private bool hasNew;
        private double lastArrivalSeconds;
        private long accepted;
        private long overwritten;

        public CutoutReceiver() : this(DefaultPort, true, DefaultStaleSeconds, null, null, null) {
        }

        /// <param name="clockSeconds">A monotonic clock for staleness; null for a stopwatch. Injected by tests so staleness needs no sleeping.</param>
        /// <param name="unixSeconds">The wall clock stamped on each frame as <see cref="CutoutFrame.ReceivedUnixSeconds"/>; null for the system clock.</param>
        public CutoutReceiver(int port, bool loopbackOnly, double staleSeconds, ILogService logService, Func<double> clockSeconds, Func<double> unixSeconds)
            : base("CutoutReceiver", port, loopbackOnly, SocketBufferBytes, logService) {
            this.staleSeconds = staleSeconds;
            if (clockSeconds != null) {
                this.clockSeconds = clockSeconds;
            } else {
                Stopwatch stopwatch = Stopwatch.StartNew();
                this.clockSeconds = delegate () {
                    return stopwatch.Elapsed.TotalSeconds;
                };
            }
            if (unixSeconds != null) {
                this.unixSeconds = unixSeconds;
            } else {
                this.unixSeconds = delegate () {
                    return (DateTime.UtcNow - UnixEpoch).TotalSeconds;
                };
            }
        }

        /// <summary>Whole, valid frames received and handed to the consumer's slot. Counted after the slot is updated, so a reader that has seen this grow can already take the frame.</summary>
        public long Accepted {
            get {
                return Interlocked.Read(ref accepted);
            }
        }

        /// <summary>Whole frames dropped because the next one completed before the consumer took them.</summary>
        public long Overwritten {
            get {
                return Interlocked.Read(ref overwritten);
            }
        }

        /// <summary>Unfinished frames dropped for a newer one: datagrams were lost, or the consumer was too slow to drain the socket.</summary>
        public long Abandoned {
            get {
                return assembler.Abandoned;
            }
        }

        public long StaleChunks {
            get {
                return assembler.Stale;
            }
        }

        public long DuplicateChunks {
            get {
                return assembler.Duplicate;
            }
        }

        /// <summary>Datagrams or frames refused, all reasons together.</summary>
        public long Rejected {
            get {
                return assembler.Rejected;
            }
        }

        public long RejectedOf(CutoutReject reason) {
            return assembler.RejectedOf(reason);
        }

        /// <summary>How many times the sender's session id changed after the first frame (the sender restarted).</summary>
        public long SessionChanges {
            get {
                return assembler.SessionChanges;
            }
        }

        /// <summary>Seconds since the last whole frame arrived; infinity before the first.</summary>
        public double AgeSeconds {
            get {
                lock (gate) {
                    return hasAny ? clockSeconds() - lastArrivalSeconds : double.PositiveInfinity;
                }
            }
        }

        public StreamState State {
            get {
                lock (gate) {
                    if (!hasAny) {
                        return StreamState.NoStream;
                    }
                    return clockSeconds() - lastArrivalSeconds <= staleSeconds ? StreamState.Live : StreamState.Stale;
                }
            }
        }

        /// <summary>
        /// Takes the newest frame if one completed since the last call: <paramref name="destination"/> and the receiver exchange buffers, so
        /// <paramref name="destination"/> now holds the frame and whatever it held before is recycled. False (and untouched) when nothing is new.
        /// </summary>
        public bool TryGetNew(CutoutFrame destination) {
            lock (gate) {
                if (!hasNew) {
                    return false;
                }
                destination.SwapWith(readyFrame);
                hasNew = false;
                return true;
            }
        }

        protected override void OnDatagram(byte[] data, int length) {
            bool complete = assembler.Add(data, length);
            if (!complete) {
                CutoutReject reason = assembler.LastReject;
                if (reason != CutoutReject.None && !rejectWarned[(int)reason]) {
                    rejectWarned[(int)reason] = true;
                    Log(LogLevel.Warning, Name + " refused a datagram of " + length + " bytes: " + reason + " (later ones of this kind are counted, not logged)");
                }
                return;
            }
            assembler.Frame.ReceivedUnixSeconds = unixSeconds();
            double now = clockSeconds();
            lock (gate) {
                if (hasNew) {
                    Interlocked.Increment(ref overwritten);
                }
                assembler.SwapFrameWith(readyFrame);
                hasAny = true;
                hasNew = true;
                lastArrivalSeconds = now;
            }
            Interlocked.Increment(ref accepted);
        }
    }
}
