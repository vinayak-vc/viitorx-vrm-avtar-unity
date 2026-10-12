using ShowcaseShared;

namespace FluidDisruption {
    /// <summary>
    /// What Fluid Disruption keeps PER PERSON on top of the shared <see cref="TrackedPerson"/> (body and adapter): the A-F
    /// state controller, so each person's detection swirl and injection gate run on their own, and the movement track of
    /// their two hands and torso. Which person sits in which slot, where they stand and when a slot is freed is the shared
    /// <see cref="ShowcaseCrowd{T}"/>'s job, the same one Chromatic Rain uses.
    /// </summary>
    public sealed class DisruptionPerson : TrackedPerson {
        public readonly FluidDisruptionStateController Machine = new FluidDisruptionStateController();
        public readonly InteractionPointTracker Points = new InteractionPointTracker();

        /// <summary>A person is being followed and their torso was measured this frame.</summary>
        public bool Tracked {
            get {
                return InUse && Machine.PersonPresent && Body.CoreFresh;
            }
        }

        protected override void ResetExperience() {
            Machine.Reset();
            Points.Reset();
        }
    }
}
