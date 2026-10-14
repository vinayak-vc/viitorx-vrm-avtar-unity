using UnityEngine;
using UnityEngine.Rendering;

namespace HumanEcho {
    /// <summary>
    /// Shared geometry for the layers that draw a flat picture-shaped quad in the orthographic view (the
    /// camera sits at z = -10 looking along +z,
    /// centred on the origin). Layers sort against each other by material render queue (set one per layer),
    /// not by depth.
    /// </summary>
    public static class LayerQuad {
        /// <summary>The Transparent queue is 3000 (the smoke container draws there); layers draw in this
        /// order, back to front.</summary>
        public const int QueueEcho = 3010;
        // Particles sit BEHIND the person: in the reference clips the figure stays readable in front of the
        // storm that leaves it.
        public const int QueueParticles = 3020;
        public const int QueuePerson = 3030;
        public const int QueueOutline = 3040;

        private const string MeshName = "HumanEcho Unit Quad";

        private static Mesh unitQuad;

        // The shared mesh is HideAndDontSave, so a domain reload (the Editor, on every Play) drops the static
        // but not the mesh. Destroy the
        // ones an earlier domain left behind, once per Play, so they do not pile up.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DestroyOrphanedMeshes() {
            unitQuad = null;
            Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            for (int i = 0; i < meshes.Length; i++) {
                if (meshes[i] != null && meshes[i].name == MeshName) {
                    Object.DestroyImmediate(meshes[i]);
                }
            }
        }

        /// <summary>A 1 x 1 quad centred on the origin in the xy plane, facing -z (toward the camera), uv
        /// 0..1. Shared; never destroy it.</summary>
        public static Mesh UnitQuad() {
            if (unitQuad != null) {
                return unitQuad;
            }
            Mesh mesh = new Mesh();
            mesh.name = MeshName;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = new Vector3[] {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            };
            mesh.uv = new Vector2[] {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.1f));
            unitQuad = mesh;
            return unitQuad;
        }

        /// <summary>
        /// Makes a child GameObject holding the unit quad, drawn with <paramref name="material"/> at
        /// <paramref name="renderQueue"/>. The caller owns
        /// the material (it must be a runtime copy) and destroys it with the layer.
        /// </summary>
        public static GameObject Create(string name, Transform parent, Material material, int renderQueue, out MeshRenderer renderer) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = UnitQuad();
            renderer = go.AddComponent<MeshRenderer>();
            material.renderQueue = renderQueue;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>
        /// The world size of a picture of <paramref name="pictureAspect"/> (width over height) fitted INSIDE
        /// the screen without stretching
        /// ("contain"): the picture touches two opposite edges and is centred.
        /// </summary>
        public static Vector2 ContainSize(float pictureAspect, float viewWidth, float viewHeight) {
            float aspect = Mathf.Max(0.0001f, pictureAspect);
            float viewAspect = viewWidth / Mathf.Max(0.0001f, viewHeight);
            if (aspect > viewAspect) {
                return new Vector2(viewWidth, viewWidth / aspect);
            }
            return new Vector2(viewHeight * aspect, viewHeight);
        }

        /// <summary>Scales and places a quad made by <see cref="Create"/>: centred on the origin at depth
        /// <paramref name="z"/>.</summary>
        public static void Place(Transform quad, Vector2 size, float z) {
            quad.localPosition = new Vector3(0f, 0f, z);
            quad.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
