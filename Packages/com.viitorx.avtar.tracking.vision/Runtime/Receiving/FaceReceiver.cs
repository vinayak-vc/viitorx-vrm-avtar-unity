using System;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Receives the face wire (face_wire.py, <c>{"kind":"face"}</c>) on UDP <see cref="DefaultPort"/> from <c>tools/face/face_udp_sender.py</c>.
    /// Use <see cref="UdpVisionReceiver{TMessage}.TryGetNew"/> once per frame with a <see cref="FaceMessage"/> you own.
    /// </summary>
    public sealed class FaceReceiver : UdpVisionReceiver<FaceMessage> {
        public const int DefaultPort = 8902;
        public const double DefaultStaleSeconds = 0.5;

        private readonly FaceWireParser parser = new FaceWireParser();

        public FaceReceiver() : this(DefaultPort, true, DefaultStaleSeconds, null, null, FaceMessage.DefaultCapacity) {
        }

        public FaceReceiver(int port, bool loopbackOnly, double staleSeconds, ILogService logService, Func<double> clockSeconds, int capacity)
            : base("FaceReceiver", port, loopbackOnly, staleSeconds, logService, clockSeconds, new FaceMessage(capacity), new FaceMessage(capacity)) {
        }

        protected override WireParseResult Parse(byte[] data, int length, FaceMessage message) {
            return parser.TryParse(data, length, message);
        }
    }
}
