using System;

using UnityEngine;

namespace VirtualMirror.Settings {
    /// <summary>
    /// Strongly typed application settings (schema v1). Serialized to settings.json via
    /// <see cref="UnityEngine.JsonUtility"/>. Backing fields are camelCase so the emitted
    /// JSON keys match the documented schema (SDS-015).
    /// </summary>
    [Serializable]
    public sealed class AppSettings {
        [SerializeField] private int schemaVersion;
        [SerializeField] private AvatarSettings avatar;
        [SerializeField] private CameraSettings camera;
        [SerializeField] private TrackingSettings tracking;
        [SerializeField] private SmoothingSettings smoothing;
        [SerializeField] private IkSettings ik;
        [SerializeField] private BackgroundSettings background;
        [SerializeField] private DisplaySettings display;
        [SerializeField] private string calibrationProfileId;

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

        public CameraSettings Camera {
            get {
                return camera;
            }
            set {
                camera = value;
            }
        }

        public TrackingSettings Tracking {
            get {
                return tracking;
            }
            set {
                tracking = value;
            }
        }

        public SmoothingSettings Smoothing {
            get {
                return smoothing;
            }
            set {
                smoothing = value;
            }
        }

        public IkSettings Ik {
            get {
                return ik;
            }
            set {
                ik = value;
            }
        }

        public BackgroundSettings Background {
            get {
                return background;
            }
            set {
                background = value;
            }
        }

        public DisplaySettings Display {
            get {
                return display;
            }
            set {
                display = value;
            }
        }

        public string CalibrationProfileId {
            get {
                return calibrationProfileId;
            }
            set {
                calibrationProfileId = value;
            }
        }

        public static AppSettings CreateDefault() {
            AppSettings settings = new AppSettings();
            settings.schemaVersion = SettingsSchema.CurrentVersion;
            settings.avatar = AvatarSettings.CreateDefault();
            settings.camera = CameraSettings.CreateDefault();
            settings.tracking = TrackingSettings.CreateDefault();
            settings.smoothing = SmoothingSettings.CreateDefault();
            settings.ik = IkSettings.CreateDefault();
            settings.background = BackgroundSettings.CreateDefault();
            settings.display = DisplaySettings.CreateDefault();
            settings.calibrationProfileId = "default";
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

    [Serializable]
    public sealed class CameraSettings {
        [SerializeField] private string deviceName;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int fps;
        [SerializeField] private bool mirrorHorizontal;

        public string DeviceName {
            get {
                return deviceName;
            }
            set {
                deviceName = value;
            }
        }

        public int Width {
            get {
                return width;
            }
            set {
                width = value;
            }
        }

        public int Height {
            get {
                return height;
            }
            set {
                height = value;
            }
        }

        public int Fps {
            get {
                return fps;
            }
            set {
                fps = value;
            }
        }

        public bool MirrorHorizontal {
            get {
                return mirrorHorizontal;
            }
            set {
                mirrorHorizontal = value;
            }
        }

        public static CameraSettings CreateDefault() {
            CameraSettings settings = new CameraSettings();
            settings.deviceName = string.Empty;
            settings.width = 1280;
            settings.height = 720;
            settings.fps = 60;
            settings.mirrorHorizontal = true;
            return settings;
        }
    }

    [Serializable]
    public sealed class TrackingSettings {
        [SerializeField] private bool enablePose;
        [SerializeField] private bool enableFace;
        [SerializeField] private bool enableHands;
        [SerializeField] private string qualityMode;

        public bool EnablePose {
            get {
                return enablePose;
            }
            set {
                enablePose = value;
            }
        }

        public bool EnableFace {
            get {
                return enableFace;
            }
            set {
                enableFace = value;
            }
        }

        public bool EnableHands {
            get {
                return enableHands;
            }
            set {
                enableHands = value;
            }
        }

        public string QualityMode {
            get {
                return qualityMode;
            }
            set {
                qualityMode = value;
            }
        }

        public static TrackingSettings CreateDefault() {
            TrackingSettings settings = new TrackingSettings();
            settings.enablePose = true;
            settings.enableFace = true;
            settings.enableHands = true;
            settings.qualityMode = "Balanced";
            return settings;
        }
    }

    [Serializable]
    public sealed class SmoothingSettings {
        [SerializeField] private float body;
        [SerializeField] private float face;
        [SerializeField] private float hands;
        [SerializeField] private float ikBlend;

        public float Body {
            get {
                return body;
            }
            set {
                body = value;
            }
        }

        public float Face {
            get {
                return face;
            }
            set {
                face = value;
            }
        }

        public float Hands {
            get {
                return hands;
            }
            set {
                hands = value;
            }
        }

        public float IkBlend {
            get {
                return ikBlend;
            }
            set {
                ikBlend = value;
            }
        }

        public static SmoothingSettings CreateDefault() {
            SmoothingSettings settings = new SmoothingSettings();
            settings.body = 0.5f;
            settings.face = 0.4f;
            settings.hands = 0.35f;
            settings.ikBlend = 1.0f;
            return settings;
        }
    }

    [Serializable]
    public sealed class IkSettings {
        [SerializeField] private string backend;
        [SerializeField] private bool enableLegs;

        public string Backend {
            get {
                return backend;
            }
            set {
                backend = value;
            }
        }

        public bool EnableLegs {
            get {
                return enableLegs;
            }
            set {
                enableLegs = value;
            }
        }

        public static IkSettings CreateDefault() {
            IkSettings settings = new IkSettings();
            settings.backend = "AnimationRigging";
            settings.enableLegs = true;
            return settings;
        }
    }

    [Serializable]
    public sealed class BackgroundSettings {
        [SerializeField] private string mode;
        [SerializeField] private string color;
        [SerializeField] private string imagePath;

        public string Mode {
            get {
                return mode;
            }
            set {
                mode = value;
            }
        }

        public string Color {
            get {
                return color;
            }
            set {
                color = value;
            }
        }

        public string ImagePath {
            get {
                return imagePath;
            }
            set {
                imagePath = value;
            }
        }

        public static BackgroundSettings CreateDefault() {
            BackgroundSettings settings = new BackgroundSettings();
            settings.mode = "Solid";
            settings.color = "#202020";
            settings.imagePath = string.Empty;
            return settings;
        }
    }

    [Serializable]
    public sealed class DisplaySettings {
        [SerializeField] private bool fullscreen;
        [SerializeField] private bool vsync;
        [SerializeField] private bool showDiagnostics;

        public bool Fullscreen {
            get {
                return fullscreen;
            }
            set {
                fullscreen = value;
            }
        }

        public bool Vsync {
            get {
                return vsync;
            }
            set {
                vsync = value;
            }
        }

        public bool ShowDiagnostics {
            get {
                return showDiagnostics;
            }
            set {
                showDiagnostics = value;
            }
        }

        public static DisplaySettings CreateDefault() {
            DisplaySettings settings = new DisplaySettings();
            settings.fullscreen = false;
            settings.vsync = true;
            settings.showDiagnostics = false;
            return settings;
        }
    }
}
