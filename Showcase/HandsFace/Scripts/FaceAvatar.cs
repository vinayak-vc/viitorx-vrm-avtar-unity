using UnityEngine;

using VirtualMirror.Vision;

namespace HandsFaceShowcase {
    /// <summary>
    /// A cartoon face built from Unity primitives (no assets) and driven by the 52 blendshapes and the head rotation of one <see cref="FaceData"/>:
    /// eyelids close with the blinks, pupils follow the eye-look scores, brows rise and fall, the mouth opens with the jaw and curves with the smile.
    /// Its job is to make the stream visible, not to be a good character: any mesh with ARKit-named blendshapes can be driven by
    /// <c>ArkitBlendshapeDriver</c> in the Vision package instead.
    ///
    /// Geometry convention: the face looks along local -z, so with the avatar placed in front of the camera and the head rotation applied as it
    /// arrives, a frontal face looks at the camera. "Reflection" mode puts the subject's LEFT eye on the viewer's left, like a mirror; off puts it on the
    /// right, like a photo of someone else. NOT TESTED against a live face.
    /// </summary>
    public sealed class FaceAvatar {
        private const int LipPoints = 17;
        private static readonly Color Skin = new Color(1f, 0.84f, 0.62f, 1f);
        private static readonly Color Dark = new Color(0.12f, 0.06f, 0.06f, 1f);

        private readonly GameObject root;
        private readonly Transform eyeNegX;
        private readonly Transform eyePosX;
        private readonly Transform pupilNegX;
        private readonly Transform pupilPosX;
        private readonly Transform browNegX;
        private readonly Transform browPosX;
        private readonly Transform cavity;
        private readonly LineRenderer upperLip;
        private readonly LineRenderer lowerLip;

        public FaceAvatar(Transform parent) {
            root = new GameObject("Face Avatar");
            root.transform.SetParent(parent, false);
            GameObject head = ShowcaseDraw.Primitive(PrimitiveType.Sphere, "Head", root.transform, Skin);
            head.transform.localScale = new Vector3(0.28f, 0.34f, 0.30f);
            GameObject nose = ShowcaseDraw.Primitive(PrimitiveType.Sphere, "Nose", root.transform, new Color(0.95f, 0.7f, 0.5f, 1f));
            nose.transform.localPosition = new Vector3(0f, -0.01f, -0.155f);
            nose.transform.localScale = new Vector3(0.04f, 0.05f, 0.05f);
            eyeNegX = MakeEye("Eye (screen left)", -0.075f, out pupilNegX);
            eyePosX = MakeEye("Eye (screen right)", 0.075f, out pupilPosX);
            browNegX = MakeBrow("Brow (screen left)", -0.075f);
            browPosX = MakeBrow("Brow (screen right)", 0.075f);
            GameObject mouthCavity = ShowcaseDraw.Primitive(PrimitiveType.Sphere, "Mouth", root.transform, Dark);
            mouthCavity.transform.localPosition = new Vector3(0f, -0.1f, -0.135f);
            cavity = mouthCavity.transform;
            upperLip = ShowcaseDraw.Line("Upper Lip", root.transform, new Color(0.65f, 0.15f, 0.2f, 1f), 0.012f, LipPoints);
            lowerLip = ShowcaseDraw.Line("Lower Lip", root.transform, new Color(0.65f, 0.15f, 0.2f, 1f), 0.012f, LipPoints);
        }

        public Transform Root {
            get {
                return root.transform;
            }
        }

        public void SetVisible(bool visible) {
            root.SetActive(visible);
        }

        public void Destroy() {
            Object.Destroy(root);
        }

        /// <summary>Poses the face from the blendshapes of <paramref name="face"/>. The head rotation is applied by the caller (it needs the camera).</summary>
        public void Apply(FaceData face, bool reflection) {
            // Which side's scores drive the eye on the viewer's left (local -x): a reflection shows the subject's left eye there.
            int leftBlink = ArkitBlendshapes.EyeBlinkLeft;
            int rightBlink = ArkitBlendshapes.EyeBlinkRight;
            float negBlink = face.Blendshape(reflection ? leftBlink : rightBlink);
            float posBlink = face.Blendshape(reflection ? rightBlink : leftBlink);
            float negWide = face.Blendshape(reflection ? ArkitBlendshapes.EyeWideLeft : ArkitBlendshapes.EyeWideRight);
            float posWide = face.Blendshape(reflection ? ArkitBlendshapes.EyeWideRight : ArkitBlendshapes.EyeWideLeft);
            PoseEye(eyeNegX, pupilNegX, negBlink, negWide, face, reflection, true);
            PoseEye(eyePosX, pupilPosX, posBlink, posWide, face, reflection, false);
            PoseBrow(browNegX, face, reflection, true);
            PoseBrow(browPosX, face, reflection, false);
            PoseMouth(face);
        }

        private Transform MakeEye(string objectName, float x, out Transform pupil) {
            GameObject white = ShowcaseDraw.Primitive(PrimitiveType.Sphere, objectName, root.transform, Color.white);
            white.transform.localPosition = new Vector3(x, 0.05f, -0.118f);
            white.transform.localScale = new Vector3(0.09f, 0.09f, 0.05f);
            GameObject dot = ShowcaseDraw.Primitive(PrimitiveType.Sphere, "Pupil", white.transform, Dark);
            dot.transform.localPosition = new Vector3(0f, 0f, -0.35f);
            dot.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            pupil = dot.transform;
            return white.transform;
        }

        private Transform MakeBrow(string objectName, float x) {
            GameObject brow = ShowcaseDraw.Primitive(PrimitiveType.Cube, objectName, root.transform, new Color(0.3f, 0.17f, 0.1f, 1f));
            brow.transform.localPosition = new Vector3(x, 0.125f, -0.128f);
            brow.transform.localScale = new Vector3(0.075f, 0.014f, 0.014f);
            return brow.transform;
        }

        private void PoseEye(Transform eye, Transform pupil, float blink, float wide, FaceData face, bool reflection, bool negativeX) {
            float open = Mathf.Clamp01(1f - blink * 0.94f);
            float widen = 1f + wide * 0.3f;
            eye.localScale = new Vector3(0.09f * widen, 0.09f * widen * Mathf.Max(0.07f, open), 0.05f);
            // The scores that belong to this eye: the subject's left set, or the right set.
            bool leftSet = negativeX == reflection;
            float lookIn = face.Blendshape(leftSet ? ArkitBlendshapes.EyeLookInLeft : ArkitBlendshapes.EyeLookInRight);
            float lookOut = face.Blendshape(leftSet ? ArkitBlendshapes.EyeLookOutLeft : ArkitBlendshapes.EyeLookOutRight);
            float lookUp = face.Blendshape(leftSet ? ArkitBlendshapes.EyeLookUpLeft : ArkitBlendshapes.EyeLookUpRight);
            float lookDown = face.Blendshape(leftSet ? ArkitBlendshapes.EyeLookDownLeft : ArkitBlendshapes.EyeLookDownRight);
            // Outward is away from the nose: -x for the eye on the viewer's left, +x for the other.
            float outwardSign = negativeX ? -1f : 1f;
            float dx = (lookOut - lookIn) * 0.22f * outwardSign;
            float dy = (lookUp - lookDown) * 0.2f;
            pupil.localPosition = new Vector3(dx, dy, -0.35f);
        }

        private void PoseBrow(Transform brow, FaceData face, bool reflection, bool negativeX) {
            bool leftSet = negativeX == reflection;
            float down = face.Blendshape(leftSet ? ArkitBlendshapes.BrowDownLeft : ArkitBlendshapes.BrowDownRight);
            float outerUp = face.Blendshape(leftSet ? ArkitBlendshapes.BrowOuterUpLeft : ArkitBlendshapes.BrowOuterUpRight);
            float innerUp = face.Blendshape(ArkitBlendshapes.BrowInnerUp);
            float y = 0.125f + 0.03f * (outerUp * 0.6f + innerUp * 0.5f) - 0.03f * down;
            float x = negativeX ? -0.075f : 0.075f;
            brow.localPosition = new Vector3(x, y, -0.128f);
            // The inner end rises with the inner-up score and the outer end with the outer-up score; a frown tilts the inner end down.
            float tilt = (innerUp - outerUp + down * 0.8f) * 18f;
            brow.localRotation = Quaternion.Euler(0f, 0f, negativeX ? -tilt : tilt);
        }

        private void PoseMouth(FaceData face) {
            float jaw = face.Blendshape(ArkitBlendshapes.JawOpen);
            float smile = 0.5f * (face.Blendshape(ArkitBlendshapes.MouthSmileLeft) + face.Blendshape(ArkitBlendshapes.MouthSmileRight));
            float frown = 0.5f * (face.Blendshape(ArkitBlendshapes.MouthFrownLeft) + face.Blendshape(ArkitBlendshapes.MouthFrownRight));
            float pucker = face.Blendshape(ArkitBlendshapes.MouthPucker);
            float funnel = face.Blendshape(ArkitBlendshapes.MouthFunnel);
            float halfWidth = 0.07f * (1f + 0.45f * smile - 0.5f * pucker - 0.25f * funnel);
            float curve = 0.05f * (smile - frown);
            float baseY = -0.1f;
            float gap = jaw * 0.075f;
            int i = 0;
            while (i < LipPoints) {
                float u = (i / (float)(LipPoints - 1)) * 2f - 1f;
                float bow = u * u;
                float x = u * halfWidth;
                float upperY = baseY + curve * bow + 0.004f;
                float lowerY = baseY + curve * bow - gap * (1f - 0.5f * bow) - 0.004f;
                upperLip.SetPosition(i, new Vector3(x, upperY, -0.14f));
                lowerLip.SetPosition(i, new Vector3(x, lowerY, -0.14f));
                i = i + 1;
            }
            cavity.localPosition = new Vector3(0f, baseY - gap * 0.5f, -0.136f);
            cavity.localScale = new Vector3(halfWidth * 1.9f, Mathf.Max(0.002f, gap), 0.02f);
        }
    }
}
