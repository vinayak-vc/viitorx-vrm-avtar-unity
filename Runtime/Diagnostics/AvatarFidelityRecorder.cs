using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Retargeting;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-26 §9.1 — records POSE FIDELITY: per-bone angular error between the tracked subject's
    /// segment direction and the rendered avatar bone's direction, one JSON record per frame.
    ///
    /// DIAGNOSTIC ONLY. Nothing in the product references this type. It reads transforms and never
    /// writes one, so it cannot alter the pose it measures.
    ///
    /// WHY IT EXISTS. Every avatar metric this project has produced measures the avatar against
    /// itself — yaw jitter, snap count, bone-length constancy, swap count. All four were reported by
    /// F-19 as "AVATAR QUALITY: PROVEN GOOD" and all four are satisfied by an avatar that is
    /// smoothly and stably in the WRONG pose, which is what the 2026-09-16 recording shows. Without
    /// this measurement any retarget change can only be judged by eye or by stability numbers that
    /// do not measure fidelity — which is exactly how the current state was reached.
    ///
    /// WHY <see cref="Application.onBeforeRender"/> AND NOT LateUpdate: the retarget chain ends with
    /// UniVRM's Vrm10Runtime.Process(), invoked from the driver's own update. Sampling in a
    /// LateUpdate races that call and can capture the pose one stage early — silently measuring a
    /// skeleton that was never drawn, and reporting a fidelity error that belongs to no frame. This
    /// is the same reasoning F19BoneRecorder records, and it matters more here: a stale sample would
    /// bias the very number the metric exists to establish.
    ///
    /// Self-terminating, per the standing requirement after editor-update samplers were left
    /// running: unsubscribes in OnDisable, closes the file in OnDestroy, and destroys itself after
    /// <see cref="maxSeconds"/> when that is set above zero.
    /// </summary>
    public sealed class AvatarFidelityRecorder : MonoBehaviour {
        [SerializeField] private string outputPath = "";
        [SerializeField] private float maxSeconds = 0f;

        private KalidokitControlRigDriver driver;
        private BoneFidelity[] buffer;
        private StreamWriter writer;
        private StringBuilder line;
        private int frames;
        private float elapsed;
        private bool subscribed;
        private bool failed;

        /// <summary>Records to <paramref name="path"/>. <paramref name="seconds"/> 0 = until destroyed.</summary>
        public void Begin(KalidokitControlRigDriver controlRig, string path, float seconds) {
            driver = controlRig;
            outputPath = path;
            maxSeconds = seconds;
            buffer = new BoneFidelity[KalidokitControlRigDriver.FidelityBoneCount];
            line = new StringBuilder(512);
            Open();
        }

        private void Open() {
            if (writer != null || failed || string.IsNullOrEmpty(outputPath)) {
                return;
            }
            try {
                string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }
                writer = new StreamWriter(outputPath, false);
                writer.AutoFlush = true;
            } catch (IOException ex) {
                // Loud, once. A recorder that fails quietly leaves a session looking captured when it
                // is not, which is the failure this whole measurement exists to stop repeating.
                Debug.LogError("[F26-FIDELITY] cannot open " + outputPath + ": " + ex.Message);
                failed = true;
            }
        }

        private void OnEnable() {
            if (!subscribed) {
                Application.onBeforeRender += Sample;
                subscribed = true;
            }
        }

        private void OnDisable() {
            if (subscribed) {
                Application.onBeforeRender -= Sample;
                subscribed = false;
            }
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

        private void Sample() {
            if (driver == null || buffer == null || writer == null || failed) {
                return;
            }
            int count = driver.SampleFidelity(buffer);
            if (count <= 0) {
                return;
            }
            CultureInfo ci = CultureInfo.InvariantCulture;
            line.Length = 0;
            line.Append("{\"frame\":").Append(frames.ToString(ci));
            line.Append(",\"t\":").Append(Time.realtimeSinceStartupAsDouble.ToString("F4", ci));
            int index = 0;
            while (index < count) {
                BoneFidelity f = buffer[index];
                line.Append(",\"").Append(f.Bone).Append("\":");
                if (f.Valid) {
                    line.Append(f.ErrorDeg.ToString("F2", ci));
                } else {
                    // null, never 0 - an unmeasurable bone must not read as a perfect one
                    line.Append("null");
                }
                line.Append(",\"").Append(f.Bone).Append("_conf\":")
                    .Append(f.MinConfidence.ToString("F3", ci));
                // The subject's and the rig's own inclination, so a FROZEN channel (source swinging,
                // rig not) can be told apart from a fixed offset - the two look identical in the
                // error column alone. Both null when the frame is unmeasurable, for the same reason.
                line.Append(",\"").Append(f.Bone).Append("_src\":")
                    .Append(f.Valid ? f.SourceDeg.ToString("F2", ci) : "null");
                line.Append(",\"").Append(f.Bone).Append("_rig\":")
                    .Append(f.Valid ? f.RigDeg.ToString("F2", ci) : "null");
                index = index + 1;
            }
            line.Append("}");
            writer.WriteLine(line.ToString());
            frames = frames + 1;
        }

        private void OnDestroy() {
            OnDisable();
            if (writer != null) {
                writer.Flush();
                writer.Close();
                writer = null;
                Debug.Log("[F26-FIDELITY] wrote " + frames + " frames to " + outputPath);
            }
        }
    }
}
