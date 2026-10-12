using System.IO;

using ChromaticRain;
using ChromaticRain.EditorTools;

using Fluxy;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

using VirtualMirror.App;

namespace FluidDisruption.EditorTools {
    /// <summary>
    /// Builds the showcase scene and its assets from code, so the scene is reproducible and the wiring is
    /// reviewable. Re-running it rewrites the scene and the showcase's own materials; it never touches
    /// FluXY's files (it only COPIES FluXY's simulation and splat materials) and never changes Build
    /// Settings or any project-wide setting. The settings asset is kept, so tuning survives a rebuild.
    /// </summary>
    public static class FluidDisruptionSceneBuilder {
        public const string Root = "Assets/Games/viitorx-vrm-avtar-unity/Showcase/FluidDisruption";
        public const string ScenePath = Root + "/Scenes/FluidDisruption_Showcase.unity";
        private const string SettingsPath = Root + "/Settings/FluidDisruptionSettings.asset";
        private const string StoragePath = Root + "/Settings/FluidDisruptionFluxyStorage.asset";
        private const string VolumePath = Root + "/Settings/FluidDisruptionPostProcess.asset";

        [MenuItem("Showcase/Fluid Disruption/Build Showcase Scene")]
        public static void BuildFromMenu() {
            string path = BuildScene();
            Debug.Log("[FluidDisruption] Scene built: " + path);
        }

        [MenuItem("Showcase/Fluid Disruption/Open Showcase Scene")]
        public static void OpenScene() {
            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            } else {
                Debug.LogWarning("[FluidDisruption] The scene does not exist yet. Use Showcase > Fluid Disruption > Build Showcase Scene.");
            }
        }

        [MenuItem("Showcase/Fluid Disruption/Reset Settings To Defaults")]
        public static void ResetSettings() {
            FluidDisruptionSettings asset = ShowcaseBuilderUtil.LoadOrCreate<FluidDisruptionSettings>(SettingsPath);
            FluidDisruptionSettings fresh = ScriptableObject.CreateInstance<FluidDisruptionSettings>();
            EditorUtility.CopySerialized(fresh, asset);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[FluidDisruption] Settings reset to the code defaults: " + SettingsPath);
        }

        public static string BuildScene() {
            ShowcaseBuilderUtil.EnsureFolders(Root, new string[] { "Scripts", "Shaders", "Materials", "Scenes", "Settings", "Editor", "Tests" });
            ShowcaseBuilderUtil.LoadOrCreate<FluidDisruptionSettings>(SettingsPath);
            ShowcaseBuilderUtil.LoadOrCreateMaterial(Root, "Fluid", "FluidDisruption/Fluid");
            ShowcaseBuilderUtil.CopyMaterial(Root, ShowcaseBuilderUtil.FluxySimulationMaterial, "FluxySimulation");
            ShowcaseBuilderUtil.CopyMaterial(Root, ShowcaseBuilderUtil.FluxySplatMaterial, "FluxySplat");

            FluxyStorage storage = ShowcaseBuilderUtil.LoadOrCreate<FluxyStorage>(StoragePath);
            storage.memoryBudget = 96;
            storage.densityPrecision = FluxyStorage.FluidTexturePrecision.Half;
            storage.velocityPrecision = FluxyStorage.FluidTexturePrecision.Half;
            EditorUtility.SetDirty(storage);
            ShowcaseBuilderUtil.LoadOrCreateBloomProfile(VolumePath, 0.9f, 0.3f, 0.6f);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "FluidDisruption_Showcase";

            // Creating a scene can unload assets that only this method referenced, so take fresh
            // references to the persisted assets before wiring anything.
            AssetDatabase.SaveAssets();
            FluidDisruptionSettings settings = AssetDatabase.LoadAssetAtPath<FluidDisruptionSettings>(SettingsPath);
            storage = AssetDatabase.LoadAssetAtPath<FluxyStorage>(StoragePath);
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            Material fluidMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Fluid.mat");
            Material simMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/FluxySimulation.mat");
            Material splatMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/FluxySplat.mat");
            Texture noiseTexture = AssetDatabase.LoadAssetAtPath<Texture>(ShowcaseBuilderUtil.FluxyNoiseTexture);
            if (settings == null || storage == null || profile == null || fluidMat == null
                || simMat == null || splatMat == null) {
                Debug.LogError("[FluidDisruption] Builder could not load its own assets; nothing was wired.");
                return null;
            }

            float width = settings.viewHeightMetres * 16f / 9f;

            // Camera: orthographic and full-screen, set up again at runtime from the settings.
            GameObject camObject = new GameObject("Showcase Camera");
            camObject.tag = "MainCamera";
            Camera cam = camObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = settings.viewHeightMetres * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = settings.backgroundLow;
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

            // FluXY: one solver, one screen-filling container, the rendering component.
            GameObject fluidObject = new GameObject("Fluid (FluXY)");
            FluxySolver solver = fluidObject.AddComponent<FluxySolver>();
            solver.storage = storage;
            solver.simulationMaterial = simMat;
            solver.desiredResolution = settings.qualityProfile.resolution;
            solver.densitySupersampling = settings.qualityProfile.supersampling;
            solver.maxTimestep = 0.008f;
            solver.maxSteps = settings.qualityProfile.maxSteps;
            solver.pressureSolver = FluxySolver.PressureSolver.Separable;
            solver.readable = FluxySolver.ReadbackMode.None;

            FluxyContainer container = fluidObject.AddComponent<FluxyContainer>();
            MeshRenderer containerRenderer = fluidObject.GetComponent<MeshRenderer>();
            containerRenderer.sharedMaterial = fluidMat;
            containerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            containerRenderer.receiveShadows = false;
            containerRenderer.lightProbeUsage = LightProbeUsage.Off;
            containerRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            container.containerShape = FluxyContainer.ContainerShape.Plane;
            container.size = new Vector3(width, settings.viewHeightMetres, 1f);
            container.solver = solver;

            FluidDisruptionFluid fluid = fluidObject.AddComponent<FluidDisruptionFluid>();
            ShowcaseBuilderUtil.SetRef(fluid, "solver", solver);
            ShowcaseBuilderUtil.SetRef(fluid, "container", container);
            ShowcaseBuilderUtil.SetRef(fluid, "containerRenderer", containerRenderer);
            ShowcaseBuilderUtil.SetRef(fluid, "fluidMaterial", fluidMat);
            ShowcaseBuilderUtil.SetRef(fluid, "simulationMaterial", simMat);
            ShowcaseBuilderUtil.SetRef(fluid, "detailTexture", noiseTexture);

            // The calm baseline: one field target (the pattern it relaxes toward) and a few flow sources,
            // independent of tracking.
            int sourceCount = settings.baselineSources.Length;
            FluxyTarget[] flowTargets = new FluxyTarget[sourceCount];
            GameObject baselineObject = new GameObject("Baseline");
            baselineObject.transform.SetParent(fluidObject.transform, false);
            FluxyTarget fieldTarget = ShowcaseBuilderUtil.CreateTarget(baselineObject.transform, "Baseline Field");
            for (int i = 0; i < sourceCount; i++) {
                flowTargets[i] = ShowcaseBuilderUtil.CreateTarget(baselineObject.transform, "Baseline Flow " + (i + 1));
            }
            FluidBaselineSources baseline = baselineObject.AddComponent<FluidBaselineSources>();
            ShowcaseBuilderUtil.SetRef(baseline, "fieldTarget", fieldTarget);
            SetRefArray(baseline, "flowTargets", flowTargets);
            ShowcaseBuilderUtil.SetRef(baseline, "splatMaterial", splatMat);
            ShowcaseBuilderUtil.SetRef(baseline, "noiseTexture", noiseTexture);

            // Interaction targets: left hand, right hand, torso.
            GameObject interactionObject = new GameObject("Interaction Targets");
            interactionObject.transform.SetParent(fluidObject.transform, false);
            FluxyTarget leftTarget = ShowcaseBuilderUtil.CreateTarget(interactionObject.transform, "Left Hand Target");
            FluxyTarget rightTarget = ShowcaseBuilderUtil.CreateTarget(interactionObject.transform, "Right Hand Target");
            FluxyTarget torsoTarget = ShowcaseBuilderUtil.CreateTarget(interactionObject.transform, "Torso Target");
            FluidDisruptionTargetDriver driver = interactionObject.AddComponent<FluidDisruptionTargetDriver>();
            ShowcaseBuilderUtil.SetRef(driver, "leftHandTarget", leftTarget);
            ShowcaseBuilderUtil.SetRef(driver, "rightHandTarget", rightTarget);
            ShowcaseBuilderUtil.SetRef(driver, "torsoTarget", torsoTarget);
            ShowcaseBuilderUtil.SetRef(driver, "splatMaterial", splatMat);
            ShowcaseBuilderUtil.SetRef(driver, "noiseTexture", noiseTexture);

            FluxyTarget[] all = new FluxyTarget[sourceCount + 4];
            all[0] = fieldTarget;
            for (int i = 0; i < sourceCount; i++) {
                all[i + 1] = flowTargets[i];
            }
            all[sourceCount + 1] = leftTarget;
            all[sourceCount + 2] = rightTarget;
            all[sourceCount + 3] = torsoTarget;
            container.targets = all;

            // FluXY builds the plane mesh itself, from the container's size, when the scene loads. Do not
            // save the Editor-time mesh (built at the default size) into the scene.
            containerRenderer.GetComponent<MeshFilter>().sharedMesh = null;

            // TRACKING IS THE BOOTSTRAP'S JOB. The same AppBootstrap component Bootstrap.unity carries,
            // with its Inspector sections ("Tracking Source", "Video Test Source (F-49)", ...), owns the
            // Python sidecar: it starts it, picks the OAK-D / video / webcam / phone / LAN-camera source,
            // and restarts it when the video fields change. The showcase only LISTENS to the stream. When
            // this scene is reached through Bootstrap.unity the component here destroys itself
            // (AppBootstrap is a singleton) and the real one keeps going.
            GameObject trackingObject = new GameObject("Tracking (AppBootstrap)");
            AppBootstrap bootstrap = trackingObject.AddComponent<AppBootstrap>();
            SerializedObject bso = new SerializedObject(bootstrap);
            bso.FindProperty("mirrorSceneName").stringValue = scene.name;
            bso.FindProperty("loadMirrorSceneOnStart").boolValue = false;
            bso.FindProperty("videoSource").stringValue = "Assets/Games/video/147_1080p.mp4";
            bso.ApplyModifiedPropertiesWithoutUndo();

            // The controller, the UI, the debug view and the optional frame probe.
            GameObject controllerObject = new GameObject("Fluid Disruption Showcase");
            FluidDisruptionController controller = controllerObject.AddComponent<FluidDisruptionController>();
            FluidDisruptionUI ui = controllerObject.AddComponent<FluidDisruptionUI>();
            controllerObject.AddComponent<ShowcasePerformanceProbe>();
            FluidDisruptionDebugView debug = controllerObject.AddComponent<FluidDisruptionDebugView>();
            debug.enabled = false;
            ShowcaseBuilderUtil.SetRef(ui, "controller", controller);
            ShowcaseBuilderUtil.SetRef(debug, "controller", controller);

            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("showcaseCamera").objectReferenceValue = cam;
            so.FindProperty("fluid").objectReferenceValue = fluid;
            so.FindProperty("baseline").objectReferenceValue = baseline;
            so.FindProperty("targetDriver").objectReferenceValue = driver;
            so.FindProperty("debugView").objectReferenceValue = debug;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ScenePath;
        }

        private static void SetRefArray(Object target, string field, Object[] values) {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) {
                Debug.LogError("[FluidDisruption] Builder: " + target.GetType().Name + " has no field '" + field + "'.");
                return;
            }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) {
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
