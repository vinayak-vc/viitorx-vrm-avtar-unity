using System;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.IO {
    /// <summary>
    /// Rolling daily file logger. Writes one file per day under the logs directory and
    /// emits a session header (app, Unity, GPU, OS) when a file is first opened.
    /// Thread-safe; failures degrade to a Unity console warning instead of throwing.
    /// </summary>
    public sealed class LogService : ILogService, IDisposable {
        private const string DateFormat = "yyyyMMdd";
        private const string TimeFormat = "HH:mm:ss.fff";

        private readonly IPathProvider pathProvider;
        private readonly bool mirrorToUnityConsole;
        private readonly object syncRoot = new object();

        private StreamWriter writer;
        private string currentDateStamp;
        private bool disposed;

        public LogService(IPathProvider pathProvider, bool mirrorToUnityConsole) {
            if (pathProvider == null) {
                throw new ArgumentNullException(nameof(pathProvider));
            }
            this.pathProvider = pathProvider;
            this.mirrorToUnityConsole = mirrorToUnityConsole;
        }

        public void Log(LogLevel level, string message) {
            if (disposed) {
                return;
            }
            lock (syncRoot) {
                try {
                    EnsureWriter();
                    string line = string.Concat(
                        DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture),
                        " [", level.ToString(), "] ", message);
                    writer.WriteLine(line);
                } catch (Exception exception) {
                    Debug.LogWarning("LogService failed to write entry: " + exception.Message);
                }
            }
            MirrorToConsole(level, message);
        }

        public void LogException(Exception exception, string context) {
            if (exception == null) {
                return;
            }
            string message;
            if (string.IsNullOrEmpty(context)) {
                message = exception.ToString();
            } else {
                message = context + " -> " + exception;
            }
            Log(LogLevel.Error, message);
        }

        public void Flush() {
            if (disposed) {
                return;
            }
            lock (syncRoot) {
                try {
                    if (writer != null) {
                        writer.Flush();
                    }
                } catch (Exception exception) {
                    Debug.LogWarning("LogService failed to flush: " + exception.Message);
                }
            }
        }

        public void Dispose() {
            lock (syncRoot) {
                if (disposed) {
                    return;
                }
                CloseWriter();
                disposed = true;
            }
        }

        private void MirrorToConsole(LogLevel level, string message) {
            if (!mirrorToUnityConsole) {
                return;
            }
            switch (level) {
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }

        private void EnsureWriter() {
            string todayStamp = DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (writer != null && string.Equals(todayStamp, currentDateStamp, StringComparison.Ordinal)) {
                return;
            }
            CloseWriter();
            pathProvider.EnsureDirectories();
            string path = pathProvider.GetLogFilePath(DateTime.Now);
            FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.AutoFlush = false;
            currentDateStamp = todayStamp;
            WriteHeader();
        }

        private void WriteHeader() {
            writer.WriteLine("==== Virtual Mirror session ====");
            writer.WriteLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            writer.WriteLine("App version: " + Application.version);
            writer.WriteLine("Unity version: " + Application.unityVersion);
            writer.WriteLine("GPU: " + SystemInfo.graphicsDeviceName);
            writer.WriteLine("OS: " + SystemInfo.operatingSystem);
            writer.WriteLine("================================");
            writer.Flush();
        }

        private void CloseWriter() {
            if (writer == null) {
                return;
            }
            try {
                writer.Flush();
                writer.Dispose();
            } catch (Exception exception) {
                Debug.LogWarning("LogService failed to close writer: " + exception.Message);
            } finally {
                writer = null;
                currentDateStamp = null;
            }
        }
    }
}
