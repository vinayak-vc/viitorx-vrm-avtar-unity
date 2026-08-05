using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Structured logging surface shared across the application.
    /// Implementations must be safe to call from any thread and must never throw to callers.
    /// </summary>
    public interface ILogService {
        void Log(LogLevel level, string message);
        void LogException(Exception exception, string context);
        void Flush();
    }
}
