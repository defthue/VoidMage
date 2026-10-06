"""
Strip the stray second skeleton out of the Magic attack clips that carry one (found 2026-09-17).

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/fix_magic_double_skeleton.py

Attack6begin, Attack6end and Attack15 hold TWO armatures: the real 87-bone Synty rig (root.001, what every other clip
has) plus a leftover copy from an old retarget (root.005/root.006, some with *_RET helper bones), so each file has two
`pelvis` bones. The s&box update of 2026-09-17 made ModelDoc refuse them ("Couldn't create animation stream pelvis",
"Node 'Attack6begin' resolve failure"), which fails human_base.vmdl and human_fp_arms.vmdl as a whole: the first-person
arms load as ERROR and the body T-poses. This keeps root.001 alone and re-exports it the way the other clips are laid out.

Originals go to tools/data/magic_double_skeleton/ first (never overwritten). Blender's FBX round trip moves keys one
frame later, so keys are shifted back before the export.
"""
import bpy, os, sys, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
CLIPS = os.path.join(ROOT, "Assets", "Animations", "Magic", "Attacks")
BACKUP = os.path.join(HERE, "data", "magic_double_skeleton")
NAMES = ["Attack6begin", "Attack6end", "Attack15"]
KEEP_BONES = 87


def shift_keys(action, by):
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points:
                        kp.co.x += by
                        kp.handle_left.x += by
                        kp.handle_right.x += by
                    fc.update()


def fix(name):
    src = os.path.join(CLIPS, name + ".fbx")
    backup = os.path.join(BACKUP, name + ".fbx")
    if not os.path.exists(backup):
        shutil.copyfile(src, backup)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=backup)
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    keep = [a for a in arms if len(a.data.bones) == KEEP_BONES and a.animation_data and a.animation_data.action]
    if len(keep) != 1:
        raise RuntimeError(f"{name}: expected exactly one {KEEP_BONES}-bone animated armature, found {[(a.name, len(a.data.bones)) for a in arms]}")
    keep = keep[0]
    for o in list(bpy.data.objects):
        if o is not keep:
            bpy.data.objects.remove(o, do_unlink=True)
    action = keep.animation_data.action
    f0, f1 = (int(round(x)) for x in action.frame_range)
    shift_keys(action, -1)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = f0 - 1, f1 - 1
    bpy.ops.object.select_all(action='DESELECT')
    keep.select_set(True)
    bpy.context.view_layer.objects.active = keep
    bpy.ops.export_scene.fbx(filepath=src, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    print(f"[magic fix] {name}: kept {keep.name} ({KEEP_BONES} bones), removed {len(arms) - 1} stray armature(s), frames {f0}-{f1} at {scene.render.fps} fps")


os.makedirs(BACKUP, exist_ok=True)
for n in NAMES:
    fix(n)
