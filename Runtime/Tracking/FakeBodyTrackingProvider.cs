using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking {
    /// <summary>
    /// Deterministic <see cref="IBodyTrackingProvider"/> that synthesizes a waving-arms pose. Used to
    /// exercise the filter → retarget → avatar path without MediaPipe. Landmarks are emitted in a
    /// Unity-aligned space (X right, Y up, Z forward) so the retargeter maps directions directly.
    /// </summary>
    public sealed class FakeBodyTrackingProvider : IBodyTrackingProvider {
        private readonly PoseLandmark[] buffer;

        private bool running;
        private float phase;
        private PoseFrame latest;

        public FakeBodyTrackingProvider() {
            buffer = new PoseLandmark[PoseFrame.LandmarkCount];
        }

        public bool IsRunning {
            get {
                return running;
            }
        }

        public void StartTracking() {
            running = true;
        }

        public void StopTracking() {
            running = false;
        }

        public void Tick(float deltaSeconds) {
            if (!running) {
                return;
            }
            phase = phase + deltaSeconds;
            BuildPose();
        }

        public bool TryGetLatestFrame(out PoseFrame frame) {
            frame = latest;
            return latest != null;
        }

        public void Dispose() {
            running = false;
            latest = null;
        }

        private void BuildPose() {
            int index = 0;
            while (index < buffer.Length) {
                buffer[index] = new PoseLandmark(Vector3.zero, 0f);
                index = index + 1;
            }

            float raise = Mathf.Sin(phase * 2f) * 0.5f + 0.5f;
            float armY = Mathf.Lerp(-0.15f, 0.55f, raise);

            Vector3 leftShoulder = new Vector3(0.16f, 1.35f, 0f);
            Vector3 rightShoulder = new Vector3(-0.16f, 1.35f, 0f);
            Vector3 leftElbow = leftShoulder + new Vector3(0.24f, armY, 0f);
            Vector3 rightElbow = rightShoulder + new Vector3(-0.24f, armY, 0f);
            Vector3 leftWrist = leftElbow + new Vector3(0.24f, armY * 0.6f, 0f);
            Vector3 rightWrist = rightElbow + new Vector3(-0.24f, armY * 0.6f, 0f);

            Set(JointId.LeftShoulder, leftShoulder);
            Set(JointId.RightShoulder, rightShoulder);
            Set(JointId.LeftElbow, leftElbow);
            Set(JointId.RightElbow, rightElbow);
            Set(JointId.LeftWrist, leftWrist);
            Set(JointId.RightWrist, rightWrist);
            Set(JointId.LeftHip, new Vector3(0.1f, 0.9f, 0f));
            Set(JointId.RightHip, new Vector3(-0.1f, 0.9f, 0f));
            Set(JointId.Nose, new Vector3(0f, 1.62f, 0.05f));
            Set(JointId.LeftKnee, new Vector3(0.1f, 0.5f, 0f));
            Set(JointId.RightKnee, new Vector3(-0.1f, 0.5f, 0f));
            Set(JointId.LeftAnkle, new Vector3(0.1f, 0.08f, 0f));
            Set(JointId.RightAnkle, new Vector3(-0.1f, 0.08f, 0f));

            PoseLandmark[] copy = new PoseLandmark[PoseFrame.LandmarkCount];
            System.Array.Copy(buffer, copy, PoseFrame.LandmarkCount);
            latest = new PoseFrame(copy, phase, true);
        }

        private void Set(JointId joint, Vector3 position) {
            buffer[(int)joint] = new PoseLandmark(position, 1f);
        }
    }
}
