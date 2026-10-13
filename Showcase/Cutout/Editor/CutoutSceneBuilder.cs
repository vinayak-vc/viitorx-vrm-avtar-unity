using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

using VirtualMirror.App;
using VirtualMirror.Vision;

namespace CutoutShowcase.EditorTools {
    /// <summary>
    /// Builds the Cutout showcase scene and its two materials from code, so the scene is reproducible and the wiring is reviewable: a camera, the
    /// AppBootstrap with the body tracking switched off (the same arrangement as the Hands and Face scenes) and the cutout stream on, and one object
    /// carrying the <see cref="CutoutView"/> and the <see cref="CutoutDemo"/>. Re-running it rewrites the scene and keeps the materials. It never
    /// changes Build Settings (run Virtual Mirror > Register All Scenes in Build Settings for that) or any project-wide setting.
    /// </summary>
    public static class CutoutSceneBuilder {
        public const string Root = "Assets/Games/viitorx-vrm-avtar-unity/Showcase/Cutout";
        public const string SceneName = "CutoutShowcase";
        public const string ScenePath = Root + "/Scenes/" + SceneName + ".unity";
        private const string PersonMaterialPath = Root + "/Materials/CutoutPerson.mat";
        private const string BackdropMaterialPath = Root + "/Materials/CutoutBackdrop.mat";

        [MenuItem("Showcase/Cutout/Build Showcase Scene")]
        public static void BuildFromMenu() {
            string path = BuildScene();
            Debug.Log("[Cutout] Scene built: " + path);
        }

        [MenuItem("Showcase/Cutout/Open Showcase Scene")]
        public static void OpenScene() {
            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            } else {
                Debug.LogWarning("[Cutout] The scene does not exist yet. Use Showcase > Cutout > Build Showcase Scene.");
            }
        }

        public static string BuildScene() {
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Scenes");
            LoadOrCreateMaterial(PersonMaterialPath, "VirtualMirror/Cutout");
            LoadOrCreateMaterial(BackdropMaterialPath, "VirtualMirror/CutoutBackdrop");
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = SceneName;

            // Creating a scene can unload assets that only this method referenced, so take fresh references to the persisted ones.
            Material personMaterial = AssetDatabase.LoadAssetAtPath<Material>(PersonMaterialPath);
            Material backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
            if (personMaterial == null || backdropMaterial == null) {
                Debug.LogError("[Cutout] Builder could not load its materials; nothing was wired.");
                return null;
            }

            GameObject cameraObject = new GameObject("Showcase Camera");
            cameraObject.tag = "MainCamera";
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.transform.position = Vector3.zero;
            cameraObject.AddComponent<AudioListener>();

            // TRACKING IS THE BOOTSTRAP'S JOB, exactly as in the Hands and Face scenes: the same AppBootstrap component, with the body path
            // switched off, serves the cutout stream and starts its sender. When the scene is reached through Bootstrap.unity instead, this one
            // destroys itself (AppBootstrap is a singleton) and the real one keeps going with its own settings.
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

            GameObject cutoutObject = new GameObject("Cutout");
            CutoutView view = cutoutObject.AddComponent<CutoutView>();
            SetRef(view, "personMaterial", personMaterial);
            SetRef(view, "backdropMaterial", backdropMaterial);
            SetRef(view, "viewCamera", cam);
            CutoutDemo demo = cutoutObject.AddComponent<CutoutDemo>();
            SetRef(demo, "view", view);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ScenePath;
        }

        private static void SetRef(Object target, string field, Object value) {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) {
                Debug.LogError("[Cutout] Builder: " + target.GetType().Name + " has no field '" + field + "'.");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string parent, string name) {
            string full = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(full)) {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName) {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) {
                Debug.LogError("[Cutout] Shader '" + shaderName + "' was not found; is it imported?");
                return null;
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            } else if (material.shader != shader) {
                material.shader = shader;
            }
            return material;
        }
    }
}
