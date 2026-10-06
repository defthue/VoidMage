using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

public enum Dir { N, S, E, W }

public static class Dirs
{
	/// <summary>Python DIRS order — the generator picks "a random direction" from this list.</summary>
	public static readonly Dir[] All = { Dir.N, Dir.S, Dir.E, Dir.W };

	public static int Dx( this Dir d ) => d switch { Dir.E => 1, Dir.W => -1, _ => 0 };
	public static int Dz( this Dir d ) => d switch { Dir.N => 1, Dir.S => -1, _ => 0 };
	public static Dir Opp( this Dir d ) => d switch { Dir.N => Dir.S, Dir.S => Dir.N, Dir.E => Dir.W, _ => Dir.E };

	/// <summary>The two directions perpendicular to d (Python TURN).</summary>
	public static Dir[] Turn( this Dir d ) => d is Dir.N or Dir.S ? new[] { Dir.E, Dir.W } : new[] { Dir.N, Dir.S };

	public static (int x, int z) Step( this Dir d, int x, int z ) => (x + d.Dx(), z + d.Dz());
}

public enum CellKind { Floor, Stair, Landing }

public enum TierKind { Corridor, Room, Hall, Tower }

/// <summary>Secret: a room with no doorway, reachable only by breaking its marked wall (decoration stage).</summary>
public enum TierTag { None, Spawn, Exit, Shrine, Ambush, Loot, Secret }

public sealed class Cell
{
	public CellKind Kind;
	public int Level;
	public Tier Tier;      // null for stairs / landings
	public Dir SDir;       // STAIR only: direction it rises toward

	public Cell( CellKind kind, int level, Tier tier, Dir sdir )
	{
		Kind = kind; Level = level; Tier = tier; SDir = sdir;
	}

	public bool Walkable => Kind != CellKind.Stair;
}

public sealed class Tier
{
	public int Id;
	public HashSet<(int x, int z)> Cells;
	public int Level;
	public TierKind Kind;
	public TierTag Tag = TierTag.None;
	/// <summary>Direction the spine continues in from this tier (path grammar).</summary>
	public Dir Heading = Dir.E;
	/// <summary>A dead-end branch off the spine rather than the spine itself.</summary>
	public bool Spur;

	public Tier( int id, IEnumerable<(int x, int z)> cells, int level, TierKind kind )
	{
		Id = id; Cells = new HashSet<(int, int)>( cells ); Level = level; Kind = kind;
	}

	/// <summary>Cells in a deterministic order — anything that feeds the RNG must use this.</summary>
	public List<(int x, int z)> Sorted() => Cells.OrderBy( c => c ).ToList();

	public (double x, double z) Centroid()
	{
		double sx = 0, sz = 0;
		foreach ( var c in Cells ) { sx += c.x; sz += c.z; }
		return (sx / Cells.Count, sz / Cells.Count);
	}
}

/// <summary>
/// The dungeon as a 3D grid: X/Z in 5 m cells, Y in 2.5 m levels (0..MaxLevel).
/// A floor cell claims its level and the one above (headroom); a stair claims three.
/// </summary>
public sealed class Grid
{
	public const int MaxLevel = 8;

	public readonly Dictionary<(int x, int z, int l), Cell> Cells = new();
	public readonly HashSet<(int x, int z, int l)> Claims = new();
	public readonly List<Tier> Tiers = new();
	/// <summary>Interior walls between two same-level cells, stored as an ordered pair.</summary>
	public readonly HashSet<((int x, int z, int l) a, (int x, int z, int l) b)> IWalls = new();
	public readonly List<(int x, int z, int l, Dir d)> Stairs = new();

	private int _nextTierId;

	public int NextTierId() => ++_nextTierId;

	public static ((int, int, int), (int, int, int)) Edge( (int x, int z, int l) a, (int x, int z, int l) b )
		=> a.CompareTo( b ) <= 0 ? (a, b) : (b, a);

	public bool HasIWall( (int x, int z, int l) a, (int x, int z, int l) b ) => IWalls.Contains( Edge( a, b ) );

	public static IEnumerable<(int x, int z, int l)> ClaimSet( int x, int z, int level, CellKind kind )
	{
		int n = kind == CellKind.Stair ? 3 : 2;
		for ( int i = 0; i < n; i++ ) yield return (x, z, level + i);
	}

	public bool Free( int x, int z, int level, CellKind kind )
	{
		if ( level < 0 || level > MaxLevel ) return false;
		foreach ( var k in ClaimSet( x, z, level, kind ) )
			if ( Claims.Contains( k ) ) return false;
		return true;
	}

	public void Put( int x, int z, int level, CellKind kind, Tier tier = null, Dir sdir = Dir.N )
	{
		Cells[(x, z, level)] = new Cell( kind, level, tier, sdir );
		foreach ( var k in ClaimSet( x, z, level, kind ) ) Claims.Add( k );
		if ( kind == CellKind.Stair ) Stairs.Add( (x, z, level, sdir) );
	}

	/// <summary>Removes a FLOOR/LANDING cell and releases its two-level claim.</summary>
	public void RemoveFloor( int x, int z, int level )
	{
		Cells.Remove( (x, z, level) );
		foreach ( var k in ClaimSet( x, z, level, CellKind.Floor ) ) Claims.Remove( k );
	}

	public Cell FloorAt( int x, int z, int level )
	{
		return Cells.TryGetValue( (x, z, level), out var c ) && c.Kind != CellKind.Stair ? c : null;
	}

	public (int x0, int x1, int z0, int z1) Bounds()
	{
		int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
		foreach ( var k in Cells.Keys )
		{
			x0 = Math.Min( x0, k.x ); x1 = Math.Max( x1, k.x );
			z0 = Math.Min( z0, k.z ); z1 = Math.Max( z1, k.z );
		}
		return (x0, x1, z0, z1);
	}

	public List<(int x, int z, int l)> SortedKeys() => Cells.Keys.OrderBy( k => k ).ToList();

	/// <summary>Walkable-graph neighbours: flat moves (unless an interior wall), stair bases/tops.</summary>
	public List<(int x, int z, int l)> Neighbours( (int x, int z, int l) key )
	{
		var (x, z, l) = key;
		var c = Cells[key];
		var outList = new List<(int, int, int)>();
		if ( c.Kind == CellKind.Stair )
		{
			int dx = c.SDir.Dx(), dz = c.SDir.Dz();
			var top = (x + dx, z + dz, l + 1);
			var bot = (x - dx, z - dz, l);
			if ( Cells.ContainsKey( top ) ) outList.Add( top );
			if ( Cells.ContainsKey( bot ) ) outList.Add( bot );
			return outList;
		}
		foreach ( var d in Dirs.All )
		{
			int dx = d.Dx(), dz = d.Dz();
			var n = (x + dx, z + dz, l);
			if ( Cells.TryGetValue( n, out var nc ) )
			{
				if ( nc.Kind == CellKind.Stair )
				{
					// a stair connects to us only if its base faces us at our level
					if ( (n.Item1 - nc.SDir.Dx(), n.Item2 - nc.SDir.Dz()) == (x, z) ) outList.Add( n );
				}
				else if ( !HasIWall( key, n ) ) outList.Add( n );
			}
			// stair top: a stair one level below whose top lands on us
			var s = (x + dx, z + dz, l - 1);
			if ( Cells.TryGetValue( s, out var sc ) && sc.Kind == CellKind.Stair )
			{
				if ( (s.Item1 + sc.SDir.Dx(), s.Item2 + sc.SDir.Dz()) == (x, z) ) outList.Add( s );
			}
		}
		return outList;
	}

	public bool Connected()
	{
		if ( Cells.Count == 0 ) return false;
		var start = SortedKeys()[0];
		var seen = new HashSet<(int, int, int)> { start };
		var q = new Queue<(int, int, int)>();
		q.Enqueue( start );
		while ( q.Count > 0 )
		{
			var k = q.Dequeue();
			foreach ( var n in Neighbours( k ) )
				if ( seen.Add( n ) ) q.Enqueue( n );
		}
		return seen.Count == Cells.Count;
	}

	/// <summary>
	/// Stairs missing the floor or landing at their foot or their head. Connected() can not see these: a stair
	/// hanging off a tier by one end is still reachable, it just walks the player off the world at the other.
	/// </summary>
	public List<(int x, int z, int l, Dir d)> DanglingStairs()
		=> Stairs.Where( s => FloorAt( s.x - s.d.Dx(), s.z - s.d.Dz(), s.l ) == null || FloorAt( s.x + s.d.Dx(), s.z + s.d.Dz(), s.l + 1 ) == null ).ToList();

	public Dictionary<(int x, int z, int l), int> DistancesFrom( (int x, int z, int l) start )
	{
		var dist = new Dictionary<(int, int, int), int> { [start] = 0 };
		var q = new Queue<(int, int, int)>();
		q.Enqueue( start );
		while ( q.Count > 0 )
		{
			var k = q.Dequeue();
			foreach ( var n in Neighbours( k ) )
			{
				if ( dist.ContainsKey( n ) ) continue;
				dist[n] = dist[k] + 1;
				q.Enqueue( n );
			}
		}
		return dist;
	}

	/// <summary>Graph distance from a tier to a cell key, or a large number if unreachable.</summary>
	public static int DistTo( Dictionary<(int x, int z, int l), int> dist, (int x, int z, int l) k, int fallback )
		=> dist.TryGetValue( k, out var v ) ? v : fallback;
}

public static class MathUtil
{
	/// <summary>Python-style floor division (rounds toward negative infinity).</summary>
	public static int FloorDiv( int a, int b )
	{
		int q = a / b;
		if ( (a % b != 0) && ((a < 0) != (b < 0)) ) q--;
		return q;
	}

	/// <summary>Python-style modulo (result has the sign of the divisor).</summary>
	public static int Mod( int a, int b )
	{
		int m = a % b;
		return m < 0 ? m + b : m;
	}
}
