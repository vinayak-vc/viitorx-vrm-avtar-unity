using System;

using UnityEngine;

using VirtualMirror.Vision;

namespace HumanEcho {
    /// <summary>
    /// The PERSON FIELD: the one GPU stage every Human Echo layer reads. It takes the two textures of the
    /// cutout stream (the sidecar's picture and
    /// mask, top row first) and keeps two render textures in Unity's usual orientation (v = 1 is the top):
    ///
    /// <see cref="Field"/>   rgb = the camera picture, a = the person mask, mirrored when asked. Same size as
    /// the mask.
    /// <see cref="Motion"/>  rg = how the silhouette moves, in PICTURE HEIGHTS PER SECOND (x right, y up, the
    /// same unit on both axes; a
    /// screen height only when the picture fills the screen's height, so a consumer that works in screen
    /// heights
    ///                         multiplies by min(1, viewAspect / pictureAspect));
    /// b = confidence 0..1 (0 where nothing moved). Half float, linear, a quarter of the field's size: it is
    /// a smooth
    ///                         field that is sampled with bilinear filtering.
    ///
    /// The motion is "normal flow" of the mask edge between the previous and the current mask, computed on a
    /// small, blurred copy of the mask
    /// (the gradient method only holds while an edge moves less than its own width) and spread over the body
    /// by a blur. It is an estimate of how
    /// the SILHOUETTE moved, not a measurement of limbs, and it is only as good as the mask edge. Nothing
    /// here segments or reads back: it is a
    /// dozen small full-screen blits, once per new mask frame.
    ///
    /// Main thread only. Dispose it when done: it owns its render textures and its material.
    /// </summary>
    public sealed class PersonField : IDisposable {
        private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
        private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        private static readonly int FieldTexId = Shader.PropertyToID("_FieldTex");
        private static readonly int NowTexId = Shader.PropertyToID("_NowTex");
        private static readonly int PrevMaskTexId = Shader.PropertyToID("_PrevMaskTex");
        private static readonly int SourceTexelId = Shader.PropertyToID("_SourceTexel");
        private static readonly int FieldTexelId = Shader.PropertyToID("_FieldTexel");
        private static readonly int MirrorId = Shader.PropertyToID("_Mirror");
        private static readonly int NoiseFloorId = Shader.PropertyToID("_NoiseFloor");
        private static readonly int GradEpsilonId = Shader.PropertyToID("_GradEpsilon");
        private static readonly int MaxPixelsId = Shader.PropertyToID("_MaxPixelsPerFrame");
        private static readonly int BlurAxisId = Shader.PropertyToID("_BlurAxis");
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int FlowFpsId = Shader.PropertyToID("_FlowFps");
        private static readonly int ConfidenceGainId = Shader.PropertyToID("_ConfidenceGain");

        private const int PassField = 0;
        private const int PassDown = 1;
        private const int PassBlur = 2;
        private const int PassFlowRaw = 3;
        private const int PassFlowFinal = 4;
        private const int PassCopyMask = 5;

        /// <summary>The flow is computed at 1 / this of the field's size.</summary>
        public const int FlowDownscale = 4;

        private const int MinFlowSize = 16;
        private const float MinInterval = 1f / 60f;
        private const float MaxInterval = 0.2f;
        private const float IntervalSmoothing = 0.2f;

        private readonly Material material;

        private RenderTexture field;
        private RenderTexture maskSmall;
        private RenderTexture maskTmp;
        private RenderTexture previousMask;
        private RenderTexture flowRaw;
        private RenderTexture flowBlur;
        private RenderTexture motion;
        private int lastVersion = -1;
        private bool hasPrevious;
        private float lastUpdateTime = -1f;
        private float frameInterval = 1f / 30f;
        private int updates;
        private bool disposed;

        /// <summary>Change of the blurred small mask below this between two frames is flicker, not motion
        /// (mask units 0..1).</summary>
        public float NoiseFloor = 0.02f;

        /// <summary>Keeps the normal-flow division finite where the mask is flat.</summary>
        public float GradientEpsilon = 0.005f;

        /// <summary>The longest flow one pixel may report, in flow pixels (a quarter of the field's) per
        /// frame.</summary>
        public float MaxPixelsPerFrame = 6f;

        /// <summary>Blur radius of the small mask before the flow is taken, flow pixels (at most 12): the
        /// edge must be wider than the movement.</summary>
        public float PreBlurRadius = 2f;

        /// <summary>Blur radius of the flow, flow pixels (at most 12): how far an edge's velocity spreads
        /// into the body.</summary>
        public float SpreadRadius = 4f;

        /// <summary>How quickly confidence rises with the amount of evidence: confidence = saturate(weight *
        /// gain).</summary>
        public float ConfidenceGain = 2f;

        public PersonField(Shader shader) {
            if (shader == null) {
                throw new ArgumentNullException("shader");
            }
            material = new Material(shader);
            material.name = "HumanEcho PersonField (runtime)";
            material.hideFlags = HideFlags.HideAndDontSave;
        }

        /// <summary>Picture (rgb) and person mask (a), upright. Null before the first frame.</summary>
        public RenderTexture Field {
            get {
                return field;
            }
        }

        /// <summary>Silhouette motion, picture heights per second in rg, confidence in b. Null before the
        /// first frame.</summary>
        public RenderTexture Motion {
            get {
                return motion;
            }
        }

        public int Width {
            get {
                return field != null ? field.width : 0;
            }
        }

        public int Height {
            get {
                return field != null ? field.height : 0;
            }
        }

        /// <summary>Width over height of the field; 1 before the first frame.</summary>
        public float Aspect {
            get {
                return field != null ? (float)field.width / field.height : 1f;
            }
        }

        /// <summary>Counts the mask frames processed.</summary>
        public int Updates {
            get {
                return updates;
            }
        }

        /// <summary>Smoothed seconds between two mask frames (what the flow is scaled by).</summary>
        public float FrameInterval {
            get {
                return frameInterval;
            }
        }

        /// <summary>
        /// Processes the newest cutout frame if there is one this call has not seen. Returns true when the
        /// field and the motion were rebuilt.
        /// <paramref name="mirror"/> flips the picture and mask left to right; <paramref name="now"/> is a
        /// monotonic time in seconds.
        /// </summary>
        public bool Update(CutoutTextures source, bool mirror, float now) {
            if (disposed || source == null || !source.HasFrame || source.MaskTexture == null) {
                return false;
            }
            if (source.Version == lastVersion) {
                return false;
            }
            lastVersion = source.Version;
            return Process(source.ColorTexture, source.MaskTexture, source.MaskWidth, source.MaskHeight, mirror, now);
        }

        /// <summary>
        /// The texture-level core of <see cref="Update"/>: <paramref name="color"/> and <paramref
        /// name="mask"/> are TOP-row-first like the wire's
        /// textures, <paramref name="width"/> x <paramref name="height"/> is the mask's size. Separate so
        /// tests can drive it with synthetic textures.
        /// </summary>
        internal bool Process(Texture color, Texture mask, int width, int height, bool mirror, float now) {
            if (disposed || color == null || mask == null || width <= 0 || height <= 0) {
                return false;
            }
            EnsureTargets(width, height);

            if (lastUpdateTime >= 0f) {
                float interval = Mathf.Clamp(now - lastUpdateTime, MinInterval, MaxInterval);
                frameInterval = frameInterval + (interval - frameInterval) * IntervalSmoothing;
            }
            lastUpdateTime = now;

            material.SetTexture(ColorTexId, color);
            material.SetTexture(MaskTexId, mask);
            material.SetFloat(MirrorId, mirror ? 1f : 0f);
            Graphics.Blit(null, field, material, PassField);

            // The small, blurred mask the flow is taken from.
            material.SetTexture(FieldTexId, field);
            material.SetVector(SourceTexelId, new Vector4(1f / field.width, 1f / field.height, field.width, field.height));
            material.SetVector(FieldTexelId, new Vector4(1f / maskSmall.width, 1f / maskSmall.height, maskSmall.width, maskSmall.height));
            Graphics.Blit(null, maskSmall, material, PassDown);
            Blur(maskSmall, maskTmp, maskSmall, PreBlurRadius);

            material.SetTexture(NowTexId, maskSmall);
            material.SetTexture(PrevMaskTexId, previousMask);
            material.SetFloat(NoiseFloorId, NoiseFloor);
            material.SetFloat(GradEpsilonId, GradientEpsilon);
            material.SetFloat(MaxPixelsId, MaxPixelsPerFrame);
            material.SetFloat(FlowFpsId, 1f / frameInterval);
            material.SetFloat(ConfidenceGainId, ConfidenceGain);

            if (!hasPrevious) {
                // The first frame has nothing to differ from: copy it so the first flow is zero, not
                // "everything appeared".
                Graphics.Blit(null, previousMask, material, PassCopyMask);
                hasPrevious = true;
            }

            Graphics.Blit(null, flowRaw, material, PassFlowRaw);
            Blur(flowRaw, flowBlur, flowRaw, SpreadRadius);
            Graphics.Blit(flowRaw, motion, material, PassFlowFinal);
            Graphics.Blit(null, previousMask, material, PassCopyMask);

            updates = updates + 1;
            return true;
        }

        /// <summary>Forgets the previous mask and the pace, as when the stream restarts or the picture is
        /// mirrored the other way.</summary>
        public void Reset() {
            lastVersion = -1;
            hasPrevious = false;
            lastUpdateTime = -1f;
            frameInterval = 1f / 30f;
        }

        /// <summary>
        /// Empties the field: no person (alpha 0), no motion, no previous mask. The controller calls it when
        /// the stream stalls or ends, so the
        /// layers, which keep ticking, stop injecting a frozen silhouette and let what they hold drain away.
        /// Also <see cref="Reset"/>s, so the
        /// first frame after the stream returns has no false motion.
        /// </summary>
        public void Blank() {
            ClearTarget(field);
            ClearTarget(maskSmall);
            ClearTarget(maskTmp);
            ClearTarget(previousMask);
            ClearTarget(flowRaw);
            ClearTarget(flowBlur);
            ClearTarget(motion);
            Reset();
        }

        private static void ClearTarget(RenderTexture texture) {
            if (texture == null) {
                return;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            ReleaseTargets();
            DestroyObject(material);
        }

        // Separable box blur of source through scratch into destination (destination may be the source).
        private void Blur(RenderTexture source, RenderTexture scratch, RenderTexture destination, float radius) {
            material.SetFloat(BlurRadiusId, Mathf.Clamp(radius, 0f, 12f));
            material.SetVector(BlurAxisId, new Vector4(1f, 0f, 0f, 0f));
            Graphics.Blit(source, scratch, material, PassBlur);
            material.SetVector(BlurAxisId, new Vector4(0f, 1f, 0f, 0f));
            Graphics.Blit(scratch, destination, material, PassBlur);
        }

        private void EnsureTargets(int width, int height) {
            if (field != null && field.width == width && field.height == height) {
                return;
            }
            ReleaseTargets();
            int smallWidth = Mathf.Max(MinFlowSize, width / FlowDownscale);
            int smallHeight = Mathf.Max(MinFlowSize, height / FlowDownscale);
            field = Create("HumanEcho Field", width, height, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            maskSmall = Create("HumanEcho Mask Small", smallWidth, smallHeight, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            maskTmp = Create("HumanEcho Mask Scratch", smallWidth, smallHeight, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            previousMask = Create("HumanEcho Previous Mask", smallWidth, smallHeight, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            flowRaw = Create("HumanEcho Flow Raw", smallWidth, smallHeight, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            flowBlur = Create("HumanEcho Flow Blur", smallWidth, smallHeight, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            motion = Create("HumanEcho Motion", smallWidth, smallHeight, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            hasPrevious = false;
        }

        private void ReleaseTargets() {
            Release(ref field);
            Release(ref maskSmall);
            Release(ref maskTmp);
            Release(ref previousMask);
            Release(ref flowRaw);
            Release(ref flowBlur);
            Release(ref motion);
        }

        private static RenderTexture Create(string name, int width, int height, RenderTextureFormat format, RenderTextureReadWrite readWrite) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, readWrite);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Create();
            return texture;
        }

        private static void Release(ref RenderTexture texture) {
            if (texture == null) {
                return;
            }
            // Graphics.Blit leaves its destination as the active target; releasing an active target logs a
            // warning.
            if (RenderTexture.active == texture) {
                RenderTexture.active = null;
            }
            texture.Release();
            DestroyObject(texture);
            texture = null;
        }

        private static void DestroyObject(UnityEngine.Object target) {
            if (target == null) {
                return;
            }
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(target);
            } else {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
