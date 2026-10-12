using UnityEngine;

namespace ShowcaseShared {
    /// <summary>
    /// World (scene metres, already mirrored, levelled and grounded by the existing tracking
    /// pipeline) to VIEW space (metres from the screen centre).
    ///
    /// The mapping is orthographic and life-size: the shape of the body is never stretched. Only the
    /// body's POSITION on the screen is exaggerated sideways by the lateral gain, so a person
    /// crossing a small room can cross the whole screen. The sender's image coordinates are not on
    /// the wire; position comes from the measured mid-hip that the existing stage applies.
    /// </summary>
    public sealed class ScreenSpaceMapper {
        public float ViewHeight = 2.8f;
        public float ViewCentreX;
        public float ViewCentreY = 0.95f;
        public float LateralGain = 1.6f;
        public float Aspect = 16f / 9f;

        public float ViewWidth {
            get {
                return ViewHeight * Aspect;
            }
        }

        public void Configure(ITrackingTuning settings, float aspect) {
            ViewHeight = Mathf.Max(0.5f, settings.ViewHeightMetres);
            ViewCentreY = settings.ViewCentreY;
            LateralGain = settings.LateralGain;
            Aspect = Mathf.Max(0.2f, aspect);
        }

        /// <summary>
        /// One world point to view metres. <paramref name="bodyCentre"/> is the world mid-hip of the
        /// same frame; the body's offset from the view centre is scaled, its shape is not.
        /// </summary>
        public Vector2 Map(Vector3 world, Vector3 bodyCentre) {
            float centreX = (bodyCentre.x - ViewCentreX) * LateralGain;
            float centreY = bodyCentre.y - ViewCentreY;
            return new Vector2(centreX + (world.x - bodyCentre.x), centreY + (world.y - bodyCentre.y));
        }

        /// <summary>View metres to a 0..1 viewport coordinate (0,0 bottom-left).</summary>
        public Vector2 ToViewport(Vector2 view) {
            return new Vector2(0.5f + view.x / ViewWidth, 0.5f + view.y / ViewHeight);
        }

        /// <summary>View metres to GUI pixels (0,0 top-left), for the debug overlay.</summary>
        public Vector2 ToGui(Vector2 view, float screenWidth, float screenHeight) {
            Vector2 vp = ToViewport(view);
            return new Vector2(vp.x * screenWidth, (1f - vp.y) * screenHeight);
        }
    }
}
