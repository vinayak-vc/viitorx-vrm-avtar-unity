using System;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

using VirtualMirror.Core;

namespace VirtualMirror.UI {
    /// <summary>
    /// Minimal "Load Avatar" panel: the user types a full absolute .vrm path and presses Load.
    /// A native file browser can replace the text field later. Depends only on Core abstractions,
    /// so it never references UniVRM or the App composition root. Uses TextMeshPro for text.
    /// </summary>
    public sealed class AvatarLibraryPanel : MonoBehaviour {
        [SerializeField] private TMP_InputField pathInput;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button browseButton;
        [SerializeField] private TMP_Dropdown presetDropdown;
        [SerializeField] private Button toggleUiButton;
        [SerializeField] private TMP_Text statusText;

        private IAvatarSession avatarSession;
        private ILogService logService;
        private bool isLoading;
        private bool loadUiVisible = true;
        private System.Collections.Generic.List<string> presetPaths = new System.Collections.Generic.List<string>();

        public void Initialize(IAvatarSession avatarSession, ILogService logService) {
            this.avatarSession = avatarSession;
            this.logService = logService;
            avatarSession.AvatarChanged += HandleAvatarChanged;
            PopulatePresets();
            SetStatus("Select a preset avatar or click Browse to load custom .vrm.");
        }

        private void Awake() {
            if (browseButton == null) {
                Transform bTransform = transform.Find("BrowseRow/BrowseButton");
                if (bTransform != null) {
                    browseButton = bTransform.GetComponent<Button>();
                }
            }
            if (presetDropdown == null) {
                Transform pTransform = transform.Find("PresetDropdown");
                if (pTransform != null) {
                    presetDropdown = pTransform.GetComponent<TMP_Dropdown>();
                }
            }
            if (loadButton == null) {
                Transform lTransform = transform.Find("LoadButton");
                if (lTransform != null) {
                    loadButton = lTransform.GetComponent<Button>();
                }
            }
            if (pathInput == null) {
                Transform iTransform = transform.Find("BrowseRow/PathInputField");
                if (iTransform != null) {
                    pathInput = iTransform.GetComponent<TMP_InputField>();
                }
            }
            if (statusText == null) {
                Transform sTransform = transform.Find("StatusText");
                if (sTransform != null) {
                    statusText = sTransform.GetComponent<TMP_Text>();
                }
            }

            if (loadButton != null) {
                loadButton.onClick.AddListener(OnLoadClicked);
            }
            if (browseButton != null) {
                browseButton.onClick.AddListener(OnBrowseClicked);
            }
            if (presetDropdown != null) {
                presetDropdown.onValueChanged.AddListener(OnPresetSelected);
            }
            if (toggleUiButton != null) {
                toggleUiButton.onClick.AddListener(OnToggleClicked);
            }
        }

        private void OnDestroy() {
            if (loadButton != null) {
                loadButton.onClick.RemoveListener(OnLoadClicked);
            }
            if (browseButton != null) {
                browseButton.onClick.RemoveListener(OnBrowseClicked);
            }
            if (presetDropdown != null) {
                presetDropdown.onValueChanged.RemoveListener(OnPresetSelected);
            }
            if (toggleUiButton != null) {
                toggleUiButton.onClick.RemoveListener(OnToggleClicked);
            }
            if (avatarSession != null) {
                avatarSession.AvatarChanged -= HandleAvatarChanged;
            }
        }

        public void PopulatePresets() {
            if (presetDropdown == null) {
                return;
            }
            presetDropdown.ClearOptions();
            presetPaths.Clear();

            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();
            options.Add("Select Preset Avatar...");
            presetPaths.Add(null);

            string folder = System.IO.Path.Combine(Application.streamingAssetsPath, "Avatars");
            if (System.IO.Directory.Exists(folder)) {
                string[] files = System.IO.Directory.GetFiles(folder, "*.vrm");
                int index = 0;
                while (index < files.Length) {
                    string fullPath = files[index];
                    string fileName = System.IO.Path.GetFileName(fullPath);
                    options.Add(fileName);
                    presetPaths.Add(fullPath);
                    index = index + 1;
                }
            }
            presetDropdown.AddOptions(options);
        }

        private void OnPresetSelected(int index) {
            if (index <= 0 || index >= presetPaths.Count) {
                return;
            }
            string path = presetPaths[index];
            if (!string.IsNullOrEmpty(path)) {
                if (pathInput != null) {
                    pathInput.text = path;
                }
                RunLoad(path);
            }
        }

        private void OnBrowseClicked() {
            string filter = "VRM Avatar Files (*.vrm)|*.vrm|All Files (*.*)|*.*";
            string initialDir = Application.streamingAssetsPath;
            string selectedFile = VirtualMirror.IO.FileExplorer.OpenFileExplorer(filter, initialDir);
            if (string.IsNullOrEmpty(selectedFile)) {
                selectedFile = VirtualMirror.IO.NativeFileDialog.OpenFile("Select VRM Avatar", "VRM Avatar Files (*.vrm)\0*.vrm\0All Files (*.*)\0*.*\0\0", "vrm");
            }
            if (!string.IsNullOrEmpty(selectedFile)) {
                if (pathInput != null) {
                    pathInput.text = selectedFile;
                }
                RunLoad(selectedFile);
            }
        }

        private void OnToggleClicked() {
            SetLoadUiVisible(!loadUiVisible);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                SetLoadUiVisible(!loadUiVisible);
            }
        }

        private void HandleAvatarChanged() {
            SetLoadUiVisible(false);
        }

        private void SetLoadUiVisible(bool visible) {
            loadUiVisible = visible;
            if (pathInput != null) {
                pathInput.gameObject.SetActive(visible);
            }
            if (loadButton != null) {
                loadButton.gameObject.SetActive(visible);
            }
            if (statusText != null) {
                statusText.gameObject.SetActive(visible);
            }
        }

        private bool EnsureSession() {
            if (avatarSession != null) {
                return true;
            }
            UnityEngine.Object[] objects = UnityEngine.Object.FindObjectsByType(typeof(MonoBehaviour), FindObjectsSortMode.None);
            int index = 0;
            while (index < objects.Length) {
                MonoBehaviour mono = objects[index] as MonoBehaviour;
                index = index + 1;
                if (mono != null && mono.GetType().Name == "AppBootstrap") {
                    System.Reflection.PropertyInfo prop = mono.GetType().GetProperty("AvatarSession");
                    if (prop != null) {
                        IAvatarSession session = prop.GetValue(mono) as IAvatarSession;
                        if (session != null) {
                            avatarSession = session;
                            avatarSession.AvatarChanged += HandleAvatarChanged;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void OnLoadClicked() {
            if (isLoading) {
                return;
            }
            if (!EnsureSession()) {
                SetStatus("Avatar session is not ready yet.");
                return;
            }
            string path = pathInput != null ? pathInput.text : null;
            if (string.IsNullOrEmpty(path)) {
                SetStatus("Path is empty.");
                return;
            }
            RunLoad(path.Trim());
        }

        private async void RunLoad(string path) {
            if (!EnsureSession()) {
                SetStatus("Avatar session is not ready yet.");
                return;
            }
            isLoading = true;
            SetInteractable(false);
            SetStatus("Loading: " + System.IO.Path.GetFileName(path));
            try {
                AvatarLoadResult result = await avatarSession.LoadFromPathAsync(path, true);
                if (result.IsSuccess) {
                    SetStatus("Loaded: " + path);
                } else {
                    SetStatus("Failed (" + result.Status + "): " + result.Message);
                }
            } catch (Exception exception) {
                if (logService != null) {
                    logService.LogException(exception, "Avatar load from UI threw");
                }
                SetStatus("Error: " + exception.Message);
            } finally {
                isLoading = false;
                SetInteractable(true);
            }
        }

        private void SetInteractable(bool interactable) {
            if (loadButton != null) {
                loadButton.interactable = interactable;
            }
            if (pathInput != null) {
                pathInput.interactable = interactable;
            }
        }

        private void SetStatus(string message) {
            if (statusText != null) {
                statusText.text = message;
            }
        }
    }
}
