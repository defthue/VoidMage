"""Write each monster's prefab (Assets/Prefabs/monster_<kind>.prefab) and brain (Assets/Npc/monster_<kind>.brain)
from tools/monster_kinds.py plus the two generated measurements, so no number in either file is a guess.

    python tools/gen_monster_prefabs.py [--kind bat]

Everything is the Tormented Soul's shape, because the monsters reuse its components exactly (SkeletonNpc +
SkeletonBody + NavMeshAgent + CapsuleCollider, model on a child object): a flier hovers, every clip plays IN
PLACE and the code moves the body.

Derived rather than authored:

* **Attack timing** from tools/out/monsters/<kind>_clips.json: HitTime is the frame the strike bone is
  furthest forward, Duration the clip's own length. LungeBeforeHit is `lunge x HitTime` for the fliers, which
  is how far the code will have carried them by the time the bite lands; SkeletonNpc.StartRange derives from
  the pair, so a monster only starts a bite it can actually finish.
* **HitRange** is the strike bone's measured reach on top of the PHYSICAL FLOOR — this monster's own collider
  radius plus the player's half-width. At this scale the reach barely matters (a rat's head leads its own root
  by 6 units) and the floor decides everything; getting it from the wrong radius is what left the spider
  unable to attack at all. `PreferredRange` comes off the same number, or the brain never stops closing.
* **Speeds** from the stance-foot measurement times the animgraph's `loco.playback`. That is the ONLY speed
  the clip supports without the feet sliding; the printed slide is 0 for anything measured. Footless kinds
  (the snake, the three fliers) and the hopping frog carry `speed` in monster_kinds.py instead.
* **Capsule and agent** from the compiled model's own size (tools/out/monsters/<kind>_model.json).

GUIDs are hashed from the kind name so a rerun rewrites the same prefab instead of churning every id.
"""
import argparse, hashlib, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import monster_kinds  # noqa: E402

# THE PHYSICAL FLOOR. Every distance in SkeletonNpc is centre-to-centre between two root origins and nothing
# subtracts a radius, so the closest a monster can ever get to the player is its own COLLIDER radius plus the
# player's half-width. Get this wrong and the monster is not merely bad at attacking, it is incapable: the
# spider's HitRange sat at a flat 38 while its capsule radius alone was 30, so `distance <= StartRange` could
# never be true. It spent its life running at a point inside the player, and because its collider is a static
# body with no Rigidbody, PhysX resolved the overlap by shoving the only movable thing — the player.
#
# The trap the first version fell into was budgeting the NavMeshAgent radius (always 16) instead of the
# CapsuleCollider radius (derived per monster from its width, up to 40). They are different numbers.
PLAYER_HALF_WIDTH = 11.32     # player.prefab: a capsule of r=11.31 over a 16x16 box, so 16/sqrt(2) at the corner
REACH_MARGIN = 18.0           # slack for the player drifting between the attack starting and the hit frame:
                              # you run at 120 u/s and the collision push adds to that, so the dragon's old
                              # 4.6 units of margin was eaten before the hit frame arrived every time
# how much of the attack's reach the brain stands off at, so it walks into range rather than through the player
STANDOFF = 0.75
# The colliders are a little bigger than the models: the user found the small creatures unnecessarily hard to
# hit. The frog is exempt — nothing needs to swing at a frog.
DEFAULT_HITBOX = 1.35


def guid(kind, slot):
    h = hashlib.sha1(("voidmage/monster/%s/%s" % (kind, slot)).encode()).hexdigest()
    return "%s-%s-%s-%s-%s" % (h[0:8], h[8:12], h[12:16], h[16:20], h[20:32])


def brain(kind, health, stand):
    """One brain per monster: no shield, no charge, a short cooldown and a little footwork. A critter's brain
    only ever supplies Wander and Health -- SkeletonNpc.Passive stops it looking for anyone.

    `stand` (PreferredRange) MUST be outside the physical floor. `_holdingGround` — the only thing that ever
    calls Stop() in the engage loop — latches on `d <= PreferredRange`, so a preferred range the colliders
    make unreachable means the monster runs at the player forever and depenetration shoves them across the
    room. That was the spider, standing off at 30 with a floor of 38."""
    critter = kind["role"] == "critter"
    return {
        # 20 % faster than the pace they shipped at (1.13 / 2.25): the user wanted every enemy swinging
        # more often without touching how fast anything moves (2026-09-17)
        "AttackCooldownMin": 0.0 if critter else 0.72,
        "AttackCooldownMax": 0.0 if critter else 1.44,
        "ChargeChance": 0.0,
        "ShieldBashChance": 0.0,
        "GuardChance": 0.0,
        "GuardHoldMin": 0.0,
        "GuardHoldMax": 0.0,
        "GuardAfterHurtChance": 0.0,
        "GuardApproachRange": 0,
        "PreferredRange": round(stand, 1),
        "StrafeChance": 0.0 if critter else 0.25,
        "StrafeSecondsMin": 0.4,
        "StrafeSecondsMax": 1.0,
        "WalkWhileEngaged": False,
        "BackOffChance": 0.0,
        # a critter never aggroes at all; the hostiles notice you across a room but not a hall
        # (650, trimmed from 800 with every other enemy on 2026-09-19 for the smaller, denser floors)
        "AggroRange": 0.0 if critter else 650.0,
        "Wander": True,
        "Health": health,
        "Jitter": 0.15,
        "__references": [],
        "__version": 0,
    }


def attack_spec(name, clip, clips, u_per_bu, damage, lunge, push, floor):
    c = clips[clip]
    hit = c["hit_time"]
    # the bite's own reach measured off the clip, but never less than the two bodies can physically close to
    reach = max(c["reach"] * u_per_bu + floor, floor + REACH_MARGIN)
    return {
        "Name": clip,
        "HitTime": round(hit, 3),
        "Duration": round(c["seconds"], 3),
        "HitRange": round(reach, 1),
        # a flier is carried in by code (LungeSpeed) the way the Tormented Soul is; a walker arrives on its feet
        "LungeBeforeHit": round(lunge * hit, 1),
        "ArcDegrees": 55.0,
        "Damage": damage,
        "Push": push,
        "FaceLockBefore": 0.1,
        "ArmorFrom": -1.0,
        "LungeSpeed": lunge,
        "MinRange": 0.0,
        "OpenerRange": 0.0,
    }


def speeds(kind, clips, u_per_bu):
    """(walk, jog, run) in units/s, and the slide each one implies (0 = the clip carries it)."""
    loco, playback = kind["loco"], kind["loco"]["playback"]
    if kind["speed"] is not None:
        return kind["speed"], None
    walk_clip, fast_clip = loco["move"], loco.get("fast")
    natural = clips[walk_clip].get("stance_speed")
    if not natural:
        raise SystemExit("%s: no stance speed measured for '%s' and no explicit `speed` in monster_kinds.py"
                         % (kind["name"], walk_clip))
    walk = natural * u_per_bu * playback
    if fast_clip:
        run = clips[fast_clip]["stance_speed"] * u_per_bu * playback
    else:
        # one gait: the blend plays that same clip at every radius, so every tier travels at its speed
        walk = run = walk
    return (round(walk, 1), round(run, 1), round(run, 1)), (walk_clip, natural, playback)


def component(type_name, kind, slot, **fields):
    base = {
        "__type": type_name,
        "__guid": guid(kind, slot),
        "__enabled": True,
        "Flags": 0,
        "OnComponentDestroy": None,
        "OnComponentDisabled": None,
        "OnComponentEnabled": None,
        "OnComponentFixedUpdate": None,
        "OnComponentStart": None,
        "OnComponentUpdate": None,
    }
    base.update(fields)
    return base


def build(name):
    k = monster_kinds.kind(name)
    clips = monster_kinds.load(k, "clips_json")["clips"]
    model = monster_kinds.load(k, "model_json")
    ragdoll = monster_kinds.load(k, "ragdoll_json")
    u_per_bu = model["units_per_bu"]
    critter = k["role"] == "critter"
    height = model["height"]
    hover = k["hover"]

    (walk, jog, run), slide = speeds(k, clips, u_per_bu)

    # The capsule wraps the body where it actually is: a flier's sits at its hover height. Its radius covers
    # the width and as much of the length as is reasonable, but a long animal's length is capped at twice its
    # width first -- taking the rat's 66-unit nose-to-tail straight would give a 52-unit-wide barrel for a
    # creature 14 across. `hitbox` then oversizes the lot a little so the small ones are not fiddly to hit.
    hitbox = k["hitbox"]
    cap_radius = round(max(max(model["width"], min(model["depth"], model["width"] * 2.0)) * 0.5, 8.0) * hitbox, 1)
    cap_bottom = round(hover + (4.0 if hover == 0 else -height * 0.35), 1)
    cap_top = round(cap_bottom + max(height * 0.8, 14.0) * hitbox, 1)
    agent_height = round(max(cap_top, 24.0), 1)
    # what the two colliders allow: BOTH radii, because every range in SkeletonNpc is centre-to-centre
    floor = round(cap_radius + PLAYER_HALF_WIDTH, 1)

    # stand a little outside the floor so `_holdingGround` can latch and it stops instead of bulldozing
    stand = floor + 6.0
    attacks = [attack_spec("Sword%s" % "AB"[i], clip, clips, u_per_bu, k["damage"],
                           k["lunge"], 60.0 + 20.0 * i, floor)
               for i, clip in enumerate(k["attacks"])]
    # SkeletonNpc always has four specs; the unused ones repeat the first so a stray roll cannot fire an
    # attack with no clip. The brain never rolls charge or bash (both chances are 0 in the brain above).
    while len(attacks) < 2 and attacks:
        attacks.append(dict(attacks[0]))

    npc_fields = {
        "Profile": None,
        "KindName": "monster_" + name,
        "DamageScale": 1.0,
        "SecondSwordDamageScale": 1.0,
        "ApplyRootMotion": False,           # no clip in either pack has root motion
        "WalkSpeed": walk, "JogSpeed": jog, "RunSpeed": run,
        "EngageRange": round(max(attacks[0]["HitRange"] * 1.6, 70.0), 1) if attacks else 80.0,
        "GuardPoints": 0,
        "HitHeightTolerance": 72.0,
        # a flier's body is up at its hover height, and everything that aims at a "chest" reads these
        "BodyHeight": round(hover + height, 1),
        "EyeHeight": round(hover + height * 0.7, 1),
        "DeathLingerSeconds": 0.3,
        # only the bat and the dragon were given a hit clip; everything else takes damage with a flash
        "CanStagger": k["stagger"],
        "HurtSeconds": round(clips["hit"]["seconds"] * 0.55, 2) if k["stagger"] else 0.0,
        "Passive": critter,
        # small animals are too light to arm a trap; they can still be caught by one already firing
        "TriggersTraps": False,
    }
    if k["hop"]:
        hop, clip = k["hop"], clips[k["hop"]["clip"]]
        # the travel window is the clip's own measured airborne span, so the body moves exactly while the
        # feet are off the ground and is still for the crouch and the landing
        lift, land = clip.get("air_start"), clip.get("air_end")
        if lift is None or land is None or land <= lift:
            raise SystemExit("%s: '%s' has no airborne window measured; rerun tools/measure_monster_clips.py"
                             % (name, hop["clip"]))
        playback = k["loco"]["playback"]
        npc_fields.update({
            "Hops": True,
            "HopSpeed": round(hop["distance"] / ((land - lift) / playback), 1),
            "HopSeconds": round(clip["seconds"] / playback, 3),
            "HopLiftTime": round(lift / playback, 3),
            "HopLandTime": round(land / playback, 3),
            "HopRestMin": hop["rest"][0],
            "HopRestMax": hop["rest"][1],
        })
    if attacks:
        npc_fields["SwordA"] = attacks[0]
        npc_fields["SwordB"] = attacks[1]
    body_fields = {
        "FlashMaterial": "materials/hit_flash.vmat",
        "Footsteps": False,
        "Ragdoll": True,
        "WalkSpeed": walk, "JogSpeed": jog, "RunSpeed": run,
        "HoverHeight": hover,
        "HoverBob": 3.0 if hover > 0 else 0.0,
        "HoverBobHz": 0.8,
        # these packs ship no sounds, and the skeleton's bone-break death on a frog would be worse than silence
        "AttackSound": "",
        "DeathSound": "",
        "BlockSound": "",
        "ShieldBone": "",
        "WeaponBodyGroups": False,          # no monster vmdl has the skeletons' sword/shield body groups
        "Bleeds": True,                     # flesh: the sword throws blood + a floor splat (HitVfx.FleshHit), not bone sparks
        # Corpse welding is MONSTER-ONLY and this flag is what keeps MonsterCorpse off a skeleton corpse. A
        # body-less bone freezes to the corpse root (ModelPhysics only overrides bones that own a body), which
        # is invisible on a skeleton's barely-weighted fingers and was half the rat's mesh here.
        "WeldCorpseBones": True,
        # only for the bones these packs parent to the ARMATURE ROOT -- the rat's and spider's feet. Every
        # other body-less bone rides its parent, which needs no data at all.
        "CorpseWelds": ["%s:%s" % (bone, target) for bone, target in sorted(ragdoll.get("welds", {}).items())],
    }

    prefab = {
        "RootObject": {
            "__guid": guid(name, "root"),
            "__version": 2,
            "Flags": 0,
            "Name": "monster_" + name,
            "Position": "0,0,0",
            "Rotation": "0,0,0,1",
            "Scale": "1,1,1",
            # a critter keeps the same tags -- it is killable by exactly the paths that damage anything else, and
            # SkeletonNpc.Passive (not the tags) is what stops it fighting back -- plus `critter`, which
            # ProjectSettings/Collision.config pairs with `player` as Ignore: you can cut a frog down, you cannot
            # kick it around or trip over one (user, 2026-09-18)
            "Tags": "enemy,npc,critter" if critter else "enemy,npc",
            "Enabled": True,
            "NetworkMode": 1,
            "NetworkFlags": 0,
            "NetworkOrphaned": 0,
            "NetworkTransmit": True,
            "OwnerTransfer": 1,
            "Components": [
                component("Sandbox.NavMeshAgent", name, "agent", Acceleration=1360, AllowDefaultArea=True,
                          AllowedAreas=[], AutoTraverseLinks=True, ForbiddenAreas=[], Height=agent_height,
                          MaxSpeed=run, Radius=16, Separation=0.5, UpdatePosition=True, UpdateRotation=False),
                component("Sandbox.CapsuleCollider", name, "capsule", ColliderFlags=0, Elasticity=None,
                          End="0,0,%g" % cap_top, Friction=None, IsTrigger=False, OnObjectTriggerEnter=None,
                          OnObjectTriggerExit=None, OnTriggerEnter=None, OnTriggerExit=None, Radius=cap_radius,
                          RollingResistance=None, Start="0,0,%g" % cap_bottom, Static=False, Surface=None,
                          SurfaceVelocity="0,0,0"),
                component("VoidMage.Npc.SkeletonNpc", name, "npc", **npc_fields),
                component("VoidMage.Npc.SkeletonBody", name, "body", **body_fields),
            ],
            "Children": [{
                "__guid": guid(name, "visual"),
                "Flags": 0,
                "Name": "body",
                "Position": "0,0,%g" % hover,
                "Rotation": "0,0,0,1",
                "Scale": "1,1,1",
                "Tags": "",
                "Enabled": True,
                "Components": [component(
                    "Sandbox.SkinnedModelRenderer", name, "renderer",
                    AnimationGraph=k["animgraph_rel"], BodyGroups=18446744073709551615, BoneMergeTarget=None,
                    CreateAttachments=False, CreateBoneObjects=False, LodOverride=None, MaterialGroup=None,
                    MaterialOverride=None, Materials=None, Model=k["vmdl_rel"], Morphs={},
                    Parameters={"bools": {}, "ints": {}, "floats": {}, "vectors": {}, "rotations": {}},
                    PlaybackRate=1,
                    RenderOptions={"GameLayer": True, "OverlayLayer": False, "BloomLayer": False, "AfterUILayer": False},
                    RenderType="On", Sequence={"Name": None, "Looping": True, "Blending": False},
                    Tint="1,1,1,1", UseAnimGraph=True)],
                "Children": [],
            }],
            "__properties": {"NetworkInterpolation": True, "TimeScale": 1, "WantsSystemScene": True, "Metadata": {}},
            "__variables": [],
        },
        "ResourceVersion": 2,
        "ShowInMenu": False,
        "MenuPath": None,
        "MenuIcon": None,
        "DontBreakAsTemplate": False,
        "__references": [],
        "__version": 2,
    }

    os.makedirs(os.path.dirname(k["prefab"]), exist_ok=True)
    with open(k["prefab"], "w", newline="\n") as f:
        json.dump(prefab, f, indent=2)
    os.makedirs(os.path.dirname(k["brain"]), exist_ok=True)
    with open(k["brain"], "w", newline="\n") as f:
        json.dump(brain(k, k["health"], stand), f, indent=2)

    note = "explicit" if slide is None else "clip %s %.2f bu/s x %.2f playback" % slide
    what = ", ".join("%s hit %.2fs reach %.0f start %.0f" % (
        a["Name"], a["HitTime"], a["HitRange"], a["HitRange"] + a["LungeBeforeHit"] * 0.75 - 4)
        for a in attacks[:len(k["attacks"])])
    if k["hop"]:
        what = "hops %.0f u at %.0f u/s over %.2f-%.2f s of a %.2f s clip, resting %.1f-%.1f s" % (
            k["hop"]["distance"], npc_fields["HopSpeed"], npc_fields["HopLiftTime"], npc_fields["HopLandTime"],
            npc_fields["HopSeconds"], npc_fields["HopRestMin"], npc_fields["HopRestMax"])
    print("%-12s %-8s hp %4.0f dmg %3.0f  size %3.0f  speed %5.1f/%5.1f (%s)  hover %4.1f  capsule r%.0f %g..%g  %s" % (
        name, k["role"], k["health"], k["damage"], model["height"], walk, run, note, hover,
        cap_radius, cap_bottom, cap_top, what or "no attack"))


GEN_CS = os.path.join(ROOT, "Code", "Npc", "MonsterKinds.Generated.cs")


def write_cs():
    """The same table on the C# side: what the spawner clones and what the dungeon rolls."""
    rows = ""
    for name in sorted(monster_kinds.KINDS):
        k = monster_kinds.kind(name)
        lo, hi = k["group"]
        rows += '\t\tnew( "%s", "%s", "monster_%s", %s, %d, %d, %sf, %sf ),\n' % (
            name, k["prefab_rel"], name, "true" if k["role"] == "critter" else "false",
            lo, hi, k["weight"], k["health"])
    out = f'''// <auto-generated> by tools/gen_monster_prefabs.py from tools/monster_kinds.py. Do not edit. </auto-generated>
namespace VoidMage.Npc;

public static partial class MonsterKinds
{{
	/// <summary>One Quaternius monster: its prefab and brain, the group it arrives in, and how often it is rolled.</summary>
	public sealed record Entry( string Name, string Prefab, string Brain, bool Critter, int GroupMin, int GroupMax, float Weight, float Health );

	/// <summary>Every monster, critters included. `Critter` ones never fight (SkeletonNpc.Passive) and are placed as ambient life, not as an encounter.</summary>
	public static readonly Entry[] All =
	{{
{rows}	}};
}}
'''
    with open(GEN_CS, "w", newline="\n") as f:
        f.write(out)
    print("wrote", os.path.relpath(GEN_CS, ROOT))


def main():
    ap = argparse.ArgumentParser()
    monster_kinds.add_kind_arg(ap)
    args = ap.parse_args()
    for name in monster_kinds.selected(args.kind):
        build(name)
    write_cs()


if __name__ == "__main__":
    main()
