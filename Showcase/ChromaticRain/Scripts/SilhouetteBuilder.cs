using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// Turns a <see cref="ShowcaseBody"/> into the shape list the mask shader draws, and the shader melts the shapes
    /// into ONE filled outline with a smooth minimum. The shape types are:
    /// <list type="bullet">
    /// <item>a round-cornered TRAPEZOID (chest, pelvis): two half-widths and a length, so the torso is a real
    /// shoulders-waist-hips outline and not a tube;</item>
    /// <item>a TAPERED CAPSULE (neck, hands, feet) and, chained, a smooth variable-width STROKE (arms and legs), so a
    /// limb narrows from the shoulder to the wrist and bulges at the calf without beads or kinks;</item>
    /// <item>one ELLIPSE for the head.</item>
    /// </list>
    /// The landmarks are JOINTS, not the outside of the body, so every width comes from <see cref="ChromaticRainSettings"/>
    /// (anatomical half-widths) and is scaled by the measured shoulder width: the figure keeps its proportions for a small
    /// or a large person, and the chest follows the person's real shoulder span.
    ///
    /// A part is drawn only while both of its ends have weight, its strength follows the weaker end, and its length is
    /// clamped to a believable limb, so a bad joint can neither spike nor connect to the origin. Pure logic, reused
    /// buffers, no allocation per frame.
    /// </summary>
    public sealed class SilhouetteBuilder {
        public const int MaxCaps = 64;

        /// <summary>Per shape: a.x, a.y, b.x, b.y. A capsule runs from a to b; a trapezoid from its base a to its top b.</summary>
        public readonly Vector4[] Caps = new Vector4[MaxCaps];

        /// <summary>Per shape: radius (or half-width) at a, at b, weight, kind. Kind 0 is a capsule that starts a new group;
        /// -1 is a capsule that CONTINUES the previous group (the pieces of one limb, joined by a hard union so the limb's
        /// outline is clean); a value above 0.5 is a rounded trapezoid whose corner rounding is (kind - 1) metres. Groups are
        /// melted into each other with the smooth minimum, pieces within a group are not.</summary>
        public readonly Vector4[] CapRadii = new Vector4[MaxCaps];

        /// <summary>Head ellipse: centre x, centre y, radius x, radius y.</summary>
        public Vector4 Head;

        /// <summary>Head up-direction x, y, weight, unused.</summary>
        public Vector4 HeadDirection = new Vector4(0f, 1f, 0f, 0f);

        public int CapCount { get; private set; }

        public float WidthScale { get; private set; }

        private const float MinWeight = 0.002f;

        /// <summary>A leg joint below this weight is not in the picture.</summary>
        private const float LegAbsentWeight = 0.05f;

        /// <summary>How far below the hips a cut-off body is continued, view metres: past the bottom of any view
        /// (the default one is 2.8 m tall and the hips sit near its middle).</summary>
        private const float CutOffExtensionMetres = 3.0f;

        /// <summary>Capsules per limb segment: more is smoother, and the budget is <see cref="MaxCaps"/>.</summary>
        private const int StrokeSteps = 4;

        // Group ids of the limbs. Pieces added with the same id one after the other form one group.
        private const int GroupLeftArm = 1;
        private const int GroupRightArm = 2;
        private const int GroupLeftLeg = 3;
        private const int GroupRightLeg = 4;

        private int lastGroup;

        public SilhouetteBuilder() {
            WidthScale = 1f;
        }

        public void Reset() {
            CapCount = 0;
            lastGroup = 0;
            HeadDirection.z = 0f;
            WidthScale = 1f;
        }

        public void Build(ShowcaseBody b, SilhouetteSkin s, float dt) {
            CapCount = 0;
            lastGroup = 0;
            HeadDirection.z = 0f;
            UpdateWidthScale(b, s, dt);
            float ws = WidthScale;

            float w11 = b.Weight[Joint.LeftShoulder];
            float w12 = b.Weight[Joint.RightShoulder];
            float w23 = b.Weight[Joint.LeftHip];
            float w24 = b.Weight[Joint.RightHip];
            Vector2 p11 = b.Position[Joint.LeftShoulder];
            Vector2 p12 = b.Position[Joint.RightShoulder];
            Vector2 p23 = b.Position[Joint.LeftHip];
            Vector2 p24 = b.Position[Joint.RightHip];

            bool shoulders = w11 > MinWeight && w12 > MinWeight;
            float shoulderW = Mathf.Min(w11, w12);
            Vector2 shoulderMid = (p11 + p12) * 0.5f;

            // Hips: both, or one with the other implied, so a lost hip does not collapse the torso.
            bool hips = w23 > MinWeight || w24 > MinWeight;
            float hipW = Mathf.Max(w23, w24);
            Vector2 hipMid;
            if (w23 > MinWeight && w24 > MinWeight) {
                hipMid = (p23 + p24) * 0.5f;
            } else if (w23 > MinWeight) {
                hipMid = p23 + new Vector2(Mathf.Sign(shoulderMid.x - p23.x) * 0.09f * ws, 0f);
            } else {
                hipMid = p24 + new Vector2(Mathf.Sign(shoulderMid.x - p24.x) * 0.09f * ws, 0f);
            }

            // F-60: the hips are not in the picture and the sender placed the root itself. The torso is then built to that
            // estimated hip centre, exactly as if the hips were there, and (per the skin) continued below it, because the
            // person does not end at the waist - the camera does. No leg is drawn: there is nothing measured to draw.
            bool noHipJoints = shoulders && !hips && b.RootEstimated;
            if (noHipJoints) {
                hips = true;
                hipMid = b.HipCentre;
                hipW = shoulderW;
            }

            // The body is cut off when no leg is in the picture: the sender found no hips (above), or the hips sit on the edge of
            // the picture (they flicker in and out of it, so the hip joints alone cannot say) and the knees and ankles are absent.
            bool legsAbsent = b.Weight[Joint.LeftKnee] < LegAbsentWeight && b.Weight[Joint.RightKnee] < LegAbsentWeight
                              && b.Weight[Joint.LeftAnkle] < LegAbsentWeight && b.Weight[Joint.RightAnkle] < LegAbsentWeight;
            bool cutOff = shoulders && hips && (b.RootEstimated || legsAbsent);

            if (shoulders && hips) {
                BuildTorso(s, ws, p11, p12, shoulderMid, shoulderW, hipMid, hipW, cutOff && s.extendCutOffBody);
            }

            BuildHead(b, s, ws, shoulders, shoulderW, shoulderMid, p11, p12);

            BuildArm(b, s, ws, Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftWrist, Joint.LeftHandEnd, GroupLeftArm);
            BuildArm(b, s, ws, Joint.RightShoulder, Joint.RightElbow, Joint.RightWrist, Joint.RightHandEnd, GroupRightArm);

            BuildLeg(b, s, ws, Joint.LeftHip, Joint.LeftKnee, Joint.LeftAnkle, Joint.LeftToe, GroupLeftLeg);
            BuildLeg(b, s, ws, Joint.RightHip, Joint.RightKnee, Joint.RightAnkle, Joint.RightToe, GroupRightLeg);
        }

        // Chest and pelvis as two rounded trapezoids along the spine: the chest is as wide as the person's own shoulders at
        // the top and narrows to the waist; the pelvis starts at the waist and widens to the hips. Together they make the
        // shoulders-waist-hips outline. The spine direction comes from the landmarks, so a lean or a bend follows.
        private void BuildTorso(SilhouetteSkin s, float ws, Vector2 p11, Vector2 p12, Vector2 shoulderMid,
                                float shoulderW, Vector2 hipMid, float hipW, bool extendBelow) {
            Vector2 spine = shoulderMid - hipMid;
            float spineLength = spine.magnitude;
            if (spineLength < 0.05f) {
                return;
            }
            Vector2 up = spine / spineLength;
            float weight = Mathf.Min(shoulderW, hipW);
            Vector2 waistMid = Vector2.Lerp(hipMid, shoulderMid, Mathf.Clamp01(s.waistPosition));
            // The crotch: a little below the hip joints.
            Vector2 crotch = hipMid - up * (s.pelvisDepth * ws);
            float chestHalf = Mathf.Max(0.08f, (p12 - p11).magnitude * 0.5f * s.chestWidthScale);
            float waistHalf = s.waistRadius * ws;
            float pelvisHalf = s.torsoBottomRadius * ws;
            float rounding = s.torsoRounding * ws;

            // The two trapezoids OVERLAP across the waist, each continuing its own edge a little way into the other, so the
            // union has no notch where two rounded corners would meet; the smooth minimum then softens the change of slope.
            float chestLength = (shoulderMid - waistMid).magnitude;
            float pelvisLength = (waistMid - crotch).magnitude;
            float overlap = Mathf.Min(0.09f * ws, 0.3f * Mathf.Min(chestLength, pelvisLength));
            float chestSlope = chestLength > 1e-4f ? (chestHalf - waistHalf) / chestLength : 0f;
            float pelvisSlope = pelvisLength > 1e-4f ? (pelvisHalf - waistHalf) / pelvisLength : 0f;
            Vector2 chestBase = waistMid - up * overlap;
            float chestBaseHalf = Mathf.Max(0.06f, waistHalf - chestSlope * overlap);
            Vector2 pelvisTop = waistMid + up * overlap;
            float pelvisTopHalf = Mathf.Max(0.06f, waistHalf - pelvisSlope * overlap);
            // The chest ends a little BELOW the shoulder line: the trapezius and the upper arms make the shoulders, so the
            // shoulder line slopes down from the neck instead of ending in a flat top with square corners.
            Vector2 chestTop = shoulderMid - up * (s.shoulderDrop * ws);
            AddTrapezoid(chestBase, chestTop, chestBaseHalf, chestHalf, rounding, weight);
            AddTrapezoid(crotch, pelvisTop, pelvisHalf, pelvisTopHalf, rounding, weight);
            if (extendBelow) {
                // A straight column, as wide as the hips, from below the screen up into the pelvis. It OVERLAPS the pelvis by
                // more than the corner rounding, so the rounded corners of the two shapes do not meet in a notch.
                Vector2 columnTop = crotch + up * Mathf.Min(0.12f * ws, 0.5f * pelvisLength);
                AddTrapezoid(hipMid - up * CutOffExtensionMetres, columnTop, pelvisHalf, pelvisHalf, rounding, weight);
            }
        }

        private void BuildHead(ShowcaseBody b, SilhouetteSkin s, float ws, bool shoulders,
                               float shoulderW, Vector2 shoulderMid, Vector2 p11, Vector2 p12) {
            float wNose = b.Weight[Joint.Nose];
            float wl = b.Weight[Joint.LeftEar];
            float wr = b.Weight[Joint.RightEar];
            Vector2 up = new Vector2(0f, 1f);
            Vector2 centre;
            float headW;

            if (wl > MinWeight && wr > MinWeight) {
                centre = (b.Position[Joint.LeftEar] + b.Position[Joint.RightEar]) * 0.5f;
                headW = Mathf.Min(wl, wr);
                if (wNose > MinWeight) {
                    headW = Mathf.Max(headW, wNose);
                }
            } else if (wNose > MinWeight) {
                Vector2 axis = shoulders ? (b.Position[Joint.Nose] - shoulderMid) : up;
                Vector2 axisUp = axis.sqrMagnitude > 1e-6f ? axis.normalized : up;
                centre = b.Position[Joint.Nose] + axisUp * (0.04f * ws);
                headW = wNose;
            } else {
                return;
            }

            if (shoulders) {
                Vector2 axis2 = centre - shoulderMid;
                if (axis2.sqrMagnitude > 1e-6f) {
                    up = axis2.normalized;
                }
            }

            Head = new Vector4(centre.x, centre.y, s.headRadiusX * ws, s.headRadiusY * ws);
            HeadDirection = new Vector4(up.x, up.y, headW, 0f);

            if (shoulders) {
                float neckW = Mathf.Min(shoulderW, headW);
                Vector2 neckEnd = centre - up * (s.headRadiusY * ws * 0.6f);
                AddCap(shoulderMid, neckEnd, s.neckRadius * ws, s.neckRadius * ws, neckW);
                // The trapezius: from each shoulder up to the base of the neck, so the shoulder line slopes into the
                // neck instead of meeting it at a right angle.
                Vector2 neckBase = shoulderMid + up * (0.07f * ws);
                AddCap(p11, neckBase, s.trapeziusRadius * ws, s.neckRadius * ws, neckW);
                AddCap(p12, neckBase, s.trapeziusRadius * ws, s.neckRadius * ws, neckW);
            }
        }

        private void BuildArm(ShowcaseBody b, SilhouetteSkin s, float ws, int shoulder,
                              int elbow, int wrist, int handEnd, int group) {
            float wS = b.Weight[shoulder];
            float wE = b.Weight[elbow];
            float wW = b.Weight[wrist];
            float maxLen = s.maxArmSegment * ws;
            Vector2 pS = b.Position[shoulder];
            Vector2 pE = ClampFrom(pS, b.Position[elbow], maxLen);
            Vector2 pW = ClampFrom(pE, b.Position[wrist], maxLen);

            // One variable-width stroke per segment: widest at the shoulder, narrowest at the wrist. The elbow is where two
            // strokes meet with the same radius, so its outside is round and the join has no bead.
            AddStroke(pS, pE, s.upperArmShoulderRadius * ws, s.upperArmElbowRadius * ws, 0.5f, 0f, Mathf.Min(wS, wE), group);
            AddStroke(pE, pW, s.foreArmElbowRadius * ws, s.foreArmWristRadius * ws, 0.5f, 0f, Mathf.Min(wE, wW), group);

            // Hand: the measured palm when hand tracking is valid, otherwise a compact shape that
            // continues the forearm. Fingers are never invented.
            float wH = b.Weight[handEnd];
            if (wH > MinWeight) {
                Vector2 pH = ClampFrom(pW, b.Position[handEnd], s.fallbackHandLength * ws * 2.2f);
                AddCap(pW, pH, s.handPalmRadius * ws, s.handEndRadius * ws, Mathf.Min(Mathf.Min(wE, wW), wH));
            } else {
                Vector2 dir = pW - pE;
                if (dir.sqrMagnitude > 1e-6f) {
                    Vector2 end = pW + dir.normalized * (s.fallbackHandLength * ws);
                    AddCap(pW, end, s.handPalmRadius * ws, s.handEndRadius * ws, Mathf.Min(wE, wW));
                }
            }
        }

        private void BuildLeg(ShowcaseBody b, SilhouetteSkin s, float ws, int hip, int knee,
                              int ankle, int toe, int group) {
            float wH = b.Weight[hip];
            float wK = b.Weight[knee];
            float wA = b.Weight[ankle];
            float wT = b.Weight[toe];
            float maxLen = s.maxLegSegment * ws;
            Vector2 pH = b.Position[hip];
            Vector2 pK = ClampFrom(pH, b.Position[knee], maxLen);
            Vector2 pA = ClampFrom(pK, b.Position[ankle], maxLen);

            float thighW = Mathf.Min(wH, wK);
            float shinW = Mathf.Min(wK, wA);
            // The thigh narrows to the knee; the shin is widest a little below it (the calf) and narrows to the ankle.
            AddStroke(pH, pK, s.thighHipRadius * ws, s.thighKneeRadius * ws, 0.5f, 0f, thighW, group);
            AddStroke(pK, pA, s.shinKneeRadius * ws, s.shinAnkleRadius * ws, s.calfBulgePosition, s.calfBulgeRadius * ws, shinW, group);

            if (wT > MinWeight) {
                Vector2 pT = ClampFrom(pA, b.Position[toe], 0.28f * ws);
                AddCap(pA, pT, s.footAnkleRadius * ws, s.footToeRadius * ws, Mathf.Min(shinW, wT));
            } else {
                // No toe landmark: a short foot continuing the shin, so the leg ends in a foot and not in a stump.
                Vector2 dir = pA - pK;
                if (dir.sqrMagnitude > 1e-6f) {
                    Vector2 end = pA + dir.normalized * (s.fallbackFootLength * ws);
                    AddCap(pA, end, s.footAnkleRadius * ws, s.footToeRadius * ws, shinW);
                }
            }
        }

        private void AddCap(Vector2 a, Vector2 bEnd, float ra, float rb, float weight) {
            Add(a, bEnd, ra, rb, 0f, weight, 0);
        }

        // A rounded trapezoid from its base (a) to its top (b), with half-widths ra at the base and rb at the top.
        private void AddTrapezoid(Vector2 a, Vector2 bEnd, float ra, float rb, float rounding, float weight) {
            Add(a, bEnd, ra, rb, 1f + Mathf.Max(0f, rounding), weight, 0);
        }

        // A limb segment as a chain of tapered capsules whose radius follows a smooth profile: it runs from ra at a to rb at b
        // and, when bulge > 0, passes through the radius `bulge` at the fraction `bulgeAt` (the calf). Consecutive capsules
        // share their end radius, so the outline has no steps.
        private void AddStroke(Vector2 a, Vector2 bEnd, float ra, float rb, float bulgeAt, float bulge, float weight, int group) {
            if (weight <= MinWeight) {
                return;
            }
            Vector2 prev = a;
            float prevR = ra;
            for (int i = 1; i <= StrokeSteps; i++) {
                float t = i / (float)StrokeSteps;
                Vector2 point = Vector2.Lerp(a, bEnd, t);
                float r = ProfileRadius(t, ra, rb, bulgeAt, bulge);
                Add(prev, point, prevR, r, 0f, weight, group);
                prev = point;
                prevR = r;
            }
        }

        /// <summary>The radius of a stroke at fraction <paramref name="t"/>: a straight taper, or, with a bulge, the smooth
        /// quadratic through the end radii and the bulge radius.</summary>
        public static float ProfileRadius(float t, float ra, float rb, float bulgeAt, float bulge) {
            if (bulge <= 0f) {
                return Mathf.Lerp(ra, rb, t);
            }
            float m = Mathf.Clamp(bulgeAt, 0.05f, 0.95f);
            // Lagrange quadratic through (0, ra), (m, bulge) and (1, rb).
            float l0 = (t - m) * (t - 1f) / ((0f - m) * (0f - 1f));
            float lm = (t - 0f) * (t - 1f) / ((m - 0f) * (m - 1f));
            float l1 = (t - 0f) * (t - m) / ((1f - 0f) * (1f - m));
            return Mathf.Max(0.005f, ra * l0 + bulge * lm + rb * l1);
        }

        // group 0 is a shape on its own. A non-zero group continues the previous group when it is the same id and the
        // previous shape was added right before it; otherwise it starts a new one.
        private void Add(Vector2 a, Vector2 bEnd, float ra, float rb, float kind, float weight, int group) {
            if (weight <= MinWeight || CapCount >= MaxCaps) {
                return;
            }
            if (!IsFinite(a) || !IsFinite(bEnd)) {
                return;
            }
            float k = kind;
            if (kind < 0.5f) {
                k = (group != 0 && group == lastGroup) ? -1f : 0f;
            }
            lastGroup = group;
            Caps[CapCount] = new Vector4(a.x, a.y, bEnd.x, bEnd.y);
            CapRadii[CapCount] = new Vector4(ra, rb, Mathf.Clamp01(weight), k);
            CapCount++;
        }

        /// <summary>
        /// Signed distance, in view metres, from <paramref name="p"/> to the silhouette: negative inside. The same
        /// arithmetic as the mask shader (head ellipse, then every shape melted in with the smooth minimum), so the
        /// shape can be tested and inspected on the CPU without a GPU.
        /// </summary>
        public float Distance(Vector2 p, float blend) {
            float d = 1000f;
            if (HeadDirection.z > 0.001f) {
                float w = Mathf.Clamp01(HeadDirection.z);
                Vector2 up = new Vector2(HeadDirection.x, HeadDirection.y);
                Vector2 right = new Vector2(up.y, -up.x);
                Vector2 q = p - new Vector2(Head.x, Head.y);
                Vector2 radii = new Vector2(Mathf.Max(Head.z * w, 1e-4f), Mathf.Max(Head.w * w, 1e-4f));
                Vector2 l = new Vector2(Vector2.Dot(q, right) / radii.x, Vector2.Dot(q, up) / radii.y);
                d = (l.magnitude - 1f) * Mathf.Min(radii.x, radii.y);
            }
            float dg = 1000f;
            bool grouped = false;
            for (int c = 0; c < CapCount; c++) {
                float w = CapRadii[c].z;
                if (w <= 0.001f) {
                    continue;
                }
                Vector2 a = new Vector2(Caps[c].x, Caps[c].y);
                Vector2 bb = new Vector2(Caps[c].z, Caps[c].w);
                float dc;
                if (CapRadii[c].w > 0.5f) {
                    dc = TrapezoidDistance(p, a, bb, CapRadii[c].x * w, CapRadii[c].y * w, CapRadii[c].w - 1f);
                } else {
                    Vector2 pa = p - a;
                    Vector2 ba = bb - a;
                    float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(Vector2.Dot(ba, ba), 1e-6f));
                    float r = Mathf.Lerp(CapRadii[c].x, CapRadii[c].y, h) * w;
                    dc = (pa - ba * h).magnitude - r;
                }
                if (CapRadii[c].w < -0.5f) {
                    dg = Mathf.Min(dg, dc);
                } else {
                    if (grouped) {
                        d = SmoothMin(d, dg, blend);
                    }
                    dg = dc;
                    grouped = true;
                }
            }
            if (grouped) {
                d = SmoothMin(d, dg, blend);
            }
            return d;
        }

        // Exact distance to an isosceles trapezoid whose base is at a and top at b (half-widths r1 and r2), with its corners
        // rounded by `rounding`. The shader does the same arithmetic.
        private static float TrapezoidDistance(Vector2 p, Vector2 a, Vector2 b, float r1, float r2, float rounding) {
            Vector2 axis = b - a;
            float length = axis.magnitude;
            if (length < 1e-5f) {
                return 1000f;
            }
            Vector2 up = axis / length;
            Vector2 lateral = new Vector2(up.y, -up.x);
            Vector2 centre = (a + b) * 0.5f;
            float he = length * 0.5f - rounding;
            float w1 = Mathf.Max(r1 - rounding, 0.002f);
            float w2 = Mathf.Max(r2 - rounding, 0.002f);
            he = Mathf.Max(he, 0.002f);
            Vector2 q = p - centre;
            Vector2 local = new Vector2(Mathf.Abs(Vector2.Dot(q, lateral)), Vector2.Dot(q, up));
            Vector2 k1 = new Vector2(w2, he);
            Vector2 k2 = new Vector2(w2 - w1, 2f * he);
            Vector2 ca = new Vector2(local.x - Mathf.Min(local.x, local.y < 0f ? w1 : w2), Mathf.Abs(local.y) - he);
            float t = Mathf.Clamp01(Vector2.Dot(k1 - local, k2) / Vector2.Dot(k2, k2));
            Vector2 cb = local - k1 + k2 * t;
            float sign = (cb.x < 0f && ca.y < 0f) ? -1f : 1f;
            return sign * Mathf.Sqrt(Mathf.Min(Vector2.Dot(ca, ca), Vector2.Dot(cb, cb))) - rounding;
        }

        private static float SmoothMin(float a, float b, float k) {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / Mathf.Max(k, 1e-5f);
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        /// <summary>The point <paramref name="to"/>, pulled back toward <paramref name="from"/> if it
        /// lies further than a believable limb length away. Keeps the limb continuous.</summary>
        public static Vector2 ClampFrom(Vector2 from, Vector2 to, float maxLength) {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len <= maxLength || len < 1e-6f) {
                return to;
            }
            return from + d * (maxLength / len);
        }

        private void UpdateWidthScale(ShowcaseBody b, SilhouetteSkin s, float dt) {
            float target = 1f;
            if (s.adaptToShoulderWidth && b.ShoulderWidth > 0.05f) {
                target = Mathf.Clamp(b.ShoulderWidth / Mathf.Max(0.1f, s.referenceShoulderWidth), 0.75f, 1.35f);
            }
            WidthScale = Mathf.Lerp(WidthScale, target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / 0.4f));
        }

        private static bool IsFinite(Vector2 v) {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));
        }
    }
}
