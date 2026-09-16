import math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from trace import *

# ---------------------------------------------------------------- fixtures
# PoseFrame (Unity) space, hip-relative metres. X = subject LEFT, Y = up, Z = subject BACK.
def base_skeleton():
    p = {i: np.zeros(3) for i in range(33)}
    p[0]  = np.array([ 0.00,  0.72, -0.10])   # nose (face = -Z)
    p[7]  = np.array([ 0.08,  0.68,  0.02])   # left ear
    p[8]  = np.array([-0.08,  0.68,  0.02])   # right ear
    p[11] = np.array([ 0.18,  0.50,  0.00])   # left shoulder
    p[12] = np.array([-0.18,  0.50,  0.00])   # right shoulder
    p[23] = np.array([ 0.10,  0.00,  0.00])   # left hip
    p[24] = np.array([-0.10,  0.00,  0.00])   # right hip
    p[13] = np.array([ 0.20,  0.22,  0.00]); p[15] = np.array([ 0.21, -0.05,  0.00])
    p[14] = np.array([-0.20,  0.22,  0.00]); p[16] = np.array([-0.21, -0.05,  0.00])
    p[25] = np.array([ 0.10, -0.45,  0.00]); p[27] = np.array([ 0.10, -0.90,  0.00])
    p[26] = np.array([-0.10, -0.45,  0.00]); p[28] = np.array([-0.10, -0.90,  0.00])
    return p

def rotY(p, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    R = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return {k: (R @ v if np.any(v) else v) for k, v in p.items()}

def poses():
    out = {}
    out['P1 standing straight'] = base_skeleton()

    p = base_skeleton()
    p[14] = np.array([-0.19, 0.75, 0.0]); p[16] = np.array([-0.18, 1.00, 0.0])
    out['P2 right arm overhead'] = p

    p = base_skeleton()
    p[13] = np.array([ 0.19, 0.75, 0.0]); p[15] = np.array([ 0.18, 1.00, 0.0])
    p[14] = np.array([-0.19, 0.75, 0.0]); p[16] = np.array([-0.18, 1.00, 0.0])
    out['P3 both arms overhead'] = p

    p = base_skeleton()
    p[13] = np.array([ 0.45, 0.50, 0.0]); p[15] = np.array([ 0.72, 0.50, 0.0])
    p[14] = np.array([-0.45, 0.50, 0.0]); p[16] = np.array([-0.72, 0.50, 0.0])
    out['P4 T-pose arms out'] = p

    p = base_skeleton()
    p[26] = np.array([-0.10, -0.42, -0.22]); p[28] = np.array([-0.10, -0.80, -0.38])
    out['P5 right leg forward'] = p

    out['P6 body turned 90deg'] = rotY(base_skeleton(), 90.0)
    out['P6b body turned 45deg'] = rotY(base_skeleton(), 45.0)

    p = base_skeleton()
    p[25] = np.array([ 0.14, -0.22, -0.30]); p[27] = np.array([ 0.11, -0.55,  0.02])
    p[26] = np.array([-0.14, -0.22, -0.30]); p[28] = np.array([-0.11, -0.55,  0.02])
    out['P7 deep squat'] = p

    p = base_skeleton()
    p[25] = np.array([ 0.11, -0.02, -0.42]); p[27] = np.array([ 0.11, -0.45, -0.40])
    p[26] = np.array([-0.11, -0.02, -0.42]); p[28] = np.array([-0.11, -0.45, -0.40])
    p[11] = np.array([ 0.18,  0.52, -0.04]); p[12] = np.array([-0.18,  0.52, -0.04])
    out['P8 sitting'] = p
    return out

# ---------------------------- VRM normalized rest directions (measured from the .vrm files)
REST = {
    'spine':         np.array([0.0,  1.0, 0.0]),
    'chest':         np.array([0.0,  1.0, 0.0]),
    'leftUpperArm':  np.array([1.0,  0.0, 0.0]),
    'leftLowerArm':  np.array([1.0,  0.0, 0.0]),
    'rightUpperArm': np.array([-1.0, 0.0, 0.0]),
    'rightLowerArm': np.array([-1.0, 0.0, 0.0]),
    'leftUpperLeg':  np.array([0.0, -1.0, 0.0]),
    'leftLowerLeg':  np.array([0.0, -1.0, 0.0]),
    'rightUpperLeg': np.array([0.0, -1.0, 0.0]),
    'rightLowerLeg': np.array([0.0, -1.0, 0.0]),
}

def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v

def ang(a, b):
    return math.degrees(math.acos(clampf(float(np.dot(unit(a), unit(b))), -1.0, 1.0)))

def fmt(v):
    return "(%+.2f,%+.2f,%+.2f)" % (v[0], v[1], v[2])

def run(pf, flip=2, signs=(1, 1, 1), torso_yaw_scale=0.0, torso_roll=0.0, bend=0.0):
    lm = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
    s = solve_pose(lm)
    q = lambda e: three_euler_to_unity(e, signs, flip)
    hips_e  = np.array([0.0, s['hips'][1] * torso_yaw_scale * 0.7,  s['hips'][2] * torso_roll * 0.7])
    spine_e = np.array([bend * 0.5, s['spine'][1] * torso_yaw_scale * 0.45, s['spine'][2] * torso_roll * 0.45])
    chest_e = np.array([bend * 0.5, s['spine'][1] * torso_yaw_scale * 0.25, s['spine'][2] * torso_roll * 0.25])
    W = {}
    W['hips']  = q(hips_e)
    W['spine'] = qmul(W['hips'], q(spine_e))
    W['chest'] = qmul(W['spine'], q(chest_e))
    W['leftUpperArm']  = qmul(W['chest'], q(s['uL']))
    W['leftLowerArm']  = qmul(W['leftUpperArm'],  q(s['lL']))
    W['rightUpperArm'] = qmul(W['chest'], q(s['uR']))
    W['rightLowerArm'] = qmul(W['rightUpperArm'], q(s['lR']))
    W['leftUpperLeg']  = qmul(W['hips'], q(s['leg_uL']))
    W['leftLowerLeg']  = qmul(W['leftUpperLeg'],  q(s['leg_lL']))
    W['rightUpperLeg'] = qmul(W['hips'], q(s['leg_uR']))
    W['rightLowerLeg'] = qmul(W['rightUpperLeg'], q(s['leg_lR']))
    dirs = {k: qrot(W[k], REST[k]) for k in REST}
    return s, W, dirs

def expected(pf):
    mx = lambda v: np.array([-v[0], v[1], v[2]])
    trunk = unit(0.5 * (pf[11] + pf[12]) - 0.5 * (pf[23] + pf[24]))
    return {
        'leftUpperArm':  mx(unit(pf[14] - pf[12])),
        'leftLowerArm':  mx(unit(pf[16] - pf[14])),
        'rightUpperArm': mx(unit(pf[13] - pf[11])),
        'rightLowerArm': mx(unit(pf[15] - pf[13])),
        'leftUpperLeg':  mx(unit(pf[26] - pf[24])),
        'leftLowerLeg':  mx(unit(pf[28] - pf[26])),
        'rightUpperLeg': mx(unit(pf[25] - pf[23])),
        'rightLowerLeg': mx(unit(pf[27] - pf[25])),
        'spine':         mx(trunk),
        'chest':         mx(trunk),
    }

BONES = ['spine', 'leftUpperArm', 'leftLowerArm', 'rightUpperArm', 'rightLowerArm',
         'leftUpperLeg', 'leftLowerLeg', 'rightUpperLeg', 'rightLowerLeg']

def report(flip, only=None, verbose=True):
    print("")
    print("############ flipQuat=%d  eulerSigns=(1,1,1)  torsoYawScale=0  torsoRoll=0 ############" % flip)
    tot = []
    for name, pf in poses().items():
        if only and name not in only:
            continue
        s, W, dirs = run(pf, flip=flip)
        exp = expected(pf)
        eulers = {
            'spine': np.array([0.0, 0.0, 0.0]),
            'leftUpperArm': s['uL'], 'leftLowerArm': s['lL'],
            'rightUpperArm': s['uR'], 'rightLowerArm': s['lR'],
            'leftUpperLeg': s['leg_uL'], 'leftLowerLeg': s['leg_lL'],
            'rightUpperLeg': s['leg_uR'], 'rightLowerLeg': s['leg_lR'],
        }
        if verbose:
            print("")
            print("=== %s ===" % name)
            print("  %-15s %-22s %-22s %8s   solver euler XYZ deg" % ('bone', 'want dir', 'got dir', 'err'))
        for b in BONES:
            e = ang(exp[b], dirs[b]); tot.append(e)
            ed = np.degrees(eulers[b])
            if verbose:
                print("  %-15s %-22s %-22s %8.1f   (%+7.1f,%+7.1f,%+7.1f)"
                      % (b, fmt(exp[b]), fmt(dirs[b]), e, ed[0], ed[1], ed[2]))
    print("")
    print("  >>> flip=%d  mean dir error %.1f deg   max %.1f deg" % (flip, float(np.mean(tot)), float(np.max(tot))))
    return float(np.mean(tot))

if __name__ == '__main__':
    mode = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if mode == 'sweep':
        best = []
        for f in (0, 1, 2, 3):
            best.append((report(f, verbose=False), f))
        print("")
        print("SWEEP:", sorted(best))
    else:
        report(int(mode) if mode.isdigit() else 2)
