"""
Close the first-person sword hand a little (user, 2026-09-17: "slightly close the left hand holding the sword, more
towards the tips of the fingers, and the middle finger sticks out a bit more than the others").

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/curl_fp_fingers.py -- [--base 1.08] [--tip 1.2] [--middle 1.12] [--side l] [--only anim_idle_breathing]

Works on every clip in Assets/Animations/SwordFP (the retargeted, mirrored first-person clips human_fp_arms.vmdl lists).
Each joint of one hand is turned a few more degrees about the hand's knuckle line (the axis fingers hinge on): `--deg`
gives the knuckle, middle and end joint, and `--index/--middle/--ring/--pinky` scale one finger's share (1 = as given,
0 = leave that finger alone, negative opens it back up). The thumb is never touched.

Judge the result by each fingertip's distance from the HANDLE AXIS (the knuckle line through the fist, the line the
grip runs along), not along the fingers: that distance is how open a finger is around the handle. On the idle clip,
measured from the knuckle centre:

    no curl at all   index 4.21  middle 3.75  ring 3.24  pinky 2.86 cm
    the defaults     index 3.34  middle 3.18  ring 2.86  pinky 2.86 cm

The user judged the ring finger right at 2.86 and wanted the middle less open, the pinky left alone and the index
not hooked, hence middle x1.2, ring x0.8, pinky x0 (its rest is already the tightest finger) and an even x1 on the
index: one joint given much more than the others reads as a hooked finger even when the distance looks right.

The clip is copied onto the real Synty rig (--rig) and exported from THAT armature, like retarget_fpp.py does: Blender
takes an animation-only FBX's own pose as its rest, so exporting the re-imported clip would write a different rest
skeleton (it broke human_fp_arms.vmdl's compile once), and its local rotations read ~0 (measured 2026-09-17: idle
middle finger 68/33/51 degrees from the Synty rest, 0/0/0 locally).

Re-runnable: the untouched clips are copied once to tools/data/swordfp_uncurled/ and every run starts from those, so
the factors never stack. Runtime bone writes on the FP rig failed twice (memory), which is why this is baked into the
clips. After running: let the editor recompile human_fp_arms.vmdl (touch it if it does not pick the FBX change up).
"""
import bpy, os, sys, glob, shutil, argparse, math
from mathutils import Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.normpath(os.path.join(HERE, ".."))
CLIPS = os.path.join(ROOT, "Assets", "Animations", "SwordFP")
ORIGINALS = os.path.join(HERE, "data", "swordfp_uncurled")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--rig", default=os.path.join(ROOT, "Assets", "CharacterToon", "human_torso.fbx"), help="an FBX carrying the Synty rest skeleton (the open hand the curl is measured from)")
ap.add_argument("--deg", default="6,10,12", help="extra curl in degrees for the knuckle, middle and end joint")
ap.add_argument("--index", type=float, default=1.0, help="share of --deg for the index finger")
ap.add_argument("--middle", type=float, default=1.2, help="share of --deg for the middle finger")
ap.add_argument("--ring", type=float, default=0.8, help="share of --deg for the ring finger")
ap.add_argument("--pinky", type=float, default=0.0, help="share of --deg for the pinky finger")
ap.add_argument("--side", default="l", help="l (the sword hand) or r")
ap.add_argument("--only", default="", help="comma list of clip names to process (default: all)")
ap.add_argument("--out", default="", help="write here instead of Assets/Animations/SwordFP (tests)")
args = ap.parse_args(argv)

import retarget_fpp as rt   # its import and bake/export helpers: the same FBX layout the vmdl expects

FINGERS = ("index", "middle", "ring", "pinky")


DEG = [float(x) for x in args.deg.split(",")]
PER_FINGER = {"index": args.index, "middle": args.middle, "ring": args.ring, "pinky": args.pinky}


def degrees_for(bone):
    finger, joint = bone.split("_")[0], bone.split("_")[1]
    return DEG[int(joint) - 1] * PER_FINGER[finger]


def curl_clip(src, dst):
    """
    The clip is copied onto the REAL Synty rig (--rig, the armature retarget_fpp.py exported every clip from), curled
    there and exported with retarget_fpp's own bake_and_export. Exporting the re-imported clip armature instead (the
    first version, 2026-09-17) wrote the clip's first pose as the skeleton's rest: human_fp_arms.vmdl and human_base.vmdl
    then failed to compile ("Couldn't create animation stream pelvis" on the Magic Attack clips) and the arms went ERROR.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig, rig_objs = rt.import_armature(args.rig)
    for o in rig_objs:
        if o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    clip, _ = rt.import_armature(src)
    action = clip.animation_data.action if clip.animation_data else None
    if action is None:
        raise RuntimeError("no animation in " + src)
    f0, f1 = (int(round(x)) for x in action.frame_range)
    # Blender's FBX round trip lands every key one frame later: shift back so the clip keeps its timing
    shift_keys(action, -1)
    f0, f1 = f0 - 1, f1 - 1

    # every rig bone follows the clip's bone of the same name in world space (same skeleton, same FBX axis correction)
    copied = 0
    for pb in rig.pose.bones:
        if pb.name not in clip.pose.bones:
            continue
        c = pb.constraints.new('COPY_TRANSFORMS')
        c.target = clip
        c.subtarget = pb.name
        c.owner_space = 'WORLD'
        c.target_space = 'WORLD'
        copied += 1

    names = [f"{finger}_{j:02d}_{args.side}" for finger in FINGERS for j in (1, 2, 3)]

    def curl(arm, a, b):
        """
        Each finger joint is turned about the hand's own knuckle line (index knuckle -> pinky knuckle), the axis the
        fingers hinge on, so the curl is the same bend a real finger makes and a bigger number always curls further.
        Scaling the baked rotation instead (the first version) mixed in the fingers' splay and saturated: +7% on the
        middle finger moved its tip 0.03 cm.
        """
        bones = [arm.pose.bones[n] for n in names if n in arm.pose.bones]
        scene = bpy.context.scene
        for pb in bones:
            pb.rotation_mode = 'QUATERNION'
        W = arm.matrix_world
        for f in range(a, b + 1):
            scene.frame_set(f)
            hand = W @ arm.pose.bones["hand_" + args.side].head
            knuckles = ((W @ arm.pose.bones["index_01_" + args.side].head) - (W @ arm.pose.bones["pinky_01_" + args.side].head)).normalized()
            fingers = ((W @ arm.pose.bones["middle_01_" + args.side].head) - hand).normalized()
            palm = fingers.cross(knuckles).normalized()
            if palm.dot((W @ arm.pose.bones["thumb_01_" + args.side].head) - hand) < 0.0:
                palm = -palm
            # + about this axis has to fold the tips toward the palm: pick the sign from the palm normal
            axis = knuckles if knuckles.cross(fingers).dot(palm) > 0.0 else -knuckles
            for pb in bones:   # parents first: a child rides its parent's new bend and then adds its own
                rt.rotate_world(arm, pb, Quaternion(axis, math.radians(degrees_for(pb.name))))
            for pb in bones:
                pb.keyframe_insert('rotation_quaternion', frame=f)

    rt.bake_and_export(rig, f0, f1, dst, post_bake=curl)
    return f0, f1, copied


def shift_keys(action, by):
    """Every key of a (Blender 5 layered) action moved by `by` frames."""
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points:
                        kp.co.x += by
                        kp.handle_left.x += by
                        kp.handle_right.x += by
                    fc.update()


def main():
    os.makedirs(ORIGINALS, exist_ok=True)
    only = set(n for n in args.only.split(",") if n)
    for path in sorted(glob.glob(os.path.join(CLIPS, "*.fbx"))):
        name = os.path.splitext(os.path.basename(path))[0]
        original = os.path.join(ORIGINALS, os.path.basename(path))
        if not os.path.exists(original):
            shutil.copyfile(path, original)
        if only and name not in only:
            continue
        target = os.path.join(args.out, os.path.basename(path)) if args.out else path
        f0, f1, n = curl_clip(original, target)
        print(f"[curl] {name}: frames {f0}-{f1}, {n} bones copied, deg {args.deg}, index x{args.index} middle x{args.middle} ring x{args.ring} pinky x{args.pinky}")


main()
