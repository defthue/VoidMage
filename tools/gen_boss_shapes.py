"""Hit shapes for the Bone Colossus: a few oriented boxes per part that follow the model instead of one big box.

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/gen_boss_shapes.py

Writes Assets/Boss/boss_shapes.json (read by tools/gen_boss_prefabs.py):
    skull    cranium + the two horns (vertices split on the horn axis, PCA box each)
    jaw      one box (parented to the skull's `jaw` child at runtime, so it rides the hinge)
    hand_l/r forearm, a wrist box (the forearm's end, rides the forearm child), the palm, and one box PER BONE of
             every finger (thumb_1..3, index_1..3, ...; parent `bone:<name>`, so BossPart pins each box to its
             animated bone and the hit shapes follow the fist/flat poses)
Boxes are in model units (the imported Blender frame x 39.37, the same frame the rig json and the stance
rotations use), padded by PAD and scaled by GROW so a hit that grazes the bone still counts.
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig_hell_hand as rig  # noqa: E402

PROJ = os.path.normpath(os.path.join(HERE, ".."))
OUT = os.path.join(PROJ, "Assets", "Boss", "boss_shapes.json")
U = 39.37          # metres (Blender import) -> model units (inches)
GROW = 1.12
PAD = 8.0          # model units, each side
# the hand bones get more: the red box is the player's forgiveness for hitting a finger-thin bone with a sword,
# and the strike surface the hand hits the player with is this box pulled back in by boss_hit_forgive
GROW_HAND = 1.25
PAD_HAND = 18.0
# Blender import frame -> s&box model frame: the skull faces -y in Blender and +x in the engine (verified by
# rendering: horns along Blender x, jaw toward -y; in the engine the jaw swings about y and the eyes sit at +x),
# so model = Rz(+90 deg) . blender: (x, y, z)_model = (-y, x, z)_blender
FRAME = Matrix(((0, -1, 0), (1, 0, 0), (0, 0, 1)))


def to_model(v):
    return FRAME @ Vector(v)
WRIST_REACH = 1.0   # metres of the forearm nearest the wrist that count as the wrist (a strike surface)


def log(*a):
    print("[shapes]", *a, flush=True)


def obb(pts, name, grow=GROW, pad=PAD):
    """PCA-oriented box of a point cloud (metres): centre, quaternion (x,y,z,w) and size in model units."""
    pts = np.asarray(pts, dtype=float)
    mean = pts.mean(axis=0)
    cov = np.cov((pts - mean).T)
    w, v = np.linalg.eigh(cov)
    order = np.argsort(w)[::-1]
    axes = v[:, order]
    if np.linalg.det(axes) < 0:
        axes[:, 2] *= -1
    proj = (pts - mean) @ axes
    lo, hi = proj.min(axis=0), proj.max(axis=0)
    centre = to_model(mean + axes @ ((lo + hi) * 0.5))
    size = (hi - lo) * U * grow + 2 * pad
    q = (FRAME @ Matrix(axes.tolist())).to_quaternion()
    return {"name": name, "center": [round(float(c) * U, 2) for c in centre],
            "rot": [round(float(q.x), 6), round(float(q.y), 6), round(float(q.z), 6), round(float(q.w), 6)],
            "size": [round(float(s), 2) for s in size]}


def load(stem):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(PROJ, "Assets", "DungeonRealms", stem + ".fbx"))
    ob = next(o for o in bpy.data.objects if o.type == "MESH" and not o.name.endswith("_Outline"))
    M = ob.matrix_world
    pts = np.array([list(M @ v.co) for v in ob.data.vertices])
    return ob, pts


def hand(side):
    ob, pts = load("SM_Env_Hell_Bone_Hand_%s_01" % side.upper())
    comps = rig.components(ob.data)
    M = ob.matrix_world
    pieces = [rig.Piece(i, comp, [M @ ob.data.vertices[j].co for j in comp]) for i, comp in enumerate(comps)]
    W = rig.classify(pieces)[0]
    shapes = []
    with open(os.path.join(PROJ, "Assets", "Boss", "hand_%s_rig.json" % side)) as f:
        wrist = json.load(f)["wrist_model"]

    def on_forearm(s):
        # the forearm is its own child object with the wrist as its origin (the split): the box rides it
        s["center"] = [round(c - w, 2) for c, w in zip(s["center"], wrist)]
        s["parent"] = "forearm"
        return s

    fore_idx = [j for p in pieces if p.bone == "forearm" for j in p.verts]
    shapes.append(on_forearm(obb(pts[fore_idx], "forearm")))
    near = [j for j in fore_idx if (Vector(pts[j]) - W).length <= WRIST_REACH]
    if near:
        shapes.append(on_forearm(obb(pts[near], "wrist", GROW_HAND, PAD_HAND)))
    palm_idx = [j for p in pieces if p.bone == "palm" for j in p.verts]
    s = obb(pts[palm_idx], "palm", GROW_HAND, PAD_HAND)
    s["parent"] = "bone:palm"
    shapes.append(s)
    for bone in rig.finger_bone_names():
        idx = [j for p in pieces if p.bone == bone for j in p.verts]
        if not idx:
            log("  %s: no piece for %s" % (side, bone))
            continue
        s = obb(pts[idx], bone, GROW_HAND, PAD_HAND)
        s["parent"] = "bone:" + bone
        shapes.append(s)
    log("hand_%s: %d shapes" % (side, len(shapes)))
    return shapes


def sphere(pts, name, pct=95.0):
    """A sphere fitted to a point cloud (metres): centre = the cloud's bounds centre, radius = the `pct` percentile
    distance (a few stray spikes do not blow it up), grown and padded like the boxes; model units."""
    pts = np.asarray(pts, dtype=float)
    centre = (pts.min(axis=0) + pts.max(axis=0)) * 0.5
    d = np.linalg.norm(pts - centre, axis=1)
    r = float(np.percentile(d, pct)) * U * GROW + PAD
    c = to_model(centre)
    return {"name": name, "center": [round(float(v) * U, 2) for v in c], "rot": [0.0, 0.0, 0.0, 1.0], "size": [round(r, 2)]}


def skull():
    """The cranium is a sphere (the whole head reads as a ball), the horns stay boxes."""
    ob, pts = load("SM_Env_Hell_Bone_Skull_01")
    span = pts.max(axis=0) - pts.min(axis=0)
    axis = int(np.argmax(span))
    half = span[axis] * 0.22
    a = pts[:, axis] - pts[:, axis].mean()
    shapes = [sphere(pts[np.abs(a) <= half], "cranium"), obb(pts[a > half], "horn_a"), obb(pts[a < -half], "horn_b")]
    log("skull: horn axis %s, span %s" % ("xyz"[axis], [round(float(s) * U) for s in span]))
    return shapes


def jaw():
    ob, pts = load("SM_Env_Hell_Bone_Skull_Jaw_01")
    return [sphere(pts, "jaw", 90.0)]


def main():
    out = {"skull": skull(), "jaw": jaw(), "hand_l": hand("l"), "hand_r": hand("r")}
    with open(OUT, "w") as f:
        json.dump(out, f, indent=1)
    for part, shapes in out.items():
        for s in shapes:
            log("  %-7s %-8s centre %s size %s" % (part, s["name"], s["center"], s["size"]))
    log("wrote", OUT)


if __name__ == "__main__":
    main()
