using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>A wall plane: the grid vertex it starts at and whether it runs along +Y (vertical in plan) or +X.</summary>
public readonly record struct WallLine( int VX, int VZ, bool AlongY ) : IComparable<WallLine>
{
	public int CompareTo( WallLine o ) => (VX, VZ, AlongY).CompareTo( (o.VX, o.VZ, o.AlongY) );
}

public enum SegKind { Full, Quarter }

/// <summary>
/// One wall piece the builder spawns: its line, the side it faces (the cell CX,CZ it belongs to), the
/// slab level K (a Full piece covers K and K+1, a Quarter only K). Exclusive means only this side asked
/// for these slabs, so nothing walkable sits directly behind the wall at this level.
/// </summary>
public readonly record struct WallSegment( WallLine Line, Dir D, int K, SegKind Kind, int CX, int CZ, bool Exclusive );

/// <summary>
/// Walls are merged per wall LINE in half-level slabs, so two cells that both want a wall on the same
/// line (an interior wall, or rooms at offset levels) never spawn two overlapping meshes. Two consecutive
/// slabs become one 5 m double-sided wall; a lone slab becomes quarter pieces facing whichever cell asked.
/// This lives outside the builder so the decorator keys wall swaps on the exact segments that get spawned.
/// </summary>
public static class Walls
{
	public static WallLine LineOf( int x, int z, Dir d ) => d switch
	{
		Dir.W => new( x, z, true ),
		Dir.E => new( x + 1, z, true ),
		Dir.S => new( x, z, false ),
		_ => new( x, z + 1, false ),   // N
	};

	/// <summary>The cell on the side of a line that a wall facing `d` belongs to (the wall is d's edge of that cell).</summary>
	public static (int x, int z) CellOf( WallLine line, Dir d ) => d switch
	{
		Dir.W => (line.VX, line.VZ),
		Dir.E => (line.VX - 1, line.VZ),
		Dir.S => (line.VX, line.VZ),
		_ => (line.VX, line.VZ - 1),   // N
	};

	public static List<WallSegment> Merge( List<EdgePiece> edges )
	{
		var lines = new Dictionary<WallLine, SortedDictionary<int, HashSet<Dir>>>();
		foreach ( var e in edges )
		{
			var line = LineOf( e.X, e.Z, e.D );
			if ( !lines.TryGetValue( line, out var slabs ) ) { slabs = new(); lines[line] = slabs; }
			for ( int k = e.Level; k < e.Level + 2; k++ )
			{
				if ( !slabs.TryGetValue( k, out var dirs ) ) { dirs = new(); slabs[k] = dirs; }
				dirs.Add( e.D );
			}
		}

		var outList = new List<WallSegment>();
		foreach ( var (line, slabs) in lines.OrderBy( kv => kv.Key ) )
		{
			var ks = slabs.Keys.ToList();
			for ( int i = 0; i < ks.Count; )
			{
				int k = ks[i];
				if ( i + 1 < ks.Count && ks[i + 1] == k + 1 )
				{
					var d = slabs[k].Min();
					var (cx, cz) = CellOf( line, d );
					bool exclusive = slabs[k].Count == 1 && slabs[k + 1].Count == 1 && slabs[k + 1].Contains( d );
					outList.Add( new WallSegment( line, d, k, SegKind.Full, cx, cz, exclusive ) );
					i += 2;
					continue;
				}
				foreach ( var d in slabs[k].OrderBy( v => v ) )
				{
					var (cx, cz) = CellOf( line, d );
					outList.Add( new WallSegment( line, d, k, SegKind.Quarter, cx, cz, slabs[k].Count == 1 ) );
				}
				i++;
			}
		}
		return outList;
	}
}
