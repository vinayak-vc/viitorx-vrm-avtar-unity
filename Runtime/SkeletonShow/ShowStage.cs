using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-30 — the physical stage every experience stands on: camera framing, the bloom volume the
    /// HDR palette needs, and a floor grid.
    ///
    /// WHY THE SCENES ARE EMPTY AND THIS IS CODE. A scene asset full of hand-placed particle systems,
    /// materials and post-process volumes is not reviewable in a diff and drifts from the code that
    /// drives it. Every experience scene here is a camera and one GameObject; everything visible is
    /// built from a file with a reason next to it. That was F-28's rule and it is kept.
    ///
    /// THE BLOOM VOLUME IS NOT DECORATION. The whole palette is HDR - colour values above 1.0 - and
    /// the bloom threshold sits at 0.9. Without this volume every colour clips to white and nothing
    /// glows; the experiences look flat and wrong rather than subtly different. That is why this is
    /// built here rather than left to each scene to remember.
    /// </summary>
    public static class ShowStage {
        /// <summary>
        /// Frame the camera for a life-size standing figure and switch on post-processing.
        /// <paramref name="distance"/> is how far back the camera sits; the default 3.2 m at 46 deg
        /// gives a ~2.7 m tall view, which holds a 1.7 m body with head- and foot-room. The first
        /// live capture at anything closer cropped the head and the feet off the frame.
        /// </summary>
        public static Camera BuildCamera(SkeletonShowPalette palette, float distance = 3.2f,
                                         float height = 1.0f, float fieldOfView = 46f) {
            Camera cam = Camera.main;
            if (cam == null) {
                GameObject camObject = new GameObject("Main Camera");
                camObject.tag = "MainCamera";
                cam = camObject.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(0f, height, -distance);
            cam.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = palette.Background;
            cam.fieldOfView = fieldOfView;
            UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) {
                data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            data.renderPostProcessing = true;
            return cam;
        }

        /// <summary>The Bloom + ACES volume the HDR palette depends on. Parented to
        /// <paramref name="parent"/> so it dies with the experience.</summary>
        public static Volume BuildPostProcessing(Transform parent) {
            GameObject volumeObject = new GameObject("PostProcessing");
            volumeObject.transform.SetParent(parent, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "ExperienceProfile";
            volume.sharedProfile = profile;
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;    // below the palette's HDR values, above ordinary pixels
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.5f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.72f;
            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;   // keeps the hot end from turning to paste
            return volume;
        }

        /// <summary>
        /// A faint floor grid. Lines rather than a plane: a lit plane needs a light, and a light
        /// washes out the additive glow everything else depends on.
        /// </summary>
        public static GameObject BuildFloorGrid(Transform parent, SkeletonShowPalette palette,
                                                float extent = 5f, int lines = 21) {
            GameObject grid = new GameObject("FloorGrid");
            grid.transform.SetParent(parent, false);
            Material material = palette.NewAdditive(new Color(palette.Cool.r * 0.06f,
                                                              palette.Cool.g * 0.06f,
                                                              palette.Cool.b * 0.10f, 0.35f));
            int i = 0;
            while (i < lines) {
                float t = -extent + (2f * extent) * (i / (float)(lines - 1));
                Line(grid.transform, material, new Vector3(t, 0f, -extent), new Vector3(t, 0f, extent));
                Line(grid.transform, material, new Vector3(-extent, 0f, t), new Vector3(extent, 0f, t));
                i = i + 1;
            }
            return grid;
        }

        private static void Line(Transform parent, Material material, Vector3 a, Vector3 b) {
            GameObject go = new GameObject("grid");
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = 0.006f;
            lr.endWidth = 0.006f;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = material;
        }

        /// <summary>An unlit sphere with its own material, the building block every experience uses
        /// for joints, targets and bubbles. Colliders are stripped: nothing here uses physics, and a
        /// few hundred stray colliders cost real time for nothing.</summary>
        public static Transform Sphere(Transform parent, string name, float size,
                                       SkeletonShowPalette palette, Color colour, bool additive,
                                       out Material material) {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Object.Destroy(collider);
            }
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            material = additive ? palette.NewAdditive(colour) : palette.NewOpaque(colour);
            mr.sharedMaterial = material;
            return go.transform;
        }

        /// <summary>
        /// THE DEPTH CUE. A flat glowing pool on the floor, directly under a body.
        ///
        /// WHY THIS AND NOT A COLOUR OR A WIDTH. The stream carries real depth — measured on a live
        /// session, the wrist moves 0.79 m in Z against 1.14 m in X — but the scenes could not show
        /// it: a dead-on camera 3.2 m away turns that 0.79 m into roughly a 15% change in apparent
        /// size, which the eye does not read. The obvious fixes are both taken. Colour already means
        /// SPEED in every mode, and <see cref="Experiences.BodyRenderer"/> says plainly why a second
        /// meaning on that channel is unreadable; line width already means confidence. A separate
        /// object on the floor spends no existing channel.
        ///
        /// It works because of where the camera is, not in spite of it. At 1 m of height and 3.2 m
        /// of distance the floor is seen at a grazing ~17 degrees, so a metre of walking toward the
        /// camera sweeps the pool a long way DOWN the screen, across the converging grid lines. The
        /// flatter the view, the better this reads — which is the opposite of how apparent size
        /// behaves, and why it is the right cue for this stage.
        ///
        /// Deliberately dim: it is an anchor, not a subject. The body has to stay the brightest
        /// thing on screen.
        /// </summary>
        public static Transform GroundPool(Transform parent, SkeletonShowPalette palette,
                                           out Material material) {
            Transform pool = Sphere(parent, "GroundPool", 1f, palette, palette.Cool, true,
                                    out material);
            return pool;
        }

        /// <summary>Place a <see cref="GroundPool"/> under a body. `floorY` is the stage's own floor,
        /// so the pool sits on the same plane the figure is grounded to rather than on y=0 while the
        /// feet are somewhere else.</summary>
        public static void PlaceGroundPool(Transform pool, Vector3 groundPoint, float radius,
                                           float floorY = 0f) {
            if (pool == null) {
                return;
            }
            pool.position = new Vector3(groundPoint.x, floorY + 0.01f, groundPoint.z);
            // Squashed to a disc rather than scaled as a sphere: a sphere sitting on the floor reads
            // as a ball the person is standing on, which is a different (and wrong) depth claim.
            pool.localScale = new Vector3(radius * 2f, 0.012f, radius * 2f);
        }

        /// <summary>A two-point line renderer in world space, for bones and links.</summary>
        public static LineRenderer Line(Transform parent, string name, float width, Material material) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = material;
            return lr;
        }
    }
}
