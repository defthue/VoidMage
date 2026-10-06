"""Retarget a Synty POLYGON animation pack (their CLASSIC rig: Root / Hips / Spine_01 / Shoulder_L / Elbow_L /
Finger_01 with the right hand's fingers suffixed `_1`, plus the Prop_L / Prop_R weapon bones) onto the skeleton
donor rig (Epic names, tools/prep_skeleton_npc.py builds it), one animation-only FBX per clip, ready for
gen_skeleton_npc_vmdl.py.

Why not play the clips directly: the enemies are all re-skinned onto the Dungeon skeleton's Epic-named rig
(skeleton_kinds.py) so one clip set, one animgraph and one ragdoll table serve every skin. The classic rig's
joints sit where the Epic rig's do (0.00 cm, measured on every Synty pack so far) but its bones are named and
ROLLED differently (47-180 degrees), so a rename binds wrong; the clips have to be re-authored on the donor.

How: the classic bones are RENAMED to their Epic twins on import (Blender rewrites the action's data paths), which
turns the job into tools/retarget_fpp.py's machinery -- pose the donor into the source's rest, bind world-space
Copy Rotation through helper bones (rest-delta transfer, so the roll difference falls out), Copy Location on the
pelvis, bake, export. Two things differ from the Manny run, both because the source is Synty's own skeleton:

 * The pre-pose is EXACT, not aim-by-direction. A Synty animation-only FBX has no bind pose: Blender takes the
   clip's FIRST FRAME as the armature's rest (the leaping attack's rest pelvis sits 1.4 m from the origin, its hips
   yawed 17 deg), and aiming the donor's bones along that pose's joint directions leaves every bone's roll
   unmatched: 3-4 cm of joint error and a constant twist through the torso and limbs. Instead the animation rig's
   own T-pose clip (--ref, skeleton_kinds.POLYGON_TPOSE_CLIP: the pose the characters are bound in, joints at
   0.00 cm from the donor's) gives the canonical rest IN THE SAME FRAME CONVENTION as the clips, and each donor bone
   is posed to src_rest * tpose_rest^-1 * donor_rest: the donor standing exactly in the clip's first frame, roll
   and all. From there the helper transfer is `src(t) * tpose_rest^-1 * donor_rest` -- the source bone's rotation
   relative to its T-pose applied to the donor's rest -- and joints land to 0.0 cm every frame. A classic-rig
   CHARACTER file cannot serve as the reference: Synty authored that rig with X along the bone and post-rotations,
   the animation rig with Y along the bone and pre-rotations only, so their bone frames differ per bone even in
   the same pose (132 deg on the pelvis for a 5 deg change of direction, measured 2026-09-12).
 * The pelvis helper sits at the SOURCE pelvis (retarget_fpp.bind pelvis_at_source), since the same skeleton
   means the same pelvis height; at the donor's rest position it would drag the body by the bind pose's offset.

The source `Root` is a real bone, so its travel is copied bone-to-bone onto the donor's `root` (the RootMotion
clip variants carry it; the plain variants keep it at zero; root rotation is not copied, as for the Sword &
Shield pack). The Prop_R / Prop_L bones (10 cm off each hand) drive the donor's weapon_r / weapon_l sockets
through the same rest-delta helpers: where Synty's prop sits relative to the hand as it does in the bind pose,
the socket sits as prep placed it, so prep's fist-derived grip still holds. No mirror, no finger relax.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup --python tools/retarget_synty.py -- ^
        --rig Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx --out Assets/Animations/SkeletonSword ^
        --strip-prefix A_ --strip-suffix _Sword [--report] [--fps 30] [--ref <T-pose clip on the same rig>] ^
        "C:/Users/skipp/Desktop/Speelboord Studios/Animation/SwordAnimations/SwordAnimations/Polygon/**/*.fbx"

The `Polygon` folder of a Synty animation pack is the classic-rig export (the `Sidekick` folder is the newer
Sidekick rig, which this tool does not take). Bones with no Epic twin (Jaw, Toes, the fourth finger segments)
are left unrenamed and simply not bound. --report prints per-frame limb angles (expect 0.0) and absolute
root / pelvis / hand positions on both rigs before and after the bake (expect 0.0 cm).
"""
import argparse, glob, os, re, sys

import bpy
from mathutils import Matrix

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import retarget_fpp as R  # noqa: E402
import skeleton_kinds  # noqa: E402

# classic -> Epic, injective (prep_skeleton_npc.py's re-skin map also MERGES Jaw/Toes/_04 into parents; a
# retarget must not, so those stay unmapped here)
UNSIDED = {"Root": "root", "Hips": "pelvis", "Spine_01": "spine_01", "Spine_02": "spine_02", "Spine_03": "spine_03",
           "Neck": "neck_01", "Head": "head", "Eyes": "eyes", "Eyebrows": "eyebrows"}
SIDED = {"Clavicle": "clavicle", "Shoulder": "upperarm", "Elbow": "lowerarm", "Hand": "hand",
         "UpperLeg": "thigh", "LowerLeg": "calf", "Ankle": "foot", "Ball": "ball", "Prop": "weapon"}
FINGERS = {"Thumb": "thumb", "IndexFinger": "index", "Finger": "middle"}
SOCKETS = ("weapon_r", "weapon_l")


def epic_name(bone):
    """Epic twin of one classic bone (bpy.types.Bone), or None. The right hand's finger bones are `Finger_01_1`
    in the pack (Blender would suffix `.001` for identical names); the side comes from the first `_L`/`_R`
    ancestor either way."""
    base = re.sub(r"(_1|\.\d{3})$", "", bone.name)
    if base in UNSIDED:
        return UNSIDED[base]
    m = re.match(r"^(.+)_([LR])$", base)
    if m and m.group(1) in SIDED:
        return SIDED[m.group(1)] + "_" + m.group(2).lower()
    m = re.match(r"^(Thumb|IndexFinger|Finger)_0([123])$", base)
    if m:
        p = bone.parent
        while p is not None and not re.search(r"_[LR]$", re.sub(r"(_1|\.\d{3})$", "", p.name)):
            p = p.parent
        if p is None:
            raise RuntimeError("no _L/_R ancestor for finger bone " + bone.name)
        return "%s_0%s_%s" % (FINGERS[m.group(1)], m.group(2), re.sub(r"(_1|\.\d{3})$", "", p.name)[-1].lower())
    return None


def rename_to_epic(src):
    renames, skipped = {}, []
    for b in src.data.bones:
        n = epic_name(b)
        if n is None:
            skipped.append(b.name)
        else:
            if n in renames.values():
                raise RuntimeError("two classic bones map to %s" % n)
            renames[b.name] = n
    for old, new in renames.items():
        src.data.bones[old].name = new
    return renames, skipped


def import_rig_only(path):
    arm, objs = R.import_armature(path)
    for o in objs:
        if o.type == "MESH":
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if data.users == 0:
                bpy.data.meshes.remove(data)
    if arm.animation_data:
        arm.animation_data.action = None
    return arm


def world_rest(arm, n):
    return (arm.matrix_world @ arm.data.bones[n].matrix_local).to_3x3()


def prepose_exact(dst, src, ref):
    """Pose `dst` (donor, Epic names) into `src`'s rest (the clip's first frame) exactly: for every bone present on
    all three rigs, world rotation = src_rest * ref_rest^-1 * dst_rest, where `ref` is the animation rig's own
    T-pose clip (same frame convention as `src`; joints at 0.00 cm from the donor's rest, so `ref_rest^-1 * dst_rest`
    is the pure rig-convention difference). Parents first; heads follow from the parents. Returns the posed bone
    names."""
    src.data.pose_position = "REST"; ref.data.pose_position = "REST"; dst.data.pose_position = "POSE"
    for pb in dst.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()

    def depth(pb):
        d = 0; p = pb.parent
        while p is not None:
            d += 1; p = p.parent
        return d
    mapped = []
    W = dst.matrix_world; Winv = W.inverted()
    for pb in sorted(dst.pose.bones, key=depth):
        n = pb.name
        if n == "root" or n not in src.data.bones or n not in ref.data.bones:
            continue
        target = world_rest(src, n) @ world_rest(ref, n).inverted() @ world_rest(dst, n)
        head = (W @ pb.matrix).to_translation()
        pb.matrix = Winv @ (Matrix.Translation(head) @ target.to_4x4())
        bpy.context.view_layer.update()
        mapped.append(n)
    return mapped


def retarget_clip(rig_path, clip_path, out_path, ref_path, do_report=False, fps=0):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    dst = import_rig_only(rig_path)
    ref = import_rig_only(ref_path)
    ref_renames, _ = rename_to_epic(ref)
    src, _ = R.import_armature(clip_path)
    action = src.animation_data.action if src.animation_data else None
    if action is None:
        raise RuntimeError("no animation in " + clip_path)
    renames, skipped = rename_to_epic(src)
    if "pelvis" not in src.pose.bones or "hand_r" not in src.pose.bones:
        raise RuntimeError("%s is not on the Synty classic rig (bones: %s)" % (clip_path, sorted(b.name for b in src.data.bones)))
    scene = bpy.context.scene
    if fps:
        scene.render.fps = fps; scene.render.fps_base = 1.0
    f0, f1 = (int(round(x)) for x in action.frame_range)
    mapped = prepose_exact(dst, src, ref)
    # the weapon sockets ride Synty's prop bones through the same rest-delta helpers as every other bone (the
    # reference character has no prop bones; the socket keeps its rest seat on the now-posed hand)
    mapped += [s for s in SOCKETS if s in dst.pose.bones and s in src.pose.bones]
    # pelvis helper at the SOURCE pelvis: this pack's bind pose is sampled mid-clip (its rest pelvis sat 1.4 m from
    # the origin on the leaping attack), and a helper at the target's rest position carried that gap into every frame
    R.bind(dst, src, mapped, root_motion=False, pelvis_at_source=True)
    if "root" in src.pose.bones and "root" in dst.pose.bones:
        pb = dst.pose.bones["root"]
        c = pb.constraints.new("COPY_LOCATION"); c.name = "VMRET_loc"; c.target = src; c.subtarget = "root"
        c.owner_space = "WORLD"; c.target_space = "WORLD"
    if do_report:
        print("== %s frames %d-%d at %g fps, %d bones bound (%d renamed, unmapped %s; reference %s, %d bones)" % (
            os.path.basename(clip_path), f0, f1, scene.render.fps / scene.render.fps_base, len(mapped), len(renames), skipped,
            os.path.basename(ref_path), len(ref_renames)))
        scene.frame_set(f0); p0 = R.whead(src, "root").copy() if "root" in src.pose.bones else None
        scene.frame_set(f1); p1 = R.whead(src, "root").copy() if "root" in src.pose.bones else None
        if p0 is not None:
            print("  source root travel f%d->f%d: %s m" % (f0, f1, tuple(round(v, 3) for v in (p1 - p0))))
        # joint geometry: how far the classic joints sit from the donor's in the pre-posed pose (0 = same body)
        scene.frame_set(f0)
        joints = [n for n in mapped if n not in SOCKETS]
        worst = max(((R.whead(src, n) - R.whead(dst, n)).length * 100.0, n) for n in joints)
        print("  worst joint offset after pre-pose: %.2f cm at %s" % worst)
        for s in SOCKETS:
            if s in src.pose.bones:
                hand = "hand_" + s[-1]
                print("  source %s (Prop_%s) sits %.1f cm from %s, %.1f cm from pelvis" % (
                    s, s[-1].upper(), (R.whead(src, s) - R.whead(src, hand)).length * 100.0, hand,
                    (R.whead(src, s) - R.whead(src, "pelvis")).length * 100.0))
        R.report(dst, src, sorted({f0, (f0 + f1) // 4, (f0 + f1) // 2, (3 * (f0 + f1)) // 4, f1}))
        absolute(dst, src, (f0, (f0 + f1) // 2, f1), "bound")
    R.bake_and_export(dst, f0, f1, out_path)
    if do_report:
        absolute(dst, src, (f0, (f0 + f1) // 2, f1), "baked")


def absolute(dst, src, frames, label):
    """World positions of root / pelvis / hand_r on both rigs: the retarget_fpp report only compares directions."""
    scene = bpy.context.scene
    for f in frames:
        scene.frame_set(f); bpy.context.view_layer.update()
        parts = []
        for n in ("root", "pelvis", "hand_r"):
            if n in dst.pose.bones and n in src.pose.bones:
                d, s = R.whead(dst, n), R.whead(src, n)
                parts.append("%s dst(%.2f %.2f %.2f) src(%.2f %.2f %.2f) off %.1f cm" % (n, d.x, d.y, d.z, s.x, s.y, s.z, (d - s).length * 100.0))
        print("  %s f%03d %s" % (label, f, " | ".join(parts)))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--rig", required=True, help="the prepared donor FBX (Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx)")
    ap.add_argument("--out", required=True, help="output folder (Assets/Animations/<set>)")
    ap.add_argument("--ref", default=skeleton_kinds.POLYGON_TPOSE_CLIP,
                    help="a T-pose clip ON THE ANIMATION RIG: the canonical rest the clips are measured against")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--fps", type=int, default=0, help="force the scene frame rate before baking (the importer normally reads it from the file)")
    ap.add_argument("--strip-prefix", default="", help="drop this prefix from the output name (A_)")
    ap.add_argument("--strip-suffix", default="", help="drop this suffix from the output name (_Sword)")
    ap.add_argument("clips", nargs="+", help="clip FBX files or globs (** recurses)")
    args = ap.parse_args(argv)
    clips = []
    for pattern in args.clips:
        clips += sorted(glob.glob(pattern, recursive=True)) or [pattern]
    names = {}
    for clip in clips:
        name = os.path.splitext(os.path.basename(clip))[0]
        if args.strip_prefix and name.startswith(args.strip_prefix):
            name = name[len(args.strip_prefix):]
        if args.strip_suffix and name.endswith(args.strip_suffix):
            name = name[:-len(args.strip_suffix)]
        name = name.lower().replace("-", "_")
        if name in names:
            raise RuntimeError("duplicate output name %s from %s and %s" % (name, names[name], clip))
        names[name] = clip
    print("[retarget_synty] %d clips -> %s" % (len(names), args.out))
    for name, clip in names.items():
        retarget_clip(os.path.abspath(args.rig), os.path.abspath(clip), os.path.join(os.path.abspath(args.out), name + ".fbx"),
                      os.path.abspath(args.ref), args.report, args.fps)


if __name__ == "__main__":
    main()
