"""Blood textures for BloodVfx (Code/Combat/BloodVfx.cs): the s&box engine's own blood splatter colour maps and drop
sprite copied into Assets/vfx as png, plus a flat dark-red albedo for the gore chunks' toon material.

    python tools/gen_blood_assets.py

Engine decals themselves are unusable here (heden_toon.shader never runs the engine's Decals::Apply), so the
splatter maps are stamped as surface quads through heden_ground_splat.shader (blood_splat_0N.vmat) instead.
Idempotent; needs Pillow.
"""
import os
from PIL import Image

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CORE = r"C:\Program Files (x86)\Steam\steamapps\common\sbox\core"
OUT = os.path.join(PROJ, "Assets", "vfx")

COPIES = [(os.path.join(CORE, "decals", "blood", "decal_blood_splatter_%02d_color.png" % n), "blood_splatter_%02d.png" % n) for n in range(1, 5)]
COPIES.append((os.path.join(CORE, "textures", "particles", "fluid", "blood_drop.tga"), "blood_drop.png"))
FLAT = (107, 8, 8, 255)   # ~ (0.42, 0.03, 0.03): the gore chunks' meat


def main():
    os.makedirs(OUT, exist_ok=True)
    for src, name in COPIES:
        im = Image.open(src).convert("RGBA")
        dst = os.path.join(OUT, name)
        im.save(dst)
        print("wrote", dst, im.size)
    flat = Image.new("RGBA", (8, 8), FLAT)
    dst = os.path.join(OUT, "blood_flat.png")
    flat.save(dst)
    print("wrote", dst)
    # the same splatter silhouettes in white, so a vmat's soot colour can print them in any liquid's colour
    # (the potion splashes, Code/Combat/ThrownBottle.cs); the red's own shading survives as a light modulation
    for n in range(1, 5):
        src = os.path.join(OUT, "blood_splatter_%02d.png" % n)
        im = Image.open(src).convert("RGBA")
        px = im.load()
        w, h = im.size
        lum_max = 1
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                lum_max = max(lum_max, max(r, g, b))
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                v = int(255 * (0.75 + 0.25 * max(r, g, b) / lum_max))
                px[x, y] = (v, v, v, a)
        dst = os.path.join(OUT, "splatter_white_%02d.png" % n)
        im.save(dst)
        print("wrote", dst, im.size)
    shaders = os.path.join(PROJ, "Assets", "Shaders")
    for kind, colour in (("heal", (0.80, 0.10, 0.14)), ("mana", (0.22, 0.38, 1.00))):
        for n in range(1, 5):
            dst = os.path.join(shaders, "potion_splat_%s_%02d.vmat" % (kind, n))
            with open(dst, "w", newline="\n") as f:
                f.write("// A potion's splash stamped on a surface (BloodVfx.Splash for ThrownBottle): the engine's splatter silhouette in\n"
                        "// white (tools/gen_blood_assets.py) printed in the liquid's colour through the soot term. Generated: edit the tool.\n"
                        "Layer0\n{\n\tshader \"shaders/heden_ground_splat.shader\"\n"
                        "\tTextureColor \"vfx/splatter_white_%02d.png\"\n"
                        "\tg_vSootColor \"[%.6f %.6f %.6f 0.000000]\"\n"
                        "\tg_flOpacity \"0.900\"\n\tg_flDepthFeather \"3.000\"\n}\n" % (n, colour[0], colour[1], colour[2]))
            print("wrote", dst)


if __name__ == "__main__":
    main()
