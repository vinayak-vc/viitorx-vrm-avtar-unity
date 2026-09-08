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
    /// (ADR-016/ADR-018, Option B2). The CANONICAL sidecar is
    /// <c>python-sidecar~/wholebody_udp_sender.py</c> (RTMW3D whole-body + measured depth): it streams JSON
    /// <c>{ "lm":[[x,y,z,vis]×33], "lh":[[x,y,z]×21]?, "rh":[…]?, "xyz":[mm×3], "src":[…] }</c> over local
    /// UDP. (<c>depthai_blazepose/udp_pose_sender.py</c> is the DEPRECATED body-only BlazePose fallback —
    /// it sends only <c>lm</c>+<c>xyz</c>, so hands never track with it.) This provider only reads the
    /// socket — no native DepthAI DLL in-process, so DepthAI instability can never crash the editor.
    ///
    /// <c>lm</c> is JointId order (index maps 1:1); optional <c>lh</c>/<c>rh</c> feed the companion
    /// <see cref="OakDUdpHandProvider"/>. Landmarks are hip-relative metres run through the shared
    /// <see cref="PoseSpaceConverter"/>. Reads happen on a background thread; frames are double-buffered.
    /// </summary>
    public sealed class OakDUdpPoseProvider : IBodyTrackingProvider {
        private const int NumKeypoints = 33;

        // Per-hand finger joint triples [proximal, middle, tip] for the curl angle, and the palm-basis
        // indices — the sidecar's lh/rh arrays are MediaPipe/COCO-WholeBody hand order (0 wrist,
        // 1-4 thumb, 5-8 index, 9-12 middle, 13-16 ring, 17-20 pinky), so this matches MediaPipeHandProvider.
        private static readonly int[] ThumbJoints = new int[] { 2, 3, 4 };
        private static readonly int[] IndexJoints = new int[] { 5, 6, 8 };
        private static readonly int[] MiddleJoints = new int[] { 9, 10, 12 };
        private static readonly int[] RingJoints = new int[] { 13, 14, 16 };
        private static readonly int[] LittleJoints = new int[] { 17, 18, 20 };
        private const int HandLandmarkCount = 21;
        private const float MaxBendAngle = 160f;

        private readonly ILogService logService;
        private readonly PoseSpaceConverter converter;
        private readonly int port;

        private readonly object gate = new object();
        private UdpClient udpClient;
        private Thread receiveThread;
        private volatile bool running;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();

        // P1-3: timestamped pose buffer + interpolation. Replaces latest-wins with temporal
        // reconstruction at (now - interpolationDelay). Delay 0 => original behaviour, exactly.
        private readonly PoseBuffer poseBuffer = new PoseBuffer(16);
        private readonly PoseFrame interpolatedFrame = new PoseFrame();
        private bool interpolationEnabled;
        private double interpolationDelaySeconds;
        private long interpAggregateCounter;
        private const int InterpLogEvery = 600;   // periodic aggregate only, never per render frame
        private double lastPacketAgeMs;
        // M9: volatile so the worker's ParseInto writes and the consumer's lock-guarded reference swap of
        // these buffers have consistent cross-thread visibility (no torn read of the in-flight frame).
        private volatile PoseFrame workerFrame;
        private volatile PoseFrame mainFrame;
        private bool hasNewFrame;
        private bool hasAnyFrame;
        // Hand frames from the same datagram (lh/rh) — one socket feeds both body and hands. Published
        // to a companion OakDUdpHandProvider facade via TryGetHandFrame. Double-buffered like the pose.
        private volatile HandFrame workerHandFrame;
        private volatile HandFrame mainHandFrame;
        private bool hasNewHand;
        private bool hasAnyHand;
        private long framesReceived;
        private long parseErrors;
        private bool handsSeen;      // M17: warn once if the stream never carries lh/rh (old body-only sender?)
        private bool handsWarned;
        private const int LogEveryFrames = 90;

        // Pipeline logging (diagnostics): when a dir is set (SetPipelineLog, before StartTracking), append one
        // recv_log.jsonl line per received datagram — seq + key converted landmarks + the derived palm
        // quaternions — to diff against the sidecar's sender_log.jsonl and Unity's model_log.jsonl via
        // compare_logs.py. Written on the receive thread only. Off unless a dir is set.
        private string pipelineLogDir;
        private System.IO.StreamWriter pipelineLog;
        private long lastSeq = -1;

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

        // Diagnostics for live bring-up: datagrams received and parse failures. Read on the main thread
        // (e.g. HUD) while the worker updates them via Interlocked.
        public long ReceivedCount {
            get {
                return Interlocked.Read(ref framesReceived);
            }
        }

        public long ParseErrorCount {
            get {
                return Interlocked.Read(ref parseErrors);
            }
        }

        // Seq of the most recently parsed datagram (aligns the model-stage log). -1 until the first frame.
        public long LastSeq {
            get {
                return Interlocked.Read(ref lastSeq);
            }
        }

        // Enable pipeline logging into <dir>/recv_log.jsonl. Call BEFORE StartTracking.
        public void SetPipelineLog(string dir) {
            pipelineLogDir = dir;
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
                workerHandFrame = new HandFrame();
                mainHandFrame = new HandFrame();
                hasNewHand = false;
                hasAnyHand = false;
                clock.Restart(); // LOW-C: real monotonic arrival timestamps (was a fixed 33 ms/frame assumption)
                if (!string.IsNullOrEmpty(pipelineLogDir)) {
                    try {
                        System.IO.Directory.CreateDirectory(pipelineLogDir);
                        pipelineLog = new System.IO.StreamWriter(System.IO.Path.Combine(pipelineLogDir, "recv_log.jsonl"), false);
                        pipelineLog.AutoFlush = true;
                        logService.Log(LogLevel.Info, "OAK-D pipeline logging -> " + System.IO.Path.Combine(pipelineLogDir, "recv_log.jsonl"));
                    } catch (Exception logEx) {
                        logService.LogException(logEx, "OAK-D pipeline log open failed");
                        pipelineLog = null;
                    }
                }
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
            // LOW-C: close the socket + join the thread (not just running=false) so a Stop→Start cycle
            // without Dispose does not hit "port in use" / two receive loops. Idempotent with Dispose.
            Teardown();
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
                    long received = Interlocked.Increment(ref framesReceived);
                    ParseInto(workerFrame, json, clock.Elapsed.TotalSeconds);
                    lock (gate) {
                        hasNewFrame = true;
                        hasNewHand = true;
                    }
                    if (received <= 3 || received % LogEveryFrames == 0) {
                        logService.Log(LogLevel.Info, "OAK-D UDP rx=" + received + " parseErr=" + Interlocked.Read(ref parseErrors) + " (port " + port + ").");
                    }
                    // M17: healthy stream but NEVER any hand data → warn once (likely the deprecated
                    // body-only BlazePose sender, or hands simply never in view).
                    if (!handsSeen && !handsWarned && received >= 150) {
                        handsWarned = true;
                        logService.Log(LogLevel.Warning, "OAK-D UDP: 150+ datagrams with no lh/rh — fingers will not track. Run wholebody_udp_sender.py (not the deprecated blazepose sender) and keep hands in view.");
                    }
                } catch (SocketException) {
                    // Receive timeout (no sidecar data yet) — keep waiting.
                } catch (ObjectDisposedException) {
                    return; // socket closed on Teardown
                } catch (Exception exception) {
                    long errs = Interlocked.Increment(ref parseErrors);
                    if (errs <= 3 || errs % LogEveryFrames == 0) {
                        logService.LogException(exception, "OAK-D UDP receive/parse failed (parseErr=" + errs + ")");
                    }
                }
            }
        }

        /// <summary>P1-3: enable temporal interpolation and set the presentation delay. A delay of 0
        /// (or negative) restores the original latest-wins behaviour exactly.</summary>
        public void SetPoseInterpolation(float delayMilliseconds) {
            bool enable = delayMilliseconds > 0.01f;
            lock (gate) {
                interpolationDelaySeconds = delayMilliseconds / 1000.0;
                if (enable != interpolationEnabled) {
                    poseBuffer.Clear();
                }
                interpolationEnabled = enable;
            }
        }

        /// <summary>P1-3 diagnostics snapshot (depth, seq pair, alpha, packet age ms).</summary>
        public void GetBufferDiagnostics(out int depth, out long seqA, out long seqB,
                                         out float alpha, out double packetAgeMs) {
            lock (gate) {
                depth = poseBuffer.Depth;
                seqA = poseBuffer.LastSeqA;
                seqB = poseBuffer.LastSeqB;
                alpha = poseBuffer.LastAlpha;
                packetAgeMs = lastPacketAgeMs;
            }
        }

        public bool TryGetLatestFrame(out PoseFrame latest) {
            if (interpolationEnabled) {
                bool sampled;
                int dDepth = 0; long dA = -1; long dB = -1; float dAlpha = 0f; double dAge = -1.0;
                lock (gate) {
                    double now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    double renderTime = now - interpolationDelaySeconds;
                    sampled = poseBuffer.Sample(renderTime, interpolatedFrame);
                    if (sampled) {
                        double newest = poseBuffer.NewestTime();
                        lastPacketAgeMs = newest > 0.0 ? (now - newest) * 1000.0 : -1.0;
                        dDepth = poseBuffer.Depth;
                        dA = poseBuffer.LastSeqA;
                        dB = poseBuffer.LastSeqB;
                        dAlpha = poseBuffer.LastAlpha;
                        dAge = lastPacketAgeMs;
                    }
                    hasNewFrame = false;   // the buffer is the source of truth while interpolating
                }
                latest = interpolatedFrame;
                if (sampled) {
                    interpAggregateCounter = interpAggregateCounter + 1;
                    if (logService != null && interpAggregateCounter % InterpLogEvery == 0) {
                        logService.Log(LogLevel.Info,
                            "[P1-3] poseBufferDepth=" + dDepth
                            + " interpDelayMs=" + (interpolationDelaySeconds * 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                            + " sourceSeqA=" + dA + " sourceSeqB=" + dB
                            + " alpha=" + dAlpha.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            + " packetAgeMs=" + dAge.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                            + " interp=" + poseBuffer.SamplesInterpolated
                            + " clampNewest=" + poseBuffer.SamplesClampedNewest
                            + " dupRej=" + poseBuffer.RejectedDuplicate
                            + " oooRej=" + poseBuffer.RejectedOutOfOrder);
                    }
                }
                return sampled;
            }

            bool available;
            lock (gate) {
                if (hasNewFrame) {
                    PoseFrame temp = mainFrame;
                    mainFrame = workerFrame;
                    workerFrame = temp;
                    hasNewFrame = false;
                    hasAnyFrame = true;
                }
                available = hasAnyFrame;
            }
            latest = mainFrame;
            return available;
        }

        // Companion hand stream (fingers) parsed from the same datagram's lh/rh arrays. The
        // OakDUdpHandProvider facade delegates here so a single socket feeds body + hands.
        public bool TryGetHandFrame(out HandFrame latest) {
            bool available;
            lock (gate) {
                if (hasNewHand) {
                    HandFrame temp = mainHandFrame;
                    mainHandFrame = workerHandFrame;
                    workerHandFrame = temp;
                    hasNewHand = false;
                    hasAnyHand = true;
                }
                available = hasAnyHand;
            }
            latest = mainHandFrame;
            return available;
        }

        public void Dispose() {
            Teardown();
        }

        private void Teardown() {
            running = false;
            if (udpClient != null) {
                udpClient.Close(); // unblocks a Receive parked in the worker
                udpClient = null;
            }
            if (receiveThread != null && receiveThread != Thread.CurrentThread) {
                receiveThread.Join(1000);
                receiveThread = null;
            }
            if (pipelineLog != null) {
                try {
                    pipelineLog.Flush();
                    pipelineLog.Dispose();
                } catch (Exception) {
                }
                pipelineLog = null;
            }
        }

        // Parse one UDP JSON datagram: { "lm": [[x,y,z,vis], ... 33], "xyz": [..] }. lm are BlazePose-33
        // world landmarks (metres, hip-relative) → 1:1 with JointId → through the converter → PoseFrame.
        private void ParseInto(PoseFrame target, string json, double timestampSeconds) {
            if (string.IsNullOrEmpty(json)) {
                target.MarkInvalid(timestampSeconds);
                workerHandFrame.MarkInvalid(timestampSeconds);
                return;
            }
            JObject root = JObject.Parse(json);
            long seq = root["seq"] != null ? root["seq"].Value<long>() : -1;
            Interlocked.Exchange(ref lastSeq, seq);
            // DIAG-ONLY (P0 acceptance §16): the sidecar's SEND epoch seconds, echoed so latency can be
            // computed against a comparable clock. `clock.Elapsed` is a Stopwatch since provider start and
            // is NOT comparable to the sidecar's time.time(). Read-only; drives no behavior.
            double sendEpoch = root["t"] != null ? root["t"].Value<double>() : 0.0;
            JArray landmarks = root["lm"] as JArray;
            if (landmarks == null || landmarks.Count == 0) {
                target.MarkInvalid(timestampSeconds);
                workerHandFrame.MarkInvalid(timestampSeconds);
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
                // "xyz" = the OAK-D SpatialLocationCalculator's measured mid-hip position (millimetres, camera
                // space: X right, Y down, Z forward). Run it through the same converter (axis/mirror parity)
                // and hand it to the frame as a world anchor so the avatar can translate with the user.
                JArray xyz = root["xyz"] as JArray;
                if (xyz != null && xyz.Count >= 3) {
                    float hipX = xyz[0].Value<float>();
                    float hipY = xyz[1].Value<float>();
                    float hipZ = xyz[2].Value<float>();
                    Vector3 rootMetres = converter.ToUnityPosition(hipX / 1000f, hipY / 1000f, hipZ / 1000f);
                    target.SetRootPosition(rootMetres);
                } else {
                    target.ClearRootPosition();
                }
                target.SetMeta(timestampSeconds, true);
            } else {
                target.MarkInvalid(timestampSeconds);
            }

            ParseHands(root, timestampSeconds);
            // P1-3: buffer this pose on the RECEIVE clock (Unity's own epoch, so no sender-skew
            // assumption). Duplicate / out-of-order / backwards-timestamp packets are rejected inside.
            if (interpolationEnabled && target.IsValid) {
                double recvEpoch = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                lock (gate) {
                    poseBuffer.Push(target, seq, recvEpoch);
                }
            }
            if (pipelineLog != null) {
                WriteRecvLog(target, workerHandFrame, seq, timestampSeconds, sendEpoch);
            }
        }

        // DIAG-ONLY: mirrors KalidokitControlRigDriver.Min3 so the logged confidence matches the gate's input.
        private static float Min3(float a, float b, float c) {
            return Mathf.Min(a, Mathf.Min(b, c));
        }

        private static string F(float v) {
            return v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        // One recv_log.jsonl line: seq + key CONVERTED landmarks (shoulders/hips/wrists, Unity space) + the
        // derived palm quaternions. Diff against sender_log.jsonl (should match modulo the axis convention)
        // and model_log.jsonl (to localize where jitter/spin enters). Receive-thread only.
        private void WriteRecvLog(PoseFrame t, HandFrame h, long seq, double ts, double sendEpoch) {
            try {
                Vector3 sL = t.GetLandmark((JointId)11).Position, sR = t.GetLandmark((JointId)12).Position;
                Vector3 hL = t.GetLandmark((JointId)23).Position, hR = t.GetLandmark((JointId)24).Position;
                Vector3 wL = t.GetLandmark((JointId)15).Position, wR = t.GetLandmark((JointId)16).Position;
                Vector3 eL = t.GetLandmark((JointId)13).Position, eR = t.GetLandmark((JointId)14).Position;
                // DIAG-ONLY (P0 acceptance §9): knees/ankles were never logged, so leg frame-to-frame
                // displacement could not be measured. Read-only.
                Vector3 kL = t.GetLandmark((JointId)25).Position, kR = t.GetLandmark((JointId)26).Position;
                Vector3 aL = t.GetLandmark((JointId)27).Position, aR = t.GetLandmark((JointId)28).Position;
                // DIAG-ONLY (P0 acceptance §6/§10): the EXACT four per-limb confidences LimbGate consumes,
                // using the same Kalidokit cross-map as KalidokitControlRigDriver.Apply. Logging these makes
                // the Unity gate's HOLD/VALID decision reproducible offline from the log alone.
                float cLArm = Min3(t.GetLandmark((JointId)12).Confidence, t.GetLandmark((JointId)14).Confidence, t.GetLandmark((JointId)16).Confidence);
                float cRArm = Min3(t.GetLandmark((JointId)11).Confidence, t.GetLandmark((JointId)13).Confidence, t.GetLandmark((JointId)15).Confidence);
                float cLLeg = Min3(t.GetLandmark((JointId)24).Confidence, t.GetLandmark((JointId)26).Confidence, t.GetLandmark((JointId)28).Confidence);
                float cRLeg = Min3(t.GetLandmark((JointId)23).Confidence, t.GetLandmark((JointId)25).Confidence, t.GetLandmark((JointId)27).Confidence);
                double recvEpoch = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                Quaternion lp = h.LeftWristRotation, rp = h.RightWristRotation;
                string line = "{\"seq\":" + seq + ",\"t\":" + ts.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"sh\":[[" + F(sL.x) + "," + F(sL.y) + "," + F(sL.z) + "],[" + F(sR.x) + "," + F(sR.y) + "," + F(sR.z) + "]]"
                    + ",\"el\":[[" + F(eL.x) + "," + F(eL.y) + "," + F(eL.z) + "],[" + F(eR.x) + "," + F(eR.y) + "," + F(eR.z) + "]]"
                    + ",\"hip\":[[" + F(hL.x) + "," + F(hL.y) + "," + F(hL.z) + "],[" + F(hR.x) + "," + F(hR.y) + "," + F(hR.z) + "]]"
                    + ",\"wr\":[[" + F(wL.x) + "," + F(wL.y) + "," + F(wL.z) + "],[" + F(wR.x) + "," + F(wR.y) + "," + F(wR.z) + "]]"
                    + ",\"lpalm\":[" + F(lp.x) + "," + F(lp.y) + "," + F(lp.z) + "," + F(lp.w) + "],\"ltrk\":" + (h.LeftWristTracked ? "true" : "false")
                    + ",\"rpalm\":[" + F(rp.x) + "," + F(rp.y) + "," + F(rp.z) + "," + F(rp.w) + "],\"rtrk\":" + (h.RightWristTracked ? "true" : "false")
                    // DIAG-ONLY (P0 acceptance): legs + per-limb gate confidence + comparable epoch clocks.
                    + ",\"kn\":[[" + F(kL.x) + "," + F(kL.y) + "," + F(kL.z) + "],[" + F(kR.x) + "," + F(kR.y) + "," + F(kR.z) + "]]"
                    + ",\"an\":[[" + F(aL.x) + "," + F(aL.y) + "," + F(aL.z) + "],[" + F(aR.x) + "," + F(aR.y) + "," + F(aR.z) + "]]"
                    + ",\"cf\":{\"lArm\":" + F(cLArm) + ",\"rArm\":" + F(cRArm) + ",\"lLeg\":" + F(cLLeg) + ",\"rLeg\":" + F(cRLeg) + "}"
                    + ",\"tSend\":" + sendEpoch.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"tRecv\":" + recvEpoch.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + "}";
                pipelineLog.WriteLine(line);
            } catch (Exception) {
            }
        }

        // ADR-021: temporal smoothing state for the re-enabled wrist palm basis (worker-thread only). The
        // raw palm quaternion from distant hand landmarks jitters/spins (ADR-018); slerp-smoothing it before
        // streaming keeps the wrist responsive without the spin.
        private const float WristSmoothing = 0.15f;   // slerp factor toward the new palm each frame — LOWERED
                                                       // from 0.35 (live logs: recv palm jittered ~10 deg/frame,
                                                       // spikes to 150 deg) so the wrist is steady, not spinning.
        private const float WristMaxStepDeg = 15f;     // rate-limit: cap the per-frame palm step so a depth-spike
                                                       // (~150 deg) can't snap the wrist. Rate-limited, never held.
        private Quaternion smoothedLeftWrist = Quaternion.identity;
        private Quaternion smoothedRightWrist = Quaternion.identity;
        private bool hasLeftWrist;
        private bool hasRightWrist;

        // Parse the sidecar's lh/rh arrays (21 x [x,y,z] hip-relative metres each) into the hand frame:
        // per-finger curl from the bend angle at each finger's middle joint (angle is invariant to the
        // converter's axis flips, so raw points are used), and a palm orientation run through the shared
        // PoseSpaceConverter for axis/mirror parity with the body (applied delta-from-neutral downstream).
        // Damp the palm quaternion: slerp toward the new value by WristSmoothing, but cap the per-frame step
        // at WristMaxStepDeg so a depth-spike (palm jumping ~150 deg between frames) can't snap the wrist.
        // Rate-limited rather than held, so it can never freeze on a persistent spike (ADR-018 lesson).
        private static Quaternion SmoothWristQuat(Quaternion smoothed, Quaternion target) {
            float angle = Quaternion.Angle(smoothed, target);
            float t = WristSmoothing;
            if (angle > 1e-3f) {
                t = Mathf.Min(t, WristMaxStepDeg / angle);
            }
            return Quaternion.Slerp(smoothed, target, t);
        }

        private void ParseHands(JObject root, double timestampSeconds) {
            Vector3[] left = ReadHand(root["lh"] as JArray);
            Vector3[] right = ReadHand(root["rh"] as JArray);
            if (left == null && right == null) {
                workerHandFrame.MarkInvalid(timestampSeconds);
                return;
            }

            float lThumb = 0f, lIndex = 0f, lMiddle = 0f, lRing = 0f, lLittle = 0f;
            float rThumb = 0f, rIndex = 0f, rMiddle = 0f, rRing = 0f, rLittle = 0f;
            Quaternion lWrist = Quaternion.identity;
            Quaternion rWrist = Quaternion.identity;
            bool lTracked = false;
            bool rTracked = false;

            if (left != null) {
                lThumb = FingerCurl(left, ThumbJoints);
                lIndex = FingerCurl(left, IndexJoints);
                lMiddle = FingerCurl(left, MiddleJoints);
                lRing = FingerCurl(left, RingJoints);
                lLittle = FingerCurl(left, LittleJoints);
                lWrist = PalmRotation(left);
                lTracked = true;
            }
            if (right != null) {
                rThumb = FingerCurl(right, ThumbJoints);
                rIndex = FingerCurl(right, IndexJoints);
                rMiddle = FingerCurl(right, MiddleJoints);
                rRing = FingerCurl(right, RingJoints);
                rLittle = FingerCurl(right, LittleJoints);
                rWrist = PalmRotation(right);
                rTracked = true;
            }

            handsSeen = true; // M17: at least one hand carried data → the stream is the whole-body sender
            workerHandFrame.SetCurls(lThumb, lIndex, lMiddle, lRing, lLittle, rThumb, rIndex, rMiddle, rRing, rLittle);
            // ADR-021: wrist rotation RE-ENABLED on the OAK path (was disabled in ADR-018 because the raw
            // palm basis spun at a distance). The palm quaternion is temporally slerp-smoothed here so it
            // stays responsive without spinning; downstream it is applied delta-from-neutral and blended by
            // the live-tunable wristRotationWeight (set the slider/field to 0 to disable). Best ~2 m framing.
            if (lTracked) {
                smoothedLeftWrist = hasLeftWrist ? SmoothWristQuat(smoothedLeftWrist, lWrist) : lWrist;
                hasLeftWrist = true;
            } else {
                hasLeftWrist = false;
            }
            if (rTracked) {
                smoothedRightWrist = hasRightWrist ? SmoothWristQuat(smoothedRightWrist, rWrist) : rWrist;
                hasRightWrist = true;
            } else {
                hasRightWrist = false;
            }
            workerHandFrame.SetWristRotations(smoothedLeftWrist, lTracked, smoothedRightWrist, rTracked);
            workerHandFrame.SetMeta(timestampSeconds, lTracked || rTracked);
        }

        // Read 21 [x,y,z] hand landmarks; null if absent/short.
        private static Vector3[] ReadHand(JArray hand) {
            if (hand == null || hand.Count < HandLandmarkCount) {
                return null;
            }
            Vector3[] points = new Vector3[HandLandmarkCount];
            int i = 0;
            while (i < HandLandmarkCount) {
                JArray point = hand[i] as JArray;
                if (point == null || point.Count < 3) {
                    return null;
                }
                points[i] = new Vector3(point[0].Value<float>(), point[1].Value<float>(), point[2].Value<float>());
                i = i + 1;
            }
            return points;
        }

        private static float FingerCurl(Vector3[] points, int[] joints) {
            Vector3 lower = points[joints[1]] - points[joints[0]];
            Vector3 upper = points[joints[2]] - points[joints[1]];
            if (lower.sqrMagnitude < 1e-10f || upper.sqrMagnitude < 1e-10f) {
                return 0f;
            }
            float angle = Vector3.Angle(lower, upper);
            return Mathf.Clamp01(angle / MaxBendAngle);
        }

        // Palm basis: forward = wrist(0) -> middleMcp(9), up = forward x (indexMcp(5) -> pinkyMcp(17)).
        // Points converted with the shared PoseSpaceConverter so the palm quaternion matches the body axes.
        private Quaternion PalmRotation(Vector3[] points) {
            Vector3 wrist = converter.ToUnity(points[0].x, points[0].y, points[0].z);
            Vector3 indexMcp = converter.ToUnity(points[5].x, points[5].y, points[5].z);
            Vector3 middleMcp = converter.ToUnity(points[9].x, points[9].y, points[9].z);
            Vector3 pinkyMcp = converter.ToUnity(points[17].x, points[17].y, points[17].z);
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
    }
}
