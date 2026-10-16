using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;

using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity.Experimental;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.MediaPipe {
    /// <summary>
    /// <see cref="IBodyTrackingProvider"/> backed by the MediaPipe Pose Landmarker (homuler plugin,
    /// ADR-010). Runs the FULL model on the CPU in VIDEO mode, reading frames from
    /// <see cref="ICameraCapture"/>. Emits world landmarks converted to Unity space so the retargeter
    /// stays source-agnostic. Offloads inference to a background thread to keep main thread responsive.
    /// Only this file references MediaPipe types.
    /// </summary>
    public sealed class MediaPipePoseProvider : IBodyTrackingProvider {
        private readonly object workerLock;
        private readonly ILogService logService;
        private readonly ICameraCapture cameraCapture;
        private readonly string modelPath;
        private readonly PoseSpaceConverter converter;

        private PoseLandmarker landmarker;
        private TextureFramePool texturePool;
        private ImageProcessingOptions imageProcessingOptions;
        private PoseLandmarkerResult result;
        private int poolWidth;
        private int poolHeight;
        private long timestampMillis;
        private bool running;
        private bool isWorkerBusy;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        private PoseFrame workerFrame;
        private PoseFrame mainFrame;
        private TextureFrame inFlightTextureFrame;
        private Mediapipe.Image inFlightImage;

        public MediaPipePoseProvider(ILogService logService, ICameraCapture cameraCapture, string modelPath, PoseSpaceConverter converter) {
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

        public bool IsRunning {
            get {
                return running;
            }
        }

        public void StartTracking() {
            if (running) {
                return;
            }
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath)) {
                logService.Log(LogLevel.Error, "MediaPipe pose model not found at: " + modelPath);
                return;
            }
            try {
                MediaPipeGlobalInit.Acquire(logService);
                byte[] modelBytes = File.ReadAllBytes(modelPath);
                BaseOptions baseOptions = new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: modelBytes);
                PoseLandmarkerOptions options = new PoseLandmarkerOptions(
                    baseOptions,
                    runningMode: RunningMode.VIDEO,
                    numPoses: 1,
                    minPoseDetectionConfidence: 0.5f,
                    minPosePresenceConfidence: 0.5f,
                    minTrackingConfidence: 0.5f,
                    outputSegmentationMasks: false,
                    resultCallback: null);
                landmarker = PoseLandmarker.CreateFromOptions(options, null);
                imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: 0);
                result = PoseLandmarkerResult.Alloc(1, false);
                workerFrame = new PoseFrame();
                mainFrame = new PoseFrame();
                hasNewFrame = false;
                hasAnyFrame = false;
                running = true;
                logService.Log(LogLevel.Info, "MediaPipe pose provider started (CPU, VIDEO, full model, async worker).");
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start MediaPipe pose provider");
                MediaPipeGlobalInit.Release(logService); // H4: pair the Acquire above so the refcount doesn't leak on failure
                running = false;
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
                logService.LogException(exception, "MediaPipe pose detection frame submit failed");
                ReleaseInFlight();
                lock (workerLock) {
                    isWorkerBusy = false;
                }
            }
        }

        public bool TryGetLatestFrame(out PoseFrame frame) {
            lock (workerLock) {
                if (hasNewFrame) {
                    PoseFrame temp = mainFrame;
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
                    logService.LogException(exception, "Failed to close MediaPipe pose landmarker");
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
                logService.LogException(exception, "MediaPipe pose worker detection failed");
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

        private void FillFrameFromWorker(PoseLandmarkerResult currentResult, long timestamp) {
            List<Landmarks> worldLandmarks = currentResult.poseWorldLandmarks;
            if (worldLandmarks == null || worldLandmarks.Count == 0) {
                workerFrame.MarkInvalid(timestamp * 0.001);
                return;
            }
            List<Landmark> landmarks = worldLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < PoseFrame.LandmarkCount) {
                workerFrame.MarkInvalid(timestamp * 0.001);
                return;
            }
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                Landmark landmark = landmarks[index];
                Vector3 unityPosition = converter.ToUnity(landmark.x, landmark.y, landmark.z);
                float confidence = landmark.visibility.GetValueOrDefault(1f);
                workerFrame.SetLandmark(index, new PoseLandmark(unityPosition, confidence));
                index = index + 1;
            }
            workerFrame.SetMeta(timestamp * 0.001, true);
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

