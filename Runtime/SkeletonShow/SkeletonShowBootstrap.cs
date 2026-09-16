using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 — the SKELETON SHOW scene's only authored component. Everything else in the scene is built
    /// from here at runtime.
    ///
    /// WHY A SEPARATE SCENE AND A SEPARATE BOOTSTRAP. AppBootstrap loads a VRM, binds a control rig,
    /// runs face/hand retargeting and owns the mirror UI — none of which any of these three modes
    /// needs, and all of which would have to be kept working while experimenting with them. This path
    /// is: UDP -> the production landmark contract -> the F-27 humanized skeleton -> a visual. The
    /// tracking side is the SAME production code (OakDUdpPoseProvider, PoseSpaceConverter, P1-3's
    /// interpolation); what is gone is the avatar.
    ///
    /// WHY THE SCENE IS BUILT IN CODE. A scene asset full of hand-placed particle systems and materials
    /// is not reviewable in a diff and drifts from the code that drives it. Here the scene file holds a
    /// camera and one GameObject, and every visual decision is in a file with a reason next to it.
    ///
    /// CONTROLS: 1 / 2 / 3 pick a mode, TAB cycles, H toggles the humanized skeleton live (the fastest
    /// way to see what F-27 does - the difference is most visible on a dropped or jittering limb), and
    /// the on-screen buttons do the same for anyone who is not at the keyboard.
    /// </summary>
    public sealed class SkeletonShowBootstrap : MonoBehaviour {
        [Header("Tracking")]
        [Tooltip("UDP port the Python sidecar sends to. Must match the sidecar's --port (default 8899).")]
        [SerializeField] private int udpPort = 8899;
        [Tooltip("P1-3 presentation delay in ms, as in the mirror app. 0 = latest-wins.")]
        [SerializeField] private float poseInterpolationDelayMs = 40f;
        [Tooltip("Mirror the subject left/right, as a real mirror does.")]
        [SerializeField] private bool flipX;
        [Tooltip("Landmark space is Y-down; the scene is Y-up. Leave on.")]
        [SerializeField] private bool flipY = true;
        [SerializeField] private bool flipZ;

        [Header("Humanized skeleton (F-27)")]
        [Tooltip("Run the pose through the humanized layer before drawing it. Toggle live with H.")]
        [SerializeField] private bool useHumanizedSkeleton = true;

        [Header("Staging")]
        [Tooltip("Where the mid-hip sits in the scene.")]
        [SerializeField] private Vector3 bodyOrigin = new Vector3(0f, 0.95f, 0f);
        [Tooltip("Metres of scene per metre of body. 1 = life size, which is what the camera is framed "
                 + "for; above that the figure is cropped.")]
        [SerializeField] private float bodyScale = 1f;
        [Tooltip("Start in this mode (0-2).")]
        [SerializeField] private int startMode;

        private ISkeletonMode[] modes;
        private int activeMode = -1;
        private Transform modeRoot;
        private SkeletonShowPalette palette;
        private SkeletonPose pose;
        private OakDUdpPoseProvider provider;
        private PoseSpaceConverter converter;
        private HumanizedSkeleton humanized;
        private ILogService log;
        private GUIStyle hudStyle;
        private GUIStyle buttonStyle;
        private float lastFrameSeconds;
        private float smoothedFps = 60f;
        private double lastPoseTimestamp;
        private PoseFrame humanizedFrame;
        private bool everTracked;

        private void Awake() {
            log = new UnityConsoleLog();
            palette = new SkeletonShowPalette();
            pose = new SkeletonPose();
            humanized = new HumanizedSkeleton();
            converter = new PoseSpaceConverter(flipX, flipY, flipZ);

            BuildStage();

            modeRoot = new GameObject("ModeRoot").transform;
            modeRoot.SetParent(transform, false);
            modes = new ISkeletonMode[] {
                new GlowSkeletonMode(),
                new EnergyBodyMode(),
                new MotionEffectsMode(),
            };

            provider = new OakDUdpPoseProvider(log, converter, udpPort);
            provider.SetPoseInterpolation(poseInterpolationDelayMs);
            provider.StartTracking();
            log.Log(LogLevel.Info, "F-28 skeleton show listening on UDP " + udpPort
                                 + "; run the sidecar as usual.");
        }

        private void Start() {
            SwitchTo(Mathf.Clamp(startMode, 0, modes.Length - 1));
        }

        private void OnDestroy() {
            if (activeMode >= 0 && modes != null) {
                modes[activeMode].Deactivate();
            }
            if (provider != null) {
                provider.StopTracking();
                provider.Dispose();
                provider = null;
            }
        }

        private void Update() {
            ReadInput();

            float dt = Time.deltaTime;
            lastFrameSeconds = dt;
            // Smoothed over ~0.5 s: one frame's delta is whatever spiked that frame, and a
            // screenshot capture alone reads as 3 fps on an otherwise healthy scene.
            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(1e-4f, dt),
                                     1f - Mathf.Exp(-dt / 0.5f));
            provider.Tick(dt);

            PoseFrame frame;
            if (provider.TryGetLatestFrame(out frame) && frame != null && frame.IsValid) {
                everTracked = true;
                PoseFrame shown = frame;
                if (useHumanizedSkeleton) {
                    // Same rule the mirror app uses: advance the layer's temporal state ONCE per pose,
                    // with the inter-pose delta. Running it per render frame would let a clamped joint
                    // creep between poses, which measured as the humanized stream jumping MORE than the
                    // raw one (F-27). The layer owns its output frame and returns the same instance
                    // every call, so caching the reference between poses is safe and allocation-free.
                    double ts = frame.TimestampSeconds;
                    if (humanizedFrame == null || ts != lastPoseTimestamp) {
                        float poseDelta = (float)(ts - lastPoseTimestamp);
                        if (poseDelta <= 0f || poseDelta > 0.5f) {
                            poseDelta = dt;
                        }
                        humanizedFrame = humanized.Process(frame, poseDelta);
                        lastPoseTimestamp = ts;
                    }
                    shown = humanizedFrame;
                }
                pose.Fill(shown, bodyOrigin, bodyScale, dt);
            } else {
                pose.Fill(null, bodyOrigin, bodyScale, dt);
            }

            if (activeMode >= 0) {
                modes[activeMode].Render(pose, dt);
            }
        }

        private void ReadInput() {
            if (Input.GetKeyDown(KeyCode.Alpha1)) {
                SwitchTo(0);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2)) {
                SwitchTo(1);
            }
            if (Input.GetKeyDown(KeyCode.Alpha3)) {
                SwitchTo(2);
            }
            if (Input.GetKeyDown(KeyCode.Tab)) {
                SwitchTo((activeMode + 1) % modes.Length);
            }
            if (Input.GetKeyDown(KeyCode.H)) {
                useHumanizedSkeleton = !useHumanizedSkeleton;
            }
        }

        /// <summary>Tear the old mode down completely before the new one builds. Nothing survives a
        /// switch - see <see cref="ISkeletonMode"/>.</summary>
        public void SwitchTo(int index) {
            if (modes == null || index < 0 || index >= modes.Length || index == activeMode) {
                return;
            }
            if (activeMode >= 0) {
                modes[activeMode].Deactivate();
            }
            activeMode = index;
            modes[activeMode].Activate(modeRoot, palette);
        }

        // ---------------------------------------------------------------------------------------
        // The stage: camera, background, bloom and a floor grid. Built here so the scene asset stays
        // a camera and one GameObject.
        // ---------------------------------------------------------------------------------------
        private void BuildStage() {
            Camera cam = Camera.main;
            if (cam == null) {
                GameObject camObject = new GameObject("Main Camera");
                camObject.tag = "MainCamera";
                cam = camObject.AddComponent<Camera>();
            }
            // Framed for a life-size figure: at 46 deg vertical FOV and 3.2 m the view is ~2.7 m tall, which
            // holds a 1.7 m body with head- and foot-room. Closer than this and the first live capture
            // cropped the head and the feet off the frame.
            cam.transform.position = new Vector3(0f, 1.0f, -3.2f);
            cam.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = palette.Background;
            cam.fieldOfView = 46f;
            UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) {
                data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            // Without this the HDR colours simply clip to white and none of the three modes glows.
            data.renderPostProcessing = true;

            GameObject volumeObject = new GameObject("PostProcessing");
            volumeObject.transform.SetParent(transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SkeletonShowProfile";
            volume.sharedProfile = profile;
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;      // below the palette's HDR values, above ordinary pixels
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.5f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.72f;
            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;   // keeps the hot end from turning to paste

            BuildFloorGrid();
        }

        // A faint grid so the body has somewhere to stand and the ripples in mode 3 have something to
        // travel across. Lines rather than a plane: a lit plane would need a light, and a light would
        // wash out the additive glow everything else depends on.
        private void BuildFloorGrid() {
            GameObject grid = new GameObject("FloorGrid");
            grid.transform.SetParent(transform, false);
            Material material = palette.NewAdditive(new Color(palette.Cool.r * 0.06f,
                                                              palette.Cool.g * 0.06f,
                                                              palette.Cool.b * 0.10f, 0.35f));
            const int lines = 21;
            const float extent = 5f;
            int i = 0;
            while (i < lines) {
                float t = -extent + (2f * extent) * (i / (float)(lines - 1));
                MakeGridLine(grid.transform, material, new Vector3(t, 0f, -extent), new Vector3(t, 0f, extent));
                MakeGridLine(grid.transform, material, new Vector3(-extent, 0f, t), new Vector3(extent, 0f, t));
                i = i + 1;
            }
        }

        private static void MakeGridLine(Transform parent, Material material, Vector3 a, Vector3 b) {
            GameObject go = new GameObject("grid");
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = 0.006f;
            lr.endWidth = 0.006f;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = material;
        }

        // ---------------------------------------------------------------------------------------
        // On-screen switcher. IMGUI on purpose: it needs no canvas, no event system and no prefab, so
        // the scene asset stays trivial. This is a show/diagnostic scene, not shipped UI.
        // ---------------------------------------------------------------------------------------
        private void OnGUI() {
            if (hudStyle == null) {
                hudStyle = new GUIStyle(GUI.skin.label);
                hudStyle.fontSize = 15;
                hudStyle.normal.textColor = new Color(0.75f, 0.9f, 1f, 1f);
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontSize = 14;
            }
            const float w = 250f;
            const float h = 34f;
            int i = 0;
            while (i < modes.Length) {
                bool current = i == activeMode;
                GUI.backgroundColor = current ? new Color(0.25f, 0.85f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.28f);
                if (GUI.Button(new Rect(16f, 16f + i * (h + 6f), w, h), modes[i].Name, buttonStyle)) {
                    SwitchTo(i);
                }
                i = i + 1;
            }
            GUI.backgroundColor = Color.white;

            // Status lives at the BOTTOM of the screen: the first live capture put it across the
            // subject's chest, which is exactly where the thing being judged is.
            float y = Screen.height - 96f;
            if (activeMode >= 0) {
                GUI.Label(new Rect(16f, y, 1200f, 22f), modes[activeMode].Description, hudStyle);
            }
            y = y + 22f;
            GUI.Label(new Rect(16f, y, 1200f, 22f),
                      "1 / 2 / 3 or TAB switch mode      H toggles the humanized skeleton", hudStyle);
            y = y + 22f;
            string tracking;
            if (!everTracked) {
                tracking = "WAITING for the sidecar on UDP " + udpPort + " - no pose has arrived yet";
            } else if (pose.Valid) {
                tracking = "TRACKING   energy " + pose.Energy.ToString("F2");
            } else {
                tracking = "NO BODY IN FRAME";
            }
            GUI.Label(new Rect(16f, y, 1600f, 22f),
                      tracking + "      humanized: " + (useHumanizedSkeleton ? "ON" : "OFF")
                      + "      " + smoothedFps.ToString("F0") + " fps",
                      hudStyle);
            if (useHumanizedSkeleton) {
                y = y + 22f;
                HumanizedStats s = humanized.Stats;
                GUI.Label(new Rect(16f, y, 1600f, 22f), "F-27  " + s.ToString(), hudStyle);
            }
        }

        /// <summary>Minimal <see cref="ILogService"/> so this scene needs no file logging and no
        /// dependency on the IO assembly. Must never throw, per the interface contract.</summary>
        private sealed class UnityConsoleLog : ILogService {
            public void Log(LogLevel level, string message) {
                if (level == LogLevel.Error) {
                    Debug.LogError("[F28] " + message);
                } else if (level == LogLevel.Warning) {
                    Debug.LogWarning("[F28] " + message);
                } else {
                    Debug.Log("[F28] " + message);
                }
            }

            public void LogException(System.Exception exception, string context) {
                Debug.LogError("[F28] " + context + ": " + (exception != null ? exception.Message : "null"));
            }

            public void Flush() {
            }
        }
    }
}
