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

        public bool TryGetLatestFrame(out PoseFrame latest) {
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
        }

        // Parse the sidecar's lh/rh arrays (21 x [x,y,z] hip-relative metres each) into the hand frame:
        // per-finger curl from the bend angle at each finger's middle joint (angle is invariant to the
        // converter's axis flips, so raw points are used), and a palm orientation run through the shared
        // PoseSpaceConverter for axis/mirror parity with the body (applied delta-from-neutral downstream).
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
            // Wrist rotation DISABLED (tracked=false): the palm basis from RTMW3D hand landmarks at a
            // distance is too noisy and made the avatar wrist spin continuously. Fingers still curl. The
            // palm is still computed above so this can be re-enabled (pass lTracked/rTracked) once hand
            // landmarks are stable — close framing + measured hand depth.
            workerHandFrame.SetWristRotations(lWrist, false, rWrist, false);
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
