"""Write Assets/Prefabs/undead_knight.prefab: the Undead Knight, heden's boss grammar as a strong voidmage enemy.

Cloned from skeleton_soldier_01.prefab (fresh GUIDs; the NavMeshAgent / capsule / SkeletonNpc / SkeletonBody /
`visual` child layout every skeleton shares) with the knight's numbers written over it. The ATTACK TABLE lives here:
one entry per attack_* clip of the Sword & Shield pack, in gen_knight_animgraph.ATTACK_CLIPS order (kind N = state N),
measured 2026-09-12 with the Blender probe at playback 1.0 (hand_r speed peak = the strike, root travel = the lunge).
Units: root travel cm x 0.3937 x model_scale 1.2 = x 0.4724. His sword is 151 cm (71 u at his scale), so a swing
reaches ~95 u planar from the root.

    python tools/gen_knight_prefab.py [--force]

Rerun after touching a number; the prefab is not hand-tuned (this file is the truth, like monster_kinds.py).
"""
import argparse, json, os, sys, uuid

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import skeleton_kinds  # noqa: E402
from gen_knight_animgraph import ATTACK_CLIPS  # noqa: E402

PREFABS = os.path.join(ROOT, "Assets", "Prefabs")
SOURCE = "skeleton_soldier_01"
NAME = "undead_knight"
U = 0.3937 * 1.2   # cm -> units at the knight's scale

# heden's boss kit, translated: a big swing reaches ~95 u; leaps and the long lunge are gap closers picked only from
# a distance (SelectMin) and never point blank; heavies carry hyper armor through the wind-up; combos flow from the
# first swing of a pair into its second (Weight 0 = combo-only, like slash_2). Cooldowns keep the leaps rare.
LIGHT, HEAVY, LEAP = 7.0, 9.0, 9.0   # halved 2026-09-18 with every other enemy's damage (100-160 bodies a floor now);
# 2026-09-21 the spread was narrowed (user: one body's attacks should not differ by almost double): 6-9, was 5-11
PUSH, HEAVY_PUSH = 200.0, 320.0
REACH, LEAP_REACH = 95.0, 100.0


def atk(name, hit, dur, lunge_cm, damage=LIGHT, push=PUSH, reach=REACH, arc=55.0, weight=1.0, cooldown=0.0,
        select_min=0.0, select_max=0.0, combo="", combo_chance=0.0, armor=-1.0, face_lock=0.15, min_range=0.0,
        opener=0.0, hit2=0.0):
    return dict(Name=name, HitTime=hit, HitTime2=hit2, Duration=dur, HitRange=reach, LungeBeforeHit=round(lunge_cm * U, 1),
                ArcDegrees=arc, Damage=damage, Push=push, FaceLockBefore=face_lock, ArmorFrom=armor, LungeSpeed=0.0,
                MinRange=min_range, OpenerRange=opener, Weight=weight, Cooldown=cooldown, SelectMin=select_min,
                SelectMax=select_max, ComboInto=combo, ComboChance=combo_chance, KeepsGuard=False, HitTag="skeleton_sword")


# (clip, HitTime s, Duration s, root travel before the hit in cm, ...). Durations cut the clips' 1.5 s flourish
# tails to a punishable recovery (heden: "recovery is a standstill the player is meant to punish").
ATTACKS = [
    atk("attack_a_01", 0.23, 1.00, 63, weight=3.0, combo="attack_a_02", combo_chance=0.7),
    atk("attack_a_02", 0.22, 0.95, 80, weight=0.0),                                            # a_01's second swing
    atk("attack_a_03", 0.53, 1.25, 238, damage=LEAP, push=HEAVY_PUSH, reach=LEAP_REACH, weight=2.0, cooldown=6.0,
        select_min=140.0, armor=0.15, face_lock=0.25),                                          # the long lunging strike
    atk("attack_b_01", 0.22, 0.95, 60, weight=2.5, combo="attack_b_02", combo_chance=0.6),
    atk("attack_b_02", 0.23, 0.95, 80, weight=0.0),                                            # b_01's second swing
    atk("attack_b_03_a", 0.27, 1.00, 92, weight=2.0, combo="attack_b_03_c", combo_chance=0.35),
    atk("attack_b_03_b", 0.63, 1.30, 300, damage=HEAVY, push=HEAVY_PUSH, reach=LEAP_REACH, weight=2.0, cooldown=8.0,
        select_min=140.0, armor=0.2, face_lock=0.25),                                           # the jumping strike
    atk("attack_b_03_c", 0.48, 1.15, 209, damage=HEAVY, push=HEAVY_PUSH, weight=2.0, cooldown=5.0, select_min=90.0,
        armor=0.15, face_lock=0.2),                                                             # the overhead heavy (fastest blade)
    atk("attack_b_03_d", 0.33, 1.00, 141, damage=6.0, push=HEAVY_PUSH, weight=1.5, cooldown=4.0),   # the slow pointing shove
    atk("attack_c_02", 0.23, 1.35, 80, damage=6.0, weight=2.0, cooldown=5.0, hit2=0.62),      # two strikes, the second steps in
    atk("attack_c_03_a", 0.52, 1.20, 263, damage=LEAP, push=HEAVY_PUSH, reach=LEAP_REACH, weight=2.5, cooldown=7.0,
        select_min=140.0, min_range=140.0, opener=260.0, armor=0.1, face_lock=0.25),          # the leap: also the opener
    atk("attack_d_01", 0.23, 0.95, 92, weight=2.5, combo="attack_d_02", combo_chance=0.7),
    atk("attack_d_02", 0.23, 0.90, 70, weight=0.0),                                            # d_01's second swing
]

NPC = dict(
    KindName=NAME, DamageScale=1.0, SecondSwordDamageScale=1.0,
    # clip root travel x 1.2 (walk 150 / jog 405 / run 617 cm/s at the scale)
    WalkSpeed=71.0, JogSpeed=191.0, RunSpeed=292.0,
    # deliberate, not a snap (heden's IdleTurnRate 240): a player circling him can reach his flank
    TurnRateEngaged=360.0, TurnRateGuarding=240.0, TurnRateMoving=420.0,
    EngageRange=130.0, HitWindowSeconds=0.14,
    GuardPoints=99, GuardBreakSeconds=1.2, GuardHitSeconds=0.45, GuardRaiseGrace=0.22,
    HurtSeconds=0.69,
    # no flinch clips on a boss body: hits flash and barely shove; posture does the staggering
    CanStagger=False, FlinchShoveScale=0.15,
    BodyHeight=84.0, EyeHeight=72.0, HitHeightTolerance=80.0,
    # posture (heden: 0.6 per damage, 260 to break, decays after 2 s; a 4 s stagger at 1.5x damage)
    PostureMax=260.0, PosturePerDamage=0.6, PostureDecayPerSecond=6.0, PostureDecayDelay=2.0,
    StaggerSeconds=3.5, StaggerDamageMultiplier=1.5,
    # the shield read: the second frontal light inside 1.6 s snaps the shield up (0.9, once per 3 s) when he is
    # interruptible; lights bounce, a heavy on it breaks it (1.25x damage, +30 posture, guard_heavy_hit)
    ShieldReactChance=0.9, ShieldReactCooldown=3.0, ShieldReactHoldSeconds=1.5, ShieldReactLightsNeeded=2,
    ShieldReactWindow=1.6, ShieldBreakDamageFactor=1.25, ShieldBreakPosture=30.0,
    RepeatPenalty=0.35, RecentPenalty=0.7,
    # root motion plants AT the player, never through them (heden's StepInStandoff)
    RootMotionStandoff=45.0,
    Attacks=ATTACKS,
)
AGENT = dict(Height=77, Radius=20, MaxSpeed=292, Acceleration=972)
CAPSULE = dict(Start="0,0,14", End="0,0,74", Radius=17)


def fresh_guids(node):
    if isinstance(node, dict):
        for k, v in node.items():
            if k == "__guid" and isinstance(v, str):
                node[k] = str(uuid.uuid4())
            else:
                fresh_guids(v)
    elif isinstance(node, list):
        for v in node:
            fresh_guids(v)


def components(node, type_name):
    found = []
    if isinstance(node, dict):
        if node.get("__type") == type_name:
            found.append(node)
        for v in node.values():
            found.extend(components(v, type_name))
    elif isinstance(node, list):
        for v in node:
            found.extend(components(v, type_name))
    return found


def one(node, type_name):
    found = components(node, type_name)
    if len(found) != 1:
        raise SystemExit("expected one %s, got %d" % (type_name, len(found)))
    return found[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    names = [a["Name"] for a in ATTACKS]
    if names != ATTACK_CLIPS:
        raise SystemExit("attack table order differs from gen_knight_animgraph.ATTACK_CLIPS:\n%s\n%s" % (names, ATTACK_CLIPS))
    for a in ATTACKS:
        for other in a["ComboInto"].split(","):
            if other and other not in names:
                raise SystemExit("%s combos into unknown %s" % (a["Name"], other))
    kind = skeleton_kinds.kind(NAME)
    dst = os.path.join(PREFABS, NAME + ".prefab")
    if os.path.exists(dst) and not args.force:
        raise SystemExit("%s exists; --force to overwrite" % dst)
    with open(os.path.join(PREFABS, SOURCE + ".prefab"), encoding="utf-8") as f:
        data = json.load(f)
    root = data["RootObject"]
    fresh_guids(data)
    root["Name"] = NAME
    npc = one(root, "VoidMage.Npc.SkeletonNpc")
    npc.update(NPC)
    one(root, "Sandbox.NavMeshAgent").update(AGENT)
    one(root, "Sandbox.CapsuleCollider").update(CAPSULE)
    ren = one(root, "Sandbox.SkinnedModelRenderer")
    ren["Model"] = os.path.relpath(kind["vmdl"], os.path.join(ROOT, "Assets")).replace("\\", "/").lower()
    ren["AnimationGraph"] = kind["animgraph"]
    nav = root.get("__properties", {}).get("NavMesh")
    if nav:
        nav["AgentHeight"], nav["AgentRadius"] = AGENT["Height"], AGENT["Radius"]
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print("wrote", dst, "attacks", len(ATTACKS), "model", ren["Model"], "graph", ren["AnimationGraph"])
    for a in ATTACKS:
        start = a["HitRange"] + a["LungeBeforeHit"] * 0.75 - 4
        print("  %-14s hit %.2f dur %.2f lunge %5.1f u  start %5.1f  select %3.0f-%3.0f  w %.1f cd %.0f %s" % (
            a["Name"], a["HitTime"], a["Duration"], a["LungeBeforeHit"], start, a["SelectMin"], a["SelectMax"] or start,
            a["Weight"], a["Cooldown"], ("-> " + a["ComboInto"]) if a["ComboInto"] else ""))


if __name__ == "__main__":
    main()
