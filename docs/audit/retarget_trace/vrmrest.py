"""Parse a .vrm (glb) and report humanoid bone rest positions/directions in UNITY model space.

UniVRM Vrm10.LoadBytesAsync(..., importerContextSettings: null) -> ImporterContextSettings default
InvertAxis = Axes.Z  => glTF->Unity conversion negates Z (UnityExtensions.ReverseZ).
Confirmed empirically: the avatar in SR.mp4 faces the Mirror-scene camera which sits at z=-3.41
looking along +Z, i.e. the model faces -Z => Z was negated on import (a +Z-facing VRM1.0 glTF).
"""
import json, struct, sys, math
import numpy as np

def read_glb(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, ver, length = struct.unpack_from('<III', data, 0)
    assert magic == 0x46546C67, 'not a glb'
    off = 12
    js = None
    while off < length:
        clen, ctype = struct.unpack_from('<II', data, off)
        off += 8
        chunk = data[off:off+clen]
        if ctype == 0x4E4F534A:
            js = json.loads(chunk.decode('utf-8'))
        off += clen
        off += (-clen) % 4 if False else 0
    return js

def trs(node):
    t = np.array(node.get('translation', [0,0,0]), dtype=float)
    r = node.get('rotation', [0,0,0,1])
    s = np.array(node.get('scale', [1,1,1]), dtype=float)
    x,y,z,w = r
    # rotation matrix from quaternion (right-handed, glTF)
    R = np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w),   2*(x*z+y*w)],
        [2*(x*y+z*w),   1-2*(x*x+z*z), 2*(y*z-x*w)],
        [2*(x*z-y*w),   2*(y*z+x*w),   1-2*(x*x+y*y)],
    ], dtype=float)
    M = np.eye(4)
    M[:3,:3] = R @ np.diag(s)
    M[:3,3] = t
    return M

def world_positions(js):
    nodes = js['nodes']
    parent = {}
    for i,n in enumerate(nodes):
        for c in n.get('children', []):
            parent[c] = i
    wm = {}
    def solve(i):
        if i in wm: return wm[i]
        M = trs(nodes[i])
        if i in parent:
            M = solve(parent[i]) @ M
        wm[i] = M
        return M
    for i in range(len(nodes)):
        solve(i)
    return {i: wm[i][:3,3] for i in range(len(nodes))}, parent

def humanoid_map(js):
    ext = js.get('extensions', {})
    if 'VRMC_vrm' in ext:
        hb = ext['VRMC_vrm']['humanoid']['humanBones']
        return {k: v['node'] for k,v in hb.items()}, '1.0'
    if 'VRM' in ext:
        hb = ext['VRM']['humanoid']['humanBones']
        return {b['bone']: b['node'] for b in hb}, '0.x'
    raise SystemExit('no VRM extension')

def to_unity(p):
    return np.array([p[0], p[1], -p[2]])

CHAIN = [
    ('hips','spine'), ('spine','chest'), ('chest','neck'), ('neck','head'),
    ('leftUpperArm','leftLowerArm'), ('leftLowerArm','leftHand'),
    ('rightUpperArm','rightLowerArm'), ('rightLowerArm','rightHand'),
    ('leftUpperLeg','leftLowerLeg'), ('leftLowerLeg','leftFoot'),
    ('rightUpperLeg','rightLowerLeg'), ('rightLowerLeg','rightFoot'),
]

def main(path):
    js = read_glb(path)
    hm, spec = humanoid_map(js)
    wp, parent = world_positions(js)
    print(f'--- {path.split("/")[-1]}  spec={spec}  bones={len(hm)}')
    for a,b in CHAIN:
        if a not in hm or b not in hm:
            print(f'  {a:16s} -> {b:16s}  MISSING')
            continue
        pa = to_unity(wp[hm[a]]); pb = to_unity(wp[hm[b]])
        d = pb-pa
        n = np.linalg.norm(d)
        u = d/n if n>1e-9 else d
        print(f'  {a:16s} -> {b:16s}  len={n:.4f}  dir=({u[0]:+.3f},{u[1]:+.3f},{u[2]:+.3f})')
    # also print absolute positions of a few
    for k in ['hips','leftShoulder','rightShoulder','leftUpperArm','rightUpperArm','head']:
        if k in hm:
            p = to_unity(wp[hm[k]])
            print(f'  pos {k:16s} = ({p[0]:+.4f},{p[1]:+.4f},{p[2]:+.4f})')

if __name__ == '__main__':
    for p in sys.argv[1:]:
        try:
            main(p)
        except Exception as e:
            print(f'--- {p}: FAILED {e}')
