"""Hybrid (Kalidokit legs + FK-aim arms) and noise robustness."""
import math, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from trace import *
from audit import poses, REST, unit, ang, fmt, expected, BONES, run
from exp import run_fk_aim, qfromto, qinv, ARMS, LEGS

def run_hybrid(pf):
    """arms via FK aim, legs+torso exactly as shipping."""
    lm = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
    legs = calc_legs(lm)
    q = lambda e: three_euler_to_unity(e, (1, 1, 1), 2)
    I = np.array([0.0, 0.0, 0.0, 1.0])
    W = {'hips': I, 'spine': I, 'chest': I}
    mx = lambda v: np.array([-v[0], v[1], v[2]])
    tgt = {'leftUpperArm': mx(unit(pf[14]-pf[12])), 'leftLowerArm': mx(unit(pf[16]-pf[14])),
           'rightUpperArm': mx(unit(pf[13]-pf[11])), 'rightLowerArm': mx(unit(pf[15]-pf[13]))}
    for bone, par in [('leftUpperArm','chest'),('leftLowerArm','leftUpperArm'),
                      ('rightUpperArm','chest'),('rightLowerArm','rightUpperArm')]:
        wl = qrot(qinv(W[par]), tgt[bone])
        W[bone] = qmul(W[par], qfromto(REST[bone], wl))
    W['leftUpperLeg']  = qmul(W['hips'], q(legs['uL'])); W['leftLowerLeg']  = qmul(W['leftUpperLeg'], q(legs['lL']))
    W['rightUpperLeg'] = qmul(W['hips'], q(legs['uR'])); W['rightLowerLeg'] = qmul(W['rightUpperLeg'], q(legs['lR']))
    return {k: qrot(W[k], REST[k]) for k in REST}

def score(fn, group, jitter=0.0, seed=0, reps=1):
    rng = np.random.default_rng(seed)
    errs = []
    for name, pf0 in poses().items():
        for _ in range(reps):
            pf = {k: (v + rng.normal(0, jitter, 3) if np.any(v) else v) for k, v in pf0.items()} if jitter else pf0
            d = fn(pf)
            exp = expected(pf0)          # score against the CLEAN pose
            errs += [ang(exp[b], d[b]) for b in group]
    return float(np.mean(errs)), float(np.max(errs))

from exp import run_variant
print("%-32s | %-18s | %-18s" % ('variant', 'ARMS mean/max deg', 'LEGS mean/max deg'))
for label, fn in [('BASELINE (shipping)', lambda pf: run_variant(pf)),
                  ('HYBRID: FK-aim arms only', run_hybrid),
                  ('FK aim everywhere', run_fk_aim)]:
    a = score(fn, ARMS); l = score(fn, LEGS)
    print("%-32s | %8.1f /%8.1f | %8.1f /%8.1f" % (label, a[0], a[1], l[0], l[1]))

print("")
print("--- noise robustness: landmark jitter sigma (metres), 40 reps, scored vs clean pose ---")
print("%-32s %10s %10s %10s %10s" % ('variant', 'sig=0.00', 'sig=0.01', 'sig=0.02', 'sig=0.04'))
for label, fn in [('BASELINE (shipping) arms', lambda pf: run_variant(pf)),
                  ('FK-aim arms', run_hybrid)]:
    row = []
    for s in (0.0, 0.01, 0.02, 0.04):
        row.append(score(fn, ARMS, jitter=s, seed=7, reps=40)[0])
    print("%-32s %10.1f %10.1f %10.1f %10.1f" % (label, *row))
for label, fn in [('BASELINE (shipping) legs', lambda pf: run_variant(pf)),
                  ('FK-aim legs', run_fk_aim)]:
    row = []
    for s in (0.0, 0.01, 0.02, 0.04):
        row.append(score(fn, LEGS, jitter=s, seed=7, reps=40)[0])
    print("%-32s %10.1f %10.1f %10.1f %10.1f" % (label, *row))

print("")
print("--- torso yaw GAIN check (sum of Kalidokit per-bone dampeners) ---")
for turn in (15, 30, 45, 60, 90):
    pf = {}
    import audit
    p0 = audit.base_skeleton()
    pf = audit.rotY(p0, turn)
    lm = [np.array([pf[i][0], -pf[i][1], pf[i][2]]) for i in range(33)]
    s = solve_pose(lm)
    q = lambda e: three_euler_to_unity(e, (1, 1, 1), 2)
    Wh = q(np.array([0.0, s['hips'][1]*0.7, 0.0]))
    Ws = qmul(Wh, q(np.array([0.0, s['spine'][1]*0.45, 0.0])))
    Wc = qmul(Ws, q(np.array([0.0, s['spine'][1]*0.25, 0.0])))
    got = qrot(Wc, np.array([0.0, 0.0, -1.0]))
    yaw_out = math.degrees(math.atan2(got[0], -got[2]))
    print("  source turn %3d deg  ->  solver hips.y=%+6.1f  chest facing yaw=%+7.1f deg  gain=%.2f"
          % (turn, math.degrees(s['hips'][1]), yaw_out, yaw_out / turn))

print("")
print("--- spine-bend high-pass decay (baselineTau = 8 s, ADR-026) ---")
for t in (0, 1, 2, 4, 8, 16):
    print("  bend held %2d s -> %5.1f %% of the real bend still applied" % (t, 100.0 * math.exp(-t / 8.0)))
