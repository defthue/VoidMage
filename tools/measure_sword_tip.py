"""Measure how the sword tip moves in each first-person attack clip, to seed SwordSlash's per-clip
emission windows (Code/Character/SwordSlash.cs) from data instead of guesses.

For every clip it loads the retargeted FBX (Assets/Animations/SwordFP, already on the Synty rig with the
sword in the LEFT hand), reads the five left-hand joints per frame, rebuilds the sword grip exactly as
Code/Character/SwordGrip.cs does in game (same offsets, same pitch/yaw trim, blade along the grip's Up) and
follows the blade tip. Output per clip: tip speed per frame, the peak, a suggested emission window (the
contiguous frames around the peak faster than --threshold of it), the swept angle inside the window and
the dominant sweep direction relative to the shoulders. Frames are clip frames (0 = first exported key),
the numbers SwordSlash's Profiles table takes.

Usage (Blender 5.0):
  "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup
      --python tools/measure_sword_tip.py -- --out tools/out/sword_tip.json "Assets/Animations/SwordFP/anim_attack_*.fbx"
Options: --grip x,y,z and --rot pitch,yaw,roll (default = the player prefab's tuned sword grip),
         --blade <units> tip distance above the guard (28 for sm_wep_ornate_sword_02 at its 0.2 import scale),
         --threshold 0..1 window cut as a fraction of the 3-frame-averaged peak speed (0.25).
Units: Blender imports the cm files as metres; everything printed is in engine units (inches, x39.37).
The pack's clips alternate fast and slow frames (the source FBX does the same), hence the averaging.
"""
import bpy, sys, os, glob, math, json, argparse
from mathutils import Vector

INCH_PER_M = 39.3701
FPS = 30.0
HAND = ['hand_l', 'index_01_l', 'pinky_01_l', 'middle_01_l', 'thumb_01_l']


def import_armature(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arms = [o for o in new if o.type == 'ARMATURE']
    if not arms:
        raise RuntimeError('no armature in ' + path)
    return arms[0]


def whead(o, n):
    return (o.matrix_world @ o.pose.bones[n].head) * INCH_PER_M


# ------------------------------------------------------------------ s&box maths, ported
def quat_from_angles(pitch, yaw, roll):
    """Rotation.From(pitch, yaw, roll) verbatim (Source's ZYX Euler): returns (x, y, z, w)."""
    p = math.radians(pitch) * 0.5; y = math.radians(yaw) * 0.5; r = math.radians(roll) * 0.5
    sp, cp, sy, cy, sr, cr = math.sin(p), math.cos(p), math.sin(y), math.cos(y), math.sin(r), math.cos(r)
    srcp = sr * cp; crsp = cr * sp
    qx = srcp * cy - crsp * sy
    qy = crsp * cy + srcp * sy
    crcp = cr * cp; srsp = sr * sp
    qz = crcp * sy - srsp * cy
    qw = crcp * cy + srsp * sy
    return (qx, qy, qz, qw)


def quat_rotate(q, v):
    """q * v * q^-1 for a unit quaternion (x, y, z, w) and a 3-vector."""
    qx, qy, qz, qw = q
    u = Vector((qx, qy, qz))
    return u * (2.0 * u.dot(v)) + v * (qw * qw - u.dot(u)) + u.cross(v) * (2.0 * qw)


def sword_grip(hand, index, pinky, middle, thumb, grip, rot, flip_palm=False):
    """SwordGrip.Compute: (position at the guard, edge, left, blade) with the extra trim folded in."""
    fingers = (middle - hand).normalized()
    knuckles = (index - pinky).normalized()
    palm = fingers.cross(knuckles).normalized()
    if palm.dot(thumb - hand) < 0.0:
        palm = -palm
    if flip_palm:
        palm = -palm
    blade = (knuckles - palm * knuckles.dot(palm)).normalized()
    edge = palm.cross(blade).normalized()
    left = blade.cross(edge).normalized()    # Rotation.LookAt(edge, blade): X = edge, Z = blade, Y = Z x X
    position = hand + fingers * grip[0] + palm * grip[1] + blade * grip[2]
    # rotation * extra: the trim rotates the grip's own axes
    q = quat_from_angles(*rot)
    def local(v):
        return edge * v.x + left * v.y + blade * v.z
    x = local(quat_rotate(q, Vector((1.0, 0.0, 0.0))))
    y = local(quat_rotate(q, Vector((0.0, 1.0, 0.0))))
    z = local(quat_rotate(q, Vector((0.0, 0.0, 1.0))))
    return position, x, y, z


# ------------------------------------------------------------------ measurement
def measure(path, grip, rot, blade_len, start_frac, threshold):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    o = import_armature(path)
    missing = [n for n in HAND + ['clavicle_l', 'clavicle_r', 'head', 'pelvis'] if n not in o.pose.bones]
    if missing:
        raise RuntimeError('%s: missing bones %s' % (path, missing))
    action = o.animation_data.action if o.animation_data else None
    if action is None:
        raise RuntimeError('%s: no action' % path)
    f0, f1 = (int(round(v)) for v in action.frame_range)
    scene = bpy.context.scene
    scene.render.fps = int(FPS)

    rows = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f); bpy.context.view_layer.update()
        j = [whead(o, n) for n in HAND]
        pos, edge, left_axis, up = sword_grip(*j, grip, rot)
        tip = pos + up * blade_len
        inner = pos + up * (blade_len * start_frac)
        head = whead(o, 'head')
        shoulder_left = (whead(o, 'clavicle_l') - whead(o, 'clavicle_r')).normalized()
        char_up = (head - whead(o, 'pelvis')).normalized()
        rows.append(dict(frame=f - f0, pos=pos, tip=tip, inner=inner, up=up, head=head, left=shoulder_left, char_up=char_up))

    n = len(rows)
    speeds = [0.0] * n
    for i in range(1, n):
        speeds[i] = (rows[i]['tip'] - rows[i - 1]['tip']).length * FPS
    # The retargeted clips alternate fast and slow frames (the pack's own keys do too), so the window
    # is cut on a 3-frame average; the raw speeds are still printed.
    smooth = [0.0] * n
    for i in range(1, n):
        lo, hi = max(1, i - 1), min(n - 1, i + 1)
        smooth[i] = sum(speeds[lo:hi + 1]) / (hi - lo + 1)
    peak_i = max(range(n), key=lambda i: smooth[i])
    peak = smooth[peak_i]
    cut = peak * threshold
    a = peak_i
    while a - 1 >= 1 and smooth[a - 1] >= cut:
        a -= 1
    b = peak_i
    while b + 1 < n and smooth[b + 1] >= cut:
        b += 1
    # speeds[i] is the motion between frame i-1 and i: the window starts one frame earlier
    start, end = max(0, a - 1), b
    raw_peak_i = max(range(n), key=lambda i: speeds[i])

    arc = 0.0
    for i in range(start + 1, end + 1):
        arc += math.degrees(rows[i - 1]['up'].angle(rows[i]['up']))
    # The character's frame is the MODEL's fixed axes, not the shoulders: in game the rig is placed on the
    # view with its model axes (FirstPersonArms.PostCameraSetup), and the torso twists during a swing, so
    # shoulder-derived axes would rotate every arc by that twist. Blender import space: left +X, up +Z,
    # forward -Y (the shoulders and spine sit exactly on these at frame 0).
    left_ref = Vector((1.0, 0.0, 0.0)); world_up = Vector((0.0, 0.0, 1.0)); forward = Vector((0.0, -1.0, 0.0))
    # Direction from the guard's travel, not the tip's: a tip that turns through more than a half circle
    # ends up near where it started.
    delta = rows[end]['pos'] - rows[start]['pos']
    comps = {'left': delta.dot(left_ref), 'up': delta.dot(world_up), 'forward': delta.dot(forward)}
    axis = max(comps, key=lambda k: abs(comps[k]))
    sign = comps[axis] >= 0.0
    opposite = {'left': 'right', 'up': 'down', 'forward': 'back'}
    direction = axis if sign else opposite[axis]
    travel = sum((rows[i]['tip'] - rows[i - 1]['tip']).length for i in range(start + 1, end + 1))

    # The slash the player sees: inside the window, drop the slow lead-in and tail (under 30% of the raw
    # peak) and keep the final run of frames where the tip is in front of the eye (the windup pulls it
    # behind the head). Three view-space points (first, half-way by travel, last) define the crescent
    # SwordSlash plays: eye-relative inches, x forward, y left, z up.
    # Eye-relative: the game pins that frame's animated head on the eye, so each frame uses its own head.
    def to_view(row):
        rel = row['tip'] - row['head']
        return Vector((rel.dot(forward), rel.dot(left_ref), rel.dot(world_up)))
    raw_peak = speeds[raw_peak_i]
    lo, hi = start, end
    while lo < hi and speeds[lo] < 0.3 * raw_peak:
        lo += 1
    while hi > lo and speeds[hi] < 0.3 * raw_peak:
        hi -= 1
    lo = max(0, lo - 1)   # speeds[i] is the move into frame i: the run starts one frame earlier
    run_end = hi
    run_start = hi
    while run_start - 1 >= lo and to_view(rows[run_start - 1]).x >= 0.0:
        run_start -= 1
    while run_end - run_start < 2 and run_start > 0:
        run_start -= 1
    pts = [to_view(rows[i]) for i in range(run_start, run_end + 1)]
    cum = [0.0]
    for i in range(1, len(pts)):
        cum.append(cum[-1] + (pts[i] - pts[i - 1]).length)
    half = cum[-1] * 0.5
    mid_i = min(range(len(pts)), key=lambda i: abs(cum[i] - half))
    if mid_i == 0 or mid_i == len(pts) - 1:
        mid_i = len(pts) // 2
    # the whoosh: the fastest frame of the visible cut (SwordSounds.WhooshSeconds, half a frame early)
    cut_peak = max(range(run_start, run_end + 1), key=lambda i: speeds[i])
    slash = dict(start_frame=run_start, end_frame=run_end, peak_frame=cut_peak, whoosh_seconds=round(max(0.0, (cut_peak - 0.5) / FPS), 3),
                 p0=[round(c, 1) for c in pts[0]], p1=[round(c, 1) for c in pts[mid_i]], p2=[round(c, 1) for c in pts[-1]],
                 view_points=[[round(c, 1) for c in p] for p in pts])

    return dict(
        slash=slash,
        clip=os.path.splitext(os.path.basename(path))[0].lower(),
        frames=n, fps=FPS, peak_frame=peak_i, peak_speed=peak, raw_peak_frame=raw_peak_i, raw_peak_speed=speeds[raw_peak_i],
        window=[start, end], arc_degrees=arc, travel=travel, direction=direction,
        components={k: v for k, v in comps.items()},
        hand_head_gap=(rows[0]['pos'] - rows[0]['head']).length,
        frame0=dict(guard=list(rows[0]['pos']), tip=list(rows[0]['tip']), head=list(rows[0]['head'])),
        speeds=[round(s, 1) for s in speeds],
        tips=[[round(c, 2) for c in r['tip']] for r in rows],
    )


def print_report(res):
    print('== %s  %d frames  peak %.0f u/s at f%d (3-frame avg; raw %.0f at f%d)  window f%d-%d  arc %.0f deg  travel %.0f u  %s  (guard-head %.1f u)'
          % (res['clip'], res['frames'], res['peak_speed'], res['peak_frame'], res['raw_peak_speed'], res['raw_peak_frame'],
             res['window'][0], res['window'][1], res['arc_degrees'], res['travel'], res['direction'], res['hand_head_gap']))
    s = res['speeds']
    line = []
    for i, v in enumerate(s):
        mark = '*' if res['window'][0] <= i <= res['window'][1] else ' '
        line.append('%s%d:%.0f' % (mark, i, v))
    for k in range(0, len(line), 10):
        print('   ' + '  '.join(line[k:k + 10]))


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='', help='JSON output path')
    ap.add_argument('--grip', default='3,1.5,-0.3', help='SwordGripOffset (inches, fist frame: fingers, palm, blade)')
    ap.add_argument('--rot', default='-8,-15,0', help='SwordRotationOffset pitch,yaw,roll (degrees)')
    ap.add_argument('--flip-palm', action='store_true')
    ap.add_argument('--blade', type=float, default=28.0, help='tip distance above the guard (units)')
    ap.add_argument('--start', type=float, default=0.35, help='ribbon inner edge as a fraction of the blade')
    ap.add_argument('--threshold', type=float, default=0.25, help='window cut as a fraction of the (3-frame averaged) peak tip speed')
    ap.add_argument('clips', nargs='+', help='clip FBX files or globs')
    args = ap.parse_args(argv)
    grip = tuple(float(v) for v in args.grip.split(','))
    rot = tuple(float(v) for v in args.rot.split(','))
    clips = []
    for pattern in args.clips:
        clips += sorted(glob.glob(pattern, recursive=True)) or [pattern]

    results = []
    for clip in clips:
        res = measure(os.path.abspath(clip), grip, rot, args.blade, args.start, args.threshold)
        results.append(res)
        print_report(res)

    print('\n// SwordSlash.Profiles seed (clip frames at 30 fps; view-space points x fwd / y left / z up, inches)')
    for r in results:
        s = r['slash']
        def v(p): return 'new( %.0f, %.0f, %.0f )' % tuple(p)
        print('\t\t["%s"] = new( %d, %d, %s, %s, %s ),  // window f%d-%d, peak f%d %.0f u/s, %s'
              % (r['clip'], s['start_frame'], s['end_frame'], v(s['p0']), v(s['p1']), v(s['p2']),
                 r['window'][0], r['window'][1], r['raw_peak_frame'], r['raw_peak_speed'], r['direction']))
        print('\t\t\t// in front: ' + '  '.join('f%d(%.0f,%.0f,%.0f)' % ((s['start_frame'] + i,) + tuple(p)) for i, p in enumerate(s['view_points']))
              + '   whoosh f%d = %.2fs' % (s['peak_frame'], s['whoosh_seconds']))

    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, 'w') as fh:
            json.dump(dict(grip=grip, rot=rot, blade=args.blade, threshold=args.threshold, clips=results), fh, indent=1)
        print('wrote', args.out)


if __name__ == '__main__':
    main()
