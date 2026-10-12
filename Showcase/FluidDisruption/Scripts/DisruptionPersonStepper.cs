using ShowcaseShared;

using VirtualMirror.Core;
using VirtualMirror.SkeletonShow;

namespace FluidDisruption {
    /// <summary>
    /// What ONE person's frame is for Fluid Disruption, called by the shared <see cref="ShowcaseCrowd{T}"/> for everyone it
    /// holds: adapter, state controller, movement tracks. Pure logic, so it is tested without a scene.
    /// </summary>
    public sealed class DisruptionPersonStepper : ICrowdStepper<DisruptionPerson> {
        private readonly FluidDisruptionSettings settings;
        private readonly ScreenSpaceMapper mapper;

        public DisruptionPersonStepper(FluidDisruptionSettings settings, ScreenSpaceMapper mapper) {
            this.settings = settings;
            this.mapper = mapper;
        }

        // The movement strengths fed to the state controller are last frame's: the state decides first, the tracks then see it.
        public void StepPerson(DisruptionPerson person, SkeletonPose pose, HandPose left, HandPose right,
                               TrackingTelemetry telemetry, bool fresh, float dt) {
            person.Adapter.Update(person.Body, pose, left, right, telemetry, fresh, dt, settings, mapper);
            bool trackedNow = fresh && person.Body.CoreFresh;
            person.Machine.Tick(trackedNow, person.Points.Left.Intensity, person.Points.Right.Intensity,
                                person.Points.Torso.Intensity, dt, settings);
            if (person.Machine.PersonAppeared) {
                // A new person: place every joint exactly and start every movement track afresh, so
                // nothing of whoever stood here before can become a velocity.
                person.Adapter.RequestSnap();
                person.Adapter.Update(person.Body, pose, left, right, telemetry, fresh, dt, settings, mapper);
                person.Points.Reset();
            }
            // Movement strength of each interaction point, from valid samples only.
            person.Points.Update(person.Body, trackedNow && person.Machine.PersonPresent, dt, settings);
        }

        // An absent person is finished once their state has settled back to calm (their gate has long closed by then).
        public bool IsFinished(DisruptionPerson person) {
            return person.Machine.State == DisruptionState.Calm;
        }
    }
}
