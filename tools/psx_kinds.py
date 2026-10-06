"""The PSX Horror-Fantasy Megapack monsters (user, 2026-09-14: "just import them and toonify them, will add animations
and purpose later"): 23 low-poly rigged monsters, one mesh + one baked 256/512/1024 texture each, T-pose only.

Source: `C:\\Users\\skipp\\Desktop\\Speelboord Studios\\PSX Horror-Fantasy Megapack\\PSX Horror-Fantasy Megapack\\<folder>\\`,
each folder an FBX (the rig + mesh, cm, no bind-time animation beyond a T-pose stack), a Godot GLB of the same model,
and loose PNGs. The GLB EMBEDS the texture the model was exported with, so gen_psx_assets.py takes the texture from
there: for five monsters the loose PNG differs from it (eyehead's is 256 vs the GLB's 512, the muscular abomination
ships three candidate PNGs and a 1024 in the GLB, three others are byte-different re-saves).

Three rig flavours, kept as they are (measured 2026-09-14 with tools/fbx_bounds.py): ten on AccuRig's UE export
(Epic names `root / pelvis / spine_01 / thigh_l` plus a `CC_Base_Pelvis` extra: 71-83 bones -- the same names as the
skeleton donor, so a retarget by name may be near for those), ten on the Reallusion CC rig (`RL_BoneRoot /
CC_Base_Hip / CC_Base_*`, 105-119 bones with `_end` leaf helpers), three on Mixamo (`mixamorig:Hips`, 33-49 bones;
toonify_psx.py strips the `mixamorig:` prefix, s&box bone names must not carry a colon).

`size` is the knob (the Quaternius monsters' rule): the model's LARGEST dimension in game units. The pack author
normalised nearly every model to 100 cm along its longest axis (the abomination is 175), so the source carries no
real-world scale and every monster needs a number here. First guesses: humanoids at the player's 72 u, the obvious
big ones 88, the dolls 48, the pig 72 long (= 37 tall). Rerun gen_psx_assets.py after changing one (no Blender).

Clips: a kind with `clips=True` lists every FBX in Assets/Animations/PsxHorror/<Mesh>/ in its vmdl (looping by LOOPING,
root motion when the `root` bone travels). tools/retarget_synty_to_rig.py writes those folders (the elkdemon carries
the Synty Idles pack's 165 Masculine clips).
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = r"C:\Users\skipp\Desktop\Speelboord Studios\PSX Horror-Fantasy Megapack\PSX Horror-Fantasy Megapack"
OUT_DIR = os.path.join(ROOT, "Assets", "Characters", "PsxHorror")
OUT_REL = "characters/psxhorror/"
TEX_DIR = os.path.join(ROOT, "Assets", "Textures", "PsxHorrorTextures")
TEX_REL = "textures/psxhorrortextures/"
MANIFEST = os.path.join(ROOT, "tools", "out", "psxhorror", "chars.json")
OUTLINE_MATERIAL = "PsxOutline"      # one outline vmat for the pack (Assets/Shaders/PsxOutline.vmat)
HUMAN, BIG, SMALL = 72.0, 88.0, 48.0


ANIM_DIR = os.path.join(ROOT, "Assets", "Animations", "PsxHorror")   # <ANIM_DIR>/<Mesh>/*.fbx = a monster's clips
ANIM_REL = "Animations/PsxHorror/"
# clip names that loop (the Synty Idles pack: `<stance>_loop` and the `base` idle; enters, exits and actions play once)
LOOPING = r"(^|_)loop$|^base$"


def kind(name, folder, fbx, glb, size=HUMAN, mesh=None, clips=False):
    """`mesh`: the vmdl's mesh node name (default: the capitalised kind name); `Psx<Mesh>` is its material / vmat.
    `clips`: list every FBX in Assets/Animations/PsxHorror/<Mesh>/ as an AnimFile (tools/retarget_synty_to_rig.py
    writes them there)."""
    mesh = mesh or name[0].upper() + name[1:]
    return dict(name=name, folder=os.path.join(SRC, folder), fbx=fbx, glb=glb, size=size, mesh=mesh, material="Psx" + mesh,
                out=name + ".fbx", clips=clips)


KINDS = {k["name"]: k for k in [
    kind("abomination2", "!NEW Abomination2", "abomination2.fbx", "abomination2.glb", BIG),
    kind("anomaly", "AnomalyMonster", "anomalyfbx.fbx", "anomalygodot.glb"),
    kind("devildemon", "Devil Demon", "devildemonfbx.fbx", "devildemongodot.glb", BIG),
    kind("abomination", "abomination", "final_rigged.fbx", "abomination.glb"),
    kind("alieninvader", "alieninvader", "alienfbx.fbx", "alieninvadergodot.glb"),
    kind("bigabomination", "bigabomination", "bigabominationfbx.fbx", "bigabominationgodot.glb", BIG),
    kind("bigfoot", "bigfoot", "bigfootfbx.fbx", "bigfootgodot.glb", BIG),
    kind("blackbutcher", "black butcher", "blackbutcherfbx.fbx", "blackbutchergodot.glb"),
    kind("bloodwraith", "bloodwraith", "bloodwraithfbx.fbx", "bloodwraithgodot.glb"),
    kind("clown1", "clowns pack/1", "final_rigged.fbx", "clown1godot.glb"),
    kind("clown2", "clowns pack/2", "cloaked_rigged.fbx", "clown2godot.glb"),
    kind("clown3", "clowns pack/3", "final_rigged.fbx", "clown3godot.glb"),
    # the Synty Idles pack (Masculine) retargeted onto its Mixamo rig, 2026-09-15: the user picks from them later
    kind("elkdemon", "elkdemon", "final_rigged.fbx", "elkdemongodot.glb", BIG, clips=True),
    kind("eyehead", "eyehead", "eyenoid_rigged.fbx", "eyeheadgodot.glb"),
    kind("greencyclope", "green cyclope", "green cyclope.fbx", "greencyclopegodot.glb", BIG),
    kind("greengoliath", "greengoliath", "greengoliathfbx.fbx", "greengoliathgodot.glb", BIG),
    kind("doll1", "horror dolls pack/1", "1_rigged.fbx", "doll1godot.glb", SMALL),
    kind("doll2", "horror dolls pack/2", "2_rigged.fbx", "doll2godot.glb", SMALL),
    kind("killerpig", "killer pig", "killerpigfbx.fbx", "killerpiggodot.glb"),
    kind("mrz", "mrZ", "mrZ.fbx", "mrzGodot.glb", mesh="MrZ"),
    kind("muscularabomination", "muscular abomination", "final.fbx", "finalglb.glb", BIG),
    kind("plaguedoctor", "plague doctor", "final_rigged.fbx", "plaguedoctorgodot.glb"),
    kind("werewolf", "werewolf", "werewolffbx.fbx", "werewolfgodot.glb", BIG),
]}


def selected(name):
    if name == "all":
        return sorted(KINDS)
    if name not in KINDS:
        raise SystemExit("unknown psx kind %r; one of all, %s" % (name, ", ".join(sorted(KINDS))))
    return [name]


def add_kind_arg(ap):
    ap.add_argument("--kind", default="all", help="a kind name or `all` (tools/psx_kinds.py)")
