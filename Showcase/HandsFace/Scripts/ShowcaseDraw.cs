using UnityEngine;

namespace HandsFaceShowcase {
    /// <summary>Materials and primitives for the sample scenes, made from code so the scenes carry no assets: every shape is a Unity primitive with an unlit colour.</summary>
    internal static class ShowcaseDraw {
        private static Texture2D white;

        /// <summary>A flat-coloured material: URP Unlit when the pipeline has it, else the sprite shader (which every project has and which also takes vertex colours).</summary>
        public static Material Unlit(Color color) {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) {
                shader = Shader.Find("Sprites/Default");
            }
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        /// <summary>A primitive without its collider, with an unlit material, parented to <paramref name="parent"/>.</summary>
        public static GameObject Primitive(PrimitiveType type, string objectName, Transform parent, Color color) {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) {
                Object.Destroy(collider);
            }
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Unlit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.transform.SetParent(parent, false);
            return go;
        }

        public static void SetColor(GameObject go, Color color) {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) {
                renderer.sharedMaterial.color = color;
            }
        }

        /// <summary>A line renderer with a sprite material, in local space of its parent.</summary>
        public static LineRenderer Line(string objectName, Transform parent, Color color, float width, int points) {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.sharedMaterial = Unlit(color);
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>A filled rectangle in GUI space, in <paramref name="color"/>.</summary>
        public static void Box(Rect rect, Color color) {
            if (white == null) {
                white = Texture2D.whiteTexture;
            }
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        /// <summary>A horizontal bar showing <paramref name="value01"/> of <paramref name="size"/>, over a dark track.</summary>
        public static void Bar(Rect rect, float value01, Color color) {
            Box(rect, new Color(0f, 0f, 0f, 0.55f));
            float clamped = Mathf.Clamp01(value01);
            if (clamped > 0f) {
                Box(new Rect(rect.x, rect.y, rect.width * clamped, rect.height), color);
            }
        }
    }
}
