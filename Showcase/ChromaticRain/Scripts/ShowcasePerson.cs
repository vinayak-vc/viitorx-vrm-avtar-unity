namespace ChromaticRain {
    /// <summary>
    /// Everything the showcase keeps PER PERSON: the view-space body, the adapter that smooths it, the A-F
    /// state machine (so each person fades in and out on their own), and the silhouette shapes.
    /// A slot is keyed on the sidecar's track id, which is stable while the person is held and never reused,
    /// so a person keeps their own history and nothing leaks to the next visitor.
    /// </summary>
    public sealed class ShowcasePerson {
        public readonly ShowcaseBody Body = new ShowcaseBody();
        public readonly ShowcasePoseAdapter Adapter = new ShowcasePoseAdapter();
        public readonly ShowcaseStateMachine Machine = new ShowcaseStateMachine();
        public readonly SilhouetteBuilder Builder = new SilhouetteBuilder();

        /// <summary>The track id this slot follows; meaningful only while <see cref="InUse"/>.</summary>
        public int Id;

        /// <summary>The slot belongs to somebody (present now, or still fading out).</summary>
        public bool InUse;

        /// <summary>The source delivered this person this frame.</summary>
        public bool Present;

        /// <summary>A person is shown and their torso was measured this frame.</summary>
        public bool Tracked {
            get {
                return InUse && Machine.Phase != ShowcasePhase.NoPerson && Machine.Phase != ShowcasePhase.Lost
                       && Body.CoreFresh;
            }
        }

        /// <summary>Hands the slot to a new person, with nothing of the previous one left.</summary>
        public void Begin(int id) {
            Id = id;
            InUse = true;
            Present = false;
            Adapter.LateralShift = 0f;
            Reset();
        }

        /// <summary>Forgets the body and every state, keeping the slot's owner.</summary>
        public void Reset() {
            Adapter.Reset(Body);
            Machine.Reset();
            Builder.Reset();
        }

        /// <summary>The slot is free again.</summary>
        public void Release() {
            InUse = false;
            Present = false;
            Id = 0;
            Adapter.LateralShift = 0f;
            Reset();
        }
    }

    /// <summary>
    /// Which slot a track id lives in. Pure logic, so it is tested without a scene: an id that already has a slot
    /// keeps it, a new id takes the first free slot, and when every slot is taken the newcomer is not drawn
    /// (people are offered most-established first, so the people who were there first keep their slot).
    /// </summary>
    public static class PersonSlots {
        public const int NoSlot = -1;

        /// <summary>The id the single-person path uses: there is exactly one person and no sidecar id.</summary>
        public const int SingleId = -1;

        public static int Acquire(ShowcasePerson[] people, int id) {
            for (int i = 0; i < people.Length; i++) {
                if (people[i].InUse && people[i].Id == id) {
                    return i;
                }
            }
            for (int i = 0; i < people.Length; i++) {
                if (!people[i].InUse) {
                    people[i].Begin(id);
                    return i;
                }
            }
            return NoSlot;
        }
    }
}
