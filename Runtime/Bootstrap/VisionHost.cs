using System;
using System.IO;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Vision;

namespace VirtualMirror.App {
    /// <summary>The streams the vision host serves.</summary>
    public enum VisionStream {
        Hands = 0,
        Face = 1,
        Cutout = 2
    }

    /// <summary>
    /// Owns everything the hands, face and cutout streams need while the app runs: the UDP receivers, the Python senders that feed them, and the
    /// per-frame hand-over of new messages to whoever subscribed. <see cref="AppBootstrap"/> creates one from its "Hands and Face" settings
    /// and ticks it; scenes ask for a stream with <see cref="AddDemand"/> and read the messages from <see cref="HandsUpdated"/> /
    /// <see cref="FaceUpdated"/> / <see cref="CutoutUpdated"/>, so a scene never opens a socket or a process of its own (one UDP port has one reader).
    ///
    /// The cutout stream is different in what it carries, not in how it is run: its receiver reassembles a camera picture and a person mask
    /// from datagram bursts, and the host uploads each new frame to two textures (<see cref="CutoutTextures"/>) that every consumer shares. The
    /// host never segments anything and never reads the VRM or skeleton paths; the cutout runs, or does not, beside them.
    ///
    /// A sender is started when its mode says so (Always, or Auto while some scene has asked for the stream), stopped when it no longer
    /// is, and restarted a second after a setting that changes its command line stops changing. A sender somebody else is already running
    /// (a stream is live that this host did not start) is left alone. Failures are one status line, not a retry loop.
    ///
    /// NOT TESTED in the Editor or against a live camera: the settings-to-flags mapping is (<see cref="VisionSenderArguments"/>), the
    /// process and socket orchestration here is not.
    /// </summary>
    public sealed class VisionHost : IDisposable {
        /// <summary>How often the receivers and senders are reconciled with the settings. The per-frame work is only the message hand-over.</summary>
        private const float ReconcileSeconds = 0.25f;

        /// <summary>How long a changed command line must stay unchanged before the sender is restarted for it (a slider drags through many values).</summary>
        private const float RestartDelaySeconds = 1.0f;

        /// <summary>How long a sender that is wanted waits, with no stream live, before it is started. Gives a sender started by hand time to be heard first.</summary>
        private const float LaunchGraceSeconds = 0.75f;

        private readonly ILogService log;
        private readonly VisionSettings settings;
        private readonly Slot handsSlot;
        private readonly Slot faceSlot;
        private readonly Slot cutoutSlot;
        private readonly CutoutFrame cutoutFrame = new CutoutFrame();
        private readonly CutoutTextures cutoutTextures = new CutoutTextures();
        private float reconcileTimer;
        private float rateTimer;

        private HandsReceiver hands;
        private FaceReceiver face;
        private CutoutReceiver cutout;
        private HandsMessage handsMessage;
        private FaceMessage faceMessage;
        private int handsAppliedPort;
        private int handsAppliedCapacity;
        private int faceAppliedPort;
        private int faceAppliedCapacity;
        private int cutoutAppliedPort;
        private bool appliedLoopback;
        private float appliedStale;
        private long handsUpdates;
        private long faceUpdates;
        private long cutoutUpdates;
        private long handsUpdatesAtRate;
        private long faceUpdatesAtRate;
        private long cutoutUpdatesAtRate;
        private float handsRate;
        private float faceRate;
        private float cutoutRate;
        private bool disposed;

        /// <summary>Raised on the main thread, from <see cref="Tick"/>, once per frame in which a new hands datagram arrived.</summary>
        public event Action<HandsMessage> HandsUpdated;

        /// <summary>Raised on the main thread, from <see cref="Tick"/>, once per frame in which a new face datagram arrived.</summary>
        public event Action<FaceMessage> FaceUpdated;

        /// <summary>
        /// Raised on the main thread, from <see cref="Tick"/>, once per frame in which a new cutout frame arrived and was uploaded. The argument is
        /// <see cref="CutoutTextures"/>, the same object every time: read the picture, the mask and the frame's facts from it.
        /// </summary>
        public event Action<CutoutTextures> CutoutUpdated;

        public VisionHost(ILogService log, VisionSettings settings) {
            this.log = log;
            this.settings = settings == null ? new VisionSettings() : settings;
            handsSlot = new Slot(VisionStream.Hands);
            faceSlot = new Slot(VisionStream.Face);
            cutoutSlot = new Slot(VisionStream.Cutout);
        }

        public VisionSettings Settings {
            get {
                return settings;
            }
        }

        /// <summary>The hands receiver, or null while hands are Off.</summary>
        public HandsReceiver Hands {
            get {
                return hands;
            }
        }

        /// <summary>The face receiver, or null while the face is Off.</summary>
        public FaceReceiver Face {
            get {
                return face;
            }
        }

        /// <summary>The cutout receiver, or null while the cutout is Off.</summary>
        public CutoutReceiver Cutout {
            get {
                return cutout;
            }
        }

        /// <summary>
        /// The camera picture and person mask of the newest cutout frame, as textures any system can sample (see <see cref="CutoutTextures"/> for
        /// the orientation). The same object for the life of the host; it is empty (<c>HasFrame</c> false) until the first frame arrives and after
        /// the stream is switched off.
        /// </summary>
        public CutoutTextures CutoutTextures {
            get {
                return cutoutTextures;
            }
        }

        /// <summary>The newest hands message, owned by the host (copy what you keep). Null while hands are Off.</summary>
        public HandsMessage HandsMessage {
            get {
                return handsMessage;
            }
        }

        /// <summary>The newest face message, owned by the host (copy what you keep). Null while the face is Off.</summary>
        public FaceMessage FaceMessage {
            get {
                return faceMessage;
            }
        }

        public long HandsUpdates {
            get {
                return handsUpdates;
            }
        }

        public long FaceUpdates {
            get {
                return faceUpdates;
            }
        }

        /// <summary>Hands messages handled per second, measured over the last second.</summary>
        public float HandsRate {
            get {
                return handsRate;
            }
        }

        /// <summary>Face messages handled per second, measured over the last second.</summary>
        public float FaceRate {
            get {
                return faceRate;
            }
        }

        public long CutoutUpdates {
            get {
                return cutoutUpdates;
            }
        }

        /// <summary>Cutout frames uploaded per second, measured over the last second.</summary>
        public float CutoutRate {
            get {
                return cutoutRate;
            }
        }

        /// <summary>A scene or component needs this stream: in Auto mode its sender runs until every demand is removed. Call from OnEnable.</summary>
        public void AddDemand(VisionStream stream) {
            SlotOf(stream).Demand = SlotOf(stream).Demand + 1;
            reconcileTimer = ReconcileSeconds;
        }

        /// <summary>The matching call from OnDisable.</summary>
        public void RemoveDemand(VisionStream stream) {
            Slot slot = SlotOf(stream);
            slot.Demand = Math.Max(0, slot.Demand - 1);
            reconcileTimer = ReconcileSeconds;
        }

        public bool HasDemand(VisionStream stream) {
            return SlotOf(stream).Demand > 0;
        }

        /// <summary>Applies the current settings now instead of at the next reconcile, and restarts a sender that failed or is running with other flags.</summary>
        public void ApplyNow() {
            handsSlot.Retry = true;
            faceSlot.Retry = true;
            cutoutSlot.Retry = true;
            handsSlot.FailedArguments = null;
            faceSlot.FailedArguments = null;
            cutoutSlot.FailedArguments = null;
            reconcileTimer = ReconcileSeconds;
            Reconcile(ReconcileSeconds);
        }

        /// <summary>Stops one sender and starts it again with the current settings, for the panel's Restart button.</summary>
        public void RestartSender(VisionStream stream) {
            Slot slot = SlotOf(stream);
            StopSender(slot);
            slot.Retry = true;
            slot.FailedArguments = null;
            slot.GraceSeconds = LaunchGraceSeconds;
            reconcileTimer = ReconcileSeconds;
        }

        /// <summary>Call every frame from a MonoBehaviour: hands new messages to subscribers and keeps the receivers and senders in line with the settings.</summary>
        public void Tick(float unscaledDeltaTime) {
            if (disposed) {
                return;
            }
            reconcileTimer = reconcileTimer + unscaledDeltaTime;
            if (reconcileTimer >= ReconcileSeconds) {
                Reconcile(reconcileTimer);
                reconcileTimer = 0f;
            }
            if (hands != null && hands.TryGetNew(handsMessage)) {
                handsUpdates = handsUpdates + 1;
                Action<HandsMessage> handler = HandsUpdated;
                if (handler != null) {
                    handler(handsMessage);
                }
            }
            if (face != null && face.TryGetNew(faceMessage)) {
                faceUpdates = faceUpdates + 1;
                Action<FaceMessage> handler = FaceUpdated;
                if (handler != null) {
                    handler(faceMessage);
                }
            }
            if (cutout != null && cutout.TryGetNew(cutoutFrame) && cutoutTextures.Upload(cutoutFrame)) {
                cutoutUpdates = cutoutUpdates + 1;
                Action<CutoutTextures> handler = CutoutUpdated;
                if (handler != null) {
                    handler(cutoutTextures);
                }
            }
            rateTimer = rateTimer + unscaledDeltaTime;
            if (rateTimer >= 1f) {
                handsRate = (handsUpdates - handsUpdatesAtRate) / rateTimer;
                faceRate = (faceUpdates - faceUpdatesAtRate) / rateTimer;
                cutoutRate = (cutoutUpdates - cutoutUpdatesAtRate) / rateTimer;
                handsUpdatesAtRate = handsUpdates;
                faceUpdatesAtRate = faceUpdates;
                cutoutUpdatesAtRate = cutoutUpdates;
                rateTimer = 0f;
            }
        }

        /// <summary>One line for the panel and the logs: who feeds this stream and in what state.</summary>
        public string SenderStatus(VisionStream stream) {
            Slot slot = SlotOf(stream);
            VisionSenderMode mode = ModeOf(stream);
            if (mode == VisionSenderMode.Off) {
                return "off";
            }
            if (!string.IsNullOrEmpty(slot.Problem)) {
                return slot.Problem;
            }
            StreamState state = StateOf(stream);
            if (slot.Launcher != null) {
                if (slot.Launcher.State == SidecarLaunchState.Failed) {
                    return "FAILED: " + slot.Launcher.LastError;
                }
                if (state == StreamState.Live) {
                    return "sender running (started here), streaming";
                }
                return "sender " + (slot.Launcher.IsOwnedProcessAlive ? "starting (models load, a few seconds)" : "not running");
            }
            if (state == StreamState.Live) {
                return "streaming from a sender started elsewhere";
            }
            if (!settings.AutoStartSenders) {
                return "waiting for a sender (auto-start is off)";
            }
            if (mode == VisionSenderMode.Auto && slot.Demand == 0) {
                return "idle (starts when a scene needs it)";
            }
            return "waiting to start the sender";
        }

        /// <summary>
        /// Should this stream's sender be running? Only when auto-start is on and the receiver is listening (a sender with nobody to hear it is a
        /// camera held for nothing), and then when the mode is Always, or Auto with at least one scene asking for the stream.
        /// </summary>
        public static bool IsSenderWanted(bool autoStart, VisionSenderMode mode, int demand, bool receiverUp) {
            if (!autoStart || !receiverUp) {
                return false;
            }
            if (mode == VisionSenderMode.Always) {
                return true;
            }
            return mode == VisionSenderMode.Auto && demand > 0;
        }

        public StreamState StateOf(VisionStream stream) {
            if (stream == VisionStream.Hands) {
                return hands == null ? StreamState.NoStream : hands.State;
            }
            if (stream == VisionStream.Cutout) {
                return cutout == null ? StreamState.NoStream : cutout.State;
            }
            return face == null ? StreamState.NoStream : face.State;
        }

        public VisionSenderMode ModeOf(VisionStream stream) {
            if (stream == VisionStream.Hands) {
                return settings.HandsMode;
            }
            if (stream == VisionStream.Cutout) {
                return settings.CutoutMode;
            }
            return settings.FaceMode;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            StopSender(handsSlot);
            StopSender(faceSlot);
            StopSender(cutoutSlot);
            DisposeHands();
            DisposeFace();
            DisposeCutout();
            cutoutTextures.Dispose();
        }

        // ------------------------------------------------------------------ reconcile

        private void Reconcile(float elapsedSeconds) {
            ReconcileReceivers();
            ReconcileSender(handsSlot, elapsedSeconds);
            ReconcileSender(faceSlot, elapsedSeconds);
            ReconcileSender(cutoutSlot, elapsedSeconds);
            handsSlot.Retry = false;
            faceSlot.Retry = false;
            cutoutSlot.Retry = false;
        }

        private void ReconcileReceivers() {
            bool loopback = !settings.AcceptFromNetwork;
            float stale = Mathf.Clamp(settings.StaleSeconds, 0.1f, 5f);
            bool receiverSettingsChanged = loopback != appliedLoopback || !Mathf.Approximately(stale, appliedStale);
            int handsCapacity = Mathf.Clamp(settings.MaxHands, 1, 4);
            int faceCapacity = Mathf.Clamp(settings.MaxFaces, 1, 4);

            bool wantHands = settings.HandsMode != VisionSenderMode.Off;
            if (hands != null && (!wantHands || receiverSettingsChanged || handsAppliedPort != settings.HandsPort || handsAppliedCapacity != handsCapacity)) {
                DisposeHands();
            }
            if (hands == null && wantHands && (handsSlot.BindFailedPort != settings.HandsPort || handsSlot.Retry)) {
                handsMessage = new HandsMessage(handsCapacity);
                hands = new HandsReceiver(settings.HandsPort, loopback, stale, log, null, handsCapacity);
                handsAppliedPort = settings.HandsPort;
                handsAppliedCapacity = handsCapacity;
                if (!hands.Start()) {
                    handsSlot.BindFailedPort = settings.HandsPort;
                    handsSlot.Problem = "PORT " + settings.HandsPort + " IS IN USE or cannot be bound";
                    log.Log(LogLevel.Error, "Vision: the hands receiver could not bind UDP " + settings.HandsPort + ". Another program (or another Unity) owns it.");
                    DisposeHands();
                } else {
                    handsSlot.BindFailedPort = 0;
                    handsSlot.Problem = string.Empty;
                }
            }

            bool wantFace = settings.FaceMode != VisionSenderMode.Off;
            if (face != null && (!wantFace || receiverSettingsChanged || faceAppliedPort != settings.FacePort || faceAppliedCapacity != faceCapacity)) {
                DisposeFace();
            }
            if (face == null && wantFace && (faceSlot.BindFailedPort != settings.FacePort || faceSlot.Retry)) {
                faceMessage = new FaceMessage(faceCapacity);
                face = new FaceReceiver(settings.FacePort, loopback, stale, log, null, faceCapacity);
                faceAppliedPort = settings.FacePort;
                faceAppliedCapacity = faceCapacity;
                if (!face.Start()) {
                    faceSlot.BindFailedPort = settings.FacePort;
                    faceSlot.Problem = "PORT " + settings.FacePort + " IS IN USE or cannot be bound";
                    log.Log(LogLevel.Error, "Vision: the face receiver could not bind UDP " + settings.FacePort + ". Another program (or another Unity) owns it.");
                    DisposeFace();
                } else {
                    faceSlot.BindFailedPort = 0;
                    faceSlot.Problem = string.Empty;
                }
            }
            bool wantCutout = settings.CutoutMode != VisionSenderMode.Off;
            if (cutout != null && (!wantCutout || receiverSettingsChanged || cutoutAppliedPort != settings.CutoutPort)) {
                DisposeCutout();
            }
            if (cutout == null && wantCutout && (cutoutSlot.BindFailedPort != settings.CutoutPort || cutoutSlot.Retry)) {
                cutout = new CutoutReceiver(settings.CutoutPort, loopback, stale, log, null, null);
                cutoutAppliedPort = settings.CutoutPort;
                if (!cutout.Start()) {
                    cutoutSlot.BindFailedPort = settings.CutoutPort;
                    cutoutSlot.Problem = "PORT " + settings.CutoutPort + " IS IN USE or cannot be bound";
                    log.Log(LogLevel.Error, "Vision: the cutout receiver could not bind UDP " + settings.CutoutPort + ". Another program (or another Unity) owns it.");
                    DisposeCutout();
                } else {
                    cutoutSlot.BindFailedPort = 0;
                    cutoutSlot.Problem = string.Empty;
                }
            }
            appliedLoopback = loopback;
            appliedStale = stale;
            if (!wantHands) {
                handsSlot.Problem = string.Empty;
            }
            if (!wantFace) {
                faceSlot.Problem = string.Empty;
            }
            if (!wantCutout) {
                cutoutSlot.Problem = string.Empty;
            }
        }

        private void ReconcileSender(Slot slot, float elapsedSeconds) {
            VisionSenderMode mode = ModeOf(slot.Stream);
            bool receiverUp = ReceiverUp(slot.Stream);
            bool wanted = IsSenderWanted(settings.AutoStartSenders, mode, slot.Demand, receiverUp);
            if (!wanted) {
                if (slot.Launcher != null) {
                    log.Log(LogLevel.Info, "Vision: stopping the " + slot.Stream + " sender (not needed now).");
                    StopSender(slot);
                }
                slot.GraceSeconds = 0f;
                slot.ChangeSeconds = 0f;
                if (!string.IsNullOrEmpty(slot.Problem) && mode != VisionSenderMode.Off && receiverUp) {
                    slot.Problem = string.Empty;
                }
                return;
            }

            string source;
            string sourceError;
            if (!ResolveSource(out source, out sourceError)) {
                // A path is typed a keystroke at a time and almost every prefix is "not found". Report it, and
                // stop a running sender, only once the source has stayed wrong for the restart delay.
                slot.ProblemSeconds = slot.ProblemSeconds + elapsedSeconds;
                if (slot.ProblemSeconds < RestartDelaySeconds) {
                    return;
                }
                if (slot.Problem != sourceError) {
                    log.Log(LogLevel.Error, "Vision: " + sourceError + " The " + slot.Stream + " sender was NOT started - fix Source in the Hands and Face settings.");
                }
                slot.Problem = sourceError;
                StopSender(slot);
                return;
            }
            slot.ProblemSeconds = 0f;
            slot.Problem = string.Empty;

            string desired = ArgumentsFor(slot.Stream, source);
            if (slot.Launcher != null) {
                bool alive = slot.Launcher.IsOwnedProcessAlive;
                if (!string.Equals(desired, slot.AppliedArguments, StringComparison.Ordinal)) {
                    slot.ChangeSeconds = slot.ChangeSeconds + elapsedSeconds;
                    if (slot.ChangeSeconds >= RestartDelaySeconds) {
                        log.Log(LogLevel.Info, "Vision: restarting the " + slot.Stream + " sender for changed settings.");
                        StopSender(slot);
                        slot.GraceSeconds = LaunchGraceSeconds;
                    }
                    return;
                }
                slot.ChangeSeconds = 0f;
                if (!alive && slot.Retry) {
                    StopSender(slot);
                    slot.GraceSeconds = LaunchGraceSeconds;
                }
                return;
            }

            if (slot.FailedArguments != null && string.Equals(slot.FailedArguments, desired, StringComparison.Ordinal) && !slot.Retry) {
                return;
            }
            if (StateOf(slot.Stream) == StreamState.Live) {
                slot.GraceSeconds = 0f;
                return;
            }
            slot.GraceSeconds = slot.GraceSeconds + elapsedSeconds;
            if (slot.GraceSeconds < LaunchGraceSeconds) {
                return;
            }
            LaunchSender(slot, source, desired);
        }

        private void LaunchSender(Slot slot, string source, string arguments) {
            slot.Retry = false;
            slot.GraceSeconds = 0f;
            slot.ChangeSeconds = 0f;
            SidecarLaunchOptions options = new SidecarLaunchOptions();
            options.AutoStart = true;
            options.DirectSenderOnly = true;
            options.DirectScript = ScriptFor(slot.Stream);
            options.DirectArguments = arguments;
            options.VideoSource = source;
            options.LanCameraPin = settings.LanCameraPin;
            SidecarProcessLauncher launcher = new SidecarProcessLauncher(log, options);
            slot.Launcher = launcher;
            slot.AppliedArguments = arguments;
            bool started = launcher.Start(SidecarLocator.Resolve(string.Empty));
            if (!started) {
                slot.FailedArguments = arguments;
                log.Log(LogLevel.Error, "Vision: the " + slot.Stream + " sender did not start: " + launcher.LastError);
            } else {
                slot.FailedArguments = null;
                log.Log(LogLevel.Info, "Vision: started the " + slot.Stream + " sender (" + VideoSourceSpec.DisplayName(source) + ").");
            }
        }

        private void StopSender(Slot slot) {
            if (slot.Launcher != null) {
                slot.Launcher.Dispose();
                slot.Launcher = null;
            }
            slot.AppliedArguments = null;
            slot.ChangeSeconds = 0f;
        }

        private bool ResolveSource(out string resolved, out string error) {
            resolved = string.Empty;
            error = string.Empty;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!VideoSourceSpec.TryResolve(settings.Source, projectRoot, out resolved, out error)) {
                error = "SOURCE ERROR: " + error;
                return false;
            }
            if (SidecarGuards.LanCameraNeedsPin(resolved, settings.LanCameraPin)) {
                error = "SOURCE ERROR: the LAN camera needs its access PIN (Lan Camera Pin, or the CAMERA_PIN environment variable).";
                return false;
            }
            return true;
        }

        private string ArgumentsFor(VisionStream stream, string source) {
            if (stream == VisionStream.Hands) {
                return VisionSenderArguments.Hands(settings, source);
            }
            if (stream == VisionStream.Cutout) {
                return VisionSenderArguments.Cutout(settings, source);
            }
            return VisionSenderArguments.Face(settings, source);
        }

        private static string ScriptFor(VisionStream stream) {
            if (stream == VisionStream.Hands) {
                return VisionSenderArguments.HandsScript;
            }
            if (stream == VisionStream.Cutout) {
                return VisionSenderArguments.CutoutScript;
            }
            return VisionSenderArguments.FaceScript;
        }

        private bool ReceiverUp(VisionStream stream) {
            if (stream == VisionStream.Hands) {
                return hands != null;
            }
            if (stream == VisionStream.Cutout) {
                return cutout != null;
            }
            return face != null;
        }

        private Slot SlotOf(VisionStream stream) {
            if (stream == VisionStream.Hands) {
                return handsSlot;
            }
            if (stream == VisionStream.Cutout) {
                return cutoutSlot;
            }
            return faceSlot;
        }

        private void DisposeHands() {
            if (hands != null) {
                hands.Dispose();
                hands = null;
            }
            handsMessage = null;
        }

        private void DisposeFace() {
            if (face != null) {
                face.Dispose();
                face = null;
            }
            faceMessage = null;
        }

        private void DisposeCutout() {
            if (cutout != null) {
                cutout.Dispose();
                cutout = null;
            }
            // Whatever the last frame showed belongs to a stream that no longer exists; the textures stay as an object so views keep their binding.
            cutoutTextures.Clear();
        }

        /// <summary>What the host remembers about one sender: who wants it, the process, and what it was started with.</summary>
        private sealed class Slot {
            public readonly VisionStream Stream;
            public int Demand;
            public SidecarProcessLauncher Launcher;
            public string AppliedArguments;
            public string FailedArguments;
            public string Problem = string.Empty;
            public float ProblemSeconds;
            public float GraceSeconds;
            public float ChangeSeconds;
            public bool Retry;
            public int BindFailedPort;

            public Slot(VisionStream stream) {
                Stream = stream;
            }
        }
    }
}
