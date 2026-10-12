using UnityEngine;

namespace ChromaticRain {
    /// <summary>
    /// Turns a <see cref="ShowcaseBody"/> into the shape list the mask shader draws: tapered
    /// capsules (limbs, torso, neck, hands, feet) and one ellipse (the head). The shader melts them
    /// together with a smooth minimum, so the head, torso, arms and legs read as ONE body rather
    /// than a set of joint markers.
    ///
    /// A segment is drawn only while both of its ends have weight, its strength follows the weaker
    /// end, and its length is clamped to a believable limb, so a bad joint can neither spike nor
    /// connect to the origin. Pure logic, reused buffers, no allocation per frame.
    /// </summary>
    public sealed class SilhouetteBuilder {
        public const int MaxCaps = 20;

        /// <summary>Per cap: a.x, a.y, b.x, b.y.</summary>
        public readonly Vector4[] Caps = new Vector4[MaxCaps];

        /// <summary>Per cap: radius at a, radius at b, weight, unused.</summary>
        public readonly Vector4[] CapRadii = new Vector4[MaxCaps];

        /// <summary>Head ellipse: centre x, centre y, radius x, radius y.</summary>
        public Vector4 Head;

        /// <summary>Head up-direction x, y, weight, unused.</summary>
        public Vector4 HeadDirection = new Vector4(0f, 1f, 0f, 0f);

        public int CapCount { get; private set; }

        public float WidthScale { get; private set; }

        private const float MinWeight = 0.002f;

        public SilhouetteBuilder() {
            WidthScale = 1f;
        }

        public void Reset() {
            CapCount = 0;
            HeadDirection.z = 0f;
            WidthScale = 1f;
        }

        public void Build(ShowcaseBody b, ChromaticRainSettings s, float dt) {
            CapCount = 0;
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

            // Torso: a tapered core plus a shoulder bar and a hip bar.
            if (shoulders && hips) {
                float torsoW = Mathf.Min(shoulderW, hipW);
                AddCap(shoulderMid, hipMid, s.torsoTopRadius * ws, s.torsoBottomRadius * ws, torsoW);
                AddCap(p11, p12, s.shoulderBarRadius * ws, s.shoulderBarRadius * ws, shoulderW);
                if (w23 > MinWeight && w24 > MinWeight) {
                    AddCap(p23, p24, s.hipBarRadius * ws, s.hipBarRadius * ws, Mathf.Min(w23, w24));
                }
            }

            BuildHead(b, s, ws, shoulders, shoulderW, shoulderMid);

            BuildArm(b, s, ws, Joint.LeftShoulder, Joint.LeftElbow, Joint.LeftWrist, Joint.LeftHandEnd);
            BuildArm(b, s, ws, Joint.RightShoulder, Joint.RightElbow, Joint.RightWrist, Joint.RightHandEnd);

            BuildLeg(b, s, ws, Joint.LeftHip, Joint.LeftKnee, Joint.LeftAnkle, Joint.LeftToe);
            BuildLeg(b, s, ws, Joint.RightHip, Joint.RightKnee, Joint.RightAnkle, Joint.RightToe);
        }

        private void BuildHead(ShowcaseBody b, ChromaticRainSettings s, float ws, bool shoulders,
                               float shoulderW, Vector2 shoulderMid) {
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
                Vector2 neckEnd = centre - up * (s.headRadiusY * ws * 0.6f);
                AddCap(shoulderMid, neckEnd, s.neckRadius * ws, s.neckRadius * ws,
                       Mathf.Min(shoulderW, headW));
            }
        }

        private void BuildArm(ShowcaseBody b, ChromaticRainSettings s, float ws, int shoulder,
                              int elbow, int wrist, int handEnd) {
            float wS = b.Weight[shoulder];
            float wE = b.Weight[elbow];
            float wW = b.Weight[wrist];
            float maxLen = s.maxArmSegment * ws;
            Vector2 pS = b.Position[shoulder];
            Vector2 pE = ClampFrom(pS, b.Position[elbow], maxLen);
            Vector2 pW = ClampFrom(pE, b.Position[wrist], maxLen);

            AddCap(pS, pE, s.upperArmShoulderRadius * ws, s.upperArmElbowRadius * ws, Mathf.Min(wS, wE));
            AddCap(pE, pW, s.foreArmElbowRadius * ws, s.foreArmWristRadius * ws, Mathf.Min(wE, wW));

            // Hand: the measured palm when hand tracking is valid, otherwise a compact shape that
            // continues the forearm. Fingers are never invented.
            float wH = b.Weight[handEnd];
            if (wH > MinWeight) {
                Vector2 pH = ClampFrom(pW, b.Position[handEnd], s.fallbackHandLength * ws * 2.2f);
                AddCap(pW, pH, s.handPalmRadius * ws, s.handEndRadius * ws, Mathf.Min(wW, wH));
            } else {
                Vector2 dir = pW - pE;
                if (dir.sqrMagnitude > 1e-6f) {
                    Vector2 end = pW + dir.normalized * (s.fallbackHandLength * ws);
                    AddCap(pW, end, s.handPalmRadius * ws, s.handEndRadius * ws, Mathf.Min(wE, wW));
                }
            }
        }

        private void BuildLeg(ShowcaseBody b, ChromaticRainSettings s, float ws, int hip, int knee,
                              int ankle, int toe) {
            float wH = b.Weight[hip];
            float wK = b.Weight[knee];
            float wA = b.Weight[ankle];
            float wT = b.Weight[toe];
            float maxLen = s.maxLegSegment * ws;
            Vector2 pH = b.Position[hip];
            Vector2 pK = ClampFrom(pH, b.Position[knee], maxLen);
            Vector2 pA = ClampFrom(pK, b.Position[ankle], maxLen);

            AddCap(pH, pK, s.thighHipRadius * ws, s.thighKneeRadius * ws, Mathf.Min(wH, wK));
            AddCap(pK, pA, s.shinKneeRadius * ws, s.shinAnkleRadius * ws, Mathf.Min(wK, wA));
            if (wT > MinWeight) {
                Vector2 pT = ClampFrom(pA, b.Position[toe], 0.28f * ws);
                AddCap(pA, pT, s.footAnkleRadius * ws, s.footToeRadius * ws, Mathf.Min(wA, wT));
            }
        }

        private void AddCap(Vector2 a, Vector2 bEnd, float ra, float rb, float weight) {
            if (weight <= MinWeight || CapCount >= MaxCaps) {
                return;
            }
            if (!IsFinite(a) || !IsFinite(bEnd)) {
                return;
            }
            Caps[CapCount] = new Vector4(a.x, a.y, bEnd.x, bEnd.y);
            CapRadii[CapCount] = new Vector4(ra, rb, Mathf.Clamp01(weight), 0f);
            CapCount++;
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

        private void UpdateWidthScale(ShowcaseBody b, ChromaticRainSettings s, float dt) {
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
