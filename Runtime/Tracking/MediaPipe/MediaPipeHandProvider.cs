using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;

using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Experimental;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.MediaPipe {
    /// <summary>
    /// <see cref="IHandTrackingProvider"/> backed by the MediaPipe Hand Landmarker (homuler plugin,
    /// ADR-010). Runs on the CPU in VIDEO mode for up to two hands, reading frames from
    /// <see cref="ICameraCapture"/>, and reduces the 21 world landmarks per hand to a per-finger curl
    /// (0 open .. 1 curled) derived from the bend angle at each finger's middle joint. Mirrors
    /// <see cref="MediaPipePoseProvider"/> exactly: inference runs on a background thread while the main
    /// thread owns the (non-thread-safe) <see cref="TextureFramePool"/> and <see cref="Mediapipe.Image"/>,
    /// with a double-buffered reusable <see cref="HandFrame"/> so there is no per-frame allocation.
    /// Only this file references MediaPipe hand types.
    /// </summary>
    public sealed class MediaPipeHandProvider : IHandTrackingProvider {
        // MediaPipe hand landmark indices: [proximal, middle, tip] joints per finger.
        private static readonly int[] ThumbJoints = new int[] { 2, 3, 4 };
        private static readonly int[] IndexJoints = new int[] { 5, 6, 8 };
        private static readonly int[] MiddleJoints = new int[] { 9, 10, 12 };
        private static readonly int[] RingJoints = new int[] { 13, 14, 16 };
        private static readonly int[] LittleJoints = new int[] { 17, 18, 20 };
        private const int HandLandmarkCount = 21;
        private const float MaxBendAngle = 160f;

        private readonly object workerLock;
        private readonly ILogService logService;
        private readonly ICameraCapture cameraCapture;
        private readonly string modelPath;
        private readonly PoseSpaceConverter converter;

        private HandLandmarker landmarker;
        private TextureFramePool texturePool;
        private ImageProcessingOptions imageProcessingOptions;
        private HandLandmarkerResult result;
        private int poolWidth;
        private int poolHeight;
        private long timestampMillis;
        private bool running;
        private bool isWorkerBusy;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        private HandFrame workerFrame;
        private HandFrame mainFrame;
        private TextureFrame inFlightTextureFrame;
        private Mediapipe.Image inFlightImage;

        public MediaPipeHandProvider(ILogService logService, ICameraCapture cameraCapture, string modelPath, PoseSpaceConverter converter) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (cameraCapture == null) {
                throw new ArgumentNullException(nameof(cameraCapture));
            }
            if (converter == null) {
                throw new ArgumentNullException(nameof(converter));
            }
            workerLock = new object();
            this.logService = logService;
            this.cameraCapture = cameraCapture;
            this.modelPath = modelPath;
            this.converter = converter;
        }

        public bool IsTracking {
            get {
                return running;
            }
        }

        public bool StartTracking() {
            if (running) {
                return true;
            }
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath)) {
                logService.Log(LogLevel.Warning, "MediaPipe hand model not found at: " + modelPath);
                return false;
            }
            try {
                MediaPipeGlobalInit.Acquire(logService);
                byte[] modelBytes = File.ReadAllBytes(modelPath);
                BaseOptions baseOptions = new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: modelBytes);
                HandLandmarkerOptions options = new HandLandmarkerOptions(
                    baseOptions,
                    runningMode: RunningMode.VIDEO,
                    numHands: 2,
                    minHandDetectionConfidence: 0.5f,
                    minHandPresenceConfidence: 0.5f,
                    minTrackingConfidence: 0.5f,
                    resultCallback: null);
                landmarker = HandLandmarker.CreateFromOptions(options, null);
                imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: 0);
                result = HandLandmarkerResult.Alloc(2);
                workerFrame = new HandFrame();
                mainFrame = new HandFrame();
                hasNewFrame = false;
                hasAnyFrame = false;
                running = true;
                logService.Log(LogLevel.Info, "MediaPipe hand provider started (CPU, VIDEO, 2 hands, async worker).");
                return true;
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start MediaPipe hand provider");
                MediaPipeGlobalInit.Release(logService);
                running = false;
                return false;
            }
        }

        public void StopTracking() {
            running = false;
        }

        public void Tick(float deltaSeconds) {
            if (!running || landmarker == null) {
                return;
            }
            lock (workerLock) {
                if (isWorkerBusy) {
                    return;
                }
            }
            // Worker is idle: release the previous frame's pool/GPU resources on the MAIN thread.
            // The TextureFramePool and Image are not thread-safe, so only the worker's TryDetectForVideo
            // runs off-thread; all pool get/release + Image build/dispose stay here.
            ReleaseInFlight();
            Texture sourceTexture = cameraCapture.CurrentTexture;
            if (sourceTexture == null || cameraCapture.Width <= 16 || cameraCapture.Height <= 16) {
                return;
            }
            EnsurePool(cameraCapture.Width, cameraCapture.Height);
            if (texturePool == null) {
                return;
            }
            TextureFrame textureFrame;
            if (!texturePool.TryGetTextureFrame(out textureFrame)) {
                return;
            }
            inFlightTextureFrame = textureFrame;
            try {
                textureFrame.ReadTextureOnCPU(sourceTexture, false, true);
                Mediapipe.Image image = textureFrame.BuildCPUImage();
                inFlightImage = image;
                timestampMillis = timestampMillis + Math.Max(1L, (long)(deltaSeconds * 1000f));
                long currentTimestamp = timestampMillis;
                lock (workerLock) {
                    isWorkerBusy = true;
                }
                ThreadPool.QueueUserWorkItem(state => {
                    ProcessFrameOnWorker(image, currentTimestamp);
                });
            } catch (Exception exception) {
                logService.LogException(exception, "MediaPipe hand detection frame submit failed");
                ReleaseInFlight();
                lock (workerLock) {
                    isWorkerBusy = false;
                }
            }
        }

        public bool TryGetFrame(out HandFrame frame) {
            lock (workerLock) {
                if (hasNewFrame) {
                    HandFrame temp = mainFrame;
                    mainFrame = workerFrame;
                    workerFrame = temp;
                    hasNewFrame = false;
                    hasAnyFrame = true;
                }
            }
            frame = mainFrame;
            return hasAnyFrame;
        }

        public void Dispose() {
            running = false;
            bool busy = true;
            while (busy) {
                lock (workerLock) {
                    busy = isWorkerBusy;
                }
                if (busy) {
                    Thread.Sleep(1);
                }
            }
            ReleaseInFlight();
            if (landmarker != null) {
                try {
                    landmarker.Close();
                } catch (Exception exception) {
                    logService.LogException(exception, "Failed to close MediaPipe hand landmarker");
                }
                landmarker = null;
            }
            if (texturePool != null) {
                texturePool.Dispose();
                texturePool = null;
            }
            MediaPipeGlobalInit.Release(logService);
        }

        private void ProcessFrameOnWorker(Mediapipe.Image image, long currentTimestamp) {
            try {
                bool detected = landmarker.TryDetectForVideo(image, currentTimestamp, imageProcessingOptions, ref result);
                if (detected) {
                    FillFrameFromWorker(result, currentTimestamp);
                } else {
                    workerFrame.MarkInvalid(currentTimestamp * 0.001);
                }
                lock (workerLock) {
                    hasNewFrame = true;
                }
            } catch (Exception exception) {
                logService.LogException(exception, "MediaPipe hand worker detection failed");
            } finally {
                // Do NOT touch the pool/Image here — the main thread owns them (see ReleaseInFlight).
                lock (workerLock) {
                    isWorkerBusy = false;
                }
            }
        }

        private void ReleaseInFlight() {
            if (inFlightImage != null) {
                inFlightImage.Dispose();
                inFlightImage = null;
            }
            if (inFlightTextureFrame != null) {
                inFlightTextureFrame.Release();
                inFlightTextureFrame = null;
            }
        }

        private void FillFrameFromWorker(HandLandmarkerResult currentResult, long timestamp) {
            List<Landmarks> worldLandmarks = currentResult.handWorldLandmarks;
            List<Classifications> handedness = currentResult.handedness;
            if (worldLandmarks == null || worldLandmarks.Count == 0) {
                workerFrame.MarkInvalid(timestamp * 0.001);
                return;
            }

            float leftThumb = 0f;
            float leftIndex = 0f;
            float leftMiddle = 0f;
            float leftRing = 0f;
            float leftLittle = 0f;
            float rightThumb = 0f;
            float rightIndex = 0f;
            float rightMiddle = 0f;
            float rightRing = 0f;
            float rightLittle = 0f;

            Quaternion leftWrist = Quaternion.identity;
            Quaternion rightWrist = Quaternion.identity;
            bool leftTracked = false;
            bool rightTracked = false;

            int handIndex = 0;
            while (handIndex < worldLandmarks.Count) {
                List<Landmark> landmarks = worldLandmarks[handIndex].landmarks;
                if (landmarks == null || landmarks.Count < HandLandmarkCount) {
                    handIndex = handIndex + 1;
                    continue;
                }
                bool isLeft = IsLeftHand(handedness, handIndex);
                float thumb = FingerCurl(landmarks, ThumbJoints);
                float index = FingerCurl(landmarks, IndexJoints);
                float middle = FingerCurl(landmarks, MiddleJoints);
                float ring = FingerCurl(landmarks, RingJoints);
                float little = FingerCurl(landmarks, LittleJoints);
                Quaternion wrist = PalmRotation(landmarks);
                if (isLeft) {
                    leftThumb = thumb;
                    leftIndex = index;
                    leftMiddle = middle;
                    leftRing = ring;
                    leftLittle = little;
                    leftWrist = wrist;
                    leftTracked = true;
                } else {
                    rightThumb = thumb;
                    rightIndex = index;
                    rightMiddle = middle;
                    rightRing = ring;
                    rightLittle = little;
                    rightWrist = wrist;
                    rightTracked = true;
                }
                handIndex = handIndex + 1;
            }

            workerFrame.SetCurls(
                leftThumb, leftIndex, leftMiddle, leftRing, leftLittle,
                rightThumb, rightIndex, rightMiddle, rightRing, rightLittle);
            workerFrame.SetWristRotations(leftWrist, leftTracked, rightWrist, rightTracked);
            workerFrame.SetMeta(timestamp * 0.001, true);
        }

        private static bool IsLeftHand(List<Classifications> handedness, int handIndex) {
            if (handedness == null || handIndex >= handedness.Count) {
                return handIndex == 0;
            }
            List<Category> categories = handedness[handIndex].categories;
            if (categories == null || categories.Count == 0) {
                return handIndex == 0;
            }
            return categories[0].categoryName == "Left";
        }

        private static float FingerCurl(List<Landmark> landmarks, int[] joints) {
            Vector3 proximal = ToVector(landmarks[joints[0]]);
            Vector3 middle = ToVector(landmarks[joints[1]]);
            Vector3 tip = ToVector(landmarks[joints[2]]);
            Vector3 lower = middle - proximal;
            Vector3 upper = tip - middle;
            if (lower.sqrMagnitude < 1e-10f || upper.sqrMagnitude < 1e-10f) {
                return 0f;
            }
            float angle = Vector3.Angle(lower, upper);
            return Mathf.Clamp01(angle / MaxBendAngle);
        }

        private static Vector3 ToVector(Landmark landmark) {
            return new Vector3(landmark.x, landmark.y, landmark.z);
        }

        // Palm orientation in Unity space: forward = wrist(0) -> middle knuckle(9) (finger direction),
        // up = palm normal = forward x acrossPalm, where acrossPalm = indexMcp(5) -> pinkyMcp(17). Points
        // are run through the SAME PoseSpaceConverter as the body so the axes/mirror match. Consumers use
        // this delta-from-neutral, so a constant bone-axis offset cancels and only relative wrist motion shows.
        private Quaternion PalmRotation(List<Landmark> landmarks) {
            Vector3 wrist = ConvertPoint(landmarks[0]);
            Vector3 indexMcp = ConvertPoint(landmarks[5]);
            Vector3 middleMcp = ConvertPoint(landmarks[9]);
            Vector3 pinkyMcp = ConvertPoint(landmarks[17]);
            Vector3 forward = middleMcp - wrist;
            Vector3 across = indexMcp - pinkyMcp;
            if (forward.sqrMagnitude < 1e-10f || across.sqrMagnitude < 1e-10f) {
                return Quaternion.identity;
            }
            Vector3 normal = Vector3.Cross(forward, across);
            if (normal.sqrMagnitude < 1e-10f) {
                return Quaternion.identity;
            }
            return Quaternion.LookRotation(forward.normalized, normal.normalized);
        }

        private Vector3 ConvertPoint(Landmark landmark) {
            return converter.ToUnity(landmark.x, landmark.y, landmark.z);
        }

        private void EnsurePool(int width, int height) {
            if (texturePool != null && poolWidth == width && poolHeight == height) {
                return;
            }
            if (texturePool != null) {
                texturePool.Dispose();
                texturePool = null;
            }
            texturePool = new TextureFramePool(width, height, TextureFormat.RGBA32, 10);
            poolWidth = width;
            poolHeight = height;
        }
    }
}
