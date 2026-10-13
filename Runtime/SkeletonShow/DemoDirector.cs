using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-46 — DRIVES THE DEMONSTRATION SO THE PERSON BEING TRACKED DOES NOT HAVE TO.
    ///
    /// THE PROBLEM THIS EXISTS FOR. Every scene here is navigated from the keyboard, and the subject
    /// stands 0.9–4 m away from it with both hands in frame. One person cannot both perform and
    /// operate. Holding a clicker is not a fix either: it occupies a hand, and the hand is the thing
    /// being demonstrated in HANDS mode. So the run of show is a table, and this walks it on a timer
    /// while the subject simply performs.
    ///
    /// IT IS DORMANT UNTIL ASKED. A <see cref="RuntimeInitializeOnLoadMethodAttribute"/> creates one
    /// of these at startup with captions off and the timer stopped, so it costs two key checks per
    /// frame and draws nothing. Nothing else in the project references it, no scene contains it, and
    /// no Inspector field configures it — which is deliberate: a demo aid that needed wiring into
    /// twenty scenes would be the F-40 mirror problem again.
    ///
    ///     F9   captions on/off          (leave them OFF for the showcase cut, ON for the proof cut)
    ///     F10  start/stop the LIVE sequence   — acts 1,2,3,6, one person in front of the camera
    ///     F12  start/stop the CROWD sequence  — act 4, driven from recorded video (see below)
    ///     F11  skip to the next beat now
    ///
    /// WHY TWO SEQUENCES AND NOT ONE. Crossing between the single-person and crowd scene columns
    /// makes `AppBootstrap` swap senders, which costs 15–20 s of no tracking (ADR-076). Worse, when
    /// the crowd half is being driven from a recorded file by tools/video/f32_multiperson_video.py
    /// rather than by the camera, an automatic sender swap would kill the very process feeding it.
    /// Keeping them as two operator-started sequences means the crossing is a decision someone makes
    /// between takes, not something that happens mid-shot.
    /// </summary>
    public sealed class DemoDirector : MonoBehaviour {
        /// <summary>One shot. <see cref="Mode"/> is the SKELETON SHOW mode index to switch to, or
        /// -1 to leave whatever the scene chose — the modes are number-keyed inside the scene, which
        /// is the other thing a performer standing across the room cannot reach.</summary>
        private readonly struct Beat {
            public readonly string Scene;
            public readonly int Mode;
            public readonly string Act;
            public readonly string Direction;
            public readonly string Proves;
            public readonly float Seconds;

            public Beat(string scene, int mode, string act, string direction, string proves,
                        float seconds) {
                Scene = scene;
                Mode = mode;
                Act = act;
                Direction = direction;
                Proves = proves;
                Seconds = seconds;
            }
        }

        // SKELETON SHOW mode indices, in the order SkeletonShowBootstrap builds them.
        private const int Glow = 0;
        private const int Energy = 1;
        private const int Motion = 2;
        private const int Trust = 3;
        private const int Hands = 4;

        /// <summary>
        /// The live run of show. Durations are performance time, not editing time — shoot long and
        /// cut short, because the one thing that cannot be fixed later is a take that ended while
        /// the body was still settling.
        /// </summary>
        private static readonly Beat[] Live = {
            new Beat("SkeletonShow", Glow, "1 — IT IS YOU",
                     "Walk in. Raise your RIGHT hand. Then your left.",
                     "the mirror is the right way round (ADR-078)", 22f),
            new Beat("SkeletonShow", Glow, "1 — IT IS YOU",
                     "Walk left, right, toward the camera, away.",
                     "the body follows you, and the floor pool goes with it", 26f),
            new Beat("SkeletonShow", Hands, "2 — IT IS PRECISE",
                     "Open and close both hands. Count on your fingers.",
                     "133 keypoints — every finger, not a blob", 22f),
            new Beat("SkeletonShow", Trust, "2 — IT IS PRECISE",
                     "Turn side on. Let one arm fall behind you.",
                     "per-joint confidence — it reports what it cannot see", 22f),
            new Beat("DepthReach", -1, "2 — IT IS PRECISE",
                     "Reach toward the camera, then far away.",
                     "real measured Z, not an apparent size change", 20f),
            new Beat("BubblePop", -1, "3 — IT IS A PRODUCT",
                     "Pop them. Use both hands and your feet.",
                     "tracked limbs drive real interaction", 16f),
            new Beat("AirGraffiti", -1, "3 — IT IS A PRODUCT",
                     "Pinch and draw. Release to stop.",
                     "finger-level gesture, not just a wrist", 16f),
            new Beat("Fluid", -1, "3 — IT IS A PRODUCT",
                     "Sweep both arms slowly, then fast.",
                     "velocity is carried through the whole chain", 16f),
            new Beat("TimeEcho", -1, "3 — IT IS A PRODUCT",
                     "Move, then hold completely still.",
                     "temporal history of a real body", 16f),
            new Beat("Mirror", -1, "6 — THE PRODUCT",
                     "Stand square. Turn your torso. Wave.",
                     "the full VRM avatar application", 24f),
        };

        /// <summary>
        /// Act 4. Shot separately because with one person it is driven from recorded video, and that
        /// changes what it is allowed to claim: a video file has ONE camera, so the harness runs with
        /// NO STEREO DEPTH (its own header says so). This sequence therefore demonstrates detection,
        /// identity tracking and rendering for N people — it does NOT demonstrate measured depth, and
        /// the caption says so on screen rather than leaving the viewer to assume.
        /// </summary>
        private static readonly Beat[] Crowd = {
            new Beat("Bonds", -1, "4 — MANY PEOPLE",
                     "Recorded source — 456.webm, seven dancers.",
                     "N bodies, stable identities — NO stereo depth from a video file", 16f),
            new Beat("Chain", -1, "4 — MANY PEOPLE",
                     "Recorded source — 456.webm.",
                     "per-person tracks drive per-person behaviour", 16f),
            new Beat("Pass", -1, "4 — MANY PEOPLE",
                     "Recorded source — 456.webm.",
                     "interaction BETWEEN tracked identities", 16f),
            new Beat("CollectiveFluid", -1, "4 — MANY PEOPLE",
                     "Recorded source — 456.webm.",
                     "every body contributes to one shared field", 16f),
        };

        private static DemoDirector instance;

        private Beat[] sequence;
        private int index;
        private float remaining;
        private bool running;
        private bool captions;
        private string sequenceName = "";
        private bool pendingModeApply;

        private GUIStyle actStyle;
        private GUIStyle directionStyle;
        private GUIStyle provesStyle;
        private GUIStyle footStyle;
        private Texture2D backdrop;

        /// <summary>
        /// Creates the (dormant) director without any scene containing it. See the class note: the
        /// alternative is adding a component to twenty scenes, and this project has already paid that
        /// bill once for a single bool.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() {
            if (instance != null) {
                return;
            }
            GameObject go = new GameObject("DemoDirector");
            instance = go.AddComponent<DemoDirector>();
            DontDestroyOnLoad(go);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F9)) {
                captions = !captions;
            }
            if (Input.GetKeyDown(KeyCode.F10)) {
                Toggle(Live, "LIVE");
            }
            if (Input.GetKeyDown(KeyCode.F12)) {
                Toggle(Crowd, "CROWD (recorded source)");
            }
            if (Input.GetKeyDown(KeyCode.F11) && running) {
                Advance();
            }

            if (pendingModeApply) {
                // The mode can only be set once the scene's bootstrap exists, which is not the frame
                // the load was requested on. Retried until it takes rather than assumed.
                pendingModeApply = !ApplyMode(sequence[index].Mode);
            }

            if (!running) {
                return;
            }
            remaining = remaining - Time.unscaledDeltaTime;
            if (remaining <= 0f) {
                Advance();
            }
        }

        private void Toggle(Beat[] which, string name) {
            if (running && sequence == which) {
                running = false;
                return;
            }
            sequence = which;
            sequenceName = name;
            index = -1;
            running = true;
            captions = true;
            Advance();
        }

        private void Advance() {
            index = index + 1;
            if (sequence == null || index >= sequence.Length) {
                running = false;
                index = sequence == null ? 0 : sequence.Length - 1;
                return;
            }
            Beat beat = sequence[index];
            remaining = beat.Seconds + SwitchAllowanceFor(beat.Scene);
            if (SceneManager.GetActiveScene().name != beat.Scene) {
                SceneManager.LoadScene(beat.Scene, LoadSceneMode.Single);
            }
            pendingModeApply = beat.Mode >= 0;
        }

        /// <summary>
        /// Extra dwell when this beat crosses into the other tracking column, because
        /// `AppBootstrap.EnsureSidecarForScene` then kills one python process and loads RTMW3D in
        /// another — 15–20 s of no tracking (ADR-076). Without this the timer would advance through a
        /// shot that had no body in it, which is the one failure that wastes a whole take.
        /// </summary>
        private float SwitchAllowanceFor(string nextScene) {
            if (index <= 0 || sequence == null) {
                return 0f;
            }
            SceneLauncher.TrackingNeed now = SceneLauncher.TrackingNeedFor(sequence[index - 1].Scene);
            SceneLauncher.TrackingNeed next = SceneLauncher.TrackingNeedFor(nextScene);
            if (now == next || now == SceneLauncher.TrackingNeed.Unspecified || now == SceneLauncher.TrackingNeed.VisionOnly
                || next == SceneLauncher.TrackingNeed.VisionOnly
                || next == SceneLauncher.TrackingNeed.Unspecified) {
                return 0f;
            }
            return 20f;
        }

        private bool ApplyMode(int mode) {
            SkeletonShowBootstrap show = Object.FindFirstObjectByType<SkeletonShowBootstrap>();
            if (show == null) {
                return false;
            }
            show.SwitchTo(mode);
            return true;
        }

        private void EnsureStyles() {
            if (actStyle != null) {
                return;
            }
            actStyle = new GUIStyle(GUI.skin.label);
            actStyle.fontSize = 20;
            actStyle.fontStyle = FontStyle.Bold;
            actStyle.normal.textColor = new Color(0.45f, 0.85f, 1f);

            directionStyle = new GUIStyle(GUI.skin.label);
            directionStyle.fontSize = 30;
            directionStyle.fontStyle = FontStyle.Bold;
            directionStyle.normal.textColor = Color.white;
            directionStyle.wordWrap = true;

            provesStyle = new GUIStyle(GUI.skin.label);
            provesStyle.fontSize = 19;
            provesStyle.normal.textColor = new Color(0.72f, 0.78f, 0.84f);
            provesStyle.wordWrap = true;

            footStyle = new GUIStyle(GUI.skin.label);
            footStyle.fontSize = 15;
            footStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);

            backdrop = new Texture2D(1, 1);
            backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            backdrop.Apply();
        }

        private void OnGUI() {
            if (!captions) {
                return;
            }
            EnsureStyles();

            // Anchored bottom-left and kept narrow: the subject is usually centre-frame and the
            // point of the shot is the body, not this.
            float w = Mathf.Min(760f, Screen.width - 48f);
            float h = 132f;
            Rect box = new Rect(24f, Screen.height - h - 24f, w, h);
            GUI.DrawTexture(box, backdrop);

            Rect inner = new Rect(box.x + 18f, box.y + 12f, box.width - 36f, box.height - 24f);
            GUILayout.BeginArea(inner);

            if (sequence != null && index >= 0 && index < sequence.Length) {
                Beat beat = sequence[index];
                GUILayout.Label(beat.Act, actStyle);
                GUILayout.Label(beat.Direction, directionStyle);
                GUILayout.Label("proves: " + beat.Proves, provesStyle);
                GUILayout.FlexibleSpace();
                string state = running
                    ? string.Format("{0}  {1}/{2}   next in {3:0}s", sequenceName, index + 1,
                                    sequence.Length, Mathf.Max(0f, remaining))
                    : sequenceName + "  PAUSED";
                GUILayout.Label(state + "     [F9] captions  [F10] live  [F12] crowd  [F11] skip",
                                footStyle);
            } else {
                GUILayout.Label("DEMO DIRECTOR", actStyle);
                GUILayout.Label("F10 runs the live sequence. F12 runs the crowd sequence.",
                                directionStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label("[F9] captions off     [F11] skip a beat", footStyle);
            }
            GUILayout.EndArea();
        }
    }
}
