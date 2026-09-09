"""Prototype of the new ArmAimSolver, in the SAME conventions the C# will use.

Frame: the control-rig rest frame == avatar model frame == PoseFrame frame
       X = avatar's LEFT, Y = up, Z = avatar's BACK   (proven in UNITY_RETARGETING_AUDIT_2026-09-08 §2/§10)
Unity Quaternion.LookRotation(forward, up):  z = forward, x = cross(up, z), y = cross(z, x);
       matrix columns [x|y|z], i.e. it maps (0,0,1)->forward and (0,1,0)->up.
"""
import math
import numpy as np

def unit(v):
    n = float(np.linalg.norm(v))
    return v / n if n > 1e-12 else np.zeros(3)

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return np.array([
        aw*bx + ax*bw + ay*bz - az*by,
        aw*by - ax*bz + ay*bw + az*bx,
        aw*bz + ax*by - ay*bx + az*bw,
        aw*bw - ax*bx - ay*by - az*bz,
    ])

def qinv(q):
    return np.array([-q[0], -q[1], -q[2], q[3]])

def qrot(q, v):
    x, y, z, w = q
    u = np.array([x, y, z])
    return 2.0*np.dot(u, v)*u + (w*w - np.dot(u, u))*v + 2.0*w*np.cross(u, v)

def mat_to_quat(m):
    """m columns are the rotated basis vectors (Unity convention)."""
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0.0:
        s = math.sqrt(t + 1.0) * 2.0
        w = 0.25 * s
        x = (m[2, 1] - m[1, 2]) / s
        y = (m[0, 2] - m[2, 0]) / s
        z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2.0
        w = (m[2, 1] - m[1, 2]) / s
        x = 0.25 * s
        y = (m[0, 1] + m[1, 0]) / s
        z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2.0
        w = (m[0, 2] - m[2, 0]) / s
        x = (m[0, 1] + m[1, 0]) / s
        y = 0.25 * s
        z = (m[1, 2] + m[2, 1]) / s
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2.0
        w = (m[1, 0] - m[0, 1]) / s
        x = (m[0, 2] + m[2, 0]) / s
        y = (m[1, 2] + m[2, 1]) / s
        z = 0.25 * s
    q = np.array([x, y, z, w])
    return q / np.linalg.norm(q)

def look_rotation(forward, up):
    z = unit(forward)
    x = np.cross(up, z)
    nx = float(np.linalg.norm(x))
    if nx < 1e-9:
        return None
    x = x / nx
    y = np.cross(z, x)
    return mat_to_quat(np.column_stack([x, y, z]))

def from_to(a, b):
    a = unit(a); b = unit(b)
    d = float(np.dot(a, b))
    if d > 1.0 - 1e-9:
        return np.array([0.0, 0.0, 0.0, 1.0])
    if d < -1.0 + 1e-9:
        axis = np.cross(a, np.array([1.0, 0.0, 0.0]))
        if np.linalg.norm(axis) < 1e-6:
            axis = np.cross(a, np.array([0.0, 1.0, 0.0]))
        axis = unit(axis)
        return np.array([axis[0], axis[1], axis[2], 0.0])
    axis = np.cross(a, b)
    s = math.sqrt((1.0 + d) * 2.0)
    return np.array([axis[0]/s, axis[1]/s, axis[2]/s, s*0.5])

# --------------------------------------------------------------------- solver
BEND_SIN_MIN = 0.20          # sin(bend) below this -> elbow plane is not informative

class ArmAimState:
    """Per-arm roll continuity state (mirrors the C# struct)."""
    def __init__(self):
        self.normal = None   # last well-determined bend normal, in the rig frame

    def reset(self):
        self.normal = None

def solve_arm(shoulder, elbow, wrist, rest_upper, rest_lower, rest_normal, state, mirror_x=True):
    """Returns (qUpperWorld, qLowerWorld, diag). All rotations in the rig rest frame."""
    mx = (lambda p: np.array([-p[0], p[1], p[2]])) if mirror_x else (lambda p: p)
    s = mx(shoulder); e = mx(elbow); w = mx(wrist)

    u = unit(e - s)
    f = unit(w - e)
    if np.linalg.norm(u) < 0.5:
        return None, None, {'valid': False}
    if np.linalg.norm(f) < 0.5:
        f = u

    cross_uf = np.cross(u, f)
    bend_sin = float(np.linalg.norm(cross_uf))
    if bend_sin >= BEND_SIN_MIN:
        n = cross_uf / bend_sin
        state.normal = n
        source = 'elbow-plane'
    else:
        # Arm (nearly) straight: the elbow plane carries no roll information (§9). Hold the last
        # well-determined normal, re-orthogonalised against the current bone axis, so roll stays
        # continuous. With no history, carry the REST normal through the roll-free aim swing —
        # FromToRotation has no roll DOF, so this cannot helicopter (§7).
        if state.normal is not None:
            proj = state.normal - u * float(np.dot(state.normal, u))
            if float(np.linalg.norm(proj)) > 1e-4:
                n = unit(proj)
                source = 'held'
            else:
                n = unit(qrot(from_to(rest_upper, u), rest_normal))
                source = 'rest-carried(degenerate-held)'
        else:
            n = unit(qrot(from_to(rest_upper, u), rest_normal))
            source = 'rest-carried'

    q_rest_u = look_rotation(rest_upper, rest_normal)
    q_rest_l = look_rotation(rest_lower, rest_normal)
    q_tgt_u = look_rotation(u, n)
    q_tgt_l = look_rotation(f, n)
    if q_rest_u is None or q_rest_l is None or q_tgt_u is None or q_tgt_l is None:
        return None, None, {'valid': False}

    q_upper = qmul(q_tgt_u, qinv(q_rest_u))
    q_lower = qmul(q_tgt_l, qinv(q_rest_l))

    # diagnostics: roll = signed angle from the carried rest normal to the applied normal, about u
    carried = unit(qrot(from_to(rest_upper, u), rest_normal))
    roll = signed_angle(carried, n, u)
    bend = math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(u, f))))))
    return q_upper, q_lower, {
        'valid': True, 'u': u, 'f': f, 'n': n, 'rollDeg': roll,
        'bendDeg': bend, 'bendSin': bend_sin, 'normalSource': source,
    }

def signed_angle(a, b, axis):
    a = unit(a - axis*float(np.dot(a, axis)))
    b = unit(b - axis*float(np.dot(b, axis)))
    if np.linalg.norm(a) < 1e-8 or np.linalg.norm(b) < 1e-8:
        return 0.0
    d = max(-1.0, min(1.0, float(np.dot(a, b))))
    ang = math.degrees(math.acos(d))
    return ang if float(np.dot(np.cross(a, b), axis)) >= 0.0 else -ang
