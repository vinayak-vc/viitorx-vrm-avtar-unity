using UnityEngine;

namespace VirtualMirror.Tracking.Filtering {
    /// <summary>
    /// One-Euro filter for a single scalar signal. Low lag when moving fast, strong smoothing when
    /// slow. See Casiez et al. 2012. Reused per axis by <see cref="JointFilterPipeline"/>.
    /// </summary>
    public sealed class OneEuroFilter {
        private readonly float minCutoff;
        private readonly float beta;
        private readonly float derivativeCutoff;

        private bool initialized;
        private float previousValue;
        private float previousDerivative;

        public OneEuroFilter(float minCutoff, float beta, float derivativeCutoff) {
            this.minCutoff = minCutoff > 0f ? minCutoff : 1f;
            this.beta = beta;
            this.derivativeCutoff = derivativeCutoff > 0f ? derivativeCutoff : 1f;
        }

        public float Filter(float value, float deltaSeconds) {
            float dt = deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
            if (!initialized) {
                initialized = true;
                previousValue = value;
                previousDerivative = 0f;
                return value;
            }
            float derivative = (value - previousValue) / dt;
            float smoothedDerivative = LowPass(derivative, previousDerivative, Alpha(derivativeCutoff, dt));
            previousDerivative = smoothedDerivative;
            float cutoff = minCutoff + beta * Mathf.Abs(smoothedDerivative);
            float smoothedValue = LowPass(value, previousValue, Alpha(cutoff, dt));
            previousValue = smoothedValue;
            return smoothedValue;
        }

        public void Reset() {
            initialized = false;
        }

        private static float Alpha(float cutoff, float dt) {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        private static float LowPass(float value, float previous, float alpha) {
            return alpha * value + (1f - alpha) * previous;
        }
    }
}
