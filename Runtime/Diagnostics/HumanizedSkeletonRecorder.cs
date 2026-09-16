using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Core.Humanize;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-27 — records BEFORE and AFTER from the SAME frame, in one pass.
    ///
    /// WHY ONE PASS. The obvious way to measure a correction layer is to run a session with it off and
    /// another with it on. That compares two different takes and calls the difference an improvement.
    /// Here the raw filtered pose and the humanized pose arrive together through
    /// <c>AppBootstrap.HumanizeTap</c>, are measured by the SAME <see cref="PoseMetrics"/> code, and are
    /// written to the same line — so every difference in the report is attributable to the layer and to
    /// nothing else.
    ///
    /// WHAT IS RECORDED, one JSON object per applied frame, matching the six things the layer claims to
    /// do: the biggest landmark jump since the previous frame, the worst bone-length error against that
    /// stream's OWN running median, the four hinge angles, the closest a non-hip joint came to the
    /// mid-hip, and the non-finite count. Plus the layer's own per-frame counters, so a claim can be
    /// cross-checked against the stage that supposedly caused it.
    ///
    /// DIAGNOSTIC ONLY. Reads two frames and writes a file; it drives nothing.
    /// </summary>
    public sealed class HumanizedSkeletonRecorder : MonoBehaviour {
        // Structural bones whose length error is worth reporting. The digits and the face cluster are
        // excluded: they are small, noisy, and nothing in the body retarget reads them, so including
        // them would let an irrelevant landmark dominate the headline number.
        private static readonly int[] BonePairs = {
            23, 25,   // left thigh
            25, 27,   // left shin
            24, 26,   // right thigh
            26, 28,   // right shin
            11, 13,   // left upper arm
            13, 15,   // left forearm
            12, 14,   // right upper arm
            14, 16,   // right forearm
            11, 12,   // shoulder span
            23, 24,   // hip span
        };

        // Non-hip joints that must never approach the mid-hip. The collapse test.
        private static readonly int[] CollapseWatch = { 13, 14, 15, 16, 25, 26, 27, 28 };

        private MonoBehaviour bootstrap;
        private StreamWriter writer;
        private StringBuilder line;
        private PoseFrame previousRaw;
        private PoseFrame previousHumanized;
        private bool hasPrevious;
        // A LOCAL median per measured pair. This deliberately does NOT reuse BoneLengthCalibrator: that
        // class indexes HumanBodyModel.Bones for its left/right mirror averaging, and this recorder's
        // pairs are a different, shorter list. Borrowing it silently averaged the shoulder span against
        // a forearm and reported ~13 cm of bone error on BOTH streams — an instrument fault that would
        // have been read as the layer failing to hold bone lengths.
        private MedianWindow[] rawLengths;
        private MedianWindow[] humanLengths;
        private HumanizedStats lastStats;
        private object layer;
        private int frames;
        private float elapsed;
        private float maxSeconds;
        private bool failed;
        private System.Action<PoseFrame, PoseFrame, float> tap;

        /// <summary>Attach to the AppBootstrap instance and start writing. <paramref name="seconds"/>
        /// 0 = until destroyed.</summary>
        public void Begin(MonoBehaviour appBootstrap, string path, float seconds) {
            bootstrap = appBootstrap;
            maxSeconds = seconds;
            line = new StringBuilder(1024);
            previousRaw = new PoseFrame();
            previousHumanized = new PoseFrame();
            int pairs = BonePairs.Length / 2;
            rawLengths = new MedianWindow[pairs];
            humanLengths = new MedianWindow[pairs];
            int m = 0;
            while (m < pairs) {
                rawLengths[m] = new MedianWindow();
                humanLengths[m] = new MedianWindow();
                m = m + 1;
            }
            try {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }
                writer = new StreamWriter(path, false);
                writer.AutoFlush = true;
            } catch (IOException ex) {
                Debug.LogError("[F27-REC] cannot open " + path + ": " + ex.Message);
                failed = true;
                return;
            }
            System.Reflection.FieldInfo tapField = bootstrap.GetType().GetField("HumanizeTap");
            System.Reflection.PropertyInfo layerProp = bootstrap.GetType().GetProperty("HumanizedLayer");
            if (tapField == null) {
                Debug.LogError("[F27-REC] AppBootstrap has no HumanizeTap - nothing to record.");
                failed = true;
                return;
            }
            layer = layerProp != null ? layerProp.GetValue(bootstrap) : null;
            if (layer != null) {
                // Baseline the counters at attach time. Without this the FIRST recorded line reports the
                // layer's whole history since Play started as if it had all happened on one frame, which
                // an analyser would read as an enormous first-frame event.
                lastStats = (HumanizedStats)layer.GetType().GetProperty("Stats").GetValue(layer);
            }
            tap = OnFrame;
            tapField.SetValue(bootstrap, tap);
            Debug.Log("[F27-REC] recording before/after to " + path);
        }

        private void Update() {
            if (maxSeconds <= 0f) {
                return;
            }
            elapsed = elapsed + Time.unscaledDeltaTime;
            if (elapsed >= maxSeconds) {
                Destroy(this);
            }
        }

        private void OnFrame(PoseFrame raw, PoseFrame humanized, float dt) {
            if (failed || writer == null || raw == null || !raw.IsValid) {
                return;
            }
            CultureInfo ci = CultureInfo.InvariantCulture;
            line.Length = 0;
            line.Append("{\"f\":").Append(frames.ToString(ci));
            line.Append(",\"t\":").Append(raw.TimestampSeconds.ToString("F4", ci));
            line.Append(",\"dt\":").Append(dt.ToString("F4", ci));
            AppendStream(ci, "raw", raw, previousRaw, rawLengths);
            AppendStream(ci, "hum", humanized, previousHumanized, humanLengths);
            AppendStats(ci);
            line.Append("}");
            writer.WriteLine(line.ToString());

            SyntheticPose.CopyInto(raw, previousRaw);
            SyntheticPose.CopyInto(humanized, previousHumanized);
            hasPrevious = true;
            frames = frames + 1;
        }

        /// <summary>A plain sliding-window median, one per measured pair. No mirroring, no anatomy —
        /// it answers only "how far is this pair from its own recent typical length".</summary>
        private sealed class MedianWindow {
            private const int Size = 120;
            private readonly float[] values = new float[Size];
            private readonly float[] scratch = new float[Size];
            private int count;
            private int write;

            public void Observe(float v) {
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) {
                    return;
                }
                values[write] = v;
                write = (write + 1) % Size;
                if (count < Size) {
                    count = count + 1;
                }
            }

            public bool Ready {
                get {
                    return count >= 12;
                }
            }

            public float Median() {
                if (count <= 0) {
                    return 0f;
                }
                System.Array.Copy(values, scratch, count);
                System.Array.Sort(scratch, 0, count);
                return scratch[count / 2];
            }
        }

        private void AppendStream(CultureInfo ci, string key, PoseFrame frame, PoseFrame previous,
                                  MedianWindow[] lengths) {
            // The biggest single-frame landmark move. This is the "large bone jump" measurement, and it
            // is taken on BOTH streams so the layer's effect on it is visible rather than asserted.
            float jump = hasPrevious ? PoseMetrics.MaxDisplacement(previous, frame) : 0f;

            // Worst bone-length error against this stream's OWN median. Using a shared reference would
            // flatter whichever stream the reference came from.
            float worstBone = 0f;
            int worstBoneIndex = -1;
            int b = 0;
            while (b < lengths.Length) {
                int a = BonePairs[b * 2];
                int c = BonePairs[b * 2 + 1];
                float len = PoseMetrics.BoneLength(frame, a, c);
                if (frame.GetLandmark((JointId)a).Confidence > 0.05f
                    && frame.GetLandmark((JointId)c).Confidence > 0.05f) {
                    lengths[b].Observe(len);
                }
                if (lengths[b].Ready) {
                    float err = Mathf.Abs(len - lengths[b].Median());
                    if (err > worstBone) {
                        worstBone = err;
                        worstBoneIndex = b;
                    }
                }
                b = b + 1;
            }

            float closest = float.MaxValue;
            int i = 0;
            while (i < CollapseWatch.Length) {
                float d = PoseMetrics.DistanceFromHip(frame, CollapseWatch[i]);
                if (!float.IsNaN(d) && d < closest) {
                    closest = d;
                }
                i = i + 1;
            }
            if (closest == float.MaxValue) {
                closest = 0f;
            }

            int nonFinite = 0;
            i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 v = frame.GetLandmark((JointId)i).Position;
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                    || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) {
                    nonFinite = nonFinite + 1;
                }
                i = i + 1;
            }

            line.Append(",\"").Append(key).Append("\":{");
            line.Append("\"jump\":").Append(jump.ToString("F4", ci));
            line.Append(",\"bone\":").Append(worstBone.ToString("F4", ci));
            line.Append(",\"boneIdx\":").Append(worstBoneIndex.ToString(ci));
            line.Append(",\"elbowL\":").Append(PoseMetrics.InteriorAngle(frame, 11, 13, 15).ToString("F1", ci));
            line.Append(",\"elbowR\":").Append(PoseMetrics.InteriorAngle(frame, 12, 14, 16).ToString("F1", ci));
            line.Append(",\"kneeL\":").Append(PoseMetrics.InteriorAngle(frame, 23, 25, 27).ToString("F1", ci));
            line.Append(",\"kneeR\":").Append(PoseMetrics.InteriorAngle(frame, 24, 26, 28).ToString("F1", ci));
            line.Append(",\"minhip\":").Append(closest.ToString("F4", ci));
            line.Append(",\"nan\":").Append(nonFinite.ToString(ci));
            line.Append("}");
        }

        // The layer's own counters, differenced per frame so a line says what fired on THAT frame.
        private void AppendStats(CultureInfo ci) {
            if (layer == null) {
                line.Append(",\"stage\":null");
                return;
            }
            HumanizedStats now = (HumanizedStats)layer.GetType().GetProperty("Stats").GetValue(layer);
            line.Append(",\"stage\":{");
            line.Append("\"held\":").Append((now.HeldJointFrames - lastStats.HeldJointFrames).ToString(ci));
            line.Append(",\"vclamp\":").Append((now.VelocityClamped - lastStats.VelocityClamped).ToString(ci));
            line.Append(",\"bone\":").Append((now.BoneLengthCorrected - lastStats.BoneLengthCorrected).ToString(ci));
            line.Append(",\"hinge\":").Append((now.HingeClamped - lastStats.HingeClamped).ToString(ci));
            line.Append(",\"cone\":").Append((now.ConeClamped - lastStats.ConeClamped).ToString(ci));
            line.Append(",\"knee\":").Append((now.KneeUnbent - lastStats.KneeUnbent).ToString(ci));
            line.Append(",\"twist\":").Append((now.TwistClamped - lastStats.TwistClamped).ToString(ci));
            line.Append(",\"recov\":").Append((now.RecoveringJointFrames - lastStats.RecoveringJointFrames).ToString(ci));
            line.Append(",\"rejOrigin\":").Append((now.RejectedOrigin - lastStats.RejectedOrigin).ToString(ci));
            line.Append(",\"rejFar\":").Append((now.RejectedOutOfBody - lastStats.RejectedOutOfBody).ToString(ci));
            line.Append(",\"rejNan\":").Append((now.RejectedNonFinite - lastStats.RejectedNonFinite).ToString(ci));
            line.Append(",\"rejConf\":").Append((now.RejectedLowConfidence - lastStats.RejectedLowConfidence).ToString(ci));
            line.Append(",\"untouched\":").Append((now.FramesUntouched - lastStats.FramesUntouched).ToString(ci));
            line.Append(",\"boneOnly\":").Append((now.FramesOnlyBoneLength - lastStats.FramesOnlyBoneLength).ToString(ci));
            line.Append("}");
            lastStats = now;
        }

        private void OnDestroy() {
            if (bootstrap != null && tap != null) {
                System.Reflection.FieldInfo tapField = bootstrap.GetType().GetField("HumanizeTap");
                if (tapField != null && ReferenceEquals(tapField.GetValue(bootstrap), tap)) {
                    // Only clear a tap that is still OURS: a later recorder may have replaced it, and
                    // silently unhooking that one would leave a session looking recorded when it is not.
                    tapField.SetValue(bootstrap, null);
                }
            }
            if (writer != null) {
                writer.Flush();
                writer.Close();
                writer = null;
                Debug.Log("[F27-REC] wrote " + frames + " frames");
            }
        }
    }
}
