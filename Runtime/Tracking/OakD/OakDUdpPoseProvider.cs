using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using UnityEngine;

using Newtonsoft.Json.Linq;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// <see cref="IBodyTrackingProvider"/> for the OAK-D depth camera via an EXTERNAL Python sidecar
    /// (ADR-016, Option B2). The sidecar (<c>oak_sidecar/depthai_blazepose/udp_pose_sender.py</c>) runs
    /// BlazePose on the OAK-D and streams the 33 body landmarks as JSON over local UDP. This provider only
    /// reads that socket — there is NO native DepthAI DLL in the Unity process, so DepthAI instability can
    /// never crash the editor (unlike the abandoned in-Unity plugin, which crashed on the native teardown).
    ///
    /// The 33 landmarks are BlazePose order == our <see cref="JointId"/> order, so index maps 1:1. The
    /// sidecar sends <c>landmarks_world</c> (metres, hip-relative, GHUM) — the same convention as the
    /// MediaPipe world-landmark path — so they run through the shared <see cref="PoseSpaceConverter"/> and
    /// the retargeter consumes them exactly like the MediaPipe provider. Reads happen on a background thread;
    /// the <see cref="PoseFrame"/> is double-buffered like <c>MediaPipePoseProvider</c>.
    /// </summary>
    public sealed class OakDUdpPoseProvider : IBodyTrackingProvider {
        private const int NumKeypoints = 33;

        private readonly ILogService logService;
        private readonly PoseSpaceConverter converter;
        private readonly int port;

        private readonly object gate = new object();
        private UdpClient udpClient;
        private Thread receiveThread;
        private volatile bool running;
        private PoseFrame workerFrame;
        private PoseFrame mainFrame;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        private long frameCounter;

        public OakDUdpPoseProvider(ILogService logService, PoseSpaceConverter converter, int port) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            if (converter == null) {
                throw new ArgumentNullException(nameof(converter));
            }
            this.logService = logService;
            this.converter = converter;
            this.port = port;
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
                udpClient = new UdpClient(port);
                udpClient.Client.ReceiveTimeout = 1000;
                workerFrame = new PoseFrame();
                mainFrame = new PoseFrame();
                hasNewFrame = false;
                hasAnyFrame = false;
                running = true;
                receiveThread = new Thread(ReceiveLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();
                logService.Log(LogLevel.Info, "OAK-D UDP pose provider listening on 127.0.0.1:" + port + " (run the Python sidecar udp_pose_sender.py).");
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to start OAK-D UDP pose provider (port " + port + " in use?)");
                running = false;
            }
        }

        public void StopTracking() {
            running = false;
        }

        public void Tick(float deltaSeconds) {
            // No-op: UDP receive runs on the background thread; TryGetLatestFrame publishes results.
        }

        private void ReceiveLoop() {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (running) {
                try {
                    byte[] data = udpClient.Receive(ref remote);
                    if (data == null || data.Length == 0) {
                        continue;
                    }
                    string json = Encoding.UTF8.GetString(data);
                    frameCounter = frameCounter + 1;
                    ParseInto(workerFrame, json, frameCounter * 0.033);
                    lock (gate) {
                        hasNewFrame = true;
                    }
                } catch (SocketException) {
                    // Receive timeout (no sidecar data yet) — keep waiting.
                } catch (ObjectDisposedException) {
                    return; // socket closed on Dispose
                } catch (Exception exception) {
                    logService.LogException(exception, "OAK-D UDP receive/parse failed");
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
            if (udpClient != null) {
                udpClient.Close(); // unblocks a Receive parked in the worker
                udpClient = null;
            }
            if (receiveThread != null) {
                receiveThread.Join(1000);
                receiveThread = null;
            }
        }

        // Parse one UDP JSON datagram: { "lm": [[x,y,z,vis], ... 33], "xyz": [..] }. lm are BlazePose-33
        // world landmarks (metres, hip-relative) → 1:1 with JointId → through the converter → PoseFrame.
        private void ParseInto(PoseFrame target, string json, double timestampSeconds) {
            if (string.IsNullOrEmpty(json)) {
                target.MarkInvalid(timestampSeconds);
                return;
            }
            JObject root = JObject.Parse(json);
            JArray landmarks = root["lm"] as JArray;
            if (landmarks == null || landmarks.Count == 0) {
                target.MarkInvalid(timestampSeconds);
                return;
            }

            int slot = 0;
            while (slot < PoseFrame.LandmarkCount) {
                target.SetLandmark(slot, new PoseLandmark(Vector3.zero, 0f));
                slot = slot + 1;
            }

            int count = landmarks.Count < NumKeypoints ? landmarks.Count : NumKeypoints;
            int mapped = 0;
            int i = 0;
            while (i < count) {
                JArray point = landmarks[i] as JArray;
                if (point != null && point.Count >= 4) {
                    float x = point[0].Value<float>();
                    float y = point[1].Value<float>();
                    float z = point[2].Value<float>();
                    float confidence = point[3].Value<float>();
                    Vector3 unity = converter.ToUnity(x, y, z);
                    target.SetLandmark(i, new PoseLandmark(unity, Mathf.Clamp01(confidence)));
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
    }
}
