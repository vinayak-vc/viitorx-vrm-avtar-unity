namespace HumanEcho {
    /// <summary>Whether the picture is flipped left to right for display.</summary>
    public enum MirrorMode {
        /// <summary>Flip live sources (camera, stream) that the sender did not already flip; leave video
        /// files alone. The Cutout showcase's rule.</summary>
        Auto = 0,
        On = 1,
        Off = 2
    }

    /// <summary>The layers that can be switched on and off at run time (in addition to what the active look's
    /// settings say).</summary>
    public enum HumanEchoLayerId {
        Smoke = 0,
        Echo = 1,
        Person = 2,
        Particles = 3,
        Outline = 4
    }
}
