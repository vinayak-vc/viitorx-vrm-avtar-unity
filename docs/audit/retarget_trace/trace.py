"""Offline, faithful replica of the Unity retarget chain, for the forensic audit.

Replicates, line for line:
  Runtime/Retargeting/Kalidokit/KMath.cs
  Runtime/Retargeting/Kalidokit/KalidokitArmSolver.cs
  Runtime/Retargeting/Kalidokit/KalidokitPoseSolver.cs
  Runtime/Retargeting/KalidokitControlRigDriver.cs  (Apply + ApplyBone, lerp=1 == converged)
  UniVRM Vrm10ControlBone            (rest world rotation == identity for EVERY control bone)

Spaces
  PoseFrame (Unity)      : X = subject's anatomical LEFT, Y = up, Z = subject's BACK
                           (sidecar emits camera X-right/Y-down/Z-forward; poseFlipY=1 only)
  Kalidokit / MediaPipe  : X = subject's LEFT, Y = DOWN, Z = BACK   (driver does (x, -y, z))
  VRM normalized (Unity) : X = avatar's LEFT, Y = up, Z = avatar's BACK  (measured from the .vrm)
"""
import math
import numpy as np

PI = math.pi
TWO_PI = 2.0 * math.pi

# ----------------------------------------------------------------- KMath.cs
def clampf(v, lo, hi): return max(min(v, hi), lo)

def find_2d_angle(cx, cy, ex, ey): return math.atan2(ey - cy, ex - cx)

def normalize_radians(r):
    if r >= PI / 2.0:
        r -= TWO_PI
    if r <= -PI / 2.0:
        r += TWO_PI
        r = PI - r
    return r / PI

def normalize_angle(r):
    a = math.fmod(r, TWO_PI)
    if a > PI:
        a -= TWO_PI
    elif a < -PI:
        a += TWO_PI
    return a / PI

def find_rotation(a, b):
    return np.array([
        normalize_radians(find_2d_angle(a[2], a[0], b[2], b[0])),
        normalize_radians(find_2d_angle(a[2], a[1], b[2], b[1])),
        normalize_radians(find_2d_angle(a[0], a[1], b[0], b[1])),
    ])

def angle_between_3d(a, b, c):
    v1 = a - b; v2 = c - b
    if v1.dot(v1) < 1e-12 or v2.dot(v2) < 1e-12: return 0.0
    d = float(np.dot(v1 / np.linalg.norm(v1), v2 / np.linalg.norm(v2)))
    return normalize_radians(math.acos(clampf(d, -1.0, 1.0)))

def roll_pitch_yaw2(a, b):
    return np.array([
        normalize_angle(find_2d_angle(a[2], a[1], b[2], b[1])),
        normalize_angle(find_2d_angle(a[2], a[0], b[2], b[0])),
        normalize_angle(find_2d_angle(a[0], a[1], b[0], b[1])),
    ])

def remap(v, lo, hi): return (clampf(v, lo, hi) - lo) / (hi - lo)

def _spherical(d):
    n = float(np.linalg.norm(d))
    if n < 1e-8: return 0.0, 0.0
    theta = math.atan2(d[2], d[1])            # axisMap x<-y, y<-z, z<-x
    phi = math.acos(clampf(d[0] / n, -1.0, 1.0))
    return theta, phi

def get_spherical(a, b):
    v = b - a
    n = np.linalg.norm(v)
    v = v / n if n > 0 else v
    t, p = _spherical(v)
    return normalize_angle(-t), normalize_angle(PI / 2.0 - p)

def get_relative_spherical(a, b, c):
    v1 = b - a; v1 = v1 / np.linalg.norm(v1)
    v2 = c - b; v2 = v2 / np.linalg.norm(v2)
    t1, p1 = _spherical(v1); t2, p2 = _spherical(v2)
    return normalize_angle(t1 - t2), normalize_angle(p1 - p2)

def three_euler_to_unity(e, signs=(1.0, 1.0, 1.0), flip=0):
    x = e[0] * signs[0]; y = e[1] * signs[1]; z = e[2] * signs[2]
    c1, c2, c3 = math.cos(x/2), math.cos(y/2), math.cos(z/2)
    s1, s2, s3 = math.sin(x/2), math.sin(y/2), math.sin(z/2)
    qx = s1*c2*c3 + c1*s2*s3
    qy = c1*s2*c3 - s1*c2*s3
    qz = c1*c2*s3 + s1*s2*c3
    qw = c1*c2*c3 - s1*s2*s3
    if flip == 1:  return np.array([qx,  qy, -qz, qw])
    if flip == 2:  return np.array([qx, -qy, -qz, qw])
    if flip == 3:  return np.array([-qx, qy, -qz, qw])
    return np.array([-qx, -qy, qz, qw])

# ------------------------------------------------- quaternion helpers (Unity order)
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return np.array([
        aw*bx + ax*bw + ay*bz - az*by,
        aw*by - ax*bz + ay*bw + az*bx,
        aw*bz + ax*by - ay*bx + az*bw,
        aw*bw - ax*bx - ay*by - az*bz,
    ])

def qrot(q, v):
    x, y, z, w = q
    u = np.array([x, y, z])
    return 2.0*np.dot(u, v)*u + (w*w - np.dot(u, u))*v + 2.0*w*np.cross(u, v)

QI = np.array([0.0, 0.0, 0.0, 1.0])

# ----------------------------------------------------------- KalidokitArmSolver.cs
def rig_arm(upper, lower, hand, invert):
    upper = upper.copy(); lower = lower.copy(); hand = hand.copy()
    lax = lower[0]; laz = lower[2]
    upper[2] *= -2.3 * invert
    upper[1] *= PI * invert
    upper[1] -= lax
    upper[1] -= -invert * max(laz, 0.0)
    upper[0] -= 0.3 * invert
    lower[2] *= -2.14 * invert
    lower[1] *= 2.14 * invert
    lower[0] *= 2.14 * invert
    upper[0] = clampf(upper[0], -0.5, PI)
    lower[0] = clampf(lower[0], -0.3, 0.3)
    hand[1] = clampf(hand[2] * 2.0, -0.6, 0.6)
    hand[2] = hand[2] * -2.3 * invert
    return upper, lower, hand

def solve_arms(lm):
    uR = find_rotation(lm[11], lm[13]); uL = find_rotation(lm[12], lm[14])
    uR[1] = angle_between_3d(lm[12], lm[11], lm[13])
    uL[1] = angle_between_3d(lm[11], lm[12], lm[14])
    lR = find_rotation(lm[13], lm[15]); lL = find_rotation(lm[14], lm[16])
    lR[1] = angle_between_3d(lm[11], lm[13], lm[15])
    lL[1] = angle_between_3d(lm[12], lm[14], lm[16])
    lR[2] = clampf(lR[2], -2.14, 0.0); lL[2] = clampf(lL[2], -2.14, 0.0)
    hR = find_rotation(lm[15], 0.5*(lm[17]+lm[19]))
    hL = find_rotation(lm[16], 0.5*(lm[18]+lm[20]))
    uR, lR, hR = rig_arm(uR, lR, hR, 1)
    uL, lL, hL = rig_arm(uL, lL, hL, -1)
    return dict(uR=uR, uL=uL, lR=lR, lL=lL, hR=hR, hL=hL,
                raw=dict(uR_pre=find_rotation(lm[11], lm[13]), uL_pre=find_rotation(lm[12], lm[14])))

# ---------------------------------------------------------- KalidokitPoseSolver.cs
UPPER_LEG_Z_OFFSET = 0.1

def calc_hips_spine(lm):
    hips = roll_pitch_yaw2(lm[23], lm[24])
    if hips[1] > 0.5: hips[1] -= 2.0
    hips[1] += 0.5
    if hips[2] > 0.0: hips[2] = 1.0 - hips[2]
    if hips[2] < 0.0: hips[2] = -1.0 - hips[2]
    hips[2] *= 1.0 - remap(abs(hips[1]), 0.2, 0.4)
    hips[0] = 0.0
    spine = roll_pitch_yaw2(lm[11], lm[12])
    if spine[1] > 0.5: spine[1] -= 2.0
    spine[1] += 0.5
    if spine[2] > 0.0: spine[2] = 1.0 - spine[2]
    if spine[2] < 0.0: spine[2] = -1.0 - spine[2]
    spine[2] *= 1.0 - remap(abs(spine[1]), 0.2, 0.4)
    spine[0] = 0.0
    return hips*PI, spine*PI

def rig_upper_leg(v, invert):
    return np.array([
        clampf(v[0], 0.0, 0.5)*PI,
        clampf(v[1], -0.25, 0.25)*PI,
        clampf(v[2], -0.5, 0.5)*PI + invert*UPPER_LEG_Z_OFFSET,
    ])

def calc_legs(lm):
    rUT, rUP = get_spherical(lm[23], lm[25])
    lUT, lUP = get_spherical(lm[24], lm[26])
    rLT, rLP = get_relative_spherical(lm[23], lm[25], lm[27])
    lLT, lLP = get_relative_spherical(lm[24], lm[26], lm[28])
    hipRot = find_rotation(lm[23], lm[24])
    uR = np.array([rUT, rLP, rUP - hipRot[2]])
    uL = np.array([lUT, lLP, lUP - hipRot[2]])
    lR = np.array([-abs(rLT), 0.0, 0.0]) * PI
    lL = np.array([-abs(lLT), 0.0, 0.0]) * PI
    return dict(uR=rig_upper_leg(uR, 1), uL=rig_upper_leg(uL, -1), lR=lR, lL=lL,
                pre=dict(uR=uR, uL=uL, rUT=rUT, rUP=rUP, rLT=rLT, rLP=rLP))

def solve_pose(lm):
    out = solve_arms(lm)
    hips, spine = calc_hips_spine(lm)
    out['hips'] = hips; out['spine'] = spine
    out.update({('leg_'+k): v for k, v in calc_legs(lm).items()})
    return out
