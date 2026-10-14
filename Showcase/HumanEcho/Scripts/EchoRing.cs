using System;

using UnityEngine;
using UnityEngine.Rendering;

namespace HumanEcho {
    /// <summary>
    /// The GPU ring of past person masks: ONE Texture2DArray render texture (R8, the field's size, one slice
    /// per capture) plus the
    /// <see cref="EchoSchedule"/> that decides when a slice is overwritten. One texture, not seventeen, so
    /// the echo shader reads every
    /// copy through a single binding and the copies are drawn in one full-screen pass.
    ///
    /// A capture is a single small blit of the field's alpha into one slice: a reused <see
    /// cref="CommandBuffer"/> binds slice N of the
    /// array as the render target (<c>SetRenderTarget(texture, 0, CubemapFace.Unknown, N)</c>) and blits the
    /// field into it with the echo
    /// shader's "Capture" pass (pass 0). A command buffer is used because the Graphics.Blit overloads with a
    /// destination slice either
    /// take no material or no destination texture in Unity 6000.3. The echo layer and the outline layer both
    /// share one ring:
    /// whoever ticks first in a frame advances it, the other call sees the mask frame is already handled and
    /// does nothing, so the
    /// order of the layers does not matter and a ring is never captured twice for one mask frame.
    ///
    /// Main thread only. Dispose it: it owns its render texture and its material.
    /// </summary>
    public sealed class EchoRing : IDisposable {
        private const int PassCapture = 0;
        private const float DefaultSpacingSeconds = 0.14f;
        private const int MaxSlices = 2048;

        private readonly EchoSchedule schedule;
        private readonly Material material;
        private readonly CommandBuffer capture;
        private RenderTexture texture;
        private int lastSeenUpdates = -1;
        private bool disposed;

        public EchoRing(Shader shader, int capacity) {
            if (shader == null) {
                throw new ArgumentNullException("shader");
            }
            schedule = new EchoSchedule(Mathf.Clamp(capacity, 2, MaxSlices));
            material = LayerResources.CreateMaterial(shader, "HumanEcho_EchoRing (runtime)");
            capture = new CommandBuffer();
            capture.name = "HumanEcho Echo Capture";
            SpacingSeconds = DefaultSpacingSeconds;
        }

        /// <summary>
        /// Seconds between two captures. The echo layer writes it every frame from its settings (even while
        /// it is switched off), so a
        /// layer that only borrows the ring, like the delayed outline, captures at the same pace.
        /// </summary>
        public float SpacingSeconds { get; set; }

        /// <summary>The Texture2DArray (R8, one slice per capture); null until the first <see
        /// cref="Advance"/> with a field.</summary>
        public RenderTexture Texture {
            get {
                return texture;
            }
        }

        public int Capacity {
            get {
                return schedule.Capacity;
            }
        }

        /// <summary>How many captures exist: ages 0 .. ValidCount - 1 can be read.</summary>
        public int ValidCount {
            get {
                return schedule.ValidCount;
            }
        }

        /// <summary>Slice of the newest capture (age 0); -1 before the first one.</summary>
        public int Head {
            get {
                return schedule.Head;
            }
        }

        public int Width {
            get {
                return texture != null ? texture.width : 0;
            }
        }

        public int Height {
            get {
                return texture != null ? texture.height : 0;
            }
        }

        public bool IsValidAge(int age) {
            return schedule.IsValidAge(age);
        }

        /// <summary>The slice that holds the capture of <paramref name="age"/> (0 = newest), or -1 when it
        /// does not exist yet.</summary>
        public int SlotForAge(int age) {
            return schedule.SlotForAge(age);
        }

        /// <summary>
        /// Takes the person field's mask into the ring when the schedule says so. <paramref name="now"/> is a
        /// monotonic time in seconds
        /// (the frame's scene time). Returns true when a slice was written. Safe to call from several layers
        /// in one frame.
        /// </summary>
        public bool Advance(PersonField field, float now) {
            if (disposed || field == null || field.Field == null) {
                return false;
            }
            RenderTexture source = field.Field;
            EnsureTexture(source.width, source.height);
            schedule.ResetIfStale(now, SpacingSeconds);

            // "A new mask arrived" is read from the field's own frame counter, not from a per-frame flag:
            // that stays true for
            // exactly one call per mask even if the ring was not advanced on the frame the mask arrived.
            bool newMask = field.Updates != lastSeenUpdates;
            lastSeenUpdates = field.Updates;
            if (!schedule.ShouldCapture(newMask, now, SpacingSeconds, field.FrameInterval)) {
                return false;
            }
            int slot = schedule.Capture(now);
            capture.Clear();
            capture.SetRenderTarget(texture, 0, CubemapFace.Unknown, slot);
            capture.Blit(source, BuiltinRenderTextureType.CurrentActive, material, PassCapture);
            Graphics.ExecuteCommandBuffer(capture);
            return true;
        }

        /// <summary>Forgets every capture; the next mask frame is captured at once.</summary>
        public void Reset() {
            schedule.Reset();
            lastSeenUpdates = -1;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            capture.Release();
            LayerResources.Release(ref texture);
            LayerResources.DestroyObject(material);
        }

        // The ring is as large as the field, so a new field size (a new source) means new masks that do not
        // fit the old slices.
        private void EnsureTexture(int width, int height) {
            if (texture != null && texture.width == width && texture.height == height) {
                return;
            }
            LayerResources.Release(ref texture);
            RenderTexture created = new RenderTexture(width, height, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            created.name = "HumanEcho Echo Ring";
            created.dimension = TextureDimension.Tex2DArray;
            created.volumeDepth = schedule.Capacity;
            created.useMipMap = false;
            created.filterMode = FilterMode.Bilinear;
            created.wrapMode = TextureWrapMode.Clamp;
            created.Create();
            texture = created;
            Reset();
        }
    }
}
