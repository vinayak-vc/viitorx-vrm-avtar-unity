using System;
using System.IO;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Settings {
    /// <summary>
    /// Loads and persists <see cref="AppSettings"/> as UTF-8 JSON via <see cref="IPathProvider"/>.
    /// Writes atomically (temp file + replace), debounces saves, and recovers from a corrupt
    /// file by backing it up and recreating defaults. Never blocks callers on a long operation.
    /// </summary>
    public sealed class SettingsStore {
        private const string BackupExtension = ".bak";
        private const string TempExtension = ".tmp";
        private const float DefaultDebounceSeconds = 0.4f;

        private readonly IPathProvider pathProvider;
        private readonly ILogService logService;
        private readonly float debounceSeconds;

        private AppSettings current;
        private bool saveRequested;
        private float saveTimer;

        public SettingsStore(IPathProvider pathProvider, ILogService logService, float debounceSeconds) {
            if (pathProvider == null) {
                throw new ArgumentNullException(nameof(pathProvider));
            }
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            this.pathProvider = pathProvider;
            this.logService = logService;
            if (debounceSeconds > 0f) {
                this.debounceSeconds = debounceSeconds;
            } else {
                this.debounceSeconds = DefaultDebounceSeconds;
            }
            current = AppSettings.CreateDefault();
        }

        public AppSettings Current {
            get {
                return current;
            }
        }

        public AppSettings Load() {
            pathProvider.EnsureDirectories();
            string path = pathProvider.SettingsFilePath;
            if (!File.Exists(path)) {
                current = AppSettings.CreateDefault();
                WriteToDisk(current);
                logService.Log(LogLevel.Info, "Settings file not found; wrote defaults to " + path);
                return current;
            }
            try {
                string json = File.ReadAllText(path);
                AppSettings loaded = JsonUtility.FromJson<AppSettings>(json);
                if (loaded == null) {
                    throw new InvalidDataException("Settings JSON deserialized to null.");
                }
                current = Migrate(loaded);
                logService.Log(LogLevel.Info, "Settings loaded from " + path);
                return current;
            } catch (Exception exception) {
                HandleCorruptFile(path, exception);
                current = AppSettings.CreateDefault();
                WriteToDisk(current);
                return current;
            }
        }

        public void RequestSave() {
            saveRequested = true;
            saveTimer = debounceSeconds;
        }

        public void Tick(float deltaSeconds) {
            if (!saveRequested) {
                return;
            }
            saveTimer -= deltaSeconds;
            if (saveTimer > 0f) {
                return;
            }
            Flush();
        }

        public void Flush() {
            if (!saveRequested) {
                return;
            }
            saveRequested = false;
            saveTimer = 0f;
            WriteToDisk(current);
        }

        private AppSettings Migrate(AppSettings settings) {
            if (SettingsSchema.IsSupported(settings.SchemaVersion)) {
                settings.SchemaVersion = SettingsSchema.CurrentVersion;
                return settings;
            }
            logService.Log(LogLevel.Warning, "Unsupported settings schemaVersion " + settings.SchemaVersion + "; applying defaults.");
            return AppSettings.CreateDefault();
        }

        private void HandleCorruptFile(string path, Exception exception) {
            logService.LogException(exception, "Corrupt settings file; recreating defaults");
            try {
                string backupPath = path + BackupExtension;
                if (File.Exists(backupPath)) {
                    File.Delete(backupPath);
                }
                File.Move(path, backupPath);
                logService.Log(LogLevel.Warning, "Corrupt settings backed up to " + backupPath);
            } catch (Exception backupException) {
                logService.LogException(backupException, "Failed to back up corrupt settings file");
            }
        }

        private void WriteToDisk(AppSettings settings) {
            string path = pathProvider.SettingsFilePath;
            string tempPath = path + TempExtension;
            try {
                pathProvider.EnsureDirectories();
                string json = JsonUtility.ToJson(settings, true);
                File.WriteAllText(tempPath, json);
                if (File.Exists(path)) {
                    File.Replace(tempPath, path, null);
                } else {
                    File.Move(tempPath, path);
                }
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to write settings file");
                TryDeleteTemp(tempPath);
            }
        }

        private void TryDeleteTemp(string tempPath) {
            if (!File.Exists(tempPath)) {
                return;
            }
            try {
                File.Delete(tempPath);
            } catch (Exception exception) {
                logService.LogException(exception, "Failed to remove temp settings file");
            }
        }
    }
}
