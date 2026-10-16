using System.Globalization;
using System.Text;

using UnityEngine;

namespace VirtualMirror.Body {
    /// <summary>
    /// The command line of the engine's <c>body</c> command, built from <see cref="BodySettings"/>. Pure: no process, no file, no clock, so the mapping from
    /// a setting to a flag is testable. The flags are those of the body sender (<c>tools/video/video_udp_sender.py</c>, which <c>viitorx-tracking body</c>
    /// passes its options to); its <c>--loop</c> is a count (0 = forever), unlike the <c>--loop</c> switch of the hands, face and cutout senders.
    /// </summary>
    public static class BodyEngineArguments {
        /// <summary>The engine subcommand.</summary>
        public const string Command = "body";

        public const string LoopbackHost = "127.0.0.1";

        /// <summary>The flags after <c>body</c>. <paramref name="resolvedSource"/> is the source already checked by <c>VideoSourceSpec.TryResolve</c>.</summary>
        public static string Build(BodySettings settings, string resolvedSource) {
            StringBuilder sb = new StringBuilder();
            sb.Append("--video ").Append(Quote(resolvedSource));
            sb.Append(" --host ").Append(LoopbackHost);
            sb.Append(" --port ").Append(settings.Port);
            sb.Append(" --loop ").Append(settings.LoopFile ? 0 : 1);
            sb.Append(" --hfov ").Append(Number(Mathf.Clamp(settings.HorizontalFovDegrees, 20f, 140f)));
            sb.Append(" --subject-height ").Append(Number(Mathf.Clamp(settings.SubjectHeightMetres, 0.5f, 2.5f)));
            Vector2 range = settings.DepthRangeMetres;
            if (settings.ClampDepth && range.x > 0f && range.y > range.x) {
                sb.Append(" --depth-range ").Append(Number(range.x)).Append(' ').Append(Number(range.y));
            }
            int rotation = NormalizedRotation(settings.RotateDegrees);
            if (rotation != 0) {
                sb.Append(" --rotate ").Append(rotation);
            }
            if (settings.AutoOrient) {
                sb.Append(" --auto-orient --hfov-axis long");
            }
            if (!settings.PartialBody) {
                sb.Append(" --no-partial-body");
            }
            if (settings.FlipHorizontal) {
                sb.Append(" --flip");
            }
            Vector2Int size = settings.CameraSize;
            if (size.x >= 160 && size.y >= 120) {
                sb.Append(" --camera-size ").Append(size.x).Append(' ').Append(size.y);
            }
            if (settings.AssumeMeasuredDepth) {
                sb.Append(" --assume-measured");
            }
            return sb.ToString();
        }

        /// <summary>0, 90, 180 or 270; anything else is read as 0 rather than forwarded (the sender would refuse it and exit).</summary>
        public static int NormalizedRotation(int degrees) {
            if (degrees == 90 || degrees == 180 || degrees == 270) {
                return degrees;
            }
            return 0;
        }

        /// <summary>Double quotes around a value, with any quote inside it dropped: a source never legitimately contains one.</summary>
        private static string Quote(string value) {
            string safe = string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", string.Empty);
            return "\"" + safe + "\"";
        }

        private static string Number(float value) {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
