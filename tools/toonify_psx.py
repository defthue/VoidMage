"""Toonify the PSX Horror-Fantasy Megapack monsters (tools/psx_kinds.py) for s&box: one rigged FBX per monster with
its outline shell, plus the manifest tools/out/psxhorror/chars.json that gen_psx_assets.py turns into vmats + vmdls.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/toonify_psx.py -- [--kind werewolf]

Per monster: import the pack FBX (custom normals kept: the low-poly PSX shading is the look), drop the T-pose action so
the bind pose exports, join the meshes if there are several, rename the mesh to the kind's node name and its one
material to `Psx<Mesh>` (gen_psx_assets.py writes that vmat over the GLB's texture), strip Mixamo's `mixamorig:`
prefix and any dot from bone names (s&box spells `a.b` as `a_b` and a colon is asking for the same), add an identity
`root` bone when the rig's only top bone is off the origin (Mixamo's Hips; AccuRig's RL_BoneRoot and the UE exports'
`root` already are roots), and build the outline shell as a SEPARATE smooth-shaded object `<Mesh>_Outline` (the
monster convention: these meshes are flat-shaded, and a hull that kept the hard normals would split at every edge),
carrying the body's vertex groups and armature modifier. Export = prep_skeleton_npc.py's rigged settings. Nothing is
scaled here: psx_kinds.py's `size` and the exported FBX's measured extent meet in the vmdl.

Rest-pose fixes (2026-09-15, found when the elkdemon got animations): the ten AccuRig UE exports ship a bind pose
Blender reads lying down while their T-pose stack and vertices stand (bind_pose_fix moves the rest onto the stack's
pose, a rigid -90 deg X turn); the three Mixamo exports stand facing +X with the origin at mid-height
(orient_and_ground turns any rig whose feet do not point -Y and drops it onto z = 0, bones and vertices). Both are
baked into the data, the object transforms stay as imported, and the pose is reset to the rest before the export
(reset_pose). The run refuses to export a monster that still faces elsewhere, floats, or whose pose differs from its
rest. The manifest records `forward`, `turned_deg`, `raised_m` and `bind_pose` per monster.
"""
import argparse, json, math, os, sys
from collections import defaultdict

import bpy
from mathutils import Matrix, Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import psx_kinds  # noqa: E402


def log(*a):
    print("[psx]", *a)


def object_mode():
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.mode_set(mode="OBJECT")


def select_only(*objs):
    object_mode()
    for o in bpy.data.objects:
        o.select_set(False)
    for o in objs:
        o.hide_viewport = False
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def material_named(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        if m.name != name:
            bpy.data.materials.remove(bpy.data.materials[name])
            m.name = name
    return m


def smooth_normals(me):
    """Area-weighted vertex normals keyed by POSITION, so vertices split at UV seams share one normal and the
    hull closes (toonify_synty.py's rule)."""
    acc = defaultdict(Vector)
    key = lambda co: (round(co.x, 5), round(co.y, 5), round(co.z, 5))
    for p in me.polygons:
        n = p.normal * p.area
        for vi in p.vertices:
            acc[key(me.vertices[vi].co)] += n
    out = []
    for v in me.vertices:
        n = acc[key(v.co)]
        out.append(n.normalized() if n.length > 1e-9 else Vector((0.0, 0.0, 1.0)))
    return out


def fix_bone_names(arm):
    renames = {}
    for b in arm.data.bones:
        n = b.name
        if n.startswith("mixamorig:"):
            n = n[len("mixamorig:"):]
        n = n.replace(":", "_").replace(".", "_")
        if n != b.name:
            renames[b.name] = n
    for old, new in renames.items():
        arm.data.bones[old].name = new   # Blender renames the meshes' vertex groups with it
    return renames


FOOT_PAIRS = [("LeftFoot", "LeftToeBase", "RightFoot", "RightToeBase"), ("foot_l", "ball_l", "foot_r", "ball_r"),
              ("CC_Base_L_Foot", "CC_Base_L_ToeBase", "CC_Base_R_Foot", "CC_Base_R_ToeBase")]
FACING_TOLERANCE_DEG = 20.0
GROUND_TOLERANCE_M = 0.01


def forward_from_feet(arm):
    """The rig's horizontal forward (world), from ankle -> toe on both feet. None when the rig has no known foot pair."""
    for fl, tl, fr, tr in FOOT_PAIRS:
        if all(n in arm.data.bones for n in (fl, tl, fr, tr)):
            d = Vector((0.0, 0.0, 0.0))
            for f, t in ((fl, tl), (fr, tr)):
                d += arm.matrix_world @ arm.data.bones[t].head_local - arm.matrix_world @ arm.data.bones[f].head_local
            d.z = 0.0
            return d.normalized() if d.length > 1e-6 else None
    return None


def transform_data(arm, meshes, M):
    """Move the rig by the WORLD matrix M inside its data (bone rests, vertices, custom normals), leaving every object
    transform as imported. Applying object transforms instead (identity objects, data in metres) makes Blender's FBX
    exporter write the mesh nodes with their own -90/x100 conversion under the armature's, a layout the importer reads
    lying down (and s&box has only ever seen the imported-transform layout from this pipeline)."""
    L = arm.matrix_world.inverted() @ M @ arm.matrix_world
    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    # a CONNECTED bone's head is its parent's tail: transforming parent and child both moved such heads twice (the
    # Mixamo feet came out turned 180 instead of 90), so the chains are unhooked for the move and hooked back after
    connected = [eb.name for eb in arm.data.edit_bones if eb.use_connect]
    for eb in arm.data.edit_bones:
        eb.use_connect = False
    for eb in arm.data.edit_bones:
        eb.transform(L, scale=False, roll=True)
    for n in connected:
        arm.data.edit_bones[n].use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        me = o.data
        Lm = o.matrix_world.inverted() @ M @ o.matrix_world
        R3 = Lm.to_3x3().normalized()
        normals = [R3 @ cn.vector for cn in me.corner_normals]
        me.transform(Lm)
        me.normals_split_custom_set(normals)
    bpy.context.view_layer.update()


def rigid_fit(P, Q):
    """Rotation + translation (no scale) taking the points P onto Q, least squares (Kabsch), and the RMS residual."""
    import numpy as np
    P = np.array([list(p) for p in P]); Q = np.array([list(q) for q in Q])
    pc, qc = P.mean(0), Q.mean(0)
    U, _, Vt = np.linalg.svd((P - pc).T @ (Q - qc))
    D = np.diag([1.0, 1.0, np.sign(np.linalg.det(Vt.T @ U.T))])
    Rm = Vt.T @ D @ U.T
    t = qc - Rm @ pc
    M = Matrix([[Rm[0, 0], Rm[0, 1], Rm[0, 2], t[0]], [Rm[1, 0], Rm[1, 1], Rm[1, 2], t[1]], [Rm[2, 0], Rm[2, 1], Rm[2, 2], t[2]], [0.0, 0.0, 0.0, 1.0]])
    return M, float(np.sqrt(((P @ Rm.T + t - Q) ** 2).sum(1).mean()))


PELVIS_NAMES = ("pelvis", "Hips", "CC_Base_Hip", "CC_Base_Pelvis")
HEAD_NAMES = ("head", "Head", "CC_Base_Head")
INSIDE_SLACK_M = 0.05


def find_bone(arm, names):
    for n in names:
        if n in arm.data.bones:
            return n
    raise RuntimeError("none of %s on the rig (%s)" % (names, [b.name for b in arm.data.bones][:8]))


def rest_bounds(meshes):
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


def inside(p, lo, hi, slack=INSIDE_SLACK_M):
    return all(lo[i] - slack <= p[i] <= hi[i] + slack for i in range(3))


def principal_rotation_near(M):
    """The +-90 / 180 degree rotation about a world axis nearest to M's rotation (within 10 deg), or None."""
    q = M.to_quaternion()
    best = None
    for axis in "XYZ":
        for deg in (90.0, -90.0, 180.0):
            R = Matrix.Rotation(math.radians(deg), 4, axis)
            d = math.degrees(q.rotation_difference(R.to_quaternion()).angle)
            if best is None or d < best[0]:
                best = (d, R, axis, deg)
    return best if best[0] <= 10.0 else None


def bind_pose_fix(arm, meshes):
    """The ten AccuRig UE exports ship a bind pose Blender reads LYING DOWN (the pelvis at y = -0.5, z = 0: the
    cluster matrices missed the Y-up conversion) while the T-pose stack's node transforms stand the same skeleton
    upright and the mesh vertices are upright too. Skinned as shipped, the stack pose deforms the body into garbage
    (the evaluated mesh of clown1 spans 1.7 x 1.0 x 0.7 m). Detected by the rest pelvis sitting OUTSIDE the rest
    mesh's bounds (a rest that agrees with its skin -- the CC ten, the Mixamo three -- is left alone). The fix moves
    the REST onto the stack's pose, bones only (the vertices already sit there): the fitted rest -> pose rotation
    (Kabsch over every bone head; the stack's hands differ, so the fit is only used to pick the axis) snapped to the
    exact principal-axis turn it approximates, no translation; refused unless the pelvis then sits inside the skin and
    the body stands. Returns (kind, angle_deg, residual_m)."""
    bpy.context.scene.frame_set(bpy.context.scene.frame_start)
    bpy.context.view_layer.update()
    lo, hi = rest_bounds(meshes)
    pelvis, head = find_bone(arm, PELVIS_NAMES), find_bone(arm, HEAD_NAMES)
    W = arm.matrix_world
    if inside(W @ arm.data.bones[pelvis].head_local, lo, hi):
        return "consistent", "", 0.0
    P = [W @ pb.bone.head_local for pb in arm.pose.bones]
    Q = [W @ pb.head for pb in arm.pose.bones]
    M, res = rigid_fit(P, Q)
    snap = principal_rotation_near(M)
    if snap is None:
        raise RuntimeError("rest pelvis outside the skin and rest -> stack pose is no principal turn (%.1f deg, rms %.1f cm)" % (
            math.degrees(M.to_quaternion().angle), res * 100.0))
    transform_data(arm, [], snap[1])
    p = W @ arm.data.bones[pelvis].head_local; h = W @ arm.data.bones[head].head_local
    up = (h - p).normalized()
    if not inside(p, lo, hi) or up.z < 0.5:
        raise RuntimeError("bind pose fix (%s %.0f deg) left the pelvis at %s, body axis %s" % (
            snap[2], snap[3], tuple(round(v, 3) for v in p), tuple(round(v, 2) for v in up)))
    return "rigid", "%s %+.0f deg" % (snap[2], snap[3]), res


def reset_pose(arm, meshes):
    """Drop the shipped T-pose stack and put every bone back on its rest: the FBX exporter writes the CURRENT pose as
    the bone node transforms, and a stale stack pose (kept on the pose bones after the action is removed) exported the
    first elkdemon with a 173x mesh node and the clowns with a rest that fought their skin."""
    for o in [arm] + list(meshes):
        if o.animation_data:
            o.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    arm.data.pose_position = "POSE"
    bpy.context.view_layer.update()


def orient_and_ground(arm, meshes):
    """The three Mixamo exports face +X with their origin at mid-height (feet at z = -0.49 m); every Synty character,
    and s&box's forward, is -Y in Blender with the feet on z = 0 (measured 2026-09-15 off the feet bones: ankle -> toe
    is forward). Yaw the rig so the feet point -Y and drop it onto z = 0, baked into the bone and vertex data
    (transform_data) so the animation FBXs exported off this rig later match. Returns (yaw_deg, dz_m)."""
    fwd = forward_from_feet(arm)
    yaw = 0.0
    if fwd is not None:
        yaw = math.atan2(-1.0, 0.0) - math.atan2(fwd.y, fwd.x)   # the Z rotation taking `fwd` onto -Y
        yaw = (yaw + math.pi) % (2.0 * math.pi) - math.pi
        if abs(yaw) < math.radians(FACING_TOLERANCE_DEG):
            yaw = 0.0
    zmin = min((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
    dz = -zmin if abs(zmin) > GROUND_TOLERANCE_M else 0.0
    if yaw == 0.0 and dz == 0.0:
        return 0.0, 0.0
    transform_data(arm, meshes, Matrix.Translation((0.0, 0.0, dz)) @ Matrix.Rotation(yaw, 4, "Z"))
    return math.degrees(yaw), dz


def ensure_root(arm):
    tops = [b for b in arm.data.bones if b.parent is None]
    if len(tops) == 1 and (arm.matrix_world @ tops[0].head_local).length < 1e-3:
        return tops[0].name, False
    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    root = eb.new("root")
    root.head = (0.0, 0.0, 0.0); root.tail = (0.0, 10.0, 0.0); root.roll = 0.0
    for b in eb:
        if b.parent is None and b.name != "root":
            b.parent = root
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return "root", True


def run(k):
    src = os.path.join(k["folder"], k["fbx"])
    dst = os.path.join(psx_kinds.OUT_DIR, k["out"])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=True)
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(arms) != 1 or not meshes:
        return dict(ok=False, error="expected one armature and meshes, got %d armatures, %d meshes" % (len(arms), len(meshes)))
    arm = arms[0]
    renames = fix_bone_names(arm)
    bind_kind, bind_angle, bind_res = bind_pose_fix(arm, meshes)   # needs the shipped stack's pose: before the actions go
    if bind_kind == "rigid":
        log(k["name"], "bind pose moved onto the stack's pose: %s, fit residual %.1f cm" % (bind_angle, bind_res * 100.0))
    reset_pose(arm, meshes)
    if len(meshes) > 1:
        select_only(*meshes)
        bpy.ops.object.join()
        meshes = [bpy.context.view_layer.objects.active]
        log(k["name"], "joined into one mesh")
    body = meshes[0]
    if not any(m.type == "ARMATURE" for m in body.modifiers):
        return dict(ok=False, error="%s carries no armature modifier" % body.name)
    if body.data.shape_keys:
        select_only(body); bpy.ops.object.shape_key_remove(all=True)

    yaw, dz = orient_and_ground(arm, meshes)   # before the root: it is added at the (now grounded) origin
    if yaw or dz:
        log(k["name"], "turned %.0f deg, raised %.2f m" % (yaw, dz))
    root_name, added = ensure_root(arm)
    body.name = k["mesh"]; body.data.name = k["mesh"]
    me = body.data
    mat = material_named(k["material"])
    if not body.material_slots:
        me.materials.append(mat)
    for i in range(len(body.material_slots)):
        body.material_slots[i].material = mat
    mat.use_backface_culling = True

    # the outline shell: a smooth-shaded copy sharing vertex groups + the armature modifier
    shell = body.copy(); shell.data = me.copy()
    shell.name = k["mesh"] + "_Outline"; shell.data.name = shell.name
    for coll in body.users_collection:
        coll.objects.link(shell)
    sme = shell.data
    for attr in ("sharp_edge", "sharp_face"):
        if attr in sme.attributes:
            sme.attributes.remove(sme.attributes[attr])
    for p in sme.polygons:
        p.use_smooth = True
    vn = smooth_normals(sme)
    sme.normals_split_custom_set([vn[sme.loops[i].vertex_index] for i in range(len(sme.loops))])
    sme.materials.clear(); sme.materials.append(material_named(psx_kinds.OUTLINE_MATERIAL))
    for p in sme.polygons:
        p.material_index = 0

    pts = [body.matrix_world @ v.co for v in me.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
    # what leaves here must stand: feet on the ground, facing -Y, the skin undeformed at the pose being exported
    fwd = forward_from_feet(arm)
    if fwd is not None and fwd.angle(Vector((0.0, -1.0, 0.0))) > math.radians(FACING_TOLERANCE_DEG):
        return dict(ok=False, error="still faces %s after the fixes" % tuple(round(v, 2) for v in fwd))
    if abs(lo[2]) > GROUND_TOLERANCE_M:
        return dict(ok=False, error="feet at z = %.3f m after the fixes" % lo[2])
    worst = max(max(abs(v) for row in (pb.matrix - pb.bone.matrix_local) for v in row) for pb in arm.pose.bones)
    if worst > 1e-4:
        return dict(ok=False, error="pose differs from rest at export (%.4f)" % worst)
    p = arm.matrix_world @ arm.data.bones[find_bone(arm, PELVIS_NAMES)].head_local
    if not inside(p, lo, hi):
        return dict(ok=False, error="rest pelvis at %s sits outside the skin %s..%s" % (
            tuple(round(v, 3) for v in p), [round(v, 2) for v in lo], [round(v, 2) for v in hi]))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    select_only(arm, body, shell)
    bpy.ops.export_scene.fbx(
        filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP", use_custom_props=False)
    bones = [b.name for b in arm.data.bones]
    rec = dict(ok=True, mode="auto", slot=k["material"],
               parts=[dict(name=k["mesh"], outline=shell.name, polys=len(me.polygons), slots=[k["material"]], outline_polys=len(sme.polygons))],
               armature=arm.name, bones=bones, root_bone=root_name, root_added=added, renamed_bones=len(renames),
               armature_scale=[round(s, 4) for s in arm.scale], turned_deg=round(yaw, 1), raised_m=round(dz, 3),
               forward=[round(v, 2) for v in (fwd or Vector((0.0, 0.0, 0.0)))],
               bind_pose=dict(kind=bind_kind, turn=bind_angle, fit_residual_cm=round(bind_res * 100.0, 1)),
               rest_min_m=[round(v, 4) for v in lo], rest_max_m=[round(v, 4) for v in hi],
               out=k["out"], src=os.path.relpath(src, psx_kinds.SRC), size=k["size"])
    log("%-22s %3d bones (root %s%s, %d renamed) %5d polys  %.2f x %.2f x %.2f m -> %s" % (
        k["name"], len(bones), root_name, " added" if added else "", len(renames), len(me.polygons),
        hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], os.path.basename(dst)))
    return rec


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    psx_kinds.add_kind_arg(ap)
    args = ap.parse_args(argv)
    os.makedirs(os.path.dirname(psx_kinds.MANIFEST), exist_ok=True)
    manifest = {}
    if os.path.exists(psx_kinds.MANIFEST):
        manifest = json.load(open(psx_kinds.MANIFEST, encoding="utf-8"))
    for name in psx_kinds.selected(args.kind):
        k = psx_kinds.KINDS[name]
        try:
            rec = run(k)
        except Exception as e:  # noqa: BLE001
            rec = dict(ok=False, error=repr(e)[:300], out=k["out"])
        if not rec.get("ok"):
            log("FAIL", name, rec.get("error"))
        manifest[name] = rec
        with open(psx_kinds.MANIFEST, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=1)
    ok = sum(1 for r in manifest.values() if r.get("ok"))
    log("manifest", psx_kinds.MANIFEST, "ok", ok, "of", len(manifest))


if __name__ == "__main__":
    main()
