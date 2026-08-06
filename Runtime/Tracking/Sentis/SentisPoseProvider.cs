using System;

using UnityEngine;

using Unity.InferenceEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.Sentis {
    /// <summary>
    /// <see cref="IBodyTrackingProvider"/> backed by a RTMW3D (RTMPose3D whole-body) ONNX model running on
    /// Unity Sentis (ADR-015). Inference runs on the GPU (<see cref="BackendType.GPUCompute"/>) on the idle
    /// RTX 3060, giving true 3D keypoints (unlike monocular MediaPipe's weak Z). The model is top-down and
    /// SimCC-coded: input is a 288x384 person crop; three outputs are per-keypoint 1D coordinate
    /// classifications for x (576 bins = 288*2), y (768 = 384*2) and z (576). Decode = argmax over the bins
    /// divided by the SimCC split ratio (2.0). The 133 COCO-WholeBody keypoints are mapped down to the 33
    /// BlazePose <see cref="JointId"/> slots the retargeter consumes, expressed hip-centred so the avatar
    /// stays in place like the MediaPipe path.
    ///
    /// Perf: a warm inference is ~35 ms on a 3060, so readback is done ASYNCHRONOUSLY (request + poll
    /// <see cref="Tensor.IsReadbackRequestDone"/>) with frame-drop while one is in flight — the ~35 ms never
    /// stalls the main thread. The output <see cref="PoseFrame"/> is double-buffered and filled in place.
    ///
    /// Several conversions here (input normalization, image->hip-centred metres scale, axis signs) are
    /// convention-sensitive and MUST be tuned live like <c>poseFlipX</c>; the knobs are constructor args.
    /// </summary>
    public sealed class SentisPoseProvider : IBodyTrackingProvider {
        private const int InputWidth = 288;
        private const int InputHeight = 384;
        private const int Keypoints = 133;
        private const float SimccSplitRatio = 2.0f;
        private const int XBins = 576;
        private const int YBins = 768;
        private const int ZBins = 576;

        // ImageNet mean/std (RTMPose default preprocessing), per RGB channel, over 0..255.
        private static readonly Vector3 NormMean = new Vector3(123.675f, 116.28f, 103.53f);
        private static readonly Vector3 NormStd = new Vector3(58.395f, 57.12f, 57.375f);

        private readonly ILogService logService;
        private readonly ICameraCapture cameraCapture;
        private readonly PoseSpaceConverter converter;
        private readonly ModelAsset modelAsset;
        private readonly bool applyImageNetNorm;
        private readonly float metreScale;

        private Model model;
        private Worker worker;
        private RenderTexture resizeTarget;
        private Texture2D readbackTexture;
        private float[] inputBuffer;
        private TensorShape inputShape;

        private Tensor<float> pendingX;
        private Tensor<float> pendingY;
        private Tensor<float> pendingZ;

        private PoseFrame workerFrame;
        private PoseFrame mainFrame;
        private bool hasAnyFrame;
        private bool hasNewFrame;
        private bool inFlight;
        private bool running;
        private long frameCounter;

        // COCO-WholeBody(133) index -> JointId slot. Only the body/foot subset the retargeter needs is
        // mapped; face + hand keypoints are left unmapped (their JointId slots stay zero-confidence and the
        // retarget gate holds them). COCO-17 body order is indices 0..16; feet are 17..22.
        private static readonly int[] CocoToJoint = BuildMapping();

        public SentisPoseProvider(ILogService logService, ICameraCapture cameraCapture, PoseSpaceConverter converter, ModelAsset modelAsset, bool applyImageNetNorm, float metreScale) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (cameraCapture == null) {
                throw new ArgumentNullException(nameof(cameraCapture));
            }
            if (converter == null) {
                throw new ArgumentNullException(nameof(converter));
            }
            if (modelAsset == null) {
                throw new ArgumentNullException(nameof(modelAsset));
            }
            this.logService = logService;
            this.cameraCapture = cameraCapture;
            this.converter = converter;
            this.modelAsset = modelAsset;
            this.applyImageNetNorm = applyImageNetNorm;
            this.metreScale = metreScale;
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
            try {
                model = ModelLoader.Load(modelAsset);
                worker = new Worker(model, BackendType.GPUCompute);
                inputShape = new TensorShape(1, 3, InputHeight, InputWidth);
                inputBuffer = new float[3 * InputHeight * InputWidth];
                resizeTarget = new RenderTexture(InputWidth, InputHeight, 0, RenderTextureFormat.ARGB32);
                resizeTarget.Create();
                readbackTexture = new Texture2D(InputWidth, InputHeight, TextureFormat.RGBA32, false);
                workerFrame = new PoseFrame();
                mainFrame = new PoseFrame();
                hasAnyFrame = false;
                hasNewFrame = false;
                inFlight = false;
                running = true;
                logService.Log(LogLevel.Info, "Sentis 3D pose provider started (RTMW3D, GPUCompute).");
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start Sentis pose provider");
                running = false;
            }
        }

        public void StopTracking() {
            running = false;
        }

        public void Tick(float deltaSeconds) {
            if (!running || worker == null) {
                return;
            }
            if (inFlight) {
                PollReadback();
                return;
            }
            Texture sourceTexture = cameraCapture.CurrentTexture;
            if (sourceTexture == null || cameraCapture.Width <= 16 || cameraCapture.Height <= 16) {
                return;
            }
            if (!BuildInput(sourceTexture)) {
                return;
            }
            frameCounter = frameCounter + 1;
            Tensor<float> input = new Tensor<float>(inputShape, inputBuffer);
            worker.Schedule(input);
            input.Dispose();

            pendingX = worker.PeekOutput("output") as Tensor<float>;
            pendingY = worker.PeekOutput("1554") as Tensor<float>;
            pendingZ = worker.PeekOutput("1556") as Tensor<float>;
            if (pendingX == null || pendingY == null || pendingZ == null) {
                return;
            }
            pendingX.ReadbackRequest();
            pendingY.ReadbackRequest();
            pendingZ.ReadbackRequest();
            inFlight = true;
        }

        public bool TryGetLatestFrame(out PoseFrame frame) {
            if (hasNewFrame) {
                PoseFrame temp = mainFrame;
                mainFrame = workerFrame;
                workerFrame = temp;
                hasNewFrame = false;
                hasAnyFrame = true;
            }
            frame = mainFrame;
            return hasAnyFrame;
        }

        public void Dispose() {
            running = false;
            if (worker != null) {
                worker.Dispose();
                worker = null;
            }
            if (resizeTarget != null) {
                resizeTarget.Release();
                UnityEngine.Object.Destroy(resizeTarget);
                resizeTarget = null;
            }
            if (readbackTexture != null) {
                UnityEngine.Object.Destroy(readbackTexture);
                readbackTexture = null;
            }
        }

        private void PollReadback() {
            if (!pendingX.IsReadbackRequestDone() || !pendingY.IsReadbackRequestDone() || !pendingZ.IsReadbackRequestDone()) {
                return;
            }
            Tensor<float> cpuX = pendingX.ReadbackAndClone();
            Tensor<float> cpuY = pendingY.ReadbackAndClone();
            Tensor<float> cpuZ = pendingZ.ReadbackAndClone();
            try {
                DecodeFrame(cpuX.DownloadToArray(), cpuY.DownloadToArray(), cpuZ.DownloadToArray(), frameCounter * 0.033);
                hasNewFrame = true;
            } catch (Exception exception) {
                logService.LogException(exception, "Sentis pose decode failed");
            } finally {
                cpuX.Dispose();
                cpuY.Dispose();
                cpuZ.Dispose();
                inFlight = false;
            }
        }

        // Resize the camera frame to 288x384 and fill the NCHW input buffer (RGB), top-down, normalized.
        // Uses a GPU blit for the resize then a small CPU readback — cheap at 288x384. Preprocessing here is
        // convention-sensitive (normalization + top-down flip): tune against a real webcam.
        private bool BuildInput(Texture sourceTexture) {
            Graphics.Blit(sourceTexture, resizeTarget);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resizeTarget;
            readbackTexture.ReadPixels(new Rect(0, 0, InputWidth, InputHeight), 0, 0, false);
            readbackTexture.Apply(false);
            RenderTexture.active = previous;

            Color32[] pixels = readbackTexture.GetPixels32();
            if (pixels == null || pixels.Length < InputWidth * InputHeight) {
                return false;
            }
            int plane = InputHeight * InputWidth;
            int y = 0;
            while (y < InputHeight) {
                int srcRow = (InputHeight - 1 - y) * InputWidth; // flip: ReadPixels origin is bottom-left
                int dstRow = y * InputWidth;
                int x = 0;
                while (x < InputWidth) {
                    Color32 p = pixels[srcRow + x];
                    float r = p.r;
                    float g = p.g;
                    float b = p.b;
                    if (applyImageNetNorm) {
                        r = (r - NormMean.x) / NormStd.x;
                        g = (g - NormMean.y) / NormStd.y;
                        b = (b - NormMean.z) / NormStd.z;
                    } else {
                        r = r / 255f;
                        g = g / 255f;
                        b = b / 255f;
                    }
                    int cell = dstRow + x;
                    inputBuffer[cell] = r;
                    inputBuffer[plane + cell] = g;
                    inputBuffer[2 * plane + cell] = b;
                    x = x + 1;
                }
                y = y + 1;
            }
            return true;
        }

        private void DecodeFrame(float[] simccX, float[] simccY, float[] simccZ, double timestampSeconds) {
            // First pass: decode every mapped keypoint into image/depth space and find the mid-hip anchor.
            Vector3 midHip = Vector3.zero;
            bool hasHip = false;
            Vector3 leftHip = Vector3.zero;
            Vector3 rightHip = Vector3.zero;
            bool hasLeftHip = false;
            bool hasRightHip = false;

            // Reset all 33 slots to zero-confidence, then fill mapped ones.
            int slot = 0;
            while (slot < PoseFrame.LandmarkCount) {
                workerFrame.SetLandmark(slot, new PoseLandmark(Vector3.zero, 0f));
                slot = slot + 1;
            }

            // Decode raw (image-space) points for the mapped keypoints into a scratch, so we can hip-centre.
            int k = 0;
            while (k < Keypoints) {
                int joint = CocoToJoint[k];
                if (joint >= 0) {
                    float xConf;
                    float xi = ArgMax(simccX, k * XBins, XBins, out xConf);
                    float yi = ArgMax(simccY, k * YBins, YBins, out _);
                    float zi = ArgMax(simccZ, k * ZBins, ZBins, out _);
                    Vector3 raw = new Vector3(xi / SimccSplitRatio, yi / SimccSplitRatio, zi / SimccSplitRatio);
                    // Store temporarily in the slot's position (image space); confidence from x peak.
                    workerFrame.SetLandmark(joint, new PoseLandmark(raw, Mathf.Clamp01(xConf)));
                    if (k == 11) {
                        leftHip = raw;
                        hasLeftHip = true;
                    } else if (k == 12) {
                        rightHip = raw;
                        hasRightHip = true;
                    }
                }
                k = k + 1;
            }
            if (hasLeftHip && hasRightHip) {
                midHip = 0.5f * (leftHip + rightHip);
                hasHip = true;
            }

            // Second pass: convert each mapped slot from image space to hip-centred Unity metres.
            slot = 0;
            while (slot < PoseFrame.LandmarkCount) {
                PoseLandmark landmark = workerFrame.GetLandmark((JointId)slot);
                if (landmark.Confidence > 0f) {
                    Vector3 image = landmark.Position;
                    Vector3 centred = hasHip ? (image - midHip) : image;
                    // Normalize by input height so units are ~[-1,1], then to metres via metreScale, and map
                    // axes through the shared PoseSpaceConverter (mirror + Y/Z sign) for parity with MediaPipe.
                    float nx = centred.x / InputHeight;
                    float ny = centred.y / InputHeight;
                    float nz = centred.z / InputHeight;
                    Vector3 unity = converter.ToUnity(nx, ny, nz) * metreScale;
                    workerFrame.SetLandmark(slot, new PoseLandmark(unity, landmark.Confidence));
                }
                slot = slot + 1;
            }

            workerFrame.SetMeta(timestampSeconds, true);
        }

        private static float ArgMax(float[] data, int offset, int count, out float peak) {
            int best = 0;
            float bestValue = data[offset];
            int i = 1;
            while (i < count) {
                float value = data[offset + i];
                if (value > bestValue) {
                    bestValue = value;
                    best = i;
                }
                i = i + 1;
            }
            peak = bestValue;
            return best;
        }

        private static int[] BuildMapping() {
            int[] map = new int[Keypoints];
            int i = 0;
            while (i < Keypoints) {
                map[i] = -1;
                i = i + 1;
            }
            // COCO-WholeBody body (0..16) + feet (17..22) -> BlazePose JointId.
            map[0] = (int)JointId.Nose;
            map[1] = (int)JointId.LeftEye;
            map[2] = (int)JointId.RightEye;
            map[3] = (int)JointId.LeftEar;
            map[4] = (int)JointId.RightEar;
            map[5] = (int)JointId.LeftShoulder;
            map[6] = (int)JointId.RightShoulder;
            map[7] = (int)JointId.LeftElbow;
            map[8] = (int)JointId.RightElbow;
            map[9] = (int)JointId.LeftWrist;
            map[10] = (int)JointId.RightWrist;
            map[11] = (int)JointId.LeftHip;
            map[12] = (int)JointId.RightHip;
            map[13] = (int)JointId.LeftKnee;
            map[14] = (int)JointId.RightKnee;
            map[15] = (int)JointId.LeftAnkle;
            map[16] = (int)JointId.RightAnkle;
            map[17] = (int)JointId.LeftFootIndex;  // left_big_toe
            map[19] = (int)JointId.LeftHeel;
            map[20] = (int)JointId.RightFootIndex; // right_big_toe
            map[22] = (int)JointId.RightHeel;
            return map;
        }
    }
}
