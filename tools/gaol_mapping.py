"""The gaol biome's model index: every Dark Fantasy / Dark Fortress piece DungeonKit.Gaol.cs uses, measured.

  python tools/gaol_mapping.py            # check every stem resolves in Assets/DarkFantasy | DarkFortress, print the table
  python tools/gaol_mapping.py --index    # also write tools/Gaol_index.md

Data, not code. Each row is (group, pack, [stems], note); the stems are checked against the toonified FBX
in the Assets folders and measured, so a typo is a hard error here rather than a missing model in the game.
Extents are METRES in the s&box local frame the decorator uses: X = depth (away from a wall / along the
facing), Y = width (along a wall / across the facing), Z = up — fbx (z, x, y), as in tools/DungeonPack_index.md
and tools/DungeonRealms_index.md. The kit's own numbers (DungeonKit.Gaol.cs) are post-scale where a def scales.
"""
import sys, os, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_bounds import parse, find

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PACKS = {"DF": os.path.join(PROJ, "Assets", "DarkFantasy"), "FO": os.path.join(PROJ, "Assets", "DarkFortress")}

# ("group", "DF"|"FO", [stems], "note")
MAP = [
    # ---- fabric: composed from 2.5 m panels (DungeonKit.Composites), the vault from four quadrants
    ("Floor", "FO", ["SM_Bld_Floor_Tile_01", "SM_Bld_Floor_Tile_02", "SM_Bld_Floor_Tile_03", "SM_Bld_Floor_Tile_04", "SM_Bld_Floor_01"],
        "2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates"),
    ("Floor", "DF", ["SM_Prop_Ritual_Circle_01", "SM_Bld_Decoration_01"], "the ritual floors: a circle (6 m, scaled 0.8) or a medallion on plain tiles"),
    ("Ceiling", "DF", ["SM_Bld_Ceiling_01"], "vault quadrant: low corner at the pivot, rises 1.5 m to the far corner; four per cell, scaled 0.947 to 2.5 m"),
    ("Wall", "FO", ["SM_Bld_Wall_01", "SM_Bld_Wall_02", "SM_Bld_Wall_03", "SM_Bld_Wall_04", "SM_Bld_Base_Wall_Half_01", "SM_Bld_Wall_Detail_02"],
        "2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room)"),
    ("Wall", "FO", ["SM_Bld_Wall_L_01", "SM_Bld_Wall_L_03", "SM_Bld_Wall_L_Damaged_04", "SM_Bld_Wall_L_Damaged_05", "SM_Bld_Wall_L_Damaged_06", "SM_Bld_Wall_L_Arch_01"],
        "5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway"),
    ("Wall", "FO", ["SM_Bld_Wall_Prison_01", "SM_Bld_Wall_Door_Prison_01", "SM_Bld_Wall_Window_02", "SM_Bld_Wall_Window_04", "SM_Bld_Wall_Window_05"],
        "iron bars (see-through, mesh collider), the prison gate, slit windows"),
    ("Wall", "DF", ["SM_Bld_Wall_Alcove_01", "SM_Prop_Boarded_Wood_01"], "the arched niche (5 m; Alcove_02 dropped, user), boards over a slit"),
    ("Doorway", "DF", ["SM_Bld_Base_Wall_Door_01", "SM_Bld_Wall_Door_Ruin_02", "SM_Prop_Crypt_Door_Chained_01"], "single door panel (hole 100 x 195), the ragged ruin doorway, the chained fake door"),
    ("Doorway", "FO", ["SM_Bld_Wall_Door_Double_02"], "double door panel (hole 200 x 245)"),
    ("Door", "DF", ["SM_Bld_Base_Door_01", "SM_Bld_Door_02", "SM_Bld_Base_Door_Large_01", "SM_Prop_Boarded_Wood_01", "SM_Prop_Boarded_Wood_02", "SM_Prop_Boarded_Wood_03", "SM_Prop_Boarded_Wood_04"],
        "leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway"),
    ("Door", "FO", ["SM_Bld_Door_01", "SM_Bld_Door_04", "SM_Bld_Door_05", "SM_Bld_Door_06"], "Door_01 is the tall pair for the double hole"),
    ("Stairs", "FO", ["SM_Bld_Stairs_Tiles_01"], "covers -X/+Y and climbs -X (StairMirrorY); scaled (2, 2, 1.667) to 5 x 5 x 2.5; flipped, it is the stair ceiling too"),
    ("Pillar", "DF", ["SM_Bld_Pillar_05"], "the outline pillar, 6 m squashed to 5"),
    ("Pillar", "FO", ["SM_Bld_L_Pillar_04", "SM_Bld_Pillar_02"], "the in-room pillar (6 m squashed) and the corner post (3 m + 2 m stacked)"),
    ("Banner", "DF", ["SM_Prop_Flag_Dark_02", "SM_Prop_Flag_Dark_Damaged_02"], "top-pivot, hang 2.9-3.1 m"),
    ("Banner", "FO", ["SM_Prop_Banner_02", "SM_Prop_Banner_03"], "top-pivot, hang 2.1-2.8 m"),
    # ---- props (the ids are DungeonKit.Gaol.cs's)
    ("Lights", "FO", ["SM_Prop_Torch_02", "SM_Prop_Torch_06", "SM_Prop_Torch_07", "SM_Prop_Candle_Stand_01", "SM_Prop_Brazier_03", "SM_Prop_Brazier_05", "SM_Prop_Chandelier_01", "SM_Prop_Chandelier_01_Damaged_01"], "torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen"),
    ("Lights", "DF", ["SM_Prop_Torch_01", "SM_Prop_Candelabra_01", "SM_Prop_Candelabra_02", "SM_Prop_Candelabra_03", "SM_Prop_Candelabra_Small_01", "SM_Prop_Candle_Rack_01", "SM_Prop_Candle_Wall_01", "SM_Prop_Brazier_01", "SM_Prop_Lamp_Hanging_01", "SM_Prop_Chandelier_01"], "candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big"),
    ("Corpses", "FO", ["SM_Prop_Corpse_01", "SM_Prop_Corpse_04", "SM_Prop_Corpse_10", "SM_Prop_Corpse_Prisoner_01", "SM_Prop_Corpse_Prisoner_03", "SM_Prop_Corpse_Prisoner_04", "SM_Prop_Corpse_Pile_02", "SM_Prop_Corpse_Spike_01", "SM_Prop_Head_Spike_01", "SM_Prop_Limb_01", "SM_Prop_Meat_01", "SM_Prop_Plague_Organs_01"],
        "corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs"),
    ("Corpses", "DF", ["SM_Prop_Gallows_01_Body_01", "SM_Prop_Body_Burnt_01", "SM_Prop_Body_Burnt_04", "SM_Prop_Body_Pile_Burnt_01", "SM_Prop_Body_Skeleton_01", "SM_Prop_Animal_Dead_01"],
        "hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass"),
    ("Bones", "FO", ["SM_Prop_Bone_01", "SM_Prop_Bone_Pile_01", "SM_Prop_Skull_01", "SM_Prop_Skull_Animal_01", "SM_Prop_Skull_Pile_01", "SM_Env_Bones_01", "SM_Env_Bones_Skull_01", "SM_Env_Bones_Hand_01", "SM_Env_Bones_Ribs_01"],
        "bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9)"),
    ("Cages/torture", "FO", ["SM_Prop_Cage_01", "SM_Prop_Cage_02", "SM_Prop_Cage_03", "SM_Prop_Cage_Hanging_01", "SM_Prop_Plinth_01", "SM_Prop_Torture_Device_01", "SM_Prop_Wheel_01", "SM_Prop_Stretching_Rack_01", "SM_Prop_Shackles_Wall_01", "SM_Prop_Wrist_Bonds_01", "SM_Prop_Key_Ring_01"],
        "cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring"),
    ("Cages/torture", "DF", ["SM_Prop_Cage_Large_01", "SM_Prop_Cage_Small_01", "SM_Prop_Stock_01", "SM_Prop_Post_01", "SM_Prop_Gallows_01", "SM_Prop_Gibbet_01", "SM_Prop_Gibbet_01_Damaged_01", "SM_Prop_Pyre_02", "SM_Prop_Pyre_Stake_01", "SM_Prop_Pyre_Stake_02_Burnt_02", "SM_Prop_Pyre_Stake_02"],
        "cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake"),
    ("Chains", "FO", ["SM_Prop_Chain_01", "SM_Prop_Chain_02", "SM_Prop_Chain_03", "SM_Prop_Chain_04", "SM_Prop_Chain_06", "SM_Prop_Chain_08", "SM_Prop_Chain_Pile_01", "SM_Prop_Skull_Hanging_01", "SM_Prop_Table_Suspended_01"],
        "chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended"),
    ("Chains", "DF", ["SM_Prop_Chain_01", "SM_Prop_Chain_02", "SM_Prop_Gibbet_01_Cage_01", "SM_Prop_Rope_Hanging_01", "SM_Prop_Noose_01", "SM_Prop_Noose_Bundle_01", "SM_Prop_Bell_Large_01", "SM_Prop_Chain_Connector_01"], "the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring"),
    ("Walls", "FO", ["SM_Prop_Head_Display_01", "SM_Prop_Plaque_01", "SM_Prop_Rune_01", "SM_Prop_Greeble_04", "SM_Prop_Greeble_01", "SM_Env_Spiderweb_06", "SM_Prop_Banner_01"],
        "wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white)"),
    ("Walls", "DF", ["SM_Prop_Head_Mounted_01"], "the mounted head"),
    ("Lab", "FO", ["SM_Prop_Specimen_Tank_01", "SM_Prop_Specimen_Tank_02", "SM_Prop_Specimen_Jar_01", "SM_Prop_Alchemy_Tool_04", "SM_Prop_Surgical_Tool_01", "SM_Prop_Research_Papers_01", "SM_Prop_Altar_01"],
        "specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab"),
    ("Chapel", "DF", ["SM_Prop_Altar_Table_01", "SM_Prop_Tabernacle_01", "SM_Prop_Organ_01", "SM_Prop_Holy_Water_01", "SM_Prop_Lecturn_01", "SM_Prop_Pew_01", "SM_Prop_Pew_Large_01", "SM_Prop_Pew_Large_01_Damaged_02", "SM_Prop_Demon_01", "SM_Prop_Demon_Ward_01", "SM_Prop_Gargoyle_Statue_01", "SM_Prop_Gargoyle_01", "SM_Prop_Statue_01", "SM_Prop_Incense_01", "SM_Prop_Candle_Blob_01"],
        "altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax"),
    ("Crypt", "DF", ["SM_Prop_Tomb_01", "SM_Prop_Tomb_02", "SM_Prop_Tomb_Stone_01", "SM_Prop_Tomb_Stone_Basic_01", "SM_Prop_Tomb_Stone_05", "SM_Prop_Crypt_01"], "tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75)"),
    ("Crypt", "FO", ["SM_Prop_Coffin_01", "SM_Prop_Coffin_01_Damaged_01", "SM_Prop_Statue_02", "SM_Prop_Statue_Base_01", "SM_Prop_Tomb_Blanket_01", "SM_Prop_Plushie_01", "SM_Prop_Urn_01", "SM_Prop_Urn_04"], "coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable)"),
    ("Stores", "DF", ["SM_Prop_Sack_01", "SM_Prop_Keg_01", "SM_Prop_Meat_Bucket_01", "SM_Prop_Counter_Bar_01", "SM_Prop_Wagon_01_Damaged_01", "SM_Prop_Well_01", "SM_Prop_Barrel_01", "SM_Prop_Crate_01", "SM_Prop_Crate_02", "SM_Prop_Rack_Weapon_01", "SM_Prop_Fire_Pit_01", "SM_Prop_Bed_01", "SM_Prop_Bookshelf_Double_01", "SM_Prop_Bookshelf_01"],
        "sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred)"),
    ("Stores", "FO", ["SM_Prop_Crate_01", "SM_Prop_Crate_Destroyed_01", "SM_Prop_Trough_01", "SM_Prop_Cupboard_01", "SM_Prop_Shelf_02", "SM_Prop_Shelf_01", "SM_Prop_Wheel_Barrow_01", "SM_Prop_Desk_01", "SM_Prop_Table_01", "SM_Prop_Table_02", "SM_Prop_Table_03", "SM_Prop_Table_Small_01", "SM_Prop_Chair_01", "SM_Prop_Bench_01", "SM_Prop_Bed_01", "SM_Prop_Throne_01", "SM_Prop_Bookshelf_01", "SM_Prop_Cauldron_02", "SM_Prop_Fireplace_02", "SM_Prop_Tent_02"],
        "crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85)"),
    ("Siege", "FO", ["SM_Prop_Ballista_01", "SM_Veh_Cannon_01", "SM_Veh_Arrow_Launcher_01", "SM_Prop_Slingshot_01", "SM_Prop_Boiling_Oil_01", "SM_Prop_Barricade_01", "SM_Prop_Barricade_02", "SM_Prop_Arrow_Basket_01", "SM_Prop_Cannonball_01", "SM_Prop_Dial_01", "SM_Prop_Pipe_01", "SM_Prop_Sewer_Grate_01", "SM_Prop_Sewer_Grate_02", "SM_Prop_Ladder_01", "SM_Prop_Fountain_01", "SM_Prop_Crystal_01", "SM_Prop_Relic_01", "SM_Prop_Portal_01"],
        "ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal"),
]


def dims(pack, stem):
    p = os.path.join(PACKS[pack], stem + ".fbx")
    if not os.path.exists(p):
        return None
    _, tree = parse(p)
    lo = [1e9] * 3; hi = [-1e9] * 3
    found = False
    for objs in find(tree, 'Objects'):
        for o in objs[2]:
            if o[0] != 'Geometry':
                continue
            nm = o[1][1].decode('latin1').split('\x00')[0]
            if nm.endswith('_Outline'):
                continue
            v = None
            for c in o[2]:
                if c[0] == 'Vertices':
                    v = c[1][0]
            if not v:
                continue
            found = True
            xs, ys, zs = v[0::3], v[1::3], v[2::3]
            # s&box local: X = fbx z, Y = fbx x, Z = fbx y (up); a multi-mesh file is measured as a whole
            for i, arr in enumerate((zs, xs, ys)):
                lo[i] = min(lo[i], min(arr) / 100); hi[i] = max(hi[i], max(arr) / 100)
    return (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]) if found else None


def main():
    missing, rows = [], []
    for group, pack, stems, note in MAP:
        for s in stems:
            d = dims(pack, s)
            if d is None:
                missing.append("%s/%s" % (pack, s))
                continue
            rows.append((group, pack, s, d, note))
    for group, pack, s, d, note in rows:
        print("%-14s %s %-36s X %6.2f..%5.2f  Y %6.2f..%5.2f  Z %6.2f..%5.2f" % (group, pack, s, *d))
    if missing:
        print("MISSING:", missing)
        sys.exit(1)
    print("%d models, 0 missing" % len(rows))
    if "--index" in sys.argv:
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Gaol_index.md")
        by = collections.OrderedDict()
        for r in rows:
            by.setdefault(r[0], []).append(r)
        with open(out, "w", newline="\n", encoding="utf-8") as f:
            f.write("# Gaol model index\n\nThe Dark Fantasy (DF) and Dark Fortress (FO) pieces the gaol biome uses (DungeonKit.Gaol.cs).\n"
                    "Extents are metres in the s&box local frame: X = depth, Y = width, Z = up; multi-mesh files measured whole.\n"
                    "Runtime paths: `darkfantasy/<name lowercased>.vmdl` and `darkfortress/<name lowercased>.vmdl`.\n\n")
            for group, rs in by.items():
                f.write("## %s\n\n| pack | model | X (m) | Y (m) | Z (m) | note |\n|---|---|---|---|---|---|\n" % group)
                for _, pack, s, d, note in rs:
                    f.write("| %s | `%s` | %.2f..%.2f | %.2f..%.2f | %.2f..%.2f | %s |\n" % (pack, s, *d, note))
                f.write("\n")
        print("wrote", out)


if __name__ == "__main__":
    main()
