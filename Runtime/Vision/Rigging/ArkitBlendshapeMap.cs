using System;
using System.Collections.Generic;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Finds, for each of the 52 <see cref="ArkitBlendshapes"/>, the blendshape of a mesh that carries the same name, so a face stream can drive any
    /// character whose mesh was exported with ARKit-named shapes. Pure logic (names in, indexes out) so it is tested without a mesh.
    ///
    /// A mesh blendshape matches a name when it is equal ignoring case, or ends with the name after a separator: exporters commonly prefix the
    /// mesh or the shape ("Face.eyeBlinkLeft", "blendShape1.jawOpen", "Body_mouthSmileLeft"). A shape matches at most one ARKit name, the first.
    /// <c>mirror</c> swaps Left and Right (a mirror avatar facing the user), since the wire's sides are the subject's.
    /// </summary>
    public static class ArkitBlendshapeMap {
        public const int NoShape = -1;

        /// <summary>For each ARKit index, the index of the matching mesh blendshape or <see cref="NoShape"/>.</summary>
        public static int[] Build(IList<string> meshShapeNames, bool mirror) {
            int[] map = new int[ArkitBlendshapes.Count];
            int i = 0;
            while (i < map.Length) {
                map[i] = NoShape;
                i = i + 1;
            }
            if (meshShapeNames == null) {
                return map;
            }
            int shape = 0;
            while (shape < meshShapeNames.Count) {
                int arkit = Match(meshShapeNames[shape]);
                if (arkit > 0) {
                    int slot = mirror ? ArkitBlendshapes.Mirror(arkit) : arkit;
                    if (map[slot] == NoShape) {
                        map[slot] = shape;
                    }
                }
                shape = shape + 1;
            }
            return map;
        }

        /// <summary>How many ARKit blendshapes found a shape in the map.</summary>
        public static int CountMapped(int[] map) {
            int count = 0;
            int i = 0;
            while (map != null && i < map.Length) {
                if (map[i] != NoShape) {
                    count = count + 1;
                }
                i = i + 1;
            }
            return count;
        }

        /// <summary>The ARKit index a mesh blendshape name stands for, or -1. Index 0 (<c>_neutral</c>) is never matched: it is not a shape a mesh has.</summary>
        public static int Match(string meshShapeName) {
            if (string.IsNullOrEmpty(meshShapeName)) {
                return -1;
            }
            int i = 1;
            while (i < ArkitBlendshapes.Count) {
                string name = ArkitBlendshapes.Names[i];
                if (string.Equals(meshShapeName, name, StringComparison.OrdinalIgnoreCase) || EndsWithAfterSeparator(meshShapeName, name)) {
                    return i;
                }
                i = i + 1;
            }
            return -1;
        }

        private static bool EndsWithAfterSeparator(string meshShapeName, string name) {
            int cut = meshShapeName.Length - name.Length - 1;
            if (cut < 0) {
                return false;
            }
            char separator = meshShapeName[cut];
            if (separator != '.' && separator != '_' && separator != '-' && separator != ' ' && separator != ':') {
                return false;
            }
            return string.Compare(meshShapeName, cut + 1, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
