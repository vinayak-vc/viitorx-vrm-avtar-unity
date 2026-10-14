namespace HumanEcho {
    /// <summary>
    /// When a layer is really being drawn, for the HUD. The controller draws a layer only when its run-time
    /// switch (the S, E, B, P, L keys) is on AND the active look's own settings for it say enabled; showing
    /// only the switch would call a layer "on" in a look that does not use it.
    /// </summary>
    public static class HumanEchoLayerRules {
        /// <summary>Whether the look's settings enable the layer. A missing look, or a missing settings
        /// block, enables nothing.</summary>
        public static bool LookEnables(HumanEchoSettings look, HumanEchoLayerId layer) {
            if (look == null) {
                return false;
            }
            switch (layer) {
                case HumanEchoLayerId.Smoke:
                    // A fluid that runs without being drawn (the particle looks use it only for its velocity)
                    // is not a smoke layer on screen.
                    return look.smoke != null && look.smoke.enabled && look.smoke.drawn;
                case HumanEchoLayerId.Echo:
                    return look.echo != null && look.echo.enabled;
                case HumanEchoLayerId.Person:
                    return look.person != null && look.person.enabled;
                case HumanEchoLayerId.Particles:
                    return look.particles != null && look.particles.enabled;
                default:
                    return look.outline != null && look.outline.enabled;
            }
        }

        /// <summary>True only when the run-time switch is on and the look uses the layer.</summary>
        public static bool IsDrawn(HumanEchoSettings look, HumanEchoLayerId layer, bool switchedOn) {
            return switchedOn && LookEnables(look, layer);
        }
    }
}
