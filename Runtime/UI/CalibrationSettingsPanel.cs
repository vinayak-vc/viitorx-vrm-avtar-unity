using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualMirror.UI {
    /// <summary>
    /// Calibration and settings UI panel for configuring camera devices, smoothing parameters, and feature toggles.
    /// </summary>
    public sealed class CalibrationSettingsPanel : MonoBehaviour {
        [SerializeField] private TMP_Dropdown cameraDropdown;
        [SerializeField] private Toggle mirrorFlipToggle;
        [SerializeField] private Toggle ikToggle;
        [SerializeField] private Toggle faceToggle;
        [SerializeField] private Toggle handToggle;
        [SerializeField] private Slider filterMinCutoffSlider;
        [SerializeField] private TextMeshProUGUI filterMinCutoffValueText;
        [SerializeField] private Slider filterBetaSlider;
        [SerializeField] private TextMeshProUGUI filterBetaValueText;
        [SerializeField] private Button closeButton;

        public event Action<string> OnCameraDeviceChanged;
        public event Action<bool> OnMirrorFlipToggled;
        public event Action<bool> OnIkToggled;
        public event Action<bool> OnFaceToggled;
        public event Action<bool> OnHandToggled;
        public event Action<float> OnFilterMinCutoffChanged;
        public event Action<float> OnFilterBetaChanged;

        private void Awake() {
            PopulateCameraDropdown();

            if (cameraDropdown != null) {
                cameraDropdown.onValueChanged.AddListener(HandleCameraChanged);
            }
            if (mirrorFlipToggle != null) {
                mirrorFlipToggle.onValueChanged.AddListener(HandleMirrorFlipToggled);
            }
            if (ikToggle != null) {
                ikToggle.onValueChanged.AddListener(HandleIkToggled);
            }
            if (faceToggle != null) {
                faceToggle.onValueChanged.AddListener(HandleFaceToggled);
            }
            if (handToggle != null) {
                handToggle.onValueChanged.AddListener(HandleHandToggled);
            }
            if (filterMinCutoffSlider != null) {
                filterMinCutoffSlider.onValueChanged.AddListener(HandleFilterMinCutoffChanged);
            }
            if (filterBetaSlider != null) {
                filterBetaSlider.onValueChanged.AddListener(HandleFilterBetaChanged);
            }
            if (closeButton != null) {
                closeButton.onClick.AddListener(HidePanel);
            }
        }

        public void PopulateCameraDropdown() {
            if (cameraDropdown == null) {
                return;
            }

            cameraDropdown.ClearOptions();
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            WebCamDevice[] devices = WebCamTexture.devices;

            if (devices == null || devices.Length == 0) {
                options.Add(new TMP_Dropdown.OptionData("Default Webcam"));
            } else {
                int i = 0;
                while (i < devices.Length) {
                    options.Add(new TMP_Dropdown.OptionData(devices[i].name));
                    i = i + 1;
                }
            }

            cameraDropdown.AddOptions(options);
        }

        public void SetInitialValues(bool mirrorFlip, bool ikEnabled, bool faceEnabled, bool handEnabled, float minCutoff, float beta) {
            if (mirrorFlipToggle != null) {
                mirrorFlipToggle.isOn = mirrorFlip;
            }
            if (ikToggle != null) {
                ikToggle.isOn = ikEnabled;
            }
            if (faceToggle != null) {
                faceToggle.isOn = faceEnabled;
            }
            if (handToggle != null) {
                handToggle.isOn = handEnabled;
            }
            if (filterMinCutoffSlider != null) {
                filterMinCutoffSlider.value = minCutoff;
                UpdateMinCutoffLabel(minCutoff);
            }
            if (filterBetaSlider != null) {
                filterBetaSlider.value = beta;
                UpdateBetaLabel(beta);
            }
        }

        public void ShowPanel() {
            gameObject.SetActive(true);
        }

        public void HidePanel() {
            gameObject.SetActive(false);
        }

        public void TogglePanel() {
            gameObject.SetActive(!gameObject.activeSelf);
        }

        private void HandleCameraChanged(int index) {
            if (cameraDropdown != null && OnCameraDeviceChanged != null) {
                string selectedDevice = cameraDropdown.options[index].text;
                OnCameraDeviceChanged.Invoke(selectedDevice);
            }
        }

        private void HandleMirrorFlipToggled(bool value) {
            if (OnMirrorFlipToggled != null) {
                OnMirrorFlipToggled.Invoke(value);
            }
        }

        private void HandleIkToggled(bool value) {
            if (OnIkToggled != null) {
                OnIkToggled.Invoke(value);
            }
        }

        private void HandleFaceToggled(bool value) {
            if (OnFaceToggled != null) {
                OnFaceToggled.Invoke(value);
            }
        }

        private void HandleHandToggled(bool value) {
            if (OnHandToggled != null) {
                OnHandToggled.Invoke(value);
            }
        }

        private void HandleFilterMinCutoffChanged(float value) {
            UpdateMinCutoffLabel(value);
            if (OnFilterMinCutoffChanged != null) {
                OnFilterMinCutoffChanged.Invoke(value);
            }
        }

        private void HandleFilterBetaChanged(float value) {
            UpdateBetaLabel(value);
            if (OnFilterBetaChanged != null) {
                OnFilterBetaChanged.Invoke(value);
            }
        }

        private void UpdateMinCutoffLabel(float value) {
            if (filterMinCutoffValueText != null) {
                filterMinCutoffValueText.text = value.ToString("0.00");
            }
        }

        private void UpdateBetaLabel(float value) {
            if (filterBetaValueText != null) {
                filterBetaValueText.text = value.ToString("0.000");
            }
        }
    }
}
