using System.IO;

using Fluxy;

using VirtualMirror.App;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ChromaticRain.EditorTools {
    /// <summary>
    /// Builds the showcase scene and its assets from code, so the scene is reproducible and the
    /// wiring is reviewable. Re-running it rewrites the scene and the showcase's own materials; it
    /// never touches FluXY's files (it only COPIES FluXY's simulation and splat materials) and never
    /// changes Build Settings or any project-wide setting.
    /// </summary>
    public static class ChromaticRainSceneBuilder {
        public const string Root = "Assets/Games/viitorx-vrm-avtar-unity/Showcase/ChromaticRain";
        public const string ScenePath = Root + "/Scenes/ChromaticRain_Showcase.unity";
        private const string SettingsPath = Root + "/Settings/ChromaticRainSettings.asset";
        private const string StoragePath = Root + "/Settings/ChromaticRainFluxyStorage.asset";
        private const string VolumePath = Root + "/Settings/ChromaticRainPostProcess.asset";
        private const string FluxySimulationMaterial = "Assets/FluXY/Resources/Materials/FluidSimulation.mat";
        private const string FluxySplatMaterial = "Assets/FluXY/Resources/Materials/Splat.mat";
        private const string FluxySplatTexture = "Assets/FluXY/Resources/Textures/SplatTexture.psd";

        [MenuItem("Showcase/Chromatic Rain/Build Showcase Scene")]
        public static void BuildFromMenu() {
            string path = BuildScene();
            Debug.Log("[ChromaticRain] Scene built: " + path);
        }

        [MenuItem("Showcase/Chromatic Rain/Open Showcase Scene")]
        public static void OpenScene() {
            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            } else {
                Debug.LogWarning("[ChromaticRain] The scene does not exist yet. Use Showcase > Chromatic Rain > Build Showcase Scene.");
            }
        }

        public static string BuildScene() {
            EnsureFolders();
            ChromaticRainSettings settings = LoadOrCreate<ChromaticRainSettings>(SettingsPath);

            Material rainMat = LoadOrCreateMaterial("Rain", "ChromaticRain/Rain");
            Material maskMat = LoadOrCreateMaterial("SilhouetteMask", "ChromaticRain/SilhouetteMask");
            Material overlayMat = LoadOrCreateMaterial("SilhouetteOverlay", "ChromaticRain/SilhouetteOverlay");
            Material fluidMat = LoadOrCreateMaterial("FluidLayer", "ChromaticRain/FluidLayer");
            Material simMat = CopyMaterial(FluxySimulationMaterial, "FluxySimulation");
            Material splatMat = CopyMaterial(FluxySplatMaterial, "FluxySplat");
            Texture splatTexture = AssetDatabase.LoadAssetAtPath<Texture>(FluxySplatTexture);

            FluxyStorage storage = LoadOrCreate<FluxyStorage>(StoragePath);
            storage.memoryBudget = 32;
            storage.densityPrecision = FluxyStorage.FluidTexturePrecision.Half;
            storage.velocityPrecision = FluxyStorage.FluidTexturePrecision.Half;
            EditorUtility.SetDirty(storage);

            VolumeProfile profile = LoadOrCreateVolumeProfile();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ChromaticRain_Showcase";

            // Creating a scene can unload assets that only this method referenced, so take fresh
            // references to the persisted assets before wiring anything.
            AssetDatabase.SaveAssets();
            settings = AssetDatabase.LoadAssetAtPath<ChromaticRainSettings>(SettingsPath);
            storage = AssetDatabase.LoadAssetAtPath<FluxyStorage>(StoragePath);
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            rainMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Rain.mat");
            maskMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/SilhouetteMask.mat");
            overlayMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/SilhouetteOverlay.mat");
            fluidMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/FluidLayer.mat");
            simMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/FluxySimulation.mat");
            splatMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/FluxySplat.mat");
            splatTexture = AssetDatabase.LoadAssetAtPath<Texture>(FluxySplatTexture);
            if (settings == null || storage == null || profile == null) {
                Debug.LogError("[ChromaticRain] Builder could not load its own assets; nothing was wired.");
                return null;
            }

            // Camera: orthographic, set up again at runtime from the settings.
            GameObject camObject = new GameObject("Showcase Camera");
            camObject.tag = "MainCamera";
            Camera cam = camObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = settings.viewHeightMetres * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = settings.backgroundColor;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.allowHDR = true;
            cam.allowMSAA = false;
            UniversalAdditionalCameraData camData = camObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;

            GameObject volumeObject = new GameObject("Post Processing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // Rain.
            GameObject rainObject = CreateQuad("Chromatic Rain", rainMat);
            RainRenderer rain = rainObject.AddComponent<RainRenderer>();
            SetRef(rain, "quad", rainObject.GetComponent<MeshRenderer>());
            SetRef(rain, "sourceMaterial", rainMat);

            // Fluid layer.
            GameObject fluidObject = new GameObject("Fluid Layer (FluXY)");
            FluxySolver solver = fluidObject.AddComponent<FluxySolver>();
            solver.storage = storage;
            solver.simulationMaterial = simMat;
            solver.desiredResolution = settings.fluidResolution;
            solver.densitySupersampling = 1;
            solver.maxTimestep = 0.008f;
            solver.maxSteps = 4;
            solver.pressureSolver = FluxySolver.PressureSolver.Separable;
            solver.readable = FluxySolver.ReadbackMode.None;

            FluxyContainer container = fluidObject.AddComponent<FluxyContainer>();
            MeshRenderer containerRenderer = fluidObject.GetComponent<MeshRenderer>();
            containerRenderer.sharedMaterial = fluidMat;
            containerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            containerRenderer.receiveShadows = false;
            container.containerShape = FluxyContainer.ContainerShape.Plane;
            container.size = new Vector3(settings.viewHeightMetres * 16f / 9f, settings.viewHeightMetres, 1f);
            container.solver = solver;

            FluxyTarget leftTarget = CreateTarget(fluidObject.transform, "Left Hand Target");
            FluxyTarget rightTarget = CreateTarget(fluidObject.transform, "Right Hand Target");
            FluxyTarget bodyTarget = CreateTarget(fluidObject.transform, "Body Target");
            container.targets = new FluxyTarget[] { leftTarget, rightTarget, bodyTarget };

            FluidLayer layer = fluidObject.AddComponent<FluidLayer>();
            SetRef(layer, "solver", solver);
            SetRef(layer, "container", container);
            SetRef(layer, "containerRenderer", containerRenderer);
            SetRef(layer, "leftHandTarget", leftTarget);
            SetRef(layer, "rightHandTarget", rightTarget);
            SetRef(layer, "bodyTarget", bodyTarget);
            SetRef(layer, "fluidMaterial", fluidMat);
            SetRef(layer, "simulationMaterial", simMat);
            SetRef(layer, "splatMaterial", splatMat);
            SetRef(layer, "splatTexture", splatTexture);

            // Silhouette overlay + mask renderer.
            GameObject overlayObject = CreateQuad("Silhouette Overlay", overlayMat);
            SilhouetteMaskRenderer maskRenderer = overlayObject.AddComponent<SilhouetteMaskRenderer>();
            SetRef(maskRenderer, "maskMaterial", maskMat);
            SetRef(maskRenderer, "overlayQuad", overlayObject.GetComponent<MeshRenderer>());
            SetRef(maskRenderer, "overlayMaterial", overlayMat);

            // TRACKING IS THE BOOTSTRAP'S JOB. The same AppBootstrap component Bootstrap.unity carries,
            // with its Inspector sections ("Tracking Source", "Video Test Source (F-49)", ...), owns the
            // Python sidecar: it starts it, picks the OAK-D / video / webcam / phone / LAN-camera source,
            // and restarts it when the video fields change. The showcase only LISTENS to the stream, as
            // every experience scene does. When this scene is reached through Bootstrap.unity the
            // component here destroys itself (AppBootstrap is a singleton) and the real one keeps going.
            GameObject trackingObject = new GameObject("Tracking (AppBootstrap)");
            AppBootstrap bootstrap = trackingObject.AddComponent<AppBootstrap>();
            SerializedObject bso = new SerializedObject(bootstrap);
            bso.FindProperty("mirrorSceneName").stringValue = scene.name;
            bso.FindProperty("loadMirrorSceneOnStart").boolValue = false;
            bso.FindProperty("videoSource").stringValue = "Assets/Games/video/147_1080p.mp4";
            bso.ApplyModifiedPropertiesWithoutUndo();

            // The controller, the UI and the debug view.
            GameObject controllerObject = new GameObject("Chromatic Rain Showcase");
            ChromaticRainShowcase showcase = controllerObject.AddComponent<ChromaticRainShowcase>();
            ShowcaseUI ui = controllerObject.AddComponent<ShowcaseUI>();
            controllerObject.AddComponent<ShowcasePerformanceProbe>();
            ShowcaseDebugOverlay debug = controllerObject.AddComponent<ShowcaseDebugOverlay>();
            debug.enabled = false;
            SetRef(ui, "showcase", showcase);
            SetRef(debug, "showcase", showcase);

            SerializedObject so = new SerializedObject(showcase);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("showcaseCamera").objectReferenceValue = cam;
            so.FindProperty("rain").objectReferenceValue = rain;
            so.FindProperty("silhouette").objectReferenceValue = maskRenderer;
            so.FindProperty("fluid").objectReferenceValue = layer;
            so.FindProperty("debugOverlay").objectReferenceValue = debug;
            SerializedProperty quads = so.FindProperty("fullScreenQuads");
            quads.arraySize = 2;
            quads.GetArrayElementAtIndex(0).objectReferenceValue = rainObject.transform;
            quads.GetArrayElementAtIndex(1).objectReferenceValue = overlayObject.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            float width = settings.viewHeightMetres * 16f / 9f;
            rainObject.transform.localScale = new Vector3(width, settings.viewHeightMetres, 1f);
            overlayObject.transform.localScale = new Vector3(width, settings.viewHeightMetres, 1f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ScenePath;
        }

        private static GameObject CreateQuad(string name, Material material) {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null) {
                Object.DestroyImmediate(collider);
            }
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return quad;
        }

        private static FluxyTarget CreateTarget(Transform parent, string name) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            FluxyTarget target = go.AddComponent<FluxyTarget>();
            target.enabled = false;
            return target;
        }

        private static void SetRef(Object target, string field, Object value) {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) {
                Debug.LogError("[ChromaticRain] Builder: " + target.GetType().Name + " has no field '" + field + "'.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders() {
            string[] folders = { "Scripts", "Shaders", "Materials", "Scenes", "Settings", "Editor", "Tests" };
            for (int i = 0; i < folders.Length; i++) {
                string full = Root + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(full)) {
                    AssetDatabase.CreateFolder(Root, folders[i]);
                }
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Material LoadOrCreateMaterial(string name, string shaderName) {
            string path = Root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null) {
                Debug.LogError("[ChromaticRain] Shader '" + shaderName + "' was not found; is it imported?");
            }
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            } else if (shader != null && material.shader != shader) {
                material.shader = shader;
            }
            return material;
        }

        /// <summary>A private copy of a FluXY material, so runtime state never dirties FluXY's own.</summary>
        private static Material CopyMaterial(string sourcePath, string name) {
            string path = Root + "/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) {
                return existing;
            }
            Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) {
                Debug.LogError("[ChromaticRain] FluXY material not found at " + sourcePath);
                return null;
            }
            Material copy = new Material(source);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        private static VolumeProfile LoadOrCreateVolumeProfile() {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            Bloom bloom;
            if (!profile.TryGet(out bloom)) {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            // Restrained: only the brightest streak cores and the rim glow, no haze.
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.35f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.55f;
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
