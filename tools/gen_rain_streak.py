#!/usr/bin/env python3
"""Write Assets/VFX/rain_streak.png: a tall, narrow RGBA texture holding one soft streak that is brightest and
widest at the head and thins out toward the tail. The sprite renderer multiplies a sprite's WIDTH by the
texture's aspect (w/h), so at a 110 u sprite a 6x256 texture draws a 110 x 2.6 u ribbon: the same shape the
trail renderer drew (width 2.2 -> 0.7, alpha 0.45 -> 0 along the path), as one sprite.

    python tools/gen_rain_streak.py                     # 6x256, head at the bottom row
    python tools/gen_rain_streak.py --width 6 --height 256 --head top
"""
import argparse
import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets" / "VFX" / "rain_streak.png"


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--width", type=int, default=6)
    ap.add_argument("--height", type=int, default=256)
    ap.add_argument("--head", choices=("bottom", "top"), default="bottom", help="which end is the bright, wide head")
    ap.add_argument("--tail-alpha", type=float, default=0.0, help="alpha at the tail end (the trail faded to 0)")
    ap.add_argument("--tail-width", type=float, default=0.32, help="tail width as a fraction of the head width")
    args = ap.parse_args()

    w, h = args.width, args.height
    cx = (w - 1) / 2.0
    im = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = im.load()
    for y in range(h):
        v = y / (h - 1)                       # 0 at the top row, 1 at the bottom row
        t = v if args.head == "bottom" else 1.0 - v   # 0 at the tail, 1 at the head
        width = args.tail_width + (1.0 - args.tail_width) * t
        alpha = args.tail_alpha + (1.0 - args.tail_alpha) * t
        # a short fade at both extreme ends so the sprite's edge never shows as a cut
        alpha *= smoothstep(0.0, 0.05, t) * smoothstep(0.0, 0.03, 1.0 - t)
        sigma = max(0.35, (w / 3.2) * width)
        for x in range(w):
            d = (x - cx) / sigma
            a = math.exp(-0.5 * d * d) * alpha
            if a < 0.004:
                continue
            px[x, y] = (255, 255, 255, int(round(a * 255)))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    im.save(OUT)
    print(f"wrote {OUT} ({w}x{h}, head {args.head})")


if __name__ == "__main__":
    main()
