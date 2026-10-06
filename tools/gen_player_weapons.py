"""
Build the player-only weapon skins (2026-09-17): Assets/PlayerWeapons/<skin id>.fbx + .vmdl for every skin in
Code/Inventory/WeaponSkins.cs that is not a starter.

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/gen_player_weapons.py [-- --only sword_kingsguard,staff_realms_03]

Why copies: the user resizes some skins for the first-person hand, and the pack's own models (and the monsters, whose
swords are baked into their character FBX by tools/skeleton_kinds.py) must keep their size. Each copy:

  * its own FBX: the pack FBX with the outline shell REBUILT from the base mesh (exact vertex copy, smooth area-weighted
    normals, no offset). Several pack weapons still carried an old toonify's 0.02 cm baked push with hard outline
    normals, which draws a broken ink line (see memory toon-outline-no-baked-offset); the shell keeps its node and
    material names, so the pack vmdl's remaps and import_filter still apply.
  * its own vmdl: the pack vmdl with the FBX path swapped and import_scale set from the weapon tuning.

Import scales come from the tuning the in-game weapon tuner saves (`weapon_tune`, Save): the project copy
Assets/data/weapon_tuning.json, overridden by the per-user save in the s&box data folder. This script merges the two,
writes the merged file back to the project with CompiledScale = the scale it just baked (so the game stops scaling the
prop at runtime), and writes the vmdls. Default import scale 0.2 (the starting sword and staff).
"""
import bpy, os, re, sys, json, math, collections

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
ASSETS = os.path.join(ROOT, "Assets")
OUT = os.path.join(ASSETS, "PlayerWeapons")
SKINS_CS = os.path.join(ROOT, "Code", "Inventory", "WeaponSkins.cs")
PROJECT_TUNING = os.path.join(ASSETS, "data", "weapon_tuning.json")
USER_TUNING = r"C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voidmage#local\voidmage\weapon_tuning.json"
DEFAULT_SCALE = 0.2

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = set()
if "--only" in argv:
    ONLY = set(argv[argv.index("--only") + 1].split(","))

# pack folder names as the vmdl paths spell them (lowercase) -> the folder on disk
PACK_DIRS = {d.lower(): d for d in os.listdir(ASSETS) if os.path.isdir(os.path.join(ASSETS, d))}


def skins():
    """(id, source vmdl, starter) from WeaponSkins.cs: the `new() { Id = ..., Source = ... }` lines and the Centred( ... ) ones."""
    text = open(SKINS_CS, encoding="utf-8").read()
    out = []
    for m in re.finditer(r'new\(\) \{ Id = (\w+|"[^"]+"), Name = "[^"]*", Hand = HandSide\.\w+, Source = ([\w.]+|"[^"]+")(.*?)\}', text):
        sid, src, rest = m.group(1), m.group(2), m.group(3)
        starter = "Starter = true" in rest
        if not sid.startswith('"') or not src.startswith('"'):
            continue   # DefaultSword / DefaultStaff: the starters, player-only models already
        out.append((sid.strip('"'), src.strip('"'), starter))
    for m in re.finditer(r'Centred\( "(\w+)", "[^"]*", "([^"]+)"', text):
        out.append((m.group(1), m.group(2), False))
    return out


def load_tuning():
    merged = {}
    for path in (PROJECT_TUNING, USER_TUNING):
        if not os.path.exists(path):
            continue
        try:
            data = json.load(open(path, encoding="utf-8-sig"))
        except Exception as e:
            print(f"[player weapons] could not read {path}: {e}")
            continue
        for entry in data.get("Weapons", []):
            merged[entry["Id"]] = {**merged.get(entry["Id"], {}), **entry}
    return merged


def disk_path(pack_path):
    """'dungeonpack/sm_wep_x.fbx' -> Assets/DungeonPack/sm_wep_x.fbx (the file's own case found on disk)."""
    folder, name = pack_path.split("/", 1)
    d = os.path.join(ASSETS, PACK_DIRS[folder.lower()])
    for f in os.listdir(d):
        if f.lower() == name.lower():
            return os.path.join(d, f)
    raise FileNotFoundError(pack_path)


def vertex_smooth_normals(me):
    """One smooth (all faces around the position, area-weighted) normal per vertex: the outline shell's normals."""
    key = lambda co: (round(co[0], 5), round(co[1], 5), round(co[2], 5))
    keys = [key(v.co) for v in me.vertices]
    acc = collections.defaultdict(lambda: [0.0, 0.0, 0.0])
    for p in me.polygons:
        for l in range(p.loop_start, p.loop_start + p.loop_total):
            s = acc[keys[me.loops[l].vertex_index]]
            s[0] += p.normal[0] * p.area
            s[1] += p.normal[1] * p.area
            s[2] += p.normal[2] * p.area
    res = []
    for k in keys:
        s = acc[k]
        ln = math.sqrt(s[0] ** 2 + s[1] ** 2 + s[2] ** 2)
        res.append((s[0] / ln, s[1] / ln, s[2] / ln) if ln > 1e-12 else (0.0, 0.0, 1.0))
    return res


def rebuild_fbx(src, dst):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, use_custom_normals=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    outlines = [o for o in meshes if "outline" in o.name.lower()]
    bases = [o for o in meshes if o not in outlines]
    if len(bases) != 1 or len(outlines) != 1:
        raise RuntimeError(f"expected one base and one outline mesh, got {[o.name for o in meshes]}")
    base, old = bases[0], outlines[0]
    shell_name, shell_mat = old.name, old.data.materials[0] if old.data.materials else None
    bpy.data.objects.remove(old, do_unlink=True)

    shell = base.copy()
    shell.data = base.data.copy()
    bpy.context.scene.collection.objects.link(shell)
    shell.name = shell_name
    shell.data.name = shell_name
    sme = shell.data
    sme.materials.clear()
    if shell_mat is not None:
        sme.materials.append(shell_mat)
    for p in sme.polygons:
        p.use_smooth = True
        p.material_index = 0
    vn = vertex_smooth_normals(sme)
    sme.normals_split_custom_set([vn[sme.loops[l].vertex_index] for l in range(len(sme.loops))])

    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=False, mesh_smooth_type='FACE',
                             use_custom_props=False, use_mesh_modifiers=False, add_leaf_bones=False, path_mode='AUTO')
    return shell_name


def main():
    os.makedirs(OUT, exist_ok=True)
    tuning = load_tuning()
    built = 0
    for sid, source, starter in skins():
        if starter or (ONLY and sid not in ONLY):
            continue
        src_vmdl = disk_path(source)
        vmdl = open(src_vmdl, encoding="utf-8").read()
        fbx_paths = set(m.group(1) for m in re.finditer(r'filename = "([^"]+\.fbx)"', vmdl, re.I))
        if len(fbx_paths) != 1:
            print(f"[player weapons] {sid}: {source} names {len(fbx_paths)} FBX files, skipped")
            continue
        fbx_rel = fbx_paths.pop()
        dst_fbx = os.path.join(OUT, f"{sid}.fbx")
        rebuild_fbx(disk_path(fbx_rel), dst_fbx)

        scale = float(tuning.get(sid, {}).get("ImportScale", DEFAULT_SCALE) or DEFAULT_SCALE)
        text = vmdl.replace(f'filename = "{fbx_rel}"', f'filename = "playerweapons/{sid}.fbx"')
        text = re.sub(r"import_scale = [0-9.]+", f"import_scale = {scale:g}", text)
        # FantasyKingdomOutline.vmat is tuned for houses (g_flOutlineMinWorld 0.2, screen width 0.8): on a hand-sized
        # blade that minimum clamps the ink line thick. Every other pack's outline is 0.05 / 1.0, so the player copies
        # all draw with the dungeon one (user, 2026-09-17: Kingsguard, Sidearm, Grave Sword and Grave Longsword)
        text = text.replace('to = "shaders/fantasykingdomoutline.vmat"', 'to = "shaders/dungeonoutline.vmat"')
        open(os.path.join(OUT, f"{sid}.vmdl"), "w", encoding="utf-8").write(text)
        entry = tuning.setdefault(sid, {"Id": sid})
        entry["ImportScale"] = scale
        entry["CompiledScale"] = scale
        built += 1
        print(f"[player weapons] {sid}: {source} -> playerweapons/{sid}.vmdl at import_scale {scale:g}")

    # both copies get the baked scales: a user save left with an old CompiledScale would scale the new model twice
    merged = {"Weapons": sorted(tuning.values(), key=lambda e: e["Id"])}
    os.makedirs(os.path.dirname(PROJECT_TUNING), exist_ok=True)
    json.dump(merged, open(PROJECT_TUNING, "w", encoding="utf-8"), indent=1)
    if os.path.exists(USER_TUNING):
        json.dump(merged, open(USER_TUNING, "w", encoding="utf-8"), indent=1)
    print(f"[player weapons] built {built}; tuning written to {PROJECT_TUNING}" + (" and the user save" if os.path.exists(USER_TUNING) else ""))


main()
