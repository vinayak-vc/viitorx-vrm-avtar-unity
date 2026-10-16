using System;
using System.Text;

namespace VirtualMirror.Vision {
    /// <summary>The field names and string values of the hands and face wires, as bytes, so a parser compares them without building strings.</summary>
    internal static class WireKeys {
        public const int SupportedVersion = 1;

        public static readonly byte[] Ver = Ascii("ver");
        public static readonly byte[] Kind = Ascii("kind");
        public static readonly byte[] Sid = Ascii("sid");
        public static readonly byte[] Seq = Ascii("seq");
        public static readonly byte[] T = Ascii("t");
        public static readonly byte[] Lat = Ascii("lat");
        public static readonly byte[] Src = Ascii("src");
        public static readonly byte[] Img = Ascii("img");
        public static readonly byte[] Fpx = Ascii("fpx");
        public static readonly byte[] Mir = Ascii("mir");
        public static readonly byte[] N = Ascii("n");
        public static readonly byte[] Bsv = Ascii("bsv");
        public static readonly byte[] Hands = Ascii("hands");
        public static readonly byte[] Faces = Ascii("faces");

        public static readonly byte[] Id = Ascii("id");
        public static readonly byte[] Side = Ascii("side");
        public static readonly byte[] Hd = Ascii("hd");
        public static readonly byte[] Conf = Ascii("conf");
        public static readonly byte[] St = Ascii("st");
        public static readonly byte[] Lm = Ascii("lm");
        public static readonly byte[] Wl = Ascii("wl");
        public static readonly byte[] Pos = Ascii("pos");
        public static readonly byte[] Why = Ascii("why");
        public static readonly byte[] Fing = Ascii("fing");
        public static readonly byte[] Ext = Ascii("ext");
        public static readonly byte[] Pinch = Ascii("pinch");
        public static readonly byte[] Ges = Ascii("ges");

        public static readonly byte[] Bs = Ascii("bs");
        public static readonly byte[] Yaw = Ascii("yaw");
        public static readonly byte[] Pitch = Ascii("pitch");
        public static readonly byte[] Roll = Ascii("roll");
        public static readonly byte[] Rot = Ascii("rot");
        public static readonly byte[] Pose = Ascii("pose");

        public static readonly byte[] KindHands = Ascii("hands");
        public static readonly byte[] KindFace = Ascii("face");

        public static readonly byte[] SourceCamera = Ascii("camera");
        public static readonly byte[] SourceStream = Ascii("stream");
        public static readonly byte[] SourceLan = Ascii("lan");
        public static readonly byte[] SourceFile = Ascii("file");

        public static readonly byte[] SideLeft = Ascii("L");
        public static readonly byte[] SideRight = Ascii("R");

        public static readonly byte[] GestureOpen = Ascii("OPEN");
        public static readonly byte[] GestureFist = Ascii("FIST");
        public static readonly byte[] GesturePoint = Ascii("POINT");
        public static readonly byte[] GestureVictory = Ascii("VICTORY");
        public static readonly byte[] GestureRock = Ascii("ROCK");
        public static readonly byte[] GestureThumbsUp = Ascii("THUMBS_UP");
        public static readonly byte[] GestureOk = Ascii("OK");

        public static readonly byte[] RefusalShape = Ascii("shape");
        public static readonly byte[] RefusalDegenerate = Ascii("degenerate");
        public static readonly byte[] RefusalScale = Ascii("scale");
        public static readonly byte[] RefusalResidual = Ascii("residual");
        public static readonly byte[] RefusalRange = Ascii("range");

        public static bool Is(ReadOnlySpan<byte> value, byte[] name) {
            return value.SequenceEqual(new ReadOnlySpan<byte>(name));
        }

        public static bool EqualsAscii(ReadOnlySpan<byte> value, string text) {
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

        public static VisionSource ReadSource(ReadOnlySpan<byte> value) {
            if (Is(value, SourceCamera)) {
                return VisionSource.Camera;
            }
            if (Is(value, SourceStream)) {
                return VisionSource.Stream;
            }
            if (Is(value, SourceLan)) {
                return VisionSource.Lan;
            }
            if (Is(value, SourceFile)) {
                return VisionSource.File;
            }
            return VisionSource.Unknown;
        }

        public static HandGesture ReadGesture(ReadOnlySpan<byte> value) {
            if (Is(value, GestureOpen)) {
                return HandGesture.Open;
            }
            if (Is(value, GestureFist)) {
                return HandGesture.Fist;
            }
            if (Is(value, GesturePoint)) {
                return HandGesture.Point;
            }
            if (Is(value, GestureVictory)) {
                return HandGesture.Victory;
            }
            if (Is(value, GestureRock)) {
                return HandGesture.Rock;
            }
            if (Is(value, GestureThumbsUp)) {
                return HandGesture.ThumbsUp;
            }
            if (Is(value, GestureOk)) {
                return HandGesture.Ok;
            }
            return HandGesture.None;
        }

        public static PositionRefusal ReadRefusal(ReadOnlySpan<byte> value) {
            if (Is(value, RefusalShape)) {
                return PositionRefusal.Shape;
            }
            if (Is(value, RefusalDegenerate)) {
                return PositionRefusal.Degenerate;
            }
            if (Is(value, RefusalScale)) {
                return PositionRefusal.Scale;
            }
            if (Is(value, RefusalResidual)) {
                return PositionRefusal.Residual;
            }
            if (Is(value, RefusalRange)) {
                return PositionRefusal.Range;
            }
            return PositionRefusal.Unknown;
        }

        private static byte[] Ascii(string text) {
            return Encoding.ASCII.GetBytes(text);
        }
    }
}
