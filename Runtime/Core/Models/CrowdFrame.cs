using System.Collections.Generic;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>One tracked person inside a <see cref="CrowdFrame"/>: a stable id plus their pose.</summary>
    public sealed class PersonPose {
        /// <summary>The sidecar's track id. STABLE across frames for as long as the tracker holds
        /// this person, and NEVER reused once retired — so a consumer may safely key state (a score,
        /// a colour, a trail) on it without that state leaking to a different visitor later.</summary>
        public int Id;

        /// <summary>The sidecar's track state: CONFIRMED (seen this frame) or LOST (held through an
        /// occlusion on prediction). A LOST person is still worth DRAWING — flickering a body out
        /// because someone walked in front of them looks far worse than a briefly stale pose — but
        /// is not worth SCORING.</summary>
        public string State;

        public readonly PoseFrame Pose = new PoseFrame();

        public bool IsConfirmed {
            get {
                return State == null || State == "CONFIRMED";
            }
        }
    }

    /// <summary>
    /// F-32 — EVERY PERSON IN FRAME, not just one.
    ///
    /// WHY THIS IS SEPARATE FROM <see cref="PoseFrame"/> RATHER THAN REPLACING IT. PoseFrame is the
    /// single-person tracking contract that the VRM mirror app, the retarget, the P1-3 buffer and
    /// all nine experience scenes consume. Multi-person is additive: the sidecar still publishes the
    /// most-established person at the root of the payload in the exact single-person shape, so every
    /// existing consumer keeps working untouched and unaware. This type carries the REST.
    ///
    /// THE ID IS THE WHOLE POINT. Detecting several people is the easy half — a detector does that
    /// per frame with no memory. What makes an experience possible is that person #3 is the SAME
    /// person #3 they were two seconds ago, so a score, a colour, or a drawing can belong to them.
    /// The sidecar's PersonTracker owns that guarantee; this type just carries it across the wire.
    ///
    /// Reused in place every frame, like every other frame type here. The list is stable storage:
    /// entries are refilled rather than reallocated, so a busy scene does not churn the GC.
    /// </summary>
    public sealed class CrowdFrame {
        /// <summary>Hard ceiling on people carried. The sidecar caps pose solves far below this
        /// (measured: 20.7 ms each, so 2-3 people at a usable frame rate), but a consumer must not
        /// be able to allocate unboundedly because a sender misbehaved.</summary>
        public const int MaxPeople = 8;

        private readonly List<PersonPose> people = new List<PersonPose>();
        private int count;

        private double timestampSeconds;
        private bool isValid;

        /// <summary>How many people the DETECTOR saw this frame, which can exceed <see cref="Count"/>
        /// — the sidecar poses only as many as the GPU budget allows. Surfacing both is what lets a
        /// HUD say "6 people seen, 3 tracked" instead of quietly implying nobody else is there.</summary>
        public int DetectedCount;

        /// <summary>How many people the sidecar's tracker is holding, including any it could not
        /// afford to pose this frame.</summary>
        public int TrackedCount;

        public bool IsValid {
            get {
                return isValid;
            }
        }

        public double TimestampSeconds {
            get {
                return timestampSeconds;
            }
        }

        /// <summary>People carried in this frame, most-established first. The sidecar sorts them, so
        /// index 0 is the person most likely to still be here next frame — which is also the person
        /// published at the payload root for single-person consumers.</summary>
        public int Count {
            get {
                return count;
            }
        }

        public PersonPose Get(int index) {
            if (index < 0 || index >= count) {
                return null;
            }
            return people[index];
        }

        /// <summary>Find a person by their stable track id, or null. This is how an experience looks
        /// up the state it keyed on an id last frame.</summary>
        public PersonPose ById(int id) {
            int i = 0;
            while (i < count) {
                if (people[i].Id == id) {
                    return people[i];
                }
                i = i + 1;
            }
            return null;
        }

        /// <summary>Begin filling a frame. Returns the slot to write into, or null when the cap is
        /// reached. Slots are recycled rather than allocated.</summary>
        public PersonPose Add(int id, string state) {
            if (count >= MaxPeople) {
                return null;
            }
            while (people.Count <= count) {
                people.Add(new PersonPose());
            }
            PersonPose p = people[count];
            p.Id = id;
            p.State = state;
            count = count + 1;
            return p;
        }

        public void Begin() {
            count = 0;
        }

        public void SetMeta(double timestamp, bool valid) {
            timestampSeconds = timestamp;
            isValid = valid;
        }

        public void MarkInvalid(double timestamp) {
            timestampSeconds = timestamp;
            isValid = false;
            count = 0;
            DetectedCount = 0;
            TrackedCount = 0;
        }

        /// <summary>Deep-copy into <paramref name="target"/>. Used to hand a receive-thread snapshot
        /// to the main thread; the pose data itself is copied, not aliased, because the worker will
        /// overwrite its own instance on the very next datagram.</summary>
        public void CopyTo(CrowdFrame target) {
            if (target == null) {
                return;
            }
            target.Begin();
            int i = 0;
            while (i < count) {
                PersonPose src = people[i];
                PersonPose dst = target.Add(src.Id, src.State);
                if (dst != null) {
                    int j = 0;
                    while (j < PoseFrame.LandmarkCount) {
                        dst.Pose.SetLandmark(j, src.Pose.GetLandmark((JointId)j));
                        j = j + 1;
                    }
                    if (src.Pose.HasRootPosition) {
                        dst.Pose.SetRootPosition(src.Pose.RootPositionMetres);
                    } else {
                        dst.Pose.ClearRootPosition();
                    }
                    dst.Pose.SetMeta(src.Pose.TimestampSeconds, src.Pose.IsValid);
                }
                i = i + 1;
            }
            target.timestampSeconds = timestampSeconds;
            target.isValid = isValid;
            target.DetectedCount = DetectedCount;
            target.TrackedCount = TrackedCount;
        }
    }
}
