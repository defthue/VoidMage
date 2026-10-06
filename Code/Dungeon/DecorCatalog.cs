using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The prop catalog, set pieces and per-role fill tables: data, not code. Every extent below was
/// measured from the FBX (tools/DungeonPack_index.md; FBX cm, Y up; s&amp;box local X = fbx Z = the front).
/// Skinned meshes with garbage bounds (skeleton_slave_*, skeleton_table_*, skeleton_knight_throne,
/// sm_item_chr_*, the chandelier and candle_stand _particle meshes) are deliberately absent, as are the
/// weapons and shields (not decor), the vase_group, and the sewer / mine / tech / goblin sets (later
/// variants). sm_env_wall_05 is reserved for the secret room and must never appear in any table here.
///
/// Four layers fill a room: the wall theme and corners (big pieces from the role's EdgeStrip / Corner
/// tables), things ON those pieces (Tops, by parent prop), things BESIDE them (Companions, by parent
/// prop: this is what makes bone piles and candles cluster), and small floor Scatter per role.
///
/// Breakables (crates, barrels, vases) are defined here but flagged Breakable and referenced by no
/// table, set piece, top or companion: they get their own placement system. LeakedBreakables() is the
/// check (run by gentest).
/// </summary>
public static class DecorCatalog
{
	private const string P = "dungeonpack/";
	private static string[] M( params string[] names ) => names.Select( n => P + n + ".vmdl" ).ToArray();
	/// <summary>Dark Fortress models (the shared bones and cobwebs every biome borrows from the gaol's packs).</summary>
	private static string[] FF( params string[] names ) => names.Select( n => "darkfortress/" + n + ".vmdl" ).ToArray();
	private static string[] Range( string stem, int from, int to, string suffix = "" )
		=> Enumerable.Range( from, to - from + 1 ).Select( i => $"{P}{stem}_{i:00}{suffix}.vmdl" ).ToArray();

	/// <summary>The two consumable pickups (DungeonBuilder gives them a PotionPickup; DecorPotions places them).</summary>
	public const string PotionHealId = "potion_heal";
	public const string PotionManaId = "potion_mana";
	/// <summary>The rack a weapon skin can be left in (ChestLoot.RackSkin).</summary>
	public const string WeaponRackId = "weapon_rack";
	/// <summary>The shrine room's altar; DungeonBuilder gives it a <see cref="VoidMage.Dungeon.ManaShrine"/>.</summary>
	public const string AltarId = "altar";

	public const string SecretWallModel = P + "sm_env_wall_05.vmdl";
	/// <summary>Single-sided plain wall, the back of the secret wall.</summary>
	public const string PlainWallModel = P + "sm_env_wall_01.vmdl";
	/// <summary>A flat floor tile (no raised detail) for cells that carry a trap plate.</summary>
	public const string PlainFloorModel = P + "sm_env_tiles_03.vmdl";
	/// <summary>Only the three that fit a 5 m wall (01/02 hang 4.2 m, 03 hangs 2 m).</summary>
	public static readonly string[] Banners = M( "sm_prop_wall_banner_01", "sm_prop_wall_banner_02", "sm_prop_wall_banner_03" );
	public static readonly string[] RuneTiles = Range( "sm_env_tiles_rune", 1, 5 );
	/// <summary>The two root mats, shared by the stone dungeon's `roots` debris and the mossy floors' three
	/// `roots_*` defs. One list so a variant can never be added to one and forgotten in the others.
	/// Declared above PropList on purpose: static initialisers run in textual order.</summary>
	private static readonly string[] RootMats = M( "sm_env_roots_01", "sm_env_roots_02" );

	// ------------------------------------------------------------------ props
	// W x D x H metres; Top = height of a usable surface for small items.

	private static readonly PropDef[] PropList =
	{
		// The run's exit portal: stood in the middle of every kit's exit room (DungeonKit.ExitProp, placed by
		// Decor.PlaceExitProp, given its RunPortal trigger by DungeonBuilder.BuildProps). The Dark Fortress model
		// on purpose: props spawn unthemed, so the one portal reads the same on every floor. Measured in
		// DungeonKit.Gaol.cs (1.7 x 0.55 x 3.36 m, blocks).
		new( "portal", new[] { "darkfortress/sm_prop_portal_01.vmdl" }, AnchorKind.Floor, 1.7f, 0.55f, 3.36f, true ),
		// furniture
		new( "bookcase", Range( "sm_prop_bookcase", 1, 3 ), AnchorKind.WallFlush, 1.3f, 0.95f, 1.5f, true, Top: 1.48f ),
		new( "weapon_rack", M( "sm_prop_weaponrack_01" ), AnchorKind.WallFlush, 2.1f, 0.42f, 1.43f, true, Embed: -0.254f ),   // 10 u off the back wall (user: it clipped)
		new( "bed", M( "sm_prop_bed_01" ), AnchorKind.WallFlush, 1.24f, 2.73f, 2.08f, true, Foot: "chest" ),
		new( "bed_big", M( "sm_prop_bed_02" ), AnchorKind.WallFlush, 2.96f, 3.77f, 2.1f, true, Foot: "chest_wood" ),
		new( "bench", M( "sm_prop_bench_01" ), AnchorKind.Floor, 2.1f, 0.83f, 1.03f, true ),
		// Top = the usable surface measured from the FBX's upward faces, not the model's highest point
		new( "table_long", M( "sm_prop_table_01" ), AnchorKind.Floor, 2.95f, 1.4f, 0.96f, true, Top: 0.93f ),
		new( "table_round", M( "sm_prop_table_round_01" ), AnchorKind.Floor, 1.36f, 1.36f, 0.91f, true, Top: 0.91f ),
		new( "table_low", M( "sm_prop_table_round_02" ), AnchorKind.Floor, 1.04f, 1.04f, 0.36f, true, Top: 0.36f ),
		new( "table_broken", M( "sm_prop_table_round_broken_01" ), AnchorKind.Floor, 1.5f, 1.53f, 0.92f, true ),
		new( "stone_table", M( "sm_prop_stonetable_01" ), AnchorKind.Floor, 2.48f, 1.18f, 0.83f, true, Top: 0.81f ),
		new( "stone_table_broken", M( "sm_prop_stonetable_broken_01" ), AnchorKind.Floor, 3.07f, 1.53f, 0.96f, true ),
		new( "stone_chair", M( "sm_prop_stonechair_01" ), AnchorKind.Floor, 0.57f, 0.55f, 1.11f, false ),
		new( "stool", Range( "sm_prop_stool", 1, 3 ), AnchorKind.Floor, 0.63f, 0.6f, 0.62f, true, Top: 0.62f ),
		new( "throne", M( "sm_env_stone_throne_01" ), AnchorKind.Floor, 1.1f, 1.0f, 1.9f, true ),
		new( "fireplace_wall", M( "sm_env_wall_fireplace_01" ), AnchorKind.WallFlush, 3.6f, 1.55f, 2.4f, true, Embed: 0.5f, Light: LightKind.Brazier, ExteriorOnly: true ),
		// a cold forge: never a flame or a light. Its hood hangs from the ceiling (the flange is the model's top) straight above it.
		new( "forge", M( "sm_prop_forge_firepit_01" ), AnchorKind.Floor, 2.88f, 2.9f, 1.21f, true ),
		new( "forge_pipe", M( "sm_prop_forge_ventilation_pipe_01" ), AnchorKind.CeilingHung, 1.35f, 1.35f, 1.76f, false, MountHeight: 1.76f ),
		new( "fountain", M( "sm_env_stone_fountain_01" ), AnchorKind.WallFlush, 1.04f, 0.63f, 1.32f, true ),
		// rug_03 has a fold that swallows table legs and stools: under furniture only the flat rugs go
		new( "rug", M( "sm_prop_rug_03" ), AnchorKind.Floor, 4.5f, 3.0f, 0.26f, false, Lift: 0.01f, Overlay: true ),
		new( "rug_flat", M( "sm_prop_rug_01", "sm_prop_rug_02" ), AnchorKind.Floor, 4.5f, 3.0f, 0.1f, false, Lift: 0.01f, Overlay: true ),
		// containers; the breakables are placed by DecorBreakables (after the fill) and never appear in a table. Their
		// variants are the ones with a debris kit (tools/gen_breakable_kits.py): crate_wood_01 and barrel_02 are out,
		// barrel_broken_01 (a damaged but whole barrel) is a third barrel, and no Decay: a broken variant cannot break.
		new( "crate", Range( "sm_prop_crate_wood", 2, 5 ), AnchorKind.Floor, 1.15f, 1.05f, 1.1f, true, Top: 1.05f, Breakable: true ),
		new( "crate_metal", M( "sm_prop_crate_metal_01", "sm_prop_crate_metal_02", "sm_prop_crate_metal_03", "sm_prop_crate_metal_open_01" ), AnchorKind.Floor, 1.05f, 1.05f, 1.05f, true, Top: 1.05f, Breakable: true ),
		new( "crate_ornate", M( "sm_prop_crate_ornate_01" ), AnchorKind.Floor, 1.01f, 1.02f, 1.02f, true, Top: 1.02f, Breakable: true ),
		new( "barrel", M( "sm_prop_barrel_01", "sm_prop_barrel_03", "sm_prop_barrel_broken_01" ), AnchorKind.Floor, 0.77f, 0.77f, 1.05f, true, Top: 1.03f, Breakable: true ),
		new( "barrel_broken", M( "sm_prop_barrel_broken_01", "sm_prop_barrel_broken_02" ), AnchorKind.Floor, 1.2f, 1.2f, 0.8f, false ),
		new( "vat", M( "sm_prop_barrel_large_01" ), AnchorKind.Floor, 3.44f, 4.66f, 3.82f, true ),
		new( "vase", Range( "sm_prop_vase", 1, 9 ), AnchorKind.Floor, 0.6f, 0.55f, 0.8f, false, Breakable: true ),
		new( "vase_broken", Range( "sm_prop_vase_broken", 1, 3 ), AnchorKind.Floor, 0.6f, 0.55f, 0.5f, false ),
		new( "shard", Range( "sm_prop_vase_shard", 1, 4 ), AnchorKind.Floor, 0.3f, 0.3f, 0.1f, false ),
		// (the DungeonPack jars sm_item_jar_01/02 are out of the dungeon: user 2026-09-15, with the decorative potions)
		new( "chest", Range( "sm_prop_chest", 1, 4 ), AnchorKind.Floor, 0.85f, 0.57f, 0.34f, true, Attach: Range( "sm_prop_chest", 1, 4, "_lid" ) ),
		new( "chest_wood", M( "sm_prop_chest_wood_01" ), AnchorKind.Floor, 1.16f, 0.81f, 0.47f, true, Attach: M( "sm_prop_chest_wood_lid" ) ),
		new( "vessel", M( "sm_env_stone_vessel_01" ), AnchorKind.Floor, 1.44f, 1.25f, 1.35f, true ),
		// crypt
		new( "coffin", M( "sm_prop_coffin_01" ), AnchorKind.Floor, 1.1f, 2.33f, 0.48f, true, Attach: M( "sm_prop_coffin_lid_01" ), Top: 0.56f ),
		new( "coffin_open", M( "sm_prop_coffin_01" ), AnchorKind.Floor, 1.1f, 2.33f, 0.48f, true ),
		new( "bone_pile", Range( "sm_env_bonepile", 1, 3 ), AnchorKind.Floor, 2.5f, 2.8f, 0.9f, false ),
		new( "bone_pile_small", M( "sm_env_bonepile_small_01", "sm_env_bonepile_small_02" ), AnchorKind.Floor, 1.2f, 1.2f, 0.3f, false ),
		new( "skeleton", M( "sm_prop_skeleton_01" ), AnchorKind.Floor, 0.7f, 1.9f, 0.35f, false ),
		// the cage's pivot is 28 cm above its floor and 1.42 m below its top; hung from a short chain (0.96 m)
		// (user-measured against the chain: 7 in forward, 10 in up from the chain's end)
		new( "skeleton_cage", M( "sm_prop_skeleton_cage_01" ), AnchorKind.CeilingHung, 0.8f, 0.5f, 1.7f, false, MountHeight: 2.156f, Hanger: "chain_short", Nudge: 0.178f ),
		new( "chain", M( "sm_prop_chain_01" ), AnchorKind.CeilingHung, 0.1f, 0.1f, 2.5f, false ),
		new( "chain_short", M( "sm_prop_chain_02" ), AnchorKind.CeilingHung, 0.1f, 0.1f, 0.96f, false ),
		// skeletons in shackles on the wall (skinned meshes: if the engine bounds come out huge the builder skips them and logs it)
		// flush: the model's back face lands exactly on the wall face (Embed 0)
		new( "skeleton_hung", M( "sm_prop_skeleton_slave_shackles_01" ), AnchorKind.WallMounted, 0.83f, 1.1f, 1.72f, false, MountHeight: 0.25f ),
		new( "skeleton_shackled", M( "sm_prop_skeleton_slave_shackles_02" ), AnchorKind.WallMounted, 0.7f, 0.4f, 2.7f, false, MountHeight: 0.05f ),
		// shrine. NO Top: the altar is the mana shrine the player walks up to and uses (VoidMage.Dungeon.ManaShrine),
		// so nothing may be left standing on it -- a Top above 0 is an invitation for the surface pass, the breakables
		// pass and the set pieces to put clutter there (user, 2026-09-18).
		new( "altar", M( "sm_env_alter_01" ), AnchorKind.Floor, 3.36f, 2.47f, 2.74f, true ),
		// the glowing orbs (sm_env_glowingorb_01..05) are out of the dungeon entirely. They read as out of place
		// in the stone rooms (user, 2026-09-07) and were tried on the mossy floors, where the user did not want
		// them either (2026-09-11). LightKind.Glow is still live in the builder but nothing in the tables uses it
		// now that the decorative potions are gone.
		new( "obelisk", M( "sm_env_obelisk_01", "sm_env_obelisk_02" ), AnchorKind.Floor, 1.3f, 2.2f, 3.6f, true ),
		new( "statue", M( "sm_env_statue_02", "sm_env_statue_03" ), AnchorKind.Floor, 1.5f, 1.4f, 3.3f, true ),
		new( "rune_pillar", Range( "sm_env_rune_pillar", 1, 5 ), AnchorKind.Floor, 1.0f, 1.0f, 2.4f, true ),
		new( "rune_block", Range( "sm_env_rune_square", 1, 5 ).Concat( Range( "sm_env_rune_rounded", 1, 5 ) ).ToArray(), AnchorKind.Floor, 1.0f, 1.05f, 0.28f, false ),
		new( "pentagram", M( "sm_env_tiles_pentagram_01" ), AnchorKind.Floor, 6.5f, 6.5f, 0.2f, false, Lift: 0.005f, Overlay: true ),
		// torture
		new( "stocks", M( "sm_prop_toture_stocks_01" ), AnchorKind.Floor, 1.72f, 1.25f, 1.25f, true ),
		new( "iron_maiden", M( "sm_prop_toture_ironmaiden_01" ), AnchorKind.Floor, 1.85f, 1.27f, 2.78f, true, Embed: -0.3f ),   // stands 30 cm further off the wall
		new( "cage", M( "sm_prop_toture_cage_01" ), AnchorKind.Floor, 0.96f, 0.96f, 2.0f, true ),
		new( "stretch_table", M( "sm_prop_toture_stretchtable_01" ), AnchorKind.Floor, 2.15f, 3.7f, 2.35f, true ),
		// alchemy / library items
		new( "cauldron", M( "sm_prop_cauldron_01" ), AnchorKind.Floor, 0.9f, 1.06f, 0.8f, true ),
		new( "book_pile", M( "sm_item_bookpile_01" ), AnchorKind.Floor, 0.37f, 0.35f, 0.17f, false ),
		new( "book", M( "sm_item_book_01" ), AnchorKind.Floor, 0.3f, 0.25f, 0.05f, false ),
		new( "inkwell", M( "sm_item_inkwell_01" ), AnchorKind.Floor, 0.15f, 0.25f, 0.28f, false ),
		new( "scale", M( "sm_item_scale_01" ), AnchorKind.Floor, 0.65f, 0.2f, 0.64f, false ),
		// vials and the potion pole stay alchemy-room dressing; the decorative potions (sm_item_potion_01..09) and
		// every coin pile are gone (user 2026-09-15). Potions 06 and 07 are the two PICKUPS below, placed only by
		// DecorPotions (a per-floor budget on tables and inside chests), never by a table: referenced by no table here on purpose.
		// Vials 02 and 03 are out too (user 2026-09-16): only vial 01 dresses the alchemy room.
		new( "potion_pole", M( "sm_item_potion_pole_01" ), AnchorKind.Floor, 0.6f, 0.32f, 1.62f, false, RoleOnly: DecorRole.Alchemy ),
		new( "vial", M( "sm_item_vial_01" ), AnchorKind.Floor, 0.2f, 0.2f, 0.4f, false, RoleOnly: DecorRole.Alchemy ),
		// Bury 0: stood on its lowest point, not its pivot. Potion 06's pivot is in the middle of the bottle, so with the
		// default half-bedded Bury the mana potion sank halfway into the table (user 2026-09-16); 07's is at its base.
		new( PotionHealId, M( "sm_item_potion_07" ), AnchorKind.Floor, 0.2f, 0.2f, 0.4f, false, Bury: 0f ),
		new( PotionManaId, M( "sm_item_potion_06" ), AnchorKind.Floor, 0.2f, 0.2f, 0.4f, false, Bury: 0f ),
		// lights
		new( "torch", M( "sm_prop_torch_ornate_02" ), AnchorKind.WallMounted, 0.3f, 0.68f, 0.9f, false, MountHeight: 1.7f, Light: LightKind.Torch, Embed: 0.217f ),
		new( "brazier", M( "sm_prop_brazier_01" ), AnchorKind.Floor, 1.12f, 1.0f, 0.65f, true, Light: LightKind.Brazier ),
		new( "bonfire", M( "sm_prop_bonfire_01" ), AnchorKind.Floor, 1.55f, 1.82f, 1.04f, false, Light: LightKind.Brazier ),
		new( "candle_stand", M( "sm_prop_candle_stand_01" ), AnchorKind.Floor, 0.7f, 0.77f, 1.75f, false, Light: LightKind.Candle ),
		// candles melt 10 cm into whatever they stand on (user-measured on tables and coffins)
		new( "candles", Range( "sm_prop_candles", 1, 4 ), AnchorKind.Floor, 0.25f, 0.25f, 0.4f, false, Attach: Range( "sm_prop_candles", 1, 4, "_particle" ), Light: LightKind.Candle, TopSink: 0.1f ),
		new( "candle", M( "sm_prop_candle_01", "sm_prop_candle_02" ), AnchorKind.Floor, 0.2f, 0.2f, 0.4f, false, Light: LightKind.Candle, TopSink: 0.1f ),
		new( "stone_lantern", M( "sm_env_stone_lantern_01" ), AnchorKind.Floor, 0.76f, 0.66f, 1.91f, true, Light: LightKind.Candle ),
		new( "chandelier", M( "sm_prop_candle_chandelier_01", "sm_prop_candle_chandelier_02" ), AnchorKind.CeilingHung, 1.2f, 1.2f, 2.3f, false, Light: LightKind.Chandelier ),
		new( "chandelier_broken", M( "sm_prop_candle_chandelier_02_broken" ), AnchorKind.CeilingHung, 2.4f, 2.4f, 3.3f, false ),
		new( "banner", Banners, AnchorKind.WallMounted, 2.0f, 0.2f, 4.2f, false, MountHeight: 4.6f ),
		// clutter / decay
		new( "rubble", Range( "sm_env_rubble", 1, 3 ), AnchorKind.Floor, 4.5f, 4.5f, 1.6f, false ),
		new( "pebbles", Range( "sm_env_rubble_pebbles", 1, 3 ), AnchorKind.Floor, 1.5f, 1.2f, 0.2f, false, Sink: 0.02f ),
		new( "bricks", Range( "sm_env_brick_rubble", 1, 6 ), AnchorKind.Floor, 0.8f, 0.6f, 0.4f, false ),
		new( "brick", M( "sm_prop_brick_01", "sm_prop_brick_02" ), AnchorKind.Floor, 0.7f, 0.33f, 0.42f, false ),
		new( "plank", Range( "sm_env_rubble_plank", 1, 3 ), AnchorKind.Floor, 2.5f, 1.8f, 0.15f, false ),
		new( "pillar_pile", M( "sm_env_pillar_broken_pile_01", "sm_env_pillar_broken_pile_02" ), AnchorKind.Floor, 1.5f, 1.5f, 1.2f, true ),
		new( "roots", RootMats, AnchorKind.Floor, 1.0f, 1.1f, 0.2f, false ),
		// mushroom_small_04 is modelled with 40 cm of its 80 cm below the pivot, so half of every one of these
		// clusters used to be underground on EVERY floor. Bury 0 stands them up; _01..._03 sit on their pivots
		// already and do not move (user, 2026-09-11: fix it everywhere, not just on the mossy floors).
		new( "mushrooms", Range( "sm_env_mushroom_small", 1, 4 ), AnchorKind.Floor, 0.6f, 0.7f, 1.0f, false, Bury: 0f ),
		// ---- the shared bones (DecorBoneLitter): every biome has skeletons walking it, so every floor gets a quarter of
		// the gaol's bone carpet (user). Own ids, so the gaol's `skull`/`bone`/`limb` overrides never shadow them. The
		// Dark Fortress pieces are the gaol's own, measured in tools/Gaol_index.md.
		new( "litter_skull", FF( "sm_prop_skull_01", "sm_prop_skull_02", "sm_prop_skull_03", "sm_prop_skull_04", "sm_prop_skull_05", "sm_prop_skull_06" ), AnchorKind.Floor, 0.45f, 0.45f, 0.35f, false ),
		new( "litter_bone", FF( "sm_prop_bone_01", "sm_prop_bone_02", "sm_prop_bone_03" ), AnchorKind.Floor, 0.45f, 0.5f, 0.3f, false ),
		new( "litter_limb", FF( "sm_prop_limb_01", "sm_prop_limb_02", "sm_prop_limb_03", "sm_prop_limb_04" ), AnchorKind.Floor, 0.4f, 0.8f, 0.25f, false ),
		// The pack's GIANT creature bones (a 12 m skull, a 30 m tail) shrunk to a dog's or a goat's (user: small creature
		// sized). One def per scale; extents post-scale from the FBX. Several are pivoted mid-height, so Bury 0 stands
		// the whole bone on the floor. Horns, spikes and the standing ribs are left out: upright, they read as stakes.
		//   skull_01 11.7 x 9.3 x 7.6, skull_02 11.7 x 9.0 x 7.6, jaw_01 8.5 x 4.1 x 2.7 m  @ 0.045
		new( "critter_skull", M( "sm_env_bone_skull_01", "sm_env_bone_skull_02", "sm_env_bone_skull_jaw_01" ), AnchorKind.Floor, 0.42f, 0.53f, 0.35f, false, Bury: 0f, Scale: 0.045f ),
		//   ribcage_01 14.7 x 9.6 x 8.5, ribs_01 6.1 x 14.7 x 8.2 m  @ 0.05
		new( "critter_ribs", M( "sm_env_bone_ribcage_01", "sm_env_bone_ribs_01" ), AnchorKind.Floor, 0.74f, 0.74f, 0.43f, false, Bury: 0f, Scale: 0.05f ),
		//   spine_01 26.2 x 6.6 x 6.5, tail_01 30.5 x 7.2 x 7.2 m  @ 0.03
		new( "critter_spine", M( "sm_env_bone_spine_01", "sm_env_bone_tail_01" ), AnchorKind.Floor, 0.22f, 0.92f, 0.22f, false, Bury: 0f, Scale: 0.03f ),
		// ---- cobwebs in the ceiling corners of every biome (DecorWebs). Fortress Spiderweb_01 is a triangular sheet
		// strung between two walls, its corner edge on the pivot, rising 0.44 m (z -0.28..0.44, x -0.76..1.31,
		// y -0.99..1.37; rendered to check). Tilt 180 hangs it from the top of the corner and Turn 180 swings the sheet
		// back out of the wall: it then reaches 0.28 m above its mount, out into the room along +X and along the wall
		// to the RIGHT, so DecorWebs mounts it at the LEFT end of a wall. The bits past the pivot sink into the walls.
		new( "web_corner_high", FF( "sm_env_spiderweb_01" ), AnchorKind.WallMounted, 2.4f, 2.1f, 0.72f, false, Tilt: 180f, Turn: 180f ),
		// ---- greenery: the mossy floors only (DecorGreenery). Extents measured per variant with
		// tools/fbx_shape.py, taking the largest of each family. Everything here is deliberately non-blocking
		// so it spawns with no collider at all and you walk straight through it; the trees are the exception
		// and take a capsule (see DungeonBuilder.CapsuleProps).
		new( "flowers", M( "sm_env_flowers_01", "sm_env_flowers_02" ), AnchorKind.Floor, 0.6f, 0.72f, 0.61f, false, Bury: 0f ),
		// The root mats: the pack's only vine WITHOUT spikes, and the backbone of the mossy look. Modelled as a
		// flat horizontal splay 85 x 91 cm across and 19 cm thick, with 14.2 cm of that hanging below the pivot —
		// which is why they were all but invisible before Bury existed, showing 4.6 cm above the flagstones.
		// Three defs over the same two models so one mat can lie on the floor, one on a wall and one on a slab;
		// the plain "roots" above stays exactly as it was for the stone dungeon's debris and decay tables.
		// Bury 0 puts the mat exactly on the floor; DecorGreenery then sinks each one an explicit 2-3 units.
		// Doing it that way round keeps the depth constant — Bury is a fraction of the mat's own 14.2 cm and
		// scales with the size roll, so a big mat would have bedded in deeper than a small one.
		new( "roots_green", RootMats, AnchorKind.Floor, 1.0f, 1.1f, 0.2f, false, Bury: 0f, Overlay: true ),
		// Tilt 90 stands the mat up against the wall with its textured face pointing into the room (-90 would
		// turn that face into the masonry). Its rotated bounds are x -0.142..0.046 and z -0.252..0.662 m, so it
		// reaches 0.662*scale ABOVE its mount and 0.252*scale below. DecorGreenery mounts it in two bands, low
		// against the floor and a few just under the ceiling, and computes the high one from the rolled scale.
		new( "roots_wall", RootMats, AnchorKind.WallMounted, 1.0f, 1.1f, 0.2f, false, MountHeight: 0.25f, Tilt: 90f ),
		// Tilt 180 turns it upside down under the slab; the mat then reaches 14.2 cm ABOVE its pivot, so hanging
		// it that far below the ceiling tucks it flush against the stone.
		new( "roots_ceiling", RootMats, AnchorKind.CeilingHung, 1.0f, 1.1f, 0.2f, false, MountHeight: 0.142f, Tilt: 180f ),
		new( "grass", M( "sm_env_grass_03" ), AnchorKind.Floor, 1.11f, 1.29f, 1.16f, false ),
		// grass_01/02 are 3.4 m across, which makes them Tile: they need a whole cell, and one def mixing them
		// with grass_03 would either be rejected everywhere or clip the big ones through the walls
		new( "grass_wide", M( "sm_env_grass_01", "sm_env_grass_02" ), AnchorKind.Floor, 3.42f, 2.84f, 0.98f, false ),
		// The moss blanket is mid-pivoted: 80 cm of it hangs BELOW the pivot and 40 cm above. On a wall that
		// makes it a hanging sheet (spans 0.8-2.0 m up); flat on the floor the buried 80 cm is exactly what is
		// wanted, leaving a 40 cm mossy lump. No Lift — lifting it stands the sheet on end.
		new( "moss_patch", M( "sm_env_moss_patch_01" ), AnchorKind.WallMounted, 1.57f, 0.84f, 1.2f, false, MountHeight: 1.6f ),
		new( "moss_floor", M( "sm_env_moss_patch_01" ), AnchorKind.Floor, 1.57f, 0.84f, 1.2f, false ),
		// "giant" is Synty's name, not ours: at 1.9-3.3 m they read as trees. Capped to knee height (user).
		new( "mushroom_giant", Range( "sm_env_mushroom_giant", 1, 5 ), AnchorKind.Floor, 1.76f, 1.49f, 3.34f, false, MaxHeight: 0.5f ),
		// wall brackets: the back plate sits on the pivot plane, so Embed 0 puts them flush like every other wall prop
		new( "mushroom_wall", Range( "sm_env_mushroom_wall", 1, 3 ), AnchorKind.WallMounted, 1.42f, 1.18f, 1.21f, false, MountHeight: 1.6f ),
		// the only ceiling-pivoted piece in the pack: 5.03 m of it hangs below the pivot, which is the whole
		// wall height, so it is cut back to a dangling strand here. The breach curtains use it at full length.
		new( "vine_hang", M( "sm_env_vine_spikes_03" ), AnchorKind.CeilingHung, 1.15f, 1.73f, 5.44f, false, MountHeight: 0.41f, MaxHeight: 2.6f ),
		// The trailing vines run 2.4-2.9 m FORWARD of their pivot, so they belong against a wall pointing into
		// the room, which is how WallFlush places them: back on the wall, creeping out across the floor. As a
		// free-standing floor prop with a random yaw they mostly grew out through the nearest wall.
		new( "vine_ground", M( "sm_env_vine_spikes_01", "sm_env_vine_spikes_02" ), AnchorKind.WallFlush, 0.98f, 3.21f, 1.13f, false ),
		// 3.9-5.8 m tall in the pack and the ceiling is under 5 m, so they are capped to fit; the cap shrinks
		// the capsule with it, which lands the trunk collider at a sensible size
		new( "tree", M( "sm_env_tree_twisted_01", "sm_env_tree_twisted_02" ), AnchorKind.Floor, 3.24f, 2.74f, 5.78f, false, MaxHeight: 4.6f ),
		// The dandelions are their own def purely so they can be capped to half the twisted trees' height
		// (user, 2026-09-11). They are 4.7-8.5 m in the pack, so all three come down a long way.
		new( "tree_dandelion", M( "sm_env_tree_dandelion_01", "sm_env_tree_dandelion_02", "sm_env_tree_dandelion_03" ),
			AnchorKind.Floor, 2.21f, 3.4f, 8.5f, false, MaxHeight: 2.3f ),
	};

	public static readonly Dictionary<string, PropDef> Props = PropList.ToDictionary( p => p.Id );

	/// <summary>
	/// Per-model corrections to a prop's Top surface, metres: how far the usable surface sits below the
	/// def's Top and how far back toward the model's rear. Bookcase_01's top shelf is recessed (measured
	/// in-engine: 13 in down, 6 in back).
	/// </summary>
	public static readonly Dictionary<string, (float drop, float back)> TopFix = new()
	{
		[P + "sm_prop_bookcase_01.vmdl"] = (0.330f, 0.152f),   // user-measured in-engine
		[P + "sm_prop_bookcase_02.vmdl"] = (0.280f, 0.150f),   // top shelf at 1.20 m
		[P + "sm_prop_bookcase_03.vmdl"] = (0.370f, 0.100f),   // top shelf at 1.11 m
		[P + "sm_prop_stool_02.vmdl"] = (-0.130f, 0f),         // taller stool, seat at 0.75 m
		[P + "sm_prop_stool_03.vmdl"] = (0.170f, 0f),          // squat stool, seat at 0.45 m
	};

	// ------------------------------------------------------------------ wall swaps

	/// <summary>
	/// A wall piece that replaces a plain segment. Behind is how far it reaches behind the wall plane
	/// (metres). Shared pieces are see-through and go only on a wall with a room on the other side, placed
	/// on both faces; the rest recess into rock and need nothing behind the wall.
	/// </summary>
	public sealed record WallSwapDef( string Id, string Model, string Insert, bool Centred, bool InsertCentred, bool MeshCollider, float Behind, string Item = null, bool Shared = false, float Setback = 0 )
	{
		/// <summary>Reaches far enough behind the wall plane to need a floor and a ceiling in the rock behind it.</summary>
		public bool Recessed => Behind >= 0.5f;
	}

	public static readonly Dictionary<string, WallSwapDef> WallSwaps = new WallSwapDef[]
	{
		// recessed pieces (rock behind the wall): the alcoves and the deep-set Wall_02
		new( "alcove", P + "sm_env_wall_alcove_small_01.vmdl", null, false, false, true, 1.5f, "candles" ),
		new( "alcove_2", P + "sm_env_wall_alcove_small_02.vmdl", null, false, false, true, 1.24f, "bone_pile_small" ),
		// pivot in the middle of the recess, not on the wall plane: set 40 in back so its face meets the neighbours
		new( "alcove_round", P + "sm_env_wall_alcove_round_01.vmdl", null, true, false, true, 1.38f, Setback: 1.016f ),
		new( "wall_deep", P + "sm_env_wall_02.vmdl", null, false, false, true, 0.98f ),
		new( "sealed_door", P + "sm_env_wall_doorframe_01.vmdl", P + "sm_env_wall_rock_blocked_01.vmdl", false, true, true, 1.7f ),
		// flat single-sided variants of the plain wall, for outside walls (nothing behind to see the back)
		new( "wall_alt", P + "sm_env_wall_01_alt.vmdl", null, false, false, false, 0.15f ),
		new( "wall_3", P + "sm_env_wall_03.vmdl", null, false, false, false, 0.16f ),
		new( "wall_4", P + "sm_env_wall_04.vmdl", null, false, false, false, 0.16f ),
		// see-through pieces: only between two rooms, placed on both faces
		new( "window", P + "sm_env_wall_window_01.vmdl", P + "sm_env_wall_window_bars_01.vmdl", false, false, true, 0.12f, Shared: true ),
		new( "window_2", P + "sm_env_wall_window_02.vmdl", P + "sm_env_wall_window_bars_02.vmdl", false, false, true, 0.25f, Shared: true ),
	}.ToDictionary( w => w.Id );

	/// <summary>The plain-wall variety every role mixes into its wall swap list.</summary>
	private static readonly FillEntry[] WallVariety = E( ("wall_alt", 18), ("wall_3", 18), ("wall_4", 18), ("wall_deep", 10) );

	// ------------------------------------------------------------------ fill helpers

	private static FillEntry[] E( params (string id, double w)[] items ) => items.Select( i => new FillEntry( i.id, i.w ) ).ToArray();
	private static FillEntry[] S( params (string id, double w)[] items ) => items.Select( i => new FillEntry( i.id, i.w, true ) ).ToArray();
	private static FillEntry[] Mix( FillEntry[] a, FillEntry[] b ) => a.Concat( b ).ToArray();

	// ------------------------------------------------------------------ layers 2 and 3: on and beside props
	// keyed by the parent prop id; "" = nothing

	/// <summary>Small items placed on a prop's Top surface.</summary>
	public static readonly Dictionary<string, FillEntry[]> Tops = new()
	{
		// vials are alchemy-only (RoleOnly on the def drops them anywhere else)
		["table_long"] = E( ("candles", 30), ("book_pile", 15), ("book", 10), ("candle", 10), ("vial", 5), ("", 30) ),
		["table_round"] = E( ("candles", 35), ("book_pile", 20), ("inkwell", 10), ("candle", 10), ("book", 5), ("", 20) ),
		["table_low"] = E( ("candles", 40), ("candle", 25), ("book", 10), ("", 25) ),
		["stone_table"] = E( ("candles", 30), ("candle", 20), ("scale", 10), ("book_pile", 10), ("", 30) ),
		["stool"] = E( ("candles", 40), ("book", 25), ("", 35) ),
		["bookcase"] = E( ("candles", 35), ("book_pile", 30), ("candle", 10), ("book", 5), ("", 20) ),
		["coffin"] = E( ("candles", 40), ("candle", 30), ("book", 10), ("", 20) ),
		["chest"] = E( ("candles", 25), ("book", 10), ("", 65) ),
	};

	/// <summary>Things that stand beside a prop, inside the space it already claimed. This is what makes clusters.</summary>
	public static readonly Dictionary<string, FillEntry[]> Companions = new()
	{
		["bookcase"] = E( ("book_pile", 35), ("candles", 15), ("stool", 10), ("", 40) ),
		["weapon_rack"] = E( ("chest", 15), ("", 75) ),
		["bed"] = E( ("stool", 30), ("candle_stand", 10), ("", 60) ),
		["bed_big"] = E( ("stool", 30), ("candle_stand", 25), ("", 45) ),
		["coffin"] = E( ("candles", 30), ("bone_pile_small", 25), ("bone_pile_small", 20), ("", 25) ),
		["coffin_open"] = E( ("bone_pile_small", 30), ("bone_pile_small", 30), ("candles", 20), ("", 20) ),
		["bone_pile"] = E( ("bone_pile_small", 40), ("candles", 15), ("", 45) ),
		["cage"] = E( ("bone_pile_small", 30), ("bone_pile_small", 25), ("brazier", 10), ("", 35) ),
		["iron_maiden"] = E( ("bone_pile_small", 25), ("candles", 20), ("bone_pile_small", 15), ("", 40) ),
		["stocks"] = E( ("bone_pile_small", 30), ("bone_pile_small", 20), ("", 50) ),
		["cauldron"] = E( ("potion_pole", 25), ("book_pile", 15), ("stool", 10), ("", 50) ),
		["statue"] = E( ("candles", 35), ("candle", 15), ("", 50) ),
		["obelisk"] = E( ("candles", 35), ("rune_block", 20), ("", 45) ),
		["rune_pillar"] = E( ("candles", 40), ("rune_block", 20), ("", 40) ),
		["candle_stand"] = E( ("book_pile", 20), ("", 80) ),
		["stone_table"] = E( ("stone_chair", 40), ("stool", 20), ("", 40) ),
		["throne"] = E( ("candle_stand", 30), ("", 70) ),
		["vessel"] = E( ("candles", 30), ("bone_pile_small", 20), ("", 50) ),
	};

	// ------------------------------------------------------------------ set pieces
	// U forward (toward the room's entrance), V right, in cell units (0.2 = 1 m); H metres up for table tops.

	private static readonly SetPiece[] SetList =
	{
		new( "table_reading", 1, 1, new SetItem[]
		{
			new( "rug_flat", 0, 0, 0, 0.3 ), new( "table_round", 0, 0, 0 ), new( "stool", 0, 0.22f, -90 ), new( "stool", 0, -0.22f, 90, 0.7 ),
			new( "book_pile", 0.04f, 0.05f, 20, 1, 0.91f ), new( "inkwell", -0.05f, 0.07f, 0, 0.8, 0.91f ),
			new( "candles", 0.03f, -0.08f, 0, 1, 0.91f ), new( "book", -0.06f, -0.04f, 70, 0.6, 0.91f ),
			new( "candle_stand", 0.28f, 0.28f, 0, 0.5 ), new( "book_pile", -0.3f, 0.2f, 40, 0.5 ),
		} ),
		new( "table_study", 1, 1, new SetItem[]
		{
			new( "table_long", 0, 0, 0 ), new( "stool", 0.25f, 0.15f, 180 ), new( "stool", 0.25f, -0.18f, 180, 0.7 ), new( "stool", -0.25f, 0, 0, 0.7 ),
			new( "book_pile", 0.04f, 0.18f, 10, 1, 0.93f ), new( "book_pile", -0.05f, -0.15f, 80, 0.7, 0.93f ), new( "book", 0.02f, 0.02f, 30, 0.8, 0.93f ),
			new( "candles", -0.04f, 0.1f, 0, 1, 0.93f ), new( "candles", 0.05f, -0.22f, 0, 0.6, 0.93f ), new( "inkwell", 0, -0.05f, 0, 0.7, 0.93f ),
			new( "scale", -0.06f, 0.24f, 90, 0.4, 0.93f ), new( "book_pile", 0.3f, 0.32f, 15, 0.5 ), new( "candle_stand", -0.3f, -0.32f, 0, 0.5 ),
		} ),
		new( "table_feast", 1, 1, new SetItem[]
		{
			new( "table_long", 0, 0, 0 ), new( "bench", 0.25f, 0, 180 ), new( "bench", -0.25f, 0, 0 ),
			new( "candles", 0, -0.2f, 0, 1, 0.93f ), new( "candles", 0, 0, 0, 1, 0.93f ), new( "candles", 0, 0.2f, 0, 0.8, 0.93f ),
			new( "book", 0.06f, -0.06f, 20, 0.4, 0.93f ),
			new( "candle", -0.04f, 0.26f, 30, 0.3, 0.93f ), new( "stool", 0, 0.38f, -90, 0.6 ), new( "stool", 0, -0.38f, 90, 0.6 ),
		} ),
		// the alchemist's bench: vials, the scale and the books (the decorative potions and jars are gone)
		new( "table_potions", 1, 1, new SetItem[]
		{
			new( "table_long", 0, 0, 0 ), new( "stool", 0.25f, 0.1f, 180, 0.8 ),
			new( "vial", 0.03f, -0.18f, 0, 1, 0.93f ), new( "vial", -0.04f, -0.08f, 0, 0.8, 0.93f ), new( "book_pile", 0.05f, 0.02f, 30, 0.8, 0.93f ),
			new( "vial", -0.03f, 0.1f, 0, 0.8, 0.93f ), new( "vial", 0.04f, 0.15f, 0, 0.6, 0.93f ), new( "inkwell", -0.05f, 0.2f, 0, 0.7, 0.93f ),
			new( "book", 0, -0.02f, 40, 0.6, 0.93f ), new( "candles", -0.05f, 0.03f, 0, 0.8, 0.93f ), new( "scale", 0.04f, 0.25f, 0, 0.5, 0.93f ),
			new( "potion_pole", -0.3f, 0.3f, 0, 0.6 ), new( "cauldron", -0.3f, -0.3f, 0, 0.4 ),
		} ),
		new( "altar_small", 1, 1, new SetItem[]
		{
			// everything here stands on the FLOOR around the altar; the book that used to lie on its top (H 1.32)
			// is gone with the altar's Top, because the shrine's surface has to stay clear
			new( "altar", 0, 0, 0 ), new( "candles", 0.28f, 0.3f, 0 ), new( "candles", 0.28f, -0.3f, 0 ),
			new( "candles", -0.28f, 0.3f, 0, 0.7 ), new( "candles", -0.28f, -0.3f, 0, 0.7 ),
			new( "bone_pile_small", 0.3f, 0, 0, 0.4 ),
		}, FloorTile: "rune" ),
		new( "altar_grand", 2, 2, new SetItem[]
		{
			new( "pentagram", 0, 0, 0 ), new( "altar", 0, 0, 0 ),
			new( "candle_stand", 0.4f, 0.4f, 0 ), new( "candle_stand", 0.4f, -0.4f, 0 ), new( "candle_stand", -0.4f, 0.4f, 0 ), new( "candle_stand", -0.4f, -0.4f, 0 ),
			new( "rune_block", 0.55f, 0.1f, 0, 0.6 ), new( "rune_block", 0.55f, -0.15f, 0, 0.6 ),
			new( "candles", 0.3f, 0, 0, 0.7 ), new( "bone_pile_small", 0.35f, 0.18f, 20, 0.4 ),
		} ),
		new( "coffin_open", 1, 1, new SetItem[]
		{
			new( "coffin_open", 0, 0, 0 ), new( "bone_pile_small", 0.15f, 0.2f, 0, 0.8 ), new( "candles", -0.12f, -0.18f, 0 ),
			new( "bone_pile_small", 0.2f, -0.12f, 40, 0.6 ), new( "candles", 0.3f, 0.05f, 0, 0.5 ),
		} ),
		new( "coffin_row", 1, 1, new SetItem[]
		{
			new( "coffin", 0, -0.28f, 0 ), new( "coffin_open", 0, 0, 0, 0.7 ), new( "coffin", 0, 0.28f, 0 ), new( "candles", 0.3f, 0, 0, 0.6 ),
			new( "bone_pile_small", 0.32f, -0.2f, 60, 0.5 ), new( "candles", -0.3f, 0.15f, 0, 0.5 ),
		} ),
		new( "cauldron_brew", 1, 1, new SetItem[]
		{
			new( "cauldron", 0, 0, 0 ), new( "book_pile", -0.15f, 0.15f, 20, 0.7 ), new( "candles", 0.18f, 0.1f, 0, 0.8 ), new( "book_pile", 0.2f, -0.12f, 40, 0.6 ),
			new( "potion_pole", -0.3f, 0.3f, 0, 0.5 ), new( "stool", 0.3f, 0.3f, -120, 0.5 ),
		} ),
		// a knocked-over experiment: vials among the shards (the decorative potions and jars are gone)
		new( "spilled_potions", 1, 1, new SetItem[]
		{
			new( "vial", 0.15f, 0.1f, 0 ), new( "shard", 0.2f, -0.05f, 0, 0.8 ),
			new( "vial", -0.12f, 0.18f, 0, 0.7 ), new( "vial", 0.1f, 0.2f, 0, 0.6 ), new( "book", -0.18f, -0.1f, 50, 0.5 ), new( "shard", 0.25f, 0.2f, 0, 0.6 ),
		} ),
		new( "stocks_skeleton", 1, 1, new SetItem[]
		{
			new( "stocks", 0, 0, 0 ), new( "skeleton", 0.25f, 0.12f, 90, 0.8 ), new( "bone_pile_small", -0.2f, -0.15f, 0, 0.6 ), new( "bone_pile_small", -0.18f, 0.2f, 0, 0.5 ),
			new( "brazier", 0.3f, -0.3f, 0, 0.5 ),
		} ),
		new( "stretch_table", 1, 1, new SetItem[]
		{
			new( "stretch_table", 0, 0, 90 ), new( "brazier", 0.3f, 0.3f, 0, 0.6 ), new( "bone_pile_small", -0.28f, -0.2f, 0, 0.5 ), new( "bone_pile_small", 0.3f, -0.3f, 0, 0.5 ),
		} ),
		new( "cage_pit", 1, 1, new SetItem[]
		{
			new( "cage", 0, 0, 0 ), new( "bone_pile_small", 0.2f, 0.15f, 0, 0.8 ), new( "chain_short", 0, 0, 0 ), new( "bone_pile_small", -0.2f, 0.2f, 0, 0.5 ),
			new( "candles", 0.25f, -0.2f, 0, 0.5 ),
		} ),
		new( "throne_dais", 1, 1, new SetItem[]
		{
			new( "throne", -0.3f, 0, 0 ), new( "brazier", -0.25f, 0.3f, 0 ), new( "brazier", -0.25f, -0.3f, 0 ), new( "rug_flat", 0.15f, 0, 0 ),
			new( "candle_stand", -0.35f, 0.15f, 0, 0.5 ), new( "candle_stand", -0.35f, -0.15f, 0, 0.5 ),
		}, WallBehind: true ),
		new( "forge", 1, 1, new SetItem[]
		{
			new( "forge", 0, 0, 0 ), new( "forge_pipe", 0, 0, 0 ), new( "stool", 0.32f, 0.3f, 0, 0.5 ),
		} ),
		new( "campfire_bones", 1, 1, new SetItem[]
		{
			new( "bonfire", 0, 0, 0 ), new( "bone_pile_small", 0.2f, 0.15f, 0, 0.8 ), new( "bone_pile_small", -0.15f, 0.2f, 0 ), new( "skeleton", 0.28f, -0.1f, 30, 0.6 ),
			new( "stool", -0.25f, -0.2f, 45, 0.5 ),
		} ),
		new( "rubble_pillar", 1, 1, new SetItem[]
		{
			new( "pillar_pile", 0, 0, 0 ), new( "bricks", 0.2f, 0.15f, 0 ), new( "bricks", -0.15f, 0.2f, 0, 0.7 ), new( "pebbles", 0.15f, -0.2f, 0, 0.7 ), new( "brick", -0.2f, -0.2f, 30, 0.6 ),
		} ),
		new( "loot_hoard", 1, 1, new SetItem[]
		{
			// no coins any more (user 2026-09-15): chests, candles and bones make the hoard
			new( "chest", 0, 0, 0 ), new( "chest", 0.2f, 0.15f, 30, 0.7 ), new( "candles", 0.1f, -0.15f, 0 ), new( "bone_pile_small", -0.15f, 0.1f, 0, 0.8 ),
			new( "book_pile", -0.2f, -0.2f, 30, 0.7 ), new( "candle_stand", 0.15f, -0.25f, 0, 0.6 ), new( "chest_wood", -0.25f, 0.25f, -20, 0.5 ), new( "candles", 0.28f, -0.05f, 10, 0.5 ),
		} ),
	};

	public static readonly Dictionary<string, SetPiece> Sets = SetList.ToDictionary( s => s.Id );

	// ------------------------------------------------------------------ fill tables

	private static Dictionary<SlotKind, double> D( double strip, double corner = -1, double wall = -1, double swap = 0, double ceiling = 0, double scatter = 0.35, double litter = 0.35 ) => new()
	{
		[SlotKind.EdgeStrip] = strip,
		[SlotKind.Corner] = corner < 0 ? strip : corner,
		[SlotKind.Wall] = wall < 0 ? strip : wall,
		[SlotKind.WallSwap] = swap,
		[SlotKind.Ceiling] = ceiling,
		[SlotKind.Centre] = 1,
		[SlotKind.Scatter] = scatter,
		[SlotKind.Litter] = litter,
	};

	private static SignatureDef Sig( double perCell, int min, params (string id, double w)[] items ) => new( E( items ), perCell, min );

	/// <summary>The debris every room and corridor gets unless its table says otherwise: nothing here blocks or lights.</summary>
	public static readonly FillEntry[] Debris = E( ("pebbles", 26), ("bricks", 24), ("brick", 14), ("bone_pile_small", 12), ("shard", 8), ("vase_broken", 6), ("roots", 6), ("", 4) );
	/// <summary>DecorBoneLitter's pick: the gaol's bone litter mix, with the shrunken creature bones in it.</summary>
	public static readonly FillEntry[] BoneLitter = E( ("litter_skull", 28), ("litter_bone", 28), ("bone_pile_small", 20), ("litter_limb", 12), ("critter_skull", 5), ("critter_ribs", 4), ("critter_spine", 3) );
	private static readonly FillEntry[] BoneDebris =E( ("bone_pile_small", 38), ("bricks", 14), ("pebbles", 14), ("brick", 10), ("shard", 8), ("vase_broken", 6), ("roots", 10) );

	private static readonly FillTable[] TableList =
	{
		new( DecorRole.Library, new()
		{
			[SlotKind.EdgeStrip] = E( ("bookcase", 45), ("candle_stand", 12), ("table_low", 10), ("stool", 8), ("", 25) ),
			[SlotKind.Wall] = E( ("banner", 20), ("torch", 25), ("", 55) ),
			[SlotKind.Corner] = E( ("book_pile", 35), ("candle_stand", 25), ("stool", 10), ("bookcase", 15), ("", 15) ),
			[SlotKind.Centre] = Mix( S( ("table_reading", 50), ("table_study", 40) ), E( ("", 10) ) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove", 30), ("alcove_round", 10), ("window", 14), ("window_2", 8) ), WallVariety ),
			[SlotKind.Ceiling] = E( ("chandelier", 1) ),
			[SlotKind.Scatter] = E( ("book_pile", 35), ("book", 25), ("candles", 15), ("inkwell", 5), ("stool", 5), ("", 15) ),
			[SlotKind.Litter] = E( ("book", 22), ("book_pile", 12), ("pebbles", 22), ("bricks", 18), ("brick", 10), ("shard", 6), ("roots", 6) ),
		}, D( 0.85, corner: 0.85, wall: 0.6, swap: 0.6, ceiling: 0.3, scatter: 0.4 ), 5, -0.6, MaxLights: 4, Signature: Sig( 0.7, 3, ("bookcase", 1) ) ),

		new( DecorRole.Armory, new()
		{
			[SlotKind.EdgeStrip] = E( ("weapon_rack", 28), ("stone_table", 12), ("chest", 14), ("bench", 8), ("stone_table_broken", 4), ("", 34) ),
			[SlotKind.Wall] = E( ("torch", 35), ("banner", 15), ("", 50) ),
			[SlotKind.Corner] = E( ("chest", 25), ("brazier", 12), ("stool", 8), ("pillar_pile", 5), ("", 50) ),
			[SlotKind.Centre] = Mix( S( ("forge", 40), ("rubble_pillar", 10) ), E( ("", 50) ) ),
			[SlotKind.WallSwap] = Mix( E( ("window", 12), ("window_2", 8), ("sealed_door", 6) ), WallVariety ),
			[SlotKind.Scatter] = E( ("brick", 12), ("candles", 15), ("stool", 8), ("", 55) ),
			[SlotKind.Litter] = E( ("brick", 24), ("bricks", 24), ("pebbles", 26), ("bone_pile_small", 8), ("shard", 6), ("roots", 6) ),
		}, D( 0.8, swap: 0.5, scatter: 0.3 ), 4, 0.1, MaxLights: 3, Signature: Sig( 0.4, 2, ("weapon_rack", 1) ) ),

		new( DecorRole.Crypt, new()
		{
			[SlotKind.EdgeStrip] = E( ("coffin", 50), ("bone_pile", 15), ("vessel", 12), ("coffin_open", 10), ("stone_table_broken", 3), ("", 10) ),
			[SlotKind.Wall] = E( ("torch", 25), ("skeleton_shackled", 8), ("", 67) ),
			[SlotKind.Corner] = E( ("bone_pile_small", 35), ("bone_pile_small", 20), ("vessel", 12), ("candle_stand", 12), ("pillar_pile", 5), ("", 16) ),
			[SlotKind.Centre] = Mix( S( ("coffin_open", 40), ("coffin_row", 35), ("campfire_bones", 10) ), E( ("", 15) ) ),
			[SlotKind.Ceiling] = E( ("chain", 55), ("skeleton_cage", 35), ("chandelier_broken", 10) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove_2", 30), ("alcove_round", 12), ("sealed_door", 8), ("window_2", 6) ), WallVariety ),
			[SlotKind.Scatter] = E( ("bone_pile_small", 35), ("bone_pile_small", 20), ("candles", 15), ("shard", 10), ("pebbles", 10), ("candle", 10) ),
			[SlotKind.Litter] = BoneDebris,
		}, D( 0.8, ceiling: 0.5, swap: 0.6, scatter: 0.4, litter: 0.45 ), 5, 0.7, MaxLights: 2, Signature: Sig( 0.6, 2, ("coffin", 70), ("coffin_open", 30) ) ),

		new( DecorRole.MessHall, new()
		{
			[SlotKind.EdgeStrip] = E( ("bench", 18), ("stool", 12), ("fireplace_wall", 10), ("stone_table", 10), ("table_low", 6), ("fountain", 5), ("table_broken", 4), ("", 35) ),
			[SlotKind.Wall] = E( ("torch", 35), ("banner", 25), ("", 40) ),
			[SlotKind.Corner] = E( ("stool", 25), ("candle_stand", 15), ("bench", 12), ("stone_chair", 8), ("", 40) ),
			[SlotKind.Centre] = Mix( S( ("table_feast", 70), ("cauldron_brew", 20) ), E( ("", 10) ) ),
			[SlotKind.Ceiling] = E( ("chandelier", 1) ),
			[SlotKind.WallSwap] = Mix( E( ("window", 16), ("window_2", 10), ("alcove", 10), ("alcove_round", 8) ), WallVariety ),
			[SlotKind.Scatter] = E( ("stool", 35), ("candles", 20), ("candle", 10), ("bench", 5), ("", 30) ),
			[SlotKind.Litter] = E( ("pebbles", 28), ("bricks", 22), ("brick", 14), ("shard", 12), ("bone_pile_small", 10), ("roots", 8) ),
		}, D( 0.8, ceiling: 0.4, swap: 0.55, scatter: 0.3 ), 3, -0.2, MaxLights: 8, Signature: Sig( 0.4, 2, ("bench", 50), ("stool", 30), ("stone_table", 20) ) ),

		new( DecorRole.Shrine, new()
		{
			[SlotKind.EdgeStrip] = E( ("candle_stand", 35), ("stone_lantern", 15), ("rune_block", 12), ("", 38) ),
			[SlotKind.Wall] = E( ("banner", 60), ("torch", 20), ("", 20) ),
			[SlotKind.Corner] = E( ("obelisk", 35), ("statue", 35), ("rune_pillar", 20), ("stone_lantern", 10) ),
			[SlotKind.Centre] = S( ("altar_grand", 60), ("altar_small", 100) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove_round", 30), ("alcove", 15) ), WallVariety ),
			[SlotKind.Scatter] = E( ("candles", 40), ("rune_block", 25), ("candle", 15), ("", 20) ),
			[SlotKind.Litter] = E( ("rune_block", 20), ("pebbles", 30), ("bricks", 20), ("bone_pile_small", 10), ("shard", 8), ("roots", 12) ),
		}, D( 0.65, corner: 1, wall: 1, swap: 0.5, scatter: 0.4, litter: 0.2 ), 0, 0, MaxLights: 10, Signature: Sig( 0.4, 2, ("candle_stand", 50), ("stone_lantern", 30), ("rune_pillar", 20) ) ),

		new( DecorRole.Storage, new()
		{
			// the crates and barrels that made this a store room come from the breakable system now
			[SlotKind.EdgeStrip] = E( ("chest", 14), ("plank", 12), ("vat", 5), ("bench", 5), ("table_broken", 6), ("", 58) ),
			[SlotKind.Wall] = E( ("torch", 15), ("", 85) ),
			[SlotKind.Corner] = E( ("plank", 22), ("chest", 12), ("stool", 8), ("pillar_pile", 8), ("", 50) ),
			[SlotKind.Centre] = Mix( S( ("rubble_pillar", 25) ), E( ("", 75) ) ),
			[SlotKind.Ceiling] = E( ("chandelier_broken", 1) ),
			[SlotKind.WallSwap] = Mix( E( ("sealed_door", 10), ("window_2", 6) ), WallVariety ),
			[SlotKind.Scatter] = E( ("shard", 12), ("plank", 12), ("brick", 12), ("candles", 6), ("", 46) ),
			[SlotKind.Litter] = E( ("bricks", 26), ("brick", 22), ("pebbles", 24), ("shard", 14), ("roots", 8) ),
		}, D( 0.9, swap: 0.5, scatter: 0.35, ceiling: 0.25 ), 5, -0.4, MaxLights: 1, Signature: Sig( 0.3, 1, ("chest", 60), ("plank", 40) ) ),

		new( DecorRole.Torture, new()
		{
			[SlotKind.EdgeStrip] = E( ("cage", 25), ("iron_maiden", 20), ("stocks", 15), ("brazier", 6), ("", 34) ),
			[SlotKind.Wall] = E( ("torch", 25), ("skeleton_hung", 12), ("skeleton_shackled", 12), ("", 51) ),
			[SlotKind.Corner] = E( ("bone_pile_small", 25), ("bone_pile_small", 25), ("brazier", 20), ("vessel", 10), ("cage", 12), ("", 8) ),
			[SlotKind.Centre] = Mix( S( ("stocks_skeleton", 40), ("stretch_table", 30), ("cage_pit", 15), ("campfire_bones", 8) ), E( ("", 7) ) ),
			[SlotKind.Ceiling] = E( ("chain", 55), ("skeleton_cage", 37), ("chandelier_broken", 8) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove_2", 20), ("sealed_door", 10), ("window_2", 8) ), WallVariety ),
			[SlotKind.Scatter] = E( ("bone_pile_small", 35), ("bone_pile_small", 20), ("candles", 15), ("shard", 10), ("brick", 10), ("skeleton", 10) ),
			[SlotKind.Litter] = BoneDebris,
		}, D( 0.8, ceiling: 0.6, swap: 0.55, scatter: 0.35, litter: 0.45 ), 4, 0.8, MaxLights: 3, Signature: Sig( 0.4, 2, ("cage", 35), ("iron_maiden", 35), ("stocks", 30) ) ),

		new( DecorRole.Throne, new()
		{
			[SlotKind.EdgeStrip] = E( ("rune_pillar", 20), ("statue", 12), ("stone_lantern", 10), ("candle_stand", 8), ("", 50) ),
			[SlotKind.Wall] = E( ("banner", 75), ("torch", 25) ),
			[SlotKind.Corner] = E( ("statue", 55), ("obelisk", 20), ("brazier", 25) ),
			[SlotKind.Centre] = S( ("throne_dais", 1) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove_round", 30), ("window", 10) ), WallVariety ),
			[SlotKind.Ceiling] = E( ("chandelier", 1) ),
			[SlotKind.Scatter] = E( ("candles", 40), ("rune_block", 15), ("", 35) ),
		}, D( 0.6, corner: 1, wall: 0.9, swap: 0.6, ceiling: 0.5, scatter: 0.25, litter: 0.2 ), 0, 0, MaxLights: 8, Signature: Sig( 0.25, 2, ("statue", 50), ("rune_pillar", 50) ) ),

		new( DecorRole.Alchemy, new()
		{
			[SlotKind.EdgeStrip] = E( ("bookcase", 40), ("cauldron", 12), ("stone_table", 8), ("potion_pole", 8), ("table_low", 5), ("", 27) ),
			[SlotKind.Wall] = E( ("torch", 25), ("", 75) ),
			[SlotKind.Corner] = E( ("book_pile", 25), ("candle_stand", 25), ("potion_pole", 20), ("", 30) ),
			[SlotKind.Centre] = Mix( S( ("table_potions", 55), ("cauldron_brew", 40) ), E( ("", 5) ) ),
			[SlotKind.Ceiling] = E( ("chandelier", 1) ),
			[SlotKind.WallSwap] = Mix( E( ("alcove", 30), ("window", 10), ("window_2", 8) ), WallVariety ),
			[SlotKind.Scatter] = E( ("vial", 15), ("book_pile", 30), ("shard", 15), ("candles", 15), ("scale", 5), ("", 20) ),
			[SlotKind.Litter] = E( ("shard", 30), ("pebbles", 22), ("bricks", 18), ("brick", 8), ("vase_broken", 8), ("book", 6), ("roots", 8) ),
		}, D( 0.8, ceiling: 0.2, swap: 0.6, scatter: 0.4 ), 4, -0.3, MaxLights: 5, Signature: Sig( 0.5, 2, ("bookcase", 50), ("cauldron", 30), ("potion_pole", 20) ) ),

		new( DecorRole.Barracks, new()
		{
			[SlotKind.EdgeStrip] = E( ("bed", 55), ("bed_big", 5), ("weapon_rack", 10), ("stool", 8), ("chest_wood", 8), ("fountain", 4), ("", 10) ),
			[SlotKind.Wall] = E( ("torch", 30), ("banner", 15), ("", 55) ),
			[SlotKind.Corner] = E( ("candle_stand", 25), ("chest", 18), ("stool", 15), ("", 42) ),
			[SlotKind.Centre] = Mix( S( ("table_reading", 40), ("table_feast", 15), ("campfire_bones", 8) ), E( ("", 37) ) ),
			[SlotKind.WallSwap] = Mix( E( ("window", 25), ("window_2", 15), ("alcove", 8) ), WallVariety ),
			[SlotKind.Scatter] = E( ("stool", 25), ("candle", 12), ("candles", 12), ("book", 12), ("", 39) ),
			[SlotKind.Litter] = E( ("pebbles", 28), ("bricks", 22), ("brick", 12), ("book", 10), ("shard", 8), ("bone_pile_small", 8), ("roots", 6) ),
		}, D( 0.85, swap: 0.6, scatter: 0.3 ), 5, -0.1, MaxLights: 3, Signature: Sig( 0.7, 3, ("bed", 90), ("bed_big", 10) ) ),

		new( DecorRole.Secret, new()
		{
			// no coins (user 2026-09-15): the hoard is chests, candles and bones; the potion pass adds a heal potion here
			[SlotKind.EdgeStrip] = E( ("chest", 40), ("chest_wood", 20), ("candle_stand", 10), ("", 30) ),
			[SlotKind.Wall] = E( ("", 1) ),
			[SlotKind.Corner] = E( ("chest", 30), ("bone_pile_small", 25), ("candles", 15), ("", 30) ),
			[SlotKind.Centre] = S( ("loot_hoard", 1) ),
			[SlotKind.Scatter] = E( ("candles", 30), ("bone_pile_small", 25), ("book_pile", 10), ("", 35) ),
			[SlotKind.Litter] = E( ("pebbles", 30), ("bricks", 24), ("shard", 16), ("bone_pile_small", 30) ),
		}, D( 1.0, scatter: 0.4 ), 0, 0, MaxLights: 1, Signature: Sig( 0.4, 2, ("chest", 70), ("chest_wood", 30) ) ),
	};

	public static readonly Dictionary<DecorRole, FillTable> Tables = TableList.ToDictionary( t => t.Role );

	/// <summary>Roles a plain room may roll (tags and halls are handled separately).</summary>
	public static readonly DecorRole[] RoomRoles = { DecorRole.Library, DecorRole.Armory, DecorRole.Crypt, DecorRole.Storage, DecorRole.Torture, DecorRole.Alchemy, DecorRole.Barracks };
	public static readonly DecorRole[] HallRoles = { DecorRole.MessHall, DecorRole.Crypt, DecorRole.Barracks, DecorRole.Armory };

	/// <summary>Corridor clutter (corners and free quarters), weights before the decay scaling.</summary>
	public static readonly FillEntry[] HallwayClutter = E( ("pebbles", 35), ("bricks", 20), ("bone_pile_small", 5), ("bone_pile_small", 5), ("brick", 5), ("", 30) );
	/// <summary>Extra clutter that only shows up deep down.</summary>
	public static readonly FillEntry[] DecayClutter = E( ("bricks", 25), ("plank", 20), ("roots", 18), ("mushrooms", 12), ("barrel_broken", 10), ("pillar_pile", 8), ("table_broken", 4), ("vase_broken", 3) );

	/// <summary>Every model path the STONE catalog can ever emit; gentest checks each kit through DungeonKit.AllModels.</summary>
	public static IEnumerable<string> AllModels()
	{
		foreach ( var p in PropList )
		{
			foreach ( var m in p.Models ) yield return m;
			if ( p.Attach != null ) foreach ( var m in p.Attach ) yield return m;
			if ( p.Decay != null ) foreach ( var m in p.Decay ) yield return m;
		}
		foreach ( var w in WallSwaps.Values )
		{
			yield return w.Model;
			if ( w.Insert != null ) yield return w.Insert;
		}
		foreach ( var m in RuneTiles ) yield return m;
		yield return SecretWallModel;
		yield return PlainWallModel;
	}

	/// <summary>Every prop id referenced by a table, set piece, top or companion must exist in the kit's Props (the
	/// stone catalog when no kit is given), and every wall swap id in its WallSwaps.</summary>
	public static IEnumerable<string> MissingIds( DungeonKit kit = null )
	{
		var props = kit?.Props ?? Props;
		return ReferencedIds( kit ).Where( id => !props.ContainsKey( id ) );
	}

	/// <summary>Breakable props referenced by the static fill: should be empty, they belong to the breakable system.</summary>
	public static IEnumerable<string> LeakedBreakables( DungeonKit kit = null )
	{
		var props = kit?.Props ?? Props;
		return ReferencedIds( kit ).Where( id => props.TryGetValue( id, out var p ) && p.Breakable );
	}

	private static IEnumerable<string> ReferencedIds( DungeonKit kit = null )
	{
		var swaps = kit?.WallSwaps ?? WallSwaps;
		var sets = kit?.Sets ?? Sets;
		var ids = new HashSet<string>();
		void Table( SlotKind kind, FillEntry[] es )
		{
			foreach ( var e in es )
			{
				if ( e.Id == "" ) continue;
				if ( kind == SlotKind.WallSwap ) { if ( !swaps.ContainsKey( e.Id ) ) ids.Add( "swap " + e.Id ); }
				else if ( e.IsSet ) { if ( !sets.ContainsKey( e.Id ) ) ids.Add( "set " + e.Id ); }
				else ids.Add( e.Id );
			}
		}
		foreach ( var t in TableList )
			foreach ( var (kind, es) in t.Entries ) Table( kind, es );
		// the kit's own slot overrides and its hallway clutter, which replace stone entries rather than add to them
		if ( kit?.Tables != null )
			foreach ( var role in kit.Tables.Values )
				foreach ( var (kind, es) in role ) Table( kind, es );
		if ( kit?.HallwayClutter != null ) Table( SlotKind.Scatter, kit.HallwayClutter );
		if ( kit?.SigilProp != null ) ids.Add( kit.SigilProp );
		foreach ( var t in TableList ) if ( t.Signature != null ) foreach ( var e in t.Signature.Entries ) if ( e.Id != "" ) ids.Add( e.Id );
		foreach ( var e in Debris.Concat( BoneDebris ).Concat( BoneLitter ) ) if ( e.Id != "" ) ids.Add( e.Id );
		ids.Add( DecorWebs.CornerId );
		foreach ( var s in sets.Values ) foreach ( var i in s.Items ) ids.Add( i.Prop );
		foreach ( var kv in Tops ) { ids.Add( kv.Key ); foreach ( var e in kv.Value ) if ( e.Id != "" ) ids.Add( e.Id ); }
		foreach ( var kv in Companions ) { ids.Add( kv.Key ); foreach ( var e in kv.Value ) if ( e.Id != "" ) ids.Add( e.Id ); }
		foreach ( var e in HallwayClutter.Concat( DecayClutter ) ) if ( e.Id != "" ) ids.Add( e.Id );
		// the greenery pass keeps its own tables (it is not driven by a role table), so name them here or a
		// typo in one of them would sail past MissingIds
		foreach ( var e in DecorGreenery.AllEntries ) if ( e.Id != "" ) ids.Add( e.Id );
		foreach ( var p in (kit != null ? kit.Props.Values : (IEnumerable<PropDef>)PropList) ) if ( p.Foot != null ) ids.Add( p.Foot );
		foreach ( var w in swaps.Values ) if ( w.Item != null ) ids.Add( w.Item );
		return ids.OrderBy( id => id );
	}
}
