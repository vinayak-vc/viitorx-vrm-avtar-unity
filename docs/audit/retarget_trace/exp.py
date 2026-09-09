"""Isolating experiments on the arm/torso path. Each variant changes ONE thing."""
import math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from trace import *
from audit import poses, REST, unit, ang, fmt, expected, BONES, run

# ---------- variant solvers -------------------------------------------------
def rig_arm_v(upper, lower, hand, invert, no_x_offset=False, sym_x=False, no_lower_clamp=False):
    upper = upper.copy(); lower = lower.copy(); hand = hand.copy()
    lax = lower[0]; laz = lower[2]
    upper[2] *= -2.3 * invert
    upper[1] *= PI * invert
    upper[1] -= lax
    upper[1] -= -invert * max(laz, 0.0)
    if sym_x:
        upper[0] = abs(upper[0])          # make pitch mirror-symmetric
    if not no_x_offset:
        upper[0] -= 0.3 * invert
    lower[2] *= -2.14 * invert
    lower[1] *= 2.14 * invert
    lower[0] *= 2.14 * invert
    upper[0] = clampf(upper[0], -0.5, PI)
    if not no_lower_clamp:
        lower[0] = clampf(lower[0], -0.3, 0.3)
    hand[1] = clampf(hand[2] * 2.0, -0.6, 0.6)
    hand[2] = hand[2] * -2.3 * invert
    return upper, lower, hand

def solve_arms_v(lm, **kw):
    uR = find_rotation(lm[11], lm[13]); uL = find_rotation(lm[12], lm[14])
    uR[1] = angle_between_3d(lm[12], lm[11], lm[13])
    uL[1] = angle_between_3d(lm[11], lm[12], lm[14])
    lR = find_rotation(lm[13], lm[15]); lL = find_rotation(lm[14], lm[16])
    lR[1] = angle_between_3d(lm[11], lm[13], lm[15])
    lL[1] = angle_between_3d(lm[12], lm[14], lm[16])
    lR[2] = clampf(lR[2], -2.14, 0.0); lL[2] = clampf(lL[2], -2.14, 0.0)
    hR = find_rotation(lm[15], 0.5*(lm[17]+lm[19])); hL = find_rotation(lm[16], 0.5*(lm[18]+lm[20]))
    uR, lR, hR = rig_arm_v(uR, lR, hR, 1, **kw)
    uL, lL, hL = rig_arm_v(uL, lL, hL, -1, **kw)
    return uR, uL, lR, lL

# ---------- direct FK aim ("own rig") --------------------------------------
def qfromto(a, b):
    a = unit(a); b = unit(b)
    d = float(np.dot(a, b))
    if d > 1 - 1e-9:
        return np.array([0.0, 0.0, 0.0, 1.0])
    if d < -1 + 1e-9:
        axis = np.cross(a, np.array([1.0, 0, 0]))
        if np.linalg.norm(axis) < 1e-6:
            axis = np.cross(a, np.array([0, 1.0, 0]))
        axis = unit(axis)
        return np.array([axis[0], axis[1], axis[2], 0.0])
    axis = np.cross(a, b)
    s = math.sqrt((1 + d) * 2)
    return np.array([axis[0] / s, axis[1] / s, axis[2] / s, s * 0.5])

def qinv(q):
    return np.array([-q[0], -q[1], -q[2], q[3]])

def run_fk_aim(pf):
    """Own-rig reference: swing each bone's rest direction onto the mirrored source direction.
    Parent-relative, roll-free (FromToRotation has no roll DOF). Torso left at rest."""
    mx = lambda v: np.array([-v[0], v[1], v[2]])
    tgt = {
        'leftUpperArm':  mx(unit(pf[14] - pf[12])), 'leftLowerArm':  mx(unit(pf[16] - pf[14])),
        'rightUpperArm': mx(unit(pf[13] - pf[11])), 'rightLowerArm': mx(unit(pf[15] - pf[13])),
        'leftUpperLeg':  mx(unit(pf[26] - pf[24])), 'leftLowerLeg':  mx(unit(pf[28] - pf[26])),
        'rightUpperLeg': mx(unit(pf[25] - pf[23])), 'rightLowerLeg': mx(unit(pf[27] - pf[25])),
        'spine': np.array([0.0, 1.0, 0.0]), 'chest': np.array([0.0, 1.0, 0.0]),
    }
    W = {'hips': np.array([0.0, 0.0, 0.0, 1.0])}
    order = [('spine', 'hips'), ('chest', 'spine'),
             ('leftUpperArm', 'chest'), ('leftLowerArm', 'leftUpperArm'),
             ('rightUpperArm', 'chest'), ('rightLowerArm', 'rightUpperArm'),
             ('leftUpperLeg', 'hips'), ('leftLowerLeg', 'leftUpperLeg'),
             ('rightUpperLeg', 'hips'), ('rightLowerLeg', 'rightUpperLeg')]
    for bone, par in order:
        want_local = qrot(qinv(W[par]), tgt[bone])       # target dir in parent space
        q = qfromto(REST[bone], want_local)              # local rotation
        W[bone] = qmul(W[par], q)
    return {k: qrot(W[k], REST[k]) for k in REST}

# ---------- driver with variant arms ---------------------------------------
def run_variant(pf, flip=2, **kw):
    lm = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
    uR, uL, lR, lL = solve_arms_v(lm, **kw)
    legs = calc_legs(lm)
    q = lambda e: three_euler_to_unity(e, (1, 1, 1), flip)
    QIq = np.array([0.0, 0.0, 0.0, 1.0])
    W = {'hips': QIq, 'spine': QIq, 'chest': QIq}
    W['leftUpperArm']  = qmul(W['chest'], q(uL));  W['leftLowerArm']  = qmul(W['leftUpperArm'], q(lL))
    W['rightUpperArm'] = qmul(W['chest'], q(uR));  W['rightLowerArm'] = qmul(W['rightUpperArm'], q(lR))
    W['leftUpperLeg']  = qmul(W['hips'], q(legs['uL'])); W['leftLowerLeg']  = qmul(W['leftUpperLeg'], q(legs['lL']))
    W['rightUpperLeg'] = qmul(W['hips'], q(legs['uR'])); W['rightLowerLeg'] = qmul(W['rightUpperLeg'], q(legs['lR']))
    return {k: qrot(W[k], REST[k]) for k in REST}

ARMS = ['leftUpperArm', 'leftLowerArm', 'rightUpperArm', 'rightLowerArm']
LEGS = ['leftUpperLeg', 'leftLowerLeg', 'rightUpperLeg', 'rightLowerLeg']

def score(fn, group):
    errs = []
    per = {}
    for name, pf in poses().items():
        d = fn(pf)
        exp = expected(pf)
        e = [ang(exp[b], d[b]) for b in group]
        per[name] = float(np.mean(e))
        errs += e
    return float(np.mean(errs)), float(np.max(errs)), per

def main():
    variants = [
        ('BASELINE (shipping)',              lambda pf: run_variant(pf)),
        ('no upperArm.x -= 0.3*invert',      lambda pf: run_variant(pf, no_x_offset=True)),
        ('symmetric upperArm.x (abs)',       lambda pf: run_variant(pf, sym_x=True)),
        ('sym x AND no 0.3 offset',          lambda pf: run_variant(pf, sym_x=True, no_x_offset=True)),
        ('no lowerArm.x clamp',              lambda pf: run_variant(pf, no_lower_clamp=True)),
        ('FK aim (own rig, roll-free)',      run_fk_aim),
    ]
    print("%-34s %10s %10s | %10s %10s" % ('variant', 'arm mean', 'arm max', 'leg mean', 'leg max'))
    for label, fn in variants:
        am, ax, _ = score(fn, ARMS)
        lm_, lx, _ = score(fn, LEGS)
        print("%-34s %9.1f%s %9.1f%s | %9.1f%s %9.1f%s" % (label, am, '', ax, '', lm_, '', lx, ''))

    print("")
    print("--- per-pose ARM mean error (deg) ---")
    names = list(poses().keys())
    hdr = "%-34s" % 'variant' + ''.join("%12s" % n.split(' ')[0] for n in names)
    print(hdr)
    for label, fn in variants:
        _, _, per = score(fn, ARMS)
        print("%-34s" % label + ''.join("%12.1f" % per[n] for n in names))

    print("")
    print("--- L/R asymmetry on a perfectly symmetric input (P1 standing) ---")
    pf = poses()['P1 standing straight']
    for label, fn in variants:
        d = fn(pf); exp = expected(pf)
        l = ang(exp['leftUpperArm'], d['leftUpperArm']); r = ang(exp['rightUpperArm'], d['rightUpperArm'])
        print("  %-34s L=%6.1f  R=%6.1f  |L-R|=%6.1f" % (label, l, r, abs(l - r)))

    print("")
    print("--- torso FACING (yaw) at torsoYawScale = 0 vs 1 ---")
    for pname in ['P1 standing straight', 'P6b body turned 45deg', 'P6 body turned 90deg']:
        pf = poses()[pname]
        # subject facing (PoseFrame space): shoulder line x up -> forward
        sl = unit(pf[11] - pf[12])                     # right->left shoulder
        fwd_subj = unit(np.cross(np.array([0.0, 1.0, 0.0]), sl))
        want = np.array([-fwd_subj[0], fwd_subj[1], fwd_subj[2]])   # mirrored
        for ys in (0.0, 1.0):
            lmk = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
            s = solve_pose(lmk)
            q = lambda e: three_euler_to_unity(e, (1, 1, 1), 2)
            Wh = q(np.array([0.0, s['hips'][1] * ys * 0.7, 0.0]))
            Ws = qmul(Wh, q(np.array([0.0, s['spine'][1] * ys * 0.45, 0.0])))
            Wc = qmul(Ws, q(np.array([0.0, s['spine'][1] * ys * 0.25, 0.0])))
            got = qrot(Wc, np.array([0.0, 0.0, -1.0]))   # avatar face = -Z
            print("  %-24s yawScale=%.0f  want %s got %s  err %5.1f deg   (hips.y=%+7.1f spine.y=%+7.1f deg)"
                  % (pname, ys, fmt(want), fmt(got), ang(want, got),
                     math.degrees(s['hips'][1]), math.degrees(s['spine'][1])))

if __name__ == '__main__':
    main()
