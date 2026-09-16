"""
Virtual Mirror — canonical VRM 1.0 rig template generator.

Builds the EXACT skeleton + expressions the tracking/retarget pipeline expects
(see docs/27_CharacterRigSpec.md), exports a valid VRM 1.0, and saves the .blend
as an artist starting point.

The rig is reverse-engineered from the pipeline contract:
  - Skeleton  = Unity Humanoid bones the retarget binds (Kalidokit normalized
                control rig + IK driver).
  - Expressions = the VRM preset keys VrmExpressionRetargeter writes.
  - Rest pose = T-pose (the retarget's neutral), character facing -Y in Blender
                (the VRM addon exports this as VRM +Z-forward, Y-up).

Run headless (no UI needed):
  "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" ^
     --background --python build_rig.py

Outputs (next to this script, in ./output/):
  VirtualMirrorRigTemplate.vrm    export-ready VRM 1.0
  VirtualMirrorRigTemplate.blend  editable source

Requires the "VRM Add-on for Blender" (saturday06, MIT) enabled as io_scene_vrm.
"""

import os
import math
import bpy

# --------------------------------------------------------------------------- #
# 0. Config
# --------------------------------------------------------------------------- #
ADDON = "io_scene_vrm"
MODEL_NAME = "VirtualMirrorRigTemplate"
OUT_DIR = os.path.join(os.path.dirname(bpy.data.filepath or __file__), "output")
# When run via --python, __file__ is the script path; fall back to CWD-safe path.
if not os.path.dirname(__file__):
    OUT_DIR = os.path.join(os.getcwd(), "output")
else:
    OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")

# Coordinate convention (Blender, meters, Z-up):
#   +Z  = up
#   -Y  = character FRONT (faces -Y, standard VRM-addon front view)
#   +X  = character's LEFT   (left arm/leg on +X; VRM/VRoid convention)
# The VRM addon converts this to VRM 1.0 space (Y-up, +Z forward) on export.

# --------------------------------------------------------------------------- #
# 1. Bone table  (name, humanoid_snake, parent, head(x,y,z), tail(x,y,z))
#    Left side + center defined explicitly; right side mirrored (negate X).
# --------------------------------------------------------------------------- #

def finger(prefix, snake_prefix, x0, y, z, segs, parent):
    """Return list of 3 phalanx bones marching along +X from x0."""
    names = ["Proximal", "Intermediate", "Distal"]
    snakes = ["proximal", "intermediate", "distal"]
    out = []
    x = x0
    p = parent
    for i in range(3):
        h = (x, y, z)
        x2 = x + segs[i]
        t = (x2, y, z)
        bn = f"Left{prefix}{names[i]}"
        sn = f"left_{snake_prefix}_{snakes[i]}"
        out.append((bn, sn, p, h, t))
        p = bn
        x = x2
    return out


# Center chain -------------------------------------------------------------- #
CENTER = [
    ("Hips",       "hips",        None,        (0.0, 0.0, 0.95), (0.0, 0.0, 1.02)),
    ("Spine",      "spine",       "Hips",      (0.0, 0.0, 1.02), (0.0, 0.0, 1.20)),
    ("Chest",      "chest",       "Spine",     (0.0, 0.0, 1.20), (0.0, 0.0, 1.34)),
    ("UpperChest", "upper_chest", "Chest",     (0.0, 0.0, 1.34), (0.0, 0.0, 1.45)),
    ("Neck",       "neck",        "UpperChest",(0.0, 0.0, 1.45), (0.0, 0.0, 1.55)),
    ("Head",       "head",        "Neck",      (0.0, 0.0, 1.55), (0.0, 0.0, 1.70)),
]

# Left arm (T-pose: horizontal along +X, palm down) ------------------------- #
Z_ARM = 1.44
LEFT_ARM = [
    ("LeftShoulder", "left_shoulder", "UpperChest", (0.02, 0.0, Z_ARM), (0.14, 0.0, Z_ARM)),
    ("LeftUpperArm", "left_upper_arm", "LeftShoulder", (0.14, 0.0, Z_ARM), (0.42, 0.0, Z_ARM)),
    ("LeftLowerArm", "left_lower_arm", "LeftUpperArm", (0.42, 0.0, Z_ARM), (0.66, 0.0, Z_ARM)),
    ("LeftHand",     "left_hand",      "LeftLowerArm", (0.66, 0.0, Z_ARM), (0.78, 0.0, Z_ARM)),
    ("LeftEye",      "left_eye",       "Head",         (0.03, -0.06, 1.60), (0.03, -0.10, 1.60)),
]

# Left fingers -------------------------------------------------------------- #
FSEG = [0.035, 0.028, 0.024]      # proximal, intermediate, distal lengths
LEFT_FINGERS = []
LEFT_FINGERS += finger("Index",  "index",  0.80, -0.035, Z_ARM, FSEG, "LeftHand")
LEFT_FINGERS += finger("Middle", "middle", 0.80, -0.012, Z_ARM, FSEG, "LeftHand")
LEFT_FINGERS += finger("Ring",   "ring",   0.80,  0.012, Z_ARM, FSEG, "LeftHand")
LEFT_FINGERS += finger("Little", "little", 0.79,  0.035, Z_ARM, [0.030, 0.024, 0.020], "LeftHand")
# Thumb: VRM 1.0 = metacarpal, proximal, distal (angled forward -Y and +X, down)
LEFT_FINGERS += [
    ("LeftThumbMetacarpal", "left_thumb_metacarpal", "LeftHand",             (0.70, -0.045, 1.42), (0.75, -0.075, 1.41)),
    ("LeftThumbProximal",   "left_thumb_proximal",   "LeftThumbMetacarpal",  (0.75, -0.075, 1.41), (0.79, -0.100, 1.40)),
    ("LeftThumbDistal",     "left_thumb_distal",     "LeftThumbProximal",    (0.79, -0.100, 1.40), (0.82, -0.120, 1.39)),
]

# Left leg ------------------------------------------------------------------ #
LEFT_LEG = [
    ("LeftUpperLeg", "left_upper_leg", "Hips",         (0.09, 0.0, 0.92), (0.10, 0.0, 0.52)),
    ("LeftLowerLeg", "left_lower_leg", "LeftUpperLeg", (0.10, 0.0, 0.52), (0.11, 0.0, 0.10)),
    ("LeftFoot",     "left_foot",      "LeftLowerLeg", (0.11, 0.0, 0.10), (0.11, -0.12, 0.03)),
    ("LeftToes",     "left_toes",      "LeftFoot",     (0.11, -0.12, 0.03), (0.11, -0.20, 0.02)),
]

LEFT_ALL = LEFT_ARM + LEFT_FINGERS + LEFT_LEG


def mirror_bone(b):
    """Mirror a Left* bone to Right* (negate X on head/tail, swap names/snake)."""
    name, snake, parent, head, tail = b
    rname = name.replace("Left", "Right", 1)
    rsnake = snake.replace("left_", "right_", 1)
    rparent = parent.replace("Left", "Right", 1) if parent and parent.startswith("Left") else parent
    rhead = (-head[0], head[1], head[2])
    rtail = (-tail[0], tail[1], tail[2])
    return (rname, rsnake, rparent, rhead, rtail)


RIGHT_ALL = [mirror_bone(b) for b in LEFT_ALL]
BONES = CENTER + LEFT_ALL + RIGHT_ALL


# --------------------------------------------------------------------------- #
# 2. Scene reset + armature
# --------------------------------------------------------------------------- #
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build_armature():
    arm_data = bpy.data.armatures.new(MODEL_NAME)
    arm_obj = bpy.data.objects.new(MODEL_NAME, arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.select_set(True)

    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    created = {}
    # first pass: create all bones with head/tail
    for name, snake, parent, head, tail in BONES:
        b = eb.new(name)
        b.head = head
        b.tail = tail
        b.use_connect = False
        created[name] = b
    # second pass: parent
    for name, snake, parent, head, tail in BONES:
        if parent:
            created[name].parent = created[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


# --------------------------------------------------------------------------- #
# 3. Placeholder skinned mesh (rigid per-part weights) — artists replace this
# --------------------------------------------------------------------------- #
def add_box(name, bone, center, size):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    vg = ob.vertex_groups.new(name=bone)
    vg.add(range(len(ob.data.vertices)), 1.0, "REPLACE")
    return ob


def build_mesh(arm_obj):
    parts = [
        # name, bone, center, size(x,y,z)
        ("m_hips",   "Hips",       (0.0, 0.0, 0.98), (0.24, 0.16, 0.14)),
        ("m_spine",  "Spine",      (0.0, 0.0, 1.12), (0.26, 0.16, 0.16)),
        ("m_chest",  "Chest",      (0.0, 0.0, 1.30), (0.30, 0.18, 0.20)),
        ("m_uchest", "UpperChest", (0.0, 0.0, 1.42), (0.30, 0.18, 0.10)),
        ("m_neck",   "Neck",       (0.0, 0.0, 1.50), (0.07, 0.07, 0.10)),
        ("m_head",   "Head",       (0.0, -0.01, 1.63),(0.16, 0.18, 0.20)),
    ]
    # limbs (left) + mirror
    limb = [
        ("m_uarm", "UpperArm", (0.28, 0.0, Z_ARM), (0.28, 0.09, 0.09)),
        ("m_larm", "LowerArm", (0.54, 0.0, Z_ARM), (0.24, 0.07, 0.07)),
        ("m_hand", "Hand",     (0.72, 0.0, Z_ARM), (0.12, 0.09, 0.03)),
        ("m_uleg", "UpperLeg", (0.095, 0.0, 0.72), (0.13, 0.14, 0.40)),
        ("m_lleg", "LowerLeg", (0.105, 0.0, 0.31), (0.11, 0.12, 0.42)),
        ("m_foot", "Foot",     (0.11, -0.06, 0.05),(0.10, 0.22, 0.06)),
    ]
    objs = []
    for n, bone, c, s in parts:
        objs.append(add_box(n, bone, c, s))
    for n, bsuffix, c, s in limb:
        objs.append(add_box(n + "_L", "Left" + bsuffix, c, s))
        cr = (-c[0], c[1], c[2])
        objs.append(add_box(n + "_R", "Right" + bsuffix, cr, s))

    # join into one Body mesh
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    body = objs[0]
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = "Body"

    # simple material (VRM needs a material)
    mat = bpy.data.materials.new("Body")
    mat.use_nodes = True
    body.data.materials.append(mat)

    # armature modifier + parent
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    body.parent = arm_obj
    return body


# --------------------------------------------------------------------------- #
# 4. Expression shape keys (placeholders — artists sculpt the deltas)
# --------------------------------------------------------------------------- #
EXPRESSION_KEYS = [
    # (shape_key_name, vrm_preset_attr)  — the presets the pipeline uses + full visemes
    ("blink",     "blink"),
    ("blinkLeft", "blink_left"),
    ("blinkRight","blink_right"),
    ("aa",        "aa"),
    ("ih",        "ih"),
    ("ou",        "ou"),
    ("ee",        "ee"),
    ("oh",        "oh"),
    ("happy",     "happy"),
    ("angry",     "angry"),
    ("sad",       "sad"),
    ("relaxed",   "relaxed"),
    ("surprised", "surprised"),
]


def build_shape_keys(body):
    body.shape_key_add(name="Basis", from_mix=False)
    for key_name, _ in EXPRESSION_KEYS:
        body.shape_key_add(name=key_name, from_mix=False)


# --------------------------------------------------------------------------- #
# 5. VRM extension: spec 1.0, meta, humanoid map, expressions, spring scaffold
# --------------------------------------------------------------------------- #
def setup_vrm(arm_obj, body):
    ext = arm_obj.data.vrm_addon_extension
    ext.spec_version = ext.SPEC_VERSION_VRM1

    # --- meta (minimal valid) ---
    meta = ext.vrm1.meta
    _try(lambda: setattr(meta, "vrm_name", MODEL_NAME))
    _try(lambda: setattr(meta, "version", "1.0"))
    _try(lambda: _add_author(meta, "ViitorCloud"))
    _try(lambda: setattr(meta, "license_url", "https://vrm.dev/licenses/1.0/"))

    # --- humanoid bone map ---
    hb = arm_obj.data.vrm_addon_extension.vrm1.humanoid.human_bones
    for name, snake, parent, head, tail in BONES:
        node = getattr(hb, snake, None)
        if node is not None:
            node.node.bone_name = name
        else:
            print("WARN no humanoid slot for", snake)

    # --- expressions bound to shape keys ---
    preset = ext.vrm1.expressions.preset
    for key_name, attr in EXPRESSION_KEYS:
        expr = getattr(preset, attr, None)
        if expr is None:
            print("WARN no expression preset", attr)
            continue
        bind = expr.morph_target_binds.add()
        bind.node.mesh_object_name = body.name
        bind.index = key_name
        bind.weight = 1.0

    # --- spring bone scaffold (example: none by default, hook documented) ---
    # Artists add hair/skirt chains here. Left intentionally empty so the
    # template has no phantom physics; see docs/27 §Spring Bones.

    # validate assignment
    try:
        arm_obj.data.vrm_addon_extension.vrm1.humanoid.human_bones.fixup_human_bones(arm_obj)
    except Exception as e:
        print("fixup_human_bones:", e)


def _try(fn):
    try:
        fn()
    except Exception as e:
        print("meta set skipped:", e)


def _add_author(meta, name):
    if hasattr(meta, "authors"):
        a = meta.authors.add()
        # element value field name varies; try common ones
        for f in ("value", "name", "author"):
            if hasattr(a, f):
                setattr(a, f, name)
                return


# --------------------------------------------------------------------------- #
# 6. Export + save
# --------------------------------------------------------------------------- #
def export(arm_obj):
    os.makedirs(OUT_DIR, exist_ok=True)
    vrm_path = os.path.join(OUT_DIR, MODEL_NAME + ".vrm")
    blend_path = os.path.join(OUT_DIR, MODEL_NAME + ".blend")

    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj

    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("SAVED_BLEND", blend_path)

    try:
        bpy.ops.export_scene.vrm(filepath=vrm_path)
        print("EXPORTED_VRM", vrm_path, os.path.getsize(vrm_path))
    except Exception as e:
        import traceback
        traceback.print_exc()
        print("EXPORT_ERR", e)


# --------------------------------------------------------------------------- #
def main():
    reset_scene()  # factory reset first (this disables addons)...
    if ADDON not in bpy.context.preferences.addons.keys():
        bpy.ops.preferences.addon_enable(module=ADDON)  # ...then re-enable VRM
    arm = build_armature()
    body = build_mesh(arm)
    build_shape_keys(body)
    setup_vrm(arm, body)
    export(arm)
    print("BONE_COUNT", len(arm.data.bones))
    print("DONE")


if __name__ == "__main__":
    main()
