using Fluxy;

using UnityEditor;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ChromaticRain.EditorTools {
    /// <summary>
    /// Editor helpers every showcase scene builder shares: asset loading and creation, private copies of
    /// FluXY's materials (so FluXY's own assets are never touched), serialized wiring, and the mild
    /// post-processing profile. Nothing here knows about one particular showcase.
    /// </summary>
    public static class ShowcaseBuilderUtil {
        public const string FluxySimulationMaterial = "Assets/FluXY/Resources/Materials/FluidSimulation.mat";
        public const string FluxySplatMaterial = "Assets/FluXY/Resources/Materials/Splat.mat";
        public const string FluxySplatTexture = "Assets/FluXY/Resources/Textures/SplatTexture.psd";
        public const string FluxyNoiseTexture = "Assets/FluXY/Resources/Textures/NoiseTexture.psd";

        public static void SetRef(Object target, string field, Object value) {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) {
                Debug.LogError("[Showcase] Builder: " + target.GetType().Name + " has no field '" + field + "'.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        public static void EnsureFolders(string root, string[] folders) {
            for (int i = 0; i < folders.Length; i++) {
                string full = root + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(full)) {
                    AssetDatabase.CreateFolder(root, folders[i]);
                }
            }
        }

        public static Material LoadOrCreateMaterial(string root, string name, string shaderName) {
            string path = root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null) {
                Debug.LogError("[Showcase] Shader '" + shaderName + "' was not found; is it imported?");
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
        public static Material CopyMaterial(string root, string sourcePath, string name) {
            string path = root + "/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) {
                return existing;
            }
            Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) {
                Debug.LogError("[Showcase] FluXY material not found at " + sourcePath);
                return null;
            }
            Material copy = new Material(source);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        public static FluxyTarget CreateTarget(Transform parent, string name) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            FluxyTarget target = go.AddComponent<FluxyTarget>();
            target.enabled = false;
            return target;
        }

        /// <summary>A global post-processing profile with one restrained Bloom: only the brightest cores
        /// glow, no haze. The scenes stay legible without it.</summary>
        public static VolumeProfile LoadOrCreateBloomProfile(string path, float threshold, float intensity, float scatter) {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Bloom bloom;
            if (!profile.TryGet(out bloom)) {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = threshold;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = intensity;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = scatter;
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
