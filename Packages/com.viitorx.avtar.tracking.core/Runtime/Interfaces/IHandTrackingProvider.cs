using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Contract for hand tracking providers that produce finger joint and curl data.
    /// </summary>
    public interface IHandTrackingProvider : IDisposable {
        bool IsTracking { get; }
        bool StartTracking();
        void Tick(float deltaSeconds);
        void StopTracking();
        bool TryGetFrame(out HandFrame frame);
    }
}
