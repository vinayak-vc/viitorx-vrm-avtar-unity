using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;

using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.Experimental;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.MediaPipe {
    /// <summary>
    /// <see cref="IFaceTrackingProvider"/> backed by the MediaPipe Face Landmarker (homuler plugin,
    /// ADR-010). Runs on the CPU in VIDEO mode with blendshape output enabled, reading frames from
    /// <see cref="ICameraCapture"/> and mapping the ARKit-style blendshape categories to
    /// <see cref="FaceFrame"/> expression weights. Mirrors <see cref="MediaPipePoseProvider"/> exactly:
    /// inference is offloaded to a background thread while the main thread owns the (non-thread-safe)
    /// <see cref="TextureFramePool"/> and <see cref="Mediapipe.Image"/>, with a double-buffered reusable
    /// <see cref="FaceFrame"/> so there is no per-frame allocation. Only this file references MediaPipe types.
    /// </summary>
    public sealed class MediaPipeFaceProvider : IFaceTrackingProvider {
        private readonly object workerLock;
        private readonly ILogService logService;
        private readonly ICameraCapture cameraCapture;
        private readonly string modelPath;

        private FaceLandmarker landmarker;
        private TextureFramePool texturePool;
        private ImageProcessingOptions imageProcessingOptions;
        private FaceLandmarkerResult result;
        private int poolWidth;
        private int poolHeight;
        private long timestampMillis;
        private bool running;
        private bool isWorkerBusy;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        private FaceFrame workerFrame;
        private FaceFrame mainFrame;
        private TextureFrame inFlightTextureFrame;
        private Mediapipe.Image inFlightImage;

        public MediaPipeFaceProvider(ILogService logService, ICameraCapture cameraCapture, string modelPath) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (cameraCapture == null) {
                throw new ArgumentNullException(nameof(cameraCapture));
            }
            workerLock = new object();
            this.logService = logService;
            this.cameraCapture = cameraCapture;
            this.modelPath = modelPath;
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
                logService.Log(LogLevel.Warning, "MediaPipe face model not found at: " + modelPath);
                return false;
            }
            try {
                MediaPipeGlobalInit.Acquire(logService);
                byte[] modelBytes = File.ReadAllBytes(modelPath);
                BaseOptions baseOptions = new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: modelBytes);
                FaceLandmarkerOptions options = new FaceLandmarkerOptions(
                    baseOptions,
                    runningMode: RunningMode.VIDEO,
                    numFaces: 1,
                    minFaceDetectionConfidence: 0.5f,
                    minFacePresenceConfidence: 0.5f,
                    minTrackingConfidence: 0.5f,
                    outputFaceBlendshapes: true,
                    outputFaceTransformationMatrixes: false,
                    resultCallback: null);
                landmarker = FaceLandmarker.CreateFromOptions(options, null);
                imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: 0);
                result = FaceLandmarkerResult.Alloc(1, true, false);
                workerFrame = new FaceFrame();
                mainFrame = new FaceFrame();
                hasNewFrame = false;
                hasAnyFrame = false;
                running = true;
                logService.Log(LogLevel.Info, "MediaPipe face provider started (CPU, VIDEO, blendshapes, async worker).");
                return true;
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start MediaPipe face provider");
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
                logService.LogException(exception, "MediaPipe face detection frame submit failed");
                ReleaseInFlight();
                lock (workerLock) {
                    isWorkerBusy = false;
                }
            }
        }

        public bool TryGetFrame(out FaceFrame frame) {
            lock (workerLock) {
                if (hasNewFrame) {
                    FaceFrame temp = mainFrame;
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
                    logService.LogException(exception, "Failed to close MediaPipe face landmarker");
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
                logService.LogException(exception, "MediaPipe face worker detection failed");
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

        private void FillFrameFromWorker(FaceLandmarkerResult currentResult, long timestamp) {
            List<Classifications> blendshapes = currentResult.faceBlendshapes;
            if (blendshapes == null || blendshapes.Count == 0) {
                workerFrame.MarkInvalid(timestamp * 0.001);
                return;
            }
            List<Category> categories = blendshapes[0].categories;
            if (categories == null || categories.Count == 0) {
                workerFrame.MarkInvalid(timestamp * 0.001);
                return;
            }

            float blinkLeft = 0f;
            float blinkRight = 0f;
            float jawOpen = 0f;
            float smileLeft = 0f;
            float smileRight = 0f;
            float browDownLeft = 0f;
            float browDownRight = 0f;
            float browInnerUp = 0f;
            float eyeWideLeft = 0f;
            float eyeWideRight = 0f;

            int index = 0;
            while (index < categories.Count) {
                Category category = categories[index];
                index = index + 1;
                switch (category.categoryName) {
                    case "eyeBlinkLeft":
                        blinkLeft = category.score;
                        break;
                    case "eyeBlinkRight":
                        blinkRight = category.score;
                        break;
                    case "jawOpen":
                        jawOpen = category.score;
                        break;
                    case "mouthSmileLeft":
                        smileLeft = category.score;
                        break;
                    case "mouthSmileRight":
                        smileRight = category.score;
                        break;
                    case "browDownLeft":
                        browDownLeft = category.score;
                        break;
                    case "browDownRight":
                        browDownRight = category.score;
                        break;
                    case "browInnerUp":
                        browInnerUp = category.score;
                        break;
                    case "eyeWideLeft":
                        eyeWideLeft = category.score;
                        break;
                    case "eyeWideRight":
                        eyeWideRight = category.score;
                        break;
                }
            }

            float smile = Mathf.Max(smileLeft, smileRight);
            float angry = Mathf.Max(browDownLeft, browDownRight);
            float surprised = Mathf.Max(browInnerUp, Mathf.Max(eyeWideLeft, eyeWideRight));
            workerFrame.SetExpressions(blinkLeft, blinkRight, jawOpen, smile, angry, surprised);
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
