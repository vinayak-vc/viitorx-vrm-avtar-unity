using System;
using System.Runtime.InteropServices;
using System.Threading;

using UnityEngine;

using Newtonsoft.Json.Linq;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// <see cref="IBodyTrackingProvider"/> backed by a Luxonis OAK-D depth camera running the MoveNet
    /// single-pose model ON-DEVICE (Myriad X VPU) and fusing the stereo depth, via the luxonis
    /// depthai-unity native plugin (ADR-016, Option B). Gives 17 COCO body keypoints with REAL metric 3D
    /// (measured stereo depth), which removes the monocular front/back ambiguity ("hands behind") and the
    /// depth-scale guesswork of the RGB paths.
    ///
    /// This calls the native <c>depthai-unity.dll</c> directly via P/Invoke — it does NOT use the plugin's
    /// C# classes (those compile into Assembly-CSharp-firstpass, which this asmdef cannot reference). The
    /// <see cref="PipelineConfig"/>/<see cref="FrameInfo"/> structs are copied VERBATIM from the plugin
    /// source (OAKForUnity.PredefinedBase) so the native ABI matches exactly — do not reorder fields.
    ///
    /// First cut: <see cref="BodyPoseResults"/> is called on the main thread each <see cref="Tick"/>; the
    /// device does the inference so it returns quickly, but if it stalls the main thread it can be moved to
    /// a background worker like <c>MediaPipePoseProvider</c>. Result is a JSON string parsed with Newtonsoft.
    /// </summary>
    public sealed class OakDPoseProvider : IBodyTrackingProvider {
        private const string NativeLibrary = "depthai-unity";

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool InitBodyPose(in PipelineConfig config);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr BodyPoseResults(out FrameInfo frameInfo, bool getPreview, int width, int height, bool useDepth, bool drawBodyPoseInPreview, float bodyLandmarkScoreThreshold, bool retrieveInformation, bool useIMU, bool useSpatialLocator, int deviceNum);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DAICloseDevice(int deviceNum);

        // --- ABI-exact structs copied verbatim from OAKForUnity.PredefinedBase (do NOT reorder/retype) ---
        [StructLayout(LayoutKind.Sequential)]
        private struct FrameInfo {
            public int monoRWidth, monoRHeight;
            public int monoLWidth, monoLHeight;
            public int colorWidth, colorHeight;
            public int colorPreviewWidth, colorPreviewHeight;
            public int diparityWidth, disparityHeight;
            public int depthWidth, depthHeight;
            public int rectifiedRWidth, rectifiedRHeight;
            public int rectifiedLWidth, rectifiedLHeight;
            public IntPtr monoRData;
            public IntPtr monoLData;
            public IntPtr colorData;
            public IntPtr colorPreviewData;
            public IntPtr disparityData;
            public IntPtr depthData;
            public IntPtr rectifiedRData;
            public IntPtr rectifiedLData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PipelineConfig {
            public int deviceNum;
            public string deviceId;
            public float colorCameraFPS;
            public int colorCameraResolution;
            [MarshalAs(UnmanagedType.I1)] public bool colorCameraInterleaved;
            public int colorCameraColorOrder;
            public int previewSizeWidth, previewSizeHeight;
            public int ispScaleF1, ispScaleF2;
            public int manualFocus;
            public float monoRCameraFPS;
            public int monoRCameraResolution;
            public float monoLCameraFPS;
            public int monoLCameraResolution;
            public int confidenceThreshold;
            [MarshalAs(UnmanagedType.I1)] public bool leftRightCheck;
            [MarshalAs(UnmanagedType.I1)] public bool subpixel;
            [MarshalAs(UnmanagedType.I1)] public bool extendedDisparity;
            public int depthAlign;
            public int medianFilter;
            public string nnPath1;
            public string nnPath2;
            public string nnPath3;
            public float rate;
            public int freq;
            public int batchReportThreshold;
            public int maxBatchReports;
            public int previewMode;
            public bool useSpatialLocator;
        }
        // --- end verbatim structs ---

        // MoveNet / COCO-17 keypoint index -> our JointId. Same order as COCO body-17.
        private static readonly JointId[] MoveNetToJoint = new JointId[] {
            JointId.Nose, JointId.LeftEye, JointId.RightEye, JointId.LeftEar, JointId.RightEar,
            JointId.LeftShoulder, JointId.RightShoulder, JointId.LeftElbow, JointId.RightElbow,
            JointId.LeftWrist, JointId.RightWrist, JointId.LeftHip, JointId.RightHip,
            JointId.LeftKnee, JointId.RightKnee, JointId.LeftAnkle, JointId.RightAnkle
        };

        private readonly ILogService logService;
        private readonly PoseSpaceConverter converter;
        private readonly string modelPath;
        private readonly int deviceNum;
        private readonly float landmarkScoreThreshold;

        // BodyPoseResults is a BLOCKING native call → it runs on a background worker thread so it never
        // stalls the main thread (a synchronous call on the main thread froze Unity). Double-buffered like
        // MediaPipePoseProvider: worker fills workerFrame, main swaps to mainFrame under the lock.
        private readonly object gate = new object();
        private Thread workerThread;
        private volatile bool running;
        private PoseFrame workerFrame;
        private PoseFrame mainFrame;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        private long frameCounter;
        private int rawDebugRemaining = 5; // TEMP: log the first few raw JSON results to diagnose parsing

        public OakDPoseProvider(ILogService logService, PoseSpaceConverter converter, string modelPath, int deviceNum, float landmarkScoreThreshold) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (converter == null) {
                throw new ArgumentNullException(nameof(converter));
            }
            this.logService = logService;
            this.converter = converter;
            this.modelPath = modelPath;
            this.deviceNum = deviceNum;
            this.landmarkScoreThreshold = landmarkScoreThreshold;
        }

        public bool IsRunning {
            get {
                return running;
            }
        }

        public void StartTracking() {
            if (running) {
                return;
            }
            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath)) {
                logService.Log(LogLevel.Error, "OAK-D MoveNet model not found at: " + modelPath);
                return;
            }
            try {
                PipelineConfig config = BuildConfig();
                workerFrame = new PoseFrame();
                mainFrame = new PoseFrame();
                hasNewFrame = false;
                hasAnyFrame = false;
                bool initialized = InitBodyPose(config);
                if (initialized) {
                    running = true;
                    workerThread = new Thread(WorkerLoop);
                    workerThread.IsBackground = true;
                    workerThread.Start();
                    logService.Log(LogLevel.Info, "OAK-D pose provider started (on-device MoveNet + stereo depth, worker thread).");
                } else {
                    running = false;
                    logService.Log(LogLevel.Warning, "OAK-D InitBodyPose returned false (no device? check USB / DepthAI).");
                }
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start OAK-D pose provider");
                running = false;
            }
        }

        public void StopTracking() {
            running = false;
        }

        public void Tick(float deltaSeconds) {
            // No-op: the blocking BodyPoseResults call runs on WorkerLoop so it never stalls the main thread.
        }

        // Background thread: repeatedly query the device (blocking) and fill the worker frame.
        private void WorkerLoop() {
            while (running) {
                try {
                    FrameInfo frameInfo;
                    IntPtr resultPtr = BodyPoseResults(out frameInfo, false, 300, 300, true, false, landmarkScoreThreshold, false, false, true, deviceNum);
                    if (resultPtr != IntPtr.Zero) {
                        string json = Marshal.PtrToStringAnsi(resultPtr);
                        if (rawDebugRemaining > 0) {
                            rawDebugRemaining = rawDebugRemaining - 1;
                            int len = json == null ? -1 : json.Length;
                            string sample = (json != null && json.Length > 600) ? json.Substring(0, 600) : json;
                            logService.Log(LogLevel.Info, "OAK-D raw[" + len + "]: " + sample);
                        }
                        frameCounter = frameCounter + 1;
                        ParseInto(workerFrame, json, frameCounter * 0.033);
                        lock (gate) {
                            hasNewFrame = true;
                        }
                    }
                } catch (Exception exception) {
                    logService.LogException(exception, "OAK-D worker results/parse failed");
                }
            }
        }

        public bool TryGetLatestFrame(out PoseFrame latest) {
            lock (gate) {
                if (hasNewFrame) {
                    PoseFrame temp = mainFrame;
                    mainFrame = workerFrame;
                    workerFrame = temp;
                    hasNewFrame = false;
                    hasAnyFrame = true;
                }
            }
            latest = mainFrame;
            return hasAnyFrame;
        }

        public void Dispose() {
            running = false;
            try {
                // Close the device first: this unblocks a worker that may be parked inside BodyPoseResults.
                DAICloseDevice(deviceNum);
            } catch (Exception exception) {
                logService.LogException(exception, "OAK-D DAICloseDevice failed");
            }
            if (workerThread != null) {
                workerThread.Join(1000);
                workerThread = null;
            }
        }

        private PipelineConfig BuildConfig() {
            // Mirrors OAKForUnity.DaiBodyPose.InitDevice defaults for the MoveNet lightning model.
            PipelineConfig config = new PipelineConfig();
            config.deviceNum = deviceNum;
            config.deviceId = string.Empty;
            config.colorCameraFPS = 30f;
            // THE_800_P (4): OV9782-based OAK-D color sensors do NOT support 1080p (it silently fell back to
            // 800p). With ISP 2/3 on 800p the RGB width became 854, which is NOT a multiple of 16, so the
            // stereo-depth aligner errored and the native pipeline aborted → Unity crash. 800p + ISP 1/2 =
            // 640-wide (a multiple of 16), which the depth aligner accepts. 720p (1) is the other safe option.
            config.colorCameraResolution = 4; // THE_800_P
            config.colorCameraInterleaved = true;
            config.colorCameraColorOrder = 0; // BGR
            config.previewSizeWidth = 192; // MoveNet lightning input
            config.previewSizeHeight = 192;
            config.ispScaleF1 = 1;
            config.ispScaleF2 = 2;
            config.manualFocus = 130;
            config.monoRCameraFPS = 30f;
            config.monoRCameraResolution = 0; // THE_400_P
            config.monoLCameraFPS = 30f;
            config.monoLCameraResolution = 0; // THE_400_P
            config.confidenceThreshold = 230;
            config.leftRightCheck = true;
            config.subpixel = true;
            config.extendedDisparity = false;
            config.depthAlign = 1; // align depth to RGB
            config.medianFilter = 0; // MEDIAN_OFF
            config.nnPath1 = modelPath;
            config.nnPath2 = string.Empty;
            config.nnPath3 = string.Empty;
            config.rate = 0f;
            config.freq = 0;
            config.batchReportThreshold = 0;
            config.maxBatchReports = 0;
            config.previewMode = 0; // CROP
            config.useSpatialLocator = true; // 3D metric keypoints from stereo depth
            return config;
        }

        // Parse the plugin's JSON body-pose result. Each landmark has index + flat spatial keys
        // "location.x/y/z" in MILLIMETRES (real 3D via spatial locator). Convert to hip-centred Unity metres.
        private void ParseInto(PoseFrame target, string json, double timestampSeconds) {
            if (string.IsNullOrEmpty(json)) {
                target.MarkInvalid(timestampSeconds);
                return;
            }
            JObject root = JObject.Parse(json);
            JToken landmarksToken = root["landmarks"];
            if (landmarksToken == null) {
                target.MarkInvalid(timestampSeconds);
                return;
            }

            int slot = 0;
            while (slot < PoseFrame.LandmarkCount) {
                target.SetLandmark(slot, new PoseLandmark(Vector3.zero, 0f));
                slot = slot + 1;
            }

            Vector3[] raw = new Vector3[MoveNetToJoint.Length];
            bool[] present = new bool[MoveNetToJoint.Length];
            bool hasLeftHip = false;
            bool hasRightHip = false;

            foreach (JToken node in landmarksToken) {
                int index = node.Value<int>("index");
                if (index < 0 || index >= MoveNetToJoint.Length) {
                    continue;
                }
                float xMm = ReadFloat(node, "location.x");
                float yMm = ReadFloat(node, "location.y");
                float zMm = ReadFloat(node, "location.z");
                if (xMm == 0f && yMm == 0f && zMm == 0f) {
                    continue; // no valid spatial location for this keypoint this frame
                }
                raw[index] = new Vector3(xMm, yMm, zMm) / 1000f; // mm -> metres (camera space)
                present[index] = true;
                if (index == 11) {
                    hasLeftHip = true;
                } else if (index == 12) {
                    hasRightHip = true;
                }
            }

            Vector3 midHip = Vector3.zero;
            bool hasHip = hasLeftHip && hasRightHip;
            if (hasHip) {
                midHip = 0.5f * (raw[11] + raw[12]);
            }

            int mapped = 0;
            int i = 0;
            while (i < MoveNetToJoint.Length) {
                if (present[i]) {
                    Vector3 centred = hasHip ? (raw[i] - midHip) : raw[i];
                    Vector3 unity = converter.ToUnity(centred.x, centred.y, centred.z);
                    target.SetLandmark((int)MoveNetToJoint[i], new PoseLandmark(unity, 1f));
                    mapped = mapped + 1;
                }
                i = i + 1;
            }

            if (mapped > 0) {
                target.SetMeta(timestampSeconds, true);
            } else {
                target.MarkInvalid(timestampSeconds);
            }
        }

        private static float ReadFloat(JToken node, string key) {
            JToken value = node[key];
            if (value == null) {
                return 0f;
            }
            return value.Value<float>();
        }
    }
}
