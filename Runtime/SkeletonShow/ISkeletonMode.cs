using UnityEngine;

namespace VirtualMirror.SkeletonShow {
    /// <summary>
    /// F-28 — one visual treatment of the tracked skeleton. Three exist and the operator switches
    /// between them live.
    ///
    /// THE CONTRACT THAT MAKES THEM INTERCHANGEABLE: a mode owns everything it creates under the root
    /// it is given, creates it in <see cref="Activate"/>, and destroys it in <see cref="Deactivate"/>.
    /// Nothing survives a switch. That is deliberate — a mode leaving a trail renderer or a particle
    /// system behind would make the NEXT mode look wrong, and the person debugging it would be looking
    /// at the wrong file.
    /// </summary>
    public interface ISkeletonMode {
        /// <summary>Shown in the on-screen switcher.</summary>
        string Name { get; }

        /// <summary>One line telling the viewer what they are looking at.</summary>
        string Description { get; }

        /// <summary>Build this mode's objects under <paramref name="root"/>.</summary>
        void Activate(Transform root, SkeletonShowPalette palette);

        /// <summary>Destroy everything this mode created. Called before another mode activates.</summary>
        void Deactivate();

        /// <summary>Draw one frame. Never called before <see cref="Activate"/>.</summary>
        void Render(SkeletonPose pose, float deltaSeconds);
    }
}
