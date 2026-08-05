using System;
using System.IO;

using VirtualMirror.Core;

namespace VirtualMirror.IO {
    /// <summary>
    /// Default <see cref="IPathProvider"/> rooted at %USERPROFILE%/Documents/MyMirror.
    /// Creates the directory tree on demand.
    /// </summary>
    public sealed class PathProvider : IPathProvider {
        private const string RootFolderName = "MyMirror";
        private const string AvatarsFolderName = "Avatars";
        private const string SettingsFolderName = "Settings";
        private const string CalibrationFolderName = "Calibration";
        private const string BackgroundsFolderName = "Backgrounds";
        private const string LogsFolderName = "Logs";
        private const string SettingsFileName = "settings.json";
        private const string LogFilePrefix = "virtual-mirror-";
        private const string LogFileExtension = ".log";

        private readonly string rootDirectory;

        public PathProvider() {
            string documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            rootDirectory = Path.Combine(documentsDirectory, RootFolderName);
        }

        public string RootDirectory {
            get {
                return rootDirectory;
            }
        }

        public string AvatarsDirectory {
            get {
                return Path.Combine(rootDirectory, AvatarsFolderName);
            }
        }

        public string SettingsDirectory {
            get {
                return Path.Combine(rootDirectory, SettingsFolderName);
            }
        }

        public string CalibrationDirectory {
            get {
                return Path.Combine(rootDirectory, CalibrationFolderName);
            }
        }

        public string BackgroundsDirectory {
            get {
                return Path.Combine(rootDirectory, BackgroundsFolderName);
            }
        }

        public string LogsDirectory {
            get {
                return Path.Combine(rootDirectory, LogsFolderName);
            }
        }

        public string SettingsFilePath {
            get {
                return Path.Combine(SettingsDirectory, SettingsFileName);
            }
        }

        public string GetCalibrationFilePath(string profileId) {
            if (string.IsNullOrWhiteSpace(profileId)) {
                throw new ArgumentException("Calibration profile id must not be null or empty.", nameof(profileId));
            }
            return Path.Combine(CalibrationDirectory, profileId + ".json");
        }

        public string GetLogFilePath(DateTime date) {
            string fileName = LogFilePrefix + date.ToString("yyyyMMdd") + LogFileExtension;
            return Path.Combine(LogsDirectory, fileName);
        }

        public void EnsureDirectories() {
            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(AvatarsDirectory);
            Directory.CreateDirectory(SettingsDirectory);
            Directory.CreateDirectory(CalibrationDirectory);
            Directory.CreateDirectory(BackgroundsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
    }
}
