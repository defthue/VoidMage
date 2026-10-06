"""Retarget the "First Person sword and shield" pack (UE5 Manny skeleton) onto the Synty Sidekick rig
used by CharacterToon/human_base.vmdl, one animation-only FBX per clip, ready to list as AnimFile
entries in the vmdl.

Why a retarget is needed at all: both rigs use Epic bone names, but Manny rests in an A-pose with
5 spine + 2 neck bones and metacarpals, while the Synty rig rests in a T-pose with 3 spine + 1 neck.
Copying rotations bone-for-bone therefore leaves the arms ~53-61 degrees off. This script:

 1. imports the Synty rig (any CharacterToon/*.fbx carries the full skeleton) and the clip,
 2. poses the Synty rig so every mapped bone points where Manny's rest pose points (hands also get
    their palm plane matched), so "Synty in this pose" == "Manny at rest",
 3. adds a helper bone on Manny per mapped bone that carries the Synty bone's posed world frame and
    world-space Copy Rotation constraints on the Synty bones (Copy Location on the pelvis). Missing
    Manny bones (spine_04/05, neck_02, metacarpals) do not matter: world rotations already include them,
 4. bakes and exports the Synty armature with its animation only (cm, Y-up, 30 fps, same layout as
    Assets/Animations/Magic/*.fbx).

Usage (Blender 4.2+ / 5.0):
  blender -b --factory-startup --python tools/retarget_fpp.py -- --rig Assets/CharacterToon/human_torso.fbx
      --out Assets/Animations/SwordFP "<pack>/Animations/*.FBX"
Options: --pelvis-height-offset <m> to raise/lower the pelvis track (default keeps the Synty pelvis height),
         --report to print per-frame limb angle differences against the source (verification).

The same script drives the Sword & Shield pack onto the Dungeon skeleton NPC (tools/prep_skeleton_npc.py
builds that rig): --no-mirror --root-motion --sockets weapon_r,shield_l --strip-prefix SSH_ --strip-suffix _v001.
Any rig with Epic bone names works: bones missing on the target (ring/pinky, twists) are skipped and the palm
plane falls back to index/middle.
"""
import bpy, sys, os, glob, math, argparse
from mathutils import Vector, Matrix, Quaternion

# --------------------------------------------------------------------------------------- mapping
def _arm(s):
    return [
        ('clavicle_%s' % s, 'clavicle_%s' % s, 'upperarm_%s' % s),
        ('upperarm_%s' % s, 'upperarm_%s' % s, 'lowerarm_%s' % s),
        ('upperarm_twist_01_%s' % s, 'upperarm_%s' % s, 'lowerarm_%s' % s),
        ('lowerarm_%s' % s, 'lowerarm_%s' % s, 'hand_%s' % s),
        ('lowerarm_twist_01_%s' % s, 'lowerarm_%s' % s, 'hand_%s' % s),
        ('hand_%s' % s, 'hand_%s' % s, 'middle_01_%s' % s),
        ('thumb_01_%s' % s, 'thumb_01_%s' % s, 'thumb_02_%s' % s),
        ('thumb_02_%s' % s, 'thumb_02_%s' % s, 'thumb_03_%s' % s),
        ('index_01_%s' % s, 'index_01_%s' % s, 'index_02_%s' % s),
        ('index_02_%s' % s, 'index_02_%s' % s, 'index_03_%s' % s),
        ('middle_01_%s' % s, 'middle_01_%s' % s, 'middle_02_%s' % s),
        ('middle_02_%s' % s, 'middle_02_%s' % s, 'middle_03_%s' % s),
        ('ring_01_%s' % s, 'ring_01_%s' % s, 'ring_02_%s' % s),
        ('ring_02_%s' % s, 'ring_02_%s' % s, 'ring_03_%s' % s),
        ('pinky_01_%s' % s, 'pinky_01_%s' % s, 'pinky_02_%s' % s),
        ('pinky_02_%s' % s, 'pinky_02_%s' % s, 'pinky_03_%s' % s),
    ]

def _leg(s):
    return [
        ('thigh_%s' % s, 'thigh_%s' % s, 'calf_%s' % s),
        ('thigh_twist_01_%s' % s, 'thigh_%s' % s, 'calf_%s' % s),
        ('calf_%s' % s, 'calf_%s' % s, 'foot_%s' % s),
        ('calf_twist_01_%s' % s, 'calf_%s' % s, 'foot_%s' % s),
        ('foot_%s' % s, 'foot_%s' % s, 'ball_%s' % s),
    ]

# (bone, from joint, to joint): the bone is aimed along from->to, evaluated on both rigs. Parents first.
SPEC = ([('pelvis', 'pelvis', 'spine_01'), ('spine_01', 'spine_01', 'spine_02'), ('spine_02', 'spine_02', 'spine_03'),
         ('spine_03', 'spine_03', 'neck_01'), ('neck_01', 'neck_01', 'head')]
        + _arm('l') + _arm('r') + _leg('l') + _leg('r'))
# rotation-only followers with no direction of their own
EXTRA_ROT = ['head', 'ball_l', 'ball_r'] + ['%s_03_%s' % (f, s) for s in 'lr' for f in ('index', 'middle', 'ring', 'pinky', 'thumb')]
HELPER_SUFFIX = '_VMRET'


def import_armature(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arms = [o for o in new if o.type == 'ARMATURE']
    if not arms:
        raise RuntimeError('no armature in ' + path)
    return arms[0], new


def whead(o, n): return o.matrix_world @ o.pose.bones[n].head
def wdir(o, a, b): return (whead(o, b) - whead(o, a)).normalized()
def deg(a, b): return math.degrees(a.angle(b))


def rotate_world(o, pb, Q):
    """Rotate a pose bone about its head by world-space quaternion Q."""
    W = o.matrix_world; M = W @ pb.matrix; h = M.to_translation()
    R = Matrix.Translation(h) @ Q.to_matrix().to_4x4() @ Matrix.Translation(-h)
    pb.matrix = W.inverted() @ R @ M
    bpy.context.view_layer.update()


def select_only(o):
    for ob in bpy.data.objects:
        ob.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


# Finger pairs that span the palm plane, first one present on BOTH rigs wins. The Sidekick rig has all five
# fingers; the Dungeon skeleton character only has thumb/index/middle.
PALM_PAIRS = [('index_01_%s', 'pinky_01_%s'), ('index_01_%s', 'middle_01_%s'), ('index_01_%s', 'thumb_01_%s')]


def palm_pair(dst, src, side):
    for a, b in PALM_PAIRS:
        a = a % side; b = b % side
        if a in dst.pose.bones and b in dst.pose.bones and a in src.pose.bones and b in src.pose.bones:
            return a, b
    return None


def prepose(dst, src):
    """Pose dst (Synty) so that each mapped bone points like src (Manny) at rest."""
    src.data.pose_position = 'REST'; dst.data.pose_position = 'POSE'
    for pb in dst.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    mapped = []
    for bone, a, b in SPEC:
        # a rig missing a joint on either side skips that bone (the legless Tormented Soul has no foot_l/r);
        # a bone the SOURCE lacks (the Synty twist bones against Quaternius' UAL rig) is skipped too: bind()
        # parents its helper under the source bone of the same name, and a twist left alone rides its parent
        if any(n not in dst.pose.bones for n in (bone, a, b)) or any(n not in src.pose.bones for n in (bone, a, b)):
            continue
        pb = dst.pose.bones[bone]
        rotate_world(dst, pb, wdir(dst, a, b).rotation_difference(wdir(src, a, b)))
        if bone.startswith('hand_'):
            s = bone[-1]; d = wdir(src, a, b)
            pair = palm_pair(dst, src, s)
            if pair is None:
                print('  WARNING: no palm-plane finger pair for', bone, '- palm roll not matched')
            else:
                i, j = pair
                ps = whead(src, i) - whead(src, j)
                pd = whead(dst, i) - whead(dst, j)
                ps -= d * ps.dot(d); pd -= d * pd.dot(d)
                angle = pd.angle(ps)
                if pd.cross(ps).dot(d) < 0:
                    angle = -angle
                rotate_world(dst, pb, Quaternion(d, angle))
        mapped.append(bone)
    mapped += [n for n in EXTRA_ROT if n in dst.pose.bones and n in src.data.bones]
    return mapped


FINGER_PREFIXES = ('index_', 'middle_', 'ring_', 'pinky_', 'thumb_')


def bind(dst, src, mapped, pelvis_height_offset=0.0, finger_influence=1.0, root_motion=False, sockets=(), pelvis_at_source=False):
    """finger_influence < 1 relaxes the fist: Manny's grip is a tight fist around a thin handle (knuckles
    ~100 deg, middle joints ~70 deg) and the chunky Synty fingers fold through the palm at that angle. The
    finger constraints then blend between the pre-posed hand (Manny's relaxed rest) and the clip.
    root_motion: Manny's `root` is an FBX Root node, so Blender makes it the clip's ARMATURE OBJECT and its
    travel lives on the object transform. Copying that object location onto the target's `root` bone keeps
    the travel on root and leaves the pelvis root-relative (bake keys each bone against its parent's final
    matrix). Without it the target root stays put and the travel folds into the pelvis track.
    sockets: free bones present on both rigs (weapon_r, shield_l, ...) that copy Manny's socket outright.
    pelvis_at_source: author the pelvis helper at the SOURCE pelvis's rest position instead of the target's. Default
    off keeps the target's own pelvis height (Manny vs Synty differ). On for a source whose rest is NOT a standing
    pose at the origin: Synty's animation-only packs carry a bind pose sampled mid-clip, so the source rest pelvis
    can sit metres from the origin and a helper at the target's rest position would drag the whole body by that
    gap (retarget_synty.py; the two rigs there have identical proportions, so the source position IS the answer)."""
    # Helper bones carry the target's pre-posed world frames. They must be authored with the clip armature at
    # its rest placement: the object may already be translated by root motion at the current frame.
    saved_loc = src.location.copy(); saved_action = src.animation_data.action if src.animation_data else None
    if saved_action is not None:
        src.animation_data.action = None
    src.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    select_only(src); bpy.ops.object.mode_set(mode='EDIT')
    Winv = src.matrix_world.inverted()
    for n in mapped:
        eb = src.data.edit_bones.new(n + HELPER_SUFFIX)
        eb.head = Vector((0, 0, 0)); eb.tail = Vector((0, 0.1, 0))
        m = dst.matrix_world @ dst.pose.bones[n].matrix
        if n == 'pelvis':
            if pelvis_at_source:
                src_rest_head = src.matrix_world @ src.data.bones[n].head_local
                m = Matrix.Translation(src_rest_head - m.to_translation()) @ m
            m = Matrix.Translation(Vector((0, 0, pelvis_height_offset))) @ m
        eb.matrix = Winv @ m
        eb.parent = src.data.edit_bones[n]; eb.use_deform = False
    bpy.ops.object.mode_set(mode='OBJECT')
    if saved_action is not None:
        src.animation_data.action = saved_action
    src.location = saved_loc
    for n in mapped:
        pb = dst.pose.bones[n]
        c = pb.constraints.new('COPY_ROTATION'); c.name = 'VMRET_rot'; c.target = src; c.subtarget = n + HELPER_SUFFIX
        c.owner_space = 'WORLD'; c.target_space = 'WORLD'
        if n.startswith(FINGER_PREFIXES):
            c.influence = finger_influence
        if n == 'pelvis':
            c = pb.constraints.new('COPY_LOCATION'); c.name = 'VMRET_loc'; c.target = src; c.subtarget = n + HELPER_SUFFIX
            c.owner_space = 'WORLD'; c.target_space = 'WORLD'
    bound_sockets = []
    for n in sockets:
        if n not in dst.pose.bones or n not in src.pose.bones:
            continue
        # rotation only: the socket's location rides the skeleton's own hand (its rest offset was taken from
        # Manny's socket-to-joint offset), so limb-length differences cannot float the prop off the hand
        pb = dst.pose.bones[n]
        c = pb.constraints.new('COPY_ROTATION'); c.name = 'VMRET_rot'; c.target = src; c.subtarget = n
        c.owner_space = 'WORLD'; c.target_space = 'WORLD'
        bound_sockets.append(n)
    if root_motion:
        if 'root' not in dst.pose.bones:
            raise RuntimeError('--root-motion needs a `root` bone on the target rig')
        pb = dst.pose.bones['root']
        c = pb.constraints.new('COPY_LOCATION'); c.name = 'VMRET_loc'; c.target = src; c.subtarget = ''
        c.owner_space = 'WORLD'; c.target_space = 'WORLD'
    src.data.pose_position = 'POSE'; bpy.context.view_layer.update()
    return bound_sockets


def report(dst, src, frames):
    scene = bpy.context.scene
    for f in frames:
        scene.frame_set(f); bpy.context.view_layer.update()
        parts = []
        for name, a, b in (('uarm_l', 'upperarm_l', 'lowerarm_l'), ('larm_l', 'lowerarm_l', 'hand_l'), ('hand_l', 'hand_l', 'middle_01_l'),
                           ('uarm_r', 'upperarm_r', 'lowerarm_r'), ('larm_r', 'lowerarm_r', 'hand_r'), ('hand_r', 'hand_r', 'middle_01_r'),
                           ('spine', 'pelvis', 'spine_03'), ('neck', 'neck_01', 'head'), ('thigh_l', 'thigh_l', 'calf_l')):
            parts.append('%s %.1f' % (name, deg(wdir(src, a, b), wdir(dst, a, b))))
        hs = whead(src, 'hand_r') - whead(src, 'head'); hd = whead(dst, 'hand_r') - whead(dst, 'head')
        print('  f%03d %s | hand_r-head gap %.3f m' % (f, '  '.join(parts), (hs - hd).length))


def _pair_name(name):
    if name.endswith('_l'): return name[:-2] + '_r'
    if name.endswith('_r'): return name[:-2] + '_l'
    return name


def mirror_pose(dst, f0, f1):
    """Post-bake: swap left and right across the armature's X=0 plane, so the pack's right-hand sword
    becomes a left-hand sword. Mirrors each bone's DELTA from rest (pose @ rest^-1), not its absolute frame:
    the Synty rig's left and right rest frames are not exact mirror images, so mirroring absolute frames
    would twist one side. Unpaired bones (pelvis, spine, head, root) mirror onto themselves."""
    scene = bpy.context.scene
    S = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
    bones = list(dst.pose.bones)
    rest = {pb.name: pb.bone.matrix_local.copy() for pb in bones}
    rest_inv = {n: m.inverted() for n, m in rest.items()}
    # parents first
    def depth(pb):
        d = 0; p = pb.parent
        while p is not None: d += 1; p = p.parent
        return d
    ordered = sorted(bones, key=depth)
    for pb in bones:
        pb.rotation_mode = 'QUATERNION'
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        pose = {pb.name: pb.matrix.copy() for pb in bones}
        target = {}
        for pb in bones:
            src = _pair_name(pb.name)
            if src not in pose: src = pb.name
            target[pb.name] = S @ pose[src] @ rest_inv[src] @ S @ rest[pb.name]
        for pb in ordered:
            t = target[pb.name]
            if pb.parent is not None:
                parent_pose = target[pb.parent.name]
                basis = (parent_pose @ rest_inv[pb.parent.name] @ rest[pb.name]).inverted() @ t
            else:
                basis = rest_inv[pb.name] @ t
            pb.matrix_basis = basis
            pb.keyframe_insert('location', frame=f)
            pb.keyframe_insert('rotation_quaternion', frame=f)


def relax_fingers(dst, f0, f1, factor, pinky_follows_ring=True):
    """Post-bake: scale every finger bone's local rotation (relative to the Synty rest, which is a nearly
    open hand) toward identity by `factor`. Constraint influence alone cannot do this: a child finger bone
    rides its parent's curl, so the middle joints stayed as tight as Manny's fist.
    pinky_follows_ring: Manny's pinky sits on its own splayed metacarpal, so the copied pinky curls
    sideways into the palm instead of around the handle; giving it the ring finger's joint rotations
    makes it curl like the other fingers (the Synty rest orientations of the two fingers match)."""
    scene = bpy.context.scene
    fingers = [pb for pb in dst.pose.bones if pb.name.startswith(FINGER_PREFIXES)]
    identity = Quaternion((1.0, 0.0, 0.0, 0.0))
    for pb in fingers:
        pb.rotation_mode = 'QUATERNION'
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        values = {}
        for pb in fingers:
            values[pb.name] = identity.slerp(pb.rotation_quaternion.copy(), factor)
        if pinky_follows_ring:
            for name in list(values):
                if name.startswith('pinky_'):
                    ring = name.replace('pinky_', 'ring_')
                    if ring in values:
                        values[name] = values[ring].copy()
        for pb in fingers:
            pb.rotation_quaternion = values[pb.name]
            pb.keyframe_insert('rotation_quaternion', frame=f)


# Source rigs that spell an Epic bone differently: Quaternius' Universal Animation Library capitalises the head.
SOURCE_RENAMES = {'Head': 'head'}


def select_take(src, take):
    """A multi-take FBX (UAL1.fbx carries 120 stacks) imports as one action per stack with the LAST one active:
    pick the action named `take` (or `<armature>|take`) and make it the clip."""
    if src.animation_data is None:
        src.animation_data_create()
    found = [a for a in bpy.data.actions if a.name == take or a.name.endswith('|' + take)]
    if not found:
        names = sorted(a.name for a in bpy.data.actions)
        raise RuntimeError('take %r not found; actions: %s' % (take, ', '.join(names[:40]) + (' ...' if len(names) > 40 else '')))
    act = found[0]
    src.animation_data.action = act
    # Blender 4.4+ slotted actions: the armature has to be told which slot of the action drives it
    if hasattr(src.animation_data, 'action_slot') and getattr(act, 'slots', None):
        try:
            src.animation_data.action_slot = act.slots[0]
        except Exception as e:  # noqa: BLE001 - slot already resolved by the assignment
            print('  (action slot not set:', e, ')')
    return act


def retarget_clip(rig_path, clip_path, out_path, pelvis_height_offset=0.0, do_report=False, finger_influence=1.0, finger_curl=1.0, mirror=False,
                  root_motion=False, sockets=(), take=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    dst, rig_objs = import_armature(rig_path)
    for o in rig_objs:
        if o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    src, _ = import_armature(clip_path)
    for old, new in SOURCE_RENAMES.items():
        if old in src.data.bones and new not in src.data.bones:
            src.data.bones[old].name = new
    if take:
        select_take(src, take)
    action = src.animation_data.action if src.animation_data else None
    if action is None:
        raise RuntimeError('no animation in ' + clip_path)
    f0, f1 = (int(round(x)) for x in action.frame_range)
    mapped = prepose(dst, src)
    bound_sockets = bind(dst, src, mapped, pelvis_height_offset, finger_influence, root_motion, sockets)
    if do_report:
        scene = bpy.context.scene
        print('== %s frames %d-%d at %g fps, %d bones bound, sockets %s' % (os.path.basename(clip_path), f0, f1,
              scene.render.fps / scene.render.fps_base, len(mapped), bound_sockets))
        scene.frame_set(f0); p0 = src.matrix_world.to_translation().copy()
        scene.frame_set(f1); p1 = src.matrix_world.to_translation().copy()
        print('  source root travel f%d->f%d: %s m' % (f0, f1, tuple(round(v, 3) for v in (p1 - p0))))
        report(dst, src, sorted({f0, (f0 + f1) // 4, (f0 + f1) // 2, (3 * (f0 + f1)) // 4, f1}))
    bake_and_export(dst, f0, f1, out_path, mirror=mirror, finger_curl=finger_curl)


def bake_and_export(dst, f0, f1, out_path, mirror=False, finger_curl=1.0, post_bake=None):
    """Bake the constrained target pose to keys (visual keying, constraints cleared), apply the optional post
    passes, and export the armature alone as an animation FBX (cm, Y-up, the scene's fps, same layout as
    Assets/Animations/Magic). Shared by the Manny retarget above, tools/retarget_synty.py and
    tools/retarget_synty_to_rig.py. `post_bake(dst, f0, f1)` runs on the baked keys before the export (that tool
    rewrites the pelvis track and renames the bones back)."""
    select_only(dst); bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.nla.bake(frame_start=f0, frame_end=f1, step=1, only_selected=False, visual_keying=True,
                     clear_constraints=True, clear_parents=False, use_current_action=False, bake_types={'POSE'})
    if post_bake is not None:
        post_bake(dst, f0, f1)
    if mirror:
        mirror_pose(dst, f0, f1)
    if finger_curl < 0.999:
        relax_fingers(dst, f0, f1, finger_curl)
    bpy.ops.object.mode_set(mode='OBJECT')
    dst.animation_data.action.name = os.path.splitext(os.path.basename(out_path))[0]
    bpy.context.scene.frame_start = f0; bpy.context.scene.frame_end = f1
    select_only(dst)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=out_path, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
    print('wrote', out_path, 'frames', f0, f1)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--rig', required=True, help='FBX carrying the Synty skeleton (e.g. Assets/CharacterToon/human_torso.fbx)')
    ap.add_argument('--out', required=True, help='output folder')
    ap.add_argument('--pelvis-height-offset', type=float, default=0.0)
    ap.add_argument('--report', action='store_true')
    ap.add_argument('--finger-influence', type=float, default=1.0,
                    help='0..1 constraint influence on finger bones (rarely useful, see --finger-curl)')
    ap.add_argument('--finger-curl', type=float, default=0.65,
                    help='0..1 scale of every finger joint\'s rotation after baking (1 = Manny\'s tight fist)')
    ap.add_argument('--mirror', dest='mirror', action='store_true', default=True,
                    help='swap left/right so the sword ends up in the LEFT hand (default)')
    ap.add_argument('--no-mirror', dest='mirror', action='store_false')
    ap.add_argument('--root-motion', action='store_true',
                    help='copy the clip armature\'s (Manny root) travel onto the target `root` bone instead of folding it into the pelvis')
    ap.add_argument('--sockets', default='',
                    help='comma list of free bones present on both rigs that copy Manny\'s socket outright (weapon_r,shield_l,weapon_l)')
    ap.add_argument('--strip-prefix', default='', help='drop this prefix from the output name (SSH_)')
    ap.add_argument('--strip-suffix', default='', help='drop this suffix from the output name (_v001)')
    ap.add_argument('--take', default='', help='name of the take (action) to use out of a multi-take FBX, e.g. Drink from UAL1.fbx')
    ap.add_argument('--name', default='', help='output clip name (default: the source file name; with --take, the take name)')
    ap.add_argument('clips', nargs='+', help='clip FBX files or globs (** recurses)')
    args = ap.parse_args(argv)
    clips = []
    for pattern in args.clips:
        clips += sorted(glob.glob(pattern, recursive=True)) or [pattern]
    sockets = [s for s in args.sockets.split(',') if s]
    if args.name and len(clips) != 1:
        raise RuntimeError('--name takes exactly one clip')
    names = {}
    for clip in clips:
        name = args.name or args.take or os.path.splitext(os.path.basename(clip))[0]
        if args.strip_prefix and name.startswith(args.strip_prefix):
            name = name[len(args.strip_prefix):]
        if args.strip_suffix and name.endswith(args.strip_suffix):
            name = name[:-len(args.strip_suffix)]
        name = name.lower().replace('-', '_')
        if name in names:
            raise RuntimeError('duplicate output name %s from %s and %s' % (name, names[name], clip))
        names[name] = clip
    for name, clip in names.items():
        retarget_clip(os.path.abspath(args.rig), os.path.abspath(clip), os.path.join(os.path.abspath(args.out), name + '.fbx'),
                      args.pelvis_height_offset, args.report, args.finger_influence, args.finger_curl, args.mirror,
                      root_motion=args.root_motion, sockets=sockets, take=args.take or None)


if __name__ == '__main__':
    main()
