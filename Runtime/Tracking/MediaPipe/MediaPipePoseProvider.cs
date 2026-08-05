using System;
using System.Collections.Generic;
using System.IO;

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
    /// stays source-agnostic. Only this file references MediaPipe types.
    /// </summary>
    public sealed class MediaPipePoseProvider : IBodyTrackingProvider {
        private static bool globalInitialized;

        private readonly ILogService logService;
        private readonly ICameraCapture cameraCapture;
        private readonly string modelPath;
        private readonly PoseSpaceConverter converter;
        private readonly PoseLandmark[] buffer;

        private PoseLandmarker landmarker;
        private TextureFramePool texturePool;
        private ImageProcessingOptions imageProcessingOptions;
        private PoseLandmarkerResult result;
        private int poolWidth;
        private int poolHeight;
        private long timestampMillis;
        private bool running;
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
            this.logService = logService;
            this.cameraCapture = cameraCapture;
            this.modelPath = modelPath;
            this.converter = converter;
            buffer = new PoseLandmark[PoseFrame.LandmarkCount];
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
                logService.Log(LogLevel.Info, "MediaPipe pose provider started (CPU, VIDEO, full model).");
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
                bool detected = landmarker.TryDetectForVideo(image, timestampMillis, imageProcessingOptions, ref result);
                if (detected) {
                    BuildFrame();
                } else {
                    latest = new PoseFrame(null, timestampMillis * 0.001, false);
                }
            } catch (Exception exception) {
                logService.LogException(exception, "MediaPipe pose detection failed");
            } finally {
                textureFrame.Release();
            }
        }

        public bool TryGetLatestFrame(out PoseFrame frame) {
            frame = latest;
            return latest != null;
        }

        public void Dispose() {
            running = false;
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
        }

        private void BuildFrame() {
            List<Landmarks> worldLandmarks = result.poseWorldLandmarks;
            if (worldLandmarks == null || worldLandmarks.Count == 0) {
                latest = new PoseFrame(null, timestampMillis * 0.001, false);
                return;
            }
            List<Landmark> landmarks = worldLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < PoseFrame.LandmarkCount) {
                latest = new PoseFrame(null, timestampMillis * 0.001, false);
                return;
            }
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                Landmark landmark = landmarks[index];
                Vector3 unityPosition = converter.ToUnity(landmark.x, landmark.y, landmark.z);
                float confidence = landmark.visibility.GetValueOrDefault(1f);
                buffer[index] = new PoseLandmark(unityPosition, confidence);
                index = index + 1;
            }
            PoseLandmark[] copy = new PoseLandmark[PoseFrame.LandmarkCount];
            Array.Copy(buffer, copy, PoseFrame.LandmarkCount);
            latest = new PoseFrame(copy, timestampMillis * 0.001, true);
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
