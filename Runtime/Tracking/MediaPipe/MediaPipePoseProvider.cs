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
        private static bool globalInitialized;

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
        private PoseFrame pendingFrame;
        private PoseFrame latest;

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
                EnsureGlobalInit();
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
                running = true;
                logService.Log(LogLevel.Info, "MediaPipe pose provider started (CPU, VIDEO, full model, async worker).");
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start MediaPipe pose provider");
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
            try {
                textureFrame.ReadTextureOnCPU(sourceTexture, false, true);
                Mediapipe.Image image = textureFrame.BuildCPUImage();
                timestampMillis = timestampMillis + Math.Max(1L, (long)(deltaSeconds * 1000f));
                long currentTimestamp = timestampMillis;

                lock (workerLock) {
                    isWorkerBusy = true;
                }

                ThreadPool.QueueUserWorkItem(state => {
                    ProcessFrameOnWorker(image, textureFrame, currentTimestamp);
                });
            } catch (Exception exception) {
                textureFrame.Release();
                logService.LogException(exception, "MediaPipe pose detection frame submit failed");
            }
        }

        public bool TryGetLatestFrame(out PoseFrame frame) {
            lock (workerLock) {
                if (pendingFrame != null) {
                    latest = pendingFrame;
                    pendingFrame = null;
                }
            }
            frame = latest;
            return latest != null;
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
            if (globalInitialized) {
                try {
                    Mediapipe.Glog.Shutdown();
                } catch (Exception exception) {
                    logService.LogException(exception, "Failed to shutdown MediaPipe Glog");
                }
                globalInitialized = false;
            }
        }

        private void ProcessFrameOnWorker(Mediapipe.Image image, TextureFrame textureFrame, long currentTimestamp) {
            try {
                bool detected = landmarker.TryDetectForVideo(image, currentTimestamp, imageProcessingOptions, ref result);
                PoseFrame frame;
                if (detected) {
                    frame = BuildFrameFromWorker(result, currentTimestamp);
                } else {
                    frame = new PoseFrame(null, currentTimestamp * 0.001, false);
                }
                lock (workerLock) {
                    pendingFrame = frame;
                }
            } catch (Exception exception) {
                logService.LogException(exception, "MediaPipe pose worker detection failed");
            } finally {
                image.Dispose();
                textureFrame.Release();
                lock (workerLock) {
                    isWorkerBusy = false;
                }
            }
        }

        private PoseFrame BuildFrameFromWorker(PoseLandmarkerResult currentResult, long timestamp) {
            List<Landmarks> worldLandmarks = currentResult.poseWorldLandmarks;
            if (worldLandmarks == null || worldLandmarks.Count == 0) {
                return new PoseFrame(null, timestamp * 0.001, false);
            }
            List<Landmark> landmarks = worldLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < PoseFrame.LandmarkCount) {
                return new PoseFrame(null, timestamp * 0.001, false);
            }
            PoseLandmark[] landmarksBuffer = new PoseLandmark[PoseFrame.LandmarkCount];
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                Landmark landmark = landmarks[index];
                Vector3 unityPosition = converter.ToUnity(landmark.x, landmark.y, landmark.z);
                float confidence = landmark.visibility.GetValueOrDefault(1f);
                landmarksBuffer[index] = new PoseLandmark(unityPosition, confidence);
                index = index + 1;
            }
            return new PoseFrame(landmarksBuffer, timestamp * 0.001, true);
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

        private void EnsureGlobalInit() {
            if (globalInitialized) {
                return;
            }
            try {
                Mediapipe.Protobuf.SetLogHandler(Mediapipe.Protobuf.DefaultLogHandler);
                Mediapipe.Glog.Initialize("MediaPipeUnityPlugin");
                globalInitialized = true;
            } catch (Exception exception) {
                logService.LogException(exception, "MediaPipe global init warning");
                globalInitialized = true;
            }
        }
    }
}

