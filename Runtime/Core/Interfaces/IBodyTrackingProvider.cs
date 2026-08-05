using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Produces <see cref="PoseFrame"/> body landmarks from some source (MediaPipe over a camera,
    /// or a fake generator). The orchestrator calls <see cref="Tick"/> once per frame and reads the
    /// latest immutable frame. Implementations never touch avatar Transforms.
    /// </summary>
    public interface IBodyTrackingProvider : IDisposable {
        bool IsRunning { get; }
        void StartTracking();
        void StopTracking();
        void Tick(float deltaSeconds);
        bool TryGetLatestFrame(out PoseFrame frame);
    }
}
