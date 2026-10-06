"""Damp or remove a bone's animation in a retargeted clip FBX (Assets/Animations/...), in place.

Why: the Sword & Shield pack drives Manny's neck_02 + head hard in some attacks (attack_b_03_c); the retarget copies
the head's world rotation, which on this rig's single neck reads as a broken, tilted head. Blending the head's local
rotation toward rest (--blend 0 = fully at rest: the head simply follows the neck) fixes the clip for good.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup --python tools/fix_clip_bones.py -- ^
        --bones head --blend 0.0 Assets/Animations/SkeletonSS/attack_b_03_c.fbx [...]
"""
import argparse, os, sys

import bpy
from mathutils import Quaternion


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--bones", default="head", help="comma separated pose bone names")
    ap.add_argument("--blend", type=float, default=0.0, help="0 = rest, 1 = untouched; the local rotation is slerped toward rest")
    ap.add_argument("clips", nargs="+")
    args = ap.parse_args(argv)
    bones = [b.strip() for b in args.bones.split(",") if b.strip()]

    for path in args.clips:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=path)
        arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
        sc = bpy.context.scene
        act = arm.animation_data.action if arm.animation_data else None
        if act is None:
            raise SystemExit("no action in " + path)
        # the clip's own range (the scene keeps Blender's default 1-250 after an import)
        f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
        sc.frame_start, sc.frame_end = f0, f1
        # sample every frame, then rewrite the keys: robust against euler/quaternion modes and fcurve layouts
        for name in bones:
            pb = arm.pose.bones[name]
            samples = []
            for f in range(f0, f1 + 1):
                sc.frame_set(f)
                samples.append(pb.matrix_basis.to_quaternion())
            worst = max(2 * __import__("math").degrees(__import__("math").acos(min(1.0, abs(q.w)))) for q in samples)
            # the retarget baked a key on every frame, so keying every frame again overwrites the whole track
            # (Blender 5 actions are layered: no Action.fcurves to delete from)
            pb.rotation_mode = "QUATERNION"
            for i, f in enumerate(range(f0, f1 + 1)):
                q = Quaternion((1, 0, 0, 0)).slerp(samples[i], max(0.0, min(1.0, args.blend)))
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=f)
            sc.frame_set(f0)
            check = pb.matrix_basis.to_quaternion()
            print("[fix]   first frame now %.1f deg from rest" % (2 * __import__("math").degrees(__import__("math").acos(min(1.0, abs(check.w))))))
            print("[fix] %s: %s max %.0f deg from rest -> blend %.2f" % (os.path.basename(path), name, worst, args.blend))
        for ob in bpy.data.objects:
            ob.select_set(ob is arm)
        bpy.context.view_layer.objects.active = arm
        # the retarget's own export settings (tools/retarget_fpp.py), so the file stays byte-compatible with its siblings
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
                                 bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                                 bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                                 bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
        print("[fix] wrote", path, "frames", f0, f1)


if __name__ == "__main__":
    main()
