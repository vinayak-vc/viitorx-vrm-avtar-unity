using System.Collections.Generic;
using System.IO;

using ChromaticRain.EditorTools;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace HumanEcho.EditorTools {
    /// <summary>
    /// Builds the Human Echo scene from code, so it is reproducible and the wiring is reviewable: an
    /// orthographic HDR camera with a mild bloom, the scene's AppBootstrap with body tracking off and the
    /// cutout stream on (the Cutout, Hands and Face arrangement), and one conductor object that carries the
    /// controller and the HUD and points at the three looks and the layers' shaders.
    ///
    /// Re-running it rewrites the scene only. The three look assets are created when missing and never
    /// overwritten (see <see cref="HumanEchoPresetBuilder"/>), so tuning survives. A shader or asset that
    /// cannot be found is a warning that names it, never a failure: the scene still builds and the layer that
    /// needs it stays off. Never touches Build Settings or any project-wide setting (Virtual Mirror >
    /// Register All Scenes in Build Settings does that).
    /// </summary>
    public static class HumanEchoSceneBuilder {
        public const string Root = "Assets/Games/viitorx-vrm-avtar-unity/Showcase/HumanEcho";
        public const string SceneName = "HumanEcho_Showcase";
        public const string ScenePath = Root + "/Scenes/" + SceneName + ".unity";
        private const string VolumePath = Root + "/Settings/HumanEchoPostProcess.asset";
        private const string ShaderFolder = Root + "/Shaders";
        private const string ParticleComputePath = ShaderFolder + "/HumanEcho_Particles.compute";

        [MenuItem("Showcase/Human Echo/Build Showcase Scene")]
        public static void BuildFromMenu() {
            string path = BuildScene();
            Debug.Log("[HumanEcho] Scene built: " + path);
        }

        [MenuItem("Showcase/Human Echo/Open Showcase Scene")]
        public static void OpenScene() {
            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            } else {
                Debug.LogWarning("[HumanEcho] The scene does not exist yet. "
                                 + "Use Showcase > Human Echo > Build Showcase Scene.");
            }
        }

        public static string BuildScene() {
            ShowcaseBuilderUtil.EnsureFolders(Root, new string[] { "Scenes", "Settings" });
            // LoadOrCreateBloomProfile rewrites the bloom values every time, so it only runs for a profile
            // that does not exist yet: a rebuild
            // must not discard the Bloom tuning someone made in the profile asset.
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath) == null) {
                ShowcaseBuilderUtil.LoadOrCreateBloomProfile(VolumePath, 0.9f, 0.45f, 0.7f);
            }
            HumanEchoPresetBuilder.EnsureLooks();
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = SceneName;

            // Creating a scene can unload assets that only this method referenced, so take fresh references
            // to the persisted ones.
            List<string> missing = new List<string>();
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null) {
                Debug.LogWarning("[HumanEcho] Builder: the post-processing profile " + VolumePath
                                 + " could not be loaded; the scene has no bloom.");
                missing.Add("post-processing profile");
            }
            Shader personField = FindShader("personFieldShader", new string[] { "HumanEcho_PersonField" },
                                            new string[] { "HumanEcho/PersonField" }, missing);
            Shader smokeInject = FindShader("smokeInjectShader", new string[] { "HumanEcho_SmokeInject" },
                                            new string[] { "HumanEcho/SmokeInject" }, missing);
            Shader smokeShader = FindShader("smokeShader", new string[] { "HumanEcho_Smoke" },
                                            new string[] { "HumanEcho/Smoke" }, missing);
            Shader echoShader = FindShader("echoShader", new string[] { "HumanEcho_Echo" },
                                           new string[] { "HumanEcho/Echo" }, missing);
            Shader outlineShader = FindShader("outlineShader", new string[] { "HumanEcho_Outline" },
                                              new string[] { "HumanEcho/Outline" }, missing);
            Shader personShader = FindShader("personShader", new string[] { "HumanEcho_Person" },
                                             new string[] { "HumanEcho/Person" }, missing);
            Shader particleDrawShader = FindShader(
                "particleDrawShader",
                new string[] { "HumanEcho_ParticleDraw", "HumanEcho_ParticlesDraw" },
                new string[] { "HumanEcho/ParticleDraw", "HumanEcho/ParticlesDraw" },
                missing);
            ComputeShader particleCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ParticleComputePath);
            if (particleCompute == null) {
                Debug.LogWarning("[HumanEcho] Builder: the compute shader " + ParticleComputePath
                                 + " was not found; the particle layer will be off.");
                missing.Add("particleCompute");
            }
            List<HumanEchoSettings> looks = LoadLooks(missing);

            GameObject camObject = new GameObject("Showcase Camera");
            camObject.tag = "MainCamera";
            Camera cam = camObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.allowHDR = true;
            cam.allowMSAA = false;
            camObject.AddComponent<AudioListener>();
            UniversalAdditionalCameraData camData = camObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;

            GameObject volumeObject = new GameObject("Post Processing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // TRACKING IS THE BOOTSTRAP'S JOB, as in the Cutout scene: the same AppBootstrap component, body
            // path off, cutout stream on.
            GameObject trackingObject = new GameObject("Tracking (AppBootstrap)");
            AppBootstrap bootstrap = trackingObject.AddComponent<AppBootstrap>();
            SerializedObject bso = new SerializedObject(bootstrap);
            bso.FindProperty("mirrorSceneName").stringValue = SceneName;
            bso.FindProperty("loadMirrorSceneOnStart").boolValue = false;
            bso.FindProperty("useOakUdpTracking").boolValue = false;
            bso.FindProperty("useMediaPipeTracking").boolValue = false;
            bso.FindProperty("showDebugSkeleton").boolValue = false;
            bso.FindProperty("vision.handsMode").intValue = (int)VisionSenderMode.Off;
            bso.FindProperty("vision.faceMode").intValue = (int)VisionSenderMode.Off;
            bso.FindProperty("vision.cutoutMode").intValue = (int)VisionSenderMode.Auto;
            bso.ApplyModifiedPropertiesWithoutUndo();

            // The conductor and its HUD live on one object, so the HUD finds the controller even if its own
            // reference is lost.
            GameObject echoObject = new GameObject("Human Echo");
            HumanEchoController controller = echoObject.AddComponent<HumanEchoController>();
            HumanEchoUI ui = echoObject.AddComponent<HumanEchoUI>();

            SerializedObject cso = new SerializedObject(controller);
            SetReference(cso, "showcaseCamera", cam);
            SetReference(cso, "personFieldShader", personField);
            SetReference(cso, "smokeInjectShader", smokeInject);
            SetReference(cso, "smokeShader", smokeShader);
            SetReference(cso, "echoShader", echoShader);
            SetReference(cso, "outlineShader", outlineShader);
            SetReference(cso, "personShader", personShader);
            SetReference(cso, "particleCompute", particleCompute);
            SetReference(cso, "particleDrawShader", particleDrawShader);
            SetReferenceArray(cso, "looks", looks.ToArray());
            cso.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject uso = new SerializedObject(ui);
            SetReference(uso, "controller", controller);
            uso.ApplyModifiedPropertiesWithoutUndo();

            if (missing.Count > 0) {
                Debug.LogWarning("[HumanEcho] Scene built, but " + missing.Count + " reference(s) could not "
                                 + "be filled and the layers that need them will be off: "
                                 + string.Join(", ", missing));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ScenePath;
        }

        /// <summary>The three looks in cycling order, loaded again from disk (a new scene can unload what an
        /// earlier call returned).</summary>
        private static List<HumanEchoSettings> LoadLooks(List<string> missing) {
            List<HumanEchoSettings> looks = new List<HumanEchoSettings>();
            for (int i = 0; i < HumanEchoPresetBuilder.LookPaths.Length; i++) {
                string path = HumanEchoPresetBuilder.LookPaths[i];
                HumanEchoSettings look = AssetDatabase.LoadAssetAtPath<HumanEchoSettings>(path);
                if (look == null) {
                    Debug.LogWarning("[HumanEcho] Builder: the look " + path
                                     + " could not be loaded and is not in the scene's look list.");
                    missing.Add(path);
                } else {
                    looks.Add(look);
                }
            }
            return looks;
        }

        // A shader is looked up by its file in Shaders/ first, then by its declared name. There is no search
        // by partial name on purpose: "HumanEcho_Person" would also match HumanEcho_PersonField and wire the
        // wrong shader into a layer without any warning.
        private static Shader FindShader(string field, string[] fileNames, string[] shaderNames,
                                         List<string> missing) {
            for (int i = 0; i < fileNames.Length; i++) {
                string path = ShaderFolder + "/" + fileNames[i] + ".shader";
                Shader byPath = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (byPath != null) {
                    return byPath;
                }
            }
            for (int i = 0; i < shaderNames.Length; i++) {
                Shader byName = Shader.Find(shaderNames[i]);
                if (byName != null) {
                    return byName;
                }
            }
            Debug.LogWarning("[HumanEcho] Builder: no shader for '" + field + "' (looked for "
                             + ShaderFolder + "/" + fileNames[0] + ".shader and the shader name '"
                             + shaderNames[0] + "'); the layer that needs it will be off.");
            missing.Add(field);
            return null;
        }

        private static void SetReference(SerializedObject owner, string field, Object value) {
            SerializedProperty property = owner.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) {
                Debug.LogWarning("[HumanEcho] Builder: " + owner.targetObject.GetType().Name
                                 + " has no object reference field '" + field
                                 + "', so it was not wired (is the controller up to date?).");
                return;
            }
            property.objectReferenceValue = value;
        }

        private static void SetReferenceArray(SerializedObject owner, string field, Object[] values) {
            SerializedProperty property = owner.FindProperty(field);
            if (property == null || !property.isArray) {
                Debug.LogWarning("[HumanEcho] Builder: " + owner.targetObject.GetType().Name
                                 + " has no array field '" + field
                                 + "', so it was not wired (is the controller up to date?).");
                return;
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
