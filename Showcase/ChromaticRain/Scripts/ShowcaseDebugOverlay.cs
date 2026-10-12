using System.Text;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace ChromaticRain {
    /// <summary>
    /// Optional debug view; off in the normal presentation. Draws the smoothed landmarks (green =
    /// measured this frame, orange = held, faded by weight), the bones, the L/R hand labels, the
    /// FluXY target positions, a preview of the mask texture, and a text block with the input
    /// source and the freshness figures the provider actually exposes.
    ///
    /// Use it to spot coordinate errors (a joint far from its limb), gaps between connected limbs
    /// and left/right flips. Keys while it is up: 1-4 arm pattern, P stream on/off, T still/travel,
    /// K hands on/off, L left arm joints lost, J right leg joints lost.
    /// </summary>
    public sealed class ShowcaseDebugOverlay : MonoBehaviour {
        private static readonly Color Measured = new Color(0.3f, 1f, 0.5f, 1f);
        private static readonly Color Held = new Color(1f, 0.6f, 0.15f, 1f);
        private static readonly Color Bone = new Color(0.6f, 0.8f, 1f, 0.55f);

        [SerializeField] private ChromaticRainShowcase showcase;

        private readonly StringBuilder text = new StringBuilder(1024);
        private GUIStyle labelStyle;
        private GUIStyle infoStyle;
        private float fpsSmoothed = 60f;
        private ShowcasePerformanceProbe probe;

        private void OnGUI() {
            if (showcase == null || showcase.Settings == null) {
                return;
            }
            if (labelStyle == null) {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = 14;
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.normal.textColor = Color.white;
                infoStyle = new GUIStyle(GUI.skin.label);
                infoStyle.fontSize = 12;
                infoStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
                infoStyle.richText = false;
            }
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime), 0.05f);
            if (probe == null) {
                probe = showcase.GetComponent<ShowcasePerformanceProbe>();
            }

            DrawSkeleton();
            DrawHands();
            if (!showcase.DebugVisible) {
                // Only the landmarks view of the silhouette debug is up: bones and joints over the shape, no text.
                return;
            }
            DrawTargets();
            DrawMaskPreview();
            DrawInfo();
        }

        private Vector2 ToGui(Vector2 view) {
            return showcase.Mapper.ToGui(view, Screen.width, Screen.height);
        }

        private void DrawSkeleton() {
            for (int p = 0; p < showcase.PersonSlotCount; p++) {
                ShowcasePerson person = showcase.PersonAt(p);
                if (person.InUse) {
                    DrawSkeleton(person.Body);
                }
            }
        }

        private void DrawSkeleton(ShowcaseBody body) {
            int[] bones = SkeletonPose.Bones;
            for (int i = 0; i + 1 < bones.Length; i += 2) {
                int a = bones[i];
                int b = bones[i + 1];
                if (body.Weight[a] < 0.05f || body.Weight[b] < 0.05f) {
                    continue;
                }
                float w = Mathf.Min(body.Weight[a], body.Weight[b]);
                Color c = Bone;
                c.a *= w;
                Line(ToGui(body.Position[a]), ToGui(body.Position[b]), 2f, c);
            }
            for (int j = 0; j < Joint.BodyCount; j++) {
                if (body.Weight[j] < 0.05f) {
                    continue;
                }
                Color c = body.Fresh[j] ? Measured : Held;
                c.a = Mathf.Clamp01(0.3f + body.Weight[j]);
                Vector2 g = ToGui(body.Position[j]);
                Dot(g, 7f, c);
            }
        }

        private void DrawHands() {
            for (int p = 0; p < showcase.PersonSlotCount; p++) {
                ShowcasePerson person = showcase.PersonAt(p);
                if (person.InUse) {
                    DrawHands(person.Body);
                }
            }
        }

        private void DrawHands(ShowcaseBody body) {
            LabelAt(Joint.LeftWrist, "L", body);
            LabelAt(Joint.RightWrist, "R", body);
            for (int slot = Joint.LeftHandEnd; slot <= Joint.RightHandEnd; slot++) {
                if (body.Weight[slot] > 0.05f) {
                    Dot(ToGui(body.Position[slot]), 6f, body.Fresh[slot] ? Measured : Held);
                }
            }
        }

        private void LabelAt(int joint, string label, ShowcaseBody body) {
            if (body.Weight[joint] < 0.05f) {
                return;
            }
            Vector2 g = ToGui(body.Position[joint]);
            GUI.Label(new Rect(g.x + 8f, g.y - 22f, 30f, 24f), label, labelStyle);
        }

        private void DrawTargets() {
            FluidLayer fluid = showcase.Fluid;
            if (fluid == null || !fluid.IsOn) {
                return;
            }
            for (int i = 0; i < fluid.TargetCount; i++) {
                Vector2 p;
                if (fluid.TryGetTargetPosition(i, out p)) {
                    Vector2 g = ToGui(p);
                    Color c = new Color(1f, 0.2f, 0.8f, 0.9f);
                    Line(g + new Vector2(-12f, 0f), g + new Vector2(12f, 0f), 2f, c);
                    Line(g + new Vector2(0f, -12f), g + new Vector2(0f, 12f), 2f, c);
                }
            }
        }

        private void DrawMaskPreview() {
            Texture mask = showcase.MaskRenderer != null ? showcase.MaskRenderer.Mask : null;
            if (mask == null) {
                return;
            }
            float w = 256f;
            float h = w * mask.height / Mathf.Max(1f, mask.width);
            Rect r = new Rect(Screen.width - w - 12f, 12f, w, h);
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture,
                            ScaleMode.StretchToFill, false, 0f, new Color(1f, 1f, 1f, 0.25f), 0f, 0f);
            GUI.DrawTexture(r, mask, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(r.x, r.yMax + 2f, w, 20f), "mask " + mask.width + "x" + mask.height + " "
                      + showcase.MaskRenderer.FormatName, infoStyle);

            FluidLayer fluid = showcase.Fluid;
            if (fluid == null) {
                return;
            }
            Texture state = fluid.StateTexture;
            Texture velocity = fluid.VelocityTexture;
            float top = r.yMax + 24f;
            float half = (w - 6f) * 0.5f;
            if (state != null) {
                GUI.DrawTexture(new Rect(r.x, top, half, half), state, ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(r.x, top + half, half, 20f), "fluid dye", infoStyle);
            }
            if (velocity != null) {
                GUI.DrawTexture(new Rect(r.x + half + 6f, top, half, half), velocity, ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(r.x + half + 6f, top + half, half, 20f), "fluid velocity", infoStyle);
            }
        }

        private void DrawInfo() {
            ChromaticRainShowcase s = showcase;
            text.Length = 0;
            text.Append("input   ").Append(s.Source).Append(s.SimulationActive ? "  (simulation running)" : "  (live)").Append('\n');
            text.Append("signal  ").Append(s.Signal).Append("   provider ").Append(s.ProviderState)
                .Append("   age ").Append(s.SecondsSinceAccepted < 0.0 ? "-" : (s.SecondsSinceAccepted * 1000.0).ToString("F0") + " ms").Append('\n');
            text.Append("packets ").Append(s.PacketsReceived).Append("   ownership ").Append(s.OwnershipState).Append('\n');
            text.Append("phase   ").Append(s.Machine.Phase).Append("   alpha ").Append(s.Machine.SilhouetteAlpha.ToString("F2"))
                .Append("   fluid ").Append(s.Machine.FluidActivity.ToString("F2")).Append('\n');
            text.Append("people  ").Append(s.TrackedPeopleCount).Append(" tracked of ").Append(s.PersonSlotCount)
                .Append(" slots (this line: person 1)").Append('\n');
            text.Append("body    core ").Append(s.Body.CoreFresh ? "yes" : "no")
                .Append(s.Body.RootEstimated ? " (hips not in the picture: root estimated)" : string.Empty).Append("   energy ")
                .Append(s.Body.Energy.ToString("F2")).Append("   shoulders ").Append(s.Body.ShoulderWidth.ToString("F2")).Append(" m")
                .Append("   caps ").Append(s.Builder.CapCount).Append("   width x").Append(s.Builder.WidthScale.ToString("F2")).Append('\n');
            text.Append("mirror  ").Append(s.MirrorX ? "ON (like a mirror)" : "off").Append("   mode ").Append(s.Mode)
                .Append("   fluid layer ").Append(s.FluidOn ? "on" : "off").Append('\n');
            text.Append(fpsSmoothed.ToString("F0")).Append(" fps");
            float avg;
            float p95;
            float worst;
            float gc;
            if (probe != null && probe.TryGetStats(out avg, out p95, out worst, out gc)) {
                text.Append("   frame avg ").Append(avg.ToString("F1")).Append(" ms  p95 ").Append(p95.ToString("F1"))
                    .Append("  max ").Append(worst.ToString("F1")).Append("   GC ").Append(gc.ToString("F0")).Append(" B/frame");
            }
            text.Append('\n');
            text.Append("keys  S source  N mode  B skin (").Append(s.ActiveSkin.name).Append(")  V silhouette view (").Append(s.DebugView).Append(")  F fluid  M mirror  R recenter  C clear fluid  D debug  H ui");
            GUI.Label(new Rect(18f, 66f, 900f, 180f), text.ToString(), infoStyle);
        }

        private static void Dot(Vector2 centre, float size, Color c) {
            Color previous = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Line(Vector2 a, Vector2 b, float width, Color c) {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) {
                return;
            }
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }
    }
}
