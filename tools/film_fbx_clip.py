"""Contact sheet of a retargeted clip, so a frame can be picked by LOOKING (the boss_film rule): every Nth
frame of an animation-only FBX on the Synty rig, rendered with Workbench from the side and the front, tiled
into one PNG with the frame number under each tile.

The clip FBX carries the armature only; the arm and torso pieces of Assets/CharacterToon are imported and
given the clip's action (they share the skeleton the clip was baked on), so what renders is the character.

Usage (Blender 5.0):
  "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup
      --python tools/film_fbx_clip.py -- --clip Assets/Animations/SwordFP/drink.fbx --out <dir>/drink_sheet.png
Options: --step 3 (every 3rd frame), --tile 200 (px), --views side,front (any of side, front, top, eye)
         eye = the first-person camera: on the head bone, looking along the body's forward, --fov 74, head and
         torso hidden, a red crosshair at screen centre
"""
import bpy, sys, os, argparse, math
import numpy as np
from mathutils import Vector, Matrix

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'tools'))
from measure_sword_tip import sword_grip  # noqa: E402  (the game's SwordGrip.Compute, ported)

INCH = 39.3701
GRIP_JOINTS = {'l': ['hand_l', 'index_01_l', 'pinky_01_l', 'middle_01_l', 'thumb_01_l'],
               'r': ['hand_r', 'index_01_r', 'pinky_01_r', 'middle_01_r', 'thumb_01_r']}
PIECES = ['human_torso.fbx', 'human_arm_upper_l.fbx', 'human_arm_upper_r.fbx', 'human_arm_lower_l.fbx',
          'human_arm_lower_r.fbx', 'human_hand_l.fbx', 'human_hand_r.fbx', 'human_head.fbx']


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arms = [o for o in new if o.type == 'ARMATURE']
    return arms[0] if arms else None, new


def assign(arm, action):
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action
    if hasattr(arm.animation_data, 'action_slot') and getattr(action, 'slots', None):
        try:
            arm.animation_data.action_slot = action.slots[0]
        except Exception:
            pass


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--clip', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--step', type=int, default=3)
    ap.add_argument('--tile', type=int, default=200)
    ap.add_argument('--views', default='side,front')
    ap.add_argument('--focus', default='', help='centre the framing on this bone (its average place over the clip), e.g. head')
    ap.add_argument('--zoom', type=float, default=1.0, help='tighter framing: 4 = a quarter of the default span (the pieces import at 1/100 scale, so this is a factor, not a length)')
    ap.add_argument('--prop', default='', help='a prop FBX to put in a hand every frame through the game\'s own grip maths (SwordGrip.Compute)')
    ap.add_argument('--prop-hand', default='r', choices=['l', 'r'])
    ap.add_argument('--prop-grip', default='1.6,1.2,-6', help='grip offset in INCHES, as the component property has it (x fingers, y palm, z blade)')
    ap.add_argument('--prop-rot', default='0,0,0', help='pitch,yaw,roll trim in degrees, as the component property has it')
    ap.add_argument('--fov', type=float, default=74.0, help='horizontal field of view of the `eye` view (the scene camera is 74)')
    args = ap.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    clip_arm, clip_objs = import_fbx(os.path.abspath(args.clip))
    if clip_arm is None or clip_arm.animation_data is None or clip_arm.animation_data.action is None:
        raise RuntimeError('no animated armature in ' + args.clip)
    action = clip_arm.animation_data.action
    f0, f1 = (int(round(x)) for x in action.frame_range)
    clip_arm.hide_render = True

    rigs = []
    body_meshes = []   # the head and torso: the first-person model has neither, so the `eye` view hides them
    for piece in PIECES:
        path = os.path.join(ROOT, 'Assets', 'CharacterToon', piece)
        if not os.path.exists(path):
            continue
        arm, objs = import_fbx(path)
        if arm is None:
            continue
        if piece in ('human_torso.fbx', 'human_head.fbx'):
            body_meshes += [o for o in objs if o.type == 'MESH']
        assign(arm, action)
        arm.hide_render = True   # bones do not render anyway; the meshes do
        rigs.append(arm)
    if not rigs:
        raise RuntimeError('no character pieces found under Assets/CharacterToon')
    ref = rigs[0]

    # the character's own axes from the skeleton (the pieces' meshes import in the FBX's raw Y-up space, so
    # world Z is not "up" here): up = pelvis->head, forward = the foot bone, right = forward x up
    scene.frame_set(f0)
    bpy.context.view_layer.update()
    def whead(n): return ref.matrix_world @ ref.pose.bones[n].head
    up = (whead('head') - whead('pelvis')).normalized()
    fwd = (whead('ball_l') - whead('foot_l'))
    fwd = (fwd - up * fwd.dot(up))
    fwd = fwd.normalized() if fwd.length > 1e-6 else up.orthogonal().normalized()
    right = fwd.cross(up).normalized()
    # frame on the meshes' world box (the pieces' object scale differs from the clip armature's)
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.hide_render:
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, w.x), min(lo.y, w.y), min(lo.z, w.z)))
            hi = Vector((max(hi.x, w.x), max(hi.y, w.y), max(hi.z, w.z)))
    centre = (lo + hi) * 0.5
    height = max(hi.z - lo.z, hi.x - lo.x, hi.y - lo.y) * 1.25
    if args.focus:
        # the average place of one bone over the clip: a fixed frame, so two renders of the same clip compare
        acc = Vector((0, 0, 0))
        count = 0
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            bpy.context.view_layer.update()
            acc += ref.matrix_world @ ref.pose.bones[args.focus].head
            count += 1
        centre = acc / max(1, count)
        scene.frame_set(f0)
        bpy.context.view_layer.update()
    height /= max(0.01, args.zoom)
    print('mesh box', tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi), 'head', tuple(round(v, 3) for v in whead('head')))

    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0.75, 0.72, 0.68)
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = args.tile
    scene.render.resolution_y = args.tile
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('w'); scene.world = world
    world.color = (0.12, 0.12, 0.14)

    cam_data = bpy.data.cameras.new('cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = height
    cam = bpy.data.objects.new('cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam

    def aim(dir_to_cam, cam_up):
        from mathutils import Matrix
        cam.location = centre + dir_to_cam * 5.0
        look = (centre - cam.location).normalized()
        y = (cam_up - look * cam_up.dot(look)).normalized()
        x = y.cross(-look).normalized()   # camera X = Y x Z with Z = -look
        m = Matrix((x, y, -look)).transposed()
        cam.rotation_euler = m.to_euler()

    # the prop: imported once and moved into the hand each frame, exactly the way the game places it
    prop_objs = []
    if args.prop:
        _, prop_objs = import_fbx(os.path.abspath(args.prop))
        prop_objs = [o for o in prop_objs if o.type == 'MESH' and o.parent is None] or [o for o in prop_objs if o.type == 'MESH']
        # the pack's props are modelled in centimetres; the pieces here are at 1/100, so a raw import matches
        for o in prop_objs:
            o.parent = None
    # How big a real metre is in this scene: the piece armatures import at their own scale (their object scale is
    # NOT the answer — it reads 0.0001 while the rig measures 1/100), so take it from the rig itself, against the
    # clip's armature which is in the model's own metres.
    def spine_of(o):
        return ((o.matrix_world @ o.pose.bones['head'].head) - (o.matrix_world @ o.pose.bones['pelvis'].head)).length
    unit = spine_of(ref) / max(1e-9, spine_of(clip_arm))
    if prop_objs:
        lo2 = Vector((1e9, 1e9, 1e9)); hi2 = Vector((-1e9, -1e9, -1e9))
        for o in prop_objs:
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                lo2 = Vector((min(lo2.x, w.x), min(lo2.y, w.y), min(lo2.z, w.z)))
                hi2 = Vector((max(hi2.x, w.x), max(hi2.y, w.y), max(hi2.z, w.z)))
        print('prop imported %s m; one game metre is %.5f here, so the prop renders %s m beside a %.5f m spine' % (
            tuple(round(v, 3) for v in (hi2 - lo2)), unit,
            tuple(round(v * unit, 5) for v in (hi2 - lo2)), spine_of(ref)))
    grip = tuple(float(v) for v in args.prop_grip.split(','))
    prop_rot = tuple(float(v) for v in args.prop_rot.split(','))
    joints = GRIP_JOINTS[args.prop_hand]
    prop_rest = [o.matrix_world.copy() for o in prop_objs]

    def place_prop():
        if not prop_objs:
            return
        pts = [ref.matrix_world @ ref.pose.bones[n].head for n in joints]
        # the grip offset is in game inches; both it and the prop are brought into this scene's units
        g = tuple(v / INCH * unit for v in grip)
        pos, x, y, z = sword_grip(*pts, g, prop_rot)
        basis = Matrix((x, y, z)).transposed().to_4x4()
        basis.translation = pos
        for o, rest in zip(prop_objs, prop_rest):
            o.matrix_world = basis @ Matrix.Scale(unit, 4) @ rest

    # The `eye` view is what the first-person camera sees: FirstPersonArms pins the animated HEAD BONE to the camera
    # (ViewOffset 0) and turns the rig with the view, so the camera sits on the head bone every frame and looks along
    # the body's forward. Perspective, the scene camera's horizontal FOV, a near plane of 2 game inches.
    eye_data = bpy.data.cameras.new('eye'); eye_data.type = 'PERSP'
    eye_data.sensor_fit = 'HORIZONTAL'; eye_data.angle = math.radians(args.fov)
    eye_data.clip_start = 2.0 / INCH * unit; eye_data.clip_end = 3.0 * unit
    eye_cam = bpy.data.objects.new('eye', eye_data); scene.collection.objects.link(eye_cam)
    eye_rot = Matrix((right, up, -fwd)).transposed().to_euler()   # camera looks down its -Z, Y up, X right

    def place_eye():
        eye_cam.location = ref.matrix_world @ ref.pose.bones['head'].head
        eye_cam.rotation_euler = eye_rot

    views = [v for v in args.views.split(',') if v]
    frames = list(range(f0, f1 + 1, max(1, args.step)))
    if frames[-1] != f1:
        frames.append(f1)
    tmp = os.path.join(os.path.dirname(os.path.abspath(args.out)), '_film_tmp')
    os.makedirs(tmp, exist_ok=True)
    tiles = {}
    for v in views:
        eye = v == 'eye'
        scene.camera = eye_cam if eye else cam
        for o in body_meshes:
            o.hide_render = eye
        if v == 'side':
            aim(right, up)
        elif v == 'front':
            aim(fwd, up)
        elif v == 'top':
            aim(up, fwd)
        for f in frames:
            scene.frame_set(f)
            bpy.context.view_layer.update()
            place_prop()
            if eye:
                place_eye()
            path = os.path.join(tmp, '%s_%03d.png' % (v, f))
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            tiles[(v, f)] = path

    # tile: one row block per view, `cols` tiles wide, frame number burnt in as a dotted count at the tile's bottom
    cols = min(len(frames), 10)
    rows_per_view = int(math.ceil(len(frames) / cols))
    T = args.tile
    W = cols * T
    H = len(views) * rows_per_view * T
    sheet = np.zeros((H, W, 4), dtype=np.float32)
    sheet[..., 3] = 1.0
    for vi, v in enumerate(views):
        for i, f in enumerate(frames):
            img = bpy.data.images.load(tiles[(v, f)])
            px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
            bpy.data.images.remove(img)
            r = vi * rows_per_view + i // cols
            c = i % cols
            y0 = H - (r + 1) * T; x0 = c * T
            # frame label: a bar of `f // 10` long ticks and `f % 10` short ticks along the bottom edge
            tens, ones = divmod(f, 10)
            for k in range(tens):
                px[2:10, 4 + k * 8: 9 + k * 8, :3] = (1.0, 0.85, 0.2)
            for k in range(ones):
                px[2:6, 4 + tens * 8 + 4 + k * 5: 4 + tens * 8 + 7 + k * 5, :3] = (0.3, 0.9, 1.0)
            if v == 'eye':
                # the crosshair: screen centre, where the camera looks
                m = T // 2
                px[m - 1:m + 1, m - 12:m + 12, :3] = (1.0, 0.25, 0.25)
                px[m - 12:m + 12, m - 1:m + 1, :3] = (1.0, 0.25, 0.25)
            sheet[y0:y0 + T, x0:x0 + T] = px
    out = bpy.data.images.new('sheet', W, H, alpha=True)
    out.pixels = sheet.ravel().tolist()
    out.filepath_raw = os.path.abspath(args.out)
    out.file_format = 'PNG'
    out.save()
    print('wrote', args.out, 'frames', frames, 'views', views, '(label: long yellow ticks = tens, short cyan = ones)')


if __name__ == '__main__':
    main()
