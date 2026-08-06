using System;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using UniVRM10;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Camera;
using VirtualMirror.Core;
using VirtualMirror.Diagnostics;
using VirtualMirror.IK;
using VirtualMirror.IO;
using VirtualMirror.Rendering;
using VirtualMirror.Retargeting;
using VirtualMirror.Settings;
using VirtualMirror.Tracking;
using VirtualMirror.Tracking.Filtering;
using VirtualMirror.Tracking.MediaPipe;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Tracking.Sentis;
using VirtualMirror.UI;

namespace VirtualMirror.App {
    /// <summary>
    /// Application composition root. Lives in Bootstrap.unity: wires the core services, loads
    /// settings, additively loads the Mirror scene, then builds the avatar session against the
    /// Mirror scene's avatar root and auto-loads the last used avatar. Survives scene loads.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour {
        [SerializeField] private string mirrorSceneName = "Mirror";
        [SerializeField] private string avatarRootName = "AvatarRoot";
        [SerializeField] private bool loadMirrorSceneOnStart = true;
        [SerializeField] private float settingsSaveDebounceSeconds = 0.4f;
        [SerializeField] private bool mirrorLogToConsole = true;
        [SerializeField] private long maxAvatarFileBytes = 268435456;
        [SerializeField] private bool useVideoSource = false;
        [SerializeField] private string sampleVideoPath = "C:/Unity/viitorx-vrm-avtar-unity-base-project/sampleVRMFiles/sample video.mp4";
        [SerializeField] private int cameraWidth = 1280;
        [SerializeField] private int cameraHeight = 720;
        [SerializeField] private int cameraFps = 30;
        [SerializeField] private float filterMinCutoff = 1f;
        [SerializeField] private float filterBeta = 0.2f;
        [SerializeField] private float filterDerivativeCutoff = 1f;
        [SerializeField] private bool useMediaPipeTracking = true;
        [SerializeField] private string poseModelFileName = "pose_landmarker_heavy.bytes";
        [SerializeField] private bool poseFlipX = true;
        [SerializeField] private bool poseFlipY = true;
        [SerializeField] private bool poseFlipZ = true;
        [SerializeField] private float retargetMinConfidence = 0.5f;
        [SerializeField] private float retargetExitConfidence = 0.3f;
        [SerializeField] private float poseStaleSeconds = 0.5f;
        [SerializeField] private bool useIkDriver = true;
        [SerializeField] private bool useFaceTracking = true;
        [SerializeField] private bool useMediaPipeFace = true;
        [SerializeField] private string faceModelFileName = "face_landmarker.bytes";
        [SerializeField] private bool useHandTracking = true;
        [SerializeField] private bool useMediaPipeHand = true;
        [SerializeField] private string handModelFileName = "hand_landmarker.bytes";
        [SerializeField] private float wristRotationWeight = 0.7f;
        [SerializeField] private bool useSentis3dTracking = false;
        [SerializeField] private Unity.InferenceEngine.ModelAsset sentisModel;
        [SerializeField] private bool sentisImageNetNorm = true;
        [SerializeField] private float sentisMetreScale = 1.7f;
        [SerializeField] private float sentisDepthScale = 0.8f;
        [SerializeField] private bool sentisPersonCrop = true;
        [SerializeField] private float sentisFilterBeta = 0.6f;
        [SerializeField] private float sentisFilterMinCutoff = 1.5f;
        [SerializeField] private bool useOakDTracking = false; // B1 (in-Unity native plugin) — abandoned, keep off
        [SerializeField] private string oakModelFileName = "movenet_singlepose_lightning_3.blob";
        [SerializeField] private int oakDeviceNum = 0;
        [SerializeField] private float oakLandmarkThreshold = 0.3f;
        [SerializeField] private bool useOakUdpTracking = false; // B2 (Python sidecar over UDP) — ADR-016
        [SerializeField] private int oakUdpPort = 8899;

        private ServiceRegistry services;
        private LogService logService;
        private SettingsStore settingsStore;
        private AvatarSessionController avatarSession;
        private ICameraCapture cameraCapture;
        private IBodyTrackingProvider bodyProvider;
        private JointFilterPipeline jointFilter;
        private HumanoidPoseRetargeter retargeter;
        private IIkSolver ikSolver;
        private IFaceTrackingProvider faceProvider;
        private VrmExpressionRetargeter expressionRetargeter;
        private IHandTrackingProvider handProvider;
        private HumanoidHandRetargeter handRetargeter;
        private PerformanceMonitor performanceMonitor;
        private DiagnosticsHudPanel diagnosticsHud;
        private CalibrationSettingsPanel calibrationPanel;
        private Animator boundAnimator;
        private MirrorCameraController cameraController;
        private double lastFrameTimestamp;
        private float lastFreshTime;

        public ServiceRegistry Services {
            get {
                return services;
            }
        }

        public AvatarSessionController AvatarSession {
            get {
                return avatarSession;
            }
        }

        public ICameraCapture CameraCapture {
            get {
                return cameraCapture;
            }
        }

        private void Awake() {
            DontDestroyOnLoad(gameObject);
            InitializeServices();
        }

        private void OnEnable() {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable() {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Start() {
            Scene mirrorScene = SceneManager.GetSceneByName(mirrorSceneName);
            if (mirrorScene.isLoaded && avatarSession == null) {
                SetupAvatarSession(mirrorScene);
            } else if (loadMirrorSceneOnStart && !mirrorScene.isLoaded) {
                LoadMirrorScene();
            }
        }

        private void Update() {
            if (settingsStore != null) {
                settingsStore.Tick(Time.unscaledDeltaTime);
            }
        }

        private void LateUpdate() {
            UpdateTracking();
        }

        private bool IsPoseStale(PoseFrame frame) {
            if (frame == null || !frame.IsValid) {
                return true;
            }
            double timestamp = frame.TimestampSeconds;
            if (timestamp != lastFrameTimestamp) {
                lastFrameTimestamp = timestamp;
                lastFreshTime = Time.unscaledTime;
            }
            return (Time.unscaledTime - lastFreshTime) > poseStaleSeconds;
        }

        private void UpdateTracking() {
            if (bodyProvider == null || retargeter == null || jointFilter == null) {
                return;
            }
            float deltaSeconds = Time.deltaTime;
            if (bodyProvider is SentisPoseProvider) {
                // Push the amplitude knobs live so they can be tuned in the Inspector during Play.
                SentisPoseProvider sentisProvider = (SentisPoseProvider)bodyProvider;
                sentisProvider.SetTuning(sentisMetreScale, sentisDepthScale);
            }
            bodyProvider.Tick(deltaSeconds);
            if (Input.GetKeyDown(KeyCode.C)) {
                retargeter.Recalibrate();
                if (handRetargeter != null) {
                    handRetargeter.Recalibrate();
                }
            }

            Animator animator = null;
            if (avatarSession != null && avatarSession.Current != null) {
                animator = avatarSession.Current.Animator;
            }
            if (animator == null) {
                return;
            }
            if (animator != boundAnimator) {
                retargeter.Bind(animator);
                if (ikSolver != null) {
                    Transform avatarRoot = avatarSession.Current.Root != null ? avatarSession.Current.Root.transform : null;
                    ikSolver.Bind(animator, avatarRoot);
                }
                boundAnimator = animator;
            }
            PoseFrame frame;
            if (bodyProvider.TryGetLatestFrame(out frame)) {
                PoseFrame filtered = jointFilter.Filter(frame, deltaSeconds);
                // FK and IK are mutually exclusive on the arm/leg bones: when the IK solver is active and
                // bound it owns the limbs, so FK drives only torso (hips/spine) + neck. Otherwise FK drives
                // everything and the solver is released so the rig stops deforming the limbs.
                bool ikActive = useIkDriver && ikSolver != null && ikSolver.IsBound;
                retargeter.SetArmsLegsDrivenExternally(ikActive);
                if (ikSolver != null && ikSolver.IsBound) {
                    ikSolver.SetActive(useIkDriver);
                }
                if (!IsPoseStale(filtered)) {
                    retargeter.Apply(filtered, retargetMinConfidence, retargetExitConfidence);
                    if (ikActive) {
                        ikSolver.Apply(filtered, retargetMinConfidence);
                    }
                }
            }

            if (useFaceTracking && faceProvider != null && expressionRetargeter != null) {
                faceProvider.Tick(deltaSeconds);
                if (avatarSession != null && avatarSession.Current != null && avatarSession.Current.Root != null) {
                    UniVRM10.Vrm10Instance vrmInstance = avatarSession.Current.Root.GetComponent<UniVRM10.Vrm10Instance>();
                    if (vrmInstance != null && !expressionRetargeter.IsBound) {
                        expressionRetargeter.Bind(vrmInstance);
                    }
                }
                FaceFrame faceFrame;
                if (faceProvider.TryGetFrame(out faceFrame)) {
                    expressionRetargeter.Apply(faceFrame);
                }
            } else if (expressionRetargeter != null && expressionRetargeter.IsBound) {
                // Face tracking toggled off: reset expressions to neutral so the face does not freeze.
                expressionRetargeter.Apply(null);
            }

            if (useHandTracking && handProvider != null && handRetargeter != null) {
                handProvider.Tick(deltaSeconds);
                if (!handRetargeter.IsBound) {
                    handRetargeter.Bind(animator);
                }
                HandFrame handFrame;
                if (handProvider.TryGetFrame(out handFrame)) {
                    handRetargeter.Apply(handFrame);
                }
            }
        }

        private void OnApplicationQuit() {
            if (faceProvider != null) {
                faceProvider.Dispose();
            }
            if (handProvider != null) {
                handProvider.Dispose();
            }
            if (ikSolver != null) {
                ikSolver.Dispose();
            }
            if (bodyProvider != null) {
                bodyProvider.Dispose();
            }
            if (cameraCapture != null) {
                cameraCapture.Dispose();
            }
            if (avatarSession != null) {
                avatarSession.Dispose();
            }
            if (settingsStore != null) {
                settingsStore.Flush();
            }
            if (logService != null) {
                logService.Log(LogLevel.Info, "Virtual Mirror shutting down.");
                logService.Flush();
                logService.Dispose();
            }
        }

        private void InitializeServices() {
            PathProvider pathProvider = new PathProvider();
            pathProvider.EnsureDirectories();
            logService = new LogService(pathProvider, mirrorLogToConsole);
            logService.Log(LogLevel.Info, "Virtual Mirror bootstrap starting.");
            settingsStore = new SettingsStore(pathProvider, logService, settingsSaveDebounceSeconds);
            settingsStore.Load();
            UniVrmAvatarLoader avatarLoader = new UniVrmAvatarLoader(logService, maxAvatarFileBytes);
            services = new ServiceRegistry(pathProvider, logService, settingsStore, avatarLoader);
            StartCameraCapture();
            StartTracking();
            logService.Log(LogLevel.Info, "Core services ready.");
        }

        private void StartCameraCapture() {
            if (useVideoSource) {
                cameraCapture = new VideoFileCaptureService(logService, sampleVideoPath);
            } else {
                cameraCapture = new WebcamCaptureService(logService);
            }
            CameraCaptureRequest request = new CameraCaptureRequest(string.Empty, cameraWidth, cameraHeight, cameraFps);
            bool started = cameraCapture.StartCapture(request);
            if (!started) {
                logService.Log(LogLevel.Warning, "Camera capture failed to start.");
            }
        }

        private void StartTracking() {
            jointFilter = new JointFilterPipeline(filterMinCutoff, filterBeta, filterDerivativeCutoff);
            retargeter = new HumanoidPoseRetargeter();
            if (useIkDriver) {
                ikSolver = new AnimationRiggingIkDriver();
            }
            PoseSpaceConverter converter = new PoseSpaceConverter(poseFlipX, poseFlipY, poseFlipZ);
            // OAK-D via the Python sidecar over UDP (B2, ADR-016) takes top priority. No native DLL in-process
            // → cannot crash Unity. Binds a UDP socket; if the port is free it "starts" and waits for the
            // sidecar's datagrams (avatar rests until the sidecar streams). Run udp_pose_sender.py separately.
            if (useOakUdpTracking) {
                OakDUdpPoseProvider oakUdp = new OakDUdpPoseProvider(logService, converter, oakUdpPort);
                oakUdp.StartTracking();
                if (oakUdp.IsRunning) {
                    bodyProvider = oakUdp;
                } else {
                    logService.Log(LogLevel.Warning, "OAK-D UDP provider failed to start; falling back.");
                    oakUdp.Dispose();
                }
            }
            // OAK-D in-Unity native plugin (B1) — abandoned as too crash-prone; kept behind the flag (OFF).
            if (bodyProvider == null && useOakDTracking) {
                string oakModelPath = Path.Combine(Application.dataPath, "Plugins", "OAKForUnity", "Models", oakModelFileName);
                OakDPoseProvider oakProvider = new OakDPoseProvider(logService, converter, oakModelPath, oakDeviceNum, oakLandmarkThreshold);
                oakProvider.StartTracking();
                if (oakProvider.IsRunning) {
                    bodyProvider = oakProvider;
                } else {
                    logService.Log(LogLevel.Warning, "OAK-D provider failed to start; falling back to RGB tracking.");
                    oakProvider.Dispose();
                }
            }
            if (bodyProvider == null) {
                if (useSentis3dTracking && sentisModel != null) {
                    bodyProvider = new SentisPoseProvider(logService, cameraCapture, converter, sentisModel, sentisImageNetNorm, sentisMetreScale, sentisDepthScale, sentisPersonCrop);
                } else if (useMediaPipeTracking) {
                    string modelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", poseModelFileName);
                    bodyProvider = new MediaPipePoseProvider(logService, cameraCapture, modelPath, converter);
                } else {
                    bodyProvider = new FakeBodyTrackingProvider();
                }
                bodyProvider.StartTracking();
                if (!bodyProvider.IsRunning && !(bodyProvider is FakeBodyTrackingProvider)) {
                    logService.Log(LogLevel.Warning, "Body tracking provider failed to start; falling back to fake tracking.");
                    bodyProvider.Dispose();
                    bodyProvider = new FakeBodyTrackingProvider();
                    bodyProvider.StartTracking();
                }
            }
            if (bodyProvider is SentisPoseProvider) {
                // The 3D path (fast dance, true depth) needs a snappier filter than the calm-webcam default,
                // or fast motion gets damped into the "under-responsive" look. Live-tunable via the sliders.
                filterMinCutoff = sentisFilterMinCutoff;
                filterBeta = sentisFilterBeta;
                jointFilter.SetParameters(sentisFilterMinCutoff, sentisFilterBeta);
            }
            logService.Log(LogLevel.Info, "Body tracking started.");

            if (useFaceTracking) {
                if (useMediaPipeFace) {
                    string faceModelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", faceModelFileName);
                    MediaPipeFaceProvider mediaPipeFace = new MediaPipeFaceProvider(logService, cameraCapture, faceModelPath);
                    if (mediaPipeFace.StartTracking()) {
                        faceProvider = mediaPipeFace;
                    } else {
                        logService.Log(LogLevel.Warning, "MediaPipe face provider failed to start; falling back to fake face tracking.");
                        mediaPipeFace.Dispose();
                        faceProvider = new FakeFaceTrackingProvider();
                        faceProvider.StartTracking();
                    }
                } else {
                    faceProvider = new FakeFaceTrackingProvider();
                    faceProvider.StartTracking();
                }
                expressionRetargeter = new VrmExpressionRetargeter();
                logService.Log(LogLevel.Info, "Face tracking started.");
            }
            if (useHandTracking) {
                if (useMediaPipeHand) {
                    string handModelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", handModelFileName);
                    PoseSpaceConverter handConverter = new PoseSpaceConverter(poseFlipX, poseFlipY, poseFlipZ);
                    MediaPipeHandProvider mediaPipeHand = new MediaPipeHandProvider(logService, cameraCapture, handModelPath, handConverter);
                    if (mediaPipeHand.StartTracking()) {
                        handProvider = mediaPipeHand;
                    } else {
                        logService.Log(LogLevel.Warning, "MediaPipe hand provider failed to start; falling back to fake hand tracking.");
                        mediaPipeHand.Dispose();
                        handProvider = new FakeHandTrackingProvider();
                        handProvider.StartTracking();
                    }
                } else {
                    handProvider = new FakeHandTrackingProvider();
                    handProvider.StartTracking();
                }
                handRetargeter = new HumanoidHandRetargeter();
                handRetargeter.SetWristWeight(wristRotationWeight);
                logService.Log(LogLevel.Info, "Hand tracking started.");
            }
        }

        private void LoadMirrorScene() {
            if (string.IsNullOrEmpty(mirrorSceneName)) {
                logService.Log(LogLevel.Warning, "Mirror scene name is empty; skipping additive load.");
                return;
            }
            if (SceneManager.GetSceneByName(mirrorSceneName).isLoaded) {
                return;
            }
            logService.Log(LogLevel.Info, "Additively loading scene: " + mirrorSceneName);
            SceneManager.LoadScene(mirrorSceneName, LoadSceneMode.Additive);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (scene.name != mirrorSceneName) {
                return;
            }
            SetupAvatarSession(scene);
        }

        private void SetupAvatarSession(Scene scene) {
            Transform avatarRoot = FindAvatarRoot(scene);
            if (avatarRoot == null) {
                logService.Log(LogLevel.Warning, "AvatarRoot '" + avatarRootName + "' not found in scene " + scene.name + ".");
                return;
            }
            avatarSession = new AvatarSessionController(services.AvatarLoader, logService, settingsStore, avatarRoot);
            AvatarMouseRotator mouseRotator = avatarRoot.gameObject.GetComponent<AvatarMouseRotator>();
            if (mouseRotator == null) {
                mouseRotator = avatarRoot.gameObject.AddComponent<AvatarMouseRotator>();
            }
            mouseRotator.SetTarget(avatarRoot);
            InitializeAvatarUi(scene);
            InitializeCameraPreview(scene);
            InitializeDiagnosticsHud(scene);
            InitializeCalibrationPanel(scene);
            cameraController = FindCameraController(scene);
            avatarSession.AvatarChanged += FrameCameraOnAvatar;
            TryAutoLoadLastAvatar();
        }

        private string DescribeActiveTracking() {
            if (bodyProvider is OakDUdpPoseProvider) {
                return "OAK-D 3D (UDP sidecar)";
            }
            if (bodyProvider is OakDPoseProvider) {
                return "OAK-D 3D (on-device)";
            }
            if (bodyProvider is SentisPoseProvider) {
                return "Sentis RTMW3D (GPU)";
            }
            if (bodyProvider is MediaPipePoseProvider) {
                return "MediaPipe Pose (CPU)";
            }
            return "Fake Tracking";
        }

        private void InitializeDiagnosticsHud(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                DiagnosticsHudPanel panel = roots[index].GetComponentInChildren<DiagnosticsHudPanel>(true);
                if (panel != null) {
                    diagnosticsHud = panel;
                    diagnosticsHud.Initialize(performanceMonitor);
                    diagnosticsHud.SetTrackingStatus(DescribeActiveTracking());
                    diagnosticsHud.SetCameraInfo(useVideoSource ? "Sample Video MP4" : "Webcam 1280x720@30fps");
                    logService.Log(LogLevel.Info, "Diagnostics HUD wired.");
                    return;
                }
                index = index + 1;
            }
        }

        private void InitializeCalibrationPanel(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                CalibrationSettingsPanel panel = roots[index].GetComponentInChildren<CalibrationSettingsPanel>(true);
                if (panel != null) {
                    calibrationPanel = panel;
                    calibrationPanel.SetInitialValues(poseFlipX, useIkDriver, useFaceTracking, useHandTracking, filterMinCutoff, filterBeta);
                    calibrationPanel.OnCameraDeviceChanged += HandleCameraDeviceChanged;
                    calibrationPanel.OnMirrorFlipToggled += (value) => { poseFlipX = value; };
                    calibrationPanel.OnIkToggled += (value) => { useIkDriver = value; };
                    calibrationPanel.OnFaceToggled += (value) => { useFaceTracking = value; };
                    calibrationPanel.OnHandToggled += (value) => { useHandTracking = value; };
                    calibrationPanel.OnFilterMinCutoffChanged += (value) => {
                        filterMinCutoff = value;
                        if (jointFilter != null) {
                            jointFilter.SetParameters(filterMinCutoff, filterBeta);
                        }
                    };
                    calibrationPanel.OnFilterBetaChanged += (value) => {
                        filterBeta = value;
                        if (jointFilter != null) {
                            jointFilter.SetParameters(filterMinCutoff, filterBeta);
                        }
                    };
                    logService.Log(LogLevel.Info, "Calibration settings panel wired.");
                    return;
                }
                index = index + 1;
            }
        }

        private void HandleCameraDeviceChanged(string deviceName) {
            if (cameraCapture == null || string.IsNullOrEmpty(deviceName)) {
                return;
            }
            cameraCapture.StopCapture();
            CameraCaptureRequest request = new CameraCaptureRequest(deviceName, cameraWidth, cameraHeight, cameraFps);
            bool started = cameraCapture.StartCapture(request);
            if (started && diagnosticsHud != null) {
                diagnosticsHud.SetCameraInfo(deviceName + " (" + cameraWidth + "x" + cameraHeight + ")");
            }
            logService.Log(LogLevel.Info, "Camera device changed to: " + deviceName + " (Started: " + started + ")");
        }

        private MirrorCameraController FindCameraController(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                MirrorCameraController controller = roots[index].GetComponentInChildren<MirrorCameraController>(true);
                if (controller != null) {
                    return controller;
                }
                index = index + 1;
            }
            return null;
        }

        private void FrameCameraOnAvatar() {
            if (avatarSession == null || avatarSession.Current == null) {
                return;
            }
            if (diagnosticsHud != null) {
                diagnosticsHud.SetAvatarName(avatarSession.Current.Root != null ? avatarSession.Current.Root.name : "Avatar");
            }
            if (cameraController != null) {
                GameObject root = avatarSession.Current.Root;
                if (root != null) {
                    cameraController.Frame(root.transform);
                }
            }
        }

        private void InitializeCameraPreview(Scene scene) {
            CameraPreviewController preview = FindCameraPreview(scene);
            if (preview == null) {
                logService.Log(LogLevel.Info, "No CameraPreviewController found in scene; skipping preview wiring.");
                return;
            }
            preview.Initialize(cameraCapture);
            logService.Log(LogLevel.Info, "Camera preview wired.");
        }

        private CameraPreviewController FindCameraPreview(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                CameraPreviewController preview = roots[index].GetComponentInChildren<CameraPreviewController>(true);
                if (preview != null) {
                    return preview;
                }
                index = index + 1;
            }
            return null;
        }

        private void InitializeAvatarUi(Scene scene) {
            AvatarLibraryPanel panel = FindAvatarPanel(scene);
            if (panel == null) {
                logService.Log(LogLevel.Info, "No AvatarLibraryPanel found in scene; skipping avatar UI wiring.");
                return;
            }
            panel.Initialize(avatarSession, logService);
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                Transform btnTransform = roots[index].transform.Find("ToggleUiButton");
                if (btnTransform != null) {
                    UnityEngine.UI.Button btn = btnTransform.GetComponent<UnityEngine.UI.Button>();
                    if (btn != null) {
                        btn.onClick.AddListener(() => {
                            panel.gameObject.SetActive(!panel.gameObject.activeSelf);
                        });
                    }
                    break;
                }
                index = index + 1;
            }
            logService.Log(LogLevel.Info, "Avatar UI wired.");
        }

        private AvatarLibraryPanel FindAvatarPanel(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                AvatarLibraryPanel panel = roots[index].GetComponentInChildren<AvatarLibraryPanel>(true);
                if (panel != null) {
                    return panel;
                }
                index = index + 1;
            }
            return null;
        }

        private Transform FindAvatarRoot(Scene scene) {
            GameObject[] roots = scene.GetRootGameObjects();
            int index = 0;
            while (index < roots.Length) {
                GameObject root = roots[index];
                if (root.name == avatarRootName) {
                    return root.transform;
                }
                index = index + 1;
            }
            return null;
        }

        private void TryAutoLoadLastAvatar() {
            AppSettings settings = settingsStore.Current;
            if (settings == null || settings.Avatar == null) {
                return;
            }
            string lastPath = settings.Avatar.LastPath;
            if (string.IsNullOrEmpty(lastPath)) {
                logService.Log(LogLevel.Info, "No last avatar path set; skipping auto-load.");
                return;
            }
            logService.Log(LogLevel.Info, "Auto-loading last avatar: " + lastPath);
            RunAutoLoad(lastPath);
        }

        private async void RunAutoLoad(string lastPath) {
            try {
                AvatarLoadResult result = await avatarSession.LoadFromPathAsync(lastPath, true);
                if (!result.IsSuccess) {
                    logService.Log(LogLevel.Warning, "Auto-load failed (" + result.Status + "): " + result.Message);
                }
            } catch (Exception exception) {
                logService.LogException(exception, "Auto-load avatar threw");
            }
        }
    }
}
