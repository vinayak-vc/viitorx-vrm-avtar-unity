using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — THE MOVEMENT THRESHOLDS, IN ONE PLACE, WITH THE CAVEAT ATTACHED.
    ///
    /// These three numbers decide whether a person is judged to be hitting something, touching it, or
    /// standing still. They were spread across three experiences and one pose class as bare literals,
    /// which made the thing that actually matters about them invisible:
    ///
    /// THEY WERE TUNED AT ROUGHLY 21 fps AND THE CROWD SCENES RUN AT ABOUT 16. The pose budget is
    /// 20.43 ms per person at p50 over 400 warm inferences (p90 20.89, p99 21.69), so three people
    /// cost about 61 ms and the stream lands near 16 fps; four cost about 82 ms and land near 12. A
    /// speed in m/s is not frame-rate coupled by definition, but the way it is MEASURED is:
    /// <see cref="SkeletonPose.Speed"/> is a smoothed difference between consecutive samples, so
    /// coarser sampling cuts the corner off a reversing movement and under-reports exactly the
    /// vigorous gesture a strike gate is looking for. The effect is one-sided — a crowd scene reads
    /// SLOWER than a single-person one, so these gates get harder to pass, not easier.
    ///
    /// SO: THESE HAVE NOT BEEN RE-MEASURED AT 16 fps. Doing that needs the camera, three people and a
    /// capture; it is not something a refactor can settle, and quietly nudging the numbers until they
    /// "feel right" would be inventing a measurement. They are gathered here so that when the
    /// measurement IS made it is one edit, and so that nobody re-tunes one scene and leaves the other
    /// two disagreeing about what "moving fast" means — which is the whole reason
    /// <see cref="SkeletonPose"/> computes Speed once for everybody.
    /// </summary>
    public static class ExperienceTuning {
        /// <summary>A joint must move at least this fast to POP something. F-28 measured a hand wave
        /// at 1–2 m/s, so this sits well below a deliberate swipe and well above idle drift.</summary>
        public const float StrikeSpeed = 0.9f;

        /// <summary>Below this, contact with an object is a touch rather than a hit. Without it,
        /// resting a hand against a ball slowly pushes it across the room.</summary>
        public const float TouchSpeed = 0.55f;

        /// <summary>At or below this a joint counts as still. One definition, owned by
        /// <see cref="SkeletonPose.PlantedMaxSpeed"/> because foot contact is where it was first
        /// measured; re-exposed here so the three thresholds can be read together.</summary>
        public const float StillSpeed = SkeletonPose.PlantedMaxSpeed;

        /// <summary>
        /// Whole-body energy at or below which a person counts as STILL, 0..1 on
        /// <see cref="SkeletonPose.Energy"/>.
        ///
        /// Energy is the mean joint speed divided by <see cref="SkeletonPose.FastSpeed"/> (2.5 m/s),
        /// so this is <see cref="StillSpeed"/> expressed on the same scale: 0.35 / 2.5 = 0.14. Derived
        /// rather than chosen, so "still" means the same thing to a foot and to a whole body.
        /// </summary>
        public const float StillEnergy = StillSpeed / SkeletonPose.FastSpeed;
    }
}
