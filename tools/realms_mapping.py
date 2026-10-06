"""The proposed DungeonPack -> DungeonRealms model mapping for the forge-hold biome.

  python tools/realms_mapping.py            # check every name resolves, print the table
  python tools/realms_mapping.py --index    # also write tools/DungeonRealms_index.md

Data, not code. Each row is (role, dungeonpack stem or "-", realms stem(s), note). The realms
column is checked against Assets/DungeonRealms and measured, so a typo is a hard error rather
than something that surfaces as a missing model at generation time three weeks later.

Extents printed are METRES in the s&box local frame the decorator uses: X = depth (away from a
wall / along the facing), Y = width (along a wall / across the facing), Z = up. That is
fbx (z, x, y), the same convention as tools/DungeonPack_index.md.
"""
import sys, os, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_bounds import parse, find

PROJ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PACK = os.path.join(PROJ, "Assets", "DungeonRealms")

# ---------------------------------------------------------------- the mapping
# ("group", dungeonpack stem, [realms stems], "note")
MAP = [
    # ---- structural: the dungeon's own fabric --------------------------------------------
    ("Floor", "sm_env_tiles_01..05,_010 (6)", ["SM_Env_Dwarf_Floor_01", "SM_Env_Dwarf_Floor_05",
        "SM_Env_Dwarf_Floor_08", "SM_Env_Dwarf_Floor_12", "SM_Env_Dwarf_Floor_17",
        "SM_Env_Dwarf_Floor_20"], "29 to choose from; walking surface sits 2 cm over the pivot, not 18"),
    ("Floor", "sm_env_tiles_03 (plain, under traps)", ["SM_Env_Dwarf_Floor_01"], "flattest variant"),
    ("Floor", "sm_env_tiles_rune_01..05", ["SM_Env_Dwarf_Floor_10", "SM_Env_Dwarf_Floor_14"],
        "the SM_Rune_Symbol_* pieces are 7 cm upright plaques, not tiles; two unused inlaid floors stand in (walkthrough pick)"),
    ("Ceiling", "sm_env_ceiling_stone_flat_01..05", ["SM_Env_Dwarf_Floor_27", "SM_Env_Dwarf_Floor_03",
        "SM_Env_Dwarf_Floor_13", "SM_Env_Dwarf_Floor_15", "SM_Env_Dwarf_Floor_23"],
        "NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one"),
    ("Wall", "sm_env_wall_01_doublesided", ["SM_Env_Dwarf_Wall_01"], "5x5 m, 56 cm thick, runs +Y like ours"),
    ("Wall", "sm_env_wall_quarter_01", ["SM_Env_Dwarf_Wall_Short_01"], "5 m run x 2.5 m tall, same profile as Wall_01: one piece per lone slab (Wall_Half_* are 2.5 m run but 5 m TALL)"),
    ("Wall", "sm_env_wall_01 (plain single-sided)", ["SM_Env_Dwarf_Wall_03"], "thinner back face"),
    ("Wall", "sm_env_wall_05 (secret tell)", ["SM_Env_Dwarf_Wall_DoorFrame_Slider_01"],
        "a sealed slider door reads as 'this opens' better than an inset arch"),
    ("Wall", "sm_env_wall_07 (broken secret)", ["SM_Env_Dwarf_Wall_Archway_01"], "the opened tell"),
    ("Wall", "sm_env_wall_broken_edge_01/02 (breach)", ["SM_Env_Dwarf_Wall_Broken_01",
        "SM_Env_Dwarf_Wall_Broken_02"], "6 broken walls; breaches are mossy-only today"),
    ("Pillar", "sm_env_pillar_square_06 (outline)", ["SM_Env_Dwarf_Pillar_03"], "1.24 m square vs our 0.60 - much chunkier"),
    ("Pillar", "sm_env_wall_pillar_large_round_01", ["SM_Env_Dwarf_Pillar_01"], "free-standing, + SM_Env_Dwarf_Pillar_Base_01 skirt"),
    ("Corner", "sm_env_wall_end_coner_inner_01", ["SM_Env_Dwarf_Pillar_07"], "USER: pillars 07/08, not the trims (the trims are horizontal 5 m bands). Centred +-0.56 m: one post per vertex"),
    ("Corner", "sm_env_wall_end_coner_outer_01", ["SM_Env_Dwarf_Pillar_08"], "ditto, convex"),
    ("Stairs", "sm_env_stairs_02", ["SM_Env_Dwarf_Stairs_01"], "rises EXACTLY 2.50 m = one level (ours is 2.96)"),
    ("Stairs", "sm_env_stairs_wall_01", ["SM_Env_Dwarf_Stairs_Wall_01"], "long axis is local X here, ours is Y - needs a yaw fix"),
    ("Stairs", "sm_env_ceiling_stone_stairs_01", ["SM_Env_Dwarf_Stairs_01"], "no sloped ceiling: the stair mesh itself, flipped, is a stepped slope rising 2.5 over 5 m"),
    # ---- doorways -------------------------------------------------------------------------
    ("Doorway", "sm_env_wall_doorframe_01 + door_frame_01", ["SM_Env_Dwarf_Wall_DoorFrame_Single_01"],
        "frame and hole are one piece here - no separate inner frame"),
    ("Doorway", "sm_env_wall_doorframe_round_01", ["SM_Env_Dwarf_Wall_DoorFrame_Single_02"], "second single"),
    ("Doorway", "sm_env_wall_doorframe_double_01", ["SM_Env_Dwarf_Wall_DoorFrame_Double_01"], "double"),
    ("Doorway", "sm_env_wall_doorframe_double_02", ["SM_Env_Dwarf_Wall_DoorFrame_Double_02"], "second double"),
    ("Doorway", "sm_env_wall_doorframe_double_round_01", ["SM_Env_Dwarf_Wall_Archway_02"], "open arch, no leaf"),
    ("Door", "sm_env_door_01..05, door_bars_01", ["SM_Env_Dwarf_Wall_DoorFrame_Single_01_Door",
        "SM_Env_Dwarf_Wall_DoorFrame_Single_02_Door"], "1.82 m leaf, hinge at +Y like ours"),
    ("Door", "sm_env_doordouble_flat_01/02", ["SM_Env_Dwarf_Wall_DoorFrame_Double_01_Door_01",
        "SM_Env_Dwarf_Wall_DoorFrame_Double_02_Door_02"], "1.37 m leaves"),
    ("Door", "sm_env_doordouble_round_01/02", ["SM_Env_Dwarf_Gate_01_Door_L", "SM_Env_Dwarf_Gate_01_Door_R"],
        "the big gate pair, for halls"),
    # ---- wall swaps -----------------------------------------------------------------------
    ("Swap", "alcove / alcove_2", ["SM_Env_Dwarf_Wall_Alcove_Small_01", "SM_Env_Dwarf_Wall_Alcove_Small_02"],
        "reaches 1.6 m behind the plane, ours 1.5"),
    ("Swap", "alcove_round", ["SM_Env_Dwarf_Wall_Alcove_Angular_01"], "angular, not round - dwarven"),
    ("Swap", "wall_deep", ["SM_Env_Dwarf_Wall_Alcove_Angular_02"], "shallow recessed bay"),
    ("Swap", "wall_alt / wall_3 / wall_4", ["SM_Env_Dwarf_Wall_02", "SM_Env_Dwarf_Wall_04",
        "SM_Env_Dwarf_Wall_05", "SM_Env_Dwarf_Wall_06"], "4 plain variants, one more than we had"),
    ("Swap", "window / window_2", ["SM_Env_Dwarf_Wall_Window_01", "SM_Env_Dwarf_Wall_Window_03"],
        "4 windows; _02/_04 are the bar inserts"),
    ("Swap", "sealed_door", ["SM_Env_Dwarf_Wall_Block_Cube_Half_01"], "a rock plug in the opening"),
    ("Swap", "-", ["SM_Env_Dwarf_Wall_Fireplace_01"], "DROPPED as a swap: 1.17 m into the room and -0.28..5.29 along it, breaks the cell"),
    ("Swap", "-", ["SM_Env_Dwarf_Grate_Wall_01"], "DROPPED as a swap: a 2.8 x 2.1 m mid-pivot insert, not a wall piece"),
    # ---- traps ----------------------------------------------------------------------------
    ("Trap", "sm_prop_trap_spears_grate_01/_spears_01", ["SM_Env_Dwarf_Grate_Floor_01",
        "SM_Prop_Dwarf_Trap_Gap_01"], "grate plate + the pit below it"),
    ("Trap", "sm_prop_trap_swing_blade_01 + housing", ["SM_Prop_Dwarf_Trap_Saw_Spin_01",
        "SM_Prop_Dwarf_Trap_Saw_01"], "a spinning sawblade instead of a pendulum"),
    ("Trap", "sm_prop_trap_flamehead_01", ["SM_Prop_Dwarf_Trap_Head_01", "SM_Prop_Dwarf_Trap_Head_Mouth_01"],
        "head + its own mouth piece"),
    ("Trap", "sm_prop_trap_acidhead_01", ["SM_Prop_Dwarf_Trap_Head_02", "SM_Prop_Dwarf_Trap_Head_Mouth_02"], "the second head"),
    ("Trap", "sm_prop_trap_saw_01 + rail", ["SM_Prop_Dwarf_Trap_Saw_01", "SM_Prop_Dwarf_Cart_Rail_01"],
        "the cart rail IS the saw rail - forge-hold made literal"),
    ("Trap", "sm_prop_trap_wallarrow_hole_01/_02", ["SM_Env_Dwarf_Wall_Dressing_01", "SM_Env_Dwarf_Wall_Dressing_02"],
        "wall dressings stand in for arrow holes"),
    ("Trap", "sm_prop_trap_wallslab_01", ["SM_Env_Dwarf_Wall_Block_Cube_Half_02"], "the crushing slab"),
    # ---- furniture ------------------------------------------------------------------------
    ("Furniture", "bookcase", ["SM_Prop_Dwarf_Shelf_01", "SM_Prop_Dwarf_Shelf_02"], "Records room"),
    ("Furniture", "weapon_rack", ["SM_Prop_Dwarf_Weapon_Rack_03"], "USER: only rack 03"),
    ("Furniture", "bed / bed_big", ["SM_Prop_Dwarf_Bed_01", "SM_Prop_Dwarf_Bed_02"], "USER: NOT USED - the DungeonPack beds stay (listed for the record)"),
    ("Furniture", "bench", ["SM_Prop_Dwarf_Chair_Bench_01"], "+ _Broken_01 for decay"),
    ("Furniture", "stool", ["SM_Prop_Dwarf_Chair_Stool_01"], "+ _Broken_01"),
    ("Furniture", "table_long / table_round / table_low", ["SM_Prop_Dwarf_Table_01",
        "SM_Prop_Dwarf_Table_02", "SM_Prop_Dwarf_Table_03", "SM_Prop_Dwarf_Table_Small_01",
        "SM_Prop_Dwarf_Table_Small_02"], "5 tables"),
    ("Furniture", "table_broken / stone_table_broken", ["SM_Prop_Dwarf_Table_Broken_01",
        "SM_Prop_Dwarf_Table_Broken_02", "SM_Prop_Dwarf_Table_Small_Broken_01"], ""),
    ("Furniture", "throne", ["SM_Prop_Dwarf_Throne_01", "SM_Prop_Dwarf_Throne_02"], ""),
    ("Furniture", "fireplace_wall", ["SM_Env_Dwarf_Wall_Fireplace_01"], "+ _Extension_01"),
    ("Furniture", "forge", ["SM_Env_Dwarf_Forge_01", "SM_Prop_Dwarf_Fire_Pit_01"], "the Forge room's centrepiece"),
    ("Furniture", "forge_pipe", ["SM_Env_Dwarf_Forge_Beam_01"], "ceiling beam over the forge"),
    ("Furniture", "fountain", ["SM_Env_Dwarf_Lava_Duct_01"], "lava runs where water did"),
    ("Furniture", "rug / rug_flat", ["SM_Prop_Camp_Rug_01", "SM_Prop_Camp_Rug_02", "SM_Prop_Camp_Rug_03"], "12 rugs in the camp set"),
    # ---- containers -----------------------------------------------------------------------
    ("Container", "crate / crate_metal / crate_ornate", ["SM_Prop_Crate_01", "SM_Prop_Crate_Stack_01"],
        "+ SM_Prop_Crate_Broken_01 as decay"),
    ("Container", "barrel", ["SM_Prop_Barrel_01", "SM_Prop_Barrel_02", "SM_Prop_Barrel_03"], "breakable"),
    ("Container", "barrel_broken", ["SM_Prop_Barrel_Broken_02", "SM_Prop_Barrel_Broken_03"], ""),
    ("Container", "vase", ["SM_Prop_Camp_Pot_02", "SM_Prop_Camp_Pot_06"],
        "USER: keep the vase def, add pots 2 and 6 to its rotation - they need debris kits"),
    ("Container", "chest / chest_wood", ["SM_Prop_Dwarf_TreasureChest_01", "SM_Prop_Camp_Chest_01"],
        "+ SM_Prop_Dwarf_TreasureChest_Lid_01 / SM_Prop_Camp_Chest_Lid_01 as the attach"),
    ("Container", "vat", ["SM_Env_Dwarf_Forge_Smelter_01"], "a smelter is the Storage vat"),
    ("Container", "vessel", ["SM_Prop_Dwarf_Smelting_Pot_01", "SM_Prop_Dwarf_Smelting_Pot_02"], ""),
    ("Container", "jar", ["SM_Prop_Camp_Pot_03", "SM_Prop_Camp_Pot_04"], ""),
    # ---- crypt ----------------------------------------------------------------------------
    ("Crypt", "coffin / coffin_open", ["SM_Prop_Sarcophagus_01"], "+ SM_Prop_Sarcophagus_01_Lid as the attach"),
    ("Crypt", "bone_pile / bone_pile_small", ["SM_Env_Bone_Rib_01", "SM_Env_Bone_Rib_04", "SM_Env_Hell_Bone_Skull_01"], ""),
    ("Crypt", "skeleton", ["SM_Prop_DeadBody_Dwarf_01", "SM_Prop_DeadBody_Nomad_01"], "8 dead bodies"),
    ("Crypt", "chain / chain_short", ["SM_Prop_Dwarf_Chain_01", "SM_Prop_Dwarf_Chain_02"], "+ _Clasp_01"),
    ("Crypt", "skeleton_cage / skeleton_hung", ["SM_Prop_Hanging_Platform_01", "SM_Prop_Camp_HangingBag_01"],
        "no gibbet in the pack; a hanging platform and slung bags are the nearest"),
    # ---- shrine / arcane ------------------------------------------------------------------
    ("Shrine", "altar", ["SM_Env_Shrine_01"], "the only shrine piece"),
    ("Shrine", "obelisk", ["SM_Env_Dwarf_Obelisk_01", "SM_Env_Dwarf_Obelisk_02"], "+ 4 broken variants"),
    ("Shrine", "statue", ["SM_Env_Statue_01", "SM_Env_Statue_02", "SM_Env_Statue_05"], "20 statues"),
    ("Shrine", "rune_pillar", ["SM_Env_Dwarf_Obelisk_01_Broken_01", "SM_Env_Dwarf_Obelisk_02_Broken_01"], ""),
    ("Shrine", "rune_block", ["SM_Rune_Symbol_02", "SM_Rune_Symbol_15", "SM_Rune_Symbol_28"], "79 to pick from"),
    ("Shrine", "pentagram", ["SM_Rune_Synty_01"], "the big floor sigil"),
    # ---- lighting -------------------------------------------------------------------------
    ("Light", "torch", ["SM_Prop_Dwarf_Torch_01", "SM_Prop_Dwarf_Torch_02", "SM_Prop_Dwarf_Torch_03"],
        "7 torches - the primary light everywhere"),
    ("Light", "brazier", ["SM_Prop_Dwarf_Brazier_01"], ""),
    ("Light", "bonfire", ["SM_Prop_Dwarf_Fire_Pit_01"], ""),
    ("Light", "chandelier", ["SM_Prop_Dwarf_Chandelier_01", "SM_Prop_Dwarf_Chandelier_03",
        "SM_Prop_Dwarf_Chandelier_05"], "6 chandeliers"),
    ("Light", "chandelier_broken", ["SM_Prop_Dwarf_Chandelier_06"], ""),
    ("Light", "candle_stand / candles / candle", ["SM_Prop_Dwarf_Torch_06", "SM_Prop_Dwarf_Torch_07"],
        "NO candles in the pack; small torches stand in"),
    ("Light", "stone_lantern", ["SM_Prop_Dwarf_Torch_04", "SM_Prop_Dwarf_Torch_05"], ""),
    ("Light", "banner", ["SM_Prop_Banner_06", "SM_Prop_Banner_Damaged_03", "SM_Prop_Banner_Damaged_04"],
        "the only ones under 4.2 m (01/03 hang 10 m, 04 5.5 m: through the floor); pivot at the top like ours"),
    # ---- clutter --------------------------------------------------------------------------
    ("Clutter", "rubble", ["SM_Env_Rubble_Rocks_01", "SM_Env_Rubble_Rocks_02", "SM_Env_Rubble_Rocks_03"], ""),
    ("Clutter", "pebbles", ["SM_Prop_Rock_Small_01", "SM_Prop_Rock_Small_02", "SM_Prop_Rock_Small_03"], "the default litter"),
    ("Clutter", "bricks / brick", ["SM_Env_Rubble_Slab_01", "SM_Env_Rubble_Tile_01", "SM_Env_Rubble_Tile_02"], ""),
    ("Clutter", "plank", ["SM_Env_Tiles_Loose_01", "SM_Env_Tiles_Loose_03"], ""),
    ("Clutter", "pillar_pile", ["SM_Env_Rubble_Pillar_01", "SM_Env_Rubble_Pillar_02"], ""),
    ("Clutter", "shard / vase_broken", ["SM_Env_Tiles_Loose_05", "SM_Env_Tiles_Loose_06"], ""),
    ("Clutter", "coins", ["SM_Prop_Gold_Coins_01", "SM_Prop_Gold_Coins_03", "SM_Prop_Gold_Coins_05"], "15 coin piles"),
    ("Clutter", "roots / mushrooms", ["SM_Env_SpiderWeb_01", "SM_Env_SpiderWeb_03"], "webs are this biome's decay"),
    # ---- NEW: what makes the forge-hold its own place --------------------------------------
    ("Forge-hold", "-", ["SM_Prop_Dwarf_Cart_01", "SM_Prop_Dwarf_Cart_02", "SM_Prop_Dwarf_Cart_03"], "ore carts"),
    ("Forge-hold", "-", ["SM_Prop_Dwarf_Cart_Rail_01"], "rails to run them on"),
    ("Forge-hold", "-", ["SM_Prop_Dwarf_Cog_Large_01", "SM_Prop_Dwarf_Cog_Large_03",
        "SM_Prop_Dwarf_Cog_Pile_01", "SM_Prop_Dwarf_Cog_Small_01"], "machinery clutter"),
    ("Forge-hold", "-", ["SM_Prop_Dwarf_Crane_01", "SM_Prop_Dwarf_Capstar_01"], "big room centrepieces"),
    ("Forge-hold", "-", ["SM_Env_Dwarf_Forge_Smelter_02", "SM_Env_Dwarf_Forge_Gutter_Straight_01"], "the Smeltery"),
    ("Forge-hold", "-", ["SM_Env_Dwarf_Lava_Duct_01", "SM_Env_Dwarf_Lava_Duct_Pillar_01",
        "SM_Env_Dwarf_Lava_Wheel_01"], "lava plumbing - the biome's signature"),
    ("Forge-hold", "-", ["SM_Prop_Anvil_01", "SM_Prop_Dwarf_Lever_01", "SM_Prop_Dwarf_Totem_01"], ""),
    ("Forge-hold", "-", ["SM_Env_Crystals_Cluster_Large_01", "SM_Prop_Crystal_01", "SM_Prop_Gem_01"],
        "crystal seams on their own shader"),
    ("Forge-hold", "-", ["SM_Env_Dwarf_Balustrade_01", "SM_Env_Dwarf_Bridge_01", "SM_Env_Dwarf_Beam_01"],
        "galleries and spans over the lava"),
]


def dims(stem):
    p = os.path.join(PACK, stem + ".fbx")
    if not os.path.exists(p):
        return None
    _, tree = parse(p)
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
            xs, ys, zs = v[0::3], v[1::3], v[2::3]
            # s&box local: X = fbx z, Y = fbx x, Z = fbx y (up)
            return (min(zs) / 100, max(zs) / 100, min(xs) / 100, max(xs) / 100,
                    min(ys) / 100, max(ys) / 100)
    return None


def main():
    missing = []
    rows = []
    for group, old, news, note in MAP:
        for n in news:
            d = dims(n)
            if d is None:
                missing.append(n)
            rows.append((group, old, n, d, note))

    by_group = collections.Counter(r[0] for r in rows)
    print("mapping rows: %d over %d groups" % (len(rows), len(by_group)))
    for g, c in by_group.most_common():
        print("   %-12s %d" % (g, c))
    print()
    if missing:
        print("MISSING FROM Assets/DungeonRealms (%d):" % len(missing))
        for m in missing:
            print("   ", m)
    else:
        print("every proposed model resolves.")

    if "--index" in sys.argv:
        lines = ["# DungeonRealms model index",
                 "",
                 "Proposed replacements for the stone dungeon, for the forge-hold biome (floors 11+).",
                 "Extents are metres in the s&box local frame: X = depth, Y = width, Z = up.",
                 "Runtime path: `dungeonrealms/<name lowercased>.vmdl`.",
                 ""]
        cur = None
        for group, old, new, d, note in rows:
            if group != cur:
                cur = group
                lines += ["", "## %s" % group, "",
                          "| replaces | Realms model | X (m) | Y (m) | Z (m) | note |",
                          "|---|---|---|---|---|---|"]
            e = ("%.2f..%.2f | %.2f..%.2f | %.2f..%.2f" % d) if d else "? | ? | ?"
            lines.append("| `%s` | `%s` | %s | %s |" % (old, new, e, note))
        out = os.path.join(PROJ, "tools", "DungeonRealms_index.md")
        open(out, "w", newline="\n", encoding="utf-8").write("\n".join(lines) + "\n")
        print("\nwrote", out)
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
