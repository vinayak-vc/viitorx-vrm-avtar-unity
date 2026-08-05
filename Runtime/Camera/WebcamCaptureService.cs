using System;
using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Camera {
    /// <summary>
    /// <see cref="ICameraCapture"/> backed by Unity <see cref="WebCamTexture"/>. Enumerates and
    /// opens webcams, exposing the live texture for tracking providers to sample. Main thread only.
    /// </summary>
    public sealed class WebcamCaptureService : ICameraCapture {
        private readonly ILogService logService;

        private WebCamTexture webCamTexture;

        public WebcamCaptureService(ILogService logService) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            this.logService = logService;
        }

        public bool IsRunning {
            get {
                return webCamTexture != null && webCamTexture.isPlaying;
            }
        }

        public int Width {
            get {
                if (webCamTexture == null) {
                    return 0;
                }
                return webCamTexture.width;
            }
        }

        public int Height {
            get {
                if (webCamTexture == null) {
                    return 0;
                }
                return webCamTexture.height;
            }
        }

        public bool DidUpdateThisFrame {
            get {
                return webCamTexture != null && webCamTexture.didUpdateThisFrame;
            }
        }

        public Texture CurrentTexture {
            get {
                return webCamTexture;
            }
        }

        public IReadOnlyList<CameraDeviceInfo> EnumerateDevices() {
            WebCamDevice[] devices = WebCamTexture.devices;
            List<CameraDeviceInfo> result = new List<CameraDeviceInfo>(devices.Length);
            int index = 0;
            while (index < devices.Length) {
                WebCamDevice device = devices[index];
                result.Add(new CameraDeviceInfo(device.name, device.isFrontFacing));
                index = index + 1;
            }
            return result;
        }

        public bool StartCapture(CameraCaptureRequest request) {
            if (request == null) {
                logService.Log(LogLevel.Warning, "Camera start request was null.");
                return false;
            }
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0) {
                logService.Log(LogLevel.Warning, "No webcam devices available.");
                return false;
            }
            string deviceName = ResolveDeviceName(request.DeviceName, devices);
            StopCapture();
            webCamTexture = new WebCamTexture(deviceName, request.RequestedWidth, request.RequestedHeight, request.RequestedFps);
            webCamTexture.Play();
            logService.Log(LogLevel.Info, "Webcam started: '" + deviceName + "' requested " + request.RequestedWidth + "x" + request.RequestedHeight + "@" + request.RequestedFps + "fps.");
            return true;
        }

        public void StopCapture() {
            if (webCamTexture == null) {
                return;
            }
            if (webCamTexture.isPlaying) {
                webCamTexture.Stop();
            }
            UnityEngine.Object.Destroy(webCamTexture);
            webCamTexture = null;
        }

        public void Dispose() {
            StopCapture();
        }

        private static string ResolveDeviceName(string requestedName, WebCamDevice[] devices) {
            if (!string.IsNullOrEmpty(requestedName)) {
                int index = 0;
                while (index < devices.Length) {
                    if (string.Equals(devices[index].name, requestedName, StringComparison.Ordinal)) {
                        return requestedName;
                    }
                    index = index + 1;
                }
            }
            return devices[0].name;
        }
    }
}
