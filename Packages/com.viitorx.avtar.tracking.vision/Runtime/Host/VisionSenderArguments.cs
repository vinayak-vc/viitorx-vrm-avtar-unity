using System.Globalization;
using System.Text;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// The command lines of the three Python senders, built from <see cref="VisionSettings"/>. Pure: no process, no file, no clock, so the
    /// mapping from a setting to a flag is testable and a change to a flag in the sender is one place here.
    /// The flags are those of <c>tools/hands/hands_udp_sender.py</c>, <c>tools/face/face_udp_sender.py</c> and
    /// <c>tools/cutout/cutout_udp_sender.py</c> (which share <c>tools/video/vision_sender.py</c>).
    /// </summary>
    public static class VisionSenderArguments {
        /// <summary>The hands sender, relative to the sidecar root.</summary>
        public const string HandsScript = "tools/hands/hands_udp_sender.py";

        /// <summary>The face sender, relative to the sidecar root.</summary>
        public const string FaceScript = "tools/face/face_udp_sender.py";

        /// <summary>The cutout sender, relative to the sidecar root.</summary>
        public const string CutoutScript = "tools/cutout/cutout_udp_sender.py";

        public const string LoopbackHost = "127.0.0.1";

        /// <summary>The hands sender's flags (everything after the script path). <paramref name="resolvedSource"/> is the already validated source.</summary>
        public static string Hands(VisionSettings settings, string resolvedSource) {
            StringBuilder sb = new StringBuilder();
            AppendCommon(sb, settings, resolvedSource, settings.HandsPort, settings.Flip, settings.Smoothing, true);
            AppendHandsOptions(sb, settings);
            return sb.ToString();
        }

        private static void AppendHandsOptions(StringBuilder sb, VisionSettings settings) {
            sb.Append(" --max-hands ").Append(Mathf.Clamp(settings.MaxHands, 1, 4));
            sb.Append(" --palm-conf ").Append(Number(Mathf.Clamp(settings.PalmConfidence, 0.05f, 0.95f)));
            sb.Append(" --conf ").Append(Number(Mathf.Clamp(settings.HandConfidence, 0.05f, 0.99f)));
            sb.Append(" --keep-conf ").Append(Number(Mathf.Clamp(settings.HandKeepConfidence, 0.05f, 0.99f)));
            sb.Append(" --hand-scale ").Append(Number(Mathf.Clamp(settings.HandScale, 0.6f, 1.6f)));
            sb.Append(" --sides ").Append(SidesName(settings.HandSides));
        }

        /// <summary>The face sender's flags (everything after the script path).</summary>
        public static string Face(VisionSettings settings, string resolvedSource) {
            StringBuilder sb = new StringBuilder();
            AppendCommon(sb, settings, resolvedSource, settings.FacePort, settings.Flip, settings.Smoothing, true);
            AppendFaceOptions(sb, settings);
            return sb.ToString();
        }

        private static void AppendFaceOptions(StringBuilder sb, VisionSettings settings) {
            sb.Append(" --max-faces ").Append(Mathf.Clamp(settings.MaxFaces, 1, 4));
            sb.Append(" --det-conf ").Append(Number(Mathf.Clamp(settings.FaceDetectorConfidence, 0.05f, 0.95f)));
            sb.Append(" --conf ").Append(Number(Mathf.Clamp(settings.FaceConfidence, 0.05f, 0.99f)));
            sb.Append(" --keep-conf ").Append(Number(Mathf.Clamp(settings.FaceKeepConfidence, 0.05f, 0.99f)));
            sb.Append(" --face-scale ").Append(Number(Mathf.Clamp(settings.FaceScale, 0.6f, 1.6f)));
            sb.Append(settings.Blendshapes ? " --blendshapes" : " --no-blendshapes");
            sb.Append(" --landmarks ").Append(LandmarksName(settings.FaceLandmarks));
        }

        /// <summary>
        /// The cutout sender's flags (everything after the script path). It takes its own mirror and temporal-smoothing switches
        /// (<see cref="VisionSettings.CutoutFlipInSender"/>, <see cref="VisionSettings.CutoutTemporal"/>) and no field of view: nothing in a cutout
        /// is measured in metres. Values are clamped to what the sender accepts, so a bad Inspector value never makes it exit before the camera opens.
        /// </summary>
        public static string Cutout(VisionSettings settings, string resolvedSource) {
            StringBuilder sb = new StringBuilder();
            AppendCommon(sb, settings, resolvedSource, settings.CutoutPort, settings.CutoutFlipInSender, settings.CutoutTemporal, false);
            AppendCutoutOptions(sb, settings);
            return sb.ToString();
        }

        private static void AppendCutoutOptions(StringBuilder sb, VisionSettings settings) {
            sb.Append(" --roi ").Append(RoiName(settings.CutoutRoi));
            sb.Append(" --det-conf ").Append(Number(Mathf.Clamp(settings.CutoutDetectorConfidence, 0.1f, 0.9f)));
            sb.Append(" --max-people ").Append(Mathf.Clamp(settings.CutoutMaxPeople, 1, 8));
            sb.Append(" --mask-long-side ").Append(MaskLongSide(settings.CutoutMaskLongSide));
            // The edge ramps from Low to High, so High must stay above Low: a slider dragged past it would hand the sender a zero-width ramp.
            float low = Mathf.Clamp(settings.CutoutEdgeLow, 0.05f, 0.9f);
            float high = Mathf.Clamp(Mathf.Max(settings.CutoutEdgeHigh, low + 0.05f), 0.1f, 0.95f);
            sb.Append(" --edge-low ").Append(Number(low));
            sb.Append(" --edge-high ").Append(Number(high));
            sb.Append(settings.CutoutGuided ? " --guided" : " --no-guided");
            sb.Append(settings.CutoutCleanup ? " --cleanup" : " --no-cleanup");
            sb.Append(settings.CutoutHighQuality ? " --tta" : " --no-tta");
            sb.Append(settings.CutoutWholePictureFallback ? " --fallback" : " --no-fallback");
            sb.Append(settings.CutoutIsolatePeople ? " --isolate" : " --no-isolate");
            sb.Append(" --color ").Append(ColorName(settings.CutoutColor));
            if (settings.CutoutColor == CutoutColorMode.Jpeg) {
                sb.Append(" --jpeg-quality ").Append(Mathf.Clamp(settings.CutoutJpegQuality, 30, 100));
            }
            sb.Append(" --mask-enc ").Append(MaskEncodingName(settings.CutoutMaskEncoding));
            sb.Append(" --send-long-side ").Append(SendLongSide(settings.CutoutSendLongSide));
            sb.Append(" --chunk-bytes ").Append(Mathf.Clamp(settings.CutoutChunkBytes, 256, 65000));
        }

        /// <summary>
        /// The command line of ONE engine process that reads the camera once and runs several streams (<c>viitorx-tracking run</c>): the source options once,
        /// then <c>--stream NAME</c> and that stream's own options, in the order hands, face, cutout. Each stream gets exactly the options its single-stream line
        /// has (the mirror is per stream: hands and face mirror, the cutout does not), so a stream behaves the same alone or in the group.
        /// The shared camera is the reason this exists: a webcam or a LAN viewer cannot be opened by two processes.
        /// </summary>
        public static string Run(VisionSettings settings, string resolvedSource, bool hands, bool face, bool cutout) {
            StringBuilder sb = new StringBuilder();
            sb.Append("--video ").Append(Quote(resolvedSource));
            int rotation = NormalizedRotation(settings.RotateDegrees);
            if (rotation != 0) {
                sb.Append(" --rotate ").Append(rotation);
            }
            Vector2Int size = settings.CameraSize;
            if (size.x >= 160 && size.y >= 120) {
                sb.Append(" --camera-size ").Append(size.x).Append(' ').Append(size.y);
            }
            sb.Append(" --camera-fps ").Append(Mathf.Clamp(settings.CameraFps, 5, 60));
            sb.Append(settings.LoopFile ? " --loop" : " --no-loop");
            if (settings.ShowPreviewWindow) {
                sb.Append(" --show");
            }
            if (hands) {
                sb.Append(" --stream hands");
                AppendStreamCommon(sb, settings, settings.HandsPort, settings.Flip, settings.Smoothing, true);
                AppendHandsOptions(sb, settings);
            }
            if (face) {
                sb.Append(" --stream face");
                AppendStreamCommon(sb, settings, settings.FacePort, settings.Flip, settings.Smoothing, true);
                AppendFaceOptions(sb, settings);
            }
            if (cutout) {
                sb.Append(" --stream cutout");
                AppendStreamCommon(sb, settings, settings.CutoutPort, settings.CutoutFlipInSender, settings.CutoutTemporal, false);
                AppendCutoutOptions(sb, settings);
            }
            return sb.ToString();
        }

        private static void AppendStreamCommon(StringBuilder sb, VisionSettings settings, int port, bool flip, bool smooth, bool includeFieldOfView) {
            sb.Append(" --host ").Append(LoopbackHost);
            sb.Append(" --port ").Append(port);
            sb.Append(" --provider ").Append(ProviderName(settings.Provider));
            if (includeFieldOfView) {
                sb.Append(" --hfov ").Append(Number(Mathf.Clamp(settings.HorizontalFovDegrees, 20f, 140f)));
                if (settings.FovAcrossLongSide) {
                    sb.Append(" --hfov-axis long");
                }
            }
            sb.Append(flip ? " --flip" : " --no-flip");
            sb.Append(smooth ? " --smooth" : " --no-smooth");
        }

        public static string RoiName(CutoutRoiMode mode) {
            if (mode == CutoutRoiMode.On) {
                return "on";
            }
            if (mode == CutoutRoiMode.Off) {
                return "off";
            }
            return "auto";
        }

        public static string ColorName(CutoutColorMode mode) {
            if (mode == CutoutColorMode.Jpeg) {
                return "jpeg";
            }
            if (mode == CutoutColorMode.None) {
                return "none";
            }
            return "rgba";
        }

        public static string MaskEncodingName(CutoutMaskMode mode) {
            return mode == CutoutMaskMode.Deflate ? "deflate" : "raw";
        }

        /// <summary>0 (the picture's own size) or 160..1280: below 160 the mask has too few pixels to outline anything.</summary>
        public static int MaskLongSide(int pixels) {
            if (pixels <= 0) {
                return 0;
            }
            return Mathf.Clamp(pixels, 160, 1280);
        }

        /// <summary>0 (send the picture as it is) or 160..3840.</summary>
        public static int SendLongSide(int pixels) {
            if (pixels <= 0) {
                return 0;
            }
            return Mathf.Clamp(pixels, 160, 3840);
        }

        public static string SidesName(HandSidesMode mode) {
            if (mode == HandSidesMode.Model) {
                return "model";
            }
            if (mode == HandSidesMode.Swapped) {
                return "swapped";
            }
            return "auto";
        }

        public static string LandmarksName(FaceLandmarkMode mode) {
            if (mode == FaceLandmarkMode.Mesh) {
                return "mesh";
            }
            if (mode == FaceLandmarkMode.None) {
                return "none";
            }
            return "full";
        }

        public static string ProviderName(VisionProvider provider) {
            if (provider == VisionProvider.Dml) {
                return "dml";
            }
            if (provider == VisionProvider.Cpu) {
                return "cpu";
            }
            return "auto";
        }

        /// <summary>0, 90, 180 or 270; anything else is read as 0 rather than forwarded (the sender would refuse it and exit).</summary>
        public static int NormalizedRotation(int degrees) {
            if (degrees == 90 || degrees == 180 || degrees == 270) {
                return degrees;
            }
            return 0;
        }

        /// <param name="includeFieldOfView">False for the cutout sender, which measures nothing in metres and so needs no field of view.</param>
        private static void AppendCommon(StringBuilder sb, VisionSettings settings, string resolvedSource, int port, bool flip, bool smooth,
            bool includeFieldOfView) {
            sb.Append("--video ").Append(Quote(resolvedSource));
            sb.Append(" --host ").Append(LoopbackHost);
            sb.Append(" --port ").Append(port);
            sb.Append(" --provider ").Append(ProviderName(settings.Provider));
            if (includeFieldOfView) {
                sb.Append(" --hfov ").Append(Number(Mathf.Clamp(settings.HorizontalFovDegrees, 20f, 140f)));
                if (settings.FovAcrossLongSide) {
                    sb.Append(" --hfov-axis long");
                }
            }
            int rotation = NormalizedRotation(settings.RotateDegrees);
            if (rotation != 0) {
                sb.Append(" --rotate ").Append(rotation);
            }
            sb.Append(flip ? " --flip" : " --no-flip");
            sb.Append(smooth ? " --smooth" : " --no-smooth");
            Vector2Int size = settings.CameraSize;
            if (size.x >= 160 && size.y >= 120) {
                sb.Append(" --camera-size ").Append(size.x).Append(' ').Append(size.y);
            }
            sb.Append(" --camera-fps ").Append(Mathf.Clamp(settings.CameraFps, 5, 60));
            sb.Append(settings.LoopFile ? " --loop" : " --no-loop");
            if (settings.ShowPreviewWindow) {
                sb.Append(" --show");
            }
        }

        /// <summary>Double quotes around a value, with any quote inside it dropped: a source never legitimately contains one.</summary>
        private static string Quote(string value) {
            string safe = string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", string.Empty);
            return "\"" + safe + "\"";
        }

        private static string Number(float value) {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
