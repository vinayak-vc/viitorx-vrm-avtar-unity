using System.Collections.Generic;

using UnityEngine;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — WHICH COLOUR BELONGS TO WHICH PERSON, decided once for every crowd experience.
    ///
    /// WHY THIS IS SHARED RATHER THAN PER-SCENE. A visitor who is cyan in one scene and orange in the
    /// next has been told, wrongly, that the system lost them. One table and one assignment order, so
    /// a person keeps their colour across the whole installation for as long as the tracker keeps
    /// their identity.
    ///
    /// COLOUR IS ASSIGNED PER TRACK ID, NEVER PER LIST POSITION. <see cref="SkeletonShow.TrackedStage"/>
    /// publishes the crowd most-established-first and re-sorts it every frame, so a position-keyed
    /// colour would make everybody change colour whenever anybody joined or left. That reads as a bug
    /// even when the tracking is perfect.
    ///
    /// WHAT THIS DOES NOT CLAIM. A colour follows an ID, and an ID is the sidecar's opinion about who
    /// somebody is. When that opinion is wrong — two people trading identities — two people trade
    /// colours, and nothing here can detect it: the live two-person session measured the migration at
    /// 0.015 m per frame against a 0.35 m margin, with no event logged. Colour is therefore the ONLY
    /// thing in the crowd experiences allowed to follow an id, precisely because a wrong colour is an
    /// embarrassment and a wrong score would be a lie.
    /// </summary>
    public sealed class CrowdPalette {
        /// <summary>Deliberately far apart in hue: two people whose colours are similar cannot be
        /// told apart at a glance, which defeats the whole point. HDR values, so Bloom lifts them —
        /// see <see cref="SkeletonShow.SkeletonShowPalette"/>.</summary>
        public static readonly Color[] Colours = {
            new Color(0.25f, 1.90f, 1.10f, 1f),   // cyan
            new Color(2.30f, 0.80f, 0.25f, 1f),   // orange
            new Color(0.70f, 0.60f, 2.40f, 1f),   // violet
            new Color(0.40f, 2.10f, 0.50f, 1f),   // green
            new Color(2.40f, 0.40f, 1.30f, 1f),   // magenta
            new Color(2.20f, 2.00f, 0.40f, 1f),   // yellow
            new Color(0.30f, 1.20f, 2.40f, 1f),   // blue
            new Color(2.40f, 1.60f, 1.40f, 1f),   // warm white
        };

        /// <summary>A long unattended session would otherwise remember every visitor who ever stood
        /// in front of the camera. Ids are never reused, so once the table is this large the old
        /// entries can only be people who have gone.</summary>
        private const int MaxRemembered = 64;

        private readonly Dictionary<int, int> indexOfId = new Dictionary<int, int>();
        private int next;

        /// <summary>How many distinct people this palette is currently remembering.</summary>
        public int Remembered {
            get {
                return indexOfId.Count;
            }
        }

        /// <summary>The colour index for a track id, assigned on first sight and then stable.</summary>
        public int IndexFor(int id) {
            int index;
            if (!indexOfId.TryGetValue(id, out index)) {
                index = next % Colours.Length;
                next = next + 1;
                indexOfId[id] = index;
                if (indexOfId.Count > MaxRemembered) {
                    indexOfId.Clear();
                    indexOfId[id] = index;
                }
            }
            return index;
        }

        /// <summary>The colour for a track id.</summary>
        public Color ColourFor(int id) {
            return Colours[IndexFor(id)];
        }

        /// <summary>Forget every assignment. Bound to an experience reset, so a fresh session starts
        /// at the first colour rather than wherever the last one left off.</summary>
        public void Clear() {
            indexOfId.Clear();
            next = 0;
        }
    }
}
