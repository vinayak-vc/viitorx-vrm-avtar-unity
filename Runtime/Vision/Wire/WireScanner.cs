using System;
using System.Buffers.Text;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// A pull-style JSON scanner over a UTF-8 byte buffer that allocates nothing. The hands and face datagrams are
    /// 2-25 KB at 30 per second; a tree parser (JObject) would build thousands of objects per frame for numbers that
    /// are about to be copied into fixed arrays. This reads the numbers straight into those arrays.
    ///
    /// It reads the JSON the sidecar writes (hand_wire.py, face_wire.py) and any other valid JSON of the same shape:
    /// whitespace, reordered keys and unknown keys are fine (<see cref="SkipValue"/>). It is strict where the contract
    /// is strict: a trailing comma, a missing colon, a number that is not a number, NaN and Infinity all fail. The
    /// first failure sticks in <see cref="Failure"/> and every later call returns false, so a parser can check once.
    ///
    /// One instance per parser, reused through <see cref="Reset"/>. Not thread-safe.
    /// </summary>
    public sealed class WireScanner {
        private byte[] buffer;
        private int end;
        private int position;
        private WireParseResult failure;

        public WireParseResult Failure {
            get {
                return failure;
            }
        }

        public bool Failed {
            get {
                return failure != WireParseResult.Ok;
            }
        }

        public void Reset(byte[] data, int length) {
            buffer = data;
            end = data == null ? 0 : Math.Min(length, data.Length);
            if (end < 0) {
                end = 0;
            }
            position = 0;
            failure = WireParseResult.Ok;
        }

        public bool Fail(WireParseResult reason) {
            if (failure == WireParseResult.Ok) {
                failure = reason;
            }
            return false;
        }

        public bool BeginObject() {
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Empty);
            }
            if (buffer[position] != (byte)'{') {
                return Fail(WireParseResult.NotJson);
            }
            position = position + 1;
            return true;
        }

        /// <summary>Moves to the next member of the object just begun. True with its name in <paramref name="key"/> (a view into the
        /// buffer, valid until the next call that reads) and the scanner on its value; false at the closing brace, or on a failure
        /// (check <see cref="Failed"/>). <paramref name="first"/> is true for the first call on an object.</summary>
        public bool NextMember(bool first, out ReadOnlySpan<byte> key) {
            key = default(ReadOnlySpan<byte>);
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            byte c = buffer[position];
            if (c == (byte)'}') {
                position = position + 1;
                return false;
            }
            if (!first) {
                if (c != (byte)',') {
                    return Fail(WireParseResult.NotJson);
                }
                position = position + 1;
                SkipWhitespace();
                if (position >= end) {
                    return Fail(WireParseResult.Truncated);
                }
                c = buffer[position];
            }
            if (c != (byte)'"') {
                return Fail(WireParseResult.NotJson);
            }
            int start = position + 1;
            int i = start;
            while (i < end && buffer[i] != (byte)'"') {
                if (buffer[i] == (byte)'\\') {
                    i = i + 1;
                }
                i = i + 1;
            }
            if (i >= end) {
                return Fail(WireParseResult.Truncated);
            }
            key = new ReadOnlySpan<byte>(buffer, start, i - start);
            position = i + 1;
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            if (buffer[position] != (byte)':') {
                return Fail(WireParseResult.NotJson);
            }
            position = position + 1;
            SkipWhitespace();
            return true;
        }

        public bool BeginArray() {
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            if (buffer[position] != (byte)'[') {
                return Fail(WireParseResult.BadShape);
            }
            position = position + 1;
            return true;
        }

        /// <summary>True with the scanner on the next element of the array just begun; false at the closing bracket or on a failure.</summary>
        public bool NextElement(bool first) {
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            byte c = buffer[position];
            if (c == (byte)']') {
                position = position + 1;
                return false;
            }
            if (!first) {
                if (c != (byte)',') {
                    return Fail(WireParseResult.NotJson);
                }
                position = position + 1;
                SkipWhitespace();
                if (position >= end) {
                    return Fail(WireParseResult.Truncated);
                }
                if (buffer[position] == (byte)']') {
                    return Fail(WireParseResult.NotJson);
                }
            }
            return true;
        }

        public bool ReadFloat(out float value) {
            value = 0f;
            if (!PrepareNumber()) {
                return false;
            }
            int consumed;
            if (!Utf8Parser.TryParse(new ReadOnlySpan<byte>(buffer, position, end - position), out value, out consumed) || consumed <= 0) {
                value = 0f;
                return Fail(WireParseResult.NotJson);
            }
            if (float.IsNaN(value) || float.IsInfinity(value)) {
                value = 0f;
                return Fail(WireParseResult.NotJson);
            }
            position = position + consumed;
            return true;
        }

        public bool ReadDouble(out double value) {
            value = 0.0;
            if (!PrepareNumber()) {
                return false;
            }
            int consumed;
            if (!Utf8Parser.TryParse(new ReadOnlySpan<byte>(buffer, position, end - position), out value, out consumed) || consumed <= 0) {
                value = 0.0;
                return Fail(WireParseResult.NotJson);
            }
            if (double.IsNaN(value) || double.IsInfinity(value)) {
                value = 0.0;
                return Fail(WireParseResult.NotJson);
            }
            position = position + consumed;
            return true;
        }

        public bool ReadInt(out int value) {
            value = 0;
            if (!PrepareNumber()) {
                return false;
            }
            int consumed;
            if (!Utf8Parser.TryParse(new ReadOnlySpan<byte>(buffer, position, end - position), out value, out consumed) || consumed <= 0) {
                value = 0;
                return Fail(WireParseResult.NotJson);
            }
            position = position + consumed;
            return true;
        }

        public bool ReadLong(out long value) {
            value = 0L;
            if (!PrepareNumber()) {
                return false;
            }
            int consumed;
            if (!Utf8Parser.TryParse(new ReadOnlySpan<byte>(buffer, position, end - position), out value, out consumed) || consumed <= 0) {
                value = 0L;
                return Fail(WireParseResult.NotJson);
            }
            position = position + consumed;
            return true;
        }

        /// <summary>Reads a string value as the raw bytes between its quotes (escapes are not decoded: the wire's strings have none).</summary>
        public bool ReadString(out ReadOnlySpan<byte> value) {
            value = default(ReadOnlySpan<byte>);
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            if (buffer[position] != (byte)'"') {
                return Fail(WireParseResult.BadShape);
            }
            int start = position + 1;
            int i = start;
            while (i < end && buffer[i] != (byte)'"') {
                if (buffer[i] == (byte)'\\') {
                    i = i + 1;
                }
                i = i + 1;
            }
            if (i >= end) {
                return Fail(WireParseResult.Truncated);
            }
            value = new ReadOnlySpan<byte>(buffer, start, i - start);
            position = i + 1;
            return true;
        }

        /// <summary>Skips one value of any kind, including nested objects and arrays. How an unknown key is ignored.</summary>
        public bool SkipValue() {
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            byte c = buffer[position];
            if (c == (byte)'"') {
                return SkipString();
            }
            if (c == (byte)'{' || c == (byte)'[') {
                int depth = 0;
                while (position < end) {
                    byte d = buffer[position];
                    if (d == (byte)'"') {
                        if (!SkipString()) {
                            return false;
                        }
                        continue;
                    }
                    if (d == (byte)'{' || d == (byte)'[') {
                        depth = depth + 1;
                    } else if (d == (byte)'}' || d == (byte)']') {
                        depth = depth - 1;
                        if (depth == 0) {
                            position = position + 1;
                            return true;
                        }
                    }
                    position = position + 1;
                }
                return Fail(WireParseResult.Truncated);
            }
            if (c == (byte)'t') {
                return SkipLiteral(LiteralTrue);
            }
            if (c == (byte)'f') {
                return SkipLiteral(LiteralFalse);
            }
            if (c == (byte)'n') {
                return SkipLiteral(LiteralNull);
            }
            int begin = position;
            while (position < end && IsNumberByte(buffer[position])) {
                position = position + 1;
            }
            if (position == begin) {
                return Fail(WireParseResult.NotJson);
            }
            return true;
        }

        /// <summary>Reads <c>[x, y, z]</c>: exactly three numbers.</summary>
        public bool ReadVector3(out Vector3 value) {
            value = Vector3.zero;
            float x;
            float y;
            float z;
            if (!BeginArray()) {
                return false;
            }
            if (!ReadNumberElement(true, out x) || !ReadNumberElement(false, out y) || !ReadNumberElement(false, out z)) {
                return false;
            }
            if (NextElement(false)) {
                return Fail(WireParseResult.BadShape);
            }
            if (Failed) {
                return false;
            }
            value = new Vector3(x, y, z);
            return true;
        }

        /// <summary>Reads <c>[[x,y,z], ...]</c> into <paramref name="destination"/>; more than <paramref name="maxCount"/> elements is a shape failure.</summary>
        public bool ReadVector3Array(Vector3[] destination, int maxCount, out int count) {
            count = 0;
            if (!BeginArray()) {
                return false;
            }
            bool first = true;
            while (NextElement(first)) {
                first = false;
                if (count >= maxCount) {
                    return Fail(WireParseResult.BadShape);
                }
                Vector3 point;
                if (!ReadVector3(out point)) {
                    return false;
                }
                destination[count] = point;
                count = count + 1;
            }
            return !Failed;
        }

        /// <summary>Reads an array of exactly <paramref name="expectedCount"/> numbers into <paramref name="destination"/>.</summary>
        public bool ReadFloatArray(float[] destination, int expectedCount) {
            if (!BeginArray()) {
                return false;
            }
            int count = 0;
            bool first = true;
            while (NextElement(first)) {
                first = false;
                if (count >= expectedCount) {
                    return Fail(WireParseResult.BadShape);
                }
                float value;
                if (!ReadFloat(out value)) {
                    return false;
                }
                destination[count] = value;
                count = count + 1;
            }
            if (Failed) {
                return false;
            }
            return count == expectedCount ? true : Fail(WireParseResult.BadShape);
        }

        /// <summary>Reads an array of exactly <paramref name="expectedCount"/> 0/1 flags into <paramref name="destination"/>.</summary>
        public bool ReadFlagArray(bool[] destination, int expectedCount) {
            if (!BeginArray()) {
                return false;
            }
            int count = 0;
            bool first = true;
            while (NextElement(first)) {
                first = false;
                if (count >= expectedCount) {
                    return Fail(WireParseResult.BadShape);
                }
                int value;
                if (!ReadInt(out value)) {
                    return false;
                }
                destination[count] = value != 0;
                count = count + 1;
            }
            if (Failed) {
                return false;
            }
            return count == expectedCount ? true : Fail(WireParseResult.BadShape);
        }

        private bool ReadNumberElement(bool first, out float value) {
            value = 0f;
            if (!NextElement(first)) {
                if (!Failed) {
                    Fail(WireParseResult.BadShape);
                }
                return false;
            }
            return ReadFloat(out value);
        }

        private bool PrepareNumber() {
            if (Failed) {
                return false;
            }
            SkipWhitespace();
            if (position >= end) {
                return Fail(WireParseResult.Truncated);
            }
            byte c = buffer[position];
            if (c != (byte)'-' && (c < (byte)'0' || c > (byte)'9')) {
                return Fail(WireParseResult.NotJson);
            }
            return true;
        }

        private bool SkipString() {
            position = position + 1;
            while (position < end) {
                byte b = buffer[position];
                if (b == (byte)'\\') {
                    position = position + 2;
                } else if (b == (byte)'"') {
                    position = position + 1;
                    return true;
                } else {
                    position = position + 1;
                }
            }
            return Fail(WireParseResult.Truncated);
        }

        private bool SkipLiteral(byte[] literal) {
            if (position + literal.Length > end) {
                return Fail(WireParseResult.Truncated);
            }
            int i = 0;
            while (i < literal.Length) {
                if (buffer[position + i] != literal[i]) {
                    return Fail(WireParseResult.NotJson);
                }
                i = i + 1;
            }
            position = position + literal.Length;
            return true;
        }

        private void SkipWhitespace() {
            while (position < end) {
                byte c = buffer[position];
                if (c == (byte)' ' || c == (byte)'\t' || c == (byte)'\r' || c == (byte)'\n') {
                    position = position + 1;
                } else {
                    return;
                }
            }
        }

        private static bool IsNumberByte(byte b) {
            return (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'-' || b == (byte)'+' || b == (byte)'.' || b == (byte)'e' || b == (byte)'E';
        }

        private static readonly byte[] LiteralTrue = { (byte)'t', (byte)'r', (byte)'u', (byte)'e' };
        private static readonly byte[] LiteralFalse = { (byte)'f', (byte)'a', (byte)'l', (byte)'s', (byte)'e' };
        private static readonly byte[] LiteralNull = { (byte)'n', (byte)'u', (byte)'l', (byte)'l' };
    }
}
