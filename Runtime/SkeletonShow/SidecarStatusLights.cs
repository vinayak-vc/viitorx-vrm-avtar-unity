using System;
using System.IO;

using UnityEngine;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-51 — FIVE LIGHTS IN THE CORNER, so a person looking at Unity can tell whether the sidecar, its
    /// watchdog, the camera, the model and the tracking are each up. The sidecar has no window (a preview
    /// window once took tracking down with its quit key), which left a healthy system and a silently failed
    /// one looking identical.
    ///
    /// WHAT IT READS. Unity's own launcher (is the process alive) and the supervisor's
    /// <c>supervisor_state.json</c>, which carries the sender's latest heartbeat. It does NOT listen on the
    /// pose port: every scene binds that port for itself and a second bind is the failure ADR-075 removed.
    /// The rules live in <see cref="SidecarStatusModel"/>, which is pure and tested; this class only polls,
    /// draws and logs.
    ///
    /// IT SURVIVES SCENE CHANGES (DontDestroyOnLoad) so the lights are there in the launcher, every
    /// experience and the avatar app alike, and it is created by whichever bootstrap starts the sidecar.
    /// A change of any light is also logged once to the console, because the console is where somebody
    /// looks first. F8 hides and shows the panel.
    /// </summary>
    public sealed class SidecarStatusLights : MonoBehaviour {
        private const float PollSeconds = 1f;
        private const KeyCode ToggleKey = KeyCode.F8;

        private static SidecarStatusLights instance;

        private readonly SidecarStatusModel model = new SidecarStatusModel();
        private SidecarProcessLauncher launcher;
        private StatusLight[] lights;
        private string[] loggedKeys = new string[0];
        private string statePath = string.Empty;
        private float pollTimer;
        private bool visible = true;

        private Texture2D dot;
        private GUIStyle labelStyle;
        private GUIStyle textStyle;
        private GUIStyle titleStyle;
        private int styleHeight = -1;

        /// <summary>Creates the lights if they do not exist yet. Safe to call from any bootstrap, any number of times.</summary>
        public static void Ensure() {
            if (instance != null) {
                return;
            }
            GameObject host = new GameObject("SidecarStatusLights");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SidecarStatusLights>();
        }

        /// <summary>Tells the lights which launcher to report on. Call again when the launcher is replaced.</summary>
        public static void Attach(SidecarProcessLauncher sidecarLauncher) {
            Ensure();
            instance.launcher = sidecarLauncher;
        }

        private void Awake() {
            if (instance != null && instance != this) {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void Start() {
            Poll();
        }

        private void OnDestroy() {
            if (instance == this) {
                instance = null;
            }
            if (dot != null) {
                Destroy(dot);
                dot = null;
            }
        }

        private void Update() {
            if (Input.GetKeyDown(ToggleKey)) {
                visible = !visible;
            }
            pollTimer = pollTimer - Time.unscaledDeltaTime;
            if (pollTimer > 0f) {
                return;
            }
            pollTimer = PollSeconds;
            Poll();
        }

        private void Poll() {
            SupervisorSnapshot snapshot = null;
            double stateAge = 0.0;
            string path = StatePath();
            if (path.Length > 0) {
                string text = ReadState(path);
                if (text != null) {
                    SupervisorSnapshot parsed;
                    if (SupervisorSnapshot.TryParse(text, out parsed)) {
                        snapshot = parsed;
                        stateAge = (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds;
                    }
                }
            }
            bool hasLauncher = launcher != null;
            SidecarLaunchState state = hasLauncher ? launcher.State : SidecarLaunchState.Disabled;
            lights = model.Evaluate(Time.unscaledTime, hasLauncher, state, snapshot, stateAge);
            LogChanges();
        }

        private string StatePath() {
            if (statePath.Length == 0) {
                SidecarPathSet paths = SidecarLocator.Resolve(string.Empty);
                statePath = SidecarStatusModel.StatePathFor(paths.Root,
                    Environment.GetEnvironmentVariable(SidecarStatusModel.EvidenceDirEnvironmentVariable));
            }
            return statePath;
        }

        /// <summary>The state file's text, or null when it is missing or the supervisor holds it for a moment.
        /// The supervisor replaces the file atomically, so a reader can be refused for an instant; the next
        /// poll (one second) simply tries again.</summary>
        private static string ReadState(string path) {
            try {
                if (!File.Exists(path)) {
                    return null;
                }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                          FileShare.ReadWrite | FileShare.Delete)) {
                    using (StreamReader reader = new StreamReader(stream)) {
                        return reader.ReadToEnd();
                    }
                }
            } catch (IOException) {
                return null;
            } catch (UnauthorizedAccessException) {
                return null;
            }
        }

        private void LogChanges() {
            if (loggedKeys.Length != lights.Length) {
                loggedKeys = new string[lights.Length];
            }
            int i = 0;
            while (i < lights.Length) {
                StatusLight light = lights[i];
                if (!string.Equals(loggedKeys[i], light.Key, StringComparison.Ordinal)) {
                    loggedKeys[i] = light.Key;
                    string line = "[SidecarStatus] " + light.Label + ": " + light.Text;
                    if (light.Level == StatusLevel.Bad) {
                        Debug.LogWarning(line);
                    } else {
                        Debug.Log(line);
                    }
                }
                i = i + 1;
            }
        }

        // ---- drawing ---------------------------------------------------------------------------

        private static Color ColourFor(StatusLevel level) {
            switch (level) {
                case StatusLevel.Ok:
                    return new Color(0.20f, 0.85f, 0.35f, 1f);
                case StatusLevel.Warn:
                    return new Color(1.00f, 0.75f, 0.15f, 1f);
                case StatusLevel.Bad:
                    return new Color(0.95f, 0.25f, 0.25f, 1f);
                case StatusLevel.Info:
                    return new Color(0.35f, 0.65f, 1.00f, 1f);
                default:
                    return new Color(0.45f, 0.45f, 0.45f, 1f);
            }
        }

        private void EnsureDrawing(float scale) {
            if (dot == null) {
                dot = MakeDot(48);
            }
            int height = Screen.height;
            if (labelStyle != null && styleHeight == height) {
                return;
            }
            styleHeight = height;
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = Mathf.RoundToInt(13f * scale);
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.white;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            textStyle = new GUIStyle(labelStyle);
            textStyle.fontStyle = FontStyle.Normal;
            textStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            titleStyle = new GUIStyle(textStyle);
            titleStyle.fontSize = Mathf.RoundToInt(11f * scale);
            titleStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        }

        /// <summary>A soft-edged white disc, tinted per light. Built once rather than shipped as an asset.</summary>
        private static Texture2D MakeDot(int size) {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            float centre = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1.5f;
            int y = 0;
            while (y < size) {
                int x = 0;
                while (x < size) {
                    float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float alpha = Mathf.Clamp01(radius - distance + 1f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    x = x + 1;
                }
                y = y + 1;
            }
            texture.Apply(false, true);
            return texture;
        }

        private void OnGUI() {
            if (!visible || lights == null) {
                return;
            }
            // Lower depth draws on top: the lights must stay visible over every scene's own HUD.
            GUI.depth = -1000;
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.6f);
            EnsureDrawing(scale);

            float pad = 8f * scale;
            float row = 22f * scale;
            float title = 16f * scale;
            float width = 330f * scale;
            float height = pad * 2f + title + row * lights.Length;
            float x = Screen.width - width - 12f * scale;
            float y = 12f * scale;

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(x + pad, y + pad * 0.5f, width - pad * 2f, title), "SYSTEM STATUS   (F8 hides)", titleStyle);
            float dotSize = 13f * scale;
            float pulse = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
            int i = 0;
            while (i < lights.Length) {
                StatusLight light = lights[i];
                float rowY = y + pad + title + row * i;
                Color colour = ColourFor(light.Level);
                if (light.Level == StatusLevel.Warn) {
                    // A slow pulse says "in progress"; a steady colour says "settled".
                    colour.a = pulse;
                }
                GUI.color = colour;
                GUI.DrawTexture(new Rect(x + pad, rowY + (row - dotSize) * 0.5f, dotSize, dotSize), dot);
                GUI.color = previous;
                GUI.Label(new Rect(x + pad + dotSize + 8f * scale, rowY, 86f * scale, row), light.Label, labelStyle);
                GUI.Label(new Rect(x + pad + dotSize + 8f * scale + 86f * scale, rowY,
                                   width - pad * 2f - dotSize - 8f * scale - 86f * scale, row), light.Text, textStyle);
                i = i + 1;
            }
        }
    }
}
