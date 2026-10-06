"""The skeleton enemies: one entry per character mesh, with the weapons it can hold.

Every Dungeon skeleton character (slave, soldier 01/02, ...) sits on the SAME Epic-named rig (43 bones, identical
rest transforms, checked 2026-09-07 with tools/fbx_bounds.py), so the retargeted Sword & Shield clips in
Assets/Animations/SkeletonSS and the animgraph are shared; only the prep (sockets + weapons + outline), the ragdoll
capsules and the vmdl are per kind. prep_skeleton_npc.py, gen_skeleton_ragdoll.py and gen_skeleton_npc_vmdl.py all
take `--kind <name>`.

The Dungeon Realms skeletons (realms_01..03) are the exception: Synty shipped them on the classic-named rig
(`Hips`, `Shoulder_L`, `Finger_01`...) whose JOINT POSITIONS equal the Epic rig's to the millimetre but whose rest
orientations differ by 47-180 degrees (measured 2026-09-11), so a bone rename cannot carry the clips. Such a kind
names a `rig_fbx` donor (the slave's Epic rig) and a `bone_map`; prep re-skins the body onto the donor (vertex
groups renamed, fingertips/jaw/toes merged into their parents) and everything downstream is the shared rig again.
The Dark Fortress characters (the corpse) are the same classic rig with SIDED finger names (`Thumb_01_L`), all in
one Characters.fbx: `bone_map="synty_classic_sided"`, and prep keeps only `body_mesh` of the file.

The Dark Fantasy skeletons are on an EPIC-named rig that IS the donor's, measured 2026-09-12 against
SK_Chr_Skeleton_Slave_01: every shared joint at 0.000 cm except the donor's splayed left thumb (thumb_02_l 2.6 cm,
thumb_03_l 4.4 cm: he grips the broken sword), rest orientations 1-25 degrees off (so a plain rename would still
bind wrong), extra `jaw` + 7 `ik_*` bones, no `root` bone (the FBX Root-node quirk). They re-skin with the
identity map `bone_map="epic"` and `reskin_slack` for the thumb; no retarget. (tools/README.md used to say the
pelvis was 180 degrees off and the limbs 1-5 cm: that came from a raw node parse and was wrong.)

A weapon is (fbx path, mesh name in the exported skeleton FBX, grip, front). `grip` "own" derives the hold from the
mesh itself; "first" reuses the grip matrix of the first weapon of that hand (the slave's unbroken sword shares the
broken one's handle exactly, so it must sit identically). `front` is the mesh axis the shield's plate faces ("+z",
Synty's convention for every shield so far; "-z" for an oddly authored one). Body-group choice order = list order
(the game rolls an index).
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, "Assets", "DungeonPack")
FK_PACK = os.path.join(ROOT, "Assets", "FantasyKingdom")   # toonified FantasyKingdom props (copied from heden)
REALMS_PACK = os.path.join(ROOT, "Assets", "DungeonRealms")   # toonified Dungeon Realms props (toonify_realms.py)
DF_PACK = os.path.join(ROOT, "Assets", "DarkFantasy")         # toonified Dark Fantasy props + character attachments (toonify_synty.py)
CHARS = r"C:\Users\skipp\Desktop\Speelboord Studios\dungeonpack\FBX\Characters\Unreal_Characters"
REALMS_CHARS = r"C:\Users\skipp\Desktop\Speelboord Studios\POLYGON_Dungeons_Realms_SourceFiles_v5\FBX\Characters"
DF_CHARS = r"C:\Users\skipp\Desktop\Speelboord Studios\POLYGON_Dark_Fantasy_SourceFiles_v3\SourceFiles\FBX\Characters\Unreal_Characters"
RIVALS_CHARS = r"C:\Users\skipp\Desktop\Speelboord Studios\PolygonFantasyRivals_Source_Files\Source_Files\Characters\Unreal_Characters"
# Synty's Polygon ANIMATION rig standing in its canonical T-pose (the baselocomotion pack's additive T-pose clip):
# retarget_synty.py's reference rest. It has to be a clip on the animation rig itself, not a character file: the
# animation rig is authored with Y along the bone and pre-rotations only, the classic CHARACTER rig with X along the
# bone and post-rotations, so their bone frames differ by a per-bone constant even in the same pose (measured
# 2026-09-12: 132 deg on the pelvis for a 5 deg change of direction), while the clip files carry only their first
# frame as "rest". Same joints, same convention, known pose = an exact rest-delta.
POLYGON_TPOSE_CLIP = r"C:\Users\skipp\Desktop\Speelboord Studios\Animation\baselocomotion\Animations\Polygon\Neutral\Additive\TPose\A_TPose_Neut.fbx"
# Synty's own "Sword Animations" pack, classic-rig export (the `Sidekick` sibling folder is the other rig); retargeted
# onto the donor by tools/retarget_synty.py into Assets/Animations/SkeletonSword
SYNTY_SWORD_CLIPS = r"C:\Users\skipp\Desktop\Speelboord Studios\Animation\SwordAnimations\SwordAnimations\Polygon"
# the Fortress pack ships its 11 characters + 7 attachments in ONE file on one armature; prep keeps `body_mesh` only
FORTRESS_CHARS_FBX = r"C:\Users\skipp\Desktop\Speelboord Studios\POLYGON_Dark_Fortress_SourceFiles_v4\FBX\Characters.fbx"
IDLE_CLIP = r"C:\Users\skipp\Desktop\Speelboord Studios\Animation\simpleswordshieldanim_fbx\Idle\SSH_Idle_v001.fbx"
ANIMGRAPH = "animgraphs/skeleton_slave.vanmgrph"   # shared by every kind (gen_skeleton_animgraph.py)


GREATSWORD_SCALE = 0.25 / 0.3937   # the props' import_scale over the models' global scale
FK_SWORD_SCALE = 0.7               # FantasyKingdom's 152 cm hero blades brought to the greatswords' ~105 cm (user, 2026-09-11)
# the Epic-named rig every retargeted clip was made for; kinds on another Synty rig are re-skinned onto it
DONOR_RIG = os.path.join(CHARS, "SK_Chr_Skeleton_Slave_01.fbx")


def wep(name, mesh=None, grip="own", scale=1.0, front="+z", pack=PACK, seat=0.0):
    """`seat` (shields, cm): how far the shield sits OUT from the arm along its plate normal. The socket lands on the
    mesh centre by default, which is right for a thin plate; a thick domed shield (Realms Shield_07 is 47 cm deep,
    centre in the middle) buries half of itself in the forearm, so its seat is about its back half-depth."""
    return dict(fbx=os.path.join(pack, name + ".fbx"), mesh=mesh or name, grip=grip, scale=scale, front=front, seat=seat)


def attach(name, bone, pack=REALMS_PACK, offset=(0.0, 0.0, 0.0)):
    """A Synty character attachment (toonified: mesh + `_Outline`), authored at its bone's pivot in the character's
    own frame; prep parents it to `bone` (100 % weight) at that joint. The pack's Undead Knight ships headless.
    `offset` (cm, the character's frame: x right, y forward, z up) nudges the seat when the in-engine look asks."""
    return dict(fbx=os.path.join(pack, name + ".fbx"), mesh=name, bone=bone, offset=tuple(offset))


def realms_skeleton(n):
    """Dungeon Realms skeleton N: soldier-strength (prefab), gravestone shield, FantasyKingdom swords 03 / 04."""
    return dict(
        char_fbx=os.path.join(REALMS_CHARS, "Chr_Skeleton_%02d.fbx" % n),
        body_mesh="Chr_Skeleton_%02d" % n,
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SkeletonRealms"),
        model="skeleton_realms_%02d" % n,
        swords=[wep("SM_Wep_Sword_03", pack=FK_PACK, scale=FK_SWORD_SCALE), wep("SM_Wep_Sword_04", pack=FK_PACK, scale=FK_SWORD_SCALE)],
        shields=[wep("SM_Wep_Shield_Gravestone_01")],
        rig_fbx=DONOR_RIG,
        bone_map="synty_classic",        # prep_skeleton_npc.SYNTY_CLASSIC_TO_EPIC
        base_material="RealmsShader",    # the Realms atlas (Dungeons_2_Texture_01_A), not the DungeonPack one
    )


SLAVE_SWORDS = [wep("SM_Wep_BrokenSword_01"), wep("SM_Wep_UNBrokenSword", mesh="SM_Wep_UNBrokenSword_01", grip="first")]
SLAVE_SHIELDS = [wep("SM_Wep_Shield_Chitin_01")]
# the user sized the greatsword props at import_scale 0.25 against the 0.3937 everything else uses; the skeletons'
# copies are baked into their FBX, so the same 0.635 is folded into the mesh here (grip re-derived)
SOLDIER_SWORDS = [wep("SM_Wep_GreatSword_01", scale=GREATSWORD_SCALE), wep("SM_Wep_Greatsword_Curved_01", scale=GREATSWORD_SCALE)]
SOLDIER_SHIELDS = [wep("SM_Wep_Shield_Heater_01"), wep("SM_Wep_Shield_Heater_02")]
# the donor's left thumb is splayed round the broken sword's grip; the DF thumb joints sit 2.6 / 4.4 cm away
DF_RESKIN_SLACK = {"thumb_02_l": 5.0, "thumb_03_l": 5.0}
DF_SWORD_SCALE = 0.6   # Dark Fantasy's 178.5 cm Sword_01 -> ~107 cm (the size the user picked for the FantasyKingdom blades)
DEMON_SWORDS = [wep("SM_Wep_Sword_01", pack=DF_PACK, scale=DF_SWORD_SCALE)]
# the swordsman's clip set (gen_skeleton_npc_vmdl.py `clip_dirs`: (folder under Assets/Animations, name regex or None)):
# every Sword Animations clip, plus the Sword & Shield locomotion the graph's loco blend plays
SWORDSMAN_CLIPS = [("SkeletonSword", None), ("SkeletonSS", r"^(walk|jog)_(f|b|l|r|bl|br)$|^run$|^idle$")]
SWORDSMAN_GRAPH = "animgraphs/swordsman.vanmgrph"   # gen_swordsman_animgraph.py, shared by both skins


def df_skeleton(name, source, swords, shields, prefab_from, helmet=None, prefab_npc=None, prefab_body=None):
    """A Dark Fantasy skeleton skin (see the module docstring for the rig): `source` is the SK_ file name,
    `helmet` the pack's separate helm attachment (the bodies all carry a skull; the helm sits over it)."""
    return dict(
        char_fbx=os.path.join(DF_CHARS, source + ".fbx"),
        body_mesh="SM_" + source[3:],   # SK_Chr_Skeleton_01 -> SM_Chr_Skeleton_01 (prep accepts any name when the file has one mesh)
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SkeletonDarkFantasy"),
        model="skeleton_" + name,
        swords=swords, shields=shields,
        attachments=[attach(helmet, bone="head", pack=DF_PACK)] if helmet else [],
        rig_fbx=DONOR_RIG,
        bone_map="epic",
        reskin_slack=DF_RESKIN_SLACK,
        base_material="DarkFantasyShader",
        prefab_from=prefab_from,          # gen_skeleton_prefab.py: the prefab whose stats this kind inherits
        prefab_npc=prefab_npc or {},
        prefab_body=prefab_body or {},
    )


KINDS = {
    "slave": dict(
        char_fbx=os.path.join(CHARS, "SK_Chr_Skeleton_Slave_01.fbx"),
        body_mesh="Character_Skeleton_Slave_01",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SkeletonSlave"),
        model="skeleton_slave_01",
        swords=[wep("SM_Wep_BrokenSword_01"), wep("SM_Wep_UNBrokenSword", mesh="SM_Wep_UNBrokenSword_01", grip="first")],
        shields=[wep("SM_Wep_Shield_Chitin_01")],
    ),
    "soldier_01": dict(
        char_fbx=os.path.join(CHARS, "SK_Chr_Skeleton_Soldier_01.fbx"),
        body_mesh="Character_Skeleton_Soldier_01",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SkeletonSoldier"),
        model="skeleton_soldier_01",
        # the user sized the greatsword props at import_scale 0.25 against the 0.3937 everything else uses; the
        # skeleton's copies are baked into its FBX, so the same 0.635 is folded into the mesh here (grip re-derived)
        swords=[wep("SM_Wep_GreatSword_01", scale=GREATSWORD_SCALE), wep("SM_Wep_Greatsword_Curved_01", scale=GREATSWORD_SCALE)],
        shields=[wep("SM_Wep_Shield_Heater_01"), wep("SM_Wep_Shield_Heater_02")],
    ),
    "soldier_02": dict(
        char_fbx=os.path.join(CHARS, "SK_Chr_Skeleton_Soldier_02.fbx"),
        body_mesh="Character_Skeleton_Soldier_02",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SkeletonSoldier"),
        model="skeleton_soldier_02",
        # the user sized the greatsword props at import_scale 0.25 against the 0.3937 everything else uses; the
        # skeleton's copies are baked into its FBX, so the same 0.635 is folded into the mesh here (grip re-derived)
        swords=[wep("SM_Wep_GreatSword_01", scale=GREATSWORD_SCALE), wep("SM_Wep_Greatsword_Curved_01", scale=GREATSWORD_SCALE)],
        shields=[wep("SM_Wep_Shield_Heater_01"), wep("SM_Wep_Shield_Heater_02")],
    ),
    # a flying, legless ghost: the same rig minus foot_l/r and ball_l/r (39 bones, identical transforms), so every
    # retargeted clip plays on it; built weaponless as a preview so the user can scroll the clips for anything usable
    # (no ragdoll yet: gen_skeleton_ragdoll.py's capsule table wants the feet)
    "tormented_soul": dict(
        char_fbx=os.path.join(CHARS, "SK_Chr_Tormented_Soul.fbx"),
        body_mesh="Character_Tormented_Soul",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "TormentedSoul"),
        model="tormented_soul",
        swords=[wep("SM_Wep_Dagger_07")],   # the user's split dagger (BladeShader / IronCrossguardPommelshader / WoodhandleShader); no shield, no guard
        shields=[],
        animgraph="animgraphs/tormented_soul.vanmgrph",   # gen_soul_animgraph.py: hover loco, three attacks
        ragdoll_skip=["PhysicsLegs"],   # thigh/calf bones exist but carry no mesh: no invisible legs under the corpse
    ),
    # the forge-hold's skeletons: three skins of one classic-rigged character, re-skinned onto the donor rig (see
    # the module docstring); mechanically the soldiers (Assets/Prefabs/skeleton_realms_0N.prefab, soldier_* brains)
    "realms_01": realms_skeleton(1),
    "realms_02": realms_skeleton(2),
    "realms_03": realms_skeleton(3),
    # the forge-hold's big one: the Dungeon Realms Undead Knight, same classic rig as the Realms skeletons (worst joint
    # 0.04 cm off, measured 2026-09-11), re-skinned the same way. Sword at its native 151 cm, whole model 1.2x (the
    # user wants him bigger than a skeleton). His own graph (gen_knight_animgraph.py: all 13 attack_* clips, the
    # guard) and prefab (gen_knight_prefab.py: the attack table, posture, the shield read) -- heden's boss grammar.
    "undead_knight": dict(
        char_fbx=os.path.join(REALMS_CHARS, "Chr_Undead_Knight_01.fbx"),
        body_mesh="Chr_Undead_Knight_01",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "UndeadKnight"),
        model="undead_knight",
        swords=[wep("SM_Wep_Sword_Medium_08", pack=REALMS_PACK)],
        # symmetric front/back (`front="-z"` if it faces the arm); 47 cm deep, so seated 18 cm out (user, 2026-09-12:
        # "sits too far in/out from the arm" with the centre on the socket)
        shields=[wep("SM_Wep_Shield_07", pack=REALMS_PACK, seat=18.0)],
        # the pack ships him headless: the user's pick is the full helm (Head_01 / Skull_01 / AnimalSkull_01 / Crown_01
        # are the other heads in the pack's Attachments folder)
        attachments=[attach("SM_Chr_Attach_Undead_Knight_Helmet_01", bone="head")],
        rig_fbx=DONOR_RIG,
        bone_map="synty_classic",
        base_material="RealmsShader",
        model_scale=1.2,   # gen_skeleton_npc_vmdl.py: ModelModifier_ScaleAndMirror = 0.3937 * this (render, ragdoll, root motion)
        animgraph="animgraphs/undead_knight.vanmgrph",
    ),
    # the Dark Fortress corpse: a slave WITHOUT a shield (no guard, no bash: corpse_* brains roll 0 for every guard
    # chance, GuardPoints 0 on the prefab is belt and braces), slave swords, and it BLEEDS (SkeletonBody.Bleeds ->
    # HitVfx.FleshHit instead of bone sparks). Classic rig with sided finger names, re-skinned like the Realms ones.
    "corpse": dict(
        char_fbx=FORTRESS_CHARS_FBX,
        body_mesh="SM_Chr_Corpse_01",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "FortressCorpse"),
        model="corpse",
        swords=SLAVE_SWORDS,
        shields=[],
        rig_fbx=DONOR_RIG,
        bone_map="synty_classic_sided",
        base_material="DarkFortressShader",
        prefab_from="skeleton",
        prefab_npc={"GuardPoints": 0},
        prefab_body={"Bleeds": True},
    ),
    # the Dark Fantasy skins (df_skeleton()): two plain slave variants, and one skin per armor tier next to the
    # DungeonPack soldiers (light = soldier_01's stats, heavy = soldier_02's; the tiers live in the prefabs/brains)
    "df_skeleton": df_skeleton("df_skeleton", "SK_Chr_Skeleton_01", SLAVE_SWORDS, SLAVE_SHIELDS, "skeleton"),
    # the flesh skeleton still has meat on it: it bleeds (user, 2026-09-12), the bare one does not
    "df_flesh": df_skeleton("df_flesh", "SK_Chr_Skeleton_Flesh_01", SLAVE_SWORDS, SLAVE_SHIELDS, "skeleton", prefab_body={"Bleeds": True}),
    "df_light": df_skeleton("df_light", "SK_Chr_Skeleton_LightArmor_01", SOLDIER_SWORDS, SOLDIER_SHIELDS, "skeleton_soldier_01",
                            helmet="SM_Chr_Attach_Skeleton_LightArmor_Helmet_01"),
    # the heavy tier's numbers, the same as skeleton_soldier_02.prefab carries by hand (a little stronger than light)
    "df_heavy": df_skeleton("df_heavy", "SK_Chr_Skeleton_HeavyArmor_01", SOLDIER_SWORDS, SOLDIER_SHIELDS, "skeleton_soldier_02",
                            helmet="SM_Chr_Attach_Skeleton_HeavyArmor_Helmet_01", prefab_npc={"DamageScale": 1.7, "GuardPoints": 4}),
    # the "swordsman", two skins (user, 2026-09-12/13): the Fantasy Rivals SpiritDemon and the Dark Fantasy Dark Lord on
    # Synty's OWN Sword Animations pack (Assets/Animations/SkeletonSword, retarget_synty.py) with the Sword & Shield
    # walk / jog / run for locomotion (the pack has none): `clip_dirs` lists both folders with a name filter on the
    # second. Both Unreal exports are the Rivals/DF Epic rig = the donor's joints (re-skin, `epic` map). One graph
    # (gen_swordsman_animgraph.py), one attack table + brain (gen_swordsman_prefab.py -> Assets/Prefabs/spirit_demon /
    # dark_lord.prefab, Assets/Npc/swordsman.brain), ragdolls from gen_skeleton_ragdoll.py.
    # The SpiritDemon body has two slots: Synty's translucent ghost material (no outline hull behind it) + the atlas.
    # The DF pack also ships SM_Chr_Attach_DarkLord_Mask_01 / _Cape_01 (Assets/DarkFantasy) as `attach(...)` options.
    # Sword: Dark Fantasy SM_Wep_Sword_01 for both for now (user, 2026-09-12: "just for visual, this one now; will give
    # them variety in swords later"); 178.5 cm native, brought to ~107 cm like the FantasyKingdom blades.
    "spirit_demon": dict(
        char_fbx=os.path.join(RIVALS_CHARS, "SK_Character_SpiritDemon.fbx"),
        body_mesh="SK_Character_SpiritDemon",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "SpiritDemon"),
        model="spirit_demon",
        swords=DEMON_SWORDS, shields=[],
        rig_fbx=DONOR_RIG,
        bone_map="epic",
        reskin_slack=DF_RESKIN_SLACK,
        base_material="FantasyRivalsShader",
        slot_materials={"_Ghost2": "FantasyRivalsGhost", "lambert39": "FantasyRivalsShader"},
        no_outline=["FantasyRivalsGhost"],
        clip_dirs=SWORDSMAN_CLIPS,
        animgraph=SWORDSMAN_GRAPH,
    ),
    "dark_lord": dict(
        char_fbx=os.path.join(DF_CHARS, "SK_Chr_DarkLord_Male_01.fbx"),
        body_mesh="SM_Chr_DarkLord_Male_01",
        out_dir=os.path.join(ROOT, "Assets", "Characters", "DarkLord"),
        model="dark_lord",
        swords=DEMON_SWORDS, shields=[],
        rig_fbx=DONOR_RIG,
        bone_map="epic",
        reskin_slack=DF_RESKIN_SLACK,
        base_material="DarkFantasyShader",
        clip_dirs=SWORDSMAN_CLIPS,
        animgraph=SWORDSMAN_GRAPH,
    ),
}


def kind(name):
    if name not in KINDS:
        raise SystemExit("unknown skeleton kind %r; one of %s" % (name, ", ".join(KINDS)))
    k = dict(KINDS[name])
    k["name"] = name
    k.setdefault("animgraph", ANIMGRAPH)
    k["fbx"] = os.path.join(k["out_dir"], k["model"] + ".fbx")
    k["vmdl"] = os.path.join(k["out_dir"], k["model"] + ".vmdl")
    k["physics_prefab"] = os.path.join(k["out_dir"], "prefabs", k["model"] + "_physicsshapelist.vmdl_prefab")
    rel = os.path.relpath(k["out_dir"], os.path.join(ROOT, "Assets")).replace("\\", "/").lower()
    k["fbx_rel"] = rel + "/" + k["model"] + ".fbx"
    k["physics_rel"] = rel + "/prefabs/" + k["model"] + "_physicsshapelist.vmdl_prefab"
    return k


def add_kind_arg(ap):
    ap.add_argument("--kind", default="slave", choices=sorted(KINDS), help="which skeleton (skeleton_kinds.py)")
