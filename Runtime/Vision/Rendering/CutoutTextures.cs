using System;
using System.Diagnostics;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// The camera picture and the person mask of the newest cutout frame, as two GPU textures any system can read. This is the REUSABLE half of the
    /// cutout feature: <see cref="CutoutView"/> draws it, and anything else that wants "the person and where they are" (a particle emitter, a
    /// VFX Graph, an echo trail, a transition) samples the same two textures and never segments anything itself.
    ///
    /// ORIENTATION, the same for both textures whatever the picture's encoding: row 0 is the TOP of the picture, because the wire sends the top row
    /// first. Sample at (u, 1 - v), or give a material <see cref="UvScale"/> / <see cref="UvOffset"/> as its tiling and offset. The mask covers
    /// the whole picture even when it is smaller than it (the sender keeps both at the same aspect): the same uv addresses both. A JPEG picture,
    /// which Unity decodes upright, is flipped on the GPU into a render texture so that this holds for it too.
    ///
    /// <see cref="MaskTexture"/> is one channel (R8): 0 background, 1 person, soft in between. <see cref="ColorTexture"/> is the picture (sRGB),
    /// or a 1x1 white texture when the stream carries no colour (<see cref="HasColor"/> false). They are created on first use and recreated only
    /// when the stream's size changes; <see cref="Generation"/> counts the recreations so a material knows when to bind again.
    ///
    /// MAIN THREAD ONLY. Upload costs two <c>SetPixelData</c> + <c>Apply</c> for a raw picture and a raw mask (no allocation), or a JPEG decode
    /// plus a blit for a JPEG picture (the network option; Unity decodes on the main thread).
    /// </summary>
    public sealed class CutoutTextures : IDisposable {
        public const string ColorTextureName = "Cutout Camera Picture";
        public const string MaskTextureName = "Cutout Person Mask";

        /// <summary>Tiling for a material that samples these textures with plain (u, v): flips v, because the rows run top to bottom.</summary>
        public static readonly Vector2 UvScale = new Vector2(1f, -1f);

        /// <summary>Offset to go with <see cref="UvScale"/>.</summary>
        public static readonly Vector2 UvOffset = new Vector2(0f, 1f);

        private const float CostSmoothing = 0.1f;

        private Texture2D rawColor;
        private Texture2D decoded;
        private RenderTexture flipped;
        private Texture2D mask;
        private Texture2D white;
        private bool disposed;
        private int generation;
        private int version;
        private long uploads;
        private long decodeFailures;
        private float uploadMilliseconds;
        private CutoutColorEncoding colorEncoding;
        private int colorWidth;
        private int colorHeight;
        private int maskWidth;
        private int maskHeight;
        private bool hasColor;
        private bool mirrored;
        private bool live;
        private bool nobody;
        private int people;
        private int modelId;
        private float foregroundFraction;
        private float senderLatencyMs;
        private float inferMs;
        private double transportMilliseconds;
        private uint sequence;

        /// <summary>The camera picture (or a 1x1 white texture when the stream has no colour). Null before the first frame.</summary>
        public Texture ColorTexture {
            get {
                if (!hasColor) {
                    return white;
                }
                return colorEncoding == CutoutColorEncoding.Jpeg ? (Texture)flipped : rawColor;
            }
        }

        /// <summary>The person mask, one channel, 0..1. Null before the first frame.</summary>
        public Texture MaskTexture {
            get {
                return mask;
            }
        }

        /// <summary>True once a frame has been uploaded.</summary>
        public bool HasFrame {
            get {
                return uploads > 0;
            }
        }

        /// <summary>True when the stream carries the picture; false for a mask-only stream (the sender's <c>--color none</c>).</summary>
        public bool HasColor {
            get {
                return hasColor;
            }
        }

        /// <summary>Counts every successful upload; a consumer redraws when it changes.</summary>
        public int Version {
            get {
                return version;
            }
        }

        /// <summary>Counts the times a texture was created or recreated; bind them to a material again when it changes.</summary>
        public int Generation {
            get {
                return generation;
            }
        }

        public long Uploads {
            get {
                return uploads;
            }
        }

        /// <summary>JPEG pictures Unity could not decode (the previous picture stays on screen).</summary>
        public long DecodeFailures {
            get {
                return decodeFailures;
            }
        }

        /// <summary>Smoothed milliseconds one <see cref="Upload"/> takes on the main thread.</summary>
        public float UploadMilliseconds {
            get {
                return uploadMilliseconds;
            }
        }

        public CutoutColorEncoding ColorEncoding {
            get {
                return colorEncoding;
            }
        }

        public int ColorWidth {
            get {
                return colorWidth;
            }
        }

        public int ColorHeight {
            get {
                return colorHeight;
            }
        }

        public int MaskWidth {
            get {
                return maskWidth;
            }
        }

        public int MaskHeight {
            get {
                return maskHeight;
            }
        }

        /// <summary>Width over height of the picture (of the mask when there is no picture); 1 before the first frame.</summary>
        public float Aspect {
            get {
                if (hasColor && colorWidth > 0 && colorHeight > 0) {
                    return (float)colorWidth / colorHeight;
                }
                if (maskWidth > 0 && maskHeight > 0) {
                    return (float)maskWidth / maskHeight;
                }
                return 1f;
            }
        }

        /// <summary>True when the sender flipped the picture left to right before it segmented.</summary>
        public bool Mirrored {
            get {
                return mirrored;
            }
        }

        /// <summary>True for a camera or a stream, false for a video file.</summary>
        public bool Live {
            get {
                return live;
            }
        }

        /// <summary>True when the sender found nobody in this picture.</summary>
        public bool Nobody {
            get {
                return nobody;
            }
        }

        public int People {
            get {
                return people;
            }
        }

        public int ModelId {
            get {
                return modelId;
            }
        }

        /// <summary>Foreground area as a fraction of the picture, 0..1.</summary>
        public float ForegroundFraction {
            get {
                return foregroundFraction;
            }
        }

        /// <summary>The sender's milliseconds from reading the camera frame to packing this one.</summary>
        public float SenderLatencyMs {
            get {
                return senderLatencyMs;
            }
        }

        /// <summary>The sender's milliseconds of segmentation work for this frame.</summary>
        public float InferMs {
            get {
                return inferMs;
            }
        }

        /// <summary>Milliseconds from the sender packing the frame to the last datagram arriving. Meaningful only when sender and Unity share a machine (one clock).</summary>
        public double TransportMilliseconds {
            get {
                return transportMilliseconds;
            }
        }

        public uint Sequence {
            get {
                return sequence;
            }
        }

        /// <summary>Uploads <paramref name="frame"/>. False (and nothing changes) when the frame holds no mask or its picture cannot be decoded.</summary>
        public bool Upload(CutoutFrame frame) {
            if (disposed || frame == null || !frame.HasMask) {
                return false;
            }
            long started = Stopwatch.GetTimestamp();
            if (!UploadColor(frame)) {
                return false;
            }
            UploadMask(frame);
            colorEncoding = frame.ColorEncoding;
            maskWidth = frame.MaskWidth;
            maskHeight = frame.MaskHeight;
            mirrored = frame.Mirrored;
            live = frame.Live;
            nobody = frame.Nobody;
            people = frame.People;
            modelId = frame.ModelId;
            foregroundFraction = frame.ForegroundFraction;
            senderLatencyMs = frame.SenderLatencyMs;
            inferMs = frame.InferMs;
            transportMilliseconds = (frame.ReceivedUnixSeconds - frame.SentUnixSeconds) * 1000.0;
            sequence = frame.Sequence;
            uploads = uploads + 1;
            version = version + 1;
            float elapsed = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            uploadMilliseconds = uploads == 1 ? elapsed : uploadMilliseconds + (elapsed - uploadMilliseconds) * CostSmoothing;
            return true;
        }

        /// <summary>Drops the textures, as when the stream ends; the next frame creates them again.</summary>
        public void Clear() {
            DestroyTexture(ref rawColor);
            DestroyTexture(ref decoded);
            DestroyTarget();
            DestroyTexture(ref mask);
            hasColor = false;
            uploads = 0;
            colorWidth = 0;
            colorHeight = 0;
            maskWidth = 0;
            maskHeight = 0;
            generation = generation + 1;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            DestroyTexture(ref rawColor);
            DestroyTexture(ref decoded);
            DestroyTarget();
            DestroyTexture(ref mask);
            DestroyTexture(ref white);
        }

        private bool UploadColor(CutoutFrame frame) {
            if (!frame.HasColor) {
                hasColor = false;
                colorWidth = 0;
                colorHeight = 0;
                EnsureWhite();
                return true;
            }
            if (frame.ColorEncoding == CutoutColorEncoding.Rgba) {
                EnsureTexture(ref rawColor, frame.ColorWidth, frame.ColorHeight, TextureFormat.RGBA32, false, ColorTextureName);
                rawColor.SetPixelData(frame.Payload, 0, frame.ColorOffset);
                rawColor.Apply(false, false);
                colorWidth = frame.ColorWidth;
                colorHeight = frame.ColorHeight;
            } else {
                if (decoded == null) {
                    decoded = CreateTexture(2, 2, TextureFormat.RGBA32, false, "Cutout JPEG Decode");
                }
                ReadOnlySpan<byte> jpeg = new ReadOnlySpan<byte>(frame.Payload, frame.ColorOffset, frame.ColorLength);
                if (!ImageConversion.LoadImage(decoded, jpeg, false)) {
                    decodeFailures = decodeFailures + 1;
                    return false;
                }
                EnsureTarget(decoded.width, decoded.height);
                // Unity decodes a picture upright (its top row at v = 1); this class promises the top row first, like the raw path.
                Graphics.Blit(decoded, flipped, UvScale, UvOffset);
                colorWidth = decoded.width;
                colorHeight = decoded.height;
            }
            hasColor = true;
            colorEncoding = frame.ColorEncoding;
            return true;
        }

        private void UploadMask(CutoutFrame frame) {
            EnsureTexture(ref mask, frame.MaskWidth, frame.MaskHeight, TextureFormat.R8, true, MaskTextureName);
            mask.SetPixelData(frame.MaskArray, 0, frame.MaskOffset);
            mask.Apply(false, false);
        }

        private void EnsureTexture(ref Texture2D texture, int width, int height, TextureFormat format, bool linear, string name) {
            if (texture != null && texture.width == width && texture.height == height && texture.format == format) {
                return;
            }
            DestroyTexture(ref texture);
            texture = CreateTexture(width, height, format, linear, name);
            generation = generation + 1;
        }

        private void EnsureTarget(int width, int height) {
            if (flipped != null && flipped.width == width && flipped.height == height) {
                return;
            }
            DestroyTarget();
            flipped = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            flipped.name = ColorTextureName;
            flipped.filterMode = FilterMode.Bilinear;
            flipped.wrapMode = TextureWrapMode.Clamp;
            flipped.Create();
            generation = generation + 1;
        }

        private static Texture2D CreateTexture(int width, int height, TextureFormat format, bool linear, string name) {
            Texture2D texture = new Texture2D(width, height, format, false, linear);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private void EnsureWhite() {
            if (white != null) {
                return;
            }
            white = CreateTexture(1, 1, TextureFormat.RGBA32, false, "Cutout White");
            white.SetPixel(0, 0, Color.white);
            white.Apply(false, false);
            generation = generation + 1;
        }

        private void DestroyTarget() {
            if (flipped == null) {
                return;
            }
            flipped.Release();
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(flipped);
            } else {
                UnityEngine.Object.DestroyImmediate(flipped);
            }
            flipped = null;
        }

        private static void DestroyTexture(ref Texture2D texture) {
            if (texture == null) {
                return;
            }
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(texture);
            } else {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            texture = null;
        }
    }
}
