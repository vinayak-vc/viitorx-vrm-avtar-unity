using System;
using System.Text;

namespace VirtualMirror.Vision {
    /// <summary>
    /// The part of parsing a hands or face datagram that is the same for both: the header (<c>ver kind sid seq t lat src img fpx mir n</c>), the version
    /// and kind checks, and the count check. A subclass supplies the kind it accepts, the name of its item array, and how to read one item.
    ///
    /// A parser owns its scanner and is NOT thread-safe: one parser per receive thread. Parsing allocates nothing except the session string, and
    /// only when the session changes.
    ///
    /// Checks, in the order they decide the result: a version other than 1 is <see cref="WireParseResult.UnknownVersion"/> (a receiver must not guess
    /// at a format it does not know, and this is reported even when the rest of the datagram would not have parsed); a kind that is not this parser's is
    /// <see cref="WireParseResult.WrongKind"/>; a malformed body is the scanner's failure; an item array whose length is not <c>n</c> is
    /// <see cref="WireParseResult.CountMismatch"/>. Unknown members are skipped, so a later sender may add fields within version 1.
    /// </summary>
    public abstract class VisionWireParser {
        private readonly WireScanner scanner = new WireScanner();

        private int version;
        private bool kindSeen;
        private bool kindOk;
        private int declaredCount;
        private int itemsSeen;

        protected WireScanner Scanner {
            get {
                return scanner;
            }
        }

        protected abstract byte[] ExpectedKind { get; }

        protected abstract byte[] ItemsKey { get; }

        protected abstract void ResetMessage(VisionMessage message);

        protected abstract int Capacity(VisionMessage message);

        /// <summary>Reads one item of the item array into slot <paramref name="index"/>. The scanner is on the item's value.</summary>
        protected abstract bool ReadItem(VisionMessage message, int index);

        /// <summary>Reads a member the shared header does not know (such as the face wire's <c>bsv</c>). Return false to leave it to the generic skip.</summary>
        protected virtual bool ReadExtraMember(ReadOnlySpan<byte> key, VisionMessage message, out bool consumed) {
            consumed = false;
            return true;
        }

        public WireParseResult TryParse(byte[] buffer, int length, VisionMessage message) {
            if (buffer == null || length <= 0) {
                return WireParseResult.Empty;
            }
            scanner.Reset(buffer, length);
            ResetMessage(message);
            version = -1;
            kindSeen = false;
            kindOk = false;
            declaredCount = -1;
            itemsSeen = -1;
            if (!scanner.BeginObject()) {
                return Resolve();
            }
            bool first = true;
            ReadOnlySpan<byte> key;
            while (scanner.NextMember(first, out key)) {
                first = false;
                bool consumed;
                if (!ReadMember(key, message, out consumed)) {
                    return Resolve();
                }
                if (version != -1 && version != WireKeys.SupportedVersion) {
                    return WireParseResult.UnknownVersion;
                }
                if (kindSeen && !kindOk) {
                    return WireParseResult.WrongKind;
                }
            }
            if (scanner.Failed) {
                return Resolve();
            }
            if (version == -1) {
                return WireParseResult.MissingField;
            }
            if (!kindSeen) {
                return WireParseResult.MissingField;
            }
            if (itemsSeen < 0 || declaredCount < 0) {
                return WireParseResult.MissingField;
            }
            if (declaredCount != itemsSeen) {
                return WireParseResult.CountMismatch;
            }
            message.DeclaredCount = declaredCount;
            int capacity = Capacity(message);
            message.Count = itemsSeen < capacity ? itemsSeen : capacity;
            return WireParseResult.Ok;
        }

        private bool ReadMember(ReadOnlySpan<byte> key, VisionMessage message, out bool consumed) {
            consumed = true;
            if (WireKeys.Is(key, WireKeys.Ver)) {
                return scanner.ReadInt(out version);
            }
            if (WireKeys.Is(key, WireKeys.Kind)) {
                ReadOnlySpan<byte> kind;
                if (!scanner.ReadString(out kind)) {
                    return false;
                }
                kindSeen = true;
                kindOk = WireKeys.Is(kind, ExpectedKind);
                return true;
            }
            if (WireKeys.Is(key, WireKeys.Sid)) {
                return ReadSession(message);
            }
            if (WireKeys.Is(key, WireKeys.Seq)) {
                long sequence;
                bool ok = scanner.ReadLong(out sequence);
                message.Sequence = sequence;
                return ok;
            }
            if (WireKeys.Is(key, WireKeys.T)) {
                double stamp;
                bool ok = scanner.ReadDouble(out stamp);
                message.TimestampUnix = stamp;
                return ok;
            }
            if (WireKeys.Is(key, WireKeys.Lat)) {
                float latency;
                bool ok = scanner.ReadFloat(out latency);
                message.LatencyMs = latency;
                return ok;
            }
            if (WireKeys.Is(key, WireKeys.Src)) {
                ReadOnlySpan<byte> source;
                if (!scanner.ReadString(out source)) {
                    return false;
                }
                message.Source = WireKeys.ReadSource(source);
                return true;
            }
            if (WireKeys.Is(key, WireKeys.Img)) {
                return ReadImageSize(message);
            }
            if (WireKeys.Is(key, WireKeys.Fpx)) {
                float focal;
                bool ok = scanner.ReadFloat(out focal);
                message.FocalPixels = focal;
                return ok;
            }
            if (WireKeys.Is(key, WireKeys.Mir)) {
                int mirrored;
                bool ok = scanner.ReadInt(out mirrored);
                message.Mirrored = mirrored != 0;
                return ok;
            }
            if (WireKeys.Is(key, WireKeys.N)) {
                return scanner.ReadInt(out declaredCount);
            }
            if (WireKeys.Is(key, ItemsKey)) {
                return ReadItems(message);
            }
            bool extra;
            if (!ReadExtraMember(key, message, out extra)) {
                return false;
            }
            if (extra) {
                return true;
            }
            return scanner.SkipValue();
        }

        private bool ReadItems(VisionMessage message) {
            if (!scanner.BeginArray()) {
                return false;
            }
            int capacity = Capacity(message);
            int index = 0;
            bool first = true;
            while (scanner.NextElement(first)) {
                first = false;
                if (index < capacity) {
                    if (!ReadItem(message, index)) {
                        return false;
                    }
                } else if (!scanner.SkipValue()) {
                    return false;
                }
                index = index + 1;
            }
            if (scanner.Failed) {
                return false;
            }
            itemsSeen = index;
            return true;
        }

        private bool ReadSession(VisionMessage message) {
            ReadOnlySpan<byte> session;
            if (!scanner.ReadString(out session)) {
                return false;
            }
            if (!WireKeys.EqualsAscii(session, message.SessionId)) {
                message.SessionId = Encoding.UTF8.GetString(session);
            }
            return true;
        }

        private bool ReadImageSize(VisionMessage message) {
            if (!scanner.BeginArray()) {
                return false;
            }
            int width;
            int height;
            if (!scanner.NextElement(true) || !scanner.ReadInt(out width)) {
                return scanner.Fail(WireParseResult.BadShape);
            }
            if (!scanner.NextElement(false) || !scanner.ReadInt(out height)) {
                return scanner.Fail(WireParseResult.BadShape);
            }
            if (scanner.NextElement(false)) {
                return scanner.Fail(WireParseResult.BadShape);
            }
            if (scanner.Failed) {
                return false;
            }
            message.ImageWidth = width;
            message.ImageHeight = height;
            return true;
        }

        private WireParseResult Resolve() {
            if (version != -1 && version != WireKeys.SupportedVersion) {
                return WireParseResult.UnknownVersion;
            }
            if (kindSeen && !kindOk) {
                return WireParseResult.WrongKind;
            }
            return scanner.Failed ? scanner.Failure : WireParseResult.NotJson;
        }
    }
}
