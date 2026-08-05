using UnityEngine;

using UniVRM10;

using VirtualMirror.Core;

namespace VirtualMirror.Avatar.Vrm {
    /// <summary>
    /// <see cref="IAvatarInstance"/> backed by a UniVRM <see cref="Vrm10Instance"/>.
    /// Disposing destroys the instantiated GameObject.
    /// </summary>
    public sealed class VrmAvatarInstance : IAvatarInstance {
        private readonly Vrm10Instance vrmInstance;
        private readonly Animator animator;
        private bool disposed;

        public VrmAvatarInstance(Vrm10Instance vrmInstance, Animator animator) {
            this.vrmInstance = vrmInstance;
            this.animator = animator;
        }

        public GameObject Root {
            get {
                if (vrmInstance == null) {
                    return null;
                }
                return vrmInstance.gameObject;
            }
        }

        public Animator Animator {
            get {
                return animator;
            }
        }

        public bool IsHumanoid {
            get {
                return animator != null && animator.isHuman;
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            if (vrmInstance != null) {
                Object.Destroy(vrmInstance.gameObject);
            }
        }
    }
}
