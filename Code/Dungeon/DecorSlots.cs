using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// What is already taken, in quarter cells, wall faces, whole tiles and ceilings. Every placement claims
/// before the next slot is considered, so props never overlap and never block the walking line.
/// Quarter index: bit 0 = east half, bit 1 = north half (SW 0, SE 1, NW 2, NE 3).
/// </summary>
public sealed class Occupancy
{
	private readonly HashSet<(int x, int z, int l, int q)> _quarter = new();
	private readonly HashSet<(int x, int z, int l, Dir d, WallLayer layer)> _face = new();
	private readonly HashSet<(int x, int z, int l)> _tile = new();
	private readonly HashSet<(int x, int z, int l)> _ceil = new();
	/// <summary>Quarters holding a piece of litter; litter claims no space, it only avoids stacking on itself.</summary>
	private readonly HashSet<(int x, int z, int l, int q)> _litter = new();
	/// <summary>Height (m) of the tallest prop standing against each wall edge; a mounted prop must clear it.</summary>
	private readonly Dictionary<(int x, int z, int l, Dir d), float> _bandHeight = new();

	public static int QuarterOf( float gx, float gz, int x, int z ) => (gx - x >= 0.5f ? 1 : 0) + (gz - z >= 0.5f ? 2 : 0);
	public static int QuarterOf( Dir ns, Dir ew ) => (ew == Dir.E ? 1 : 0) + (ns == Dir.N ? 2 : 0);

	/// <summary>The two quarters that touch wall edge d of a cell.</summary>
	public static int[] QuartersOn( Dir d ) => d switch
	{
		Dir.N => new[] { 2, 3 },
		Dir.S => new[] { 0, 1 },
		Dir.E => new[] { 1, 3 },
		_ => new[] { 0, 2 },
	};

	/// <summary>The quarter touching wall d and the one behind it: what a deep prop (bed, coffin) against that wall takes.</summary>
	public static int[] ColumnOf( Dir d, int wallQuarter ) => new[] { wallQuarter, wallQuarter ^ (d is Dir.N or Dir.S ? 2 : 1) };

	public bool QuarterFree( int x, int z, int l, int q ) => !_tile.Contains( (x, z, l) ) && !_quarter.Contains( (x, z, l, q) );
	public bool LitterFree( int x, int z, int l, int q ) => QuarterFree( x, z, l, q ) && !_litter.Contains( (x, z, l, q) );
	public void NoteLitter( int x, int z, int l, int q ) => _litter.Add( (x, z, l, q) );
	public bool TileFree( int x, int z, int l ) => !_tile.Contains( (x, z, l) ) && Enumerable.Range( 0, 4 ).All( q => !_quarter.Contains( (x, z, l, q) ) );
	/// <summary>Something owns the WHOLE cell (an altar, a centre set piece, a trap plate). Not the same question
	/// as TileFree, which also fails on a cell holding one small prop in one quarter — the greenery's root mats
	/// want to lie over those, and only need to stay off the things that own the floor outright.</summary>
	public bool TileClaimed( int x, int z, int l ) => _tile.Contains( (x, z, l) );
	public bool CellEmpty( int x, int z, int l ) => TileFree( x, z, l );
	public bool FaceFree( int x, int z, int l, Dir d, WallLayer layer ) => !_face.Contains( (x, z, l, d, layer) );
	/// <summary>Nothing mounted or hung on this wall segment: a banner and a torch must never share one.</summary>
	public bool WallFree( int x, int z, int l, Dir d ) => FaceFree( x, z, l, d, WallLayer.Mount ) && FaceFree( x, z, l, d, WallLayer.High );
	public void ClaimWall( int x, int z, int l, Dir d ) { ClaimFace( x, z, l, d, WallLayer.Mount ); ClaimFace( x, z, l, d, WallLayer.High ); }
	public bool CeilFree( int x, int z, int l ) => !_ceil.Contains( (x, z, l) );
	public float BandHeight( int x, int z, int l, Dir d ) => _bandHeight.TryGetValue( (x, z, l, d), out var h ) ? h : 0;

	public void ClaimQuarter( int x, int z, int l, int q )
	{
		if ( !_quarter.Add( (x, z, l, q) ) ) throw new InvalidOperationException( $"quarter {x},{z},{l} q{q} claimed twice" );
	}
	public void ClaimTile( int x, int z, int l )
	{
		if ( !TileFree( x, z, l ) ) throw new InvalidOperationException( $"tile {x},{z},{l} claimed twice" );
		_tile.Add( (x, z, l) );
	}
	public void ClaimFace( int x, int z, int l, Dir d, WallLayer layer )
	{
		if ( !_face.Add( (x, z, l, d, layer) ) ) throw new InvalidOperationException( $"face {x},{z},{l} {d} {layer} claimed twice" );
	}
	public void ClaimCeil( int x, int z, int l )
	{
		if ( !_ceil.Add( (x, z, l) ) ) throw new InvalidOperationException( $"ceiling {x},{z},{l} claimed twice" );
	}
	public void NoteBand( int x, int z, int l, Dir d, float height )
	{
		var k = (x, z, l, d);
		_bandHeight[k] = Math.Max( BandHeight( x, z, l, d ), height );
	}
}

/// <summary>Everything the fill pass needs to know about one tier.</summary>
public sealed class TierCtx
{
	public Tier T;
	public DecorRole Role;
	public double DepthScore;
	/// <summary>Cells with a way out (doorway, stair base/top, interior wall gap) and the direction out.</summary>
	public List<((int x, int z) cell, Dir outDir)> Portals = new();
	public HashSet<(int x, int z)> Walk = new();
	public HashSet<(int x, int z)> Approach = new();
	public List<Slot> WallSlots = new();
	public List<WallRun> Runs = new();
	public List<Slot> Corners = new();
	/// <summary>Candidate centre blocks: the cells and their extent (across x, along z).</summary>
	public List<List<(int x, int z)>> Centres = new();
	public List<Slot> Ceilings = new();
	public List<Slot> PillarFaces = new();
	/// <summary>Exclusive outside walls with an empty column behind: alcoves and other pieces that recess into rock.</summary>
	public List<WallSegment> SwapCandidates = new();
	/// <summary>Walls with another room directly behind at the same level: windows.</summary>
	public List<SharedWall> SharedWalls = new();
	/// <summary>Grid vertices at this level carrying a pillar (outline posts and interior columns alike).</summary>
	public HashSet<(int vx, int vz)> PillarVerts = new();
	public int Lights;

	public bool Has( int x, int z ) => T.Cells.Contains( (x, z) );
	/// <summary>Whether the corner of cell (x,z) on the side of quarter q carries a pillar.</summary>
	public bool PillarAtQuarter( int x, int z, int q ) => PillarVerts.Contains( (x + ((q & 1) == 1 ? 1 : 0), z + ((q & 2) == 2 ? 1 : 0)) );
	public bool Clear( int x, int z ) => !Walk.Contains( (x, z) ) && !Approach.Contains( (x, z) );
}

/// <summary>Masks and slot extraction: the geometry side of decoration, in cell and edge coordinates.</summary>
public static class DecorSlots
{
	public static Dictionary<int, TierCtx> Build( DungeonLayout lay, DecorResult res, Occupancy occ, int depth )
	{
		var g = lay.Grid;
		var wallSet = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var e in lay.Edges ) if ( e.Kind == EdgeKind.Wall ) wallSet.Add( (e.X, e.Z, e.Level, e.D) );
		var pillarVerts = new HashSet<(int vx, int vz, int l)>( lay.Pillars.Select( p => (p.VX, p.VZ, p.TopLevel) ) );
		var interior = lay.Pillars.Where( p => p.Interior ).ToList();
		var segments = Walls.Merge( lay.Edges );
		var spawn = lay.Find( TierTag.Spawn );

		// occupancy seeded with the free-standing pillars: nothing may stand inside one of those columns. The
		// outline pillars are 60 cm posts in the room corners; props just keep clear of them (PillarVerts).
		foreach ( var p in interior )
		{
			int l = p.TopLevel;
			foreach ( var (cx, cz, q) in new[] { (p.VX - 1, p.VZ - 1, 3), (p.VX, p.VZ - 1, 2), (p.VX - 1, p.VZ, 1), (p.VX, p.VZ, 0) } )
				if ( g.Cells.TryGetValue( (cx, cz, l), out var c ) && c.Tier != null && occ.QuarterFree( cx, cz, l, q ) )
					occ.ClaimQuarter( cx, cz, l, q );
		}

		// the secret room's tell wall carries nothing on either side: a torch or banner would hide the tell
		if ( lay.SecretWall.HasValue )
		{
			var sw = lay.SecretWall.Value;
			occ.ClaimWall( sw.x, sw.z, sw.l, sw.d );
			occ.ClaimWall( sw.x + sw.d.Dx(), sw.z + sw.d.Dz(), sw.l, sw.d.Opp() );
		}

		var ctxs = new Dictionary<int, TierCtx>();
		foreach ( var t in g.Tiers.OrderBy( t => t.Id ) )
		{
			if ( t.Cells.Count == 0 ) continue;
			var ctx = new TierCtx { T = t, DepthScore = DepthScore( depth, t.Level ) };
			int L = t.Level;
			var sorted = t.Sorted();
			foreach ( var p in lay.Pillars ) if ( p.TopLevel == L ) ctx.PillarVerts.Add( (p.VX, p.VZ) );

			// ---- portals
			foreach ( var c in sorted )
			{
				foreach ( var n in g.Neighbours( (c.x, c.z, L) ) )
				{
					if ( g.Cells[n].Tier == t ) continue;
					ctx.Portals.Add( (c, DirTo( c, (n.x, n.z) )) );
				}
			}
			// interior wall gaps (Cathedral hall): a same-tier crossing on a line that is walled elsewhere
			var walledLines = new HashSet<WallLine>();
			foreach ( var c in sorted )
				foreach ( var d in Dirs.All )
					if ( g.HasIWall( (c.x, c.z, L), (c.x + d.Dx(), c.z + d.Dz(), L) ) && t.Cells.Contains( (c.x + d.Dx(), c.z + d.Dz()) ) )
						walledLines.Add( Walls.LineOf( c.x, c.z, d ) );
			foreach ( var c in sorted )
				foreach ( var d in new[] { Dir.E, Dir.N } )
				{
					var n = (c.x + d.Dx(), c.z + d.Dz());
					if ( !t.Cells.Contains( n ) || g.HasIWall( (c.x, c.z, L), (n.Item1, n.Item2, L) ) ) continue;
					if ( !walledLines.Contains( Walls.LineOf( c.x, c.z, d ) ) ) continue;
					ctx.Portals.Add( (c, d) );
					ctx.Portals.Add( (n, d.Opp()) );
				}
			ctx.Portals = ctx.Portals.Distinct().OrderBy( p => p ).ToList();

			// ---- walk + approach
			foreach ( var (cell, outDir) in ctx.Portals )
			{
				ctx.Approach.Add( cell );
				var inward = (cell.x - outDir.Dx(), cell.z - outDir.Dz());
				if ( t.Cells.Contains( inward ) ) ctx.Approach.Add( inward );
			}
			var portalCells = ctx.Portals.Select( p => p.cell ).Distinct().ToList();
			if ( portalCells.Count == 1 )
			{
				ctx.Walk.UnionWith( ctx.Approach );
			}
			else if ( portalCells.Count <= 8 )
			{
				for ( int i = 0; i < portalCells.Count; i++ )
					for ( int j = i + 1; j < portalCells.Count; j++ )
						ctx.Walk.UnionWith( Path( g, t, portalCells[i], portalCells[j] ) );
			}
			else
			{
				var mid = Nearest( sorted, t.Centroid() );
				foreach ( var p in portalCells ) ctx.Walk.UnionWith( Path( g, t, p, mid ) );
			}
			ctx.Walk.UnionWith( ctx.Approach );
			if ( t == spawn )
			{
				var s = Nearest( sorted, t.Centroid() );
				ctx.Walk.Add( s );
				foreach ( var d in Dirs.All ) if ( t.Cells.Contains( (s.x + d.Dx(), s.z + d.Dz()) ) ) ctx.Walk.Add( (s.x + d.Dx(), s.z + d.Dz()) );
			}
			if ( t.Tag == TierTag.Secret && lay.SecretWall.HasValue )
			{
				var sw = lay.SecretWall.Value;
				var behind = (sw.x + sw.d.Dx(), sw.z + sw.d.Dz());
				if ( t.Cells.Contains( behind ) ) ctx.Walk.Add( behind );
			}
			// and the cell in FRONT of the tell, in the room it opens from, stays on the walking line too: nothing
			// blocking stands in front of it (user: never anything against or in front of the secret wall)
			if ( lay.SecretWall.HasValue && lay.SecretWall.Value.l == L && t.Cells.Contains( (lay.SecretWall.Value.x, lay.SecretWall.Value.z) ) )
				ctx.Walk.Add( (lay.SecretWall.Value.x, lay.SecretWall.Value.z) );
			foreach ( var c in ctx.Walk ) res.Walk.Add( (c.x, c.z, L) );
			foreach ( var c in ctx.Approach ) res.Approach.Add( (c.x, c.z, L) );

			// ---- wall slots and runs
			var portalSet = new HashSet<(int x, int z)>( portalCells );
			foreach ( var c in sorted )
				foreach ( var d in Dirs.All )
				{
					if ( !wallSet.Contains( (c.x, c.z, L, d) ) ) continue;
					if ( IsSecretWall( lay, c.x, c.z, L, d ) ) continue;   // nothing stands in front of the tell either
					var (va, vb) = EdgeVerts( c.x, c.z, d );
					bool pillar = pillarVerts.Contains( (va.Item1, va.Item2, L) ) || pillarVerts.Contains( (vb.Item1, vb.Item2, L) );
					ctx.WallSlots.Add( new Slot( SlotKind.Wall, c.x, c.z, L, d, d, t.Id, portalSet.Contains( c ), pillar ) );
				}
			foreach ( var group in ctx.WallSlots.GroupBy( s => (Walls.LineOf( s.X, s.Z, s.D ), s.D) ).OrderBy( gr => gr.Key ) )
			{
				var along = group.Key.Item1.AlongY ? group.OrderBy( s => s.Z ).ToList() : group.OrderBy( s => s.X ).ToList();
				WallRun run = null;
				int prev = int.MinValue;
				foreach ( var s in along )
				{
					int coord = group.Key.Item1.AlongY ? s.Z : s.X;
					if ( run == null || coord != prev + 1 )
					{
						run = new WallRun { TierId = t.Id, D = s.D };
						ctx.Runs.Add( run );
					}
					run.Slots.Add( s );
					prev = coord;
				}
			}

			// ---- corners
			foreach ( var c in sorted )
				foreach ( var ns in new[] { Dir.S, Dir.N } )
					foreach ( var ew in new[] { Dir.W, Dir.E } )
					{
						if ( !wallSet.Contains( (c.x, c.z, L, ns) ) || !wallSet.Contains( (c.x, c.z, L, ew) ) ) continue;
						// a corner on the secret tell is no corner: its prop would stand against the tell (user: a statue did)
						if ( IsSecretWall( lay, c.x, c.z, L, ns ) || IsSecretWall( lay, c.x, c.z, L, ew ) ) continue;
						var v = (c.x + (ew == Dir.E ? 1 : 0), c.z + (ns == Dir.N ? 1 : 0));
						ctx.Corners.Add( new Slot( SlotKind.Corner, c.x, c.z, L, ns, ew, t.Id, portalSet.Contains( c ), pillarVerts.Contains( (v.Item1, v.Item2, L) ) ) );
					}

			// ---- centres: one per connected partition of the tier (interior walls split halls);
			// corridors too, since a tagged dead-end hallway can carry a role (shrine, loot)
			{
				foreach ( var part in Partitions( g, t ) )
				{
					int x0 = part.Min( c => c.x ), x1 = part.Max( c => c.x ), z0 = part.Min( c => c.z ), z1 = part.Max( c => c.z );
					int w = x1 - x0 + 1, h = z1 - z0 + 1;
					var xs = w % 2 == 1 ? new[] { x0 + w / 2 } : new[] { x0 + w / 2 - 1, x0 + w / 2 };
					var zs = h % 2 == 1 ? new[] { z0 + h / 2 } : new[] { z0 + h / 2 - 1, z0 + h / 2 };
					var block = xs.SelectMany( x => zs.Select( z => (x, z) ) ).Where( part.Contains ).OrderBy( c => c ).ToList();
					if ( block.Count == xs.Length * zs.Length ) ctx.Centres.Add( block );
					// single-cell fallbacks so a 2x2 room with one door still gets something
					foreach ( var c in block ) ctx.Centres.Add( new List<(int x, int z)> { c } );
				}
			}

			// ---- ceilings
			foreach ( var c in sorted )
				ctx.Ceilings.Add( new Slot( SlotKind.Ceiling, c.x, c.z, L, Dir.N, Dir.N, t.Id, portalSet.Contains( c ), false ) );

			// ---- pillar faces
			foreach ( var p in interior )
			{
				if ( p.TopLevel != L ) continue;
				var around = new[] { (p.VX - 1, p.VZ - 1), (p.VX, p.VZ - 1), (p.VX - 1, p.VZ), (p.VX, p.VZ) };
				if ( !around.All( c => t.Cells.Contains( c ) ) ) continue;
				foreach ( var f in Dirs.All )
					ctx.PillarFaces.Add( new Slot( SlotKind.Pillar, p.VX, p.VZ, L, f, f, t.Id, false, true ) );
			}

			// ---- wall swap candidates: exclusive full segments of this tier with an empty column behind
			// (recessed pieces), and full segments with another room's floor directly behind (windows)
			foreach ( var seg in segments )
			{
				if ( seg.Kind != SegKind.Full || seg.K != L ) continue;
				if ( lay.SecretWall.HasValue && Walls.LineOf( lay.SecretWall.Value.x, lay.SecretWall.Value.z, lay.SecretWall.Value.d ) == seg.Line && lay.SecretWall.Value.l == L ) continue;
				var (bx, bz) = Walls.CellOf( seg.Line, seg.D.Opp() );
				bool ownsFront = t.Cells.Contains( (seg.CX, seg.CZ) ), ownsBack = t.Cells.Contains( (bx, bz) );
				if ( !ownsFront && !ownsBack ) continue;
				var (ox, oz, od) = ownsFront ? (seg.CX, seg.CZ, seg.D) : (bx, bz, seg.D.Opp());
				var (fx, fz, fd) = ownsFront ? (bx, bz, seg.D.Opp()) : (seg.CX, seg.CZ, seg.D);
				if ( portalSet.Contains( (ox, oz) ) ) continue;
				// rock behind at this level and the one above (a recessed piece gets a ceiling at L+2, so nothing may stand there either)
			if ( ownsFront && seg.Exclusive && !g.Claims.Contains( (fx, fz, L) ) && !g.Claims.Contains( (fx, fz, L + 1) ) && !g.Claims.Contains( (fx, fz, L + 2) ) )
				{
					ctx.SwapCandidates.Add( seg );
					continue;
				}
				// a window needs a walkable room behind it, never the secret room (it would give the secret away)
				var far = g.FloorAt( fx, fz, L );
				if ( far == null || far.Tier == null || far.Tier.Tag == TierTag.Secret ) continue;
				ctx.SharedWalls.Add( new SharedWall( seg, ox, oz, od, fx, fz, fd ) );
			}

			res.SlotCount += ctx.WallSlots.Count + ctx.Corners.Count + ctx.Centres.Count + ctx.Ceilings.Count + ctx.PillarFaces.Count + ctx.SwapCandidates.Count + ctx.SharedWalls.Count;
			ctxs[t.Id] = ctx;
		}
		return ctxs;
	}

	/// <summary>Either side of the secret room's tell wall.</summary>
	public static bool IsSecretWall( DungeonLayout lay, int x, int z, int l, Dir d )
	{
		if ( !lay.SecretWall.HasValue ) return false;
		var sw = lay.SecretWall.Value;
		if ( sw.l != l ) return false;
		return (x, z, d) == (sw.x, sw.z, sw.d) || (x, z, d) == (sw.x + sw.d.Dx(), sw.z + sw.d.Dz(), sw.d.Opp());
	}

	/// <summary>0 at the top of floor 1, 1 at the bottom of floor 5 and deeper.</summary>
	public static double DepthScore( int depth, int level )
		=> 0.5 * Math.Clamp( (depth - 1) / 4.0, 0, 1 ) + 0.5 * (Grid.MaxLevel - level) / (double)Grid.MaxLevel;

	public static Dir DirTo( (int x, int z) a, (int x, int z) b )
	{
		int dx = b.x - a.x, dz = b.z - a.z;
		if ( Math.Abs( dx ) >= Math.Abs( dz ) ) return dx >= 0 ? Dir.E : Dir.W;
		return dz >= 0 ? Dir.N : Dir.S;
	}

	public static (int x, int z) Nearest( List<(int x, int z)> sorted, (double x, double z) p )
		=> sorted.OrderBy( c => Math.Abs( c.x - p.x ) + Math.Abs( c.z - p.z ) ).ThenBy( c => c ).First();

	/// <summary>Grid vertices at both ends of edge d of a cell.</summary>
	public static ((int, int), (int, int)) EdgeVerts( int x, int z, Dir d ) => d switch
	{
		Dir.N => ((x, z + 1), (x + 1, z + 1)),
		Dir.S => ((x, z), (x + 1, z)),
		Dir.E => ((x + 1, z), (x + 1, z + 1)),
		_ => ((x, z), (x, z + 1)),
	};

	/// <summary>Shortest path between two cells of a tier through its own walkable graph (interior walls respected).</summary>
	public static List<(int x, int z)> Path( Grid g, Tier t, (int x, int z) a, (int x, int z) b )
	{
		int L = t.Level;
		var prev = new Dictionary<(int, int), (int, int)?> { [a] = null };
		var q = new Queue<(int x, int z)>();
		q.Enqueue( a );
		while ( q.Count > 0 )
		{
			var cur = q.Dequeue();
			if ( cur == b ) break;
			foreach ( var n in g.Neighbours( (cur.x, cur.z, L) ).OrderBy( n => n ) )
			{
				if ( n.l != L || g.Cells[n].Tier != t ) continue;
				var k = (n.x, n.z);
				if ( prev.ContainsKey( k ) ) continue;
				prev[k] = cur;
				q.Enqueue( k );
			}
		}
		var path = new List<(int x, int z)>();
		if ( !prev.ContainsKey( b ) ) { path.Add( a ); path.Add( b ); return path; }
		(int, int)? c = b;
		while ( c != null ) { path.Add( c.Value ); c = prev[c.Value]; }
		return path;
	}

	/// <summary>Connected components of a tier's cells through its own walkable graph.</summary>
	public static List<HashSet<(int x, int z)>> Partitions( Grid g, Tier t )
	{
		var parts = new List<HashSet<(int x, int z)>>();
		var seen = new HashSet<(int x, int z)>();
		foreach ( var c0 in t.Sorted() )
		{
			if ( !seen.Add( c0 ) ) continue;
			var part = new HashSet<(int x, int z)> { c0 };
			var stack = new Stack<(int x, int z)>();
			stack.Push( c0 );
			while ( stack.Count > 0 )
			{
				var c = stack.Pop();
				foreach ( var n in g.Neighbours( (c.x, c.z, t.Level) ) )
				{
					if ( n.l != t.Level || g.Cells[n].Tier != t ) continue;
					if ( seen.Add( (n.x, n.z) ) ) { part.Add( (n.x, n.z) ); stack.Push( (n.x, n.z) ); }
				}
			}
			parts.Add( part );
		}
		return parts;
	}
}
