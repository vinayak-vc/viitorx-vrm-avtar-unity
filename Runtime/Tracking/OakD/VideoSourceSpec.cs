using System;
using System.IO;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// F-49 — turns what somebody typed into the "Video Test Source" Inspector field into the one
    /// string the sidecar is given, or says why it cannot.
    ///
    /// THE FIELD TAKES THREE THINGS and nothing else: a link (http or https), an absolute path, or a
    /// path relative to the Unity PROJECT folder (so <c>Assets/Games/video/SR7.mp4</c> works the way it
    /// reads in the Project window). The value is later placed on a command line and, for a page
    /// link, handed to a downloader, so it is checked here against an allowlist instead of being
    /// forwarded and hoped about: no other URL scheme, no quotes, no control characters.
    ///
    /// PURE AND UNITY-FREE ON PURPOSE (plain System.IO), so the rules can be asserted without a
    /// player. The Python end re-validates independently (<c>video_udp_sender.classify_source</c>);
    /// this class exists so a typo is reported in the Unity console at Play, not as a Python traceback
    /// in a log nobody opens.
    /// </summary>
    public static class VideoSourceSpec {
        /// <summary>Longest value accepted. A real link or path is far shorter; an absurd length is a
        /// paste accident or an attempt to overflow something downstream.</summary>
        public const int MaxLength = 2048;

        /// <summary>
        /// <paramref name="input"/> is the raw Inspector text. <paramref name="projectRoot"/> is the
        /// folder that contains <c>Assets</c>; relative paths are resolved against it.
        /// On success returns true and <paramref name="resolved"/> is the link or an absolute path
        /// with forward slashes. On failure returns false and <paramref name="error"/> says why.
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
                error = "no video source given - paste a link or a path into 'Video Source'";
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

            if (LooksLikeUrl(text)) {
                Uri uri;
                if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) {
                    error = "video source is not a valid link: " + text;
                    return false;
                }
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) {
                    error = "only http and https links are accepted, not '" + uri.Scheme + ":'";
                    return false;
                }
                if (string.IsNullOrEmpty(uri.Host)) {
                    error = "video link has no host: " + text;
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

        /// <summary>A short name for the HUD: the file name, or the link's host and last segment.</summary>
        public static string DisplayName(string resolved) {
            if (string.IsNullOrEmpty(resolved)) {
                return string.Empty;
            }
            if (LooksLikeUrl(resolved)) {
                Uri uri;
                if (Uri.TryCreate(resolved, UriKind.Absolute, out uri)) {
                    string last = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : string.Empty;
                    last = last.Trim('/');
                    return string.IsNullOrEmpty(last) ? uri.Host : uri.Host + "/" + last;
                }
            }
            return Path.GetFileName(resolved);
        }
    }
}
