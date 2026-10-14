using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// The few object-lifetime helpers the echo, outline and person layers share (they each create runtime
    /// material copies and render
    /// textures and must destroy them the way the existing layers do: Destroy while playing, DestroyImmediate
    /// in edit mode).
    /// </summary>
    internal static class LayerResources {
        /// <summary>A runtime copy of <paramref name="shader"/>'s material: never saved, destroyed by the
        /// layer that asked for it.</summary>
        public static Material CreateMaterial(Shader shader, string name) {
            Material material = new Material(shader);
            material.name = name;
            material.hideFlags = HideFlags.HideAndDontSave;
            return material;
        }

        public static void DestroyObject(UnityEngine.Object value) {
            if (value == null) {
                return;
            }
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(value);
            } else {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        public static void Release(ref RenderTexture texture) {
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

        /// <summary>
        /// Checks that a shader can be used. Logs ONE error naming <paramref name="owner"/> and returns false
        /// when it is missing or does
        /// not compile for this GPU / graphics API, so the layer can stay off and the others carry on.
        /// </summary>
        public static bool ShaderUsable(Shader shader, string owner, string shaderName) {
            if (shader == null) {
                Debug.LogError("[HumanEcho] The " + owner + " layer has no shader (" + shaderName + "); it stays off.");
                return false;
            }
            if (!shader.isSupported) {
                Debug.LogError("[HumanEcho] The shader " + shaderName + " does not compile for this graphics device (see the shader's "
                               + "Inspector for the reason); the " + owner + " layer stays off.");
                return false;
            }
            return true;
        }
    }
}
