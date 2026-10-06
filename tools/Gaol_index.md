# Gaol model index

The Dark Fantasy (DF) and Dark Fortress (FO) pieces the gaol biome uses (DungeonKit.Gaol.cs).
Extents are metres in the s&box local frame: X = depth, Y = width, Z = up; multi-mesh files measured whole.
Runtime paths: `darkfantasy/<name lowercased>.vmdl` and `darkfortress/<name lowercased>.vmdl`.

## Floor

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Bld_Floor_Tile_01` | -0.00..2.50 | -0.01..2.50 | -0.09..0.06 | 2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates |
| FO | `SM_Bld_Floor_Tile_02` | -0.00..2.50 | -0.00..2.50 | -0.10..0.06 | 2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates |
| FO | `SM_Bld_Floor_Tile_03` | -0.00..2.50 | -0.00..2.50 | -0.09..0.05 | 2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates |
| FO | `SM_Bld_Floor_Tile_04` | -0.00..2.50 | -0.00..2.50 | -0.09..0.06 | 2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates |
| FO | `SM_Bld_Floor_01` | 0.00..2.50 | 0.00..2.50 | -0.09..0.01 | 2.5 m tiles, four per cell in six fixed mixes (no Round_Tile: their edge opens a hole, user); Floor_01 plain under the trap plates |
| DF | `SM_Prop_Ritual_Circle_01` | -3.04..3.03 | -3.03..3.03 | -0.01..1.08 | the ritual floors: a circle (6 m, scaled 0.8) or a medallion on plain tiles |
| DF | `SM_Bld_Decoration_01` | -1.01..1.01 | -1.01..1.01 | -0.03..0.12 | the ritual floors: a circle (6 m, scaled 0.8) or a medallion on plain tiles |

## Ceiling

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Bld_Ceiling_01` | -2.64..0.01 | -0.01..2.64 | -0.04..1.50 | vault quadrant: low corner at the pivot, rises 1.5 m to the far corner; four per cell, scaled 0.947 to 2.5 m |

## Wall

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Bld_Wall_01` | -0.11..0.25 | 0.00..2.50 | -0.01..3.01 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Wall_02` | -0.11..0.28 | 0.00..2.50 | 0.00..3.01 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Wall_03` | -0.11..0.28 | 0.00..2.50 | 0.00..3.01 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Wall_04` | -0.30..0.30 | 0.00..2.50 | -0.02..3.01 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Base_Wall_Half_01` | -0.11..0.25 | 0.00..1.25 | -0.01..3.01 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Wall_Detail_02` | 0.20..0.43 | 0.00..5.00 | -0.19..0.19 | 2.5 x 3 panels (bottom row unscaled, top row scaled to 2 m), the 1.25 m half, the 5 m moulding over the seam (the builder's WallTrim, not in the throne room) |
| FO | `SM_Bld_Wall_L_01` | -0.23..0.23 | 0.00..5.00 | 0.00..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_L_03` | -0.23..0.23 | 0.00..5.00 | 0.00..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_L_Damaged_04` | -0.55..0.59 | 0.00..5.00 | 0.00..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_L_Damaged_05` | -0.58..0.57 | 0.00..5.00 | -0.02..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_L_Damaged_06` | -0.68..0.54 | -0.46..5.00 | 0.00..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_L_Arch_01` | -0.39..0.39 | 0.00..5.00 | -0.00..6.01 | 5 x 6 m pieces squashed to 5 m: the breach leaves, the secret tell (L_03) and its opened state (Damaged_06), the open arch doorway |
| FO | `SM_Bld_Wall_Prison_01` | -0.11..0.12 | 0.00..2.50 | 0.00..3.01 | iron bars (see-through, mesh collider), the prison gate, slit windows |
| FO | `SM_Bld_Wall_Door_Prison_01` | -0.14..0.14 | 0.00..2.50 | -0.00..3.01 | iron bars (see-through, mesh collider), the prison gate, slit windows |
| FO | `SM_Bld_Wall_Window_02` | -0.22..0.23 | 0.00..2.50 | 0.00..3.01 | iron bars (see-through, mesh collider), the prison gate, slit windows |
| FO | `SM_Bld_Wall_Window_04` | -0.18..0.18 | 0.00..2.50 | 0.00..3.01 | iron bars (see-through, mesh collider), the prison gate, slit windows |
| FO | `SM_Bld_Wall_Window_05` | -0.17..0.17 | 0.00..2.50 | 0.00..3.01 | iron bars (see-through, mesh collider), the prison gate, slit windows |
| DF | `SM_Bld_Wall_Alcove_01` | -0.26..0.26 | 0.00..5.00 | 0.00..3.01 | the arched niche (5 m; Alcove_02 dropped, user), boards over a slit |
| DF | `SM_Prop_Boarded_Wood_01` | -0.11..0.11 | -0.60..0.60 | -0.59..0.59 | the arched niche (5 m; Alcove_02 dropped, user), boards over a slit |

## Doorway

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Bld_Base_Wall_Door_01` | -0.14..0.14 | 0.00..2.50 | -0.00..3.01 | single door panel (hole 100 x 195), the ragged ruin doorway, the chained fake door |
| DF | `SM_Bld_Wall_Door_Ruin_02` | -0.18..0.18 | 0.00..2.50 | -0.23..3.01 | single door panel (hole 100 x 195), the ragged ruin doorway, the chained fake door |
| DF | `SM_Prop_Crypt_Door_Chained_01` | -0.23..0.11 | -0.04..1.04 | -0.00..1.97 | single door panel (hole 100 x 195), the ragged ruin doorway, the chained fake door |
| FO | `SM_Bld_Wall_Door_Double_02` | -0.24..0.24 | 0.00..2.50 | -0.00..3.01 | double door panel (hole 200 x 245) |

## Door

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Bld_Base_Door_01` | -0.12..0.12 | -0.00..1.00 | 0.00..1.97 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Bld_Door_02` | -0.10..0.10 | -0.02..1.00 | -0.00..1.97 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Bld_Base_Door_Large_01` | -0.12..0.12 | 0.00..1.12 | 0.00..2.47 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Prop_Boarded_Wood_01` | -0.11..0.11 | -0.60..0.60 | -0.59..0.59 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Prop_Boarded_Wood_02` | -0.07..0.07 | -0.59..0.59 | -0.59..0.59 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Prop_Boarded_Wood_03` | -0.08..0.08 | -0.61..0.61 | -0.61..0.61 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| DF | `SM_Prop_Boarded_Wood_04` | -0.07..0.07 | -0.59..0.59 | -0.62..0.62 | leaves hinge at y 0 and run +Y; the boards are breakable panels that shut a dead-end room's doorway |
| FO | `SM_Bld_Door_01` | -0.18..0.07 | 0.00..0.99 | -0.00..2.49 | Door_01 is the tall pair for the double hole |
| FO | `SM_Bld_Door_04` | -0.15..0.07 | -0.00..1.00 | -0.00..1.97 | Door_01 is the tall pair for the double hole |
| FO | `SM_Bld_Door_05` | -0.19..0.08 | -0.00..1.00 | -0.00..1.97 | Door_01 is the tall pair for the double hole |
| FO | `SM_Bld_Door_06` | -0.19..0.07 | -0.07..1.00 | -0.00..1.97 | Door_01 is the tall pair for the double hole |

## Stairs

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Bld_Stairs_Tiles_01` | -2.51..-0.00 | 0.00..2.50 | -0.33..1.51 | covers -X/+Y and climbs -X (StairMirrorY); scaled (2, 2, 1.667) to 5 x 5 x 2.5; flipped, it is the stair ceiling too |

## Pillar

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Bld_Pillar_05` | -0.54..0.54 | -0.54..0.54 | 0.00..6.00 | the outline pillar, 6 m squashed to 5 |
| FO | `SM_Bld_L_Pillar_04` | -0.72..0.72 | -0.72..0.72 | 0.00..6.00 | the in-room pillar (6 m squashed) and the corner post (3 m + 2 m stacked) |
| FO | `SM_Bld_Pillar_02` | -0.50..0.50 | -0.50..0.50 | 0.00..3.03 | the in-room pillar (6 m squashed) and the corner post (3 m + 2 m stacked) |

## Banner

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Prop_Flag_Dark_02` | -0.13..0.13 | 0.00..2.56 | -3.09..0.34 | top-pivot, hang 2.9-3.1 m |
| DF | `SM_Prop_Flag_Dark_Damaged_02` | -0.13..0.13 | 0.00..2.56 | -2.56..0.34 | top-pivot, hang 2.9-3.1 m |
| FO | `SM_Prop_Banner_02` | -0.13..0.13 | -1.03..1.03 | -2.75..0.71 | top-pivot, hang 2.1-2.8 m |
| FO | `SM_Prop_Banner_03` | -0.10..0.10 | -0.83..0.88 | -2.09..0.30 | top-pivot, hang 2.1-2.8 m |

## Lights

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Torch_02` | -0.00..0.66 | -0.28..0.32 | -0.14..0.46 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Torch_06` | -0.12..0.14 | -0.13..0.10 | 0.00..1.23 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Torch_07` | -0.14..0.14 | -0.13..0.13 | 0.00..1.23 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Candle_Stand_01` | -0.28..0.28 | -0.28..0.28 | 0.00..0.80 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Brazier_03` | -0.63..0.54 | -0.58..0.58 | 0.00..1.23 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Brazier_05` | -0.49..0.47 | -0.47..0.44 | 0.00..1.14 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Chandelier_01` | -0.71..0.71 | -0.71..0.71 | -1.43..-0.01 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| FO | `SM_Prop_Chandelier_01_Damaged_01` | -0.60..0.50 | -0.60..0.66 | 0.00..1.00 | torch (Torch_05 dropped: a spike lantern, no wall face), candle_stand, brazier, chandelier, chandelier_fallen |
| DF | `SM_Prop_Torch_01` | -0.02..0.53 | -0.31..0.27 | 0.00..1.00 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candelabra_01` | -0.35..0.35 | -0.35..0.35 | 0.00..1.76 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candelabra_02` | -0.18..0.18 | -0.34..0.34 | 0.00..1.80 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candelabra_03` | -0.39..0.39 | -0.66..0.66 | -0.00..3.14 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candelabra_Small_01` | -0.26..0.26 | -0.26..0.26 | 0.00..0.84 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candle_Rack_01` | -0.41..0.42 | -1.08..1.07 | 0.00..1.24 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Candle_Wall_01` | -0.01..0.22 | -0.30..0.30 | 0.00..0.96 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Brazier_01` | -0.39..0.39 | -0.40..0.39 | 0.00..0.90 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Lamp_Hanging_01` | -0.22..0.22 | -0.20..0.20 | -1.23..0.00 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |
| DF | `SM_Prop_Chandelier_01` | -1.07..1.08 | -1.11..1.12 | -1.68..0.32 | candle_stand, candelabra_tall/small, candle_rack, candle_wall, lamp_hanging, chandelier_big |

## Corpses

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Corpse_01` | -1.03..1.03 | -0.88..0.47 | -0.07..0.24 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_04` | -0.69..1.15 | -0.54..0.49 | -0.25..1.23 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_10` | -0.17..0.29 | -0.22..0.24 | -1.47..0.04 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_Prisoner_01` | -0.21..0.76 | -0.42..0.28 | -0.04..1.36 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_Prisoner_03` | -0.42..0.30 | -0.27..0.26 | -2.02..0.19 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_Prisoner_04` | -0.21..0.21 | -0.97..0.97 | -0.79..0.79 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_Pile_02` | -1.36..1.19 | -2.20..1.66 | -0.18..0.72 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Corpse_Spike_01` | -0.18..0.28 | -0.25..0.22 | -0.41..4.27 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Head_Spike_01` | -0.18..0.11 | -0.14..0.13 | -0.29..2.26 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Limb_01` | -0.40..0.30 | -0.15..0.04 | -0.01..0.09 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Meat_01` | -0.38..0.36 | -0.16..0.15 | -0.12..0.16 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| FO | `SM_Prop_Plague_Organs_01` | -0.15..0.15 | -0.16..0.16 | -0.00..0.10 | corpse (lying), corpse_sitting, corpse_hanging (top pivot), skeleton_hung (mid pivot, spread on the wall), corpse_pile, corpse_spike, head_spike, limb, meat, organs |
| DF | `SM_Prop_Gallows_01_Body_01` | -0.10..0.43 | -0.21..0.30 | -0.02..1.80 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |
| DF | `SM_Prop_Body_Burnt_01` | -0.94..0.91 | -0.21..0.57 | -0.07..0.25 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |
| DF | `SM_Prop_Body_Burnt_04` | 0.15..1.11 | -0.58..0.58 | 0.04..0.88 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |
| DF | `SM_Prop_Body_Pile_Burnt_01` | -1.30..1.31 | -1.08..1.27 | -0.17..0.79 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |
| DF | `SM_Prop_Body_Skeleton_01` | -0.96..0.90 | -0.52..0.46 | -0.09..0.21 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |
| DF | `SM_Prop_Animal_Dead_01` | -1.43..1.48 | -0.48..0.95 | -0.07..0.54 | hanged_body (feet pivot, hung at 3.7 so the rope meets the head), corpse_burnt, corpse_burnt_kneeling, corpse_pile_burnt, skeleton, carcass |

## Bones

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Bone_01` | -0.23..0.24 | -0.07..0.20 | 0.00..0.13 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Prop_Bone_Pile_01` | -1.03..0.99 | -1.37..1.20 | -0.21..0.56 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Prop_Skull_01` | -0.13..0.13 | -0.11..0.11 | -0.07..0.21 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Prop_Skull_Animal_01` | -0.50..0.53 | -0.32..0.32 | -0.02..0.40 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Prop_Skull_Pile_01` | -0.18..0.16 | -0.38..0.50 | -0.05..0.58 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Env_Bones_01` | -0.46..0.38 | -0.33..0.33 | -0.31..0.88 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Env_Bones_Skull_01` | -1.27..1.11 | -0.95..0.94 | -0.01..1.78 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Env_Bones_Hand_01` | -0.12..1.38 | -0.57..0.71 | -0.89..1.82 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |
| FO | `SM_Env_Bones_Ribs_01` | -2.65..2.20 | -2.07..2.07 | -1.79..1.77 | bone, bone_pile, skull, skull_animal, skull_pile, bone_ribs, giant_skull, giant_hand, beast_ribs (Bury 0, scaled 0.9) |

## Cages/torture

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Cage_01` | -0.72..0.72 | -0.71..0.78 | 0.00..2.35 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Cage_02` | -0.23..0.24 | -0.41..0.40 | 0.00..1.96 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Cage_03` | -0.50..0.30 | -0.60..0.61 | 0.00..2.02 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Cage_Hanging_01` | -0.75..0.69 | -0.71..0.77 | -4.10..0.00 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Plinth_01` | -0.34..0.34 | -0.40..0.40 | 0.00..0.82 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Torture_Device_01` | -0.66..0.66 | -1.00..1.00 | -0.03..1.43 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Wheel_01` | -2.40..2.40 | -1.86..1.86 | 0.00..3.30 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Stretching_Rack_01` | -1.68..1.75 | -1.14..1.14 | 0.00..1.82 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Shackles_Wall_01` | -0.00..0.19 | -0.18..0.14 | -0.66..0.11 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Wrist_Bonds_01` | -0.07..0.07 | -0.32..0.32 | -0.00..0.88 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| FO | `SM_Prop_Key_Ring_01` | -0.07..0.10 | -0.04..0.04 | -0.24..0.01 | cage_standing/cage, cage_wall, skeleton_cage (hangs 4.1 m: capped to 2.9), plinth_cage, torture_device, wheel, stretch_table, shackles, wrist_bonds, key_ring |
| DF | `SM_Prop_Cage_Large_01` | -1.57..1.62 | -0.77..0.77 | -0.04..2.19 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Cage_Small_01` | -0.71..0.71 | -0.63..0.63 | 0.05..1.26 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Stock_01` | -0.64..0.64 | -0.81..0.81 | -0.02..2.48 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Post_01` | -0.28..1.35 | -0.27..0.27 | 0.00..3.97 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Gallows_01` | -0.78..1.37 | -3.05..3.08 | -0.07..4.22 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Gibbet_01` | -1.62..1.59 | -1.51..1.82 | -0.02..4.79 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Gibbet_01_Damaged_01` | -0.53..0.53 | -1.12..1.08 | -0.04..0.97 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Pyre_02` | -1.59..1.64 | -1.55..1.61 | -0.53..1.39 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Pyre_Stake_01` | -0.90..0.40 | -1.82..1.82 | -0.25..4.92 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Pyre_Stake_02_Burnt_02` | -0.44..0.77 | -0.27..0.39 | -0.18..1.65 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |
| DF | `SM_Prop_Pyre_Stake_02` | -0.43..0.09 | -0.08..0.08 | -0.18..2.17 | cage_large, cage_small, stocks, whipping_post, gallows (0.8), gibbet (0.9), gibbet_fallen, pyre, pyre_stake (0.9), stake_burnt, stake |

## Chains

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Chain_01` | -0.03..0.00 | -0.10..0.10 | -0.75..0.00 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_02` | -0.04..0.00 | -0.10..0.10 | -1.50..0.00 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_03` | -0.05..0.00 | -0.10..0.10 | -3.01..0.00 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_04` | -0.04..0.00 | -1.30..1.30 | -0.47..0.10 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_06` | -0.04..0.00 | -2.50..2.50 | -1.06..0.10 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_08` | -0.02..0.06 | -0.67..0.85 | -1.50..0.00 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Chain_Pile_01` | -0.64..0.88 | -0.50..0.94 | -0.01..0.08 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Skull_Hanging_01` | -0.17..0.22 | -0.34..0.34 | -1.47..0.04 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| FO | `SM_Prop_Table_Suspended_01` | -1.10..1.10 | -0.83..0.82 | 0.80..2.56 | chain_short, chain, chain_long, chain_swag_short (2.6 m, both ends up), chain_swag (5 m), chain_hook, chain_pile, skull_hanging, table_suspended |
| DF | `SM_Prop_Chain_01` | -0.06..0.05 | -0.06..0.06 | -0.06..1.00 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Chain_02` | -0.06..0.06 | -0.06..0.06 | -0.06..3.00 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Gibbet_01_Cage_01` | -0.53..0.50 | 0.99..1.97 | 1.70..3.87 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Rope_Hanging_01` | -0.11..0.12 | -0.10..0.13 | -2.08..0.01 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Noose_01` | -0.06..0.06 | -0.16..0.15 | -0.62..0.67 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Noose_Bundle_01` | -0.20..0.20 | -0.13..0.13 | -0.17..0.17 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Bell_Large_01` | -0.66..0.68 | -0.68..0.68 | -1.45..0.00 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |
| DF | `SM_Prop_Chain_Connector_01` | -0.21..0.21 | -0.21..0.21 | -0.03..0.15 | the hangers (chain, chain_short, chain_long: atlas chains flipped to hang, the Fortress cutout chains do not render), the hung gibbet cage, rope, noose, rope_bundle, bell, chain_ring |

## Walls

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Head_Display_01` | -0.15..0.16 | -0.26..0.25 | -0.01..0.60 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Prop_Plaque_01` | -0.00..0.23 | -0.73..0.73 | 0.00..2.73 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Prop_Rune_01` | -0.03..0.03 | -0.40..0.40 | 0.00..0.60 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Prop_Greeble_04` | -0.01..0.09 | -0.45..0.44 | -0.47..0.46 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Prop_Greeble_01` | -0.01..0.16 | -1.79..1.78 | -0.02..0.92 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Env_Spiderweb_06` | -0.43..0.51 | 0.05..1.44 | -0.00..1.06 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| FO | `SM_Prop_Banner_01` | -0.28..0.23 | 0.00..1.24 | -3.39..0.48 | wall_trophy (with DF Head_Mounted_01), wall_plaque, wall_rune, wall_greeble, wall_band, web_corner, thorns_wall (3-D pieces only), thorns_ceiling, banner_pole; the grunge/moss decals are out (cutout vmats render white) |
| DF | `SM_Prop_Head_Mounted_01` | -0.00..1.05 | -0.52..0.51 | -0.42..0.99 | the mounted head |

## Lab

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Specimen_Tank_01` | -0.57..0.58 | -0.74..0.82 | -0.02..3.13 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Specimen_Tank_02` | -2.59..0.87 | -1.05..1.11 | -0.01..3.68 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Specimen_Jar_01` | -0.15..0.15 | -0.15..0.15 | 0.00..0.36 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Alchemy_Tool_04` | -0.26..0.21 | -0.20..0.26 | -0.01..0.84 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Surgical_Tool_01` | -0.04..0.05 | -0.22..0.21 | -0.20..0.77 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Research_Papers_01` | 0.00..0.11 | -1.02..1.09 | -0.54..0.53 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |
| FO | `SM_Prop_Altar_01` | -0.56..0.56 | -1.08..1.08 | 0.00..0.66 | specimen_tank, specimen_tank_big, specimen_jar (+ inserts), alchemy_tools, tool_wall, papers_wall, altar_slab |

## Chapel

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Prop_Altar_Table_01` | -0.45..0.45 | -1.01..1.01 | -0.00..0.86 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Tabernacle_01` | -0.61..0.61 | -1.82..1.82 | 0.00..1.44 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Organ_01` | -0.76..1.51 | -3.62..3.62 | -0.01..10.08 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Holy_Water_01` | -0.56..0.51 | -0.55..0.55 | -0.00..1.31 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Lecturn_01` | -0.39..0.39 | -0.39..0.39 | 0.00..1.52 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Pew_01` | -0.38..0.38 | -0.01..2.00 | 0.00..1.03 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Pew_Large_01` | -0.38..0.38 | -0.01..4.01 | 0.00..1.04 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Pew_Large_01_Damaged_02` | -0.35..0.49 | -0.13..1.86 | -0.06..1.05 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Demon_01` | -0.22..1.15 | -1.54..1.59 | -0.00..1.64 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Demon_Ward_01` | -0.25..0.46 | -0.69..0.79 | -0.03..3.16 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Gargoyle_Statue_01` | -0.46..0.63 | -1.22..1.23 | 0.05..1.68 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Gargoyle_01` | 0.14..1.49 | -0.96..0.96 | 0.03..1.29 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Statue_01` | -0.54..0.59 | -0.90..0.88 | -0.41..2.03 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Incense_01` | -0.01..0.01 | -0.01..0.01 | -0.09..0.40 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |
| DF | `SM_Prop_Candle_Blob_01` | -0.16..0.16 | -0.24..0.24 | -0.01..0.05 | altar_table, tabernacle, organ (0.4), font, lectern, pew (recentred), pew_long, pew_broken, demon_statue, demon_ward, gargoyle_statue, gargoyle_wall, statue_pedestal, incense, wax |

## Crypt

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Prop_Tomb_01` | -1.30..1.32 | -0.67..0.68 | -0.01..1.76 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| DF | `SM_Prop_Tomb_02` | -1.41..1.41 | -0.75..0.72 | 0.01..1.17 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| DF | `SM_Prop_Tomb_Stone_01` | -0.28..0.28 | -0.39..0.38 | -0.04..1.22 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| DF | `SM_Prop_Tomb_Stone_Basic_01` | -0.19..0.19 | -0.55..0.56 | -0.24..1.28 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| DF | `SM_Prop_Tomb_Stone_05` | -1.33..1.33 | -0.70..0.70 | 0.00..0.30 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| DF | `SM_Prop_Crypt_01` | -2.54..2.54 | -2.36..2.36 | -0.08..5.69 | tomb, tomb_low (0.88), tombstone, tombstone_plain, grave_slab (0.85), mausoleum (0.75) |
| FO | `SM_Prop_Coffin_01` | -1.11..1.13 | -0.56..0.56 | 0.00..0.53 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Coffin_01_Damaged_01` | -1.15..1.15 | -0.56..0.56 | 0.00..0.76 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Statue_02` | -1.12..1.16 | -1.12..1.12 | 0.00..5.70 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Statue_Base_01` | -0.47..0.47 | -0.47..0.47 | 0.00..0.23 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Tomb_Blanket_01` | -0.34..0.34 | -0.86..0.86 | -0.22..0.22 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Plushie_01` | -0.10..0.10 | -0.17..0.17 | 0.00..0.63 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Urn_01` | -0.35..0.35 | -0.34..0.34 | 0.00..1.10 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |
| FO | `SM_Prop_Urn_04` | -0.22..0.22 | -0.22..0.22 | -0.00..0.38 | coffin (+ decay), statue_chained (0.8), statue_base, tomb_blanket, plushie, vase (the urns, clay-breakable) |

## Stores

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| DF | `SM_Prop_Sack_01` | -0.28..0.28 | -0.28..0.28 | -0.02..0.54 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Keg_01` | -0.55..0.65 | -0.63..0.63 | 0.00..1.88 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Meat_Bucket_01` | -0.25..0.25 | -0.25..0.25 | 0.00..0.47 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Counter_Bar_01` | -0.41..0.41 | -1.27..1.27 | 0.00..1.07 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Wagon_01_Damaged_01` | -2.38..2.83 | -1.55..1.95 | -0.19..1.83 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Well_01` | -1.61..1.60 | -1.81..1.83 | -0.06..1.10 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Barrel_01` | -0.36..0.36 | -0.37..0.37 | 0.01..0.96 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Crate_01` | -0.34..0.34 | -0.56..0.56 | 0.00..0.40 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Crate_02` | -0.54..0.54 | -0.54..0.54 | 0.00..0.50 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Rack_Weapon_01` | -0.18..0.18 | -0.71..0.71 | -0.01..0.93 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Fire_Pit_01` | -0.75..0.71 | -1.28..1.24 | 0.00..0.51 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Bed_01` | -1.12..1.15 | -0.86..0.85 | -0.02..1.10 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Bookshelf_Double_01` | -0.00..0.64 | 0.00..2.51 | 0.00..2.91 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| DF | `SM_Prop_Bookshelf_01` | 0.07..0.72 | -0.00..1.27 | 0.00..2.90 | sack, keg, bucket, counter, wagon_wrecked (0.9), well, barrel (breakable), crate (breakable), weapon_rack (1.4, breakable), fire_pit, bed (+ decay), bookcase, bookcase_small (recentred) |
| FO | `SM_Prop_Crate_01` | -0.25..0.25 | -0.38..0.38 | 0.00..0.39 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Crate_Destroyed_01` | -0.54..0.54 | -0.54..0.54 | 0.00..1.11 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Trough_01` | -0.63..0.63 | -1.60..1.60 | 0.00..1.71 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Cupboard_01` | -0.45..0.45 | -0.89..0.89 | -0.01..2.44 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Shelf_02` | -0.46..0.46 | -1.09..1.09 | -0.00..2.15 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Shelf_01` | -0.18..0.15 | -0.53..0.53 | 0.00..0.83 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Wheel_Barrow_01` | -0.62..1.61 | -0.42..0.36 | -0.02..0.71 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Desk_01` | -0.65..0.65 | -1.22..1.22 | 0.00..0.86 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Table_01` | -0.51..0.51 | -1.01..1.01 | 0.00..0.95 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Table_02` | -0.78..0.78 | -0.78..0.78 | 0.00..0.97 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Table_03` | -0.65..0.65 | -1.69..1.69 | 0.00..0.70 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Table_Small_01` | -0.39..0.38 | -0.39..0.38 | -0.01..0.68 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Chair_01` | -0.21..0.24 | -0.25..0.27 | 0.00..1.00 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Bench_01` | -0.26..0.26 | -0.89..0.88 | 0.00..0.51 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Bed_01` | -0.02..1.11 | -1.37..1.37 | -0.70..0.19 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Throne_01` | -0.53..0.75 | -1.05..1.05 | -0.02..3.12 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Bookshelf_01` | -0.89..0.89 | -1.13..1.13 | 0.00..2.72 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Cauldron_02` | -1.55..1.36 | -1.16..1.14 | -0.01..2.28 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Fireplace_02` | -1.07..1.07 | -1.07..1.07 | 0.00..0.98 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |
| FO | `SM_Prop_Tent_02` | -0.40..1.98 | -2.32..2.32 | -0.06..2.79 | crate, crate_broken, trough, cupboard, shelf, shelf_wall, wheelbarrow, desk, table_long/round/low, stone_table, stool, bench, bed_big (pivot 0.7 up), throne (+ dead king), bookcase_double, cauldron_big, fire_pit_round, tent (0.85) |

## Siege

| pack | model | X (m) | Y (m) | Z (m) | note |
|---|---|---|---|---|---|
| FO | `SM_Prop_Ballista_01` | -2.06..2.15 | -1.81..1.84 | -0.06..2.09 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Veh_Cannon_01` | -2.06..2.07 | -0.90..0.90 | 0.00..2.62 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Veh_Arrow_Launcher_01` | -2.13..2.13 | -1.69..1.69 | -0.02..3.64 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Slingshot_01` | -1.33..1.10 | -1.43..1.43 | -0.01..1.73 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Boiling_Oil_01` | -1.24..1.99 | -1.50..1.50 | 0.00..2.20 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Barricade_01` | -1.67..0.09 | -0.88..0.82 | -0.07..2.02 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Barricade_02` | -1.60..0.20 | -1.48..1.48 | -0.06..1.83 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Arrow_Basket_01` | -0.30..0.29 | -0.28..0.24 | 0.00..0.85 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Cannonball_01` | -0.14..0.16 | -0.14..0.15 | 0.00..0.30 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Dial_01` | -0.73..0.73 | -0.86..0.86 | -0.02..2.80 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Pipe_01` | -0.21..0.21 | -0.03..1.50 | -0.21..0.21 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Sewer_Grate_01` | -1.18..1.18 | -1.18..1.18 | -0.22..0.32 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Sewer_Grate_02` | 0.01..0.27 | -0.27..0.27 | -0.00..0.36 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Ladder_01` | -0.11..0.11 | -0.48..0.48 | 0.00..2.74 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Fountain_01` | -0.01..0.39 | -0.35..0.36 | -0.31..0.68 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Crystal_01` | -0.43..0.43 | -0.36..0.51 | 0.00..1.99 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Relic_01` | -0.19..0.20 | -0.18..0.18 | 0.00..0.54 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |
| FO | `SM_Prop_Portal_01` | -0.19..0.35 | -0.84..0.86 | -0.01..3.35 | ballista (0.9), cannon, arrow_launcher (0.9), slingshot, boiling_oil, barricade, barricade_low, arrow_basket, cannonball, dial (no lever: user), pipe_wall (recentred), sewer_grate, wall_drain, ladder, wall_font, crystal, relic, portal |

