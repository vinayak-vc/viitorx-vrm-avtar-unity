using System;
using System.IO;

using UnityEditor;

using UnityEngine;

using VirtualMirror.App;

namespace VirtualMirror.Editor {
    /// <summary>
    /// AppBootstrap has over a hundred settings spread over ten sections, and the camera is chosen in three of
    /// them. This Inspector has two views. SIMPLE shows one "Quick setup" panel: ONE input choice (webcam, video
    /// file, network / LAN camera, OAK-D) that is written to every place that needs it, the three vision
    /// streams, how many people, and the two switches people reach for. ADVANCED is the full list of collapsible
    /// sections (<see cref="GroupedInspectorEditor"/>) with a short note on what each camera section is for.
    /// Nothing is serialized differently: both views edit the same fields.
    /// </summary>
    [CustomEditor(typeof(AppBootstrap))]
    public sealed class AppBootstrapEditor : GroupedInspectorEditor {
        private const string AdvancedPrefKey = "VirtualMirror.AppBootstrap.AdvancedView";
        private const string DefaultCameraSource = "camera:0";
        private const string DefaultVideoFolder = "Assets/Games/video";

        private enum InputMode {
            Camera = 0,
            VideoFile = 1,
            Network = 2,
            OakD = 3
        }

        private static readonly string[] ModeLabels = new string[] {
            "Webcam / phone camera app",
            "Video file",
            "Network or LAN camera link",
            "OAK-D depth camera"
        };

        private static readonly string[] VideoExtensions = new string[] { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v" };

        public override void OnInspectorGUI() {
            bool advanced = EditorPrefs.GetBool(AdvancedPrefKey, false);
            int choice = GUILayout.Toolbar(advanced ? 1 : 0, new string[] { "Simple", "Advanced" }, GUILayout.Height(24f));
            if (choice != (advanced ? 1 : 0)) {
                advanced = choice == 1;
                EditorPrefs.SetBool(AdvancedPrefKey, advanced);
            }
            EditorGUILayout.Space(4f);

            if (advanced) {
                EditorGUILayout.HelpBox(
                        "The camera is chosen in THREE sections, so they are explained here:\n"
                        + "  Tracking Source: body tracking with the OAK-D depth camera (skeleton, avatar).\n"
                        + "  Video Test Source: body tracking from a webcam, video or LAN camera instead of the OAK-D.\n"
                        + "  Hands and Face: the hands, face and cutout streams, with their OWN Source box.\n"
                        + "Switch to Simple to set one input for all of them.",
                        MessageType.Info);
                base.OnInspectorGUI();
                return;
            }
            serializedObject.Update();
            DrawSimple();
            serializedObject.ApplyModifiedProperties();
        }

        protected override string GroupHint(string title) {
            if (title == "Tracking Source") {
                return "Body tracking (skeleton, avatar) with the OAK-D depth camera and its Python sidecar.";
            }
            if (title == "Video Test Source (F-49)") {
                return "Body tracking from a webcam, video file or LAN camera INSTEAD of the OAK-D. Needs 'Track From Video' on. "
                        + "The Source box in Hands and Face is a separate copy: the Simple view keeps both the same.";
            }
            if (title == "Hands and Face (H-03)") {
                return "Hands, face and cutout streams (no body needed). They read their own Source below (camera, file, LAN link).";
            }
            return null;
        }

        private void DrawSimple() {
            SerializedProperty useOak = serializedObject.FindProperty("useOakUdpTracking");
            SerializedProperty fromVideo = serializedObject.FindProperty("trackFromVideo");
            SerializedProperty visionSource = serializedObject.FindProperty("vision.source");
            SerializedProperty visionPin = serializedObject.FindProperty("vision.lanCameraPin");
            SerializedProperty visionLoop = serializedObject.FindProperty("vision.loopFile");
            if (useOak == null || fromVideo == null || visionSource == null || visionPin == null || visionLoop == null) {
                EditorGUILayout.HelpBox("A setting this view needs was not found; use the Advanced view.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            InputMode current = CurrentMode(useOak.boolValue, fromVideo.boolValue, visionSource.stringValue);
            InputMode picked = (InputMode)EditorGUILayout.Popup("Where the picture comes from", (int)current, ModeLabels);
            if (picked != current) {
                ChangeMode(picked, current, useOak, fromVideo, visionSource);
                current = picked;
            }

            string source = visionSource.stringValue;
            if (current == InputMode.OakD) {
                EditorGUILayout.HelpBox("Body tracking reads the OAK-D. The hands, face and cutout streams cannot use it, so they read a camera:", MessageType.None);
                string newCamera = DrawCameraIndex(visionSource.stringValue);
                if (newCamera != visionSource.stringValue) {
                    visionSource.stringValue = newCamera;
                }
            } else if (current == InputMode.Camera) {
                string newCamera = DrawCameraIndex(source);
                if (newCamera != source) {
                    SetSource(newCamera, useOak, visionSource);
                }
                EditorGUILayout.LabelField(" ", "Try 1 or 2 if 0 is the laptop camera. A phone appears here when DroidCam, Iriun or Camo is running.", EditorStyles.miniLabel);
            } else if (current == InputMode.VideoFile) {
                EditorGUILayout.BeginHorizontal();
                string newPath = EditorGUILayout.TextField("File", source);
                if (GUILayout.Button("Browse...", GUILayout.Width(80f))) {
                    string chosen = BrowseVideo(source);
                    if (!string.IsNullOrEmpty(chosen)) {
                        newPath = chosen;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();
                if (newPath != source) {
                    SetSource(newPath, useOak, visionSource);
                }
                if (!VideoFileExists(newPath)) {
                    EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(newPath)
                            ? "Choose a video file with Browse."
                            : "This file was not found. Check the path, or use Browse.", MessageType.Warning);
                }
                bool loop = EditorGUILayout.Toggle("Play again and again", visionLoop.boolValue);
                if (loop != visionLoop.boolValue) {
                    visionLoop.boolValue = loop;
                }
            } else {
                string newLink = EditorGUILayout.TextField("Link", source);
                if (newLink != source) {
                    SetSource(newLink, useOak, visionSource);
                }
                string pin = EditorGUILayout.PasswordField("LAN camera PIN (if asked)", visionPin.stringValue);
                if (pin != visionPin.stringValue) {
                    visionPin.stringValue = pin;
                }
                EditorGUILayout.LabelField(" ", "http://host:port/video, rtsp://..., or a LAN Camera Streaming viewer link.", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("What to run", EditorStyles.boldLabel);
            DrawToggle(useOak, "Body tracking (skeleton, avatar)",
                    "OFF in the hands, face and cutout showcase scenes: they need no body.");
            SerializedProperty people = serializedObject.FindProperty("videoMaxPeople");
            if (people != null && useOak.boolValue && fromVideo.boolValue) {
                EditorGUILayout.IntSlider(people, 1, 8, new GUIContent("People to track (body)",
                        "1 is the single-person path. 2 or more needs the person detector."));
            }
            DrawEnum("vision.handsMode", "Hands stream", "Off, Auto (starts when a scene asks for it) or Always.");
            DrawEnum("vision.faceMode", "Face stream", "Off, Auto (starts when a scene asks for it) or Always.");
            DrawEnum("vision.cutoutMode", "Cutout stream", "Off, Auto (starts when a scene asks for it) or Always.");
            SerializedProperty cutoutPeople = serializedObject.FindProperty("vision.cutoutMaxPeople");
            if (cutoutPeople != null) {
                EditorGUILayout.IntSlider(cutoutPeople, 1, 8, new GUIContent("People in the cutout",
                        "The most people the cutout looks for. Each costs a little time."));
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
            DrawAutoStart();
            DrawToggle(serializedObject.FindProperty("vision.showPanelOnStart"), "Open the control panel at start",
                    "The runtime control panel opens with the Panel Key (F6) anyway.");

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Everything else (rotation, field of view, depth, models, ports, mask quality and more) is in the Advanced view.", MessageType.None);
        }

        // ---------------------------------------------------------------- mode logic

        private static InputMode CurrentMode(bool bodyOn, bool bodyFromVideo, string source) {
            if (bodyOn && !bodyFromVideo) {
                return InputMode.OakD;
            }
            return Classify(source);
        }

        private static InputMode Classify(string source) {
            string text = string.IsNullOrWhiteSpace(source) ? string.Empty : source.Trim();
            if (text.Length == 0) {
                return InputMode.Camera;
            }
            if (text.StartsWith("camera:", StringComparison.OrdinalIgnoreCase)) {
                return InputMode.Camera;
            }
            int index;
            if (int.TryParse(text, out index)) {
                return InputMode.Camera;
            }
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) {
                return InputMode.Network;
            }
            return InputMode.VideoFile;
        }

        // Switching the choice picks a sensible starting value for the new kind and leaves the body path on or
        // off as the scene had it (the vision-only showcases keep it off); only the OAK-D choice turns it on.
        private void ChangeMode(InputMode picked, InputMode previous, SerializedProperty useOak, SerializedProperty fromVideo,
                SerializedProperty visionSource) {
            if (picked == InputMode.OakD) {
                useOak.boolValue = true;
                fromVideo.boolValue = false;
                if (Classify(visionSource.stringValue) != InputMode.Camera) {
                    visionSource.stringValue = DefaultCameraSource;
                }
                return;
            }
            string start = visionSource.stringValue;
            if (picked == InputMode.Camera && Classify(start) != InputMode.Camera) {
                start = DefaultCameraSource;
            } else if (picked == InputMode.VideoFile && Classify(start) != InputMode.VideoFile) {
                // Never empty: an empty source reads as a webcam and would flip the choice straight back.
                start = FirstVideoIn(DefaultVideoFolder);
                if (string.IsNullOrEmpty(start)) {
                    start = DefaultVideoFolder + "/";
                }
            } else if (picked == InputMode.Network && Classify(start) != InputMode.Network) {
                start = "http://";
            }
            SetSource(start, useOak, visionSource);
            if (previous == InputMode.OakD && useOak.boolValue) {
                fromVideo.boolValue = true;
            }
        }

        // The input is one setting (Hands and Face > Source) read by every stream and by the body path. The body
        // path is switched to video only while the body path is on.
        private void SetSource(string value, SerializedProperty useOak, SerializedProperty visionSource) {
            visionSource.stringValue = value;
            if (useOak.boolValue) {
                serializedObject.FindProperty("trackFromVideo").boolValue = true;
            }
        }

        // ---------------------------------------------------------------- drawing helpers

        private static string DrawCameraIndex(string source) {
            int index = 0;
            string text = string.IsNullOrWhiteSpace(source) ? string.Empty : source.Trim();
            if (text.StartsWith("camera:", StringComparison.OrdinalIgnoreCase)) {
                text = text.Substring("camera:".Length);
            }
            if (!int.TryParse(text, out index)) {
                index = 0;
            }
            int changed = Mathf.Max(0, EditorGUILayout.IntField("Camera number", index));
            return changed == index && Classify(source) == InputMode.Camera ? source : "camera:" + changed;
        }

        private static string ProjectRoot() {
            return Path.GetDirectoryName(Application.dataPath);
        }

        // The first video in the folder, as a project-relative path, so choosing "Video file" starts on something
        // that exists. Empty when the folder has none.
        private static string FirstVideoIn(string projectRelativeFolder) {
            string folder = Path.Combine(ProjectRoot(), projectRelativeFolder);
            if (!Directory.Exists(folder)) {
                return string.Empty;
            }
            string[] files = Directory.GetFiles(folder);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++) {
                string extension = Path.GetExtension(files[i]);
                for (int e = 0; e < VideoExtensions.Length; e++) {
                    if (string.Equals(extension, VideoExtensions[e], StringComparison.OrdinalIgnoreCase)) {
                        return projectRelativeFolder + "/" + Path.GetFileName(files[i]);
                    }
                }
            }
            return string.Empty;
        }

        private static bool VideoFileExists(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return false;
            }
            string full = Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot(), path);
            return File.Exists(full);
        }

        private static string BrowseVideo(string current) {
            string folder = DefaultVideoFolder;
            if (!string.IsNullOrWhiteSpace(current) && Classify(current) == InputMode.VideoFile) {
                string directory = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) {
                    folder = directory;
                }
            }
            string picked = EditorUtility.OpenFilePanelWithFilters("Choose a video", folder,
                    new string[] { "Video", "mp4,webm,mov,mkv,avi,m4v", "All files", "*" });
            if (string.IsNullOrEmpty(picked)) {
                return null;
            }
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
            string normalized = picked.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) {
                return normalized.Substring(projectRoot.Length);
            }
            return normalized;
        }

        private static void DrawToggle(SerializedProperty property, string label, string tooltip) {
            if (property == null) {
                return;
            }
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        }

        private void DrawEnum(string path, string label, string tooltip) {
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null) {
                return;
            }
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        }

        // One switch for both launchers (the body sidecar and the vision senders).
        private void DrawAutoStart() {
            SerializedProperty sidecar = serializedObject.FindProperty("autoStartSidecar");
            SerializedProperty senders = serializedObject.FindProperty("vision.autoStartSenders");
            if (sidecar == null || senders == null) {
                return;
            }
            bool mixed = sidecar.boolValue != senders.boolValue;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent("Start the Python trackers automatically",
                    "ON: pressing Play launches the sidecar and the vision senders. OFF: run them by hand."), sidecar.boolValue);
            if (EditorGUI.EndChangeCheck()) {
                sidecar.boolValue = value;
                senders.boolValue = value;
            }
            EditorGUI.showMixedValue = false;
        }
    }
}
