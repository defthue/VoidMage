"""Pull the sprites, sprite metas and fonts out of Synty's two Dark Fantasy interface packages.

  python tools/extract_dfui.py [out_dir]          (default c:/tmp/dfui - keep it SHORT, see below)

Neither package is in the repo; both live in the asset library on the Desktop. A .unitypackage is a gzip tar of
guid-named folders, each holding `pathname`, `asset` and `asset.meta`. The .meta is kept next to every png because
that is where Unity stores the 9-slice borders (`spriteBorder: {x: left, y: bottom, z: right, w: top}`), and
tools/gen_ui_kit.py slices by them.

The out dir has to be short: the pack's own names run past 100 characters and Windows' 260 limit bites under the
scratchpad. Gamepad glyph sets are skipped (keyboard and mouse only).

Writes <out>/hud/... and <out>/menus/... with `Assets/Synty/` and the pack folder stripped, so e.g.
  <out>/hud/Sprites/DarkFantasy/SPR_DarkFantasy_Frame_Box_Large_02.png(.meta)
  <out>/menus/Fonts/Pirata One/PirataOne-Regular.ttf
"""
import os, sys, tarfile

LIB = r"C:\Users\skipp\Desktop\Speelboord Studios"
PACKS = (
    ("hud", "INTERFACE_Dark_Fantasy_HUD_Unity_2022_3_v1_1_1.unitypackage", "InterfaceDarkFantasyHUD/"),
    ("menus", "INTERFACE_Dark_Fantasy_Menus_Unity_2022_3_v1_0_1.unitypackage", "InterfaceDarkFantasyMenus/"),
)
KEEP = (".png", ".ttf", ".otf", ".pdf", ".txt")
OUT = sys.argv[1] if len(sys.argv) > 1 else "c:/tmp/dfui"

for tag, name, folder in PACKS:
    pkg = os.path.join(LIB, name)
    guid2path = {}
    with tarfile.open(pkg, mode="r|gz") as tf:
        for m in tf:
            if m.isfile() and m.name.endswith("/pathname"):
                guid = m.name.lstrip("./").split("/")[0]
                guid2path[guid] = tf.extractfile(m).read().decode("utf-8", "replace").split("\n")[0].strip()

    n = 0
    with tarfile.open(pkg, mode="r|gz") as tf:
        for m in tf:
            if not m.isfile():
                continue
            parts = m.name.lstrip("./").split("/")
            if len(parts) != 2 or parts[0] not in guid2path:
                continue
            p = guid2path[parts[0]]
            if not p.lower().endswith(KEEP):
                continue
            if "/Icons_Input/" in p and "MouseKeyboard" not in p:
                continue
            rel = p.replace("Assets/Synty/", "").replace(folder, "")
            if parts[1] == "asset":
                dst = os.path.join(OUT, tag, rel)
            elif parts[1] == "asset.meta" and p.lower().endswith(".png"):
                dst = os.path.join(OUT, tag, rel + ".meta")
            else:
                continue
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, "wb") as fh:
                fh.write(tf.extractfile(m).read())
            n += 1
    print(tag, "wrote", n, flush=True)
