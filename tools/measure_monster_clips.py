"""Measure every monster clip in Blender so the prefabs carry real numbers instead of guesses:
when each attack lands, how far it reaches, and how fast the walk/run/hop clips are actually travelling.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/measure_monster_clips.py -- [--kind bat]

Writes tools/out/monsters/<kind>_clips.json (read by gen_monster_prefabs.py). Everything is measured in
Blender units and seconds; the json also carries `units_per_bu` (= 100 x the vmdl scale, because the FBX
export puts the geometry in metres under a 100x node scale) so the generator can convert without guessing.

* **hit time** is the frame where the strike bone (the head, or the wasp's sting) is moving forward fastest
  relative to the root — the same "peak tip speed" rule the player's sword crescent uses.
* **reach** is how far that bone gets from the root, along the facing, over the clip: the attack's HitRange.
* **travel speed** for a walk/run/hop is the STANCE foot's backward speed relative to the root, medianed over
  the cycle. Every clip in these packs animates in place, so this is the only ground truth for how fast the
  body may move before the feet slide (the lesson the skeleton walk cycles taught).
* facing is derived per rig from where the head sits relative to the root, never assumed: these packs are
  not consistent about it (the bat faces -Y, the rat +Y).
"""
import bpy, sys, os, json, argparse, statistics
from mathutils import Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
import monster_kinds  # noqa: E402

# the bone whose peak forward speed IS the hit; the wasp stabs with its abdomen, everything else bites
STRIKE_BONE = {"wasp": "Sting"}
DEFAULT_STRIKE = ("Head", "head", "Face", "Nose")


def log(*a):
    print("[measure]", *a)


def action_slot(ad, action):
    ad.action = action
    if hasattr(ad, "action_slot") and ad.action_slot is None and getattr(action, "slots", None):
        ad.action_slot = action.slots[0]


def pick(bones, names):
    for n in names:
        if n in bones:
            return n
    return None


def sample(arm, bone, f0, f1):
    """World positions of one bone over the clip, and the root's, frame by frame."""
    out = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        out.append((arm.matrix_world @ arm.pose.bones[bone].head).copy())
    return out


def run(name):
    kind = monster_kinds.kind(name)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.open_mainfile(filepath=kind["blend"])
    for o in bpy.data.objects:
        if o.mode != "OBJECT":
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.mode_set(mode="OBJECT")
    arm = bpy.data.objects[kind["armature"]]
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    bones = arm.pose.bones
    root = next(b.name for b in arm.data.bones if b.parent is None)
    strike = pick(bones, (STRIKE_BONE.get(name),) if name in STRIKE_BONE else ()) or pick(bones, DEFAULT_STRIKE)
    feet = [b.name for b in arm.data.bones if "foot" in b.name.lower() or "feet" in b.name.lower()]

    # facing: where the head sits relative to the root in the REST pose, flattened. Guessing -Y (Blender's
    # usual front) would be wrong on half these rigs.
    head = pick(bones, DEFAULT_STRIKE) or strike
    rest = (arm.matrix_world @ arm.data.bones[head].head_local) - (arm.matrix_world @ arm.data.bones[root].head_local)
    forward = Vector((rest.x, rest.y, 0.0))
    forward = forward.normalized() if forward.length > 1e-4 else Vector((0.0, -1.0, 0.0))
    log("%s: root %s, strike bone %s, %d feet, facing %s" % (
        name, root, strike, len(feet), tuple(round(v, 2) for v in forward)))

    ad = arm.animation_data or arm.animation_data_create()
    clips = {}
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        clip = (action.name.split("_", 1)[1] if "_" in action.name else action.name).lower()
        action_slot(ad, action)
        f0, f1 = int(round(action.frame_range[0])), int(round(action.frame_range[1]))
        info = dict(frames=[f0, f1], seconds=round((f1 - f0) / fps, 3))

        origin = sample(arm, root, f0, f1)
        tip = sample(arm, strike, f0, f1)
        # forward reach and forward speed of the strike bone, relative to the root, in the facing frame
        reach = [(tip[i] - origin[i]).dot(forward) for i in range(len(tip))]
        speed = [(reach[i] - reach[i - 1]) * fps for i in range(1, len(reach))]
        if speed:
            # THE HIT IS MAXIMUM EXTENSION, not peak speed. The player's sword lands at peak tip speed because it
            # is a cut; every one of these is a BITE or a sting, which connects when the jaws are furthest out.
            # Measured both ways they are far apart: the spider's head peaks in speed at 0.03 bu of reach on the
            # way out and only touches anything at 0.66, half a clip later.
            far = max(range(len(reach)), key=lambda i: reach[i])
            peak = max(range(len(speed)), key=lambda i: speed[i])
            info["hit_frame"] = f0 + far
            info["hit_time"] = round(far / fps, 3)
            info["reach"] = round(reach[far], 4)
            info["peak_speed_time"] = round((peak + 1) / fps, 3)
            info["peak_speed"] = round(speed[peak], 3)
            info["reach_at_peak_speed"] = round(reach[peak + 1], 4)

        # travel speed: the stance foot's backward speed, medianed over the cycle
        if feet:
            per_frame = []
            tracks = {f: sample(arm, f, f0, f1) for f in feet}
            for i in range(1, len(origin)):
                back = [-((tracks[f][i] - tracks[f][i - 1]).dot(forward)) * fps for f in feet]
                per_frame.append(max(back))
            if per_frame:
                info["stance_speed"] = round(statistics.median(per_frame), 4)
                info["stance_speed_peak"] = round(max(per_frame), 4)
        # a hop: how high the root's child body rises, and the airborne span (the frog travels by jumping)
        body = pick(bones, ("Body", "BodyRoot", "Torso", "Thorax"))
        if body:
            z = [(arm.matrix_world @ bones[body].head).z for f in range(f0, f1 + 1) if not scene.frame_set(f)]
            info["body_rise"] = round(max(z) - min(z), 4)
        if feet:
            # the airborne window: the frames where the LOWEST foot has left the ground. This is what a hop's
            # travel has to happen inside — the frog's jump crouches for its first third and settles for its
            # last, and moving it at a constant speed across all of that is the "hops weird" the user saw.
            low = []
            for f in range(f0, f1 + 1):
                scene.frame_set(f)
                low.append(min((arm.matrix_world @ bones[n].head).z for n in feet))
            ground, rise = min(low), max(low) - min(low)
            if rise > 1e-3:
                air = [i for i, z in enumerate(low) if z - ground > rise * 0.15]
                info["air_start"] = round(air[0] / fps, 3)
                info["air_end"] = round(air[-1] / fps, 3)
                info["foot_rise"] = round(rise, 4)
        clips[clip] = info
        log("  %-9s %5.2f s  hit %s  reach %s  stance %s" % (
            clip, info["seconds"], info.get("hit_time"), info.get("reach"), info.get("stance_speed")))
    ad.action = None

    out = dict(kind=name, fps=fps, root_bone=root, strike_bone=strike, feet=feet,
               forward=[round(v, 4) for v in forward], clips=clips)
    path = os.path.join(monster_kinds.ROOT, "tools", "out", "monsters", name + "_clips.json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="\n") as f:
        json.dump(out, f, indent=2)
    log("  wrote", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    monster_kinds.add_kind_arg(ap)
    args = ap.parse_args(argv)
    for name in monster_kinds.selected(args.kind):
        run(name)


if __name__ == "__main__":
    main()
