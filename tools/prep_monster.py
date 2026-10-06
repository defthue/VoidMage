"""Toonify one Quaternius monster (tools/monster_kinds.py) out of its .blend and export it for s&box:
the character FBX with the outline shell, one FBX per animation, and a JSON manifest of what was found.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/prep_monster.py -- --kind bat
    ... --kind all

What it fixes on the way through (all of it measured in tools/out/monsters/<kind>.json):

* The eyes of the Bat and the Dragon ship as their own mesh on a SEPARATE one-bone armature that no clip
  animates. Left alone they hang in mid-air while the body flies, so they are joined into the body mesh and
  skinned 100 % to the head bone the kind names (`eyes` in monster_kinds.py).
* The Easy pack meshes carry no UV layer at all; the surface materials are flat colours on a white texture,
  so an empty UV map is added rather than an unwrap.
* Material slots are renamed to the canonical `Monster<Kind><Slot>` names that the vmdl remaps onto
  Assets/Shaders/*.vmat (gen_monster_assets.py writes those vmats from the colours logged here).
* The outline shell is a SEPARATE object (`<Kind>_Outline`), not extra faces in the body mesh the way the
  Synty characters do it: these meshes are flat shaded, and heden_outline.shader inflates along the vertex
  normal, so a shell that kept the pack's hard normals would split open at every edge. The duplicate is
  shade-smoothed (sharp edges, sharp faces and custom split normals all cleared) and keeps the body's
  vertex groups and armature modifier, so it deforms with it.

Nothing here is scaled: monster_kinds.py's `size` and the measured rest bounds meet in the vmdl, so
resizing a monster never needs Blender again.
"""
import bpy, sys, os, json, math, argparse
from mathutils import Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import monster_kinds  # noqa: E402


def log(*a):
    print("[monster]", *a)


def object_mode():
    """Force every object out of pose/edit mode. `mode` is PER OBJECT: several of these files were saved with
    their armature in pose mode, and the FBX exporter puts it back there after each export, which fails the poll
    of every object operator (select_all, duplicate, join) on the next call."""
    # bpy.data.objects, not view_layer.objects: the latter hands out None entries right after a join or a
    # remove, before the view layer is rebuilt
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.mode_set(mode="OBJECT")


def select_only(*objs):
    object_mode()
    for o in bpy.data.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def base_color(mat):
    """The flat colour the pack authored, in LINEAR space.

    Follow the surface link out of the ACTIVE Material Output: every one of these materials also carries a
    stale, disconnected `Diffuse BSDF` left over from the pack's authoring, and it holds a different colour
    (the bat's belly is 0.80/0.53/0.09 live and 0.74/0.63/0.32 stale). Picking the first BSDF node in the
    tree silently imports the wrong palette."""
    tree = mat.node_tree
    if tree:
        outputs = [n for n in tree.nodes if n.type == "OUTPUT_MATERIAL"]
        # active output first, then any other: `get_output_node("ALL")` is no help — the bat's ALL-target
        # output is the stale one, and the frog has no ALL-target output at all
        outputs.sort(key=lambda n: not n.is_active_output)
        surfaces = [l.from_node for n in outputs for l in n.inputs["Surface"].links]
        for socket in ("Base Color", "Color"):
            for node in surfaces:
                if socket in node.inputs:
                    return [round(float(c), 6) for c in node.inputs[socket].default_value[:3]]
    return [round(float(c), 6) for c in mat.diffuse_color[:3]]


def action_slot(ad, action):
    """Blender 4.4+/5.0 slotted actions: a freshly assigned action needs its slot picked too."""
    ad.action = action
    if hasattr(ad, "action_slot") and ad.action_slot is None and getattr(action, "slots", None):
        ad.action_slot = action.slots[0]


def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def join_eyes(body, arm, kind):
    """Fold the never-animated eye mesh into the body, skinned to one head bone."""
    eyes_name, eye_arm_name, bone = kind["eyes"]
    eyes = bpy.data.objects.get(eyes_name)
    if eyes is None:
        raise RuntimeError("no eye mesh %r in %s" % (eyes_name, kind["blend"]))
    if bone not in arm.data.bones:
        raise RuntimeError("eye bone %r not on %s (have %s)" % (bone, arm.name, [b.name for b in arm.data.bones]))
    eye_lo, eye_hi = world_bounds(eyes)
    centre = (eye_lo + eye_hi) * 0.5
    head = arm.matrix_world @ arm.data.bones[bone].head_local
    # the pack's own weights point at a foreign rig (the bat's eyes carry ear groups): drop them all
    for g in list(eyes.vertex_groups):
        eyes.vertex_groups.remove(g)
    for m in list(eyes.modifiers):
        eyes.modifiers.remove(m)
    vg = eyes.vertex_groups.new(name=bone)
    vg.add(list(range(len(eyes.data.vertices))), 1.0, "REPLACE")
    log("  eyes %s -> bone %s (eye centre %s, bone head %s, %.2f apart)" % (
        eyes_name, bone, tuple(round(v, 2) for v in centre), tuple(round(v, 2) for v in head), (head - centre).length))
    select_only(body, eyes)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    eye_arm = bpy.data.objects.get(eye_arm_name)
    if eye_arm is not None:
        bpy.data.objects.remove(eye_arm, do_unlink=True)


def rename_materials(body, kind):
    """Pack slot name -> Monster<Kind><Slot>, the name the vmdl remaps onto Assets/Shaders/<that>.vmat."""
    colors = {}
    for slot in body.material_slots:
        m = slot.material
        if m is None:
            raise RuntimeError("%s has an empty material slot" % body.name)
        pack_name = m.name
        # `.001` suffixes and the like would end up in a filename: strip them, then let the kind rename
        canonical = kind["mat_prefix"] + kind["materials"].get(pack_name, pack_name.split(".")[0])
        m.name = canonical
        colors[canonical] = dict(pack_slot=pack_name, linear=base_color(m))
    return colors


def build_outline(body, kind):
    """A smooth-shaded duplicate of the body: heden_outline.shader inflates it along the vertex normal."""
    select_only(body)
    bpy.ops.object.duplicate()
    shell = bpy.context.view_layer.objects.active
    shell.name = kind["outline_mesh"]
    shell.data.name = shell.name
    me = shell.data
    me.materials.clear()
    me.materials.append(bpy.data.materials.new(monster_kinds.OUTLINE_MATERIAL))
    for p in me.polygons:
        p.material_index = 0
    # smooth the shell: sharp faces, sharp edges and custom split normals would each tear it open at a corner
    select_only(shell)
    if getattr(me, "has_custom_normals", False):
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    for name in ("sharp_face", "sharp_edge"):
        if name in me.attributes:
            me.attributes.remove(me.attributes[name])
    bpy.ops.object.shade_smooth()
    return shell


def export_fbx(path, objects, bake_anim):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(*objects)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True,
        object_types={"ARMATURE", "MESH"} if not bake_anim else {"ARMATURE"},
        add_leaf_bones=False, armature_nodetype="NULL", use_armature_deform_only=False,
        apply_unit_scale=True, bake_space_transform=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, path_mode="STRIP", use_custom_props=False,
        bake_anim=bake_anim, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)


def clip_name(action_name):
    """`Bat_Attack2` -> `attack2`, `Frog_Idle` -> `idle`; the pack prefixes every action with the monster."""
    return (action_name.split("_", 1)[1] if "_" in action_name else action_name).lower()


def run(name):
    kind = monster_kinds.kind(name)
    log("===", name, "from", kind["blend"])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.open_mainfile(filepath=kind["blend"])
    object_mode()
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base

    arm = bpy.data.objects.get(kind["armature"])
    body = bpy.data.objects.get(kind["body"])
    if arm is None or arm.type != "ARMATURE":
        raise RuntimeError("no armature %r in %s" % (kind["armature"], kind["blend"]))
    if body is None or body.type != "MESH":
        raise RuntimeError("no mesh %r in %s" % (kind["body"], kind["blend"]))

    if kind["eyes"]:
        join_eyes(body, arm, kind)
    if not body.data.uv_layers:
        body.data.uv_layers.new(name="UVMap")     # the flat colours ride a white texture; nothing to unwrap
        log("  added an empty UV map (the pack has none)")

    body.name = kind["body_mesh"]
    body.data.name = body.name
    colors = rename_materials(body, kind)
    shell = build_outline(body, kind)

    # Turn a rig that was authored facing the wrong way (the wasp's head points +X where every other rig in
    # both packs points -Y, so it flew sideways). This stays an OBJECT rotation and is deliberately NOT
    # applied: the bones' local matrices are untouched, so every clip exported below is unaffected, and the
    # FBX carries the rotation on the armature and mesh nodes so the whole model turns together.
    if kind["yaw"]:
        arm.rotation_euler.z += math.radians(kind["yaw"])
        bpy.context.view_layer.update()
        log("  turned %+.0f degrees about Z (this rig faces the wrong way)" % kind["yaw"])

    # rest pose for the character export: an action left assigned would leak its frame into the bind pose
    if arm.animation_data:
        arm.animation_data.action = None
    scene.frame_set(scene.frame_start)
    bpy.context.view_layer.update()

    for o in list(bpy.data.objects):
        if o not in (arm, body, shell):
            bpy.data.objects.remove(o, do_unlink=True)

    lo, hi = world_bounds(body)
    size = hi - lo
    log("  rest bounds %s -> %s (size %s, max %.3f) armature scale %s" % (
        tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi),
        tuple(round(v, 3) for v in size), max(size), tuple(round(v, 3) for v in arm.scale)))
    log("  materials", {k: v["linear"] for k, v in colors.items()})

    export_fbx(kind["fbx"], [arm, body, shell], bake_anim=False)
    log("  wrote", kind["fbx"], "bones", len(arm.data.bones), "polys", len(body.data.polygons))

    clips = []
    ad = arm.animation_data or arm.animation_data_create()
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        action_slot(ad, action)
        f0, f1 = int(round(action.frame_range[0])), int(round(action.frame_range[1]))
        scene.frame_start, scene.frame_end = f0, f1
        out = os.path.join(kind["clip_dir"], clip_name(action.name) + ".fbx")
        export_fbx(out, [arm], bake_anim=True)
        clips.append(dict(name=clip_name(action.name), action=action.name, start=f0, end=f1,
                          seconds=round((f1 - f0) / fps, 3)))
        log("    clip %-10s frames %d-%d (%.2f s) -> %s" % (clips[-1]["name"], f0, f1, clips[-1]["seconds"], out))
    ad.action = None

    manifest = dict(
        kind=name, title=kind["title"], blend=kind["blend"], fps=fps,
        armature=arm.name, root_bone=next(b.name for b in arm.data.bones if b.parent is None),
        bones=[b.name for b in arm.data.bones],
        body_mesh=body.name, outline_mesh=shell.name,
        materials=colors, mat_prefix=kind["mat_prefix"],
        rest_min=[round(v, 4) for v in lo], rest_max=[round(v, 4) for v in hi],
        rest_size=[round(v, 4) for v in size], rest_max_dim=round(max(size), 4),
        clips=clips)
    os.makedirs(os.path.dirname(kind["manifest"]), exist_ok=True)
    with open(kind["manifest"], "w", newline="\n") as f:
        json.dump(manifest, f, indent=2)
    log("  wrote", kind["manifest"])


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    monster_kinds.add_kind_arg(ap)
    args = ap.parse_args(argv)
    for name in monster_kinds.selected(args.kind):
        run(name)


if __name__ == "__main__":
    main()
