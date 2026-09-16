namespace VirtualMirror.Core {
    /// <summary>
    /// F-26 §9.1 — one bone's POSE FIDELITY. Lives in Core, not Diagnostics: the RETARGET
    /// produces it and DIAGNOSTICS consumes it, so Core is the only place both assemblies
    /// can see it without a circular reference.
    ///
    /// One bone's POSE FIDELITY: the angle between the direction the tracked subject's
    /// segment points and the direction the rendered avatar bone actually ends up pointing.
    ///
    /// WHY THIS TYPE EXISTS AT ALL. Every avatar-side number this project has produced measures the
    /// avatar against ITSELF — yaw jitter, snap count, bone-length constancy, left/right swap count.
    /// F-19 reported all four as "AVATAR QUALITY: PROVEN GOOD", and every one of them is fully
    /// compatible with an avatar that is smoothly, stably and consistently in the WRONG pose, which
    /// is what the 2026-09-16 recording shows it doing. Nothing anywhere compared the avatar's pose
    /// to the SUBJECT's pose. This is that comparison.
    ///
    /// WHAT IT IS NOT. An angular error per bone is not a complete fidelity measure: two poses can
    /// agree on every bone DIRECTION and still place the hands in different places, because the
    /// avatar's bone LENGTHS are its own (F-26 §3). Direction error is what a rotation-driven rig can
    /// be held to; endpoint error is a different measurement and is deliberately not conflated here.
    /// </summary>
    public struct BoneFidelity {
        /// <summary>Bone name, matching the retarget's own trace names (e.g. "leftUpperArm").</summary>
        public string Bone;

        /// <summary>Angle in degrees between the wanted and the achieved direction. 0 = perfect.</summary>
        public float ErrorDeg;

        /// <summary>
        /// Lowest confidence of the two source landmarks. A bone whose source is not being observed
        /// says nothing about the retarget, so the analyser filters on this rather than averaging it
        /// in — "absence of observation is not evidence of failure" (AGENTS.md).
        /// </summary>
        public float MinConfidence;

        /// <summary>False when a source landmark or an avatar bone was missing; excluded, never zeroed.</summary>
        public bool Valid;

        /// <summary>
        /// Angle in degrees between the SUBJECT's segment and the rig's vertical (+Y of the rig
        /// frame). Paired with <see cref="RigDeg"/> this turns a bare error into a diagnosis: a
        /// channel whose source swings widely while the rig's stays put is FROZEN, which reads as a
        /// large but near-CONSTANT ErrorDeg and is otherwise indistinguishable from a fixed offset.
        ///
        /// It also exposes this metric's floor. The bound VRM's rest hips-&gt;upperChest chain is
        /// itself 8.8 deg off vertical, so a perfect trunk retarget cannot report 0 - the floor has
        /// to be read off these two columns rather than assumed away.
        /// </summary>
        public float SourceDeg;

        /// <summary>Angle in degrees between the rendered AVATAR bone and the rig's vertical (+Y).</summary>
        public float RigDeg;
    }
}
