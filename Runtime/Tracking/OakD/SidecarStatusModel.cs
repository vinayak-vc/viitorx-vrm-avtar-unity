using System;

using Newtonsoft.Json.Linq;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>How a status light should look. The order is not a severity ranking.</summary>
    public enum StatusLevel {
        /// <summary>Nothing to report: not running, not applicable.</summary>
        Off = 0,
        /// <summary>Informational: working, but not by us (an external sidecar).</summary>
        Info = 1,
        /// <summary>In progress or degraded: starting, loading, waiting, restarting.</summary>
        Warn = 2,
        /// <summary>Healthy.</summary>
        Ok = 3,
        /// <summary>Broken: needs a person.</summary>
        Bad = 4,
    }

    /// <summary>One light: what it is about, how it looks, and the few words that say why.</summary>
    public sealed class StatusLight {
        public string Label;
        public StatusLevel Level;
        public string Text;

        /// <summary>Changes exactly when the light's meaning changes, so a transition can be logged once.</summary>
        public string Key {
            get {
                return Label + "|" + Level + "|" + Text;
            }
        }
    }

    /// <summary>The sender's own latest heartbeat line, as the supervisor copies it into its state file.</summary>
    public sealed class ChildHeartbeat {
        /// <summary>Source frames the sender's loop has consumed.</summary>
        public long Frames;
        /// <summary>Datagrams the sender has actually emitted (a person was found and posed).</summary>
        public long Sent;
        /// <summary>'Y' or 'n' for a live source (a phone or webcam connected or not); null otherwise.</summary>
        public string Live;
        /// <summary>The free text after the numbers: "ready", "loading RTMW3D ...", "waiting for frames".</summary>
        public string Note = string.Empty;
        /// <summary>How long ago the sender's log last grew, seconds. The sender prints every ~2 s.</summary>
        public double LineAgeSeconds;
    }

    /// <summary>The supervisor's <c>supervisor_state.json</c>.</summary>
    public sealed class SupervisorSnapshot {
        public string State = string.Empty;
        public bool Ready;
        public int RestartCount;
        public int LastExitCode;
        public bool HasLastExitCode;
        public double BackoffSeconds;
        /// <summary>Null until the sender has printed its first heartbeat.</summary>
        public ChildHeartbeat Child;

        /// <summary>
        /// Parses the state file. Returns false for anything that is not a JSON object with a
        /// <c>SupervisorState</c> - an empty file, a half-written one, the wrong file - and never throws.
        /// Fields the file does not have keep their defaults, so a supervisor from before the heartbeat
        /// was added still yields a usable snapshot with a null <see cref="Child"/>.
        /// </summary>
        public static bool TryParse(string json, out SupervisorSnapshot snapshot) {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }
            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (Exception) {
                // A reader can catch the file between the supervisor's write and its rename.
                return false;
            }
            string state = (string)root["SupervisorState"];
            if (string.IsNullOrEmpty(state)) {
                return false;
            }
            SupervisorSnapshot parsed = new SupervisorSnapshot();
            parsed.State = state;
            parsed.Ready = ReadBool(root["SidecarReady"]);
            parsed.RestartCount = (int)ReadNumber(root["RestartCount"]);
            parsed.BackoffSeconds = ReadNumber(root["BackoffSeconds"]);
            JToken exit = root["LastExitCode"];
            if (exit != null && exit.Type == JTokenType.Integer) {
                parsed.LastExitCode = (int)exit;
                parsed.HasLastExitCode = true;
            }
            JObject child = root["Child"] as JObject;
            if (child != null) {
                ChildHeartbeat beat = new ChildHeartbeat();
                beat.Frames = (long)ReadNumber(child["Frames"]);
                beat.Sent = (long)ReadNumber(child["Sent"]);
                JToken live = child["Live"];
                beat.Live = live != null && live.Type == JTokenType.String ? (string)live : null;
                JToken note = child["Note"];
                beat.Note = note != null && note.Type == JTokenType.String ? (string)note : string.Empty;
                beat.LineAgeSeconds = ReadNumber(child["LineAgeSeconds"]);
                parsed.Child = beat;
            }
            snapshot = parsed;
            return true;
        }

        private static double ReadNumber(JToken token) {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) {
                return 0.0;
            }
            return (double)token;
        }

        private static bool ReadBool(JToken token) {
            return token != null && token.Type == JTokenType.Boolean && (bool)token;
        }
    }

    /// <summary>
    /// F-51 — turns "what Unity launched" and "what the supervisor says" into five lights, so a person looking
    /// at the Unity window can tell a dead sidecar from a loading model from an empty room.
    ///
    /// THE SIDECAR HAS NO WINDOW, ON PURPOSE (the preview window once bound ESC to quit and took tracking down
    /// with it), so until now a healthy system and a silently failed one looked identical. The five lights are
    /// the five questions somebody asks, in the order they fail:
    ///
    ///   SIDECAR   did Unity start the python process, and is it alive?
    ///   WATCHDOG  is the supervisor (the thing that restarts it) running and still reporting?
    ///   SOURCE    is the camera or phone connected?
    ///   MODEL     has the pose model loaded and is it consuming frames?
    ///   TRACKING  is a PERSON being found and sent? (frames moving while nothing is sent = nobody found)
    ///
    /// The last three come from the sender's own heartbeat, relayed by the supervisor. That deliberately avoids
    /// a second UDP listener: every scene binds the pose port for itself and a second bind is the failure
    /// ADR-075 removed. "Moving" means the counter changed within <see cref="ActivitySeconds"/>.
    ///
    /// PURE AND UNITY-FREE (System + Newtonsoft), like <see cref="VideoSourceSpec"/>, so every rule is asserted
    /// without a player. The caller supplies the clock.
    /// </summary>
    public sealed class SidecarStatusModel {
        public const string SidecarLabel = "SIDECAR";
        public const string WatchdogLabel = "WATCHDOG";
        public const string SourceLabel = "SOURCE";
        public const string ModelLabel = "MODEL";
        public const string TrackingLabel = "TRACKING";

        /// <summary>A counter that changed within this many seconds counts as moving. The sender prints every
        /// ~2 s and the supervisor copies it every ~2 s, so a healthy counter changes about every 2-4 s; 6 s
        /// rides over one missed update without ever calling a stalled sender healthy for long.</summary>
        public const double ActivitySeconds = 6.0;

        /// <summary>A state file not rewritten for this long means the supervisor is not running (it writes
        /// every ~2 s). Ten seconds is well beyond its own 5-try retry on a locked file.</summary>
        public const double StaleStateSeconds = 10.0;

        /// <summary>A sender log that has not grown for this long means the sender is hung or dead (it prints
        /// every ~2 s; the supervisor's own watchdog kills at 10 s).</summary>
        public const double SilentChildSeconds = 8.0;

        /// <summary>Overrides the sidecar's evidence root, for the supervisor and for this reader alike
        /// (<c>evidence_paths.EVIDENCE_ROOT</c>).</summary>
        public const string EvidenceDirEnvironmentVariable = "VIRTUAL_MIRROR_EVIDENCE_DIR";

        /// <summary>
        /// Where the supervisor writes <c>supervisor_state.json</c>: its default evidence folder,
        /// <c>&lt;evidence root&gt;/oak_v4/f20b</c>, where the evidence root is the sidecar root's
        /// <c>evidence</c> folder unless <paramref name="evidenceRootOverride"/> is set. Unity launches the
        /// supervisor without <c>--evidence-dir</c>, so this is the folder it uses. Empty when there is no root.
        /// </summary>
        public static string StatePathFor(string sidecarRoot, string evidenceRootOverride) {
            string evidenceRoot = !string.IsNullOrEmpty(evidenceRootOverride)
                ? evidenceRootOverride
                : (string.IsNullOrEmpty(sidecarRoot) ? string.Empty : System.IO.Path.Combine(sidecarRoot, "evidence"));
            if (evidenceRoot.Length == 0) {
                return string.Empty;
            }
            return System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(evidenceRoot, "oak_v4"), "f20b"),
                                          "supervisor_state.json");
        }

        private long lastFrames = -1;
        private long lastSent = -1;
        private double framesMovedAt = double.NegativeInfinity;
        private double sentMovedAt = double.NegativeInfinity;
        private double firstChildSeenAt = double.NegativeInfinity;

        /// <summary>
        /// The five lights now. <paramref name="launcher"/> is what Unity's own launcher reports (or
        /// <see cref="SidecarLaunchState.Disabled"/> with <paramref name="hasLauncher"/> false when Unity never
        /// started one). <paramref name="snapshot"/> is null when the state file is missing or unreadable.
        /// <paramref name="stateAgeSeconds"/> is how long ago that file was last written.
        /// </summary>
        public StatusLight[] Evaluate(double now, bool hasLauncher, SidecarLaunchState launcher,
                                      SupervisorSnapshot snapshot, double stateAgeSeconds) {
            bool launcherActive = hasLauncher
                && (launcher == SidecarLaunchState.Starting || launcher == SidecarLaunchState.Running);
            bool stale = snapshot != null && stateAgeSeconds > StaleStateSeconds;
            // A stale file is the previous run's leftover when nothing of ours is running, so it is ignored
            // rather than reported as a dead supervisor.
            SupervisorSnapshot live = (snapshot != null && !stale) ? snapshot : null;

            TrackMotion(now, live);

            StatusLight[] lights = new StatusLight[5];
            lights[0] = SidecarLight(hasLauncher, launcher, live);
            lights[1] = WatchdogLight(launcherActive, launcher, stale, live);
            lights[2] = SourceLight(now, live);
            lights[3] = ModelLight(now, live);
            lights[4] = TrackingLight(now, live);
            return lights;
        }

        private void TrackMotion(double now, SupervisorSnapshot live) {
            if (live == null || live.Child == null) {
                return;
            }
            if (double.IsNegativeInfinity(firstChildSeenAt)) {
                firstChildSeenAt = now;
            }
            // The FIRST sighting of a counter only records it: movement needs two observations, otherwise a
            // stalled sender whose last heartbeat is still in the file would read as healthy for a few seconds.
            if (live.Child.Frames != lastFrames) {
                // Any later change counts, including a restart that resets it to zero: either way the sender
                // is alive and reporting.
                if (lastFrames >= 0) {
                    framesMovedAt = now;
                }
                lastFrames = live.Child.Frames;
            }
            if (live.Child.Sent != lastSent) {
                if (lastSent >= 0) {
                    sentMovedAt = now;
                }
                lastSent = live.Child.Sent;
            }
        }

        private static StatusLight Light(string label, StatusLevel level, string text) {
            StatusLight light = new StatusLight();
            light.Label = label;
            light.Level = level;
            light.Text = text;
            return light;
        }

        private static StatusLight SidecarLight(bool hasLauncher, SidecarLaunchState launcher,
                                                SupervisorSnapshot live) {
            if (!hasLauncher) {
                return live != null
                    ? Light(SidecarLabel, StatusLevel.Info, "running (started outside Unity)")
                    : Light(SidecarLabel, StatusLevel.Off, "not started by Unity");
            }
            switch (launcher) {
                case SidecarLaunchState.Starting:
                    return Light(SidecarLabel, StatusLevel.Warn, "starting");
                case SidecarLaunchState.Running:
                    return Light(SidecarLabel, StatusLevel.Ok, "running");
                case SidecarLaunchState.AttachedExternal:
                    return Light(SidecarLabel, StatusLevel.Info, "external sidecar (not ours)");
                case SidecarLaunchState.Failed:
                    return Light(SidecarLabel, StatusLevel.Bad, "failed to start (see console)");
                case SidecarLaunchState.Disabled:
                    return Light(SidecarLabel, StatusLevel.Off, "manual (auto-start off)");
                default:
                    return Light(SidecarLabel, StatusLevel.Off, "stopped");
            }
        }

        private static StatusLight WatchdogLight(bool launcherActive, SidecarLaunchState launcher,
                                                 bool stale, SupervisorSnapshot live) {
            if (live == null) {
                if (stale && launcher == SidecarLaunchState.Running) {
                    return Light(WatchdogLabel, StatusLevel.Bad, "supervisor not responding");
                }
                return launcherActive
                    ? Light(WatchdogLabel, StatusLevel.Warn, "waiting for the supervisor")
                    : Light(WatchdogLabel, StatusLevel.Off, "no supervisor");
            }
            switch (live.State) {
                case "RUNNING":
                    return live.RestartCount > 0
                        ? Light(WatchdogLabel, StatusLevel.Ok, "watching (restarted " + live.RestartCount + "x)")
                        : Light(WatchdogLabel, StatusLevel.Ok, "watching");
                case "STARTING":
                    return Light(WatchdogLabel, StatusLevel.Warn, "starting the sidecar");
                case "EXITED":
                case "BACKOFF":
                    return Light(WatchdogLabel, StatusLevel.Warn, live.HasLastExitCode
                        ? "restarting (last exit code " + live.LastExitCode + ")"
                        : "restarting");
                case "FAILED_PERMANENT":
                    return Light(WatchdogLabel, StatusLevel.Bad, "crash loop - retrying (see sidecar log)");
                case "STOPPED":
                    return Light(WatchdogLabel, StatusLevel.Off, "stopped");
                default:
                    return Light(WatchdogLabel, StatusLevel.Info, live.State.ToLowerInvariant());
            }
        }

        /// <summary>True for the first <see cref="ActivitySeconds"/> after a sender was first seen, while no
        /// counter has been observed to move yet: too early to call it idle, and "idle" would be a false alarm
        /// on the first poll after Unity attaches to a sidecar that was already healthy.</summary>
        private bool StillChecking(double now) {
            return double.IsNegativeInfinity(framesMovedAt) && now - firstChildSeenAt < ActivitySeconds;
        }

        private static bool Silent(SupervisorSnapshot live) {
            return live.Child != null && live.Child.LineAgeSeconds > SilentChildSeconds;
        }

        private static bool NoteSays(SupervisorSnapshot live, string word) {
            return live.Child != null && live.Child.Note != null
                && live.Child.Note.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private StatusLight SourceLight(double now, SupervisorSnapshot live) {
            if (live == null || live.State == "STOPPED") {
                return Light(SourceLabel, StatusLevel.Off, "-");
            }
            if (live.Child == null) {
                return Light(SourceLabel, StatusLevel.Warn, "waiting for the sidecar");
            }
            if (Silent(live)) {
                return Light(SourceLabel, StatusLevel.Bad, "sidecar silent");
            }
            if (live.Child.Live == "Y") {
                return Light(SourceLabel, StatusLevel.Ok, "connected");
            }
            if (live.Child.Live == "n") {
                return Light(SourceLabel, StatusLevel.Warn, "waiting for the camera / phone");
            }
            // No live= field: a file or the OAK-D. Frames arriving is the whole evidence available.
            if (now - framesMovedAt <= ActivitySeconds && live.Child.Frames > 0) {
                return Light(SourceLabel, StatusLevel.Ok, "frames arriving");
            }
            if (live.Child.Frames > 0 && StillChecking(now)) {
                return Light(SourceLabel, StatusLevel.Warn, "checking...");
            }
            return NoteSays(live, "loading")
                ? Light(SourceLabel, StatusLevel.Warn, "waiting (model loading)")
                : Light(SourceLabel, StatusLevel.Warn, "no frames yet");
        }

        private StatusLight ModelLight(double now, SupervisorSnapshot live) {
            if (live == null || live.State == "STOPPED") {
                return Light(ModelLabel, StatusLevel.Off, "-");
            }
            if (live.Child == null) {
                return Light(ModelLabel, StatusLevel.Warn, "starting");
            }
            if (Silent(live)) {
                return Light(ModelLabel, StatusLevel.Bad, "sidecar silent");
            }
            if (live.Child.Frames == 0 && NoteSays(live, "loading")) {
                return Light(ModelLabel, StatusLevel.Warn, "loading (~10-15 s)");
            }
            if (now - framesMovedAt <= ActivitySeconds && live.Child.Frames > 0) {
                return Light(ModelLabel, StatusLevel.Ok, "running");
            }
            if (live.Child.Frames > 0 && StillChecking(now)) {
                return Light(ModelLabel, StatusLevel.Warn, "checking...");
            }
            return live.Child.Frames == 0
                ? Light(ModelLabel, StatusLevel.Warn, "loaded, waiting for frames")
                : Light(ModelLabel, StatusLevel.Warn, "idle: no new frames");
        }

        private StatusLight TrackingLight(double now, SupervisorSnapshot live) {
            if (live == null || live.Child == null || live.State == "STOPPED" || Silent(live)) {
                return Light(TrackingLabel, StatusLevel.Off, "-");
            }
            bool framesMoving = now - framesMovedAt <= ActivitySeconds && live.Child.Frames > 0;
            if (!framesMoving) {
                return Light(TrackingLabel, StatusLevel.Off, "-");
            }
            if (now - sentMovedAt <= ActivitySeconds && live.Child.Sent > 0) {
                return Light(TrackingLabel, StatusLevel.Ok, "person tracked");
            }
            return live.Child.Sent == 0
                ? Light(TrackingLabel, StatusLevel.Warn, "no person found yet")
                : Light(TrackingLabel, StatusLevel.Warn, "person lost");
        }
    }
}
