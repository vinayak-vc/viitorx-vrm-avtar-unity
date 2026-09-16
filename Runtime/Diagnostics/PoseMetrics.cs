using UnityEngine;

using VirtualMirror.Core;

namespace VirtualMirror.Diagnostics {
    /// <summary>
    /// F-27 - measurements taken ON a pose, whatever produced it. Deliberately separate from
    /// <see cref="SyntheticPose"/>: these run on REAL tracked frames as well as generated ones, and the
    /// before/after report depends on BOTH streams being measured by the identical instrument. A metric
    /// that lived inside the synthetic generator would invite a second, subtly different copy for live
    /// data, which is how two numbers stop being comparable without anybody noticing.
    /// </summary>
    public static class PoseMetrics {

        /// <summary>Distance between two landmarks in a frame.</summary>
        public static float BoneLength(PoseFrame frame, int a, int b) {
            return Vector3.Distance(frame.GetLandmark((JointId)a).Position,
                                    frame.GetLandmark((JointId)b).Position);
        }

        /// <summary>Interior angle at <paramref name="joint"/>, degrees. 180 = straight.</summary>
        public static float InteriorAngle(PoseFrame frame, int a, int joint, int b) {
            Vector3 j = frame.GetLandmark((JointId)joint).Position;
            Vector3 va = frame.GetLandmark((JointId)a).Position - j;
            Vector3 vb = frame.GetLandmark((JointId)b).Position - j;
            if (va.sqrMagnitude < 1e-12f || vb.sqrMagnitude < 1e-12f) {
                return 180f;
            }
            return Vector3.Angle(va, vb);
        }

        /// <summary>How far a landmark sits from the mid-hip. Near zero on a non-hip joint is the
        /// collapse signature.</summary>
        public static float DistanceFromHip(PoseFrame frame, int landmark) {
            return frame.GetLandmark((JointId)landmark).Position.magnitude;
        }

        /// <summary>Largest landmark displacement between two frames — the "big jump" measurement.</summary>
        public static float MaxDisplacement(PoseFrame a, PoseFrame b) {
            float worst = 0f;
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                float d = Vector3.Distance(a.GetLandmark((JointId)i).Position,
                                           b.GetLandmark((JointId)i).Position);
                if (!float.IsNaN(d) && d > worst) {
                    worst = d;
                }
                i = i + 1;
            }
            return worst;
        }

        /// <summary>Is the whole frame free of NaN/Inf? A single poisoned landmark propagates through
        /// a solver and takes the entire avatar with it.</summary>
        public static bool AllFinite(PoseFrame frame) {
            int i = 0;
            while (i < PoseFrame.LandmarkCount) {
                Vector3 v = frame.GetLandmark((JointId)i).Position;
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                    || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) {
                    return false;
                }
                i = i + 1;
            }
            return true;
        }
    }
}
