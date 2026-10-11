using Fluxy;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Maintains the calm baseline with two things, both pooled <see cref="FluxyTarget"/>s:
    ///
    /// 1. The FIELD target. Its density texture is the baseline pattern (wide, wavy bands of colour). It
    ///    splats over the whole fluid area a few times a second with a small weight, so every pass pulls
    ///    the dye a little toward that pattern. The fluid is therefore populated from the first frame, keeps
    ///    its recognisable palette and structure, and settles back to it after any disturbance, without
    ///    ever being cleared. The pattern drifts very slowly and its colours breathe.
    /// 2. The FLOW sources: wide bands that wander on small ellipses and give the fluid a steady push
    ///    along themselves (<c>force</c>), in alternating directions at different heights. They inject no
    ///    dye; the resulting slow shear layers bend the bands into waves and swirls.
    ///
    /// Independent of tracking: it never reads a pose, a person or a state, and it is neither reset nor
    /// scaled when someone enters or leaves. It is deliberately gentle, so a hand-driven disturbance is
    /// clearly stronger than the background motion.
    /// </summary>
    public sealed class FluidBaselineSources : MonoBehaviour {
        [SerializeField] private FluxyTarget fieldTarget;
        [SerializeField] private FluxyTarget[] flowTargets;
        [SerializeField] private Material splatMaterial;
        [SerializeField] private Texture noiseTexture;

        private Material fieldSplat;
        private Material[] flowSplats;
        private Texture field;
        private float viewWidth = 4.27f;
        private float viewHeight = 2.4f;
        private float clock;

        public bool IsReady { get; private set; }

        public int Count {
            get {
                return flowTargets != null ? flowTargets.Length : 0;
            }
        }

        public bool Initialize(FluidDisruptionSettings s, float width, float height, Texture softSplat, Texture baselineField) {
            if (fieldTarget == null || flowTargets == null || flowTargets.Length == 0 || splatMaterial == null
                || baselineField == null) {
                Debug.LogError("[FluidDisruption] The baseline is missing its field target, flow targets, splat "
                               + "material or pattern. Run Showcase > Fluid Disruption > Build Showcase Scene.", this);
                return false;
            }
            viewWidth = width;
            viewHeight = height;
            field = baselineField;

            fieldSplat = FluidTargets.Configure(fieldTarget, splatMaterial, field, noiseTexture,
                                                "FluidDisruption_BaselineField (runtime)");
            fieldTarget.overridePosition = true;
            fieldTarget.enabled = true;

            flowSplats = new Material[flowTargets.Length];
            for (int i = 0; i < flowTargets.Length; i++) {
                flowSplats[i] = FluidTargets.Configure(flowTargets[i], splatMaterial, softSplat, noiseTexture,
                                                       "FluidDisruption_BaselineFlow (runtime)");
                // A source with no config simply stays off.
                flowTargets[i].enabled = i < s.baselineSources.Length;
                if (flowTargets[i].enabled) {
                    Place(flowTargets[i], s.baselineSources[i], 0f);
                }
            }
            IsReady = true;
            Tick(0f, s);
            return true;
        }

        public void Resize(float width, float height) {
            viewWidth = width;
            viewHeight = height;
        }

        /// <summary>One frame, before FluXY's own LateUpdate. Refreshes every tunable, so the settings
        /// asset can be edited live.</summary>
        public void Tick(float dt, FluidDisruptionSettings s) {
            if (!IsReady) {
                return;
            }
            clock += dt;
            TickField(s);
            for (int i = 0; i < flowTargets.Length; i++) {
                FluxyTarget t = flowTargets[i];
                if (i >= s.baselineSources.Length) {
                    t.enabled = false;
                    continue;
                }
                if (!t.enabled) {
                    t.enabled = true;
                }
                BaselineSourceConfig c = s.baselineSources[i];
                Place(t, c, clock);

                float angle = c.flowAngle * Mathf.Deg2Rad;
                t.force = new Vector3(Mathf.Cos(angle) * c.flowStrength, Mathf.Sin(angle) * c.flowStrength, 0f);
                t.velocityWeight = s.velocityWeight;
                t.rateOverTime = c.rate;
                t.densityWeight = 0f;
                t.scale = c.size;
                t.rotation = c.tilt;
                t.positionRandomness = 0.1f;
                t.scaleRandomness = 0.1f;
                t.velocityNoise = s.baselineSwirl;
                t.velocityNoiseOffset = 0.15f;
                t.velocityNoiseTiling = 1f;
            }
        }

        // The pattern covers the whole fluid area. In FluXY's override-position mode the splat size is a
        // fraction of the area, so (1 / pattern aspect, 1 / area aspect) makes it exactly one area wide
        // and tall; the position is in area units, so the slow drift moves it a few percent.
        private void TickField(FluidDisruptionSettings s) {
            float patternAspect = field.width / (float)field.height;
            float areaAspect = viewWidth / Mathf.Max(0.01f, viewHeight);
            FluxyTarget t = fieldTarget;
            t.scale = new Vector2(1f / patternAspect, 1f / areaAspect);
            float tx = Mathf.Sin(clock * 2f * Mathf.PI / Mathf.Max(1f, s.fieldDriftPeriod.x));
            float ty = Mathf.Sin(clock * 2f * Mathf.PI / Mathf.Max(1f, s.fieldDriftPeriod.y) + 1.3f);
            t.position = new Vector2(s.fieldDrift.x * tx, s.fieldDrift.y * ty);
            t.rateOverTime = s.fieldRate;
            t.densityWeight = s.fieldWeight;
            t.velocityWeight = 0f;
            t.velocityScale = Vector3.zero;

            // Colours breathe: the pattern's tint wanders slowly between a cooler and a warmer white.
            float k = s.fieldTintSwing;
            float a = Mathf.Sin(clock * 2f * Mathf.PI / 83f);
            float b = Mathf.Sin(clock * 2f * Mathf.PI / 61f + 2.1f);
            t.color = new Color(1f - k * (0.5f + 0.5f * a), 1f - k * 0.25f * (0.5f + 0.5f * b), 1f - k * (0.5f - 0.5f * a) * 0.5f, 1f);
        }

        private void Place(FluxyTarget t, BaselineSourceConfig c, float time) {
            float turn = c.phase * 2f * Mathf.PI + time * 2f * Mathf.PI / Mathf.Max(1f, c.period);
            float x = (c.centre.x + c.radius.x * Mathf.Cos(turn)) * viewWidth;
            float y = (c.centre.y + c.radius.y * Mathf.Sin(turn)) * viewHeight;
            t.transform.position = new Vector3(x, y, 0f);
        }

        private void OnDestroy() {
            FluidTargets.Destroy(fieldSplat);
            if (flowSplats == null) {
                return;
            }
            for (int i = 0; i < flowSplats.Length; i++) {
                FluidTargets.Destroy(flowSplats[i]);
            }
        }
    }
}
