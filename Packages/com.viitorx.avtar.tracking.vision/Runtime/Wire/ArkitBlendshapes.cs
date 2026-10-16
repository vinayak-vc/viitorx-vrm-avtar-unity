using System;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Indexes into <see cref="FaceData.Blendshapes"/>: the 52 scores MediaPipe's blendshape network emits, in its order (<c>bsv</c> 1 on the wire).
    /// Index 0 is <c>_neutral</c>, MediaPipe's own extra channel (how neutral the face is); 1-51 are the ARKit-compatible blendshapes. There is NO
    /// tongueOut, so a rig that expects ARKit's 52 by position is off: bind by name (<see cref="IndexOf"/>). Left and Right are the SUBJECT's,
    /// as in ARKit. The names are the sidecar's own (face_landmarker.BLENDSHAPE_NAMES); a test pins this list against the wire fixtures.
    /// </summary>
    public static class ArkitBlendshapes {
        public const int Count = 52;

        public const int Neutral = 0;
        public const int BrowDownLeft = 1;
        public const int BrowDownRight = 2;
        public const int BrowInnerUp = 3;
        public const int BrowOuterUpLeft = 4;
        public const int BrowOuterUpRight = 5;
        public const int CheekPuff = 6;
        public const int CheekSquintLeft = 7;
        public const int CheekSquintRight = 8;
        public const int EyeBlinkLeft = 9;
        public const int EyeBlinkRight = 10;
        public const int EyeLookDownLeft = 11;
        public const int EyeLookDownRight = 12;
        public const int EyeLookInLeft = 13;
        public const int EyeLookInRight = 14;
        public const int EyeLookOutLeft = 15;
        public const int EyeLookOutRight = 16;
        public const int EyeLookUpLeft = 17;
        public const int EyeLookUpRight = 18;
        public const int EyeSquintLeft = 19;
        public const int EyeSquintRight = 20;
        public const int EyeWideLeft = 21;
        public const int EyeWideRight = 22;
        public const int JawForward = 23;
        public const int JawLeft = 24;
        public const int JawOpen = 25;
        public const int JawRight = 26;
        public const int MouthClose = 27;
        public const int MouthDimpleLeft = 28;
        public const int MouthDimpleRight = 29;
        public const int MouthFrownLeft = 30;
        public const int MouthFrownRight = 31;
        public const int MouthFunnel = 32;
        public const int MouthLeft = 33;
        public const int MouthLowerDownLeft = 34;
        public const int MouthLowerDownRight = 35;
        public const int MouthPressLeft = 36;
        public const int MouthPressRight = 37;
        public const int MouthPucker = 38;
        public const int MouthRight = 39;
        public const int MouthRollLower = 40;
        public const int MouthRollUpper = 41;
        public const int MouthShrugLower = 42;
        public const int MouthShrugUpper = 43;
        public const int MouthSmileLeft = 44;
        public const int MouthSmileRight = 45;
        public const int MouthStretchLeft = 46;
        public const int MouthStretchRight = 47;
        public const int MouthUpperUpLeft = 48;
        public const int MouthUpperUpRight = 49;
        public const int NoseSneerLeft = 50;
        public const int NoseSneerRight = 51;

        private static readonly string[] names = {
            "_neutral",
            "browDownLeft",
            "browDownRight",
            "browInnerUp",
            "browOuterUpLeft",
            "browOuterUpRight",
            "cheekPuff",
            "cheekSquintLeft",
            "cheekSquintRight",
            "eyeBlinkLeft",
            "eyeBlinkRight",
            "eyeLookDownLeft",
            "eyeLookDownRight",
            "eyeLookInLeft",
            "eyeLookInRight",
            "eyeLookOutLeft",
            "eyeLookOutRight",
            "eyeLookUpLeft",
            "eyeLookUpRight",
            "eyeSquintLeft",
            "eyeSquintRight",
            "eyeWideLeft",
            "eyeWideRight",
            "jawForward",
            "jawLeft",
            "jawOpen",
            "jawRight",
            "mouthClose",
            "mouthDimpleLeft",
            "mouthDimpleRight",
            "mouthFrownLeft",
            "mouthFrownRight",
            "mouthFunnel",
            "mouthLeft",
            "mouthLowerDownLeft",
            "mouthLowerDownRight",
            "mouthPressLeft",
            "mouthPressRight",
            "mouthPucker",
            "mouthRight",
            "mouthRollLower",
            "mouthRollUpper",
            "mouthShrugLower",
            "mouthShrugUpper",
            "mouthSmileLeft",
            "mouthSmileRight",
            "mouthStretchLeft",
            "mouthStretchRight",
            "mouthUpperUpLeft",
            "mouthUpperUpRight",
            "noseSneerLeft",
            "noseSneerRight"
        };

        /// <summary>The 52 names in index order. Do not modify.</summary>
        public static string[] Names {
            get {
                return names;
            }
        }

        public static string NameOf(int index) {
            if (index < 0 || index >= names.Length) {
                return string.Empty;
            }
            return names[index];
        }

        /// <summary>The index of a blendshape by name, ignoring case; -1 when it is not one of the 52.</summary>
        public static int IndexOf(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            int i = 0;
            while (i < names.Length) {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return i;
                }
                i = i + 1;
            }
            return -1;
        }

        /// <summary>The index of the same shape on the other side of the face (browDownLeft &lt;-&gt; browDownRight); the index itself for a shape with no side.</summary>
        public static int Mirror(int index) {
            string name = NameOf(index);
            if (name.EndsWith("Left", StringComparison.Ordinal)) {
                int other = IndexOf(name.Substring(0, name.Length - 4) + "Right");
                return other < 0 ? index : other;
            }
            if (name.EndsWith("Right", StringComparison.Ordinal)) {
                int other = IndexOf(name.Substring(0, name.Length - 5) + "Left");
                return other < 0 ? index : other;
            }
            return index;
        }
    }
}
