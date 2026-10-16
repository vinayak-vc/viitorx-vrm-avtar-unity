using System;

namespace VirtualMirror.Core {
    /// <summary>
    /// Resolves absolute paths for all user data (avatars, settings, calibration, logs).
    /// Feature code must request paths here instead of hardcoding absolute locations.
    /// </summary>
    public interface IPathProvider {
        string RootDirectory { get; }
        string AvatarsDirectory { get; }
        string SettingsDirectory { get; }
        string CalibrationDirectory { get; }
        string BackgroundsDirectory { get; }
        string LogsDirectory { get; }
        string SettingsFilePath { get; }
        string GetCalibrationFilePath(string profileId);
        string GetLogFilePath(DateTime date);
        void EnsureDirectories();
    }
}
