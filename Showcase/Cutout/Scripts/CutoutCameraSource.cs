using System;
using System.Globalization;

using VirtualMirror.Tracking.OakD;

namespace CutoutShowcase {
    /// <summary>
    /// Stepping through webcams for the Cutout showcase: the source text <c>camera:N</c> (or a bare digit) becomes the next or previous index, and
    /// anything that is not a webcam (a video file, a link, a LAN camera) becomes camera 0 whichever way the step goes. Pure text in, text out, so
    /// the rule is testable without a camera. Which indices actually open on a machine is the sender's to say; this only walks the numbers the
    /// sender accepts (0 to <see cref="VideoSourceSpec.MaxCameraIndex"/>), and wraps.
    /// </summary>
    public static class CutoutCameraSource {
        private const string Prefix = "camera:";

        /// <summary>The webcam <paramref name="steps"/> away from <paramref name="current"/> (negative goes back), wrapping at both ends; camera 0 when <paramref name="current"/> is not a webcam.</summary>
        public static string Step(string current, int steps) {
            int count = VideoSourceSpec.MaxCameraIndex + 1;
            int index;
            if (!TryIndexOf(current, out index)) {
                return Prefix + "0";
            }
            int next = ((index + steps) % count + count) % count;
            return Prefix + next.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>True when <paramref name="source"/> names a webcam, <c>camera:N</c> or a bare digit, and gives N.</summary>
        public static bool TryIndexOf(string source, out int index) {
            index = 0;
            if (string.IsNullOrWhiteSpace(source)) {
                return false;
            }
            string text = source.Trim();
            if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) {
                text = text.Substring(Prefix.Length).Trim();
            } else if (text.StartsWith("cam:", StringComparison.OrdinalIgnoreCase)) {
                text = text.Substring(4).Trim();
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0 && index <= VideoSourceSpec.MaxCameraIndex;
        }
    }
}
