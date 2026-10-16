using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using NUnit.Framework;

using VirtualMirror.Core;
using VirtualMirror.Tracking.OakD;
using VirtualMirror.Tracking.OakD.Editor;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="EngineRelease"/> and <see cref="EngineInstaller"/>: what is accepted from a release descriptor, and that a download which is wrong in any way
    /// (size, SHA-256, a truncated body, an archive that tries to leave its folder) installs nothing and leaves nothing behind. The downloads come from a small
    /// HTTP server on the loopback address, so no network is used; the real engine zip is installed in the last test when the environment names it.
    /// </summary>
    public sealed class EngineInstallerTests {
        private const string Top = "viitorx-tracking";
        private const string Exe = "viitorx-tracking.exe";

        private sealed class QuietLog : ILogService {
            public readonly List<string> Lines = new List<string>();

            public void Log(LogLevel level, string message) {
                lock (Lines) {
                    Lines.Add(level + ": " + message);
                }
            }

            public void LogException(Exception exception, string context) {
                lock (Lines) {
                    Lines.Add("EXCEPTION: " + context + ": " + exception.Message);
                }
            }

            public void Flush() {
            }
        }

        private sealed class Reply {
            public int Status = 200;
            public byte[] Body = new byte[0];

            /// <summary>-1: the body's length. 0 or more: announce this many bytes. -2: announce none (the body ends when the connection closes).</summary>
            public long Announce = -1;

            /// <summary>-1: send the whole body. Otherwise only this many bytes, then close (or hold the connection open).</summary>
            public int SendOnly = -1;

            public bool HoldOpen;
        }

        /// <summary>A one-purpose HTTP server on 127.0.0.1: every GET is answered by <see cref="Answer"/>; the requests are counted.</summary>
        private sealed class MiniServer : IDisposable {
            private readonly TcpListener listener;
            private readonly Thread thread;
            private readonly List<TcpClient> clients = new List<TcpClient>();
            private volatile bool stopped;
            public int Requests;
            public Func<string, Reply> Answer;

            public MiniServer() {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.Start();
            }

            public int Port {
                get {
                    return ((IPEndPoint)listener.LocalEndpoint).Port;
                }
            }

            public string Url(string path) {
                return "http://127.0.0.1:" + Port + "/" + path;
            }

            private void Loop() {
                while (!stopped) {
                    try {
                        TcpClient client = listener.AcceptTcpClient();
                        lock (clients) {
                            clients.Add(client);
                        }
                        Thread handler = new Thread(delegate() { Serve(client); });
                        handler.IsBackground = true;
                        handler.Start();
                    } catch (SocketException) {
                        return;
                    } catch (InvalidOperationException) {
                        return;
                    }
                }
            }

            private void Serve(TcpClient client) {
                try {
                    NetworkStream stream = client.GetStream();
                    StringBuilder head = new StringBuilder();
                    byte[] one = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) {
                        if (stream.Read(one, 0, 1) <= 0) {
                            return;
                        }
                        head.Append((char)one[0]);
                    }
                    Interlocked.Increment(ref Requests);
                    string path = head.ToString().Split(' ')[1].TrimStart('/');
                    Reply reply = Answer(path);
                    long announce = reply.Announce == -1 ? reply.Body.Length : reply.Announce;
                    StringBuilder response = new StringBuilder();
                    response.Append("HTTP/1.1 ").Append(reply.Status).Append(reply.Status == 200 ? " OK" : " Not Found").Append("\r\n");
                    if (reply.Announce != -2) {
                        response.Append("Content-Length: ").Append(announce).Append("\r\n");
                    }
                    response.Append("Connection: close\r\n\r\n");
                    byte[] headBytes = Encoding.ASCII.GetBytes(response.ToString());
                    stream.Write(headBytes, 0, headBytes.Length);
                    int count = reply.SendOnly < 0 ? reply.Body.Length : Math.Min(reply.SendOnly, reply.Body.Length);
                    stream.Write(reply.Body, 0, count);
                    stream.Flush();
                    if (reply.HoldOpen) {
                        while (!stopped) {
                            Thread.Sleep(50);
                        }
                    }
                } catch (IOException) {
                } catch (ObjectDisposedException) {
                } finally {
                    client.Close();
                }
            }

            public void Dispose() {
                stopped = true;
                listener.Stop();
                lock (clients) {
                    foreach (TcpClient client in clients) {
                        client.Close();
                    }
                }
            }
        }

        private readonly List<string> temporary = new List<string>();
        private readonly List<IDisposable> servers = new List<IDisposable>();

        [TearDown]
        public void TearDown() {
            foreach (IDisposable server in servers) {
                server.Dispose();
            }
            servers.Clear();
            foreach (string folder in temporary) {
                try {
                    if (Directory.Exists(folder)) {
                        Directory.Delete(folder, true);
                    }
                } catch (IOException) {
                }
            }
            temporary.Clear();
        }

        private string NewRoot() {
            string parent = Path.Combine(Path.GetTempPath(), "vit" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(parent);
            temporary.Add(parent);
            return Path.Combine(parent, "TrackingEngine");
        }

        private MiniServer NewServer() {
            MiniServer server = new MiniServer();
            servers.Add(server);
            return server;
        }

        private static byte[] Zip(params KeyValuePair<string, byte[]>[] entries) {
            using (MemoryStream memory = new MemoryStream()) {
                using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
                    foreach (KeyValuePair<string, byte[]> entry in entries) {
                        ZipArchiveEntry created = archive.CreateEntry(entry.Key);
                        using (Stream stream = created.Open()) {
                            stream.Write(entry.Value, 0, entry.Value.Length);
                        }
                    }
                }
                return memory.ToArray();
            }
        }

        private static KeyValuePair<string, byte[]> File2(string name, string text) {
            return new KeyValuePair<string, byte[]>(name, Encoding.UTF8.GetBytes(text));
        }

        // an engine folder of a few files: the stand-in exe is not a program, so tests that use it turn the version check off
        private static byte[] GoodZip() {
            return Zip(File2(Top + "/" + Exe, "MZ stand-in"), File2(Top + "/manifest.json", "{}"), File2(Top + "/lib/a.dll", new string('a', 4000)), File2(Top + "/models/README.txt", "models"));
        }

        private static string Sha(byte[] data) {
            using (SHA256 sha = SHA256.Create()) {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data)) {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static EngineRelease ReleaseFor(byte[] zip, string url, string version) {
            EngineRelease release = new EngineRelease();
            release.Name = Top;
            release.Version = version;
            release.File = "engine.zip";
            release.Url = url;
            release.Sha256 = Sha(zip);
            release.SizeBytes = zip.Length;
            release.UnpackedBytes = 100000;
            release.TopFolder = Top;
            release.Exe = Exe;
            return release;
        }

        private static string Json(string schema, string platform, string version, string sha, string size, string unpacked, string top, string exe) {
            return "{\"schema\":" + schema + ",\"name\":\"viitorx-tracking\",\"version\":\"" + version + "\",\"platform\":\"" + platform + "\",\"file\":\"e.zip\",\"url\":\"\",\"sha256\":\"" + sha
                + "\",\"sizeBytes\":" + size + ",\"unpackedBytes\":" + unpacked + ",\"topFolder\":\"" + top + "\",\"exe\":\"" + exe + "\",\"wireSpec\":\"1.0\"}";
        }

        private static readonly string GoodSha = new string('a', 64);

        private static string GoodJson() {
            return Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", Top, Exe);
        }

        private EngineInstallResult Install(EngineInstaller installer, EngineRelease release, string urlOverride) {
            return installer.InstallAsync(release, urlOverride, CancellationToken.None, null).GetAwaiter().GetResult();
        }

        private static EngineInstaller InstallerFor(string root) {
            EngineInstaller installer = new EngineInstaller(new QuietLog(), root);
            installer.RunVersionCheck = false;
            return installer;
        }

        // nothing but (at most) the root folder itself may remain after a failed install
        private static void AssertNothingLeft(string root) {
            if (!Directory.Exists(root)) {
                return;
            }
            Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length, "no version folder, partial folder or download file is left in " + root);
        }

        // ---- the descriptor ----

        [Test]
        public void AGoodDescriptorIsParsedFieldByField() {
            EngineRelease release;
            string error;
            Assert.IsTrue(EngineRelease.TryParse(GoodJson(), out release, out error), error);
            Assert.AreEqual("0.1.0", release.Version);
            Assert.AreEqual(GoodSha, release.Sha256);
            Assert.AreEqual(1000L, release.SizeBytes);
            Assert.AreEqual(5000L, release.UnpackedBytes);
            Assert.AreEqual(Top, release.TopFolder);
            Assert.AreEqual(Exe, release.Exe);
            Assert.AreEqual(string.Empty, release.Url, "an empty url is allowed: the publisher fills it in later");
        }

        [Test]
        public void TheShaIsKeptInLowerCase() {
            EngineRelease release;
            string error;
            Assert.IsTrue(EngineRelease.TryParse(Json("1", "win-x64", "0.1.0", GoodSha.ToUpperInvariant(), "1000", "5000", Top, Exe), out release, out error), error);
            Assert.AreEqual(GoodSha, release.Sha256);
        }

        [Test]
        public void EveryWayAnUntrustedDescriptorCanBeWrongIsRefusedWithTheFieldNamed() {
            string[][] bad = {
                new string[] { "not json at all", "not JSON" },
                new string[] { Json("2", "win-x64", "0.1.0", GoodSha, "1000", "5000", Top, Exe), "schema" },
                new string[] { Json("1", "linux-x64", "0.1.0", GoodSha, "1000", "5000", Top, Exe), "linux-x64" },
                new string[] { Json("1", "win-x64", "../0.1.0", GoodSha, "1000", "5000", Top, Exe), "version" },
                new string[] { Json("1", "win-x64", "a/b", GoodSha, "1000", "5000", Top, Exe), "version" },
                new string[] { Json("1", "win-x64", "", GoodSha, "1000", "5000", Top, Exe), "version" },
                new string[] { Json("1", "win-x64", ".hidden", GoodSha, "1000", "5000", Top, Exe), "version" },
                new string[] { Json("1", "win-x64", "0.1..0", GoodSha, "1000", "5000", Top, Exe), "version" },
                new string[] { Json("1", "win-x64", "0.1.0", new string('a', 63), "1000", "5000", Top, Exe), "sha256" },
                new string[] { Json("1", "win-x64", "0.1.0", new string('g', 64), "1000", "5000", Top, Exe), "sha256" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "0", "5000", Top, Exe), "sizeBytes" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "9999999999", "5000", Top, Exe), "sizeBytes" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "0", Top, Exe), "unpackedBytes" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "99999999999", Top, Exe), "unpackedBytes" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", "..", Exe), "topFolder" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", "a/b", Exe), "topFolder" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", Top, "sub/engine.exe"), "exe" },
                new string[] { Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", Top, "engine.bat"), "exe" }
            };
            foreach (string[] pair in bad) {
                EngineRelease release;
                string error;
                Assert.IsFalse(EngineRelease.TryParse(pair[0], out release, out error), "should be refused: " + pair[0]);
                Assert.IsNull(release);
                StringAssert.Contains(pair[1], error, "the reason names the field: " + pair[0]);
            }
        }

        [Test]
        public void OnlyHttpsOrLoopbackHttpIsAnAllowedAddress() {
            string reason;
            Assert.IsTrue(EngineInstaller.IsAllowedUrl("https://example.com/e.zip", out reason), reason);
            Assert.IsTrue(EngineInstaller.IsAllowedUrl("http://127.0.0.1:8080/e.zip", out reason), reason);
            Assert.IsTrue(EngineInstaller.IsAllowedUrl("http://localhost/e.zip", out reason), reason);
            Assert.IsFalse(EngineInstaller.IsAllowedUrl("http://example.com/e.zip", out reason));
            StringAssert.Contains("https", reason);
            Assert.IsFalse(EngineInstaller.IsAllowedUrl("http://192.168.1.5/e.zip", out reason), "a LAN address is not loopback");
            Assert.IsFalse(EngineInstaller.IsAllowedUrl("ftp://example.com/e.zip", out reason));
            Assert.IsFalse(EngineInstaller.IsAllowedUrl("file:///C:/e.zip", out reason));
            Assert.IsFalse(EngineInstaller.IsAllowedUrl("e.zip", out reason));
        }

        // ---- the install ----

        [Test]
        public void ADownloadThatMatchesItsDescriptorIsInstalledWhereTheLocatorLooks() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineRelease release = ReleaseFor(zip, server.Url("engine.zip"), "0.1.0");
            List<float> progress = new List<float>();
            EngineInstallResult result = InstallerFor(root).InstallAsync(release, null, CancellationToken.None, new SyncProgress(progress)).GetAwaiter().GetResult();
            Assert.AreEqual(EngineInstallStatus.Installed, result.Status, result.Message);
            Assert.AreEqual(Path.Combine(root, "0.1.0"), result.Folder);
            Assert.IsTrue(File.Exists(Path.Combine(result.Folder, Exe)));
            Assert.IsTrue(File.Exists(Path.Combine(result.Folder, "manifest.json")));
            Assert.AreEqual(4000, new FileInfo(Path.Combine(result.Folder, "lib", "a.dll")).Length, "a nested file arrived whole");
            Assert.IsTrue(File.Exists(Path.Combine(result.Folder, "models", "README.txt")));
            Assert.AreEqual(1, Directory.GetFileSystemEntries(root).Length, "only the version folder is in the root: the download and the partial folder are gone");
            Assert.AreEqual(1f, progress[progress.Count - 1], 1e-6f, "progress ended at 1");
            for (int i = 1; i < progress.Count; i++) {
                Assert.GreaterOrEqual(progress[i], progress[i - 1], "progress never goes back");
            }
            string found = EngineLocator.UserInstall(root, string.Empty, File.Exists, Directory.Exists, Directory.GetDirectories);
            Assert.AreEqual(Path.GetFullPath(result.ExePath).Replace('\\', '/'), Path.GetFullPath(found).Replace('\\', '/'), "the engine locator finds what the installer installed");
        }

        [Test]
        public void ASecondInstallOfTheSameVersionAsksTheServerForNothing() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineRelease release = ReleaseFor(zip, server.Url("engine.zip"), "0.1.0");
            Assert.AreEqual(EngineInstallStatus.Installed, Install(InstallerFor(root), release, null).Status);
            Assert.AreEqual(1, server.Requests);
            EngineInstallResult again = Install(InstallerFor(root), release, null);
            Assert.AreEqual(EngineInstallStatus.AlreadyInstalled, again.Status);
            Assert.IsTrue(again.Ok);
            Assert.AreEqual(1, server.Requests, "no second download");
        }

        [Test]
        public void AWrongShaInstallsNothingAndLeavesNothing() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineRelease release = ReleaseFor(zip, server.Url("engine.zip"), "0.1.0");
            release.Sha256 = new string('0', 64);
            EngineInstallResult result = Install(InstallerFor(root), release, null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("SHA-256", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void AFileOfTheRightSizeWithOneByteChangedIsRefused() {
            byte[] zip = GoodZip();
            byte[] changed = (byte[])zip.Clone();
            changed[changed.Length / 2] = (byte)(changed[changed.Length / 2] ^ 0xFF);
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = changed };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("SHA-256", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ATruncatedDownloadInstallsNothingAndLeavesNothing() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip, SendOnly = zip.Length / 2 };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status, result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ABodyThatEndsEarlyWithoutAnAnnouncedLengthIsCaughtBySize() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip, Announce = -2, SendOnly = zip.Length - 10 };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("ended after", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ABodyLargerThanTheDescriptorSaysIsStopped() {
            byte[] zip = GoodZip();
            byte[] more = new byte[zip.Length + 5000];
            Array.Copy(zip, more, zip.Length);
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = more, Announce = -2 };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("larger", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ALengthAnnouncedThatDiffersFromTheDescriptorIsRefusedBeforeTheBody() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip, Announce = zip.Length + 1000 };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("announces", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ANotFoundIsReportedWithItsStatus() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Status = 404 };
            };
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("404", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void CancellingMidDownloadStopsAndLeavesNothing() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip, SendOnly = zip.Length / 2, HoldOpen = true };
            };
            string root = NewRoot();
            EngineInstaller installer = InstallerFor(root);
            using (CancellationTokenSource cancel = new CancellationTokenSource()) {
                System.Threading.Tasks.Task<EngineInstallResult> task = installer.InstallAsync(ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null, cancel.Token, null);
                Thread.Sleep(500);
                Assert.IsFalse(task.IsCompleted, "the download is waiting for the rest of the body");
                cancel.Cancel();
                Assert.IsTrue(task.Wait(10000), "the install ended after the cancel");
                Assert.AreEqual(EngineInstallStatus.Cancelled, task.Result.Status, task.Result.Message);
            }
            AssertNothingLeft(root);
        }

        [Test]
        public void AnAddressThatIsNotHttpsIsRefusedWithoutAnyNetwork() {
            byte[] zip = GoodZip();
            string root = NewRoot();
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, "http://example.com/e.zip", "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("https", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void AReleaseWithNoUrlAndNoneGivenSaysSo() {
            byte[] zip = GoodZip();
            EngineInstallResult result = Install(InstallerFor(NewRoot()), ReleaseFor(zip, string.Empty, "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("no url", result.Message);
        }

        [Test]
        public void AnAddressGivenToTheInstallerReplacesTheDescriptorsOwn() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return path == "mirror/engine.zip" ? new Reply { Body = zip } : new Reply { Status = 404 };
            };
            string root = NewRoot();
            EngineRelease release = ReleaseFor(zip, "https://nowhere.invalid/engine.zip", "0.1.0");
            Assert.AreEqual(EngineInstallStatus.Installed, Install(InstallerFor(root), release, server.Url("mirror/engine.zip")).Status);
        }

        // ---- what an archive may do ----

        private EngineInstallResult InstallZip(byte[] zip, long unpackedBytes) {
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineRelease release = ReleaseFor(zip, server.Url("e.zip"), "0.1.0");
            release.UnpackedBytes = unpackedBytes;
            EngineInstallResult result = Install(InstallerFor(root), release, null);
            lastRoot = root;
            return result;
        }

        private string lastRoot;

        [Test]
        public void AnEntryThatClimbsOutOfTheFolderIsRefusedAndNothingIsWrittenOutside() {
            byte[] zip = Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/manifest.json", "{}"), File2(Top + "/../../escaped.txt", "evil"));
            EngineInstallResult result = InstallZip(zip, 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("leaves", result.Message);
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(lastRoot), "escaped.txt")), "nothing was written above the root");
            Assert.IsFalse(File.Exists(Path.Combine(lastRoot, "escaped.txt")));
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void AnEntryOutsideTheTopFolderIsRefused() {
            byte[] zip = Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/manifest.json", "{}"), File2("other/x.dll", "x"));
            EngineInstallResult result = InstallZip(zip, 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("outside", result.Message);
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void AnAbsolutePathEntryIsRefused() {
            byte[] zip = Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/manifest.json", "{}"), File2("C:/Windows/Temp/viitorx-escaped.txt", "x"));
            EngineInstallResult result = InstallZip(zip, 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            Assert.IsFalse(File.Exists("C:/Windows/Temp/viitorx-escaped.txt"));
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void AnArchiveWithoutTheExeOrWithoutAManifestIsNotAnEngineRelease() {
            EngineInstallResult noExe = InstallZip(Zip(File2(Top + "/manifest.json", "{}"), File2(Top + "/a.dll", "x")), 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, noExe.Status);
            StringAssert.Contains(Exe, noExe.Message);
            AssertNothingLeft(lastRoot);
            EngineInstallResult noManifest = InstallZip(Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/a.dll", "x")), 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, noManifest.Status);
            StringAssert.Contains("manifest.json", noManifest.Message);
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void AnArchiveThatUnpacksToMoreThanTheDescriptorSaysIsStopped() {
            byte[] zip = Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/manifest.json", "{}"), File2(Top + "/big.bin", new string('z', 3000000)));
            EngineInstallResult result = InstallZip(zip, 1000);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("unpacks to more", result.Message);
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void SomethingThatIsNotAZipIsRefusedEvenWithTheRightShaOfItself() {
            byte[] notZip = Encoding.UTF8.GetBytes("this is not an archive at all, it only has the right hash");
            EngineInstallResult result = InstallZip(notZip, 100000);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("zip", result.Message);
            AssertNothingLeft(lastRoot);
        }

        [Test]
        public void AnEngineThatCannotBeStartedFailsTheInstallAndRemovesIt() {
            byte[] zip = GoodZip();
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineInstaller installer = new EngineInstaller(new QuietLog(), root);
            installer.RunVersionCheck = true;
            EngineInstallResult result = Install(installer, ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("could not be started", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void AProgramThatExitsWithAnErrorOnTheVersionCheckFailsTheInstall() {
            // PING.EXE stands in for an engine that starts but does not answer `version --json` with success: it exits 1 on the unknown option
            string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
            if (!File.Exists(ping)) {
                Assert.Ignore("PING.EXE is not available.");
            }
            byte[] zip = Zip(new KeyValuePair<string, byte[]>(Top + "/" + Exe, File.ReadAllBytes(ping)), File2(Top + "/manifest.json", "{}"));
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = NewRoot();
            EngineInstaller installer = new EngineInstaller(new QuietLog(), root);
            installer.RunVersionCheck = true;
            EngineInstallResult result = Install(installer, ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("exited", result.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ARootThatLeavesNoRoomForTheEnginesDeepPathsIsRefusedPlainlyAndLeavesNothing() {
            byte[] zip = Zip(File2(Top + "/" + Exe, "MZ"), File2(Top + "/manifest.json", "{}"), File2(Top + "/" + new string('d', 100) + "/" + new string('f', 60) + ".dll", "x"));
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = zip };
            };
            string root = Path.Combine(NewRoot(), new string('r', 90));
            EngineInstallResult result = Install(InstallerFor(root), ReleaseFor(zip, server.Url("e.zip"), "0.1.0"), null);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("too long", result.Message);
            AssertNothingLeft(root);
        }

        // ---- a zip already on disk ----

        [Test]
        public void AZipOnDiskIsInstalledOnlyWhenItsSizeAndShaMatch() {
            byte[] zip = GoodZip();
            string root = NewRoot();
            Directory.CreateDirectory(Path.GetDirectoryName(root));
            string path = Path.Combine(Path.GetDirectoryName(root), "engine.zip");
            File.WriteAllBytes(path, zip);
            EngineRelease release = ReleaseFor(zip, string.Empty, "0.1.0");
            EngineRelease wrongSha = ReleaseFor(zip, string.Empty, "0.1.0");
            wrongSha.Sha256 = new string('1', 64);
            EngineRelease wrongSize = ReleaseFor(zip, string.Empty, "0.1.0");
            wrongSize.SizeBytes = zip.Length + 1;
            Assert.AreEqual(EngineInstallStatus.Failed, InstallerFor(root).InstallFromZip(wrongSha, path).Status);
            Assert.AreEqual(EngineInstallStatus.Failed, InstallerFor(root).InstallFromZip(wrongSize, path).Status);
            Assert.AreEqual(EngineInstallStatus.Failed, InstallerFor(root).InstallFromZip(release, Path.Combine(root, "missing.zip")).Status);
            AssertNothingLeft(root);
            EngineInstallResult result = InstallerFor(root).InstallFromZip(release, path);
            Assert.AreEqual(EngineInstallStatus.Installed, result.Status, result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(result.Folder, Exe)));
            Assert.IsTrue(File.Exists(path), "the caller's zip is left alone");
        }

        [Test]
        public void TheMenuRefusesAMissingOrBrokenReleaseFileWithoutInstallingAnything() {
            string root = NewRoot();
            string folder = Path.GetDirectoryName(root);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("does not exist"));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("version"));
            EngineInstallResult missing = EngineInstallMenu.Run(Path.Combine(folder, "nope.release.json"), string.Empty, string.Empty, root);
            Assert.AreEqual(EngineInstallStatus.Failed, missing.Status);
            StringAssert.Contains("does not exist", missing.Message);
            string broken = Path.Combine(folder, "broken.release.json");
            File.WriteAllText(broken, Json("1", "win-x64", "../evil", GoodSha, "1000", "5000", Top, Exe));
            EngineInstallResult refused = EngineInstallMenu.Run(broken, string.Empty, string.Empty, root);
            Assert.AreEqual(EngineInstallStatus.Failed, refused.Status);
            StringAssert.Contains("version", refused.Message);
            AssertNothingLeft(root);
        }

        [Test]
        public void ADescriptorWithNoUrlAndNoZipBesideItFailsPlainly() {
            string root = NewRoot();
            string folder = Path.GetDirectoryName(root);
            string descriptor = Path.Combine(folder, "e.release.json");
            File.WriteAllText(descriptor, Json("1", "win-x64", "0.1.0", GoodSha, "1000", "5000", Top, Exe));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("no url"));
            EngineInstallResult result = EngineInstallMenu.Run(descriptor, string.Empty, string.Empty, root);
            Assert.AreEqual(EngineInstallStatus.Failed, result.Status);
            StringAssert.Contains("no url", result.Message);
            AssertNothingLeft(root);
        }

        // ---- the real engine ----

        /// <summary>Needs <c>VIITORX_TEST_RELEASE_DIR</c>: a folder with the engine zip and its .release.json (what build_engine.py assemble writes).</summary>
        [Test]
        public void TheRealEngineZipIsDownloadedVerifiedUnpackedAndStartedOnce() {
            string folder = Environment.GetEnvironmentVariable("VIITORX_TEST_RELEASE_DIR");
            if (string.IsNullOrEmpty(folder)) {
                Assert.Ignore("VIITORX_TEST_RELEASE_DIR is not set: the real engine release is not part of this run.");
            }
            string[] descriptors = Directory.GetFiles(folder, "*.release.json");
            Assert.AreEqual(1, descriptors.Length, "one descriptor in " + folder);
            EngineRelease release;
            string error;
            Assert.IsTrue(EngineRelease.TryParse(File.ReadAllText(descriptors[0]), out release, out error), error);
            string zipPath = Path.Combine(folder, release.File);
            MiniServer server = NewServer();
            server.Answer = delegate(string path) {
                return new Reply { Body = File.ReadAllBytes(zipPath) };
            };
            string root = NewRoot();
            EngineInstaller installer = new EngineInstaller(new QuietLog(), root);
            EngineInstallResult result = Install(installer, release, server.Url(release.File));
            Assert.AreEqual(EngineInstallStatus.Installed, result.Status, result.Message);
            Assert.IsTrue(File.Exists(result.ExePath));
            Assert.IsTrue(File.Exists(Path.Combine(result.Folder, "manifest.json")));
            Assert.AreEqual(1, Directory.GetFileSystemEntries(root).Length, "only the version folder is left");
            string found = EngineLocator.UserInstall(root, release.Version, File.Exists, Directory.Exists, Directory.GetDirectories);
            Assert.IsTrue(found.Length > 0, "the locator finds the pinned version");

            // the menu's offline paths: a zip path given, and the zip that sits beside the descriptor (the build folder has both): no network either way
            string offlineRoot = NewRoot();
            EngineInstallResult offline = EngineInstallMenu.Run(descriptors[0], string.Empty, zipPath, offlineRoot);
            Assert.AreEqual(EngineInstallStatus.Installed, offline.Status, offline.Message);
            Assert.IsTrue(File.Exists(offline.ExePath));
            string besideRoot = NewRoot();
            EngineInstallResult beside = EngineInstallMenu.Run(descriptors[0], string.Empty, string.Empty, besideRoot);
            Assert.AreEqual(EngineInstallStatus.Installed, beside.Status, beside.Message);
            Assert.IsTrue(File.Exists(beside.ExePath), "the zip beside the descriptor was used (the descriptor's url is empty, so a download was impossible)");

            // the same zip under a descriptor that names another version: the engine says what it is, and the install is refused
            EngineRelease wrong = new EngineRelease();
            wrong.Name = release.Name;
            wrong.Version = "9.9.9";
            wrong.File = release.File;
            wrong.Url = release.Url;
            wrong.Sha256 = release.Sha256;
            wrong.SizeBytes = release.SizeBytes;
            wrong.UnpackedBytes = release.UnpackedBytes;
            wrong.TopFolder = release.TopFolder;
            wrong.Exe = release.Exe;
            string otherRoot = NewRoot();
            EngineInstallResult refused = Install(new EngineInstaller(new QuietLog(), otherRoot), wrong, server.Url(release.File));
            Assert.AreEqual(EngineInstallStatus.Failed, refused.Status);
            StringAssert.Contains("not 9.9.9", refused.Message);
            AssertNothingLeft(otherRoot);
        }

        private sealed class SyncProgress : IProgress<float> {
            private readonly List<float> values;

            public SyncProgress(List<float> values) {
                this.values = values;
            }

            public void Report(float value) {
                lock (values) {
                    values.Add(value);
                }
            }
        }
    }
}
