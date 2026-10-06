using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The Gaol: a plague-fortress prison built from POLYGON Dark Fantasy (chapel, cemetery, gallows, pyres, candles)
/// and POLYGON Dark Fortress (prison walls, corpses, cages, torture machines, the specimen lab, siege engines,
/// chains) together. Cell blocks behind iron bars, a torturer's court, a charnel crypt, a plague doctor's
/// laboratory, a desecrated chapel, a butchery, a siege store, and an oubliette with a portal.
///
/// Both packs are on a 2.5 m x 3 m grid (the Fortress "L" pieces are 5 x 6 m), and squashing a 6 m door wall to
/// our 5 m would drop its 1.97 m door hole to 1.64, so the fabric is COMPOSED (DungeonKit.Composites): a bottom row
/// of two unscaled 2.5 x 3 m panels, a top row of two panels scaled to 2 m, and a 5 m moulding over the seam.
/// Every cell's ceiling is a groin vault of four Dark Fantasy quadrants springing from the corner posts; the
/// generator never stacks rooms closer than 5 m and spawns no ceiling at exactly +2 levels, so the vault's 1.5 m
/// rise above the plane is safe. Measured with tools/fbx_bounds.py (metres, s&box local frame: X depth, Y along
/// the wall, Z up):
///   panel:       pivot at one end, runs +Y 2.5 m, 3 m tall, thickness -0.11..+0.25/0.28 (Dx -0.07/-0.085 centres it)
///   floor tile:  2.5 x 2.5, pivot at a corner covering +X/+Y, top at +0.06
///   vault:       SM_Bld_Ceiling_01, pivot at the LOW corner (-0.04), covers -X/+Y 2.64 m, rises to 1.5 at the far corner
///   stair:       Stairs_Tiles_01 covers -X/+Y and climbs -X, 2.5 run x 2.5 wide x 1.5 rise -> scaled (2, 2, 1.667)
///   posts:       Pillar_02 1.0 m square, 3 m, centred; stacked 3 + 2 m
///   secret wall: Wall_L_03 (5 x 6 squashed), the only seamless plain wall on the floor; Wall_L_Damaged_06 opens it
/// The vault edge along a wall runs from 3.5 m at the corners to 4.9 at the wall middle, so wall-side props stay
/// under 3.4 m (MaxHeight where a model is taller) and only the centre sets go higher.
/// </summary>
public sealed partial record DungeonKit
{
	private const string DFP = "darkfantasy/";
	private const string FFP = "darkfortress/";
	private static string[] DF( params string[] names ) => names.Select( n => DFP + n + ".vmdl" ).ToArray();
	private static string[] FF( params string[] names ) => names.Select( n => FFP + n + ".vmdl" ).ToArray();
	private static string Df( string name ) => DFP + name + ".vmdl";
	private static string Ff( string name ) => FFP + name + ".vmdl";
	private static KitPiece P( string model, float dx = 0f, float dy = 0f, float dz = 0f, float yaw = 0f, float sz = 1f, float sx = 1f, float sy = 1f, PieceCollider col = PieceCollider.Inherit )
		=> new( model, dx, dy, dz, yaw, 0f, sx, sy, sz, col );

	// the top row of every wall: two plain panels squashed from 3 m to 2 m. The moulding that hides the seam on both
	// faces (Wall_Detail_02) is the kit's WallTrim, spawned by the builder per face so the throne room can go without
	// (user: it crossed the banners there). Wall_02 is the even 0.21-0.28 relief top to bottom; Wall_01 is a flat face
	// at 0.11 with a bone skirt to 0.25 over its lower 0.9 m, so it is a variety swap, never the main wall (the user:
	// things against the bones read wrong)
	private static readonly KitPiece[] GaolTopRow =
	{
		P( Ff( "sm_bld_wall_02" ), -0.085f, 0f, 3f, sz: 0.6667f ), P( Ff( "sm_bld_wall_02" ), -0.085f, 2.5f, 3f, sz: 0.6667f ),
	};
	private static KitPiece[] Wall( params KitPiece[] bottom ) => bottom.Concat( GaolTopRow ).ToArray();
	// the plain 2.5 m panels, centred on the grid line
	private static KitPiece W01( float dy ) => P( Ff( "sm_bld_wall_01" ), -0.07f, dy );
	private static KitPiece W02( float dy ) => P( Ff( "sm_bld_wall_02" ), -0.085f, dy );
	private static KitPiece W03( float dy ) => P( Ff( "sm_bld_wall_03" ), -0.085f, dy );
	private static KitPiece Half( float dy ) => P( Ff( "sm_bld_base_wall_half_01" ), -0.07f, dy );
	// a 5 m cell of four 2.5 m floor tiles
	private static KitPiece[] Floor4( string a, string b, string c, string d ) => new[] { P( Ff( a ) ), P( Ff( b ), 2.5f ), P( Ff( c ), 0f, 2.5f ), P( Ff( d ), 2.5f, 2.5f ) };

	private static readonly Dictionary<string, KitPiece[]> GaolComposites = new()
	{
		// ---- floors: six fixed mixes of the four plain tiles; plain slabs under the trap plates. None of the Round_Tile
		// pieces (user: their rounded edge opens a hole in the floor)
		["floor_a"] = Floor4( "sm_bld_floor_tile_01", "sm_bld_floor_tile_02", "sm_bld_floor_tile_03", "sm_bld_floor_tile_04" ),
		["floor_b"] = Floor4( "sm_bld_floor_tile_02", "sm_bld_floor_tile_04", "sm_bld_floor_tile_01", "sm_bld_floor_tile_03" ),
		["floor_c"] = Floor4( "sm_bld_floor_tile_03", "sm_bld_floor_tile_04", "sm_bld_floor_tile_02", "sm_bld_floor_tile_01" ),
		["floor_d"] = Floor4( "sm_bld_floor_tile_02", "sm_bld_floor_tile_01", "sm_bld_floor_tile_04", "sm_bld_floor_tile_03" ),
		["floor_e"] = Floor4( "sm_bld_floor_tile_04", "sm_bld_floor_tile_03", "sm_bld_floor_tile_01", "sm_bld_floor_tile_02" ),
		["floor_f"] = Floor4( "sm_bld_floor_tile_01", "sm_bld_floor_tile_01", "sm_bld_floor_tile_02", "sm_bld_floor_tile_02" ),
		["floor_plain"] = Floor4( "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01" ),
		// the ritual floors: plain tiles with a ritual circle (6 m, scaled to fit the bay) or a stone medallion on them
		["floor_ritual"] = Floor4( "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01" )
			.Append( P( Df( "sm_prop_ritual_circle_01" ), 2.5f, 2.5f, 0.06f, sx: 0.8f, sy: 0.8f, sz: 0.8f, col: PieceCollider.None ) ).ToArray(),
		["floor_medallion"] = Floor4( "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01", "sm_bld_floor_01" )
			.Append( P( Df( "sm_bld_decoration_01" ), 2.5f, 2.5f, 0.06f, col: PieceCollider.None ) ).ToArray(),
		// ---- the vault: four quadrants, each pivoted at a cell corner, rising to the apex at the ceiling plane
		["ceiling_vault"] = new[]
		{
			P( Df( "sm_bld_ceiling_01" ), 5f, 0f, -1.5f, yaw: 0f, sx: 0.947f, sy: 0.947f ),
			P( Df( "sm_bld_ceiling_01" ), 5f, 5f, -1.5f, yaw: 90f, sx: 0.947f, sy: 0.947f ),
			P( Df( "sm_bld_ceiling_01" ), 0f, 5f, -1.5f, yaw: 180f, sx: 0.947f, sy: 0.947f ),
			P( Df( "sm_bld_ceiling_01" ), 0f, 0f, -1.5f, yaw: 270f, sx: 0.947f, sy: 0.947f ),
		},
		// ---- walls
		["wall_a"] = Wall( W02( 0f ), W02( 2.5f ) ),
		["wall_b"] = Wall( W01( 0f ), W01( 2.5f ) ),
		["wall_c"] = Wall( W03( 0f ), P( Ff( "sm_bld_wall_04" ), 0f, 2.5f ) ),
		["wall_short"] = new[] { P( Ff( "sm_bld_wall_02" ), -0.085f, 0f, sz: 0.8333f ), P( Ff( "sm_bld_wall_02" ), -0.085f, 2.5f, sz: 0.8333f ) },
		["wall_secret"] = new[] { P( Ff( "sm_bld_wall_l_03" ), sz: 0.8333f ) },
		["wall_secret_open"] = new[] { P( Ff( "sm_bld_wall_l_damaged_06" ), sz: 0.8333f ) },
		// a slit window boarded over, on a rock-backed wall
		["wall_window_boarded"] = Wall( P( Ff( "sm_bld_wall_window_04" ) ), W03( 2.5f ), P( Df( "sm_prop_boarded_wood_01" ), 0f, 1.25f, 1.6f, col: PieceCollider.None ) ),
		// a breach into the masonry: the damaged wall with holes at eye height and an inner leaf a metre behind
		["wall_breach"] = new[] { P( Ff( "sm_bld_wall_l_damaged_04" ), sz: 0.8333f ), P( Ff( "sm_bld_wall_l_01" ), -1f, sz: 0.8333f ) },
		["wall_breach2"] = new[] { P( Ff( "sm_bld_wall_l_damaged_05" ), sz: 0.8333f ), P( Ff( "sm_bld_wall_l_01" ), -1f, sz: 0.8333f ) },
		// an arched niche: the 5 m arch panel, a back wall 1.45 m behind and two side panels turned along the jambs
		["alcove"] = Wall( P( Df( "sm_bld_wall_alcove_01" ) ),
			P( Ff( "sm_bld_wall_02" ), -1.45f, 1.25f ), P( Ff( "sm_bld_base_wall_half_01" ), 0f, 1.55f, yaw: 90f ), P( Ff( "sm_bld_base_wall_half_01" ), -1.25f, 3.45f, yaw: 270f ) ),
		["wall_window"] = Wall( P( Ff( "sm_bld_wall_window_02" ) ), P( Ff( "sm_bld_wall_window_05" ), 0f, 2.5f ) ),
		["wall_window2"] = Wall( P( Ff( "sm_bld_wall_window_04" ) ), P( Ff( "sm_bld_wall_window_02" ), 0f, 2.5f ) ),
		// the cell-block walls: iron bars you see and cast through (mesh colliders through MeshCollider on the swap)
		["wall_bars"] = Wall( P( Ff( "sm_bld_wall_prison_01" ) ), P( Ff( "sm_bld_wall_prison_01" ), 0f, 2.5f ) ),
		["wall_bars_half"] = Wall( P( Ff( "sm_bld_wall_prison_01" ) ), W03( 2.5f ) ),
		// ---- doorways (DoorCatalog.GaolFits, same order)
		["door_single"] = Wall( Half( 0f ), P( Df( "sm_bld_base_wall_door_01" ), 0f, 1.25f ), Half( 3.75f ) ),
		["door_double"] = Wall( Half( 0f ), P( Ff( "sm_bld_wall_door_double_02" ), 0f, 1.25f ), Half( 3.75f ) ),
		["door_prison"] = Wall( P( Ff( "sm_bld_wall_door_prison_01" ) ), P( Ff( "sm_bld_wall_prison_01" ), 0f, 2.5f ) ),
		["door_arch"] = new[] { P( Ff( "sm_bld_wall_l_arch_01" ), sz: 0.8333f ) },
		["door_ruin"] = Wall( Half( 0f ), P( Df( "sm_bld_wall_door_ruin_02" ), 0f, 1.25f ), Half( 3.75f ) ),
		// the sealed doorway: a single doorway with a chained crypt door fixed in its hole
		["door_sealed"] = new[] { P( "@door_single" ), P( Df( "sm_prop_crypt_door_chained_01" ), 0f, 1.96f ) },
		// ---- stairs, pillars, posts
		["stair"] = new[] { P( Ff( "sm_bld_stairs_tiles_01" ), sx: 2f, sy: 2f, sz: 1.6667f ) },
		["pillar"] = new[] { P( Df( "sm_bld_pillar_05" ), sz: 0.8333f ) },
		["pillar_round"] = new[] { P( Ff( "sm_bld_l_pillar_04" ), sz: 0.8333f ) },
		["post"] = new[] { P( Ff( "sm_bld_pillar_02" ) ), P( Ff( "sm_bld_pillar_02" ), 0f, 0f, 3f, sz: 0.6667f ) },
		// the Dark Fantasy torch pivots at the BOTTOM of its 1 m plate (flame at 0.84) where the Fortress one is centred on the
		// wall face: lowered so the two hang level at one MountHeight (its plate spans 1.25-2.25 m, the fire at 2.09)
		["torch_df"] = new[] { P( Df( "sm_prop_torch_01" ), 0f, 0f, -0.45f ) },
		// ---- props whose pivot sits at one end, recentred
		["pew"] = new[] { P( Df( "sm_prop_pew_01" ), 0f, -1f ) },
		["pew_long"] = new[] { P( Df( "sm_prop_pew_large_01" ), 0f, -2f ) },
		["bookcase"] = new[] { P( Df( "sm_prop_bookshelf_double_01" ), -0.32f, -1.25f ) },
		["bookcase_damaged"] = new[] { P( Df( "sm_prop_bookshelf_double_01_damaged_01" ), -0.37f, -1.17f ) },
		["bookcase_small"] = new[] { P( Df( "sm_prop_bookshelf_01" ), -0.4f, -0.63f ) },
		["bookcase_small_damaged"] = new[] { P( Df( "sm_prop_bookshelf_01_damaged_01" ), -0.32f, -0.68f ) },
		["bed_big"] = new[] { P( Ff( "sm_prop_bed_01" ), 0f, 0f, 0.7f ) },
		["pipe_wall"] = new[] { P( Ff( "sm_prop_pipe_01" ), 0f, -0.75f ) },
		["banner_pole"] = new[] { P( Ff( "sm_prop_banner_01" ), 0f, -0.62f ) },
		// the hangers: the Dark Fantasy chains are on the atlas but modelled UP from a bottom pivot, so they are flipped
		// to hang below it (the Fortress chains sit on a cutout vmat that does not render: cages and corpses floated)
		["chain_1m"] = new[] { new KitPiece( Df( "sm_prop_chain_01" ), 0f, 0f, 0f, 0f, 180f ) },
		["chain_3m"] = new[] { new KitPiece( Df( "sm_prop_chain_02" ), 0f, 0f, 0f, 0f, 180f ) },
		// the gibbet's cage, modelled up on its post's arm, recentred to hang from its own top
		["gibbet_cage"] = new[] { P( Df( "sm_prop_gibbet_01_cage_01" ), 0f, -1.48f, -3.87f ) },
	};

	private static readonly Dictionary<string, DecorCatalog.WallSwapDef> GaolWallSwaps = new DecorCatalog.WallSwapDef[]
	{
		new( "alcove", "@alcove", null, false, false, true, 1.25f, "corpse_sitting" ),
		new( "alcove_2", "@alcove", null, false, false, true, 1.25f, "candle_rack" ),
		new( "alcove_round", "@alcove", null, false, false, true, 1.25f, "skull_pile" ),
		new( "wall_deep", "@wall_breach", null, false, false, true, 1.0f ),
		new( "sealed_door", "@door_sealed", null, false, false, true, 0.2f ),
		new( "wall_alt", "@wall_b", null, false, false, false, 0.2f ),
		new( "wall_3", "@wall_c", null, false, false, false, 0.3f ),
		new( "wall_4", "@wall_breach2", null, false, false, true, 1.0f ),
		new( "window", "@wall_window", null, false, false, true, 0.23f, Shared: true ),
		new( "window_2", "@wall_window2", null, false, false, true, 0.23f, Shared: true ),
		new( "bars", "@wall_bars", null, false, false, true, 0.12f, Shared: true ),
		new( "bars_half", "@wall_bars_half", null, false, false, true, 0.12f, Shared: true ),
	}.ToDictionary( w => w.Id );

	/// <summary>The gaol's own defs, written over the stone catalog by id (W x D x H metres, post-scale).</summary>
	private static readonly PropDef[] GaolProps =
	{
		// ---- overrides of stone ids
		// the Fortress wall-arm coal basket (origin on the wall face, coals 0.38 out / 0.20 up) and the Dark Fantasy sconce
		// recentred; Torch_05 is out (measured: a free-standing spike lantern with no wall face, its pole sat in the wall)
		new( "torch", FF( "sm_prop_torch_02" ).Append( "@torch_df" ).ToArray(), AnchorKind.WallMounted, 0.66f, 0.66f, 1.0f, false, MountHeight: 1.7f, Light: LightKind.Torch ),
		new( "candle_stand", FF( "sm_prop_torch_06", "sm_prop_torch_07", "sm_prop_candle_stand_01" ).Concat( DF( "sm_prop_candelabra_01", "sm_prop_candelabra_02" ) ).ToArray(), AnchorKind.Floor, 0.7f, 0.7f, 1.8f, false, Light: LightKind.Candle ),
		new( "candles", DF( "sm_prop_candle_01", "sm_prop_candle_02", "sm_prop_candle_03", "sm_prop_candle_04", "sm_prop_candle_09" ).Concat( FF( "sm_prop_candle_01", "sm_prop_candle_02" ) ).ToArray(), AnchorKind.Floor, 0.3f, 0.3f, 0.6f, false, Light: LightKind.Candle, TopSink: 0.05f ),
		new( "candle", DF( "sm_prop_candle_05", "sm_prop_candle_06", "sm_prop_candle_07", "sm_prop_candle_08" ), AnchorKind.Floor, 0.12f, 0.12f, 0.35f, false, Light: LightKind.Candle, TopSink: 0.05f ),
		new( "brazier", FF( "sm_prop_brazier_03", "sm_prop_brazier_05" ).Concat( DF( "sm_prop_brazier_01" ) ).ToArray(), AnchorKind.Floor, 1.2f, 1.2f, 1.25f, true, Light: LightKind.Brazier ),
		new( "chandelier", FF( "sm_prop_chandelier_01" ), AnchorKind.CeilingHung, 1.41f, 1.41f, 1.43f, false, Light: LightKind.Chandelier ),
		new( "chain", new[] { "@chain_1m" }, AnchorKind.CeilingHung, 0.11f, 0.11f, 1.5f, false, Scale: 1.5f ),
		new( "chain_short", new[] { "@chain_1m" }, AnchorKind.CeilingHung, 0.11f, 0.11f, 1.0f, false ),
		// the gibbet's cage on a short chain: its bottom hangs 1.83 m up (the Fortress hanging cage's chain does not render)
		new( "skeleton_cage", new[] { "@gibbet_cage" }, AnchorKind.CeilingHung, 0.98f, 1.03f, 2.17f, false, MountHeight: 1.0f, Hanger: "chain_short" ),
		new( "banner", DF( "sm_prop_flag_dark_02" ), AnchorKind.WallMounted, 2.6f, 0.3f, 3.1f, false, MountHeight: 4.1f ),
		new( "weapon_rack", DF( "sm_prop_rack_weapon_01" ), AnchorKind.WallFlush, 2.0f, 0.52f, 1.32f, true, Scale: 1.4f ),
		// Top = the 1.27 m shelf (measured: shelves at 0.23, 1.27, 1.77)
		new( "bookcase", new[] { "@bookcase" }, AnchorKind.WallFlush, 2.4f, 0.75f, 2.9f, true, Top: 1.27f, Decay: new[] { "@bookcase_damaged" } ),
		new( "bookcase_small", new[] { "@bookcase_small" }, AnchorKind.WallFlush, 1.36f, 0.72f, 2.9f, true, Top: 1.27f, Decay: new[] { "@bookcase_small_damaged" } ),
		new( "bed", DF( "sm_prop_bed_01" ), AnchorKind.Floor, 1.71f, 2.27f, 1.12f, true, Foot: "chest", Decay: DF( "sm_prop_bed_01_damaged_01" ) ),
		new( "bed_big", new[] { "@bed_big" }, AnchorKind.Floor, 2.74f, 1.13f, 0.89f, true ),
		new( "bench", FF( "sm_prop_bench_01" ), AnchorKind.Floor, 1.8f, 0.55f, 0.51f, true ),
		new( "stool", FF( "sm_prop_chair_01", "sm_prop_chair_03", "sm_prop_chair_04", "sm_prop_chair_05" ).Concat( DF( "sm_prop_chair_01", "sm_prop_stool_bar_01" ) ).ToArray(), AnchorKind.Floor, 0.7f, 0.7f, 1.2f, true,
			Decay: FF( "sm_prop_chair_01_damaged_01" ).Concat( DF( "sm_prop_chair_01_damaged_01" ) ).ToArray() ),
		// Tops are the measured surfaces (upward faces), not the bounds tops: 0.93 / 0.96 / 0.68
		// no Decay on the long table: a set's authored table-top items would float over the broken twin (user: broken
		// tables carry nothing); the damaged Fortress table is a table_broken variant instead
		new( "table_long", FF( "sm_prop_table_01" ), AnchorKind.Floor, 2.0f, 1.0f, 0.95f, true, Top: 0.93f ),
		new( "table_round", FF( "sm_prop_table_02" ), AnchorKind.Floor, 1.55f, 1.55f, 0.97f, true, Top: 0.96f ),
		new( "table_low", FF( "sm_prop_table_small_01" ), AnchorKind.Floor, 0.77f, 0.77f, 0.69f, true, Top: 0.68f ),
		new( "table_broken", DF( "sm_prop_table_01_damaged_02" ).Concat( FF( "sm_prop_table_02_damaged_01", "sm_prop_table_01_damaged_01" ) ).ToArray(), AnchorKind.Floor, 1.5f, 1.4f, 1.3f, true ),
		new( "stone_table", FF( "sm_prop_table_03" ), AnchorKind.Floor, 3.4f, 1.3f, 0.7f, true, Top: 0.7f ),
		new( "stone_table_broken", DF( "sm_prop_table_01_damaged_01" ), AnchorKind.Floor, 1.85f, 1.36f, 0.82f, true ),
		new( "throne", FF( "sm_prop_throne_01" ), AnchorKind.WallFlush, 2.1f, 1.3f, 3.15f, true, Attach: FF( "sm_prop_throne_01_insert_01" ), Bury: 0f, Decay: FF( "sm_prop_throne_01_damaged_01" ) ),
		// Statue_04 is a seated figure (0.82 x 0.78 x 1.47): it floated beside the throne, and only a throne would seat it (user)
		new( "statue", DF( "sm_prop_statue_people_01", "sm_prop_statue_people_02", "sm_prop_statue_people_03" ).Concat( FF( "sm_prop_statue_01", "sm_prop_statue_03" ) ).ToArray(), AnchorKind.Floor, 0.9f, 0.8f, 2.0f, true,
			Decay: DF( "sm_prop_statue_people_damaged_01", "sm_prop_statue_people_damaged_02" ).Concat( FF( "sm_prop_statue_01_damaged_01", "sm_prop_statue_03_damaged_01" ) ).ToArray() ),
		new( "coffin", FF( "sm_prop_coffin_01" ), AnchorKind.Floor, 1.15f, 2.25f, 0.55f, true, Top: 0.47f, Decay: FF( "sm_prop_coffin_01_damaged_01" ) ),
		new( "bone_pile", FF( "sm_prop_bone_pile_01" ), AnchorKind.Floor, 2.4f, 2.0f, 0.8f, false ),
		new( "bone_pile_small", DF( "sm_prop_bone_pile_01", "sm_prop_bone_pile_02" ), AnchorKind.Floor, 1.0f, 1.0f, 0.4f, false ),
		new( "skeleton", DF( "sm_prop_body_skeleton_01", "sm_prop_body_skeleton_02", "sm_prop_body_skeleton_03" ), AnchorKind.Floor, 1.0f, 1.9f, 0.9f, false ),
		// the spread prisoner is modelled with its flat back at +X, so it is turned to put that on the wall; hung with its
		// bottom above the bone skirt (user). The stone slave-shackle skeletons keep their ids as they are.
		new( "corpse_wall", FF( "sm_prop_corpse_prisoner_04" ), AnchorKind.WallMounted, 1.94f, 0.42f, 1.58f, false, MountHeight: 1.8f, Turn: 180f ),
		new( "hanged_wall", DF( "sm_prop_gallows_01_body_01", "sm_prop_gallows_01_body_02" ), AnchorKind.WallMounted, 0.5f, 0.55f, 1.8f, false, MountHeight: 1.0f ),
		new( "stocks", DF( "sm_prop_stock_01" ), AnchorKind.Floor, 1.65f, 1.28f, 2.5f, true, Attach: DF( "sm_prop_stock_01_body_01" ) ),
		new( "cage", FF( "sm_prop_cage_01" ), AnchorKind.Floor, 1.5f, 1.5f, 2.35f, true, Attach: FF( "sm_prop_cage_01_insert_01" ) ),
		// 3.5 m long: declared Wide so it takes the whole cell
		new( "stretch_table", FF( "sm_prop_stretching_rack_01" ), AnchorKind.Floor, 2.7f, 3.5f, 1.85f, true, Attach: FF( "sm_prop_stretching_rack_01_insert_01" ) ),
		// tops measured: DF crate_01 lid 0.35, crate_02 0.47, the Fortress crate is an open box (TopFix per model)
		new( "crate", DF( "sm_prop_crate_01", "sm_prop_crate_02" ).Concat( FF( "sm_prop_crate_01" ) ).ToArray(), AnchorKind.Floor, 1.1f, 1.1f, 0.5f, true, Top: 0.35f, Breakable: true ),
		new( "barrel", DF( "sm_prop_barrel_01", "sm_prop_barrel_open_01" ), AnchorKind.Floor, 0.73f, 0.71f, 0.95f, true, Top: 0.82f, Breakable: true ),
		// user: the breakable-loot pottery is the vases and pots on every floor, and the Fortress urn joins them (it breaks
		// like the rest and leaves the broken vase: BreakableKits.Build)
		new( "vase", Pack( "sm_prop_vase_01", "sm_prop_vase_02", "sm_prop_vase_03", "sm_prop_vase_04", "sm_prop_vase_05", "sm_prop_vase_06", "sm_prop_vase_07", "sm_prop_vase_08", "sm_prop_vase_09" )
			.Concat( Realm( "sm_prop_camp_pot_02", "sm_prop_camp_pot_06" ) ).Concat( FF( "sm_prop_urn_02" ) ).ToArray(), AnchorKind.Floor, 0.62f, 0.62f, 1.0f, false, Breakable: true ),
		new( "jar", FF( "sm_prop_jar_01", "sm_prop_jar_02", "sm_prop_pot_01", "sm_prop_pot_03" ), AnchorKind.Floor, 0.5f, 0.5f, 0.46f, false ),
		new( "book", FF( "sm_prop_book_01", "sm_prop_book_03", "sm_prop_book_04" ).Concat( DF( "sm_prop_book_02", "sm_prop_book_04", "sm_prop_book_open_02", "sm_prop_book_open_03" ) ).ToArray(), AnchorKind.Floor, 0.6f, 0.66f, 0.24f, false ),
		new( "book_pile", FF( "sm_prop_book_stack_01", "sm_prop_book_stack_02", "sm_prop_book_stack_03", "sm_prop_book_stack_04" ), AnchorKind.Floor, 0.75f, 0.6f, 0.57f, false ),
		new( "rubble", FF( "sm_env_rubble_pile_01", "sm_env_rubble_pile_02" ), AnchorKind.Floor, 2.5f, 2.05f, 0.5f, false ),
		new( "bricks", FF( "sm_env_rubble_brick_01", "sm_env_rubble_brick_03", "sm_env_rubble_brick_04" ), AnchorKind.Floor, 1.66f, 1.63f, 0.55f, false ),
		new( "brick", FF( "sm_env_rubble_brick_02" ), AnchorKind.Floor, 0.68f, 0.88f, 0.36f, false ),
		new( "pebbles", FF( "sm_env_rubble_pile_03" ), AnchorKind.Floor, 0.96f, 0.74f, 0.26f, false ),
		new( "plank", FF( "sm_env_rubble_plank_01", "sm_env_rubble_plank_02", "sm_env_rubble_plank_03" ), AnchorKind.Floor, 2.24f, 1.15f, 0.13f, false ),
		new( "rug", FF( "sm_prop_rug_01", "sm_prop_rug_01_alt_01" ), AnchorKind.Floor, 2.05f, 2.45f, 0.06f, false, Lift: 0.01f, Overlay: true ),
		new( "rug_flat", DF( "sm_prop_rug_01", "sm_prop_rug_01_alt_01" ), AnchorKind.Floor, 1.05f, 1.25f, 0.06f, false, Lift: 0.01f, Overlay: true ),
		// ---- corpses and bones
		new( "corpse", FF( "sm_prop_corpse_01", "sm_prop_corpse_02", "sm_prop_corpse_03", "sm_prop_corpse_03_alt_01", "sm_prop_corpse_05", "sm_prop_corpse_09", "sm_prop_corpse_experiment_01" ), AnchorKind.Floor, 1.4f, 2.1f, 0.4f, false ),
		new( "corpse_sitting", FF( "sm_prop_corpse_04", "sm_prop_corpse_06", "sm_prop_corpse_07", "sm_prop_corpse_08", "sm_prop_corpse_12", "sm_prop_corpse_prisoner_01", "sm_prop_corpse_prisoner_02" ), AnchorKind.WallFlush, 1.6f, 1.0f, 1.4f, true ),
		new( "corpse_hanging", FF( "sm_prop_corpse_10", "sm_prop_corpse_11", "sm_prop_corpse_prisoner_03", "sm_prop_corpse_prisoner_05" ), AnchorKind.CeilingHung, 0.55f, 0.7f, 2.2f, false, MountHeight: 1.5f, Hanger: "chain" ),
		// feet-pivoted bodies hung so the rope from the ceiling meets the head
		new( "hanged_body", DF( "sm_prop_gallows_01_body_01", "sm_prop_gallows_01_body_02" ), AnchorKind.CeilingHung, 0.5f, 0.55f, 1.8f, false, MountHeight: 3.7f, Hanger: "rope" ),
		new( "rope", DF( "sm_prop_rope_hanging_01" ), AnchorKind.CeilingHung, 0.24f, 0.24f, 2.09f, false ),
		new( "noose", DF( "sm_prop_noose_01", "sm_prop_noose_02" ), AnchorKind.CeilingHung, 0.31f, 0.12f, 0.71f, false, MountHeight: 2.09f, Hanger: "rope" ),
		new( "rope_bundle", DF( "sm_prop_noose_bundle_01", "sm_prop_noose_bundle_02" ), AnchorKind.Floor, 0.26f, 0.4f, 0.35f, false, Bury: 0f ),
		new( "corpse_pile", FF( "sm_prop_corpse_pile_02" ), AnchorKind.Floor, 3.9f, 2.6f, 0.9f, true ),
		new( "corpse_burnt", DF( "sm_prop_body_burnt_01", "sm_prop_body_burnt_02", "sm_prop_body_burnt_03" ), AnchorKind.Floor, 1.45f, 1.85f, 0.4f, false ),
		new( "corpse_burnt_kneeling", DF( "sm_prop_body_burnt_04", "sm_prop_body_burnt_05" ), AnchorKind.Floor, 1.2f, 1.0f, 0.85f, true ),
		new( "corpse_pile_burnt", DF( "sm_prop_body_pile_burnt_01" ), AnchorKind.Floor, 2.35f, 2.6f, 0.95f, true ),
		new( "corpse_spike", FF( "sm_prop_corpse_spike_01", "sm_prop_corpse_spike_02", "sm_prop_corpse_spike_03" ), AnchorKind.Floor, 0.7f, 0.7f, 4.3f, true ),
		new( "head_spike", FF( "sm_prop_head_spike_01", "sm_prop_head_spike_02" ), AnchorKind.Floor, 0.35f, 0.35f, 2.25f, true ),
		new( "carcass", DF( "sm_prop_animal_dead_01", "sm_prop_animal_dead_02" ), AnchorKind.Floor, 2.7f, 3.1f, 0.6f, true ),
		new( "limb", FF( "sm_prop_limb_01", "sm_prop_limb_02", "sm_prop_limb_03", "sm_prop_limb_04" ), AnchorKind.Floor, 0.4f, 0.8f, 0.25f, false ),
		new( "meat", FF( "sm_prop_meat_01", "sm_prop_meat_02", "sm_prop_meat_03" ), AnchorKind.Floor, 0.5f, 0.75f, 0.3f, false ),
		new( "organs", FF( "sm_prop_plague_organs_01" ), AnchorKind.Floor, 0.32f, 0.3f, 0.1f, false ),
		new( "bone", FF( "sm_prop_bone_01", "sm_prop_bone_02", "sm_prop_bone_03" ), AnchorKind.Floor, 0.45f, 0.5f, 0.3f, false ),
		new( "skull", FF( "sm_prop_skull_01", "sm_prop_skull_02", "sm_prop_skull_03", "sm_prop_skull_04", "sm_prop_skull_05", "sm_prop_skull_06" ).Concat( DF( "sm_prop_skull_01", "sm_prop_skull_02", "sm_prop_skull_03" ) ).ToArray(), AnchorKind.Floor, 0.45f, 0.45f, 0.35f, false ),
		new( "skull_animal", FF( "sm_prop_skull_animal_01", "sm_prop_skull_animal_02", "sm_prop_skeleton_bird_01", "sm_prop_skeleton_bird_02" ), AnchorKind.Floor, 0.65f, 1.05f, 0.42f, false ),
		new( "skull_pile", FF( "sm_prop_skull_pile_01", "sm_prop_skull_pile_02", "sm_prop_skull_pile_03" ).Concat( DF( "sm_prop_skull_pile_01", "sm_prop_skull_pile_02", "sm_prop_skull_pile_03" ) ).ToArray(), AnchorKind.Floor, 1.1f, 0.5f, 0.7f, false ),
		new( "bone_ribs", FF( "sm_env_bones_01", "sm_env_bones_02" ), AnchorKind.Floor, 0.7f, 0.85f, 1.2f, true ),
		new( "giant_skull", FF( "sm_env_bones_skull_01", "sm_env_bones_skull_02" ), AnchorKind.Floor, 1.9f, 2.4f, 1.8f, true ),
		new( "giant_hand", FF( "sm_env_bones_hand_01", "sm_env_bones_hand_02", "sm_env_bones_hand_03", "sm_env_bones_hand_04" ), AnchorKind.Floor, 1.3f, 1.5f, 1.9f, true ),
		// the user rescaled this vmdl to import_scale 0.1: 1.05 x 1.25 x 0.9 m about its middle now, a third of it bedded in
		new( "beast_ribs", FF( "sm_env_bones_ribs_01" ), AnchorKind.Floor, 1.05f, 1.25f, 0.9f, true, Bury: 0.3f ),
		// ---- cages and torture
		new( "cage_standing", FF( "sm_prop_cage_01" ), AnchorKind.Floor, 1.5f, 1.5f, 2.35f, true, Attach: FF( "sm_prop_cage_01_insert_01" ) ),
		new( "cage_large", DF( "sm_prop_cage_large_01" ), AnchorKind.Floor, 1.55f, 3.2f, 2.25f, true, Attach: DF( "sm_prop_cage_large_01_insert_01" ) ),
		new( "cage_small", DF( "sm_prop_cage_small_01" ), AnchorKind.Floor, 1.3f, 1.45f, 1.25f, true ),
		new( "cage_wall", FF( "sm_prop_cage_02", "sm_prop_cage_03" ), AnchorKind.WallMounted, 1.2f, 0.8f, 2.0f, false, MountHeight: 1.0f, Attach: new[] { Ff( "sm_prop_cage_02_insert_01" ), null } ),
		new( "plinth_cage", FF( "sm_prop_plinth_01" ), AnchorKind.Floor, 0.8f, 0.7f, 1.85f, true, Attach: FF( "sm_prop_plinth_01_cage_01" ) ),
		new( "torture_device", FF( "sm_prop_torture_device_01" ), AnchorKind.Floor, 2.0f, 1.35f, 1.5f, true, Attach: FF( "sm_prop_torture_device_01_insert_01" ) ),
		// a set item's footprint must stay inside its cell (reach = half the longer side + the wall's half thickness < 2.5 m): the
		// 4.7 m wheel and wagon are scaled to fit
		new( "wheel", FF( "sm_prop_wheel_01" ), AnchorKind.Floor, 3.4f, 4.2f, 3.0f, true, Attach: FF( "sm_prop_wheel_01_insert_01" ), Scale: 0.9f ),
		new( "whipping_post", DF( "sm_prop_post_01" ), AnchorKind.Floor, 0.55f, 1.65f, 3.4f, true, MaxHeight: 3.4f ),
		new( "gallows", DF( "sm_prop_gallows_01" ), AnchorKind.Floor, 4.9f, 1.72f, 3.43f, true, Scale: 0.8f ),
		new( "gibbet", DF( "sm_prop_gibbet_01" ), AnchorKind.Floor, 3.0f, 2.9f, 4.33f, true, Attach: DF( "sm_prop_gibbet_01_cage_01" ), Scale: 0.9f ),
		new( "gibbet_fallen", DF( "sm_prop_gibbet_01_damaged_01" ), AnchorKind.Floor, 2.2f, 1.1f, 1.0f, true ),
		new( "pyre", DF( "sm_prop_pyre_02", "sm_prop_pyre_02_burnt_01" ), AnchorKind.Floor, 3.2f, 3.3f, 1.9f, true ),
		new( "pyre_stake", DF( "sm_prop_pyre_stake_01", "sm_prop_pyre_stake_01_burnt_01", "sm_prop_pyre_stake_01_burnt_02", "sm_prop_pyre_stake_01_demon_01" ), AnchorKind.Floor, 3.3f, 2.0f, 4.6f, true, Scale: 0.9f ),
		new( "stake_burnt", DF( "sm_prop_pyre_stake_02_burnt_02", "sm_prop_pyre_stake_02_burnt_03", "sm_prop_pyre_stake_02_burnt_04" ), AnchorKind.Floor, 0.7f, 1.25f, 2.1f, true ),
		new( "stake", DF( "sm_prop_pyre_stake_02", "sm_prop_pyre_stake_02_burnt_01" ), AnchorKind.Floor, 0.2f, 0.55f, 2.2f, true ),
		new( "shackles", FF( "sm_prop_shackles_wall_01" ), AnchorKind.WallMounted, 0.35f, 0.2f, 0.8f, false, MountHeight: 1.8f ),
		new( "wrist_bonds", FF( "sm_prop_wrist_bonds_01" ), AnchorKind.WallMounted, 0.65f, 0.15f, 0.9f, false, MountHeight: 1.3f ),
		new( "key_ring", FF( "sm_prop_key_ring_01" ), AnchorKind.WallMounted, 0.1f, 0.2f, 0.26f, false, MountHeight: 1.5f ),
		// ---- chains and the hanging gallery
		new( "chain_pile", FF( "sm_prop_chain_pile_01", "sm_prop_chain_pile_02" ), AnchorKind.Floor, 1.5f, 1.55f, 0.1f, false ),
		new( "chain_long", new[] { "@chain_3m" }, AnchorKind.CeilingHung, 0.11f, 0.11f, 3.0f, false ),
		// the Fortress swag chains stay defined for when their vmat renders; no table names them
		new( "chain_swag", FF( "sm_prop_chain_06", "sm_prop_chain_07" ), AnchorKind.CeilingHung, 5.0f, 0.05f, 1.85f, false ),
		new( "chain_swag_short", FF( "sm_prop_chain_04", "sm_prop_chain_05" ), AnchorKind.CeilingHung, 2.65f, 0.05f, 1.2f, false ),
		new( "chain_hook", FF( "sm_prop_chain_08" ), AnchorKind.CeilingHung, 1.55f, 0.1f, 1.5f, false ),
		new( "chain_ring", DF( "sm_prop_chain_connector_01" ), AnchorKind.CeilingHung, 0.42f, 0.42f, 0.18f, false ),
		new( "skull_hanging", FF( "sm_prop_skull_hanging_01" ), AnchorKind.CeilingHung, 0.67f, 0.4f, 1.5f, false, MountHeight: 1.0f, Hanger: "chain_short" ),
		new( "bell", DF( "sm_prop_bell_large_01" ), AnchorKind.CeilingHung, 1.4f, 1.35f, 1.5f, false ),
		new( "lamp_hanging", DF( "sm_prop_lamp_hanging_01" ), AnchorKind.CeilingHung, 0.4f, 0.45f, 1.23f, false, Light: LightKind.Chandelier ),
		new( "chandelier_big", DF( "sm_prop_chandelier_01" ), AnchorKind.CeilingHung, 2.25f, 2.2f, 1.7f, false, Light: LightKind.Chandelier ),
		new( "chandelier_fallen", FF( "sm_prop_chandelier_01_damaged_01" ), AnchorKind.Floor, 1.3f, 1.1f, 1.0f, false ),
		// modelled 0.8 m above its pivot on chains that end 2.56 m up: hung so the pivot lands at floor level and a long chain bridges the gap
		new( "table_suspended", FF( "sm_prop_table_suspended_01" ), AnchorKind.CeilingHung, 1.65f, 2.2f, 2.56f, false, MountHeight: 4.9f, Hanger: "chain_long" ),
		// ---- the walls' dressing: trophies (the sigil prop), plaques, runes, stains, growth
		new( "wall_trophy", DF( "sm_prop_head_mounted_01" ).Concat( FF( "sm_prop_head_display_01", "sm_prop_head_display_02", "sm_prop_shackles_wall_01" ) ).ToArray(), AnchorKind.WallMounted, 1.05f, 1.05f, 1.41f, false, MountHeight: 1.6f ),
		new( "wall_plaque", FF( "sm_prop_plaque_01" ), AnchorKind.WallMounted, 1.5f, 0.25f, 2.75f, false, MountHeight: 0.5f ),
		new( "wall_rune", FF( "sm_prop_rune_01", "sm_prop_rune_02", "sm_prop_rune_03", "sm_prop_rune_04", "sm_prop_rune_05", "sm_prop_rune_06", "sm_prop_rune_07", "sm_prop_rune_08" ), AnchorKind.WallMounted, 0.8f, 0.06f, 0.65f, false, MountHeight: 1.6f ),
		new( "wall_greeble", FF( "sm_prop_greeble_04", "sm_prop_greeble_05", "sm_prop_greeble_12", "sm_prop_greeble_13" ), AnchorKind.WallMounted, 1.5f, 0.15f, 1.25f, false, MountHeight: 2.2f ),
		new( "wall_band", FF( "sm_prop_greeble_01", "sm_prop_greeble_03" ), AnchorKind.WallMounted, 3.6f, 0.2f, 1.5f, false, MountHeight: 2.6f ),
		new( "banner_pole", new[] { "@banner_pole" }, AnchorKind.WallMounted, 1.24f, 0.52f, 3.9f, false, MountHeight: 3.6f ),
		// no grunge or moss decals and NO thorns at all: the Dark Fantasy ones render as a white plane (user) and the Fortress
		// ones sit on the same cutout-vmat trick; only the corner webs stay
		new( "web_corner", FF( "sm_env_spiderweb_06", "sm_env_spiderweb_07" ), AnchorKind.Floor, 1.4f, 1.0f, 1.4f, false ),
		// ---- the laboratory
		new( "specimen_tank", FF( "sm_prop_specimen_tank_01" ), AnchorKind.Floor, 1.55f, 1.2f, 2.85f, true ),
		new( "specimen_tank_big", FF( "sm_prop_specimen_tank_02" ), AnchorKind.Floor, 2.7f, 3.5f, 3.2f, true ),
		new( "specimen_jar", FF( "sm_prop_specimen_jar_01", "sm_prop_specimen_jar_01", "sm_prop_specimen_jar_01", "sm_prop_specimen_jar_02", "sm_prop_specimen_jar_02", "sm_prop_specimen_jar_02", "sm_prop_specimen_jar_03" ), AnchorKind.Floor, 0.5f, 0.5f, 0.7f, false,
			Attach: FF( "sm_prop_specimen_jar_01_insert_01", "sm_prop_specimen_jar_01_insert_02", "sm_prop_specimen_jar_01_insert_03", "sm_prop_specimen_jar_02_insert_01", "sm_prop_specimen_jar_02_insert_02", "sm_prop_specimen_jar_02_insert_03" ).Append( null ).ToArray() ),
		new( "alchemy_tools", FF( "sm_prop_alchemy_tool_04", "sm_prop_alchemy_tools_01", "sm_prop_alchemy_tools_02", "sm_prop_alchemy_tools_03" ), AnchorKind.Floor, 0.75f, 0.5f, 0.86f, false ),
		new( "tool_wall", FF( "sm_prop_surgical_tool_01" ), AnchorKind.WallMounted, 0.45f, 0.1f, 1.0f, false, MountHeight: 1.4f ),
		new( "papers_wall", FF( "sm_prop_research_papers_01", "sm_prop_research_papers_02", "sm_prop_research_papers_03" ), AnchorKind.WallMounted, 2.1f, 0.15f, 1.1f, false, MountHeight: 1.2f ),
		// ---- the chapel
		new( "altar_slab", FF( "sm_prop_altar_01" ), AnchorKind.Floor, 2.2f, 1.1f, 0.66f, true, Top: 0.66f ),
		new( "altar_table", DF( "sm_prop_altar_table_01" ), AnchorKind.Floor, 2.05f, 0.9f, 0.86f, true, Top: 0.84f ),
		new( "tabernacle", DF( "sm_prop_tabernacle_01" ), AnchorKind.WallFlush, 3.65f, 1.25f, 1.45f, true ),
		new( "organ", DF( "sm_prop_organ_01" ), AnchorKind.WallFlush, 2.9f, 0.95f, 4.05f, true, Scale: 0.4f ),
		new( "font", DF( "sm_prop_holy_water_01" ), AnchorKind.Floor, 1.1f, 1.06f, 1.31f, true ),
		new( "lectern", DF( "sm_prop_lecturn_01" ).Concat( FF( "sm_prop_lectern_01" ) ).ToArray(), AnchorKind.Floor, 0.8f, 0.8f, 1.5f, true ),
		new( "pew", new[] { "@pew" }, AnchorKind.Floor, 2.0f, 0.76f, 1.03f, true ),
		new( "pew_long", new[] { "@pew_long" }, AnchorKind.Floor, 4.0f, 0.76f, 1.04f, true ),
		new( "pew_broken", DF( "sm_prop_pew_large_01_damaged_02", "sm_prop_pew_large_01_damaged_03" ), AnchorKind.Floor, 2.0f, 0.85f, 1.05f, true ),
		new( "candle_rack", DF( "sm_prop_candle_rack_01", "sm_prop_candle_rack_01", "sm_prop_candle_rack_01" ), AnchorKind.Floor, 2.15f, 0.85f, 1.31f, true, Light: LightKind.Candle,
			Attach: DF( "sm_prop_candle_rack_01_insert_01", "sm_prop_candle_rack_01_insert_02", "sm_prop_candle_rack_01_insert_03" ) ),
		new( "candelabra_tall", DF( "sm_prop_candelabra_03" ), AnchorKind.Floor, 1.35f, 0.8f, 3.15f, true, Light: LightKind.Candle ),
		new( "candelabra_small", DF( "sm_prop_candelabra_small_01", "sm_prop_candelabra_small_02", "sm_prop_candelabra_small_03" ), AnchorKind.Floor, 0.75f, 0.55f, 1.0f, false, Light: LightKind.Candle ),
		new( "candle_wall", DF( "sm_prop_candle_wall_01" ), AnchorKind.WallMounted, 0.61f, 0.23f, 0.96f, false, MountHeight: 1.5f, Light: LightKind.Candle ),
		new( "wax", DF( "sm_prop_candle_blob_01", "sm_prop_candle_blob_02", "sm_prop_candle_drip_01", "sm_prop_candle_drip_02" ), AnchorKind.Floor, 0.5f, 0.35f, 0.06f, false, Overlay: true ),
		new( "incense", DF( "sm_prop_incense_01", "sm_prop_incense_02", "sm_prop_incense_03" ), AnchorKind.Floor, 0.27f, 0.27f, 0.61f, false ),
		new( "demon_statue", DF( "sm_prop_demon_01" ), AnchorKind.WallFlush, 3.15f, 1.4f, 1.65f, true ),
		new( "demon_ward", DF( "sm_prop_demon_ward_01" ), AnchorKind.Floor, 1.5f, 0.75f, 3.2f, true ),
		new( "gargoyle_statue", DF( "sm_prop_gargoyle_statue_01", "sm_prop_gargoyle_statue_alt_01" ), AnchorKind.Floor, 2.45f, 1.1f, 1.65f, true ),
		new( "gargoyle_wall", DF( "sm_prop_gargoyle_01", "sm_prop_gargoyle_02", "sm_prop_gargoyle_03" ), AnchorKind.WallMounted, 1.92f, 1.5f, 1.3f, false, MountHeight: 2.6f ),
		new( "statue_chained", FF( "sm_prop_statue_02" ), AnchorKind.Floor, 1.8f, 1.85f, 4.6f, true, Attach: FF( "sm_prop_statue_02_chain_01" ), Scale: 0.8f ),
		new( "statue_pedestal", DF( "sm_prop_statue_01", "sm_prop_statue_02" ), AnchorKind.Floor, 1.9f, 1.15f, 2.4f, true ),
		new( "statue_base", FF( "sm_prop_statue_base_01" ), AnchorKind.Floor, 0.93f, 0.93f, 0.23f, false ),
		// ---- the crypt
		new( "tomb", DF( "sm_prop_tomb_01" ), AnchorKind.Floor, 1.4f, 2.65f, 1.76f, true ),
		new( "tomb_low", DF( "sm_prop_tomb_02" ), AnchorKind.Floor, 1.3f, 2.5f, 1.03f, true, Scale: 0.88f ),
		new( "tombstone", DF( "sm_prop_tomb_stone_01", "sm_prop_tomb_stone_02", "sm_prop_tomb_stone_03", "sm_prop_tomb_stone_04" ).Concat( FF( "sm_prop_grave_01" ) ).ToArray(), AnchorKind.Floor, 1.1f, 0.65f, 1.8f, true ),
		new( "tombstone_plain", DF( "sm_prop_tomb_stone_basic_01", "sm_prop_tomb_stone_basic_02" ), AnchorKind.Floor, 1.11f, 0.37f, 1.53f, true ),
		new( "grave_slab", DF( "sm_prop_tomb_stone_05", "sm_prop_tomb_stone_06" ), AnchorKind.Floor, 1.25f, 2.45f, 0.33f, false, Scale: 0.85f ),
		new( "mausoleum", DF( "sm_prop_crypt_01", "sm_prop_crypt_02" ), AnchorKind.Floor, 3.55f, 3.8f, 4.7f, true, Scale: 0.75f ),
		new( "tomb_blanket", FF( "sm_prop_tomb_blanket_01" ), AnchorKind.Floor, 1.71f, 0.68f, 0.45f, false, Bury: 0f ),
		new( "plushie", FF( "sm_prop_plushie_01", "sm_prop_plushie_02", "sm_prop_plushie_03" ), AnchorKind.Floor, 0.46f, 0.35f, 0.63f, false ),
		// ---- stores and the butchery
		new( "sack", DF( "sm_prop_sack_01", "sm_prop_sack_02", "sm_prop_sack_03", "sm_prop_sack_04", "sm_prop_sack_05" ), AnchorKind.Floor, 0.6f, 0.6f, 0.72f, false ),
		new( "keg", DF( "sm_prop_keg_01" ), AnchorKind.Floor, 1.3f, 1.2f, 1.9f, true ),
		new( "crate_broken", FF( "sm_prop_crate_destroyed_01" ), AnchorKind.Floor, 1.07f, 1.07f, 1.11f, true ),
		new( "bucket", DF( "sm_prop_meat_bucket_01" ), AnchorKind.Floor, 0.5f, 0.5f, 0.47f, false ),
		new( "trough", FF( "sm_prop_trough_01" ), AnchorKind.WallFlush, 3.2f, 1.3f, 1.7f, true ),
		new( "counter", DF( "sm_prop_counter_bar_01" ), AnchorKind.WallFlush, 2.55f, 0.82f, 1.07f, true, Top: 1.07f ),
		new( "cupboard", FF( "sm_prop_cupboard_01" ), AnchorKind.WallFlush, 1.78f, 0.9f, 2.45f, true ),
		new( "shelf", FF( "sm_prop_shelf_02", "sm_prop_shelf_02", "sm_prop_shelf_02", "sm_prop_shelf_02_damaged_01", "sm_prop_shelf_03" ).Concat( DF( "sm_prop_shelf_01" ) ).ToArray(), AnchorKind.WallFlush, 2.35f, 1.2f, 2.41f, true,
			Attach: new[] { Ff( "sm_prop_shelf_02_insert_01" ), Ff( "sm_prop_shelf_02_insert_02" ), Ff( "sm_prop_shelf_02_insert_03" ), null, null, Df( "sm_prop_shelf_01_insert_01" ) } ),
		new( "shelf_wall", FF( "sm_prop_shelf_01", "sm_prop_shelf_01", "sm_prop_shelf_04", "sm_prop_shelf_04" ), AnchorKind.WallMounted, 1.7f, 0.35f, 0.95f, false, MountHeight: 1.3f,
			Attach: FF( "sm_prop_shelf_01_insert_01", "sm_prop_shelf_01_insert_02", "sm_prop_shelf_04_insert_01", "sm_prop_shelf_04_insert_02" ) ),
		new( "wheelbarrow", FF( "sm_prop_wheel_barrow_01" ), AnchorKind.Floor, 0.8f, 2.05f, 0.75f, true ),
		new( "wagon_wrecked", DF( "sm_prop_wagon_01_damaged_01" ), AnchorKind.Floor, 2.8f, 4.2f, 1.65f, true, Scale: 0.8f ),
		new( "well", DF( "sm_prop_well_01" ), AnchorKind.Floor, 3.65f, 3.2f, 2.5f, true, Attach: DF( "sm_prop_well_01_insert_01" ) ),
		new( "desk", FF( "sm_prop_desk_01" ), AnchorKind.Floor, 2.45f, 1.3f, 0.86f, true, Top: 0.84f ),
		new( "desk_reading", DF( "sm_prop_desk_reading_01" ), AnchorKind.Floor, 0.9f, 0.58f, 1.2f, true ),   // slanted top: nothing stands on it
		new( "goblet", DF( "sm_prop_goblet_01", "sm_prop_goblet_02", "sm_prop_goblet_03" ).Concat( FF( "sm_prop_goblet_01", "sm_prop_goblet_02", "sm_prop_goblet_03" ) ).ToArray(), AnchorKind.Floor, 0.3f, 0.3f, 0.46f, false ),
		new( "plate", DF( "sm_prop_plate_01", "sm_prop_plate_02", "sm_prop_plate_03", "sm_prop_plate_04" ).Concat( FF( "sm_prop_plate_02", "sm_prop_plate_03" ) ).ToArray(), AnchorKind.Floor, 0.44f, 0.44f, 0.12f, false ),
		new( "bowl", DF( "sm_prop_bowl_01", "sm_prop_bowl_02", "sm_prop_bowl_03" ), AnchorKind.Floor, 0.34f, 0.33f, 0.16f, false ),
		new( "bottle", DF( "sm_prop_bottle_01", "sm_prop_bottle_02" ).Concat( FF( "sm_prop_bottle_01", "sm_prop_bottle_02", "sm_prop_bottle_03", "sm_prop_bottle_04", "sm_prop_bottle_05" ) ).ToArray(), AnchorKind.Floor, 0.36f, 0.36f, 0.62f, false ),
		new( "mug", DF( "sm_prop_mug_beer_01" ).Concat( FF( "sm_prop_cup_01" ) ).ToArray(), AnchorKind.Floor, 0.23f, 0.23f, 0.23f, false ),
		new( "food", FF( "sm_prop_food_01", "sm_prop_food_02", "sm_prop_food_03", "sm_prop_food_04", "sm_prop_food_05", "sm_prop_food_06" ).Concat( DF( "sm_prop_bread_rotten_01" ) ).ToArray(), AnchorKind.Floor, 0.7f, 1.05f, 0.25f, false ),
		new( "food_scraps", DF( "sm_prop_apple_rotten_01", "sm_prop_pear_rotten_01", "sm_prop_bread_slice_rotten_01", "sm_prop_fish_rotten_01", "sm_prop_meat_rotten_01", "sm_prop_meat_rotten_02", "sm_prop_bones_rotten_01", "sm_prop_sludge_rotten_01" ), AnchorKind.Floor, 0.3f, 0.4f, 0.16f, false ),
		new( "papers", FF( "sm_prop_paper_01", "sm_prop_paper_02", "sm_prop_letter_01", "sm_prop_scroll_01", "sm_prop_scroll_02", "sm_prop_scroll_open_01", "sm_prop_scroll_open_02" ).Concat( DF( "sm_prop_papers_01", "sm_prop_papers_02", "sm_prop_papers_03", "sm_prop_scroll_01", "sm_prop_scroll_02", "sm_prop_scroll_03" ) ).ToArray(), AnchorKind.Floor, 1.0f, 1.0f, 0.3f, false, Overlay: true ),
		// ---- the siege store
		new( "ballista", FF( "sm_prop_ballista_01" ), AnchorKind.Floor, 3.3f, 3.8f, 1.9f, true, Scale: 0.9f ),
		new( "cannon", FF( "sm_veh_cannon_01" ), AnchorKind.Floor, 2.7f, 3.3f, 2.5f, true ),
		new( "arrow_launcher", FF( "sm_veh_arrow_launcher_01" ), AnchorKind.Floor, 3.1f, 3.8f, 3.3f, true, Scale: 0.9f ),
		new( "slingshot", FF( "sm_prop_slingshot_01" ), AnchorKind.Floor, 2.85f, 2.45f, 1.75f, true ),
		new( "boiling_oil", FF( "sm_prop_boiling_oil_01" ), AnchorKind.Floor, 2.7f, 3.3f, 2.2f, true, Attach: FF( "sm_prop_boiling_oil_01_insert_01" ) ),
		new( "barricade", FF( "sm_prop_barricade_01" ), AnchorKind.WallFlush, 1.75f, 1.8f, 2.1f, true ),
		new( "barricade_low", FF( "sm_prop_barricade_02" ).Concat( DF( "sm_prop_barricade_03", "sm_prop_barricade_04" ) ).ToArray(), AnchorKind.Floor, 3.5f, 2.1f, 1.9f, true ),
		new( "arrow_basket", FF( "sm_prop_arrow_basket_01" ), AnchorKind.Floor, 0.55f, 0.6f, 0.85f, false ),
		new( "cannonball", FF( "sm_prop_cannonball_01" ), AnchorKind.Floor, 0.3f, 0.3f, 0.3f, false ),
		// ---- odds and ends
		new( "dial", FF( "sm_prop_dial_01" ), AnchorKind.Floor, 1.5f, 1.5f, 2.8f, true ),
		new( "pipe_wall", new[] { "@pipe_wall" }, AnchorKind.WallMounted, 1.53f, 0.42f, 0.42f, false, MountHeight: 2.6f ),
		new( "wall_drain", FF( "sm_prop_sewer_grate_02" ), AnchorKind.WallMounted, 0.55f, 0.25f, 0.36f, false, MountHeight: 0.2f ),
		new( "sewer_grate", FF( "sm_prop_sewer_grate_01" ), AnchorKind.Floor, 2.4f, 2.4f, 0.32f, false, Overlay: true ),
		new( "ladder", FF( "sm_prop_ladder_01" ), AnchorKind.WallMounted, 1.0f, 0.25f, 2.75f, false, MountHeight: 0f, Embed: -0.02f ),
		// user: above the bone skirt (its bowl spans 0.69..1.68 now)
		new( "wall_font", FF( "sm_prop_fountain_01" ), AnchorKind.WallMounted, 0.72f, 0.4f, 1.0f, false, MountHeight: 1.0f ),
		new( "bell_wall", DF( "sm_prop_bell_01" ), AnchorKind.WallMounted, 0.77f, 0.51f, 0.92f, false, MountHeight: 2.2f ),
		new( "crystal", FF( "sm_prop_crystal_01", "sm_prop_crystal_02", "sm_prop_crystal_03" ), AnchorKind.Floor, 0.9f, 0.9f, 2.0f, true, Bury: 0f ),
		new( "relic", FF( "sm_prop_relic_01" ), AnchorKind.Floor, 0.36f, 0.39f, 0.54f, false ),
		new( "portal", FF( "sm_prop_portal_01" ), AnchorKind.Floor, 1.7f, 0.55f, 3.36f, true ),
		new( "fire_pit", DF( "sm_prop_fire_pit_01" ), AnchorKind.Floor, 2.5f, 1.5f, 1.7f, true, Attach: DF( "sm_prop_fire_pit_01_rotisserie_01" ), Light: LightKind.Brazier ),
		new( "fire_pit_round", FF( "sm_prop_fireplace_02" ), AnchorKind.Floor, 2.15f, 2.15f, 1.0f, true, Light: LightKind.Brazier ),
		new( "cauldron_big", FF( "sm_prop_cauldron_02" ), AnchorKind.Floor, 2.7f, 2.9f, 2.3f, true, Attach: FF( "sm_prop_cauldron_02_insert_01" ) ),
		new( "tent", FF( "sm_prop_tent_02" ), AnchorKind.WallFlush, 3.95f, 2.0f, 2.45f, true, Scale: 0.85f ),
		new( "bookcase_double", FF( "sm_prop_bookshelf_01", "sm_prop_bookshelf_01" ), AnchorKind.Floor, 2.26f, 1.78f, 2.72f, true, Top: 1.48f, Attach: FF( "sm_prop_bookshelf_01_insert_01", "sm_prop_bookshelf_01_insert_02" ), Decay: FF( "sm_prop_bookshelf_01_damaged_01" ) ),
		// ---- borrowed from the stone and forge-hold kits (they fit a gaol): the DungeonPack torture timber and bones under new
		// ids so the gaol's own stay, and the Realms pieces copied as they are
		new( "stocks_stone", Pack( "sm_prop_toture_stocks_01" ), AnchorKind.Floor, 1.72f, 1.25f, 1.25f, true ),
		new( "rack_stone", Pack( "sm_prop_toture_stretchtable_01" ), AnchorKind.Floor, 2.15f, 3.7f, 2.35f, true ),
		new( "cage_stone", Pack( "sm_prop_toture_cage_01" ), AnchorKind.Floor, 0.96f, 0.96f, 2.0f, true ),
		new( "skeleton_stone", Pack( "sm_prop_skeleton_01" ), AnchorKind.Floor, 0.7f, 1.9f, 0.35f, false ),
		new( "bone_heap", Pack( "sm_env_bonepile_01", "sm_env_bonepile_02", "sm_env_bonepile_03" ), AnchorKind.Floor, 2.5f, 2.8f, 0.9f, false ),
		new( "cage_hung_stone", Pack( "sm_prop_skeleton_cage_01" ), AnchorKind.CeilingHung, 0.8f, 0.5f, 1.7f, false, MountHeight: 1.0f, Hanger: "chain_short", Nudge: 0.178f ),
		// (copied from the Realms kit rather than referenced: the two parts of this partial record initialise in an order
		// the compiler picks, and a reference across them could read a null array)
		new( "sarcophagus", Realm( "sm_prop_sarcophagus_01" ), AnchorKind.Floor, 1.66f, 2.46f, 1.11f, true, Top: 1.11f ),
		new( "dead_body", Realm( "sm_prop_deadbody_nomad_02", "sm_prop_deadbody_nomad_03" ), AnchorKind.Floor, 2.41f, 1.47f, 0.56f, false ),
		new( "statue_broken", Realm( "sm_env_statue_broken_01", "sm_env_statue_broken_02", "sm_env_statue_broken_03", "sm_env_statue_broken_05", "sm_env_statue_broken_06" ), AnchorKind.Floor, 1.55f, 1.23f, 1.96f, true ),   // 04 out (user)
		new( "statue_head", Realm( "sm_env_statue_broken_01_head" ), AnchorKind.Floor, 0.9f, 0.89f, 0.52f, false ),
		new( "charm_hanging", Realm( "sm_prop_charm_hanging_01", "sm_prop_charm_hanging_02", "sm_prop_charm_hanging_03" ), AnchorKind.CeilingHung, 0.2f, 0.18f, 0.74f, false ),
		new( "bedroll", Realm( "sm_prop_camp_roll_02", "sm_prop_camp_roll_01" ), AnchorKind.Floor, 2.7f, 0.44f, 0.88f, false, Bury: 0f ),
		new( "crystal_rock", Realm( "sm_prop_crystal_rock_a_01", "sm_prop_crystal_rock_b_01", "sm_prop_crystal_rock_c_01", "sm_prop_crystal_rock_d_01", "sm_prop_crystal_rock_e_01", "sm_prop_crystal_rock_f_01", "sm_prop_crystal_rock_g_01" ),
			AnchorKind.Floor, 0.9f, 0.9f, 1.27f, true, Bury: 0f ),
		new( "weapon_plaque", Realm( "sm_prop_hanging_weapon_01", "sm_prop_hanging_weapon_02", "sm_prop_hanging_weapon_03" ), AnchorKind.WallMounted, 0.99f, 0.15f, 0.92f, false, MountHeight: 1.6f ),
		new( "wall_sigil", Realm( "sm_prop_hell_symbol_01", "sm_prop_hell_symbol_03", "sm_prop_hell_symbol_05" ), AnchorKind.WallMounted, 3.0f, 0.28f, 3.31f, false, MountHeight: 2.5f ),
	};

	/// <summary>Per-model corrections to the gaol's Top surfaces (measured upward faces): drop below the def's Top, back toward the rear.</summary>
	private static readonly Dictionary<string, (float drop, float back)> GaolTopFix = new Dictionary<string, (float, float)>( DecorCatalog.TopFix )
	{
		[Df( "sm_prop_crate_02" )] = (-0.12f, 0f),            // lid at 0.47
		[Ff( "sm_prop_crate_01" )] = (0.31f, 0f),             // an open box: things sit inside at 0.04
		[Df( "sm_prop_barrel_open_01" )] = (0.61f, 0f),       // open: inside at 0.21
	};

	/// <summary>What stands on the gaol's tables, desks, altars and shelves: skulls, organs and jars among the candles.</summary>
	private static readonly Dictionary<string, FillEntry[]> GaolTops = new()
	{
		["table_long"] = E( ("candles", 20), ("skull", 15), ("plate", 10), ("goblet", 10), ("book_pile", 10), ("food", 8), ("organs", 5), ("meat", 5), ("specimen_jar", 5), ("", 12) ),
		["table_round"] = E( ("candles", 25), ("skull", 15), ("bottle", 10), ("goblet", 10), ("plate", 10), ("book", 8), ("", 22) ),
		["table_low"] = E( ("candle", 30), ("bowl", 20), ("skull", 15), ("mug", 10), ("", 25) ),
		["stone_table"] = E( ("candles", 25), ("meat", 15), ("plate", 15), ("skull", 10), ("limb", 10), ("", 25) ),
		["desk"] = E( ("book", 25), ("papers", 20), ("candelabra_small", 15), ("specimen_jar", 10), ("skull", 10), ("", 20) ),
		["counter"] = E( ("plate", 20), ("goblet", 15), ("mug", 15), ("bottle", 15), ("food", 15), ("candles", 10), ("", 10) ),
		["altar_slab"] = E( ("skull", 30), ("candles", 25), ("organs", 15), ("bone", 10), ("", 20) ),
		["altar_table"] = E( ("candles", 40), ("skull", 20), ("relic", 10), ("incense", 10), ("", 20) ),
		["coffin"] = E( ("candles", 30), ("skull", 25), ("bone", 15), ("", 30) ),
		["crate"] = E( ("skull", 20), ("candle", 15), ("bottle", 15), ("", 50) ),
		["barrel"] = E( ("mug", 20), ("bottle", 20), ("candle", 15), ("", 45) ),
		// no candles on the wooden shelves (user); the damaged twins carry nothing at all (Decor.OnTop)
		["bookcase"] = E( ("book", 30), ("specimen_jar", 20), ("skull", 20), ("", 30) ),
		["bookcase_small"] = E( ("book", 30), ("specimen_jar", 20), ("skull", 20), ("", 30) ),
		["bookcase_double"] = E( ("book", 30), ("specimen_jar", 20), ("skull", 20), ("", 30) ),
		["stool"] = E( ("skull", 20), ("candle", 20), ("book", 15), ("", 45) ),
	};

	/// <summary>Bones underfoot in every room (the litter pass only takes small, low, non-blocking pieces): every roll
	/// lands one, and the kit's LitterScale rolls more often (user: twice the bones).</summary>
	private static readonly FillEntry[] GaolBoneLitter = E( ("skull", 28), ("bone", 28), ("bone_pile_small", 20), ("skull_animal", 12), ("limb", 12), ("critter_skull", 4), ("critter_ribs", 3), ("critter_spine", 3) );

	/// <summary>The gaol's set pieces, all 1 x 1: U toward the room's entrance, V right, cell units; H metres up.</summary>
	private static readonly SetPiece[] GaolSets =
	{
		new( "rack_room", 1, 1, new SetItem[]
		{
			new( "stretch_table", 0, 0, 90 ), new( "brazier", 0.35f, 0.35f, 0, 0.8 ), new( "bucket", -0.35f, 0.3f, 0, 0.8 ),
			new( "bone_pile_small", -0.3f, -0.35f, 0, 0.8 ), new( "limb", 0.35f, -0.3f, 30, 0.7 ), new( "organs", -0.32f, -0.2f, 0, 0.7 ),
		} ),
		new( "breaking_wheel", 1, 1, new SetItem[]
		{
			new( "wheel", 0, 0, 0 ), new( "chain_pile", 0.35f, 0.35f, 0, 0.7 ), new( "head_spike", -0.35f, 0.35f, 0, 0.8 ), new( "head_spike", -0.35f, -0.35f, 0, 0.8 ),
			new( "skull", 0.15f, -0.38f, 0, 0.8 ), new( "bone", 0.38f, -0.2f, 40, 0.7 ),
		} ),
		new( "impaled_ring", 1, 1, new SetItem[]
		{
			new( "corpse_spike", 0.32f, 0, 0 ), new( "corpse_spike", -0.2f, 0.32f, 40 ), new( "corpse_spike", -0.2f, -0.32f, 80, 0.8 ),
			new( "skull_pile", 0, 0, 0, 0.8 ), new( "bone", 0.1f, 0.3f, 0, 0.6 ), new( "bone", -0.35f, 0, 0, 0.6 ), new( "skull", 0.3f, -0.3f, 0, 0.6 ),
		} ),
		new( "pyre_court", 1, 1, new SetItem[]
		{
			new( "pyre", 0, 0, 0 ), new( "stake_burnt", 0.38f, 0.35f, 0, 0.8 ), new( "corpse_burnt", 0.35f, -0.35f, 45, 0.7 ),
			new( "corpse_burnt", -0.35f, 0.35f, 200, 0.7 ), new( "corpse_burnt_kneeling", -0.35f, -0.3f, 300, 0.7 ),
		} ),
		new( "stake_burning", 1, 1, new SetItem[]
		{
			new( "pyre_stake", 0, 0, 0 ), new( "corpse_burnt", 0.38f, 0.35f, 30, 0.7 ), new( "skull_pile", -0.38f, -0.35f, 0, 0.7 ),
			new( "stake", 0.4f, -0.38f, 0, 0.6 ), new( "bone_pile_small", -0.36f, 0.3f, 0, 0.6 ),
		} ),
		new( "gallows_yard", 1, 1, new SetItem[]
		{
			new( "gallows", 0, 0, 0 ), new( "hanged_body", 0, 0.18f, 0, 1, 2.0f ), new( "hanged_body", 0, -0.2f, 20, 0.6, 2.0f ),
			new( "skull_pile", 0.35f, 0.35f, 0, 0.8 ), new( "rope_bundle", -0.35f, 0.32f, 0, 0.6 ), new( "bone_heap", -0.3f, -0.3f, 0, 0.5 ),
		} ),
		new( "gibbet_post", 1, 1, new SetItem[]
		{
			new( "gibbet", 0, 0, 0 ), new( "corpse", 0.3f, 0.3f, 30, 0.8 ), new( "bone_pile_small", -0.35f, 0.25f, 0, 0.7 ), new( "skull", 0.15f, -0.3f, 0, 0.8 ),
		} ),
		new( "charnel_heap", 1, 1, new SetItem[]
		{
			new( "corpse_pile", 0, 0, 0 ), new( "limb", 0.38f, 0.3f, 0, 0.8 ), new( "limb", -0.36f, -0.3f, 90, 0.8 ),
			new( "skull", 0.38f, -0.32f, 0, 0.8 ), new( "skull", -0.38f, 0.32f, 0, 0.6 ), new( "bone", 0.3f, 0.4f, 60, 0.6 ), new( "candle", 0.4f, 0.4f, 0, 0.6 ),
		} ),
		new( "beast_carcass", 1, 1, new SetItem[]
		{
			new( "beast_ribs", 0, 0, 0 ), new( "bone_heap", 0.3f, 0.3f, 0, 0.7 ), new( "bone", -0.38f, 0.3f, 60, 0.8 ), new( "bone", 0.3f, -0.38f, 120, 0.8 ),
			new( "skull_animal", -0.35f, -0.35f, 0, 0.8 ), new( "giant_skull", -0.32f, 0.05f, 45, 0.4 ),
		} ),
		new( "bone_pit", 1, 1, new SetItem[]
		{
			new( "bone_heap", 0, 0, 0 ), new( "skeleton", 0.32f, 0.3f, 30, 0.8 ), new( "skeleton_stone", -0.32f, -0.3f, 200, 0.7 ),
			new( "skull_pile", 0.35f, -0.35f, 0, 0.7 ), new( "bone_ribs", -0.35f, 0.35f, 0, 0.6 ), new( "skull", 0.1f, 0.4f, 0, 0.6 ), new( "bone", -0.4f, 0.05f, 90, 0.6 ),
		} ),
		new( "mausoleum", 1, 1, new SetItem[]
		{
			new( "mausoleum", 0, 0, 0 ), new( "tombstone", 0.42f, 0.42f, 0, 0.8 ), new( "tombstone", -0.42f, -0.42f, 180, 0.8 ),
			new( "candles", 0.42f, -0.42f, 0, 0.8 ), new( "grave_slab", -0.35f, 0.4f, 90, 0.5 ), new( "skull", -0.42f, 0.1f, 0, 0.5 ),
		} ),
		new( "cell_camp", 1, 1, new SetItem[]
		{
			new( "tomb_blanket", 0.1f, 0.2f, 0 ), new( "tomb_blanket", -0.15f, -0.2f, 90, 0.8 ), new( "bowl", 0.35f, -0.35f, 0, 0.8 ),
			new( "candle", -0.35f, 0.35f, 0, 0.8 ), new( "plushie", 0.1f, 0.2f, 0, 0.5, 0.45f ), new( "chain_pile", 0.35f, 0.35f, 0, 0.6 ), new( "skull", -0.35f, -0.4f, 0, 0.5 ),
		} ),
		new( "cell_table", 1, 1, new SetItem[]
		{
			new( "table_low", 0, 0, 0 ), new( "stool", 0.28f, 0, 180 ), new( "stool", -0.28f, 0, 0, 0.8 ),
			new( "plate", 0.05f, 0.08f, 0, 1, 0.68f ), new( "mug", -0.06f, -0.1f, 0, 0.8, 0.68f ), new( "food_scraps", 0.35f, -0.35f, 0, 0.7 ),
		} ),
		new( "study", 1, 1, new SetItem[]
		{
			// the desk's top is 2.45 x 1.3 m: items stay within +-0.24 along it (V) and +-0.12 across it (U)
			new( "desk", 0, 0, 0 ), new( "stool", 0.3f, 0, 180 ), new( "book", -0.06f, 0.18f, 0, 1, 0.84f ), new( "book", 0.02f, -0.12f, 30, 0.8, 0.84f ),
			new( "candelabra_small", -0.06f, -0.2f, 0, 0.9, 0.84f ), new( "papers", 0.04f, 0.04f, 0, 0.8, 0.84f ), new( "papers", -0.35f, 0.35f, 0, 0.7 ),
		} ),
		new( "double_stack", 1, 1, new SetItem[]
		{
			new( "bookcase_double", 0, 0, 90 ), new( "book_pile", 0.35f, 0.35f, 0, 0.8 ), new( "stool", -0.35f, -0.35f, 45, 0.6 ),
		} ),
		new( "dissection", 1, 1, new SetItem[]
		{
			// the slab lies along U (yaw 90), so the corpse lies along it at yaw 0
			new( "altar_slab", 0, 0, 90 ), new( "corpse", 0, 0, 0, 1, 0.66f ), new( "alchemy_tools", 0.35f, 0.35f, 0, 0.8 ),
			new( "bucket", 0.35f, -0.35f, 0, 0.8 ), new( "organs", -0.35f, 0.2f, 0, 0.8 ), new( "limb", -0.3f, -0.35f, 20, 0.7 ), new( "candelabra_small", 0.15f, -0.4f, 0, 0.7 ),
		} ),
		new( "tank_row", 1, 1, new SetItem[]
		{
			new( "specimen_tank_big", 0, 0, 0 ), new( "specimen_jar", -0.35f, 0.35f, 0, 0.8 ), new( "specimen_jar", -0.35f, -0.35f, 0, 0.8 ), new( "bone", 0.2f, 0, 0, 0.6 ),
		} ),
		new( "altar_sacrifice", 1, 1, new SetItem[]
		{
			// the slab lies along V (yaw 0), so the corpse turns 90 to lie along it (user: it lay across the altar)
			new( "altar_slab", 0, 0, 0 ), new( "corpse", 0, 0, 90, 1, 0.66f ), new( "candle_stand", 0.35f, 0.35f, 0 ), new( "candle_stand", 0.35f, -0.35f, 0 ),
			new( "skull_pile", -0.35f, 0, 0, 0.8 ), new( "bone", -0.3f, 0.3f, 0, 0.6 ),
		}, FloorTile: "rune" ),
		new( "ritual_circle", 1, 1, new SetItem[]
		{
			new( "demon_ward", 0, 0, 0 ), new( "candles", 0.35f, 0.35f, 0 ), new( "candles", 0.35f, -0.35f, 0 ), new( "candles", -0.35f, 0.35f, 0 ), new( "candles", -0.35f, -0.35f, 0 ),
			new( "corpse", 0.25f, 0, 90, 0.8 ), new( "incense", -0.35f, 0, 0, 0.7 ), new( "skull", 0, 0.3f, 0, 0.6 ),
		}, FloorTile: "rune" ),
		new( "chapel_pews", 1, 1, new SetItem[]
		{
			// the pew's seat is at 0.39 m over its front edge; the book sits on the seat, not over the backrest
			new( "pew", 0.22f, 0, 180 ), new( "pew", -0.22f, 0, 180 ), new( "wax", 0.1f, 0.35f, 0, 0.7 ), new( "wax", -0.3f, -0.35f, 0, 0.7 ),
			new( "book", 0.24f, -0.15f, 0, 0.5, 0.42f ), new( "skull", -0.4f, 0.4f, 0, 0.5 ),
		} ),
		new( "court_of_heads", 1, 1, new SetItem[]
		{
			new( "head_spike", 0.34f, 0.34f, 0 ), new( "head_spike", 0.34f, -0.34f, 0 ), new( "head_spike", -0.34f, 0.34f, 0 ), new( "head_spike", -0.34f, -0.34f, 0 ),
			new( "rug", 0, 0, 0 ), new( "skull", 0, 0.3f, 0, 0.6 ), new( "skull", -0.1f, -0.3f, 0, 0.6 ), new( "bone", 0.3f, 0, 0, 0.5 ),
		} ),
		new( "chained_idol", 1, 1, new SetItem[]
		{
			new( "statue_chained", 0, 0, 0 ), new( "candles", 0.35f, 0.35f, 0, 0.8 ), new( "candles", -0.35f, -0.35f, 0, 0.8 ), new( "skull", 0.35f, -0.35f, 0, 0.6 ),
		} ),
		// the throne court's tables: a long table with a bench each side and the meal still on it (top at 0.93; the table is
		// 2.0 x 1.0 m, so items stay within +-0.15 along it and +-0.08 across)
		new( "court_feast", 1, 1, new SetItem[]
		{
			new( "table_long", 0, 0, 0 ), new( "bench", 0.25f, 0, 180 ), new( "bench", -0.25f, 0, 0 ),
			new( "candles", 0.05f, 0.12f, 0, 1, 0.93f ), new( "goblet", -0.06f, -0.14f, 0, 0.7, 0.93f ), new( "plate", 0.07f, -0.08f, 0, 0.7, 0.93f ),
			new( "skull", -0.06f, 0.1f, 30, 0.6, 0.93f ), new( "bone", 0.35f, 0.4f, 60, 0.5 ),
		} ),
		new( "spit_roast", 1, 1, new SetItem[]
		{
			new( "fire_pit", 0, 0, 0 ), new( "bench", 0.35f, 0, 180 ), new( "bench", -0.35f, 0, 0, 0.8 ),
			new( "plate", 0.38f, 0.35f, 0, 0.7 ), new( "food", -0.38f, 0.35f, 0, 0.7 ), new( "mug", 0.36f, -0.35f, 0, 0.7 ), new( "bone", -0.36f, -0.35f, 0, 0.6 ),
		} ),
		new( "siege_store", 1, 1, new SetItem[]
		{
			new( "ballista", 0, 0, 0 ), new( "cannonball", 0.4f, 0.35f, 0, 0.8 ), new( "cannonball", 0.42f, 0.28f, 0, 0.6 ), new( "arrow_basket", -0.4f, 0.4f, 0, 0.8 ),
		} ),
		new( "slingshot_bay", 1, 1, new SetItem[]
		{
			new( "slingshot", 0, 0, 0 ), new( "cannonball", 0.4f, 0.4f, 0, 0.8 ), new( "cannonball", 0.42f, 0.3f, 0, 0.6 ), new( "sack", -0.4f, -0.4f, 0, 0.6 ),
		} ),
		new( "cannon_bay", 1, 1, new SetItem[]
		{
			new( "cannon", 0, 0, 0 ), new( "cannonball", 0.35f, 0.4f, 0, 0.8 ), new( "cannonball", 0.4f, 0.32f, 0, 0.8 ), new( "cannonball", 0.28f, 0.4f, 0, 0.6 ), new( "arrow_basket", -0.4f, -0.4f, 0, 0.5 ),
		} ),
		new( "launcher_bay", 1, 1, new SetItem[]
		{
			new( "arrow_launcher", 0, 0, 0 ), new( "arrow_basket", 0.4f, 0.4f, 0, 0.9 ), new( "arrow_basket", -0.4f, 0.4f, 0, 0.6 ),
		} ),
		new( "oil_works", 1, 1, new SetItem[]
		{
			new( "boiling_oil", 0, 0, 0 ), new( "cannonball", 0.4f, 0.4f, 0, 0.7 ), new( "bucket", -0.4f, -0.4f, 0, 0.8 ), new( "chain_pile", -0.4f, 0.4f, 0, 0.6 ),
		} ),
		new( "wrecked_wagon", 1, 1, new SetItem[]
		{
			new( "wagon_wrecked", 0, 0, 0 ), new( "sack", 0.4f, 0.4f, 0, 0.8 ), new( "sack", 0.42f, -0.3f, 0, 0.8 ), new( "crate_broken", -0.4f, -0.4f, 30, 0.7 ),
		} ),
		new( "well_head", 1, 1, new SetItem[]
		{
			new( "well", 0, 0, 0 ), new( "bucket", 0.42f, 0.42f, 0, 0.8 ), new( "chain_pile", -0.42f, -0.42f, 0, 0.7 ), new( "skull", 0.3f, -0.4f, 0, 0.6 ),
		} ),
		new( "hanging_table", 1, 1, new SetItem[]
		{
			new( "table_suspended", 0, 0, 0 ), new( "bucket", 0.35f, 0.35f, 0, 0.8 ), new( "limb", -0.35f, 0.3f, 0, 0.7 ), new( "organs", 0.3f, -0.35f, 0, 0.7 ), new( "bone_pile_small", -0.3f, -0.35f, 0, 0.7 ),
		} ),
	};

	// the bones under everyone's feet: every room's litter, the corridors' litter and a share of every scatter and corner
	private static readonly FillEntry[] GaolBoneScatter = E( ("skull", 12), ("bone", 12), ("bone_pile_small", 10), ("skeleton", 8), ("skeleton_stone", 6), ("bone_heap", 6), ("bone_ribs", 4), ("dead_body", 4) );
	private static FillEntry[] Bones( FillEntry[] entries ) => Mix( entries, GaolBoneScatter );

	private static readonly Dictionary<DecorRole, Dictionary<SlotKind, FillEntry[]>> GaolTables = new()
	{
		[DecorRole.Torture] = new()
		{
			[SlotKind.Centre] = Mix( S( ("rack_room", 16), ("breaking_wheel", 12), ("impaled_ring", 12), ("pyre_court", 10), ("stake_burning", 8), ("gallows_yard", 12), ("gibbet_post", 10), ("bone_pit", 8), ("hanging_table", 8), ("stretch_table", 6) ), E( ("", 5) ) ),
			[SlotKind.EdgeStrip] = E( ("cage_standing", 14), ("stocks", 12), ("stocks_stone", 8), ("rack_stone", 6), ("cage_stone", 8), ("corpse_sitting", 12), ("torture_device", 10), ("cage_large", 6), ("whipping_post", 6), ("plinth_cage", 4), ("head_spike", 8), ("", 16) ),
			[SlotKind.Wall] = E( ("shackles", 16), ("wrist_bonds", 12), ("cage_wall", 14), ("corpse_wall", 10), ("skeleton_hung", 8), ("skeleton_shackled", 8), ("tool_wall", 8), ("torch", 18), ("", 6) ),
			[SlotKind.WallSwap] = E( ("bars", 25), ("wall_alt", 15), ("wall_3", 15), ("wall_deep", 10), ("alcove", 10), ("", 25) ),
			[SlotKind.Corner] = Bones( E( ("brazier", 16), ("cage_small", 10), ("bucket", 10), ("skull_pile", 12), ("web_corner", 6), ("plinth_cage", 6), ("", 20) ) ),
			[SlotKind.Ceiling] = E( ("skeleton_cage", 30), ("cage_hung_stone", 10), ("corpse_hanging", 25), ("chain", 12), ("chandelier_broken", 5), ("", 18) ),
			[SlotKind.Scatter] = Bones( E( ("limb", 15), ("organs", 10), ("chain_pile", 12), ("stake", 6), ("", 26) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Barracks] = new()
		{
			[SlotKind.Centre] = Mix( S( ("cell_camp", 40), ("cell_table", 35), ("campfire_bones", 10) ), E( ("", 15) ) ),
			[SlotKind.EdgeStrip] = E( ("bed", 40), ("bed_big", 8), ("tomb_blanket", 12), ("bedroll", 8), ("corpse_sitting", 10), ("cage_large", 5), ("tent", 4), ("chest_wood", 6), ("", 7) ),
			[SlotKind.Wall] = E( ("shackles", 18), ("key_ring", 10), ("wrist_bonds", 10), ("torch", 22), ("corpse_wall", 6), ("", 30) ),
			[SlotKind.WallSwap] = E( ("bars", 55), ("bars_half", 15), ("wall_alt", 10), ("wall_3", 10), ("", 10) ),
			[SlotKind.Corner] = Bones( E( ("bucket", 18), ("sack", 14), ("skull", 8), ("stool", 14), ("web_corner", 8), ("", 24) ) ),
			[SlotKind.Scatter] = Bones( E( ("food_scraps", 15), ("plate", 10), ("chain_pile", 10), ("sewer_grate", 5), ("", 34) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Crypt] = new()
		{
			[SlotKind.Centre] = Mix( S( ("charnel_heap", 22), ("mausoleum", 18), ("beast_carcass", 12), ("bone_pit", 14), ("gibbet_post", 8), ("coffin_row", 12), ("coffin_open", 8) ), E( ("", 6) ) ),
			[SlotKind.EdgeStrip] = E( ("coffin", 24), ("sarcophagus", 10), ("tomb", 12), ("tomb_low", 8), ("tombstone", 10), ("vessel", 6), ("corpse_pile_burnt", 6), ("giant_hand", 5), ("giant_skull", 5), ("bone_pile", 8), ("", 6) ),
			[SlotKind.Wall] = E( ("wall_plaque", 14), ("torch", 14), ("skeleton_shackled", 8), ("skeleton_hung", 6), ("gargoyle_wall", 8), ("", 44) ),
			[SlotKind.WallSwap] = E( ("bars", 20), ("alcove", 20), ("alcove_round", 15), ("wall_alt", 15), ("wall_deep", 15), ("wall_4", 10), ("", 5) ),
			[SlotKind.Corner] = Bones( E( ("bone_ribs", 12), ("giant_skull", 8), ("skull_pile", 12), ("statue_broken", 10), ("statue_head", 8), ("tombstone_plain", 12), ("web_corner", 12), ("", 22) ) ),
			[SlotKind.Ceiling] = E( ("skull_hanging", 25), ("chain", 18), ("skeleton_cage", 12), ("cage_hung_stone", 8), ("bell", 4), ("chain_ring", 4), ("", 29) ),
			[SlotKind.Scatter] = Bones( E( ("skull", 10), ("bone", 10), ("grave_slab", 8), ("corpse", 8), ("gibbet_fallen", 4), ("", 28) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Alchemy] = new()
		{
			[SlotKind.Centre] = S( ("dissection", 35), ("tank_row", 30), ("cauldron_brew", 20), ("table_potions", 15) ),
			[SlotKind.EdgeStrip] = E( ("specimen_tank", 25), ("shelf", 18), ("altar_table", 12), ("cupboard", 10), ("desk", 8), ("desk_reading", 6), ("", 21) ),
			[SlotKind.Wall] = E( ("papers_wall", 25), ("shelf_wall", 20), ("pipe_wall", 10), ("tool_wall", 10), ("candle_wall", 15), ("wall_drain", 4), ("", 16) ),
			[SlotKind.Ceiling] = E( ("chandelier", 40), ("lamp_hanging", 40), ("chain", 20) ),
			// the gaol's jars and pots used to come in through the stone alchemy corner; that lost its jars, so they are named here
			[SlotKind.Corner] = Bones( E( ("jar", 25), ("candle_stand", 20), ("bucket", 10), ("skull_pile", 10), ("", 35) ) ),
			[SlotKind.Scatter] = Bones( E( ("organs", 12), ("bucket", 10), ("bottle", 10), ("jar", 8), ("limb", 8), ("", 45) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Library] = new()
		{
			[SlotKind.Centre] = Mix( S( ("study", 40), ("double_stack", 25), ("table_reading", 25) ), E( ("", 10) ) ),
			[SlotKind.EdgeStrip] = E( ("bookcase", 50), ("bookcase_small", 15), ("desk", 10), ("lectern", 8), ("", 17) ),
			[SlotKind.Wall] = E( ("papers_wall", 25), ("candle_wall", 20), ("wall_greeble", 10), ("torch", 15), ("", 30) ),
			[SlotKind.Corner] = Bones( E( ("candelabra_tall", 20), ("statue", 12), ("book_pile", 20), ("web_corner", 10), ("", 30) ) ),
			[SlotKind.Ceiling] = E( ("chandelier", 45), ("lamp_hanging", 35), ("", 20) ),
			[SlotKind.Scatter] = Bones( E( ("book_pile", 15), ("papers", 15), ("book", 10), ("candle", 10), ("", 40) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Shrine] = new()
		{
			[SlotKind.Centre] = S( ("altar_sacrifice", 30), ("ritual_circle", 25), ("chapel_pews", 25), ("altar_grand", 20) ),
			[SlotKind.EdgeStrip] = E( ("pew_long", 18), ("pew", 15), ("candle_rack", 15), ("tabernacle", 12), ("organ", 8), ("font", 8), ("pew_broken", 8), ("", 16) ),
			[SlotKind.Wall] = E( ("wall_plaque", 18), ("wall_rune", 18), ("wall_sigil", 15), ("candle_wall", 18), ("banner", 22), ("", 9) ),
			[SlotKind.Corner] = Bones( E( ("candelabra_tall", 28), ("demon_ward", 14), ("statue", 18), ("gargoyle_statue", 10), ("incense", 8), ("", 12) ) ),
			[SlotKind.Scatter] = Bones( E( ("wax", 22), ("candles", 18), ("incense", 8), ("", 32) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Throne] = new()
		{
			// the throne always; the court around it comes from the kit's ExtraCentres (user: the middle stood empty)
			[SlotKind.Centre] = S( ("throne_dais", 1) ),
			[SlotKind.EdgeStrip] = E( ("throne", 20), ("statue_pedestal", 15), ("brazier", 15), ("demon_statue", 10), ("banner_pole", 10), ("", 30) ),
			[SlotKind.Wall] = E( ("banner", 42), ("wall_plaque", 15), ("wall_band", 10), ("bell_wall", 5), ("torch", 20), ("", 8) ),
			[SlotKind.Corner] = Bones( E( ("candelabra_tall", 25), ("statue", 22), ("statue_broken", 8), ("gargoyle_statue", 12), ("head_spike", 15), ("", 12) ) ),
			[SlotKind.Ceiling] = E( ("chandelier_big", 40), ("chandelier", 35), ("", 25) ),
			[SlotKind.Scatter] = Bones( E( ("skull", 12), ("rug_flat", 10), ("", 44) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.MessHall] = new()
		{
			[SlotKind.Centre] = Mix( S( ("spit_roast", 40), ("table_feast", 32), ("well_head", 20) ), E( ("", 8) ) ),
			[SlotKind.EdgeStrip] = E( ("counter", 15), ("trough", 12), ("cauldron_big", 10), ("fire_pit_round", 6), ("bench", 15), ("stool", 10), ("keg", 10), ("shelf", 8), ("", 14) ),
			[SlotKind.Wall] = E( ("torch", 30), ("banner", 15), ("wall_font", 12), ("shelf_wall", 12), ("", 31) ),
			[SlotKind.Corner] = Bones( E( ("keg", 20), ("sack", 15), ("brazier", 15), ("bucket", 10), ("", 30) ) ),
			[SlotKind.Ceiling] = E( ("chandelier", 40), ("lamp_hanging", 28), ("chandelier_big", 12), ("corpse_hanging", 8), ("bell", 4), ("", 8) ),
			[SlotKind.Scatter] = Bones( E( ("food", 15), ("plate", 12), ("goblet", 10), ("mug", 8), ("food_scraps", 15), ("", 25) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Armory] = new()
		{
			[SlotKind.Centre] = Mix( S( ("siege_store", 26), ("cannon_bay", 22), ("launcher_bay", 12), ("slingshot_bay", 10), ("oil_works", 22) ), E( ("", 8) ) ),
			[SlotKind.EdgeStrip] = E( ("weapon_rack", 30), ("barricade", 15), ("arrow_basket", 10), ("chest", 12), ("barricade_low", 6), ("shelf", 6), ("", 21) ),
			[SlotKind.Wall] = E( ("wall_greeble", 15), ("wall_band", 10), ("weapon_plaque", 15), ("torch", 30), ("ladder", 8), ("", 22) ),
			[SlotKind.Corner] = Bones( E( ("cannonball", 15), ("arrow_basket", 15), ("dial", 8), ("brazier", 15), ("", 36) ) ),
			[SlotKind.Scatter] = Bones( E( ("cannonball", 15), ("arrow_basket", 8), ("chain_pile", 8), ("", 48) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Storage] = new()
		{
			[SlotKind.Centre] = Mix( S( ("wrecked_wagon", 35), ("well_head", 25), ("rubble_pillar", 15), ("beast_carcass", 10) ), E( ("", 15) ) ),
			// crates, barrels and vases are placed by the breakables pass, never from a table
			[SlotKind.EdgeStrip] = E( ("shelf", 20), ("cupboard", 10), ("keg", 20), ("crate_broken", 12), ("sack", 15), ("wheelbarrow", 5), ("carcass", 5), ("", 13) ),
			[SlotKind.Wall] = E( ("torch", 25), ("ladder", 10), ("wall_drain", 8), ("shelf_wall", 15), ("", 42) ),
			[SlotKind.Corner] = Bones( E( ("sack", 22), ("crate_broken", 15), ("wheelbarrow", 10), ("keg", 15), ("gibbet_fallen", 5), ("chandelier_fallen", 5), ("", 18) ) ),
			[SlotKind.Ceiling] = E( ("corpse_hanging", 35), ("chain", 25), ("skull_hanging", 10), ("", 30) ),
			[SlotKind.Scatter] = Bones( E( ("sack", 12), ("meat", 10), ("food_scraps", 10), ("chain_pile", 8), ("", 40) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
		[DecorRole.Secret] = new()
		{
			// the portal is the exit room's (user); the oubliette keeps its hoard and its idol
			[SlotKind.Centre] = S( ("loot_hoard", 55), ("chained_idol", 45) ),
			[SlotKind.EdgeStrip] = E( ("crystal", 25), ("chest", 25), ("corpse_sitting", 15), ("cage_standing", 10), ("", 25) ),
			[SlotKind.Wall] = E( ("wall_rune", 30), ("wall_sigil", 25), ("candle_wall", 15), ("", 30) ),
			[SlotKind.Corner] = Bones( E( ("crystal", 25), ("crystal_rock", 10), ("skull_pile", 15), ("cage_small", 10), ("web_corner", 10), ("", 20) ) ),
			[SlotKind.Scatter] = Bones( E( ("crystal", 15), ("skull", 15), ("bone", 10), ("web_corner", 8), ("", 32) ) ),
			[SlotKind.Litter] = GaolBoneLitter,
		},
	};

	public static readonly DungeonKit Gaol = new()
	{
		Id = "gaol",
		FloorModels = new[] { "@floor_a", "@floor_b", "@floor_c", "@floor_d", "@floor_e", "@floor_f" },
		PlainFloorModel = "@floor_plain",
		RuneFloorModels = new[] { "@floor_ritual", "@floor_medallion" },
		CeilingModels = new[] { "@ceiling_vault" },
		PlainCeilingChance = 1f,
		CeilingDrop = 4f,     // the vault's apex sits at the plane; hung props start just under it
		CeilingFlipped = false,
		FloorHasUnderside = true,
		WallModel = "@wall_a",
		QuarterWallModel = "@wall_short",
		QuarterWallScale = null,
		QuarterFullLength = true,
		WallHalf = 6.3f,      // 0.16 m: Wall_02's relief spans 0.125..0.195 once centred; the skirt of the Wall_01 variant reaches 0.18
		WallSetback = 0f,     // every composite carries its own centring offset
		DoubleFaced = true,
		SecretWallModel = "@wall_secret",
		PlainWallModel = "@wall_a",
		BrokenSecretWallModel = "@wall_secret_open",
		// Wall_L_Damaged_06 is the wall gone but for a stub inside the corner post: open from 0 to 5 m, to the top; Wall_L_03's front is 0.23 m proud
		SecretOpenAlongMin = 4f,
		SecretOpenAlongMax = 193f,
		SecretOpenTop = 190f,
		SecretFaceForward = 9.1f,
		SecretRubble = FF( "sm_env_rubble_brick_01", "sm_env_rubble_brick_02", "sm_env_rubble_brick_03", "sm_env_rubble_brick_04", "sm_env_rubble_pile_03" ),
		WallSwaps = GaolWallSwaps,
		DoorFrameModels = new[] { "@door_single", "@door_single", "@door_double", "@door_prison", "@door_arch", "@door_ruin" },
		DoorInnerFrameModels = null,
		DoorInnerFrameScale = null,
		DoorFits = DoorCatalog.GaolFits,
		BoardModels = DF( "sm_prop_boarded_wood_01", "sm_prop_boarded_wood_02", "sm_prop_boarded_wood_03", "sm_prop_boarded_wood_04" ),
		BoardChance = 0.25f,
		StairModel = "@stair",
		StairMirrorY = true,
		// neither pack has a wedge: the stone one sits at the top of a 5 m wall and reads as masonry
		StairWallModel = PP + "sm_env_stairs_wall_01.vmdl",
		StairWallYawOffset = 0f,
		StairCeilingModel = "@stair",
		StairCeilingFlipped = true,
		// the stone wedge is 5.046 m long with its slab at -0.258..+0.168 m about the pivot; the gaol wall is +-0.197: shortened to
		// the run, its slab thinned to +-0.192 and centred (user: it poked through the neighbouring wall)
		StairWallScale = (0.9f, 0.991f, 1f),
		StairWallNudge = 0.04f,
		SquarePillarModel = "@pillar",
		RoundPillarModel = "@pillar_round",
		// the base, not the 0.40 shaft the torches mount on
		InteriorPillarRadius = 0.72f,
		// L_Pillar_04's shaft is 0.38-0.46 m from the axis at torch height under a 0.72 m plinth (user: the torch floated)
		PillarMountRadius = 0.40f,
		InnerCornerModel = "@post",
		OuterCornerModel = "@post",
		CornerCentred = true,
		CornerGapCells = 0.15f,
		// the stone kit's trap heads (user: the gargoyles did not work as heads)
		FlameHeadModel = PP + "sm_prop_trap_flamehead_01.vmdl",
		FlameHeadMouth = null,
		FlameHeadHeight = 1.5f,
		FlameHeadYaw = 0f,
		AcidHeadModel = PP + "sm_prop_trap_acidhead_01.vmdl",
		AcidHeadMouth = null,
		AcidHeadHeight = 0.9f,
		AcidHeadYaw = 0f,
		Banners = DF( "sm_prop_flag_dark_02", "sm_prop_flag_dark_damaged_02" ).Concat( FF( "sm_prop_banner_02", "sm_prop_banner_03" ) ).ToArray(),
		Props = Overlay( GaolProps ),
		Excluded = new() { "fountain", "forge", "forge_pipe", "vat", "stone_lantern", "pentagram" },
		Sets = OverlaySets( GaolSets ),
		Tables = GaolTables,
		HallwayClutter = E( ("skull", 20), ("bone", 20), ("skeleton", 12), ("skeleton_stone", 6), ("bone_heap", 10), ("bone_ribs", 4), ("chain_pile", 8), ("limb", 6), ("food_scraps", 5), ("bricks", 8), ("rubble", 5), ("sewer_grate", 3), ("web_corner", 4), ("", 13) ),
		HallwayLitter = GaolBoneLitter,
		HallwayCeiling = E( ("chain", 14), ("skeleton_cage", 14), ("cage_hung_stone", 6), ("corpse_hanging", 10), ("skull_hanging", 8), ("noose", 10), ("charm_hanging", 6), ("chain_ring", 3), ("", 29) ),
		Tops = GaolTops,
		Composites = GaolComposites,
		SigilProp = "wall_trophy",
		SigilsMin = 5,
		SigilsMax = 10,
		TopFix = GaolTopFix,
		SkinPairs = Array.Empty<(string, string)>(),
		// the fire of each wall torch, metres out and up from its pivot (measured: the coal bed / the modelled flame)
		TorchFlame = new() { [Ff( "sm_prop_torch_02" )] = (0.38f, 0.30f), ["@torch_df"] = (0.375f, 0.84f) },
		// the moulding over the wall seam, per face; not in the throne room (user: it crossed the banners there) and not on
		// the pieces that are one tall panel with no top row (the arch doorway, the secret tell, the breaches)
		WallTrimModel = Ff( "sm_bld_wall_detail_02" ),
		WallTrimHeight = 3f,
		WallTrimSkipRoles = new[] { DecorRole.Throne },
		WallTrimSkipModels = new() { "@door_arch", "@wall_secret", "@wall_secret_open", "@wall_breach", "@wall_breach2" },
		// the throne court: tables, benches and the grimmer furniture on the hall's interior cells around the dais
		ExtraCentres = new()
		{
			[DecorRole.Throne] = (3, S( ("court_feast", 35), ("table_feast", 25), ("court_of_heads", 15), ("chained_idol", 10), ("gallows_yard", 15) )),
		},
		ExitProp = "portal",
		LitterScale = 1.9,
		// the vault springs from 3.5 m at the corner posts, its lowest point: the cobwebs hang just under it
		WebTop = 3.4f,
	};
}
