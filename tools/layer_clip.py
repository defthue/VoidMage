"""Layer one clip's bones over another clip, both already on the same rig (baked by retarget_fpp.py), and
write the result as a new animation-only FBX. Built for the UAL `Drink` take: the source animates the RIGHT
arm only and holds the T-pose everywhere else (verified on a contact sheet, tools/film_fbx_clip.py), so
played whole it would swing the sword arm out sideways. Here the right-arm chain comes from the drink and
every other bone from the first-person idle (anim_idle_breathing, looped), and the T-pose lead-in / lead-out
frames are trimmed off. Output keys start at frame 1 like every other clip in Assets/Animations.

Usage (Blender 5.0):
  blender -b --factory-startup --python tools/layer_clip.py -- --base Assets/Animations/SwordFP/anim_idle_breathing.fbx
      --overlay <retargeted drink>.fbx --out Assets/Animations/SwordFP/drink.fbx --bones clavicle_r --start 9 --end 76
--bones: comma list of bone names; each one's whole subtree is taken from the overlay (clavicle_r = the arm,
         the hand and every finger). --start/--end: overlay frames kept (inclusive, the overlay's own numbering).

--hand-at fixes the other half of a rotation-only retarget: the source's arm reached ITS head, and on a rig with
different proportions the same joint angles put the hand somewhere else entirely (measured on the drink: the hand
ended up 11 cm BEHIND the eyes, beside the temple, instead of at the mouth, which is what made it look like the arm
went through the head). It re-solves the arm each frame so the hand sits at a point given relative to the eyes, with
the elbow swung down (--elbow-down) and the fist turned so what it holds points at the mouth (--hand-aim); all of it
fades out with distance from the head (--hand-near/--hand-far), so only the gesture at the face is touched.
"""
import bpy, sys, os, argparse, math
from mathutils import Vector, Quaternion

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'tools'))
from retarget_fpp import import_armature, bake_and_export, select_only, rotate_world, whead  # noqa: E402
from measure_sword_tip import sword_grip  # noqa: E402  (the game's SwordGrip.Compute, ported)


def subtree(arm, roots):
    names = set()
    for r in roots:
        if r not in arm.pose.bones:
            raise RuntimeError('bone %s not on the rig' % r)
        stack = [arm.pose.bones[r]]
        while stack:
            pb = stack.pop()
            names.add(pb.name)
            stack.extend(pb.children)
    return names


def body_forward_local(arm, frame):
    """The direction the CHARACTER faces, as a vector in the pelvis's local space.

    Not the head's own forward: the head tilts back to drink, and neither view that matters tilts with it — the
    third-person body keeps its own head (UpperBodyPoseCopy leaves neck and head to the locomotion and the head
    look) and the first-person camera is the player's aim. So the hand has to clear the head as the head actually
    is, which is upright. Read off the feet at the rest frame (up = pelvis to head), then carried per frame by the
    pelvis, so a clip that turns the body turns this with it."""
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    M = arm.matrix_world @ arm.pose.bones['pelvis'].matrix
    up = (whead(arm, 'head') - whead(arm, 'pelvis')).normalized()
    f = whead(arm, 'ball_l') - whead(arm, 'foot_l')
    f -= up * f.dot(up)
    if f.length < 1e-6:
        raise RuntimeError('cannot read a facing direction from the foot bones')
    return M.to_3x3().inverted() @ f.normalized()


def solve_arm(arm, side, T, up, down_weight, exact_wrist=False):
    """Two-bone IK: turn the upper and lower arm so the hand's head lands on world point `T`; with `exact_wrist` the
    wrist keeps its world rotation (so a held prop keeps its angle). The elbow stays in the plane the pose already
    used, turned toward straight down by `down_weight` (0..1). False when the target sits on the shoulder."""
    upper, lower, hand_b = 'upperarm_%s' % side, 'lowerarm_%s' % side, 'hand_%s' % side
    S, E, H = whead(arm, upper), whead(arm, lower), whead(arm, hand_b)
    L1, L2 = (E - S).length, (H - E).length
    u = (T - S)
    d = u.length
    if d < 1e-6:
        return False
    u = u / d
    d = max(abs(L1 - L2) + 1e-4, min(d, L1 + L2 - 1e-4))
    # The pole decides where the elbow goes on its circle: the plane the pose already used, turned toward
    # straight down as the correction takes hold, so the drinking elbow hangs instead of sticking out forward.
    def flatten(v):
        v = v - u * v.dot(u)
        return v.normalized() if v.length > 1e-6 else Vector((0.0, 0.0, 0.0))

    old_u = (H - S)
    old_u = old_u.normalized() if old_u.length > 1e-6 else u
    pole = flatten((E - S) - old_u * (E - S).dot(old_u))
    down = flatten(-up)
    pole = flatten(pole.lerp(down, down_weight) if pole.length > 1e-6 else down)
    if pole.length < 1e-6:
        pole = u.orthogonal().normalized()
    a = (d * d + L1 * L1 - L2 * L2) / (2.0 * d)
    h2 = max(0.0, L1 * L1 - a * a)
    E2 = S + u * a + pole * math.sqrt(h2)

    keep = (arm.matrix_world @ arm.pose.bones[hand_b].matrix).to_3x3()   # the wrist's world rotation is preserved
    rotate_world(arm, arm.pose.bones[upper], (E - S).normalized().rotation_difference((E2 - S).normalized()))
    E_now, H_now = whead(arm, lower), whead(arm, hand_b)
    rotate_world(arm, arm.pose.bones[lower], (H_now - E_now).normalized().rotation_difference((T - E_now).normalized()))
    pb = arm.pose.bones[hand_b]
    now = (arm.matrix_world @ pb.matrix).to_3x3().to_quaternion()
    if exact_wrist:
        rotate_world(arm, pb, keep.to_quaternion() @ now.inverted())
    else:
        # a.rotation_difference(b) is a LOCAL turn (a @ q = b) while rotate_world applies a world one, so this does
        # not really restore the wrist; the shipped drink (hand aim + wrist-in tuned in game) was built on it
        rotate_world(arm, pb, now.rotation_difference(keep.to_quaternion()))
    return True


def body_axes(arm, f_local):
    """This frame's body forward, left and up in world space (f_local from body_forward_local)."""
    M = arm.matrix_world @ arm.pose.bones['pelvis'].matrix
    f = (M.to_3x3() @ f_local).normalized()
    up = (whead(arm, 'head') - whead(arm, 'pelvis')).normalized()
    return f, up.cross(f).normalized(), up


def place_hand(arm, side, f_local, target, near, far, elbow_down, aim=None, shoulder_drop=0.0, wrist_in=0.0):
    """Put this frame's hand at `target` (metres from the eye midpoint, in the body's own forward/left/up) and
    re-solve the arm for it, keeping the elbow low.

    This is the other half of a rotation-only retarget. The source's arm reached the SOURCE's head; the Synty arm
    is longer against its own head, so the same joint angles carry the hand past the mouth to beside the ear (the
    drink measured 11 cm behind the eyes). Moving the hand alone is not enough — the elbow follows it up and the
    arm reads as a salute — so the elbow is also swung down under the shoulder-to-hand line.

    Both corrections fade out between `near` and `far` metres of hand-to-head distance, so the arm hanging at the
    side is left exactly as animated and the fix eases in and out with the gesture. Returns (before, after) as the
    hand's distance from the eyes in metres."""
    upper, lower, hand_b = 'upperarm_%s' % side, 'lowerarm_%s' % side, 'hand_%s' % side
    M = arm.matrix_world @ arm.pose.bones['pelvis'].matrix
    f = (M.to_3x3() @ f_local).normalized()
    up = (whead(arm, 'head') - whead(arm, 'pelvis')).normalized()
    left = up.cross(f).normalized()
    eye = (whead(arm, 'eye_l') + whead(arm, 'eye_r')) * 0.5

    S, E, H = whead(arm, upper), whead(arm, lower), whead(arm, hand_b)
    before = (H - eye).length
    t = (H - whead(arm, 'head')).length
    w = 1.0 if t <= near else 0.0 if t >= far else (far - t) / (far - near)
    w = w * w * (3.0 - 2.0 * w)       # smoothstep: no kink where the fade starts or ends
    if w <= 1e-3:
        return before, before
    ideal = eye + f * target[0] + left * target[1] + up * target[2]
    T = H.lerp(ideal, w)
    if not solve_arm(arm, side, T, up, w * max(0.0, min(1.0, elbow_down))):
        return before, before
    pb = arm.pose.bones[hand_b]

    if aim is not None:
        # Turn the fist so what it holds points at the mouth. The clip's own wrist never tips the vessel (the
        # source drank from something much smaller), and the potion is a 50 cm flask: without this the neck ends
        # up pointing at the ceiling whatever the arm does. The axis is the one the game's grip puts a prop's
        # length along (SwordGrip: the knuckle line, flattened against the palm).
        at = eye + f * aim[0] + left * aim[1] + up * aim[2]
        names = ('hand_%s', 'index_01_%s', 'pinky_01_%s', 'middle_01_%s', 'thumb_01_%s')
        joints = [whead(arm, n % side) for n in names]
        pos, _, _, blade = sword_grip(*joints, (0.0, 0.0, 0.0), (0.0, 0.0, 0.0))
        want = (at - pos)
        if want.length > 1e-6:
            turn = blade.rotation_difference(want.normalized())
            rotate_world(arm, pb, Quaternion().slerp(turn, w))

    if shoulder_drop:
        # The last word, as the user put it: "rotate his arm from the shoulder downwards". The whole arm swings
        # down about the shoulder joint as one piece (the fist and the flask turn with it), around the body's own
        # left axis, faded like everything else so the lift and the flick keep their motion.
        S = whead(arm, upper)
        q = Quaternion(left, math.radians(shoulder_drop) * w)
        if (q @ (whead(arm, hand_b) - S)).dot(up) > (whead(arm, hand_b) - S).dot(up):
            q = Quaternion(left, -math.radians(shoulder_drop) * w)   # whichever way takes the hand DOWN
        rotate_world(arm, arm.pose.bones[upper], q)

    if wrist_in:
        # "his wrist rotated slightly towards the face/body": the fist turns so the top of what it holds tips toward
        # the mouth, about the axis at right angles to both the held prop's length and the way to the mouth, by this
        # many degrees at most. After the shoulder drop, so the drop does not undo it; faded like the rest.
        mouth = eye + up * -0.06
        names = ('hand_%s', 'index_01_%s', 'pinky_01_%s', 'middle_01_%s', 'thumb_01_%s')
        pos, _, _, blade = sword_grip(*[whead(arm, n % side) for n in names], (0.0, 0.0, 0.0), (0.0, 0.0, 0.0))
        to_face = mouth - pos
        axis = blade.cross(to_face)
        if axis.length > 1e-6 and to_face.length > 1e-6:
            gap = math.degrees(blade.angle(to_face.normalized()))
            turn = (min(wrist_in, gap) if wrist_in > 0 else wrist_in) * w     # toward: never past pointing straight at the mouth
            rotate_world(arm, arm.pose.bones[hand_b], Quaternion(axis.normalized(), math.radians(turn)))

    return before, (whead(arm, hand_b) - eye).length


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', required=True)
    ap.add_argument('--overlay', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--bones', required=True)
    ap.add_argument('--start', type=int, required=True)
    ap.add_argument('--end', type=int, required=True)
    ap.add_argument('--hand-at', default='',
                    help='put the hand here while it is up at the head: "fwd,left,up" metres from the eye midpoint, '
                         'in the BODY\'s axes (a drink wants it in front of and below the eyes). Empty = leave the arm alone.')
    ap.add_argument('--hand-side', default='r', choices=['l', 'r'])
    ap.add_argument('--hand-near', type=float, default=0.26, help='hand-to-head distance (m) at which the correction is full')
    ap.add_argument('--hand-far', type=float, default=0.46, help='and where it has faded out entirely')
    ap.add_argument('--hand-aim', default='', help='turn the fist so what it holds points at this "fwd,left,up" point (metres from the eyes), e.g. the mouth')
    ap.add_argument('--wrist-in', type=float, default=0.0, help='degrees the fist turns while at the head, after the shoulder drop: positive tips the length of the held prop toward the mouth as measured here, negative the other way (in game the drink wanted -15)')
    ap.add_argument('--shoulder-drop', type=float, default=0.0, help='degrees the whole arm is swung down about the shoulder while at the head, after everything else')
    ap.add_argument('--elbow-down', type=float, default=1.0, help='0..1 how far the elbow is swung under the arm while corrected')
    ap.add_argument('--debug', action='store_true', help='print the hand-to-eye distance before and after per sampled frame')
    ap.add_argument('--follow-hand', default='',
                    help='"fwd,left,up" scales: instead of copying the overlay\'s joint angles, the base arm (hand_side) is re-solved '
                         'each frame so its hand moves the way the overlay\'s hand moves from its first kept frame, in the body\'s '
                         'axes and scaled per axis, wrist rotation and fingers untouched. For a gesture made with a hand that '
                         'is already holding something in a different pose (the pickup reach on the staff arm); --bones is ignored.')
    args = ap.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    base, _ = import_armature(os.path.abspath(args.base))
    over, _ = import_armature(os.path.abspath(args.overlay))
    base_act = base.animation_data.action
    over_act = over.animation_data.action
    b0, b1 = (int(round(x)) for x in base_act.frame_range)
    o0, o1 = (int(round(x)) for x in over_act.frame_range)
    s, e = max(args.start, o0), min(args.end, o1)
    if e <= s:
        raise RuntimeError('empty range %d..%d inside overlay %d..%d' % (args.start, args.end, o0, o1))
    layered = subtree(over, [b for b in args.bones.split(',') if b])
    print('base %s frames %d-%d, overlay %s frames %d-%d, keeping %d-%d, %d layered bones' % (
        os.path.basename(args.base), b0, b1, os.path.basename(args.overlay), o0, o1, s, e, len(layered)))

    scene = bpy.context.scene
    n = e - s + 1
    base_len = max(1, b1 - b0 + 1)
    # sample both clips first (reading a pose changes nothing), then write the result onto the base rig
    samples = []
    follow = tuple(float(v) for v in args.follow_hand.split(',')) if args.follow_hand else None
    if follow is not None and len(follow) != 3:
        raise RuntimeError('--follow-hand wants three scales: fwd,left,up')
    if follow is not None:
        layered = set()
    over_f_local = body_forward_local(over, s) if follow is not None else None
    reach = []
    for i in range(n):
        f_over = s + i
        f_base = b0 + (i % base_len)
        scene.frame_set(f_over)
        bpy.context.view_layer.update()
        pose_over = {pb.name: pb.matrix_basis.copy() for pb in over.pose.bones}
        if follow is not None:
            fo, lo, uo = body_axes(over, over_f_local)
            h = whead(over, 'hand_%s' % args.hand_side) - whead(over, 'pelvis')
            reach.append(Vector((h.dot(fo), h.dot(lo), h.dot(uo))))
        scene.frame_set(f_base)
        bpy.context.view_layer.update()
        pose_base = {pb.name: pb.matrix_basis.copy() for pb in base.pose.bones}
        samples.append((pose_over, pose_base))

    for pb in base.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    target = tuple(float(v) for v in args.hand_at.split( ',' )) if args.hand_at else None
    if target is not None and len( target ) != 3:
        raise RuntimeError('--hand-at wants three numbers: fwd,left,up in metres')
    aim = tuple(float(v) for v in args.hand_aim.split(',')) if args.hand_aim else None
    if aim is not None and len(aim) != 3:
        raise RuntimeError('--hand-aim wants three numbers: fwd,left,up in metres')
    f_local = body_forward_local(base, b0) if target is not None else None
    f_base_local = body_forward_local(base, b0) if follow is not None else None

    # Pass one, with NO action on the rig: posing by hand and then asking for world positions (the IK needs them)
    # makes Blender re-evaluate the armature, and an assigned action would overwrite the pose we just set with
    # whatever it holds at the scene's current frame. Unbound, the hand-set pose survives the update.
    base.animation_data.action = None
    fixed = 0
    worst = 0.0
    poses = []
    for i, (pose_over, pose_base) in enumerate(samples):
        for pb in base.pose.bones:
            src = pose_over if pb.name in layered and pb.name in pose_over else pose_base
            pb.matrix_basis = src[pb.name]
        if follow is not None:
            bpy.context.view_layer.update()
            fb, lb, ub = body_axes(base, f_base_local)
            d = reach[i] - reach[0]
            hand = whead(base, 'hand_%s' % args.hand_side)
            solve_arm(base, args.hand_side, hand + fb * d.x * follow[0] + lb * d.y * follow[1] + ub * d.z * follow[2], ub, 0.0, exact_wrist=True)
            if args.debug and (i % 3 == 0 or i == n - 1):
                print('   f%03d reach fwd %.3f left %.3f up %.3f m' % (1 + i, d.x * follow[0], d.y * follow[1], d.z * follow[2]))
        if f_local is not None:
            bpy.context.view_layer.update()
            before, after = place_hand(base, args.hand_side, f_local, target, args.hand_near, args.hand_far, args.elbow_down, aim, args.shoulder_drop, args.wrist_in)
            if abs( after - before ) > 1e-4:
                fixed += 1
                worst = max(worst, abs( after - before ))
            if args.debug and (i % 6 == 0 or i == n - 1):
                print('   f%03d hand-to-eye %.3f -> %.3f m' % (1 + i, before, after))
        poses.append({pb.name: pb.matrix_basis.copy() for pb in base.pose.bones})
    if f_local is not None:
        print('hand placed at %s (m from the eyes): %d of %d frames corrected, biggest move %.3f m (%.1f in)' % (
            args.hand_at, fixed, n, worst, worst * 39.3701))

    # Pass two: write the finished poses as keys, with nothing between setting a value and keying it
    out_act = bpy.data.actions.new(os.path.splitext(os.path.basename(args.out))[0])
    base.animation_data.action = out_act
    if hasattr(base.animation_data, 'action_slot'):
        try:
            slot = out_act.slots.new(base.id_type, base.name) if hasattr(out_act, 'slots') else None
            if slot is not None:
                base.animation_data.action_slot = slot
        except Exception as ex:  # noqa: BLE001
            print('  (slot:', ex, ')')
    for i, pose in enumerate(poses):
        f = 1 + i
        for pb in base.pose.bones:
            pb.matrix_basis = pose[pb.name]
            pb.keyframe_insert('location', frame=f)
            pb.keyframe_insert('rotation_quaternion', frame=f)
            pb.keyframe_insert('scale', frame=f)
    select_only(base)
    bake_and_export(base, 1, n, os.path.abspath(args.out), mirror=False, finger_curl=1.0)
    if follow is not None:
        print('layered clip: %d frames (overlay %d..%d), the base pose with hand_%s following the overlay hand x %s' % (n, s, e, args.hand_side, args.follow_hand))
    else:
        print('layered clip: %d frames (overlay %d..%d), right-arm bones from the overlay, the rest from the idle' % (n, s, e))


if __name__ == '__main__':
    main()
