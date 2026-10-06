"""Copy the POLYGON Dark Fortress textures the toonified pack references into the project, lowercased, PNG only.

  python tools/darkfortress_textures.py <POLYGON_Dark_Fortress_SourceFiles_v4>

The twelve atlas palettes (Texture/Alts), the twelve emissive masks (reference only, nothing reads them),
the eight tiling diffuse maps the MaterialList's slots resolve to (Texture/Env: brick large/medium/small,
smooth stone light/dark, chipped stone, planks) and the five alpha-cut card textures (grunge, moss, thorns,
chains, spiderweb). Normal maps, the FX fire sheets and the unused floor-tile/rock textures stay behind.
Prints each file's alpha range.
"""
import os, sys, shutil
from PIL import Image

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SRC = os.path.join(sys.argv[1], "Texture")
DST = os.path.join(PROJ, "Assets", "Textures", "DarkFortressTextures")
os.makedirs(DST, exist_ok=True)

FILES = (
    [os.path.join("Alts", "PolygonDarkFortress_Texture_%s_%s.png" % (t, v)) for t in ("01", "02", "03", "04") for v in "ABC"]
    + [os.path.join("Emissive", "PolygonDarkFortress_Emissive_%s_%s.png" % (t, v)) for t in ("01", "02", "03", "04") for v in "ABC"]
    + [os.path.join("Env", f) for f in (
        "Brick_Large_Texture_01.png", "Brick_Medium_Texture_01.png", "Brick_Small_Texture_01.png",
        "SmoothStone_Texture_01.png", "SmoothStone_Dark_Texture_01.png", "Chipped_Stone_01.png", "Planks_Texture_01.png",
        "Grunge_01.png", "Moss_01.tga")]     # the moss PNG has a solid alpha; the TGA carries the cutout
    + [os.path.join("Misc", f) for f in ("Thorns_Diffuse_01.png", "Chains_01.tga", "Spiderweb_01.png")]
)

n = 0
for rel in FILES:
    src = os.path.join(SRC, rel)
    if not os.path.exists(src):
        print("  MISSING %s" % rel)
        continue
    stem = os.path.splitext(os.path.basename(rel))[0].lower()
    out = os.path.join(DST, stem + ".png")
    im = Image.open(src)
    alpha = im.getchannel("A").getextrema() if im.mode in ("RGBA", "LA") else None
    if rel.lower().endswith(".png") and im.mode in ("RGB", "RGBA"):
        shutil.copyfile(src, out)
    else:                                   # TGA, or a grey+alpha PNG (the spiderweb): write plain RGBA
        im.convert("RGBA").save(out)
    n += 1
    print("  %-48s %s %-5s alpha %-12s -> %s" % (rel, im.size, im.mode, alpha, os.path.relpath(out, PROJ)))
print("%d textures in %s" % (n, os.path.relpath(DST, PROJ)))
