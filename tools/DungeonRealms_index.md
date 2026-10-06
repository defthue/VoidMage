# DungeonRealms model index

Proposed replacements for the stone dungeon, for the forge-hold biome (floors 11+).
Extents are metres in the s&box local frame: X = depth, Y = width, Z = up.
Runtime path: `dungeonrealms/<name lowercased>.vmdl`.


## Floor

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_01` | -0.00..5.00 | -0.00..5.00 | -0.23..0.02 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_05` | -0.00..5.00 | -0.00..5.00 | -0.23..0.03 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_08` | -0.00..5.01 | -0.00..5.00 | -0.23..0.04 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_12` | -0.00..5.00 | -0.00..5.00 | -0.23..0.04 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_17` | 0.00..5.00 | -0.00..5.00 | -0.23..0.02 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_01..05,_010 (6)` | `SM_Env_Dwarf_Floor_20` | -0.00..5.00 | -0.00..5.00 | -0.23..0.03 | 29 to choose from; walking surface sits 2 cm over the pivot, not 18 |
| `sm_env_tiles_03 (plain, under traps)` | `SM_Env_Dwarf_Floor_01` | -0.00..5.00 | -0.00..5.00 | -0.23..0.02 | flattest variant |
| `sm_env_tiles_rune_01..05` | `SM_Env_Dwarf_Floor_10` | -0.00..5.00 | 0.00..5.00 | -0.23..0.04 | the SM_Rune_Symbol_* pieces are 7 cm upright plaques, not tiles; two unused inlaid floors stand in (walkthrough pick) |
| `sm_env_tiles_rune_01..05` | `SM_Env_Dwarf_Floor_14` | 0.00..5.00 | -0.00..5.00 | -0.23..0.00 | the SM_Rune_Symbol_* pieces are 7 cm upright plaques, not tiles; two unused inlaid floors stand in (walkthrough pick) |

## Ceiling

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_ceiling_stone_flat_01..05` | `SM_Env_Dwarf_Floor_27` | -0.00..5.00 | -0.00..5.00 | -0.23..0.00 | NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one |
| `sm_env_ceiling_stone_flat_01..05` | `SM_Env_Dwarf_Floor_03` | 0.00..5.00 | -0.00..5.00 | -0.23..0.08 | NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one |
| `sm_env_ceiling_stone_flat_01..05` | `SM_Env_Dwarf_Floor_13` | -0.00..5.00 | -0.00..5.00 | -0.23..0.02 | NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one |
| `sm_env_ceiling_stone_flat_01..05` | `SM_Env_Dwarf_Floor_15` | -0.00..5.00 | 0.00..5.00 | -0.23..0.03 | NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one |
| `sm_env_ceiling_stone_flat_01..05` | `SM_Env_Dwarf_Floor_23` | 0.00..5.00 | -0.00..5.00 | -0.23..0.01 | NO ceiling pieces in the pack: floor slabs FLIPPED so the chosen face points down (user: 03 and 13); _27 is the flat plain one |

## Wall

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_wall_01_doublesided` | `SM_Env_Dwarf_Wall_01` | -0.14..0.42 | 0.00..5.00 | 0.00..5.00 | 5x5 m, 56 cm thick, runs +Y like ours |
| `sm_env_wall_quarter_01` | `SM_Env_Dwarf_Wall_Short_01` | -0.14..0.42 | 0.00..5.00 | 0.00..2.50 | 5 m run x 2.5 m tall, same profile as Wall_01: one piece per lone slab (Wall_Half_* are 2.5 m run but 5 m TALL) |
| `sm_env_wall_01 (plain single-sided)` | `SM_Env_Dwarf_Wall_03` | -0.14..0.31 | 0.00..5.00 | 0.00..5.00 | thinner back face |
| `sm_env_wall_05 (secret tell)` | `SM_Env_Dwarf_Wall_DoorFrame_Slider_01` | -0.42..0.42 | -0.00..5.00 | -0.00..5.00 | a sealed slider door reads as 'this opens' better than an inset arch |
| `sm_env_wall_07 (broken secret)` | `SM_Env_Dwarf_Wall_Archway_01` | -0.39..0.39 | 0.00..5.00 | 0.00..5.00 | the opened tell |
| `sm_env_wall_broken_edge_01/02 (breach)` | `SM_Env_Dwarf_Wall_Broken_01` | -0.42..0.42 | 0.00..5.00 | 0.00..5.00 | 6 broken walls; breaches are mossy-only today |
| `sm_env_wall_broken_edge_01/02 (breach)` | `SM_Env_Dwarf_Wall_Broken_02` | -0.42..0.42 | -0.00..5.00 | -0.00..5.00 | 6 broken walls; breaches are mossy-only today |

## Pillar

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_pillar_square_06 (outline)` | `SM_Env_Dwarf_Pillar_03` | -0.62..0.62 | -0.62..0.62 | 0.00..5.00 | 1.24 m square vs our 0.60 - much chunkier |
| `sm_env_wall_pillar_large_round_01` | `SM_Env_Dwarf_Pillar_01` | -0.62..0.62 | -0.62..0.62 | 0.00..5.00 | free-standing, + SM_Env_Dwarf_Pillar_Base_01 skirt |

## Corner

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_wall_end_coner_inner_01` | `SM_Env_Dwarf_Pillar_07` | -0.56..0.56 | -0.56..0.56 | 0.00..5.00 | USER: pillars 07/08, not the trims (the trims are horizontal 5 m bands). Centred +-0.56 m: one post per vertex |
| `sm_env_wall_end_coner_outer_01` | `SM_Env_Dwarf_Pillar_08` | -0.56..0.56 | -0.56..0.56 | 0.00..5.00 | ditto, convex |

## Stairs

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_stairs_02` | `SM_Env_Dwarf_Stairs_01` | -5.00..0.00 | 0.00..5.00 | -0.23..2.50 | rises EXACTLY 2.50 m = one level (ours is 2.96) |
| `sm_env_stairs_wall_01` | `SM_Env_Dwarf_Stairs_Wall_01` | -5.00..-0.00 | -0.31..0.14 | -0.00..2.50 | long axis is local X here, ours is Y - needs a yaw fix |
| `sm_env_ceiling_stone_stairs_01` | `SM_Env_Dwarf_Stairs_01` | -5.00..0.00 | 0.00..5.00 | -0.23..2.50 | no sloped ceiling: the stair mesh itself, flipped, is a stepped slope rising 2.5 over 5 m |

## Doorway

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_wall_doorframe_01 + door_frame_01` | `SM_Env_Dwarf_Wall_DoorFrame_Single_01` | -0.33..0.33 | 0.00..5.00 | -0.00..5.00 | frame and hole are one piece here - no separate inner frame |
| `sm_env_wall_doorframe_round_01` | `SM_Env_Dwarf_Wall_DoorFrame_Single_02` | -0.33..0.33 | 0.00..5.00 | -0.00..5.00 | second single |
| `sm_env_wall_doorframe_double_01` | `SM_Env_Dwarf_Wall_DoorFrame_Double_01` | -0.33..0.33 | 0.00..5.00 | -0.00..5.00 | double |
| `sm_env_wall_doorframe_double_02` | `SM_Env_Dwarf_Wall_DoorFrame_Double_02` | -0.39..0.39 | -0.00..5.00 | 0.00..5.00 | second double |
| `sm_env_wall_doorframe_double_round_01` | `SM_Env_Dwarf_Wall_Archway_02` | -0.31..0.31 | 0.00..5.00 | 0.00..5.00 | open arch, no leaf |

## Door

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_env_door_01..05, door_bars_01` | `SM_Env_Dwarf_Wall_DoorFrame_Single_01_Door` | -0.17..0.17 | -0.06..1.76 | 0.00..2.46 | 1.82 m leaf, hinge at +Y like ours |
| `sm_env_door_01..05, door_bars_01` | `SM_Env_Dwarf_Wall_DoorFrame_Single_02_Door` | -0.20..0.20 | -0.06..1.80 | 0.01..2.44 | 1.82 m leaf, hinge at +Y like ours |
| `sm_env_doordouble_flat_01/02` | `SM_Env_Dwarf_Wall_DoorFrame_Double_01_Door_01` | -0.20..0.20 | -0.09..1.28 | -0.00..2.93 | 1.37 m leaves |
| `sm_env_doordouble_flat_01/02` | `SM_Env_Dwarf_Wall_DoorFrame_Double_02_Door_02` | -0.20..0.20 | 0.00..1.41 | 0.00..3.26 | 1.37 m leaves |
| `sm_env_doordouble_round_01/02` | `SM_Env_Dwarf_Gate_01_Door_L` | -0.95..0.18 | -0.17..3.16 | 0.00..8.94 | the big gate pair, for halls |
| `sm_env_doordouble_round_01/02` | `SM_Env_Dwarf_Gate_01_Door_R` | -0.95..0.18 | -3.16..0.17 | 0.00..8.94 | the big gate pair, for halls |

## Swap

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `alcove / alcove_2` | `SM_Env_Dwarf_Wall_Alcove_Small_01` | -1.61..0.40 | 0.00..5.00 | -0.00..5.00 | reaches 1.6 m behind the plane, ours 1.5 |
| `alcove / alcove_2` | `SM_Env_Dwarf_Wall_Alcove_Small_02` | -1.61..0.31 | 0.00..5.00 | -0.00..5.00 | reaches 1.6 m behind the plane, ours 1.5 |
| `alcove_round` | `SM_Env_Dwarf_Wall_Alcove_Angular_01` | -1.31..0.25 | 0.00..5.00 | -0.00..5.00 | angular, not round - dwarven |
| `wall_deep` | `SM_Env_Dwarf_Wall_Alcove_Angular_02` | -2.14..0.35 | 0.00..5.00 | 0.00..5.00 | shallow recessed bay |
| `wall_alt / wall_3 / wall_4` | `SM_Env_Dwarf_Wall_02` | -0.14..0.42 | -0.00..5.00 | 0.00..5.00 | 4 plain variants, one more than we had |
| `wall_alt / wall_3 / wall_4` | `SM_Env_Dwarf_Wall_04` | -0.14..0.31 | 0.00..5.00 | 0.00..5.00 | 4 plain variants, one more than we had |
| `wall_alt / wall_3 / wall_4` | `SM_Env_Dwarf_Wall_05` | -0.14..0.42 | 0.00..5.00 | -0.00..5.00 | 4 plain variants, one more than we had |
| `wall_alt / wall_3 / wall_4` | `SM_Env_Dwarf_Wall_06` | -0.14..0.42 | 0.00..5.00 | 0.00..5.00 | 4 plain variants, one more than we had |
| `window / window_2` | `SM_Env_Dwarf_Wall_Window_01` | -0.34..0.34 | 0.00..5.00 | 0.00..5.00 | 4 windows; _02/_04 are the bar inserts |
| `window / window_2` | `SM_Env_Dwarf_Wall_Window_03` | -0.41..0.41 | -0.00..5.00 | -0.00..5.00 | 4 windows; _02/_04 are the bar inserts |
| `sealed_door` | `SM_Env_Dwarf_Wall_Block_Cube_Half_01` | -0.00..2.50 | -0.00..2.50 | -0.00..5.00 | a rock plug in the opening |
| `-` | `SM_Env_Dwarf_Wall_Fireplace_01` | -0.95..1.17 | -0.28..5.29 | -0.00..5.00 | DROPPED as a swap: 1.17 m into the room and -0.28..5.29 along it, breaks the cell |
| `-` | `SM_Env_Dwarf_Grate_Wall_01` | -0.64..0.64 | -1.40..1.40 | -0.97..1.15 | DROPPED as a swap: a 2.8 x 2.1 m mid-pivot insert, not a wall piece |

## Trap

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `sm_prop_trap_spears_grate_01/_spears_01` | `SM_Env_Dwarf_Grate_Floor_01` | -1.34..1.05 | -1.40..1.40 | -0.32..0.28 | grate plate + the pit below it |
| `sm_prop_trap_spears_grate_01/_spears_01` | `SM_Prop_Dwarf_Trap_Gap_01` | -0.29..0.29 | -0.58..0.58 | -2.84..0.94 | grate plate + the pit below it |
| `sm_prop_trap_swing_blade_01 + housing` | `SM_Prop_Dwarf_Trap_Saw_Spin_01` | -1.11..1.11 | -0.18..0.18 | -1.13..1.13 | a spinning sawblade instead of a pendulum |
| `sm_prop_trap_swing_blade_01 + housing` | `SM_Prop_Dwarf_Trap_Saw_01` | -1.19..1.19 | -0.25..0.25 | -0.05..0.05 | a spinning sawblade instead of a pendulum |
| `sm_prop_trap_flamehead_01` | `SM_Prop_Dwarf_Trap_Head_01` | 0.27..0.82 | -0.69..0.69 | -0.91..0.91 | head + its own mouth piece |
| `sm_prop_trap_flamehead_01` | `SM_Prop_Dwarf_Trap_Head_Mouth_01` | -0.16..0.16 | -0.32..0.32 | -0.23..0.23 | head + its own mouth piece |
| `sm_prop_trap_acidhead_01` | `SM_Prop_Dwarf_Trap_Head_02` | -0.29..0.29 | -0.58..0.58 | -0.94..0.94 | the second head |
| `sm_prop_trap_acidhead_01` | `SM_Prop_Dwarf_Trap_Head_Mouth_02` | -0.13..0.13 | -0.30..0.30 | -0.46..0.46 | the second head |
| `sm_prop_trap_saw_01 + rail` | `SM_Prop_Dwarf_Trap_Saw_01` | -1.19..1.19 | -0.25..0.25 | -0.05..0.05 | the cart rail IS the saw rail - forge-hold made literal |
| `sm_prop_trap_saw_01 + rail` | `SM_Prop_Dwarf_Cart_Rail_01` | -0.30..0.30 | -5.00..5.00 | -0.14..0.42 | the cart rail IS the saw rail - forge-hold made literal |
| `sm_prop_trap_wallarrow_hole_01/_02` | `SM_Env_Dwarf_Wall_Dressing_01` | 0.00..0.59 | 0.00..5.00 | -0.02..5.00 | wall dressings stand in for arrow holes |
| `sm_prop_trap_wallarrow_hole_01/_02` | `SM_Env_Dwarf_Wall_Dressing_02` | 0.00..0.59 | 0.00..5.00 | -0.01..2.68 | wall dressings stand in for arrow holes |
| `sm_prop_trap_wallslab_01` | `SM_Env_Dwarf_Wall_Block_Cube_Half_02` | 0.00..2.44 | 0.00..2.44 | 0.00..5.00 | the crushing slab |

## Furniture

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `bookcase` | `SM_Prop_Dwarf_Shelf_01` | 0.00..0.31 | -0.78..0.78 | -0.08..0.08 | Records room |
| `bookcase` | `SM_Prop_Dwarf_Shelf_02` | 0.00..0.31 | -0.38..0.38 | -0.08..0.08 | Records room |
| `weapon_rack` | `SM_Prop_Dwarf_Weapon_Rack_03` | -0.96..0.96 | -0.38..0.38 | -0.00..0.67 | USER: only rack 03 |
| `bed / bed_big` | `SM_Prop_Dwarf_Bed_01` | -1.28..1.28 | -0.69..0.69 | 0.00..0.72 | USER: NOT USED - the DungeonPack beds stay (listed for the record) |
| `bed / bed_big` | `SM_Prop_Dwarf_Bed_02` | -1.28..1.28 | -1.08..1.08 | 0.00..0.71 | USER: NOT USED - the DungeonPack beds stay (listed for the record) |
| `bench` | `SM_Prop_Dwarf_Chair_Bench_01` | -0.22..0.22 | -0.79..0.79 | 0.00..0.42 | + _Broken_01 for decay |
| `stool` | `SM_Prop_Dwarf_Chair_Stool_01` | -0.22..0.22 | -0.22..0.22 | 0.00..0.46 | + _Broken_01 |
| `table_long / table_round / table_low` | `SM_Prop_Dwarf_Table_01` | -0.68..0.68 | -1.01..1.01 | 0.00..0.56 | 5 tables |
| `table_long / table_round / table_low` | `SM_Prop_Dwarf_Table_02` | -0.68..0.68 | -1.01..1.01 | -0.00..0.91 | 5 tables |
| `table_long / table_round / table_low` | `SM_Prop_Dwarf_Table_03` | -0.68..0.68 | -1.67..1.66 | 0.00..0.91 | 5 tables |
| `table_long / table_round / table_low` | `SM_Prop_Dwarf_Table_Small_01` | -0.40..0.40 | -0.39..0.39 | 0.00..0.91 | 5 tables |
| `table_long / table_round / table_low` | `SM_Prop_Dwarf_Table_Small_02` | 0.00..0.53 | -0.76..0.76 | 0.00..0.91 | 5 tables |
| `table_broken / stone_table_broken` | `SM_Prop_Dwarf_Table_Broken_01` | -0.99..0.86 | -1.30..1.16 | -0.01..0.72 |  |
| `table_broken / stone_table_broken` | `SM_Prop_Dwarf_Table_Broken_02` | -0.81..0.81 | -1.37..1.37 | -0.01..0.89 |  |
| `table_broken / stone_table_broken` | `SM_Prop_Dwarf_Table_Small_Broken_01` | -0.66..0.60 | -0.39..0.70 | -0.00..0.91 |  |
| `throne` | `SM_Prop_Dwarf_Throne_01` | -0.93..0.85 | -1.58..1.58 | -0.10..6.54 |  |
| `throne` | `SM_Prop_Dwarf_Throne_02` | -2.89..2.37 | -2.72..2.80 | -0.00..6.68 |  |
| `fireplace_wall` | `SM_Env_Dwarf_Wall_Fireplace_01` | -0.95..1.17 | -0.28..5.29 | -0.00..5.00 | + _Extension_01 |
| `forge` | `SM_Env_Dwarf_Forge_01` | -1.30..1.65 | -2.12..2.12 | -0.00..2.45 | the Forge room's centrepiece |
| `forge` | `SM_Prop_Dwarf_Fire_Pit_01` | -2.08..2.08 | -0.99..0.99 | -0.13..0.24 | the Forge room's centrepiece |
| `forge_pipe` | `SM_Env_Dwarf_Forge_Beam_01` | -0.56..0.67 | 0.00..10.00 | -0.56..0.56 | ceiling beam over the forge |
| `fountain` | `SM_Env_Dwarf_Lava_Duct_01` | -1.73..1.73 | 0.00..10.00 | 0.00..10.83 | lava runs where water did |
| `rug / rug_flat` | `SM_Prop_Camp_Rug_01` | -1.07..1.07 | -0.79..0.79 | -0.01..0.01 | 12 rugs in the camp set |
| `rug / rug_flat` | `SM_Prop_Camp_Rug_02` | -1.05..1.05 | -0.86..0.86 | -0.01..0.01 | 12 rugs in the camp set |
| `rug / rug_flat` | `SM_Prop_Camp_Rug_03` | -1.40..1.48 | -0.71..0.71 | -0.01..0.01 | 12 rugs in the camp set |

## Container

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `crate / crate_metal / crate_ornate` | `SM_Prop_Crate_01` | -0.55..0.55 | -0.55..0.55 | 0.00..1.10 | + SM_Prop_Crate_Broken_01 as decay |
| `crate / crate_metal / crate_ornate` | `SM_Prop_Crate_Stack_01` | -0.58..1.64 | -1.66..0.58 | 0.00..2.18 | + SM_Prop_Crate_Broken_01 as decay |
| `barrel` | `SM_Prop_Barrel_01` | -0.36..0.36 | -0.37..0.35 | 0.00..0.91 | breakable |
| `barrel` | `SM_Prop_Barrel_02` | -0.35..0.35 | -0.35..0.35 | 0.00..0.88 | breakable |
| `barrel` | `SM_Prop_Barrel_03` | -0.58..0.58 | -0.44..0.44 | 0.00..1.12 | breakable |
| `barrel_broken` | `SM_Prop_Barrel_Broken_02` | -0.35..0.35 | -0.40..0.37 | -0.00..0.88 |  |
| `barrel_broken` | `SM_Prop_Barrel_Broken_03` | -0.66..0.48 | -0.40..0.43 | -0.10..0.97 |  |
| `vase` | `SM_Prop_Camp_Pot_02` | -0.31..0.31 | -0.31..0.31 | 0.07..1.00 | USER: keep the vase def, add pots 2 and 6 to its rotation - they need debris kits |
| `vase` | `SM_Prop_Camp_Pot_06` | -0.31..0.31 | -0.31..0.31 | 0.07..1.00 | USER: keep the vase def, add pots 2 and 6 to its rotation - they need debris kits |
| `chest / chest_wood` | `SM_Prop_Dwarf_TreasureChest_01` | -0.94..0.94 | -1.92..1.91 | -0.01..1.00 | + SM_Prop_Dwarf_TreasureChest_Lid_01 / SM_Prop_Camp_Chest_Lid_01 as the attach |
| `chest / chest_wood` | `SM_Prop_Camp_Chest_01` | -0.46..0.49 | -0.71..0.71 | 0.00..0.61 | + SM_Prop_Dwarf_TreasureChest_Lid_01 / SM_Prop_Camp_Chest_Lid_01 as the attach |
| `vat` | `SM_Env_Dwarf_Forge_Smelter_01` | -8.33..8.33 | -8.33..8.33 | -0.19..20.48 | a smelter is the Storage vat |
| `vessel` | `SM_Prop_Dwarf_Smelting_Pot_01` | -0.49..0.32 | -1.67..1.67 | -2.12..0.13 |  |
| `vessel` | `SM_Prop_Dwarf_Smelting_Pot_02` | -1.35..1.43 | -1.57..1.57 | -1.45..0.91 |  |
| `jar` | `SM_Prop_Camp_Pot_03` | -0.20..0.20 | -0.20..0.20 | 0.04..0.59 |  |
| `jar` | `SM_Prop_Camp_Pot_04` | -0.24..0.24 | -0.24..0.24 | -0.00..0.40 |  |

## Crypt

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `coffin / coffin_open` | `SM_Prop_Sarcophagus_01` | -1.23..1.23 | -0.83..0.83 | -0.00..1.11 | + SM_Prop_Sarcophagus_01_Lid as the attach |
| `bone_pile / bone_pile_small` | `SM_Env_Bone_Rib_01` | -2.20..2.03 | -1.83..20.91 | -2.92..21.37 |  |
| `bone_pile / bone_pile_small` | `SM_Env_Bone_Rib_04` | -2.34..2.48 | -1.13..19.59 | -2.54..21.53 |  |
| `bone_pile / bone_pile_small` | `SM_Env_Hell_Bone_Skull_01` | -3.51..3.51 | -8.96..8.96 | -3.49..3.49 |  |
| `skeleton` | `SM_Prop_DeadBody_Dwarf_01` | -0.64..0.70 | -0.39..0.97 | -0.06..1.39 | 8 dead bodies |
| `skeleton` | `SM_Prop_DeadBody_Nomad_01` | -0.66..0.80 | -1.03..0.71 | -0.05..0.31 | 8 dead bodies |
| `chain / chain_short` | `SM_Prop_Dwarf_Chain_01` | -0.05..0.05 | -0.05..0.05 | -0.06..3.64 | + _Clasp_01 |
| `chain / chain_short` | `SM_Prop_Dwarf_Chain_02` | -0.05..0.05 | -0.05..0.05 | -0.06..3.58 | + _Clasp_01 |
| `skeleton_cage / skeleton_hung` | `SM_Prop_Hanging_Platform_01` | -2.00..2.00 | -2.00..2.00 | -0.30..5.74 | no gibbet in the pack; a hanging platform and slung bags are the nearest |
| `skeleton_cage / skeleton_hung` | `SM_Prop_Camp_HangingBag_01` | -0.33..0.33 | -0.36..0.36 | -1.57..0.23 | no gibbet in the pack; a hanging platform and slung bags are the nearest |

## Shrine

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `altar` | `SM_Env_Shrine_01` | -3.95..3.95 | -3.92..3.92 | -1.17..8.66 | the only shrine piece |
| `obelisk` | `SM_Env_Dwarf_Obelisk_01` | -1.40..1.40 | -1.40..1.40 | -0.10..5.84 | + 4 broken variants |
| `obelisk` | `SM_Env_Dwarf_Obelisk_02` | -1.40..1.40 | -1.40..1.40 | -0.10..4.15 | + 4 broken variants |
| `statue` | `SM_Env_Statue_01` | -0.37..0.90 | -0.95..0.95 | -0.00..2.18 | 20 statues |
| `statue` | `SM_Env_Statue_02` | -0.33..0.90 | -0.94..0.96 | -0.01..1.95 | 20 statues |
| `statue` | `SM_Env_Statue_05` | -0.32..0.54 | -0.64..0.64 | -0.00..1.97 | 20 statues |
| `rune_pillar` | `SM_Env_Dwarf_Obelisk_01_Broken_01` | -1.40..1.40 | -1.40..1.40 | -0.00..2.30 |  |
| `rune_pillar` | `SM_Env_Dwarf_Obelisk_02_Broken_01` | -1.40..1.40 | -1.40..1.40 | -0.10..1.36 |  |
| `rune_block` | `SM_Rune_Symbol_02` | -0.07..0.07 | -0.61..0.61 | -0.55..0.55 | 79 to pick from |
| `rune_block` | `SM_Rune_Symbol_15` | -0.07..0.07 | -1.22..1.22 | -0.15..0.15 | 79 to pick from |
| `rune_block` | `SM_Rune_Symbol_28` | -0.07..0.07 | -0.34..0.34 | -0.30..0.30 | 79 to pick from |
| `pentagram` | `SM_Rune_Synty_01` | -0.07..0.07 | -0.21..0.21 | -0.25..0.25 | the big floor sigil |

## Light

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `torch` | `SM_Prop_Dwarf_Torch_01` | -0.03..0.54 | -0.12..0.12 | -0.26..0.52 | 7 torches - the primary light everywhere |
| `torch` | `SM_Prop_Dwarf_Torch_02` | 0.00..0.15 | -0.16..0.16 | -0.18..0.18 | 7 torches - the primary light everywhere |
| `torch` | `SM_Prop_Dwarf_Torch_03` | -0.00..0.19 | -0.19..0.19 | -0.23..0.02 | 7 torches - the primary light everywhere |
| `brazier` | `SM_Prop_Dwarf_Brazier_01` | -1.09..1.09 | -1.09..1.09 | 0.00..1.62 |  |
| `bonfire` | `SM_Prop_Dwarf_Fire_Pit_01` | -2.08..2.08 | -0.99..0.99 | -0.13..0.24 |  |
| `chandelier` | `SM_Prop_Dwarf_Chandelier_01` | -2.96..2.96 | -2.92..2.92 | 0.00..3.63 | 6 chandeliers |
| `chandelier` | `SM_Prop_Dwarf_Chandelier_03` | -0.19..0.19 | -0.19..0.19 | 0.00..0.73 | 6 chandeliers |
| `chandelier` | `SM_Prop_Dwarf_Chandelier_05` | -0.23..0.23 | -0.23..0.23 | 0.00..0.98 | 6 chandeliers |
| `chandelier_broken` | `SM_Prop_Dwarf_Chandelier_06` | -1.60..1.60 | -1.41..1.41 | 0.00..3.00 |  |
| `candle_stand / candles / candle` | `SM_Prop_Dwarf_Torch_06` | -0.29..0.29 | -0.30..0.31 | 0.00..0.55 | NO candles in the pack; small torches stand in |
| `candle_stand / candles / candle` | `SM_Prop_Dwarf_Torch_07` | -0.36..0.36 | -0.36..0.36 | 0.00..1.26 | NO candles in the pack; small torches stand in |
| `stone_lantern` | `SM_Prop_Dwarf_Torch_04` | -0.00..0.30 | -0.31..0.31 | 0.00..0.28 |  |
| `stone_lantern` | `SM_Prop_Dwarf_Torch_05` | 0.00..0.23 | -0.23..0.23 | -0.19..0.10 |  |
| `banner` | `SM_Prop_Banner_06` | -0.12..0.12 | -0.55..0.55 | -2.49..0.10 | the only ones under 4.2 m (01/03 hang 10 m, 04 5.5 m: through the floor); pivot at the top like ours |
| `banner` | `SM_Prop_Banner_Damaged_03` | -0.12..0.12 | -0.96..0.96 | -3.94..0.10 | the only ones under 4.2 m (01/03 hang 10 m, 04 5.5 m: through the floor); pivot at the top like ours |
| `banner` | `SM_Prop_Banner_Damaged_04` | -0.12..0.12 | -0.96..0.96 | -2.35..0.10 | the only ones under 4.2 m (01/03 hang 10 m, 04 5.5 m: through the floor); pivot at the top like ours |

## Clutter

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `rubble` | `SM_Env_Rubble_Rocks_01` | -1.05..1.05 | -1.25..1.25 | -0.10..0.18 |  |
| `rubble` | `SM_Env_Rubble_Rocks_02` | -0.31..0.76 | -0.99..0.68 | -0.10..0.18 |  |
| `rubble` | `SM_Env_Rubble_Rocks_03` | -0.40..0.63 | -0.80..0.99 | -0.10..0.18 |  |
| `pebbles` | `SM_Prop_Rock_Small_01` | -0.32..0.31 | -0.35..0.28 | -0.14..0.14 | the default litter |
| `pebbles` | `SM_Prop_Rock_Small_02` | -0.22..0.30 | -0.26..0.21 | -0.09..0.12 | the default litter |
| `pebbles` | `SM_Prop_Rock_Small_03` | -0.53..0.47 | -0.46..0.36 | -0.14..0.14 | the default litter |
| `bricks / brick` | `SM_Env_Rubble_Slab_01` | -1.97..1.97 | -1.88..1.88 | -0.06..0.51 |  |
| `bricks / brick` | `SM_Env_Rubble_Tile_01` | -1.26..1.26 | -0.78..1.25 | 0.00..0.26 |  |
| `bricks / brick` | `SM_Env_Rubble_Tile_02` | -0.07..1.25 | -1.27..0.13 | 0.00..0.26 |  |
| `plank` | `SM_Env_Tiles_Loose_01` | -0.95..0.68 | -1.03..0.98 | -0.16..0.18 |  |
| `plank` | `SM_Env_Tiles_Loose_03` | -0.81..0.82 | -0.50..0.52 | -0.16..0.17 |  |
| `pillar_pile` | `SM_Env_Rubble_Pillar_01` | -1.86..1.86 | -2.06..2.06 | -1.05..1.05 |  |
| `pillar_pile` | `SM_Env_Rubble_Pillar_02` | -1.50..1.82 | -1.24..1.57 | -1.88..1.86 |  |
| `shard / vase_broken` | `SM_Env_Tiles_Loose_05` | -0.65..0.63 | -0.55..0.54 | -0.20..0.15 |  |
| `shard / vase_broken` | `SM_Env_Tiles_Loose_06` | -0.49..0.49 | -0.30..0.29 | -0.18..0.07 |  |
| `coins` | `SM_Prop_Gold_Coins_01` | -0.35..0.35 | -0.41..0.41 | 0.00..0.07 | 15 coin piles |
| `coins` | `SM_Prop_Gold_Coins_03` | -0.23..0.23 | -0.27..0.27 | 0.00..0.07 | 15 coin piles |
| `coins` | `SM_Prop_Gold_Coins_05` | -1.01..1.01 | -0.78..0.78 | 0.00..0.04 | 15 coin piles |
| `roots / mushrooms` | `SM_Env_SpiderWeb_01` | -0.08..4.15 | 0.12..4.94 | -0.12..5.04 | webs are this biome's decay |
| `roots / mushrooms` | `SM_Env_SpiderWeb_03` | -2.54..1.71 | 0.18..2.72 | -0.00..4.68 | webs are this biome's decay |

## Forge-hold

| replaces | Realms model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| `-` | `SM_Prop_Dwarf_Cart_01` | -1.00..0.07 | -0.32..0.32 | -3.06..0.02 | ore carts |
| `-` | `SM_Prop_Dwarf_Cart_02` | -1.22..1.22 | -0.24..0.24 | -3.00..0.02 | ore carts |
| `-` | `SM_Prop_Dwarf_Cart_03` | -1.07..1.07 | -0.50..0.50 | 0.13..0.87 | ore carts |
| `-` | `SM_Prop_Dwarf_Cart_Rail_01` | -0.30..0.30 | -5.00..5.00 | -0.14..0.42 | rails to run them on |
| `-` | `SM_Prop_Dwarf_Cog_Large_01` | -0.92..0.92 | -0.92..0.92 | -0.10..0.10 | machinery clutter |
| `-` | `SM_Prop_Dwarf_Cog_Large_03` | -0.91..0.92 | -0.92..0.92 | -0.09..0.09 | machinery clutter |
| `-` | `SM_Prop_Dwarf_Cog_Pile_01` | -0.61..0.58 | -0.77..0.77 | -0.01..0.26 | machinery clutter |
| `-` | `SM_Prop_Dwarf_Cog_Small_01` | -0.49..0.49 | -0.49..0.49 | -0.06..0.06 | machinery clutter |
| `-` | `SM_Prop_Dwarf_Crane_01` | -11.29..4.53 | -3.22..3.22 | -0.00..6.32 | big room centrepieces |
| `-` | `SM_Prop_Dwarf_Capstar_01` | -0.55..0.55 | -0.55..0.55 | -0.04..1.19 | big room centrepieces |
| `-` | `SM_Env_Dwarf_Forge_Smelter_02` | -2.56..4.34 | -2.56..2.56 | -0.18..2.00 | the Smeltery |
| `-` | `SM_Env_Dwarf_Forge_Gutter_Straight_01` | -0.00..5.00 | -0.41..0.41 | -0.36..0.11 | the Smeltery |
| `-` | `SM_Env_Dwarf_Lava_Duct_01` | -1.73..1.73 | 0.00..10.00 | 0.00..10.83 | lava plumbing - the biome's signature |
| `-` | `SM_Env_Dwarf_Lava_Duct_Pillar_01` | -2.85..2.85 | -2.85..2.85 | 0.00..13.69 | lava plumbing - the biome's signature |
| `-` | `SM_Env_Dwarf_Lava_Wheel_01` | -3.59..3.59 | -2.30..2.30 | -3.59..3.59 | lava plumbing - the biome's signature |
| `-` | `SM_Prop_Anvil_01` | -0.33..0.33 | -0.89..0.89 | 0.00..1.02 |  |
| `-` | `SM_Prop_Dwarf_Lever_01` | -0.49..0.49 | -0.36..0.36 | -0.21..0.34 |  |
| `-` | `SM_Prop_Dwarf_Totem_01` | -0.72..0.72 | -0.83..0.83 | 0.00..2.71 |  |
| `-` | `SM_Env_Crystals_Cluster_Large_01` | -1.84..2.02 | -2.73..2.91 | -1.28..6.22 | crystal seams on their own shader |
| `-` | `SM_Prop_Crystal_01` | -0.05..0.05 | -0.05..0.05 | -0.10..0.10 | crystal seams on their own shader |
| `-` | `SM_Prop_Gem_01` | -0.05..0.05 | -0.05..0.05 | -0.05..0.02 | crystal seams on their own shader |
| `-` | `SM_Env_Dwarf_Balustrade_01` | -0.14..0.14 | -2.50..0.00 | 0.00..0.87 | galleries and spans over the lava |
| `-` | `SM_Env_Dwarf_Bridge_01` | 0.00..10.00 | -0.65..5.65 | -10.00..0.00 | galleries and spans over the lava |
| `-` | `SM_Env_Dwarf_Beam_01` | -0.31..0.31 | 0.00..5.00 | -0.00..2.19 | galleries and spans over the lava |
