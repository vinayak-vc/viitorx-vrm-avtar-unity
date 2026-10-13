using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Turns a <see cref="FaceMessage"/> into the project's existing <see cref="FaceFrame"/> (blinks, mouth, smile, angry, surprised), the six
    /// numbers <c>VrmExpressionRetargeter</c> drives a VRM avatar from. The formulas are the ones <c>MediaPipeFaceProvider</c> uses on the same
    /// blendshape names, so the avatar behaves the same whichever source feeds it:
    /// mouthOpen = jawOpen, smile = max(mouthSmileLeft, mouthSmileRight), angry = max(browDownLeft, browDownRight),
    /// surprised = max(browInnerUp, eyeWideLeft, eyeWideRight).
    ///
    /// The first face is used. No face, or a sender running without blendshapes, is an invalid frame, which the retargeter resets to neutral.
    /// <c>swapSides</c> exchanges the eyes: the wire's Left is the SUBJECT's left, so a mirror avatar facing the user takes the opposite eye.
    /// </summary>
    public static class FaceFrameMapper {
        public static void Fill(FaceFrame frame, FaceMessage message, bool swapSides, double timestampSeconds) {
            if (message.Count < 1 || !message.Face(0).HasBlendshapes) {
                frame.SetExpressions(0f, 0f, 0f, 0f, 0f, 0f);
                frame.MarkInvalid(timestampSeconds);
                return;
            }
            FaceData face = message.Face(0);
            float blinkLeft = face.Blendshape(swapSides ? ArkitBlendshapes.EyeBlinkRight : ArkitBlendshapes.EyeBlinkLeft);
            float blinkRight = face.Blendshape(swapSides ? ArkitBlendshapes.EyeBlinkLeft : ArkitBlendshapes.EyeBlinkRight);
            float jawOpen = face.Blendshape(ArkitBlendshapes.JawOpen);
            float smile = Mathf.Max(face.Blendshape(ArkitBlendshapes.MouthSmileLeft), face.Blendshape(ArkitBlendshapes.MouthSmileRight));
            float angry = Mathf.Max(face.Blendshape(ArkitBlendshapes.BrowDownLeft), face.Blendshape(ArkitBlendshapes.BrowDownRight));
            float surprised = Mathf.Max(face.Blendshape(ArkitBlendshapes.BrowInnerUp),
                Mathf.Max(face.Blendshape(ArkitBlendshapes.EyeWideLeft), face.Blendshape(ArkitBlendshapes.EyeWideRight)));
            frame.SetExpressions(blinkLeft, blinkRight, jawOpen, smile, angry, surprised);
            frame.SetMeta(timestampSeconds, true);
        }
    }
}
