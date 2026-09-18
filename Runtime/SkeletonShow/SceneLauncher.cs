using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using VirtualMirror.Core;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-35 — THE MENU. One scene that launches any of the others, split by how many people they
    /// need.
    ///
    /// WHY THE SPLIT IS THE WHOLE POINT. Nineteen scenes with no grouping is a list nobody reads, and
    /// the grouping that matters is not theme or age — it is **how many people have to be standing in
    /// front of the camera**. Ten of these do nothing useful alone: Bonds needs somebody to bond
    /// with, Chain cannot be a chain of one, Mirror Each Other has nobody to agree with. Picking one
    /// of those on your own and watching it sit there is the single most likely way to conclude the
    /// installation is broken. So the menu says which is which before you choose, and then says
    /// whether the sender upstream can even supply a second person.
    ///
    /// IT RUNS THE REAL TRACKING WHILE YOU CHOOSE, and that is not decoration. The menu is where you
    /// find out that the sidecar is not running, or that it is running in single-person mode, BEFORE
    /// you pick a scene and blame the scene. The bodies drawn behind the menu are the same
    /// <see cref="TrackedStage"/> every experience uses, so what you see here is exactly what the
    /// scene you pick will see.
    ///
    /// A SCENE THAT IS NOT IN BUILD SETTINGS CANNOT BE LOADED, and Unity's failure for that is a
    /// runtime error after the click. Every row is therefore checked once at startup and a missing
    /// one is drawn greyed with the reason, which turns "the button did nothing" into "this scene is
    /// not in the build list".
    ///
    /// IMGUI, NO CANVAS, NO PREFAB — the same rule the experience scenes follow. The scene asset stays
    /// a camera and one GameObject, so it is reviewable in a diff.
    /// </summary>
    public sealed class SceneLauncher : MonoBehaviour {
        [Header("Tracking")]
        [Tooltip("UDP port the Python sidecar sends to. Must match the sidecar's --port.")]
        [SerializeField] private int udpPort = 8899;
        [Tooltip("P1-3 presentation delay in ms, as in the mirror app. 0 = latest-wins.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;
        [Tooltip("Mirror the subject left/right, as a real mirror does.")]
        [SerializeField] private bool flipX = true;
        [Tooltip("Landmark space is Y-down; the scene is Y-up. Leave on.")]
        [SerializeField] private bool flipY = true;
        [SerializeField] private bool flipZ;

        [Header("Staging")]
        [Tooltip("How far back the camera sits, in metres. Wide, because the menu shows everybody.")]
        [SerializeField] private float cameraDistance = 5.2f;
        [Tooltip("Show the synthetic demonstration figure when nobody is tracked.")]
        [SerializeField] private bool attractEnabled = true;

        /// <summary>Most bodies drawn behind the menu. Matches the wire cap.</summary>
        private const int MaxBodies = 8;

        /// <summary>
        /// The catalogue. Titles and one-liners are the scenes' OWN <c>Title</c> and
        /// <c>Instruction</c> strings, copied deliberately rather than read by reflection: the menu
        /// lives in the SkeletonShow assembly and the experiences live in another that references it,
        /// so it cannot see their types — and a menu that loaded every experience class just to read
        /// two strings would be a worse trade than keeping the list honest by hand.
        ///
        /// If a title here ever disagrees with the scene it launches, the scene is right.
        /// </summary>
        private static readonly Entry[] SinglePerson = {
            new Entry("SkeletonShow", "SKELETON SHOW",
                      "Five ways of drawing one tracked body. 1-5 switches, TAB cycles."),
            new Entry("PoseMatch", "POSE MATCH",
                      "Copy the ghost. Your joints turn green as they land."),
            new Entry("BubblePop", "BUBBLE POP",
                      "Pop the bubbles. Hands, feet or head - but you have to SWING at them."),
            new Entry("ObjectPlay", "OBJECTS",
                      "Hit them, kick them, head them. Pinch to pick one up and let go to throw it."),
            new Entry("AirGraffiti", "AIR GRAFFITI",
                      "Pinch your thumb and finger together, then draw. Let go to stop."),
            new Entry("DepthReach", "DEPTH REACH",
                      "Reach INTO the glowing ring - not just at it. Distance counts."),
            new Entry("Fluid", "FLUID",
                      "Walk through it. Sweep your hands. It keeps moving after you stop."),
            new Entry("Footprints", "FOOTPRINTS",
                      "Stand still. The floor remembers how long you stayed."),
            new Entry("TimeEcho", "TIME ECHO",
                      "Move. Everything you just did is still happening behind you."),
        };

        private static readonly Entry[] MultiPerson = {
            new Entry("Bonds", "BONDS",
                      "Bring a friend. The closer you stand, the stronger the line. Touch hands."),
            new Entry("CollectiveFluid", "COLLECTIVE",
                      "Bring everyone. You are all stirring the same water - find someone else's wake."),
            new Entry("StillnessTug", "STILLNESS",
                      "Whoever is stillest draws the light. Stop moving. Out-calm each other."),
            new Entry("CrowdFootprints", "TRACES",
                      "Stand still, together. The floor remembers where all of you stood."),
            new Entry("Eclipse", "ECLIPSE",
                      "One light, at the camera. Step in front of someone and you take theirs."),
            new Entry("Chord", "CHORD",
                      "You are a note. Walk apart to change the chord. Move to be heard."),
            new Entry("Chain", "CHAIN",
                      "Hold hands. Link everybody in the room and the whole chain lights."),
            new Entry("Pass", "PASS",
                      "Keep it up between you. Hit it, kick it, head it - the count is everyone's."),
            new Entry("Podium", "PODIUM",
                      "One crown, live. Tallest, then stillest, then fastest. Take it off somebody."),
            new Entry("MirrorEachOther", "MIRROR EACH OTHER",
                      "Two of you. Hold the same shape. Nobody is right - you just have to agree."),
        };

        /// <summary>The full VRM mirror application, which is not an experience and does not share
        /// their scaffold. Listed because "launch any scene" has to mean any scene, and separated
        /// because launching it starts the whole app rather than a demonstration.</summary>
        private static readonly Entry[] Production = {
            new Entry("Mirror", "VIRTUAL MIRROR",
                      "The full avatar application: loads a VRM, retargets it and shows the mirror UI."),
        };

        /// <summary>One row of the menu. Public so the catalogue's invariants — no duplicates, no
        /// column longer than the number-key shortcut can reach — are pinned by a unit test rather
        /// than discovered as a dead button.</summary>
        public readonly struct Entry {
            public readonly string Scene;
            public readonly string Title;
            public readonly string Blurb;

            public Entry(string scene, string title, string blurb) {
                Scene = scene;
                Title = title;
                Blurb = blurb;
            }
        }

        /// <summary>Scenes that work with one person in front of the camera.</summary>
        public static IReadOnlyList<Entry> SinglePersonScenes {
            get {
                return SinglePerson;
            }
        }

        /// <summary>Scenes that need two or more people to do anything.</summary>
        public static IReadOnlyList<Entry> MultiPersonScenes {
            get {
                return MultiPerson;
            }
        }

        /// <summary>Scenes that are the product rather than a demonstration of it.</summary>
        public static IReadOnlyList<Entry> ProductionScenes {
            get {
                return Production;
            }
        }

        /// <summary>How many people a scene needs the sidecar to track. See
        /// <see cref="TrackingNeedFor"/>.</summary>
        public enum TrackingNeed {
            /// <summary>Not in the catalogue — the Launcher, Bootstrap, the licence scene. The
            /// sidecar must be left exactly as it is; passing through the menu is not a reason to
            /// restart a producer.</summary>
            Unspecified,
            /// <summary>One person, tracked as well as this rig can. The dedicated single-person
            /// sender, not the multi-person one capped to one.</summary>
            SinglePerson,
            /// <summary>Two or more, which only the multi-person sender produces.</summary>
            MultiPerson
        }

        /// <summary>
        /// Which sender a scene needs, from the catalogue above — the same list the menu draws, so
        /// the columns a person sees and the tracking they get cannot disagree.
        ///
        /// THE PRODUCTION COLUMN COUNTS AS SINGLE-PERSON. `Mirror` loads one VRM and retargets one
        /// body; the multi-person path would cost it the dedicated single-person pipeline for a
        /// crowd it cannot show.
        ///
        /// An unknown scene is <see cref="TrackingNeed.Unspecified"/> rather than a default, because
        /// the caller's correct response to "I don't know" is to change nothing. The Launcher itself
        /// is the case that matters: it is passed through on every single navigation, and defaulting
        /// it either way would restart the sidecar every time somebody backed out of a scene.
        /// </summary>
        public static TrackingNeed TrackingNeedFor(string sceneName) {
            if (string.IsNullOrEmpty(sceneName)) {
                return TrackingNeed.Unspecified;
            }
            if (Contains(MultiPerson, sceneName)) {
                return TrackingNeed.MultiPerson;
            }
            if (Contains(SinglePerson, sceneName) || Contains(Production, sceneName)) {
                return TrackingNeed.SinglePerson;
            }
            return TrackingNeed.Unspecified;
        }

        private static bool Contains(Entry[] entries, string sceneName) {
            for (int i = 0; i < entries.Length; i++) {
                if (string.Equals(entries[i].Scene, sceneName, System.StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>How many rows a column may hold before the number-key shortcut stops covering
        /// it: 1-9 and then 0. A longer column is not a bug in itself, but it silently leaves rows
        /// that cannot be reached from the keyboard, so it is pinned.</summary>
        public const int MaxShortcutRows = 10;

        private TrackedStage stage;
        private SkeletonShowPalette palette;
        private GlowSkeletonMode[] bodies;
        private Transform bodyRoot;
        private ILogService log;

        private readonly HashSet<string> available = new HashSet<string>();
        /// <summary>Set once a scene has been asked for. The menu is torn down at the end of the
        /// frame, and both Update and OnGUI still run before that - against a stage whose socket has
        /// already been released. Skipping the rest of the frame is simpler than guarding each use.</summary>
        private bool leaving;

        private int column;      // 0 single, 1 multi, 2 production
        private int row;
        private float smoothedFps = 60f;

        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle rowTitleStyle;
        private GUIStyle rowBlurbStyle;
        private GUIStyle noteStyle;
        private GUIStyle footerStyle;
        private GUIStyle rowStyle;
        private Texture2D rowNormal;
        private Texture2D rowSelected;
        private Texture2D rowMissing;

        private void Awake() {
            log = new LauncherLog();
            palette = new SkeletonShowPalette();

            ShowStage.BuildCamera(palette, cameraDistance, 1.1f);
            ShowStage.BuildPostProcessing(transform);
            ShowStage.BuildFloorGrid(transform, palette);

            stage = new TrackedStage(log, udpPort, poseInterpolationDelayMs);
            stage.AttractEnabled = attractEnabled;
            stage.GroundToFloor = true;
            stage.Start(flipX, flipY, flipZ);

            // Pushed back and down so the menu reads over the top of them. The bodies are here to
            // prove the tracking is alive, not to be looked at.
            bodyRoot = new GameObject("Bodies").transform;
            bodyRoot.SetParent(transform, false);
            bodies = new GlowSkeletonMode[MaxBodies];
            int i = 0;
            while (i < MaxBodies) {
                bodies[i] = new GlowSkeletonMode();
                bodies[i].Activate(bodyRoot, palette);
                i = i + 1;
            }

            CacheAvailability();
            log.Log(LogLevel.Info, "Launcher listening on UDP " + udpPort
                    + "; " + available.Count + " of " + TotalEntries() + " scenes are in Build Settings.");
        }

        private void OnDestroy() {
            if (stage != null) {
                stage.Stop();
                stage = null;
            }
        }

        // Checked ONCE, at startup. A scene missing from Build Settings cannot be loaded, and Unity's
        // failure for that is an error after the click - which reads as the button being broken.
        private void CacheAvailability() {
            available.Clear();
            int count = SceneManager.sceneCountInBuildSettings;
            int i = 0;
            while (i < count) {
                available.Add(LauncherScene.NameOf(SceneUtility.GetScenePathByBuildIndex(i)));
                i = i + 1;
            }
        }

        private static int TotalEntries() {
            return SinglePerson.Length + MultiPerson.Length + Production.Length;
        }

        private void Update() {
            if (leaving) {
                return;
            }
            float dt = Time.deltaTime;
            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(1e-4f, dt),
                                     1f - Mathf.Exp(-dt / 0.5f));
            stage.Tick(dt);
            DrawBodies();
            ReadInput();
        }

        private void DrawBodies() {
            IReadOnlyList<TrackedBody> crowd = stage.Bodies;
            int i = 0;
            while (i < bodies.Length) {
                bodies[i].Render(i < crowd.Count ? crowd[i].Pose : Empty, Time.deltaTime);
                i = i + 1;
            }
        }

        /// <summary>A permanently invalid pose, so a renderer with nobody to draw hides itself rather
        /// than holding the last body that stood there.</summary>
        private static readonly SkeletonPose Empty = new SkeletonPose();

        private void ReadInput() {
            Entry[] current = Column(column);

            if (Input.GetKeyDown(KeyCode.DownArrow)) {
                row = (row + 1) % current.Length;
            }
            if (Input.GetKeyDown(KeyCode.UpArrow)) {
                row = (row + current.Length - 1) % current.Length;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow)) {
                column = (column + 1) % 3;
                row = Mathf.Clamp(row, 0, Column(column).Length - 1);
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                column = (column + 2) % 3;
                row = Mathf.Clamp(row, 0, Column(column).Length - 1);
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space)) {
                Launch(current[row]);
            }

            // Number keys pick within the HIGHLIGHTED column, which is the only rule that stays
            // explainable with three columns and ten rows. 0 is the tenth.
            int digit = 0;
            while (digit < 10) {
                KeyCode key = digit == 9 ? KeyCode.Alpha0 : (KeyCode)((int)KeyCode.Alpha1 + digit);
                if (Input.GetKeyDown(key) && digit < current.Length) {
                    row = digit;
                    Launch(current[digit]);
                    return;
                }
                digit = digit + 1;
            }
        }

        private static Entry[] Column(int index) {
            if (index == 0) {
                return SinglePerson;
            }
            return index == 1 ? MultiPerson : Production;
        }

        private void Launch(Entry entry) {
            if (!available.Contains(entry.Scene)) {
                log.Log(LogLevel.Warning, entry.Scene + " is not in Build Settings, so it cannot be "
                        + "loaded. Add Scenes/" + entry.Scene + ".unity to the scene list.");
                return;
            }
            // Stop the tracking BEFORE the load rather than leaving it to OnDestroy. The socket is
            // bound to a fixed port and the scene being opened binds the same one; releasing it here
            // removes any question about which happens first.
            if (stage != null) {
                stage.Stop();
            }
            leaving = true;
            SceneManager.LoadScene(entry.Scene, LoadSceneMode.Single);
        }

        // ---------------------------------------------------------------- GUI

        private void OnGUI() {
            if (leaving) {
                return;
            }
            EnsureStyles();

            float margin = 24f;
            float top = 96f;
            float gap = 16f;
            // Three columns, but the production one only ever holds a row or two, so it gets a
            // narrower share rather than an equal third of the screen.
            float usable = Screen.width - margin * 2f - gap * 2f;
            float wide = Mathf.Max(260f, usable * 0.375f);
            float narrow = Mathf.Max(220f, usable - wide * 2f);

            GUI.Label(new Rect(margin, 18f, Screen.width - margin * 2f, 40f),
                      "VIRTUAL MIRROR", titleStyle);
            GUI.Label(new Rect(margin, 58f, Screen.width - margin * 2f, 28f), Header(), noteStyle);

            float x = margin;
            DrawColumn(0, new Rect(x, top, wide, Screen.height - top - 64f),
                       "ONE PERSON", "Works on your own.");
            x = x + wide + gap;
            DrawColumn(1, new Rect(x, top, wide, Screen.height - top - 64f),
                       "TWO OR MORE", MultiNote());
            x = x + wide + gap;
            DrawColumn(2, new Rect(x, top, narrow, Screen.height - top - 64f),
                       "PRODUCTION", "Not a demonstration.");

            // The sidecar line only appears when a boot scene actually started one. Claiming a
            // sidecar this scene knows nothing about would be worse than saying nothing.
            string sidecar = SidecarBootstrap.Status;
            GUI.Label(new Rect(margin, Screen.height - 54f, Screen.width - margin * 2f, 22f),
                      stage.StatusLine()
                      + (string.IsNullOrEmpty(sidecar) ? string.Empty : "      " + sidecar)
                      + "      " + smoothedFps.ToString("F0") + " fps", noteStyle);
            GUI.Label(new Rect(margin, Screen.height - 32f, Screen.width - margin * 2f, 22f),
                      "CLICK a scene, or ARROWS to move and ENTER to launch, or the NUMBER shown."
                      + "      ESC returns here from any scene.", footerStyle);
        }

        private string Header() {
            if (!stage.EverTracked) {
                return "Waiting for the sidecar on UDP " + udpPort
                       + " - you can still launch anything; the scene will wait too.";
            }
            string line = "people  " + stage.DetectedCount + " seen   "
                          + stage.TrackedCount + " tracked   "
                          + stage.Bodies.Count + " posed";
            if (!stage.HasCrowd) {
                line = line + "      SINGLE-PERSON SENDER upstream";
            }
            return line;
        }

        private string MultiNote() {
            if (!stage.EverTracked) {
                return "Needs the multi-person sender.";
            }
            if (!stage.HasCrowd) {
                // The single most useful thing this menu can say, and the reason it runs the tracking
                // at all: these scenes will open and will look broken, and the fix is a different
                // sidecar rather than a different scene.
                return "SINGLE-PERSON SENDER upstream - these will only ever show one person.\n"
                       + "Run multiperson_udp_sender.py.";
            }
            return "Multi-person sender is running.";
        }

        private void DrawColumn(int index, Rect area, string heading, string note) {
            Entry[] entries = Column(index);
            GUI.Label(new Rect(area.x, area.y, area.width, 26f), heading, headingStyle);
            GUI.Label(new Rect(area.x, area.y + 26f, area.width, 40f), note, noteStyle);

            float y = area.y + 68f;
            float rowHeight = 50f;
            int i = 0;
            while (i < entries.Length) {
                Rect r = new Rect(area.x, y, area.width, rowHeight - 6f);
                DrawRow(entries[i], r, index == column && i == row, i);
                y = y + rowHeight;
                i = i + 1;
            }
        }

        private void DrawRow(Entry entry, Rect r, bool selected, int index) {
            bool missing = !available.Contains(entry.Scene);
            rowStyle.normal.background = missing ? rowMissing : (selected ? rowSelected : rowNormal);
            rowStyle.hover.background = rowStyle.normal.background;
            rowStyle.active.background = rowSelected;

            if (GUI.Button(r, GUIContent.none, rowStyle)) {
                column = ColumnOf(entry);
                row = index;
                Launch(entry);
            }

            string number = (index == 9 ? "0" : (index + 1).ToString());
            GUI.Label(new Rect(r.x + 10f, r.y + 4f, 26f, 22f), number, noteStyle);
            rowTitleStyle.normal.textColor = missing
                ? new Color(0.55f, 0.55f, 0.6f, 1f)
                : (selected ? new Color(1f, 0.95f, 0.75f, 1f) : new Color(0.62f, 0.94f, 1f, 1f));
            GUI.Label(new Rect(r.x + 38f, r.y + 3f, r.width - 46f, 22f), entry.Title, rowTitleStyle);
            GUI.Label(new Rect(r.x + 38f, r.y + 23f, r.width - 46f, 20f),
                      missing ? "NOT IN BUILD SETTINGS - add Scenes/" + entry.Scene + ".unity"
                              : entry.Blurb,
                      rowBlurbStyle);
        }

        private static int ColumnOf(Entry entry) {
            int i = 0;
            while (i < MultiPerson.Length) {
                if (MultiPerson[i].Scene == entry.Scene) {
                    return 1;
                }
                i = i + 1;
            }
            i = 0;
            while (i < Production.Length) {
                if (Production[i].Scene == entry.Scene) {
                    return 2;
                }
                i = i + 1;
            }
            return 0;
        }

        private void EnsureStyles() {
            if (titleStyle != null) {
                return;
            }
            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 30;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.55f, 0.95f, 1f, 1f);

            headingStyle = new GUIStyle(GUI.skin.label);
            headingStyle.fontSize = 19;
            headingStyle.fontStyle = FontStyle.Bold;
            headingStyle.normal.textColor = new Color(1f, 0.85f, 0.45f, 1f);

            rowTitleStyle = new GUIStyle(GUI.skin.label);
            rowTitleStyle.fontSize = 16;
            rowTitleStyle.fontStyle = FontStyle.Bold;

            rowBlurbStyle = new GUIStyle(GUI.skin.label);
            rowBlurbStyle.fontSize = 12;
            rowBlurbStyle.wordWrap = false;
            rowBlurbStyle.clipping = TextClipping.Clip;
            rowBlurbStyle.normal.textColor = new Color(0.68f, 0.78f, 0.88f, 1f);

            noteStyle = new GUIStyle(GUI.skin.label);
            noteStyle.fontSize = 13;
            noteStyle.wordWrap = true;
            noteStyle.normal.textColor = new Color(0.72f, 0.86f, 0.96f, 1f);

            footerStyle = new GUIStyle(GUI.skin.label);
            footerStyle.fontSize = 13;
            footerStyle.normal.textColor = new Color(0.60f, 0.72f, 0.84f, 1f);

            // Flat colour blocks rather than a skin: the menu sits in front of a live, glowing scene,
            // so the rows need to be legible against anything moving behind them.
            rowNormal = Fill(new Color(0.05f, 0.09f, 0.17f, 0.82f));
            rowSelected = Fill(new Color(0.10f, 0.26f, 0.42f, 0.94f));
            rowMissing = Fill(new Color(0.12f, 0.06f, 0.06f, 0.72f));

            rowStyle = new GUIStyle(GUI.skin.box);
            rowStyle.border = new RectOffset(2, 2, 2, 2);
            rowStyle.padding = new RectOffset(0, 0, 0, 0);
            rowStyle.margin = new RectOffset(0, 0, 0, 0);
        }

        private static Texture2D Fill(Color c) {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>Minimal log, so the menu needs no dependency on the IO assembly. Must never
        /// throw, per the interface contract.</summary>
        private sealed class LauncherLog : ILogService {
            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError("[Launcher] " + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning("[Launcher] " + message);
                } else {
                    Debug.Log("[Launcher] " + message);
                }
            }

            public void LogException(System.Exception exception, string context) {
                Debug.LogError("[Launcher] " + context + ": "
                               + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
