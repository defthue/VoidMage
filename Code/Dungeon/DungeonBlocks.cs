using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>One cell of a stair/landing plan, applied to the grid via CommitPlan.</summary>
public readonly record struct PlanStep( int X, int Z, int Level, CellKind Kind, Dir SDir );

/// <summary>Where a stair run ends: first cell of the new tier and the direction it continues in.</summary>
public readonly record struct Arrival( int X, int Z, int Level, Dir D );

/// <summary>Building blocks shared by every archetype. Ported 1:1 from dungeon_gen.py.</summary>
public static class Blocks
{
	public static HashSet<(int x, int z)> Rect( int x0, int z0, int w, int h )
	{
		var s = new HashSet<(int, int)>();
		for ( int i = 0; i < w; i++ )
			for ( int j = 0; j < h; j++ )
				s.Add( (x0 + i, z0 + j) );
		return s;
	}

	public static Tier TryPlaceTier( Grid g, IEnumerable<(int x, int z)> cells, int level, TierKind kind, TierTag tag = TierTag.None )
	{
		var list = cells.ToList();
		if ( list.Count == 0 ) return null;
		foreach ( var (x, z) in list )
			if ( !g.Free( x, z, level, CellKind.Floor ) ) return null;
		var t = new Tier( g.NextTierId(), list, level, kind ) { Tag = tag };
		foreach ( var (x, z) in list ) g.Put( x, z, level, CellKind.Floor, t );
		g.Tiers.Add( t );
		return t;
	}

	/// <summary>
	/// Stair run starting in the cell adjacent to (x,z) in direction d.
	/// delta 1: one stair. delta 2: stair, landing, stair (the landing may turn).
	/// </summary>
	public static (List<PlanStep> plan, Arrival arrival)? TryStairRun( Grid g, int x, int z, int level, Dir d, int delta, DungeonRng rng, bool allowTurn = true )
	{
		int dx = d.Dx(), dz = d.Dz();
		int sx = x + dx, sz = z + dz;
		if ( delta == 1 )
		{
			if ( g.Free( sx, sz, level, CellKind.Stair ) )
				return (new List<PlanStep> { new( sx, sz, level, CellKind.Stair, d ) }, new Arrival( sx + dx, sz + dz, level + 1, d ));
			return null;
		}
		if ( delta == 2 )
		{
			int lx = sx + dx, lz = sz + dz;
			var opts = new List<Dir> { d };
			if ( allowTurn ) opts.AddRange( d.Turn() );
			rng.Shuffle( opts );
			foreach ( var d2 in opts )
			{
				int d2x = d2.Dx(), d2z = d2.Dz();
				int s2x = lx + d2x, s2z = lz + d2z;
				var plan = new List<PlanStep>
				{
					new( sx, sz, level, CellKind.Stair, d ),
					new( lx, lz, level + 1, CellKind.Landing, Dir.N ),
					new( s2x, s2z, level + 1, CellKind.Stair, d2 ),
				};
				bool distinct = new HashSet<(int, int)> { (sx, sz), (lx, lz), (s2x, s2z) }.Count == 3;
				if ( g.Free( sx, sz, level, CellKind.Stair ) && g.Free( lx, lz, level + 1, CellKind.Floor )
					&& g.Free( s2x, s2z, level + 1, CellKind.Stair ) && distinct )
					return (plan, new Arrival( s2x + d2x, s2z + d2z, level + 2, d2 ));
			}
			return null;
		}
		return null;
	}

	/// <summary>Stair run going DOWN from (ex,ez,level) in direction d by `drop` levels. Stairs rise toward the source.</summary>
	public static (List<PlanStep> plan, Arrival arrival)? PlanDescend( Grid g, int ex, int ez, int level, Dir d, int drop, DungeonRng rng )
	{
		int dx = d.Dx(), dz = d.Dz();
		int sx = ex + dx, sz = ez + dz;
		if ( drop == 1 )
		{
			if ( g.Free( sx, sz, level - 1, CellKind.Stair ) )
				return (new List<PlanStep> { new( sx, sz, level - 1, CellKind.Stair, d.Opp() ) }, new Arrival( sx + dx, sz + dz, level - 1, d ));
			return null;
		}
		int lx = sx + dx, lz = sz + dz;
		var opts = new List<Dir> { d };
		opts.AddRange( d.Turn() );
		rng.Shuffle( opts );
		foreach ( var d2 in opts )
		{
			int d2x = d2.Dx(), d2z = d2.Dz();
			int s2x = lx + d2x, s2z = lz + d2z;
			bool distinct = new HashSet<(int, int)> { (sx, sz), (lx, lz), (s2x, s2z) }.Count == 3;
			if ( g.Free( sx, sz, level - 1, CellKind.Stair ) && g.Free( lx, lz, level - 1, CellKind.Floor )
				&& g.Free( s2x, s2z, level - 2, CellKind.Stair ) && distinct )
			{
				var plan = new List<PlanStep>
				{
					new( sx, sz, level - 1, CellKind.Stair, d.Opp() ),
					new( lx, lz, level - 1, CellKind.Landing, Dir.N ),
					new( s2x, s2z, level - 2, CellKind.Stair, d2.Opp() ),
				};
				return (plan, new Arrival( s2x + d2x, s2z + d2z, level - 2, d2 ));
			}
		}
		return null;
	}

	public static void CommitPlan( Grid g, List<PlanStep> plan )
	{
		foreach ( var p in plan ) g.Put( p.X, p.Z, p.Level, p.Kind, null, p.SDir );
	}

	public static HashSet<(int x, int z)> PlanCols( List<PlanStep> plan )
		=> new( plan.Select( p => (p.X, p.Z) ) );

	/// <summary>Rectangle of w (across) x depth (along d) whose near edge contains (x,z).</summary>
	public static HashSet<(int x, int z)> TierFootprintAhead( int x, int z, Dir d, int w, int depth, DungeonRng rng, bool sideShift = true )
	{
		int dx = d.Dx(), dz = d.Dz();
		int px = -dz, pz = dx;   // perpendicular
		int shift = sideShift ? rng.RandInt( -(w - 1), 0 ) : -(w / 2);
		var cells = new HashSet<(int, int)>();
		for ( int i = 0; i < w; i++ )
			for ( int j = 0; j < depth; j++ )
				cells.Add( (x + px * (i + shift) + dx * j, z + pz * (i + shift) + dz * j) );
		return cells;
	}

	/// <summary>3x3 spiral stair tower with a hollow centre. Entry column e has floor at `low` and at `low+4`.</summary>
	public static List<PlanStep> PlanTower( Grid g, (int x, int z) e, int low, Dir d1, Dir p )
	{
		int dx = d1.Dx(), dz = d1.Dz(), px = p.Dx(), pz = p.Dz();
		var (ex, ez) = e;
		var A = (ex + dx, ez + dz); var L1 = (ex + 2 * dx, ez + 2 * dz);
		var B = (L1.Item1 + px, L1.Item2 + pz); var L2 = (L1.Item1 + 2 * px, L1.Item2 + 2 * pz);
		var C = (L2.Item1 - dx, L2.Item2 - dz); var L3 = (L2.Item1 - 2 * dx, L2.Item2 - 2 * dz);
		var D = (L3.Item1 - px, L3.Item2 - pz);
		var plan = new List<PlanStep>
		{
			new( A.Item1, A.Item2, low, CellKind.Stair, d1 ), new( L1.Item1, L1.Item2, low + 1, CellKind.Landing, Dir.N ),
			new( B.Item1, B.Item2, low + 1, CellKind.Stair, p ), new( L2.Item1, L2.Item2, low + 2, CellKind.Landing, Dir.N ),
			new( C.Item1, C.Item2, low + 2, CellKind.Stair, d1.Opp() ), new( L3.Item1, L3.Item2, low + 3, CellKind.Landing, Dir.N ),
			new( D.Item1, D.Item2, low + 3, CellKind.Stair, p.Opp() ),
		};
		if ( D != (ex + px, ez + pz) || low < 0 || low + 4 > Grid.MaxLevel ) return null;
		foreach ( var s in plan )
			if ( !g.Free( s.X, s.Z, s.Level, s.Kind ) ) return null;
		return plan;
	}

	/// <summary>Cut a room/hall with wall lines that have one door gap each.</summary>
	public static void AddInteriorWalls( Grid g, Tier tier, DungeonRng rng, int nSplits )
	{
		var cells = tier.Cells;
		var xs = cells.Select( c => c.x ).Distinct().OrderBy( v => v ).ToList();
		var zs = cells.Select( c => c.z ).Distinct().OrderBy( v => v ).ToList();
		// all lines in one tier share an orientation: crossing lines with single doors can isolate a quadrant
		bool vertical = (xs.Count >= 3 && zs.Count >= 3) ? rng.NextDouble() < 0.5 : xs.Count >= 3;
		var used = new HashSet<int>();
		for ( int n = 0; n < nSplits; n++ )
		{
			if ( vertical && xs.Count >= 3 )
			{
				int xline = rng.Choice( xs.Skip( 1 ).ToList() );          // wall between xline-1 and xline
				if ( used.Contains( xline ) || used.Contains( xline - 1 ) || used.Contains( xline + 1 ) ) continue;
				used.Add( xline );
				var seg = zs.Where( z => cells.Contains( (xline - 1, z) ) && cells.Contains( (xline, z) ) ).Select( z => (xline - 1, z) ).ToList();
				if ( seg.Count < 2 ) continue;
				var door = rng.Choice( seg );
				foreach ( var c in seg )
					if ( c != door )
						g.IWalls.Add( Grid.Edge( (c.Item1, c.z, tier.Level), (c.Item1 + 1, c.z, tier.Level) ) );
			}
			else if ( !vertical && zs.Count >= 3 )
			{
				int zline = rng.Choice( zs.Skip( 1 ).ToList() );
				if ( used.Contains( zline ) || used.Contains( zline - 1 ) || used.Contains( zline + 1 ) ) continue;
				used.Add( zline );
				var seg = xs.Where( x => cells.Contains( (x, zline - 1) ) && cells.Contains( (x, zline) ) ).Select( x => (x, zline - 1) ).ToList();
				if ( seg.Count < 2 ) continue;
				var door = rng.Choice( seg );
				foreach ( var c in seg )
					if ( c != door )
						g.IWalls.Add( Grid.Edge( (c.x, c.Item2, tier.Level), (c.x, c.Item2 + 1, tier.Level) ) );
			}
		}
	}

	/// <summary>
	/// BFS over free cells at `level` from `start` until a cell for which isTarget is true is reached.
	/// Returns the free cells to fill (excluding the target) or null.
	/// </summary>
	public static List<(int x, int z)> BridgeCorridor( Grid g, (int x, int z) start, int level, Func<int, int, bool> isTarget, int maxLen, DungeonRng rng, HashSet<(int x, int z)> avoid = null )
	{
		avoid ??= new HashSet<(int, int)>();
		var q = new Queue<(int x, int z)>();
		q.Enqueue( start );
		var prev = new Dictionary<(int, int), (int, int)?> { [start] = null };
		var depth = new Dictionary<(int, int), int> { [start] = 0 };
		while ( q.Count > 0 )
		{
			var cur = q.Dequeue();
			if ( isTarget( cur.x, cur.z ) )
			{
				var path = new List<(int, int)>();
				var c = prev[cur];
				while ( c != null ) { path.Add( c.Value ); c = prev[c.Value]; }
				return path;
			}
			if ( depth[cur] >= maxLen ) continue;
			var dirs = Dirs.All.ToList();
			rng.Shuffle( dirs );
			foreach ( var d in dirs )
			{
				var n = (cur.x + d.Dx(), cur.z + d.Dz());
				if ( prev.ContainsKey( n ) || avoid.Contains( n ) ) continue;
				if ( isTarget( n.Item1, n.Item2 ) || g.Free( n.Item1, n.Item2, level, CellKind.Floor ) )
				{
					prev[n] = cur;
					depth[n] = depth[cur] + 1;
					q.Enqueue( n );
				}
			}
		}
		return null;
	}

	/// <summary>
	/// Where two different tiers touch at the same level, wall the shared edge and leave a single door gap.
	/// Halls touching halls stay open (Pit bands).
	/// </summary>
	public static void AddDoorsBetweenTiers( Grid g, DungeonRng rng, double keepOpenProb = 0.15 )
	{
		var order = new List<(int, int)>();
		var shared = new Dictionary<(int, int), List<((int x, int z, int l), (int x, int z, int l))>>();
		foreach ( var key in g.SortedKeys() )
		{
			var c = g.Cells[key];
			if ( c.Kind != CellKind.Floor || c.Tier == null ) continue;
			foreach ( var (dx, dz) in new[] { (1, 0), (0, 1) } )
			{
				var n = (key.x + dx, key.z + dz, key.l);
				if ( g.Cells.TryGetValue( n, out var nc ) && nc.Kind == CellKind.Floor && nc.Tier != null && nc.Tier != c.Tier )
				{
					var pair = (Math.Min( c.Tier.Id, nc.Tier.Id ), Math.Max( c.Tier.Id, nc.Tier.Id ));
					if ( !shared.TryGetValue( pair, out var list ) )
					{
						list = new(); shared[pair] = list; order.Add( pair );
					}
					list.Add( (key, n) );
				}
			}
		}
		var tiers = g.Tiers.ToDictionary( t => t.Id );
		foreach ( var pair in order )
		{
			var a = tiers[pair.Item1]; var b = tiers[pair.Item2];
			if ( a.Kind == TierKind.Hall && b.Kind == TierKind.Hall ) continue;
			if ( rng.NextDouble() < keepOpenProb ) continue;
			var edges = shared[pair];
			var door = rng.Choice( edges );
			foreach ( var e in edges )
				if ( e != door ) g.IWalls.Add( Grid.Edge( e.Item1, e.Item2 ) );
		}
	}

	/// <summary>Cells that a stair connects to (base and top) plus the stair itself. Never remove these.</summary>
	public static HashSet<(int x, int z)> ProtectedCells( Grid g )
	{
		var p = new HashSet<(int, int)>();
		foreach ( var (x, z, _, d) in g.Stairs )
		{
			p.Add( (x - d.Dx(), z - d.Dz()) ); p.Add( (x + d.Dx(), z + d.Dz()) ); p.Add( (x, z) );
		}
		return p;
	}

	private static bool AllNeighboursIn( HashSet<(int x, int z)> cells, (int x, int z) c )
		=> Dirs.All.All( d => cells.Contains( (c.x + d.Dx(), c.z + d.Dz()) ) );

	/// <summary>Remove interior cells from a hall (floor holes you can fall through / see down).</summary>
	public static void PunchHoles( Grid g, Tier tier, DungeonRng rng, int n )
	{
		var cells = tier.Cells;
		var prot = ProtectedCells( g );
		var interior = tier.Sorted().Where( c => !prot.Contains( c ) && AllNeighboursIn( cells, c ) ).ToList();
		rng.Shuffle( interior );
		foreach ( var c in interior.Take( n ) )
		{
			// keep connectivity trivially: only remove if all 4 neighbours remain
			if ( AllNeighboursIn( cells, c ) )
			{
				cells.Remove( c );
				g.RemoveFloor( c.x, c.z, tier.Level );
			}
		}
	}

	/// <summary>Inside a big hall, cut a corner block and re-place it one level up with a single stair from the hall.</summary>
	public static void RaiseStep( Grid g, Tier tier, DungeonRng rng )
	{
		var cells = tier.Cells;
		var xs = cells.Select( c => c.x ).Distinct().OrderBy( v => v ).ToList();
		var zs = cells.Select( c => c.z ).Distinct().OrderBy( v => v ).ToList();
		if ( xs.Count < 5 || zs.Count < 4 ) return;
		int w = rng.RandInt( 2, Math.Min( 3, xs.Count - 3 ) );
		int h = rng.RandInt( 2, Math.Min( 3, zs.Count - 2 ) );
		var corner = rng.Choice( new List<(int, int)>
		{
			(xs[0], zs[0]), (xs[^1] - w + 1, zs[0]), (xs[0], zs[^1] - h + 1), (xs[^1] - w + 1, zs[^1] - h + 1),
		} );
		var block = Rect( corner.Item1, corner.Item2, w, h );
		if ( !block.IsSubsetOf( cells ) ) return;
		var prot = ProtectedCells( g );
		if ( block.Overlaps( prot ) ) return;
		// find a stair cell: a hall cell adjacent to the block, with the block cell ahead
		var cands = new List<((int x, int z) s, Dir d)>();
		foreach ( var (bx, bz) in block.OrderBy( c => c ) )
			foreach ( var d in Dirs.All )
			{
				var s = (bx - d.Dx(), bz - d.Dz());
				if ( cells.Contains( s ) && !block.Contains( s ) && !prot.Contains( s ) ) cands.Add( (s, d) );
			}
		rng.Shuffle( cands );
		foreach ( var ((sx, sz), d) in cands )
		{
			var removed = new HashSet<(int x, int z)>( block ) { (sx, sz) };
			foreach ( var c in removed ) g.RemoveFloor( c.x, c.z, tier.Level );
			cells.ExceptWith( removed );
			if ( g.Free( sx, sz, tier.Level, CellKind.Stair ) && block.All( c => g.Free( c.x, c.z, tier.Level + 1, CellKind.Floor ) ) )
			{
				g.Put( sx, sz, tier.Level, CellKind.Stair, null, d );
				TryPlaceTier( g, block, tier.Level + 1, TierKind.Room );
				return;
			}
			// undo
			foreach ( var c in removed ) g.Put( c.x, c.z, tier.Level, CellKind.Floor, tier );
			cells.UnionWith( removed );
		}
	}

	public static readonly (TierKind kind, double w)[] DefaultKindW = { (TierKind.Corridor, 5), (TierKind.Room, 4), (TierKind.Hall, 1) };
	public static readonly (int delta, double w)[] DefaultDeltaW = { (0, 4), (1, 3), (-1, 3), (2, 2), (-2, 2) };

	/// <summary>
	/// Grow hallways and rooms into empty space inside the bounding box at any level in `levels`,
	/// attached to existing tiers by adjacency or stairs, until the floor has `targetCells` walkable cells.
	/// Columns in avoidCols never get floor (e.g. the Pit void).
	/// </summary>
	public static int Densify( Grid g, DungeonRng rng, (int lo, int hi) xr, (int lo, int hi) zr, int targetCells,
		HashSet<(int x, int z)> avoidCols = null, (int lo, int hi)? levels = null,
		(TierKind kind, double w)[] kindW = null, (int delta, double w)[] deltaW = null, int maxTries = 6000 )
	{
		avoidCols ??= new HashSet<(int, int)>();
		var (lo, hi) = levels ?? (0, Grid.MaxLevel);
		kindW ??= DefaultKindW;
		deltaW ??= DefaultDeltaW;
		var kinds = kindW.Select( k => k.kind ).ToList(); var kindWeights = kindW.Select( k => k.w ).ToList();
		var deltas = deltaW.Select( k => k.delta ).ToList(); var deltaWeights = deltaW.Select( k => k.w ).ToList();

		bool Inside( HashSet<(int x, int z)> cells )
			=> cells.All( c => xr.lo <= c.x && c.x <= xr.hi && zr.lo <= c.z && c.z <= zr.hi && !avoidCols.Contains( c ) );

		int placed = g.Cells.Count;
		int tries = 0;
		while ( placed < targetCells && tries < maxTries )
		{
			tries++;
			var tier = rng.Choice( g.Tiers );
			if ( tier.Cells.Count == 0 ) continue;
			var d = rng.Choice( Dirs.All ); int dx = d.Dx(), dz = d.Dz();
			var edge = tier.Sorted().Where( c => !tier.Cells.Contains( (c.x + dx, c.z + dz) ) ).ToList();
			if ( edge.Count == 0 ) continue;
			var (ex, ez) = rng.Choice( edge );
			int delta = rng.Choices( deltas, deltaWeights );
			int nl = tier.Level + delta;
			if ( !(lo <= nl && nl <= hi) ) continue;
			var plan = new List<PlanStep>();
			int ax, az; Dir nd;
			if ( delta == 0 )
			{
				ax = ex + dx; az = ez + dz; nd = d;
			}
			else if ( delta > 0 )
			{
				var res = TryStairRun( g, ex, ez, tier.Level, d, delta, rng );
				if ( res == null ) continue;
				plan = res.Value.plan; var a = res.Value.arrival; ax = a.X; az = a.Z; nl = a.Level; nd = a.D;
			}
			else
			{
				var res = PlanDescend( g, ex, ez, tier.Level, d, -delta, rng );
				if ( res == null ) continue;
				plan = res.Value.plan; var a = res.Value.arrival; ax = a.X; az = a.Z; nl = a.Level; nd = a.D;
			}
			var kind = rng.Choices( kinds, kindWeights );
			HashSet<(int x, int z)> cells;
			if ( kind == TierKind.Corridor )
			{
				var (w, dp) = rng.Choice( new List<(int, int)> { (1, 3), (1, 4), (1, 5), (2, 4), (2, 5) } );
				cells = TierFootprintAhead( ax, az, nd, w, dp, rng, sideShift: w > 1 );
			}
			else if ( kind == TierKind.Room )
			{
				var (w, dp) = rng.Choice( new List<(int, int)> { (2, 2), (2, 3), (3, 2), (3, 3) } );
				cells = TierFootprintAhead( ax, az, nd, w, dp, rng );
			}
			else
			{
				var (w, dp) = rng.Choice( new List<(int, int)> { (4, 3), (3, 4), (4, 4) } );
				cells = TierFootprintAhead( ax, az, nd, w, dp, rng );
			}
			if ( !Inside( cells ) || !cells.All( c => g.Free( c.x, c.z, nl, CellKind.Floor ) ) ) continue;
			if ( plan.Count > 0 && cells.Overlaps( PlanCols( plan ) ) ) continue;
			CommitPlan( g, plan );
			var t = TryPlaceTier( g, cells, nl, kind );
			if ( t == null ) continue;
			placed = g.Cells.Count;
			if ( kind == TierKind.Hall && rng.NextDouble() < 0.5 ) AddInteriorWalls( g, t, rng, 1 );
		}
		return placed;
	}
}
