"""Generate the toon flame sprites the fire rig uses (Assets/VFX/flame_*.png).

The tongue and lick carry their own colour bands (white-yellow core, orange, dark red edge) with
stepped alpha, so every particle is a tiny drawn flame and the bands survive the retro filter's
dither as bands instead of noise. The particle gradient multiplies on top (hot -> tint -> deep),
which keeps the hue of the light. The glow is white so it takes any colour. Pure python, no PIL.

    python tools/gen_fire_sprites.py
"""
import math, os, struct, zlib

SIZE = 128
SS = 4          # supersampling per axis: soft 1-2 px edges on the bands
OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'VFX')


def png(path, size, sample):
    """sample(x, y) -> (r, g, b, a) floats; x, y in -1..1 with +y up."""
    rows = []
    for py in range(size):
        row = bytearray([0])
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    x = ((px + (sx + 0.5) / SS) / size) * 2 - 1
                    y = 1 - ((py + (sy + 0.5) / SS) / size) * 2
                    r, g, b, a = sample(x, y)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            a = acc[3]
            if a > 0:
                rgb = [c / a for c in acc[:3]]
            else:
                rgb = [1.0, 1.0, 1.0]
            row += bytes((int(round(rgb[0] * 255)), int(round(rgb[1] * 255)), int(round(rgb[2] * 255)), int(round(a / (SS * SS) * 255))))
        rows.append(bytes(row))
    raw = b''.join(rows)

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)

    data = b'\x89PNG\r\n\x1a\n'
    data += chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
    data += chunk(b'IDAT', zlib.compress(raw, 9))
    data += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(data)


def bands(s, levels):
    """s = 0 at the edge .. 1 at the core; levels = ((threshold, (r, g, b, a)), ...) outside in."""
    out = (0.0, 0.0, 0.0, 0.0)
    for t, v in levels:
        if s > t:
            out = v
    return out


def teardrop(x, y, base_r, base_y, tip_y):
    """Inside-ness of a teardrop: a circle at the bottom and a straight taper to the tip."""
    if y < base_y:
        w = math.sqrt(max(0.0, base_r * base_r - (y - base_y) ** 2))
    else:
        w = base_r * max(0.0, 1 - (y - base_y) / (tip_y - base_y))
    if w <= 0.0:
        return 0.0
    s = 1 - abs(x) / w
    if s <= 0.0:
        return 0.0
    # thin the core toward the tip so the bands taper too
    tip = 1.0 if y < base_y else max(0.0, 1 - (y - base_y) / (tip_y - base_y))
    return s * (0.35 + 0.65 * tip)


CORE = (1.0, 0.86, 0.48, 1.0)
MID = (1.0, 0.52, 0.10, 0.9)
EDGE = (0.68, 0.13, 0.03, 0.65)
WHITE = (1.0, 1.0, 1.0, 1.0)
HALF = (1.0, 1.0, 1.0, 0.5)


def tongue(x, y):
    return bands(teardrop(x, y, 0.58, -0.35, 0.95), ((0.0, EDGE), (0.28, MID), (0.6, CORE)))


def lick(x, y):
    return bands(teardrop(x, y, 0.3, -0.55, 0.95), ((0.0, MID), (0.5, CORE)))


def glow(x, y):
    r = math.sqrt(x * x + y * y)
    return bands(1 - r, ((0.0, (0, 0, 0, 0)), (0.3, HALF), (0.65, WHITE)))


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for name, fn in (('flame_tongue', tongue), ('flame_lick', lick), ('flame_glow', glow)):
        p = os.path.join(OUT, name + '.png')
        png(p, SIZE, fn)
        print('wrote', p)
