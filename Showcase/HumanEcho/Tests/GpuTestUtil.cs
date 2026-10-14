using System;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// Helpers for tests that drive the GPU stages with synthetic textures. The textures are TOP-row-first
    /// like the wire's, because that is what
    /// <see cref="PersonField"/> and the cutout stream agree on: row 0 of the data is the top of the picture.
    /// </summary>
    internal static class GpuTestUtil {
        /// <summary>A one-channel mask texture; <paramref name="value"/> is called with (x, row counted from
        /// the TOP) and returns 0..1.</summary>
        public static Texture2D MaskTexture(int width, int height, Func<int, int, float> value) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.R8, false, true);
            byte[] data = new byte[width * height];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    data[y * width + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(value(x, y)) * 255f);
                }
            }
            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>A flat colour picture (RGBA32).</summary>
        public static Texture2D ColorTexture(int width, int height, Color32 color) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>
        /// A soft-edged axis-aligned box as a mask value: 1 inside, 0 outside, a ramp of <paramref
        /// name="soft"/> pixels at the edge (real masks
        /// have a few pixels of ramp, and the motion estimate depends on it). Rows count from the top.
        /// </summary>
        public static float Box(int x, int y, float left, float top, float right, float bottom, float soft) {
            float dx = Mathf.Min(x + 0.5f - left, right - (x + 0.5f));
            float dy = Mathf.Min(y + 0.5f - top, bottom - (y + 0.5f));
            float d = Mathf.Min(dx, dy);
            return Mathf.Clamp01(0.5f + d / soft);
        }

        /// <summary>Reads a whole render texture back as floats. Row 0 of the result is the BOTTOM row
        /// (Unity's texture convention).</summary>
        public static Color[] Read(RenderTexture texture) {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            Color[] pixels = readback.GetPixels();
            UnityEngine.Object.DestroyImmediate(readback);
            return pixels;
        }

        public static RenderTexture MakeAtlas(int width, int height, Func<float, float, Vector2> velocity) {
            Texture2D source = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Vector2 v = velocity((x + 0.5f) / width, (y + 0.5f) / height);
                    pixels[y * width + x] = new Color(v.x, v.y, 0f, 0f);
                }
            }
            source.SetPixels(pixels);
            source.Apply(false, false);
            source.filterMode = FilterMode.Bilinear;
            RenderTexture target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Bilinear;
            target.wrapMode = TextureWrapMode.Clamp;
            target.Create();
            Graphics.Blit(source, target);
            UnityEngine.Object.DestroyImmediate(source);
            return target;
        }

        public static void Destroy(UnityEngine.Object value) {
            if (value != null) {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
