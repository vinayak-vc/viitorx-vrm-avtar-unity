// Virtual Mirror — VRM rig import validator.
//
// Validates a .vrm file against the canonical rig contract the tracking/retarget
// pipeline requires (docs/27_CharacterRigSpec.md): VRM 1.0 spec version, the full
// Unity-Humanoid bone set the retarget binds (incl. all 30 finger bones), and the
// VRM expression presets VrmExpressionRetargeter drives.
//
// It parses the glb JSON chunk directly (the same VRMC_vrm data UniVRM reads at
// load), so it needs no async load and no UniVRM dependency — only Newtonsoft.
//
// Usage:
//   * Menu:  Virtual Mirror -> Validate VRM Rig (Pick File)...
//   * Select a .vrm asset in the Project window, then
//            Virtual Mirror -> Validate Selected VRM
//   * Programmatic: VrmRigValidator.Validate(path, out report).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VirtualMirror.Editor
{
    public static class VrmRigValidator
    {
        // --- The contract (must mirror docs/27_CharacterRigSpec.md) ---------- //

        // Bones the retarget/IK path binds and cannot degrade gracefully without.
        static readonly string[] RequiredBones =
        {
            "hips", "spine", "chest", "neck", "head",
            "leftUpperArm", "leftLowerArm", "leftHand",
            "rightUpperArm", "rightLowerArm", "rightHand",
            "leftUpperLeg", "leftLowerLeg", "leftFoot",
            "rightUpperLeg", "rightLowerLeg", "rightFoot",
        };

        // All 30 finger bones (OAK 21-pt hands + Kalidokit finger curl).
        static readonly string[] FingerBones = BuildFingerBones();

        // Strongly recommended; the pipeline uses them when present.
        static readonly string[] OptionalBones =
        {
            "upperChest", "leftShoulder", "rightShoulder",
            "leftToes", "rightToes", "leftEye", "rightEye",
        };

        // Expression presets VrmExpressionRetargeter writes (blendshapes required).
        static readonly string[] RequiredExpressions =
        {
            "blinkLeft", "blinkRight", "aa", "happy", "angry", "surprised",
        };

        // Recommended for future lip-sync; warn (not fail) if absent.
        static readonly string[] RecommendedExpressions =
        {
            "ih", "ou", "ee", "oh",
        };

        const uint GlbMagic = 0x46546C67;      // "glTF"
        const uint ChunkJson = 0x4E4F534A;     // "JSON"

        // --- Menu entry points ---------------------------------------------- //

        [MenuItem("Virtual Mirror/Validate VRM Rig (Pick File)...", priority = 100)]
        static void ValidatePicked()
        {
            string start = Application.streamingAssetsPath;
            string path = EditorUtility.OpenFilePanel("Select .vrm to validate", start, "vrm");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            RunAndReport(path);
        }

        [MenuItem("Virtual Mirror/Validate Selected VRM", priority = 101)]
        static void ValidateSelected()
        {
            UnityEngine.Object obj = Selection.activeObject;
            if (obj == null)
            {
                EditorUtility.DisplayDialog("VRM Rig Validator", "Select a .vrm asset in the Project window first.", "OK");
                return;
            }
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.ToLowerInvariant().EndsWith(".vrm"))
            {
                EditorUtility.DisplayDialog("VRM Rig Validator", "Selected asset is not a .vrm file.", "OK");
                return;
            }
            RunAndReport(Path.GetFullPath(assetPath));
        }

        [MenuItem("Virtual Mirror/Validate Selected VRM", validate = true)]
        static bool ValidateSelectedEnabled()
        {
            UnityEngine.Object obj = Selection.activeObject;
            if (obj == null)
            {
                return false;
            }
            string p = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(p) && p.ToLowerInvariant().EndsWith(".vrm");
        }

        static void RunAndReport(string path)
        {
            bool ok = Validate(path, out string report);
            if (ok)
            {
                Debug.Log("[VRM Rig Validator] PASS\n" + report);
            }
            else
            {
                Debug.LogError("[VRM Rig Validator] FAIL\n" + report);
            }
            EditorUtility.DisplayDialog("VRM Rig Validator — " + (ok ? "PASS" : "FAIL"), report, "OK");
        }

        // --- Core validation (public + testable) ---------------------------- //

        public static bool Validate(string vrmPath, out string report)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("File: " + vrmPath);

            if (!File.Exists(vrmPath))
            {
                report = "File not found: " + vrmPath;
                return false;
            }

            JObject gltf;
            try
            {
                gltf = ReadGltfJson(vrmPath);
            }
            catch (Exception e)
            {
                report = "Not a readable glb/vrm: " + e.Message;
                return false;
            }

            JToken vrm = gltf.SelectToken("extensions.VRMC_vrm");
            if (vrm == null)
            {
                report = sb.ToString() + "No VRMC_vrm extension — this is not a VRM 1.0 file (VRM 0.x is not supported by this pipeline).";
                return false;
            }

            bool pass = true;

            // 1. spec version
            string spec = (string)vrm.SelectToken("specVersion");
            bool specOk = spec != null && spec.StartsWith("1.");
            pass &= specOk;
            sb.AppendLine((specOk ? "[OK]  " : "[FAIL] ") + "specVersion = " + (spec ?? "<none>") + " (need 1.x)");

            // 2. humanoid bones
            JObject humanBones = vrm.SelectToken("humanoid.humanBones") as JObject;
            HashSet<string> present = humanBones != null
                ? new HashSet<string>(humanBones.Properties().Select(p => p.Name))
                : new HashSet<string>();

            List<string> missReq = RequiredBones.Where(b => !present.Contains(b)).ToList();
            List<string> missFing = FingerBones.Where(b => !present.Contains(b)).ToList();
            List<string> haveOpt = OptionalBones.Where(b => present.Contains(b)).ToList();
            List<string> missOpt = OptionalBones.Where(b => !present.Contains(b)).ToList();

            pass &= missReq.Count == 0;
            pass &= missFing.Count == 0;

            sb.AppendLine((missReq.Count == 0 ? "[OK]  " : "[FAIL] ") +
                          "core/limb bones (" + (RequiredBones.Length - missReq.Count) + "/" + RequiredBones.Length + ")" +
                          (missReq.Count > 0 ? "  MISSING: " + string.Join(", ", missReq) : ""));
            sb.AppendLine((missFing.Count == 0 ? "[OK]  " : "[FAIL] ") +
                          "finger bones (" + (FingerBones.Length - missFing.Count) + "/" + FingerBones.Length + ")" +
                          (missFing.Count > 0 ? "  MISSING: " + string.Join(", ", missFing) : ""));
            sb.AppendLine("[INFO] optional bones present: " + (haveOpt.Count > 0 ? string.Join(", ", haveOpt) : "none") +
                          (missOpt.Count > 0 ? "   (recommended, absent: " + string.Join(", ", missOpt) + ")" : ""));

            // 3. expressions
            JObject preset = vrm.SelectToken("expressions.preset") as JObject;
            Dictionary<string, int> binds = new Dictionary<string, int>();
            if (preset != null)
            {
                foreach (JProperty p in preset.Properties())
                {
                    JArray mtb = p.Value.SelectToken("morphTargetBinds") as JArray;
                    binds[p.Name] = mtb != null ? mtb.Count : 0;
                }
            }

            List<string> missExpr = RequiredExpressions
                .Where(e => !binds.ContainsKey(e) || binds[e] == 0)
                .ToList();
            List<string> missRec = RecommendedExpressions
                .Where(e => !binds.ContainsKey(e) || binds[e] == 0)
                .ToList();

            pass &= missExpr.Count == 0;
            sb.AppendLine((missExpr.Count == 0 ? "[OK]  " : "[FAIL] ") +
                          "required expressions (" + (RequiredExpressions.Length - missExpr.Count) + "/" + RequiredExpressions.Length + ")" +
                          (missExpr.Count > 0 ? "  MISSING/unbound: " + string.Join(", ", missExpr) : ""));
            if (missRec.Count > 0)
            {
                sb.AppendLine("[WARN] recommended visemes absent/unbound: " + string.Join(", ", missRec) + " (needed only for future lip-sync)");
            }

            // 4. spring bones (informational)
            bool hasSpring = gltf.SelectToken("extensions.VRMC_springBone") != null;
            sb.AppendLine("[INFO] spring bones: " + (hasSpring ? "present" : "none (fine unless the model has hair/cloth that should sway)"));

            sb.AppendLine();
            sb.AppendLine(pass ? "RESULT: PASS — model satisfies the Virtual Mirror rig contract."
                               : "RESULT: FAIL — fix the [FAIL] items above before using this avatar.");
            report = sb.ToString();
            return pass;
        }

        // --- helpers -------------------------------------------------------- //

        static JObject ReadGltfJson(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 12)
            {
                throw new Exception("file too small");
            }
            uint magic = BitConverter.ToUInt32(data, 0);
            if (magic != GlbMagic)
            {
                throw new Exception("bad glb magic");
            }
            uint length = BitConverter.ToUInt32(data, 8);
            int off = 12;
            while (off + 8 <= length && off + 8 <= data.Length)
            {
                uint chunkLen = BitConverter.ToUInt32(data, off);
                uint chunkType = BitConverter.ToUInt32(data, off + 4);
                int dataStart = off + 8;
                if (chunkType == ChunkJson)
                {
                    string json = Encoding.UTF8.GetString(data, dataStart, (int)chunkLen);
                    return JObject.Parse(json);
                }
                off = dataStart + (int)chunkLen;
            }
            throw new Exception("no JSON chunk");
        }

        static string[] BuildFingerBones()
        {
            List<string> list = new List<string>();
            string[] sides = { "left", "right" };
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string s in sides)
            {
                foreach (string f in fingers)
                {
                    // VRM 1.0 thumb = Metacarpal/Proximal/Distal; others Proximal/Intermediate/Distal.
                    string[] phalanges = f == "Thumb"
                        ? new[] { "Metacarpal", "Proximal", "Distal" }
                        : new[] { "Proximal", "Intermediate", "Distal" };
                    foreach (string p in phalanges)
                    {
                        list.Add(s + f + p);
                    }
                }
            }
            return list.ToArray();
        }
    }
}
