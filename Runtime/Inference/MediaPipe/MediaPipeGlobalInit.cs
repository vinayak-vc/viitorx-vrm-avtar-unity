using System;

using VirtualMirror.Core;

namespace VirtualMirror.Tracking.MediaPipe {
    /// <summary>
    /// Reference-counted MediaPipe global initialization (Protobuf log handler + Glog). Shared by every
    /// MediaPipe task provider (pose, face, ...) so the native library is initialized exactly once and
    /// shut down only after the last provider is disposed. Calling Glog.Initialize twice crashes the
    /// native process (see ai_handoff.md), so coordination lives here rather than per-provider.
    /// </summary>
    internal static class MediaPipeGlobalInit {
        private static readonly object gate = new object();
        private static int refCount;
        private static bool initialized;

        public static void Acquire(ILogService logService) {
            lock (gate) {
                if (!initialized) {
                    try {
                        Mediapipe.Protobuf.SetLogHandler(Mediapipe.Protobuf.DefaultLogHandler);
                        Mediapipe.Glog.Initialize("MediaPipeUnityPlugin");
                    } catch (Exception exception) {
                        if (logService != null) {
                            logService.LogException(exception, "MediaPipe global init warning");
                        }
                    }
                    initialized = true;
                }
                refCount = refCount + 1;
            }
        }

        public static void Release(ILogService logService) {
            lock (gate) {
                if (refCount <= 0) {
                    return;
                }
                refCount = refCount - 1;
                if (refCount == 0 && initialized) {
                    try {
                        Mediapipe.Glog.Shutdown();
                    } catch (Exception exception) {
                        if (logService != null) {
                            logService.LogException(exception, "Failed to shutdown MediaPipe Glog");
                        }
                    }
                    initialized = false;
                }
            }
        }
    }
}
