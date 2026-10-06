"""Retarget a Synty POLYGON animation pack (classic animation rig, the `Polygon` folder) onto a rig that is NOT the
skeleton donor: a monster with its own proportions, its own rest pose and its own bone names (the Mixamo-rigged PSX
Horror monsters first: elkdemon). One animation-only FBX per clip, ready for gen_psx_assets.py to list.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup --python tools/retarget_synty_to_rig.py -- ^
        --rig Assets/Characters/PsxHorror/elkdemon.fbx --names mixamo --out Assets/Animations/PsxHorror/Elkdemon ^
        --strip-prefix A_POLY_IDL_ --strip-suffix _Masc [--report] [--ref <T-pose clip on the animation rig>] ^
        "<Idles pack>/SourceFiles/Animations/Polygon/Masculine/**/*.fbx"

How it differs from tools/retarget_synty.py (same clips onto the donor, which shares Synty's skeleton):

 * Names: the target's bones are RENAMED to Epic names for the run (--names picks the table; `mixamo` maps Hips /
   Spine / LeftArm / LeftHandIndex1 ... onto pelvis / spine_01 / upperarm_l / index_01_l) and renamed BACK before the
   export, so the clip FBX carries the model's own skeleton. Bones without a twin (HeadTop_End, finger tips, Toe_End)
   keep their rest pose.
 * Rest pose: the monster does not rest in Synty's T-pose (the elkdemon hangs its arms, 80 deg off, and pitches its
   head 32 deg down), so the pure rest-delta transfer would carry that offset into every frame. Step one is
   retarget_fpp.prepose(): aim every mapped bone along the T-pose reference's joint directions (palm plane from
   index/thumb), i.e. stand the monster in Synty's T-pose. Step two is retarget_synty's exact delta on top of THAT
   pose: world rotation = src_rest * tpose_rest^-1 * monster_tposed, so the monster stands in the clip's first frame;
   the helper-bone transfer then plays src(t) * tpose_rest^-1 * monster_tposed, and joint directions match the source
   to 0.0 deg (bone rolls differ per rig and fall out of the world-space transfer).
 * Pelvis: the source's pelvis position is meaningless for a body of another size, so after the bake the pelvis track
   is rewritten as  monster_rest_pelvis + k * (src_pelvis(t) - tpose_pelvis)  with k = the pelvis-height ratio of
   the two rests (the elkdemon's 0.58 m over Synty's 0.88 m): sways and leans scale with the body, and a clip whose
   first frame is mid-lean still starts in the right place because the T-pose, not the first frame, is the zero. Root
   travel (the `_RootMotion_` variants) is copied onto the monster's `root` scaled by the same k.
 * Facing: the monster must face -Y in Blender like the animation rig (toonify_psx.py turns the Mixamo ones); the
   tool refuses a rig whose feet point elsewhere instead of guessing a yaw.

--report prints the joint-direction differences per sampled frame (expect 0.0 on the limbs; the spine line spans three
bones of different proportions and stays a few degrees off) and the pelvis heights on both rigs.
"""
import argparse, glob, math, os, sys

import bpy
from mathutils import Matrix, Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import retarget_fpp as R  # noqa: E402
import retarget_synty as S  # noqa: E402
import skeleton_kinds  # noqa: E402

# target-rig naming tables: their bone -> Epic twin (the Synty side is retarget_synty's classic -> Epic table)
MIXAMO = {"Hips": "pelvis", "Spine": "spine_01", "Spine1": "spine_02", "Spine2": "spine_03", "Neck": "neck_01", "Head": "head"}
for _side, _s in (("Left", "l"), ("Right", "r")):
    MIXAMO.update({_side + "Shoulder": "clavicle_" + _s, _side + "Arm": "upperarm_" + _s, _side + "ForeArm": "lowerarm_" + _s,
                   _side + "Hand": "hand_" + _s, _side + "UpLeg": "thigh_" + _s, _side + "Leg": "calf_" + _s,
                   _side + "Foot": "foot_" + _s, _side + "ToeBase": "ball_" + _s})
    for _f, _e in (("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Pinky", "pinky")):
        for _i in (1, 2, 3):
            MIXAMO["%sHand%s%d" % (_side, _f, _i)] = "%s_0%d_%s" % (_e, _i, _s)
NAME_TABLES = {"mixamo": MIXAMO}
FACING_TOLERANCE_DEG = 30.0


def rename(arm, table):
    """Rename `arm`'s bones through `table` (Blender rewrites the action's data paths with them). Returns the inverse
    map for renaming back."""
    back = {}
    for b in list(arm.data.bones):
        if b.name in table:
            new = table[b.name]
            if new in arm.data.bones:
                raise RuntimeError("%s: renaming %s to %s collides with an existing bone" % (arm.name, b.name, new))
            back[new] = b.name
            b.name = new
    return back


def facing(arm):
    """Horizontal ankle -> toe direction of the rig at rest (world)."""
    d = Vector((0.0, 0.0, 0.0))
    for s in "lr":
        f, t = "foot_" + s, "ball_" + s
        if f in arm.data.bones and t in arm.data.bones:
            d += arm.matrix_world @ arm.data.bones[t].head_local - arm.matrix_world @ arm.data.bones[f].head_local
    d.z = 0.0
    return d.normalized() if d.length > 1e-6 else None


def world_pose(arm, n):
    return (arm.matrix_world @ arm.pose.bones[n].matrix).to_3x3()


def prepose_two_step(dst, src, ref):
    """1. aim `dst` into the T-pose reference's joint directions (retarget_fpp.prepose); 2. rotate each mapped bone by
    the clip's first-frame delta from the T-pose (retarget_synty's exact rule), parents first. Returns the mapped names."""
    ref.data.pose_position = "REST"; src.data.pose_position = "REST"
    # the hand is aimed hand -> middle_01 in retarget_fpp's table; a rig without a middle finger (Mixamo's PSX monsters
    # carry thumb + index only) aims it at the index instead of skipping the hand (26 deg of wrist error otherwise)
    spec = []
    for bone, a, b in R.SPEC:
        if bone.startswith("hand_") and b not in dst.pose.bones:
            alt = "index_01_" + bone[-1]
            if alt in dst.pose.bones and alt in ref.pose.bones:
                b = alt
        spec.append((bone, a, b))
    saved_spec, R.SPEC = R.SPEC, spec
    try:
        mapped = R.prepose(dst, ref)
    finally:
        R.SPEC = saved_spec
    mapped = [n for n in mapped if n in src.data.bones and n in ref.data.bones]
    tposed = {n: world_pose(dst, n) for n in mapped}

    def depth(pb):
        d = 0; p = pb.parent
        while p is not None:
            d += 1; p = p.parent
        return d
    W = dst.matrix_world; Winv = W.inverted()
    for pb in sorted((dst.pose.bones[n] for n in mapped), key=depth):
        n = pb.name
        target = S.world_rest(src, n) @ S.world_rest(ref, n).inverted() @ tposed[n]
        head = (W @ pb.matrix).to_translation()
        pb.matrix = Winv @ (Matrix.Translation(head) @ target.to_4x4())
        bpy.context.view_layer.update()
    return mapped


def report(dst, src, frames):
    scene = bpy.context.scene
    chains = (("uarm_l", "upperarm_l", "lowerarm_l"), ("larm_l", "lowerarm_l", "hand_l"), ("hand_l", "hand_l", "index_01_l"),
              ("uarm_r", "upperarm_r", "lowerarm_r"), ("larm_r", "lowerarm_r", "hand_r"), ("thigh_l", "thigh_l", "calf_l"),
              ("calf_l", "calf_l", "foot_l"), ("foot_l", "foot_l", "ball_l"), ("neck", "neck_01", "head"), ("spine", "pelvis", "spine_03"))
    for f in frames:
        scene.frame_set(f); bpy.context.view_layer.update()
        parts = []
        for name, a, b in chains:
            if all(n in dst.pose.bones and n in src.pose.bones for n in (a, b)):
                parts.append("%s %.1f" % (name, R.deg(R.wdir(src, a, b), R.wdir(dst, a, b))))
        print("  f%03d %s | pelvis z dst %.3f src %.3f" % (f, "  ".join(parts), R.whead(dst, "pelvis").z, R.whead(src, "pelvis").z))


def retarget_clip(rig_path, table, clip_path, out_path, ref_path, do_report=False, fps=0):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    dst = S.import_rig_only(rig_path)
    back = rename(dst, table)
    if "pelvis" not in dst.data.bones or "root" not in dst.data.bones:
        raise RuntimeError("%s: the rig needs a root and a pelvis after the %s rename (bones %s)" % (rig_path, "/".join(NAME_TABLES), sorted(b.name for b in dst.data.bones)))
    ref = S.import_rig_only(ref_path)
    S.rename_to_epic(ref)
    src, _ = R.import_armature(clip_path)
    action = src.animation_data.action if src.animation_data else None
    if action is None:
        raise RuntimeError("no animation in " + clip_path)
    S.rename_to_epic(src)
    if "pelvis" not in src.pose.bones or "hand_r" not in src.pose.bones:
        raise RuntimeError("%s is not on the Synty classic rig" % clip_path)
    fd, fr = facing(dst), facing(ref)
    if fd is None or fr is None or fd.angle(fr) > math.radians(FACING_TOLERANCE_DEG):
        raise RuntimeError("%s faces %s but the animation rig faces %s: turn the model first (toonify_psx.py orient_and_ground)" % (
            os.path.basename(rig_path), None if fd is None else tuple(round(v, 2) for v in fd), None if fr is None else tuple(round(v, 2) for v in fr)))
    scene = bpy.context.scene
    if fps:
        scene.render.fps = fps; scene.render.fps_base = 1.0
    f0, f1 = (int(round(x)) for x in action.frame_range)

    # rest geometry for the pelvis rewrite (all three rigs at rest, before anything is posed)
    for a in (dst, ref, src):
        a.data.pose_position = "REST"
    bpy.context.view_layer.update()
    dst_root_rest = dst.matrix_world @ dst.data.bones["root"].head_local
    dst_pelvis_rest = dst.matrix_world @ dst.data.bones["pelvis"].head_local
    ref_pelvis = ref.matrix_world @ ref.data.bones["pelvis"].head_local
    ref_root = ref.matrix_world @ ref.data.bones["root"].head_local if "root" in ref.data.bones else Vector((0.0, 0.0, 0.0))
    k = (dst_pelvis_rest.z - dst_root_rest.z) / (ref_pelvis.z - ref_root.z)
    dst.data.pose_position = "POSE"

    mapped = prepose_two_step(dst, src, ref)
    R.bind(dst, src, mapped, root_motion=False, pelvis_at_source=True)
    if do_report:
        print("== %s frames %d-%d at %g fps, %d bones bound, pelvis ratio k=%.3f (dst %.3f m, ref %.3f m)" % (
            os.path.basename(clip_path), f0, f1, scene.render.fps / scene.render.fps_base, len(mapped), k, dst_pelvis_rest.z, ref_pelvis.z))

    def post_bake(arm, a0, a1):
        # pelvis + root tracks: the monster's own rest, plus the source's motion about ITS T-pose, scaled to the body
        src_has_root = "root" in src.pose.bones
        for pb in (arm.pose.bones["root"], arm.pose.bones["pelvis"]):
            pb.rotation_mode = "QUATERNION"
        for f in range(a0, a1 + 1):
            scene.frame_set(f); bpy.context.view_layer.update()
            s_root = R.whead(src, "root") if src_has_root else Vector((0.0, 0.0, 0.0))
            for n in ("root", "pelvis"):
                pb = arm.pose.bones[n]
                if n == "root":
                    want = dst_root_rest + k * (s_root - ref_root)
                else:
                    root_now = R.whead(arm, "root")
                    want = root_now + (dst_pelvis_rest - dst_root_rest) + k * ((R.whead(src, "pelvis") - s_root) - (ref_pelvis - ref_root))
                m = arm.matrix_world @ pb.matrix
                m = Matrix.Translation(want - m.to_translation()) @ m
                pb.matrix = arm.matrix_world.inverted() @ m
                bpy.context.view_layer.update()
                pb.keyframe_insert("location", frame=f)
        if do_report:
            report(arm, src, sorted({a0, (a0 + a1) // 4, (a0 + a1) // 2, (3 * (a0 + a1)) // 4, a1}))
        rename(arm, back)   # the clip FBX must carry the model's own bone names

    R.bake_and_export(dst, f0, f1, out_path, post_bake=post_bake)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--rig", required=True, help="the target model FBX (Assets/Characters/PsxHorror/elkdemon.fbx)")
    ap.add_argument("--names", required=True, choices=sorted(NAME_TABLES), help="the target rig's naming (mixamo)")
    ap.add_argument("--out", required=True, help="output folder (Assets/Animations/PsxHorror/<Mesh>)")
    ap.add_argument("--ref", default=skeleton_kinds.POLYGON_TPOSE_CLIP,
                    help="a T-pose clip ON THE ANIMATION RIG: the canonical rest the clips are measured against")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--fps", type=int, default=0)
    ap.add_argument("--strip-prefix", default="", help="drop this prefix from the output name (A_POLY_IDL_)")
    ap.add_argument("--strip-suffix", default="", help="drop this suffix from the output name (_Masc)")
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
    print("[retarget_synty_to_rig] %d clips -> %s" % (len(names), args.out))
    for name, clip in names.items():
        retarget_clip(os.path.abspath(args.rig), NAME_TABLES[args.names], os.path.abspath(clip),
                      os.path.join(os.path.abspath(args.out), name + ".fbx"), os.path.abspath(args.ref), args.report, args.fps)


if __name__ == "__main__":
    main()
