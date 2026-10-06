using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// A wall that has come down. X,Z,Level,D is the cell and the side it stood on (the wall-swap convention).
/// Scale1/Scale2 size the two ragged stubs left at each end of the 5 m segment; the hole between them is
/// whatever is left over. Seed is derived from the position rather than drawn from a stream, so every peer
/// dresses the same breach the same way.
/// </summary>
public readonly record struct Breach( int X, int Z, int Level, Dir D, float Scale1, float Scale2, int Seed )
{
	/// <summary>The wall plane the breach sits in (what BuildCorners keys its "solid" set on).</summary>
	public WallLine Line => Walls.LineOf( X, Z, D );
	/// <summary>The cell on the far side of the hole.</summary>
	public (int x, int z, int l) Far => (X + D.Dx(), Z + D.Dz(), Level);
	/// <summary>Metres from the segment's pivot end to the near edge of the hole.</summary>
	public float Offset => DungeonBreach.JambStartLen * Scale1;
	/// <summary>Width of the hole in metres.</summary>
	public float Gap => DungeonBreach.SegmentM - DungeonBreach.JambStartLen * Scale1 - DungeonBreach.JambEndLen * Scale2;
}

/// <summary>Why the candidate walls were turned down, so a floor with too few breaches can say what happened.</summary>
public sealed class BreachReport
{
	public int Walls, SameTier, Secret, Linked, MixedLine, Eligible, Wanted, SamePair, SameLine, TooFar, Opened;

	public override string ToString()
		=> $"{Walls} walls, {Eligible} eligible, wanted {Wanted}, opened {Opened} (rejected: same tier {SameTier}, " +
			$"secret {Secret}, already linked {Linked}, line shared with another level {MixedLine}; " +
			$"passed over: same room pair {SamePair}, line used {SameLine}, too far around {TooFar})";
}

/// <summary>
/// The mossy floors' one structural difference: a share of the walls where two rooms merely TOUCH have fallen
/// in. Same grammar, same spine, same seeds — but the floor plays as a porous ruin rather than a tree of
/// corridors: you find shortcuts, and things flank you through them.
///
/// A breach is made by deleting the interior wall from the grid, nothing more. Everything downstream then
/// treats it as an opening for free — the walking graph joins the two cells, Derive.EdgesEnclosed emits no
/// wall, the decorator's portals come off Grid.Neighbours so the threshold lands on the walking line and no
/// blocking prop or trap can sit in it, and the navmesh bakes straight through. The builder only dresses the
/// hole afterwards (DungeonBuilder.Breaches.cs). No doorway, no frame, no door.
///
/// Runs after the connectivity check and after the tags, and can only ADD routes, so no seed's layout,
/// tagging or decoration shifts because of it.
/// </summary>
public static class DungeonBreach
{
	/// <summary>One wall segment, metres. (The kit's pieces are 5.0-5.2 m; the grid pitch is what matters here.)</summary>
	public const float SegmentM = 5f;
	/// <summary>How far each stub runs from its own pivot, metres, measured from the FBX with tools/fbx_shape.py:
	/// sm_env_wall_broken_edge_01 spans x -8..212 cm (so +2.116 m from the pivot) and _02 spans x -188..4 cm.</summary>
	public const float JambStartLen = 2.116f, JambEndLen = 1.878f;

	/// <summary>
	/// Stub scale. At natural size the pair leaves a 1.0 m slot, which fights the player capsule and the
	/// navmesh agent radius; this range opens it to roughly 1.5-2.2 m. The floor of 0.70 is also what keeps
	/// the toon outline (a world-space extrude) from going visibly uneven on a squashed piece.
	/// </summary>
	private const double ScaleMin = 0.70, ScaleMax = 0.88;
	/// <summary>Asymmetry between the two stubs: shifts where along the wall the hole sits without changing its width.</summary>
	private const double SkewMax = 0.10;

	/// <summary>
	/// Most cells of walking a breach may save. Without this a hole between two tiers that happen to touch but
	/// sit at opposite ends of the spine would skip most of the floor; "not spawn-to-exit" is nowhere near
	/// enough on its own. Measured on the live graph, so two breaches can never compound into a bigger one.
	/// </summary>
	private const int MaxDetour = 10;

	/// <summary>
	/// Opens walls in `L`, aiming for between `min` and `max` of them. Returns the breaches and the EdgePieces
	/// that no longer exist because of them — Derive.Supports still needs to see those or it will grow a pillar
	/// where the wall used to hold the vertex (see DungeonGenerator).
	///
	/// A COUNT, not a per-wall chance. Only a handful of walls on a floor are ever eligible (a small floor can
	/// have as few as two), so a probability produced nothing at 25% and still missed the budget at 100%. The
	/// eligible walls are gathered in sorted order, shuffled, and taken until the target is met.
	/// </summary>
	public static (List<Breach> breaches, List<EdgePiece> removed) Open( Layout L, DungeonRng rng, int min, int max, BreachReport report = null )
	{
		report ??= new BreachReport();
		var breaches = new List<Breach>();
		var removed = new List<EdgePiece>();
		if ( max <= 0 ) return (breaches, removed);
		var g = L.G;

		// Which levels each wall plane carries geometry at, indexed BEFORE anything is removed. A wall at level
		// k occupies slabs k and k+1, and Walls.Merge pairs consecutive slabs per line — so deleting this wall
		// can change how a NEIGHBOURING one on the same line pairs up, putting a 5 m wall where a 2.5 m quarter
		// was. Only walls one level away can overlap us, so only those disqualify the line.
		var lineLevels = new Dictionary<WallLine, HashSet<int>>();
		foreach ( var e in Derive.EdgesEnclosed( g ) )
		{
			var line = Walls.LineOf( e.X, e.Z, e.D );
			if ( !lineLevels.TryGetValue( line, out var set ) ) lineLevels[line] = set = new HashSet<int>();
			set.Add( e.Level );
		}

		// ---- gather. Stateless filters only, in sorted order: this list must be identical on every peer, and
		// identical with and without decoration (the secret room's own walls are dropped by the Secret test
		// before they can affect anything).
		var eligible = new List<((int x, int z, int l) a, (int x, int z, int l) b, Tier ta, Tier tb, Dir d, WallLine line)>();
		foreach ( var edge in g.IWalls.OrderBy( e => e ).ToList() )
		{
			var (a, b) = edge;
			if ( a.l != b.l ) continue;
			if ( !g.Cells.TryGetValue( a, out var ca ) || !g.Cells.TryGetValue( b, out var cb ) ) continue;
			if ( ca.Kind != CellKind.Floor || cb.Kind != CellKind.Floor ) continue;
			report.Walls++;

			var ta = ca.Tier;
			var tb = cb.Tier;
			// the interior walls of one hall stay up: a hole in those is a hole in a room, not a new route
			if ( ta == null || tb == null || ta == tb ) { report.SameTier++; continue; }
			// the secret room has exactly one way in and it is the tell
			if ( ta.Tag == TierTag.Secret || tb.Tag == TierTag.Secret ) { report.Secret++; continue; }
			// they already have a doorway: a second hole between the same two rooms is a wider door, not a discovery
			if ( L.Links.Contains( Layout.LinkKey( ta, tb ) ) ) { report.Linked++; continue; }

			var d = b.x > a.x ? Dir.E : Dir.N;
			var line = Walls.LineOf( a.x, a.z, d );
			if ( lineLevels.TryGetValue( line, out var levels ) && (levels.Contains( a.l - 1 ) || levels.Contains( a.l + 1 )) ) { report.MixedLine++; continue; }

			eligible.Add( (a, b, ta, tb, d, line) );
		}
		report.Eligible = eligible.Count;
		if ( eligible.Count == 0 ) return (breaches, removed);

		// ---- choose. Shuffle draws exactly eligible.Count - 1 numbers, so the stream stays in step whatever
		// the target turns out to be.
		rng.Shuffle( eligible );
		int want = Math.Min( rng.RandInt( Math.Min( min, max ), max ), eligible.Count );
		report.Wanted = want;

		var usedPairs = new HashSet<(int, int)>();
		var usedLines = new HashSet<(WallLine line, int level)>();
		foreach ( var (a, b, ta, tb, d, line) in eligible )
		{
			if ( breaches.Count >= want ) break;
			// one hole per pair of rooms and one per wall plane; both only knowable as we go
			var pair = Layout.LinkKey( ta, tb );
			if ( !usedPairs.Add( pair ) ) { report.SamePair++; continue; }
			if ( !usedLines.Add( (line, a.l) ) ) { report.SameLine++; continue; }

			// how far the walk between these two cells is today: that is exactly what the hole would erase.
			// Measured on the LIVE graph, so two breaches cannot compound into one big shortcut.
			var dist = g.DistancesFrom( a );
			if ( !dist.TryGetValue( b, out var steps ) || steps > MaxDetour ) { report.TooFar++; continue; }

			g.IWalls.Remove( Grid.Edge( a, b ) );
			// Decor.AssignRoles builds its "don't repeat a neighbour's role" adjacency from Links; without
			// this, two rooms joined by a breach can both roll Crypt. Nothing else reads Links by now.
			L.Links.Add( pair );
			removed.Add( new EdgePiece( a.x, a.z, a.l, d, EdgeKind.Wall ) );
			removed.Add( new EdgePiece( b.x, b.z, b.l, d.Opp(), EdgeKind.Wall ) );

			// One scale for both stubs plus a small skew: rolling them independently puts two visibly
			// different brick widths facing each other across the hole.
			double s = ScaleMin + rng.NextDouble() * (ScaleMax - ScaleMin);
			double skew = (rng.NextDouble() * 2 - 1) * SkewMax;
			int seed = DungeonRng.Mix( DungeonRng.Mix( a.x, a.z ), DungeonRng.Mix( a.l, (int)d ) );
			breaches.Add( new Breach( a.x, a.z, a.l, d, (float)(s * (1 + skew)), (float)(s * (1 - skew)), seed ) );
			report.Opened++;
		}
		return (breaches, removed);
	}
}
