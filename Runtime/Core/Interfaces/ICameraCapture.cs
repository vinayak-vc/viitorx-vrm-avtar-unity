using System;
using System.Collections.Generic;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Abstracts an RGB camera source (webcam today, native/depth later). Produces a live
    /// <see cref="Texture"/> that tracking providers sample. Implementations run on the main thread.
    /// </summary>
    public interface ICameraCapture : IDisposable {
        bool IsRunning { get; }
        int Width { get; }
        int Height { get; }
        bool DidUpdateThisFrame { get; }
        Texture CurrentTexture { get; }
        IReadOnlyList<CameraDeviceInfo> EnumerateDevices();
        bool StartCapture(CameraCaptureRequest request);
        void StopCapture();
    }
}
