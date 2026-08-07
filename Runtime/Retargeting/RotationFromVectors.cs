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
            // M13: use ONE shared up for both bases. The old code resolved the up independently for rest
            // and target; when one direction was near-parallel to referenceUp it fell back to a different
            // axis than the other, so targetBasis * inverse(restBasis) carried spurious roll (twist).
            Vector3 up = SharedUp(restDirection, targetDirection, referenceUp);
            Quaternion restBasis = Quaternion.LookRotation(restDirection, up);
            Quaternion targetBasis = Quaternion.LookRotation(targetDirection, up);
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

        // M13: resolve ONE up shared by both bases. It must be well-separated from BOTH the rest and
        // the target direction; otherwise LookRotation for the near-parallel side falls back to a
        // different internal axis and the delta carries spurious roll. Prefer referenceUp, then the
        // world axes, picking the first that is usable against both directions.
        private static Vector3 SharedUp(Vector3 restForward, Vector3 targetForward, Vector3 referenceUp) {
            Vector3 candidate = referenceUp.normalized;
            if (candidate.sqrMagnitude > 1e-6f && IsUsableUp(restForward, targetForward, candidate)) {
                return candidate;
            }
            if (IsUsableUp(restForward, targetForward, Vector3.up)) {
                return Vector3.up;
            }
            if (IsUsableUp(restForward, targetForward, Vector3.right)) {
                return Vector3.right;
            }
            return Vector3.forward;
        }

        private static bool IsUsableUp(Vector3 restForward, Vector3 targetForward, Vector3 up) {
            return Mathf.Abs(Vector3.Dot(restForward, up)) < 0.99f
                && Mathf.Abs(Vector3.Dot(targetForward, up)) < 0.99f;
        }
    }
}
