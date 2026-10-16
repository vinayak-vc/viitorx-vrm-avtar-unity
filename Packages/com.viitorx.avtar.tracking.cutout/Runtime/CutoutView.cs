using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Draws the person the sidecar cut out of its camera picture (<see cref="CutoutTextures"/>): the camera as it is, the person on a backdrop or
    /// on nothing, or the person as one colour, with an optional glowing outline. It is a presentation component and nothing else: no socket, no
    /// segmentation, no knowledge of the VRM avatar or of skeletal tracking, so it can sit in any scene beside any other system.
    ///
    /// It makes two quads in front of a camera at runtime: the BACKDROP (a full-view quad, drawn first) and the PERSON (the picture's own aspect,
    /// fitted into the view). Both follow the camera every frame, so the view works unchanged under a different camera, window size or aspect.
    /// <see cref="Look"/> is read every frame: switching mode, colour or backdrop is instant and touches neither the sender nor the camera.
    ///
    /// Give it textures with <see cref="Bind"/> (the vision host owns them: <c>AppBootstrap.Instance.Vision.CutoutTextures</c>). Until the first
    /// frame arrives, nothing is drawn.
    /// </summary>
    [AddComponentMenu("Virtual Mirror/Cutout View")]
    public sealed class CutoutView : MonoBehaviour {
        private const string PersonShaderName = "VirtualMirror/Cutout";
        private const string BackdropShaderName = "VirtualMirror/CutoutBackdrop";

        /// <summary>How far behind the person the backdrop sits, metres, so the two never fight for the same depth.</summary>
        private const float BackdropOffset = 0.02f;

        private static readonly int ColorTextureId = Shader.PropertyToID("_ColorTex");
        private static readonly int MaskTextureId = Shader.PropertyToID("_MaskTex");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int MirrorId = Shader.PropertyToID("_Mirror");
        private static readonly int SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
        private static readonly int EdgeContrastId = Shader.PropertyToID("_EdgeContrast");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowWidthId = Shader.PropertyToID("_GlowWidth");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int KindId = Shader.PropertyToID("_Kind");
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int ImageTextureId = Shader.PropertyToID("_ImageTex");
        private static readonly int ImageAspectId = Shader.PropertyToID("_ImageAspect");

        [Tooltip("The material of the person quad (shader VirtualMirror/Cutout). Copied at runtime; the asset is never changed.")]
        [SerializeField] private Material personMaterial;
        [Tooltip("The material of the backdrop quad (shader VirtualMirror/CutoutBackdrop).")]
        [SerializeField] private Material backdropMaterial;
        [Tooltip("The camera the view is placed in front of. Empty: the main camera.")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("How far in front of the camera the person is drawn, metres. Only matters when something else is in the scene to sort against.")]
        [Min(0.1f)]
        [SerializeField] private float distance = 5f;
        [SerializeField] private CutoutFit fit = CutoutFit.Contain;
        [SerializeField] private CutoutLook look = new CutoutLook();

        private CutoutTextures textures;
        private Mesh quadMesh;
        private GameObject personObject;
        private GameObject backdropObject;
        private Material personInstance;
        private Material backdropInstance;
        private int boundGeneration = -1;
        private CutoutMode effectiveMode;
        private bool mirrorApplied;

        /// <summary>How the cutout is shown; change it at any time.</summary>
        public CutoutLook Look {
            get {
                return look;
            }
        }

        public CutoutFit Fit {
            get {
                return fit;
            }
            set {
                fit = value;
            }
        }

        public Camera ViewCamera {
            get {
                return viewCamera;
            }
            set {
                viewCamera = value;
            }
        }

        /// <summary>The mode actually drawn: a stream with no picture (a mask-only sender) can only show a silhouette whatever was asked.</summary>
        public CutoutMode EffectiveMode {
            get {
                return effectiveMode;
            }
        }

        /// <summary>Whether the display is flipped left to right this frame.</summary>
        public bool MirrorApplied {
            get {
                return mirrorApplied;
            }
        }

        /// <summary>The runtime copy of the person material, for inspection and tests. Null until something has been drawn.</summary>
        internal Material PersonInstance {
            get {
                return personInstance;
            }
        }

        internal Material BackdropInstance {
            get {
                return backdropInstance;
            }
        }

        /// <summary>Takes the textures to draw. Null stops drawing. The view never owns them.</summary>
        public void Bind(CutoutTextures source) {
            textures = source;
            boundGeneration = -1;
        }

        private void LateUpdate() {
            Refresh();
        }

        private void OnDisable() {
            SetObjectsActive(false, false);
        }

        private void OnDestroy() {
            Release();
        }

        /// <summary>Destroys the quads, the runtime materials and the mesh. Called when the view is destroyed; a second call does nothing.</summary>
        internal void Release() {
            DestroyObject(personObject);
            DestroyObject(backdropObject);
            DestroyObject(personInstance);
            DestroyObject(backdropInstance);
            DestroyObject(quadMesh);
            personObject = null;
            backdropObject = null;
            personInstance = null;
            backdropInstance = null;
            quadMesh = null;
            boundGeneration = -1;
        }

        /// <summary>Applies <see cref="Look"/> and the camera's current view to the quads. Called every frame; public so a test can draw one frame without a player loop.</summary>
        public void Refresh() {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (textures == null || !textures.HasFrame || cam == null || !isActiveAndEnabled) {
                SetObjectsActive(false, false);
                return;
            }
            if (!EnsureObjects()) {
                return;
            }
            if (boundGeneration != textures.Generation) {
                personInstance.SetTexture(ColorTextureId, textures.ColorTexture);
                personInstance.SetTexture(MaskTextureId, textures.MaskTexture);
                boundGeneration = textures.Generation;
            }
            ApplyPerson();
            ApplyBackdrop(cam);
            PlaceQuads(cam);
            SetObjectsActive(true, look.Backdrop != CutoutBackdrop.None);
        }

        private void ApplyPerson() {
            effectiveMode = look.Mode;
            if (!textures.HasColor && effectiveMode != CutoutMode.Silhouette) {
                effectiveMode = CutoutMode.Silhouette;
            }
            if (look.Mirror == CutoutMirror.On) {
                mirrorApplied = true;
            } else if (look.Mirror == CutoutMirror.Off) {
                mirrorApplied = false;
            } else {
                mirrorApplied = textures.Live && !textures.Mirrored;
            }
            bool glowOn = look.Glow && effectiveMode != CutoutMode.Original;
            personInstance.SetFloat(ModeId, (float)(int)effectiveMode);
            personInstance.SetFloat(MirrorId, mirrorApplied ? 1f : 0f);
            personInstance.SetColor(SilhouetteColorId, look.SilhouetteColor);
            personInstance.SetFloat(EdgeContrastId, Mathf.Clamp(look.EdgeContrast, 0.5f, 4f));
            personInstance.SetColor(GlowColorId, look.GlowColor);
            personInstance.SetFloat(GlowWidthId, glowOn ? Mathf.Clamp(look.GlowWidth, 0f, 16f) : 0f);
            personInstance.SetFloat(GlowStrengthId, glowOn ? Mathf.Clamp(look.GlowStrength, 0f, 2f) : 0f);
            personInstance.SetFloat(OpacityId, Mathf.Clamp01(look.Opacity));
        }

        private void ApplyBackdrop(Camera cam) {
            if (look.Backdrop == CutoutBackdrop.None) {
                return;
            }
            float kind = 0f;
            if (look.Backdrop == CutoutBackdrop.Color) {
                kind = 1f;
            } else if (look.Backdrop == CutoutBackdrop.Gradient) {
                kind = 2f;
            } else if (look.Backdrop == CutoutBackdrop.Image) {
                kind = look.BackdropImage != null ? 3f : 1f;
            }
            backdropInstance.SetFloat(KindId, kind);
            backdropInstance.SetColor(ColorAId, look.BackdropColor);
            backdropInstance.SetColor(ColorBId, look.BackdropColor2);
            backdropInstance.SetFloat(AspectId, ViewAspect(cam));
            if (look.BackdropImage != null) {
                backdropInstance.SetTexture(ImageTextureId, look.BackdropImage);
                backdropInstance.SetFloat(ImageAspectId, (float)look.BackdropImage.width / Mathf.Max(1, look.BackdropImage.height));
            }
        }

        private void PlaceQuads(Camera cam) {
            Transform cameraTransform = cam.transform;
            float viewAspect = ViewAspect(cam);
            float personHeight = ViewHeightAt(cam, distance);
            float personWidth = personHeight * viewAspect;
            float aspect = textures.Aspect;
            float width;
            float height;
            bool pictureIsWider = aspect > viewAspect;
            if ((fit == CutoutFit.Contain) == pictureIsWider) {
                width = personWidth;
                height = width / aspect;
            } else {
                height = personHeight;
                width = height * aspect;
            }
            Place(personObject.transform, cameraTransform, distance, width, height);
            if (look.Backdrop != CutoutBackdrop.None) {
                float backdropDistance = distance + BackdropOffset;
                float backdropHeight = ViewHeightAt(cam, backdropDistance);
                Place(backdropObject.transform, cameraTransform, backdropDistance, backdropHeight * viewAspect, backdropHeight);
            }
        }

        private static float ViewAspect(Camera cam) {
            return Mathf.Max(0.01f, cam.aspect);
        }

        /// <summary>The height of the camera's view at <paramref name="at"/> metres in front of it.</summary>
        private static float ViewHeightAt(Camera cam, float at) {
            if (cam.orthographic) {
                return 2f * cam.orthographicSize;
            }
            return 2f * at * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        private void Place(Transform quad, Transform cameraTransform, float at, float width, float height) {
            quad.SetPositionAndRotation(cameraTransform.position + cameraTransform.forward * at, cameraTransform.rotation);
            Vector3 parentScale = transform.lossyScale;
            quad.localScale = new Vector3(width / Mathf.Max(0.0001f, parentScale.x), height / Mathf.Max(0.0001f, parentScale.y), 1f);
        }

        private bool EnsureObjects() {
            if (personObject != null) {
                return true;
            }
            Shader personShader = personMaterial != null ? personMaterial.shader : Shader.Find(PersonShaderName);
            Shader backdropShader = backdropMaterial != null ? backdropMaterial.shader : Shader.Find(BackdropShaderName);
            if (personShader == null || backdropShader == null) {
                Debug.LogError("CutoutView: the shaders " + PersonShaderName + " and " + BackdropShaderName + " were not found. Assign the materials, "
                        + "or keep the two shader files in the project.");
                enabled = false;
                return false;
            }
            quadMesh = BuildQuad();
            personInstance = personMaterial != null ? new Material(personMaterial) : new Material(personShader);
            backdropInstance = backdropMaterial != null ? new Material(backdropMaterial) : new Material(backdropShader);
            personInstance.name = "Cutout Person (runtime)";
            backdropInstance.name = "Cutout Backdrop (runtime)";
            personObject = CreateQuadObject("Cutout Person", personInstance);
            backdropObject = CreateQuadObject("Cutout Backdrop", backdropInstance);
            return true;
        }

        private GameObject CreateQuadObject(string objectName, Material material) {
            GameObject quad = new GameObject(objectName);
            quad.hideFlags = HideFlags.DontSave;
            quad.layer = gameObject.layer;
            quad.transform.SetParent(transform, false);
            MeshFilter filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = quadMesh;
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            quad.SetActive(false);
            return quad;
        }

        private void SetObjectsActive(bool person, bool backdrop) {
            if (personObject != null && personObject.activeSelf != person) {
                personObject.SetActive(person);
            }
            if (backdropObject != null && backdropObject.activeSelf != backdrop) {
                backdropObject.SetActive(backdrop);
            }
        }

        /// <summary>A unit quad in the XY plane, uv (0,0) at the bottom left, as seen from a camera looking along +Z.</summary>
        private static Mesh BuildQuad() {
            Mesh mesh = new Mesh();
            mesh.name = "Cutout Quad";
            mesh.vertices = new Vector3[] {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.01f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void DestroyObject(Object target) {
            if (target == null) {
                return;
            }
            if (Application.isPlaying) {
                Destroy(target);
            } else {
                DestroyImmediate(target);
            }
        }
    }
}
