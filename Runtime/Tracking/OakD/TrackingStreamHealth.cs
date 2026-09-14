namespace VirtualMirror.Tracking.OakD {
    /// <summary>Where the tracking stream actually is, as opposed to whether packets are arriving.</summary>
    public enum TrackingState {
        /// <summary>Nothing has ever been received on the socket.</summary>
        NoStream = 0,
        /// <summary>Datagrams are arriving but no pose has been accepted yet (first contact).</summary>
        Connecting = 1,
        /// <summary>A current-session pose was accepted within the warn threshold.</summary>
        Live = 2,
        /// <summary>No accepted pose for longer than the hold threshold — the last valid pose is held.</summary>
        StaleHold = 3,
        /// <summary>No accepted pose for longer than the failsafe threshold — hold a neutral pose, not a human one.</summary>
        StaleFailsafe = 4,
        /// <summary>Was stale; datagrams are arriving again but none accepted yet (e.g. mid session change).</summary>
        Reconnecting = 5,
        /// <summary>Poses are being accepted again after a stale period; settling before declaring Live.</summary>
        Recovering = 6
    }

    /// <summary>What the consumer must do with its buffer for this datagram.</summary>
    public enum SessionVerdict {
        /// <summary>Same producer session — normal ordering rules apply unchanged.</summary>
        SameSession = 0,
        /// <summary>First session ever seen — initialise.</summary>
        FirstSession = 1,
        /// <summary>A different producer session — flush ordering state and the pose buffer.</summary>
        NewSession = 2,
        /// <summary>
        /// A session we have ALREADY LEFT. Drop the packet entirely: it is a datagram that was still
        /// in flight when its producer died, and adopting it would drag the consumer backwards and
        /// flush a healthy buffer. Found by the F-20A §14 injection test, which sent ten packets from
        /// the previous session after a new one had taken over and saw them adopted.
        /// </summary>
        OldSession = 3
    }

    /// <summary>
    /// F-20A — producer-session identification and stale-stream watchdog for the UDP tracking stream.
    ///
    /// WHY THIS EXISTS. F-19 measured a public-installation-ending failure: restarting the Python
    /// sidecar restarts its <c>seq</c> at 1, P1-3 correctly rejects every packet as out-of-order
    /// (RejectedOutOfOrder = 4948), and the avatar renders the last accepted pose FOREVER — while
    /// <c>IsRunning</c> is true, <c>ReceivedCount</c> climbs and <c>ParseErrors</c> stays 0. One
    /// observed case reached <c>packetAgeMs = 208417</c>. "Packets are arriving" is not "tracking is
    /// healthy", and nothing in the old code could tell the difference.
    ///
    /// WHAT IS AND IS NOT CHANGED. Out-of-order protection is NOT weakened: within a session the
    /// existing rules stand exactly as they were. What is added is the ability to recognise that the
    /// packets now arriving belong to a DIFFERENT producer run, for which the old sequence numbers are
    /// meaningless — and a clock that notices when nothing has been accepted for too long.
    ///
    /// A <c>seq</c> reset is deliberately NOT used as the session identifier: it is exactly the
    /// signature an attacker, a duplicated sender or a wrapped counter would also produce, and
    /// treating it as authoritative would be the same as disabling ordering. The identifier is an
    /// explicit <c>sid</c> field the sidecar generates once per process. The sequence-regression rule
    /// below is a bounded LAST RESORT for senders that predate <c>sid</c> (the deprecated body-only
    /// BlazePose sender), and it requires BOTH a sustained run of regressions AND a quiet period, so a
    /// single stray or duplicated packet can never trigger it.
    ///
    /// THREADING: not thread-safe by itself; the owner serialises access, exactly as
    /// <see cref="VirtualMirror.Core.PoseBuffer"/> does. All times are passed in, so this class is
    /// deterministic and unit-testable without a socket, a clock or a Unity frame.
    /// </summary>
    public sealed class TrackingStreamHealth {
        private readonly double warnSeconds;
        private readonly double holdSeconds;
        private readonly double failsafeSeconds;
        private readonly int legacyRegressionPackets;
        private readonly double legacyRegressionQuietSeconds;
        private readonly double recoverSettleSeconds;

        private string sessionId;
        private bool hasSession;
        // Sessions we have moved on from. Small and bounded: only enough to cover datagrams still in
        // flight across a restart, which is at most a few packets. A uuid4-derived id makes an
        // accidental repeat impossible in practice, so remembering a handful costs nothing.
        private const int RetiredCapacity = 8;
        private readonly string[] retired = new string[RetiredCapacity];
        private int retiredCount;
        private int retiredNext;
        private long lastAcceptedSeq = -1;
        private long lastReceivedSeq = -1;
        private double lastAcceptedTime = -1.0;
        private double lastDatagramTime = -1.0;
        private double recoveringSince = -1.0;
        private int legacyRegressionRun;
        private bool wasStale;

        private long sessionTransitions;
        private long acceptedNewSession;
        private long rejectedOldSession;
        private long datagrams;
        private long accepted;

        public TrackingStreamHealth(double warnSeconds, double holdSeconds, double failsafeSeconds,
                                    int legacyRegressionPackets = 8,
                                    double legacyRegressionQuietSeconds = 0.25,
                                    double recoverSettleSeconds = 0.25) {
            this.warnSeconds = warnSeconds;
            this.holdSeconds = holdSeconds;
            this.failsafeSeconds = failsafeSeconds;
            this.legacyRegressionPackets = legacyRegressionPackets < 2 ? 2 : legacyRegressionPackets;
            this.legacyRegressionQuietSeconds = legacyRegressionQuietSeconds;
            this.recoverSettleSeconds = recoverSettleSeconds;
        }

        public string SessionId {
            get {
                return sessionId;
            }
        }

        public long LastAcceptedSeq {
            get {
                return lastAcceptedSeq;
            }
        }

        public long LastReceivedSeq {
            get {
                return lastReceivedSeq;
            }
        }

        public double LastAcceptedTime {
            get {
                return lastAcceptedTime;
            }
        }

        public long SessionTransitions {
            get {
                return sessionTransitions;
            }
        }

        public long AcceptedNewSession {
            get {
                return acceptedNewSession;
            }
        }

        public long RejectedOldSession {
            get {
                return rejectedOldSession;
            }
        }

        public long Datagrams {
            get {
                return datagrams;
            }
        }

        public long AcceptedPoses {
            get {
                return accepted;
            }
        }

        public double WarnSeconds {
            get {
                return warnSeconds;
            }
        }

        public double HoldSeconds {
            get {
                return holdSeconds;
            }
        }

        public double FailsafeSeconds {
            get {
                return failsafeSeconds;
            }
        }

        /// <summary>
        /// Classify an arriving datagram against the current producer session. Call once per datagram,
        /// BEFORE pushing to the pose buffer; on <see cref="SessionVerdict.NewSession"/> the caller must
        /// flush its buffer and ordering state before the push, so no sample can ever be interpolated
        /// across a session boundary.
        /// </summary>
        /// <param name="incomingSessionId">The packet's <c>sid</c>; null/empty for a legacy sender.</param>
        /// <param name="seq">The packet's sequence number (-1 when absent).</param>
        /// <param name="nowSeconds">Receive-side monotonic-ish clock, seconds.</param>
        public SessionVerdict ClassifyDatagram(string incomingSessionId, long seq, double nowSeconds) {
            datagrams = datagrams + 1;
            lastReceivedSeq = seq;
            lastDatagramTime = nowSeconds;
            bool hasId = !string.IsNullOrEmpty(incomingSessionId);

            if (!hasSession) {
                sessionId = hasId ? incomingSessionId : string.Empty;
                hasSession = true;
                legacyRegressionRun = 0;
                return SessionVerdict.FirstSession;
            }

            if (hasId) {
                if (string.Equals(sessionId, incomingSessionId)) {
                    legacyRegressionRun = 0;
                    return SessionVerdict.SameSession;
                }
                if (IsRetired(incomingSessionId)) {
                    // A session we already left. Late in-flight datagram from the dead producer —
                    // drop it rather than flapping back to it.
                    rejectedOldSession = rejectedOldSession + 1;
                    return SessionVerdict.OldSession;
                }
                // An explicit, previously-unseen session id. This is the authoritative signal and
                // needs no corroboration: the producer has told us it is a new run.
                Retire(sessionId);
                sessionId = incomingSessionId;
                sessionTransitions = sessionTransitions + 1;
                acceptedNewSession = acceptedNewSession + 1;
                legacyRegressionRun = 0;
                return SessionVerdict.NewSession;
            }

            // ---- legacy sender (no sid) -------------------------------------------------------------
            if (!string.IsNullOrEmpty(sessionId)) {
                // We were talking to a sid-capable producer and are now receiving un-identified packets:
                // the producer changed. Treat as a new session rather than silently mixing the two.
                Retire(sessionId);
                sessionId = string.Empty;
                sessionTransitions = sessionTransitions + 1;
                acceptedNewSession = acceptedNewSession + 1;
                legacyRegressionRun = 0;
                return SessionVerdict.NewSession;
            }

            // Bounded last-resort recovery: a SUSTAINED run of backwards sequence numbers AND a quiet
            // period since the last accepted pose. Either alone is not enough — a duplicated or
            // reordered packet gives regressions without a quiet period, and a genuine stall gives a
            // quiet period without regressions. Requiring both keeps single stray packets harmless.
            if (seq >= 0 && lastAcceptedSeq >= 0 && seq < lastAcceptedSeq) {
                legacyRegressionRun = legacyRegressionRun + 1;
                bool quiet = lastAcceptedTime >= 0.0
                             && (nowSeconds - lastAcceptedTime) >= legacyRegressionQuietSeconds;
                if (legacyRegressionRun >= legacyRegressionPackets && quiet) {
                    sessionTransitions = sessionTransitions + 1;
                    acceptedNewSession = acceptedNewSession + 1;
                    legacyRegressionRun = 0;
                    return SessionVerdict.NewSession;
                }
                rejectedOldSession = rejectedOldSession + 1;
                return SessionVerdict.SameSession;
            }

            legacyRegressionRun = 0;
            return SessionVerdict.SameSession;
        }

        private bool IsRetired(string id) {
            for (int i = 0; i < retiredCount; i++) {
                if (string.Equals(retired[i], id)) {
                    return true;
                }
            }
            return false;
        }

        private void Retire(string id) {
            if (string.IsNullOrEmpty(id) || IsRetired(id)) {
                return;
            }
            retired[retiredNext] = id;
            retiredNext = (retiredNext + 1) % RetiredCapacity;
            if (retiredCount < RetiredCapacity) {
                retiredCount = retiredCount + 1;
            }
        }

        /// <summary>Called after the pose buffer accepted a push.</summary>
        public void OnPoseAccepted(long seq, double nowSeconds) {
            accepted = accepted + 1;
            lastAcceptedSeq = seq;
            lastAcceptedTime = nowSeconds;
            legacyRegressionRun = 0;
            if (wasStale) {
                // First acceptance after a stale period: settle before claiming Live again, so a single
                // packet arriving into an otherwise-dead stream does not flicker the state.
                if (recoveringSince < 0.0) {
                    recoveringSince = nowSeconds;
                }
            }
        }

        /// <summary>Called when a session boundary flushed the consumer's buffer and ordering state.</summary>
        public void OnSessionReset(double nowSeconds) {
            lastAcceptedSeq = -1;
            recoveringSince = wasStale ? nowSeconds : recoveringSince;
        }

        /// <summary>
        /// Current stream state. Pure function of the timestamps recorded above and
        /// <paramref name="nowSeconds"/>; call it as often as you like.
        /// </summary>
        public TrackingState Evaluate(double nowSeconds) {
            if (datagrams == 0) {
                return TrackingState.NoStream;
            }
            if (lastAcceptedTime < 0.0) {
                return TrackingState.Connecting;
            }

            double sinceAccepted = nowSeconds - lastAcceptedTime;
            if (sinceAccepted >= failsafeSeconds) {
                wasStale = true;
                recoveringSince = -1.0;
                return TrackingState.StaleFailsafe;
            }
            if (sinceAccepted >= holdSeconds) {
                wasStale = true;
                recoveringSince = -1.0;
                // Distinguish "the producer is gone" from "the producer is talking but we are not
                // accepting yet" — the latter is a reconnect in progress and is worth its own state
                // because it is the normal path through a sidecar restart.
                bool datagramsFlowing = lastDatagramTime >= 0.0
                                        && (nowSeconds - lastDatagramTime) < holdSeconds;
                return datagramsFlowing ? TrackingState.Reconnecting : TrackingState.StaleHold;
            }

            if (wasStale) {
                if (recoveringSince >= 0.0 && (nowSeconds - recoveringSince) >= recoverSettleSeconds) {
                    wasStale = false;
                    recoveringSince = -1.0;
                    return TrackingState.Live;
                }
                return TrackingState.Recovering;
            }
            return TrackingState.Live;
        }

        /// <summary>True when the stream is fresher than the warn threshold — the "nothing to see" case.</summary>
        public bool IsFresh(double nowSeconds) {
            return lastAcceptedTime >= 0.0 && (nowSeconds - lastAcceptedTime) < warnSeconds;
        }

        /// <summary>Seconds since the last ACCEPTED pose; -1 when none has ever been accepted.</summary>
        public double SecondsSinceAccepted(double nowSeconds) {
            return lastAcceptedTime < 0.0 ? -1.0 : nowSeconds - lastAcceptedTime;
        }
    }
}
