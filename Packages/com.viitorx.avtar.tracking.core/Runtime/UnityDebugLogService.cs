using System;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>The Unity console as an <see cref="ILogService"/>, for a project that has no log service of its own. Debug.Log may be called from any thread.</summary>
    public sealed class UnityDebugLogService : ILogService {
        private const string Prefix = "[Viitorx] ";

        public void Log(LogLevel level, string message) {
            switch (level) {
                case LogLevel.Error:
                    Debug.LogError(Prefix + message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(Prefix + message);
                    break;
                default:
                    Debug.Log(Prefix + message);
                    break;
            }
        }

        public void LogException(Exception exception, string context) {
            Debug.LogError(Prefix + context + ": " + exception);
        }

        public void Flush() {
        }
    }
}
