using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace ShowcaseShared {
    /// <summary>
    /// Everything a showcase keeps for ONE tracked person that is not about the experience itself: the view-space body and
    /// the adapter that smooths it. An experience derives from this and adds its own per-person state (a silhouette, a state
    /// machine, interaction points, fluid targets) in <see cref="ResetExperience"/>. A slot is keyed on the sidecar's track id,
    /// which is stable while the person is held and never reused, so a person keeps their own history and nothing leaks to
    /// the next visitor.
    /// </summary>
    public abstract class TrackedPerson {
        public readonly ShowcaseBody Body = new ShowcaseBody();
        public readonly ShowcasePoseAdapter Adapter = new ShowcasePoseAdapter();

        /// <summary>The track id this slot follows; meaningful only while <see cref="InUse"/>.</summary>
        public int Id;

        /// <summary>The slot belongs to somebody (present now, or still fading out).</summary>
        public bool InUse;

        /// <summary>The source delivered this person this frame.</summary>
        public bool Present;

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
            ResetExperience();
        }

        /// <summary>The slot is free again.</summary>
        public void Release() {
            InUse = false;
            Present = false;
            Id = 0;
            Adapter.LateralShift = 0f;
            Reset();
        }

        /// <summary>The experience forgets its own per-person state (state machine, silhouette, movement tracks).</summary>
        protected abstract void ResetExperience();
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

        public static int Acquire<T>(T[] people, int id) where T : TrackedPerson {
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

    /// <summary>Where a crowd comes from: the people the live tracking holds this frame, most-established first. The shared
    /// <see cref="ShowcaseTrackingSource"/> is the real one; a test supplies its own.</summary>
    public interface ICrowdSource {
        /// <summary>True while the live sidecar is publishing a crowd. False for a single-person sender and the simulation.</summary>
        bool CrowdActive { get; }

        int LiveBodyCount { get; }

        ShowcaseLiveBody LiveBody(int index);
    }

    /// <summary>What an experience does for one person each frame; <see cref="ShowcaseCrowd{T}"/> calls it.</summary>
    public interface ICrowdStepper<T> where T : TrackedPerson {
        /// <summary>One pipeline step for one person: adapter, state, whatever the experience derives. A person who is not
        /// delivered this frame is stepped with a null pose and <paramref name="fresh"/> false until they are finished.</summary>
        void StepPerson(T person, SkeletonPose pose, HandPose left, HandPose right, TrackingTelemetry telemetry,
                        bool fresh, float dt);

        /// <summary>True when an absent person has faded out of the experience and their slot may be freed.</summary>
        bool IsFinished(T person);
    }

    /// <summary>
    /// Who is on screen this frame, for ANY experience. With a crowd upstream every tracked person gets a slot keyed on the
    /// sidecar's track id. With a single-person sender, or the simulation, there is one person in the slot keyed
    /// <see cref="PersonSlots.SingleId"/>. Each person is placed by their own sideways position (the stage places everyone
    /// relative to the first), and a person who disappears keeps their slot while the experience fades them out.
    ///
    /// This is the one place that knows how a crowd reaches an experience: a showcase supplies a <see cref="TrackedPerson"/>
    /// subclass and an <see cref="ICrowdStepper{T}"/>, and every change to how people are tracked, placed or kept applies to all.
    /// </summary>
    public sealed class ShowcaseCrowd<T> where T : TrackedPerson {
        private readonly T[] people;

        public ShowcaseCrowd(T[] people) {
            this.people = people;
        }

        public T[] People {
            get {
                return people;
            }
        }

        public int Count {
            get {
                return people.Length;
            }
        }

        public T At(int slot) {
            return people[slot];
        }

        /// <summary>The first person that is in use, or the first slot when nobody is: what a single-person view shows.</summary>
        public T Primary {
            get {
                return people[PrimaryIndex];
            }
        }

        /// <summary>The slot of <see cref="Primary"/>.</summary>
        public int PrimaryIndex {
            get {
                for (int i = 0; i < people.Length; i++) {
                    if (people[i].InUse) {
                        return i;
                    }
                }
                return 0;
            }
        }

        public void ReleaseAll() {
            for (int i = 0; i < people.Length; i++) {
                people[i].Release();
            }
        }

        public void Update(ICrowdSource tracking, bool sourceFresh, SkeletonPose pose, HandPose left,
                           HandPose right, TrackingTelemetry telemetry, float dt, ICrowdStepper<T> stepper) {
            for (int i = 0; i < people.Length; i++) {
                people[i].Present = false;
            }

            if (tracking.CrowdActive) {
                for (int b = 0; b < tracking.LiveBodyCount; b++) {
                    ShowcaseLiveBody live = tracking.LiveBody(b);
                    int slot = PersonSlots.Acquire(people, live.Id);
                    if (slot == PersonSlots.NoSlot) {
                        continue;
                    }
                    T person = people[slot];
                    person.Present = true;
                    // Hands and the trust channel exist for the first person only; the rest are drawn from the body.
                    UpdateLateralShift(person, live);
                    stepper.StepPerson(person, live.Pose, live.Primary ? left : null, live.Primary ? right : null,
                                       live.Primary ? telemetry : null, true, dt);
                }
            } else {
                int slot = PersonSlots.Acquire(people, PersonSlots.SingleId);
                if (slot != PersonSlots.NoSlot) {
                    T person = people[slot];
                    person.Present = true;
                    stepper.StepPerson(person, pose, left, right, telemetry, sourceFresh, dt);
                }
            }

            for (int i = 0; i < people.Length; i++) {
                T person = people[i];
                if (!person.InUse || person.Present) {
                    continue;
                }
                stepper.StepPerson(person, null, null, null, null, false, dt);
                if (stepper.IsFinished(person)) {
                    person.Release();
                }
            }
        }

        // Where the camera sees this person, sideways, against where the stage staged them. Updated only while both hips
        // are measured (the centre is the mid-hip) or the sender says it anchored the root itself (F-60); otherwise the
        // last good value stays, so a person whose hips are briefly lost does not slide across the screen.
        private static void UpdateLateralShift(T person, ShowcaseLiveBody live) {
            if (!live.HasRoot
                || (!live.Pose.RootEstimated && (!live.Pose.Present[Joint.LeftHip] || !live.Pose.Present[Joint.RightHip]))) {
                return;
            }
            float shift = live.RootX - live.Pose.Centre.x;
            if (float.IsNaN(shift) || float.IsInfinity(shift)) {
                return;
            }
            person.Adapter.LateralShift = shift;
        }
    }
}
