using System.Diagnostics;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// <see cref="IHandTrackingProvider"/> over the hands wire: a drop-in hand source for a camera that sees hands and no body. Owns (or borrows)
    /// a <see cref="HandsReceiver"/>; <see cref="Tick"/> pulls the newest datagram and maps it with <see cref="HandFrameMapper"/>.
    ///
    /// <see cref="TryGetFrame"/> is true once any datagram has arrived, and the frame's <c>IsValid</c> says whether a hand is in view RIGHT NOW: it
    /// goes invalid when no hand is in the datagram and when the stream goes stale, so a consumer resets the avatar's fingers instead of freezing them.
    /// </summary>
    public sealed class UdpHandTrackingProvider : IHandTrackingProvider {
        private readonly HandsReceiver receiver;
        private readonly bool ownsReceiver;
        private readonly bool swapSides;
        private readonly HandsMessage message = new HandsMessage();
        private readonly HandFrame frame = new HandFrame();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool hasAnyFrame;

        public UdpHandTrackingProvider(bool swapSides) : this(new HandsReceiver(), true, swapSides) {
        }

        public UdpHandTrackingProvider(HandsReceiver receiver, bool ownsReceiver, bool swapSides) {
            this.receiver = receiver;
            this.ownsReceiver = ownsReceiver;
            this.swapSides = swapSides;
        }

        public HandsReceiver Receiver {
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
                HandFrameMapper.Fill(frame, message, swapSides, now);
                hasAnyFrame = true;
            } else if (hasAnyFrame && receiver.State == StreamState.Stale && frame.IsValid) {
                frame.MarkInvalid(now);
            }
        }

        public void StopTracking() {
            receiver.Stop();
        }

        public bool TryGetFrame(out HandFrame latest) {
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
