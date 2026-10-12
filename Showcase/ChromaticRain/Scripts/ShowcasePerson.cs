using ShowcaseShared;

namespace ChromaticRain {
    /// <summary>
    /// What Chromatic Rain keeps PER PERSON on top of the shared <see cref="TrackedPerson"/> (body and adapter): the A-F state
    /// machine, so each person fades in and out on their own, and the silhouette shapes. The slot logic, the placement and
    /// the lifetime live in <see cref="ShowcaseCrowd{T}"/>, shared with every other showcase.
    /// </summary>
    public sealed class ShowcasePerson : TrackedPerson {
        public readonly ShowcaseStateMachine Machine = new ShowcaseStateMachine();
        public readonly SilhouetteBuilder Builder = new SilhouetteBuilder();

        /// <summary>A person is shown and their torso was measured this frame.</summary>
        public bool Tracked {
            get {
                return InUse && Machine.Phase != ShowcasePhase.NoPerson && Machine.Phase != ShowcasePhase.Lost
                       && Body.CoreFresh;
            }
        }

        protected override void ResetExperience() {
            Machine.Reset();
            Builder.Reset();
        }
    }
}
