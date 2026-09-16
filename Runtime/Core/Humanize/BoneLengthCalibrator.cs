using UnityEngine;

namespace VirtualMirror.Core.Humanize {
    /// <summary>
    /// F-27 — learns THIS person's bone lengths from the tracker and then holds them fixed.
    ///
    /// The tracker reports a position per landmark, independently, every frame. Nothing in it knows
    /// that a forearm has a length, so the distance between the tracked elbow and the tracked wrist
    /// breathes with depth noise. Downstream, the avatar's own bones are rigid, so that breathing
    /// turns into aim error rather than a stretching arm — the retarget can only ever AIM a bone, never
    /// place its end (F-26 §3). Fixing the length upstream removes a whole class of jitter that the
    /// retarget is structurally unable to absorb.
    ///
    /// WHY A MEDIAN AND NOT A MEAN. The failure this has to survive is a joint dropping to the origin
    /// or blowing up in depth, which produces a wildly wrong length for a handful of frames. A mean
    /// absorbs those permanently; a median over a sliding window ignores them as long as they are the
    /// minority, which for tracking faults they always are.
    ///
    /// WHY IT KEEPS LEARNING. The window slides rather than locking, so a different person stepping in
    /// front of the camera is re-measured within ~4 seconds instead of wearing the last person's
    /// skeleton. The cost is that a long sustained mis-measurement would eventually be adopted — which
    /// is why samples outside plausible human bone lengths never enter the window at all.
    ///
    /// LEFT/RIGHT ARE AVERAGED. Real limbs are symmetric to within a couple of per cent; a monocular
    /// or a partially-occluded tracker's are not. Sharing one length between mirrored bones makes the
    /// body symmetric for free and doubles the sample rate for each of them.
    /// </summary>
    public sealed class BoneLengthCalibrator {
        /// <summary>Sliding window per bone. At ~30 Hz this is ~4 s of history — long enough for the
        /// median to be robust, short enough to re-learn a new subject quickly.</summary>
        public const int WindowSize = 120;

        /// <summary>Below this the median is not trustworthy and the bone is reported UNCALIBRATED, so
        /// the caller falls back to the observed length rather than to a number invented from noise.</summary>
        public const int MinSamples = 12;

        private readonly float[] samples;     // [bone * WindowSize + slot]
        private readonly int[] counts;        // how many valid samples this bone has ever taken
        private readonly int[] writeIndex;
        private readonly float[] median;
        private readonly float[] scratch;
        private bool dirty = true;

        public BoneLengthCalibrator() {
            int boneCount = HumanBodyModel.Bones.Length;
            samples = new float[boneCount * WindowSize];
            counts = new int[boneCount];
            writeIndex = new int[boneCount];
            median = new float[boneCount];
            scratch = new float[WindowSize];
        }

        /// <summary>Forget everything. Called when the tracking session restarts, so one subject's
        /// skeleton is never carried into another's session.</summary>
        public void Reset() {
            int i = 0;
            while (i < counts.Length) {
                counts[i] = 0;
                writeIndex[i] = 0;
                median[i] = 0f;
                i = i + 1;
            }
            dirty = true;
        }

        /// <summary>
        /// Offer one measured length for a bone. Rejected silently unless it is finite and inside
        /// plausible human anatomy — a sample taken from a collapsed or blown-up joint must never be
        /// allowed to move the median, because the median is what protects against exactly that sample.
        /// </summary>
        public void Observe(int bone, float length) {
            if (bone < 0 || bone >= counts.Length) {
                return;
            }
            if (float.IsNaN(length) || float.IsInfinity(length)) {
                return;
            }
            if (length < HumanBodyModel.MinBoneM || length > HumanBodyModel.MaxBoneM) {
                return;
            }
            int slot = writeIndex[bone];
            samples[bone * WindowSize + slot] = length;
            writeIndex[bone] = (slot + 1) % WindowSize;
            if (counts[bone] < WindowSize) {
                counts[bone] = counts[bone] + 1;
            }
            dirty = true;
        }

        /// <summary>True once this bone has enough samples for its median to mean anything.</summary>
        public bool IsCalibrated(int bone) {
            if (bone < 0 || bone >= counts.Length) {
                return false;
            }
            return counts[bone] >= MinSamples;
        }

        /// <summary>
        /// The calibrated length for a bone, metres, or 0 when uncalibrated. Mirrored bones return the
        /// mean of both sides once both are calibrated; one calibrated side alone serves both, so an
        /// arm that is occluded throughout still gets a correct length from the visible arm.
        /// </summary>
        public float Length(int bone) {
            if (bone < 0 || bone >= counts.Length) {
                return 0f;
            }
            if (dirty) {
                Recompute();
            }
            int mirror = HumanBodyModel.Bones[bone].Mirror;
            bool self = IsCalibrated(bone);
            bool other = mirror >= 0 && mirror < counts.Length && IsCalibrated(mirror);
            if (self && other) {
                return 0.5f * (median[bone] + median[mirror]);
            }
            if (self) {
                return median[bone];
            }
            if (other) {
                return median[mirror];
            }
            return 0f;
        }

        /// <summary>Number of samples taken for a bone, for diagnostics.</summary>
        public int SampleCount(int bone) {
            if (bone < 0 || bone >= counts.Length) {
                return 0;
            }
            return counts[bone];
        }

        // Recomputed lazily and at most once per frame: Length() is called ~24 times per frame and the
        // window only changes between frames, so doing this per call would sort the same data 24 times.
        private void Recompute() {
            int bone = 0;
            while (bone < counts.Length) {
                int n = counts[bone];
                if (n <= 0) {
                    median[bone] = 0f;
                    bone = bone + 1;
                    continue;
                }
                int i = 0;
                while (i < n) {
                    scratch[i] = samples[bone * WindowSize + i];
                    i = i + 1;
                }
                InsertionSort(scratch, n);
                median[bone] = scratch[n / 2];
                bone = bone + 1;
            }
            dirty = false;
        }

        // Insertion sort rather than System.Array.Sort: n is at most 120, the data is nearly sorted in
        // practice (bone lengths barely change), and Array.Sort on a subrange of a shared scratch buffer
        // would still need a comparer allocation on some runtimes. This allocates nothing, ever.
        private static void InsertionSort(float[] values, int n) {
            int i = 1;
            while (i < n) {
                float key = values[i];
                int j = i - 1;
                while (j >= 0 && values[j] > key) {
                    values[j + 1] = values[j];
                    j = j - 1;
                }
                values[j + 1] = key;
                i = i + 1;
            }
        }

        /// <summary>Diagnostics: how many bones have a usable length yet.</summary>
        public int CalibratedBoneCount() {
            int n = 0;
            int bone = 0;
            while (bone < counts.Length) {
                if (Length(bone) > 0f) {
                    n = n + 1;
                }
                bone = bone + 1;
            }
            return n;
        }

        /// <summary>Total height of the calibrated skeleton (hip to nose), metres, or 0. Used as a
        /// single legible number for "did calibration converge on a human".</summary>
        public float TrunkAndHeadLength() {
            return Mathf.Max(0f, Length(2)) + Mathf.Max(0f, Length(5));
        }
    }
}
