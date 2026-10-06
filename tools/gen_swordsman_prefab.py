"""Write the swordsman's prefabs and brain: Assets/Prefabs/spirit_demon.prefab, Assets/Prefabs/dark_lord.prefab (two
skins of one enemy, differing only in KindName / model / whether it bleeds) and Assets/Npc/swordsman.brain.

Cloned from skeleton_soldier_01.prefab (fresh GUIDs; the NavMeshAgent / capsule / SkeletonNpc / SkeletonBody / `visual`
child layout every skeleton shares) with the swordsman's numbers written over it. The ATTACK TABLE lives here: one entry
per attack clip of Synty's Sword Animations pack in gen_swordsman_animgraph.ATTACK_CLIPS order (kind N = state N). Its
timings and reaches are READ from tools/out/sword_clips.json (tools/measure_sword_clips.py, Blender, playback 1.0):
HitTime = the blade tip's speed peak, HitRange = the tip's forward reach at that frame, LungeBeforeHit = the root's
forward travel to it, Duration = the hit plus a fixed recovery (a punishable standstill; the clips' own tails and the
`_returntoidle` variants are longer). The design -- weights, cooldowns, ranges, combos, hyper armor, damage -- is this
file's, not the knight's: light combo chains a -> b -> c and flows out of the fencing poke; heavy combo is committed
(armor through the wind-up) and ends in a stepping finisher; the stab is the mid-range pick and never point blank; the
flourish is rare and shoves; the leaping strike opens the fight from a distance and re-engages from far only.

    python tools/gen_swordsman_prefab.py [--force]

Rerun after touching a number or re-measuring; the prefabs are not hand-tuned (this file is the truth).
"""
import argparse, json, os, sys, uuid

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import skeleton_kinds  # noqa: E402
from gen_swordsman_animgraph import ATTACK_CLIPS, HURT_CLIPS, KD_BEGIN  # noqa: E402

PREFABS = os.path.join(ROOT, "Assets", "Prefabs")
SOURCE = "skeleton_soldier_01"
KINDS = ["spirit_demon", "dark_lord"]
BRAIN = os.path.join(ROOT, "Assets", "Npc", "swordsman.brain")
MEASURED = os.path.join(ROOT, "tools", "out", "sword_clips.json")

# between the soldier (12 x 1.5 = 18 a swing) and the knight (14 / 22)
# halved 2026-09-18 with every other enemy's damage, the floors having gone from 50-80 bodies to 100-160
# 2026-09-21 the spread was narrowed (user: one body's attacks should not differ by almost double): 6-9, was 6-10.5
LIGHT, LIGHT_FINISH, HEAVY, HEAVY_FINISH, STAB, FLOURISH, LEAP = 6.0, 7.0, 8.5, 9.0, 8.0, 9.0, 8.0
PUSH, POKE_PUSH, THRUST_PUSH, HEAVY_PUSH, FLOURISH_PUSH = 150.0, 120.0, 230.0, 280.0, 340.0
# seconds of standstill after the blade lands (the clip keeps playing until `busy` drops)
RECOVER_LIGHT, RECOVER_HEAVY, RECOVER_LEAP = 0.5, 0.8, 0.7
REACH_MIN, REACH_MAX = 50.0, 110.0


def row(clip, damage, push, recover, arc=55.0, weight=1.0, cooldown=0.0, select_min=0.0, select_max=0.0, combo="",
        combo_chance=0.0, armor=-1.0, face_lock=0.15, min_range=0.0, opener=0.0, hit_peak=None, two_hit=False, extension=False):
    """The design of one attack; `hit_peak` picks a measured speed peak by index when the loudest one is a wind-up,
    `two_hit` takes the next measured peak as HitTime2, `extension` lands the hit at the tip's maximum forward reach
    instead of its speed peak (a thrust: the tip moves along its own axis, so its speed peak undersells it)."""
    return dict(clip=clip, damage=damage, push=push, recover=recover, arc=arc, weight=weight, cooldown=cooldown,
                select_min=select_min, select_max=select_max, combo=combo, combo_chance=combo_chance, armor=armor,
                face_lock=face_lock, min_range=min_range, opener=opener, hit_peak=hit_peak, two_hit=two_hit, extension=extension)


ROWS = [
    row("attack_lightcombo01a_rootmotion", LIGHT, PUSH, RECOVER_LIGHT, arc=50.0, weight=3.0,
        combo="attack_lightcombo01b_rootmotion", combo_chance=0.75, face_lock=0.12),                      # the fast chain's first cut
    row("attack_lightcombo01b_rootmotion", LIGHT, PUSH, RECOVER_LIGHT, arc=50.0, weight=0.0,
        combo="attack_lightcombo01c_rootmotion", combo_chance=0.6, face_lock=0.12),                       # combo-only
    row("attack_lightcombo01c_rootmotion", LIGHT_FINISH, THRUST_PUSH, RECOVER_LIGHT, arc=55.0, weight=0.0, face_lock=0.12,
        extension=True),                                                                                  # the chain's finisher: a thrust
    row("attack_lightfencing01_rootmotion", LIGHT, POKE_PUSH, RECOVER_LIGHT, arc=25.0, weight=3.0, cooldown=1.5, select_min=60.0,
        combo="attack_lightcombo01a_rootmotion", combo_chance=0.3, face_lock=0.10),                       # the lunging poke from mid range (narrow)
    row("attack_heavycombo01a_rootmotion", HEAVY, HEAVY_PUSH, RECOVER_HEAVY, arc=55.0, weight=2.0, cooldown=4.0,
        combo="attack_heavycombo01b_rootmotion", combo_chance=0.7, armor=0.10, face_lock=0.20),           # the committed chain
    row("attack_heavycombo01b_rootmotion", HEAVY, HEAVY_PUSH, RECOVER_HEAVY, arc=55.0, weight=0.0,
        combo="attack_heavycombo01c_rootmotion", combo_chance=0.55, armor=0.05, face_lock=0.20),          # combo-only
    row("attack_heavycombo01c_rootmotion", HEAVY_FINISH, FLOURISH_PUSH, RECOVER_HEAVY, arc=60.0, weight=0.0, armor=0.0, face_lock=0.25),   # the stepping finisher
    # measured: the stab's tip only reaches 131 cm (the root steps 50) -- it is not a long thrust in this pack, so it is
    # the POINT-BLANK pick: a slow armored thrust into a player standing inside the cuts' arcs
    row("attack_heavystab01_rootmotion", STAB, THRUST_PUSH, RECOVER_HEAVY, arc=20.0, weight=2.5, cooldown=3.0,
        select_max=60.0, armor=0.15, face_lock=0.20, extension=True),
    row("attack_heavyflourish01_rootmotion", FLOURISH, FLOURISH_PUSH, RECOVER_HEAVY, arc=80.0, weight=1.2, cooldown=7.0,
        armor=0.10, face_lock=0.25, hit_peak=1, two_hit=True),                                            # rare, wide: passes at 1.2 and 1.47 s (the 0.87 peak is the wind-up)
    row("attack_lightleaping01_rootmotion", LEAP, HEAVY_PUSH, RECOVER_LEAP, arc=45.0, weight=1.5, cooldown=8.0,
        select_min=140.0, min_range=120.0, opener=280.0, armor=0.15, face_lock=0.35),                     # the opener; re-engages from far
]

# the clip root travel at scale 1.0 (walk 150 / jog 405 / run 617 cm/s = the code's defaults) is left as is
NPC = dict(
    DamageScale=1.0, SecondSwordDamageScale=1.0,
    TurnRateEngaged=540.0, TurnRateGuarding=300.0, TurnRateMoving=480.0,
    EngageRange=130.0, HitWindowSeconds=0.12,
    # no shield: the guard never rises (swordsman.brain rolls no guard either)
    GuardPoints=0, GuardBreakSeconds=1.0, GuardHitSeconds=0.45, GuardRaiseGrace=0.0,
    HurtSeconds=0.6, CanStagger=True, FlinchShoveScale=0.6,
    BodyHeight=74.0, EyeHeight=66.0, HitHeightTolerance=72.0,
    # posture: ~115 damage of a 200 hp body knocks him DOWN (the pack's knockdown chain, hurt kind 5) for the
    # chain's length, at 1.5x damage; once per fight in practice
    PostureMax=70.0, PosturePerDamage=0.6, PostureDecayPerSecond=8.0, PostureDecayDelay=2.0,
    StaggerSeconds=4.07, StaggerDamageMultiplier=1.5,
    ShieldReactChance=0.0,
    RepeatPenalty=0.35, RecentPenalty=0.7,
    # root motion plants AT the player, never through them (the leap's 289 u is clamped to the gap)
    RootMotionStandoff=40.0,
)
AGENT = dict(Height=66, Radius=16, MaxSpeed=243, Acceleration=972)
CAPSULE = dict(Start="0,0,12", End="0,0,64", Radius=14)
BODY = {"spirit_demon": dict(Bleeds=False), "dark_lord": dict(Bleeds=True)}
BRAIN_VALUES = dict(
    AttackCooldownMin=0.28, AttackCooldownMax=0.72,   # the 20 % faster swing rate every enemy got (2026-09-17)
    ChargeChance=0.0, ShieldBashChance=0.0, GuardChance=0.0, GuardHoldMin=1.0, GuardHoldMax=1.5, GuardAfterHurtChance=0.0,
    GuardApproachRange=0, PreferredRange=85, StrafeChance=0.45, StrafeSecondsMin=0.6, StrafeSecondsMax=1.0,
    WalkWhileEngaged=False, BackOffChance=0.15, AggroRange=850, Wander=False, Health=65, Jitter=0.1,
)


def build_table(measured):
    u = measured["units_per_cm"]
    table = []
    for r in ROWS:
        m = measured["clips"].get(r["clip"])
        if m is None or "hit_time" not in m:
            raise SystemExit("%s is not measured; run tools/measure_sword_clips.py" % r["clip"])
        if r["extension"]:
            hit, reach, lunge = m["max_reach_time"], m["max_reach_forward_cm"], m["lunge_at_max_reach_cm"]
        elif r["hit_peak"] is not None:
            peak = m["peaks"][r["hit_peak"]]
            hit, reach, lunge = peak["time"], peak["reach_forward_cm"], peak["lunge_before_cm"]
        else:
            hit, reach, lunge = m["hit_time"], m["reach_forward_cm"], m["lunge_before_hit_cm"]
        hit2 = 0.0
        if r["two_hit"]:
            later = [p["time"] for p in m["peaks"] if p["time"] >= hit + 0.25]
            if not later:
                raise SystemExit("%s: two_hit but no second peak in %s" % (r["clip"], [p["time"] for p in m["peaks"]]))
            hit2 = later[0]
        if r["clip"].endswith("_rootmotion") and m["root_travel_cm"] < 1.0:
            raise SystemExit("%s carries no root travel" % r["clip"])
        # the body stays locked `recover` seconds past the (last) hit, past the clip's end if need be: the graph blends
        # back to the stance while the host still says busy, which reads as the recovery pause it is
        duration = round(max(hit, hit2) + r["recover"], 2)
        table.append(dict(
            Name=r["clip"], HitTime=hit, HitTime2=hit2, Duration=duration,
            HitRange=round(min(REACH_MAX, max(REACH_MIN, reach * u + 4.0)), 1), LungeBeforeHit=round(lunge * u, 1),
            ArcDegrees=r["arc"], Damage=r["damage"], Push=r["push"], FaceLockBefore=r["face_lock"], ArmorFrom=r["armor"],
            LungeSpeed=0.0, MinRange=r["min_range"], OpenerRange=r["opener"], Weight=r["weight"], Cooldown=r["cooldown"],
            SelectMin=r["select_min"], SelectMax=r["select_max"], ComboInto=r["combo"], ComboChance=r["combo_chance"],
            KeepsGuard=False, HitTag="skeleton_sword"))
    return table


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
    if not os.path.exists(MEASURED):
        raise SystemExit("%s missing: run tools/measure_sword_clips.py first" % MEASURED)
    with open(MEASURED) as f:
        measured = json.load(f)
    table = build_table(measured)
    names = [a["Name"] for a in table]
    if names != ATTACK_CLIPS:
        raise SystemExit("attack table order differs from gen_swordsman_animgraph.ATTACK_CLIPS:\n%s\n%s" % (names, ATTACK_CLIPS))
    for a in table:
        for other in a["ComboInto"].split(","):
            if other and other not in names:
                raise SystemExit("%s combos into unknown %s" % (a["Name"], other))
    if sum(1 for a in table if a["OpenerRange"] > 0) != 1:
        raise SystemExit("exactly one attack must carry OpenerRange")
    chain = sum(measured["clips"][c]["seconds"] for c, _ in HURT_CLIPS[KD_BEGIN:]) + 0.1
    if abs(chain - NPC["StaggerSeconds"]) > 0.15:
        raise SystemExit("StaggerSeconds %.2f should match the knockdown chain %.2f s" % (NPC["StaggerSeconds"], chain))

    for name in KINDS:
        kind = skeleton_kinds.kind(name)
        dst = os.path.join(PREFABS, name + ".prefab")
        if os.path.exists(dst) and not args.force:
            raise SystemExit("%s exists; --force to overwrite" % dst)
        with open(os.path.join(PREFABS, SOURCE + ".prefab"), encoding="utf-8") as f:
            data = json.load(f)
        root = data["RootObject"]
        fresh_guids(data)
        root["Name"] = name
        npc = one(root, "VoidMage.Npc.SkeletonNpc")
        npc.update(NPC)
        npc["KindName"] = name
        npc["Attacks"] = table
        body = one(root, "VoidMage.Npc.SkeletonBody")
        body.update(BODY[name])
        body["BlockSound"] = ""   # a posture break is hurt kind 5, which SkeletonBody treats as a shield event: no clang
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
        print("wrote", dst, "model", ren["Model"], "graph", ren["AnimationGraph"], "body", BODY[name])

    brain = dict(BRAIN_VALUES)
    brain["__references"] = []
    brain["__version"] = 0
    with open(BRAIN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(brain, f, indent=2)
    print("wrote", BRAIN)
    print("attack table (%d rows, knockdown chain %.2f s):" % (len(table), chain))
    for a in table:
        start = a["HitRange"] + a["LungeBeforeHit"] * 0.75 - 4
        print("  %-38s hit %.2f%s dur %.2f reach %5.1f lunge %5.1f u  start %5.1f  select %3.0f-%3.0f  w %.1f cd %.0f %s" % (
            a["Name"], a["HitTime"], (" +%.2f" % a["HitTime2"]) if a["HitTime2"] else "", a["Duration"], a["HitRange"], a["LungeBeforeHit"],
            start, a["SelectMin"], a["SelectMax"] or start, a["Weight"], a["Cooldown"], ("-> " + a["ComboInto"]) if a["ComboInto"] else ""))


if __name__ == "__main__":
    main()
