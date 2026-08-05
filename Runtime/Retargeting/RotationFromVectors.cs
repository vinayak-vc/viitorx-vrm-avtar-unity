using UnityEngine;

namespace VirtualMirror.Retargeting {
    /// <summary>
    /// Vector-based FK rotation helpers. A plain "align rest direction onto target direction" leaves
    /// twist (roll) about that direction unconstrained, so limbs can spin arbitrarily. To pin the
    /// roll, both the rest and target directions are turned into full orientations using a shared
    /// reference axis, and the delta between those orientations is returned.
    /// </summary>
    public static class RotationFromVectors {
        public static Quaternion Delta(Vector3 restForward, Vector3 targetForward, Vector3 referenceUp) {
            if (restForward.sqrMagnitude < 1e-8f || targetForward.sqrMagnitude < 1e-8f) {
                return Quaternion.identity;
            }
            Vector3 restDirection = restForward.normalized;
            Vector3 targetDirection = targetForward.normalized;
            Quaternion restBasis = Quaternion.LookRotation(restDirection, SafeUp(restDirection, referenceUp));
            Quaternion targetBasis = Quaternion.LookRotation(targetDirection, SafeUp(targetDirection, referenceUp));
            return targetBasis * Quaternion.Inverse(restBasis);
        }

        public static bool TryBasis(Vector3 right, Vector3 up, out Quaternion basis) {
            if (right.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f) {
                basis = Quaternion.identity;
                return false;
            }
            Vector3 forward = Vector3.Cross(right.normalized, up.normalized);
            if (forward.sqrMagnitude < 1e-8f) {
                basis = Quaternion.identity;
                return false;
            }
            basis = Quaternion.LookRotation(forward.normalized, up.normalized);
            return true;
        }

        private static Vector3 SafeUp(Vector3 forward, Vector3 referenceUp) {
            Vector3 candidate = referenceUp.normalized;
            if (candidate.sqrMagnitude > 1e-6f && Mathf.Abs(Vector3.Dot(forward, candidate)) < 0.99f) {
                return candidate;
            }
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.99f) {
                return Vector3.up;
            }
            return Vector3.right;
        }
    }
}
