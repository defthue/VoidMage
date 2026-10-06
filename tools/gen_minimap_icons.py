"""Bakes the minimap's pickup icons (Code/UI/Minimap.razor): tiny pixel-art marks for what still lies on the floor.

Each icon is drawn as a 7x7 fill; a one pixel black rim is grown around it (so it reads over any slab colour),
which makes it 9x9. Written at SCALE x nearest-neighbour so the texture compiler's filtering and mips cannot
soften it; the panel draws it at 2 screen pixels per art pixel, close to the map's own pixel grid.

    python tools/gen_minimap_icons.py            -> Assets/ui/minimap/*.png
    python tools/gen_minimap_icons.py --sheet p  -> also a magnified contact sheet at path p, to look at
"""
import os
import sys
from PIL import Image

SCALE = 8
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "ui", "minimap")

PALETTE = {
    "K": (10, 8, 14),         # rim
    "C": (236, 222, 186),     # parchment
    "T": (150, 104, 60),      # wooden rollers
    "R": (196, 40, 34),       # red seal
    "G": (255, 200, 64),      # gold
    "V": (58, 40, 120),       # spell indigo
    "W": (255, 255, 255),
    "S": (150, 190, 232),     # cold steel
    "H": (120, 78, 44),       # grip
    "L": (226, 196, 255),     # shard highlight
    "P": (168, 96, 255),      # shard body
    "D": (96, 44, 176),       # shard shade
}

ICONS = {
    # enchantment scroll: parchment between two rollers, the red wax seal and its ribbon in the middle
    "enchantment": [
        "TTTTTTT",
        ".CCCCC.",
        ".CRRRC.",
        ".CRRRC.",
        ".CCRCC.",
        ".CCCCC.",
        "TTTTTTT",
    ],
    # spell scroll: gold rollers, indigo page, a gold star
    "spell": [
        "GGGGGGG",
        ".VVVVV.",
        ".VVGVV.",
        ".VGWGV.",
        ".VVGVV.",
        ".VVVVV.",
        "GGGGGGG",
    ],
    # void shard: a purple crystal
    "shard": [
        "...L...",
        "..LPP..",
        ".LPPPD.",
        ".LPPPD.",
        "..PPD..",
        "..PDD..",
        "...D...",
    ],
    # weapon skin: a cold steel sword
    "weapon": [
        ".....WW",
        "....WSW",
        "...WSW.",
        "G.WSW..",
        ".GSW...",
        ".HGG...",
        "H..G...",
    ],
}


def bake(rows):
    n = len(rows)
    size = n + 2
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    filled = set()
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            px[x + 1, y + 1] = PALETTE[ch] + (255,)
            filled.add((x + 1, y + 1))
    for (x, y) in list(filled):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            p = (x + dx, y + dy)
            if p not in filled:
                px[p] = PALETTE["K"] + (255,)
    return img.resize((size * SCALE, size * SCALE), Image.NEAREST)


def main():
    os.makedirs(OUT, exist_ok=True)
    baked = {}
    for name, rows in ICONS.items():
        baked[name] = bake(rows)
        path = os.path.join(OUT, f"{name}.png")
        baked[name].save(path)
        print("wrote", os.path.normpath(path))
    if "--sheet" in sys.argv:
        path = sys.argv[sys.argv.index("--sheet") + 1]
        # over the map's own room colour, large and at the size the panel draws them
        w = baked["shard"].width
        sheet = Image.new("RGBA", ((w + 16) * len(baked) + 16, w + 32 + 34), (128, 117, 168, 255))
        for i, img in enumerate(baked.values()):
            sheet.alpha_composite(img, (16 + i * (w + 16), 16))
            small = img.resize((18, 18), Image.NEAREST)
            sheet.alpha_composite(small, (16 + i * (w + 16), w + 32))
        sheet.save(path)
        print("sheet", path)


if __name__ == "__main__":
    main()
