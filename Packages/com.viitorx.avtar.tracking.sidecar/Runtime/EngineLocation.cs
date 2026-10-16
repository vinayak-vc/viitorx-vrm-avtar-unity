using System.Collections.Generic;
using System.Text;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>What the engine is: the compiled program, the Python source tree, or nothing found.</summary>
    public enum EngineKind {
        None = 0,
        Executable = 1,
        PythonProject = 2
    }

    /// <summary>Which step of the search found the engine, in the order they are tried.</summary>
    public enum EngineSource {
        None = 0,
        Setting = 1,
        Environment = 2,
        Registry = 3,
        UserInstall = 4,
        StreamingAssets = 5,
        PythonProject = 6
    }

    /// <summary>The result of <see cref="EngineLocator.Resolve"/>: what to start and how it was found, or why nothing was.</summary>
    public sealed class EngineLocation {
        public EngineKind Kind = EngineKind.None;
        public EngineSource Source = EngineSource.None;
        public string ExecutablePath = string.Empty;
        public SidecarPathSet PythonPaths;
        public string Problem = string.Empty;
        public readonly List<string> Tried = new List<string>();

        public bool Found {
            get {
                return Kind != EngineKind.None;
            }
        }

        public string Folder {
            get {
                if (Kind == EngineKind.Executable) {
                    return System.IO.Path.GetDirectoryName(ExecutablePath).Replace('\\', '/');
                }
                if (Kind == EngineKind.PythonProject && PythonPaths != null) {
                    return PythonPaths.Root;
                }
                return string.Empty;
            }
        }

        public string Describe() {
            if (Kind == EngineKind.Executable) {
                return "engine: " + ExecutablePath + " (" + SourceName(Source) + ")";
            }
            if (Kind == EngineKind.PythonProject) {
                return "engine: the Python project " + Folder + " (" + SourceName(Source) + ")";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("engine: not found");
            if (Problem.Length > 0) {
                sb.Append(". ").Append(Problem);
            }
            if (Tried.Count > 0) {
                sb.Append(" Looked at: ").Append(string.Join("; ", Tried.ToArray())).Append('.');
            }
            return sb.ToString();
        }

        public static string SourceName(EngineSource source) {
            switch (source) {
                case EngineSource.Setting:
                    return "the path in the settings";
                case EngineSource.Environment:
                    return "environment variable " + EngineLocator.EnvironmentVariableName;
                case EngineSource.Registry:
                    return "the machine-wide install";
                case EngineSource.UserInstall:
                    return "the per-user install";
                case EngineSource.StreamingAssets:
                    return "StreamingAssets";
                case EngineSource.PythonProject:
                    return "the Python project";
                default:
                    return "nowhere";
            }
        }
    }
}
