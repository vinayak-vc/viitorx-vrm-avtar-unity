using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>What a "Video Source" value turned out to be.</summary>
    public enum VideoSourceKind {
        /// <summary>A file on disk.</summary>
        File = 0,
        /// <summary>An http(s) link, to a video file or to a page a downloader can resolve.</summary>
        Link = 1,
        /// <summary>A webcam by index, which is how a phone appears once an app exposes it as one.</summary>
        Camera = 2,
        /// <summary>A LIVE stream: an RTSP camera, or a phone "IP camera" app serving MJPEG over Wi-Fi.</summary>
        Stream = 3,
    }

    /// <summary>
    /// F-49 — turns what somebody typed into the "Video Test Source" Inspector field into the one
    /// string the sidecar is given, or says why it cannot.
    ///
    /// THE FIELD TAKES FIVE THINGS and nothing else:
    ///   * a link (http or https) to a video file, or to a page a downloader can resolve;
    ///   * an absolute path, or one relative to the Unity PROJECT folder (so
    ///     <c>Assets/Games/video/SR7.mp4</c> works the way it reads in the Project window);
    ///   * <c>camera:N</c> (or just a digit): a webcam. A phone becomes one when an app such as DroidCam,
    ///     Iriun or Camo exposes it to Windows;
    ///   * <c>rtsp://...</c>: an IP camera;
    ///   * <c>http://192.168.x.x:PORT/video</c> or <c>stream:&lt;url&gt;</c>: a phone IP-camera app. An
    ///     http link to a private/LAN address is a CAMERA, not a web page, and is read as a live stream.
    ///
    /// The value is later placed on a command line and, for a page link, handed to a downloader or to a
    /// video decoder, so it is checked here against an allowlist instead of being forwarded and hoped
    /// about: no other URL scheme, no quotes, no control characters. Credentials inside a link are
    /// never shown: use <see cref="Redact"/> for anything printed.
    ///
    /// PURE AND UNITY-FREE ON PURPOSE (plain System), so the rules can be asserted without a player.
    /// The Python end re-validates independently (<c>video_udp_sender.classify_source</c>); this class
    /// exists so a typo is reported in the Unity console at Play, not as a Python traceback in a log
    /// nobody opens.
    /// </summary>
    public static class VideoSourceSpec {
        /// <summary>Longest value accepted. A real link or path is far shorter; an absurd length is a
        /// paste accident or an attempt to overflow something downstream.</summary>
        public const int MaxLength = 2048;

        /// <summary>Highest webcam index accepted. Matches the sender.</summary>
        public const int MaxCameraIndex = 15;

        private const string StreamPrefix = "stream:";

        private static readonly Regex CameraPattern =
            new Regex(@"^(?:camera|cam):\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BareDigitPattern =
            new Regex(@"^\d{1,2}$", RegexOptions.CultureInvariant);

        private static readonly Regex CredentialPattern =
            new Regex(@"\b([a-z][a-z0-9+.\-]*://)[^/@\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v" };

        /// <summary>
        /// <paramref name="input"/> is the raw Inspector text. <paramref name="projectRoot"/> is the
        /// folder that contains <c>Assets</c>; relative paths are resolved against it.
        /// On success returns true and <paramref name="resolved"/> is the link, a canonical
        /// <c>camera:N</c>, or an absolute path with forward slashes. On failure returns false and
        /// <paramref name="error"/> says why.
        /// </summary>
        public static bool TryResolve(string input, string projectRoot, out string resolved,
                                      out string error) {
            resolved = string.Empty;
            error = string.Empty;

            string text = input == null ? string.Empty : input.Trim();
            // Pasting a path from Explorer ("Copy as path") wraps it in quotes.
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"') {
                text = text.Substring(1, text.Length - 2).Trim();
            }
            if (text.Length == 0) {
                error = "no video source given - paste a link, a path or camera:0 into 'Video Source'";
                return false;
            }
            if (text.Length > MaxLength) {
                error = "video source is longer than " + MaxLength + " characters";
                return false;
            }
            int i = 0;
            while (i < text.Length) {
                char c = text[i];
                if (c < 0x20 || c == 0x7F || c == '"') {
                    // A quote would end the quoted argument early and let the rest be read as further
                    // arguments; a control character has no business in a file name or a link.
                    error = "video source contains a quote or a control character";
                    return false;
                }
                i = i + 1;
            }

            // A webcam. A bare digit is a camera only when no file of that name exists, so a file that
            // happens to be called "0" still wins, exactly as it does in the sender.
            Match cam = CameraPattern.Match(text);
            bool bareDigit = !cam.Success && BareDigitPattern.IsMatch(text)
                             && !File.Exists(Path.Combine(projectRoot ?? string.Empty, text));
            if (cam.Success || bareDigit) {
                int index = int.Parse(cam.Success ? cam.Groups[1].Value : text,
                                      System.Globalization.CultureInfo.InvariantCulture);
                if (index > MaxCameraIndex) {
                    error = "camera index " + index + " is out of range (0-" + MaxCameraIndex + ")";
                    return false;
                }
                resolved = "camera:" + index;
                return true;
            }

            if (text.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase)) {
                string inner = text.Substring(StreamPrefix.Length).Trim();
                if (!TryAcceptLink(inner, out error)) {
                    return false;
                }
                resolved = StreamPrefix + inner;
                return true;
            }

            if (LooksLikeUrl(text)) {
                if (!TryAcceptLink(text, out error)) {
                    return false;
                }
                resolved = text;
                return true;
            }

            string full;
            try {
                full = Path.IsPathRooted(text)
                    ? Path.GetFullPath(text)
                    : Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, text));
            } catch (Exception ex) {
                error = "video path is not valid: " + ex.Message;
                return false;
            }
            if (!File.Exists(full)) {
                error = "video file not found: " + full;
                return false;
            }
            resolved = full.Replace('\\', '/');
            return true;
        }

        // The schemes OpenCV's FFmpeg backend and the downloader are allowed to be given.
        private static bool TryAcceptLink(string link, out string error) {
            error = string.Empty;
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) {
                error = "video source is not a valid link: " + Redact(link);
                return false;
            }
            bool allowed = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
                           || string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase);
            if (!allowed) {
                error = "only http, https and rtsp links are accepted, not '" + uri.Scheme + ":'";
                return false;
            }
            if (string.IsNullOrEmpty(uri.Host)) {
                error = "video link has no host: " + Redact(link);
                return false;
            }
            return true;
        }

        /// <summary>What a RESOLVED value is. Mirrors the sender's classification closely enough to
        /// label it on the HUD; the sender remains the authority on how it is opened.</summary>
        public static VideoSourceKind KindOf(string resolved) {
            if (string.IsNullOrEmpty(resolved)) {
                return VideoSourceKind.File;
            }
            if (resolved.StartsWith("camera:", StringComparison.OrdinalIgnoreCase)) {
                return VideoSourceKind.Camera;
            }
            if (resolved.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase)) {
                return VideoSourceKind.Stream;
            }
            if (!LooksLikeUrl(resolved)) {
                return VideoSourceKind.File;
            }
            Uri uri;
            if (!Uri.TryCreate(resolved, UriKind.Absolute, out uri)) {
                return VideoSourceKind.Link;
            }
            if (string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase)) {
                return VideoSourceKind.Stream;
            }
            string ext = Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath)).ToLowerInvariant();
            if (Array.IndexOf(VideoExtensions, ext) >= 0) {
                return VideoSourceKind.Link;
            }
            return IsLanHost(uri.Host) ? VideoSourceKind.Stream : VideoSourceKind.Link;
        }

        /// <summary>True for a source with no end, which has no loop and no replay: a camera or a stream.</summary>
        public static bool IsLive(string resolved) {
            VideoSourceKind kind = KindOf(resolved);
            return kind == VideoSourceKind.Camera || kind == VideoSourceKind.Stream;
        }

        /// <summary>Loopback, private or link-local address, or a .local name: where a phone serves from.</summary>
        public static bool IsLanHost(string host) {
            if (string.IsNullOrEmpty(host)) {
                return false;
            }
            string h = host.Trim('[', ']').ToLowerInvariant();
            if (h == "localhost" || h.EndsWith(".local", StringComparison.Ordinal)) {
                return true;
            }
            IPAddress ip;
            if (!IPAddress.TryParse(h, out ip)) {
                return false;
            }
            if (IPAddress.IsLoopback(ip)) {
                return true;
            }
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4) {
                return b[0] == 10
                       || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                       || (b[0] == 192 && b[1] == 168)
                       || (b[0] == 169 && b[1] == 254);
            }
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
        }

        /// <summary>A scheme of two or more letters followed by "://". One letter is a Windows drive
        /// (<c>C:/clip.mp4</c>), which is a path.</summary>
        public static bool LooksLikeUrl(string text) {
            int colon = text.IndexOf("://", StringComparison.Ordinal);
            if (colon < 2) {
                return false;
            }
            int i = 0;
            while (i < colon) {
                if (!char.IsLetter(text[i])) {
                    return false;
                }
                i = i + 1;
            }
            return true;
        }

        /// <summary>The same text with any <c>user:password@</c> inside a link removed. Everything that is
        /// printed, logged or shown goes through this: an IP-camera link commonly carries its login.</summary>
        public static string Redact(string text) {
            return string.IsNullOrEmpty(text) ? string.Empty : CredentialPattern.Replace(text, "$1***@");
        }

        /// <summary>A short, credential-free name for the HUD.</summary>
        public static string DisplayName(string resolved) {
            if (string.IsNullOrEmpty(resolved)) {
                return string.Empty;
            }
            VideoSourceKind kind = KindOf(resolved);
            if (kind == VideoSourceKind.Camera) {
                return "camera " + resolved.Substring("camera:".Length);
            }
            if (kind == VideoSourceKind.File) {
                return Path.GetFileName(resolved);
            }
            string link = resolved.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase)
                ? resolved.Substring(StreamPrefix.Length).Trim()
                : resolved;
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) {
                return Redact(link);
            }
            if (kind == VideoSourceKind.Stream) {
                return "stream " + uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
            }
            string last = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : string.Empty;
            last = last.Trim('/');
            return string.IsNullOrEmpty(last) ? uri.Host : uri.Host + "/" + last;
        }
    }
}
