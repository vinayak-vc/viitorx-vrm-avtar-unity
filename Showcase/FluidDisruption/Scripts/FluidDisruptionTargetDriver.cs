using System.Collections.Generic;

using Fluxy;

using UnityEngine;

namespace FluidDisruption {
    /// <summary>
    /// Drives the pooled <see cref="FluxyTarget"/>s of every person slot (left hand, right hand, torso, three per person) from
    /// their movement estimators. Nothing is created per frame: slot 0 uses the three targets of the scene, the others are
    /// created once, when the scene starts with room for more than one person.
    ///
    /// Why a still target injects nothing: every target splats only by distance travelled
    /// (<c>rateOverDistance</c>), and its velocity weight, velocity scale, density weight and turbulence
    /// are all multiplied by the smooth movement strength. A hand inside the dead zone therefore adds no
    /// dye and no velocity, and cannot act as an eraser either.
    ///
    /// Why a lost point cannot misbehave: a point without a valid sample has its target DISABLED where it
    /// is (never moved, never sent to the origin), and a re-enabled target re-seeds its old position in
    /// <c>FluxyTarget.OnEnable</c>, so it starts again from the new position with no velocity jump. A slot nobody uses
    /// is released the same way.
    /// </summary>
    public sealed class FluidDisruptionTargetDriver : MonoBehaviour {
        /// <summary>Targets per person: left hand, right hand, torso.</summary>
        public const int PointsPerPerson = 3;

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

        private PointState[] points = new PointState[0];
        private readonly List<FluxyTarget> created = new List<FluxyTarget>();
        private int slotCount = 1;

        public bool IsReady { get; private set; }

        /// <summary>How many person slots have targets.</summary>
        public int SlotCount {
            get {
                return slotCount;
            }
        }

        /// <summary>The targets this driver created for slots 1 and up. The container must be told about them: the ones in
        /// the scene are already in its list.</summary>
        public IReadOnlyList<FluxyTarget> CreatedTargets {
            get {
                return created;
            }
        }

        /// <summary>Position and strength of a target for the debug view; false while it is not injecting. Index 0-2 are the
        /// first slot's left hand, right hand and torso, 3-5 the second slot's, and so on.</summary>
        public bool TryGetTarget(int index, out Vector2 position, out float strength) {
            if (index < 0 || index >= points.Length) {
                position = Vector2.zero;
                strength = 0f;
                return false;
            }
            PointState state = points[index];
            position = state.Last;
            strength = state.Strength;
            return state.Active && state.Target != null && state.Target.enabled;
        }

        public bool Initialize(FluidDisruptionSettings s, Texture splatTexture, int people) {
            if (leftHandTarget == null || rightHandTarget == null || torsoTarget == null || splatMaterial == null) {
                Debug.LogError("[FluidDisruption] The target driver is missing a FluxyTarget or the splat "
                               + "material. Run Showcase > Fluid Disruption > Build Showcase Scene.", this);
                return false;
            }
            slotCount = Mathf.Clamp(people, 1, 6);
            points = new PointState[slotCount * PointsPerPerson];
            for (int slot = 0; slot < slotCount; slot++) {
                for (int kind = 0; kind < PointsPerPerson; kind++) {
                    PointState state = new PointState();
                    FluxyTarget target = slot == 0 ? SceneTarget(kind) : CreateTarget(slot, kind);
                    Bind(state, target, splatTexture, "FluidDisruption_" + PartName(kind) + "Splat" + slot + " (runtime)");
                    if (kind == 2) {
                        state.Target.scale = new Vector2(s.torsoSize, s.torsoSize);
                    }
                    points[slot * PointsPerPerson + kind] = state;
                }
            }
            IsReady = true;
            return true;
        }

        private FluxyTarget SceneTarget(int kind) {
            return kind == 0 ? leftHandTarget : (kind == 1 ? rightHandTarget : torsoTarget);
        }

        private static string PartName(int kind) {
            return kind == 0 ? "LeftHand" : (kind == 1 ? "RightHand" : "Torso");
        }

        // A target of another person: its own GameObject, because FluXY splats at the target's transform.
        private FluxyTarget CreateTarget(int slot, int kind) {
            GameObject host = new GameObject("FluidTarget_person" + slot + "_" + PartName(kind));
            host.transform.SetParent(transform, false);
            FluxyTarget target = host.AddComponent<FluxyTarget>();
            target.enabled = false;
            created.Add(target);
            return target;
        }

        private void Bind(PointState state, FluxyTarget target, Texture splatTexture, string materialName) {
            state.Target = target;
            state.Splat = FluidTargets.Configure(target, splatMaterial, splatTexture, noiseTexture, materialName);
            state.Active = false;
            state.Strength = 0f;
        }

        /// <summary>Switch every target of every slot off (the fluid itself is untouched).</summary>
        public void Release() {
            for (int i = 0; i < points.Length; i++) {
                Disable(points[i]);
            }
        }

        /// <summary>Switch one slot's three targets off: nobody stands there.</summary>
        public void ReleaseSlot(int slot) {
            if (slot < 0 || slot >= slotCount) {
                return;
            }
            for (int kind = 0; kind < PointsPerPerson; kind++) {
                Disable(points[slot * PointsPerPerson + kind]);
            }
        }

        /// <summary>One frame of one person's slot, before FluXY's own LateUpdate.</summary>
        public void Tick(int slot, FluidDisruptionStateController state, MovementImpulseEstimator leftHand,
                         MovementImpulseEstimator rightHand, MovementImpulseEstimator torsoPoint,
                         FluidDisruptionSettings s) {
            if (!IsReady || slot < 0 || slot >= slotCount) {
                return;
            }
            float gate = state.Gate;
            float pulse = state.Pulse;
            bool open = gate > 0.01f;
            int baseIndex = slot * PointsPerPerson;

            DriveHand(points[baseIndex], leftHand, open, gate, s, PersonColour(s.leftHandColour, slot));
            DriveHand(points[baseIndex + 1], rightHand, open, gate, s, PersonColour(s.rightHandColour, slot));
            DriveTorso(points[baseIndex + 2], torsoPoint, open || pulse > 0.001f, gate, pulse, s);
        }

        /// <summary>Each person's hands drift to the next colour of the palette, so two people do not paint the same colour.
        /// Slot 0 is unchanged. Index 0 of the palette is the midnight background tone and is never used for dye.</summary>
        public static int PersonColour(int colourIndex, int slot) {
            if (slot <= 0) {
                return colourIndex;
            }
            return 1 + (((colourIndex - 1 + slot) % 6) + 6) % 6;
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
            for (int i = 0; i < points.Length; i++) {
                FluidTargets.Destroy(points[i].Splat);
            }
        }
    }
}
