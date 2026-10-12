using System.Text;

using ShowcaseShared;

using Joint = ShowcaseShared.Joint;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Optional development overlay (key D, or the small "debug" control). It is off in the presentation.
    ///
    /// It draws, over the fluid: the tracked position of each interaction point with its velocity vector,
    /// and the FluXY target positions that were actually mapped (a ring where a target is injecting). It
    /// lists the input source, the state, the filtered movement strengths, which landmarks are fresh,
    /// held or missing, and whether the Container and Solver are configured correctly.
    /// The text is rebuilt five times a second, not every frame.
    /// </summary>
    public sealed class FluidDisruptionDebugView : MonoBehaviour {
        private const float VectorSeconds = 0.35f;
        private const float PanelTop = 168f;

        [SerializeField] private FluidDisruptionController controller;

        private readonly StringBuilder text = new StringBuilder(1024);
        private GUIStyle style;
        private GUIStyle boxStyle;
        private string cached = string.Empty;
        private float refreshAt;

        private void OnGUI() {
            if (controller == null || controller.Settings == null) {
                return;
            }
            if (style == null) {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = 12;
                style.richText = false;
                style.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
                boxStyle = new GUIStyle(GUI.skin.box);
            }
            if (Time.unscaledTime >= refreshAt) {
                refreshAt = Time.unscaledTime + 0.2f;
                Rebuild();
            }

            DrawPoints();

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            // Below the bootstrap's own status lights, which occupy the top-right corner.
            GUI.Box(new Rect(Screen.width - 468f, PanelTop, 458f, 266f), GUIContent.none, boxStyle);
            GUI.color = previous;
            GUI.Label(new Rect(Screen.width - 460f, PanelTop + 4f, 446f, 260f), cached, style);
        }

        private void DrawPoints() {
            ScreenSpaceMapper mapper = controller.Mapper;
            for (int slot = 0; slot < controller.PersonSlotCount; slot++) {
                DisruptionPerson person = controller.PersonAt(slot);
                if (!person.InUse) {
                    continue;
                }
                DrawPoint(person.Points.Left, new Color(0.2f, 0.9f, 1f, 1f), mapper);
                DrawPoint(person.Points.Right, new Color(0.7f, 0.5f, 1f, 1f), mapper);
                DrawPoint(person.Points.Torso, new Color(1f, 0.85f, 0.3f, 1f), mapper);
            }

            // Where the FluXY targets really are (a ring), so a mapping error shows up as a gap.
            int targetCount = controller.Targets != null ? controller.Targets.SlotCount * FluidDisruptionTargetDriver.PointsPerPerson : 0;
            for (int i = 0; i < targetCount; i++) {
                Vector2 p;
                float strength;
                if (controller.Targets != null && controller.Targets.TryGetTarget(i, out p, out strength)) {
                    Vector2 g = mapper.ToGui(p, Screen.width, Screen.height);
                    float r = 10f + 26f * strength;
                    DrawRing(g, r, Color.white);
                }
            }
        }

        private void DrawPoint(MovementImpulseEstimator est, Color colour, ScreenSpaceMapper mapper) {
            if (!est.HasSample) {
                return;
            }
            Vector2 g = mapper.ToGui(est.Position, Screen.width, Screen.height);
            Vector2 tip = mapper.ToGui(est.Position + est.Velocity * VectorSeconds, Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(g.x - 4f, g.y - 4f, 8f, 8f), Texture2D.whiteTexture);
            DrawLine(g, tip, 2f);
            GUI.color = previous;
        }

        private static void DrawRing(Vector2 centre, float radius, Color colour) {
            Color previous = GUI.color;
            GUI.color = colour;
            const int segments = 20;
            Vector2 last = centre + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++) {
                float a = i * 2f * Mathf.PI / segments;
                Vector2 next = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                DrawLine(last, next, 1.5f);
                last = next;
            }
            GUI.color = previous;
        }

        private static void DrawLine(Vector2 a, Vector2 b, float width) {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) {
                return;
            }
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = saved;
        }

        private void Rebuild() {
            text.Length = 0;
            FluidDisruptionController c = controller;
            FluidDisruptionDemoPoseSource demo = c.Demo;

            text.Append("INPUT   ").Append(c.Signal).Append("   mode ").Append(c.Source)
                .Append(c.MirrorX ? "   mirror" : "   no mirror").Append('\n');
            if (c.SimulationActive) {
                text.Append("DEMO    ").Append(demo.SegmentName).Append("  ")
                    .Append(demo.SegmentTime.ToString("0.0")).Append("s")
                    .Append(demo.IsHolding ? "  (held)" : string.Empty)
                    .Append(demo.playProgramme ? string.Empty : "  (paused)")
                    .Append(c.StreamCut ? "  STREAM CUT" : string.Empty).Append('\n');
            } else {
                text.Append("LIVE    provider ").Append(c.ProviderState).Append("  fresh ")
                    .Append(c.StreamFresh ? "yes" : "NO").Append("  packets ").Append(c.PacketsReceived)
                    .Append("  last ").Append(c.SecondsSinceAccepted.ToString("0.00")).Append("s")
                    .Append(string.IsNullOrEmpty(c.LiveSourceLabel) ? string.Empty : "  " + c.LiveSourceLabel)
                    .Append('\n');
            }
            text.Append("PEOPLE  ").Append(c.TrackedPeopleCount).Append(" tracked of ").Append(c.PersonSlotCount)
                .Append(" slots (first one in use is listed below)").Append('\n');
            text.Append("STATE   ").Append(c.Machine.State).Append("   gate ").Append(c.Machine.Gate.ToString("0.00"))
                .Append("   pulse ").Append(c.Machine.Pulse.ToString("0.00")).Append('\n');
            AppendPoint("LEFT    ", c.LeftHand, Joint.LeftWrist, 0);
            AppendPoint("RIGHT   ", c.RightHand, Joint.RightWrist, 1);
            AppendPoint("TORSO   ", c.Torso, -1, 2);

            FluidDisruptionFluid f = c.Fluid;
            string problems = f != null ? f.DescribeProblems() : "no fluid component";
            text.Append("FLUXY   ").Append(c.SimulationOn ? "on" : "OFF").Append("  ").Append(c.Quality)
                .Append("  velocity ").Append(f != null ? f.VelocityResolution : 0)
                .Append("  dye ").Append(f != null ? f.DyeResolution : 0)
                .Append("  enhance ").Append(c.ShaderEnhancements ? "on" : "off").Append('\n');
            text.Append("CONFIG  ").Append(string.IsNullOrEmpty(problems) ? "container + solver OK" : problems).Append('\n');
            text.Append("FRAME   ").Append(c.FrameMilliseconds.ToString("0.0")).Append(" ms  (")
                .Append((1000f / Mathf.Max(0.1f, c.FrameMilliseconds)).ToString("0")).Append(" fps)").Append('\n');
            text.Append('\n');
            text.Append("keys: S source  M mirror  Q quality  E enhance  F fluid  C reset  R recenter  H text\n");
            text.Append("      Space pause demo  , . prev/next segment  / run  P cut stream  L K lose hand L/R");
            cached = text.ToString();
        }

        private void AppendPoint(string label, MovementImpulseEstimator est, int wrist, int index) {
            ShowcaseBody body = controller.Body;
            string landmark;
            if (wrist < 0) {
                landmark = body.CoreFresh ? "fresh" : "NOT MEASURED";
            } else if (body.Fresh[wrist]) {
                landmark = "fresh";
            } else if (body.Weight[wrist] > 0.05f) {
                landmark = "STALE (held)";
            } else {
                landmark = "MISSING";
            }
            Vector2 target;
            float strength;
            bool injecting = controller.Targets != null
                             && controller.Targets.TryGetTarget(controller.PrimarySlot * FluidDisruptionTargetDriver.PointsPerPerson + index,
                                                                out target, out strength);
            text.Append(label).Append(landmark);
            if (est.HasSample) {
                text.Append("  pos ").Append(est.Position.x.ToString("0.00")).Append(',').Append(est.Position.y.ToString("0.00"))
                    .Append("  speed ").Append(est.Speed.ToString("0.00"))
                    .Append("  strength ").Append(est.Intensity.ToString("0.00"));
            } else {
                text.Append("  no track");
            }
            text.Append(injecting ? "  target ON" : "  target off").Append('\n');
        }
    }
}
