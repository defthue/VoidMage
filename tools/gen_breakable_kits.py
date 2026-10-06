"""Breakable kits: the debris a crate or a barrel turns into, cut out of the prop's own mesh.

    "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe" -b --factory-startup ^
        --python tools/gen_breakable_kits.py -- [kit ...]        (no names = every kit)

The Synty props are ONE mesh object made of disconnected shells: a crate is a frame cage, six panels,
six diagonal braces and (on 01/03/04) eight corner blocks; a barrel is a stave shell, two lids, four
uprights and two hoops. Blender's "separate by loose parts" gives those back, and every part stays
exactly where it was in the prop, so a piece spawned at the prop's transform draws where the intact
mesh's geometry was and the break is seamless (heden's ore-kit convention). The big shells are then
cut into boards: panels into two strips, the stave body into six wedges, hoops into halves, the crate
frame into four corner posts (the corner blocks join their post). Single-sided parts (the stave shell,
the lid discs) are first given real thickness with the Solidify modifier, inward, so their insides carry
the outside's UVs instead of showing a black back. Cut faces are capped and get a UV from a neighbouring
face. Every piece then gets its own toon outline hull (the piece pushed out a hair, faces flipped);
Synty's `<name>_Outline` shell is not reused because it only wrapped the OUTSIDE of the shell.

Output, per kit:
    Assets/Breakables/<kit>.fbx               all pieces, named pNN / pNN_Outline, at the prop origin
    Assets/Breakables/<kit>_pNN.vmdl           one per piece: render = piece + outline, physics =
                                              PhysicsHullFromRender SingleHull (a dynamic body has to be
                                              convex; PhysX cannot simulate a concave trimesh)
    Code/Combat/BreakableKits.Generated.cs     the piece lists, largest first, plus the extras

Extras are pieces that are NOT authored in place: `barrel_bits` (SM_Prop_Barrel_Broken_02 split into its
bent planks and rings, each re-centred on its own bounds), `plank_01/02` and `vase_shard_01..04` (the pack's
own models, referenced from their DungeonPack FBX with a hull). The game spawns those at jittered spots
inside the prop.

Scale: the imported objects carry a 0.01 object scale with cm mesh data (Blender FBX importer), exported
unapplied with apply_unit_scale, the same path prep_skeleton_npc.py takes; the script then re-reads the
exported FBX and derives import_scale the way gen_dungeon_assets does (node scale 100 -> 0.003937, else
0.3937) and checks the piece bounds add up to the source prop's bounds.
"""
import json
import math
import os
import re
import struct
import sys
import zlib

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)   # Blender runs this as a script: make tools/ importable
import wood_props
PROJ = os.path.normpath(os.path.join(HERE, ".."))
OUT = os.path.join(PROJ, "Assets", "Breakables")
GEN_CS = os.path.join(PROJ, "Code", "Combat", "BreakableKits.Generated.cs")
CM_TO_UNIT = 0.3937

# The packs a kit's source can come from. `prefix` is the runtime model path prefix (the key the game looks a
# kit up by: dungeonpack/sm_prop_barrel_01.vmdl), `matfrom` the material name the source FBX carries (a Blender
# re-export keeps it, so the piece vmdl's remap has to match it or the piece renders on the default material),
# `outline` the outline vmat the pack's own vmdls use, and `vmat` a single material for every piece of that pack
# (None = the wood/stone choice below, which only DungeonPack has).
PACKS = {
    "pack": {"dir": "DungeonPack", "prefix": "dungeonpack", "matfrom": "dungeonshader.vmat",
             "outline": "shaders/dungeonoutline.vmat", "vmat": None},
    "realms": {"dir": "DungeonRealms", "prefix": "dungeonrealms", "matfrom": "realmsshader.vmat",
               "outline": "shaders/realmsoutline.vmat", "vmat": "shaders/realmsshader.vmat"},
    # the gaol's two packs (2026-09-11); every kit prop here is on the packs' main atlas
    "darkfantasy": {"dir": "DarkFantasy", "prefix": "darkfantasy", "matfrom": "darkfantasyshader.vmat",
                    "outline": "shaders/darkfantasyoutline.vmat", "vmat": "shaders/darkfantasyshader.vmat"},
    "darkfortress": {"dir": "DarkFortress", "prefix": "darkfortress", "matfrom": "darkfortressshader.vmat",
                     "outline": "shaders/darkfortressoutline.vmat", "vmat": "shaders/darkfortressshader.vmat"},
}
PACK = os.path.join(PROJ, "Assets", PACKS["pack"]["dir"])

# kit id -> (source FBX stem, rule[, pack]). A Realms kit is named realms_* on purpose: the two packs share
# stems (both have an SM_Prop_Barrel_01), and a same-named kit would overwrite the stone one's pieces in
# Assets/Breakables and leave both dictionary keys pointing at the same files.
KITS = {
    "crate_wood_02": ("SM_Prop_Crate_Wood_02", "crate"),
    "crate_wood_03": ("SM_Prop_Crate_Wood_03", "crate"),
    "crate_wood_04": ("SM_Prop_Crate_Wood_04", "crate"),
    "crate_wood_05": ("SM_Prop_Crate_Wood_05", "crate"),
    "barrel_01": ("SM_Prop_Barrel_01", "barrel"),
    "barrel_03": ("SM_Prop_Barrel_03", "barrel"),
    "barrel_broken_01": ("SM_Prop_Barrel_Broken_01", "barrel"),
    # extras: a fully broken barrel's bent planks and rings, re-centred so they can be spawned anywhere
    "barrel_bits": ("SM_Prop_Barrel_Broken_02", "bits"),
    # wooden furniture (user's list, 2026-09-07): their own parts, long parts segmented, sheets into boards
    "table_01": ("SM_Prop_Table_01", "furniture"),
    "table_round_01": ("SM_Prop_Table_Round_01", "furniture"),
    "table_round_02": ("SM_Prop_Table_Round_02", "furniture"),
    "stool_01": ("SM_Prop_Stool_01", "furniture"),
    "stool_02": ("SM_Prop_Stool_02", "furniture"),
    "stool_03": ("SM_Prop_Stool_03", "furniture"),
    "bench_01": ("SM_Prop_Bench_01", "furniture"),
    "bed_01": ("SM_Prop_Bed_01", "furniture"),
    "bed_02": ("SM_Prop_Bed_02", "furniture"),
    "bookcase_01": ("SM_Prop_Bookcase_01", "furniture"),
    "bookcase_02": ("SM_Prop_Bookcase_02", "furniture"),
    "bookcase_03": ("SM_Prop_Bookcase_03", "furniture"),
    "coffin_01": ("SM_Prop_Coffin_01", "furniture"),
    "coffin_lid_01": ("SM_Prop_Coffin_Lid_01", "furniture"),
    "weaponrack_01": ("SM_Prop_WeaponRack_01", "furniture"),
    # the forge-hold (Dungeon Realms, 2026-09-11): the user's barrels and shelves, and the rack he wanted in planks
    "realms_barrel_01": ("SM_Prop_Barrel_01", "barrel", "realms"),
    "realms_barrel_02": ("SM_Prop_Barrel_02", "barrel", "realms"),
    "realms_barrel_broken_01": ("SM_Prop_Barrel_Broken_01", "barrel", "realms"),
    "realms_barrel_broken_02": ("SM_Prop_Barrel_Broken_02", "barrel", "realms"),
    "realms_barrel_shelf_large_01": ("SM_Prop_Barrel_Shelf_Large_01", "furniture", "realms"),
    "realms_barrel_shelf_large_02": ("SM_Prop_Barrel_Shelf_Large_02", "furniture", "realms"),
    "realms_barrel_shelf_small_01": ("SM_Prop_Barrel_Shelf_Small_01", "furniture", "realms"),
    "realms_barrel_shelf_small_02": ("SM_Prop_Barrel_Shelf_Small_02", "furniture", "realms"),
    "realms_weapon_rack_03": ("SM_Prop_Dwarf_Weapon_Rack_03", "furniture", "realms"),
    # the gaol (Dark Fantasy df_*, Dark Fortress dk_*, 2026-09-11): its barrels and crates, the rack at 1.4x, and the
    # plank panels that board up a dead-end room's doorway (one panel per hit, DungeonBuilder.BuildBarricade)
    "df_barrel_01": ("SM_Prop_Barrel_01", "barrel", "darkfantasy"),
    "df_barrel_open_01": ("SM_Prop_Barrel_Open_01", "barrel", "darkfantasy"),
    "df_crate_01": ("SM_Prop_Crate_01", "crate", "darkfantasy"),
    "df_crate_02": ("SM_Prop_Crate_02", "crate", "darkfantasy"),
    # the Fortress crate is built from thin boards with no frame cage: the crate rule keeps two of them, the furniture
    # rule keeps every board (the runtime still throws loose planks with it, BreakableKits picks the branch by name)
    "dk_crate_01": ("SM_Prop_Crate_01", "furniture", "darkfortress"),
    "df_rack_weapon_01": ("SM_Prop_Rack_Weapon_01", "furniture", "darkfantasy"),
    "df_boarded_wood_01": ("SM_Prop_Boarded_Wood_01", "furniture", "darkfantasy"),
    "df_boarded_wood_02": ("SM_Prop_Boarded_Wood_02", "furniture", "darkfantasy"),
    "df_boarded_wood_03": ("SM_Prop_Boarded_Wood_03", "furniture", "darkfantasy"),
    "df_boarded_wood_04": ("SM_Prop_Boarded_Wood_04", "furniture", "darkfantasy"),
    # everything wooden in the gaol breaks (user, 2026-09-11): the furniture, the torture timber, the siege engines
    "dk_table_01": ("SM_Prop_Table_01", "furniture", "darkfortress"),
    "dk_table_02": ("SM_Prop_Table_02", "furniture", "darkfortress"),
    "dk_table_03": ("SM_Prop_Table_03", "furniture", "darkfortress"),
    "dk_table_small_01": ("SM_Prop_Table_Small_01", "furniture", "darkfortress"),
    "dk_table_01_damaged_01": ("SM_Prop_Table_01_Damaged_01", "furniture", "darkfortress"),
    "dk_table_02_damaged_01": ("SM_Prop_Table_02_Damaged_01", "furniture", "darkfortress"),
    "df_table_01_damaged_01": ("SM_Prop_Table_01_Damaged_01", "furniture", "darkfantasy"),
    "df_table_01_damaged_02": ("SM_Prop_Table_01_Damaged_02", "furniture", "darkfantasy"),
    "dk_chair_01": ("SM_Prop_Chair_01", "furniture", "darkfortress"),
    "dk_chair_03": ("SM_Prop_Chair_03", "furniture", "darkfortress"),
    "dk_chair_04": ("SM_Prop_Chair_04", "furniture", "darkfortress"),
    "dk_chair_05": ("SM_Prop_Chair_05", "furniture", "darkfortress"),
    "dk_chair_01_damaged_01": ("SM_Prop_Chair_01_Damaged_01", "furniture", "darkfortress"),
    "df_chair_01": ("SM_Prop_Chair_01", "furniture", "darkfantasy"),
    "df_chair_01_damaged_01": ("SM_Prop_Chair_01_Damaged_01", "furniture", "darkfantasy"),
    "df_stool_bar_01": ("SM_Prop_Stool_Bar_01", "furniture", "darkfantasy"),
    "dk_bench_01": ("SM_Prop_Bench_01", "furniture", "darkfortress"),
    "df_bed_01": ("SM_Prop_Bed_01", "furniture", "darkfantasy"),
    "df_bed_01_damaged_01": ("SM_Prop_Bed_01_Damaged_01", "furniture", "darkfantasy"),
    "dk_bed_01": ("SM_Prop_Bed_01", "furniture", "darkfortress"),
    "df_bookshelf_01": ("SM_Prop_Bookshelf_01", "furniture", "darkfantasy"),
    "df_bookshelf_01_damaged_01": ("SM_Prop_Bookshelf_01_Damaged_01", "furniture", "darkfantasy"),
    "df_bookshelf_double_01": ("SM_Prop_Bookshelf_Double_01", "furniture", "darkfantasy"),
    "df_bookshelf_double_01_damaged_01": ("SM_Prop_Bookshelf_Double_01_Damaged_01", "furniture", "darkfantasy"),
    "dk_bookshelf_01": ("SM_Prop_Bookshelf_01", "furniture", "darkfortress"),
    "dk_bookshelf_01_damaged_01": ("SM_Prop_Bookshelf_01_Damaged_01", "furniture", "darkfortress"),
    "df_pew_01": ("SM_Prop_Pew_01", "furniture", "darkfantasy"),
    "df_pew_01_damaged_01": ("SM_Prop_Pew_01_Damaged_01", "furniture", "darkfantasy"),
    "df_pew_large_01": ("SM_Prop_Pew_Large_01", "furniture", "darkfantasy"),
    "df_pew_large_01_damaged_02": ("SM_Prop_Pew_Large_01_Damaged_02", "furniture", "darkfantasy"),
    "df_pew_large_01_damaged_03": ("SM_Prop_Pew_Large_01_Damaged_03", "furniture", "darkfantasy"),
    "df_desk_reading_01": ("SM_Prop_Desk_Reading_01", "furniture", "darkfantasy"),
    "dk_shelf_01": ("SM_Prop_Shelf_01", "furniture", "darkfortress"),
    "dk_shelf_02": ("SM_Prop_Shelf_02", "furniture", "darkfortress"),
    "dk_shelf_03": ("SM_Prop_Shelf_03", "furniture", "darkfortress"),
    "dk_shelf_04": ("SM_Prop_Shelf_04", "furniture", "darkfortress"),
    "dk_shelf_02_damaged_01": ("SM_Prop_Shelf_02_Damaged_01", "furniture", "darkfortress"),
    "df_shelf_01": ("SM_Prop_Shelf_01", "furniture", "darkfantasy"),
    "dk_cupboard_01": ("SM_Prop_Cupboard_01", "furniture", "darkfortress"),
    "df_counter_bar_01": ("SM_Prop_Counter_Bar_01", "furniture", "darkfantasy"),
    "df_lecturn_01": ("SM_Prop_Lecturn_01", "furniture", "darkfantasy"),
    "dk_lectern_01": ("SM_Prop_Lectern_01", "furniture", "darkfortress"),
    "df_keg_01": ("SM_Prop_Keg_01", "furniture", "darkfantasy"),
    "dk_crate_destroyed_01": ("SM_Prop_Crate_Destroyed_01", "furniture", "darkfortress"),
    "dk_wheel_barrow_01": ("SM_Prop_Wheel_Barrow_01", "furniture", "darkfortress"),
    "dk_throne_01": ("SM_Prop_Throne_01", "furniture", "darkfortress"),
    "dk_throne_01_damaged_01": ("SM_Prop_Throne_01_Damaged_01", "furniture", "darkfortress"),
    "df_stock_01": ("SM_Prop_Stock_01", "furniture", "darkfantasy"),
    "df_gallows_01": ("SM_Prop_Gallows_01", "furniture", "darkfantasy"),
    "df_gibbet_01": ("SM_Prop_Gibbet_01", "furniture", "darkfantasy"),
    "df_gibbet_01_damaged_01": ("SM_Prop_Gibbet_01_Damaged_01", "furniture", "darkfantasy"),
    "df_pyre_02": ("SM_Prop_Pyre_02", "furniture", "darkfantasy"),
    "df_pyre_02_burnt_01": ("SM_Prop_Pyre_02_Burnt_01", "furniture", "darkfantasy"),
    "df_pyre_stake_01": ("SM_Prop_Pyre_Stake_01", "furniture", "darkfantasy"),
    "df_pyre_stake_01_burnt_01": ("SM_Prop_Pyre_Stake_01_Burnt_01", "furniture", "darkfantasy"),
    "df_pyre_stake_01_burnt_02": ("SM_Prop_Pyre_Stake_01_Burnt_02", "furniture", "darkfantasy"),
    "df_pyre_stake_01_demon_01": ("SM_Prop_Pyre_Stake_01_Demon_01", "furniture", "darkfantasy"),
    "df_pyre_stake_02": ("SM_Prop_Pyre_Stake_02", "furniture", "darkfantasy"),
    "df_pyre_stake_02_burnt_01": ("SM_Prop_Pyre_Stake_02_Burnt_01", "furniture", "darkfantasy"),
    "df_pyre_stake_02_burnt_02": ("SM_Prop_Pyre_Stake_02_Burnt_02", "furniture", "darkfantasy"),
    "df_pyre_stake_02_burnt_03": ("SM_Prop_Pyre_Stake_02_Burnt_03", "furniture", "darkfantasy"),
    "df_pyre_stake_02_burnt_04": ("SM_Prop_Pyre_Stake_02_Burnt_04", "furniture", "darkfantasy"),
    "df_post_01": ("SM_Prop_Post_01", "furniture", "darkfantasy"),
    "dk_torture_device_01": ("SM_Prop_Torture_Device_01", "furniture", "darkfortress"),
    "dk_wheel_01": ("SM_Prop_Wheel_01", "furniture", "darkfortress"),
    "dk_stretching_rack_01": ("SM_Prop_Stretching_Rack_01", "furniture", "darkfortress"),
    "dk_table_suspended_01": ("SM_Prop_Table_Suspended_01", "furniture", "darkfortress"),
    "dk_trap_wall_01": ("SM_Prop_Trap_Wall_01", "furniture", "darkfortress"),
    "dk_barricade_01": ("SM_Prop_Barricade_01", "furniture", "darkfortress"),
    "dk_barricade_02": ("SM_Prop_Barricade_02", "furniture", "darkfortress"),
    "df_barricade_03": ("SM_Prop_Barricade_03", "furniture", "darkfantasy"),
    "df_barricade_04": ("SM_Prop_Barricade_04", "furniture", "darkfantasy"),
    "dk_ladder_01": ("SM_Prop_Ladder_01", "furniture", "darkfortress"),
    "dk_coffin_01": ("SM_Prop_Coffin_01", "furniture", "darkfortress"),
    "dk_coffin_01_damaged_01": ("SM_Prop_Coffin_01_Damaged_01", "furniture", "darkfortress"),
    "df_wagon_01_damaged_01": ("SM_Prop_Wagon_01_Damaged_01", "furniture", "darkfantasy"),
    "df_organ_01": ("SM_Prop_Organ_01", "furniture", "darkfantasy"),
    "df_altar_table_01": ("SM_Prop_Altar_Table_01", "furniture", "darkfantasy"),
    "df_tabernacle_01": ("SM_Prop_Tabernacle_01", "furniture", "darkfantasy"),
    "dk_arrow_basket_01": ("SM_Prop_Arrow_Basket_01", "furniture", "darkfortress"),
    "df_meat_bucket_01": ("SM_Prop_Meat_Bucket_01", "furniture", "darkfantasy"),
    "dk_ballista_01": ("SM_Prop_Ballista_01", "furniture", "darkfortress"),
    "dk_veh_cannon_01": ("SM_Veh_Cannon_01", "furniture", "darkfortress"),
    "dk_veh_arrow_launcher_01": ("SM_Veh_Arrow_Launcher_01", "furniture", "darkfortress"),
    "dk_slingshot_01": ("SM_Prop_Slingshot_01", "furniture", "darkfortress"),
    "dk_boiling_oil_01": ("SM_Prop_Boiling_Oil_01", "furniture", "darkfortress"),
    "dk_lever_01": ("SM_Prop_Lever_01", "furniture", "darkfortress"),
    # the DungeonPack wooden chest the gaol's barracks use
    "chest_wood_01": ("SM_Prop_Chest_Wood_01", "furniture"),
    "chest_wood_lid": ("SM_Prop_Chest_Wood_Lid", "furniture"),
    # every chest breaks (user 2026-09-16): the DungeonPack chests every kit uses, and their lids
    "chest_01": ("SM_Prop_Chest_01", "chest"),
    "chest_01_lid": ("SM_Prop_Chest_01_Lid", "chest"),
    "chest_02": ("SM_Prop_Chest_02", "chest"),
    "chest_02_lid": ("SM_Prop_Chest_02_Lid", "chest"),
    "chest_03": ("SM_Prop_Chest_03", "chest"),
    "chest_03_lid": ("SM_Prop_Chest_03_Lid", "chest"),
    "chest_04": ("SM_Prop_Chest_04", "chest"),
    "chest_04_lid": ("SM_Prop_Chest_04_Lid", "chest"),
}

# most pieces one furniture kit may have; the smallest merge into their neighbours past it
MAX_PIECES = 28

# the pack's own small models that get a hull vmdl of their own (render nodes from the DungeonPack FBX)
STANDALONE = {
    "plank_01": "SM_Prop_Plank_01",
    "plank_02": "SM_Prop_Plank_02",
    "vase_shard_01": "SM_Prop_Vase_Shard_01",
    "vase_shard_02": "SM_Prop_Vase_Shard_02",
    "vase_shard_03": "SM_Prop_Vase_Shard_03",
    "vase_shard_04": "SM_Prop_Vase_Shard_04",
    # a bookcase's books mostly merge into its shelves under the piece cap: these fly out instead
    "book_01": "SM_Item_Book_01",
    "bookpile_01": "SM_Item_BookPile_01",
    # the masonry the secret wall's doorway collapses into: the pack's own bricks, hulled so they can
    # be dynamic bodies (their dungeonpack vmdls are PhysicsMeshFile, which PhysX cannot simulate)
    "brick_01": "SM_Prop_Brick_01",
    "brick_02": "SM_Prop_Brick_02",
}

UP = 1   # mesh space is the raw FBX space: x, y up, z (the importer keeps the axis fix on the object)
SHELL_THICKNESS = 3.0   # cm given to single-sided parts (barrel staves, lid discs) before they are cut


def log(*a):
    print("[kits]", *a, flush=True)


# --------------------------------------------------------------------------- bmesh helpers

def bounds(bm):
    if not bm.verts:
        return Vector((0, 0, 0)), Vector((0, 0, 0))
    lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
    hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
    return lo, hi


def dims(bm):
    lo, hi = bounds(bm)
    return hi - lo


def centre(bm):
    lo, hi = bounds(bm)
    return (lo + hi) * 0.5


def volume(bm):
    d = dims(bm)
    return max(d.x, 0.1) * max(d.y, 0.1) * max(d.z, 0.1)


def components(bm):
    """Connected components as lists of vertex indices."""
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    seen = [False] * len(bm.verts)
    out = []
    for v0 in bm.verts:
        if seen[v0.index]:
            continue
        comp, stack = [], [v0]
        seen[v0.index] = True
        while stack:
            v = stack.pop()
            comp.append(v.index)
            for e in v.link_edges:
                o = e.other_vert(v)
                if not seen[o.index]:
                    seen[o.index] = True
                    stack.append(o)
        out.append(comp)
    return out


def extract(bm, keep):
    """A new bmesh holding only the vertices in `keep` (indices into bm)."""
    part = bm.copy()
    part.verts.ensure_lookup_table()
    part.verts.index_update()
    keep = set(keep)
    bmesh.ops.delete(part, geom=[v for v in part.verts if v.index not in keep], context="VERTS")
    return part


def loose_parts(bm):
    return [extract(bm, c) for c in components(bm)]


def cap(bm, res):
    """Fill the cut loops and give the new faces a UV from a neighbouring face."""
    cut_edges = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
    if not cut_edges:
        return
    filled = bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
    new_faces = set(filled["faces"])
    uv = bm.loops.layers.uv.active
    if uv is None:
        return
    for f in new_faces:
        for l in f.loops:
            src = next((ol for ol in l.vert.link_loops if ol.face not in new_faces), None)
            if src is not None:
                l[uv].uv = src[uv].uv


def split(bm, co, no):
    """Two bmeshes: the geometry on the negative side of the plane and the positive side, both capped."""
    out = []
    for clear_outer, clear_inner in ((True, False), (False, True)):
        part = bm.copy()
        res = bmesh.ops.bisect_plane(part, geom=part.verts[:] + part.edges[:] + part.faces[:], dist=1e-4,
                                     plane_co=co, plane_no=no, clear_outer=clear_outer, clear_inner=clear_inner)
        if len(part.faces) == 0:
            part.free()
            continue
        cap(part, res)
        out.append(part)
    return out


def split_many(pieces, planes):
    for co, no in planes:
        nxt = []
        for p in pieces:
            nxt.extend(split(p, co, no))
        pieces = nxt
    return pieces


def join(dst, src):
    tmp = bpy.data.meshes.new("tmp_join")
    src.to_mesh(tmp)
    dst.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)


def axis_vec(i):
    v = Vector((0, 0, 0))
    v[i] = 1.0
    return v


def face_uv(bm):
    """A UV inside the largest face's atlas patch: the flat colour that face is painted with."""
    uv = bm.loops.layers.uv.active
    if uv is None or not bm.faces:
        return Vector((0.5, 0.5))
    f = max(bm.faces, key=lambda f: f.calc_area())
    acc = Vector((0.0, 0.0))
    for l in f.loops:
        acc += l[uv].uv
    return acc / len(f.loops)


def make_box(lo, hi, uv):
    """A closed box between two corners, every face painted with one atlas UV (a Synty flat colour)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    c = (lo + hi) * 0.5
    s = hi - lo
    for v in bm.verts:
        v.co = Vector((c.x + v.co.x * s.x, c.y + v.co.y * s.y, c.z + v.co.z * s.z))
    layer = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        f.material_index = 0
        for l in f.loops:
            l[layer].uv = uv
    bm.normal_update()
    return bm


def is_open(bm):
    """A single-sided surface (the barrel's stave shell, a lid disc): anything with a boundary edge."""
    return any(e.is_boundary for e in bm.edges)


def _solidify_once(bm, thickness, offset):
    me = bpy.data.meshes.new("tmp_solid")
    bm.to_mesh(me)
    ob = bpy.data.objects.new("tmp_solid", me)
    bpy.context.scene.collection.objects.link(ob)
    mod = ob.modifiers.new("solid", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = offset
    mod.use_even_offset = False
    mod.use_rim = True
    mod.use_quality_normals = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    out = bmesh.new()
    out.from_mesh(ev.to_mesh())
    ev.to_mesh_clear()
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me)
    return out


def solidify(bm, thickness):
    """Give a single-sided shell real thickness, INWARD, through the Solidify modifier so the inner shell and the rim
    carry the outside's UVs (a stave lying face down must show wood, not a black plane). Mesh data is in cm, so
    thickness is cm. Inward is judged, not assumed: the side that keeps the part's bounds is the inside, so a part
    whose normals happen to point the other way still thickens into itself and the prop's silhouette never grows."""
    lo, hi = bounds(bm)
    best, best_grow = None, None
    for offset in (-1.0, 1.0):
        out = _solidify_once(bm, thickness, offset)
        olo, ohi = bounds(out)
        grow = max(max(lo[i] - olo[i], ohi[i] - hi[i]) for i in range(3))
        if best is None or grow < best_grow:
            if best is not None:
                best.free()
            best, best_grow = out, grow
        else:
            out.free()
    # a part thinner than the thickness (a 2 cm panel) grows by the difference INTO the prop, which the prop-level
    # bounds check tolerates; more than that means the normals pointed the wrong way somewhere
    if best_grow > thickness + 0.5:
        log("   WARNING: solidified part grew %.1f cm past its bounds (mixed normals?)" % best_grow)
    bm.free()
    return best


def make_outline(bm):
    """The toon hull for a finished piece: the piece itself pushed a hair along its normals with the faces flipped,
    so the hull wraps the whole solid (Synty's outline only wrapped the outside of the shell, and from the concave
    side of a cut stave you looked straight at its black back)."""
    o = bm.copy()
    o.normal_update()
    for v in o.verts:
        v.co += v.normal * 0.02
    bmesh.ops.reverse_faces(o, faces=o.faces[:])
    for f in o.faces:
        f.material_index = 0
    return o


# --------------------------------------------------------------------------- rules

class Part:
    """One loose part and its outline twin, cut in step. `was_open` remembers whether it started as a single-sided
    shell (the rules tell a closed box from a solidified cage by that, not by vertex counts)."""

    def __init__(self, main, outline, was_open=False, kind="part"):
        self.main = main
        self.outline = outline
        self.was_open = was_open
        # what the game treats it as: "ring" pieces are rationed per break (1-3 of them), the rest always spawn
        self.kind = kind

    def cut(self, planes):
        mains = split_many([self.main], planes)
        outs = split_many([self.outline], planes) if self.outline is not None else [None] * len(mains)
        # pair the outline fragments to the main fragments by centre
        pieces = []
        for m in mains:
            best = None
            if outs and outs[0] is not None:
                c = centre(m)
                best = min(outs, key=lambda o: (centre(o) - c).length)
                outs.remove(best)
            pieces.append(Part(m, best, self.was_open, self.kind))
        return pieces


def horizontal_axes():
    return [i for i in range(3) if i != UP]


def rule_crate(parts):
    """Frame cage -> its 12 straight beams (cut just inside every face; the junction cubes and corner blocks join
    the nearest post); panels -> 2 boards each; the diagonal X braces are dropped (an X flying off read as a weird
    bit: the game throws the pack's planks instead). A crate that is one closed shell (Crate_Wood_05) yields no
    in-place pieces at all and breaks into planks only."""
    hx, hz = horizontal_axes()
    beams, blocks, boards = [], [], []
    sheets = [p for p in parts if min(dims(p.main)) < 6 and max(dims(p.main)) > 60]
    opening = max((max(dims(p.main)) for p in sheets), default=0)
    for p in parts:
        d = dims(p.main)
        mx, mn = max(d), min(d)
        if mx > 100 and mn > 40:
            if not p.was_open:
                log("   closed shell %.0f x %.0f x %.0f cm: no in-place pieces (planks only)" % (d.x, d.y, d.z))
                continue
            # The cage is rebuilt as 12 clean box beams (4 posts, 8 rails) sized from its bounds and the panel opening,
            # painted with the cage's own atlas colour: cutting the solidified cage left its inner skin behind as
            # square tubes. A box beam is exactly what the frame reads as under the toon shader anyway.
            t = (mx - opening) / 2.0 + 1.0 if opening > 0 else 10.0
            lo, hi = bounds(p.main)
            uv = face_uv(p.main)
            def box(a0, a1, b0, b1, c0, c1):
                blo, bhi = Vector((0, 0, 0)), Vector((0, 0, 0))
                blo[hx], bhi[hx] = a0, a1
                blo[UP], bhi[UP] = b0, b1
                blo[hz], bhi[hz] = c0, c1
                return Part(make_box(blo, bhi, uv), None, False, "beam")
            for x0, x1 in ((lo[hx], lo[hx] + t), (hi[hx] - t, hi[hx])):
                for z0, z1 in ((lo[hz], lo[hz] + t), (hi[hz] - t, hi[hz])):
                    beams.append(box(x0, x1, lo[UP], hi[UP], z0, z1))                       # posts
                for y0, y1 in ((lo[UP], lo[UP] + t), (hi[UP] - t, hi[UP])):
                    beams.append(box(x0, x1, y0, y1, lo[hz] + t, hi[hz] - t))               # rails along z
            for z0, z1 in ((lo[hz], lo[hz] + t), (hi[hz] - t, hi[hz])):
                for y0, y1 in ((lo[UP], lo[UP] + t), (hi[UP] - t, hi[UP])):
                    beams.append(box(lo[hx] + t, hi[hx] - t, y0, y1, z0, z1))               # rails along x
            p.main.free()
        elif mx < 25:
            blocks.append(p)
        elif mn < 6 and mx > 60:
            # a thin sheet: cut across the longest HORIZONTAL in-plane axis so the boards stand like boards
            c = centre(p.main)
            thick = min(range(3), key=lambda i: d[i])
            cands = [i for i in horizontal_axes() if i != thick] or [i for i in range(3) if i != thick]
            n = max(cands, key=lambda i: d[i])
            p.kind = "board"
            boards.extend(p.cut([(c, axis_vec(n))]))
        else:
            # the X braces (two dims ~100, 8 thick after solidify) and anything else odd: dropped
            log("   dropped part %.0f x %.0f x %.0f cm (%d verts)" % (d.x, d.y, d.z, len(p.main.verts)))
    posts = [q for q in beams if dims(q.main)[UP] >= max(dims(q.main)[hx], dims(q.main)[hz])]
    for b in blocks:
        hosts = posts or beams
        if not hosts:
            continue
        cb = centre(b.main)
        host = min(hosts, key=lambda q: (centre(q.main) - cb).length)
        join(host.main, b.main)
    return beams + boards


def rule_barrel(parts):
    """Stave shell -> 6 wedges; hoops whole (kind ring, rationed by the game); lids and uprights whole. Judged by
    shape: the shell is the one part that is wide AND tall; flat wide parts are a lid (a full disc, many verts)
    or a hoop (a ring, few)."""
    out = []
    hx, hz = horizontal_axes()
    for p in parts:
        d = dims(p.main)
        c = centre(p.main)
        wide = min(d[hx], d[hz]) > 55
        if wide and d[UP] > 55:
            planes = []
            for k in range(3):
                a = math.radians(60 * k)
                n = Vector((0, 0, 0))
                n[hx], n[hz] = math.cos(a), math.sin(a)
                planes.append((c, n))
            p.kind = "stave"
            out.extend(p.cut(planes))
        elif wide and d[UP] < 20:
            p.kind = "ring" if len(p.main.verts) < 200 else "lid"
            out.append(p)
        else:
            out.append(p)
    return out


def rule_bits(parts):
    """Every part on its own, re-centred on its bounds (they spawn anywhere, not in place). Flat wide parts with
    few verts are rings, the rest bent staves."""
    hx, hz = horizontal_axes()
    for p in parts:
        d = dims(p.main)
        # a ring is flat, wide and round (square footprint); a bent stave is flat and wide but long
        square = max(d[hx], d[hz]) < 1.3 * min(d[hx], d[hz])
        p.kind = "ring" if (min(d[hx], d[hz]) > 50 and d[UP] < 20 and square and 40 <= len(p.main.verts) <= 130) else "bit"
        c = centre(p.main)
        for bm in (p.main, p.outline):
            if bm is None:
                continue
            for v in bm.verts:
                v.co -= c
    return parts


def _segments(p, ax, n, planes):
    lo, hi = bounds(p.main)
    d = hi - lo
    c = (lo + hi) * 0.5
    for k in range(1, n):
        co = c.copy()
        co[ax] = lo[ax] + d[ax] * k / n
        planes.append((co, axis_vec(ax)))


def _absorb(into, piece):
    join(into.main, piece.main)
    piece.main.free()


def rule_furniture(parts):
    """Tables, stools, benches, beds, bookcases, coffins, the weapon rack: every part is a piece. Thin sheets (a
    round table top, a bookcase back) become boards across their longest horizontal axis; anything over 1.5 m
    is segmented every ~80 cm and anything over 1 m on a second axis halved; a chunky carcass (a bookcase body)
    is quartered. Bits under 16 cm (table knobs, stool pegs) join the nearest piece, and past MAX_PIECES the
    smallest pieces merge into their neighbours."""
    hx, hz = horizontal_axes()
    out, tiny = [], []
    for p in parts:
        d = dims(p.main)
        mx, mn = max(d), min(d)
        srt = sorted(d)
        if mx < 16:
            tiny.append(p)
            continue
        planes = []
        if mn < 12 and srt[1] > 60:
            thick = min(range(3), key=lambda i: d[i])
            cands = [i for i in (hx, hz) if i != thick] or [i for i in range(3) if i != thick]
            ax = max(cands, key=lambda i: d[i])
            _segments(p, ax, min(4, max(2, round(d[ax] / 40.0))), planes)
            p.kind = "board"
        else:
            axes = sorted(range(3), key=lambda i: -d[i])
            for i, ax in enumerate(axes):
                if d[ax] > 150:
                    n = min(4, max(2, round(d[ax] / 80.0)))
                elif i > 0 and d[ax] > 100:
                    n = 2
                elif mn > 50 and d[ax] > 90:
                    n = 2
                else:
                    continue
                _segments(p, ax, n, planes)
        out.extend(p.cut(planes) if planes else [p])
    for t in tiny:
        if not out:
            out.append(t)
            continue
        ct = centre(t.main)
        _absorb(min(out, key=lambda q: (centre(q.main) - ct).length), t)
    while len(out) > MAX_PIECES:
        small = min(out, key=lambda q: volume(q.main))
        out.remove(small)
        cs = centre(small.main)
        _absorb(min(out, key=lambda q: (centre(q.main) - cs).length), small)
    return out


def rule_chest(parts):
    """The DungeonPack chests and their lids: a chest is a box shell (or a lid shell) and a few metal bands, too small
    for the furniture rule to cut anything, so it broke into 1-3 lumps. The box and the lid are cut on a grid instead:
    every ~30 cm along the width and in two across the depth, so they come apart as wall and floor chunks the way a
    crate's panels do. Bands and trims (thin and long) stay whole; bits under 16 cm join the nearest piece."""
    hx, hz = horizontal_axes()
    out, tiny = [], []
    for p in parts:
        d = dims(p.main)
        mx, mn = max(d), min(d)
        if mx < 16:
            tiny.append(p)
            continue
        wide = max(d[hx], d[hz]) > 50 and min(d[hx], d[hz]) > 30
        if not wide or mn < 4:
            out.append(p)
            continue
        long_ax = hx if d[hx] >= d[hz] else hz
        short_ax = hz if long_ax == hx else hx
        planes = []
        _segments(p, long_ax, min(4, max(2, round(d[long_ax] / 30.0))), planes)
        if d[short_ax] > 45:
            _segments(p, short_ax, 2, planes)
        out.extend(p.cut(planes))
    for t in tiny:
        if not out:
            out.append(t)
            continue
        ct = centre(t.main)
        _absorb(min(out, key=lambda q: (centre(q.main) - ct).length), t)
    while len(out) > MAX_PIECES:
        small = min(out, key=lambda q: volume(q.main))
        out.remove(small)
        cs = centre(small.main)
        _absorb(min(out, key=lambda q: (centre(q.main) - cs).length), small)
    return out


RULES = {"crate": rule_crate, "barrel": rule_barrel, "bits": rule_bits, "furniture": rule_furniture, "chest": rule_chest}


# --------------------------------------------------------------------------- blender in / out

def import_prop(stem, pack="pack"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(PROJ, "Assets", PACKS[pack]["dir"], stem + ".fbx")
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    main = [o for o in meshes if not o.name.endswith("_Outline")]
    outline = [o for o in meshes if o.name.endswith("_Outline")]
    if len(main) == 0:
        raise SystemExit("%s: no mesh object" % stem)
    if len(main) > 1:
        # a multi-mesh prop (a coffin and its lid, a rack and its wheel, an engine and its carriage): joined into one
        # object first, so its sub-meshes become loose parts like any other and keep their place in the prop
        bpy.ops.object.select_all(action="DESELECT")
        for o in main:
            o.select_set(True)
        bpy.context.view_layer.objects.active = main[0]
        bpy.ops.object.join()
        main = [main[0]]
    return main[0], (outline[0] if outline else None)


def to_bm(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    return bm


def write_object(name, bm, template, collection):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    for m in template.data.materials:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    ob.matrix_world = template.matrix_world.copy()
    collection.objects.link(ob)
    return ob


def build_kit(kit, stem, rule, pack="pack"):
    main_ob, out_ob = import_prop(stem, pack)
    src_bm = to_bm(main_ob)
    src_lo, src_hi = bounds(src_bm)
    mains = loose_parts(src_bm)
    # Synty's outline shell is not reused: every piece gets a hull of its own from its finished geometry
    parts = []
    opened = 0
    for m in mains:
        open_ = is_open(m)
        if open_:
            m = solidify(m, SHELL_THICKNESS)
            opened += 1
        parts.append(Part(m, None, open_))
    log("%s: %d loose parts, %d single-sided shells solidified to %.1f cm" % (kit, len(parts), opened, SHELL_THICKNESS))

    pieces = RULES[rule](parts)
    pieces = [p for p in pieces if len(p.main.faces) > 0]
    pieces.sort(key=lambda p: -volume(p.main))
    still_open = sum(1 for p in pieces if is_open(p.main))
    log("%s: %d pieces after cutting%s" % (kit, len(pieces), (", %d STILL OPEN" % still_open) if still_open else ""))

    coll = bpy.context.scene.collection
    objs = []
    manifest = []
    for i, p in enumerate(pieces):
        name = "p%02d" % i
        ob = write_object(name, p.main, main_ob, coll)
        objs.append(ob)
        if out_ob is not None:
            objs.append(write_object(name + "_Outline", make_outline(p.main), out_ob, coll))
        d = dims(p.main)
        c = centre(p.main)
        manifest.append({"name": name, "kind": p.kind, "verts": len(p.main.verts), "size_cm": [d.x, d.y, d.z], "centre_cm": [c.x, c.y, c.z]})
        log("   %s %-5s verts %4d  %5.1f x %5.1f x %5.1f cm" % (name, p.kind, len(p.main.verts), d.x, d.y, d.z))

    # the originals go, only the pieces are exported
    for ob in (main_ob, out_ob):
        if ob is not None:
            bpy.data.objects.remove(ob, do_unlink=True)

    os.makedirs(OUT, exist_ok=True)
    if not pieces:
        # nothing in place (the one-shell crate): the game throws loose planks instead
        return {"kit": kit, "source": stem, "rule": rule, "pack": pack, "fbx": "breakables/%s.fbx" % kit, "import_scale": "0.3937",
                "pieces": [], "verified": True}
    fbx = os.path.join(OUT, kit + ".fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs:
        ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        bake_space_transform=False, mesh_smooth_type="OFF", use_mesh_modifiers=True, bake_anim=False,
        path_mode="STRIP", use_custom_props=False, add_leaf_bones=False)
    log("wrote", fbx)

    # the remaps the pieces will get: the prop's own plus the pack's main and outline pair (write_vmdl adds those)
    p = PACKS[pack]
    remaps = source_remaps(stem, pack) + [(p["matfrom"], p["vmat"] or ""), ("fantasykingdomoutline.vmat", p["outline"])]
    scale, ok = verify(fbx, [m["name"] for m in manifest], (src_lo, src_hi), rule != "bits", remaps)
    return {"kit": kit, "source": stem, "rule": rule, "pack": pack, "fbx": "breakables/%s.fbx" % kit, "import_scale": scale,
            "pieces": manifest, "verified": ok}


# --------------------------------------------------------------------------- verification (binary FBX)

def _read_props(data, pos, count):
    props = []
    for _ in range(count):
        t = data[pos:pos + 1]
        pos += 1
        if t == b"Y": props.append(struct.unpack_from("<h", data, pos)[0]); pos += 2
        elif t == b"C": props.append(bool(data[pos])); pos += 1
        elif t == b"I": props.append(struct.unpack_from("<i", data, pos)[0]); pos += 4
        elif t == b"F": props.append(struct.unpack_from("<f", data, pos)[0]); pos += 4
        elif t == b"D": props.append(struct.unpack_from("<d", data, pos)[0]); pos += 8
        elif t == b"L": props.append(struct.unpack_from("<q", data, pos)[0]); pos += 8
        elif t in (b"S", b"R"):
            n = struct.unpack_from("<I", data, pos)[0]; pos += 4
            props.append(data[pos:pos + n]); pos += n
        elif t in (b"f", b"d", b"l", b"i", b"b"):
            n, enc, clen = struct.unpack_from("<III", data, pos); pos += 12
            raw = data[pos:pos + clen]; pos += clen
            if enc == 1: raw = zlib.decompress(raw)
            fmt = {b"f": "f", b"d": "d", b"l": "q", b"i": "i", b"b": "b"}[t]
            props.append(struct.unpack("<%d%s" % (n, fmt), raw))
        else:
            raise ValueError("unknown prop type %r" % t)
    return props, pos


def _read_node(data, pos, v64):
    if v64:
        end, nprops, plen = struct.unpack_from("<QQQ", data, pos); pos += 24
    else:
        end, nprops, plen = struct.unpack_from("<III", data, pos); pos += 12
    nlen = data[pos]; pos += 1
    name = data[pos:pos + nlen].decode("latin1"); pos += nlen
    if end == 0:
        return None, pos
    props, pos = _read_props(data, pos, nprops)
    children = []
    while pos < end:
        child, pos = _read_node(data, pos, v64)
        if child is None: break
        children.append(child)
    return (name, props, children), end


def _parse(path):
    data = open(path, "rb").read()
    version = struct.unpack_from("<I", data, 23)[0]
    pos, nodes = 27, []
    while pos < len(data):
        node, pos = _read_node(data, pos, version >= 7500)
        if node is None: break
        nodes.append(node)
    return nodes


def _props70(node):
    out = {}
    for c in node[2]:
        if c[0] == "Properties70":
            for p in c[2]:
                if p[0] == "P":
                    out[p[1][0].decode()] = p[1][4:]
    return out


def verify(fbx, names, src_bounds, in_place, remaps=()):
    """Node scale -> import_scale; then the union of the piece bounds (x node scale) must match the source; and
    every material the kit FBX carries must have a remap, or the piece renders on the error material."""
    nodes = _parse(fbx)
    scales, geo, mats = {}, {}, set()
    for objs in (n for n in nodes if n[0] == "Objects"):
        for o in objs[2]:
            nm = o[1][1].decode("latin1").split("\x00")[0]
            if o[0] == "Model":
                s = _props70(o).get("Lcl Scaling")
                scales[nm] = s[0] if s else 1.0
            elif o[0] == "Material":
                mats.add(nm)
            elif o[0] == "Geometry":
                for c in o[2]:
                    if c[0] == "Vertices":
                        v = c[1][0]
                        geo[nm] = (min(v[0::3]), max(v[0::3]), min(v[1::3]), max(v[1::3]), min(v[2::3]), max(v[2::3]))
    mapped = {f.lower() for f, _ in remaps}
    unmapped = sorted(m for m in mats if m.lower() + ".vmat" not in mapped)
    if unmapped:
        log("   MATERIAL UNMAPPED: %s (the piece vmdls remap %s)" % (", ".join(unmapped), ", ".join(sorted(mapped))))
    node_scale = {round(s, 4) for n, s in scales.items() if n in names}
    scale = "0.003937" if node_scale == {100.0} else "0.3937"
    k = 100.0 if node_scale == {100.0} else 1.0
    lo = [min(g[i * 2] for n, g in geo.items() if not n.endswith("_Outline")) * k for i in range(3)]
    hi = [max(g[i * 2 + 1] for n, g in geo.items() if not n.endswith("_Outline")) * k for i in range(3)]
    log("   node scales %s -> import_scale %s; pieces span x[%.1f %.1f] y[%.1f %.1f] z[%.1f %.1f] cm"
        % (sorted(node_scale), scale, lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
    ok = not unmapped
    if in_place:
        slo, shi = src_bounds
        # a part with mixed normals (a bench leg) can thicken a couple of cm past the prop; that is invisible, a
        # scale or axis mistake is tens of cm
        for i in range(3):
            if abs(lo[i] - slo[i]) > 3.0 or abs(hi[i] - shi[i]) > 3.0:
                ok = False
        log("   source span x[%.1f %.1f] y[%.1f %.1f] z[%.1f %.1f] cm -> %s"
            % (slo.x, shi.x, slo.y, shi.y, slo.z, shi.z, "MATCH" if ok else "MISMATCH"))
    return scale, ok


# --------------------------------------------------------------------------- vmdl + C#

VMDL = '''<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
%(remaps)s
\t\t\t\t\t\t]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = "materials/default.vmat"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "PhysicsShapeList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "PhysicsHullFromRender"
\t\t\t\t\t\tparent_bone = ""
\t\t\t\t\t\tsurface_prop = "%(surface)s"
\t\t\t\t\t\tcollision_tags = "solid"
\t\t\t\t\t\tfaceMergeAngle = 20.0
\t\t\t\t\t\tmaxHullVertices = 16
\t\t\t\t\t\thull_mode = "SingleHull"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tfilename = "%(fbx)s"
\t\t\t\t\t\timport_translation = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\timport_rotation = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\timport_scale = %(scale)s
\t\t\t\t\t\talign_origin_x_type = "None"
\t\t\t\t\t\talign_origin_y_type = "None"
\t\t\t\t\t\talign_origin_z_type = "None"
\t\t\t\t\t\tparent_bone = ""
\t\t\t\t\t\timport_filter =
\t\t\t\t\t\t{
\t\t\t\t\t\t\texclude_by_default = true
\t\t\t\t\t\t\texception_list =
\t\t\t\t\t\t\t[
%(nodes)s
\t\t\t\t\t\t\t]
\t\t\t\t\t\t}
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t\tmodel_archetype = ""
\t\tprimary_associated_entity = ""
\t\tanim_graph_name = ""
\t\tbase_model_name = ""
\t}
}'''


_REMAP_RE = re.compile(r'from\s*=\s*"([^"]+)"\s*to\s*=\s*"([^"]+)"', re.S)


def source_remaps(stem, pack):
    """The material remaps of the prop's own vmdl (Assets/<pack dir>/<stem>.vmdl), as (from, to) pairs. The kit
    FBX carries the same material names as the source FBX, so its pieces need the same remaps: a prop on the
    pack's second atlas page (DarkFortressShader01B) or with a chain material rendered on the error material
    when every piece was given the pack's ONE main remap (the boiling oil, the damaged chair). Empty when the
    source vmdl is not there (DungeonPack, whose remap is the wood/stone choice)."""
    path = os.path.join(PROJ, "Assets", PACKS[pack]["dir"], stem.lower() + ".vmdl")
    if not os.path.exists(path):
        path = os.path.join(PROJ, "Assets", PACKS[pack]["dir"], stem + ".vmdl")
    if not os.path.exists(path):
        return []
    text = open(path, encoding="utf-8", errors="replace").read()
    return _REMAP_RE.findall(text)


def write_vmdl(path, fbx, nodes, scale, surface="wood", wood=True, pack="pack", remaps=None):
    # DungeonPack's wooden pieces render on shaders/woodshader.vmat like the props they fall out of
    # (tools/wood_props.py); a pack with one shader (Realms) puts every piece on it. A prop whose own vmdl
    # remaps several materials passes them in (`remaps`), and the piece gets every one of them.
    p = PACKS[pack]
    mat = p["vmat"] or (wood_props.WOOD_VMAT if wood else wood_props.STONE_VMAT)
    pairs = list(remaps) if remaps else []
    if not any(f.lower() == p["matfrom"].lower() for f, _ in pairs):
        pairs.append((p["matfrom"], mat))
    if not any(f.lower() == "fantasykingdomoutline.vmat" for f, _ in pairs):
        pairs.append(("fantasykingdomoutline.vmat", p["outline"]))
    remap_text = "\n".join("\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\tfrom = \"%s\"\n\t\t\t\t\t\t\t\tto = \"%s\"\n\t\t\t\t\t\t\t}," % (f, t) for f, t in pairs)
    body = VMDL % {"fbx": fbx, "scale": scale, "surface": surface, "remaps": remap_text,
                   "nodes": "\n".join("\t\t\t\t\t\t\t\t\"%s\"," % n for n in nodes)}
    with open(path, "w", newline="\n") as f:
        f.write(body)


def piece_vmdls(result):
    out = []
    remaps = source_remaps(result["source"], result.get("pack", "pack"))
    for m in result["pieces"]:
        vm = "%s_%s.vmdl" % (result["kit"], m["name"])
        write_vmdl(os.path.join(OUT, vm), result["fbx"], [m["name"] + "_Outline", m["name"]], result["import_scale"],
                   wood=wood_props.is_wood_kit(result["kit"]), pack=result.get("pack", "pack"), remaps=remaps)
        s = [x * CM_TO_UNIT for x in m["size_cm"]]
        out.append(("breakables/" + vm, s, m.get("kind", "part")))
    return out


def standalone_vmdls():
    out = {}
    for sid, node in STANDALONE.items():
        vm = "%s.vmdl" % sid
        # judged by the MODEL, not the kit id: is_wood_kit() only knows "not a vase, not a book", which would
        # call a brick wood. target_vmat() consults the actual wood list and agrees with it on all the rest.
        wood = wood_props.target_vmat(node) == wood_props.WOOD_VMAT
        write_vmdl(os.path.join(OUT, vm), "dungeonpack/%s.fbx" % node.lower(), [node + "_Outline", node], "0.3937",
                   surface="wood" if wood else "default", wood=wood)
        out[sid] = "breakables/" + vm
    return out


CS_HEAD = '''// <auto-generated> by tools/gen_breakable_kits.py: the debris pieces per breakable model. Do not edit. </auto-generated>
using System.Collections.Generic;

namespace VoidMage.Combat;

public static partial class BreakableKits
{
\t/// <summary>Pieces cut out of the prop's own mesh, authored at the prop origin (spawn at the prop's transform), largest first.</summary>
\tpublic static readonly Dictionary<string, Piece[]> InPlace = new()
\t{
'''


def write_cs(results, standalone):
    lines = [CS_HEAD]
    for r in results:
        if r["rule"] == "bits":
            continue
        lines.append('\t\t["%s/%s.vmdl"] = new Piece[]\n\t\t{\n' % (PACKS[r.get("pack", "pack")]["prefix"], r["source"].lower()))
        for model, s, kind in r["vmdls"]:
            lines.append('\t\t\tnew( "%s", %.1ff, %.1ff, %.1ff, "%s" ),\n' % (model, s[0], s[1], s[2], kind))
        lines.append("\t\t},\n")
    lines.append("\t};\n\n")
    lines.append("\t/// <summary>Loose pieces centred on their own origin, spawned at jittered spots inside the prop.</summary>\n")
    lines.append("\tpublic static readonly Dictionary<string, Piece[]> Loose = new()\n\t{\n")
    for r in results:
        if r["rule"] != "bits":
            continue
        lines.append('\t\t["%s"] = new Piece[]\n\t\t{\n' % r["kit"])
        for model, s, kind in r["vmdls"]:
            lines.append('\t\t\tnew( "%s", %.1ff, %.1ff, %.1ff, "%s" ),\n' % (model, s[0], s[1], s[2], kind))
        lines.append("\t\t},\n")
    for group, kind, ids in (("planks", "plank", ["plank_01", "plank_02"]),
                             ("vase_shards", "shard", ["vase_shard_01", "vase_shard_02", "vase_shard_03", "vase_shard_04"]),
                             ("books", "book", ["book_01", "book_01", "book_01", "bookpile_01"]),
                             ("wall_bricks", "brick", ["brick_01", "brick_02"])):
        lines.append('\t\t["%s"] = new Piece[]\n\t\t{\n' % group)
        for sid in ids:
            if sid in standalone:
                lines.append('\t\t\tnew( "%s", 0f, 0f, 0f, "%s" ),\n' % (standalone[sid], kind))
        lines.append("\t\t},\n")
    lines.append("\t};\n}\n")
    with open(GEN_CS, "w", newline="\n") as f:
        f.write("".join(lines))
    log("wrote", GEN_CS)


# --------------------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = [k for k in KITS if not argv or k in argv]
    if argv and len(wanted) != len(argv):
        raise SystemExit("unknown kit(s): %s (have %s)" % (sorted(set(argv) - set(wanted)), ", ".join(KITS)))
    results = []
    for kit in wanted:
        stem, rule, *rest = KITS[kit]
        r = build_kit(kit, stem, rule, rest[0] if rest else "pack")
        r["vmdls"] = piece_vmdls(r)
        results.append(r)
    standalone = standalone_vmdls()
    # keep earlier kits' data when only a subset was rebuilt
    state = os.path.join(OUT, "kits.json")
    old = json.load(open(state)) if os.path.exists(state) else []
    merged = {r["kit"]: r for r in old}
    for r in results:
        merged[r["kit"]] = r
    results = [merged[k] for k in KITS if k in merged]
    for r in results:
        # older state files carry (model, size) pairs without a kind
        r["vmdls"] = [tuple(v) if len(v) == 3 else (v[0], v[1], "part") for v in r["vmdls"]]
    with open(state, "w", newline="\n") as f:
        json.dump(results, f, indent=1)
    write_cs(results, standalone)
    bad = [r["kit"] for r in results if not r["verified"]]
    log("done: %d kits, %d piece vmdls, %d standalone%s" % (
        len(results), sum(len(r["vmdls"]) for r in results), len(standalone),
        ("; BOUNDS MISMATCH in " + ", ".join(bad)) if bad else ""))


if __name__ == "__main__":
    main()
