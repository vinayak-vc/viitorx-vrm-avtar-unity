using System;
using System.Collections.Generic;
using System.IO;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>The facts the search needs, so the search itself touches nothing but the file system through the delegates it is given.</summary>
    public sealed class EngineLocatorInputs {
        public string SettingPath = string.Empty;
        public bool UsePythonProject;
        public string PythonProjectPath = string.Empty;
        public string EnvironmentValue = string.Empty;
        public string RegistryInstallDir = string.Empty;
        public string LocalAppData = string.Empty;
        public string StreamingAssetsPath = string.Empty;
        public string PinnedVersion = string.Empty;
        public bool AllowPythonFallback = true;
        public string DefaultPythonRoot = string.Empty;
    }

    /// <summary>
    /// Finds the tracking engine. Order (plan section 3, with the machine-wide install added after the environment variable):
    /// 1. the path in the settings, or the Python project when <see cref="EngineLocatorInputs.UsePythonProject"/> is on;
    /// 2. the environment variable <c>VIITORX_TRACKING_ENGINE</c>;
    /// 3. the machine-wide install (registry value <c>InstallDir</c>, written by the installer);
    /// 4. the per-user install <c>%LOCALAPPDATA%\Viitorx\TrackingEngine\&lt;version&gt;</c>;
    /// 5. <c>StreamingAssets/Viitorx/TrackingEngine</c>;
    /// 6. the Python source tree, when allowed (what the application always did).
    /// A path the user typed into the settings that does not hold the engine is an ERROR, not a reason to try the next step: falling through would hide the typo.
    /// A bad environment variable or registry value is only noted.
    /// </summary>
    public static class EngineLocator {
        public const string ExecutableName = "viitorx-tracking.exe";
        public const string EnvironmentVariableName = "VIITORX_TRACKING_ENGINE";
        public const string RegistryKeyPath = "Software\\Viitorx\\TrackingEngine";
        public const string RegistryValueName = "InstallDir";
        public const string UserInstallSubfolder = "Viitorx/TrackingEngine";
        public const string StreamingAssetsSubfolder = "Viitorx/TrackingEngine";

        public static EngineLocation Resolve(EngineLocatorInputs inputs, Func<string, bool> fileExists, Func<string, bool> directoryExists, Func<string, string[]> subDirectories) {
            EngineLocation result = new EngineLocation();
            if (inputs == null) {
                result.Problem = "No locator inputs.";
                return result;
            }
            if (inputs.UsePythonProject) {
                return ResolvePython(inputs, result, fileExists, true);
            }
            if (!string.IsNullOrEmpty(inputs.SettingPath)) {
                string fromSetting = ExecutableIn(inputs.SettingPath, fileExists, directoryExists);
                if (fromSetting.Length > 0) {
                    return Executable(result, fromSetting, EngineSource.Setting);
                }
                result.Tried.Add("settings path " + inputs.SettingPath + " (no " + ExecutableName + " there)");
                result.Problem = "The engine path in the settings does not hold " + ExecutableName + ": " + inputs.SettingPath + ".";
                return result;
            }
            if (!string.IsNullOrEmpty(inputs.EnvironmentValue)) {
                string fromEnvironment = ExecutableIn(inputs.EnvironmentValue, fileExists, directoryExists);
                if (fromEnvironment.Length > 0) {
                    return Executable(result, fromEnvironment, EngineSource.Environment);
                }
                result.Tried.Add(EnvironmentVariableName + "=" + inputs.EnvironmentValue + " (no " + ExecutableName + " there)");
            }
            if (!string.IsNullOrEmpty(inputs.RegistryInstallDir)) {
                string fromRegistry = ExecutableIn(inputs.RegistryInstallDir, fileExists, directoryExists);
                if (fromRegistry.Length > 0) {
                    return Executable(result, fromRegistry, EngineSource.Registry);
                }
                result.Tried.Add("machine-wide install " + inputs.RegistryInstallDir + " (no " + ExecutableName + " there)");
            }
            if (!string.IsNullOrEmpty(inputs.LocalAppData)) {
                string userRoot = Join(inputs.LocalAppData, UserInstallSubfolder);
                string fromUser = UserInstall(userRoot, inputs.PinnedVersion, fileExists, directoryExists, subDirectories);
                if (fromUser.Length > 0) {
                    return Executable(result, fromUser, EngineSource.UserInstall);
                }
                result.Tried.Add("per-user install " + userRoot);
            }
            if (!string.IsNullOrEmpty(inputs.StreamingAssetsPath)) {
                string streaming = Join(inputs.StreamingAssetsPath, StreamingAssetsSubfolder);
                string fromStreaming = ExecutableIn(streaming, fileExists, directoryExists);
                if (fromStreaming.Length > 0) {
                    return Executable(result, fromStreaming, EngineSource.StreamingAssets);
                }
                result.Tried.Add("StreamingAssets " + streaming);
            }
            if (inputs.AllowPythonFallback) {
                return ResolvePython(inputs, result, fileExists, false);
            }
            result.Problem = "No compiled engine was found.";
            return result;
        }

        private static EngineLocation ResolvePython(EngineLocatorInputs inputs, EngineLocation result, Func<string, bool> fileExists, bool requested) {
            string root = !string.IsNullOrEmpty(inputs.PythonProjectPath) ? inputs.PythonProjectPath : inputs.DefaultPythonRoot;
            if (string.IsNullOrEmpty(root)) {
                result.Tried.Add("Python project (none configured and none found beside the application)");
                result.Problem = requested
                    ? "Use Python Project is on but no Python project folder was found."
                    : "No compiled engine and no Python project were found.";
                return result;
            }
            SidecarPathSet paths = SidecarPaths.Resolve(root, null);
            if (paths.PythonExe == null || !fileExists(paths.PythonExe)) {
                result.Tried.Add("Python project " + root + " (no virtualenv interpreter at " + paths.PythonExe + ")");
                result.Problem = requested
                    ? "Use Python Project is on but the project at " + root + " has no virtualenv interpreter."
                    : "No compiled engine was found, and the Python project at " + root + " has no virtualenv interpreter.";
                return result;
            }
            result.Kind = EngineKind.PythonProject;
            result.Source = EngineSource.PythonProject;
            result.PythonPaths = paths;
            return result;
        }

        private static EngineLocation Executable(EngineLocation result, string path, EngineSource source) {
            result.Kind = EngineKind.Executable;
            result.Source = source;
            result.ExecutablePath = path.Replace('\\', '/');
            return result;
        }

        private static string Join(string folder, string name) {
            return folder.TrimEnd('/', '\\') + "/" + name;
        }

        /// <summary>The engine executable for a path that is the executable itself or the folder that holds it; empty when it is neither.</summary>
        public static string ExecutableIn(string path, Func<string, bool> fileExists, Func<string, bool> directoryExists) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }
            string trimmed = path.Trim().Trim('"');
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                return fileExists(trimmed) ? trimmed : string.Empty;
            }
            string inside = Join(trimmed, ExecutableName);
            if (directoryExists(trimmed) && fileExists(inside)) {
                return inside;
            }
            return string.Empty;
        }

        /// <summary>The executable of the pinned version under <paramref name="userRoot"/>, or of the highest version folder that holds one.</summary>
        public static string UserInstall(string userRoot, string pinnedVersion, Func<string, bool> fileExists, Func<string, bool> directoryExists, Func<string, string[]> subDirectories) {
            if (!directoryExists(userRoot)) {
                return string.Empty;
            }
            if (!string.IsNullOrEmpty(pinnedVersion)) {
                return ExecutableIn(Join(userRoot, pinnedVersion), fileExists, directoryExists);
            }
            string[] folders = subDirectories(userRoot);
            List<string> names = new List<string>();
            if (folders != null) {
                foreach (string folder in folders) {
                    names.Add(Path.GetFileName(folder.TrimEnd('/', '\\')));
                }
            }
            names.Sort(CompareVersionsDescending);
            foreach (string name in names) {
                string candidate = ExecutableIn(Join(userRoot, name), fileExists, directoryExists);
                if (candidate.Length > 0) {
                    return candidate;
                }
            }
            return string.Empty;
        }

        /// <summary>Highest version first. A name that is not a version sorts after every real one, then by name.</summary>
        public static int CompareVersionsDescending(string a, string b) {
            Version va;
            Version vb;
            bool okA = Version.TryParse(StripSuffix(a), out va);
            bool okB = Version.TryParse(StripSuffix(b), out vb);
            if (okA && okB) {
                int byVersion = vb.CompareTo(va);
                return byVersion != 0 ? byVersion : string.CompareOrdinal(b, a);
            }
            if (okA) {
                return -1;
            }
            if (okB) {
                return 1;
            }
            return string.CompareOrdinal(a, b);
        }

        private static string StripSuffix(string name) {
            int dash = name.IndexOf('-');
            return dash >= 0 ? name.Substring(0, dash) : name;
        }
    }
}
