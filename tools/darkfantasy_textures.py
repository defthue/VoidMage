"""Copy the POLYGON Dark Fantasy textures the toonified pack references into the project, lowercased, PNG only.

  python tools/darkfantasy_textures.py <SourceFiles>

Atlases: Textures/Alts/PolygonDarkFantasy_Texture_01_A (the base; Synty's own layout has it at the top
level, the user's copy keeps it in Alts) and the eleven palette twins. Tiling textures (Textures/Misc):
only the diffuse maps the MaterialList actually names -- heden_toon reads no normal or metallic map, so
the *_Normals / Metallic files (16 MB of them) stay behind. The three window TGAs, the moss and the
divider pattern are converted to PNG. Emissive masks are copied for reference only: nothing reads them.
Prints each file's alpha range, which is how the vines/moss/divider/window materials were decided
(alpha varying -> cutout material, alpha solid -> plain toon).
"""
import os, sys, shutil
from PIL import Image

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SRC = os.path.join(sys.argv[1], "Textures")
DST = os.path.join(PROJ, "Assets", "Textures", "DarkFantasyTextures")
os.makedirs(DST, exist_ok=True)

FILES = (
    [os.path.join("Alts", "PolygonDarkFantasy_Texture_%s_%s.png" % (t, v)) for t in ("01", "02", "03", "04") for v in "ABC"]
    + [os.path.join("Emissive", "PolygonDarkFantasy_Emissive_%s_%s.png" % (t, v)) for t in ("01", "02", "03", "04") for v in "ABC"]
    + [os.path.join("Misc", f) for f in (
        "Brick_Small_01.png", "Brick_Small_02.png", "Brick_Large_01.png", "Brick_Large_02.png", "Roof_01.png",
        "PolygonDarkFantasy_Stone_02.png", "Stucco_Brown_01.png", "Stucco_Brown_02.png", "Stucco_Brown_03.png",
        "Floor_Tiles_01.png", "Grunge_Diffuse.png", "Wood_FloorBoards_01_Diffuse.png", "Wood_FloorBoards_02_Diffuse.png",
        "HouseWall_Diffuse.png", "Vines_Diffuse.png", "Moss_TGA.tga", "DividerPattern_01.tga")]
    + [os.path.join("Windows", "PolygonDarkFantasy_Window_%s.tga" % n) for n in ("01", "02", "04")]
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
    if rel.lower().endswith(".png"):
        shutil.copyfile(src, out)
    else:
        im.convert("RGBA").save(out)
    n += 1
    print("  %-48s %s %-5s alpha %-12s -> %s" % (rel, im.size, im.mode, alpha, os.path.relpath(out, PROJ)))
print("%d textures in %s" % (n, os.path.relpath(DST, PROJ)))
