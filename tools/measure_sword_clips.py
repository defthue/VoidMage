"""Measure a retargeted Synty clip pack in Blender so an attack table carries real numbers: when each swing lands,
how far the blade reaches, how far the root travels BEFORE the hit, how long the clip is.

Written for the Synty Sword Animations (Assets/Animations/SkeletonSword, retarget_synty.py) behind the swordsman's
table (tools/gen_swordsman_prefab.py); `--dir` and `--calibration` point it at any other pack on the same donor rig.
The base skeleton's own pack is SkeletonSS, which is what SkeletonNpc.SwordA/SwordB/Charge/ShieldBash describe:

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/measure_sword_clips.py -- [--filter "^attack_"] [--blade-cm 93] [--out tools/out/sword_clips.json]

    ... --python tools/measure_sword_clips.py -- --dir Assets/Animations/SkeletonSS --calibration run ^
        --filter "^(attack_d_0|guard_attack_parry|charge_attack_)" --out tools/out/skeleton_ss_clips.json

Rules (the player's sword crescent and the knight's hand-measured table used the same):
* the STRIKE is the frame where the blade TIP moves fastest relative to the root (a cut, unlike the monsters' bites,
  which land at maximum extension). Speed is 3-frame averaged (Blender's alternating fast/slow frames). `peaks` lists
  every local maximum above 40 % of the top one and at least 6 frames apart, so a two-strike clip (combos, the
  flourish) can be told from a wind-up: the generator pins `hit_peak` / `two_hit` per row.
* the blade tip is derived like prep_skeleton_npc.py derives the grip: from the weapon_r socket along the knuckle line
  (index_01_r -> middle_01_r, toward the thumb), `--blade-cm` beyond it (93 = the 107 cm Sword_01 minus its 13 % grip).
* reach = the tip's forward distance from the root around the hit (HitRange), lunge = the root's forward travel from
  the first frame to the hit (LungeBeforeHit; only the `_rootmotion` variants carry any).
* forward is calibrated, not assumed: the root travel of `attack_lightleaping01_rootmotionhorizontal` (the root never
  rotates in this pack, so one direction serves every clip).
Everything is in cm (the clips are cm under a 0.01 armature scale; Blender world is metres, x100 here) and seconds
at playback 1.0; the json carries `units_per_cm` (0.3937) so the generator converts without guessing.
"""
import argparse, glob, json, os, re, sys

import bpy
from mathutils import Vector

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
DEFAULT_CLIP_DIR = os.path.join("Assets", "Animations", "SkeletonSword")
DEFAULT_CALIBRATION = "attack_lightleaping01_rootmotionhorizontal"
DEFAULT_FILTER = r"^(attack_|hit_|knockdown_|stun_|idle_base$)"
CM = 100.0


def log(*a):
    print("[measure]", *a)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    action = arm.animation_data.action
    f0, f1 = (int(round(x)) for x in action.frame_range)
    sc = bpy.context.scene
    fps = sc.render.fps / sc.render.fps_base
    return arm, f0, f1, fps


def head(arm, name):
    return (arm.matrix_world @ arm.pose.bones[name].head) * CM


def planar(v):
    return Vector((v.x, v.y, 0.0))


def calibrate(path):
    arm, f0, f1, _ = load(path)
    sc = bpy.context.scene
    sc.frame_set(f0); a = head(arm, "root").copy()
    sc.frame_set(f1); b = head(arm, "root").copy()
    d = planar(b - a)
    if d.length < 50.0:
        raise SystemExit("calibration clip %s travels only %.1f cm; cannot derive forward" % (path, d.length))
    return d.normalized()


def measure(path, forward, blade_cm, is_attack):
    arm, f0, f1, fps = load(path)
    sc = bpy.context.scene
    bones = arm.pose.bones
    strike = "weapon_r" if "weapon_r" in bones else "hand_r"
    info = dict(frames=[f0, f1], seconds=round((f1 - f0) / fps, 3))
    roots, tips = [], []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        r = head(arm, "root").copy()
        roots.append(r)
        if is_attack:
            knuckles = (head(arm, "index_01_r") - head(arm, "middle_01_r")).normalized()
            tips.append(head(arm, strike) + knuckles * blade_cm - r)
    r0 = roots[0]
    fwd = [planar(r - r0).dot(forward) for r in roots]
    info["root_travel_cm"] = round(max(fwd) - min(fwd), 1)
    info["root_lateral_cm"] = round(max(abs(planar(r - r0).cross(forward).z) for r in roots), 1)
    info["root_rise_cm"] = round(max(r.z for r in roots) - r0.z, 1)
    if not is_attack:
        return info, strike
    speed = [planar(tips[i] - tips[i - 1]).length * fps for i in range(1, len(tips))]
    avg = [sum(speed[max(0, i - 1):i + 2]) / len(speed[max(0, i - 1):i + 2]) for i in range(len(speed))]
    top = max(range(len(avg)), key=lambda i: avg[i])
    # local maxima above 40 % of the top, accepted tallest first so the top always survives the 6-frame spacing rule
    candidates = [i for i in range(len(avg)) if avg[i] >= 0.4 * avg[top] and avg[i] >= max(avg[max(0, i - 3):i + 4])]
    peaks = []
    for i in sorted(candidates, key=lambda i: -avg[i]):
        if all(abs(i - j) >= 6 for j in peaks):
            peaks.append(i)
    peaks.sort()

    def reach_at(i):
        # `i` indexes speed (between frames i and i+1); the tip at frame i+1, widened by two frames either side
        window = range(max(0, i - 1), min(len(tips), i + 4))
        return max(planar(tips[j]).dot(forward) for j in window), max(planar(tips[j]).length for j in window)
    peak_list = []
    for i in peaks:
        fwd_reach, planar_reach = reach_at(i)
        peak_list.append(dict(time=round((i + 1) / fps, 3), speed_cm_s=round(avg[i], 0), reach_forward_cm=round(fwd_reach, 1),
                              reach_planar_cm=round(planar_reach, 1), lunge_before_cm=round(fwd[min(i + 1, len(fwd) - 1)], 1)))
    hit = peak_list[peaks.index(top)]
    later = [p for p in peak_list if p["time"] >= hit["time"] + 0.25]
    info.update(hit_time=hit["time"], hit_speed_cm_s=hit["speed_cm_s"], reach_forward_cm=hit["reach_forward_cm"],
                reach_planar_cm=hit["reach_planar_cm"], lunge_before_hit_cm=hit["lunge_before_cm"],
                hit2_time=later[0]["time"] if later else None, peaks=peak_list)
    sc.frame_set(f0 + 1 + top)
    info["hand_reach_forward_cm"] = round(planar(head(arm, strike) - roots[top + 1]).dot(forward), 1)
    # maximum extension (a THRUST lands here, not at peak tip speed: its tip moves along its own axis, the monsters' bite rule)
    ext = [planar(t).dot(forward) for t in tips]
    far = max(range(len(ext)), key=lambda i: ext[i])
    info.update(max_reach_time=round(far / fps, 3), max_reach_forward_cm=round(ext[far], 1), lunge_at_max_reach_cm=round(fwd[far], 1))
    return info, strike


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--filter", default=DEFAULT_FILTER, help="regex on the clip name")
    ap.add_argument("--blade-cm", type=float, default=93.0, help="blade length beyond the weapon_r socket")
    ap.add_argument("--dir", default=DEFAULT_CLIP_DIR, help="clip folder, relative to the project root")
    ap.add_argument("--calibration", default=DEFAULT_CALIBRATION,
                    help="clip in that folder whose root travels far enough to derive forward (a run or a leap)")
    ap.add_argument("--out", default=os.path.join(ROOT, "tools", "out", "sword_clips.json"))
    args = ap.parse_args(argv)
    clip_dir = args.dir if os.path.isabs(args.dir) else os.path.join(ROOT, args.dir)
    rx = re.compile(args.filter)
    clips = [c for c in sorted(glob.glob(os.path.join(clip_dir, "*.fbx"))) if rx.search(os.path.splitext(os.path.basename(c))[0])]
    if not clips:
        raise SystemExit("no clips match %r in %s" % (args.filter, clip_dir))
    forward = calibrate(os.path.join(clip_dir, args.calibration + ".fbx"))
    log("forward (from %s) = %s" % (args.calibration, tuple(round(v, 3) for v in forward)))
    out = dict(fps=30, units_per_cm=0.3937, blade_cm=args.blade_cm, root_bone="root", strike_bone="weapon_r",
               forward=[round(v, 4) for v in forward], clips={})
    for path in clips:
        name = os.path.splitext(os.path.basename(path))[0]
        info, strike = measure(path, forward, args.blade_cm, name.startswith("attack_"))
        out["strike_bone"] = strike
        out["clips"][name] = info
        if "hit_time" in info:
            log("  %-46s %5.2f s  hit %.2f (%4.0f cm/s) reach %5.1f (hand %5.1f)  lunge %5.1f / travel %5.1f cm  hit2 %s  peaks %s  max ext %5.1f @ %.2f" % (
                name, info["seconds"], info["hit_time"], info["hit_speed_cm_s"], info["reach_forward_cm"], info["hand_reach_forward_cm"],
                info["lunge_before_hit_cm"], info["root_travel_cm"], info["hit2_time"], [p["time"] for p in info["peaks"]],
                info["max_reach_forward_cm"], info["max_reach_time"]))
        else:
            log("  %-46s %5.2f s  travel %5.1f cm rise %4.1f" % (name, info["seconds"], info["root_travel_cm"], info["root_rise_cm"]))
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", newline="\n") as f:
        json.dump(out, f, indent=2)
    log("wrote", args.out, len(out["clips"]), "clips")


if __name__ == "__main__":
    main()
