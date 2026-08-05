using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Video;

using VirtualMirror.Core;

namespace VirtualMirror.Camera {
    /// <summary>
    /// <see cref="ICameraCapture"/> that plays a video file into a <see cref="RenderTexture"/> so it
    /// can stand in for a live webcam during development. Same interface as the webcam service, so
    /// tracking providers are agnostic to the source. Main thread only.
    /// </summary>
    public sealed class VideoFileCaptureService : ICameraCapture {
        private const int DefaultWidth = 1280;
        private const int DefaultHeight = 720;

        private readonly ILogService logService;
        private readonly string videoFilePath;

        private GameObject host;
        private VideoPlayer player;
        private RenderTexture renderTexture;

        public VideoFileCaptureService(ILogService logService, string videoFilePath) {
            if (logService == null) {
                throw new ArgumentNullException(nameof(logService));
            }
            this.logService = logService;
            this.videoFilePath = videoFilePath;
        }

        public bool IsRunning {
            get {
                return player != null && player.isPlaying;
            }
        }

        public int Width {
            get {
                if (renderTexture == null) {
                    return 0;
                }
                return renderTexture.width;
            }
        }

        public int Height {
            get {
                if (renderTexture == null) {
                    return 0;
                }
                return renderTexture.height;
            }
        }

        public bool DidUpdateThisFrame {
            get {
                return player != null && player.isPlaying;
            }
        }

        public Texture CurrentTexture {
            get {
                return renderTexture;
            }
        }

        public IReadOnlyList<CameraDeviceInfo> EnumerateDevices() {
            List<CameraDeviceInfo> result = new List<CameraDeviceInfo>(1);
            if (!string.IsNullOrEmpty(videoFilePath)) {
                result.Add(new CameraDeviceInfo(Path.GetFileName(videoFilePath), false));
            }
            return result;
        }

        public bool StartCapture(CameraCaptureRequest request) {
            if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath)) {
                logService.Log(LogLevel.Warning, "Sample video not found: " + videoFilePath);
                return false;
            }
            StopCapture();

            int width = request != null && request.RequestedWidth > 0 ? request.RequestedWidth : DefaultWidth;
            int height = request != null && request.RequestedHeight > 0 ? request.RequestedHeight : DefaultHeight;

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            renderTexture.Create();

            host = new GameObject("VideoCaptureHost");
            host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(host);

            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.source = VideoSource.Url;
            player.url = videoFilePath;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = renderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.errorReceived += HandleVideoError;
            player.Play();

            logService.Log(LogLevel.Info, "Video capture started: '" + videoFilePath + "' into " + width + "x" + height + " render texture.");
            return true;
        }

        public void StopCapture() {
            if (player != null) {
                player.errorReceived -= HandleVideoError;
                player.Stop();
            }
            if (host != null) {
                UnityEngine.Object.Destroy(host);
                host = null;
            }
            player = null;
            if (renderTexture != null) {
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
                renderTexture = null;
            }
        }

        public void Dispose() {
            StopCapture();
        }

        private void HandleVideoError(VideoPlayer source, string message) {
            logService.Log(LogLevel.Error, "Video capture error: " + message);
        }
    }
}
