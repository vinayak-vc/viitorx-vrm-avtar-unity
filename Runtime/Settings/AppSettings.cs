using System;

using UnityEngine;

namespace VirtualMirror.Settings {
    /// <summary>
    /// Persisted application settings (schema v1). Serialized to settings.json via
    /// <see cref="UnityEngine.JsonUtility"/>; camelCase backing fields keep the emitted JSON keys
    /// matching the documented schema (SDS-015).
    ///
    /// H2 (audit 2026-08-07) — SINGLE SOURCE OF TRUTH. Runtime configuration (camera, tracking,
    /// smoothing, IK, display) lives ONLY on the <c>[SerializeField]</c> fields of AppBootstrap, tuned
    /// in the Inspector / Bootstrap scene. The old AppSettings.Camera/Tracking/Smoothing/Ik/Background/
    /// Display sections were written to disk but never read back, and their defaults diverged from the
    /// real fields (e.g. camera fps 60 here vs 30 on AppBootstrap) — this was the "config in two places"
    /// root cause. Those sections were removed; only the genuinely-consumed avatar path persists here.
    /// Old settings.json files still load cleanly (JsonUtility ignores the now-absent keys and simply
    /// stops re-emitting them on the next save).
    /// </summary>
    [Serializable]
    public sealed class AppSettings {
        [SerializeField] private int schemaVersion;
        [SerializeField] private AvatarSettings avatar;

        public int SchemaVersion {
            get {
                return schemaVersion;
            }
            set {
                schemaVersion = value;
            }
        }

        public AvatarSettings Avatar {
            get {
                return avatar;
            }
            set {
                avatar = value;
            }
        }

        public static AppSettings CreateDefault() {
            AppSettings settings = new AppSettings();
            settings.schemaVersion = SettingsSchema.CurrentVersion;
            settings.avatar = AvatarSettings.CreateDefault();
            return settings;
        }
    }

    [Serializable]
    public sealed class AvatarSettings {
        [SerializeField] private string lastPath;
        [SerializeField] private string libraryId;

        public string LastPath {
            get {
                return lastPath;
            }
            set {
                lastPath = value;
            }
        }

        public string LibraryId {
            get {
                return libraryId;
            }
            set {
                libraryId = value;
            }
        }

        public static AvatarSettings CreateDefault() {
            AvatarSettings settings = new AvatarSettings();
            settings.lastPath = string.Empty;
            settings.libraryId = "builtin_01";
            return settings;
        }
    }
}
