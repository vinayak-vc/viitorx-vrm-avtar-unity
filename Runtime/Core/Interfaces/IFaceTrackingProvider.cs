using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Contract for face tracking providers that produce facial expression weights.
    /// </summary>
    public interface IFaceTrackingProvider : IDisposable {
        bool IsTracking { get; }
        bool StartTracking();
        void Tick(float deltaSeconds);
        void StopTracking();
        bool TryGetFrame(out FaceFrame frame);
    }
}
