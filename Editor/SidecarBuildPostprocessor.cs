using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Editor {
    /// <summary>
    /// Copies the Python sidecar SOURCE into the built player's StreamingAssets (ADR-064).
    ///
    /// Why a post-build step instead of keeping the files in Assets/StreamingAssets: the sidecar's
    /// .py files change constantly. Importing them would spawn a .meta file each, churn them on
    /// every edit, and leave two copies of every module to drift apart. Copying at build time keeps
    /// python-sidecar~ the single source of truth and guarantees the player gets the same revision
    /// the editor just ran.
    ///
    /// TopDirectoryOnly is load-bearing, not incidental. Since the ADR-065 reorganisation the
    /// sidecar root holds exactly the production path - the two entry points plus the nine modules
    /// wholebody_udp_sender imports - while every harness and self-test lives under tools/ and
    /// tests/. Copying only the root therefore ships precisely what a player needs to track and
    /// nothing that only matters on a developer's machine.
    ///
    /// What deliberately does NOT ship:
    ///   .venv/                 342 MB and NOT relocatable. pyvenv.cfg pins an absolute `home` to the
    ///                          base interpreter and Lib/ holds only site-packages, no stdlib, so a
    ///                          copied venv breaks on any machine without that exact Python. It is
    ///                          created on the target by setup_sidecar.ps1 instead.
    ///   rtmw3d-x.onnx          369 MB. Copying it per build makes every build minutes slower for a
    ///                          file that changes roughly never; setup_sidecar.ps1 places it once.
    ///   depthai_blazepose/     86 MB. The superseded Phase-1 BlazePose path — referenced only in a
    ///                          docstring in wholebody_udp_sender.py, never imported.
    ///   *_evidence/, pipeline_logs*/, __pycache__/, docs/, .git/
    /// </summary>
    public sealed class SidecarBuildPostprocessor : IPostprocessBuildWithReport {

        public int callbackOrder => 0;

        /// <summary>Extra non-.py files the sidecar needs at runtime or at setup time.</summary>
        private static readonly string[] AdditionalFiles = {
            "requirements.txt",
            "requirements.lock.txt",
            SidecarPaths.SetupScriptName,
            "README.md"
        };

        public void OnPostprocessBuild(BuildReport report) {
            if (report.summary.platform != BuildTarget.StandaloneWindows64
                && report.summary.platform != BuildTarget.StandaloneWindows) {
                // v1 is Windows-only: the launcher's job-object teardown and the OAK-D DirectML
                // stack both assume it. Say so rather than shipping a build that cannot track.
                Debug.LogWarning("[SidecarBuild] Target is " + report.summary.platform
                                 + "; the Python sidecar is Windows-only and was not copied.");
                return;
            }

            string sourceRoot = SidecarPaths.ResolveEditorRoot(Application.dataPath);
            if (string.IsNullOrEmpty(sourceRoot)) {
                Debug.LogError("[SidecarBuild] Could not locate " + SidecarPaths.EditorSidecarFolderName
                               + " — the built player will have no sidecar and cannot track.");
                return;
            }

            string playerDataDir = Path.GetDirectoryName(report.summary.outputPath);
            string exeName = Path.GetFileNameWithoutExtension(report.summary.outputPath);
            string streamingAssets = Path.Combine(playerDataDir, exeName + "_Data", "StreamingAssets");
            string destRoot = Path.Combine(streamingAssets, SidecarPaths.PlayerSidecarFolderName);

            try {
                CopySidecar(sourceRoot, destRoot);
            } catch (Exception ex) {
                Debug.LogError("[SidecarBuild] Failed to copy the sidecar: " + ex.Message);
            }
        }

        private static void CopySidecar(string sourceRoot, string destRoot) {
            Directory.CreateDirectory(destRoot);

            int copied = 0;
            long bytes = 0;

            foreach (string file in Directory.GetFiles(sourceRoot, "*.py", SearchOption.TopDirectoryOnly)) {
                CopyOne(file, destRoot, ref copied, ref bytes);
            }

            foreach (string name in AdditionalFiles) {
                string path = Path.Combine(sourceRoot, name);
                if (File.Exists(path)) {
                    CopyOne(path, destRoot, ref copied, ref bytes);
                }
            }

            // The model folder is created empty and explained, so a missing model reads as "not
            // installed yet" rather than as a broken build.
            string modelDir = Path.Combine(destRoot, "models");
            Directory.CreateDirectory(modelDir);
            string placeholder = Path.Combine(modelDir, "PLACE_rtmw3d-x.onnx_HERE.txt");
            if (!File.Exists(placeholder)) {
                File.WriteAllText(placeholder,
                    "The pose model is not bundled with the build (369 MB).\r\n"
                    + "Run " + SidecarPaths.SetupScriptName + " in the parent folder, or copy\r\n"
                    + "rtmw3d-x.onnx into this folder by hand.\r\n"
                    + "See README.md at the build root.\r\n");
            }

            Debug.Log(string.Format("[SidecarBuild] Copied {0} sidecar files ({1:N0} KB) to {2}",
                                    copied, bytes / 1024, destRoot));
        }

        private static void CopyOne(string sourceFile, string destDir, ref int copied, ref long bytes) {
            string dest = Path.Combine(destDir, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, dest, true);
            copied++;
            bytes += new FileInfo(sourceFile).Length;
        }
    }
}
