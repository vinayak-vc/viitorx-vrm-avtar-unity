using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-30 EXPERIENCE 5 — FOOTPRINTS. Stand still and the floor starts to remember you. A mark
    /// grows under each planted foot, and the longer you stay the more it opens — a small bloom of
    /// petals that keeps growing while you are there and stays behind when you leave.
    ///
    /// WHY THIS IS THE ONE THAT CHANGES HOW A ROOM FEELS. Every other experience rewards MOVEMENT,
    /// which means the room is only interesting while somebody is performing in it. This one rewards
    /// PRESENCE — the opposite instinct — and it accumulates, so a room that has had people in it
    /// all day looks different from one that has just opened. A visitor who walks in sees the
    /// evidence of everyone before them, which is a thing a screen cannot usually say.
    ///
    /// IT IS BUILT ON <see cref="SkeletonPose.Planted"/>, which is the F-29 foot work doing the job
    /// it was measured for: a foot counts as planted when it is within 80 mm of the estimated floor
    /// AND moving slower than 0.35 m/s. The measured floor matters here more than anywhere else —
    /// marks are laid ON the floor plane, and before F-29 that plane was 81–127 mm out, so every
    /// mark would have floated or sunk.
    ///
    /// THE MARKS THEMSELVES LIVE IN <see cref="FloorMarks"/> as of F-34, unchanged. They moved
    /// because <see cref="CrowdFootprintsExperience"/> writes into the same kind of floor with
    /// several people on it, and the honest limit that shapes the design — a mark is a PLACE, placed
    /// once and never moved — is a property of the floor rather than of this scene.
    /// </summary>
    public sealed class FootprintsExperience : ExperienceBase {
        [Header("Footprints")]
        [Tooltip("Seconds of standing for a mark to reach full bloom.")]
        [SerializeField] private float secondsToFullBloom = 12f;
        [Tooltip("How many marks the floor remembers before the oldest fades away.")]
        [SerializeField] private int maxMarks = 48;

        private BodyRenderer body;
        private readonly FloorMarks floor = new FloorMarks();

        public override string Title {
            get {
                return "FOOTPRINTS";
            }
        }

        public override string Instruction {
            get {
                return "Stand still. The floor remembers how long you stayed.";
            }
        }

        protected override string ExtraKeyHelp() {
            return "      C clear the floor";
        }

        protected override void BuildExperience(Transform root) {
            body = new BodyRenderer(Palette);
            body.Build(root);
            body.Dim = 0.5f;
            floor.SecondsToFullBloom = secondsToFullBloom;
            floor.MaxMarks = maxMarks;
            floor.Build(root, Palette);
        }

        protected override void ReadExtraInput() {
            if (Input.GetKeyDown(KeyCode.C)) {
                floor.Clear();
            }
        }

        // NOT cleared when a new person arrives, unlike every other experience here. The whole point
        // is that the floor accumulates across visitors - resetting per person would throw away the
        // only thing this experience is trying to build.
        protected override void ResetExperience() {
        }

        protected override void Play(float deltaSeconds) {
            body.Render(Stage.Pose);
            SkeletonPose pose = Stage.Pose;

            // Only a real person leaves a trace. The attract figure is drawn standing on the floor,
            // and if it could leave marks an unattended machine would carpet the room overnight with
            // the footprints of somebody who was never there.
            if (ScoringAllowed && pose.HasFloor) {
                int i = 0;
                while (i < SkeletonPose.FootContacts.Length) {
                    if (pose.Planted[i]) {
                        int joint = SkeletonPose.FootContacts[i];
                        if (pose.Present[joint]) {
                            Vector3 at = pose.World[joint];
                            at.y = pose.FloorY;   // marks live ON the floor plane, not on the foot
                            int petal = floor.Grow(at, deltaSeconds);
                            if (petal >= 0) {
                                // One note per petal, climbing. Standing still is otherwise a silent
                                // activity, and this is the only feedback that rewards it without
                                // asking the person to look down.
                                Audio.Play(SoundCue.Bloom, petal, 0.5f);
                            }
                        }
                    }
                    i = i + 1;
                }
            }

            floor.Animate(deltaSeconds);
        }

        protected override string StatusText() {
            if (!ScoringAllowed) {
                return floor.Count > 0
                    ? "The floor remembers " + floor.Count + " place" + (floor.Count == 1 ? "" : "s")
                      + " somebody stood."
                    : "Step in front of the camera and stand still.";
            }
            int planted = 0;
            int i = 0;
            while (i < Stage.Pose.Planted.Length) {
                if (Stage.Pose.Planted[i]) {
                    planted = planted + 1;
                }
                i = i + 1;
            }
            string feet = planted > 0
                ? planted + " foot point" + (planted == 1 ? "" : "s") + " planted - growing"
                : "keep still to leave a mark";
            return feet
                   + "\nmarks " + floor.Count + " / " + maxMarks
                   + "      total time stood " + Mathf.FloorToInt(floor.TotalDwell) + " s";
        }
    }
}
