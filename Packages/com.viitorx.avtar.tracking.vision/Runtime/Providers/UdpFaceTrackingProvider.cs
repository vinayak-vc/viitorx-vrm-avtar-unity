using System.Diagnostics;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// <see cref="IFaceTrackingProvider"/> over the face wire: a drop-in face source for a camera that sees a face and no body. Owns (or borrows) a
    /// <see cref="FaceReceiver"/>; <see cref="Tick"/> pulls the newest datagram and maps it with <see cref="FaceFrameMapper"/>.
    ///
    /// <see cref="TryGetFrame"/> is true once any datagram has arrived, and the frame's <c>IsValid</c> says whether a face is in view RIGHT NOW: it goes
    /// invalid when no face is in the datagram and when the stream goes stale, so <c>VrmExpressionRetargeter</c> resets the avatar to neutral.
    /// </summary>
    public sealed class UdpFaceTrackingProvider : IFaceTrackingProvider {
        private readonly FaceReceiver receiver;
        private readonly bool ownsReceiver;
        private readonly bool swapSides;
        private readonly FaceMessage message = new FaceMessage();
        private readonly FaceFrame frame = new FaceFrame();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool hasAnyFrame;

        public UdpFaceTrackingProvider(bool swapSides) : this(new FaceReceiver(), true, swapSides) {
        }

        public UdpFaceTrackingProvider(FaceReceiver receiver, bool ownsReceiver, bool swapSides) {
            this.receiver = receiver;
            this.ownsReceiver = ownsReceiver;
            this.swapSides = swapSides;
        }

        public FaceReceiver Receiver {
            get {
                return receiver;
            }
        }

        public bool IsTracking {
            get {
                return receiver.IsRunning && receiver.State == StreamState.Live;
            }
        }

        public bool StartTracking() {
            return receiver.IsRunning || receiver.Start();
        }

        public void Tick(float deltaSeconds) {
            double now = clock.Elapsed.TotalSeconds;
            if (receiver.TryGetNew(message)) {
                FaceFrameMapper.Fill(frame, message, swapSides, now);
                hasAnyFrame = true;
            } else if (hasAnyFrame && receiver.State == StreamState.Stale && frame.IsValid) {
                frame.MarkInvalid(now);
            }
        }

        public void StopTracking() {
            receiver.Stop();
        }

        public bool TryGetFrame(out FaceFrame latest) {
            latest = frame;
            return hasAnyFrame;
        }

        public void Dispose() {
            if (ownsReceiver) {
                receiver.Dispose();
            }
        }
    }
}
