using System.Collections.Generic;

using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — WHAT THE FLOOR REMEMBERS, owned once so one person and a crowd write into the same
    /// memory.
    ///
    /// This is F-30's footprint bloom lifted out of <see cref="FootprintsExperience"/> unchanged —
    /// same dwell-to-petals curve, same 0.22 m merge radius, same ten-minute fade. It moved because
    /// the crowd scene is not a second kind of floor, it is the SAME floor with more feet on it, and
    /// two copies would drift apart the first time either was tuned.
    ///
    /// A MARK IS A PLACE, NOT A PERSON, and that is the entire reason the crowd version is safe. The
    /// position where a foot first settled is recorded and then never moved, and nothing about who
    /// was standing there is stored — no id, no colour, no owner. An identity switch, which nothing
    /// announces (measured live at 0.015 m per frame against a 0.35 m margin, zero events logged),
    /// therefore has nothing here to corrupt: the mark was never attached to an identity in the first
    /// place. Colour comes from how LONG somebody stood, which is a property of the place.
    ///
    /// WHY THE MARK NEVER MOVES ONCE PLACED. Floor contact was measured reliable to about 21 mm on a
    /// single subject at 1.4 m, and to about 81 mm on the seven-person clip — where F-32 showed the
    /// error is the crop migrating between dancers rather than depth drift. Whichever of those is
    /// acting, a continuously tracked mark would wander across the floor, and a mark that wanders is
    /// worse than one placed slightly wrong.
    ///
    /// <see cref="Build"/> is optional: with no parent no Unity objects are created and the
    /// bookkeeping still runs, so the merge and eviction rules are unit-testable without an Editor.
    /// </summary>
    public sealed class FloorMarks {
        /// <summary>Petals per mark. The bloom opens one petal at a time, so a viewer can SEE the
        /// count going up and works out for themselves that standing longer is what does it.</summary>
        public const int Petals = 9;

        /// <summary>A new mark is only started this far from every existing one, so shifting weight
        /// grows the mark you are already standing on instead of stamping a new one beside it.</summary>
        public float NewMarkDistance = 0.22f;

        /// <summary>Seconds of standing for a mark to reach full bloom.</summary>
        public float SecondsToFullBloom = 12f;

        /// <summary>How many marks the floor remembers before the oldest is dropped.</summary>
        public int MaxMarks = 48;

        private readonly List<Mark> marks = new List<Mark>();
        private Transform root;

        /// <summary>Whether <see cref="Build"/> was called. A plain bool rather than a null check on
        /// <see cref="root"/> on purpose: UnityEngine.Object overloads == with a native call, so
        /// comparing a Transform to null is an ECall and throws outside a player. This keeps the
        /// bookkeeping testable without changing what it does.</summary>
        private bool hasVisuals;
        private SkeletonShowPalette palette;
        private float totalDwell;

        private sealed class Mark {
            public Vector3 Position;
            public Transform Root;
            public Transform Core;
            public Material CoreMaterial;
            public Transform[] PetalVisuals;
            public Material[] PetalMaterials;
            public float Dwell;       // seconds of standing accumulated here
            public float Age;         // seconds since it was last grown, for the slow fade
            public float Spin;
        }

        public int Count {
            get {
                return marks.Count;
            }
        }

        /// <summary>Total seconds of standing this floor has recorded, across everybody.</summary>
        public float TotalDwell {
            get {
                return totalDwell;
            }
        }

        /// <summary>Create the container. Optional — without it the marks still exist, they are just
        /// not drawn, which is what makes this testable outside an Editor.</summary>
        public void Build(Transform parent, SkeletonShowPalette sharedPalette) {
            palette = sharedPalette;
            root = new GameObject("Marks").transform;
            root.SetParent(parent, false);
            hasVisuals = true;
        }

        /// <summary>
        /// Add dwell to the mark under this point, starting one if there is none.
        ///
        /// Returns the petal count when a new petal has just opened, or -1 when nothing changed — the
        /// caller turns that into a sound, because the audio layer belongs to the experience and one
        /// note per petal is the only feedback that rewards standing still without asking the person
        /// to look down.
        /// </summary>
        public int Grow(Vector3 at, float dt) {
            Mark nearest = null;
            float best = NewMarkDistance;
            int i = 0;
            while (i < marks.Count) {
                float d = Vector3.Distance(marks[i].Position, at);
                if (d < best) {
                    best = d;
                    nearest = marks[i];
                }
                i = i + 1;
            }
            if (nearest == null) {
                nearest = Create(at);
            }
            float span = Mathf.Max(0.5f, SecondsToFullBloom);
            int petalsBefore = Mathf.FloorToInt(Mathf.Clamp01(nearest.Dwell / span) * Petals + 0.001f);
            nearest.Dwell = nearest.Dwell + dt;
            nearest.Age = 0f;
            totalDwell = totalDwell + dt;
            int petalsAfter = Mathf.FloorToInt(Mathf.Clamp01(nearest.Dwell / span) * Petals + 0.001f);
            return petalsAfter > petalsBefore ? petalsAfter : -1;
        }

        // PURE ON PURPOSE - no Unity call appears anywhere in this method body, not even on a branch
        // that is not taken. The runtime refuses to JIT a whole method that references a native ECall
        // outside a player, so a single `Random.Range` here would make the merge and eviction rules
        // untestable however carefully it was guarded. The visuals therefore live in their own method.
        private Mark Create(Vector3 at) {
            Mark m = new Mark();
            m.Position = at;
            if (hasVisuals) {
                BuildMarkVisuals(m);
            }

            marks.Add(m);
            while (marks.Count > Mathf.Max(1, MaxMarks)) {
                Release(marks[0]);
                marks.RemoveAt(0);
            }
            return m;
        }

        private void BuildMarkVisuals(Mark m) {
            m.Spin = Random.Range(0f, Mathf.PI * 2f);
            m.Root = new GameObject("mark").transform;
            m.Root.SetParent(root, false);
            m.Root.position = m.Position;

            m.Core = ShowStage.Sphere(m.Root, "core", 0.06f, palette, palette.Warm, true,
                                      out m.CoreMaterial);
            m.Core.localScale = new Vector3(0.06f, 0.012f, 0.06f);   // flattened onto the floor

            m.PetalVisuals = new Transform[Petals];
            m.PetalMaterials = new Material[Petals];
            int i = 0;
            while (i < Petals) {
                m.PetalVisuals[i] = ShowStage.Sphere(m.Root, "petal_" + i, 0.05f, palette,
                                                     palette.Cool, true, out m.PetalMaterials[i]);
                m.PetalVisuals[i].gameObject.SetActive(false);
                i = i + 1;
            }
        }

        /// <summary>Advance the bloom, the spin and the fade, and drop marks that have faded out.</summary>
        public void Animate(float dt) {
            int i = 0;
            while (i < marks.Count) {
                Mark m = marks[i];
                m.Age = m.Age + dt;
                m.Spin = m.Spin + dt * 0.35f;

                float bloom = Mathf.Clamp01(m.Dwell / Mathf.Max(0.5f, SecondsToFullBloom));
                // Marks fade over ten minutes rather than persisting forever: an installation that
                // never forgets ends the day as an unreadable smear of overlapping blooms.
                float fade = Mathf.Clamp01(1f - (m.Age - 60f) / 600f);

                if (hasVisuals) {
                    float coreSize = 0.06f + 0.10f * bloom;
                    m.Core.localScale = new Vector3(coreSize, 0.012f, coreSize);
                    SkeletonShowPalette.SetColour(m.CoreMaterial,
                        Color.Lerp(palette.Warm, palette.Hot, bloom) * fade);

                    // Petals open one at a time as the dwell grows, so the mark counts the time
                    // visibly.
                    int open = Mathf.FloorToInt(bloom * Petals + 0.001f);
                    int p = 0;
                    while (p < Petals) {
                        bool show = p < open;
                        m.PetalVisuals[p].gameObject.SetActive(show && fade > 0.02f);
                        if (show) {
                            float angle = m.Spin + (Mathf.PI * 2f) * (p / (float)Petals);
                            float radius = 0.10f + 0.16f * bloom;
                            m.PetalVisuals[p].position = m.Position
                                + new Vector3(Mathf.Cos(angle) * radius, 0.004f,
                                              Mathf.Sin(angle) * radius);
                            float petalSize = 0.035f + 0.03f * bloom;
                            m.PetalVisuals[p].localScale = new Vector3(petalSize, 0.010f, petalSize);
                            SkeletonShowPalette.SetColour(m.PetalMaterials[p],
                                Color.Lerp(palette.Cool, palette.Warm, p / (float)Petals) * fade);
                        }
                        p = p + 1;
                    }
                }

                if (fade <= 0.01f) {
                    Release(m);
                    marks.RemoveAt(i);
                    continue;
                }
                i = i + 1;
            }
        }

        /// <summary>Wipe the floor.</summary>
        public void Clear() {
            int i = 0;
            while (i < marks.Count) {
                Release(marks[i]);
                i = i + 1;
            }
            marks.Clear();
            totalDwell = 0f;
        }

        /// <summary>Dwell at a mark, by index. For tests and for a HUD.</summary>
        public float DwellAt(int index) {
            return index >= 0 && index < marks.Count ? marks[index].Dwell : 0f;
        }

        /// <summary>Position of a mark, by index.</summary>
        public Vector3 PositionAt(int index) {
            return index >= 0 && index < marks.Count ? marks[index].Position : Vector3.zero;
        }

        private void Release(Mark m) {
            if (hasVisuals) {
                Object.Destroy(m.Root.gameObject);
            }
        }
    }
}
