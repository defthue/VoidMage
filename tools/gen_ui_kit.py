"""Bake Synty's Dark Fantasy interface frames into the inventory's three trial looks.

  python tools/extract_dfui.py            once: unpack the two .unitypackages to c:/tmp/dfui
  python tools/gen_ui_kit.py [theme ...]  bake (default: every theme)

The Tab inventory (Code/UI/SpellLoadout.razor) lays everything out in C# at fixed pixel sizes inside a 1280 x 780
window, so nothing here relies on the engine's border-image: every frame is 9-sliced HERE, by the borders in the
sprite's Unity .meta, to the exact size of the element it dresses, with its `_Background` fill tinted underneath.
(border-image also writes real border widths into the layout, which would shove the absolutely placed slots.)

Two ways a baked piece is worn:
  self     background-image on the element itself, cropped to it. Slots, tabs, cards in a grid.
  overlay  a `<div class="df df-...">` first child that may hang outside its parent (spikes, horns, crests). The
           window and the cards. Overhang is measured off the sprite: the element's edge is the OUTER edge of the
           frame's bar (median first opaque pixel along the stretch zone), whatever pokes past that hangs out.

`python tools/gen_ui_kit.py kit` builds only the iron kit every OTHER HUD wears (see "the iron kit" below).

Per theme it writes
  Assets/ui/darkfantasy/<theme>/*.png      baked at 2x (the window at 1.5x)
  Code/UI/Themes/inv_<theme>.scss          GENERATED - edit THEMES / css() here, not the file
  tools/out/ui_mock/<theme>.png            the staff tab mocked up offline at 1x, to judge a look without the engine

Every rule is prefixed `SpellLoadout.df-<theme>` so it outranks the placeholder sheet that imports it.
"""
import os, re, sys, glob, statistics
from PIL import Image, ImageDraw, ImageFont, ImageChops

SRC = "c:/tmp/dfui"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
OUT_UI = os.path.join(ASSETS, "ui", "darkfantasy")
OUT_SCSS = os.path.join(ROOT, "Code", "UI", "Themes")
OUT_MOCK = os.path.join(ROOT, "tools", "out", "ui_mock")
FONT_DIR = os.path.join(ASSETS, "Fonts")

FONTS = {
    "Pirata One": (os.path.join(SRC, "menus/Fonts/Pirata One/PirataOne-Regular.ttf"), 400),
    "Markazi Text": (os.path.join(SRC, "menus/Fonts/Markazi/MarkaziText-SemiBold.ttf"), 600),
    "LT Museum": (os.path.join(SRC, "menus/Fonts/LT Museum/LTMuseum-Bold.ttf"), 700),
    "Almendra": (os.path.join(FONT_DIR, "Almendra-Bold.ttf"), 700),
}

# ------------------------------------------------------------------------------------------------ sprites

_index = {}


def sprite_path(spec):
    """'hud/Frame_Box_Large_02' -> the one png in that pack whose name ends with it."""
    pack, name = spec.split("/", 1)
    if pack not in _index:
        _index[pack] = glob.glob(os.path.join(SRC, pack, "Sprites", "**", "*.png"), recursive=True)
    hits = [p for p in _index[pack] if os.path.basename(p)[:-4].endswith("_" + name)]
    if not hits:
        raise SystemExit(f"no sprite for {spec}")
    return min(hits, key=len)


def load(spec):
    """RGBA image and its 9-slice borders as (left, top, right, bottom) source pixels."""
    path = sprite_path(spec)
    img = Image.open(path).convert("RGBA")
    border = (0, 0, 0, 0)
    try:
        meta = open(path + ".meta", encoding="utf-8", errors="replace").read()
        m = re.search(r"spriteBorder:\s*\{x:\s*([\d.]+),\s*y:\s*([\d.]+),\s*z:\s*([\d.]+),\s*w:\s*([\d.]+)\}", meta)
        if m:
            l, b, r, t = (int(float(v)) for v in m.groups())   # Unity: x left, y bottom, z right, w top
            border = (l, t, r, b)
    except OSError:
        pass
    return img, border


def measure(img, border):
    """Outer edge of the frame's bar on each side, source px: (left, top, right, bottom)."""
    a = img.getchannel("A").load()
    w, h = img.size
    l, t, r, b = border
    ys = range(t, h - b) if h - b - t >= 8 else range(int(h * 0.4), int(h * 0.6))
    xs = range(l, w - r) if w - r - l >= 8 else range(int(w * 0.4), int(w * 0.6))

    def first(line, coords):
        for k, c in enumerate(coords):
            if a[c] > 96:
                return k
        return None

    def med(vals):
        vals = [v for v in vals if v is not None]
        return int(statistics.median(vals)) if vals else 0

    left = med(first(y, [(x, y) for x in range(w // 2)]) for y in ys)
    right = med(first(y, [(w - 1 - x, y) for x in range(w // 2)]) for y in ys)
    top = med(first(x, [(x, y) for y in range(h // 2)]) for x in xs)
    bottom = med(first(x, [(x, h - 1 - y) for y in range(h // 2)]) for x in xs)
    return left, top, right, bottom


def nine_slice(img, border, s, out_w, out_h):
    """Corners scaled by s, edges and centre stretched to fill out_w x out_h."""
    w, h = img.size
    out_w, out_h = max(1, int(round(out_w))), max(1, int(round(out_h)))
    l, t, r, b = border
    if not any(border):
        return img.resize((out_w, out_h), Image.LANCZOS)
    dl, dt, dr, db = (int(round(v * s)) for v in border)
    if dl + dr > out_w:
        k = out_w / float(dl + dr); dl, dr = int(dl * k), int(dr * k)
    if dt + db > out_h:
        k = out_h / float(dt + db); dt, db = int(dt * k), int(db * k)
    sx = [0, l, w - r, w]; sy = [0, t, h - b, h]
    dx = [0, dl, out_w - dr, out_w]; dy = [0, dt, out_h - db, out_h]
    out = Image.new("RGBA", (out_w, out_h), (0, 0, 0, 0))
    for j in range(3):
        for i in range(3):
            sw, sh = sx[i + 1] - sx[i], sy[j + 1] - sy[j]
            dw, dh = dx[i + 1] - dx[i], dy[j + 1] - dy[j]
            if sw <= 0 or sh <= 0 or dw <= 0 or dh <= 0:
                continue
            part = img.crop((sx[i], sy[j], sx[i + 1], sy[j + 1])).resize((dw, dh), Image.LANCZOS)
            out.paste(part, (dx[i], dy[j]))
    return out


def tint(img, rgba):
    """Multiply by a colour: the packs' `_Background` fills are white-to-grey on purpose."""
    r, g, b, a = rgba
    solid = Image.new("RGBA", img.size, (r, g, b, 255))
    out = ImageChops.multiply(img, solid)
    out.putalpha(img.getchannel("A").point(lambda v: int(v * a / 255)))
    return out


def col(hex_):
    hex_ = hex_.lstrip("#")
    if len(hex_) == 6:
        hex_ += "ff"
    return tuple(int(hex_[i:i + 2], 16) for i in (0, 2, 4, 6))


def bake(piece, w, h, ss=2.0):
    """One dressed element. Returns (image at ss, overhang l/t/r/b in element px)."""
    frame, border = load(piece["frame"])
    if piece.get("stretch"):
        box = frame.getbbox()
        frame, border = frame.crop(box), (0, 0, 0, 0)
    ol, ot, or_, ob = measure(frame, border)
    if piece.get("stretch"):
        ol = ot = or_ = ob = 0
    fw, fh = frame.size
    if any(border):
        s = piece["border"] / float(max(border))
        sx = sy = s
        cw, ch = w + (ol + or_) * s, h + (ot + ob) * s
    else:
        # no slices: stretch so the bar's outer box is the element
        sx, sy = w / float(fw - ol - or_), h / float(fh - ot - ob)
        s = sx
        cw, ch = fw * sx, fh * sy
    over = (ol * sx, ot * sy, or_ * sx, ob * sy)

    canvas = Image.new("RGBA", (int(round(cw * ss)), int(round(ch * ss))), (0, 0, 0, 0))
    fill = piece.get("fill")
    if fill:
        if piece.get("bg"):
            bg, bg_border = load(piece["bg"])
            if bg.size != frame.size:
                bg = bg.resize(frame.size, Image.LANCZOS)
            layer = nine_slice(bg, border if any(border) else bg_border, s * ss, canvas.width, canvas.height)
            canvas.alpha_composite(tint(layer, col(fill)))
        else:
            inset = piece.get("fill_inset", 0.5) * piece.get("bar", 6)
            box = [(over[0] + inset) * ss, (over[1] + inset) * ss, (over[0] + w - inset) * ss, (over[1] + h - inset) * ss]
            ImageDraw.Draw(canvas).rectangle(box, fill=col(fill))
    if piece.get("paper"):
        paper, pb = load(piece["paper"])
        inset = piece.get("paper_inset", 6)
        pw, ph = (w - 2 * inset) * ss, (h - 2 * inset) * ss
        layer = nine_slice(paper, pb, piece.get("paper_scale", 0.12) * ss, pw, ph)
        canvas.alpha_composite(tint(layer, col(piece["paper_fill"])), (int((over[0] + inset) * ss), int((over[1] + inset) * ss)))
    layer = nine_slice(frame, border, s * ss, canvas.width, canvas.height)
    if piece.get("frame_tint"):
        layer = tint(layer, col(piece["frame_tint"]))
    canvas.alpha_composite(layer)

    if piece.get("wear") == "self":
        x0, y0 = int(round(over[0] * ss)), int(round(over[1] * ss))
        canvas = canvas.crop((x0, y0, x0 + int(round(w * ss)), y0 + int(round(h * ss))))
        over = (0, 0, 0, 0)
    return canvas, over


def bake_line(spec, w, scale, ss=2.0):
    """A divider: ends kept, middle stretched to the width."""
    img, border = load(spec)
    h = img.height * scale
    return nine_slice(img, border, scale * ss, w * ss, h * ss), h


def bake_plain(spec, w, ss=2.0, tint_hex=None):
    """A greeble or divider: scaled to a width, aspect kept."""
    img, _ = load(spec)
    box = img.getbbox()
    img = img.crop(box)
    h = w * img.height / float(img.width)
    out = img.resize((int(round(w * ss)), int(round(h * ss))), Image.LANCZOS)
    if tint_hex:
        out = tint(out, col(tint_hex))
    return out, h


# ------------------------------------------------------------------------------------------------ the inventory's elements
# name -> (width, height) in the razor's px. Keep in step with the constants in SpellLoadout.razor.
ELEMENTS = {
    "window": (1280, 780),
    "tab_on": (132, 52), "tab_off": (132, 52),
    "slot_spell": (170, 150), "slot_mod": (84, 84), "slot_bag": (92, 92), "slot_shelf": (57, 57), "slot_book": (46, 46),
    "boon": (44, 44),
    "card_stats": (300, 264), "card_details": (500, 314), "card_side": (440, 640), "card_boons": (368, 110),
    "brazier": (300, 84), "seal": (506, 254), "banner": (700, 64), "confirm": (460, 244),
    "index_spell": (100, 124), "index_mod": (87, 122), "skin": (104, 100),
}
# which theme piece dresses which element
PIECE_OF = {
    "window": "window", "tab_on": "tab_on", "tab_off": "tab_off",
    "slot_spell": "socket", "slot_mod": "slot", "slot_bag": "slot", "slot_shelf": "slot_small", "slot_book": "slot_small",
    "boon": "slot_small",
    "card_stats": "card", "card_details": "card", "card_side": "card", "card_boons": "card_low",
    "brazier": "brazier", "seal": "seal", "banner": "banner", "confirm": "card",
    "index_spell": "tile", "index_mod": "tile", "skin": "tile",
}
SS = {"window": 1.5, "card_side": 1.5, "seal": 1.5}

# ------------------------------------------------------------------------------------------------ the three looks
THEMES = {
    # 1. Wrought iron: the HUD pack's thin gothic linework, cold steel on a violet-black glass. Quiet, lets the
    #    scroll icons carry the colour.
    "iron": {
        "title": "Wrought Iron",
        "head_font": "Pirata One", "body_font": "Markazi Text", "body_size": 1.38, "min_size": 19, "shadow": True,
        "pal": {
            "text": "#e4e2ec", "dim": "#a3a0b5", "head": "#e6d9ad", "label": "#c0bcd2", "accent": "#e6d9ad",
            "card_text": "#dddbe6", "card_dim": "#adaabf", "card_head": "#e6d9ad",
            "up": "#86cf95", "down": "#e2887a", "slot_rest": "#c4c4cc", "slot_hover": "#ffffff", "accept": "#ffe6a6",
            "button": "#1b1c26", "button_edge": "#4b4c5e", "backdrop_in": "rgba(10, 9, 18, 0.5)", "backdrop_out": "rgba(3, 3, 8, 0.92)",
        },
        "pieces": {
            "window": {"frame": "hud/Frame_Box_Large_02", "bg": "hud/Frame_Box_Large_02_Background", "border": 150, "fill": "#0d0d15f4", "wear": "overlay"},
            "card": {"frame": "hud/Frame_Box_Large_04", "border": 46, "fill": "#14141ee6", "bar": 5, "wear": "overlay"},
            "card_low": {"frame": "hud/Frame_Box_Large_04", "border": 34, "fill": "#14141ee6", "bar": 4, "wear": "overlay"},
            "slot": {"frame": "hud/Frame_Box_Medium_17", "border": 16, "fill": "#08080eea", "bar": 4, "wear": "self"},
            "slot_small": {"frame": "hud/Frame_Box_Medium_18", "border": 9, "fill": "#08080eea", "bar": 3, "wear": "self"},
            "socket": {"frame": "hud/Frame_Box_Medium_16", "border": 40, "fill": "#0b0a14f0", "bar": 5, "wear": "self"},
            "tile": {"frame": "hud/Frame_Box_Medium_19", "border": 16, "fill": "#12121be6", "bar": 4, "wear": "self"},
            "tab_on": {"frame": "hud/Frame_Bar_02", "bg": "hud/Frame_Bar_02_Background", "border": 26, "fill": "#2b2740f0", "wear": "self"},
            "tab_off": {"frame": "hud/Frame_Bar_02", "bg": "hud/Frame_Bar_02_Background", "border": 26, "fill": "#0d0d15a0", "frame_tint": "#8c8c98c0", "wear": "self"},
            "brazier": {"frame": "hud/Frame_Bar_03", "border": 50, "fill": "#140e0cec", "bar": 6, "fill_inset": 5, "wear": "self"},
            "seal": {"frame": "hud/Frame_Box_Large_07", "border": 14, "fill": "#0a0812d8", "bar": 4, "wear": "self"},
            "banner": {"frame": "hud/Frame_Bar_02", "bg": "hud/Frame_Bar_02_Background", "border": 32, "fill": "#1a1628f6", "wear": "self"},
        },
        "divider": {"sprite": "hud/Bar_01_Top", "scale": 0.17, "y": 74},
        "crest": {"sprite": "hud/Greeble_Tracery_14", "width": 210, "lift": 0.62},
    },
    # 2. Ossuary: bone frames around torn parchment. The cards read as pages (dark ink on paper), the slots are
    #    pale stone. The warm one.
    "ossuary": {
        "title": "Ossuary",
        "head_font": "Almendra", "body_font": "Almendra", "body_size": 1.08, "min_size": 15, "name_stroke": "#21170f",
        "pal": {
            "text": "#e2d6bb", "dim": "#8d8067", "head": "#f0dfae", "label": "#c4b48f", "accent": "#d9a441",
            "card_text": "#2b2117", "card_dim": "#6a5a44", "card_head": "#6b1d14",
            "up": "#2f6b34", "down": "#9c2b1c", "slot_rest": "#cfc8bb", "slot_hover": "#ffffff", "accept": "#ffe2a0",
            "button": "#2a2119", "button_edge": "#6e5b41", "backdrop_in": "rgba(18, 12, 6, 0.5)", "backdrop_out": "rgba(6, 4, 2, 0.92)",
        },
        "pieces": {
            "window": {"frame": "menus/Frame_Box_Large_13", "bg": "menus/Frame_Box_Large_13_Background", "border": 150, "fill": "#17110df6", "wear": "overlay"},
            "card": {"frame": "hud/Frame_Box_Large_03", "border": 70, "bar": 8, "paper": "menus/Box_Large_Parchment_01", "paper_fill": "#cdb98ef8", "paper_inset": 5, "paper_scale": 0.16, "wear": "overlay"},
            "card_low": {"frame": "hud/Frame_Box_Large_03", "border": 50, "bar": 6, "paper": "menus/Box_Large_Parchment_01", "paper_fill": "#cdb98ef8", "paper_inset": 4, "paper_scale": 0.1, "wear": "overlay"},
            "slot": {"frame": "hud/Frame_Box_Medium_07_Variant_01", "border": 13, "fill": "#0e0a07f0", "bar": 12, "fill_inset": 0.4, "wear": "self"},
            "slot_small": {"frame": "hud/Frame_Box_Medium_07_Variant_02", "border": 8, "fill": "#0e0a07f0", "bar": 8, "fill_inset": 0.4, "wear": "self"},
            "socket": {"frame": "menus/Frame_Box_Medium_04", "bg": "menus/Frame_Box_Medium_04_Background", "border": 46, "fill": "#0e0a07f4", "wear": "self"},
            "tile": {"frame": "hud/Frame_Box_Medium_07_Variant_03", "border": 12, "fill": "#1d1610f0", "bar": 11, "fill_inset": 0.4, "wear": "self"},
            "tab_on": {"frame": "menus/Bar_Scroll_01", "stretch": True, "wear": "self"},
            "tab_off": {"frame": "menus/Bar_Scroll_01", "stretch": True, "frame_tint": "#6b6257c8", "wear": "self"},
            "brazier": {"frame": "menus/Frame_Box_Large_14", "bg": "menus/Frame_Box_Large_14_Background", "border": 44, "fill": "#1c0f0af2", "wear": "self"},
            "seal": {"frame": "hud/Frame_Box_Large_03", "border": 50, "fill": "#0c0806dc", "bar": 6, "wear": "self"},
            "banner": {"frame": "menus/Bar_Scroll_01", "border": 131, "wear": "self"},
        },
        "divider": {"sprite": "hud/Bar_06", "scale": 0.17, "y": 74},
        "crest": {"sprite": "menus/Greeble_DeerSkull_01", "width": 124, "lift": 0.78},
        "tab_ink": True,
    },
    # 3. Blood chapel: the Menus pack's heavy bevelled iron with the red undersides, gems and horns, on black.
    #    The loud one.
    "chapel": {
        "title": "Blood Chapel",
        "head_font": "LT Museum", "body_font": "Markazi Text", "body_size": 1.24, "min_size": 17,
        "pal": {
            "text": "#ddd3d3", "dim": "#85757a", "head": "#f1e3d0", "label": "#c9a8a4", "accent": "#d8403a",
            "card_text": "#d6cccc", "card_dim": "#8d7c80", "card_head": "#f1d9b0",
            "up": "#84cf8f", "down": "#f0705f", "slot_rest": "#c9c3c3", "slot_hover": "#ffffff", "accept": "#ffd9a0",
            "button": "#1f1216", "button_edge": "#6b2a2e", "backdrop_in": "rgba(20, 4, 8, 0.5)", "backdrop_out": "rgba(6, 1, 3, 0.93)",
        },
        "pieces": {
            "window": {"frame": "menus/Frame_Box_Large_06", "bg": "menus/Frame_Box_Large_06_Background", "border": 150, "fill": "#0f090cf6", "wear": "overlay"},
            "card": {"frame": "menus/Frame_Box_Medium_03", "bg": "menus/Frame_Box_Medium_03_Background", "border": 58, "fill": "#190e13f2", "wear": "overlay"},
            "card_low": {"frame": "menus/Frame_Box_Medium_02", "bg": "menus/Frame_Box_Medium_02_Background", "border": 38, "fill": "#190e13f2", "wear": "overlay"},
            "slot": {"frame": "hud/Frame_Box_Medium_01_Variant_01", "bg": "hud/Frame_Box_Medium_01_Background", "border": 22, "fill": "#070406f4", "wear": "self"},
            "slot_small": {"frame": "hud/Frame_Box_Medium_03_Variant_02", "bg": "hud/Frame_Box_Medium_01_Background", "border": 13, "fill": "#070406f4", "wear": "self"},
            "socket": {"frame": "menus/Frame_Box_Medium_05", "bg": "menus/Frame_Box_Medium_05_Background", "border": 44, "fill": "#0a0508f6", "wear": "self"},
            "tile": {"frame": "hud/Frame_Box_Medium_01_Variant_03", "bg": "hud/Frame_Box_Medium_01_Background", "border": 20, "fill": "#150b10f2", "wear": "self"},
            "tab_on": {"frame": "menus/Frame_Bar_Large_04", "bg": "menus/Frame_Bar_Large_04_Background", "border": 50, "fill": "#5c1216f4", "wear": "self"},
            "tab_off": {"frame": "menus/Frame_Bar_Large_04", "bg": "menus/Frame_Bar_Large_04_Background", "border": 50, "fill": "#140b0ed0", "frame_tint": "#8a8080d0", "wear": "self"},
            "brazier": {"frame": "menus/Frame_Bar_Large_10", "bg": "menus/Frame_Bar_Large_10_Background", "border": 80, "fill": "#1c0c0af4", "wear": "self"},
            "seal": {"frame": "menus/Frame_Box_Medium_02", "bg": "menus/Frame_Box_Medium_02_Background", "border": 44, "fill": "#0a0508e0", "wear": "self"},
            "banner": {"frame": "menus/Frame_Bar_Large_11", "bg": "menus/Frame_Bar_Large_11_Background", "border": 60, "fill": "#2a0f12f6", "wear": "self"},
        },
        "divider": {"sprite": "menus/Bar_11", "scale": 0.2, "y": 74},
        "crest": {"sprite": "menus/Greeble_DemonSkull_01", "width": 116, "lift": 0.74},
    },
}

# ------------------------------------------------------------------------------------------------ stylesheet


def css(theme_id, theme, overs, extras):
    p = theme["pal"]
    R = f"SpellLoadout.df-{theme_id}"
    U = f"ui/darkfantasy/{theme_id}"
    head, body = theme["head_font"], theme["body_font"]
    head_w = FONTS[head][1]
    body_w = FONTS[body][1]
    bs = theme["body_size"]
    tab_ink = p["card_head"] if theme.get("tab_ink") else p["head"]

    def px(v):
        return f"{v:.1f}px"

    def fs(n):
        return px(max(n * bs, theme.get("min_size", 0)))

    def overlay(cls, el):
        l, t, r, b = overs[el]
        w, h = ELEMENTS[el]
        return (f"{R} .{cls} {{ display: flex; left: {px(-l)}; top: {px(-t)}; width: {px(w + l + r)}; height: {px(h + t + b)}; "
                f"background-image: url(\"{U}/{el}.png\"); }}")

    def worn(sel, el, extra=""):
        return (f"{R} {sel} {{ background-color: transparent; border-width: 0px; border-radius: 0px; "
                f"background-image: url(\"{U}/{el}.png\"); background-size: 100% 100%; background-repeat: no-repeat; {extra}}}")

    dv, cr = extras["divider"], extras["crest"]
    lines = [
        f"// GENERATED by tools/gen_ui_kit.py ({theme['title']}) - edit THEMES / css() there and re-run.",
        f"// Frames: Synty INTERFACE Dark Fantasy HUD + Menus, 9-sliced offline to each element's exact size.",
        "",
        f"{R} {{ font-family: \"{body}\"; font-weight: {body_w}; color: {p['text']}; " + ("text-shadow: 0px 1px 2px rgba(0, 0, 0, 0.95); " if theme.get("shadow") else "") + "}",
        f"{R} .backdrop {{ background: radial-gradient(circle, {p['backdrop_in']} 0%, {p['backdrop_out']} 100%); }}",
        f"{R} .window {{ background-color: transparent; border-width: 0px; border-radius: 0px; box-shadow: none; }}",
        f"{R} .dim {{ color: {p['dim']}; font-family: \"{body}\"; font-weight: {body_w}; font-size: {fs(14)}; }}",
        "",
        "// ---- overlays (first children, may hang outside their parent)",
        overlay("df-window", "window"),
        overlay("df-card-stats", "card_stats"),
        overlay("df-card-details", "card_details"),
        overlay("df-card-side", "card_side"),
        overlay("df-card-boons", "card_boons"),
        overlay("df-confirm", "confirm"),
        (f"{R} .df-divider {{ display: flex; left: {px(640 - dv['w'] / 2)}; top: {px(dv['y'])}; width: {px(dv['w'])}; height: {px(dv['h'])}; "
         f"background-image: url(\"{U}/divider.png\"); opacity: 0.85; }}"),
        (f"{R} .df-crest {{ display: flex; left: {px(640 - cr['w'] / 2)}; top: {px(-cr['h'] * cr['lift'])}; width: {px(cr['w'])}; height: {px(cr['h'])}; "
         f"background-image: url(\"{U}/crest.png\"); }}"),
        "",
        "// ---- header",
        f"{R} .tabs {{ top: 12px; }}",
        worn(".tab", "tab_off", f"font-family: \"{head}\"; font-weight: {head_w}; font-size: 23px; color: {p['dim']}; padding-bottom: 2px; "),
        f"{R} .tab:hover {{ color: {p['text']}; }}",
        f"{R} .tab.on {{ background-image: url(\"{U}/tab_on.png\"); color: {tab_ink}; }}",
        f"{R} .tab .badge {{ background-color: {p['accent']}; top: 6px; right: 14px; }}",
        f"{R} .underline {{ top: 54px; height: 2px; background-color: {p['accent']}; }}",
        f"{R} .purse .shards {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 24px; color: {p['head']}; }}",
        f"{R} .purse .where {{ border-color: {p['button_edge']}; color: {p['label']}; border-radius: 2px; font-size: {fs(13)}; }}",
        f"{R} .purse .where.home {{ border-color: {p['accent']}; color: {p['accent']}; }}",
        worn(".found-banner", "banner"),
        f"{R} .found-banner .found-title {{ font-family: \"{head}\"; font-weight: {head_w}; color: {tab_ink if theme.get('tab_ink') else p['head']}; }}",
        f"{R} .found-banner .found-sub {{ color: {p['card_dim'] if theme.get('tab_ink') else p['dim']}; }}",
        "",
        "// ---- page furniture",
        (f"{R} .section-label {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 22px; letter-spacing: 1px; text-transform: none; "
         f"color: {p['label']}; margin-top: -7px; }}"),
        f"{R} .section-label .dim, {R} .purse .shards .dim {{ margin-bottom: 1px; }}",
        f"{R} .none {{ color: {p['card_dim']}; font-size: {fs(15)}; }}",
        f"{R} .card {{ background-color: transparent; border-width: 0px; border-radius: 0px; padding: 16px 24px; color: {p['card_text']}; }}",
        f"{R} .footer .hint {{ color: {p['dim']}; font-size: {fs(14)}; }}",
        f"{R} .hint-block {{ color: {p['dim']}; font-size: {fs(14)}; }}",
        "",
        "// ---- slots (the frame is the element's own background: nothing shifts)",
        f"{R} .slot {{ background-color: transparent; border-width: 0px; border-radius: 0px; background-size: 100% 100%; background-repeat: no-repeat; background-tint: {p['slot_rest']}; }}",
        f"{R} .slot.hover {{ background-tint: {p['slot_hover']}; }}",
        f"{R} .slot.socket {{ border-width: 0px; background-color: transparent; }}",
        f"{R} .slot.socket.accept {{ background-tint: {p['accept']}; }}",
        f"{R} .slot.socket.spell {{ background-image: url(\"{U}/slot_spell.png\"); }}",
        f"{R} .slot.socket.mod {{ background-image: url(\"{U}/slot_mod.png\"); }}",
        f"{R} .slot.bag {{ background-image: url(\"{U}/slot_bag.png\"); }}",
        f"{R} .slot.shelf {{ background-image: url(\"{U}/slot_shelf.png\"); }}",
        f"{R} .slot.book {{ background-image: url(\"{U}/slot_book.png\"); background-color: transparent; border-width: 0px; }}",
        f"{R} .slot .empty-hint {{ color: {p['dim']}; opacity: 0.55; }}",
        f"{R} .socket.spell .socket-head {{ margin-top: 9px; height: 22px; padding: 0px 18px; border-bottom-width: 0px; }}",
        f"{R} .socket.spell .socket-head .key {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 17px; color: {p['dim']}; }}",
        f"{R} .socket.spell .socket-head .state {{ color: {p['dim']}; font-size: {fs(12)}; }}",
        f"{R} .socket.spell .socket-head.active {{ background-color: transparent; }}",
        f"{R} .slot .empty-hint {{ font-size: {fs(13)}; }}",
        f"{R} .socket.spell .socket-head.active .key, {R} .socket.spell .socket-head.active .state {{ color: {p['accent']}; }}",
        f"{R} .socket.spell .tile {{ width: 106px; height: 106px; }}",
        f"{R} .socket.spell .empty-hint {{ margin-top: 30px; }}",
        f"{R} .tile .count {{ font-family: \"{head}\"; font-weight: {head_w}; color: {p['head']}; }}",
        "",
        "// ---- staff tab furniture",
        worn(".seal", "seal"),
        f"{R} .seal .seal-rune {{ color: {p['dim']}; }}",
        f"{R} .seal .seal-text {{ color: {p['label']}; font-size: {fs(17)}; }}",
        worn(".brazier", "brazier", f"padding: 0px 26px; background-tint: {p['slot_rest']}; "),
        f"{R} .brazier.lit {{ border-width: 0px; background-tint: #ffffff; }}",
        f"{R} .brazier .brazier-sub {{ font-size: {fs(12)}; color: {p['dim']}; }}",
        f"{R} .boons .boon .tip .tip-line {{ font-size: {fs(14)}; }}",
        f"{R} .stats .stats-note {{ font-size: {fs(12)}; }}",
        f"{R} .details .detail-kind {{ font-size: {fs(13)}; letter-spacing: 1px; }}",
        f"{R} .found-banner .found-sub {{ font-size: {fs(14)}; }}",
        f"{R} .index-card .owned {{ font-size: {fs(12)}; }}",
        f"{R} .skin-card .skin-badge {{ font-size: {fs(11)}; padding: 0px 6px; }}",
        f"{R} .progress-text {{ font-size: {fs(14)}; }}",
        f"{R} .purse .style-switch {{ font-size: {fs(13)}; }}",
        f"{R} .dev .dev-note {{ font-size: {fs(13)}; color: {p['dim']}; }}",
        f"{R} .brazier .brazier-title {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 21px; }}",
        f"{R} .boons {{ padding: 20px 22px; }}",
        worn(".boons .boon", "boon"),
        f"{R} .boons .boon.edge {{ background-tint: #e6d3a0; }}",
        f"{R} .boons .boon.font {{ background-tint: #a9bdf5; }}",
        f"{R} .boons .boon.vein {{ background-tint: #f0a0a0; }}",
        f"{R} .boons .boon.hoard {{ background-tint: #f2d684; }}",
        f"{R} .boons .boon.step {{ background-tint: #a4e6c4; }}",
        f"{R} .boons .boon.pact {{ background-tint: #ff7a70; background-color: transparent; }}",
        f"{R} .boons .boon .tip {{ background-color: {p['button']}; border-color: {p['button_edge']}; border-radius: 0px; color: {p['text']}; }}",
        f"{R} .boons .boon .tip .tip-name {{ font-family: \"{head}\"; font-weight: {head_w}; color: {p['head']}; }}",
        f"{R} .stats .stats-name {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 31px; margin-bottom: 12px; " + (f"text-stroke: 4px {theme['name_stroke']}; " if theme.get("name_stroke") else "") + "}",
        f"{R} .stats .stat {{ margin-bottom: 8px; }}",
        f"{R} .stats .stat-label {{ color: {p['card_dim']}; font-size: {fs(16)}; width: 112px; }}",
        f"{R} .stats .stat-value {{ color: {p['card_text']}; font-size: {fs(17)}; }}",
        f"{R} .stats .stat-value.up {{ color: {p['up']}; }}",
        f"{R} .stats .stat-value.down {{ color: {p['down']}; }}",
        f"{R} .stats .stats-note {{ color: {p['card_head']}; }}",
        f"{R} .details .detail-name {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 29px; color: {p['card_head']}; }}",
        f"{R} .details .detail-kind {{ color: {p['card_dim']}; }}",
        f"{R} .details .detail-head {{ margin-bottom: 6px; }}",
        f"{R} .details .detail-icon {{ width: 58px; height: 58px; }}",
        f"{R} .details .detail-icon .runes {{ font-size: 21px; }}",
        f"{R} .details .detail-desc {{ color: {p['card_text']}; font-size: {fs(16)}; margin-bottom: 8px; }}",
        f"{R} .details .detail-line {{ color: {p['card_dim']}; font-size: {fs(15)}; margin-bottom: 3px; }}",
        f"{R} .details .detail-foot {{ color: {p['card_dim']}; font-size: {fs(14)}; margin-top: 8px; }}",
        f"{R} .index-detail .detail-desc, {R} .index-detail .detail-line {{ padding: 0px 8px; }}",
        "",
        "// ---- the are-you-sure card",
        f"{R} .confirm {{ background-color: transparent; border-width: 0px; border-radius: 0px; }}",
        f"{R} .confirm .confirm-title {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 30px; color: {p['card_head']}; }}",
        f"{R} .confirm .confirm-name {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 26px; color: {p['card_text']}; }}",
        f"{R} .confirm .confirm-sub {{ font-size: {fs(15)}; color: {p['card_dim']}; }}",
        f"{R} .confirm .button {{ font-size: 21px; }}",
        f"{R} .confirm .button.danger {{ border-color: {p['down']}; color: {p['down']}; }}",
        f"{R} .confirm .button.danger:hover {{ background-color: {p['down']}; color: #0c0a0a; }}",
        "",
        "// ---- index + armory",
        worn(".index-card", "index_mod", f"background-tint: {p['slot_rest']}; "),
        f"{R} .index-card.spell-card {{ background-image: url(\"{U}/index_spell.png\"); }}",
        worn(".skin-card", "skin", f"background-tint: {p['slot_rest']}; "),
        f"{R} .index-card:hover, {R} .skin-card:hover {{ background-tint: #ffffff; }}",
        f"{R} .index-card.selected, {R} .skin-card.selected {{ border-width: 0px; background-tint: {p['accept']}; }}",
        f"{R} .index-card.unknown, {R} .skin-card.unknown {{ background-color: transparent; background-tint: #77777a; }}",
        f"{R} .index-card.new, {R} .skin-card.new {{ background-tint: {p['accept']}; }}",
        f"{R} .skin-card.equipped {{ border-width: 0px; background-tint: #ffffff; }}",
        f"{R} .skin-card .skin-badge {{ background-color: {p['accent']}; color: #0c0a0a; border-radius: 0px; }}",
        f"{R} .skin-card {{ justify-content: flex-start; padding-top: 4px; }}",
        f"{R} .skin-card .skin-icon {{ width: 50px; height: 50px; margin-bottom: 1px; }}",
        f"{R} .skin-card .skin-name {{ line-height: {fs(12)}; }}",
        f"{R} .index-card .name, {R} .skin-card .skin-name {{ color: {p['text']}; font-size: {fs(12)}; }}",
        f"{R} .index-card.unknown .name, {R} .skin-card.unknown .skin-name {{ color: {p['dim']}; }}",
        f"{R} .index-card .owned {{ color: {p['accent']}; }}",
        f"{R} .index-detail .big-rune {{ color: {p['card_dim']}; }}",
        f"{R} .progress {{ background-color: {p['button']}; border-radius: 0px; }}",
        f"{R} .progress .progress-fill {{ background-color: {p['accent']}; }}",
        f"{R} .progress-text {{ color: {p['dim']}; }}",
        "",
        "// ---- buttons + dev",
        (f"{R} .button {{ font-family: \"{head}\"; font-weight: {head_w}; font-size: 17px; background-color: {p['button']}; border-color: {p['button_edge']}; "
         f"border-radius: 0px; color: {p['text']}; }}"),
        f"{R} .button:hover {{ border-color: {p['accent']}; color: {p['head']}; }}",
        f"{R} .button.on {{ border-color: {p['accent']}; background-color: {p['button_edge']}; color: {p['head']}; }}",
        f"{R} .style-switch {{ color: {p['dim']}; border-color: {p['button_edge']}; }}",
        f"{R} .style-switch:hover {{ color: {p['head']}; border-color: {p['accent']}; }}",
        "",
    ]
    return "\n".join(lines)


# ------------------------------------------------------------------------------------------------ mockup


def font(name, size):
    return ImageFont.truetype(FONTS[name][0], int(round(size)))


def mock(theme_id, theme, baked, overs, extras, tab="staff"):
    """The staff tab at 1x on a 1600 x 920 screen, from the very PNGs the game loads."""
    p = theme["pal"]
    W, H = 1600, 920
    im = Image.new("RGBA", (W, H), (14, 12, 18, 255))
    # a stand-in for the dimmed dungeon behind the panel
    d = ImageDraw.Draw(im)
    for k in range(0, 46):
        v = int(30 - k * 0.5)
        d.ellipse([W / 2 - 900 + k * 18, H / 2 - 560 + k * 11, W / 2 + 900 - k * 18, H / 2 + 560 - k * 11], fill=(v + 6, v, v + 10, 255))
    wx, wy = (W - 1280) // 2, (H - 780) // 2 + 20
    head, body, bs = theme["head_font"], theme["body_font"], theme["body_size"]

    def fs(n):
        return max(n * bs, theme.get("min_size", 0))

    def put(el, x, y, tint_hex=None):
        img = baked[el]
        l, t, r, b = overs[el]
        w, h = ELEMENTS[el]
        img = img.resize((int(round(w + l + r)), int(round(h + t + b))), Image.LANCZOS)
        if tint_hex:
            img = tint(img, col(tint_hex))
        im.alpha_composite(img, (int(round(x - l)), int(round(y - t))))

    def text(x, y, s, fnt, fill, anchor="la"):
        ImageDraw.Draw(im).text((x, y), s, font=fnt, fill=col(fill), anchor=anchor)

    put("window", wx, wy)
    cr = extras["crest"]
    crest = extras["crest_img"].resize((int(cr["w"]), int(cr["h"])), Image.LANCZOS)
    im.alpha_composite(crest, (int(wx + 640 - cr["w"] / 2), int(wy - cr["h"] * cr["lift"])))
    dv = extras["divider"]
    div = extras["divider_img"].resize((int(dv["w"]), max(1, int(dv["h"]))), Image.LANCZOS)
    div.putalpha(div.getchannel("A").point(lambda v: int(v * 0.85)))
    im.alpha_composite(div, (int(wx + 640 - dv["w"] / 2), int(wy + dv["y"])))

    # header
    tab_ink = p["card_head"] if theme.get("tab_ink") else p["head"]
    on = 2 if tab == "armory" else 0
    for i, name in enumerate(("Staff", "Index", "Armory", "Dev")):
        tx, ty = wx + 24 + i * 132, wy + 12
        put("tab_on" if i == on else "tab_off", tx, ty)
        text(tx + 66, ty + 25, name, font(head, 23), tab_ink if i == on else p["dim"], "mm")
    ImageDraw.Draw(im).rectangle([wx + 24 + on * 132 + 18, wy + 12 + 54, wx + 24 + on * 132 + 132 - 18, wy + 12 + 56], fill=col(p["accent"]))
    text(wx + 1280 - 24, wy + 38, "128", font(head, 24), p["head"], "rm")
    gx, gy = wx + 1280 - 24 - 62, wy + 38
    ImageDraw.Draw(im).polygon([(gx, gy - 9), (gx + 8, gy), (gx, gy + 9), (gx - 8, gy)], fill=col(p["head"]))
    text(wx + 1280 - 130, wy + 38, "In the dungeon", font(body, fs(13)), p["label"], "rm")

    cx, cy = wx + 24, wy + 84   # content origin
    lf = font(head, 22)

    def label(x, y, s, dim=None):
        text(cx + x, cy + y - 7, s, lf, p["label"])
        if dim:
            wdt = ImageDraw.Draw(im).textlength(s, font=lf)
            text(cx + x + wdt + 8, cy + y, dim, font(body, fs(14)), p["dim"])

    if tab == "armory":
        wdir = os.path.join(ASSETS, "ui", "weapons")
        ids = sorted(os.path.basename(f)[:-4] for f in glob.glob(os.path.join(wdir, "*.png")) if not f.endswith("_shade.png"))
        names = {"sword_soldier": "Soldier's Greatsword", "sword_knight": "Forge-Hold Longsword", "sword_warden": "Warden's Straightsword",
                 "staff_realms_06": "Mountain King's Staff", "staff_kingdom_09": "Hedge Wizard's Staff"}
        swords = [k for k in ids if k.startswith("sword")]; staffs = [k for k in ids if k.startswith("staff")]
        label(0, 0, "Swords", f"10 / {len(swords)}"); label(0, 352, "Staffs", f"2 / {len(staffs)}")
        nf = font(body, fs(12))
        for group, top in ((swords, 28), (staffs, 380)):
            for n, k in enumerate(group):
                x, y = cx + (n % 7) * 108, cy + top + (n // 7) * 106
                owned = n % 3 != 1
                put("skin", x, y, p["slot_hover"] if n == 0 else (p["slot_rest"] if owned else "#77777a"))
                ic = Image.open(os.path.join(wdir, k + ("" if owned else "_shade") + ".png")).convert("RGBA").resize((50, 50), Image.LANCZOS)
                im.alpha_composite(ic, (int(x + 27), int(y + 4)))
                nm = names.get(k, k.split("_", 1)[1].replace("_", " ").title()) if owned else "???"
                words, lines_ = nm.split(), [""]
                for wd in words:
                    trial = (lines_[-1] + " " + wd).strip()
                    if ImageDraw.Draw(im).textlength(trial, font=nf) > 96 and lines_[-1]:
                        lines_.append(wd)
                    else:
                        lines_[-1] = trial
                for li, ln in enumerate(lines_[:2]):
                    text(x + 52, y + 56 + li * fs(12), ln, nf, p["text"] if owned else p["dim"], "ma")
        sxx, syy = cx + 792, cy
        put("card_side", sxx, syy)
        big = Image.open(os.path.join(wdir, "sword_soldier.png")).convert("RGBA").resize((380, 380), Image.LANCZOS)
        im.alpha_composite(big, (int(sxx + 30), int(syy + 26)))
        text(sxx + 220, syy + 416, "Soldier's Greatsword", font(head, 29), p["card_head"], "ma")
        text(sxx + 220, syy + 454, "SWORD · COSMETIC", font(body, fs(13)), p["card_dim"], "ma")
        text(sxx + 220, syy + 478, "Carried by skeleton soldiers", font(body, fs(14)), p["card_dim"], "ma")
        ImageDraw.Draw(im).rectangle([sxx + 160, syy + 530, sxx + 280, syy + 570], fill=col(p["button"]), outline=col(p["button_edge"]))
        text(sxx + 220, syy + 550, "Equip", font(head, 20), p["text"], "mm")
        text(wx + 24, wy + 780 - 27, "Click a weapon you own to equip it · Tab closes", font(body, fs(14)), p["dim"], "lm")
        os.makedirs(OUT_MOCK, exist_ok=True)
        out = os.path.join(OUT_MOCK, theme_id + "_armory.png")
        im.convert("RGB").save(out)
        return out

    label(0, 0, "Staff"); label(0, 196, "Enchantments", "stack up to three")
    label(400, 0, "Satchel", "4 / 6"); label(732, 0, "Library", "9 / 32 shelves")
    label(0, 326, "Spellbook", "3 / 7"); label(0, 420, "Boons this run")
    label(400, 350, "Active spell"); label(732, 300, "Details")

    icons = {k: Image.open(os.path.join(ASSETS, "ui", "scrolls", k + ".png")).convert("RGBA")
             for k in ("open_mod", "open_spell", "closed_mod", "closed_spell")}
    rune_font = os.path.join(FONT_DIR, "NotoSansRunic-Regular.ttf")

    def scroll(x, y, size, kind, runes=None, colour="#d8b24a"):
        ic = icons[kind].resize((int(size), int(size)), Image.LANCZOS)
        im.alpha_composite(ic, (int(x), int(y)))
        if runes:
            f = ImageFont.truetype(rune_font, int(size * 0.34))
            text(x + size * 0.502, y + size * 0.506, runes, f, colour, "mm")

    # spell sockets
    for i in range(2):
        x, y = cx + i * 190, cy + 28
        put("slot_spell", x, y, p["slot_hover"] if i == 0 else p["slot_rest"])
        text(x + 18, y + 9 + 11, "AB"[i], font(head, 17), p["accent"] if i == 0 else p["dim"], "lm")
        text(x + 36, y + 9 + 12, "active" if i == 0 else "make active", font(body, fs(12)), p["accent"] if i == 0 else p["dim"], "lm")
        scroll(x + 32, y + 33, 106, "open_spell", ("ᚠᛒ", "ᛁᛚ")[i], ("#ff9a4a", "#8ad0ff")[i])
    for i in range(4):
        x, y = cx + i * 92, cy + 222
        put("slot_mod", x, y, p["slot_rest"])
        if i < 3:
            scroll(x, y, 84, "open_mod", ("ᚷᛚ", "ᚺᛞ", "ᚹᚱ")[i], ("#c9a0ff", "#ffd36a", "#ff7a70")[i])
    for i in range(7):
        x, y = cx + i * 52, cy + 352
        put("slot_book", x, y, p["slot_rest"])
        if i < 3:
            scroll(x, y, 46, "open_spell")
        else:
            text(x + 23, y + 23, "?", font(body, 18), p["dim"], "mm")
    for i in range(6):
        x, y = cx + 400 + (i % 3) * 104, cy + 28 + (i // 3) * 104
        put("slot_bag", x, y, p["slot_hover"] if i == 1 else p["slot_rest"])
        if i < 4:
            scroll(x, y, 92, ("open_mod", "closed_mod", "open_mod", "closed_spell")[i], ("ᛊᚹ", None, "ᛏᚲ", None)[i], "#9adf9a")
    for i in range(32):
        x, y = cx + 732 + (i % 8) * 63, cy + 28 + (i // 8) * 63
        put("slot_shelf", x, y, p["slot_rest"])
        if i < 9:
            scroll(x, y, 57, "open_mod", ("ᚷᛚ", "ᚺᛞ", "ᚹᚱ", "ᛊᚹ", "ᛏᚲ", "ᛒᚱ", "ᛗᚾ", "ᛚᛟ", "ᛞᚨ")[i], "#d8b24a")
            if i % 3 == 0:
                text(x + 54, y + 56, "×" + str(2 + i % 2), font(head, 14), p["head"], "rd")

    # brazier
    bx, by = cx + 400, cy + 244
    put("brazier", bx, by, p["slot_rest"])
    text(bx + 34 + 14, by + 42, "ᚲ", ImageFont.truetype(rune_font, 38), "#e8782a", "mm")
    text(bx + 34 + 40, by + 30, "Brazier", font(head, 19), "#d8a068", "lm")
    text(bx + 34 + 40, by + 54, "destroy scrolls here", font(body, fs(12)), p["dim"], "lm")

    # boons
    put("card_boons", cx, cy + 446)
    for i, tint_hex in enumerate(("#e6d3a0", "#a9bdf5", "#f0a0a0", "#f2d684", "#ff7a70")):
        x, y = cx + 22 + i * 52, cy + 446 + 20
        put("boon", x, y, tint_hex)
        text(x + 22, y + 22, "ᛖᚠᚹᚺᛈ"[i], ImageFont.truetype(rune_font, 24), tint_hex, "mm")
    text(cx, cy + 590, "Q or R swaps the active spell.", font(body, fs(14)), p["dim"])
    text(cx, cy + 612, "Enchantments you carry are lost if you die. Bring them home.", font(body, fs(14)), p["dim"])

    # stats card
    sx_, sy_ = cx + 400, cy + 376
    put("card_stats", sx_, sy_)
    ImageDraw.Draw(im).text((sx_ + 24, sy_ + 20), "Fireball", font=font(head, 31), fill=col("#ff9a4a"),
                            stroke_width=2 if theme.get("name_stroke") else 0, stroke_fill=col(theme.get("name_stroke", "#000000")))
    for k, (a, b_, state) in enumerate((("Damage", "34  (+8)", "up"), ("Mana", "18", ""), ("Cast time", "0.62 s", "down"),
                                        ("Speed", "1400", ""), ("Radius", "120", "up"), ("Bolts", "1", ""))):
        if k > 4:
            break
        yy = sy_ + 64 + k * 35
        text(sx_ + 24, yy, a, font(body, fs(16)), p["card_dim"])
        text(sx_ + 24 + 112, yy - 1, b_, font(body, fs(17)), p[state] if state else p["card_text"])

    # details card
    dx_, dy_ = cx + 732, cy + 326
    put("card_details", dx_, dy_)
    scroll(dx_ + 24, dy_ + 16, 58, "open_mod", "ᚷᛚ", "#c9a0ff")
    text(dx_ + 24 + 72, dy_ + 12, "Gluttony", font(head, 29), p["card_head"])
    text(dx_ + 24 + 72, dy_ + 48, "ENCHANTMENT · DAMAGE", font(body, fs(13)), p["card_dim"])
    desc = "A bigger bolt that hits much harder for the same mana, but the spell takes far longer to charge."
    df_, words, rows = font(body, fs(16)), desc.split(), [""]
    for wd in words:
        trial = (rows[-1] + " " + wd).strip()
        if ImageDraw.Draw(im).textlength(trial, font=df_) > 452 and rows[-1]:
            rows.append(wd)
        else:
            rows[-1] = trial
    for k, row in enumerate(rows):
        text(dx_ + 24, dy_ + 82 + k * 27, row, df_, p["card_text"])
    top = dy_ + 82 + len(rows) * 27 + 8
    for k, s in enumerate(("×1  +60% damage · bolt ×1.35 size · charge time ×1.8", "×2  +120% damage · bolt ×1.7 size · charge time ×2.6", "×3  +180% damage · bolt ×2 size · charge time ×3.4")):
        text(dx_ + 24, top + k * 26, s, font(body, fs(15)), p["card_dim"])
    text(dx_ + 24, dy_ + 270, "Owned: 3", font(body, fs(14)), p["card_dim"])
    text(dx_ + 500 - 24, dy_ + 270, "Hold right-click to burn", font(body, fs(14)), p["card_dim"], "ra")

    text(wx + 24, wy + 780 - 27, "Click a scroll to pick it up · Tab closes", font(body, fs(14)), p["dim"], "lm")
    text(W / 2, H - 16, f"{theme['title']}   (hud_style {list(THEMES).index(theme_id) + 1})", font(head, 26), "#ffffff", "mm")

    os.makedirs(OUT_MOCK, exist_ok=True)
    out = os.path.join(OUT_MOCK, theme_id + ".png")
    im.convert("RGB").save(out)
    return out


# ------------------------------------------------------------------------------------------------ the iron kit (every other HUD)
# The user picked Wrought Iron (2026-09-18) and asked for every HUD in it (2026-09-19). Those panels flow instead of
# sitting at fixed sizes, so they cannot be baked to size: they wear STRETCHY frames, the engine's border-image, on
# childless `<div class="dfk dfk-...">` overlays (border-image writes real border widths into the layout; on a div with
# nothing inside that is harmless). Sources are scaled here so a slice is exactly twice its on-screen width.
#   Assets/ui/darkfantasy/kit/*.png        Code/UI/Themes/_iron_kit.scss (GENERATED)        tools/out/ui_mock/hud.png

KIT_DIR = os.path.join(OUT_UI, "kit")
KIT_URL = "ui/darkfantasy/kit"

# name -> box frames: (frame, bg or None, fill, on-screen width of the widest border, bar thickness for a drawn fill)
KIT_BOXES = {
    "window": ("hud/Frame_Box_Large_02", "hud/Frame_Box_Large_02_Background", "#0d0d15f4", 150, 0),
    "panel": ("hud/Frame_Box_Large_04", None, "#12121cee", 46, 5),
    "panel-low": ("hud/Frame_Box_Large_04", None, "#12121cee", 30, 4),
    "slot": ("hud/Frame_Box_Medium_17", None, "#08080eea", 14, 4),
    "tile": ("hud/Frame_Box_Medium_19", None, "#12121be6", 16, 4),
    "key": ("hud/Frame_Box_Medium_18", None, "#0a0a12f4", 7, 3),
}
# name -> bars: (frame, bg, fill, on-screen height of the bar's BODY). The body is the bg sprite's own box: the ends
# of the frame hang outside it, so a rectangular health fill inside the element sits exactly in the frame.
KIT_BARS = {
    "bar-16": ("hud/Frame_Bar_03", "hud/Frame_Bar_03_Background", "#05050af0", 16),
    "bar-26": ("hud/Frame_Bar_03", "hud/Frame_Bar_03_Background", "#030308f4", 26),
    "bar-26-rails": ("hud/Frame_Bar_03", "hud/Frame_Bar_03_Background", None, 26),   # no fill: goes OVER the liquid
    "bar-20": ("hud/Frame_Bar_03", "hud/Frame_Bar_03_Background", "#05050af0", 20),
    "bar-11": ("hud/Frame_Bar_03", "hud/Frame_Bar_03_Background", "#05050af0", 11),
    "btn-52": ("hud/Frame_Bar_02", "hud/Frame_Bar_02_Background", "#1d1b2cf4", 52),
    "btn-38": ("hud/Frame_Bar_02", "hud/Frame_Bar_02_Background", "#1d1b2cf4", 38),
    "plate-40": ("hud/Frame_Bar_02", "hud/Frame_Bar_02_Background", "#0a0a12d8", 40),
}
IRON = THEMES["iron"]


def kit_box(name):
    frame_spec, bg_spec, fill, width, bar = KIT_BOXES[name]
    frame, border = load(frame_spec)
    ol, ot, or_, ob = measure(frame, border)
    s2 = 2.0 * width / max(border)
    size = (int(round(frame.width * s2)), int(round(frame.height * s2)))
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    if bg_spec:
        bg, _ = load(bg_spec)
        canvas.alpha_composite(tint(bg.resize(size, Image.LANCZOS), col(fill)))
    else:
        inset = bar * 0.5 * 2
        ImageDraw.Draw(canvas).rectangle([ol * s2 + inset, ot * s2 + inset, size[0] - or_ * s2 - inset, size[1] - ob * s2 - inset], fill=col(fill))
    canvas.alpha_composite(frame.resize(size, Image.LANCZOS))
    canvas.save(os.path.join(KIT_DIR, name + ".png"), optimize=True)
    l, t, r, b = (int(round(v * s2)) for v in border)
    return {"img": canvas, "slice": (t, r, b, l), "width": (t / 2, r / 2, b / 2, l / 2),
            "over": (ol * s2 / 2, ot * s2 / 2, or_ * s2 / 2, ob * s2 / 2)}


def kit_bar(name):
    frame_spec, bg_spec, fill, height = KIT_BARS[name]
    frame, border = load(frame_spec)
    full_w = frame.width
    bg, _ = load(bg_spec)
    if bg.size != frame.size:
        bg = bg.resize(frame.size, Image.LANCZOS)
    bx0, by0, bx1, by1 = bg.getbbox()                 # the body
    fx0, fy0, fx1, fy1 = frame.getbbox()
    x0, y0, x1, y1 = min(bx0, fx0), min(by0, fy0), max(bx1, fx1), max(by1, fy1)
    frame, bg = frame.crop((x0, y0, x1, y1)), bg.crop((x0, y0, x1, y1))
    s2 = 2.0 * height / (by1 - by0)
    size = (int(round(frame.width * s2)), int(round(frame.height * s2)))
    canvas = tint(bg.resize(size, Image.LANCZOS), col(fill)) if fill else Image.new("RGBA", size, (0, 0, 0, 0))
    canvas.alpha_composite(frame.resize(size, Image.LANCZOS))
    canvas.save(os.path.join(KIT_DIR, name + ".png"), optimize=True)
    # the meta's left/right slices, less what the crop took off each side
    left = int(round((border[0] - x0) * s2))
    right = int(round((border[2] - (full_w - x1)) * s2))
    return {"img": canvas, "slice": (0, right, 0, left), "width": (0, right / 2, 0, left / 2),
            "over": ((bx0 - x0) * s2 / 2, (by0 - y0) * s2 / 2, (x1 - bx1) * s2 / 2, (y1 - by1) * s2 / 2)}


def kit_css(pieces, divider_h):
    p = IRON["pal"]
    head, body = IRON["head_font"], IRON["body_font"]
    out = [
        "// GENERATED by tools/gen_ui_kit.py (the iron kit) - edit KIT_* / kit_css() there and re-run.",
        "// Every HUD outside the inventory imports this: stretchy Wrought Iron frames for childless overlay divs,",
        "// the shared type and colours, and the one rise-in every panel appears with.",
        "",
        f"$df-head: \"{head}\";",
        f"$df-body: \"{body}\";",
        f"$df-text: {p['text']};",
        f"$df-dim: {p['dim']};",
        f"$df-label: {p['label']};",
        f"$df-gold: {p['head']};",
        f"$df-up: {p['up']};",
        f"$df-down: {p['down']};",
        "$df-void: #b48ae6;",
        "",
        ".dfk { position: absolute; pointer-events: none; }",
    ]
    for name, d in pieces.items():
        t, r, b, l = d["slice"]
        wt, wr, wb, wl = d["width"]
        ol, ot, or_, ob = d["over"]
        out.append(f".dfk-{name} {{ left: {-ol:.1f}px; top: {-ot:.1f}px; right: {-or_:.1f}px; bottom: {-ob:.1f}px; "
                   f"border-image: url(\"{KIT_URL}/{name}.png\") {t} {r} {b} {l} fill / {wt:.1f}px {wr:.1f}px {wb:.1f}px {wl:.1f}px; }}")
    out += [
        f".dfk-divider {{ position: relative; pointer-events: none; height: {divider_h:.1f}px; background-image: url(\"{KIT_URL}/divider.png\"); background-size: 100% 100%; background-repeat: no-repeat; opacity: 0.85; }}",
        ".dfk-crest { left: 50%; margin-left: -105px; width: 210px; height: 56px; background-image: url(\"ui/darkfantasy/iron/crest.png\"); background-size: 100% 100%; background-repeat: no-repeat; }",
        "",
        "// the inventory's appear: up out of a short drop, eased hard at the start. ALWAYS an iteration count (the",
        "// engine loops an animation that has none), and it only plays on a panel that was just created.",
        "@keyframes df-rise { 0% { opacity: 0; transform: translateY( 18px ) scale( 0.97 ); } 100% { opacity: 1; transform: translateY( 0px ) scale( 1 ); } }",
        "@keyframes df-drop { 0% { opacity: 0; transform: translateY( -14px ); } 100% { opacity: 1; transform: translateY( 0px ); } }",
        "@keyframes df-fade { 0% { opacity: 0; } 100% { opacity: 1; } }",
        "",
    ]
    return "\n".join(out)


def kit_draw(im, pieces, name, x, y, w, h, tint_hex=None):
    """Mock: what the engine's border-image will draw for an element at x, y, w x h."""
    d = pieces[name]
    ol, ot, or_, ob = d["over"]
    t, r, b, l = d["slice"]
    img = nine_slice(d["img"], (l, t, r, b), 0.5, w + ol + or_, h + ot + ob)
    if tint_hex:
        img = tint(img, col(tint_hex))
    im.alpha_composite(img, (int(round(x - ol)), int(round(y - ot))))


def mock_hud(pieces):
    """A 1920 x 1080 screen with every dressed HUD on it at once, to judge the kit without the engine."""
    p = IRON["pal"]
    head, body = IRON["head_font"], IRON["body_font"]
    W, H = 1920, 1080
    im = Image.new("RGBA", (W, H), (22, 19, 28, 255))
    dr = ImageDraw.Draw(im)
    for k in range(60):
        v = int(46 - k * 0.6)
        dr.ellipse([W / 2 - 1100 + k * 16, H / 2 - 700 + k * 10, W / 2 + 1100 - k * 16, H / 2 + 700 - k * 10], fill=(v + 8, v + 2, v + 14, 255))

    def text(x, y, s, fnt, fill, anchor="la", stroke=2):
        ImageDraw.Draw(im).text((x, y), s, font=fnt, fill=col(fill), anchor=anchor, stroke_width=stroke, stroke_fill=(0, 0, 0, 255))

    # health, mana, shards (bottom left): glass tubes of potion liquid, the number inside
    def liquid_bar(x, y, w, h, pct, top, mid, low, label):
        kit_draw(im, pieces, "bar-26", x, y, w, h)
        fw = int(w * pct)
        grad = Image.new("RGBA", (fw, h))
        gd = ImageDraw.Draw(grad)
        for yy in range(h):
            t = yy / float(h - 1)
            a, b, k = (col(top), col(mid), t / 0.45) if t < 0.45 else (col(mid), col(low), (t - 0.45) / 0.55)
            gd.line([(0, yy), (fw, yy)], fill=tuple(int(a[i] + (b[i] - a[i]) * k) for i in range(3)) + (255,))
        for bx_, by_, r_ in ((0.08, 0.7, 2), (0.3, 0.35, 3), (0.55, 0.6, 2), (0.8, 0.25, 2)):
            gd.ellipse([fw * bx_ - r_, h * by_ - r_, fw * bx_ + r_, h * by_ + r_], fill=(255, 255, 255, 95))
        im.alpha_composite(grad, (int(x), int(y)))
        ImageDraw.Draw(im).ellipse([x + fw - 6, y - 3, x + fw + 6, y + 11], fill=col(mid))
        ImageDraw.Draw(im).ellipse([x + fw - 9, y + h - 12, x + fw + 3, y + h + 3], fill=col(mid))
        glass = Image.new("RGBA", (w, h))
        gg = ImageDraw.Draw(glass)
        for yy in range(h):
            t = yy / float(h - 1)
            gg.line([(0, yy), (w, yy)], fill=(255, 255, 255, int(87 * (1 - t / 0.5))) if t < 0.5 else (0, 0, 0, int(97 * (t - 0.5) / 0.5)))
        im.alpha_composite(glass, (int(x), int(y)))
        kit_draw(im, pieces, "bar-26-rails", x, y, w, h)
        text(x + w / 2, y + h / 2, label, font(body, 21), "#fbf6ea", "mm")

    liquid_bar(76, H - 84 - 26, 320, 26, 0.72, "#ef2f3a", "#c9161f", "#6d0a14", "72 / 100")
    liquid_bar(76, H - 44 - 26, 320, 26, 0.45, "#5a8cff", "#2f56e8", "#1b1a86", "45 / 100")
    text(84, H - 130, "128", font(head, 38), p["head"], "ls")
    text(158, H - 132, "340 banked", font(body, 21), p["dim"], "ls")
    ImageDraw.Draw(im).polygon([(60, H - 152), (70, H - 142), (60, H - 132), (50, H - 142)], fill=col("#b48ae6"))

    # potion hotbar (bottom middle)
    for i, (k, n, colour) in enumerate((("1", "3", "#cd2832"), ("2", "1", "#466ef5"))):
        x = W / 2 - 62 + i * 66
        kit_draw(im, pieces, "slot", x, 980, 56, 66)
        ImageDraw.Draw(im).rounded_rectangle([x + 17, 1004, x + 39, 1032], 6, fill=col(colour), outline=(255, 255, 255, 150))
        ImageDraw.Draw(im).rectangle([x + 23, 996, x + 33, 1004], fill=(230, 240, 255, 110), outline=(255, 255, 255, 130))
        text(x + 6, 982, k, font(head, 17), p["dim"])
        text(x + 50, 1044, n, font(head, 21), p["head"], "rs")

    # boss bar (top middle)
    text(W / 2, 40, "The Bone Colossus", font(head, 34), "#f1dcc0", "mm")
    kit_draw(im, pieces, "bar-20", W / 2 - 300, 66, 600, 20)
    ImageDraw.Draw(im).rectangle([W / 2 - 300, 66, W / 2 - 300 + 600 * 0.64, 85], fill=col("#be1e1e"))
    for side, x in ((0, W / 2 - 300), (1, W / 2 + 20)):
        kit_draw(im, pieces, "bar-11", x, 104, 280, 11)
        ImageDraw.Draw(im).rectangle([x, 104, x + 280 * (0.8 if side == 0 else 0.35), 114], fill=col("#c87828"))

    # pickup prompt
    kit_draw(im, pieces, "plate-40", W / 2 - 150, 610, 300, 40)
    kit_draw(im, pieces, "key", W / 2 - 124, 616, 28, 28)
    text(W / 2 - 110, 630, "E", font(head, 19), p["head"], "mm", 0)
    text(W / 2 - 84, 630, "Pick up Moonspire Staff", font(body, 21), p["text"], "lm")

    # unlock notice
    kit_draw(im, pieces, "panel-low", W / 2 - 210, 150, 420, 92)
    text(W / 2 - 170, 196, "ᛏ", ImageFont.truetype(os.path.join(FONT_DIR, "NotoSansRunic-Regular.ttf"), 44), "#ffd76a", "mm")
    text(W / 2 - 130, 170, "WEAPON UNLOCKED", font(body, 17), p["label"])
    text(W / 2 - 130, 188, "Soldier's Greatsword", font(head, 30), p["head"])

    # exit card
    cx, cy, cw, ch = 140, 300, 560, 330
    kit_draw(im, pieces, "panel", cx, cy, cw, ch)
    text(cx + cw / 2, cy + 56, "Floor 2", font(head, 54), p["head"], "mm")
    text(cx + cw / 2, cy + 106, "Below: The Forge-Hold.", font(body, 24), p["label"], "mm")
    text(cx + cw / 2, cy + 148, "Carrying 128 shards. Home banks them; death loses them.", font(body, 20), p["text"], "mm")
    for i, (lab, tn) in enumerate((("Return home", None), ("Go deeper", "#d9c2ff"))):
        bx = cx + 40 + i * 250
        kit_draw(im, pieces, "btn-52", bx, cy + 190, 230, 52, tn)
        text(bx + 115, cy + 215, lab, font(head, 26), p["text"] if i == 0 else "#e9dcff", "mm")
    kit_draw(im, pieces, "btn-38", cx + cw / 2 - 80, cy + 264, 160, 38, "#a9a9b4")
    text(cx + cw / 2, cy + 282, "Not yet", font(head, 20), p["dim"], "mm")

    # a boon card
    bx, by, bw, bh = 1320, 270, 290, 410
    kit_draw(im, pieces, "panel", bx, by, bw, bh, "#e6d3a0")
    text(bx + bw / 2, by + 70, "ᛖ", ImageFont.truetype(os.path.join(FONT_DIR, "NotoSansRunic-Regular.ttf"), 72), "#c9b27a", "mm")
    text(bx + bw / 2, by + 140, "Keen Edge", font(head, 36), p["head"], "mm")
    text(bx + bw / 2, by + 176, "EDGE", font(body, 18), p["dim"], "mm")
    dv = Image.open(os.path.join(KIT_DIR, "divider.png")).convert("RGBA").resize((200, 14), Image.LANCZOS)
    im.alpha_composite(dv, (int(bx + 45), int(by + 196)))
    text(bx + bw / 2, by + 244, "+15% sword damage", font(body, 25), p["text"], "mm")
    text(bx + bw / 2, by + 280, "+5% crit chance", font(body, 25), p["text"], "mm")

    # a wardrobe tile and a category
    kit_draw(im, pieces, "tile", 1320, 730, 126, 150)
    text(1383, 868, "Wizard Hat", font(body, 18), p["text"], "ms")
    kit_draw(im, pieces, "tile", 1460, 730, 126, 150, p["accept"])
    text(1523, 868, "Hood", font(body, 18), p["head"], "ms")

    # shopkeeper box
    kit_draw(im, pieces, "window", 140, 700, 1000, 250)
    text(200, 740, "John", font(head, 34), p["head"])
    text(200, 780, "KEEPER OF THE TABLE", font(body, 17), p["dim"])
    text(420, 740, "Scrolls? Take one. They bite less than they look.", font(body, 27), p["text"])
    for i, o in enumerate(("What do you sell?", "Who are you?", "Leave")):
        text(440, 800 + i * 36, "▸  " + o, font(body, 22), p["label"] if i else p["head"])

    text(W / 2, H - 14, "Iron kit: every HUD at once (mock)", font(head, 22), "#ffffff", "ms")
    os.makedirs(OUT_MOCK, exist_ok=True)
    out = os.path.join(OUT_MOCK, "hud.png")
    im.convert("RGB").save(out)
    print("mock", out, flush=True)


def build_kit():
    os.makedirs(KIT_DIR, exist_ok=True)
    os.makedirs(OUT_SCSS, exist_ok=True)
    pieces = {}
    for name in KIT_BOXES:
        pieces[name] = kit_box(name)
    for name in KIT_BARS:
        pieces[name] = kit_bar(name)
    div, div_h = bake_line("hud/Bar_01_Top", 900, 0.17)
    div.save(os.path.join(KIT_DIR, "divider.png"), optimize=True)
    with open(os.path.join(OUT_SCSS, "_iron_kit.scss"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(kit_css(pieces, div_h))
    total = sum(d["img"].width * d["img"].height * 4 for d in pieces.values())
    print(f"kit: {len(pieces) + 1} pngs, {total / 1048576:.1f} MB as RGBA", flush=True)
    return pieces


# ------------------------------------------------------------------------------------------------ main


def build(theme_id):
    theme = THEMES[theme_id]
    out_dir = os.path.join(OUT_UI, theme_id)
    os.makedirs(out_dir, exist_ok=True)
    baked, overs, total = {}, {}, 0
    for el, (w, h) in ELEMENTS.items():
        piece = theme["pieces"][PIECE_OF[el]]
        img, over = bake(piece, w, h, SS.get(el, 2.0))
        img.save(os.path.join(out_dir, el + ".png"), optimize=True)
        baked[el], overs[el] = img, over
        total += img.width * img.height * 4

    extras = {}
    dv = theme["divider"]
    img, h = bake_line(dv["sprite"], 1232, dv["scale"])
    img.save(os.path.join(out_dir, "divider.png"), optimize=True)
    extras["divider"] = {"w": 1232, "h": h, "y": dv["y"] - h / 2}; extras["divider_img"] = img
    cr = theme["crest"]
    img, h = bake_plain(cr["sprite"], cr["width"])
    img.save(os.path.join(out_dir, "crest.png"), optimize=True)
    extras["crest"] = {"w": cr["width"], "h": h, "lift": cr["lift"]}; extras["crest_img"] = img

    os.makedirs(OUT_SCSS, exist_ok=True)
    with open(os.path.join(OUT_SCSS, f"inv_{theme_id}.scss"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(css(theme_id, theme, overs, extras))
    path = mock(theme_id, theme, baked, overs, extras)
    mock(theme_id, theme, baked, overs, extras, tab="armory")
    print(f"{theme_id}: {len(baked) + 2} pngs, {total / 1048576:.1f} MB as RGBA, window overhang "
          f"{tuple(round(v) for v in overs['window'])}, mock {path}", flush=True)


if __name__ == "__main__":
    if not os.path.isdir(SRC):
        raise SystemExit(f"{SRC} is missing: run tools/extract_dfui.py first")
    for name, (path, _) in FONTS.items():
        dst = os.path.join(FONT_DIR, os.path.basename(path))
        if not os.path.exists(dst):
            with open(path, "rb") as src, open(dst, "wb") as out:
                out.write(src.read())
            print("font ->", dst)
    args = sys.argv[1:]
    for tid in ([a for a in args if a in THEMES] or ([] if args else list(THEMES))):
        build(tid)
    if not args or "kit" in args:
        mock_hud(build_kit())
