using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="WireScanner"/>: the allocation-free JSON reader under both wire parsers. These are the cases a datagram can actually produce
    /// (Python's number formats, whitespace, reordered and unknown members) and the malformed ones a receiver must survive without throwing.
    /// </summary>
    public sealed class VisionWireScannerTests {
        private static WireScanner Scan(string json) {
            byte[] bytes = VisionFixtures.Bytes(json);
            WireScanner scanner = new WireScanner();
            scanner.Reset(bytes, bytes.Length);
            return scanner;
        }

        private static float ReadOneFloat(string json, out bool ok) {
            WireScanner scanner = Scan(json);
            float value;
            ok = scanner.ReadFloat(out value);
            return value;
        }

        [Test]
        public void ReadFloat_ReadsEveryNumberFormatPythonWrites() {
            bool ok;
            Assert.AreEqual(0.5f, ReadOneFloat("0.5", out ok), 1e-7f);
            Assert.IsTrue(ok);
            Assert.AreEqual(-0.25f, ReadOneFloat("-0.25", out ok), 1e-7f);
            Assert.AreEqual(12f, ReadOneFloat("12", out ok), 1e-7f);
            Assert.AreEqual(1e-5f, ReadOneFloat("1e-05", out ok), 1e-10f);
            Assert.IsTrue(ok, "Python prints 0.00001 as 1e-05");
            Assert.AreEqual(-2.5e-7f, ReadOneFloat("-2.5e-07", out ok), 1e-12f);
            Assert.AreEqual(150000f, ReadOneFloat("1.5E5", out ok), 1e-2f);
            Assert.AreEqual(0f, ReadOneFloat("0.0", out ok), 0f);
            Assert.AreEqual(-0.0f, ReadOneFloat("-0.0", out ok), 0f);
            Assert.AreEqual(1791890123.456f, ReadOneFloat("1791890123.456", out ok), 200f);
        }

        [Test]
        public void ReadFloat_RefusesWhatIsNotAJsonNumber() {
            bool ok;
            ReadOneFloat("NaN", out ok);
            Assert.IsFalse(ok, "NaN is not JSON");
            ReadOneFloat("Infinity", out ok);
            Assert.IsFalse(ok);
            ReadOneFloat("-Infinity", out ok);
            Assert.IsFalse(ok);
            ReadOneFloat("+1", out ok);
            Assert.IsFalse(ok, "JSON has no leading plus");
            ReadOneFloat("abc", out ok);
            Assert.IsFalse(ok);
            ReadOneFloat("\"1\"", out ok);
            Assert.IsFalse(ok, "a string is not a number");
            ReadOneFloat("", out ok);
            Assert.IsFalse(ok);
            ReadOneFloat("1e999", out ok);
            Assert.IsFalse(ok, "a number that overflows float is not a usable value");
        }

        [Test]
        public void ReadFloat_StopsAtTheNextDelimiterAndIgnoresLeadingWhitespace() {
            WireScanner scanner = Scan("  [ 1.5 , 2 ]");
            Assert.IsTrue(scanner.BeginArray());
            float a;
            float b;
            Assert.IsTrue(scanner.NextElement(true));
            Assert.IsTrue(scanner.ReadFloat(out a));
            Assert.IsTrue(scanner.NextElement(false));
            Assert.IsTrue(scanner.ReadFloat(out b));
            Assert.IsFalse(scanner.NextElement(false));
            Assert.IsFalse(scanner.Failed);
            Assert.AreEqual(1.5f, a);
            Assert.AreEqual(2f, b);
        }

        [Test]
        public void ReadInt_AndLong_AndDouble() {
            WireScanner scanner = Scan("[42,-7,9007199254,1791890123.456]");
            int i = 0;
            int j = 0;
            long l = 0L;
            double d = 0.0;
            Assert.IsTrue(scanner.BeginArray());
            Assert.IsTrue(scanner.NextElement(true) && scanner.ReadInt(out i));
            Assert.IsTrue(scanner.NextElement(false) && scanner.ReadInt(out j));
            Assert.IsTrue(scanner.NextElement(false) && scanner.ReadLong(out l));
            Assert.IsTrue(scanner.NextElement(false) && scanner.ReadDouble(out d));
            Assert.AreEqual(42, i);
            Assert.AreEqual(-7, j);
            Assert.AreEqual(9007199254L, l);
            Assert.AreEqual(1791890123.456, d, 1e-6);
        }

        [Test]
        public void ReadString_ReturnsTheBytesBetweenTheQuotes() {
            WireScanner scanner = Scan("  \"hd0123456789\"");
            System.ReadOnlySpan<byte> value;
            Assert.IsTrue(scanner.ReadString(out value));
            Assert.IsTrue(WireKeysEquals(value, "hd0123456789"));
        }

        [Test]
        public void ReadString_SkipsAnEscapedQuoteAndFailsOnAnUnterminatedString() {
            WireScanner scanner = Scan("\"a\\\"b\"");
            System.ReadOnlySpan<byte> value;
            Assert.IsTrue(scanner.ReadString(out value));
            Assert.AreEqual(4, value.Length, "the raw bytes a \\ \" b, escapes not decoded");
            WireScanner broken = Scan("\"never closed");
            Assert.IsFalse(broken.ReadString(out value));
            Assert.AreEqual(WireParseResult.Truncated, broken.Failure);
        }

        [Test]
        public void NextMember_WalksAnObjectInAnyKeyOrder() {
            WireScanner scanner = Scan("{ \"b\" : 2 , \"a\":1 }");
            Assert.IsTrue(scanner.BeginObject());
            System.ReadOnlySpan<byte> key;
            int value;
            Assert.IsTrue(scanner.NextMember(true, out key));
            Assert.IsTrue(WireKeysEquals(key, "b"));
            Assert.IsTrue(scanner.ReadInt(out value));
            Assert.AreEqual(2, value);
            Assert.IsTrue(scanner.NextMember(false, out key));
            Assert.IsTrue(WireKeysEquals(key, "a"));
            Assert.IsTrue(scanner.ReadInt(out value));
            Assert.IsFalse(scanner.NextMember(false, out key));
            Assert.IsFalse(scanner.Failed, "reaching the closing brace is the end, not a failure");
        }

        [Test]
        public void NextMember_AnEmptyObjectEndsAtOnce() {
            WireScanner scanner = Scan("{}");
            System.ReadOnlySpan<byte> key;
            Assert.IsTrue(scanner.BeginObject());
            Assert.IsFalse(scanner.NextMember(true, out key));
            Assert.IsFalse(scanner.Failed);
        }

        [Test]
        public void NextMember_RejectsMalformedObjects() {
            System.ReadOnlySpan<byte> key;
            WireScanner trailing = Scan("{\"a\":1,}");
            trailing.BeginObject();
            Assert.IsTrue(trailing.NextMember(true, out key));
            int ignored;
            trailing.ReadInt(out ignored);
            Assert.IsFalse(trailing.NextMember(false, out key));
            Assert.IsTrue(trailing.Failed, "a trailing comma is not JSON");

            WireScanner noColon = Scan("{\"a\" 1}");
            noColon.BeginObject();
            Assert.IsFalse(noColon.NextMember(true, out key));
            Assert.AreEqual(WireParseResult.NotJson, noColon.Failure);

            WireScanner unquoted = Scan("{a:1}");
            unquoted.BeginObject();
            Assert.IsFalse(unquoted.NextMember(true, out key));
            Assert.AreEqual(WireParseResult.NotJson, unquoted.Failure);

            WireScanner cut = Scan("{\"a\":1");
            cut.BeginObject();
            cut.NextMember(true, out key);
            cut.ReadInt(out ignored);
            Assert.IsFalse(cut.NextMember(false, out key));
            Assert.AreEqual(WireParseResult.Truncated, cut.Failure);

            WireScanner notObject = Scan("[1]");
            Assert.IsFalse(notObject.BeginObject());
            Assert.AreEqual(WireParseResult.NotJson, notObject.Failure);

            WireScanner blank = Scan("   ");
            Assert.IsFalse(blank.BeginObject());
            Assert.AreEqual(WireParseResult.Empty, blank.Failure);
        }

        [Test]
        public void SkipValue_SkipsEveryKindOfValueIncludingNestedAndTrickyStrings() {
            string[] values = {
                "123", "-1.5e-3", "\"plain\"", "\"has } and ] and { inside\"", "\"esc \\\" quote\"", "true", "false", "null",
                "[]", "[1,2,[3,[4]]]", "{}", "{\"a\":{\"b\":[1,{\"c\":\"}\"}]}}", "[\"]\",\"[\"]"
            };
            int i = 0;
            while (i < values.Length) {
                WireScanner scanner = Scan("[" + values[i] + ",7]");
                int seven;
                Assert.IsTrue(scanner.BeginArray());
                Assert.IsTrue(scanner.NextElement(true));
                Assert.IsTrue(scanner.SkipValue(), "skipping " + values[i]);
                Assert.IsTrue(scanner.NextElement(false), "after skipping " + values[i]);
                Assert.IsTrue(scanner.ReadInt(out seven), "reading on after " + values[i]);
                Assert.AreEqual(7, seven, "landed on the next value after " + values[i]);
                i = i + 1;
            }
        }

        [Test]
        public void SkipValue_FailsOnATruncatedValue() {
            Assert.IsFalse(Scan("{\"a\":[1,2").SkipValue());
            Assert.IsFalse(Scan("\"open").SkipValue());
            Assert.IsFalse(Scan("tru").SkipValue());
            Assert.IsFalse(Scan("trux").SkipValue());
        }

        [Test]
        public void ReadVector3_ReadsExactlyThreeNumbers() {
            Vector3 v;
            Assert.IsTrue(Scan("[1, -2.5, 3e0]").ReadVector3(out v));
            Assert.AreEqual(new Vector3(1f, -2.5f, 3f), v);
            Assert.IsFalse(Scan("[1,2]").ReadVector3(out v), "two numbers");
            Assert.IsFalse(Scan("[1,2,3,4]").ReadVector3(out v), "four numbers (a body landmark's visibility is not part of a hand or face point)");
            Assert.IsFalse(Scan("[1,2,\"x\"]").ReadVector3(out v));
            Assert.IsFalse(Scan("[]").ReadVector3(out v));
            Assert.IsFalse(Scan("1").ReadVector3(out v));
        }

        [Test]
        public void ReadVector3Array_ReadsAllAndRefusesMoreThanTheCapacity() {
            Vector3[] destination = new Vector3[3];
            int count;
            Assert.IsTrue(Scan("[[1,2,3],[4,5,6]]").ReadVector3Array(destination, 3, out count));
            Assert.AreEqual(2, count);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), destination[1]);
            Assert.IsTrue(Scan("[]").ReadVector3Array(destination, 3, out count));
            Assert.AreEqual(0, count);
            Assert.IsFalse(Scan("[[1,2,3],[4,5,6],[7,8,9],[1,1,1]]").ReadVector3Array(destination, 3, out count), "a fourth point does not fit");
            Assert.IsFalse(Scan("[[1,2,3],[4,5]]").ReadVector3Array(destination, 3, out count), "a short point");
        }

        [Test]
        public void ReadFloatArray_AndFlagArray_RequireTheExactCount() {
            float[] values = new float[3];
            bool[] flags = new bool[3];
            Assert.IsTrue(Scan("[0.1,0.2,0.3]").ReadFloatArray(values, 3));
            Assert.AreEqual(0.2f, values[1], 1e-7f);
            Assert.IsFalse(Scan("[0.1,0.2]").ReadFloatArray(values, 3), "too few");
            Assert.IsFalse(Scan("[0.1,0.2,0.3,0.4]").ReadFloatArray(values, 3), "too many");
            Assert.IsTrue(Scan("[1,0,1]").ReadFlagArray(flags, 3));
            Assert.IsTrue(flags[0]);
            Assert.IsFalse(flags[1]);
            Assert.IsTrue(flags[2]);
            Assert.IsFalse(Scan("[1,0]").ReadFlagArray(flags, 3));
        }

        [Test]
        public void ArrayWalking_RejectsATrailingCommaAndAMissingComma() {
            WireScanner trailing = Scan("[1,]");
            float value;
            trailing.BeginArray();
            trailing.NextElement(true);
            trailing.ReadFloat(out value);
            Assert.IsFalse(trailing.NextElement(false));
            Assert.IsTrue(trailing.Failed);

            WireScanner missing = Scan("[1 2]");
            missing.BeginArray();
            missing.NextElement(true);
            missing.ReadFloat(out value);
            Assert.IsFalse(missing.NextElement(false));
            Assert.IsTrue(missing.Failed);
        }

        [Test]
        public void TheFirstFailureSticksAndResetClearsIt() {
            WireScanner scanner = Scan("{");
            Assert.IsTrue(scanner.BeginObject());
            System.ReadOnlySpan<byte> key;
            Assert.IsFalse(scanner.NextMember(true, out key));
            Assert.AreEqual(WireParseResult.Truncated, scanner.Failure);
            Assert.IsFalse(scanner.BeginArray(), "every later call returns false");
            Assert.AreEqual(WireParseResult.Truncated, scanner.Failure, "and the first reason is kept");
            byte[] fresh = VisionFixtures.Bytes("{}");
            scanner.Reset(fresh, fresh.Length);
            Assert.IsFalse(scanner.Failed);
            Assert.IsTrue(scanner.BeginObject());
        }

        [Test]
        public void ResetHonoursTheLengthNotTheArray() {
            byte[] padded = VisionFixtures.Bytes("[1,2]garbage that is not part of the datagram");
            WireScanner scanner = new WireScanner();
            scanner.Reset(padded, 5);
            float value;
            Assert.IsTrue(scanner.BeginArray());
            Assert.IsTrue(scanner.NextElement(true) && scanner.ReadFloat(out value));
            Assert.IsTrue(scanner.NextElement(false) && scanner.ReadFloat(out value));
            Assert.IsFalse(scanner.NextElement(false));
            Assert.IsFalse(scanner.Failed);
            scanner.Reset(null, 10);
            Assert.IsFalse(scanner.BeginObject());
            scanner.Reset(padded, -3);
            Assert.IsFalse(scanner.BeginObject());
        }

        private static bool WireKeysEquals(System.ReadOnlySpan<byte> value, string text) {
            if (value.Length != text.Length) {
                return false;
            }
            int i = 0;
            while (i < value.Length) {
                if (value[i] != (byte)text[i]) {
                    return false;
                }
                i = i + 1;
            }
            return true;
        }
    }
}
