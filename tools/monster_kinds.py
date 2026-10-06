"""The Quaternius monster kinds imported into the game, and where each one's files land.

Two packs, both Quaternius, both shipping .blend files with the clips as plain Blender actions
(no NLA, no root motion anywhere: every clip animates in place, so the code moves the body the
way it already does for the Tormented Soul):

    Monster Pack Animated by Quaternius   Bat, Dragon   (mesh + a SEPARATE one-bone eye armature)
    Easy Animated Enemy Pack - Jan 2019   Frog, Rat, Snake, Snake_Angry, Spider, Wasp

Every monster is its OWN rig (different bone names and counts), so unlike the skeleton kinds
nothing is shared: one FBX, one clip folder, one vmdl and one set of vmats each.

`size` is the knob: the max dimension the model should have IN GAME, in s&box units (1 u = 1 inch;
the player is ~72 u tall). prep_monster.py measures the rest pose and gen_monster_assets.py turns
the pair into the vmdl's scale modifier, so retuning a monster's size is one number here plus a
rerun of gen_monster_assets.py (no Blender, no re-export).

`materials` renames the pack's material slots to the canonical `Monster<Kind><Slot>` names the vmdl
remaps onto Assets/Shaders/*.vmat. Slots not listed keep their pack name (capitalised).
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESKTOP = r"C:\Users\skipp\Desktop\Speelboord Studios"
QUATERNIUS = os.path.join(DESKTOP, "Monster Pack Animated by Quaternius", "Blend")
EASY = os.path.join(DESKTOP, "Easy Animated Enemy Pack - Jan 2019", "Easy Animated Enemy Pack - Jan 2019", "Blends")

# Shared material identities. The outline shell is one material for every monster (heden_outline.shader);
# the surface materials are per monster+slot so each colour is its own tweakable vmat.
OUTLINE_MATERIAL = "MonsterOutline"
FLAT_TEXTURE = "textures/monsters/flat_white.png"

# ------------------------------------------------------------------ gameplay
#
# The user's brief (2026-09-09): simple, straightforward, LOW health and LOW damage monsters that come in
# small groups. The dragon is the exception: it comes SOLO and is somewhat stronger than the rest, but still
# weaker than a base skeleton slave (60 health, 12 damage). NEVER use the packs' death clips — every one of
# them ragdolls (SkeletonBody.Ragdoll, capsules from gen_monster_ragdoll.py).
#
# `loco` names the clips the locomotion blend uses and its playback speed. Speeds are NOT free: every clip
# animates in place, so the body may only travel as fast as the clip's own stance feet do (measured into
# tools/out/monsters/<kind>_clips.json by measure_monster_clips.py) times `playback`, or the feet slide --
# the lesson the skeleton walk cycles taught. gen_monster_prefabs.py derives WalkSpeed/RunSpeed from the
# measurement and prints the slide, so the one knob for "this thing is too slow" is `playback`.
# The footless ones (snake, and the three fliers) have no ground truth and no contact to betray a slide, so
# they carry `speed` outright.
#
# `attacks` are (clip, animgraph kind) in order; hit time, duration and reach all come from the measured
# clips. `stagger` is whether the pack gave it a hit reaction at all -- only the bat and the dragon have one,
# so everything else takes damage with a flash and no clip (SkeletonNpc.CanStagger).

KINDS = {
    "bat": dict(
        blend=os.path.join(QUATERNIUS, "Bat.blend"),
        armature="BatArmature", body="Bat",
        # the eyes ship on their own never-animated one-bone armature: they would hang in the air while
        # the bat flies, so prep joins them into the body and skins them 100% to this bone
        eyes=("Eyes", "EyeArmature", "Face"),
        size=30.0,
        role="hostile", health=12.0, damage=2.5,
        loco=dict(move="flying", playback=1.0), speed=(90.0, 120.0, 150.0),
        hover=42.0, attacks=("attack", "attack2"), lunge=220.0, stagger=True,
        group=(2, 4), weight=1.0,
    ),
    "dragon": dict(
        blend=os.path.join(QUATERNIUS, "Dragon.blend"),
        armature="DragonArmature", body="Dragon",
        eyes=("Eyes", "EyeArmature", "Face"),
        size=48.0,
        # the one that comes alone and hits harder -- still under a skeleton slave's 60 / 12
        role="hostile", health=22.0, damage=4.5,
        loco=dict(move="flying", playback=1.0), speed=(100.0, 140.0, 180.0),
        # low and short-armed (user, 2026-09-23: as the Colossus's minion it hovered out of the sword's swing and
        # lunged in from too far to be answered): hover 54 -> 34, lunge 250 -> 140 (the bite's own reach stays)
        hover=34.0, attacks=("attack", "attack2"), lunge=140.0, stagger=True,
        group=(1, 1), weight=0.35,
    ),
    "frog": dict(
        blend=os.path.join(EASY, "Frog.blend"),
        armature="Armature", body="Frog",
        size=18.0,
        # NOT an enemy: ambient life. It hops about, never attacks, never chases, and can be killed.
        role="critter", health=1.0, damage=0,   # one hit point, as in the village (user, 2026-09-20)
        # A HOPPER, not a walker. Its locomotion is a one-shot per hop, not a looping clip under a constant
        # speed: the jump crouches for its first third and settles for its last, so travelling the whole time
        # reads as a frog skating along with a jump animation playing. `distance` is the ground it covers per
        # hop, spent entirely inside the clip's measured airborne window; `rest` is the stillness between hops.
        loco=dict(idle="idle", move="idle", playback=1.0), speed=(45.0, 45.0, 45.0),
        hop=dict(clip="jump", distance=65.0, rest=(0.5, 1.5)),
        hitbox=1.0,          # nothing needs help hitting a frog, and a fat collider on one reads badly
        hover=0.0, attacks=(), lunge=0.0, stagger=False,
        group=(1, 3), weight=1.0,
    ),
    "rat": dict(
        blend=os.path.join(EASY, "Rat.blend"),
        armature="Armature", body="Rat",
        # the user's call: three times the size it was imported at (a dungeon rat, not a house rat)
        size=66.0,
        role="hostile", health=12.0, damage=2.5,
        # the only monster with two gaits: a walk cycle and a run cycle. Playback dropped from 2.5 when the
        # size tripled: speed is `stance-foot speed x units_per_bu x playback` and units_per_bu scales with
        # `size`, so without this the giant rat would run at 253 u/s -- faster than a skeleton. The user asked
        # for it to stay at the pace it had, so the two factors cancel and the feet still never slide.
        loco=dict(idle="idle", move="walk", fast="run", playback=1.66),
        ragdoll_max=16,      # 4 legs plus a tail; everything past this is welded, not jointed
        hover=0.0, attacks=("attack",), lunge=0.0, stagger=False,
        group=(3, 5), weight=1.2,
    ),
    "snake": dict(
        blend=os.path.join(EASY, "Snake.blend"),
        armature="SnakeArmature", body="Snake",
        size=34.0,
        role="hostile", health=12.0, damage=2.5,
        # no feet at all: a slither cannot slide, so the speed is free
        loco=dict(idle="idle", move="walk", playback=1.6), speed=(70.0, 70.0, 70.0),
        hover=0.0, attacks=("attack",), lunge=0.0, stagger=False,
        ragdoll_max=13,      # its whole chain: head, body, tail; the tongue is welded
        group=(2, 3), weight=1.0,
    ),
    "snake_angry": dict(
        blend=os.path.join(EASY, "Snake_Angry.blend"),
        armature="SnakeArmature", body="Snake",
        # same mesh, same slot names, same colours as `snake`: share its vmats instead of writing seven more
        material_kind="snake",
        size=34.0,
        # the user: same enemy, same stats, just a different appearance. The dungeon picks one at random.
        role="hostile", health=12.0, damage=2.5,
        loco=dict(idle="idle", move="walk", playback=1.6), speed=(70.0, 70.0, 70.0),
        hover=0.0, attacks=("attack",), lunge=0.0, stagger=False,
        ragdoll_max=13,      # its whole chain: head, body, tail; the tongue is welded
        group=(2, 3), weight=1.0, twin_of="snake",
    ),
    "spider": dict(
        blend=os.path.join(EASY, "Spider.blend"),
        armature="HumanArmature", body="Cube",
        # the pack left these on Blender's default names
        materials={"Material": "Body", "Material.001": "Markings"},
        # the user's call: twice the size it was imported at
        size=60.0,
        role="hostile", health=12.0, damage=2.5,
        # The one monster whose legs are DELIBERATELY out of step with the ground. Playback 4.5 was what the
        # derived speed needed (stance 4.03 bu/s x 10.11 u/bu x 4.5 = 183 u/s), but at that rate the legs read
        # as a blur, far faster than the spider visibly travels. The user's call: keep the speed, halve the
        # frenzy. Speed is therefore PINNED here rather than derived, which is the only way to break the
        # feet-match rule, and it costs some foot slide at a full-speed charge.
        loco=dict(idle="idle", move="walk", playback=2.0), speed=(183.3, 183.3, 183.3),
        ragdoll_max=16,      # 8 legs: one body per leg root, the segments past it welded
        hover=0.0, attacks=("attack",), lunge=0.0, stagger=False,
        group=(2, 4), weight=1.2,
    ),
    "wasp": dict(
        blend=os.path.join(EASY, "Wasp.blend"),
        armature="Armature", body="Wasp",
        size=26.0,
        role="hostile", health=12.0, damage=2.5,
        # the only rig in either pack whose head points +X instead of -Y, so it flew sideways until this
        yaw=-90.0,
        loco=dict(move="flying", playback=1.0), speed=(90.0, 130.0, 160.0),
        hover=40.0, attacks=("attack",), lunge=240.0, stagger=False,
        group=(2, 4), weight=1.0,
    ),
}

HOSTILE = [k for k, v in KINDS.items() if v["role"] == "hostile"]
CRITTERS = [k for k, v in KINDS.items() if v["role"] == "critter"]


def kind(name):
    if name not in KINDS:
        raise SystemExit("unknown monster %r; known: %s" % (name, ", ".join(sorted(KINDS))))
    k = dict(KINDS[name])
    title = "".join(p.capitalize() for p in name.split("_"))   # bat -> Bat, snake_angry -> SnakeAngry
    k.setdefault("materials", {})
    k.setdefault("eyes", None)
    k.setdefault("material_kind", name)
    k["name"] = name
    k["title"] = title
    k["mat_prefix"] = "Monster" + "".join(p.capitalize() for p in k["material_kind"].split("_"))
    k["body_mesh"] = title                                     # the mesh object name inside the exported FBX
    k["outline_mesh"] = title + "_Outline"
    k["dir"] = os.path.join(ROOT, "Assets", "Characters", "Monsters", title)
    k["fbx"] = os.path.join(k["dir"], name + ".fbx")
    k["fbx_rel"] = "Characters/Monsters/%s/%s.fbx" % (title, name)
    k["vmdl"] = os.path.join(k["dir"], name + ".vmdl")
    k["clip_dir"] = os.path.join(ROOT, "Assets", "Animations", "Monsters", title)
    k["clip_rel"] = "Animations/Monsters/%s/" % title
    k["manifest"] = os.path.join(ROOT, "tools", "out", "monsters", name + ".json")
    k["clips_json"] = os.path.join(ROOT, "tools", "out", "monsters", name + "_clips.json")
    k["model_json"] = os.path.join(ROOT, "tools", "out", "monsters", name + "_model.json")
    k["ragdoll_json"] = os.path.join(ROOT, "tools", "out", "monsters", name + "_ragdoll.json")
    k["animgraph"] = os.path.join(ROOT, "Assets", "animgraphs", "monster_" + name + ".vanmgrph")
    k["animgraph_rel"] = "animgraphs/monster_%s.vanmgrph" % name
    k["vmdl_rel"] = "characters/monsters/%s/%s.vmdl" % (title.lower(), name)
    k["physics_prefab"] = os.path.join(k["dir"], "prefabs", name + "_physicsshapelist.vmdl_prefab")
    k["physics_rel"] = "characters/monsters/%s/prefabs/%s_physicsshapelist.vmdl_prefab" % (title.lower(), name)
    k["prefab"] = os.path.join(ROOT, "Assets", "Prefabs", "monster_" + name + ".prefab")
    k["prefab_rel"] = "prefabs/monster_%s.prefab" % name
    k["brain"] = os.path.join(ROOT, "Assets", "Npc", "monster_" + name + ".brain")
    k.setdefault("speed", None)
    k.setdefault("twin_of", None)
    k.setdefault("hop", None)
    k.setdefault("yaw", 0.0)          # degrees about Z applied at export, to face the rig -Y like the rest
    k.setdefault("hitbox", 1.35)      # collider oversize; the reach and the standoff both derive from it
    return k


def load(kind, key):
    """One of the generated json side-cars (`manifest`, `clips_json`, `model_json`), with a useful error."""
    import json
    path = kind[key]
    if not os.path.exists(path):
        raise SystemExit("missing %s - run the tool that writes it (see tools/README.md) for %s" % (path, kind["name"]))
    with open(path) as f:
        return json.load(f)


def add_kind_arg(ap):
    ap.add_argument("--kind", default="all", help="monster name, or 'all' (%s)" % ", ".join(sorted(KINDS)))


def selected(name):
    return sorted(KINDS) if name == "all" else [name]
