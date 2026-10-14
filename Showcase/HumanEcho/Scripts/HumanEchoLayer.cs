using System;

using UnityEngine;

namespace HumanEcho {
    /// <summary>What every layer is told once per frame.</summary>
    public struct FrameContext {
        /// <summary>Seconds since the previous frame (<c>Time.deltaTime</c>).</summary>
        public float DeltaTime;

        /// <summary>Seconds since the scene started (<c>Time.time</c>); drives hue cycling and
        /// noise.</summary>
        public float Time;

        /// <summary>World width of the screen in metres (the camera is orthographic and centred on the
        /// origin).</summary>
        public float ViewWidth;

        /// <summary>World height of the screen in metres.</summary>
        public float ViewHeight;

        /// <summary>True when the person field was rebuilt this frame (a new mask arrived); false on frames
        /// that repeat the last mask.</summary>
        public bool NewMask;

        /// <summary>Width over height of the screen.</summary>
        public float ViewAspect {
            get {
                return ViewWidth / Mathf.Max(0.0001f, ViewHeight);
            }
        }
    }

    /// <summary>
    /// One drawable effect of Human Echo (smoke, echo copies, outline, the person itself, particles). The
    /// controller builds each layer once,
    /// calls <see cref="Tick"/> from Update (BEFORE FluXY's own LateUpdate) once the person field has a
    /// frame, and disposes it with the scene.
    /// A layer never reads the camera, the socket or the cutout textures: it is handed the <see
    /// cref="PersonField"/>.
    /// </summary>
    public interface IHumanEchoLayer : IDisposable {
        /// <summary>True once the layer has built everything it needs. A layer that failed to build stays
        /// false and the others carry on.</summary>
        bool IsReady { get; }

        /// <summary>
        /// One frame. <paramref name="field"/> has a non-null <see cref="PersonField.Field"/> and <see
        /// cref="PersonField.Motion"/> whenever this is called.
        /// The layer reads its own settings every frame, so they can be edited while playing; a layer whose
        /// settings say "off" must draw nothing
        /// and cost (almost) nothing.
        /// </summary>
        void Tick(PersonField field, FrameContext frame);

        /// <summary>Shows or hides everything the layer draws without disposing it (a hidden layer does no
        /// GPU work).</summary>
        void SetActive(bool active);
    }
}
