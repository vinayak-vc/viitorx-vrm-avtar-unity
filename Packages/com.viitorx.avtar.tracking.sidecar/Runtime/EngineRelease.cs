using System;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// What an installer needs to fetch and check one release of the tracking engine: the descriptor <c>viitorx-tracking-&lt;version&gt;-win-x64.release.json</c> that
    /// <c>python-sidecar~/build/build_engine.py assemble</c> writes beside the zip. Everything in it is untrusted text until <see cref="TryParse"/> has accepted it:
    /// names that become folder or file names are checked against path tricks, sizes against limits, the SHA-256 against its shape.
    /// </summary>
    public sealed class EngineRelease {
        /// <summary>Largest zip an installer will accept, bytes. The engine's zip is about 125 MB.</summary>
        public const long MaxZipBytes = 2L * 1024L * 1024L * 1024L;

        /// <summary>Largest unpacked size an installer will accept, bytes. The engine unpacks to about 345 MB.</summary>
        public const long MaxUnpackedBytes = 4L * 1024L * 1024L * 1024L;

        public const int SupportedSchema = 1;
        public const string SupportedPlatform = "win-x64";

        private static readonly Regex SafeName = new Regex("^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex Sha256Shape = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

        public string Name = string.Empty;
        public string Version = string.Empty;
        public string File = string.Empty;

        /// <summary>Where the zip is published; empty when the publisher has not filled it in (an installer then needs a URL given to it, or a zip it already has).</summary>
        public string Url = string.Empty;

        /// <summary>Lower-case hex SHA-256 of the zip.</summary>
        public string Sha256 = string.Empty;

        public long SizeBytes;
        public long UnpackedBytes;

        /// <summary>The one folder the zip's entries sit under.</summary>
        public string TopFolder = string.Empty;

        public string Exe = string.Empty;
        public string WireSpec = string.Empty;

        /// <summary>True when <paramref name="name"/> is one plain file or folder name: no separator, no dot-dot, no drive, no leading dot.</summary>
        public static bool IsSafeName(string name) {
            return name != null && SafeName.IsMatch(name) && !name.Contains("..");
        }

        public static bool TryParse(string json, out EngineRelease release, out string error) {
            release = null;
            error = string.Empty;
            JObject root;
            try {
                root = JObject.Parse(json ?? string.Empty);
            } catch (Exception exception) {
                error = "the release descriptor is not JSON: " + exception.Message;
                return false;
            }
            int schema = Int(root, "schema");
            if (schema != SupportedSchema) {
                error = "the release descriptor has schema " + schema + "; this package reads schema " + SupportedSchema + ".";
                return false;
            }
            string platform = Text(root, "platform");
            if (platform != SupportedPlatform) {
                error = "the release is for '" + platform + "', not " + SupportedPlatform + ".";
                return false;
            }
            EngineRelease parsed = new EngineRelease();
            parsed.Name = Text(root, "name");
            parsed.Version = Text(root, "version");
            parsed.File = Text(root, "file");
            parsed.Url = Text(root, "url");
            parsed.Sha256 = Text(root, "sha256").ToLowerInvariant();
            parsed.SizeBytes = Long(root, "sizeBytes");
            parsed.UnpackedBytes = Long(root, "unpackedBytes");
            parsed.TopFolder = Text(root, "topFolder");
            parsed.Exe = Text(root, "exe");
            parsed.WireSpec = Text(root, "wireSpec");
            if (!IsSafeName(parsed.Version)) {
                error = "the release's version '" + parsed.Version + "' is not a plain name (it becomes a folder name).";
                return false;
            }
            if (!IsSafeName(parsed.TopFolder)) {
                error = "the release's topFolder '" + parsed.TopFolder + "' is not a plain folder name.";
                return false;
            }
            if (!IsSafeName(parsed.Exe) || !parsed.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                error = "the release's exe '" + parsed.Exe + "' is not a plain .exe file name.";
                return false;
            }
            if (!Sha256Shape.IsMatch(parsed.Sha256)) {
                error = "the release's sha256 is not 64 hexadecimal characters.";
                return false;
            }
            if (parsed.SizeBytes <= 0L || parsed.SizeBytes > MaxZipBytes) {
                error = "the release's sizeBytes (" + parsed.SizeBytes + ") is outside 1 .. " + MaxZipBytes + ".";
                return false;
            }
            if (parsed.UnpackedBytes <= 0L || parsed.UnpackedBytes > MaxUnpackedBytes) {
                error = "the release's unpackedBytes (" + parsed.UnpackedBytes + ") is outside 1 .. " + MaxUnpackedBytes + ".";
                return false;
            }
            release = parsed;
            return true;
        }

        private static string Text(JObject root, string key) {
            JToken token = root[key];
            return token != null && token.Type == JTokenType.String ? token.Value<string>() : string.Empty;
        }

        private static long Long(JObject root, string key) {
            JToken token = root[key];
            return token != null && (token.Type == JTokenType.Integer) ? token.Value<long>() : 0L;
        }

        private static int Int(JObject root, string key) {
            return (int)Math.Max(0L, Math.Min(int.MaxValue, Long(root, key)));
        }
    }
}
