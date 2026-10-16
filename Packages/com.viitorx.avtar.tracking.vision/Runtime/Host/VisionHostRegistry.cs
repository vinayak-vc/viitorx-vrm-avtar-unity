using UnityEngine;

using VirtualMirror.App;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Where a scene finds the <see cref="VisionHost"/> of the running application: <see cref="VisionBootstrap"/> registers its host here, and so does
    /// <c>AppBootstrap</c> in the Virtual Mirror application, so a component written against the registry works in either. One host at a time: one UDP
    /// port has one reader.
    /// </summary>
    public static class VisionHostRegistry {
        private static VisionHost current;

        /// <summary>The registered host, or null when no bootstrap is running.</summary>
        public static VisionHost Current {
            get {
                return current;
            }
        }

        public static void Register(VisionHost host) {
            current = host;
        }

        /// <summary>Forgets <paramref name="host"/> if it is the registered one; another host registered since stays.</summary>
        public static void Unregister(VisionHost host) {
            if (current == host) {
                current = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForFastEnterPlayMode() {
            current = null;
        }
    }
}
