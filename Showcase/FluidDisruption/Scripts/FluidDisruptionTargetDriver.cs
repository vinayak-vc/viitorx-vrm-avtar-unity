using Fluxy;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Drives three pooled <see cref="FluxyTarget"/>s (left hand, right hand, torso) from the movement
    /// estimators. Nothing is created per frame.
    ///
    /// Why a still target injects nothing: every target splats only by distance travelled
    /// (<c>rateOverDistance</c>), and its velocity weight, velocity scale, density weight and turbulence
    /// are all multiplied by the smooth movement strength. A hand inside the dead zone therefore adds no
    /// dye and no velocity, and cannot act as an eraser either.
    ///
    /// Why a lost point cannot misbehave: a point without a valid sample has its target DISABLED where it
    /// is (never moved, never sent to the origin), and a re-enabled target re-seeds its old position in
    /// <c>FluxyTarget.OnEnable</c>, so it starts again from the new position with no velocity jump.
    /// </summary>
    public sealed class FluidDisruptionTargetDriver : MonoBehaviour {
        private sealed class PointState {
            public FluxyTarget Target;
            public Material Splat;
            public bool Active;
            public Vector2 Last;
            public float Strength;
        }

        [SerializeField] private FluxyTarget leftHandTarget;
        [SerializeField] private FluxyTarget rightHandTarget;
        [SerializeField] private FluxyTarget torsoTarget;
        [SerializeField] private Material splatMaterial;
        [SerializeField] private Texture noiseTexture;

        private readonly PointState left = new PointState();
        private readonly PointState right = new PointState();
        private readonly PointState torso = new PointState();

        public bool IsReady { get; private set; }

        /// <summary>Position and strength of a target for the debug view; false while it is not injecting.</summary>
        public bool TryGetTarget(int index, out Vector2 position, out float strength) {
            PointState state = index == 0 ? left : (index == 1 ? right : torso);
            position = state.Last;
            strength = state.Strength;
            return state.Active && state.Target != null && state.Target.enabled;
        }

        public bool Initialize(FluidDisruptionSettings s, Texture splatTexture) {
            if (leftHandTarget == null || rightHandTarget == null || torsoTarget == null || splatMaterial == null) {
                Debug.LogError("[FluidDisruption] The target driver is missing a FluxyTarget or the splat "
                               + "material. Run Showcase > Fluid Disruption > Build Showcase Scene.", this);
                return false;
            }
            Bind(left, leftHandTarget, splatTexture, "FluidDisruption_LeftHandSplat (runtime)");
            Bind(right, rightHandTarget, splatTexture, "FluidDisruption_RightHandSplat (runtime)");
            Bind(torso, torsoTarget, splatTexture, "FluidDisruption_TorsoSplat (runtime)");
            torso.Target.scale = new Vector2(s.torsoSize, s.torsoSize);
            IsReady = true;
            return true;
        }

        private void Bind(PointState state, FluxyTarget target, Texture splatTexture, string materialName) {
            state.Target = target;
            state.Splat = FluidTargets.Configure(target, splatMaterial, splatTexture, noiseTexture, materialName);
            state.Active = false;
            state.Strength = 0f;
        }

        /// <summary>Switch every target off (the fluid itself is untouched).</summary>
        public void Release() {
            Disable(left);
            Disable(right);
            Disable(torso);
        }

        /// <summary>One frame, before FluXY's own LateUpdate.</summary>
        public void Tick(FluidDisruptionStateController state, MovementImpulseEstimator leftHand,
                         MovementImpulseEstimator rightHand, MovementImpulseEstimator torsoPoint,
                         FluidDisruptionSettings s) {
            if (!IsReady) {
                return;
            }
            float gate = state.Gate;
            float pulse = state.Pulse;
            bool open = gate > 0.01f;

            DriveHand(left, leftHand, open, gate, s, s.leftHandColour);
            DriveHand(right, rightHand, open, gate, s, s.rightHandColour);
            DriveTorso(torso, torsoPoint, open || pulse > 0.001f, gate, pulse, s);
        }

        private void DriveHand(PointState p, MovementImpulseEstimator est, bool open, float gate,
                               FluidDisruptionSettings s, int colourIndex) {
            if (!open || !est.HasSample) {
                Disable(p);
                return;
            }
            float strength = est.Intensity * gate;
            Place(p, est.Position, s.maxTargetJump);

            FluxyTarget t = p.Target;
            p.Strength = strength;
            t.rateOverDistance = s.splatsPerMetre;
            float size = Mathf.Lerp(s.handSize.x, s.handSize.y, strength);
            t.scale = new Vector2(size, size);
            t.scaleRandomness = 0.1f;
            t.densityWeight = s.handDensity * strength;
            t.velocityWeight = s.velocityWeight * strength;
            float stir = s.handStir * strength;
            t.velocityScale = new Vector3(stir, stir, 0f);
            t.velocityNoise = s.handTurbulence * strength * strength;
            t.velocityNoiseOffset = 0.4f;
            t.color = Color.Lerp(s.PaletteColor(colourIndex), s.PaletteColor(s.accentColour),
                                 s.accentAtFullSpeed * strength);
        }

        private void DriveTorso(PointState p, MovementImpulseEstimator est, bool open, float gate, float pulse,
                                FluidDisruptionSettings s) {
            if (!open || !est.HasSample) {
                Disable(p);
                return;
            }
            float strength = est.Intensity * gate;
            Place(p, est.Position, s.maxTargetJump);

            FluxyTarget t = p.Target;
            p.Strength = Mathf.Max(strength, pulse);
            t.rateOverDistance = s.splatsPerMetre * 0.5f;
            // The detection swirl is a short burst of soft blobs in place, with curl noise for the swirl.
            t.rateOverTime = s.pulseRate * pulse;
            float size = Mathf.Lerp(s.torsoSize, s.pulseSize, Mathf.Clamp01(pulse));
            t.scale = new Vector2(size, size);
            t.positionRandomness = 0.45f * pulse;
            t.scaleRandomness = 0.2f;
            t.densityWeight = s.torsoDensity * strength + s.pulseDensity * pulse;
            t.velocityWeight = s.velocityWeight * Mathf.Max(strength, pulse);
            float stir = s.torsoStir * strength;
            t.velocityScale = new Vector3(stir, stir, 0f);
            t.velocityNoise = s.handTurbulence * 0.5f * strength * strength + s.pulseSwirl * pulse;
            t.velocityNoiseOffset = 0.3f;
            t.color = Color.Lerp(s.PaletteColor(3), s.PaletteColor(s.accentColour), Mathf.Clamp01(pulse * 0.6f + strength * 0.4f));
        }

        // Puts the target at the valid position. A first placement, or a jump larger than a gesture can
        // be, re-seats the target (disable then enable) so FluxyTarget carries no velocity across it.
        private static void Place(PointState p, Vector2 position, float maxJump) {
            FluxyTarget t = p.Target;
            bool reseat = !p.Active || (position - p.Last).magnitude > maxJump;
            t.transform.position = new Vector3(position.x, position.y, 0f);
            if (reseat) {
                t.enabled = false;
                t.enabled = true;
            } else if (!t.enabled) {
                t.enabled = true;
            }
            p.Last = position;
            p.Active = true;
        }

        private static void Disable(PointState p) {
            if (p.Target != null && p.Target.enabled) {
                p.Target.enabled = false;
            }
            p.Active = false;
            p.Strength = 0f;
        }

        private void OnDestroy() {
            FluidTargets.Destroy(left.Splat);
            FluidTargets.Destroy(right.Splat);
            FluidTargets.Destroy(torso.Splat);
        }
    }
}
