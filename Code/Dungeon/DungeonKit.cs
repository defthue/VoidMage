using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// Everything about a dungeon's FABRIC that depends on which model pack builds it: the floor, ceiling,
/// wall, doorway, stair, pillar and corner pieces, the measured numbers that go with them, and the pivot
/// conventions the builder has to honour for each. One instance per theme; <see cref="For"/> picks it.
///
/// Stone is the DungeonPack kit exactly as it always was — every value here was lifted out of
/// DungeonBuilder's constant block unchanged, and tools/gentest proved a stone floor byte-identical
/// before and after the move. Mossy is Stone with the green material pairs. Realms is POLYGON Dungeon
/// Realms, the forge-hold of floors 11+: a pack machined to our 5 m grid but with its own conventions
/// (walls off-centre on their pivot, doorways fused into one piece, mirrored stairs, no ceiling pieces),
/// each of which is a flag the builder reads rather than a branch on the theme.
///
/// Props are here too, as the per-kit view of the catalog: <see cref="Props"/> is DecorCatalog's stone
/// list overlaid by a per-kit list of full PropDefs (same id, other models, re-measured extents), so a
/// prop the kit does not name simply keeps its DungeonPack model. That is the user's rule for the
/// forge-hold — Realms by default, DungeonPack where it is better (the beds, the chests) — as data. The
/// same overlay pattern gives a kit its own set pieces (<see cref="Sets"/>) and slot-by-slot overrides of
/// the role tables (<see cref="Tables"/>).
///
/// Engine-free: tools/gentest compiles this file, so no Sandbox types. Scales are float triples and the
/// builder turns them into Vector3. Static initialisers run in textual order, so the shared helper arrays
/// come first and the instances after them; nothing in DecorCatalog or DoorCatalog refers back here.
///
/// A model path beginning with '@' names a COMPOSITE (<see cref="Composites"/>): a list of pieces spawned
/// together as one kit piece, each at its own offset, turn and scale in the composite's frame. That is how a
/// pack on a 2.5 m grid (Dark Fantasy / Dark Fortress, DungeonKit.Gaol.cs) builds our 5 m walls and floors,
/// how a 6 m wall is squashed to 5, how a vault is made of four quadrants, and how a prop whose pivot sits at
/// one end is recentred. Every consumer of a kit path goes through DungeonBuilder.Spawn, which expands it.
/// </summary>
public sealed partial record DungeonKit
{
	public string Id { get; init; }

	// ---- floors and ceilings
	/// <summary>Random floor tiles; Landing cells take [0]. The thinnest one's top sets every floor collider's height.</summary>
	public string[] FloorModels { get; init; }
	/// <summary>A flat tile with no raised detail, for cells that carry a trap plate.</summary>
	public string PlainFloorModel { get; init; }
	/// <summary>Tiles the shrine set pieces stamp under the altar.</summary>
	public string[] RuneFloorModels { get; init; }
	/// <summary>[0] is the plain slab and the default; the rest are picked with 1 - PlainCeilingChance.</summary>
	public string[] CeilingModels { get; init; }
	public float PlainCeilingChance { get; init; }
	/// <summary>Units the ceiling's visible underside hangs below the grid plane: what CeilingHung props hang from.</summary>
	public float CeilingDrop { get; init; }
	/// <summary>The ceiling pieces are floor slabs spawned upside down: pivot moved to the (x, z+1) corner, yaw 180
	/// and pitch 180, lowered by the slab's own depth so it sits in the band just below the plane.</summary>
	public bool CeilingFlipped { get; init; }
	/// <summary>The floor slab has a modelled underside, so a raised floor needs no ceiling piece hung under it.</summary>
	public bool FloorHasUnderside { get; init; }

	// ---- walls
	public string WallModel { get; init; }
	/// <summary>The piece for a lone half-level slab. Stone's is 2.5 m long and spawned twice; Realms's is 5 m (QuarterFullLength).</summary>
	public string QuarterWallModel { get; init; }
	public (float x, float y, float z)? QuarterWallScale { get; init; }
	public bool QuarterFullLength { get; init; }
	/// <summary>Units: half the wall's thickness once it stands centred on the grid line — where a wall prop's back face lands.</summary>
	public float WallHalf { get; init; }
	/// <summary>Metres a plain wall is pulled back along its own -Forward so its thickness centres on the grid line
	/// (Realms walls span -0.14..+0.42 m across their pivot). Wall swaps carry their own Setback per piece.</summary>
	public float WallSetback { get; init; }
	/// <summary>Every wall piece has a finished face on both sides, so a doorway, a shared window or the secret tell
	/// is spawned ONCE on the grid plane rather than as two single-sided copies nudged apart.</summary>
	public bool DoubleFaced { get; init; }
	public string SecretWallModel { get; init; }
	/// <summary>Single-sided plain wall, the back of the secret wall (unused by a DoubleFaced kit).</summary>
	public string PlainWallModel { get; init; }
	/// <summary>The secret tell once a spell opens it: the same wall with the doorway as a real archway.</summary>
	public string BrokenSecretWallModel { get; init; }
	/// <summary>The arch's opening in the tell's own frame, units: along the wall from its pivot, and the head height
	/// (Combat.SecretWall reads them for the burst box and the rubble).</summary>
	public float SecretOpenAlongMin { get; init; }
	public float SecretOpenAlongMax { get; init; }
	public float SecretOpenTop { get; init; }
	/// <summary>Units the tell's front face stands forward of its pivot plane.</summary>
	public float SecretFaceForward { get; init; }
	/// <summary>Static masonry left in the opened threshold.</summary>
	public string[] SecretRubble { get; init; }
	public Dictionary<string, DecorCatalog.WallSwapDef> WallSwaps { get; init; }

	// ---- doorways
	public string[] DoorFrameModels { get; init; }
	/// <summary>The centre-pivoted jambs-and-lintel that goes in the middle of each doorway, same order; null when
	/// the kit's doorframe is one fused piece and there is nothing to add.</summary>
	public string[] DoorInnerFrameModels { get; init; }
	public Dictionary<string, (float x, float y, float z)> DoorInnerFrameScale { get; init; }
	/// <summary>Which leaves fit each doorway, same order; a null entry means "never hang a door here".</summary>
	public DoorCatalog.DoorFit[] DoorFits { get; init; }
	/// <summary>Breakable plank panels that board up a dead-end room's only door instead of a leaf (the gaol);
	/// null = never. The panel layout per doorway is DoorFit.Boards.</summary>
	public string[] BoardModels { get; init; }
	/// <summary>Share of qualifying doorways (into a dressed room with exactly one door) that are boarded up.</summary>
	public float BoardChance { get; init; }

	// ---- stairs
	public string StairModel { get; init; }
	/// <summary>The stair fills its cell's -X/+Y quadrant from the pivot rather than -X/-Y: the mirrored pivot table.</summary>
	public bool StairMirrorY { get; init; }
	public string StairWallModel { get; init; }
	/// <summary>Added to the wedge's yaw: Realms's wedge runs along local -X where Stone's runs along +Y.</summary>
	public float StairWallYawOffset { get; init; }
	/// <summary>The sloped ceiling over a run. With StairCeilingFlipped it is the stair mesh itself, upside down.</summary>
	public string StairCeilingModel { get; init; }
	public bool StairCeilingFlipped { get; init; }
	/// <summary>Scale applied to the wedge at spawn (the gaol borrows the stone wedge, 5.05 m over a 5 m run and thicker
	/// than its walls); null = as modelled.</summary>
	public (float x, float y, float z)? StairWallScale { get; init; }
	/// <summary>Metres the wedge is pushed along its own +X after scaling, to centre an off-centre slab on the grid line.</summary>
	public float StairWallNudge { get; init; }

	// ---- pillars and corner posts
	public string SquarePillarModel { get; init; }
	public string RoundPillarModel { get; init; }
	/// <summary>Metres from an interior pillar's axis to the face a mounted torch's back lands on; 0 = the round pillar
	/// model's bounds (wrong for a column whose plinth is wider than its shaft).</summary>
	public float PillarMountRadius { get; init; }
	/// <summary>Half the width of the free-standing (RoundPillarModel) pillar at its widest, metres: DecorBreakables stands
	/// pottery on a ring just outside it. Measured: DungeonPack 0.74, dwarf 0.62, Fortress L_Pillar_04 0.72.</summary>
	public float InteriorPillarRadius { get; init; } = 0.75f;
	public string InnerCornerModel { get; init; }
	public string OuterCornerModel { get; init; }
	/// <summary>The corner post is a column centred on the vertex (Realms), not a body in one quadrant (Stone): one
	/// post per vertex covers every room meeting there, and a level-offset room gets a half post for its top slab.</summary>
	public bool CornerCentred { get; init; }
	/// <summary>Cells a corner prop keeps from the walls when no pillar stands there (Decor.PlaceCorner): 0.06 is
	/// the wall's half thickness, 0.16 clears a centred post that protrudes 0.28 m past the wall face.</summary>
	public float CornerGapCells { get; init; }

	// ---- traps: the one trap piece that is themed
	public string FlameHeadModel { get; init; }
	/// <summary>A separate jaw piece spawned at the head's own transform (Realms); null for a one-piece head.</summary>
	public string FlameHeadMouth { get; init; }
	/// <summary>Metres up the wall the head's pivot sits.</summary>
	public float FlameHeadHeight { get; init; }
	/// <summary>Degrees the head is turned at spawn for a model whose mouth is not along its local +X (the push,
	/// nozzle and pilot are measured on the turned box).</summary>
	public float FlameHeadYaw { get; init; }
	public string AcidHeadModel { get; init; }
	public string AcidHeadMouth { get; init; }
	public float AcidHeadHeight { get; init; }
	public float AcidHeadYaw { get; init; }

	// ---- props, tables and materials
	/// <summary>The per-floor banner styles (one is rolled per floor).</summary>
	public string[] Banners { get; init; }
	/// <summary>The catalog as this kit sees it: stone defs overlaid by the kit's own, keyed by id.</summary>
	public Dictionary<string, PropDef> Props { get; init; }
	/// <summary>Ids this kit never places, whichever table names them (the stone fountain in the forge-hold).</summary>
	public HashSet<string> Excluded { get; init; }
	/// <summary>Set pieces as this kit sees them: stone sets overlaid by the kit's own.</summary>
	public Dictionary<string, SetPiece> Sets { get; init; }
	/// <summary>Slot-by-slot overrides of the role tables (Decor.Entries): a role and slot not named here is the stone table's.</summary>
	public Dictionary<DecorRole, Dictionary<SlotKind, FillEntry[]>> Tables { get; init; }
	/// <summary>Corridor clutter, or null for the stone list.</summary>
	public FillEntry[] HallwayClutter { get; init; }
	/// <summary>Corridor litter underfoot, or null for the stone Debris list.</summary>
	public FillEntry[] HallwayLitter { get; init; }
	/// <summary>Small items on a prop's Top surface by parent id, or null for the stone Tops table.</summary>
	public Dictionary<string, FillEntry[]> Tops { get; init; }
	/// <summary>Things hung from corridor ceilings (cages, chains, nooses), placed like a room's Ceiling slot at a
	/// fixed density from its own stream; null = corridors hang nothing (stone).</summary>
	public FillEntry[] HallwayCeiling { get; init; }
	/// <summary>The composites this kit's '@' paths name (see the type doc); null for a kit without any.</summary>
	public Dictionary<string, KitPiece[]> Composites { get; init; }
	/// <summary>A wall prop hung on a couple of whole free room walls per floor (the forge-hold's hell symbols); null = none.</summary>
	public string SigilProp { get; init; }
	public int SigilsMin { get; init; }
	public int SigilsMax { get; init; }
	/// <summary>Per-model corrections to a prop's Top surface, keyed by model path (DecorCatalog.TopFix).</summary>
	public Dictionary<string, (float drop, float back)> TopFix { get; init; }
	/// <summary>Authored material -> the material to put in its slot (the mossy re-skin). Empty = no re-skin.</summary>
	public (string from, string to)[] SkinPairs { get; init; }
	/// <summary>Where a torch model's fire is, metres forward (into the room) and up from its pivot, by model path or
	/// composite id; a model not listed uses the stone torch's 0.48 / 0.46.</summary>
	public Dictionary<string, (float fwd, float up)> TorchFlame { get; init; }
	/// <summary>A moulding spawned along the top of every 5 m wall face and doorway (the gaol's Wall_Detail_02), at
	/// WallTrimHeight metres, skipped on faces into a room whose role is in WallTrimSkipRoles and on the segment models
	/// in WallTrimSkipModels (pieces with no top row of their own). Null = no trim pass.</summary>
	public string WallTrimModel { get; init; }
	public float WallTrimHeight { get; init; }
	public DecorRole[] WallTrimSkipRoles { get; init; }
	public HashSet<string> WallTrimSkipModels { get; init; }
	/// <summary>Further 1 x 1 centre sets stamped on a role's interior cells after the centre piece (a throne room's
	/// tables and benches): how many to try and what to draw from. Null = the one centre placement every room gets.</summary>
	public Dictionary<DecorRole, (int count, FillEntry[] entries)> ExtraCentres { get; init; }
	/// <summary>A prop stood in the middle of the exit room (the gaol's portal); null = the exit room is bare like a corridor.</summary>
	public string ExitProp { get; init; }
	/// <summary>Multiplies the litter density of every room and corridor (the gaol's bones): 1 = the stone tables' own.</summary>
	public double LitterScale { get; init; } = 1.0;
	/// <summary>DecorBoneLitter's extra bone layer, as a multiple of the room tables' litter density; 0 = none (the gaol,
	/// whose own litter is all bones already).</summary>
	public double BoneLitterScale { get; init; }
	/// <summary>Metres from the walking surface to where a room corner meets the ceiling: DecorWebs hangs its cobwebs
	/// from there. 0 = no webs.</summary>
	public float WebTop { get; init; }

	// ------------------------------------------------------------------ helpers

	private const string PP = "dungeonpack/";
	private const string RP = "dungeonrealms/";
	private static string[] Pack( params string[] names ) => names.Select( n => PP + n + ".vmdl" ).ToArray();
	private static string[] Realm( params string[] names ) => names.Select( n => RP + n + ".vmdl" ).ToArray();
	private static string R( string name ) => RP + name + ".vmdl";
	private static FillEntry[] E( params (string id, double w)[] items ) => items.Select( i => new FillEntry( i.id, i.w ) ).ToArray();
	private static FillEntry[] S( params (string id, double w)[] items ) => items.Select( i => new FillEntry( i.id, i.w, true ) ).ToArray();
	private static FillEntry[] Mix( FillEntry[] a, FillEntry[] b ) => a.Concat( b ).ToArray();

	/// <summary>The stone catalog with this kit's own defs written over it, by id.</summary>
	private static Dictionary<string, PropDef> Overlay( IEnumerable<PropDef> own )
	{
		var d = new Dictionary<string, PropDef>( DecorCatalog.Props );
		foreach ( var p in own ) d[p.Id] = p;
		return d;
	}

	private static Dictionary<string, SetPiece> OverlaySets( IEnumerable<SetPiece> own )
	{
		var d = new Dictionary<string, SetPiece>( DecorCatalog.Sets );
		foreach ( var s in own ) d[s.Id] = s;
		return d;
	}

	// ---- composites
	public static bool IsComposite( string path ) => path != null && path.Length > 1 && path[0] == '@';

	/// <summary>The pieces of a composite path; throws with the path in the message when the kit has no such composite.</summary>
	public KitPiece[] Pieces( string path )
	{
		if ( Composites != null && Composites.TryGetValue( path.Substring( 1 ), out var pieces ) ) return pieces;
		throw new KeyNotFoundException( $"kit {Id} has no composite {path}" );
	}

	/// <summary>The real model paths behind a kit path: itself when plain, every leaf piece when composite (nested composites walked).</summary>
	public IEnumerable<string> Expand( string path, int depth = 0 )
	{
		if ( !IsComposite( path ) ) { yield return path; yield break; }
		if ( depth > 4 ) throw new InvalidOperationException( $"composite {path} nests too deep (a cycle?)" );
		foreach ( var pc in Pieces( path ) )
			foreach ( var m in Expand( pc.Model, depth + 1 ) ) yield return m;
	}

	/// <summary>Every fabric model this kit can spawn, for the asset check in gentest (props are checked through Props).</summary>
	public IEnumerable<string> FabricModels()
	{
		foreach ( var m in FloorModels ) yield return m;
		yield return PlainFloorModel;
		foreach ( var m in RuneFloorModels ) yield return m;
		foreach ( var m in CeilingModels ) yield return m;
		yield return WallModel;
		yield return QuarterWallModel;
		yield return SecretWallModel;
		yield return PlainWallModel;
		yield return BrokenSecretWallModel;
		foreach ( var m in SecretRubble ) yield return m;
		foreach ( var w in WallSwaps.Values )
		{
			yield return w.Model;
			if ( w.Insert != null ) yield return w.Insert;
		}
		foreach ( var m in DoorFrameModels ) yield return m;
		if ( DoorInnerFrameModels != null ) foreach ( var m in DoorInnerFrameModels ) yield return m;
		foreach ( var f in DoorFits ) if ( f != null ) foreach ( var o in f.Options ) foreach ( var l in o ) yield return l.Model;
		yield return StairModel;
		yield return StairWallModel;
		yield return StairCeilingModel;
		yield return SquarePillarModel;
		yield return RoundPillarModel;
		yield return InnerCornerModel;
		yield return OuterCornerModel;
		yield return FlameHeadModel;
		if ( FlameHeadMouth != null ) yield return FlameHeadMouth;
		yield return AcidHeadModel;
		if ( AcidHeadMouth != null ) yield return AcidHeadMouth;
		foreach ( var m in Banners ) yield return m;
		if ( BoardModels != null ) foreach ( var m in BoardModels ) yield return m;
	}

	/// <summary>Every model this kit can put in the dungeon: the fabric plus every prop, attach and decay model,
	/// composites expanded to the vmdls behind them.</summary>
	public IEnumerable<string> AllModels()
	{
		IEnumerable<string> raw = FabricModels();
		foreach ( var p in Props.Values )
		{
			raw = raw.Concat( p.Models );
			if ( p.Attach != null ) raw = raw.Concat( p.Attach.Where( a => a != null ) );
			if ( p.Decay != null ) raw = raw.Concat( p.Decay );
		}
		foreach ( var m in raw )
			foreach ( var leaf in Expand( m ) ) yield return leaf;
	}

	// ------------------------------------------------------------------ Stone: DungeonPack, as it always was
	// Measured from the FBX files (tools/fbx_pivots.py, tools/fbx_measure.py) and the Synty demo placements:
	//   tile:        pivot at a corner, covers +X..+Y from it (5 x 5 m), top surface at the pivot height
	//   ceiling:     same footprint, hangs 15 cm below the pivot (04/05 carry beams that reach lower)
	//   wall/frame:  pivot at one end, runs along +Y, thin in X, front toward +X; 5 m long, 5 m tall
	//   quarter:     same, but 2.5 m long and 2.5 m tall, single-sided
	//   stair:       covers the -X/-Y cell from its pivot and climbs toward -X, bottom at pivot height
	//   stairwedge:  pivot at the low end, runs along +Y and rises toward +Y (2.5 m at the high end)
	//   stairceiling: covers +X/+Y from its pivot and rises toward +X, low edge at pivot height
	//   pillar:      centred on the pivot, 5 m tall
	//   corner post: pivot ON the wall junction vertex, 5 m tall; the body is off-centre: back edge ~25 cm
	//                behind the vertex (inside the wall thickness), front 41 cm (outer) / 52 cm (inner) into
	//                the local +X/+Y quadrant, which is aimed at the room that sees it

	// ---- what the stone floors borrow from the gaol's packs (user: bookcases and shelves from the other packs). A
	// kit's own defs and table slots rather than the shared catalog, so the forge-hold (which reads the stone tables
	// for its libraries) and the gaol stay as they are; the mossy floors are Stone `with` and get them too.
	// Measured with tools/fbx_bounds.py and rendered: the Dark Fantasy bookshelves are pivoted at one end (recentred
	// here like the gaol's), the Fortress Shelf_02 inserts are jars, bottles and boxes (alchemy only), Shelf_03 is an
	// empty rack and the Dark Fantasy Shelf_01 insert is four barrels on their sides.
	private static readonly Dictionary<string, KitPiece[]> StoneComposites = new()
	{
		["df_bookcase"] = new[] { P( Df( "sm_prop_bookshelf_double_01" ), -0.32f, -1.25f ) },
		["df_bookcase_damaged"] = new[] { P( Df( "sm_prop_bookshelf_double_01_damaged_01" ), -0.37f, -1.17f ) },
		["df_bookcase_small"] = new[] { P( Df( "sm_prop_bookshelf_01" ), -0.4f, -0.63f ) },
		["df_bookcase_small_damaged"] = new[] { P( Df( "sm_prop_bookshelf_01_damaged_01" ), -0.32f, -0.68f ) },
	};

	private static readonly PropDef[] StoneBorrowedProps =
	{
		// 2.5 x 0.64 x 2.9 m and 1.27 x 0.65 x 2.9 m of built-in books: nothing stands on them
		new( "bookcase_tall", new[] { "@df_bookcase" }, AnchorKind.WallFlush, 2.4f, 0.75f, 2.9f, true, Decay: new[] { "@df_bookcase_damaged" } ),
		new( "bookcase_tall_small", new[] { "@df_bookcase_small" }, AnchorKind.WallFlush, 1.36f, 0.72f, 2.9f, true, Decay: new[] { "@df_bookcase_small_damaged" } ),
		// Shelf_02 2.18 x 0.92 x 2.15 m, its loads reach 2.33
		new( "shelf_stocked", FF( "sm_prop_shelf_02", "sm_prop_shelf_02", "sm_prop_shelf_02" ), AnchorKind.WallFlush, 2.2f, 0.95f, 2.35f, true,
			Attach: new[] { Ff( "sm_prop_shelf_02_insert_01" ), Ff( "sm_prop_shelf_02_insert_02" ), Ff( "sm_prop_shelf_02_insert_03" ) }, RoleOnly: DecorRole.Alchemy ),
		// Shelf_03 2.34 x 1.18 x 2.41, the broken Shelf_02 2.16 x 0.9 x 2.15
		new( "shelf_empty", FF( "sm_prop_shelf_03", "sm_prop_shelf_02_damaged_01" ), AnchorKind.WallFlush, 2.35f, 1.2f, 2.41f, true ),
		// Shelf_01 2.1 x 0.91 x 2.36, its barrels 1.04 deep
		new( "barrel_rack", DF( "sm_prop_shelf_01" ), AnchorKind.WallFlush, 2.1f, 1.05f, 2.36f, true, Attach: DF( "sm_prop_shelf_01_insert_01" ) ),
	};

	/// <summary>The stone role slots that take the borrowed shelving; every other slot is the catalog's own.</summary>
	private static readonly Dictionary<DecorRole, Dictionary<SlotKind, FillEntry[]>> StoneTables = new()
	{
		[DecorRole.Library] = new()
		{
			[SlotKind.EdgeStrip] = E( ("bookcase", 32), ("bookcase_tall", 9), ("bookcase_tall_small", 6), ("candle_stand", 12), ("table_low", 10), ("stool", 8), ("", 23) ),
		},
		[DecorRole.Alchemy] = new()
		{
			[SlotKind.EdgeStrip] = E( ("bookcase", 30), ("shelf_stocked", 12), ("cauldron", 12), ("stone_table", 8), ("potion_pole", 8), ("table_low", 5), ("", 25) ),
		},
		[DecorRole.Storage] = new()
		{
			[SlotKind.EdgeStrip] = E( ("chest", 14), ("plank", 12), ("shelf_empty", 8), ("barrel_rack", 7), ("vat", 5), ("bench", 5), ("table_broken", 6), ("", 43) ),
		},
	};

	public static readonly DungeonKit Stone = new()
	{
		Id = "stone",
		FloorModels = Pack( "sm_env_tiles_01", "sm_env_tiles_010", "sm_env_tiles_02", "sm_env_tiles_03", "sm_env_tiles_04", "sm_env_tiles_05" ),
		PlainFloorModel = DecorCatalog.PlainFloorModel,
		RuneFloorModels = DecorCatalog.RuneTiles,
		CeilingModels = Pack( "sm_env_ceiling_stone_flat_01", "sm_env_ceiling_stone_flat_02", "sm_env_ceiling_stone_flat_03", "sm_env_ceiling_stone_flat_04", "sm_env_ceiling_stone_flat_05" ),
		PlainCeilingChance = 0.7f,
		CeilingDrop = 6f,
		CeilingFlipped = false,
		FloorHasUnderside = false,
		WallModel = PP + "sm_env_wall_01_doublesided.vmdl",
		QuarterWallModel = PP + "sm_env_wall_quarter_01.vmdl",
		QuarterWallScale = null,
		QuarterFullLength = false,
		WallHalf = 10.6f,
		WallSetback = 0f,
		DoubleFaced = false,
		SecretWallModel = DecorCatalog.SecretWallModel,
		PlainWallModel = DecorCatalog.PlainWallModel,
		BrokenSecretWallModel = PP + "sm_env_wall_07.vmdl",
		// SM_Env_Wall_07's opening is x 110..394 cm of the 5.18 m piece, up to the arch at ~420 cm; wall_05's front is 26.4 cm proud
		SecretOpenAlongMin = 43.3f,
		SecretOpenAlongMax = 155.1f,
		SecretOpenTop = 165.4f,
		SecretFaceForward = 10.4f,
		SecretRubble = Pack( "sm_env_brick_rubble_01", "sm_env_brick_rubble_02", "sm_env_brick_rubble_03", "sm_env_brick_rubble_04", "sm_env_brick_rubble_05", "sm_env_brick_rubble_06" ),
		WallSwaps = DecorCatalog.WallSwaps,
		DoorFrameModels = Pack( "sm_env_wall_doorframe_01", "sm_env_wall_doorframe_round_01", "sm_env_wall_doorframe_double_01", "sm_env_wall_doorframe_double_02", "sm_env_wall_doorframe_double_round_01" ),
		// Measured openings (cm): DoorFrame_01/Round_01 165 wide, Double_01 230, Double_02 270, Double_Round_01 305.
		// Door_Frame_02 (293 wide, 224 opening) is Synty's own pairing for both square double doorways;
		// Door_Large_Frame_01 is a 6 m x 5.6 m gatehouse frame with a 4 m opening and does not fit a 5 m wall.
		DoorInnerFrameModels = Pack( "sm_env_door_frame_01", "sm_env_door_frame_round_01", "sm_env_door_frame_02", "sm_env_door_frame_02", "sm_env_door_frame_round_02" ),
		// Local X is the wall's thickness axis. Door_Frame_Round_01 is exactly as thick as the wall pair and fights
		// it for visibility; 10% thicker puts its stone just proud of both faces.
		DoorInnerFrameScale = new() { [PP + "sm_env_door_frame_round_01.vmdl"] = (1.1f, 1f, 1f) },
		DoorFits = DoorCatalog.Fits,
		BoardModels = null,
		BoardChance = 0f,
		StairModel = PP + "sm_env_stairs_02.vmdl",
		StairMirrorY = false,
		StairWallModel = PP + "sm_env_stairs_wall_01.vmdl",
		StairWallYawOffset = 0f,
		StairCeilingModel = PP + "sm_env_ceiling_stone_stairs_01.vmdl",
		StairCeilingFlipped = false,
		SquarePillarModel = PP + "sm_env_pillar_square_06.vmdl",
		RoundPillarModel = PP + "sm_env_wall_pillar_large_round_01.vmdl",
		InteriorPillarRadius = 0.74f,
		InnerCornerModel = PP + "sm_env_wall_end_coner_inner_01.vmdl",
		OuterCornerModel = PP + "sm_env_wall_end_coner_outer_01.vmdl",
		CornerCentred = false,
		CornerGapCells = 0.06f,
		FlameHeadModel = PP + "sm_prop_trap_flamehead_01.vmdl",
		FlameHeadMouth = null,
		FlameHeadHeight = 1.5f,
		FlameHeadYaw = 0f,
		AcidHeadModel = PP + "sm_prop_trap_acidhead_01.vmdl",
		AcidHeadMouth = null,
		AcidHeadHeight = 0.9f,
		AcidHeadYaw = 0f,
		Banners = DecorCatalog.Banners,
		Props = Overlay( StoneBorrowedProps ),
		Excluded = null,
		Sets = DecorCatalog.Sets,
		Tables = StoneTables,
		HallwayClutter = null,
		HallwayLitter = null,
		Tops = null,
		HallwayCeiling = null,
		Composites = StoneComposites,
		SigilProp = null,
		SigilsMin = 0,
		SigilsMax = 0,
		ExitProp = "portal",
		TopFix = DecorCatalog.TopFix,
		SkinPairs = Array.Empty<(string, string)>(),
		// a quarter of the gaol's bone count (user); tuned with gentest's per-floor counts
		BoneLitterScale = 0.7,
		// the slab is 4.74 m over the walking surface (DecorGreenery.RoomHeight)
		WebTop = 4.7f,
	};

	/// <summary>
	/// Stone gone to seed: the same pieces re-skinned per material slot to the green atlas. Every DungeonPack model
	/// carries its own material (dungeonshader, or woodshader for the wooden props) plus the outline hull's; only
	/// the first is swapped, so the outline survives. Both mossy vmats are hand-kept copies of the stone ones with
	/// only the atlas changed (Synty's 03_B: identical UV layout, recoloured green).
	/// </summary>
	public static readonly DungeonKit Mossy = Stone with
	{
		Id = "mossy",
		SkinPairs = new[]
		{
			("shaders/dungeonshader.vmat", "shaders/dungeonshadermossy.vmat"),
			("shaders/woodshader.vmat", "shaders/woodshadermossy.vmat"),
		},
	};

	// ------------------------------------------------------------------ Realms: the dwarven forge-hold
	// POLYGON Dungeon Realms, Dwarf sub-kit, measured with tools/fbx_bounds.py (metres, s&box local frame):
	//   floor:       0..5 x 0..5, walking surface AT the pivot (+0.02..0.04 relief), slab hangs 0.23 m below it
	//   ceiling:     none in the pack. A floor slab flipped so its face points down, in the 0.25 m under the plane
	//   wall:        pivot at one end, runs +Y, 5 x 5 m exactly; thickness -0.14..+0.42 across the pivot, so it is
	//                pulled back 0.14 m to centre on the grid line (WallSetback). Both faces finished.
	//   doorframe:   one fused 5 x 5 piece, symmetric +-0.33 about the plane; no separate inner frame
	//   short wall:  5 m long x 2.5 m tall, same profile: one piece per lone half-level slab
	//   stair:       low end AT the pivot, climbs into -X rising exactly 2.50 m, fills the -X/+Y quadrant (mirrored)
	//   stairwedge:  runs along -X from the low end, thin in Y (-0.31..+0.14), so it is turned 270 onto the edge
	//   stairceiling: the stair mesh upside down, pivot at the HIGH end at level l+3, stepped face descending 2.5 m
	//   pillar:      centred, +-0.62, 5 m tall
	//   corner post: Pillar_07/08, centred +-0.56 (user: not the wall trims, which are horizontal bands). One per
	//                vertex; it also covers the 0.28 m notch two exactly-5 m walls leave at an outside corner.
	//   secret wall: Dwarf_Wall_04 is a SOLID plain slab (user: the stone tell was too obvious), -0.14..+0.31 with
	//                no setback so it stands 3 cm proud of its neighbours; Archway_04 opens it: a 205 cm pointed arch
	//                centred at 250, jambs to 347 cm

	private static readonly Dictionary<string, DecorCatalog.WallSwapDef> RealmsWallSwaps = new DecorCatalog.WallSwapDef[]
	{
		// the alcoves keep the wall-plane pivot (X min is the recess depth), so no Setback, unlike stone's alcove_round
		new( "alcove", R( "sm_env_dwarf_wall_alcove_small_01" ), null, false, false, true, 1.61f, "candles" ),
		new( "alcove_2", R( "sm_env_dwarf_wall_alcove_small_02" ), null, false, false, true, 1.61f, "bone_pile_small" ),
		new( "alcove_round", R( "sm_env_dwarf_wall_alcove_angular_01" ), null, false, false, true, 1.31f ),
		new( "wall_deep", R( "sm_env_dwarf_wall_alcove_angular_02" ), null, false, false, true, 2.14f ),
		// a sealed sliding door: the slider frame with its own closed leaf as the insert (both symmetric about the plane)
		new( "sealed_door", R( "sm_env_dwarf_wall_doorframe_slider_01" ), R( "sm_env_dwarf_wall_doorframe_slider_01_door" ), false, false, true, 0.42f ),
		// plain variety: the same -0.14..+0.42 profile as the main wall, so the same pull-back
		new( "wall_alt", R( "sm_env_dwarf_wall_02" ), null, false, false, false, 0.28f, Setback: 0.14f ),
		new( "wall_3", R( "sm_env_dwarf_wall_05" ), null, false, false, false, 0.28f, Setback: 0.14f ),
		new( "wall_4", R( "sm_env_dwarf_wall_06" ), null, false, false, false, 0.28f, Setback: 0.14f ),
		// windows are double-faced and symmetric: spawned once (DoubleFaced), no bar insert
		new( "window", R( "sm_env_dwarf_wall_window_01" ), null, false, false, true, 0.34f, Shared: true ),
		new( "window_2", R( "sm_env_dwarf_wall_window_03" ), null, false, false, true, 0.41f, Shared: true ),
	}.ToDictionary( w => w.Id );

	/// <summary>
	/// The forge-hold's own defs, written over the stone catalog by id: W x D x H metres from the FBX
	/// (tools/realms_mapping.py --index; X = depth, Y = width, Z = up). Anything not listed keeps its DungeonPack
	/// model — the beds and chests (user), the bookcases (Realms's shelf is a wall shelf, a different anchor),
	/// the crates (no debris kits yet), the candles, the coffins, the altar, the torture pieces, the alchemy items.
	/// Left out on purpose after measuring: the fire pit (a 4 m trough), the 6 m chandelier, the smelting pots
	/// (pivoted mid-air), the broken obelisks and rubble pillars (2.8-4 m across), the treasure chest (3.8 m wide),
	/// the cannon (its wheels are two separate meshes and a def carries one attach).
	/// </summary>
	private static readonly PropDef[] RealmsProps =
	{
		// ---- overrides of stone ids
		// the primary light everywhere. Back face 3 cm behind the pivot, so it sits flush at Embed 0
		new( "torch", Realm( "sm_prop_dwarf_torch_01" ), AnchorKind.WallMounted, 0.24f, 0.57f, 0.78f, false, MountHeight: 1.7f, Light: LightKind.Torch ),
		// user: rack 03 only, at 1.5x (extents are the spawned size). Modelled end-on (its long side along local X),
		// so Turn 90 lays it along the wall. Breaks through the realms_weapon_rack_03 kit. It is 2.88 m long but
		// declared 2.6 so it claims the wall's two quarters (Wide) rather than the whole cell (Tile), which halved
		// the armouries that managed their signature rack; at 1.14 m deep the whole cell is not needed, and the
		// half-slot clamp still keeps its ends off the side walls.
		new( "weapon_rack", Realm( "sm_prop_dwarf_weapon_rack_03" ), AnchorKind.WallFlush, 2.6f, 1.14f, 1.0f, true, Turn: 90f, Scale: 1.5f ),
		new( "bench", Realm( "sm_prop_dwarf_chair_bench_01" ), AnchorKind.Floor, 1.58f, 0.44f, 0.42f, true ),
		new( "stool", Realm( "sm_prop_dwarf_chair_stool_01" ), AnchorKind.Floor, 0.44f, 0.44f, 0.46f, true, Top: 0.46f ),
		new( "table_long", Realm( "sm_prop_dwarf_table_03" ), AnchorKind.Floor, 3.33f, 1.36f, 0.91f, true, Top: 0.91f ),
		new( "table_round", Realm( "sm_prop_dwarf_table_small_01" ), AnchorKind.Floor, 0.78f, 0.80f, 0.91f, true, Top: 0.91f ),
		new( "table_low", Realm( "sm_prop_dwarf_table_01" ), AnchorKind.Floor, 2.02f, 1.36f, 0.56f, true, Top: 0.56f ),
		new( "table_broken", Realm( "sm_prop_dwarf_table_small_broken_01" ), AnchorKind.Floor, 1.09f, 1.26f, 0.91f, true ),
		new( "stone_table_broken", Realm( "sm_prop_dwarf_table_broken_02" ), AnchorKind.Floor, 2.74f, 1.62f, 0.89f, true ),
		// a 6.5 m monument in the pack: capped to 3 m, which brings it to 1.45 m wide (extents are the capped size)
		new( "throne", Realm( "sm_prop_dwarf_throne_01" ), AnchorKind.Floor, 1.45f, 0.82f, 3.0f, true, MaxHeight: 3.0f, Bury: 0f ),
		// 2.2 m across in the pack, capped to a hearth's height
		new( "brazier", Realm( "sm_prop_dwarf_brazier_01" ), AnchorKind.Floor, 1.48f, 1.48f, 1.1f, true, Light: LightKind.Brazier, MaxHeight: 1.1f ),
		// pivot at the BOTTOM of the lantern, so it hangs its own height below the slab with its top touching it
		new( "chandelier", Realm( "sm_prop_dwarf_chandelier_05" ), AnchorKind.CeilingHung, 0.46f, 0.46f, 0.98f, false, MountHeight: 0.98f, Light: LightKind.Chandelier ),
		// the free-standing dwarf torch stands in for the candle stand wherever one is asked for; its light sits at the top
		new( "candle_stand", Realm( "sm_prop_dwarf_torch_07" ), AnchorKind.Floor, 0.72f, 0.72f, 1.26f, false, Light: LightKind.Candle ),
		new( "jar", Realm( "sm_prop_camp_pot_03", "sm_prop_camp_pot_04" ), AnchorKind.Floor, 0.48f, 0.48f, 0.59f, false, RoleOnly: DecorRole.Alchemy ),
		// 2.8 m across and 4.15 tall in the pack: capped to 3 m
		new( "obelisk", Realm( "sm_env_dwarf_obelisk_02" ), AnchorKind.Floor, 2.03f, 2.03f, 3.0f, true, MaxHeight: 3.0f, Bury: 0f ),
		new( "statue", Realm( "sm_env_statue_01", "sm_env_statue_02", "sm_env_statue_05", "sm_env_statue_06", "sm_env_statue_male_01", "sm_env_statue_male_02", "sm_env_statue_male_03",
			"sm_env_statue_female_01", "sm_env_statue_female_02", "sm_env_statue_female_03" ), AnchorKind.Floor, 1.9f, 1.27f, 2.18f, true ),
		// litter: the rocks and loose tiles are modelled part-sunk like the stone pebbles, so Bury stays 1
		new( "rubble", Realm( "sm_env_rubble_rocks_01", "sm_env_rubble_rocks_02", "sm_env_rubble_rocks_03" ), AnchorKind.Floor, 2.5f, 2.1f, 0.18f, false ),
		new( "pebbles", Realm( "sm_prop_rock_small_01", "sm_prop_rock_small_02", "sm_prop_rock_small_03" ), AnchorKind.Floor, 1.0f, 1.0f, 0.14f, false ),
		new( "bricks", Realm( "sm_env_tiles_loose_05" ), AnchorKind.Floor, 1.09f, 1.28f, 0.15f, false ),
		new( "brick", Realm( "sm_env_tiles_loose_06" ), AnchorKind.Floor, 0.59f, 0.98f, 0.07f, false ),
		new( "plank", Realm( "sm_env_tiles_loose_01", "sm_env_tiles_loose_03" ), AnchorKind.Floor, 2.01f, 1.63f, 0.18f, false ),
		// (no gold: the coin piles, the gold pile and the coin stacks are out of every kit, user 2026-09-15)
		// user: no dwarf dead bodies in the forge-hold, the nomads only
		new( "skeleton", Realm( "sm_prop_deadbody_nomad_01" ), AnchorKind.Floor, 1.74f, 1.46f, 1.39f, false ),
		// user: the short DungeonPack chain, not the 3.6 m dwarf one that reached down to head height
		new( "chain", Pack( "sm_prop_chain_02" ), AnchorKind.CeilingHung, 0.1f, 0.1f, 0.96f, false ),
		// user: the stone vases plus pots 2 and 6 (BreakableKits knows the pots: same shards, same broken base)
		new( "vase", Pack( "sm_prop_vase_01", "sm_prop_vase_02", "sm_prop_vase_03", "sm_prop_vase_04", "sm_prop_vase_05", "sm_prop_vase_06", "sm_prop_vase_07", "sm_prop_vase_08", "sm_prop_vase_09" )
			.Concat( Realm( "sm_prop_camp_pot_02", "sm_prop_camp_pot_06" ) ).ToArray(), AnchorKind.Floor, 0.62f, 0.62f, 1.0f, false, Breakable: true ),
		// user: barrels 01/02 and the two staved-in broken ones (still whole barrels), breakable through their realms_* kits
		new( "barrel", Realm( "sm_prop_barrel_01", "sm_prop_barrel_02", "sm_prop_barrel_broken_01", "sm_prop_barrel_broken_02" ), AnchorKind.Floor, 0.84f, 0.84f, 0.94f, true, Top: 0.9f, Breakable: true ),
		new( "rug", Realm( "sm_prop_camp_rug_03", "sm_prop_camp_rug_04" ), AnchorKind.Floor, 2.9f, 1.7f, 0.02f, false, Lift: 0.01f, Overlay: true ),
		new( "rug_flat", Realm( "sm_prop_camp_rug_01", "sm_prop_camp_rug_02", "sm_prop_camp_rug_05", "sm_prop_camp_rug_06" ), AnchorKind.Floor, 2.9f, 1.7f, 0.02f, false, Lift: 0.01f, Overlay: true ),
		// ---- the forge-hold's own
		// the loaded barrel racks and their empty and broken variants (user: all the barrel shelves); the loaded and
		// empty ones break through furniture kits, rack and barrels as pieces
		new( "barrel_shelf", Realm( "sm_prop_barrel_shelf_large_01", "sm_prop_barrel_shelf_small_01" ), AnchorKind.WallFlush, 2.03f, 0.9f, 2.78f, true ),
		new( "barrel_shelf_empty", Realm( "sm_prop_barrel_shelf_large_02", "sm_prop_barrel_shelf_small_02" ), AnchorKind.WallFlush, 2.03f, 0.8f, 2.21f, true ),
		new( "barrel_shelf_broken", Realm( "sm_prop_barrel_shelf_large_broken_02", "sm_prop_barrel_shelf_small_broken_01", "sm_prop_barrel_shelf_small_broken_02" ), AnchorKind.WallFlush, 2.23f, 1.55f, 2.21f, true ),
		new( "anvil", Realm( "sm_prop_anvil_01" ), AnchorKind.Floor, 1.79f, 0.66f, 1.02f, true ),
		new( "cog_box", Realm( "sm_prop_dwarf_cog_box_01_1" ), AnchorKind.Floor, 1.64f, 1.64f, 1.64f, true, Bury: 0f ),
		new( "cog_large", Realm( "sm_prop_dwarf_cog_large_01", "sm_prop_dwarf_cog_large_02", "sm_prop_dwarf_cog_large_03", "sm_prop_dwarf_cog_large_04", "sm_prop_dwarf_cog_large_05" ), AnchorKind.Floor, 1.83f, 1.83f, 0.2f, false ),
		new( "cog_small", Realm( "sm_prop_dwarf_cog_small_01", "sm_prop_dwarf_cog_small_02" ), AnchorKind.Floor, 0.98f, 0.98f, 0.12f, false ),
		new( "cog_pile", Realm( "sm_prop_dwarf_cog_pile_01", "sm_prop_dwarf_cog_pile_04" ), AnchorKind.Floor, 2.0f, 1.95f, 0.45f, false ),
		new( "cart", Realm( "sm_prop_dwarf_cart_03" ), AnchorKind.Floor, 1.0f, 2.14f, 0.87f, true, Attach: Realm( "sm_prop_dwarf_cart_03_ore" ) ),
		new( "capstan", Realm( "sm_prop_dwarf_capstar_01" ), AnchorKind.Floor, 1.1f, 1.1f, 1.19f, true, Bury: 0f ),
		// no lever anywhere (user: no lever models in the generation)
		new( "totem", Realm( "sm_prop_dwarf_totem_01" ), AnchorKind.Floor, 1.66f, 1.44f, 2.71f, true, Attach: Realm( "sm_prop_dwarf_totem_gold_01" ) ),
		// the crystal seams (RealmsCrystal shader): pivot 9 cm under the rock base, so Bury 0 stands them on it
		new( "crystal_rock", Realm( "sm_prop_crystal_rock_a_01", "sm_prop_crystal_rock_b_01", "sm_prop_crystal_rock_c_01", "sm_prop_crystal_rock_d_01", "sm_prop_crystal_rock_e_01", "sm_prop_crystal_rock_f_01", "sm_prop_crystal_rock_g_01" ),
			AnchorKind.Floor, 0.9f, 0.9f, 1.27f, true, Bury: 0f ),
		new( "crystal_cluster", Realm( "sm_env_crystals_cluster_large_04" ), AnchorKind.Floor, 2.52f, 2.02f, 3.03f, true, Bury: 0f ),
		new( "gem_cluster", Realm( "sm_prop_gem_cluster_01", "sm_prop_gem_cluster_02", "sm_prop_gem_cluster_03" ), AnchorKind.Floor, 0.38f, 0.38f, 0.06f, false ),
		// no lid: the lid mesh pivots at the floor, so as an attach it would sit inside the box
		new( "sarcophagus", Realm( "sm_prop_sarcophagus_01" ), AnchorKind.Floor, 1.66f, 2.46f, 1.11f, true, Top: 1.11f ),
		new( "dead_body", Realm( "sm_prop_deadbody_nomad_02", "sm_prop_deadbody_nomad_03" ), AnchorKind.Floor, 2.41f, 1.47f, 0.56f, false ),
		new( "statue_broken", Realm( "sm_env_statue_broken_01", "sm_env_statue_broken_02", "sm_env_statue_broken_03", "sm_env_statue_broken_05", "sm_env_statue_broken_06" ), AnchorKind.Floor, 1.55f, 1.23f, 1.96f, true ),   // 04 out (user)
		new( "statue_head", Realm( "sm_env_statue_broken_01_head" ), AnchorKind.Floor, 0.9f, 0.89f, 0.52f, false ),
		// pivot at the top of the lantern's chain, so it hangs straight off the slab; the light sits half a metre up from its bottom
		new( "hanging_lantern", Realm( "sm_prop_camp_lantern_01" ), AnchorKind.CeilingHung, 0.4f, 0.4f, 1.19f, false, Light: LightKind.Chandelier ),
		new( "charm_hanging", Realm( "sm_prop_charm_hanging_01", "sm_prop_charm_hanging_02", "sm_prop_charm_hanging_03" ), AnchorKind.CeilingHung, 0.2f, 0.18f, 0.74f, false ),
		// wall plaques of weapons: back face 2 cm behind the pivot, flush at Embed 0
		new( "weapon_plaque", Realm( "sm_prop_hanging_weapon_01", "sm_prop_hanging_weapon_02", "sm_prop_hanging_weapon_03" ), AnchorKind.WallMounted, 0.99f, 0.15f, 0.92f, false, MountHeight: 1.6f ),
		new( "camp_basket", Realm( "sm_prop_camp_basket_01" ), AnchorKind.Floor, 1.08f, 1.0f, 0.94f, true ),
		// a flat roll and a bundle, both 2.7 m long, modelled about their middle: Bury 0 lays them on the floor
		new( "bedroll", Realm( "sm_prop_camp_roll_02", "sm_prop_camp_roll_01" ), AnchorKind.Floor, 2.7f, 0.44f, 0.88f, false, Bury: 0f ),
		new( "camp_pot", Realm( "sm_prop_camp_pot_01" ), AnchorKind.Floor, 0.5f, 0.5f, 0.64f, false ),
		// the hell sigils: 2.3-3 m plaques centred on their pivot, hung at 2.5 m so they span most of a 5 m wall
		new( "hell_symbol", Realm( "sm_prop_hell_symbol_01", "sm_prop_hell_symbol_03", "sm_prop_hell_symbol_05" ), AnchorKind.WallMounted, 3.0f, 0.28f, 3.31f, false, MountHeight: 2.5f ),
		// ---- mining and forge work (user), measured with tools/fbx_bounds.py
		// the forge tools hang on the wall: modelled upright with the head across local X, so Turn 90 lays the head along
		// the wall. Hammer 0.4 x 0.75 tall, pickaxes 0.51-0.98 x 0.6-0.85, spade 0.34 x 1.23, tongs 0.18 x 1.13; the
		// deepest reaches 0.2 m below its pivot
		new( "tool_wall", Realm( "sm_prop_forge_hammer_01", "sm_prop_forge_hammer_02", "sm_prop_forge_pickaxe_01", "sm_prop_forge_pickaxe_03", "sm_prop_forge_spade_01", "sm_prop_forge_tongs_01" ),
			AnchorKind.WallMounted, 0.98f, 0.22f, 1.23f, false, MountHeight: 1.25f, Turn: 90f ),
		// DungeonPack's machine gears are flat discs standing on edge, centred: they hang flat on a wall. 0.6-1.1 m across
		new( "cog_wall", Pack( "sm_prop_tech_cog_01", "sm_prop_tech_cog_02", "sm_prop_tech_cog_03" ), AnchorKind.WallMounted, 1.0f, 0.22f, 1.1f, false, MountHeight: 2.1f ),
		// DungeonPack's turbine housing, 1.32 x 0.59 x 1.87 m, pivot at its base
		new( "turbine", Pack( "sm_prop_tech_turbine_01" ), AnchorKind.WallFlush, 1.32f, 0.59f, 1.87f, true ),
		// drinking vessels for the feast tables: tankard 0.24 x 0.16 x 0.21, mug 0.3 x 0.37 x 0.3, goblet 0.12 x 0.24 tall
		new( "tankard", Realm( "sm_prop_tankard_01", "sm_prop_mug_01" ), AnchorKind.Floor, 0.37f, 0.3f, 0.3f, false ),
		new( "goblet", Realm( "sm_prop_goblet_01" ), AnchorKind.Floor, 0.12f, 0.12f, 0.24f, false ),
	};

	/// <summary>The stone Tops with the forge-hold's tankards and goblets on its tables.</summary>
	private static readonly Dictionary<string, FillEntry[]> RealmsTops = new( DecorCatalog.Tops )
	{
		["table_long"] = E( ("candles", 25), ("tankard", 20), ("goblet", 8), ("book_pile", 10), ("book", 5), ("vial", 5), ("", 27) ),
		["table_round"] = E( ("candles", 30), ("tankard", 20), ("goblet", 10), ("book_pile", 10), ("", 30) ),
		["table_low"] = E( ("candles", 35), ("tankard", 20), ("candle", 15), ("", 30) ),
	};

	/// <summary>The forge-hold's set pieces, all 1 x 1: U toward the room's entrance, V right, cell units; H metres up for table tops.</summary>
	private static readonly SetPiece[] RealmsSets =
	{
		new( "forge_works", 1, 1, new SetItem[]
		{
			new( "anvil", 0, 0, 90 ), new( "cog_box", -0.3f, 0.3f, 0 ), new( "candle_stand", 0.25f, -0.25f, 0, 0.7 ),
			new( "cog_pile", 0.28f, 0.28f, 30, 0.7 ),
		} ),
		new( "machinery", 1, 1, new SetItem[]
		{
			new( "cog_box", 0, 0, 0 ), new( "cog_large", 0.3f, -0.2f, 0, 0.8 ), new( "cog_small", -0.25f, 0.25f, 0, 0.7 ),
			new( "cog_pile", 0.28f, 0.28f, 0, 0.6 ),
		} ),
		new( "ore_cart", 1, 1, new SetItem[]
		{
			new( "cart", 0, 0, 0 ), new( "crystal_rock", 0.25f, 0.25f, 0, 0.8 ), new( "crystal_rock", -0.22f, 0.28f, 40, 0.6 ),
			new( "cog_pile", 0.28f, -0.22f, 0, 0.6 ), new( "gem_cluster", 0.1f, -0.3f, 0, 0.5 ),
		} ),
		new( "crystal_shrine", 1, 1, new SetItem[]
		{
			new( "crystal_cluster", 0, 0, 0 ), new( "crystal_rock", 0.35f, 0, 0 ), new( "crystal_rock", -0.3f, 0.3f, 60, 0.8 ), new( "crystal_rock", -0.3f, -0.3f, 120, 0.8 ),
			new( "gem_cluster", 0.15f, -0.35f, 0, 0.7 ), new( "gem_cluster", -0.35f, 0.1f, 0, 0.7 ), new( "charm_hanging", 0.3f, 0.3f, 0, 0.6 ),
		} ),
		// the forge-hold's hoard is its crystal: gems around a chest (the gold went with every coin pile, user 2026-09-15)
		new( "gem_hoard", 1, 1, new SetItem[]
		{
			new( "chest", 0, 0, 0 ), new( "gem_cluster", 0.25f, 0.2f, 0, 0.8 ), new( "gem_cluster", -0.22f, -0.25f, 0, 0.8 ),
			new( "crystal_rock", 0.2f, -0.3f, 0, 0.7 ), new( "gem_cluster", -0.3f, 0.2f, 0, 0.7 ), new( "chest", 0.3f, -0.3f, 30, 0.7 ), new( "candles", -0.3f, 0.3f, 0, 0.6 ),
		} ),
		new( "crypt_sarcophagus", 1, 1, new SetItem[]
		{
			new( "sarcophagus", 0, 0, 0 ), new( "dead_body", 0, 0.35f, 90, 0.7 ), new( "candles", -0.32f, 0.25f, 0, 0.8 ),
			new( "statue_head", 0.3f, -0.3f, 0, 0.4 ), new( "bone_pile_small", -0.28f, -0.3f, 0, 0.5 ),
		} ),
		new( "dwarf_feast", 1, 1, new SetItem[]
		{
			new( "table_long", 0, 0, 0 ), new( "bench", 0.25f, 0, 180 ), new( "bench", -0.25f, 0, 0 ),
			new( "camp_pot", 0.03f, -0.2f, 0, 1, 0.91f ), new( "camp_pot", -0.04f, 0.22f, 0, 0.8, 0.91f ),
			new( "tankard", 0.05f, 0.4f, 30, 0.8, 0.91f ), new( "tankard", -0.05f, -0.38f, 200, 0.7, 0.91f ), new( "goblet", 0.02f, 0.12f, 0, 0.5, 0.91f ),
			new( "candles", 0, 0.05f, 0, 1, 0.91f ), new( "candles", 0.04f, -0.28f, 0, 0.7, 0.91f ), new( "candle_stand", 0.38f, 0.38f, 0, 0.5 ),
		} ),
		new( "bedroll_camp", 1, 1, new SetItem[]
		{
			new( "bedroll", 0, 0, 0 ), new( "bedroll", 0.3f, 0.2f, 0, 0.7 ), new( "camp_basket", -0.3f, -0.3f, 0, 0.6 ),
			new( "candle_stand", 0.3f, -0.3f, 0, 0.5 ), new( "camp_pot", -0.1f, 0.35f, 0, 0.5 ),
		} ),
	};

	/// <summary>
	/// The forge-hold's role tables, slot by slot over the stone ones. The Centre entries are the point: stone's
	/// Armory (50 % empty), Storage (75 %) and Barracks (37 %) left big open middles, and the user wanted things there.
	/// </summary>
	private static readonly Dictionary<DecorRole, Dictionary<SlotKind, FillEntry[]>> RealmsTables = new()
	{
		[DecorRole.Armory] = new()
		{
			[SlotKind.Centre] = Mix( S( ("forge_works", 55), ("machinery", 35) ), E( ("", 10) ) ),
			[SlotKind.EdgeStrip] = E( ("weapon_rack", 28), ("anvil", 12), ("chest", 12), ("bench", 8), ("barrel_shelf", 8), ("cog_box", 6), ("turbine", 5), ("", 21) ),
			[SlotKind.Corner] = E( ("chest", 20), ("brazier", 12), ("cog_pile", 15), ("crystal_rock", 8), ("", 45) ),
			[SlotKind.Wall] = E( ("torch", 35), ("weapon_plaque", 16), ("tool_wall", 12), ("cog_wall", 5), ("banner", 10), ("", 22) ),
		},
		[DecorRole.Storage] = new()
		{
			[SlotKind.Centre] = Mix( S( ("ore_cart", 40), ("machinery", 30), ("rubble_pillar", 10) ), E( ("", 20) ) ),
			[SlotKind.EdgeStrip] = E( ("barrel_shelf", 22), ("barrel_shelf_empty", 8), ("barrel_shelf_broken", 8), ("chest", 12), ("plank", 8), ("cog_box", 6), ("turbine", 5), ("", 31) ),
			[SlotKind.Wall] = E( ("torch", 15), ("tool_wall", 20), ("cog_wall", 6), ("", 59) ),
			[SlotKind.Corner] = E( ("cog_pile", 20), ("plank", 15), ("camp_basket", 12), ("crystal_rock", 8), ("", 45) ),
			[SlotKind.Scatter] = E( ("gem_cluster", 8), ("cog_small", 12), ("cog_pile", 10), ("camp_basket", 8), ("brick", 10), ("", 52) ),
		},
		[DecorRole.Barracks] = new()
		{
			[SlotKind.Centre] = Mix( S( ("bedroll_camp", 30), ("table_reading", 25), ("dwarf_feast", 20), ("campfire_bones", 8) ), E( ("", 17) ) ),
			[SlotKind.EdgeStrip] = E( ("bed", 55), ("bed_big", 5), ("weapon_rack", 8), ("bedroll", 10), ("chest_wood", 8), ("stool", 6), ("", 8) ),
			[SlotKind.Wall] = E( ("torch", 30), ("banner", 10), ("weapon_plaque", 12), ("", 48) ),
		},
		[DecorRole.Crypt] = new()
		{
			[SlotKind.Centre] = Mix( S( ("crypt_sarcophagus", 35), ("coffin_open", 30), ("coffin_row", 25) ), E( ("", 10) ) ),
			[SlotKind.EdgeStrip] = E( ("coffin", 40), ("sarcophagus", 20), ("bone_pile", 12), ("vessel", 8), ("coffin_open", 10), ("", 10) ),
			[SlotKind.Scatter] = E( ("bone_pile_small", 30), ("bone_pile_small", 15), ("dead_body", 15), ("statue_broken", 8), ("candles", 12), ("shard", 10), ("candle", 10) ),
		},
		[DecorRole.Shrine] = new()
		{
			[SlotKind.Centre] = S( ("altar_grand", 40), ("altar_small", 45), ("crystal_shrine", 45) ),
			[SlotKind.EdgeStrip] = E( ("candle_stand", 30), ("crystal_rock", 20), ("totem", 10), ("rune_block", 10), ("", 30) ),
			[SlotKind.Corner] = E( ("obelisk", 25), ("statue", 30), ("totem", 20), ("crystal_cluster", 15), ("candle_stand", 10) ),
		},
		[DecorRole.Throne] = new()
		{
			[SlotKind.Corner] = E( ("statue", 45), ("totem", 20), ("brazier", 25), ("obelisk", 10) ),
			[SlotKind.Scatter] = E( ("candles", 30), ("gem_cluster", 20), ("", 50) ),
		},
		[DecorRole.MessHall] = new()
		{
			[SlotKind.Centre] = Mix( S( ("dwarf_feast", 50), ("table_feast", 30), ("cauldron_brew", 10) ), E( ("", 10) ) ),
			[SlotKind.EdgeStrip] = E( ("bench", 18), ("stool", 12), ("barrel_shelf", 10), ("stone_table", 8), ("table_low", 6), ("camp_basket", 6), ("", 40) ),
			[SlotKind.Ceiling] = E( ("chandelier", 70), ("hanging_lantern", 30) ),
		},
		[DecorRole.Library] = new() { [SlotKind.Ceiling] = E( ("chandelier", 60), ("hanging_lantern", 40) ) },
		// the camp pots (this kit's `jar`) used to ride the stone alchemy tables; those lost their jars with the
		// DungeonPack ones, so the forge-hold names its pots itself
		[DecorRole.Alchemy] = new()
		{
			[SlotKind.Ceiling] = E( ("chandelier", 60), ("hanging_lantern", 40) ),
			[SlotKind.Corner] = E( ("jar", 25), ("candle_stand", 25), ("potion_pole", 20), ("", 30) ),
			[SlotKind.Scatter] = E( ("jar", 25), ("book_pile", 25), ("shard", 15), ("candles", 15), ("", 20) ),
		},
		[DecorRole.Secret] = new() { [SlotKind.Centre] = S( ("gem_hoard", 60), ("loot_hoard", 40) ) },
	};

	public static readonly DungeonKit Realms = new()
	{
		Id = "realms",
		FloorModels = Realm( "sm_env_dwarf_floor_01", "sm_env_dwarf_floor_05", "sm_env_dwarf_floor_08", "sm_env_dwarf_floor_12", "sm_env_dwarf_floor_17", "sm_env_dwarf_floor_20" ),
		PlainFloorModel = R( "sm_env_dwarf_floor_01" ),
		RuneFloorModels = Realm( "sm_env_dwarf_floor_10", "sm_env_dwarf_floor_14" ),
		// _27 is dead flat (the plain slab); 03 and 13 are the user's picks
		CeilingModels = Realm( "sm_env_dwarf_floor_27", "sm_env_dwarf_floor_03", "sm_env_dwarf_floor_13", "sm_env_dwarf_floor_15", "sm_env_dwarf_floor_23" ),
		PlainCeilingChance = 0.7f,
		CeilingDrop = 9.84f,   // 0.25 m: the 0.23 m slab plus its relief, lowered to sit under the plane
		CeilingFlipped = true,
		FloorHasUnderside = true,
		WallModel = R( "sm_env_dwarf_wall_01" ),
		QuarterWallModel = R( "sm_env_dwarf_wall_short_01" ),
		QuarterWallScale = null,
		QuarterFullLength = true,
		WallHalf = 11.02f,      // 0.28 m once the wall is centred
		WallSetback = 0.14f,
		DoubleFaced = true,
		SecretWallModel = R( "sm_env_dwarf_wall_04" ),
		PlainWallModel = R( "sm_env_dwarf_wall_03" ),
		BrokenSecretWallModel = R( "sm_env_dwarf_wall_archway_04" ),
		// Archway_04: hole at 147.3..352.7 cm of the 5 m piece, jambs to 347.5 cm (the crown reaches 421); Wall_04's front is 0.31 m proud
		SecretOpenAlongMin = 58.0f,
		SecretOpenAlongMax = 138.9f,
		SecretOpenTop = 136.8f,
		SecretFaceForward = 12.2f,
		SecretRubble = Realm( "sm_env_tiles_loose_05", "sm_env_tiles_loose_06", "sm_prop_rock_small_01", "sm_prop_rock_small_02", "sm_prop_rock_small_03" ),
		WallSwaps = RealmsWallSwaps,
		DoorFrameModels = Realm( "sm_env_dwarf_wall_doorframe_single_01", "sm_env_dwarf_wall_doorframe_single_02", "sm_env_dwarf_wall_doorframe_double_01", "sm_env_dwarf_wall_doorframe_double_02", "sm_env_dwarf_wall_archway_02" ),
		DoorInnerFrameModels = null,
		DoorInnerFrameScale = null,
		DoorFits = DoorCatalog.RealmsFits,
		BoardModels = null,
		BoardChance = 0f,
		StairModel = R( "sm_env_dwarf_stairs_01" ),
		StairMirrorY = true,
		StairWallModel = R( "sm_env_dwarf_stairs_wall_01" ),
		StairWallYawOffset = 270f,
		StairCeilingModel = R( "sm_env_dwarf_stairs_01" ),
		StairCeilingFlipped = true,
		SquarePillarModel = R( "sm_env_dwarf_pillar_03" ),
		RoundPillarModel = R( "sm_env_dwarf_pillar_01" ),
		InteriorPillarRadius = 0.62f,
		InnerCornerModel = R( "sm_env_dwarf_pillar_07" ),
		OuterCornerModel = R( "sm_env_dwarf_pillar_08" ),
		CornerCentred = true,
		CornerGapCells = 0.16f,
		// the dwarf trap heads (user): both pivoted at their middle, so they hang a little higher than the stone ones
		FlameHeadModel = R( "sm_prop_dwarf_trap_head_01" ),
		FlameHeadMouth = R( "sm_prop_dwarf_trap_head_mouth_01" ),
		FlameHeadHeight = 1.5f,
		FlameHeadYaw = 0f,
		AcidHeadModel = R( "sm_prop_dwarf_trap_head_02" ),
		AcidHeadMouth = R( "sm_prop_dwarf_trap_head_mouth_02" ),
		AcidHeadHeight = 1.1f,
		AcidHeadYaw = 0f,
		// the only ones under 4.2 m; 01/03 hang 10 m and 04 5.5 m, straight through the floor
		Banners = Realm( "sm_prop_banner_06", "sm_prop_banner_damaged_03", "sm_prop_banner_damaged_04", "sm_prop_banner_damaged_05" ),
		Props = Overlay( RealmsProps ),
		// user: no stone fountain in the forge-hold; and no stone broken barrels either, since the only Realms
		// barrels he named are the four breakable ones above
		Excluded = new() { "fountain", "barrel_broken" },
		Sets = OverlaySets( RealmsSets ),
		Tables = RealmsTables,
		HallwayClutter = E( ("pebbles", 30), ("cog_small", 10), ("cog_pile", 8), ("crystal_rock", 10), ("bricks", 12), ("", 30) ),
		HallwayLitter = null,
		Tops = RealmsTops,
		HallwayCeiling = null,
		Composites = null,
		SigilProp = "hell_symbol",
		SigilsMin = 5,
		SigilsMax = 10,
		ExitProp = "portal",
		TopFix = DecorCatalog.TopFix,
		SkinPairs = Array.Empty<(string, string)>(),
		BoneLitterScale = 0.7,
		// the flipped slab hangs 0.25 m under the plane, 0.1 lower than the stone one
		WebTop = 4.55f,
	};

	// a property, not a field: the Gaol instance lives in another part of this partial record, and the order in
	// which the parts' static initialisers run is not defined, so a field here could capture a null Gaol
	public static DungeonKit[] All => _all ??= new[] { Stone, Mossy, Realms, Gaol };
	private static DungeonKit[] _all;

	public static DungeonKit For( DungeonTheme theme ) => theme switch
	{
		DungeonTheme.Mossy => Mossy,
		DungeonTheme.Realms => Realms,
		DungeonTheme.Gaol => Gaol,
		_ => Stone,
	};
}

/// <summary>How one piece of a composite collides: as the composite asked (Inherit), or its own way. A composite
/// spawned with a Box collider gets ONE box over the whole assembly and its pieces none, unless a piece says
/// otherwise (an iron-bar panel keeps a mesh collider so a bolt passes between the bars).</summary>
public enum PieceCollider { Inherit, None, Box, Model }

/// <summary>
/// One piece of a composite kit piece: a model (or another composite) at an offset in METRES in the composite's
/// own frame (X depth, Y along, Z up — the same frame the builder places the composite in), turned by Yaw about
/// up and Pitch about its left axis (multiples of 90), and scaled per axis. The composite's own pivot convention
/// is whatever its pieces add up to: a wall composite runs +Y from the origin and faces +X like a wall model.
/// </summary>
public sealed record KitPiece( string Model, float Dx = 0f, float Dy = 0f, float Dz = 0f, float Yaw = 0f, float Pitch = 0f,
	float Sx = 1f, float Sy = 1f, float Sz = 1f, PieceCollider Collider = PieceCollider.Inherit );
