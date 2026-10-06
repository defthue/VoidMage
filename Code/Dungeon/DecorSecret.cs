using System;
using System.Collections.Generic;
using System.Linq;
using static VoidMage.Dungeon.Blocks;

namespace VoidMage.Dungeon;

/// <summary>
/// One secret room per floor: a small room with no doorway, tucked against a corridor or room wall at the
/// same level. Exactly one shared wall segment gets the reserved sm_env_wall_05 piece (the tell); the
/// room is excluded from the connectivity check (placed after it) and from the walking line.
/// Breaking in is a later mechanic: swap the tell for wall_broken_edge + brick rubble and open the edge.
/// </summary>
public static class DecorSecret
{
	/// <summary>Returns the host-side cell, level and direction of the tell wall, or null.</summary>
	public static (int x, int z, int l, Dir d)? Place( Layout L, DungeonRng rng )
	{
		var g = L.G;
		var prot = ProtectedCells( g );
		var hosts = g.Tiers.Where( t => t.Cells.Count > 0 && t.Tag == TierTag.None && t.Kind is TierKind.Corridor or TierKind.Room )
			.OrderBy( t => t.Spur ? 1 : 0 ).ThenBy( t => t.Kind == TierKind.Corridor ? 0 : 1 ).ThenBy( t => t.Id ).ToList();
		var candidates = new List<(Tier t, (int x, int z) c, Dir d, int w, int dp)>();
		foreach ( var t in hosts )
		{
			int lvl = t.Level;
			foreach ( var c in t.Sorted() )
				foreach ( var d in Dirs.All )
				{
					var n = (c.x + d.Dx(), c.z + d.Dz());
					if ( g.Cells.ContainsKey( (n.Item1, n.Item2, lvl) ) ) continue;
					foreach ( var (w, dp) in new[] { (2, 2), (2, 3), (3, 2) } )
					{
						if ( Fits( L, t, n, d, w, dp, prot ) ) { candidates.Add( (t, c, d, w, dp) ); break; }
					}
				}
		}
		if ( candidates.Count == 0 ) return null;
		// corridors on the spine first: a suspicious wall in a hallway is the one players notice
		var weights = candidates.Select( c => (c.t.Spur ? 1.0 : 3.0) * (c.t.Kind == TierKind.Corridor ? 3.0 : 1.0) ).ToList();
		var pick = rng.Choices( candidates, weights );
		var cells = Footprint( pick.c.x + pick.d.Dx(), pick.c.z + pick.d.Dz(), pick.d, pick.w, pick.dp );
		var room = TryPlaceTier( g, cells, pick.t.Level, TierKind.Room, TierTag.Secret );
		if ( room == null ) return null;
		room.Heading = pick.d;
		// every shared edge walled, no door
		foreach ( var rc in room.Sorted() )
			foreach ( var d in Dirs.All )
			{
				var o = (rc.x + d.Dx(), rc.z + d.Dz(), room.Level);
				if ( g.Cells.TryGetValue( o, out var oc ) && oc.Tier == pick.t )
					g.IWalls.Add( Grid.Edge( (rc.x, rc.z, room.Level), o ) );
			}
		return (pick.c.x, pick.c.z, pick.t.Level, pick.d);
	}

	/// <summary>A w-across by dp-along rectangle starting at (x,z), centred on the entry column.</summary>
	private static HashSet<(int x, int z)> Footprint( int x, int z, Dir d, int w, int dp )
	{
		int dx = d.Dx(), dz = d.Dz(), px = -dz, pz = dx;
		int shift = -(w / 2);
		var cells = new HashSet<(int, int)>();
		for ( int i = 0; i < w; i++ )
			for ( int j = 0; j < dp; j++ )
				cells.Add( (x + px * (i + shift) + dx * j, z + pz * (i + shift) + dz * j) );
		return cells;
	}

	private static bool Fits( Layout L, Tier host, (int x, int z) n, Dir d, int w, int dp, HashSet<(int x, int z)> prot )
	{
		var g = L.G;
		int lvl = host.Level;
		var cells = Footprint( n.x, n.z, d, w, dp );
		if ( !L.Inside( cells ) ) return false;
		foreach ( var c in cells )
		{
			if ( prot.Contains( c ) || !g.Free( c.x, c.z, lvl, CellKind.Floor ) ) return false;
			// nothing but the host may touch the room at its level, and no stair may land on it from below
			foreach ( var dd in Dirs.All )
			{
				var o = (c.x + dd.Dx(), c.z + dd.Dz());
				if ( cells.Contains( o ) ) continue;
				if ( g.Cells.TryGetValue( (o.Item1, o.Item2, lvl), out var oc ) && (oc.Kind != CellKind.Floor || oc.Tier != host) ) return false;
				if ( g.Cells.TryGetValue( (o.Item1, o.Item2, lvl - 1), out var below ) && below.Kind == CellKind.Stair ) return false;
				if ( g.Cells.TryGetValue( (o.Item1, o.Item2, lvl + 1), out var above ) && above.Kind == CellKind.Stair ) return false;
			}
		}
		return true;
	}
}
