using System;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using VirtualMirror.Avatar.Vrm;
using VirtualMirror.Camera;
using VirtualMirror.Core;
using VirtualMirror.IK;
using VirtualMirror.IO;
using VirtualMirror.Rendering;
using VirtualMirror.Retargeting;
using VirtualMirror.Settings;
using VirtualMirror.Tracking;
using VirtualMirror.Tracking.Filtering;
using VirtualMirror.Tracking.MediaPipe;
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
        [SerializeField] private float filterBeta = 0.02f;
        [SerializeField] private float filterDerivativeCutoff = 1f;
        [SerializeField] private bool useMediaPipeTracking = true;
        [SerializeField] private string poseModelFileName = "pose_landmarker_full.bytes";
        [SerializeField] private bool poseFlipX = true;
        [SerializeField] private bool poseFlipY = true;
        [SerializeField] private bool poseFlipZ = true;
        [SerializeField] private float retargetMinConfidence = 0.5f;
        [SerializeField] private bool useIkDriver = true;

        private ServiceRegistry services;
        private LogService logService;
        private SettingsStore settingsStore;
        private AvatarSessionController avatarSession;
        private ICameraCapture cameraCapture;
        private IBodyTrackingProvider bodyProvider;
        private JointFilterPipeline jointFilter;
        private HumanoidPoseRetargeter retargeter;
        private IIkSolver ikSolver;
        private Animator boundAnimator;
        private MirrorCameraController cameraController;

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
            if (loadMirrorSceneOnStart) {
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

        private void UpdateTracking() {
            if (bodyProvider == null || retargeter == null || jointFilter == null) {
                return;
            }
            float deltaSeconds = Time.deltaTime;
            bodyProvider.Tick(deltaSeconds);

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
            if (!bodyProvider.TryGetLatestFrame(out frame)) {
                return;
            }
            PoseFrame filtered = jointFilter.Filter(frame, deltaSeconds);
            retargeter.Apply(filtered, retargetMinConfidence);
            if (ikSolver != null && ikSolver.IsBound) {
                ikSolver.Apply(filtered, retargetMinConfidence);
            }
        }

        private void OnApplicationQuit() {
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
            if (useMediaPipeTracking) {
                string modelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", poseModelFileName);
                PoseSpaceConverter converter = new PoseSpaceConverter(poseFlipX, poseFlipY, poseFlipZ);
                bodyProvider = new MediaPipePoseProvider(logService, cameraCapture, modelPath, converter);
            } else {
                bodyProvider = new FakeBodyTrackingProvider();
            }
            bodyProvider.StartTracking();
            if (useMediaPipeTracking && !bodyProvider.IsRunning) {
                logService.Log(LogLevel.Warning, "MediaPipe provider failed to start; falling back to fake tracking.");
                bodyProvider.Dispose();
                bodyProvider = new FakeBodyTrackingProvider();
                bodyProvider.StartTracking();
            }
            logService.Log(LogLevel.Info, "Body tracking started.");
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
            InitializeAvatarUi(scene);
            InitializeCameraPreview(scene);
            cameraController = FindCameraController(scene);
            avatarSession.AvatarChanged += FrameCameraOnAvatar;
            TryAutoLoadLastAvatar();
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
            if (cameraController == null || avatarSession == null || avatarSession.Current == null) {
                return;
            }
            GameObject root = avatarSession.Current.Root;
            if (root != null) {
                cameraController.Frame(root.transform);
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
