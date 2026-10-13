using System;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Receives the hands wire (hand_wire.py, <c>{"kind":"hands"}</c>) on UDP <see cref="DefaultPort"/> from <c>tools/hands/hands_udp_sender.py</c>.
    /// Use <see cref="UdpVisionReceiver{TMessage}.TryGetNew"/> once per frame with a <see cref="HandsMessage"/> you own.
    /// </summary>
    public sealed class HandsReceiver : UdpVisionReceiver<HandsMessage> {
        public const int DefaultPort = 8901;
        public const double DefaultStaleSeconds = 0.5;

        private readonly HandsWireParser parser = new HandsWireParser();

        public HandsReceiver() : this(DefaultPort, true, DefaultStaleSeconds, null, null, HandsMessage.DefaultCapacity) {
        }

        public HandsReceiver(int port, bool loopbackOnly, double staleSeconds, ILogService logService, Func<double> clockSeconds, int capacity)
            : base("HandsReceiver", port, loopbackOnly, staleSeconds, logService, clockSeconds, new HandsMessage(capacity), new HandsMessage(capacity)) {
        }

        protected override WireParseResult Parse(byte[] data, int length, HandsMessage message) {
            return parser.TryParse(data, length, message);
        }
    }
}
