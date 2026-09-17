using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 — the scaffold every experience scene stands on. One component, one GameObject, one
    /// scene; everything else is built at runtime.
    ///
    /// WHAT A SUBCLASS GETS AND WHAT IT OWES. It gets a tracked body (<see cref="Stage"/>) already
    /// through the humanized layer, staged, grounded on its measured floor, with both hands and the
    /// trust channel attached — and a camera, bloom volume and floor grid already built. It owes
    /// three methods: <see cref="BuildExperience"/>, <see cref="Play"/>, and a name. It must never
    /// call Update itself, because the ordering between "advance the tracking" and "read the
    /// tracking" is the one thing a scene must not get wrong and is therefore not left to a subclass.
    ///
    /// THE ATTRACT RULE, and every subclass has to respect it: when <see cref="TrackedStage.IsAttract"/>
    /// is true the body on screen is SYNTHETIC. It is there so an empty room shows something alive
    /// rather than a black screen. An experience may draw it and should, but must never SCORE it —
    /// a high score set by the attract loop is a lie, and the first thing anyone notices.
    /// <see cref="ScoringAllowed"/> is that check, already written.
    ///
    /// WHY IMGUI FOR THE HUD. No canvas, no event system, no prefab, so the scene asset stays a
    /// camera and one GameObject. These are demonstration scenes, not shipped UI.
    /// </summary>
    public abstract class ExperienceBase : MonoBehaviour {
        [Header("Tracking")]
        [Tooltip("UDP port the Python sidecar sends to. Must match the sidecar's --port.")]
        [SerializeField] protected int udpPort = 8899;
        [Tooltip("P1-3 presentation delay in ms, as in the mirror app. 0 = latest-wins.")]
        [SerializeField] protected float poseInterpolationDelayMs = 40f;
        [Tooltip("Mirror the subject left/right, as a real mirror does.")]
        [SerializeField] protected bool flipX;
        [Tooltip("Landmark space is Y-down; the scene is Y-up. Leave on.")]
        [SerializeField] protected bool flipY = true;
        [SerializeField] protected bool flipZ;

        [Header("Staging")]
        [Tooltip("Where the mid-hip sits before grounding.")]
        [SerializeField] protected Vector3 bodyOrigin = new Vector3(0f, 0.95f, 0f);
        [SerializeField] protected float bodyScale = 1f;
        [Tooltip("How far back the camera sits, in metres.")]
        [SerializeField] protected float cameraDistance = 3.2f;
        [Tooltip("Run the F-27 humanized layer before drawing. Toggle live with H.")]
        [SerializeField] protected bool useHumanizedSkeleton = true;
        [Tooltip("Show the synthetic demonstration figure when nobody is tracked. Toggle with A.")]
        [SerializeField] protected bool attractEnabled = true;
        [Tooltip("Stand the figure on its measured floor. Toggle with G.")]
        [SerializeField] protected bool groundToFloor = true;

        [Header("Sound (F-31)")]
        [Tooltip("Procedural audio: a drone that follows whole-body energy, a movement layer, and "
                 + "event cues. Toggle live with M.")]
        [SerializeField] protected bool soundEnabled = true;
        [Range(0f, 1f)]
        [SerializeField] protected float soundVolume = 0.8f;

        protected TrackedStage Stage { get; private set; }

        /// <summary>F-31 shared sound. Every experience gets the drone and the movement layer for
        /// free; a subclass only calls <see cref="ExperienceAudio.Play"/> for things that HAPPEN.</summary>
        protected ExperienceAudio Audio { get; private set; }

        protected SkeletonShowPalette Palette { get; private set; }
        protected Transform Root { get; private set; }
        protected ILogService Log { get; private set; }

        /// <summary>F-34 - arrivals and departures BY TRACK ID. The presence gate cannot report
        /// these: it answers "is there a person", so in a crowd it never changes.</summary>
        protected CrowdRoster Roster {
            get {
                return roster;
            }
        }

        private readonly CrowdRoster roster = new CrowdRoster();

        private GUIStyle hudStyle;
        private GUIStyle titleStyle;
        private float smoothedFps = 60f;

        /// <summary>Shown top-left. Two or three words.</summary>
        public abstract string Title { get; }

        /// <summary>One line telling a viewer what to DO. This is the only instruction most people
        /// will read, so it is an instruction, not a description of the technology.</summary>
        public abstract string Instruction { get; }

        /// <summary>Build whatever this experience needs under <paramref name="root"/>.</summary>
        protected abstract void BuildExperience(Transform root);

        /// <summary>One frame. The tracking has already been advanced; just read it and draw.</summary>
        protected abstract void Play(float deltaSeconds);

        /// <summary>Extra HUD lines, or null. Drawn under the instruction.</summary>
        protected virtual string StatusText() {
            return null;
        }

        /// <summary>Reset whatever this experience counts. Bound to R, and called automatically when
        /// a new person arrives — a visitor must never inherit the previous visitor's score.</summary>
        protected virtual void ResetExperience() {
        }

        /// <summary>
        /// F-34 - whether a new VISITOR wipes this experience.
        ///
        /// True for the single-person scenes, which is right: an inherited score is the first thing
        /// anybody notices. A CROWD scene overrides it to false, because with three people already
        /// playing the fourth walking in must not wipe the other three - and because a crowd scene
        /// that keeps no per-visitor state has nothing to wipe in the first place, which is exactly
        /// what makes it safe against an identity switch. Use the two hooks below instead.
        /// </summary>
        protected virtual bool ResetsOnNewVisitor {
            get {
                return true;
            }
        }

        /// <summary>A track id appeared. For ALLOCATING something to a person - a voice, a slot.
        /// Not for starting a score: see <see cref="CrowdRoster"/>.</summary>
        protected virtual void OnPersonArrived(int id) {
        }

        /// <summary>A track id went away. For RELEASING what was allocated to them.</summary>
        protected virtual void OnPersonLeft(int id) {
        }

        /// <summary>Per-experience keys, handled after the shared ones.</summary>
        protected virtual void ReadExtraInput() {
        }

        /// <summary>
        /// True when the body on screen is a REAL tracked person, so scoring it means something.
        /// False during attract, which is synthetic, and false when nothing is tracked.
        /// </summary>
        protected bool ScoringAllowed {
            get {
                return Stage != null && Stage.IsLive && Stage.Pose.Valid;
            }
        }

        private void Awake() {
            Log = new ExperienceLog(Title);
            Palette = new SkeletonShowPalette();

            ShowStage.BuildCamera(Palette, cameraDistance);
            ShowStage.BuildPostProcessing(transform);
            ShowStage.BuildFloorGrid(transform, Palette);

            Stage = new TrackedStage(Log, udpPort, poseInterpolationDelayMs);
            Stage.BodyOrigin = bodyOrigin;
            Stage.BodyScale = bodyScale;
            Stage.UseHumanized = useHumanizedSkeleton;
            Stage.AttractEnabled = attractEnabled;
            Stage.GroundToFloor = groundToFloor;
            Stage.Start(flipX, flipY, flipZ);

            Audio = new ExperienceAudio();
            Audio.Enabled = soundEnabled;
            Audio.Volume = soundVolume;
            Audio.Build(transform);

            Root = new GameObject("ExperienceRoot").transform;
            Root.SetParent(transform, false);
            BuildExperience(Root);

            Log.Log(LogLevel.Info, Title + " listening on UDP " + udpPort + "; run the sidecar as usual.");
        }

        private void OnDestroy() {
            if (Stage != null) {
                Stage.Stop();
                Stage = null;
            }
        }

        /// <summary>Set once the launcher has been asked for. The scene is torn down at the end of
        /// the frame; skipping the rest of it keeps an experience from scoring or drawing against a
        /// pose whose provider has already been released.</summary>
        private bool leaving;

        private void Update() {
            float dt = Time.deltaTime;
            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(1e-4f, dt),
                                     1f - Mathf.Exp(-dt / 0.5f));
            ReadInput();
            if (leaving) {
                return;
            }

            // Tracking FIRST, then the experience reads it. The whole reason TrackedStage is a plain
            // class rather than a component: this order is in the code, not in a project setting.
            bool wasLive = Stage.IsLive;
            Stage.Tick(dt);
            Audio.Tick(Stage.Pose, dt);
            // The presence gate already knows when somebody walks up or leaves; until now nothing
            // marked it. A room that answers when you arrive is the cheapest possible signal that
            // the installation is alive and has noticed you.
            if (Stage.Presence.Changed) {
                Audio.Play(Stage.Presence.Present ? SoundCue.Arrive : SoundCue.Depart, 0, 0.5f);
            }
            roster.Update(Stage.Bodies);
            if (ResetsOnNewVisitor && Stage.IsLive && !wasLive && Stage.Presence.Changed) {
                // A new person just walked up. Whatever the last one scored is theirs, not this
                // visitor's, and an inherited score is the first thing anybody notices.
                ResetExperience();
            }
            int arrivedAt = 0;
            while (arrivedAt < roster.Arrived.Count) {
                OnPersonArrived(roster.Arrived[arrivedAt]);
                arrivedAt = arrivedAt + 1;
            }
            int leftAt = 0;
            while (leftAt < roster.Left.Count) {
                OnPersonLeft(roster.Left[leftAt]);
                leftAt = leftAt + 1;
            }

            Play(dt);
        }

        private void ReadInput() {
            // F-35 - back to the menu, from every experience, on one key. Nothing else is read this
            // frame: the scene is about to be torn down and a toggle applied on the way out would be
            // lost anyway. Does nothing when there is no launcher in Build Settings, so a scene
            // opened on its own in the Editor behaves exactly as it did before.
            if (LauncherScene.ReturnRequested() && LauncherScene.IsAvailable) {
                // Release the UDP socket here rather than leaving it to OnDestroy: the scene being
                // opened binds the same fixed port.
                if (Stage != null) {
                    Stage.Stop();
                }
                leaving = LauncherScene.Load();
                if (leaving) {
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                useHumanizedSkeleton = !useHumanizedSkeleton;
                Stage.UseHumanized = useHumanizedSkeleton;
            }
            if (Input.GetKeyDown(KeyCode.A)) {
                attractEnabled = !attractEnabled;
                Stage.AttractEnabled = attractEnabled;
            }
            if (Input.GetKeyDown(KeyCode.G)) {
                groundToFloor = !groundToFloor;
                Stage.GroundToFloor = groundToFloor;
                Stage.ResetGrounding();
            }
            if (Input.GetKeyDown(KeyCode.M)) {
                soundEnabled = !soundEnabled;
                Audio.Enabled = soundEnabled;
            }
            if (Input.GetKeyDown(KeyCode.R)) {
                ResetExperience();
            }
            ReadExtraInput();
        }

        private void OnGUI() {
            if (leaving) {
                return;
            }
            if (hudStyle == null) {
                hudStyle = new GUIStyle(GUI.skin.label);
                hudStyle.fontSize = 15;
                hudStyle.normal.textColor = new Color(0.75f, 0.9f, 1f, 1f);
                titleStyle = new GUIStyle(GUI.skin.label);
                titleStyle.fontSize = 26;
                titleStyle.fontStyle = FontStyle.Bold;
                titleStyle.normal.textColor = new Color(0.55f, 0.95f, 1f, 1f);
            }

            GUI.Label(new Rect(16f, 12f, 900f, 34f), Title, titleStyle);
            GUI.Label(new Rect(16f, 46f, 1200f, 22f), Instruction, hudStyle);

            string status = StatusText();
            if (!string.IsNullOrEmpty(status)) {
                GUI.Label(new Rect(16f, 74f, 1400f, 300f), status, hudStyle);
            }

            // Bottom of the screen on purpose: F-28's first live capture put the status across the
            // subject's chest, which is exactly where the thing being judged is.
            float y = Screen.height - 52f;
            GUI.Label(new Rect(16f, y, 1600f, 22f),
                      Stage.StatusLine()
                      + "      humanized " + (useHumanizedSkeleton ? "ON" : "OFF")
                      + "      attract " + (attractEnabled ? "ON" : "OFF")
                      + "      ground " + (groundToFloor ? "ON" : "OFF")
                      + "      sound " + (soundEnabled ? "ON" : "OFF")
                      + "      " + smoothedFps.ToString("F0") + " fps",
                      hudStyle);
            GUI.Label(new Rect(16f, y + 22f, 1600f, 22f),
                      "R reset      H humanized      A attract      G ground      M mute"
                      + ExtraKeyHelp() + LauncherScene.KeyHelp(),
                      hudStyle);
        }

        /// <summary>Appended to the key help line, e.g. "      C clear".</summary>
        protected virtual string ExtraKeyHelp() {
            return string.Empty;
        }

        /// <summary>Minimal log so an experience scene needs no file logging and no dependency on the
        /// IO assembly. Must never throw, per the interface contract.</summary>
        private sealed class ExperienceLog : ILogService {
            private readonly string tag;

            public ExperienceLog(string title) {
                tag = "[" + title + "] ";
            }

            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError(tag + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning(tag + message);
                } else {
                    Debug.Log(tag + message);
                }
            }

            public void LogException(System.Exception exception, string context) {
                Debug.LogError(tag + context + ": " + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
