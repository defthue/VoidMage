using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The mossy floors' greenery: what has grown in since the dungeon was abandoned. Runs over EVERY tier,
/// rooms and corridors alike, in layers — things rooted in the floor, things clinging to the walls, things
/// hanging off the ceiling, and last the root mats, which get a pass of their own (see Roots).
///
/// Nothing here blocks. Every entry is non-blocking, which means the builder gives it no collider at all and
/// you walk straight through it (the trees are the one exception and take a slim capsule). That is the point:
/// the greenery dresses the room without ever changing where you can go. It is allowed onto the walking line
/// at reduced density for the same reason — moss on the path is the whole idea.
///
/// Its own RNG stream per tier and sorted iteration throughout, like every other decoration pass, so a seed
/// dresses identically on every peer. It draws nothing from the global pass's stream, so a stone floor is
/// byte-for-byte unchanged by the existence of this file.
/// </summary>
public static class DecorGreenery
{
	// Rolled per candidate slot, as everywhere else in the decorator. Tuned by eye in-engine; the builder's
	// GreeneryDensity property scales all of them together.
	private const double FloorDensity = 0.42;
	private const double WallDensity = 0.42;
	/// <summary>The hanging spiky vines: a couple a floor, not a hanging garden. The breach curtains are where
	/// that model earns its keep.</summary>
	private const double CeilingDensity = 0.05;
	/// <summary>
	/// Whole-cell pieces (trees, the big grass tufts, the biggest orb). Rolled per free cell OFF the walking
	/// line, and most cells are on it or already hold furniture, so this reads higher than it plays: a handful
	/// of trees a floor, a small grove where a hall has the room for one.
	/// </summary>
	private const double BigDensity = 0.28;
	/// <summary>Off the walking line greenery grows freely; on it, thinner — you should be able to see the floor.</summary>
	private const double WalkShare = 0.4;
	/// <summary>How far under the base size a piece may come out. Downward only: "half the size" is a ceiling,
	/// not an average, so nothing ever exceeds BaseSize.</summary>
	private const double SizeVariance = 0.15;

	// ---- the root mats. Their own densities and their own builder property, because "cover the dungeon in
	// roots" is a separate dial from how much grass and how many mushrooms a floor gets.
	/// <summary>Rolled per floor quarter, TWICE — on the walking line as much as off it. Roots underfoot is the
	/// whole point: it is what makes the green in the floor tiles read as something that grew. Two rolls because
	/// a quarter is 2.5 m and a mat 0.85-1.8 m, and mats are allowed to tangle with each other.</summary>
	private const double RootFloorDensity = 0.34;
	/// <summary>Rolled per wall face: once for each half of its 5 m run low against the floor, and once more
	/// high under the ceiling.</summary>
	private const double RootWallLowDensity = 0.75, RootWallHighDensity = 0.40;
	/// <summary>Rolled per WALL FACE, not per cell — that is what puts the ceiling mats along the wall lines
	/// rather than out in the middle of a room.</summary>
	private const double RootCeilingDensity = 0.75;
	/// <summary>How far a mat beds into the surface it lies on, in source units (M = 39.37 per metre), rolled
	/// per mat so a bed of them is not uniformly deep (user: "sink all of them in 2-3 units").</summary>
	private const double RootSinkMin = 2.0, RootSinkMax = 3.0;
	/// <summary>
	/// Clear height from a floor's walking surface to the slab above it, metres. A room is two levels; the slab
	/// hangs CeilingDrop under the grid plane at the top of them and the walking surface sits a tile's collider
	/// top above the one below, so (2 * 98.425 - 6 - 3.94) / 39.37. Wall mounts are measured from that surface
	/// (BuildProps uses `up = surface` for WallMounted), so this is what the high band counts down from.
	/// </summary>
	private const float RoomHeight = 4.74f;
	/// <summary>The low wall band's own size cap. At the full RootScaleMax a mat mounted at 0.25 m would rise to
	/// 1.99 m and grow into the torch cups, which start at 1.70.</summary>
	private const double WallLowScaleMax = 1.6;
	/// <summary>How far out of a cell's centre a ceiling mat is pushed toward its walled edge, in cell units.
	/// At RootScaleMax the mat's 0.55 m half-extent then overhangs the wall plane by less than its thickness.</summary>
	private const float CeilingEdgeOffset = 0.34f;
	/// <summary>How far a wall mat reaches above its own mount, in metres per unit of scale: the model's local
	/// -X extent, which Tilt 90 turns into +Z. The high band's mount is RoomHeight minus this, so a big mat is
	/// hung lower and never pokes through the slab.</summary>
	private const float WallMatRise = 0.662f;
	/// <summary>
	/// A mat is only 85 cm across and a cell is 5 m, so at pack size a mat covers about a twenty-fifth of the
	/// cell it is in and a floor's worth of them still reads as scattered rather than overgrown. Scaling up is
	/// the cheap way out: area per renderer is free and the renderer count is not, so these run bigger than
	/// anything else in this file. At the top of the range a mat is 1.8 m across, which still fits inside its
	/// own cell wherever Jitter can put it. The spread also stops a bed of them looking stamped out.
	/// </summary>
	private const double RootScaleMin = 1.0, RootScaleMax = 2.1;

	/// <summary>
	/// The moss sheet alone is modelled at set-dressing size and reads as a boulder in a room; it is capped to
	/// half (user, 2026-09-10). Everything else is placed at the size the pack ships it at.
	///
	/// Applied here rather than as a PropDef.MaxHeight because the moss shares its def with nothing but itself
	/// today — but `roots` and `mushrooms` in these same tables ARE shared with the stone dungeon's debris, so
	/// keeping every size decision in this one pass is what stops floors 1-5 shifting under it.
	/// </summary>
	private static float BaseSizeOf( PropDef def ) => def.Id is "moss_floor" or "moss_patch" ? 0.5f : 1f;
	/// <summary>
	/// A piece wider than this only goes on a cell with tier on all four sides, or it grows out through a wall.
	/// A cell is 5 m and a quarter's centre sits 1.25 m from two of its edges, so anything up to 2.5 m across
	/// still lands inside its own cell wherever it is jittered.
	/// </summary>
	private const float RoomyExtent = 2.5f;

	/// <summary>
	/// Rooted in the floor, a quarter-cell at a time. The root mats are NOT in here — they get their own
	/// claim-free layer below, because competing for quarter slots is exactly what kept them out of the rooms.
	/// </summary>
	private static readonly FillEntry[] FloorTable = Entries(
		("moss_floor", 18), ("grass", 16), ("mushrooms", 14), ("flowers", 12),
		("mushroom_giant", 8), ("", 30) );

	/// <summary>Against a wall: mounted on the face (moss sheets, mushroom brackets) or rooted at its foot and
	/// creeping out across the floor. The spiky trailing vine is rare here — it is the breach curtain's model,
	/// and it means "this hurts".</summary>
	private static readonly FillEntry[] WallTable = Entries( ("mushroom_wall", 40), ("moss_patch", 26), ("vine_ground", 6), ("", 12) );

	/// <summary>Hanging from a ceiling slab. See CeilingDensity: rare.</summary>
	private static readonly FillEntry[] CeilingTable = Entries( ("vine_hang", 1) );

	/// <summary>Whole-cell pieces. Only ever placed off the walking line, on a cell with tier all around it.</summary>
	// the tree is weighted up because it is the one that fits anywhere; the wide grass needs a cell with tier all
	// around it, which most rooms simply do not have, so a lot of its rolls come to nothing
	// The twisted trees ask for more weight than the dandelions to land the same number: capped to 4.6 m they
	// are still 2.58 m across, over RoomyExtent, so most of their rolls need a cell with tier on all four sides
	// and come to nothing. The dandelions, capped to half that, fit anywhere.
	private static readonly FillEntry[] BigTable = Entries( ("tree", 40), ("tree_dandelion", 22), ("grass_wide", 30), ("", 24) );

	/// <summary>The root mats, one per layer. Single-entry tables rather than bare ids so DecorCatalog.MissingIds
	/// and the gentest audit line pick them up through AllEntries like everything else here.</summary>
	private static readonly FillEntry[] RootFloorTable = Entries( ("roots_green", 1) );
	private static readonly FillEntry[] RootWallTable = Entries( ("roots_wall", 1) );
	private static readonly FillEntry[] RootCeilingTable = Entries( ("roots_ceiling", 1) );

	private static FillEntry[] Entries( params (string id, double weight)[] items )
		=> items.Select( i => new FillEntry( i.id, i.weight ) ).ToArray();

	/// <summary>Every id this pass can ask for. DecorCatalog.MissingIds audits it: these tables are not role
	/// tables, so nothing else would ever notice a typo in them.</summary>
	public static IEnumerable<FillEntry> AllEntries
		=> FloorTable.Concat( WallTable ).Concat( CeilingTable ).Concat( BigTable )
			.Concat( RootFloorTable ).Concat( RootWallTable ).Concat( RootCeilingTable );

	/// <summary>The three root ids, for the harness: they dwarf everything else and want their own audit line.</summary>
	public static readonly string[] RootIds = { "roots_green", "roots_wall", "roots_ceiling" };

	/// <param name="density">Multiplier on every greenery roll; 1 is the tuned default, 0 turns the pass off.</param>
	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ,
		double density = 1.0 )
	{
		if ( density <= 0 ) return;
		// ctx.Ceilings has a slot for every cell of a tier, but a cell with a room above it has no ceiling
		// slab at all (Layout.Ceilings) — hanging a vine there would dangle it from the upstairs floor.
		var slabs = new HashSet<(int x, int z, int l)>( lay.Ceilings );

		foreach ( var kv in ctxs.OrderBy( kv => kv.Key ) )
		{
			var ctx = kv.Value;
			var t = ctx.T;
			if ( t.Cells.Count == 0 || t.Kind == TierKind.Tower ) continue;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( t.Id, 0x6E33 ) ) );

			Big( ctx, res, occ, rng, density );
			FloorLayer( ctx, res, occ, rng, density );
			WallFaces( ctx, res, occ, rng, density );
			CeilingLayer( ctx, res, occ, rng, density, slabs );
		}
	}

	/// <summary>
	/// The root mats, run as the LAST decoration pass of the whole floor — after the room fill, after the global
	/// pass, after the breakables. They have to be last because they are the one pass whose rule is "do not touch
	/// anything", and a crate placed after them would be placed straight through one. Its own RNG stream per
	/// tier, so it is independent of the greenery streams above and of the breakables' own.
	/// </summary>
	/// <param name="density">The builder's Roots property; 1 is the tuned default, 0 turns the mats off.</param>
	public static void RootPass( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ, double density = 1.0 )
	{
		if ( density <= 0 ) return;
		var slabs = new HashSet<(int x, int z, int l)>( lay.Ceilings );
		var standing = FloorFootprints( res );
		var blocked = BlockedFaces( res );
		foreach ( var kv in ctxs.OrderBy( kv => kv.Key ) )
		{
			var ctx = kv.Value;
			if ( ctx.T.Cells.Count == 0 || ctx.T.Kind == TierKind.Tower ) continue;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0x600D ) ) );
			Roots( ctx, res, occ, rng, density, slabs, standing, blocked );
		}
	}

	/// <summary>Tier on all four sides: a sprawling piece here cannot reach out through a wall.</summary>
	private static bool Roomy( TierCtx ctx, int x, int z )
		=> Dirs.All.All( d => ctx.Has( x + d.Dx(), z + d.Dz() ) );

	/// <summary>
	/// How wide the piece will actually be once it is spawned. The catalog's extents are the model's own, but
	/// MaxHeight shrinks it uniformly before it ever reaches the floor — a "giant" mushroom capped to knee
	/// height is 26 cm across, not 1.8 m, and judging it on the raw number kept it out of every room.
	/// </summary>
	private static float Reach( PropDef def )
	{
		float shrink = def.MaxHeight > 0f && def.Height > def.MaxHeight ? def.MaxHeight / def.Height : 1f;
		return Math.Max( def.Width, def.Depth ) * shrink * BaseSizeOf( def );
	}

	/// <summary>At most the base size and up to 15% under it, so a bed of moss is not one model stamped out five times.</summary>
	private static float RollScale( DungeonRng rng, PropDef def )
		=> BaseSizeOf( def ) * (float)(1.0 - rng.NextDouble() * SizeVariance);

	/// <summary>Whole-cell pieces first, so they get the pick of the free cells before the small stuff claims quarters.</summary>
	private static void Big( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, double density )
	{
		int L = ctx.T.Level;
		foreach ( var (x, z) in ctx.T.Sorted() )
		{
			// never on the walking line: these are the only greenery you can actually bump into
			if ( !ctx.Clear( x, z ) ) continue;
			if ( !occ.TileFree( x, z, L ) ) continue;
			if ( rng.NextDouble() >= BigDensity * density ) continue;
			var id = Decor.Pick( rng, BigTable );
			if ( id == "" ) continue;
			var def = res.Kit.Props[id];
			// a capped tree ends up under 2 m across and sits happily in the middle of any cell; the big grass
			// tufts really are cell-sized and need tier all around them
			if ( Reach( def ) > RoomyExtent && !Roomy( ctx, x, z ) ) continue;
			occ.ClaimTile( x, z, L );
			Decor.Add( res, ctx, def, rng, AnchorKind.Floor, x, z, L, Dir.N,
				x + 0.5f, z + 0.5f, 0, def.Lift, rng.RandRange( 360 ), 3, scale: RollScale( rng, def ) );
		}
	}

	private static void FloorLayer( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, double density )
	{
		int L = ctx.T.Level;
		foreach ( var (x, z) in ctx.T.Sorted() )
		{
			bool clear = ctx.Clear( x, z );
			bool roomy = Roomy( ctx, x, z );
			double d = FloorDensity * density * (clear ? 1.0 : WalkShare);
			for ( int q = 0; q < 4; q++ )
			{
				if ( !occ.QuarterFree( x, z, L, q ) ) continue;
				if ( rng.NextDouble() >= d ) continue;
				var id = Decor.Pick( rng, FloorTable );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( def.Tile || def.Wide ) continue;
				// the ground vines really do trail 3 m from their pivot
				if ( !roomy && Reach( def ) > RoomyExtent ) continue;
				var (gx, gz) = Jitter( rng, x, z, q, def.Width );
				occ.ClaimQuarter( x, z, L, q );
				Decor.Add( res, ctx, def, rng, AnchorKind.Floor, x, z, L, Dir.N,
					gx, gz, 0, def.Lift, rng.RandRange( 360 ), 3, scale: RollScale( rng, def ) );
			}
		}
	}

	/// <summary>
	/// Wall greenery claims the Band layer only. Mount and High belong to the torches and banners, which are
	/// all placed before this pass runs, so checking WallFree keeps moss off a lit face while claiming Band
	/// keeps two pieces off each other. Handles both anchors: WallMounted pieces sit on the face at a rolled
	/// height, WallFlush ones stand at its foot and trail into the room.
	/// </summary>
	private static void WallFaces( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, double density )
	{
		int L = ctx.T.Level;
		foreach ( var s in ctx.WallSlots )
		{
			if ( s.NearDoor ) continue;
			if ( !occ.WallFree( s.X, s.Z, L, s.D ) ) continue;
			if ( !occ.FaceFree( s.X, s.Z, L, s.D, WallLayer.Band ) ) continue;
			if ( rng.NextDouble() >= WallDensity * density ) continue;
			var id = Decor.Pick( rng, WallTable );
			if ( id == "" ) continue;
			var def = res.Kit.Props[id];
			// something already stands against this wall: a vine trailing out of it would run through the furniture
			if ( def.Anchor == AnchorKind.WallFlush && occ.BandHeight( s.X, s.Z, L, s.D ) > 0.6f ) continue;
			occ.ClaimFace( s.X, s.Z, L, s.D, WallLayer.Band );
			// along the wall and up it: moss does not grow on a grid
			float along = (float)(rng.NextDouble() - 0.5) * 0.5f;
			float height = def.Anchor == AnchorKind.WallMounted
				? def.MountHeight + (float)(rng.NextDouble() - 0.5) * 0.7f
				: def.Lift;
			Decor.Add( res, ctx, def, rng, def.Anchor, s.X, s.Z, L, s.D,
				s.X + 0.5f, s.Z + 0.5f, along, height, Decor.YawOf( s.D.Opp() ), 3, scale: RollScale( rng, def ) );
		}
	}

	private static void CeilingLayer( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, double density, HashSet<(int x, int z, int l)> slabs )
	{
		int L = ctx.T.Level;
		foreach ( var s in ctx.Ceilings )
		{
			if ( s.NearDoor ) continue;
			if ( !slabs.Contains( (s.X, s.Z, L + 2) ) ) continue;
			if ( !occ.CeilFree( s.X, s.Z, L ) ) continue;
			if ( rng.NextDouble() >= CeilingDensity * density ) continue;
			var id = Decor.Pick( rng, CeilingTable );
			if ( id == "" ) continue;
			var def = res.Kit.Props[id];
			occ.ClaimCeil( s.X, s.Z, L );
			float gx = s.X + 0.3f + (float)rng.NextDouble() * 0.4f;
			float gz = s.Z + 0.3f + (float)rng.NextDouble() * 0.4f;
			Decor.Add( res, ctx, def, rng, AnchorKind.CeilingHung, s.X, s.Z, L, Dir.N,
				gx, gz, 0, def.MountHeight, rng.RandRange( 360 ), 3, scale: RollScale( rng, def ) );
		}
	}

	/// <summary>
	/// The root mats over the floor, the wall feet and the ceiling slabs of every room and corridor. This is the
	/// backbone of the mossy look: sm_env_roots_01/02 are the pack's only vine WITHOUT spikes, and a thick creep
	/// of them everywhere is what makes the green in the tiles read as something that grew there.
	///
	/// It CLAIMS NOTHING, which is the whole reason it is a layer of its own rather than an entry in the tables
	/// above. By the time greenery runs, a dressed room's off-lane floor quarters are nearly all claimed by the
	/// role fill while a corridor's are nearly all free — so a claiming roots pass piles up in the corridors and
	/// vanishes from exactly the rooms you stand in and look around. A mat is a flat 19 cm thing you walk over;
	/// it has no business reserving floor space from a bookcase.
	///
	/// The rule the user set is about CONTACT, not about slots: "any model it doesnt touch can be placed, as
	/// long as they dont touch". That is a CONTACT rule, not a slot rule, and the two give very different
	/// answers: a quarter is 2.5 m square and a stool is 63 cm, so sharing a quarter usually means no contact at
	/// all. Using the slots as the test is what would carpet the corridors and leave the rooms bare.
	///
	///  - floor: a real footprint test against everything already standing on this floor (FloorFootprints).
	///    Overlay props — rugs, the pentagram, and the mats themselves — are not in that index, so mats tangle
	///    with each other freely, which is what reads as coverage rather than as scattered patches.
	///  - wall: NOT WallFree. That was the bug: it rejected a whole 5 m face because a torch owns it 1.7 m up,
	///    which capped this layer at a quarter of the wall mats asked for. A mat at the foot of a wall and a
	///    torch above it never meet. Rejected instead: faces carrying something that DOES span a band's height
	///    (BlockedFaces), the greenery's own Band layer, and whatever BandHeight says stands on the floor there.
	///  - ceiling: iterated per WALL FACE rather than per cell, which is what puts the mats along the wall lines
	///    instead of out in the middle of the room, and gives four candidate spots in a corridor cell.
	///
	/// Trap cells are skipped outright on every layer: a spike plate at rest lies flush with the floor and a
	/// blade hangs still, so their geometry says nothing about where they sweep.
	/// </summary>
	private static void Roots( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, double density,
		HashSet<(int x, int z, int l)> slabs, Dictionary<(int x, int z, int l), List<Footprint>> standing,
		HashSet<(int x, int z, int l, Dir d)> blocked )
	{
		int L = ctx.T.Level;
		var floorDef = res.Kit.Props[RootFloorTable[0].Id];
		var wallDef = res.Kit.Props[RootWallTable[0].Id];
		var ceilDef = res.Kit.Props[RootCeilingTable[0].Id];

		foreach ( var (x, z) in ctx.T.Sorted() )
		{
			bool trap = res.TrapCells.Contains( (x, z, L) );
			for ( int q = 0; q < 4; q++ )
				// twice per quarter: a quarter is 2.5 m across and a mat at most 1.8 m, and mats may tangle
				for ( int i = 0; i < 2; i++ )
				{
					if ( rng.NextDouble() >= RootFloorDensity * density ) continue;
					if ( trap ) continue;
					float scale = RootScale( rng );
					// the SCALED width, not the def's: a mat rolled at the top of the range is 1.8 m across and
					// has to be jittered less, or its pivot crosses into the next cell and FloorTopFor — which
					// keys on the rounded-down position, not on X,Z — reads that cell's floor height instead of
					// this one's. Its geometry can still overhang into a wall at that size; a root mat running
					// under the masonry is what roots do, and the wall is thicker than the overhang.
					var (gx, gz) = Jitter( rng, x, z, q, floorDef.Width * scale );
					if ( Touches( standing, L, gx, gz, MatRadius( floorDef, scale ) ) ) continue;
					Decor.Add( res, ctx, floorDef, rng, AnchorKind.Floor, x, z, L, Dir.N,
						gx, gz, 0, -RootSink( rng ), rng.RandRange( 360 ), 3, scale: scale );
				}
		}

		foreach ( var s in ctx.WallSlots )
		{
			if ( blocked.Contains( (s.X, s.Z, L, s.D) ) || res.TrapCells.Contains( (s.X, s.Z, L) ) ) continue;
			float yaw = Decor.YawOf( s.D.Opp() );
			// Low band: roots creeping up out of the floor. Its own scale range, capped well under the wall
			// pass's: at the full RootScaleMax a mat would rise to 1.99 m and grow into the torch cups, which
			// start at 1.70. Rejected where furniture stands against this wall (BandHeight) or where the
			// greenery's own wall pass has hung a moss sheet or a mushroom bracket (the Band layer).
			// Rolled once for each half of the 5 m segment, so a long wall gets a run of roots along its foot
			// rather than one mat in the middle of every face. The two halves cannot collide with each other.
			bool clearFoot = occ.BandHeight( s.X, s.Z, L, s.D ) <= 0f && occ.FaceFree( s.X, s.Z, L, s.D, WallLayer.Band );
			for ( int half = 0; half < 2; half++ )
			{
				if ( rng.NextDouble() >= RootWallLowDensity * density || !clearFoot ) continue;
				float scale = (float)(RootScaleMin + rng.NextDouble() * (WallLowScaleMax - RootScaleMin));
				Decor.Add( res, ctx, wallDef, rng, AnchorKind.WallMounted, s.X, s.Z, L, s.D,
					s.X + 0.5f, s.Z + 0.5f, AlongOffset( rng, s, scale, half ),
					wallDef.MountHeight + (float)rng.NextDouble() * 0.25f, yaw, 3,
					scale: scale, embed: RootSink( rng ) );
			}
			// High band: a few reaching down out of the ceiling corner. Needs a real slab overhead — about one
			// cell in seven has a room directly above and only 2.5 m of headroom, and a mat at 4 m there would
			// hang above the visible ceiling. The mount comes off the ROLLED scale, because the mat rises
			// WallMatRise per unit of it and a fixed height would push a big one through the slab.
			if ( rng.NextDouble() < RootWallHighDensity * density && slabs.Contains( (s.X, s.Z, L + 2) ) )
			{
				float scale = RootScale( rng );
				float top = RoomHeight - WallMatRise * scale - (float)rng.NextDouble() * 0.35f;
				Decor.Add( res, ctx, wallDef, rng, AnchorKind.WallMounted, s.X, s.Z, L, s.D,
					s.X + 0.5f, s.Z + 0.5f, AlongOffset( rng, s, scale ), top, yaw, 3,
					scale: scale, embed: RootSink( rng ) );
			}
		}

		foreach ( var s in ctx.WallSlots )
		{
			if ( rng.NextDouble() >= RootCeilingDensity * density ) continue;
			if ( !slabs.Contains( (s.X, s.Z, L + 2) ) ) continue;
			if ( res.TrapCells.Contains( (s.X, s.Z, L) ) ) continue;
			// CeilFree is read here, unlike the wall bands: the broken chandelier is 2.4 m across and the
			// hanging vines 1.7 m, and both reach far enough out of the middle to meet a mat at the edge.
			if ( !occ.CeilFree( s.X, s.Z, L ) ) continue;
			float scale = RootScale( rng );
			// pushed out to this cell's walled edge, jittered ALONG it (never across, or the pivot would leave
			// the cell and the slab test above would be checking the wrong one). The yaw faces the mat away
			// from that wall so its off-pivot reach runs into the cell rather than through the masonry.
			float side = (float)(rng.NextDouble() - 0.5) * 0.5f;
			float gx = s.X + 0.5f + s.D.Dx() * CeilingEdgeOffset - s.D.Dz() * side;
			float gz = s.Z + 0.5f + s.D.Dz() * CeilingEdgeOffset + s.D.Dx() * side;
			// the ceiling drop IS multiplied by the scale in the builder (p.Height * M * sc), so the sink has to
			// be divided by it here to come out the same 2-3 units on a big mat and a small one. This would need
			// revisiting if roots_ceiling ever gained a MaxHeight, which caps sc after the roll.
			float drop = ceilDef.MountHeight - RootSink( rng ) / scale;
			Decor.Add( res, ctx, ceilDef, rng, AnchorKind.CeilingHung, s.X, s.Z, L, Dir.N,
				gx, gz, 0, drop, Decor.YawOf( s.D.Opp() ) + (float)(rng.NextDouble() - 0.5) * 40f, 3, scale: scale );
		}
	}

	/// <summary>Units per metre, as the builder measures them. Only the sinks need it here.</summary>
	private const float M = 39.37f;
	/// <summary>Half the wall's thickness in cell units (10.6 u), so a mat can be kept out of the masonry.</summary>
	private const float WallHalfCells = 0.054f;

	/// <summary>
	/// How far along its 5 m segment a wall mat sits, in cell units either side of centre. Clamped by the mat's
	/// own half width so a big one cannot run past the end of the segment into the return wall or a corner post;
	/// a face with a pillar at either end, or a door jamb, gets the wider clearance the rest of the decorator
	/// uses. `half` 0 and 1 take the two sides of the segment, so two mats on one face never meet.
	/// </summary>
	private static float AlongOffset( DungeonRng rng, Slot s, float scale, int half = -1 )
	{
		float limit = Math.Max( 0.02f, 0.5f - (0.425f * scale) / 5f - (s.HasPillar || s.NearDoor ? 0.076f : WallHalfCells) );
		limit = Math.Min( limit, 0.34f );
		if ( half < 0 ) return (float)(rng.NextDouble() - 0.5) * 2f * limit;
		float lo = half == 0 ? -limit : 0.04f;
		return lo + (float)rng.NextDouble() * (limit - 0.04f);
	}

	/// <summary>A mat's reach from its pivot, in cell units, on the catalog's own terms.</summary>
	private static float MatRadius( PropDef def, float scale ) => Math.Max( def.Width, def.Depth ) / 2f * scale / 5f;

	/// <summary>Something already standing on the floor: its centre in cell units and how far it reaches.</summary>
	private readonly record struct Footprint( float GX, float GZ, float R );

	/// <summary>
	/// Everything already standing on the floor of the whole dungeon, indexed by the cell its pivot is in. Both
	/// Floor and WallFlush anchors carry a resolved GX/GZ (Decor.PlaceAgainstWall computes one for the flush
	/// furniture too), so the two together are every prop a mat could grow into.
	///
	/// Overlay props are left out on purpose — rugs and the pentagram lie as flat as the mats do, and
	/// roots_green is itself Overlay, which is how mats come to tangle with each other for free.
	/// </summary>
	private static Dictionary<(int x, int z, int l), List<Footprint>> FloorFootprints( DecorResult res )
	{
		var index = new Dictionary<(int x, int z, int l), List<Footprint>>();
		foreach ( var p in res.Props )
		{
			if ( p.Anchor is not (AnchorKind.Floor or AnchorKind.WallFlush) ) continue;
			if ( !res.Kit.Props.TryGetValue( p.Prop, out var def ) || def.Overlay ) continue;
			var key = ((int)Math.Floor( p.GX ), (int)Math.Floor( p.GZ ), p.Level);
			if ( !index.TryGetValue( key, out var list ) ) index[key] = list = new List<Footprint>();
			list.Add( new Footprint( p.GX, p.GZ, MatRadius( def, p.Scale > 0f ? p.Scale : 1f ) ) );
		}
		return index;
	}

	/// <summary>Would a mat of this reach at this spot meet something already standing? Checks the spot's own
	/// cell and its eight neighbours, since a big prop's footprint spills past its own pivot's cell.</summary>
	private static bool Touches( Dictionary<(int x, int z, int l), List<Footprint>> standing, int l, float gx, float gz, float r )
	{
		int cx = (int)Math.Floor( gx ), cz = (int)Math.Floor( gz );
		for ( int dx = -1; dx <= 1; dx++ )
			for ( int dz = -1; dz <= 1; dz++ )
			{
				if ( !standing.TryGetValue( (cx + dx, cz + dz, l), out var list ) ) continue;
				foreach ( var f in list )
				{
					float ex = f.GX - gx, ez = f.GZ - gz, reach = f.R + r;
					if ( ex * ex + ez * ez < reach * reach ) return true;
				}
			}
		return false;
	}

	/// <summary>
	/// Wall faces no root mat may go on, at either band. A trap's own face, which gentest asserts nothing
	/// mounted shares; a wall swap's, because a window has no wall mesh at all and an alcove has a recess behind
	/// it; and any mounted prop that is not a torch — the banner sweeps 0.4-4.6 m and the shackled skeletons
	/// 0.05-2.75 m, and neither of those calls NoteBand, so BandHeight would never see them.
	/// </summary>
	private static HashSet<(int x, int z, int l, Dir d)> BlockedFaces( DecorResult res )
	{
		var blocked = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var t in res.Traps ) blocked.Add( (t.WallX, t.WallZ, t.Level, t.WallD) );
		foreach ( var w in res.WallSwaps )
		{
			blocked.Add( (w.Seg.CX, w.Seg.CZ, w.Seg.K, w.Seg.D) );
			var (fx, fz) = Walls.CellOf( w.Seg.Line, w.Seg.D.Opp() );
			blocked.Add( (fx, fz, w.Seg.K, w.Seg.D.Opp()) );
		}
		foreach ( var p in res.Props )
			if ( p.Anchor == AnchorKind.WallMounted && p.Prop != "torch" && !RootIds.Contains( p.Prop ) )
				blocked.Add( (p.X, p.Z, p.Level, p.Edge) );
		return blocked;
	}

	private static float RootScale( DungeonRng rng )
		=> (float)(RootScaleMin + rng.NextDouble() * (RootScaleMax - RootScaleMin));

	/// <summary>How deep this mat beds into its surface, in METRES (the callers all want metres).</summary>
	private static float RootSink( DungeonRng rng )
		=> (float)(RootSinkMin + rng.NextDouble() * (RootSinkMax - RootSinkMin)) / M;

	/// <summary>A spot inside quarter q of a cell, kept far enough in that a prop of `width` stays in the cell.</summary>
	private static (float gx, float gz) Jitter( DungeonRng rng, int x, int z, int q, float width )
	{
		float cx = (q & 1) == 1 ? 0.75f : 0.25f;
		float cz = (q & 2) == 2 ? 0.75f : 0.25f;
		float slack = Math.Max( 0f, 0.25f - width / 10f );
		return (x + cx + (float)(rng.NextDouble() - 0.5) * 2f * slack,
			z + cz + (float)(rng.NextDouble() - 0.5) * 2f * slack);
	}
}
