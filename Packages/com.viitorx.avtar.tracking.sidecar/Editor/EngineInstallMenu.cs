using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.OakD.Editor {
    /// <summary>
    /// Installs the tracking engine from the Editor: <i>Tools &gt; Viitorx &gt; Tracking Engine &gt; Install From Release File</i> asks for a
    /// <c>.release.json</c> (the descriptor that sits beside the engine's zip), downloads and checks the zip it names (or, for a machine without network, takes that zip
    /// when it sits beside the descriptor) and installs it for the current user under <c>%LOCALAPPDATA%\Viitorx\TrackingEngine\&lt;version&gt;</c>, where every Viitorx
    /// package looks. See <see cref="EngineInstaller"/> for what is checked and what that does and does not protect against.
    /// </summary>
    public static class EngineInstallMenu {
        [MenuItem("Tools/Viitorx/Tracking Engine/Install From Release File...")]
        private static void InstallFromFile() {
            string descriptor = EditorUtility.OpenFilePanel("Tracking engine release file (.release.json)", string.Empty, "json");
            if (string.IsNullOrEmpty(descriptor)) {
                return;
            }
            EngineInstallResult result = Run(descriptor, string.Empty, string.Empty, EngineInstaller.DefaultRoot());
            EditorUtility.DisplayDialog("Tracking engine", result.Ok ? "Installed at\n" + result.Folder : "Not installed.\n\n" + result.Message, "OK");
        }

        [MenuItem("Tools/Viitorx/Tracking Engine/Show Install Folder")]
        private static void ShowInstallFolder() {
            string root = EngineInstaller.DefaultRoot();
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        /// <summary>
        /// Reads the descriptor at <paramref name="descriptorPath"/> and installs it into <paramref name="root"/>. With <paramref name="zipPath"/> the zip on disk is used
        /// (no network); with neither a zip path nor an address, the zip the descriptor names is used when it sits beside the descriptor; otherwise it is downloaded from
        /// <paramref name="urlOverride"/> or, when that is empty, from the descriptor's own url. Blocks the Editor behind a
        /// progress bar with Cancel; returns the outcome, which is also logged.
        /// </summary>
        public static EngineInstallResult Run(string descriptorPath, string urlOverride, string zipPath, string root) {
            UnityDebugLogService log = new UnityDebugLogService();
            EngineRelease release;
            string error;
            if (!File.Exists(descriptorPath)) {
                return Failed(log, "The release file '" + descriptorPath + "' does not exist.");
            }
            if (!EngineRelease.TryParse(File.ReadAllText(descriptorPath), out release, out error)) {
                return Failed(log, error);
            }
            EngineInstaller installer = new EngineInstaller(log, root);
            if (string.IsNullOrEmpty(zipPath) && string.IsNullOrEmpty(urlOverride)) {
                // a machine without network: the zip the release file names, saved beside it, is used instead of downloading
                string beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(descriptorPath)), release.File);
                if (EngineRelease.IsSafeName(release.File) && File.Exists(beside)) {
                    log.Log(LogLevel.Info, "Engine installer: using " + beside + " (beside the release file) instead of downloading.");
                    zipPath = beside;
                }
            }
            if (!string.IsNullOrEmpty(zipPath)) {
                return installer.InstallFromZip(release, zipPath);
            }
            using (CancellationTokenSource cancel = new CancellationTokenSource()) {
                float fraction = 0f;
                Task<EngineInstallResult> task = installer.InstallAsync(release, urlOverride, cancel.Token, new Reporter(delegate(float value) {
                    fraction = value;
                }));
                try {
                    while (!task.IsCompleted) {
                        if (EditorUtility.DisplayCancelableProgressBar("Installing the tracking engine " + release.Version, "Downloading, checking and unpacking", fraction)) {
                            cancel.Cancel();
                        }
                        Thread.Sleep(100);
                    }
                } finally {
                    EditorUtility.ClearProgressBar();
                }
                return task.GetAwaiter().GetResult();
            }
        }

        private static EngineInstallResult Failed(ILogService log, string message) {
            log.Log(LogLevel.Error, "Engine installer: " + message);
            return new EngineInstallResult { Status = EngineInstallStatus.Failed, Message = message };
        }

        private sealed class Reporter : IProgress<float> {
            private readonly Action<float> report;

            public Reporter(Action<float> report) {
                this.report = report;
            }

            public void Report(float value) {
                report(value);
            }
        }
    }
}
