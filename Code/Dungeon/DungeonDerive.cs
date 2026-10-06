using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

public enum EdgeKind { Wall, Railing, StairWall }

/// <summary>One edge of a floor cell that needs geometry.</summary>
public readonly record struct EdgePiece( int X, int Z, int Level, Dir D, EdgeKind Kind );

/// <summary>
/// A pillar on a grid vertex, running from BaseLevel (or the ground when -1) up to TopLevel.
/// Interior: the vertex is surrounded by floor on all four sides (a free-standing column in a room)
/// rather than on the room's outline.
/// </summary>
public readonly record struct Pillar( int VX, int VZ, int TopLevel, int BaseLevel, bool Interior );

/// <summary>
/// Layers derived from the grid and never stored: per-cell-edge open / wall / railing / stair side wall,
/// and the pillars that hold raised floors up.
/// </summary>
public static class Derive
{
	/// <summary>For every floor cell edge decide: open / railing / wall.</summary>
	public static List<EdgePiece> Edges( Grid g, double railingBias, DungeonRng rng )
	{
		var edges = new List<EdgePiece>();
		foreach ( var key in g.SortedKeys() )
		{
			var (x, z, l) = key;
			var c = g.Cells[key];
			foreach ( var d in Dirs.All )
			{
				int dx = d.Dx(), dz = d.Dz();
				var n = (x + dx, z + dz, l);
				if ( c.Kind == CellKind.Stair )
				{
					// stairs: side walls on the two perpendicular sides
					if ( d == c.SDir.Turn()[0] || d == c.SDir.Turn()[1] )
						edges.Add( new EdgePiece( x, z, l, d, EdgeKind.StairWall ) );
					continue;
				}
				if ( g.Cells.TryGetValue( n, out var nc ) )
				{
					if ( nc.Kind == CellKind.Stair )
					{
						if ( (n.Item1 - nc.SDir.Dx(), n.Item2 - nc.SDir.Dz()) == (x, z) )
							continue;            // stair base faces us: open
					}
					else if ( g.HasIWall( key, n ) )
					{
						edges.Add( new EdgePiece( x, z, l, d, EdgeKind.Wall ) ); continue;
					}
					else continue;           // open floor
				}
				// stair top from below lands here?
				var s = (x + dx, z + dz, l - 1);
				if ( g.Cells.TryGetValue( s, out var sc ) && sc.Kind == CellKind.Stair )
				{
					if ( (s.Item1 + sc.SDir.Dx(), s.Item2 + sc.SDir.Dz()) == (x, z) ) continue;
				}
				// nothing walkable there: railing if there is anything below in that column
				bool below = false;
				for ( int k = 0; k < l; k++ )
					if ( g.Cells.ContainsKey( (x + dx, z + dz, k) ) ) { below = true; break; }
				bool railing = (below && rng.NextDouble() < railingBias + 0.3) || (!below && rng.NextDouble() < railingBias - 0.5);
				var kind = railing ? EdgeKind.Railing : EdgeKind.Wall;
				if ( l == 0 && !below ) kind = EdgeKind.Wall;
				edges.Add( new EdgePiece( x, z, l, d, kind ) );
			}
		}
		return edges;
	}

	/// <summary>
	/// Enclosed variant (v2): every floor-cell edge that is not open floor, a stair base, a stair top
	/// or a doorway gets a wall. No railings anywhere.
	/// </summary>
	public static List<EdgePiece> EdgesEnclosed( Grid g )
	{
		var edges = new List<EdgePiece>();
		foreach ( var key in g.SortedKeys() )
		{
			var (x, z, l) = key;
			var c = g.Cells[key];
			foreach ( var d in Dirs.All )
			{
				int dx = d.Dx(), dz = d.Dz();
				var n = (x + dx, z + dz, l);
				if ( c.Kind == CellKind.Stair )
				{
					if ( d == c.SDir.Turn()[0] || d == c.SDir.Turn()[1] )
						edges.Add( new EdgePiece( x, z, l, d, EdgeKind.StairWall ) );
					continue;
				}
				if ( g.Cells.TryGetValue( n, out var nc ) )
				{
					if ( nc.Kind == CellKind.Stair )
					{
						if ( (n.Item1 - nc.SDir.Dx(), n.Item2 - nc.SDir.Dz()) == (x, z) ) continue;   // stair base faces us
					}
					else if ( g.HasIWall( key, n ) )
					{
						edges.Add( new EdgePiece( x, z, l, d, EdgeKind.Wall ) ); continue;
					}
					else continue;   // open floor or doorway
				}
				var s = (x + dx, z + dz, l - 1);
				if ( g.Cells.TryGetValue( s, out var sc ) && sc.Kind == CellKind.Stair )
				{
					if ( (s.Item1 + sc.SDir.Dx(), s.Item2 + sc.SDir.Dz()) == (x, z) ) continue;     // stair top lands here
				}
				edges.Add( new EdgePiece( x, z, l, d, EdgeKind.Wall ) );
			}
		}
		return edges;
	}

	/// <summary>
	/// Pillars on grid vertices, per level and per connected floor region: outline corners, every few
	/// vertices along straight runs, and a sparse interior lattice. Skipped where a floor below is close
	/// enough to rest on or a wall from two levels down reaches the vertex.
	/// </summary>
	public static List<Pillar> Supports( Grid g, List<EdgePiece> edges, int span = 5, int edgeEvery = 4 )
	{
		var wallVertices = new HashSet<(int, int, int)>();
		foreach ( var e in edges )
		{
			if ( e.Kind == EdgeKind.Railing ) continue;
			var vs = e.D switch
			{
				Dir.N => new[] { (e.X, e.Z + 1), (e.X + 1, e.Z + 1) },
				Dir.S => new[] { (e.X, e.Z), (e.X + 1, e.Z) },
				Dir.E => new[] { (e.X + 1, e.Z), (e.X + 1, e.Z + 1) },
				_ => new[] { (e.X, e.Z), (e.X, e.Z + 1) },
			};
			foreach ( var v in vs ) wallVertices.Add( (v.Item1, v.Item2, e.Level + 2) );
		}
		var levels = g.Cells.Keys.Select( k => k.l ).Distinct().OrderBy( l => l ).ToList();
		var outList = new List<Pillar>();
		var placed = new HashSet<(int, int, int)>();
		foreach ( var l in levels )
		{
			if ( l == 0 ) continue;
			var cells = g.Cells.Keys.Where( k => k.l == l ).Select( k => (k.x, k.z) ).ToHashSet();
			var seen = new HashSet<(int x, int z)>();
			foreach ( var c0 in cells.OrderBy( c => c ) )
			{
				if ( seen.Contains( c0 ) ) continue;
				// connected region at this level (4-neighbour)
				var reg = new HashSet<(int x, int z)> { c0 };
				var stack = new Stack<(int x, int z)>(); stack.Push( c0 ); seen.Add( c0 );
				while ( stack.Count > 0 )
				{
					var (x, z) = stack.Pop();
					foreach ( var d in Dirs.All )
					{
						var n = (x + d.Dx(), z + d.Dz());
						if ( cells.Contains( n ) && seen.Add( n ) ) { reg.Add( n ); stack.Push( n ); }
					}
				}
				var verts = new HashSet<(int, int)>();
				foreach ( var (x, z) in reg )
				{
					verts.Add( (x, z) ); verts.Add( (x + 1, z) ); verts.Add( (x, z + 1) ); verts.Add( (x + 1, z + 1) );
				}
				var wanted = new List<(int vx, int vz, bool interior)>();
				foreach ( var (vx, vz) in verts.OrderBy( v => v ) )
				{
					bool o00 = reg.Contains( (vx - 1, vz - 1) ), o10 = reg.Contains( (vx, vz - 1) );
					bool o01 = reg.Contains( (vx - 1, vz) ), o11 = reg.Contains( (vx, vz) );
					int n = (o00 ? 1 : 0) + (o10 ? 1 : 0) + (o01 ? 1 : 0) + (o11 ? 1 : 0);
					if ( n == 4 )
					{
						if ( MathUtil.Mod( vx, span ) == 0 && MathUtil.Mod( vz, span ) == 0 ) wanted.Add( (vx, vz, true) );   // sparse interior lattice
					}
					else if ( n == 1 ) wanted.Add( (vx, vz, false) );                                                        // convex outline corner
					else if ( n == 2 )
					{
						if ( (o00 && o11 && !o10 && !o01) || (o10 && o01 && !o00 && !o11) ) wanted.Add( (vx, vz, false) );   // diagonal pinch
						else if ( MathUtil.Mod( vx, edgeEvery ) == 0 && MathUtil.Mod( vz, edgeEvery ) == 0 ) wanted.Add( (vx, vz, false) ); // mid-edge post
					}
				}
				foreach ( var (vx, vz, interior) in wanted )
				{
					if ( placed.Contains( (vx, vz, l) ) ) continue;
					// supported already?
					var cols = new[] { (vx - 1, vz - 1), (vx, vz - 1), (vx - 1, vz), (vx, vz) };
					int baseLevel = -1;
					for ( int k = l - 1; k >= 0; k-- )
					{
						if ( cols.Any( cc => g.Cells.ContainsKey( (cc.Item1, cc.Item2, k) ) ) ) { baseLevel = k; break; }
					}
					if ( baseLevel >= 0 && l - baseLevel <= 1 ) continue;          // resting on a step below
					if ( wallVertices.Contains( (vx, vz, l) ) ) continue;            // wall from below reaches
					bool near = false;
					for ( int i = -1; i <= 1 && !near; i++ )
						for ( int j = -1; j <= 1; j++ )
							if ( placed.Contains( (vx + i, vz + j, l) ) ) { near = true; break; }
					if ( near ) continue;
					placed.Add( (vx, vz, l) );
					outList.Add( new Pillar( vx, vz, l, baseLevel, interior ) );
				}
			}
		}
		return outList;
	}
}
