using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD {
    public enum EngineInstallStatus {
        Installed = 0,
        AlreadyInstalled = 1,
        Failed = 2,
        Cancelled = 3
    }

    public sealed class EngineInstallResult {
        public EngineInstallStatus Status;
        public string Folder = string.Empty;
        public string ExePath = string.Empty;
        public string Message = string.Empty;

        public bool Ok {
            get {
                return Status == EngineInstallStatus.Installed || Status == EngineInstallStatus.AlreadyInstalled;
            }
        }
    }

    /// <summary>
    /// Fetches one release of the tracking engine and installs it where <see cref="EngineLocator"/> looks (<c>%LOCALAPPDATA%\Viitorx\TrackingEngine\&lt;version&gt;</c>),
    /// without administrator rights. The order is the point: the zip is downloaded to a temporary file while its SHA-256 is computed, and NOTHING is unpacked
    /// or run unless the size and the SHA-256 are exactly those of the release descriptor; it is unpacked into a temporary folder (every entry checked against
    /// path tricks and size limits), the engine is started once with <c>version --json</c> to see that it runs and is the version it claims, and only then does the
    /// folder move to its final name. Any failure or cancellation leaves no partial folder and no temporary file.
    ///
    /// What the SHA-256 protects against: a corrupt, truncated, wrong or swapped file. It does NOT protect against a publisher whose descriptor and zip are
    /// both replaced: the descriptor must come from somewhere you trust (commit it to your project; do not fetch it from the host that serves the zip). The
    /// engine is not code-signed. https is required, except to the loopback host (tests, a local mirror).
    /// </summary>
    public sealed class EngineInstaller {
        private const int BufferBytes = 81920;
        private const int MaxEntries = 20000;

        // Windows (and Unity's runtime) cannot open a path longer than this; the engine has paths of 112 characters of its own, so the root must leave room for them.
        private const int MaxPathChars = 259;
        private const int VersionCheckSeconds = 60;
        private static readonly HttpClient Client = CreateClient();

        private readonly ILogService log;
        private readonly string root;

        public EngineInstaller(ILogService log, string root) {
            if (log == null) {
                throw new ArgumentNullException(nameof(log));
            }
            if (string.IsNullOrWhiteSpace(root)) {
                throw new ArgumentException("An install root is required.", nameof(root));
            }
            this.log = log;
            this.root = Path.GetFullPath(root);
        }

        /// <summary>Start the engine once with <c>version --json</c> after unpacking, and require the version the descriptor names. On by default; a test with a stand-in exe turns it off.</summary>
        public bool RunVersionCheck = true;

        /// <summary><c>%LOCALAPPDATA%\Viitorx\TrackingEngine</c>: the folder whose children are version folders, which the engine locator reads.</summary>
        public static string DefaultRoot() {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viitorx", "TrackingEngine");
        }

        public string Root {
            get {
                return root;
            }
        }

        /// <summary>The folder a release installs to.</summary>
        public string FolderOf(EngineRelease release) {
            return Path.Combine(root, release.Version);
        }

        /// <summary>True when http is acceptable for this address: only to the loopback host. Everything else must be https.</summary>
        public static bool IsAllowedUrl(string url, out string reason) {
            reason = string.Empty;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) {
                reason = "the download address '" + url + "' is not an absolute URL.";
                return false;
            }
            if (uri.Scheme == Uri.UriSchemeHttps) {
                return true;
            }
            if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) {
                return true;
            }
            reason = "the download address must be https (http is accepted only for the loopback host): " + uri.Scheme + "://" + uri.Host;
            return false;
        }

        /// <summary>
        /// Downloads (unless the version is already installed), verifies and installs <paramref name="release"/>. <paramref name="urlOverride"/> replaces the descriptor's url
        /// (a mirror); one of the two must be set. <paramref name="progress"/> gets 0..1 across the download and the unpacking.
        /// </summary>
        public async Task<EngineInstallResult> InstallAsync(EngineRelease release, string urlOverride, CancellationToken cancellationToken, IProgress<float> progress) {
            if (release == null) {
                return Fail("No release given.");
            }
            EngineInstallResult already = CheckInstalled(release);
            if (already != null) {
                return already;
            }
            string url = string.IsNullOrWhiteSpace(urlOverride) ? release.Url : urlOverride;
            if (string.IsNullOrWhiteSpace(url)) {
                return Fail("The release has no url (the publisher has not filled it in) and none was given.");
            }
            string reason;
            if (!IsAllowedUrl(url, out reason)) {
                return Fail(reason);
            }
            string zipPath = null;
            try {
                Directory.CreateDirectory(root);
                zipPath = Path.Combine(root, ".d" + ShortId() + ".zip");
                log.Log(LogLevel.Info, "Engine installer: downloading " + url + " (" + release.SizeBytes + " bytes expected).");
                string problem = await Download(url, zipPath, release, cancellationToken, progress).ConfigureAwait(false);
                if (problem != null) {
                    return Fail(problem);
                }
                return InstallVerifiedZip(release, zipPath, progress);
            } catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // a cancelled read ends as OperationCanceled, ObjectDisposed or IO depending on the runtime: all of them mean the caller said stop
                return new EngineInstallResult { Status = EngineInstallStatus.Cancelled, Message = "Cancelled." };
            } catch (HttpRequestException exception) {
                return Fail("The download failed: " + exception.Message);
            } catch (IOException exception) {
                return Fail("The download could not be read or written: " + exception.Message);
            } catch (InvalidDataException exception) {
                return Fail("The download is not a zip archive: " + exception.Message);
            } catch (UnauthorizedAccessException exception) {
                return Fail("The install folder is not writable: " + exception.Message);
            } finally {
                DeleteQuietly(zipPath);
            }
        }

        /// <summary>Installs from a zip that is already on disk (an offline machine): the same size and SHA-256 check, then the same unpacking.</summary>
        public EngineInstallResult InstallFromZip(EngineRelease release, string zipPath) {
            if (release == null) {
                return Fail("No release given.");
            }
            EngineInstallResult already = CheckInstalled(release);
            if (already != null) {
                return already;
            }
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) {
                return Fail("The zip '" + zipPath + "' does not exist.");
            }
            long size = new FileInfo(zipPath).Length;
            if (size != release.SizeBytes) {
                return Fail("The zip is " + size + " bytes; the release says " + release.SizeBytes + ".");
            }
            string actual;
            using (FileStream stream = File.OpenRead(zipPath)) {
                using (SHA256 sha = SHA256.Create()) {
                    actual = Hex(sha.ComputeHash(stream));
                }
            }
            if (!string.Equals(actual, release.Sha256, StringComparison.Ordinal)) {
                return Fail("The zip's SHA-256 is " + actual + "; the release says " + release.Sha256 + ". Nothing was installed.");
            }
            try {
                Directory.CreateDirectory(root);
                return InstallVerifiedZip(release, zipPath, null);
            } catch (IOException exception) {
                return Fail("The install failed: " + exception.Message);
            } catch (InvalidDataException exception) {
                return Fail("The file is not a zip archive: " + exception.Message);
            } catch (UnauthorizedAccessException exception) {
                return Fail("The install folder is not writable: " + exception.Message);
            }
        }

        private EngineInstallResult CheckInstalled(EngineRelease release) {
            string folder = FolderOf(release);
            string exe = Path.Combine(folder, release.Exe);
            if (File.Exists(exe)) {
                log.Log(LogLevel.Info, "Engine installer: version " + release.Version + " is already installed at " + folder + ".");
                return new EngineInstallResult { Status = EngineInstallStatus.AlreadyInstalled, Folder = folder, ExePath = exe, Message = "Already installed." };
            }
            return null;
        }

        // Streams the response into zipPath, hashing as it goes; returns null on success or the reason it cannot be trusted.
        private async Task<string> Download(string url, string zipPath, EngineRelease release, CancellationToken cancellationToken, IProgress<float> progress) {
            using (HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                if (!response.IsSuccessStatusCode) {
                    return "The server answered " + (int)response.StatusCode + " " + response.ReasonPhrase + ".";
                }
                long? declared = response.Content.Headers.ContentLength;
                if (declared.HasValue && declared.Value != release.SizeBytes) {
                    return "The server announces " + declared.Value + " bytes; the release says " + release.SizeBytes + ".";
                }
                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                // some runtimes (Unity's Mono) do not stop a read that is waiting for data when the token is cancelled: closing the stream does
                using (cancellationToken.Register(delegate { body.Dispose(); }))
                using (FileStream file = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                    byte[] buffer = new byte[BufferBytes];
                    long total = 0L;
                    int read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    while (read > 0) {
                        total = total + read;
                        if (total > release.SizeBytes) {
                            return "The download is larger than the " + release.SizeBytes + " bytes the release says; stopped.";
                        }
                        hash.AppendData(buffer, 0, read);
                        file.Write(buffer, 0, read);
                        if (progress != null) {
                            progress.Report(0.9f * total / release.SizeBytes);
                        }
                        read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    }
                    if (total != release.SizeBytes) {
                        return "The download ended after " + total + " bytes; the release says " + release.SizeBytes + ".";
                    }
                    string actual = Hex(hash.GetHashAndReset());
                    if (!string.Equals(actual, release.Sha256, StringComparison.Ordinal)) {
                        return "The download's SHA-256 is " + actual + "; the release says " + release.Sha256 + ". Nothing was installed.";
                    }
                }
            }
            return null;
        }

        // The zip's size and SHA-256 have been checked. Unpack, look, move.
        private EngineInstallResult InstallVerifiedZip(EngineRelease release, string zipPath, IProgress<float> progress) {
            string partial = Path.Combine(root, ".p" + ShortId());
            try {
                string problem = Unpack(release, zipPath, partial, progress);
                if (problem != null) {
                    return Fail(problem);
                }
                string unpacked = Path.Combine(partial, release.TopFolder);
                string exe = Path.Combine(unpacked, release.Exe);
                if (!File.Exists(exe)) {
                    return Fail("The archive has no " + release.TopFolder + "/" + release.Exe + ".");
                }
                if (!File.Exists(Path.Combine(unpacked, "manifest.json"))) {
                    return Fail("The archive has no manifest.json: it is not an engine release.");
                }
                if (RunVersionCheck) {
                    string versionProblem = CheckVersion(exe, release);
                    if (versionProblem != null) {
                        return Fail(versionProblem);
                    }
                }
                string target = FolderOf(release);
                if (Directory.Exists(target)) {
                    // another installer finished first; its result stands
                    EngineInstallResult other = CheckInstalled(release);
                    return other ?? Fail("The folder " + target + " exists but holds no " + release.Exe + "; remove it and install again.");
                }
                Directory.Move(unpacked, target);
                log.Log(LogLevel.Info, "Engine installer: installed " + release.Version + " at " + target + ".");
                if (progress != null) {
                    progress.Report(1f);
                }
                return new EngineInstallResult { Status = EngineInstallStatus.Installed, Folder = target, ExePath = Path.Combine(target, release.Exe), Message = "Installed." };
            } finally {
                DeleteDirectoryQuietly(partial);
            }
        }

        // Extracts only entries under release.TopFolder, each to a path that is inside `destination`, within the entry-count and size limits.
        private string Unpack(EngineRelease release, string zipPath, string destination, IProgress<float> progress) {
            string prefix = release.TopFolder + "/";
            string destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long limit = release.UnpackedBytes + release.UnpackedBytes / 10L + 1048576L;
            long written = 0L;
            Directory.CreateDirectory(destination);
            using (FileStream file = File.OpenRead(zipPath))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read)) {
                if (archive.Entries.Count > MaxEntries) {
                    return "The archive has " + archive.Entries.Count + " entries (limit " + MaxEntries + ").";
                }
                int done = 0;
                foreach (ZipArchiveEntry entry in archive.Entries) {
                    done = done + 1;
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)) {
                        return "The archive has an entry outside " + release.TopFolder + "/: '" + entry.FullName + "'.";
                    }
                    string target = Path.GetFullPath(Path.Combine(destination, name));
                    if (!target.StartsWith(destinationRoot, StringComparison.Ordinal)) {
                        return "The archive has an entry that leaves the install folder: '" + entry.FullName + "'.";
                    }
                    if (target.Length > MaxPathChars) {
                        return "The install folder path is too long: '" + target + "' would be " + target.Length + " characters (the limit is " + MaxPathChars
                            + "). Install under a shorter folder.";
                    }
                    if (name.EndsWith("/", StringComparison.Ordinal)) {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    written = written + entry.Length;
                    if (written > limit) {
                        return "The archive unpacks to more than the " + release.UnpackedBytes + " bytes the release says; stopped.";
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        input.CopyTo(output, BufferBytes);
                    }
                    if (progress != null && done % 50 == 0) {
                        progress.Report(0.9f + 0.1f * done / archive.Entries.Count);
                    }
                }
            }
            return null;
        }

        // Starts the unpacked engine once; it must answer `version --json` and name the version the descriptor names.
        private static string CheckVersion(string exe, EngineRelease release) {
            EngineProbeResult probe = EngineProbe.Run(exe, VersionCheckSeconds);
            if (!probe.Ran) {
                return probe.Problem;
            }
            if (!string.Equals(probe.EngineVersion, release.Version, StringComparison.Ordinal)) {
                return "The unpacked engine says it is version '" + probe.EngineVersion + "', not " + release.Version + ".";
            }
            return null;
        }

        private EngineInstallResult Fail(string message) {
            log.Log(LogLevel.Error, "Engine installer: " + message);
            return new EngineInstallResult { Status = EngineInstallStatus.Failed, Message = message };
        }

        private static string ShortId() {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string Hex(byte[] bytes) {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private static void DeleteQuietly(string path) {
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (IOException) {
                // a temporary file we could not remove: the next install uses another name
            } catch (UnauthorizedAccessException) {
            }
        }

        private static void DeleteDirectoryQuietly(string path) {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, true);
                }
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        private static HttpClient CreateClient() {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("viitorx-engine-installer/1");
            return client;
        }
    }
}
