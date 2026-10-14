using ChromaticRain.EditorTools;

using UnityEditor;

using UnityEngine;

namespace HumanEcho.EditorTools {
    /// <summary>
    /// Creates the three Human Echo looks (one per reference clip) as <see cref="HumanEchoSettings"/> assets
    /// under Settings/. The values are first guesses read off the settings classes' tooltips and the three
    /// clips; they are meant to be tuned in the Inspector while the scene plays, and the tuned assets are the
    /// product.
    ///
    /// <see cref="EnsureLooks"/> only CREATES what is missing: an existing asset is returned as it is, so
    /// re-running the scene builder never discards an edit. <see cref="ResetLooks"/> (the menu item) is the
    /// one way back to these values: it copies a freshly built look over each asset in place with
    /// <see cref="EditorUtility.CopySerialized"/>, as Fluid Disruption does, so the asset's GUID and every
    /// reference to it survive (deleting assets through <c>AssetDatabase.DeleteAsset</c> is blocked in this
    /// environment, and would break the scene's references).
    /// </summary>
    public static class HumanEchoPresetBuilder {
        public const string SettingsFolder = HumanEchoSceneBuilder.Root + "/Settings";
        public const string RainbowSmokePath = SettingsFolder + "/HumanEchoLook_RainbowSmoke.asset";
        public const string RedEchoPath = SettingsFolder + "/HumanEchoLook_RedEcho.asset";
        public const string ParticleBodyPath = SettingsFolder + "/HumanEchoLook_ParticleBody.asset";

        /// <summary>The look assets in the order the controller cycles them: clip 27845, clip 14008, clip
        /// 11367.</summary>
        public static readonly string[] LookPaths = { RainbowSmokePath, RedEchoPath, ParticleBodyPath };

        [MenuItem("Showcase/Human Echo/Reset Looks To Defaults")]
        public static void ResetLooks() {
            // Overwriting the assets cannot be undone and the menu sits next to harmless items, so it asks
            // first.
            if (!EditorUtility.DisplayDialog("Reset Human Echo looks",
                                             "This overwrites the three look assets with the values in the code. Any tuning of them is lost.",
                                             "Reset", "Cancel")) {
                return;
            }
            EnsureLooks();
            for (int i = 0; i < LookPaths.Length; i++) {
                HumanEchoSettings asset = AssetDatabase.LoadAssetAtPath<HumanEchoSettings>(LookPaths[i]);
                if (asset == null) {
                    Debug.LogWarning("[HumanEcho] Could not load " + LookPaths[i] + "; it was not reset.");
                    continue;
                }
                HumanEchoSettings fresh = ScriptableObject.CreateInstance<HumanEchoSettings>();
                ApplyPreset(fresh, i);
                // CopySerialized copies the object name too; keep the asset's own so the Project window and
                // the HUD fallback do not change.
                fresh.name = asset.name;
                EditorUtility.CopySerialized(fresh, asset);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[HumanEcho] The three looks were reset to the code defaults; any tuning is "
                             + "gone (" + SettingsFolder + ").");
        }

        /// <summary>
        /// Returns the three look assets in cycling order, creating the ones that do not exist yet. An asset
        /// that exists is never modified, so tuning survives a rebuild of the scene.
        /// </summary>
        public static HumanEchoSettings[] EnsureLooks() {
            ShowcaseBuilderUtil.EnsureFolders(HumanEchoSceneBuilder.Root, new string[] { "Settings" });
            HumanEchoSettings[] looks = new HumanEchoSettings[LookPaths.Length];
            bool created = false;
            for (int i = 0; i < LookPaths.Length; i++) {
                HumanEchoSettings asset = AssetDatabase.LoadAssetAtPath<HumanEchoSettings>(LookPaths[i]);
                if (asset == null) {
                    asset = ScriptableObject.CreateInstance<HumanEchoSettings>();
                    ApplyPreset(asset, i);
                    AssetDatabase.CreateAsset(asset, LookPaths[i]);
                    created = true;
                    Debug.Log("[HumanEcho] Created look " + LookPaths[i]);
                }
                looks[i] = asset;
            }
            if (created) {
                AssetDatabase.SaveAssets();
            }
            return looks;
        }

        private static void ApplyPreset(HumanEchoSettings look, int index) {
            switch (index) {
                case 0:
                    ApplyRainbowSmoke(look);
                    break;
                case 1:
                    ApplyRedEcho(look);
                    break;
                default:
                    ApplyParticleBody(look);
                    break;
            }
        }

        // Clip 27845: a group dancing in front of a projection wall. True-colour people, rainbow dotted smoke
        // rising off them, stippled rainbow ghosts of where they were a moment ago, white-hot flashes, and a
        // jagged colour-cycling neon outline.
        private static void ApplyRainbowSmoke(HumanEchoSettings look) {
            look.displayName = "Rainbow Smoke";
            look.notes = "Clip 27845: true-colour cutouts, rainbow dotted smoke and stippled echo ghosts, "
                         + "a jagged colour-cycling neon outline.";
            look.viewHeightMetres = 2.4f;
            look.backgroundColor = new Color(0.012f, 0.012f, 0.03f, 1f);

            PersonSettings person = look.person;
            person.enabled = true;
            person.colorMode = PersonColorMode.Picture;
            person.tint = Color.white;
            person.opacity = 1f;
            person.brightness = 1.05f;
            person.contrast = 1.05f;
            person.flash = 0.35f;
            person.edgeContrast = 1.5f;
            person.rim = false;

            SmokeSettings smoke = look.smoke;
            smoke.enabled = true;
            smoke.look = SmokeLook.Dots;
            smoke.hueSpeed = 0.09f;
            smoke.hueSpread = 0.55f;
            smoke.saturation = 0.9f;
            smoke.value = 1f;
            smoke.pictureMix = 0.3f;
            smoke.gain = 2.4f;
            smoke.opacity = 1f;
            smoke.dotsPerScreenHeight = 150f;
            smoke.dotSoftness = 0.18f;
            smoke.dotFlow = 0.6f;
            smoke.buoyancy = 0.7f;
            smoke.dissipation = 0.3f;

            EchoSettings echo = look.echo;
            echo.enabled = true;
            echo.copies = 6;
            echo.spacingSeconds = 0.12f;
            echo.colorMode = EchoColorMode.HueRamp;
            echo.hueStart = 0f;
            echo.hueStep = 0.12f;
            echo.hueDrift = 0.06f;
            echo.saturation = 0.85f;
            echo.value = 1f;
            echo.opacityNewest = 0.7f;
            echo.opacityOldest = 0.12f;
            echo.offsetPerCopy = Vector2.zero;
            echo.scaleStep = 0.012f;
            echo.look = EchoLook.Stipple;
            echo.dotsPerScreenHeight = 150f;
            echo.dotSoftness = 0.2f;
            echo.waveAmount = 0f;
            echo.gain = 1.3f;
            echo.edgeContrast = 1.5f;

            OutlineSettings outline = look.outline;
            outline.enabled = true;
            outline.colorMode = OutlineColorMode.HueCycle;
            outline.hueSpeed = 0.2f;
            outline.widthThousandths = 1.8f;
            outline.softness = 0.3f;
            outline.intensity = 2.6f;
            outline.haloThousandths = 8f;
            outline.haloStrength = 0.4f;
            outline.jitterThousandths = 3f;
            outline.jitterFrequency = 28f;
            outline.jitterSpeed = 16f;
            outline.delayCopies = 0;

            look.particles.enabled = false;
        }

        // Clip 14008: a black stage. A white / grey figure with a red rim, flat red copies of it trailing
        // sideways in wavy ribbons, and storms of big red spheres and small dots. No smoke, no outline, no
        // floor.
        private static void ApplyRedEcho(HumanEchoSettings look) {
            look.displayName = "Red Echo";
            look.notes = "Clip 14008: a white figure with a red rim on black, flat red echo copies trailing "
                         + "sideways, red spheres and dots.";
            look.viewHeightMetres = 2.4f;
            look.backgroundColor = new Color(0f, 0f, 0f, 1f);

            PersonSettings person = look.person;
            person.enabled = true;
            person.colorMode = PersonColorMode.Grayscale;
            person.tint = Color.white;
            person.opacity = 1f;
            person.brightness = 1.1f;
            person.contrast = 1.2f;
            person.flash = 0f;
            person.edgeContrast = 1.6f;
            person.rim = true;
            person.rimColor = new Color(1f, 0.08f, 0.05f, 1f);
            person.rimWidth = 2.5f;
            person.rimStrength = 2f;

            // The fluid runs but is not drawn: the particles ride on its velocity, so they swirl around the
            // dancer.
            SmokeSettings smoke = look.smoke;
            smoke.enabled = true;
            smoke.drawn = false;
            smoke.turbulence = 12f;
            smoke.buoyancy = 0.25f;
            smoke.motionGain = 2.5f;
            smoke.velocityWeight = 0.3f;

            EchoSettings echo = look.echo;
            echo.enabled = true;
            echo.copies = 8;
            echo.spacingSeconds = 0.1f;
            echo.colorMode = EchoColorMode.Solid;
            echo.solidColor = new Color(1f, 0.05f, 0.05f, 1f);
            echo.opacityNewest = 0.8f;
            echo.opacityOldest = 0.15f;
            echo.offsetPerCopy = new Vector2(0.03f, 0f);
            echo.scaleStep = 0f;
            echo.look = EchoLook.Solid;
            echo.waveAmount = 0.012f;
            echo.waveFrequency = 9f;
            echo.waveSpeed = 1.2f;
            echo.gain = 1.5f;
            echo.edgeContrast = 1.5f;

            look.outline.enabled = false;

            ParticleSettings particles = look.particles;
            particles.enabled = true;
            particles.maxParticles = 200000;
            particles.spawnPerSecond = 5500f;
            particles.motionBoost = 3f;
            particles.emitFrom = ParticleEmitFrom.Both;
            particles.edgeShare = 0.5f;
            particles.lifetimeMin = 1.6f;
            particles.lifetimeMax = 3.2f;
            particles.sizeMinThousandths = 3f;
            particles.sizeMaxThousandths = 20f;
            particles.sphereFraction = 0.5f;
            particles.colorMode = ParticleColorMode.Solid;
            particles.solidColor = new Color(1f, 0.04f, 0.03f, 1f);
            particles.gain = 1f;
            particles.gravity = 0f;
            particles.flowInfluence = 3.5f;
            particles.motionKick = 2f;
            particles.swirl = 0.2f;
            particles.swirlScale = 3.5f;
            particles.drag = 0.6f;
            particles.floorEnabled = false;
        }

        // Clip 11367: a single dancer made of dots and shaded spheres whose colour cycles with time, so the
        // body lags behind in a rainbow. Particles fall off the body and pile up on a floor line; the real
        // picture is nearly gone and a thin white neon outline remains.
        private static void ApplyParticleBody(HumanEchoSettings look) {
            look.displayName = "Particle Body";
            look.notes = "Clip 11367: a dancer made of hue-cycling dots and spheres that fall and pile up "
                         + "on a floor line, thin white outline.";
            look.viewHeightMetres = 2.4f;
            look.backgroundColor = new Color(0.005f, 0.005f, 0.012f, 1f);

            PersonSettings person = look.person;
            person.enabled = true;
            person.colorMode = PersonColorMode.Grayscale;
            person.tint = new Color(0.6f, 0.7f, 1f, 1f);
            person.opacity = 0.08f;
            person.brightness = 0.8f;
            person.contrast = 1f;
            person.flash = 0f;
            person.edgeContrast = 1.5f;
            person.rim = false;

            // The fluid runs but is not drawn: it only carries the particles.
            SmokeSettings smoke = look.smoke;
            smoke.enabled = true;
            smoke.drawn = false;
            smoke.turbulence = 8f;
            smoke.buoyancy = 0.6f;
            smoke.motionGain = 2.5f;
            smoke.velocityWeight = 0.25f;
            look.echo.enabled = false;

            OutlineSettings outline = look.outline;
            outline.enabled = true;
            outline.colorMode = OutlineColorMode.Fixed;
            outline.color = new Color(1f, 1f, 1f, 1f);
            outline.widthThousandths = 1.2f;
            outline.softness = 0.3f;
            outline.intensity = 2.4f;
            outline.haloThousandths = 5f;
            outline.haloStrength = 0.3f;
            outline.jitterThousandths = 0f;
            outline.delayCopies = 0;

            ParticleSettings particles = look.particles;
            particles.enabled = true;
            particles.maxParticles = 200000;
            particles.spawnPerSecond = 26000f;
            particles.motionBoost = 2.5f;
            particles.emitFrom = ParticleEmitFrom.Both;
            particles.edgeShare = 0.35f;
            particles.lifetimeMin = 1f;
            particles.lifetimeMax = 2.4f;
            particles.sizeMinThousandths = 2f;
            particles.sizeMaxThousandths = 5f;
            particles.sphereFraction = 0.05f;
            particles.colorMode = ParticleColorMode.HueOverTime;
            particles.hueSpeed = 0.12f;
            particles.saturation = 0.9f;
            particles.gain = 1.1f;
            particles.gravity = -0.24f;
            particles.flowInfluence = 0.9f;
            particles.motionKick = 1f;
            particles.swirl = 0.05f;
            particles.swirlScale = 5f;
            particles.drag = 1.8f;
            particles.floorEnabled = true;
            particles.floorHeight = 0.06f;
            particles.pileSeconds = 6f;
            particles.pileGrowthThousandths = 0.4f;
            particles.pileMaxHeight = 0.06f;
        }
    }
}
