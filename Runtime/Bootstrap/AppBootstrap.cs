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
        // ---- Scene & services plumbing (set once; hidden from the Inspector to keep it focused) ----
        [HideInInspector] [SerializeField] private string mirrorSceneName = "Mirror";
        [HideInInspector] [SerializeField] private string avatarRootName = "AvatarRoot";
        [HideInInspector] [SerializeField] private bool loadMirrorSceneOnStart = true;
        [HideInInspector] [SerializeField] private float settingsSaveDebounceSeconds = 0.4f;
        [HideInInspector] [SerializeField] private bool mirrorLogToConsole = true;
        [HideInInspector] [SerializeField] private long maxAvatarFileBytes = 268435456;

        [Header("Tracking Source")]
        [Tooltip("OAK-D depth camera via the Python sidecar over UDP — the production path. Takes priority over the other providers when it starts.")]
        [SerializeField] private bool useOakUdpTracking = false; // B2 (Python sidecar over UDP) — ADR-016
        [SerializeField] private int oakUdpPort = 8899;
        [Tooltip("Use the sample video file instead of a live webcam (ignored on the OAK-D path).")]
        [SerializeField] private bool useVideoSource = false;
        [Tooltip("MediaPipe pose on the webcam/video (CPU). Fallback when OAK/Sentis are off.")]
        [SerializeField] private bool useMediaPipeTracking = true;

        [Header("Kalidokit Body — active whole-body retarget (ADR-022/023)")]
        [Tooltip("Drive the whole skeleton via the VRM normalized control rig. MUST be set BEFORE Play (the control rig is generated at load).")]
        [SerializeField] private bool useKalidokitBody = false;
        [Tooltip("Global three.js->Unity handedness conversion (0..3). 2 is correct for this rig.")]
        [SerializeField] private int kalidokitBodyFlipQuat = 0;
        [SerializeField] private Vector3 kalidokitBodyEulerSigns = Vector3.one;
        [Tooltip("Smoothing toward the solved pose (0..1).")]
        [SerializeField] private float kalidokitBodyLerp = 0.5f;
        [Tooltip("Reflect the input skeleton (negate X + swap L/R). Leave OFF — reflecting the input twists a rotation retarget (ADR-023); a true mirror must be done output-side.")]
        [SerializeField] private bool kalidokitBodyMirror = false;
        [Tooltip("Torso side-lean amount (0 = upright; 1 = full side-lean tracking).")]
        [SerializeField] private float kalidokitBodyTorsoRoll = 0f;
        [Tooltip("Waist forward-bend from OAK depth (0 = off/upright, 1 = full; raise for a more pronounced bend). Flip the sign if it bends the wrong way. Press C standing upright to re-seed. Live-tunable.")]
        [SerializeField] private float kalidokitSpineBendScale = 1f;
        [Tooltip("Waist-bend baseline time-constant (s). Larger holds a sustained bend longer but corrects the distance/systematic lean slower; very large ~= a fixed neutral (ADR-026). Live-tunable.")]
        [SerializeField] private float kalidokitSpineBendBaselineTau = 8f;
        [Tooltip("Body-turn amount (ADR-027): 0 = frontal-lock (default — a mirror is frontal, and turning is unreliable past ~2 m where depth degrades → false 'facing away'). Raise toward 1 ONLY when standing close (<~1.8 m) if you want body-turn. Live-tunable.")]
        [Range(0f, 1f)]
        [SerializeField] private float kalidokitTorsoYawScale = 0f;
        [SerializeField] private bool kalidokitBodyLegs = true;
        [Tooltip("Normalized-bone flexion axis for the curl-driven fingers.")]
        [SerializeField] private Vector3 kalidokitFingerCurlAxis = new Vector3(0f, 0f, -1f);
        [Tooltip("Finger curl weight (0 disables fingers).")]
        [SerializeField] private float kalidokitFingerWeight = 1f;
        [Tooltip("P0-1: a limb (arm/leg) accepts a fresh Kalidokit solve only when its weakest driving joint's " +
                 "confidence is at least this; below it, the limb HOLDS its last valid rotation instead of " +
                 "collapsing toward the origin on an invalid/occluded joint. 0 disables the gate.")]
        [SerializeField] private float limbConfidenceThreshold = 0.3f;

        [Header("Pose Mapping")]
        [SerializeField] private bool poseFlipX = true;
        [SerializeField] private bool poseFlipY = true;
        [SerializeField] private bool poseFlipZ = true;

        [Header("Features")]
        [SerializeField] private bool useFaceTracking = true;
        [SerializeField] private bool useHandTracking = true;
        [Tooltip("Wrist rotation weight (0 disables wrist rotation; fingers still curl). Live-tunable.")]
        [SerializeField] private float wristRotationWeight = 0.7f;
        [Tooltip("Track legs (off keeps legs straight when the lower body is occluded / out of frame).")]
        [SerializeField] private bool trackLegs = false;
        [Tooltip("Translate the avatar from the OAK-D measured hip position (walk/jump with the user).")]
        [SerializeField] private bool trackPosition = true;
        [SerializeField] private float positionScale = 1f;

        [Header("Debug")]
        [Tooltip("Draw a live line-skeleton of the raw tracked pose beside the avatar to validate tracking by eye.")]
        [SerializeField] private bool showDebugSkeleton = false;
        [Tooltip("Uniform size of the debug skeleton.")]
        [SerializeField] private float debugSkeletonScale = 1.5f;
        [Tooltip("Offset of the debug skeleton from the avatar root.")]
        [SerializeField] private Vector3 debugSkeletonOffset = new Vector3(1.4f, 1.0f, 0f);
        [Tooltip("Pipeline logging: write recv_log.jsonl (received) + model_log.jsonl (applied) to compare against the sidecar's sender_log.jsonl. Run the sidecar with --log-dir <the same dir>, then diff with compare_logs.py.")]
        [SerializeField] private bool pipelineLogging = false;
        [SerializeField] private string pipelineLogDir = "D:/Unity/viitorx-vrm-avtar-unity-base-project/Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~/pipeline_logs";

        // ---- Capture / filter / retarget internals (tuned at runtime via the calibration panel; hidden) ----
        [HideInInspector] [SerializeField] private string poseModelFileName = "pose_landmarker_heavy.bytes";
        [HideInInspector] [SerializeField] private string sampleVideoPath = "C:/Unity/viitorx-vrm-avtar-unity-base-project/sampleVRMFiles/sample video.mp4";
        [HideInInspector] [SerializeField] private int cameraWidth = 1280;
        [HideInInspector] [SerializeField] private int cameraHeight = 720;
        [HideInInspector] [SerializeField] private int cameraFps = 30;
        [HideInInspector] [SerializeField] private float filterMinCutoff = 1f;
        [HideInInspector] [SerializeField] private float filterBeta = 0.2f;
        [HideInInspector] [SerializeField] private float filterDerivativeCutoff = 1f;
        [HideInInspector] [SerializeField] private float retargetMinConfidence = 0.5f;
        [HideInInspector] [SerializeField] private float retargetExitConfidence = 0.3f;
        [HideInInspector] [SerializeField] private float poseStaleSeconds = 0.5f;
        [HideInInspector] [SerializeField] private float positionSmoothing = 12f;
        [HideInInspector] [SerializeField] private bool useMediaPipeFace = true;
        [HideInInspector] [SerializeField] private string faceModelFileName = "face_landmarker.bytes";
        [HideInInspector] [SerializeField] private bool useMediaPipeHand = true;
        [HideInInspector] [SerializeField] private string handModelFileName = "hand_landmarker.bytes";

        // ---- Legacy FK/IK path (fully bypassed while Kalidokit Body is on) ----
        [HideInInspector] [SerializeField] private bool useIkDriver = true;

        // ---- Sentis RTMW3D GPU path (optional alternate provider; off by default) ----
        [HideInInspector] [SerializeField] private bool useSentis3dTracking = false;
        [HideInInspector] [SerializeField] private Unity.InferenceEngine.ModelAsset sentisModel;
        [HideInInspector] [SerializeField] private bool sentisImageNetNorm = true;
        [HideInInspector] [SerializeField] private float sentisMetreScale = 1.7f;
        [HideInInspector] [SerializeField] private float sentisDepthScale = 0.8f;
        [HideInInspector] [SerializeField] private bool sentisPersonCrop = true;
        [HideInInspector] [SerializeField] private float sentisFilterBeta = 0.6f;
        [HideInInspector] [SerializeField] private float sentisFilterMinCutoff = 1.5f;

        private ServiceRegistry services;
        private LogService logService;
        private SettingsStore settingsStore;
        private AvatarSessionController avatarSession;
        private ICameraCapture cameraCapture;
        private IBodyTrackingProvider bodyProvider;
        private PoseSpaceConverter converter;
        private JointFilterPipeline jointFilter;
        private HumanoidPoseRetargeter retargeter;
        private KalidokitControlRigDriver kalidokitControlRig;
        private PoseDebugSkeleton debugSkeleton;
        private System.IO.StreamWriter modelLog;
        private bool modelLogFailed;
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
        private double lastFilterTimestamp; // M10: timestamp of the last frame actually pushed through jointFilter
        private PoseFrame lastFilteredFrame; // M10: reused on repeat Updates so the filter runs once per NEW frame
        private Transform avatarRootTransform;
        private Vector3 avatarRootInitialPosition;
        private bool hasPositionNeutral;
        private Vector3 positionNeutralHip;
        private Vector3 positionCurrentOffset;

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
            if (performanceMonitor != null) {
                performanceMonitor.Tick(Time.unscaledDeltaTime); // H1: drive the FPS/frame-time HUD
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
                hasPositionNeutral = false;
                if (handRetargeter != null) {
                    handRetargeter.Recalibrate();
                }
                if (kalidokitControlRig != null) {
                    kalidokitControlRig.Recalibrate(); // re-capture the wrist neutral palm
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
                retargeter.SetTrackLegs(trackLegs);
                if (kalidokitControlRig != null) {
                    // ADR-022 whole-body: bind the normalized control rig (null unless loaded with it).
                    UniVRM10.Vrm10Instance vrmForRig = avatarSession.Current.Root != null ? avatarSession.Current.Root.GetComponent<UniVRM10.Vrm10Instance>() : null;
                    kalidokitControlRig.Bind(vrmForRig);
                }
                // H3: the avatar was hot-swapped — unbind the face + hand retargeters too so the guarded
                // `!IsBound` rebinds below re-target the NEW avatar's rig instead of the disposed one.
                if (handRetargeter != null) {
                    handRetargeter.Unbind();
                }
                if (expressionRetargeter != null) {
                    expressionRetargeter.Unbind();
                }
                Transform avatarRoot = avatarSession.Current.Root != null ? avatarSession.Current.Root.transform : null;
                avatarRootTransform = avatarRoot;
                if (avatarRootTransform != null) {
                    avatarRootInitialPosition = avatarRootTransform.position;
                }
                hasPositionNeutral = false;
                positionCurrentOffset = Vector3.zero;
                if (ikSolver != null) {
                    ikSolver.Bind(animator, avatarRoot);
                    ikSolver.SetLegTracking(trackLegs);
                }
                boundAnimator = animator;
            }
            // ADR-022 whole-body: when on + the control rig is present, it owns the whole skeleton (FK/IK +
            // hand-curl bypassed). Requires the avatar to have loaded WITH the control rig (useKalidokitBody
            // set before Play).
            bool kalidokitBodyActive = useKalidokitBody && kalidokitControlRig != null && kalidokitControlRig.IsBound;
            PoseFrame frame;
            if (bodyProvider.TryGetLatestFrame(out frame)) {
                // M3/M18: the OAK sidecar already smooths (One-Euro + outlier gate) at the source, so
                // running Unity's One-Euro again would double-filter and add lag. Smoothing is single-owned
                // by the sidecar for the OAK path; pass the frame through unfiltered here.
                // M10: for the MediaPipe/Sentis paths TryGetLatestFrame returns the SAME cached frame on
                // every render tick, so filtering each Update (~60 Hz) over ~30 Hz data mistuned the
                // One-Euro derivative (dt = render delta, not the inter-sample delta). Filter ONLY on a
                // genuinely-new frame (timestamp changed), using the timestamp delta, and reuse the last
                // filtered buffer on repeats.
                PoseFrame filtered;
                if (bodyProvider is OakDUdpPoseProvider) {
                    filtered = frame;
                } else {
                    double frameTimestamp = frame.TimestampSeconds;
                    if (lastFilteredFrame == null || frameTimestamp != lastFilterTimestamp) {
                        float sampleDelta = deltaSeconds;
                        if (lastFilteredFrame != null && frameTimestamp > lastFilterTimestamp) {
                            sampleDelta = (float)(frameTimestamp - lastFilterTimestamp);
                        }
                        lastFilteredFrame = jointFilter.Filter(frame, sampleDelta);
                        lastFilterTimestamp = frameTimestamp;
                    }
                    filtered = lastFilteredFrame;
                }
                RenderDebugSkeleton(filtered);
                if (kalidokitBodyActive) {
                    // ADR-022 whole-body: the normalized control rig owns the entire skeleton, so release
                    // FK + IK (they must not write raw bones the control rig would overwrite each Process).
                    retargeter.SetArmsLegsDrivenExternally(true);
                    retargeter.SetArmsExternallyDriven(true);
                    if (ikSolver != null && ikSolver.IsBound) {
                        ikSolver.SetActive(false);
                    }
                    if (!IsPoseStale(filtered)) {
                        kalidokitControlRig.SetTuning(kalidokitBodyEulerSigns, kalidokitBodyFlipQuat, kalidokitBodyLerp, kalidokitBodyLegs, kalidokitBodyMirror, kalidokitBodyTorsoRoll);
                        kalidokitControlRig.SetSpineBend(kalidokitSpineBendScale);
                        kalidokitControlRig.SetSpineBendDynamics(kalidokitSpineBendBaselineTau);
                        kalidokitControlRig.SetTorsoYawScale(kalidokitTorsoYawScale);
                        kalidokitControlRig.SetLimbConfidence(limbConfidenceThreshold); // P0-1 live-tunable gate
                        kalidokitControlRig.Apply(filtered);
                    }
                } else {
                    // FK and IK are mutually exclusive on the arm/leg bones: when the IK solver is active and
                    // bound it owns the limbs, so FK drives only torso (hips/spine) + neck. Otherwise FK drives
                    // everything and the solver is released so the rig stops deforming the limbs.
                    bool ikActive = useIkDriver && ikSolver != null && ikSolver.IsBound;
                    retargeter.SetArmsLegsDrivenExternally(ikActive);
                    // FK owns the arms on this legacy path (the Kalidokit whole-body path drives arms via the
                    // control rig instead — see kalidokitBodyActive above).
                    retargeter.SetArmsExternallyDriven(false);
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
                // Physical movement (walk/jump): drive the avatar ROOT from the OAK measured hip xyz on BOTH
                // retarget paths. This used to live only in the FK else-branch, so the Kalidokit body path
                // never translated (Milestone-2 fix). Runs whenever a frame carries a measured root position.
                ApplyRootPosition(frame, deltaSeconds);
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

            if (!kalidokitBodyActive && useHandTracking && handProvider != null && handRetargeter != null) {
                handProvider.Tick(deltaSeconds);
                // ADR-021: push the wrist weight every frame so the serialized wristRotationWeight is
                // live-tunable during Play (0 disables wrist rotation, fingers still curl) — the user can
                // dial it in without a recompile instead of stop→edit→Play cycles.
                handRetargeter.SetWristWeight(wristRotationWeight);
                if (!handRetargeter.IsBound) {
                    handRetargeter.Bind(animator);
                }
                HandFrame handFrame;
                if (handProvider.TryGetFrame(out handFrame)) {
                    handRetargeter.Apply(handFrame);
                }
            } else if (!kalidokitBodyActive && handRetargeter != null && handRetargeter.IsBound) {
                // LOW-A: hand tracking toggled off — relax fingers to the open rest pose so they don't
                // freeze mid-curl (mirrors the face-tracking reset above).
                handRetargeter.ResetToOpen();
            }

            // ADR-022 fingers + ADR-023 wrist: on the Kalidokit body path, drive the control-rig fingers from
            // the hand provider's curls (the hand-curl retargeter above is bypassed because the control rig
            // owns the skeleton). Fingers run BEFORE ProcessRuntime; the WRIST runs AFTER (below), on the raw
            // hand bone. Keep the frame reference so the wrist can reuse it post-Process.
            HandFrame kaliHandFrame = null;
            bool haveKaliHandFrame = false;
            if (kalidokitBodyActive && useHandTracking && handProvider != null && kalidokitControlRig != null) {
                handProvider.Tick(deltaSeconds);
                kalidokitControlRig.SetFingerTuning(kalidokitFingerCurlAxis, kalidokitFingerWeight);
                kalidokitControlRig.SetWristWeight(wristRotationWeight); // live-tunable; 0 disables the wrist
                if (handProvider.TryGetFrame(out kaliHandFrame)) {
                    haveKaliHandFrame = true;
                    kalidokitControlRig.ApplyFingers(kaliHandFrame);
                }
            }

            // ADR-022 whole-body: the VRM is set to manual update while the control rig owns it, so run its
            // runtime ONCE here — AFTER the body pose + face expressions + fingers were written this frame —
            // to apply everything (control rig → skeleton, springbones, expressions). Without this the
            // avatar stays in T-pose (the reported bug).
            if (kalidokitBodyActive && kalidokitControlRig != null) {
                kalidokitControlRig.ProcessRuntime();
                // ADR-023: the WRIST bend is applied to the RAW hand bone AFTER Process (Process would
                // overwrite a control-rig write). Roll-free swing from the OAK hand stream's palm forward.
                if (haveKaliHandFrame) {
                    kalidokitControlRig.ApplyWrist(kaliHandFrame);
                }
                if (pipelineLogging) {
                    WriteModelLog();
                }
            }
        }

        // Live line-rendered debug skeleton of the RAW tracked pose, drawn beside the avatar so tracking can
        // be validated by eye against the retargeted VRM (the user's request). Lazily created; toggled live by
        // showDebugSkeleton. Purely diagnostic — no effect on the avatar.
        private void RenderDebugSkeleton(PoseFrame frame) {
            if (!showDebugSkeleton) {
                if (debugSkeleton != null && debugSkeleton.gameObject.activeSelf) {
                    debugSkeleton.gameObject.SetActive(false);
                }
                return;
            }
            if (debugSkeleton == null) {
                GameObject skeletonObject = new GameObject("PoseDebugSkeleton");
                debugSkeleton = skeletonObject.AddComponent<PoseDebugSkeleton>();
                Vector3 basePosition = avatarRootTransform != null ? avatarRootInitialPosition : Vector3.zero;
                skeletonObject.transform.position = basePosition + debugSkeletonOffset;
                skeletonObject.transform.localScale = Vector3.one * debugSkeletonScale;
            }
            if (!debugSkeleton.gameObject.activeSelf) {
                debugSkeleton.gameObject.SetActive(true);
            }
            debugSkeleton.Render(frame);
        }

        // Physical movement: translate the avatar root by the OAK-measured mid-hip position (neutral-relative,
        // scaled, exponentially smoothed) so the avatar walks/jumps with the user. Shared by both retarget
        // paths (Milestone-2). No-op when the frame carries no measured root (RGB paths) or trackPosition off.
        private void ApplyRootPosition(PoseFrame frame, float deltaSeconds) {
            if (!trackPosition || avatarRootTransform == null || frame == null || !frame.HasRootPosition) {
                return;
            }
            if (!hasPositionNeutral) {
                positionNeutralHip = frame.RootPositionMetres;
                hasPositionNeutral = true;
            }
            Vector3 targetOffset = (frame.RootPositionMetres - positionNeutralHip) * positionScale;
            float lerpT = 1f - Mathf.Exp(-positionSmoothing * deltaSeconds);
            positionCurrentOffset = Vector3.Lerp(positionCurrentOffset, targetOffset, lerpT);
            avatarRootTransform.position = avatarRootInitialPosition + positionCurrentOffset;
        }

        // Pipeline logging (diagnostics): one model_log.jsonl line per applied Kalidokit-body frame — seq +
        // the resulting avatar bone orientations (hips facing, hands, forearms) — to diff against
        // recv_log.jsonl and localize where the retarget introduces jitter/spin (compare_logs.py).
        private void WriteModelLog() {
            if (boundAnimator == null || modelLogFailed) {
                return;
            }
            try {
                if (modelLog == null) {
                    System.IO.Directory.CreateDirectory(pipelineLogDir);
                    modelLog = new System.IO.StreamWriter(System.IO.Path.Combine(pipelineLogDir, "model_log.jsonl"), false);
                    modelLog.AutoFlush = true;
                }
                long seq = bodyProvider is OakDUdpPoseProvider ? ((OakDUdpPoseProvider)bodyProvider).LastSeq : -1;
                Transform hips = boundAnimator.GetBoneTransform(HumanBodyBones.Hips);
                Transform lh = boundAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rh = boundAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform ll = boundAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                Transform rl = boundAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                string line = "{\"seq\":" + seq
                    + ",\"hipsY\":" + LogF(hips != null ? hips.eulerAngles.y : 0f)
                    + ",\"hipsFwd\":" + LogV(hips != null ? hips.forward : Vector3.zero)
                    + ",\"lhand\":" + LogV(lh != null ? lh.eulerAngles : Vector3.zero)
                    + ",\"rhand\":" + LogV(rh != null ? rh.eulerAngles : Vector3.zero)
                    + ",\"llow\":" + LogV(ll != null ? ll.eulerAngles : Vector3.zero)
                    + ",\"rlow\":" + LogV(rl != null ? rl.eulerAngles : Vector3.zero)
                    + ",\"lhandF\":" + LogV(lh != null ? lh.forward : Vector3.zero)
                    + ",\"rhandF\":" + LogV(rh != null ? rh.forward : Vector3.zero)
                    + ",\"llowF\":" + LogV(ll != null ? ll.forward : Vector3.zero)
                    + ",\"rlowF\":" + LogV(rl != null ? rl.forward : Vector3.zero)
                    + "}";
                modelLog.WriteLine(line);
            } catch (Exception) {
                modelLogFailed = true;
                if (modelLog != null) {
                    try {
                        modelLog.Dispose();
                    } catch (Exception) {
                    }
                    modelLog = null;
                }
            }
        }

        private static string LogF(float v) {
            return v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string LogV(Vector3 v) {
            return "[" + LogF(v.x) + "," + LogF(v.y) + "," + LogF(v.z) + "]";
        }

        private bool servicesTornDown;

        private void OnApplicationQuit() {
            TeardownServices();
        }

        // M5: also tear down on OnDestroy so an Editor domain reload / script recompile while playing
        // releases the OAK-D UDP socket + receive thread. Without it the socket leaks and the next Play
        // hits "port 8899 in use" and silently falls back off the OAK path. Idempotent (servicesTornDown).
        private void OnDestroy() {
            TeardownServices();
        }

        private void TeardownServices() {
            if (servicesTornDown) {
                return;
            }
            servicesTornDown = true;
            if (modelLog != null) {
                try {
                    modelLog.Flush();
                    modelLog.Dispose();
                } catch (Exception) {
                }
                modelLog = null;
            }
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
            // ADR-022: the whole-body Kalidokit path needs the VRM normalized control rig, which is a
            // load-time option — set it before the avatar auto-loads. Off → raw bones for the FK/IK path.
            avatarLoader.GenerateControlRig = useKalidokitBody;
            services = new ServiceRegistry(pathProvider, logService, settingsStore, avatarLoader);
            performanceMonitor = new PerformanceMonitor(); // H1: was never instantiated → HUD FPS stayed blank
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
            if (useOakUdpTracking) {
                // LOW-A: the OAK-D path streams its own frames from the Python sidecar over UDP; the local
                // webcam (often a virtual cam that fails to open in this setup) is not needed and only logs
                // a scary "camera failed" warning. Keep the service object (so any fallback still has one)
                // but do not open a device.
                logService.Log(LogLevel.Info, "OAK-D path active; skipping local camera open.");
                return;
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
            kalidokitControlRig = new KalidokitControlRigDriver(); // ADR-022: whole-body via normalized control rig
            kalidokitControlRig.SetLogger(logService); // P0-1: sparse limb hold/reacquire diagnostics
            if (useIkDriver) {
                EnsureIkSolver();
            }
            converter = new PoseSpaceConverter(poseFlipX, poseFlipY, poseFlipZ);
            // OAK-D via the Python sidecar over UDP (B2, ADR-016) takes top priority. No native DLL in-process
            // → cannot crash Unity. Binds a UDP socket; if the port is free it "starts" and waits for the
            // sidecar's datagrams (avatar rests until the sidecar streams). Run udp_pose_sender.py separately.
            if (useOakUdpTracking) {
                OakDUdpPoseProvider oakUdp = new OakDUdpPoseProvider(logService, converter, oakUdpPort);
                if (pipelineLogging) {
                    oakUdp.SetPipelineLog(pipelineLogDir); // recv_log.jsonl — must be set before StartTracking
                }
                oakUdp.StartTracking();
                if (oakUdp.IsRunning) {
                    bodyProvider = oakUdp;
                } else {
                    logService.Log(LogLevel.Warning, "OAK-D UDP provider failed to start; falling back.");
                    oakUdp.Dispose();
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
                // or fast motion gets damped into the "under-responsive" look. LOW-A: apply the Sentis
                // profile to the LIVE filter WITHOUT overwriting the serialized filterMinCutoff/Beta fields
                // — those stay the single source for the calibration sliders and the other providers.
                jointFilter.SetParameters(sentisFilterMinCutoff, sentisFilterBeta);
            }
            logService.Log(LogLevel.Info, "Body tracking started.");

            if (useFaceTracking) {
                EnsureFaceProvider();
            }
            if (useHandTracking) {
                EnsureHandProvider();
            }
        }

        // H5: the IK solver used to be constructed only at startup and only when useIkDriver was already
        // true; the scene ships it off, so ticking "IK Arm/Leg Driver" at runtime just flipped a bool and
        // nothing ever bound → the whole IK driver was dead as shipped. Build on demand and bind
        // immediately if an avatar is already loaded (UpdateTracking's animator-changed block only binds a
        // solver that already exists). Idempotent.
        private void EnsureIkSolver() {
            if (ikSolver == null) {
                ikSolver = new AnimationRiggingIkDriver();
            }
            if (!ikSolver.IsBound && boundAnimator != null) {
                Transform avatarRoot = null;
                if (avatarSession != null && avatarSession.Current != null && avatarSession.Current.Root != null) {
                    avatarRoot = avatarSession.Current.Root.transform;
                }
                ikSolver.Bind(boundAnimator, avatarRoot);
                ikSolver.SetLegTracking(trackLegs);
            }
        }

        // M1: extracted from StartTracking so the face toggle can lazily construct the provider when it is
        // enabled at runtime. The old code built providers once behind the start-time bool, so toggling a
        // feature on later did nothing (the provider stayed null). Idempotent.
        private void EnsureFaceProvider() {
            if (faceProvider != null) {
                return;
            }
            if (bodyProvider is OakDUdpPoseProvider) {
                // LOW-A: the OAK-D setup feeds no RGB webcam into Unity (the OAK color sensor stays on the
                // sidecar) and the OAK stream carries no face data, so a MediaPipe face provider here would
                // only poll a dead capture. Skip it; face tracking is unavailable on the OAK path.
                logService.Log(LogLevel.Info, "Face tracking unavailable on the OAK-D path (no RGB webcam); skipping face provider.");
                return;
            }
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
            if (expressionRetargeter == null) {
                expressionRetargeter = new VrmExpressionRetargeter();
            }
            logService.Log(LogLevel.Info, "Face tracking started.");
        }

        // M1: same lazy-construct treatment for the hand toggle. Idempotent.
        private void EnsureHandProvider() {
            if (handProvider != null) {
                return;
            }
            if (bodyProvider is OakDUdpPoseProvider) {
                // The OAK-D sidecar streams fingers (lh/rh) alongside the body in one datagram, so drive
                // the hands from that same stream via a facade over the pose provider's socket — no
                // separate MediaPipe RGB webcam needed (which is unavailable in the OAK setup anyway).
                OakDUdpPoseProvider oakBody = (OakDUdpPoseProvider)bodyProvider;
                handProvider = new OakDUdpHandProvider(oakBody);
                handProvider.StartTracking();
                logService.Log(LogLevel.Info, "Hand tracking source: OAK-D UDP (fingers from the whole-body stream).");
            } else if (useMediaPipeHand) {
                string handModelPath = Path.Combine(Application.streamingAssetsPath, "MediaPipe", handModelFileName);
                // LOW-A: reuse the SINGLE shared converter so a live mirror-flip (SetFlipX) applies to the
                // hands too. The old code built a SECOND converter that never received runtime flip changes,
                // so a live mirror toggle mirrored the body but not the fingers.
                MediaPipeHandProvider mediaPipeHand = new MediaPipeHandProvider(logService, cameraCapture, handModelPath, converter);
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
            if (handRetargeter == null) {
                handRetargeter = new HumanoidHandRetargeter();
                handRetargeter.SetWristWeight(wristRotationWeight);
            }
            logService.Log(LogLevel.Info, "Hand tracking started.");
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
            // M4: an additive Mirror-scene reload re-runs this. Dispose the previous session (and unsubscribe
            // its event) first, or the old session + its loaded avatar GameObject leak and AvatarChanged
            // handlers accumulate on orphaned sessions.
            if (avatarSession != null) {
                avatarSession.AvatarChanged -= FrameCameraOnAvatar;
                avatarSession.Dispose();
                avatarSession = null;
                boundAnimator = null;
            }
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
                    // LOW-A: interpolate the real serialized camera fields instead of a hardcoded string
                    // (the scene may run 640x400 for OAK / any webcam resolution, not always 1280x720@30).
                    diagnosticsHud.SetCameraInfo(useVideoSource
                        ? "Sample Video MP4"
                        : ("Webcam " + cameraWidth + "x" + cameraHeight + "@" + cameraFps + "fps"));
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
                    // LOW-A: only show the webcam picker when a webcam is actually the source.
                    calibrationPanel.SetCameraDropdownVisible(!useVideoSource && !useOakUdpTracking);
                    calibrationPanel.OnCameraDeviceChanged += HandleCameraDeviceChanged;
                    calibrationPanel.OnMirrorFlipToggled += (value) => {
                        poseFlipX = value;
                        if (converter != null) {
                            converter.SetFlipX(value);
                        }
                    };
                    calibrationPanel.OnIkToggled += (value) => {
                        useIkDriver = value;
                        if (value) {
                            EnsureIkSolver(); // H5: build + bind the solver on enable, not just flip the bool
                        }
                    };
                    calibrationPanel.OnFaceToggled += (value) => {
                        useFaceTracking = value;
                        if (value) {
                            EnsureFaceProvider(); // M1: construct the provider on enable
                        }
                    };
                    calibrationPanel.OnHandToggled += (value) => {
                        useHandTracking = value;
                        if (value) {
                            EnsureHandProvider(); // M1: construct the provider on enable
                        }
                    };
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
