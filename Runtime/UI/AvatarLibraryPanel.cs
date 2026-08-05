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
        [SerializeField] private Button toggleUiButton;
        [SerializeField] private TMP_Text statusText;

        private IAvatarSession avatarSession;
        private ILogService logService;
        private bool isLoading;
        private bool loadUiVisible = true;

        public void Initialize(IAvatarSession avatarSession, ILogService logService) {
            this.avatarSession = avatarSession;
            this.logService = logService;
            avatarSession.AvatarChanged += HandleAvatarChanged;
            SetStatus("Enter a full .vrm path and press Load.");
        }

        private void Awake() {
            if (loadButton != null) {
                loadButton.onClick.AddListener(OnLoadClicked);
            }
            if (toggleUiButton != null) {
                toggleUiButton.onClick.AddListener(OnToggleClicked);
            }
        }

        private void OnDestroy() {
            if (loadButton != null) {
                loadButton.onClick.RemoveListener(OnLoadClicked);
            }
            if (toggleUiButton != null) {
                toggleUiButton.onClick.RemoveListener(OnToggleClicked);
            }
            if (avatarSession != null) {
                avatarSession.AvatarChanged -= HandleAvatarChanged;
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

        private void OnLoadClicked() {
            if (isLoading) {
                return;
            }
            if (avatarSession == null) {
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
            isLoading = true;
            SetInteractable(false);
            SetStatus("Loading: " + path);
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
