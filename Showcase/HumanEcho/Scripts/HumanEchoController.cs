using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering.Universal;

using VirtualMirror.App;
using VirtualMirror.SkeletonShow;
using VirtualMirror.Vision;

namespace HumanEcho {
    /// <summary>
    /// Human Echo: the live person, from the Python sidecar's cutout stream, drawn as smoke (FluXY), echo
    /// copies, a neon outline, the true-colour
    /// cutout and GPU particles. This component is the conductor: it finds the cutout textures the scene's
    /// <see cref="AppBootstrap"/> keeps, runs the
    /// <see cref="PersonField"/> stage once per new mask frame and ticks every layer with the ACTIVE look (a
    /// <see cref="HumanEchoSettings"/> asset).
    /// It owns no socket and starts no process: the bootstrap's vision host does both (the same arrangement
    /// as the Cutout, Hands and Face scenes).
    ///
    /// Looks switch instantly because every layer reads its settings every frame. The runtime layer toggles
    /// (<see cref="ToggleLayer"/>) are kept
    /// here, never on the settings assets (an asset edited in Play mode keeps the edit in the Editor,
    /// ADR-091).
    /// </summary>
    [AddComponentMenu("Virtual Mirror/Human Echo Controller")]
    public sealed class HumanEchoController : MonoBehaviour {
        private const int LayerCount = 5;
        private const float StatusRefreshSeconds = 0.5f;

        /// <summary>No new mask for this long and the person field is emptied (a mask normally arrives 15 to
        /// 30 times a second).</summary>
        private const float StaleSeconds = 1f;

        [SerializeField] private Camera showcaseCamera;
        [Header("Shaders")]
        [SerializeField] private Shader personFieldShader;
        [SerializeField] private Shader smokeInjectShader;
        [SerializeField] private Shader smokeShader;
        [SerializeField] private Shader echoShader;
        [SerializeField] private Shader outlineShader;
        [SerializeField] private Shader personShader;
        [SerializeField] private ComputeShader particleCompute;
        [SerializeField] private Shader particleDrawShader;
        [Header("Looks")]
        [Tooltip("The looks the number keys and Tab cycle. Empty: one built-in default look is used.")]
        [SerializeField] private HumanEchoSettings[] looks;
        [SerializeField] private int startLook;
        [SerializeField] private MirrorMode mirror = MirrorMode.Auto;
        [SerializeField] private bool showDebugTiles;

        private readonly bool[] layerOn = new bool[LayerCount];
        private PersonField field;
        private SmokeLayer smokeLayer;
        private ParticleLayer particleLayer;
        private EchoLayer echoLayer;
        private OutlineLayer outlineLayer;
        private PersonLayer personLayer;
        private CutoutStreamBinder binder;
        private HumanEchoSettings fallbackLook;
        private HumanEchoSettings[] lookArray;
        private int lookIndex;
        private float viewHeight = 2.4f;
        private float viewWidth = 2.4f * 16f / 9f;
        private float lastAspect;
        private bool mirrorApplied;
        private float displayFps;
        private float maskRate;
        private int lastFieldUpdates;
        private float rateClock;
        private string statusLine = "";
        private float statusClock;
        private bool newMaskThisFrame;
        private int seenVersion = -1;
        private int seenGeneration = -1;
        private float lastFrameTime;
        private bool blanked;
        private bool leaving;

        /// <summary>The looks in cycling order (never empty: a built-in default look stands in when none is
        /// assigned).</summary>
        public IReadOnlyList<HumanEchoSettings> Looks {
            get {
                return lookArray;
            }
        }

        public int LookIndex {
            get {
                return lookIndex;
            }
        }

        public HumanEchoSettings CurrentLook {
            get {
                return lookArray != null && lookArray.Length > 0 ? lookArray[lookIndex] : null;
            }
        }

        public MirrorMode Mirror {
            get {
                return mirror;
            }
        }

        /// <summary>Whether the display is flipped left to right this frame.</summary>
        public bool MirrorApplied {
            get {
                return mirrorApplied;
            }
        }

        /// <summary>Smoothed display frames per second.</summary>
        public float DisplayFps {
            get {
                return displayFps;
            }
        }

        /// <summary>Mask frames per second reaching the person field (what the sender delivers).</summary>
        public float MaskRate {
            get {
                return maskRate;
            }
        }

        /// <summary>The cutout stream's textures, or null until the vision host exists.</summary>
        public CutoutTextures Textures {
            get {
                AppBootstrap bootstrap = AppBootstrap.Instance;
                if (bootstrap == null || bootstrap.Vision == null) {
                    return null;
                }
                return bootstrap.Vision.CutoutTextures;
            }
        }

        public PersonField Field {
            get {
                return field;
            }
        }

        public SmokeLayer SmokeLayer {
            get {
                return smokeLayer;
            }
        }

        public ParticleLayer ParticleLayer {
            get {
                return particleLayer;
            }
        }

        public EchoLayer EchoLayer {
            get {
                return echoLayer;
            }
        }

        public OutlineLayer OutlineLayer {
            get {
                return outlineLayer;
            }
        }

        public PersonLayer PersonLayer {
            get {
                return personLayer;
            }
        }

        public bool ShowDebugTiles {
            get {
                return showDebugTiles;
            }
            set {
                showDebugTiles = value;
            }
        }

        /// <summary>A short text about the stream (people, foreground share, sender timing), refreshed twice
        /// a second.</summary>
        public string StatusLine {
            get {
                return statusLine;
            }
        }

        public bool IsLayerOn(HumanEchoLayerId id) {
            return layerOn[(int)id];
        }

        public void ToggleLayer(HumanEchoLayerId id) {
            layerOn[(int)id] = !layerOn[(int)id];
        }

        public void SelectLook(int index) {
            if (lookArray == null || lookArray.Length == 0) {
                return;
            }
            lookIndex = Mathf.Clamp(index, 0, lookArray.Length - 1);
            ApplyLookBackground();
        }

        public void NextLook() {
            if (lookArray != null && lookArray.Length > 0) {
                SelectLook((lookIndex + 1) % lookArray.Length);
            }
        }

        public void CycleMirror() {
            mirror = (MirrorMode)(((int)mirror + 1) % 3);
        }

        /// <summary>Empties the fluid and the particle pool.</summary>
        public void ClearFluid() {
            if (smokeLayer != null) {
                smokeLayer.Clear();
            }
            if (particleLayer != null) {
                particleLayer.Clear();
            }
        }

        private void Awake() {
            if (showcaseCamera == null) {
                showcaseCamera = Camera.main;
            }
            for (int i = 0; i < LayerCount; i++) {
                layerOn[i] = true;
            }
            binder = gameObject.GetComponent<CutoutStreamBinder>();
            if (binder == null) {
                binder = gameObject.AddComponent<CutoutStreamBinder>();
            }
            BuildLookArray();
        }

        private void Start() {
            ConfigureCamera();
            ApplyLookBackground();
            if (personFieldShader == null) {
                Debug.LogError("[HumanEcho] The person field shader is missing. Run Showcase > Human Echo > Build Showcase Scene.", this);
                enabled = false;
                return;
            }
            field = new PersonField(personFieldShader);

            smokeLayer = new SmokeLayer();
            if (!smokeLayer.Build(transform, CurrentLook.smoke, smokeInjectShader, smokeShader, viewWidth, viewHeight)) {
                smokeLayer = null;
            }
            particleLayer = new ParticleLayer();
            if (!particleLayer.Build(particleCompute, particleDrawShader, smokeLayer)) {
                particleLayer = null;
            }

            echoLayer = new EchoLayer(transform, echoShader);
            if (!echoLayer.IsReady) {
                echoLayer.Dispose();
                echoLayer = null;
            }
            // The outline borrows the echo layer's ring of past masks for its delayed look.
            outlineLayer = new OutlineLayer(transform, outlineShader, echoLayer != null ? echoLayer.Ring : null);
            if (!outlineLayer.IsReady) {
                outlineLayer.Dispose();
                outlineLayer = null;
            }
            personLayer = new PersonLayer(transform, personShader);
            if (!personLayer.IsReady) {
                personLayer.Dispose();
                personLayer = null;
            }
        }

        private void Update() {
            // ESC returns to the Launcher, as in every experience scene; nothing happens when the scene was
            // opened on its own.
            if (!leaving && LauncherScene.IsAvailable && LauncherScene.ReturnRequested()) {
                leaving = LauncherScene.Load();
            }
            ApplyViewSettings();

            CutoutTextures textures = Textures;
            float now = Time.unscaledTime;
            bool hasFrame = textures != null && textures.HasFrame;
            if (hasFrame && textures.Generation != seenGeneration) {
                // The texture set was (re)created: a new stream or a new picture size, so nothing of the old
                // one may be compared with it.
                seenGeneration = textures.Generation;
                ForgetHistory();
            }
            if (hasFrame && textures.Version != seenVersion) {
                seenVersion = textures.Version;
                lastFrameTime = now;
            }

            newMaskThisFrame = false;
            if (!hasFrame || now - lastFrameTime > StaleSeconds) {
                // No fresh mask: empty the field once, instead of letting the layers keep stamping the last
                // silhouette for ever.
                if (!blanked) {
                    field.Blank();
                    if (echoLayer != null) {
                        echoLayer.Clear();
                    }
                    blanked = true;
                }
            } else {
                bool mirrorNow = mirror == MirrorMode.On || (mirror == MirrorMode.Auto && textures.Live && !textures.Mirrored);
                if (mirrorNow != mirrorApplied) {
                    // A mirrored mask next to an unmirrored one would read as the silhouette jumping across
                    // the screen.
                    ForgetHistory();
                }
                mirrorApplied = mirrorNow;
                blanked = false;
                newMaskThisFrame = field.Update(textures, mirrorApplied, now);
            }
            if (field.Field != null && field.Motion != null) {
                TickLayers();
            }
            UpdateStatistics(textures);
        }

        // The person field forgets its previous mask and the echo ring its captures.
        private void ForgetHistory() {
            if (field != null) {
                field.Reset();
            }
            if (echoLayer != null) {
                echoLayer.Clear();
            }
        }

        // The active look's screen height and background are read every frame, so they can be edited while
        // playing.
        private void ApplyViewSettings() {
            HumanEchoSettings look = CurrentLook;
            if (look != null) {
                float wanted = Mathf.Max(0.5f, look.viewHeightMetres);
                if (!Mathf.Approximately(wanted, viewHeight)) {
                    viewHeight = wanted;
                    ConfigureCamera();
                    if (smokeLayer != null) {
                        smokeLayer.Resize(viewWidth, viewHeight);
                    }
                }
                if (showcaseCamera != null) {
                    showcaseCamera.backgroundColor = look.backgroundColor;
                }
            }
            if (showcaseCamera != null && !Mathf.Approximately(showcaseCamera.aspect, lastAspect)) {
                ConfigureCamera();
                if (smokeLayer != null) {
                    smokeLayer.Resize(viewWidth, viewHeight);
                }
            }
        }

        private void OnDestroy() {
            if (personLayer != null) {
                personLayer.Dispose();
            }
            if (outlineLayer != null) {
                outlineLayer.Dispose();
            }
            if (echoLayer != null) {
                echoLayer.Dispose();
            }
            if (particleLayer != null) {
                particleLayer.Dispose();
            }
            if (smokeLayer != null) {
                smokeLayer.Dispose();
            }
            if (field != null) {
                field.Dispose();
            }
            if (fallbackLook != null) {
                Destroy(fallbackLook);
            }
        }

        private void TickLayers() {
            HumanEchoSettings look = CurrentLook;
            FrameContext frame = new FrameContext();
            frame.DeltaTime = Time.deltaTime;
            frame.Time = Time.time;
            frame.ViewWidth = viewWidth;
            frame.ViewHeight = viewHeight;
            frame.NewMask = newMaskThisFrame;

            if (smokeLayer != null) {
                smokeLayer.Settings = look.smoke;
                TickLayer(smokeLayer, HumanEchoLayerId.Smoke, look.smoke.enabled, frame);
            }
            if (echoLayer != null) {
                echoLayer.Settings = look.echo;
                // The ring keeps the pace of the active look even while the echo layer is hidden (the outline
                // may still borrow it).
                echoLayer.Ring.SpacingSeconds = look.echo.spacingSeconds;
                TickLayer(echoLayer, HumanEchoLayerId.Echo, look.echo.enabled, frame);
            }
            if (personLayer != null) {
                personLayer.Settings = look.person;
                TickLayer(personLayer, HumanEchoLayerId.Person, look.person.enabled, frame);
            }
            if (particleLayer != null) {
                particleLayer.Settings = look.particles;
                TickLayer(particleLayer, HumanEchoLayerId.Particles, look.particles.enabled, frame);
            }
            if (outlineLayer != null) {
                outlineLayer.Settings = look.outline;
                TickLayer(outlineLayer, HumanEchoLayerId.Outline, look.outline.enabled, frame);
            }
        }

        private void TickLayer(IHumanEchoLayer layer, HumanEchoLayerId id, bool settingsEnabled, FrameContext frame) {
            if (!layer.IsReady) {
                return;
            }
            if (layerOn[(int)id] && settingsEnabled) {
                layer.SetActive(true);
                layer.Tick(field, frame);
            } else {
                layer.SetActive(false);
            }
        }

        private void BuildLookArray() {
            if (looks != null && looks.Length > 0) {
                int valid = 0;
                for (int i = 0; i < looks.Length; i++) {
                    if (looks[i] != null) {
                        valid++;
                    }
                }
                if (valid > 0) {
                    lookArray = new HumanEchoSettings[valid];
                    int next = 0;
                    for (int i = 0; i < looks.Length; i++) {
                        if (looks[i] != null) {
                            lookArray[next] = looks[i];
                            next++;
                        }
                    }
                }
            }
            if (lookArray == null) {
                // No look assigned: a plain smoke look so the scene still shows something.
                fallbackLook = ScriptableObject.CreateInstance<HumanEchoSettings>();
                fallbackLook.displayName = "Default (no look assigned)";
                lookArray = new HumanEchoSettings[] { fallbackLook };
            }
            lookIndex = Mathf.Clamp(startLook, 0, lookArray.Length - 1);
            viewHeight = Mathf.Max(0.5f, lookArray[lookIndex].viewHeightMetres);
        }

        private void ApplyLookBackground() {
            if (showcaseCamera != null && CurrentLook != null) {
                showcaseCamera.backgroundColor = CurrentLook.backgroundColor;
            }
        }

        private void ConfigureCamera() {
            if (showcaseCamera == null) {
                viewWidth = viewHeight * 16f / 9f;
                return;
            }
            showcaseCamera.orthographic = true;
            showcaseCamera.orthographicSize = viewHeight * 0.5f;
            showcaseCamera.transform.position = new Vector3(0f, 0f, -10f);
            showcaseCamera.transform.rotation = Quaternion.identity;
            showcaseCamera.clearFlags = CameraClearFlags.SolidColor;
            showcaseCamera.allowHDR = true;
            showcaseCamera.allowMSAA = false;
            UniversalAdditionalCameraData data = showcaseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) {
                data.renderPostProcessing = true;
            }
            lastAspect = showcaseCamera.aspect;
            viewWidth = viewHeight * showcaseCamera.aspect;
        }

        private void UpdateStatistics(CutoutTextures textures) {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            displayFps = displayFps + (1f / dt - displayFps) * 0.05f;
            rateClock += dt;
            if (rateClock >= 1f) {
                maskRate = (field.Updates - lastFieldUpdates) / rateClock;
                lastFieldUpdates = field.Updates;
                rateClock = 0f;
            }
            statusClock += dt;
            if (statusClock >= StatusRefreshSeconds) {
                statusClock = 0f;
                if (textures == null || !textures.HasFrame) {
                    statusLine = "no frame yet";
                } else {
                    statusLine = "people " + textures.People + "  foreground " + (textures.ForegroundFraction * 100f).ToString("F0")
                                 + "%  sender " + textures.SenderLatencyMs.ToString("F0") + " ms";
                }
            }
        }

        private void OnGUI() {
            if (!showDebugTiles) {
                return;
            }
            float tile = 200f;
            float y = Screen.height - tile - 10f;
            DrawTile(field != null ? field.Field : null, 10f, y, tile, "field");
            DrawTile(field != null ? field.Motion : null, 220f, y, tile, "motion");
            DrawTile(smokeLayer != null ? smokeLayer.DyePattern : null, 430f, y, tile, "dye pattern");
        }

        private static void DrawTile(Texture texture, float x, float y, float size, string label) {
            GUI.Box(new Rect(x - 2f, y - 18f, size + 4f, size + 20f), label);
            if (texture != null) {
                GUI.DrawTexture(new Rect(x, y, size, size), texture, ScaleMode.ScaleToFit, false);
            }
        }
    }
}
