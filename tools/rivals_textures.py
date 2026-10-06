"""Copy the POLYGON Fantasy Rivals textures into the project, lowercased, PNG only.

  python tools/rivals_textures.py <Source_Files>

The 16 atlases (FantasyRivals_Texture_01..04 x A..D: one UV layout, sixteen palettes) and the five
emissive masks land as-is in Assets/Textures/FantasyRivalsTextures/. The two TGAs (Mystic_Arms, the
ghost alpha gradient; Runes, the rune decal) are converted to PNG because everything else in the
project's texture folders is PNG. The emissive masks are copied for reference only: heden_toon has
no emissive input, so nothing reads them yet.
"""
import os, sys, shutil
from PIL import Image

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SRC = os.path.join(sys.argv[1], "Textures")
DST = os.path.join(PROJ, "Assets", "Textures", "FantasyRivalsTextures")
os.makedirs(DST, exist_ok=True)

n = 0
for f in sorted(os.listdir(SRC)):
    stem, ext = os.path.splitext(f)
    ext = ext.lower()
    out = os.path.join(DST, stem.lower() + ".png")
    if ext == ".png":
        shutil.copyfile(os.path.join(SRC, f), out)
    elif ext == ".tga":
        Image.open(os.path.join(SRC, f)).convert("RGBA").save(out)
    else:
        continue
    n += 1
    print("  %s -> %s" % (f, os.path.relpath(out, PROJ)))
print("%d textures in %s" % (n, os.path.relpath(DST, PROJ)))
