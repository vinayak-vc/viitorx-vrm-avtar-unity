using System;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>An <see cref="ILogService"/> that writes to the Unity console. Safe from any thread, as <c>Debug.Log</c> is.</summary>
    internal sealed class VisionDebugLog : ILogService {
        private const string Prefix = "[Vision] ";

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
