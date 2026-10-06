"""Rig one of the Hell bone hands (the Bone Colossus boss's hands, Dungeon Realms SM_Env_Hell_Bone_Hand_R/L_01).

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/rig_hell_hand.py -- --hand r [--render <dir>] [--curl 1.0]

The hand is ONE mesh of 28 disconnected bones: two forearm bones, seven carpals, five metacarpals and fourteen
phalanges (three per finger, two for the thumb). Every piece is rigid, so the rig is one bone per moving piece
bound at weight 1.0 and there is no weight painting at all. The pieces are sorted by geometry, not by hand:

    forearm     the two longest pieces; the wrist W is the end of them nearest everything else
    metacarpals the five slender (length > 1.5x width) pieces whose near end is closest to W
    phalanges   chains grown from each metacarpal's far end, nearest free piece first, three rounds, greedy
                across all five chains so the thumb (two phalanges) stops when the tips run out
    carpals     whatever is left, which must be seven
    thumb       the chain with two phalanges; index..pinky are the rest ordered away from it
The two hands are different poses (R lies flat, L claws upward) but the same mesh topology, so the piece
indices agree and the classification is run on each and asserted equal.

Bones: root (model origin) > forearm > palm (carpals + the four finger metacarpals, rigid) > thumb_1 (its
metacarpal) > thumb_2 > thumb_3, and index/middle/ring/pinky _1.._3 under palm. The hand's frame is measured
from the PALM, never from the forearm (both sculpts have bent wrists, the right one by 72 degrees): the palm
normal N is the least-variance axis of the carpal + metacarpal points (signed toward the side the knuckles bend
to) and the finger direction F is the mean finger-metacarpal direction in that plane; the forearm axis D is
only the forearm bone. Every bone's roll is aligned to N so that a POSITIVE rotation about the bone's local X
curls the finger toward the palm; the existing bend of the sculpt at each joint is measured, and the poses are
ABSOLUTE bends from straight (the sculpt's bend is subtracted), so "open" is a flat hand and "fist" a fist.
Rest pose = the sculpt's own pose.

Clips (60 fps, Assets/Boss/Animations/hand_<r|l>/<clip>.fbx, armature only, the retarget_fpp export):
    open, fist, point, grab, splay   static poses (6 frames): the animgraph blends between them
    twitch                           2 s loop, a few degrees of drift per finger, seamless
Model: Assets/Boss/hand_<r|l>.fbx (mesh + outline + armature, the prep_skeleton_npc export), cm data with a
unit node scale like the skeleton characters: the vmdl keeps import_scale 1.0 and ModelModifier_ScaleAndMirror
0.3937 (tools/gen_boss_assets.py). --render writes a colour-per-bone PNG of the rest pose and of every clip's
last frame so the classification and the curl direction can be checked by eye.
"""
import argparse
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.normpath(os.path.join(HERE, ".."))
OUT = os.path.join(PROJ, "Assets", "Boss")
FPS = 60
RIG_NAME = "HellHandRig"
FINGERS = ["index", "middle", "ring", "pinky"]

# finger bends per joint, ABSOLUTE from straight (degrees, positive = toward the palm), [proximal, middle,
# distal]: the sculpt's measured bend at each joint is subtracted, so these read like a real hand's angles. The
# thumb is a DELTA on the sculpt [metacarpal, prox, dist] (its metacarpal's rest is its neutral). Absolute
# targets only work with the palm-measured curl axes: with the forearm-based ones a 90 degree straightening
# fanned the fingers sideways through each other.
POSES = {
    "open": {"finger": (6.0, 6.0, 4.0), "thumb": (0.0, -10.0, -8.0)},
    "fist": {"finger": (85.0, 95.0, 70.0), "thumb": (15.0, 35.0, 30.0)},
    "grab": {"finger": (45.0, 60.0, 45.0), "thumb": (8.0, 20.0, 16.0)},
    "point": {"finger": (85.0, 95.0, 70.0), "thumb": (15.0, 35.0, 30.0), "index": (6.0, 6.0, 4.0)},
    "splay": {"finger": (6.0, 6.0, 4.0), "thumb": (0.0, -6.0, -4.0), "spread": {"thumb": -6.0, "index": -4.0, "middle": -1.0, "ring": 1.0, "pinky": 4.0}},
}
# the idle's base: a hand hanging relaxed, a little curled
RELAXED = {"finger": (30.0, 35.0, 25.0), "thumb": (0.0, 0.0, 0.0)}
STATIC_FRAMES = 6
TWITCH_SECONDS = 2.0
TWITCH_DEGREES = 4.0


def log(*a):
    print("[hand]", *a, flush=True)


# --------------------------------------------------------------------------- pieces

class Piece:
    def __init__(self, index, vert_indices, world):
        self.index = index
        self.verts = vert_indices
        pts = np.array([[v.x, v.y, v.z] for v in world])
        self.centre = Vector(pts.mean(axis=0))
        cov = np.cov((pts - pts.mean(axis=0)).T)
        w, v = np.linalg.eigh(cov)
        order = np.argsort(w)[::-1]
        axis = Vector(v[:, order[0]])
        proj = (pts - pts.mean(axis=0)) @ np.array(axis)
        self.axis = axis
        self.length = float(proj.max() - proj.min())
        self.p0 = self.centre + axis * float(proj.min())
        self.p1 = self.centre + axis * float(proj.max())
        # width: the extent across the two minor axes
        w2 = (pts - pts.mean(axis=0)) @ np.array(Vector(v[:, order[1]]))
        w3 = (pts - pts.mean(axis=0)) @ np.array(Vector(v[:, order[2]]))
        self.width = float(max(w2.max() - w2.min(), w3.max() - w3.min()))
        self.pts = pts
        self.prox = None   # end nearest the wrist / the parent joint
        self.dist = None
        self.bone = None

    @property
    def chunky(self):
        return self.length < 1.6 * self.width

    def orient(self, towards):
        """Pick the end nearest `towards` as proximal. A chunky piece (a claw tip, a carpal) has no trustworthy
        principal axis: its joint end is the vertex nearest the parent and it points at its farthest vertex."""
        if self.chunky:
            d = ((self.pts - np.array(towards)) ** 2).sum(axis=1)
            self.prox = Vector(self.pts[int(d.argmin())])
            far = ((self.pts - np.array(self.prox)) ** 2).sum(axis=1)
            self.dist = Vector(self.pts[int(far.argmax())])
            return
        if (self.p0 - towards).length <= (self.p1 - towards).length:
            self.prox, self.dist = self.p0, self.p1
        else:
            self.prox, self.dist = self.p1, self.p0

    @property
    def direction(self):
        return (self.dist - self.prox).normalized()


def components(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    seen = [False] * len(bm.verts)
    out = []
    for v0 in bm.verts:
        if seen[v0.index]:
            continue
        comp, stack = [], [v0]
        seen[v0.index] = True
        while stack:
            v = stack.pop()
            comp.append(v.index)
            for e in v.link_edges:
                o = e.other_vert(v)
                if not seen[o.index]:
                    seen[o.index] = True
                    stack.append(o)
        out.append(sorted(comp))
    bm.free()
    # deterministic: by lowest vertex index
    out.sort(key=lambda c: c[0])
    return out


def classify(pieces):
    """Assign every piece a bone name. Returns (W, D, N, chains) where chains maps finger name -> [pieces]."""
    by_len = sorted(pieces, key=lambda p: -p.length)
    forearm = by_len[:2]
    rest = [p for p in pieces if p not in forearm]
    hand_centre = sum((p.centre for p in rest), Vector()) / len(rest)
    for p in forearm:
        p.orient(hand_centre)          # prox = elbow end (far from the hand), dist = wrist end... flip below
    # for the forearm "prox" should be the elbow; orient() put the end NEAREST the hand as prox, so swap
    for p in forearm:
        p.prox, p.dist = p.dist, p.prox
        p.bone = "forearm"
    W = (forearm[0].dist + forearm[1].dist) * 0.5
    E = (forearm[0].prox + forearm[1].prox) * 0.5
    D = (W - E).normalized()
    for p in rest:
        p.orient(W)
    # the carpals sit in a tight cluster at the wrist and the metacarpals start past a clear gap: split the
    # pieces sorted by centroid distance from W at the largest gap among the first ten
    by_dist = sorted(rest, key=lambda p: (p.centre - W).length)
    dist = [(p.centre - W).length for p in by_dist]
    split = max(range(5, 10), key=lambda i: dist[i] - dist[i - 1])
    carpal_set = by_dist[:split]
    log("  wrist cluster: %d pieces within %.2f m, next at %.2f m" % (split, dist[split - 1], dist[split]))
    beyond = [p for p in by_dist[split:] if p.length > 1.2 * p.width]
    beyond.sort(key=lambda p: (p.prox - W).length)
    metas = beyond[:5]
    if len(metas) < 5:
        raise SystemExit("only %d metacarpal candidates" % len(metas))
    for p in carpal_set:
        p.bone = "palm"
    free = [p for p in rest if p not in metas and p not in carpal_set]
    chains = {m: [m] for m in metas}
    for rnd in range(3):
        picks = []
        for m, chain in chains.items():
            if len(chain) < rnd + 1:
                continue
            tip = chain[-1]
            for cand in free:
                if cand.bone is not None:
                    continue
                d = min((cand.p0 - tip.dist).length, (cand.p1 - tip.dist).length)
                picks.append((d, m, cand))
        picks.sort(key=lambda t: t[0])
        taken = set()
        for d, m, cand in picks:
            if m in taken or cand.bone is not None or d > 1.2:
                continue
            tip = chains[m][-1]
            cand.orient(tip.dist)
            cand.bone = "chain"
            chains[m].append(cand)
            taken.add(m)
    carpals = carpal_set + [p for p in free if p.bone is None]
    lengths = sorted(len(c) - 1 for c in chains.values())
    if lengths != [2, 3, 3, 3, 3] or len(carpals) != 7:
        for m, c in chains.items():
            log("  chain from #%d: %s" % (m.index, [q.index for q in c[1:]]))
        raise SystemExit("classification failed: phalanx chain lengths %s, %d carpals" % (lengths, len(carpals)))
    thumb_meta = next(m for m, c in chains.items() if len(c) == 3)   # metacarpal + two phalanges
    others = [m for m in metas if m is not thumb_meta]
    lateral = (thumb_meta.centre - sum((m.centre for m in others), Vector()) / 4)
    lateral = (lateral - D * lateral.dot(D)).normalized()
    others.sort(key=lambda m: -m.centre.dot(lateral))    # nearest the thumb first
    named = {"thumb": chains[thumb_meta]}
    for name, m in zip(FINGERS, others):
        named[name] = chains[m]
    # the palm plane: the least-variance axis of the carpal + finger-metacarpal points (NOT anything to do with
    # the forearm: the wrists are bent); sign = the side the first knuckles bend toward (a knuckle cannot
    # hyperextend 40 degrees; the tips are no use because these claws curl past 180 degrees)
    metas4 = [named[n][0] for n in FINGERS]
    pts = np.concatenate([p.pts for p in carpal_set + metas4])
    w, v = np.linalg.eigh(np.cov((pts - pts.mean(axis=0)).T))
    N = Vector(v[:, int(np.argsort(w)[0])]).normalized()
    bend = 0.0
    for name in FINGERS:
        c = named[name]
        a = c[0].direction.cross(N)
        bend += c[0].direction.cross(c[1].direction).dot(a)
    if bend < 0:
        N = -N
    # the finger direction: the mean finger-metacarpal direction, in the palm plane
    F = sum((m.direction for m in metas4), Vector())
    F = (F - N * F.dot(N)).normalized()
    for p in carpals:
        p.bone = "palm"
    for name, chain in named.items():
        if name == "thumb":
            for k, p in enumerate(chain):
                p.bone = "thumb_%d" % (k + 1)
        else:
            chain[0].bone = "palm"
            for k, p in enumerate(chain[1:]):
                p.bone = "%s_%d" % (name, k + 1)
    if sorted(p.bone for p in pieces if p.bone not in ("palm", "forearm")) != sorted(finger_bone_names()):
        raise SystemExit("finger bones incomplete: %s" % sorted(p.bone for p in pieces))
    return W, E, D, F, N, named, carpals, forearm


# --------------------------------------------------------------------------- armature

def curl_axes(named, F, N):
    """One lateral axis per finger: metacarpal direction x palm normal. A positive rotation about it bends the
    finger toward the palm. Shared by every bone of the finger so the joints all curl the same way, whatever
    the sculpt's tips point at (a claw tip pointing at the palm has no usable roll from the normal alone)."""
    axes = {}
    for name, chain in named.items():
        a = chain[0].direction.cross(N).normalized()
        # each finger's own first knuckle says which way is palm-ward for it (the thumb's metacarpal can point
        # anywhere the sculptor liked, backwards along the forearm on the right hand)
        if chain[0].direction.cross(chain[1].direction).dot(a) < 0:
            a = -a
        axes[name] = a
    return axes


def build_armature(W, E, F, N, named, forearm, frame):
    """The armature object takes the mesh object's transform (`frame`: the FBX importer's 0.01 scale and 90 degree
    X rotation), and the bones are laid out in that local frame (cm, Y-up), exactly as an imported rig would be.
    Exported with apply_unit_scale the armature node then comes out at unit scale like the skeleton characters,
    instead of a 100x node the engine would have to fold into every bone."""
    arm = bpy.data.armatures.new(RIG_NAME)
    ob = bpy.data.objects.new(RIG_NAME, arm)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = frame.copy()
    inv = frame.inverted()
    rot = inv.to_3x3().normalized()
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    axes = curl_axes(named, F, N)

    def bone(name, head, tail, parent=None, x_axis=None):
        b = eb.new(name)
        b.head, b.tail = inv @ head, inv @ tail
        if (b.tail - b.head).length < 5.0:
            b.tail = b.head + (rot @ Vector((0, 0, 0.3))) * 100.0
        b.use_connect = False
        b.use_deform = True
        if parent is not None:
            b.parent = eb[parent]
        y = (b.tail - b.head).normalized()
        if x_axis is None:
            x_axis = F.cross(N).normalized()
        x_local = (rot @ x_axis).normalized()
        # local X = Y x Z; aligning Z to (X x Y) makes X the requested axis projected off the bone
        b.align_roll(x_local.cross(y))
        return b

    bone("root", Vector((0, 0, 0)), Vector((0, 0, 0.5)), None, Vector((1, 0, 0)))
    bone("forearm", E, W, "root")
    finger_tips = [named[n][0].dist for n in FINGERS]
    palm_tail = sum(finger_tips, Vector()) / 4
    bone("palm", W, palm_tail, "forearm")
    for name, chain in named.items():
        segs = chain if name == "thumb" else chain[1:]
        parent = "palm"
        for k, p in enumerate(segs):
            bn = "%s_%d" % (name, k + 1)
            bone(bn, p.prox, p.dist, parent, axes[name])
            parent = bn
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def joint_bends(arm, named, F, N):
    """Existing bend at every finger joint (degrees, positive = toward the palm) around the finger's curl axis,
    from straight: a finger's first joint against its metacarpal, the thumb's against the finger direction."""
    axes = curl_axes(named, F, N)
    bends = {}
    for name, chain in named.items():
        segs = chain if name == "thumb" else chain[1:]
        prev_dir = F if name == "thumb" else chain[0].direction
        a = axes[name]
        for k, p in enumerate(segs):
            bn = "%s_%d" % (name, k + 1)
            d = p.direction
            # measure in the plane perpendicular to the curl axis
            pp = (prev_dir - a * prev_dir.dot(a)).normalized()
            dd = (d - a * d.dot(a)).normalized()
            bends[bn] = math.degrees(math.atan2(pp.cross(dd).dot(a), pp.dot(dd)))
            prev_dir = d
    # the thumb's metacarpal sits where the sculptor put it; its rest IS its neutral, the poses add to it
    bends["thumb_1"] = 0.0
    return bends


# --------------------------------------------------------------------------- weights

def bind(mesh_obj, arm, pieces, comps):
    for vg in list(mesh_obj.vertex_groups):
        mesh_obj.vertex_groups.remove(vg)
    groups = {}
    for p, comp in zip(pieces, comps):
        vg = groups.get(p.bone)
        if vg is None:
            vg = groups[p.bone] = mesh_obj.vertex_groups.new(name=p.bone)
        vg.add(comp, 1.0, "REPLACE")
    # parent WITHOUT moving: the mesh keeps its world transform (the importer's 0.01 scale and 90 degree X, the same
    # matrix the armature was given), so it exports as an identity child of the rig node. With the identity parent
    # inverse the mesh's basis stacked on the armature's and the FBX carried a mesh node 100x too small, turned
    # 90 degrees against its own bones.
    mesh_obj.parent = arm
    mesh_obj.matrix_parent_inverse = arm.matrix_world.inverted()
    for m in list(mesh_obj.modifiers):
        mesh_obj.modifiers.remove(m)
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_vertex_groups = True


def match_outline(main, outline):
    """The outline is toonify_realms' exact copy: same vertex order. Verified; else matched by nearest vertex."""
    a = main.data.vertices
    b = outline.data.vertices
    if len(a) == len(b) and all((a[i].co - b[i].co).length < 1e-3 for i in range(0, len(a), max(1, len(a) // 50))):
        return list(range(len(a)))
    log("  outline vertex order differs: matching by position")
    pts = np.array([[v.co.x, v.co.y, v.co.z] for v in a])
    out = []
    for v in b:
        d = ((pts - np.array([v.co.x, v.co.y, v.co.z])) ** 2).sum(axis=1)
        out.append(int(d.argmin()))
    return out


# --------------------------------------------------------------------------- poses

def finger_bone_names():
    out = ["thumb_%d" % k for k in (1, 2, 3)]
    for f in FINGERS:
        out += ["%s_%d" % (f, k) for k in (1, 2, 3)]
    return out


def pose_angles(spec, bends, curl):
    """Per bone (x_degrees, z_degrees) for one static pose: the fingers' absolute bends less the sculpt's own
    (`bends`), the thumb's delta on the sculpt; `curl` scales the bends of the fist-like poses."""
    out = {}
    for f in ["thumb"] + FINGERS:
        target = spec.get(f, spec["thumb"] if f == "thumb" else spec["finger"])
        for k in (1, 2, 3):
            bn = "%s_%d" % (f, k)
            x = target[k - 1] * curl if f == "thumb" else target[k - 1] * curl - bends[bn]
            z = spec.get("spread", {}).get(f, 0.0) if k == 1 else 0.0
            out[bn] = (x, z)
    return out


def key_pose(arm, angles, frame):
    for bn, (x, z) in angles.items():
        pb = arm.pose.bones[bn]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(x), 0.0, math.radians(z))
        pb.keyframe_insert("rotation_euler", frame=frame)


def new_action(arm, name):
    if arm.animation_data is None:
        arm.animation_data_create()
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
    return act


def make_static_clip(arm, name, angles):
    new_action(arm, name)
    for f in (1, STATIC_FRAMES):
        key_pose(arm, angles, f)
    return 1, STATIC_FRAMES


def make_twitch_clip(arm, base):
    """The idle: a slow drift of a few degrees per finger on top of `base` (the relaxed, slightly curled hand)."""
    new_action(arm, "twitch")
    n = int(TWITCH_SECONDS * FPS)
    names = ["thumb"] + FINGERS
    for f in range(1, n + 1):
        t = (f - 1) / n
        angles = {}
        for i, fn in enumerate(names):
            phase = i * 1.3
            for k in (1, 2, 3):
                cycles = 1 if k == 1 else 2
                bn = "%s_%d" % (fn, k)
                bx, bz = base.get(bn, (0.0, 0.0))
                a = TWITCH_DEGREES * (0.6 + 0.2 * k) * math.sin(math.tau * cycles * t + phase + k * 0.7)
                angles[bn] = (bx + a, bz)
        key_pose(arm, angles, f)
    return 1, n


# --------------------------------------------------------------------------- the wrist split

def to_model(v):
    """Blender import frame (metres) -> engine model units: (x, y, z) = (-y, x, z) x 39.37 (see gen_boss_shapes.py)."""
    return [round(-v.y * 39.37, 3), round(v.x * 39.37, 3), round(v.z * 39.37, 3)]


def remove_verts(ob, remove):
    """Delete a set of vertex indices from `ob`'s mesh in place; returns the old->new index map of the survivors."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    doomed = [bm.verts[i] for i in sorted(remove)]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()
    remap = {}
    n = 0
    for i in range(len(ob.data.vertices) + len(remove)):
        if i in remove:
            continue
        remap[i] = n
        n += 1
    assert n == len(ob.data.vertices), (n, len(ob.data.vertices))
    return remap


def split_off(ob, keep, name, origin_local):
    """A new object with only the `keep` vertices of `ob`, its mesh re-centred so `origin_local` (in the mesh's
    local frame) becomes the origin; same object transform as `ob`."""
    me = ob.data.copy()
    me.name = name
    new = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(new)
    new.matrix_world = ob.matrix_world.copy()
    doomed = set(range(len(me.vertices))) - set(keep)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in sorted(doomed)], context="VERTS")
    bm.to_mesh(me)
    bm.free()
    for v in me.vertices:
        v.co -= origin_local
    return new


def export_static(objects, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, add_leaf_bones=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=False, bake_anim=False, path_mode="STRIP", use_custom_props=False)


def export_clip(arm, path, f0, f1):
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = f0, f1
    scene.render.fps = FPS
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype="NULL",
                             apply_unit_scale=True, bake_space_transform=False, path_mode="STRIP", use_custom_props=False)


# --------------------------------------------------------------------------- render

PALETTE = {
    "forearm": (0.55, 0.55, 0.6), "palm": (0.9, 0.9, 0.85),
    "thumb": (0.95, 0.4, 0.2), "index": (0.25, 0.75, 0.3), "middle": (0.2, 0.5, 0.95),
    "ring": (0.9, 0.8, 0.2), "pinky": (0.8, 0.3, 0.8),
}


def render_views(main, outline, arm, pieces, comps, out_dir, tag):
    """Colour-per-bone Workbench render: a separate object per piece (evaluated through the armature)."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = main.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [main.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    temp = []
    for p, comp in zip(pieces, comps):
        bm = bmesh.new()
        idx = {}
        for i in comp:
            idx[i] = bm.verts.new(pts[i])
        bm.verts.ensure_lookup_table()
        for poly in main.data.polygons:
            if poly.vertices[0] in idx:
                try:
                    bm.faces.new([idx[i] for i in poly.vertices])
                except ValueError:
                    pass
        m = bpy.data.meshes.new("tmp")
        bm.to_mesh(m)
        bm.free()
        o = bpy.data.objects.new("tmp_%d" % p.index, m)
        bpy.context.scene.collection.objects.link(o)
        key = p.bone.split("_")[0]
        base = PALETTE.get(key, (1, 0, 1))
        k = int(p.bone.split("_")[1]) if "_" in p.bone else 0
        shade = 1.0 - 0.22 * k
        o.color = (base[0] * shade, base[1] * shade, base[2] * shade, 1.0)
        temp.append(o)
    hidden = []
    for o in (main, outline, arm):
        if o is not None:
            hidden.append((o, o.hide_render))
            o.hide_render = True
    allc = [p.centre for p in pieces]
    centre = sum(allc, Vector()) / len(allc)
    lo = Vector((min(c.x for c in allc), min(c.y for c in allc), min(c.z for c in allc)))
    hi = Vector((max(c.x for c in allc), max(c.y for c in allc), max(c.z for c in allc)))
    span = max((hi - lo).length, 6.0)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = scene.render.resolution_y = 1000
    if scene.world is None:
        scene.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.get("render_cam")
    if cam is None:
        cd = bpy.data.cameras.new("render_cam")
        cd.type = "ORTHO"
        cam = bpy.data.objects.new("render_cam", cd)
        scene.collection.objects.link(cam)
    cam.data.ortho_scale = span * 1.2
    scene.camera = cam
    for name, dirv in (("top", Vector((0, 0, 1))), ("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0)))):
        cam.location = centre + dirv * (span * 3)
        cam.rotation_euler = (centre - cam.location).normalized().to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, "%s_%s.png" % (tag, name))
        bpy.ops.render.render(write_still=True)
    for o in temp:
        bpy.data.objects.remove(o, do_unlink=True)
    for o, h in hidden:
        o.hide_render = h


# --------------------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--hand", required=True, choices=["r", "l"])
    ap.add_argument("--render", default="", help="directory for the check renders")
    ap.add_argument("--curl", type=float, default=1.0, help="scale on the fist/grab/point curl targets")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args(argv)

    stem = "SM_Env_Hell_Bone_Hand_%s_01" % args.hand.upper()
    src = os.path.join(PROJ, "Assets", "DungeonRealms", stem + ".fbx")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    main_ob = next(o for o in meshes if not o.name.endswith("_Outline"))
    out_ob = next((o for o in meshes if o.name.endswith("_Outline")), None)
    log("%s: %d verts, outline %s" % (main_ob.name, len(main_ob.data.vertices), out_ob.name if out_ob else "none"))

    comps = components(main_ob.data)
    M = main_ob.matrix_world
    pieces = [Piece(i, comp, [M @ main_ob.data.vertices[j].co for j in comp]) for i, comp in enumerate(comps)]
    log("%d loose pieces" % len(pieces))
    W, E, D, F, N, named, carpals, forearm = classify(pieces)
    log("wrist %s  elbow %s  forearm D %s  fingers F %s  palm normal N %s  (wrist bend %.0f deg)" % (
        tuple(round(v, 2) for v in W), tuple(round(v, 2) for v in E), tuple(round(v, 2) for v in D),
        tuple(round(v, 2) for v in F), tuple(round(v, 2) for v in N), math.degrees(math.acos(max(-1.0, min(1.0, D.dot(F)))))))
    for name in ["thumb"] + FINGERS:
        log("  %-6s pieces %s" % (name, [p.index for p in named[name]]))
    log("  carpals %s  forearm %s" % ([p.index for p in carpals], [p.index for p in forearm]))

    arm = build_armature(W, E, F, N, named, forearm, main_ob.matrix_world)
    bends = joint_bends(arm, named, F, N)
    log("  existing bends: " + ", ".join("%s %.0f" % (k, v) for k, v in bends.items()))
    palm_pieces = [p for p in pieces if p.bone == "palm"]
    palm_centre = sum((p.centre for p in palm_pieces), Vector()) / len(palm_pieces)

    # the wrist split: the two forearm pieces become their own static model with the wrist as its origin, so the
    # engine can hang and swing it from the wrist (Code/Boss/BossArm.cs); the hand model keeps palm + fingers
    os.makedirs(args.out, exist_ok=True)
    fore_set = set(j for p in forearm for j in p.verts)
    w_local = main_ob.matrix_world.inverted() @ W
    fore_objs = [split_off(main_ob, fore_set, stem + "_Forearm", w_local)]
    if out_ob is not None:
        order = match_outline(main_ob, out_ob)
        fore_out_set = set(oi for oi, mi in enumerate(order) if mi in fore_set)
        fore_objs.append(split_off(out_ob, fore_out_set, stem + "_Forearm_Outline", w_local))
        remove_verts(out_ob, fore_out_set)
    fore_fbx = os.path.join(args.out, "hand_%s_forearm.fbx" % args.hand)
    export_static(fore_objs, fore_fbx)
    for o in fore_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    log("wrote", fore_fbx, "(%d forearm verts, origin at the wrist)" % len(fore_set))
    remap = remove_verts(main_ob, fore_set)
    keep = [i for i, p in enumerate(pieces) if p.bone != "forearm"]
    pieces = [pieces[i] for i in keep]
    comps = [[remap[j] for j in comps[i]] for i in keep]

    bind(main_ob, arm, pieces, comps)
    if out_ob is not None:
        order = match_outline(main_ob, out_ob)
        inv = {}
        for oi, mi in enumerate(order):
            inv.setdefault(mi, []).append(oi)
        out_comps = [[oi for mi in comp for oi in inv.get(mi, [])] for comp in comps]
        bind(out_ob, arm, pieces, out_comps)

    report = {
        "hand": args.hand, "source": stem, "wrist": list(W), "elbow": list(E), "direction": list(F), "palm_normal": list(N),
        "forearm_dir": list(D),
        "wrist_model": to_model(W), "elbow_model": to_model(E), "palm_model": to_model(palm_centre),
        "forearm_fbx": os.path.basename(fore_fbx),
        "pieces": {p.index: p.bone for p in pieces},
        "bends": bends,
        "bones": [b.name for b in arm.data.bones],
    }
    if args.render:
        os.makedirs(args.render, exist_ok=True)
        render_views(main_ob, out_ob, arm, pieces, comps, args.render, "hand_%s_rest" % args.hand)

    # the model: rest pose, mesh + outline + armature, no animation
    bpy.ops.object.select_all(action="DESELECT")
    model_fbx = os.path.join(args.out, "hand_%s.fbx" % args.hand)
    bpy.ops.export_scene.fbx(
        filepath=model_fbx, use_selection=False, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=False, bake_anim=False, path_mode="STRIP", use_custom_props=False)
    log("wrote", model_fbx, "bones", len(arm.data.bones))

    # clips
    clip_dir = os.path.join(args.out, "Animations", "hand_%s" % args.hand)
    clips = {}
    for pose in POSES:
        f0, f1 = make_static_clip(arm, pose, pose_angles(POSES[pose], bends, args.curl))
        path = os.path.join(clip_dir, pose + ".fbx")
        export_clip(arm, path, f0, f1)
        clips[pose] = {"frames": [f0, f1], "looping": False}
        if args.render:
            bpy.context.scene.frame_set(f1)
            render_views(main_ob, out_ob, arm, pieces, comps, args.render, "hand_%s_%s" % (args.hand, pose))
    f0, f1 = make_twitch_clip(arm, pose_angles(RELAXED, bends, args.curl))
    export_clip(arm, os.path.join(clip_dir, "twitch.fbx"), f0, f1)
    clips["twitch"] = {"frames": [f0, f1], "looping": True}
    report["clips"] = clips
    with open(os.path.join(args.out, "hand_%s_rig.json" % args.hand), "w") as f:
        json.dump(report, f, indent=1)
    log("wrote %d clips to %s" % (len(clips), clip_dir))


if __name__ == "__main__":
    main()
