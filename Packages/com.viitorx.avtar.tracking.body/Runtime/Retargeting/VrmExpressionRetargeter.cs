using System.Collections.Generic;

using UnityEngine;

using UniVRM10;

using VirtualMirror.Core;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Retargets facial expression weights from <see cref="FaceFrame"/> to VRM 1.0 <see cref="Vrm10Instance"/>.
    /// Maps eye blinks, mouth opening, and emotional expressions.
    ///
    /// Every write is guarded against the avatar's actually-registered expressions (SDS-008 §5): a key the
    /// VRM does not define is skipped and a single Warning is logged per missing key. When the face frame
    /// is invalid (tracking lost) or on <see cref="Unbind"/>, all mapped weights are reset to neutral so the
    /// face does not freeze at its last expression.
    /// </summary>
    public sealed class VrmExpressionRetargeter {
        private static readonly ExpressionKey[] MappedKeys = new ExpressionKey[] {
            ExpressionKey.BlinkLeft,
            ExpressionKey.BlinkRight,
            ExpressionKey.Aa,
            ExpressionKey.Happy,
            ExpressionKey.Angry,
            ExpressionKey.Surprised
        };

        private readonly HashSet<ExpressionKey> registeredKeys = new HashSet<ExpressionKey>(ExpressionKey.Comparer);
        private readonly HashSet<ExpressionKey> warnedMissingKeys = new HashSet<ExpressionKey>(ExpressionKey.Comparer);
        private Vrm10Instance boundVrmInstance;

        public bool IsBound {
            get {
                return boundVrmInstance != null;
            }
        }

        public void Bind(Vrm10Instance vrmInstance) {
            boundVrmInstance = vrmInstance;
            registeredKeys.Clear();
            warnedMissingKeys.Clear();
            if (vrmInstance == null) {
                return;
            }
            Vrm10RuntimeExpression expressionRuntime = vrmInstance.Runtime.Expression;
            if (expressionRuntime == null) {
                return;
            }
            IReadOnlyList<ExpressionKey> keys = expressionRuntime.ExpressionKeys;
            int index = 0;
            while (index < keys.Count) {
                registeredKeys.Add(keys[index]);
                index = index + 1;
            }
        }

        public void Unbind() {
            if (boundVrmInstance != null) {
                Vrm10RuntimeExpression expressionRuntime = boundVrmInstance.Runtime.Expression;
                if (expressionRuntime != null) {
                    ResetToNeutral(expressionRuntime);
                }
            }
            boundVrmInstance = null;
            registeredKeys.Clear();
            warnedMissingKeys.Clear();
        }

        public void Apply(FaceFrame frame) {
            if (boundVrmInstance == null) {
                return;
            }
            Vrm10RuntimeExpression expressionRuntime = boundVrmInstance.Runtime.Expression;
            if (expressionRuntime == null) {
                return;
            }
            if (frame == null || !frame.IsValid) {
                ResetToNeutral(expressionRuntime);
                return;
            }

            SetGuarded(expressionRuntime, ExpressionKey.BlinkLeft, Mathf.Clamp01(frame.BlinkLeft));
            SetGuarded(expressionRuntime, ExpressionKey.BlinkRight, Mathf.Clamp01(frame.BlinkRight));
            SetGuarded(expressionRuntime, ExpressionKey.Aa, Mathf.Clamp01(frame.MouthOpen));
            SetGuarded(expressionRuntime, ExpressionKey.Happy, Mathf.Clamp01(frame.Smile));
            SetGuarded(expressionRuntime, ExpressionKey.Angry, Mathf.Clamp01(frame.Angry));
            SetGuarded(expressionRuntime, ExpressionKey.Surprised, Mathf.Clamp01(frame.Surprised));
        }

        private void ResetToNeutral(Vrm10RuntimeExpression expressionRuntime) {
            int index = 0;
            while (index < MappedKeys.Length) {
                SetGuarded(expressionRuntime, MappedKeys[index], 0f);
                index = index + 1;
            }
        }

        private void SetGuarded(Vrm10RuntimeExpression expressionRuntime, ExpressionKey key, float weight) {
            if (!registeredKeys.Contains(key)) {
                if (warnedMissingKeys.Add(key)) {
                    Debug.LogWarning("[VrmExpressionRetargeter] Avatar has no '" + key + "' expression; skipping.");
                }
                return;
            }
            expressionRuntime.SetWeight(key, weight);
        }
    }
}
