using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 1 — AIR GRAFFITI. Pinch and draw. The stroke stays in the air where you left
    /// it, as a real object in 3D, so walking around it shows it has depth.
    ///
    /// WHY THIS ONE FIRST. It is the cheapest demonstration of the thing nobody expects from a
    /// skeleton demo — that the fingers are tracked individually — and it is the one people
    /// understand without being told. It also has no fail state, no timer and no score, so a visitor
    /// cannot lose, which matters for the first thing anyone tries.
    ///
    /// PINCH, NOT "HAND UP", is the trigger for a specific reason: a draw gesture needs an
    /// unambiguous start and stop, and anything based on position alone (above the shoulder, in a
    /// zone) leaves a trail of accidental marks every time the person moves between strokes. Pinch
    /// has a clean edge, is already hysteretic in <see cref="HandPose"/>, and is scale-invariant so
    /// it does not change meaning as the person moves nearer or further.
    ///
    /// THE INK IS THE INDEX FINGERTIP, not the wrist. It is the point a person is actually aiming
    /// with, and using the wrist makes the line lag behind the gesture by a hand's length, which
    /// reads as the system being slow rather than as a different anchor point.
    /// </summary>
    public sealed class AirGraffitiExperience : ExperienceBase {
        /// <summary>Index fingertip — the point the person is pointing with.</summary>
        private const int InkTip = 8;

        /// <summary>Minimum travel before a new point is added. Without it a still hand piles
        /// thousands of coincident points into the mesh and the stroke stops being smooth.</summary>
        private const float MinSegment = 0.012f;

        /// <summary>Points per stroke and strokes kept. Old strokes are retired oldest-first so a
        /// long session cannot grow without bound - this runs unattended in a room.</summary>
        private const int MaxPointsPerStroke = 400;
        private const int MaxStrokes = 24;

        private BodyRenderer body;
        private Transform strokeRoot;
        private readonly List<Stroke> strokes = new List<Stroke>();
        private Stroke leftStroke;
        private Stroke rightStroke;
        private int colourCycle;

        // A stroke in progress or finished. One LineRenderer each: strokes are drawn once and then
        // only ever appended to, so there is nothing to gain from batching them.
        private sealed class Stroke {
            public LineRenderer Line;
            public Material Material;
            public int Count;
            public Vector3 Last;
        }

        public override string Title {
            get {
                return "AIR GRAFFITI";
            }
        }

        public override string Instruction {
            get {
                return "Pinch your thumb and finger together, then draw. Let go to stop.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      C clear";
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root, withHands: true);
            body.Dim = 0.5f;   // present, but the drawing is the subject
            strokeRoot = new GameObject("Strokes").transform;
            strokeRoot.SetParent(root, false);
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.C)) {
                ClearStrokes();
            }
        }

        protected override void ResetExperience() {
            ClearStrokes();
        }

        private void ClearStrokes() {
            int i = 0;
            while (i < strokes.Count) {
                if (strokes[i].Line != null) {
                    Object.Destroy(strokes[i].Line.gameObject);
                }
                i = i + 1;
            }
            strokes.Clear();
            leftStroke = null;
            rightStroke = null;
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            body.RenderHands(Stage.Left, Stage.Right);

            // Drawing requires a REAL hand. The attract figure has no hand landmarks at all, so it
            // cannot draw and does not need to be excluded separately - but a hand that failed the
            // plausibility gate must be excluded, or an out-of-range subject scribbles at random.
            leftStroke = Advance(Stage.Left, leftStroke);
            rightStroke = Advance(Stage.Right, rightStroke);
        }

        // Extend, start or finish one hand's stroke. Returns the stroke still in progress, or null.
        private Stroke Advance(HandPose hand, Stroke current) {
            bool drawing = hand != null && hand.Valid && hand.Pinching;
            if (!drawing) {
                return null;   // releasing the pinch ends the stroke; the next one starts fresh
            }

            Vector3 tip = hand.World[InkTip];
            if (current == null) {
                current = BeginStroke(tip);
                Audio.Play(SoundCue.Grab, colourCycle, 0.4f);
                return current;
            }
            if (Vector3.Distance(tip, current.Last) < MinSegment) {
                return current;
            }
            if (current.Count >= MaxPointsPerStroke) {
                // Cap reached: start a new stroke from here rather than silently stopping. The person
                // is still pinching and still moving, and a line that just quits looks broken.
                return BeginStroke(tip);
            }
            current.Line.positionCount = current.Count + 1;
            current.Line.SetPosition(current.Count, tip);
            current.Count = current.Count + 1;
            current.Last = tip;
            return current;
        }

        private Stroke BeginStroke(Vector3 start) {
            // Each stroke takes the next colour around the palette, so consecutive strokes are
            // distinguishable and a drawing built from several reads as several.
            Color colour = StrokeColour(colourCycle);
            colourCycle = colourCycle + 1;

            Material material = Palette.NewAdditive(colour);
            LineRenderer line = ShowStage.Line(strokeRoot, "stroke_" + colourCycle, 0.014f, material);
            line.positionCount = 1;
            line.SetPosition(0, start);
            line.numCornerVertices = 4;

            Stroke stroke = new Stroke();
            stroke.Line = line;
            stroke.Material = material;
            stroke.Count = 1;
            stroke.Last = start;
            strokes.Add(stroke);

            while (strokes.Count > MaxStrokes) {
                if (strokes[0].Line != null) {
                    Object.Destroy(strokes[0].Line.gameObject);
                }
                strokes.RemoveAt(0);
            }
            return stroke;
        }

        // Cycles the show's own three colours rather than an arbitrary rainbow, so the drawing still
        // belongs to the same piece of work as the modes next door.
        private Color StrokeColour(int index) {
            int n = index % 3;
            if (n == 0) {
                return Palette.Warm;
            }
            if (n == 1) {
                return Palette.Hot;
            }
            return new Color(1.6f, 1.9f, 0.5f, 1f);
        }

        protected override string StatusText() {
            string left = HandState(Stage.Left, "LEFT");
            string right = HandState(Stage.Right, "RIGHT");
            return left + "\n" + right + "\nstrokes " + strokes.Count + " / " + MaxStrokes;
        }

        private static string HandState(HandPose hand, string label) {
            if (hand == null || (!hand.Valid && !hand.Implausible)) {
                return label + "   no hand tracked";
            }
            if (hand.Implausible) {
                // Named rather than hidden. Roughly 40% of hand observations fail this gate on the
                // crowded regression clip (F-32), and some fraction will at genuine distance too;
                // either way "step closer" is a far more useful thing to tell a visitor than a hand
                // that simply never draws.
                return label + "   OUT OF RANGE - step closer to the camera";
            }
            return label + "   " + (hand.Pinching ? "DRAWING" : "ready - pinch to draw")
                   + "   pinch " + hand.PinchNormalised.ToString("F2");
        }
    }
}
