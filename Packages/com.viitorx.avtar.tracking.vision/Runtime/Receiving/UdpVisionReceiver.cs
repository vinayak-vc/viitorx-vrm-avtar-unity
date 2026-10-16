using System;
using System.Diagnostics;
using System.Threading;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Receives one kind of vision datagram (hands or face) from the Python sidecar over UDP on a background thread, parses it without allocating, and
    /// hands the newest message to the main thread. The socket and the thread are <see cref="UdpDatagramReceiver"/>'s; this class is what a datagram
    /// MEANS: one self-contained message, newest wins.
    ///
    /// The sidecar is a separate process on purpose (ADR-016): a native inference crash can never take the player down. This is only a socket and a
    /// parser; no model, no camera, no native plugin runs in the player.
    ///
    /// THREADING. The receive thread parses into a private worker message, then copies it into a main message under a lock. A consumer calls
    /// <see cref="TryGetNew"/> or <see cref="TryGetLatest"/>, which copy that message into the consumer's own instance under the same lock, so the
    /// consumer never sees a half-written message and never holds a reference the worker will overwrite.
    ///
    /// ORDERING. A datagram whose sequence is not greater than the last accepted one in the same session is counted and dropped (UDP may reorder or
    /// duplicate). A new session id is a new producer: the sequence restarts, and <see cref="SessionChanges"/> counts it so a consumer can drop what
    /// it remembered about ids.
    ///
    /// A datagram that does not parse is counted by reason (<see cref="CountOf"/>) and never throws; the first of each kind is logged once.
    /// </summary>
    public abstract class UdpVisionReceiver<TMessage> : UdpDatagramReceiver where TMessage : VisionMessage {
        private const int SocketBufferBytes = 1 << 20;

        private readonly Func<double> clockSeconds;
        private readonly double staleSeconds;
        private readonly TMessage workerMessage;
        private readonly TMessage mainMessage;
        private readonly long[] resultCounts = new long[ResultKindCount];
        private readonly bool[] resultWarned = new bool[ResultKindCount];
        private readonly object gate = new object();

        private static readonly int ResultKindCount = Enum.GetValues(typeof(WireParseResult)).Length;

        private long accepted;
        private long outOfOrder;
        private long sessionChanges;

        private bool hasAny;
        private bool hasNew;
        private string lastSession;
        private long lastSequence;
        private double lastArrivalSeconds;

        protected UdpVisionReceiver(string name, int port, bool loopbackOnly, double staleSeconds, ILogService logService,
            Func<double> clockSeconds, TMessage workerMessage, TMessage mainMessage) : base(name, port, loopbackOnly, SocketBufferBytes, logService) {
            this.staleSeconds = staleSeconds;
            this.workerMessage = workerMessage;
            this.mainMessage = mainMessage;
            if (clockSeconds != null) {
                this.clockSeconds = clockSeconds;
            } else {
                Stopwatch stopwatch = Stopwatch.StartNew();
                this.clockSeconds = delegate () {
                    return stopwatch.Elapsed.TotalSeconds;
                };
            }
        }

        /// <summary>Datagrams that parsed and were newer than the last: the ones a consumer can see.</summary>
        public long Accepted {
            get {
                return Interlocked.Read(ref accepted);
            }
        }

        /// <summary>Datagrams that parsed but repeated or went backwards in sequence within a session.</summary>
        public long OutOfOrder {
            get {
                return Interlocked.Read(ref outOfOrder);
            }
        }

        /// <summary>How many times the producer session id changed after the first datagram (the sidecar restarted).</summary>
        public long SessionChanges {
            get {
                return Interlocked.Read(ref sessionChanges);
            }
        }

        public long CountOf(WireParseResult result) {
            return Interlocked.Read(ref resultCounts[(int)result]);
        }

        /// <summary>Datagrams that did not parse (everything except <see cref="WireParseResult.Ok"/>).</summary>
        public long Rejected {
            get {
                long total = 0L;
                int i = 1;
                while (i < ResultKindCount) {
                    total = total + Interlocked.Read(ref resultCounts[i]);
                    i = i + 1;
                }
                return total;
            }
        }

        /// <summary>Seconds since the last accepted datagram arrived; infinity before the first.</summary>
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

        protected abstract WireParseResult Parse(byte[] data, int length, TMessage message);

        /// <summary>Copies the newest message into <paramref name="destination"/>. False when nothing has ever arrived.</summary>
        public bool TryGetLatest(TMessage destination) {
            lock (gate) {
                if (!hasAny) {
                    return false;
                }
                destination.CopyFrom(mainMessage);
                hasNew = false;
                return true;
            }
        }

        /// <summary>Copies the newest message into <paramref name="destination"/> only if one arrived since the last call to this or <see cref="TryGetLatest"/>.</summary>
        public bool TryGetNew(TMessage destination) {
            lock (gate) {
                if (!hasAny || !hasNew) {
                    return false;
                }
                destination.CopyFrom(mainMessage);
                hasNew = false;
                return true;
            }
        }

        protected override void OnDatagram(byte[] data, int length) {
            WireParseResult result = Parse(data, length, workerMessage);
            Interlocked.Increment(ref resultCounts[(int)result]);
            if (result != WireParseResult.Ok) {
                if (!resultWarned[(int)result]) {
                    resultWarned[(int)result] = true;
                    Log(LogLevel.Warning, Name + " rejected a datagram of " + length + " bytes: " + result + " (later ones of this kind are counted, not logged)");
                }
                return;
            }
            bool newSession = !string.Equals(workerMessage.SessionId, lastSession, StringComparison.Ordinal);
            if (newSession) {
                if (lastSession != null) {
                    Interlocked.Increment(ref sessionChanges);
                }
                lastSession = workerMessage.SessionId;
            } else if (workerMessage.Sequence <= lastSequence) {
                Interlocked.Increment(ref outOfOrder);
                return;
            }
            lastSequence = workerMessage.Sequence;
            double now = clockSeconds();
            lock (gate) {
                mainMessage.CopyFrom(workerMessage);
                hasAny = true;
                hasNew = true;
                lastArrivalSeconds = now;
            }
            Interlocked.Increment(ref accepted);
        }
    }
}
