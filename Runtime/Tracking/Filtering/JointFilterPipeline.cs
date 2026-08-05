using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.Filtering {
    /// <summary>
    /// Applies a per-axis One-Euro filter to every landmark position, producing a smoothed
    /// <see cref="PoseFrame"/>. Confidence is passed through. Low-confidence gating can layer on top.
    /// </summary>
    public sealed class JointFilterPipeline {
        private readonly OneEuroFilter[] filtersX;
        private readonly OneEuroFilter[] filtersY;
        private readonly OneEuroFilter[] filtersZ;
        private readonly PoseLandmark[] buffer;

        public JointFilterPipeline(float minCutoff, float beta, float derivativeCutoff) {
            filtersX = new OneEuroFilter[PoseFrame.LandmarkCount];
            filtersY = new OneEuroFilter[PoseFrame.LandmarkCount];
            filtersZ = new OneEuroFilter[PoseFrame.LandmarkCount];
            buffer = new PoseLandmark[PoseFrame.LandmarkCount];
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                filtersX[index] = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
                filtersY[index] = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
                filtersZ[index] = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
                index = index + 1;
            }
        }

        public PoseFrame Filter(PoseFrame frame, float deltaSeconds) {
            if (frame == null || !frame.IsValid) {
                return frame;
            }
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                PoseLandmark landmark = frame.GetLandmark((JointId)index);
                Vector3 position = landmark.Position;
                float x = filtersX[index].Filter(position.x, deltaSeconds);
                float y = filtersY[index].Filter(position.y, deltaSeconds);
                float z = filtersZ[index].Filter(position.z, deltaSeconds);
                buffer[index] = new PoseLandmark(new Vector3(x, y, z), landmark.Confidence);
                index = index + 1;
            }
            PoseLandmark[] copy = new PoseLandmark[PoseFrame.LandmarkCount];
            System.Array.Copy(buffer, copy, PoseFrame.LandmarkCount);
            return new PoseFrame(copy, frame.TimestampSeconds, true);
        }

        public void Reset() {
            int index = 0;
            while (index < PoseFrame.LandmarkCount) {
                filtersX[index].Reset();
                filtersY[index].Reset();
                filtersZ[index].Reset();
                index = index + 1;
            }
        }
    }
}
