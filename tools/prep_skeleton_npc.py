"""Build one skeleton kind's FBX (tools/skeleton_kinds.py: Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx,
.../SkeletonSoldier/skeleton_soldier_01.fbx, ...): Synty's Dungeon skeleton character on its Unreal-named rig, with
weapon sockets, the kind's swords and shields skinned to those sockets (one body-group choice each in the vmdl),
and the toon outline shell, ready for the retargeted Sword & Shield pack (tools/retarget_fpp.py --sockets ...).

Why this exists: the raw Synty FBX has no weapon sockets and no outline, and the pack's clips animate
Manny's `weapon_r` / `shield_l` sockets. Sockets are placed anatomically: the skeleton rig is first posed
into Manny's rest pose (retarget_fpp.prepose), Manny's socket offset from its parent joint is applied in
that pose, and the result is mapped back to the skeleton's own rest frame. The retarget copies only the
socket's world ROTATION from Manny, so the socket location stays glued to the skeleton's hand.

The sword/shield grip is derived from the fist: blade along the knuckle line toward the thumb, flat
facing the palm, grip point on the socket; shield face away from the palm, centred on its socket.
--sword-pos/--sword-rot and --shield-pos/--shield-rot (cm, degrees XYZ, in socket space) tweak on top.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/prep_skeleton_npc.py -- --kind soldier_01 [--sword-rot 0,0,0 ...]

A kind with `rig_fbx` + `bone_map` (the Dungeon Realms skeletons) is RE-SKINNED first: its own armature is Synty's
classic rig, whose joints sit exactly where the Epic rig's do but whose bones are named and oriented differently,
so the body's vertex groups are renamed onto the donor Epic rig (fingertips, jaw and toes merged into their
parents), the body is re-parented to the donor, and the classic armature is thrown away. From there on it is the
slave's rig and every retargeted clip, the animgraph and the ragdoll table apply unchanged.
"""
import bpy, bmesh, sys, os, math, argparse, re
from mathutils import Vector, Matrix, Euler

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
from retarget_fpp import import_armature, prepose, select_only, whead  # noqa: E402
import skeleton_kinds  # noqa: E402

RIG_NAME = "SkeletonRig"
BASE_MATERIAL = "DungeonShader"
OUTLINE_MATERIAL = "DungeonOutline"
SHIELD_MATERIAL = "DungeonShaderShield"
# The weapons were split in Blender by the user into handle / blade / fittings slots (the shields into wood / iron);
# the FBX material names are mapped onto canonical slot names that gen_skeleton_npc_vmdl.py remaps to the vmats.
# Swords: BladeShader and WoodHandleShader are the players' sword materials, IronShader the skeletons' darker iron
# where the players' sword has brass, BrassShader the players' brass (the curved greatsword's trim).
# Shields: their own identities (ShieldIronShader / ShieldWoodShader / DungeonShaderShield), because a blocked hit
# flashes exactly the shield's material slots blue (SkeletonBody.FlashMaterials) and must not light the sword.
SWORD_MATERIALS = {"WoodhandleShader": "WoodHandleShader", "SwordIronShader": "BladeShader", "BladeShader": "BladeShader",
                   "IronCrossguardPommelshader": "IronShader", "Brassshader": "BrassShader", "BrassShader": "BrassShader",
                   "DungeonShader": BASE_MATERIAL,
                   # unsplit FantasyKingdom / Dungeon Realms props (the Realms skeletons' swords 03/04, the Undead
                   # Knight's Sword_Medium_08): their one atlas slot as-is
                   "FantasyKingdomShader": "FantasyKingdomShader", "RealmsShader": "RealmsShader",
                   "DarkFantasyShader": "DarkFantasyShader",   # the Dark Fantasy weapons (SpiritDemon / Dark Lord's Sword_01)
                   "SwordOutline": OUTLINE_MATERIAL, "DaggerOutline": OUTLINE_MATERIAL, "FantasyKingdomOutline": OUTLINE_MATERIAL}
SHIELD_MATERIALS = {"ShieldIronShader": "ShieldIronShader", "ShieldWoodShader": "ShieldWoodShader",
                    "IronCrossguardPommelshader": "ShieldIronShader", "DungeonShader": SHIELD_MATERIAL,
                    # a Realms-atlas shield (the knight's Shield_07) gets its own identity for the block flash, as
                    # DungeonShader -> DungeonShaderShield does; Assets/Shaders/RealmsShaderShield.vmat is its twin
                    "RealmsShader": "RealmsShaderShield",
                    "ShieldOutline": OUTLINE_MATERIAL, "FantasyKingdomOutline": OUTLINE_MATERIAL}
# character attachments (a helmet on the head bone): the body's own atlas family
ATTACH_MATERIALS = {"RealmsShader": "RealmsShader", "DungeonShader": BASE_MATERIAL, "DarkFantasyShader": "DarkFantasyShader",
                    "FantasyKingdomOutline": OUTLINE_MATERIAL}
SOCKETS = [("weapon_r", "hand_r"), ("weapon_l", "hand_l"), ("shield_l", "lowerarm_l")]

# Synty's classic character rig (Dungeon Realms Chr_Skeleton_0N: 50 bones, `Hips`, `Shoulder_L`, `Finger_01`...)
# onto the Epic-named rig (43 bones). Measured 2026-09-11: every joint below sits at 0.00 cm from its Epic twin;
# the classic rig's extra leaf bones (Jaw, Toes_L/R, the 4th finger segments) carry 0-6 verts each and fold into
# their parents. The FBX names both hands' finger bones IDENTICALLY (Blender suffixes the second `.001`), so a
# finger's side comes from the first `_L`/`_R` ancestor, never from the suffix. The Dark Fortress characters
# (Characters.fbx) are the same rig with SIDED finger names (`Thumb_01_L`, `IndexFinger_04_R`): the suffix wins
# when it is there ("synty_classic_sided" is the same mapper; the key documents which pack a kind came from).
CLASSIC_UNSIDED = {"Root": "root", "Hips": "pelvis", "Spine_01": "spine_01", "Spine_02": "spine_02", "Spine_03": "spine_03",
                   "Neck": "neck_01", "Head": "head", "Eyes": "eyes", "Eyebrows": "eyebrows", "Jaw": "head"}
CLASSIC_SIDED = {"Clavicle": "clavicle", "Shoulder": "upperarm", "Elbow": "lowerarm", "Hand": "hand",
                 "UpperLeg": "thigh", "LowerLeg": "calf", "Ankle": "foot", "Ball": "ball", "Toes": "ball"}
CLASSIC_FINGERS = {"Thumb": "thumb", "IndexFinger": "index", "Finger": "middle"}   # Epic has 3 segments each


def classic_to_epic(bone):
    """Epic name for one classic bone (bpy.types.Bone); a bone that has no Epic twin merges into its parent's."""
    base = bone.name.split(".")[0]
    if base in CLASSIC_UNSIDED:
        return CLASSIC_UNSIDED[base]
    m = re.match(r"^(.+)_([LR])$", base)
    if m and m.group(1) in CLASSIC_SIDED:
        return CLASSIC_SIDED[m.group(1)] + "_" + m.group(2).lower()
    m = re.match(r"^(Thumb|IndexFinger|Finger)_0(\d)(?:_([LR]))?$", base)
    if m:
        side = m.group(3)
        if side is None:
            p = bone.parent
            while p is not None and not re.search(r"_[LR]$", p.name.split(".")[0]):
                p = p.parent
            if p is None:
                raise RuntimeError("no _L/_R ancestor for finger bone " + bone.name)
            side = p.name.split(".")[0][-1]
        return "%s_0%d_%s" % (CLASSIC_FINGERS[m.group(1)], min(int(m.group(2)), 3), side.lower())
    raise RuntimeError("no Epic name for classic bone %r; extend CLASSIC_* in prep_skeleton_npc.py" % bone.name)


def epic_identity(bone):
    """A character already on Epic names (the Dark Fantasy skeletons): the same name on the donor, `jaw` folded into
    `head`, the UE `ik_*` helpers dropped (None = no target; they carry no skin). Anything else unknown to the donor
    is caught by reskin()'s missing-bone check."""
    base = bone.name.split(".")[0]
    if base == "jaw":
        return "head"
    if base.startswith("ik_"):
        return None
    return base


BONE_MAPS = {"synty_classic": classic_to_epic, "synty_classic_sided": classic_to_epic, "epic": epic_identity}


def log(*a):
    print("[prep]", *a)


def reskin(body, src_arm, kind):
    """Move `body` from its own armature onto the kind's donor rig (kind["rig_fbx"]): vertex groups renamed through
    kind["bone_map"] (merged where several classic bones land on one Epic bone), body re-parented, old armature
    removed. Refuses if any mapped joint is not where the donor's is: the skin is bound in the donor's rest pose
    from then on, which is only right because the two rests coincide."""
    mapper = BONE_MAPS[kind["bone_map"]]
    names = {b.name: mapper(b) for b in src_arm.data.bones}
    dropped = sorted(n for n, t in names.items() if t is None)
    names = {n: t for n, t in names.items() if t is not None}
    if dropped:
        log("re-skin: dropping %d helper bones with no donor twin: %s" % (len(dropped), dropped))
    # per-bone tolerance (cm) for the joint check below: the DF skeletons' left thumb sits 2.6 / 4.4 cm from the
    # donor's (his is splayed round the broken sword's grip); a thumb segment pivoting a few cm off is invisible
    slack = kind.get("reskin_slack", {})
    donor, donor_objs = import_armature(kind["rig_fbx"])
    for o in donor_objs:
        if o is not donor:
            # the donor's own body (and its material) must not linger as orphan data in the export
            data = o.data if o.type == "MESH" else None
            bpy.data.objects.remove(o, do_unlink=True)
            if data is not None and data.users == 0:
                bpy.data.meshes.remove(data)
    missing = sorted(set(names.values()) - set(donor.data.bones.keys()))
    if missing:
        raise RuntimeError("donor rig %s lacks %s" % (kind["rig_fbx"], missing))
    worst = 0.0
    for src_name, dst_name in names.items():
        base = src_name.split(".")[0]
        a = (src_arm.matrix_world @ src_arm.data.bones[src_name].matrix_local).to_translation()
        b = (donor.matrix_world @ donor.data.bones[dst_name].matrix_local).to_translation()
        d = (a - b).length * 100.0
        merged_leaf = base in ("Jaw", "Toes_L", "Toes_R", "jaw") or base.startswith(("Finger_04", "IndexFinger_04"))
        if merged_leaf:
            continue
        limit = float(slack.get(base, 0.5))
        if d > limit:
            raise RuntimeError("joint %s sits %.2f cm from the donor's %s: not the same rest pose, re-skin refused" % (src_name, d, dst_name))
        if d > 0.5:
            log("re-skin: joint %s is %.2f cm from the donor's %s, allowed by reskin_slack (%.1f cm)" % (src_name, d, dst_name, limit))
        worst = max(worst, d)
    log("re-skin: %d source bones -> %d Epic bones on %s, worst 1:1 joint offset %.2f cm (merged leaves excluded)" % (
        len(names), len(set(names.values())), os.path.basename(kind["rig_fbx"]), worst))
    weights = {}
    for v in body.data.vertices:
        for g in v.groups:
            gname = body.vertex_groups[g.group].name
            if gname in dropped and g.weight > 0.0:
                raise RuntimeError("vertex group %r carries skin but its bone has no donor twin (extend the bone map)" % gname)
            if gname not in names:
                if gname in dropped:
                    continue
                raise RuntimeError("vertex group %r is not a bone of the source rig" % gname)
            tgt = weights.setdefault(names[gname], {})
            tgt[v.index] = tgt.get(v.index, 0.0) + g.weight
    for vg in list(body.vertex_groups):
        body.vertex_groups.remove(vg)
    for tgt, per_vert in weights.items():
        vg = body.vertex_groups.new(name=tgt)
        for vi, w in per_vert.items():
            vg.add([vi], w, "REPLACE")
    light = [v.index for v in body.data.vertices if sum(g.weight for g in v.groups) < 0.99]
    log("re-skin: %d vertex groups, %d verts under 0.99 total weight" % (len(weights), len(light)))
    M = body.matrix_world.copy()
    body.parent = donor
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_world = M
    for mod in body.modifiers:
        if mod.type == "ARMATURE":
            mod.object = donor
    bpy.data.objects.remove(src_arm, do_unlink=True)
    bpy.context.view_layer.update()
    return donor


def parse_vec(s, default=(0.0, 0.0, 0.0)):
    if not s:
        return Vector(default)
    parts = [float(x) for x in s.split(",")]
    if len(parts) != 3:
        raise ValueError("expected x,y,z: " + s)
    return Vector(parts)


def tweak_matrix(pos_cm, rot_deg):
    """Socket space is armature space, which is in cm here (cm mesh under a 0.01 object scale)."""
    return Matrix.Translation(pos_cm) @ Euler([math.radians(v) for v in rot_deg], "XYZ").to_matrix().to_4x4()


def bone_rest_world(o, name):
    return o.matrix_world @ o.data.bones[name].matrix_local


def bone_pose_world(o, name):
    return o.matrix_world @ o.pose.bones[name].matrix


def frame_from_axes(origin, x, y):
    """Right-handed 4x4 with the given X and (orthogonalised) Y axes."""
    x = x.normalized()
    y = (y - x * y.dot(x)).normalized()
    z = x.cross(y)
    m = Matrix.Identity(4)
    m.col[0][:3] = x; m.col[1][:3] = y; m.col[2][:3] = z; m.col[3][:3] = origin
    return m


def unify_material(obj, prefix, canonical):
    for i, slot in enumerate(obj.material_slots):
        if slot.material is not None and slot.material.name.split(".")[0] == prefix:
            obj.material_slots[i].material = canonical


def import_meshes(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]


def is_outline(obj):
    """The outline shell is the object whose every material is an outline material (the user's exports name the
    object `...01Outline`, `...02outline` or even `....001`, so the name is no guide)."""
    names = [s.material.name.split(".")[0].lower() for s in obj.material_slots if s.material is not None]
    return bool(names) and all(n.endswith("outline") for n in names)


def name_parts(parts, mesh_name):
    """One main mesh plus its outline shell, renamed <mesh_name> and <mesh_name>_Outline whatever the export
    called them (gen_skeleton_npc_vmdl.py lists these names)."""
    mains = [o for o in parts if not is_outline(o)]
    outlines = [o for o in parts if is_outline(o)]
    if len(mains) != 1 or len(outlines) > 1:
        raise RuntimeError("expected one mesh and at most one outline, got %s" % [(o.name, [s.material.name for s in o.material_slots]) for o in parts])
    main = mains[0]
    main.name = mesh_name; main.data.name = mesh_name
    for o in outlines:
        o.name = mesh_name + "_Outline"; o.data.name = o.name
    return main


def principal_axis(obj):
    """Longest axis of the mesh (PCA, mesh space) plus the extreme points along it and the flat normal."""
    import numpy as np
    pts = np.array([v.co[:] for v in obj.data.vertices])
    c = pts.mean(axis=0)
    w, vecs = np.linalg.eigh(np.cov((pts - c).T))
    v = Vector(vecs[:, 2].tolist()).normalized()      # largest spread = blade axis
    flat = Vector(vecs[:, 0].tolist()).normalized()   # smallest spread = flat normal
    proj = (pts - c) @ np.array(v[:])
    c = Vector(c.tolist())
    return c, v, float(proj.min()), float(proj.max()), flat


def toonify(mesh_obj, skip_slots=()):
    """Duplicate every face in place into a second material slot (the outline shell). Normals untouched:
    heden_outline.shader inflates along normals and culls front faces. Faces on a material in `skip_slots`
    (names) get no shell: a hull behind a translucent card is a black card (the SpiritDemon's ghost slot)."""
    me = mesh_obj.data
    skip = {i for i, m in enumerate(me.materials) if m is not None and m.name.split(".")[0] in skip_slots}
    outline = bpy.data.materials.get(OUTLINE_MATERIAL) or bpy.data.materials.new(OUTLINE_MATERIAL)
    me.materials.append(outline)
    slot = len(me.materials) - 1
    base_polys = len(me.polygons)
    bm = bmesh.new()
    bm.from_mesh(me)
    faces = [f for f in bm.faces if f.material_index not in skip]
    verts = {v for f in faces for v in f.verts}
    edges = {e for f in faces for e in f.edges}
    ret = bmesh.ops.duplicate(bm, geom=list(verts) + list(edges) + faces)
    for g in ret["geom"]:
        if isinstance(g, bmesh.types.BMFace):
            g.material_index = slot
    bm.to_mesh(me)
    bm.free()
    me.update()
    assert len(me.polygons) == base_polys + len(faces), (len(me.polygons), base_polys, len(faces))
    if skip:
        log("outline: %d of %d faces skipped (slots %s)" % (base_polys - len(faces), base_polys, sorted(skip_slots)))
    return base_polys


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--sword-pos", default="", help="cm x,y,z in weapon_r socket space")
    ap.add_argument("--sword-rot", default="", help="degrees x,y,z (XYZ Euler) in weapon_r socket space")
    ap.add_argument("--shield-pos", default="")
    ap.add_argument("--shield-rot", default="")
    ap.add_argument("--shield-spin", type=float, default=-90.0,
                    help="degrees to spin the shield about its plate normal (anticlockwise seen from the front)")
    ap.add_argument("--grip-from-pommel", type=float, default=0.13,
                    help="grip point as a fraction of the sword length from the pommel end")
    ap.add_argument("--no-weapons", action="store_true")
    ap.add_argument("--out", default="", help="output FBX (default: the kind's)")
    skeleton_kinds.add_kind_arg(ap)
    args = ap.parse_args(argv)
    kind = skeleton_kinds.kind(args.kind)
    out_fbx = args.out or kind["fbx"]
    log("kind", kind["name"], "->", out_fbx)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    dst, objs = import_armature(kind["char_fbx"])
    meshes = [o for o in objs if o.type == "MESH"]
    if len(meshes) > 1:
        # a pack that ships every character in one file (Dark Fortress Characters.fbx: 11 bodies + 7 attachments on
        # one armature): keep the kind's body, drop the rest and their mesh data so nothing of them reaches the export
        keep = [o for o in meshes if o.name.split(".")[0] == kind["body_mesh"]]
        if len(keep) != 1:
            raise RuntimeError("the character FBX has %d meshes and none/several named %r: %s" % (len(meshes), kind["body_mesh"], sorted(o.name for o in meshes)))
        for o in meshes:
            if o is keep[0]:
                continue
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if data.users == 0:
                bpy.data.meshes.remove(data)
        log("kept %s out of %d meshes in the character FBX" % (keep[0].name, len(meshes)))
        meshes = keep
    if len(meshes) != 1:
        raise RuntimeError("expected 1 mesh in the character FBX, got %d" % len(meshes))
    body = meshes[0]
    if kind.get("rig_fbx"):
        log("character on its own rig (%d bones, map %s): re-skinning onto the donor" % (len(dst.data.bones), kind["bone_map"]))
        dst = reskin(body, dst, kind)
    dst.name = RIG_NAME; dst.data.name = RIG_NAME
    if body.data.shape_keys:
        select_only(body); body.shape_key_clear()
    log("character bones", len(dst.data.bones), "mesh", body.name, "verts", len(body.data.vertices))

    # ---- sockets from Manny's rest pose, applied in the pre-posed skeleton
    src, src_objs = import_armature(skeleton_kinds.IDLE_CLIP)
    if src.animation_data:
        src.animation_data.action = None
    src.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    mapped = prepose(dst, src)
    log("pre-posed", len(mapped), "bones into Manny's rest")
    socket_rest = {}
    for sock, parent in SOCKETS:
        if sock not in src.data.bones:
            raise RuntimeError("Manny has no socket " + sock)
        Mw = src.matrix_world @ src.data.bones[sock].matrix_local     # Manny socket, rest, world
        Pm = src.matrix_world @ src.data.bones[src.data.bones[sock].parent.name].matrix_local
        Hp = bone_pose_world(dst, parent)                             # skeleton parent, pre-posed
        Hr = bone_rest_world(dst, parent)                             # skeleton parent, rest
        S_pre = Matrix.Translation(Hp.to_translation() + (Mw.to_translation() - Pm.to_translation())) @ Mw.to_3x3().to_4x4()
        socket_rest[sock] = (Hr @ Hp.inverted() @ S_pre, parent, S_pre)
        log("socket %-9s parent %-11s manny-offset %s cm" % (sock, parent, tuple(round(v * 100, 1) for v in (Mw.to_translation() - Pm.to_translation()))))

    # anatomy in the pre-posed pose (== Manny's rest), used for the default grip
    anatomy = {}
    for s in "lr":
        fingers = (whead(dst, "middle_01_" + s) - whead(dst, "hand_" + s)).normalized()
        knuckles = (whead(dst, "index_01_" + s) - whead(dst, "middle_01_" + s)).normalized()  # toward the thumb
        palm = fingers.cross(knuckles).normalized()
        to_body = (whead(dst, "pelvis") - whead(dst, "hand_" + s))
        if palm.dot(to_body) < 0:
            palm = -palm
        forearm = (whead(dst, "hand_" + s) - whead(dst, "lowerarm_" + s)).normalized()
        anatomy[s] = dict(fingers=fingers, knuckles=knuckles, palm=palm, forearm=forearm)

    # back to rest, forget Manny
    for pb in dst.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    for o in src_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()

    select_only(dst); bpy.ops.object.mode_set(mode="EDIT")
    for sock, (S_rest, parent, _) in socket_rest.items():
        eb = dst.data.edit_bones.new(sock)
        eb.head = Vector((0, 0, 0)); eb.tail = Vector((0, 0.05, 0))
        eb.matrix = dst.matrix_world.inverted() @ S_rest
        eb.parent = dst.data.edit_bones[parent]
        eb.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    log("bones now", len(dst.data.bones))

    # ---- materials: one shared block each
    # the body's one slot takes the kind's atlas name (DungeonShader, or RealmsShader for the Realms skins);
    # gen_skeleton_npc_vmdl.py remaps <name>.vmat -> shaders/<name>.vmat
    base_name = kind.get("base_material", BASE_MATERIAL)
    slot_map = kind.get("slot_materials")
    if slot_map:
        # a body with several slots (the SpiritDemon: Synty's translucent ghost slot + the atlas): the kind maps the
        # FBX material names (Maya junk, stable per file) onto the vmat names; every slot must be covered
        fbx_names = [m.name.split(".")[0] for m in body.data.materials]
        unmapped = [n for n in fbx_names if n not in slot_map]
        if unmapped or base_name not in slot_map.values():
            raise RuntimeError("body slots %s vs slot_materials %s (base_material %s must be one of them)" % (fbx_names, slot_map, base_name))
        for m in body.data.materials:
            m.name = slot_map[m.name.split(".")[0]]
            m.use_backface_culling = True
        base_mat = next(m for m in body.data.materials if m.name == base_name)
        log("body slots:", [m.name for m in body.data.materials])
    else:
        if len(body.data.materials) != 1:
            raise RuntimeError("body has %d material slots %s; give the kind a slot_materials map" % (
                len(body.data.materials), [m.name for m in body.data.materials]))
        base_mat = body.data.materials[0]
        base_mat.name = base_name
        base_mat.use_backface_culling = True
    outline_mat = bpy.data.materials.new(OUTLINE_MATERIAL)
    # the shield gets its own (identical) material so the game can flash that one slot blue on a block
    # (material overrides are per slot and matched by material identity; gen_skeleton_npc_vmdl.py remaps it)
    shield_mat = base_mat.copy()
    shield_mat.name = SHIELD_MATERIAL
    canon = {base_name: base_mat, OUTLINE_MATERIAL: outline_mat, SHIELD_MATERIAL: shield_mat}
    for canonical in set(SWORD_MATERIALS.values()) | set(SHIELD_MATERIALS.values()):
        if canonical not in canon:
            canon[canonical] = bpy.data.materials.new(canonical)

    # ---- weapons skinned to the sockets
    if not args.no_weapons:
        pre = {sock: S_pre for sock, (_, _, S_pre) in socket_rest.items()}
        rest = {sock: S_rest for sock, (S_rest, _, _) in socket_rest.items()}
        first_grip = {}
        jobs = [(w, "weapon_r", "r", args.sword_pos, args.sword_rot, "sword", SWORD_MATERIALS) for w in kind["swords"]] + \
               [(w, "shield_l", "l", args.shield_pos, args.shield_rot, "shield", SHIELD_MATERIALS) for w in kind["shields"]]
        for w, sock, side, pos, rot, kind_name, mat_map in jobs:
            fbx, scale = w["fbx"], w["scale"]
            parts = import_meshes(fbx)
            for o in parts:
                # geometry stays in FBX (Y-up) mesh space; only the authoring scale is folded in
                if scale != 1.0:
                    o.data.transform(Matrix.Scale(scale, 4))
                    o.data.update()
            main_part = name_parts(parts, w["mesh"])
            c, axis, lo, hi, flat = principal_axis(main_part)
            a = anatomy[side]
            reuse = w["grip"] == "first" and sock in first_grip
            if reuse:
                # a twin of the first weapon on this socket (same handle): its grip matrix verbatim; the log shows
                # both ends so a mismatch is visible
                log("%s %s length %.1f cm along %s, ends %s / %s (mesh cm): grip of the first %s" % (
                    kind_name, w["mesh"], hi - lo, tuple(round(v, 2) for v in axis),
                    tuple(round(v, 1) for v in (c + axis * lo)), tuple(round(v, 1) for v in (c + axis * hi)), kind_name))
                grip = first_grip[sock]
            elif kind_name == "sword":
                # which end is the tip: the thinner one (fewer verts near the extreme)
                proj = [(v.co - c).dot(axis) for v in main_part.data.vertices]
                near_hi = sum(1 for p in proj if p > hi - (hi - lo) * 0.1)
                near_lo = sum(1 for p in proj if p < lo + (hi - lo) * 0.1)
                tip_sign = 1.0 if near_hi < near_lo else -1.0
                blade_m = axis * tip_sign
                pommel_m = c + axis * (lo if tip_sign > 0 else hi)
                grip_m = pommel_m + blade_m * (hi - lo) * args.grip_from_pommel
                # world target: blade along the knuckle line toward the thumb, flat normal along the palm normal
                target = frame_from_axes(pre[sock].to_translation(), a["knuckles"], a["palm"])
                source = frame_from_axes(grip_m, blade_m, flat)
                log("sword %s length %.1f cm, tip %s, pommel %s, grip %s (mesh cm)" % (w["mesh"], (hi - lo), tuple(round(v, 1) for v in (c + axis * (hi if tip_sign > 0 else lo))), tuple(round(v, 1) for v in pommel_m), tuple(round(v, 1) for v in grip_m)))
            else:
                # shield: Synty authors every shield with the plate facing mesh +Z and the grip behind (chitin: the dome
                # on +Z; the heaters: a flat plate on +Z, straps and grip on -Z). The old "side with most vertices"
                # rule picked the strap side on the heaters and they faced the arm. `front` in skeleton_kinds.py
                # ("+z" default, "-z") overrides per shield; face the plate away from the palm, long axis along the forearm
                import numpy as np
                want = -1.0 if w.get("front", "+z") == "-z" else 1.0
                front_m = flat if flat.z * want > 0 else -flat
                pts = np.array([v.co[:] for v in main_part.data.vertices])
                depth = (pts - np.array(c[:])) @ np.array(front_m[:])
                log("shield %s plate normal %s (mesh), vertices ahead of the centre %.0f%%" % (
                    w["mesh"], tuple(round(v, 2) for v in front_m), 100.0 * float((depth > 0).mean())))
                up_m = axis
                target = frame_from_axes(pre[sock].to_translation(), -a["palm"], a["forearm"])
                # spin about the plate normal: positive = anticlockwise for a viewer looking at the plate
                spin = Matrix.Rotation(math.radians(args.shield_spin), 4, front_m)
                # the socket lands `seat` cm behind the mesh centre along the plate normal (skeleton_kinds.wep): a thick
                # shield with its centre on the arm buries its back half in the forearm
                seat = float(w.get("seat", 0.0))
                source = frame_from_axes(c - front_m * seat, front_m, (spin @ up_m.to_4d()).to_3d())
                log("shield %s %.0f cm along its long axis, plate faces mesh %s, seated %.0f cm out" % (w["mesh"], (hi - lo), tuple(round(v, 2) for v in front_m), seat))
            if not reuse:
                # mesh data is in cm (every object in this scene is a cm mesh under a 0.01 object scale), world is
                # metres: mesh -> grip frame (cm) -> metres -> world, in the pre-posed pose
                world_pre = target @ Matrix.Scale(0.01, 4) @ source.inverted()
                grip = pre[sock].inverted() @ world_pre           # mesh -> socket space (cm), constant
                grip = tweak_matrix(parse_vec(pos), parse_vec(rot)) @ grip
                first_grip.setdefault(sock, grip)
            for o in parts:
                o.parent = dst
                o.matrix_parent_inverse = Matrix.Identity(4)
                o.matrix_world = rest[sock] @ grip
                vg = o.vertex_groups.new(name=sock)
                vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
                mod = o.modifiers.new("Armature", "ARMATURE"); mod.object = dst
                for src_name, canonical in mat_map.items():
                    unify_material(o, src_name, canon[canonical])
                stray = [s.material.name for s in o.material_slots if s.material is not None and s.material not in canon.values()]
                if stray:
                    raise RuntimeError("%s has unmapped materials %s; add them to %s_MATERIALS" % (o.name, stray, kind_name.upper()))
            bpy.context.view_layer.update()
            log("  %s world scale %s, origin at %s m (hand_%s joint at %s m)" % (
                kind_name, tuple(round(v, 3) for v in parts[0].matrix_world.to_scale()),
                tuple(round(v, 3) for v in parts[0].matrix_world.to_translation()), side,
                tuple(round(v, 3) for v in whead(dst, "hand_" + side))))
            log("%s placed on %s: %s" % (kind_name, sock, [o.name for o in parts]))

    # ---- attachments (a helmet): authored at the bone's pivot in the character's own frame, so they sit at the joint
    # with the character's orientation and scale (0.01, Y-up), skinned 100 % to that bone; `offset` (cm, world axes
    # of the character: x right, y forward, z up) nudges the seat
    for att in kind.get("attachments", []):
        parts = import_meshes(att["fbx"])
        name_parts(parts, att["mesh"])
        offset = Vector(att.get("offset", (0.0, 0.0, 0.0))) * 0.01
        joint = bone_rest_world(dst, att["bone"]).to_translation() + offset
        for o in parts:
            o.parent = dst
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_world = Matrix.Translation(joint) @ dst.matrix_world.to_3x3().to_4x4()
            vg = o.vertex_groups.new(name=att["bone"])
            vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
            mod = o.modifiers.new("Armature", "ARMATURE"); mod.object = dst
            for src_name, canonical in ATTACH_MATERIALS.items():
                if canonical not in canon:
                    canon[canonical] = bpy.data.materials.new(canonical)
                unify_material(o, src_name, canon[canonical])
            stray = [s.material.name for s in o.material_slots if s.material is not None and s.material not in canon.values()]
            if stray:
                raise RuntimeError("%s has unmapped materials %s; add them to ATTACH_MATERIALS" % (o.name, stray))
        bpy.context.view_layer.update()
        log("attachment %s on %s at %s m (offset %s cm): %s" % (att["mesh"], att["bone"], tuple(round(v, 3) for v in joint),
                                                              tuple(att.get("offset", (0.0, 0.0, 0.0))), [o.name for o in parts]))

    # ---- outline shell on the character
    base_polys = toonify(body, skip_slots=kind.get("no_outline", ()))
    body.data.materials[len(body.data.materials) - 1] = outline_mat
    body.name = kind["body_mesh"]; body.data.name = body.name
    log("toonified", base_polys, "polys ->", len(body.data.polygons))

    # ---- export
    for m in list(bpy.data.materials):
        if m.users == 0:
            bpy.data.materials.remove(m)
    for o in bpy.data.objects:
        if o.type == "MESH":
            log("  mesh %-32s dims %s cm  materials %s" % (o.name, tuple(round(v * 100, 1) for v in o.dimensions), [s.material.name for s in o.material_slots]))
    os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx, use_selection=False, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP", use_custom_props=False)
    log("wrote", out_fbx, "bones", len(dst.data.bones), "meshes", [o.name for o in bpy.data.objects if o.type == "MESH"],
        "materials", [m.name for m in bpy.data.materials])


if __name__ == "__main__":
    main()
