using UniVRM10;
using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Retargets facial expression weights from <see cref="FaceFrame"/> to VRM 1.0 <see cref="Vrm10Instance"/>.
    /// Maps eye blinks, mouth opening, and emotional expressions.
    /// </summary>
    public sealed class VrmExpressionRetargeter {
        private Vrm10Instance boundVrmInstance;

        public bool IsBound {
            get {
                return boundVrmInstance != null;
            }
        }

        public void Bind(Vrm10Instance vrmInstance) {
            boundVrmInstance = vrmInstance;
        }

        public void Unbind() {
            boundVrmInstance = null;
        }

        public void Apply(FaceFrame frame) {
            if (boundVrmInstance == null || frame == null || !frame.IsValid) {
                return;
            }

            Vrm10RuntimeExpression expressionRuntime = boundVrmInstance.Runtime.Expression;
            if (expressionRuntime == null) {
                return;
            }

            expressionRuntime.SetWeight(ExpressionKey.BlinkLeft, Mathf.Clamp01(frame.BlinkLeft));
            expressionRuntime.SetWeight(ExpressionKey.BlinkRight, Mathf.Clamp01(frame.BlinkRight));
            expressionRuntime.SetWeight(ExpressionKey.Aa, Mathf.Clamp01(frame.MouthOpen));
            expressionRuntime.SetWeight(ExpressionKey.Happy, Mathf.Clamp01(frame.Smile));
            expressionRuntime.SetWeight(ExpressionKey.Angry, Mathf.Clamp01(frame.Angry));
            expressionRuntime.SetWeight(ExpressionKey.Surprised, Mathf.Clamp01(frame.Surprised));
        }
    }
}
