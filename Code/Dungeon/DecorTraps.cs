using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>All the trap kinds of the design (TrapCatalog.Enabled gates which are built).</summary>
public enum TrapKind { SpikeFloor, SwingBlade, FlameHead, ArrowWall, SawRail, AcidHead, WallSlab }

/// <summary>Pressure: fires when stepped on, after a wind-up, then resets. Cycling: always running. Proximity: fires when approached, then resets.</summary>
public enum TrapTrigger { Pressure, Cycling, Proximity }

/// <summary>Where a kind stands: the middle of a straight 1-wide hallway, or a room's door approach.</summary>
public enum TrapSite { Hallway, RoomDoor }

public sealed record TrapDef( TrapKind Kind, TrapTrigger Trigger, TrapSite Site, string[] Models, bool Enabled );

/// <summary>The trap table: kind, trigger, site and the models the builder spawns. Engine-free, checked by gentest.</summary>
public static class TrapCatalog
{
	private const string P = "dungeonpack/";

	public static readonly Dictionary<TrapKind, TrapDef> Defs = new()
	{
		[TrapKind.SpikeFloor] = new( TrapKind.SpikeFloor, TrapTrigger.Pressure, TrapSite.Hallway, new[] { P + "sm_prop_trap_spears_grate_01.vmdl", P + "sm_prop_trap_spears_01.vmdl" }, true ),
		[TrapKind.SwingBlade] = new( TrapKind.SwingBlade, TrapTrigger.Cycling, TrapSite.Hallway, new[] { P + "sm_prop_trap_swing_blade_01.vmdl", P + "sm_prop_trap_swing_blade_housing_01.vmdl" }, true ),
		[TrapKind.FlameHead] = new( TrapKind.FlameHead, TrapTrigger.Proximity, TrapSite.RoomDoor, new[] { P + "sm_prop_trap_flamehead_01.vmdl" }, true ),
		[TrapKind.ArrowWall] = new( TrapKind.ArrowWall, TrapTrigger.Pressure, TrapSite.Hallway, new[] { P + "sm_prop_trap_wallarrow_hole_01.vmdl", P + "sm_prop_trap_wallarrow_hole_02.vmdl", P + "sm_item_arrow_01.vmdl" }, true ),
		// disabled (user, 2026-09-11: "remove the saw trap completely from all dungeons"); the kind, its builder and
		// its component stay compiled so an old layout still reads, but nothing places one
		[TrapKind.SawRail] = new( TrapKind.SawRail, TrapTrigger.Cycling, TrapSite.Hallway, new[] { P + "sm_prop_trap_saw_01.vmdl", P + "sm_prop_trap_saw_rail_01.vmdl" }, false ),
		// disabled (user, 2026-09-20: "remove the acid trap replace all with firetraps the acid traps are annoying"): its
		// turn in the rotation is a second flame head. Kept compiled like the saw so an old layout still reads.
		[TrapKind.AcidHead] = new( TrapKind.AcidHead, TrapTrigger.Proximity, TrapSite.RoomDoor, new[] { P + "sm_prop_trap_acidhead_01.vmdl" }, false ),
		[TrapKind.WallSlab] = new( TrapKind.WallSlab, TrapTrigger.Pressure, TrapSite.Hallway, new[] { P + "sm_prop_trap_wallslab_01.vmdl" }, true ),
	};

	/// <summary>Every model an enabled kind can spawn (the asset check).</summary>
	public static IEnumerable<string> AllModels() => Defs.Values.Where( d => d.Enabled ).SelectMany( d => d.Models );
}

/// <summary>
/// Per-floor knobs: how many traps, which kinds, and the least walking distance between two of them.
/// Kinds is the readable-first rotation: the spine hallway nearest the spawn gets Kinds[0], the next
/// eligible spot the next kind, and so on. Per-floor tables come later; ForDepth is the single default.
/// </summary>
public sealed record TrapBudget( int Min, int Max, TrapKind[] Kinds, int MinGapCells = 6, int MinSpawnDist = 3 )
{
	public static TrapBudget ForDepth( int depth ) => new( 6, 8, new[]
	{
		TrapKind.SpikeFloor, TrapKind.SwingBlade, TrapKind.FlameHead, TrapKind.ArrowWall,
		TrapKind.FlameHead, TrapKind.WallSlab,
	} );
}

/// <summary>
/// One trap. X,Z,Level is the anchor cell: the plate, the blade's cell, the saw rail's first cell, the slab's
/// cell, or (heads) the door's portal cell inside the room. Facing: hallway kinds carry the run axis (N or E);
/// heads carry the portal's way OUT of the room. WallX/WallZ/WallD is the wall face the kind hangs on (heads,
/// arrow holes, the slab). Extent is how many cells along Facing the trap spans from the anchor (the saw rail).
/// WalkDist is the walking-graph distance from the spawn cell (ordering and spacing). SpineIndex is -1 off the
/// spine. Variant is reserved (0 so far).
/// </summary>
public readonly record struct TrapPlacement(
	TrapKind Kind, int Index, int TierId, int SpineIndex, int WalkDist,
	int X, int Z, int Level, Dir Facing, int WallX, int WallZ, Dir WallD, int Variant, int Extent = 1 )
{
	public string Name => $"trap {Kind} {X},{Z},{Level}";
}

/// <summary>
/// The trap pass: runs before any room is dressed, so a trap's cells are claimed before furniture, litter or
/// breakables can cover the tell. Engine-free and deterministic like the rest of Decor (its own RNG stream).
/// Rules: hallway traps only on the middle of a straight 1-wide corridor (never the first or last cell, never
/// a doorway cell); room traps only on the inward approach of the door the player arrives through; nothing
/// in spawn, exit, shrine, loot or secret tiers; spine first, nearest the spawn first, spurs after; at most
/// one trap per tier; a minimum walking distance between traps; the first trap is always a hallway one.
/// </summary>
public static class DecorTraps
{
	/// <summary>Cells the wall slab's shafts reach into behind its wall (6 m of model behind the plate).</summary>
	public const int SlabRockCells = 2;

	private sealed class Candidate
	{
		public TrapSite Site;
		public TierCtx Ctx;
		public int X, Z;
		public Dir Facing;
		public int WallX, WallZ;
		public Dir WallD;
		public int WalkDist;
		public int SpineIndex;
		/// <summary>Hallway: the side faces of the anchor cell that are real, bare, non-secret walls.</summary>
		public List<Dir> SideWalls = new();
		/// <summary>Hallway: a side wall with nothing but rock behind it for SlabRockCells cells, if any.</summary>
		public Dir? RockSide;
		/// <summary>Hallway: the contiguous bare interior cells around the anchor along the run (the anchor included), ordered along Facing.</summary>
		public List<(int x, int z)> Rail = new();
	}

	/// <summary>The cells a fired trap hurts: the plate; the blade's cell and both neighbours along its swing; the saw's rail cells; the slab's cell; a head's doorway cell.</summary>
	public static List<(int x, int z)> DamageCells( in TrapPlacement t )
	{
		var cells = new List<(int x, int z)> { (t.X, t.Z) };
		switch ( t.Kind )
		{
			case TrapKind.SwingBlade:
				cells.Add( (t.X + t.Facing.Dx(), t.Z + t.Facing.Dz()) );
				cells.Add( (t.X - t.Facing.Dx(), t.Z - t.Facing.Dz()) );
				break;
			case TrapKind.SawRail:
				for ( int i = 1; i < t.Extent; i++ ) cells.Add( (t.X + t.Facing.Dx() * i, t.Z + t.Facing.Dz() * i) );
				break;
		}
		return cells;
	}

	/// <summary>The cells that set a trap off (none for a cycling trap): the cell in front of a plate, a slab or an arrow wall; a head's doorway cell.</summary>
	public static List<(int x, int z)> TriggerCells( in TrapPlacement t )
	{
		switch ( t.Kind )
		{
			case TrapKind.SpikeFloor:
			case TrapKind.FlameHead:
			case TrapKind.ArrowWall:
			case TrapKind.AcidHead:
			case TrapKind.WallSlab:
				return new List<(int x, int z)> { (t.X, t.Z) };
			default:
				return new List<(int x, int z)>();
		}
	}

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ, TrapBudget budget, DungeonRng rng )
	{
		if ( budget == null || budget.Max <= 0 ) return;
		var g = lay.Grid;
		var kinds = budget.Kinds.Where( k => TrapCatalog.Defs.TryGetValue( k, out var d ) && d.Enabled ).ToList();
		if ( kinds.Count == 0 ) return;

		var wallSet = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var e in lay.Edges ) if ( e.Kind == EdgeKind.Wall ) wallSet.Add( (e.X, e.Z, e.Level, e.D) );
		var ceilings = new HashSet<(int x, int z, int l)>( lay.Ceilings );

		// walking distance from where the player starts: the ordering and the spacing rule
		var spawn = lay.Find( TierTag.Spawn );
		var dist = new Dictionary<(int x, int z, int l), int>();
		if ( spawn != null )
		{
			var s0 = DecorSlots.Nearest( spawn.Sorted(), spawn.Centroid() );
			dist = g.DistancesFrom( (s0.x, s0.z, spawn.Level) );
		}
		var spineIndex = new Dictionary<int, int>();
		for ( int i = 0; i < lay.Layout.Spine.Count; i++ ) spineIndex[lay.Layout.Spine[i].Id] = i;

		var candidates = new List<Candidate>();
		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			var t = ctx.T;
			if ( t.Cells.Count == 0 || t.Kind == TierKind.Tower ) continue;
			if ( t.Tag is TierTag.Spawn or TierTag.Exit or TierTag.Shrine or TierTag.Loot or TierTag.Secret ) continue;
			var c = t.Kind == TierKind.Corridor ? HallwayCandidate( g, lay, ctx, occ, wallSet, dist ) : RoomCandidate( g, lay, ctx, occ, wallSet, dist );
			if ( c == null ) continue;
			c.SpineIndex = spineIndex.TryGetValue( t.Id, out var si ) ? si : -1;
			candidates.Add( c );
		}
		candidates = candidates.OrderBy( c => c.SpineIndex >= 0 ? 0 : 1 ).ThenBy( c => c.WalkDist ).ThenBy( c => c.Ctx.T.Id ).ThenBy( c => (c.X, c.Z) ).ToList();

		int want = budget.Min + rng.RandRange( Math.Max( 1, budget.Max - budget.Min + 1 ) );
		var placed = new List<TrapPlacement>();
		var usedTiers = new HashSet<int>();
		int cursor = 0;
		// two passes: full spacing first, half spacing if the floor could not seat the budget
		foreach ( var gap in new[] { budget.MinGapCells, Math.Max( 1, budget.MinGapCells / 2 ) } )
		{
			foreach ( var cand in candidates )
			{
				if ( placed.Count >= want ) break;
				if ( usedTiers.Contains( cand.Ctx.T.Id ) ) continue;
				if ( cand.WalkDist < budget.MinSpawnDist ) continue;
				// the first trap a player meets is a hallway one (the plate on the walking line), never a room head:
				// a room trap needs a hallway trap already placed AND nearer the spawn than itself
				if ( cand.Site == TrapSite.RoomDoor && !placed.Any( p => TrapCatalog.Defs[p.Kind].Site == TrapSite.Hallway && p.WalkDist < cand.WalkDist ) ) continue;
				if ( placed.Any( p => Math.Abs( p.WalkDist - cand.WalkDist ) < gap ) ) continue;
				for ( int i = 0; i < kinds.Count; i++ )
				{
					var kind = kinds[(cursor + i) % kinds.Count];
					if ( TrapCatalog.Defs[kind].Site != cand.Site ) continue;
					var placement = TryPlace( g, ceilings, wallSet, occ, res, rng, kind, cand, placed.Count );
					if ( placement == null ) continue;
					placed.Add( placement.Value );
					usedTiers.Add( cand.Ctx.T.Id );
					cursor = (cursor + i + 1) % kinds.Count;
					break;
				}
			}
			if ( placed.Count >= want ) break;
		}

		foreach ( var p in placed )
		{
			res.Traps.Add( p );
			foreach ( var (x, z) in DamageCells( p ) ) res.TrapCells.Add( (x, z, p.Level) );
		}
	}

	/// <summary>The middle of a straight 1-wide corridor: never its first or last cell, never a doorway cell; plus what its side walls offer.</summary>
	private static Candidate HallwayCandidate( Grid g, DungeonLayout lay, TierCtx ctx, Occupancy occ, HashSet<(int x, int z, int l, Dir d)> wallSet, Dictionary<(int x, int z, int l), int> dist )
	{
		var t = ctx.T;
		int L = t.Level;
		var cells = t.Sorted();
		if ( cells.Count < 3 ) return null;
		bool alongZ = cells.All( c => c.x == cells[0].x );
		bool alongX = cells.All( c => c.z == cells[0].z );
		if ( !alongZ && !alongX ) return null;
		var axis = alongZ ? Dir.N : Dir.E;
		var run = alongZ ? cells.OrderBy( c => c.z ).ToList() : cells.OrderBy( c => c.x ).ToList();
		var portals = new HashSet<(int x, int z)>( ctx.Portals.Select( p => p.cell ) );
		bool Bare( (int x, int z) c ) => !portals.Contains( c ) && occ.TileFree( c.x, c.z, L );
		double mid = (run.Count - 1) / 2.0;
		var interior = Enumerable.Range( 1, run.Count - 2 ).Select( i => (i, cell: run[i]) )
			.Where( e => Bare( e.cell ) )
			.OrderBy( e => Math.Abs( e.i - mid ) ).ThenBy( e => e.cell ).ToList();
		if ( interior.Count == 0 ) return null;
		var (pickIndex, pick) = interior[0];

		var cand = new Candidate
		{
			Site = TrapSite.Hallway, Ctx = ctx, X = pick.x, Z = pick.z, Facing = axis,
			WallX = pick.x, WallZ = pick.z, WallD = axis.Turn()[0],
			WalkDist = Grid.DistTo( dist, (pick.x, pick.z, L), 9999 ),
		};
		// the side walls: real, bare, not the secret tell; and whether solid rock lies behind one of them
		foreach ( var d in axis.Turn() )
		{
			if ( !wallSet.Contains( (pick.x, pick.z, L, d) ) || DecorSlots.IsSecretWall( lay, pick.x, pick.z, L, d ) || !occ.WallFree( pick.x, pick.z, L, d ) ) continue;
			cand.SideWalls.Add( d );
			bool rock = true;
			for ( int k = 1; k <= SlabRockCells && rock; k++ )
			{
				int bx = pick.x + d.Dx() * k, bz = pick.z + d.Dz() * k;
				if ( g.Claims.Contains( (bx, bz, L) ) || g.Claims.Contains( (bx, bz, L + 1) ) ) rock = false;
			}
			if ( rock && cand.RockSide == null ) cand.RockSide = d;
		}
		// the rail: the bare interior cells contiguous with the pick, along the run
		int lo = pickIndex, hi = pickIndex;
		while ( lo - 1 >= 1 && Bare( run[lo - 1] ) ) lo--;
		while ( hi + 1 <= run.Count - 2 && Bare( run[hi + 1] ) ) hi++;
		for ( int i = lo; i <= hi; i++ ) cand.Rail.Add( run[i] );
		return cand;
	}

	/// <summary>
	/// The door the player arrives through (nearest the spawn by walking distance), with its inward approach
	/// cell and a bare wall face beside the frame for the head. Only doorways between two floors at the same
	/// level count; stairs and the hall's own interior openings do not.
	/// </summary>
	private static Candidate RoomCandidate( Grid g, DungeonLayout lay, TierCtx ctx, Occupancy occ, HashSet<(int x, int z, int l, Dir d)> wallSet, Dictionary<(int x, int z, int l), int> dist )
	{
		var t = ctx.T;
		int L = t.Level;
		var portals = new HashSet<(int x, int z)>( ctx.Portals.Select( p => p.cell ) );
		Candidate best = null;
		foreach ( var (P, o) in ctx.Portals )
		{
			var beyond = (P.x + o.Dx(), P.z + o.Dz(), L);
			if ( !g.Cells.TryGetValue( beyond, out var bc ) || bc.Kind != CellKind.Floor || bc.Tier == null || bc.Tier == t ) continue;
			var I = (x: P.x - o.Dx(), z: P.z - o.Dz());
			if ( !t.Cells.Contains( I ) || portals.Contains( I ) ) continue;
			if ( g.HasIWall( (P.x, P.z, L), (I.x, I.z, L) ) ) continue;
			if ( !occ.TileFree( P.x, P.z, L ) || !occ.TileFree( I.x, I.z, L ) ) continue;
			Dir? face = null;
			foreach ( var d in o.Turn() )
			{
				if ( !wallSet.Contains( (P.x, P.z, L, d) ) || DecorSlots.IsSecretWall( lay, P.x, P.z, L, d ) || !occ.WallFree( P.x, P.z, L, d ) ) continue;
				face = d;
				break;
			}
			if ( face == null ) continue;
			int wd = Grid.DistTo( dist, beyond, 9999 );
			if ( best != null && wd >= best.WalkDist ) continue;
			best = new Candidate
			{
				Site = TrapSite.RoomDoor, Ctx = ctx, X = P.x, Z = P.z, Facing = o,
				WallX = P.x, WallZ = P.z, WallD = face.Value, WalkDist = wd,
			};
		}
		return best;
	}

	/// <summary>Kind-specific fit checks, then the claims: the tile(s), the ceiling, the side walls, a head's face, the plate's flat tile.</summary>
	private static TrapPlacement? TryPlace( Grid g, HashSet<(int x, int z, int l)> ceilings, HashSet<(int x, int z, int l, Dir d)> wallSet, Occupancy occ, DecorResult res, DungeonRng rng, TrapKind kind, Candidate c, int index )
	{
		var t = c.Ctx.T;
		int L = t.Level;
		var p = new TrapPlacement( kind, index, t.Id, c.SpineIndex, c.WalkDist, c.X, c.Z, L, c.Facing, c.WallX, c.WallZ, c.WallD, 0 );
		switch ( kind )
		{
			case TrapKind.SpikeFloor:
				// the spears sink two metres: a room right below would get them through its ceiling
				if ( g.Claims.Contains( (c.X, c.Z, L - 1) ) ) return null;
				occ.ClaimTile( c.X, c.Z, L );
				res.FloorSwaps.Add( new FloorSwap( c.X, c.Z, L, res.Kit.PlainFloorModel ) );
				return p;

			case TrapKind.SwingBlade:
			{
				if ( !ceilings.Contains( (c.X, c.Z, L + 2) ) || !occ.CeilFree( c.X, c.Z, L ) ) return null;
				// the swing reaches into both neighbours along the run: they must be this corridor's and still bare
				if ( !DamageCells( p ).All( cell => t.Cells.Contains( cell ) && occ.TileFree( cell.x, cell.z, L ) ) ) return null;
				foreach ( var (x, z) in DamageCells( p ) )
				{
					occ.ClaimTile( x, z, L );
					// nothing mounted beside the sweep: a torch inside the arc would be sliced every pass
					foreach ( var d in c.Facing.Turn() )
						if ( wallSet.Contains( (x, z, L, d) ) && occ.WallFree( x, z, L, d ) ) occ.ClaimWall( x, z, L, d );
				}
				occ.ClaimCeil( c.X, c.Z, L );
				return p;
			}

			case TrapKind.FlameHead:
			case TrapKind.AcidHead:
			{
				var I = (x: c.X - c.Facing.Dx(), z: c.Z - c.Facing.Dz());
				occ.ClaimWall( c.WallX, c.WallZ, L, c.WallD );
				occ.ClaimTile( c.X, c.Z, L );
				occ.ClaimTile( I.x, I.z, L );
				return p;
			}

			case TrapKind.ArrowWall:
			{
				// holes on one side wall; the stuck arrows (the second tell) on the other if it is a wall too
				if ( c.SideWalls.Count == 0 ) return null;
				var holes = c.SideWalls[0];
				p = p with { WallX = c.X, WallZ = c.Z, WallD = holes };
				occ.ClaimTile( c.X, c.Z, L );
				foreach ( var d in c.SideWalls ) occ.ClaimWall( c.X, c.Z, L, d );
				return p;
			}

			case TrapKind.SawRail:
			{
				// the rail wants at least two bare interior cells so the saw has somewhere to patrol
				if ( c.Rail.Count < 2 ) return null;
				var first = c.Rail[0];
				p = p with { X = first.x, Z = first.z, Extent = c.Rail.Count, WallX = first.x, WallZ = first.z };
				foreach ( var (x, z) in c.Rail ) occ.ClaimTile( x, z, L );
				return p;
			}

			case TrapKind.WallSlab:
			{
				// the slab's shafts hide in rock behind its wall, and it squashes against a solid wall opposite
				if ( c.RockSide == null || c.SideWalls.Count < 2 ) return null;
				var side = c.RockSide.Value;
				p = p with { WallX = c.X, WallZ = c.Z, WallD = side };
				occ.ClaimTile( c.X, c.Z, L );
				foreach ( var d in c.SideWalls ) occ.ClaimWall( c.X, c.Z, L, d );
				return p;
			}

			default:
				return null;
		}
	}
}
