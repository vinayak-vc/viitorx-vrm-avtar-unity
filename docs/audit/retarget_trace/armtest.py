"""Deterministic arm-retarget acceptance harness: A1-A11, symmetry, roll, noise."""
import math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from armaim import *
from trace import solve_arms, three_euler_to_unity, qmul as kqmul, qrot as kqrot

REST_U_L = np.array([1.0, 0.0, 0.0])    # measured from the .vrm (audit §2)
REST_U_R = np.array([-1.0, 0.0, 0.0])
REST_FWD = np.array([0.0, 0.0, -1.0])   # VRM faces -Z in Unity after UniVRM's ReverseZ import
REST_N_L = unit(np.cross(REST_U_L, REST_FWD))   # = +Y : left elbow points FORWARD when bent
REST_N_R = unit(np.cross(REST_U_R, REST_FWD))   # = -Y

# ------------------------------------------------------------------ fixtures
def base():
    p = {i: np.zeros(3) for i in range(33)}
    p[0]  = np.array([0.00,  0.72, -0.10])
    p[11] = np.array([ 0.18, 0.50, 0.00]); p[12] = np.array([-0.18, 0.50, 0.00])
    p[23] = np.array([ 0.10, 0.00, 0.00]); p[24] = np.array([-0.10, 0.00, 0.00])
    p[13] = np.array([ 0.20, 0.22, 0.00]); p[15] = np.array([ 0.21, -0.05, 0.00])
    p[14] = np.array([-0.20, 0.22, 0.00]); p[16] = np.array([-0.21, -0.05, 0.00])
    p[25] = np.array([ 0.10,-0.45, 0.00]); p[27] = np.array([ 0.10, -0.90, 0.00])
    p[26] = np.array([-0.10,-0.45, 0.00]); p[28] = np.array([-0.10, -0.90, 0.00])
    return p

UP, LO = 0.28, 0.26   # upper-arm / forearm lengths

def set_arm(p, side, upper_dir, fore_dir):
    """side 'L' = subject's LEFT (indices 11/13/15), 'R' = subject's RIGHT (12/14/16)."""
    sh, el, wr = (11, 13, 15) if side == 'L' else (12, 14, 16)
    p[el] = p[sh] + unit(upper_dir) * UP
    p[wr] = p[el] + unit(fore_dir) * LO
    return p

def rotY(p, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    R = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return {k: (R @ v if np.any(v) else v) for k, v in p.items()}

DOWN = np.array([0.0, -1.0, 0.0]); UPV = np.array([0.0, 1.0, 0.0])
OUT_L = np.array([1.0, 0.0, 0.0]); OUT_R = np.array([-1.0, 0.0, 0.0])
FWD = np.array([0.0, 0.0, -1.0]); BACK = np.array([0.0, 0.0, 1.0])

def poses():
    o = {}
    o['A1  standing arms down'] = base()
    p = base(); set_arm(p, 'R', UPV, UPV);                        o['A2  right arm straight up'] = p
    p = base(); set_arm(p, 'L', UPV, UPV);                        o['A3  left arm straight up'] = p
    p = base(); set_arm(p, 'L', UPV, UPV); set_arm(p, 'R', UPV, UPV); o['A4  both arms overhead'] = p
    p = base(); set_arm(p, 'L', OUT_L, OUT_L); set_arm(p, 'R', OUT_R, OUT_R); o['A5  T-pose'] = p
    p = base(); set_arm(p, 'L', FWD, FWD); set_arm(p, 'R', FWD, FWD); o['A6  arms forward'] = p
    p = base(); set_arm(p, 'L', BACK, BACK); set_arm(p, 'R', BACK, BACK); o['A7  arms backward'] = p
    p = base(); set_arm(p, 'L', OUT_L, FWD); set_arm(p, 'R', OUT_R, FWD); o['A8  elbows bent fwd'] = p
    p = base(); set_arm(p, 'L', UPV, unit(FWD+DOWN)); set_arm(p, 'R', OUT_R, unit(OUT_R+DOWN))
    o['A9  asymmetric'] = p
    o['A10 body rotated 45deg'] = rotY(base(), 45.0)
    o['A11 body rotated 90deg'] = rotY(base(), 90.0)
    return o

def roll_poses():
    o = {}
    p = base(); set_arm(p, 'L', OUT_L, FWD);  o['R1 horiz, elbow fwd'] = p
    p = base(); set_arm(p, 'L', OUT_L, BACK); o['R2 horiz, elbow back'] = p
    p = base(); set_arm(p, 'L', OUT_L, UPV);  o['R3 horiz, forearm up'] = p
    p = base(); set_arm(p, 'L', OUT_L, DOWN); o['R4 horiz, forearm down'] = p
    p = base(); set_arm(p, 'L', UPV, FWD);    o['R5 overhead, forearm fwd'] = p
    p = base(); set_arm(p, 'L', UPV, BACK);   o['R6 overhead, forearm back'] = p
    return o

# ------------------------------------------------------------------ evaluation
def want_dirs(pf):
    """Wanted avatar directions: Kalidokit cross-map + sagittal mirror (audit §3)."""
    mx = lambda v: np.array([-v[0], v[1], v[2]])
    return {
        'leftUpperArm':  mx(unit(pf[14]-pf[12])), 'leftLowerArm':  mx(unit(pf[16]-pf[14])),
        'rightUpperArm': mx(unit(pf[13]-pf[11])), 'rightLowerArm': mx(unit(pf[15]-pf[13])),
    }

def run_new(pf, stL=None, stR=None):
    """New aim solver. Torso is left at rest (this task does not touch it), so parentWorld = identity."""
    stL = stL or ArmAimState(); stR = stR or ArmAimState()
    I = np.array([0.0, 0.0, 0.0, 1.0])
    out = {}; diag = {}
    # avatar LEFT arm  <- subject RIGHT arm (12/14/16)   [Kalidokit cross-map, preserved]
    qu, ql, d = solve_arm(pf[12], pf[14], pf[16], REST_U_L, REST_U_L, REST_N_L, stL)
    if qu is not None:
        out['leftUpperArm'] = qrot(qu, REST_U_L); out['leftLowerArm'] = qrot(ql, REST_U_L)
        diag['left'] = d
    # avatar RIGHT arm <- subject LEFT arm (11/13/15)
    qu, ql, d = solve_arm(pf[11], pf[13], pf[15], REST_U_R, REST_U_R, REST_N_R, stR)
    if qu is not None:
        out['rightUpperArm'] = qrot(qu, REST_U_R); out['rightLowerArm'] = qrot(ql, REST_U_R)
        diag['right'] = d
    return out, diag

def run_old(pf):
    lm = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
    s = solve_arms(lm)
    q = lambda e: three_euler_to_unity(e, (1, 1, 1), 2)
    I = np.array([0.0, 0.0, 0.0, 1.0])
    wuL = kqmul(I, q(s['uL'])); wlL = kqmul(wuL, q(s['lL']))
    wuR = kqmul(I, q(s['uR'])); wlR = kqmul(wuR, q(s['lR']))
    return {'leftUpperArm': kqrot(wuL, REST_U_L), 'leftLowerArm': kqrot(wlL, REST_U_L),
            'rightUpperArm': kqrot(wuR, REST_U_R), 'rightLowerArm': kqrot(wlR, REST_U_R)}

def ang(a, b):
    d = max(-1.0, min(1.0, float(np.dot(unit(a), unit(b)))))
    return math.degrees(math.acos(d))

BONES = ['leftUpperArm', 'leftLowerArm', 'rightUpperArm', 'rightLowerArm']
def fmt(v): return "(%+.2f,%+.2f,%+.2f)" % (v[0], v[1], v[2])

def section(t): print("\n" + "=" * 96 + "\n" + t + "\n" + "=" * 96)

def main():
    section("13. DETERMINISTIC POSE RESULTS  (A1-A11)  err = angle(want, got), degrees")
    print("%-26s %-30s %8s %8s | %8s %8s" % ('pose', 'bone', 'OLD', 'NEW', 'roll', 'bend'))
    old_all, new_all = [], []
    for name, pf in poses().items():
        want = want_dirs(pf)
        gnew, diag = run_new(pf)
        gold = run_old(pf)
        for b in BONES:
            eo = ang(want[b], gold[b]); en = ang(want[b], gnew[b])
            old_all.append(eo); new_all.append(en)
            side = 'left' if b.startswith('left') else 'right'
            d = diag.get(side, {})
            print("%-26s %-30s %8.1f %8.1f | %8.1f %8.1f"
                  % (name if b == BONES[0] else '', b, eo, en, d.get('rollDeg', 0.0), d.get('bendDeg', 0.0)))
    print("\n  OLD mean %.2f  max %.2f     NEW mean %.4f  max %.4f"
          % (np.mean(old_all), np.max(old_all), np.mean(new_all), np.max(new_all)))

    section("14. SYMMETRY  (perfectly symmetric source input -> |errL - errR| must be ~0)")
    print("%-26s %10s %10s %10s | %10s %10s %10s" % ('pose', 'OLD L', 'OLD R', 'OLD |d|', 'NEW L', 'NEW R', 'NEW |d|'))
    worst_new = 0.0
    for name in ['A1  standing arms down', 'A4  both arms overhead', 'A5  T-pose',
                 'A6  arms forward', 'A7  arms backward', 'A8  elbows bent fwd']:
        pf = poses()[name]; want = want_dirs(pf)
        gnew, _ = run_new(pf); gold = run_old(pf)
        ol = ang(want['leftUpperArm'], gold['leftUpperArm']); orr = ang(want['rightUpperArm'], gold['rightUpperArm'])
        nl = ang(want['leftUpperArm'], gnew['leftUpperArm']); nr = ang(want['rightUpperArm'], gnew['rightUpperArm'])
        worst_new = max(worst_new, abs(nl - nr))
        print("%-26s %10.1f %10.1f %10.1f | %10.4f %10.4f %10.4f" % (name, ol, orr, abs(ol-orr), nl, nr, abs(nl-nr)))
    print("\n  worst NEW L/R asymmetry: %.6f deg   (float32 direction round-trip is ~1e-3 deg)" % worst_new)

    section("15. ROLL  (elbow plane -> avatar bend normal; roll must follow, not flip)")
    print("%-26s %-22s %-22s %8s %8s %10s  %s"
          % ('pose', 'want bendNormal', 'got bendNormal', 'nErr', 'rollDeg', 'foreErr', 'normal src'))
    for name, pf in roll_poses().items():
        want = want_dirs(pf); gnew, diag = run_new(pf)
        # avatar LEFT arm is the one driven in these fixtures (subject's right arm is at rest)
        wn = unit(np.cross(want['rightUpperArm'], want['rightLowerArm']))
        gn = unit(np.cross(gnew['rightUpperArm'], gnew['rightLowerArm']))
        d = diag['right']
        print("%-26s %-22s %-22s %8.3f %8.1f %10.4f  %s"
              % (name, fmt(wn), fmt(gn), ang(wn, gn), d['rollDeg'],
                 ang(want['rightLowerArm'], gnew['rightLowerArm']), d['normalSource']))

    section("15b. ROLL CONTINUITY SWEEP  (forearm swept 0-350 deg around the upper-arm axis)")
    print("  arm held horizontal (subject's left, +X); forearm bent 60 deg, plane swept in 10 deg steps")
    st = ArmAimState()
    prev_roll = None; max_jump = 0.0; flips = 0
    rows = []
    for deg in range(0, 360, 10):
        a = math.radians(deg)
        # forearm 60 deg off the upper-arm axis, its bend plane rotated by `a` about the axis
        axis = OUT_L
        perp1 = np.array([0.0, 1.0, 0.0]); perp2 = np.cross(axis, perp1)
        radial = perp1*math.cos(a) + perp2*math.sin(a)
        fore = unit(axis*math.cos(math.radians(60)) + radial*math.sin(math.radians(60)))
        p = base(); set_arm(p, 'L', axis, fore)
        want = want_dirs(p)
        gnew, diag = run_new(p, stR=st) if False else (None, None)
        stR = st
        qu, ql, d = solve_arm(p[11], p[13], p[15], REST_U_R, REST_U_R, REST_N_R, stR)
        gotU = qrot(qu, REST_U_R); gotF = qrot(ql, REST_U_R)
        eu = ang(want['rightUpperArm'], gotU); ef = ang(want['rightLowerArm'], gotF)
        roll = d['rollDeg']
        if prev_roll is not None:
            jump = abs(((roll - prev_roll + 180.0) % 360.0) - 180.0)
            max_jump = max(max_jump, jump)
            if jump > 90.0:
                flips += 1
        prev_roll = roll
        rows.append((deg, roll, eu, ef))
    for i in range(0, len(rows), 3):
        print("   " + "   ".join("sweep%3d: roll%+7.1f uErr%6.3f fErr%6.3f" % r for r in rows[i:i+3]))
    print("\n  max roll step between adjacent 10 deg samples: %.1f deg   >90 deg flips: %d" % (max_jump, flips))
    print("  (a smooth 10 deg-per-step roll ramp and 0 flips == no helicopter, no 180 reversal)")

    section("16. NOISE SWEEP  (landmark jitter sigma metres, 40 reps, scored vs the clean pose)")
    print("%-22s %10s %10s %10s %10s" % ('variant', 'sig=0.00', 'sig=0.01', 'sig=0.02', 'sig=0.04'))
    for label, fn in [('OLD (RigArm)', 'old'), ('NEW (aim)', 'new')]:
        row = []
        for sig in (0.0, 0.01, 0.02, 0.04):
            rng = np.random.default_rng(7)
            errs = []
            for name, pf0 in poses().items():
                st_l, st_r = ArmAimState(), ArmAimState()
                for _ in range(40):
                    pf = {k: (v + rng.normal(0, sig, 3) if np.any(v) else v) for k, v in pf0.items()} if sig else pf0
                    want = want_dirs(pf0)
                    g = run_old(pf) if fn == 'old' else run_new(pf, st_l, st_r)[0]
                    errs += [ang(want[b], g[b]) for b in BONES if b in g]
            row.append(float(np.mean(errs)))
        print("%-22s %10.1f %10.1f %10.1f %10.1f" % (label, *row))

if __name__ == '__main__':
    main()
