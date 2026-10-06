"""Bring the staged Synty Sidekick "Fantasy Sorcerers" parts into the project and generate the wardrobe
catalogue from their manifest.

The parts were exported and toonified once by `SorcererParts/tools/sidekick_parts_export.py` (Blender) into
a staging folder that mirrors Assets/. This tool does the project side, and is safe to rerun:

  1. copies charactertoon/sorc_*.fbx + .vmdl, the palette PNGs and the manifest into Assets/
  2. writes shaders/sorcererpalette.vmat (default palette) plus sorcererpalette_01..06.vmat, one per Synty
     colour preset: identical heden_toon material, only AlbedoTexture differs
  3. rewrites every copied vmdl's MaterialGroupList so the model carries a material group per palette
     (`palette_01`..`palette_06`); a colour set in game is then `renderer.MaterialGroup`, never a
     MaterialOverride (the hit flash owns that and material groups yield to it and come back on their own).
     Hair and beards are the exception: they remap to CharacterColor.vmat (flat renderer Tint, like the
     base hairs) so the wardrobe's hair colour applies to them, and carry no palette groups
  4. generates Code/Character/SorcererCatalogue.cs from the manifest and Synty's Sidekick database (read only):
     hats, hairs, beards, shoes (left+right as one), left/right shoulder and left/right hip pieces are one option
     per pack piece; torso (+ both arms and hands) and legs (+ hips) are the six "Fantasy Sorcerers NN" presets'
     bundles; plus the six presets themselves. File numbers do NOT match pieces up (torso 01 is not worn with
     upper arms 01), the database presets are the only real pairing. Back, face and hip-back pieces are left out.

    python tools/gen_sorcerer_parts.py [staging_dir]
"""
import json, os, re, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGING = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\skipp\Desktop\Speelboord Studios\SorcererParts\drop_into_assets"
ASSETS = os.path.join(ROOT, "Assets")

PALETTES = 6
PALETTE_TEXTURE = {0: "materials/sorcerer/palette_default.png"}
for i in range(1, PALETTES + 1):
    PALETTE_TEXTURE[i] = f"materials/sorcerer/palette_fantasysorcerers_{i:02}.png"

# Synty's Sidekick database (read only): the six hand-matched Fantasy Sorcerers presets per body area
SIDEKICK_DB = r"C:\Users\skipp\My project (5)\Assets\Synty\SidekickCharacters\Database\Side_Kick_Data.db"
PRESET_PREFIX = "Fantasy Sorcerers "
PRESETS = 6

# Loose slots, one option per file number (left/right pieces of a number are one option):
# manifest slot -> (AppearanceSlot, id key, display noun, side or None)
LOOSE = {
    "attach_head": ("Hat", "hat", "Hat", None),
    "hair": ("Hair", "hair", "Sorcerer Hair", None),
    "facial_hair": ("FacialHair", "beard", "Beard", None),
    "foot_l": ("Shoes", "shoes", "Shoes", "l"),
    "foot_r": ("Shoes", "shoes", "Shoes", "r"),
    "attach_shoulder_l": ("ShoulderLeft", "shoulder_l", "Left Shoulder", None),
    "attach_shoulder_r": ("ShoulderRight", "shoulder_r", "Right Shoulder", None),
    "attach_hips_l": ("HipLeft", "hip_l", "Left Hip", None),
    "attach_hips_r": ("HipRight", "hip_r", "Right Hip", None),
}
# Bundled slots come from the presets instead: a torso option is preset N's torso + both arms + both
# hands, a legs option is preset N's hips + both legs. Back, face and hip-back pieces are not offered.
TORSO_PIECES = ["torso", "arm_upper_l", "arm_upper_r", "arm_lower_l", "arm_lower_r", "hand_l", "hand_r"]
LEGS_PIECES = ["hips", "leg_l", "leg_r"]

# manifest hides_body_region -> BodyParts constant in AppearanceOptions.cs
HIDES = {
    "Torso": "BodyParts.Torso",
    "UpperArms": "BodyParts.UpperArms",
    "Forearms": "BodyParts.Forearms",
    "Hands": "BodyParts.Hands",
    "Hips": "BodyParts.Hips",
    "Shins": "BodyParts.Shins",
    "Feet": "BodyParts.Feet",
}

PART_NAME_RE = re.compile(r"^SK_FANT_SORC_(\d+)_(\d{2}[A-Z]{4})_HU01$")


def copy_tree_files(src, dst, pattern):
    os.makedirs(dst, exist_ok=True)
    n = 0
    for name in sorted(os.listdir(src)):
        if re.match(pattern, name):
            shutil.copyfile(os.path.join(src, name), os.path.join(dst, name))
            n += 1
    return n


MATERIAL_GROUPS_RE = re.compile(
    r'(\t\t\t\t_class = "MaterialGroupList"\n\t\t\t\tchildren =\n\t\t\t\t\[\n)(.*?)(\n\t\t\t\t\]\n)', re.S)


def material_group_block(i):
    return (
        "\t\t\t\t\t{\n"
        '\t\t\t\t\t\t_class = "MaterialGroup"\n'
        f'\t\t\t\t\t\tname = "palette_{i:02}"\n'
        "\t\t\t\t\t\tremaps =\n"
        "\t\t\t\t\t\t[\n"
        "\t\t\t\t\t\t\t{\n"
        # named groups remap what the DEFAULT group produced, not the raw mesh material name: keyed on the raw
        # name the compiled model ended up with no group materials at all (verified in the .vmdl_c strings)
        '\t\t\t\t\t\t\t\tfrom = "shaders/sorcererpalette.vmat"\n'
        f'\t\t\t\t\t\t\t\tto = "shaders/sorcererpalette_{i:02}.vmat"\n'
        "\t\t\t\t\t\t\t},\n"
        "\t\t\t\t\t\t]\n"
        "\t\t\t\t\t},"
    )


HAIR_RE = re.compile(r"^sorc_\d+_(hair|facial_hair)\.vmdl$")


def patch_vmdl(text, hair):
    m = MATERIAL_GROUPS_RE.search(text)
    if not m:
        raise SystemExit("MaterialGroupList not found in vmdl")
    children = m.group(2)
    if '_class = "DefaultMaterialGroup"' not in children:
        raise SystemExit("DefaultMaterialGroup not found in vmdl")
    # drop any palette groups from an earlier run, then append the fresh set after the default group
    children = re.sub(r'\n\t\t\t\t\t\{\n\t\t\t\t\t\t_class = "MaterialGroup"\n.*?\n\t\t\t\t\t\},', "", children, flags=re.S)
    if hair:
        # hair and beards take the renderer Tint like the base hairs (CharacterColor: flat colour, no texture),
        # so they get no palette groups: a group remap would put the palette back
        children = children.replace('to = "shaders/sorcererpalette.vmat"', 'to = "shaders/charactercolor.vmat"')
        return text[:m.start(2)] + children + text[m.end(2):]
    groups = "\n".join(material_group_block(i) for i in range(1, PALETTES + 1))
    return text[:m.start(2)] + children + "\n" + groups + text[m.end(2):]


def write_vmats(template):
    shaders = os.path.join(ASSETS, "Shaders")
    os.makedirs(shaders, exist_ok=True)
    for i, tex in PALETTE_TEXTURE.items():
        body = re.sub(r'AlbedoTexture "[^"]*"', f'AlbedoTexture "{tex}"', template)
        name = "SorcererPalette.vmat" if i == 0 else f"SorcererPalette_{i:02}.vmat"
        with open(os.path.join(shaders, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(body)


def read_presets():
    """{preset number: {manifest slot: (set, code)}} merged over Synty's three body-area presets of that number.
    Base human parts (bare hands, ears, brows) are left out: the body already draws those."""
    import sqlite3
    db = sqlite3.connect(f"file:{SIDEKICK_DB}?mode=ro", uri=True)
    presets = {}
    for pid, name in db.execute("select id, name from sk_part_preset where name like ?", (PRESET_PREFIX + "%",)):
        n = int(name[len(PRESET_PREFIX):])
        pieces = presets.setdefault(n, {})
        for (part_name,) in db.execute("select part_name from sk_part_preset_row where ptr_part_preset=?", (pid,)):
            m = PART_NAME_RE.match(part_name or "")
            if m:
                pieces[m.group(2)] = m.group(1)
    if sorted(presets) != list(range(1, PRESETS + 1)):
        raise SystemExit(f"expected presets 1..{PRESETS} in {SIDEKICK_DB}, found {sorted(presets)}")
    return presets


def build_catalogue(manifest):
    parts = manifest["parts"]
    by_code = {(p["set"], p["sidekick_code"]): p for p in parts}
    rows = {}

    def row(opt_id, name, slot):
        return rows.setdefault(opt_id, {"id": opt_id, "name": name, "slot": slot, "models": [], "hides": []})

    def add(r, p):
        r["models"].append(p["model"])
        hides = HIDES.get(p.get("hides_body_region"))
        if hides and hides not in r["hides"]:
            r["hides"].append(hides)

    loose_ids = {}
    for p in sorted(parts, key=lambda p: (p["slot"], p["set"])):
        if p["slot"] not in LOOSE:
            continue
        slot, key, noun, _ = LOOSE[p["slot"]]
        opt_id = f"sorc_{p['set']}_{key}"
        add(row(opt_id, f"{noun} {int(p['set'])}", slot), p)
        loose_ids[(p["set"], p["sidekick_code"])] = opt_id

    presets = []
    for n, pieces in sorted(read_presets().items()):
        chosen = {}
        # manifest slot -> the preset's piece
        slot_of = {}
        for code, set_ in pieces.items():
            p = by_code.get((set_, code))
            if p is None:
                raise SystemExit(f"preset {n}: {set_}_{code} is not in the manifest")
            slot_of[p["slot"]] = p

        for bundle, slot, noun, members in (("torso", "Torso", "Torso", TORSO_PIECES), ("legs", "Legs", "Legs", LEGS_PIECES)):
            r = row(f"sorc_preset_{n:02}_{bundle}", f"{noun} {n}", slot)
            for member in members:
                if member in slot_of:
                    add(r, slot_of[member])
            chosen[slot] = r["id"]

        for manifest_slot, (slot, _, _, side) in LOOSE.items():
            p = slot_of.get(manifest_slot)
            if p is None:
                continue
            opt_id = loose_ids[(p["set"], p["sidekick_code"])]
            if slot in chosen and chosen[slot] != opt_id:
                raise SystemExit(f"preset {n}: {slot} left and right disagree ({chosen[slot]} vs {opt_id})")
            chosen[slot] = opt_id
        presets.append({"id": f"sorcerer_{n:02}", "name": f"Sorcerer {n}", "options": chosen})

    order = ["Hat", "Hair", "FacialHair", "Torso", "Legs", "Shoes", "ShoulderLeft", "ShoulderRight", "HipLeft", "HipRight"]
    ordered = sorted(rows.values(), key=lambda r: (order.index(r["slot"]), r["id"]))
    return ordered, presets


def palette_swatches(manifest):
    """{material group or "" for default: [css colour, ...]}: each palette's colour in the four zones the torso,
    arms, hips and hats paint most, so a wardrobe swatch shows what the outfit will look like, not a 16x16 grid."""
    from collections import Counter
    from PIL import Image
    with open(os.path.join(STAGING, "palette_zones.json"), encoding="utf-8") as f:
        zones = {z["name"]: z for z in json.load(f)["zones"]}
    faces = Counter()
    for p in manifest["parts"]:
        if p["slot"] in ("torso", "hips", "arm_upper_l", "arm_lower_l", "attach_head"):
            for z in p["palette_zones"]:
                faces[z["name"]] += z["faces"]
    top = [name for name, _ in faces.most_common() if name in zones][:4]
    swatches = {}
    for i, tex in PALETTE_TEXTURE.items():
        im = Image.open(os.path.join(ASSETS, tex.replace("/", os.sep))).convert("RGB")
        scale = im.width // 32
        colours = []
        for name in top:
            z = zones[name]
            r, g, b = im.getpixel((z["png_x"] * scale + scale, z["png_y"] * scale + scale))
            colours.append(f"#{r:02x}{g:02x}{b:02x}")
        swatches["" if i == 0 else f"palette_{i:02}"] = colours
    return swatches


def write_catalogue(rows, presets, swatches):
    lines = [
        "// Generated by tools/gen_sorcerer_parts.py from the Sidekick manifest and Synty's Sidekick presets. Do not edit: rerun the tool.",
        "using System.Collections.Generic;",
        "",
        "namespace VoidMage.Character;",
        "",
        "/// <summary>",
        "/// The Fantasy Sorcerers pieces as wardrobe options. Hats, hairs, beards, shoes, shoulders and hips are one option per",
        "/// pack piece (left/right shoes are one); torso (with both arms and hands) and legs (with the hips) are Synty's six",
        "/// matched presets. Presets name the option of every slot they fill; a slot they leave out is worn bare, except hair,",
        "/// which is kept.",
        "/// </summary>",
        "public static class SorcererCatalogue",
        "{",
        "\tpublic sealed record Row( string Id, string Name, AppearanceSlot Slot, string[] Models, string[] Hides );",
        "\tpublic sealed record Preset( string Id, string Name, IReadOnlyDictionary<AppearanceSlot, string> Options );",
        "",
        "\tpublic static readonly IReadOnlyList<Row> Rows = new Row[]",
        "\t{",
    ]
    for r in rows:
        models = ", ".join(f'"{m}"' for m in r["models"])
        hides = ", ".join(r["hides"])
        lines.append(f'\t\tnew( "{r["id"]}", "{r["name"]}", AppearanceSlot.{r["slot"]}, new[] {{ {models} }}, new string[] {{ {hides} }} ),')
    lines += ["\t};", "", "\tpublic static readonly IReadOnlyList<Preset> Presets = new Preset[]", "\t{"]
    for p in presets:
        opts = ", ".join(f'[AppearanceSlot.{k}] = "{v}"' for k, v in p["options"].items())
        lines.append(f'\t\tnew( "{p["id"]}", "{p["name"]}", new Dictionary<AppearanceSlot, string> {{ {opts} }} ),')
    lines += ["\t};", "",
              "\t/// <summary>Material group (\"\" = default palette) -> the palette's colours in the outfit zones painted most, for the wardrobe swatches.</summary>",
              "\tpublic static readonly IReadOnlyDictionary<string, string[]> PaletteSwatches = new Dictionary<string, string[]>",
              "\t{"]
    for group, colours in swatches.items():
        lines.append(f'\t\t["{group}"] = new[] {{ ' + ", ".join(f'"{c}"' for c in colours) + " },")
    lines += ["\t};", "}", ""]
    path = os.path.join(ROOT, "Code", "Character", "SorcererCatalogue.cs")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    return path


def main():
    src_ct = os.path.join(STAGING, "charactertoon")
    dst_ct = os.path.join(ASSETS, "CharacterToon")
    n_fbx = copy_tree_files(src_ct, dst_ct, r"^sorc_\d+_\w+\.fbx$")
    n_vmdl = 0
    for name in sorted(os.listdir(src_ct)):
        if not re.match(r"^sorc_\d+_\w+\.vmdl$", name):
            continue
        with open(os.path.join(src_ct, name), encoding="utf-8") as f:
            text = f.read()
        with open(os.path.join(dst_ct, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(patch_vmdl(text, bool(HAIR_RE.match(name))))
        n_vmdl += 1

    n_png = copy_tree_files(os.path.join(STAGING, "materials", "sorcerer"), os.path.join(ASSETS, "materials", "sorcerer"), r"^palette_.*\.png$")

    with open(os.path.join(STAGING, "shaders", "SorcererPalette.vmat"), encoding="utf-8", errors="replace") as f:
        write_vmats(f.read())

    manifest_path = os.path.join(STAGING, "sorcerer_parts.json")
    shutil.copyfile(manifest_path, os.path.join(dst_ct, "sorcerer_parts.json"))
    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)
    rows, presets = build_catalogue(manifest)
    cs = write_catalogue(rows, presets, palette_swatches(manifest))

    print(f"copied {n_fbx} fbx, {n_vmdl} vmdl (+{PALETTES} material groups each), {n_png} palette png")
    print(f"wrote {PALETTES + 1} SorcererPalette vmats, {len(rows)} catalogue rows, {len(presets)} presets -> {os.path.relpath(cs, ROOT)}")
    by_slot = {}
    for r in rows:
        by_slot[r["slot"]] = by_slot.get(r["slot"], 0) + 1
    print("  " + ", ".join(f"{k} {v}" for k, v in by_slot.items()))
    for p in presets:
        print(f"  {p['name']}: " + ", ".join(f"{k}={v}" for k, v in p["options"].items()))


if __name__ == "__main__":
    main()
