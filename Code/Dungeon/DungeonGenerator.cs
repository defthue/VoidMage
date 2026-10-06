using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>A finished floor: the grid plus everything derived from it.</summary>
public sealed class DungeonLayout
{
	public Grid Grid;
	public Layout Layout;
	public List<EdgePiece> Edges;
	public List<Pillar> Pillars;
	/// <summary>Ceiling slabs (x, z, level): the level is where the slab hangs from, two above the floor.</summary>
	public List<(int x, int z, int l)> Ceilings;
	/// <summary>Doorways: the two same-level cells each opening joins.</summary>
	public List<((int x, int z, int l) a, (int x, int z, int l) b)> Doors;
	public int Attempt;
	public Archetype Archetype;
	public int Seed;
	/// <summary>Host-side cell and direction of the secret room's tell wall, if a secret room was placed.</summary>
	public (int x, int z, int l, Dir d)? SecretWall;
	/// <summary>Props, wall swaps, floor swaps and lights (null when generated without decoration).</summary>
	public DecorResult Decor;
	/// <summary>How this floor is dressed. Mossy floors get the green atlas, greenery and breached walls.</summary>
	public DungeonTheme Theme;
	/// <summary>Walls that have fallen in (mossy floors only); empty otherwise.</summary>
	public List<Breach> Breaches = new();
	/// <summary>Why the candidate walls were turned down: a floor with no breaches can say what happened.</summary>
	public BreachReport BreachStats;

	public IEnumerable<Tier> Tagged => Grid.Tiers.Where( t => t.Tag != TierTag.None && t.Cells.Count > 0 );

	public Tier Find( TierTag tag ) => Grid.Tiers.FirstOrDefault( t => t.Tag == tag && t.Cells.Count > 0 );

	public string Stats()
	{
		var g = Grid;
		var (x0, x1, z0, z1) = g.Bounds();
		var levels = g.Cells.Keys.Select( k => k.l ).Distinct().OrderBy( l => l ).ToList();
		var perLevel = string.Join( " ", levels.Select( l => $"L{l}:{g.Cells.Keys.Count( k => k.l == l )}" ) );
		var tags = string.Join( " ", Tagged.Select( t => $"{t.Tag}={t.Kind}@L{t.Level}" ) );
		// corridors with a single way in: the ones a player walks down for nothing
		int deadCorridors = g.Tiers.Count( t => t.Kind == TierKind.Corridor && t.Cells.Count > 0 && t.Tag != TierTag.Secret && VoidMage.Dungeon.Layout.NLinks( g, t ) <= 1 );
		return $"{Archetype} {Theme} seed {Seed} (attempt {Attempt}): {g.Cells.Count} cells, {g.Tiers.Count} tiers, spine {Layout.Spine.Count}, " +
			$"{g.Stairs.Count} stairs, {Doors.Count} doorways, {Breaches.Count} breaches, {deadCorridors} dead-end corridors, footprint {(x1 - x0 + 1) * 5}x{(z1 - z0 + 1) * 5} m, " +
			$"{Edges.Count} walls, {Ceilings.Count} ceilings, {Pillars.Count} pillars | {perLevel} | {tags}" +
			(Theme == DungeonTheme.Mossy && BreachStats != null ? $"\n[Dungeon] breaches: {BreachStats}" : "");
	}
}

/// <summary>
/// Driver (dungeon_genv2.py): spine + spurs -> wall off unlinked tiers -> connectivity check -> secret
/// room -> tags -> derived walls, pillars and ceilings -> decoration. Retries with a new sub-seed until
/// the spine places and connects. Decoration uses its own RNG streams, so the layout of a seed never
/// changes when the decoration tables do.
/// </summary>
public static class DungeonGenerator
{
	/// <summary>
	/// Whether a floor gets its walled-off secret room. Off (user, 2026-09-20: "should remove the secret room, we dont
	/// have enough usecases for it and we dont want the player to miss anything in their run"). The room was placed from
	/// its own rng stream after the layout was settled, so switching it off moves nothing else; DecorSecret, the tell
	/// wall and the break-in (Combat.SecretWall) stay compiled behind this.
	/// </summary>
	public static bool SecretRooms { get; set; } = false;

	/// <param name="depth">Floor number in a run (1 = first); drives role weights and decay.</param>
	/// <param name="lightBudget">Most point lights on the floor; the rest of the torches stay unlit.</param>
	/// <param name="traps">Trap count and kinds; null = the depth default (TrapBudget.ForDepth).</param>
	/// <param name="theme">Stone or Mossy. Mossy adds the greenery pass and the breached walls; the grammar is the same either way.</param>
	/// <param name="minBreaches">Mossy only: fewest walls to bring down (capped by how many are eligible).</param>
	/// <param name="maxBreaches">Mossy only: most walls to bring down.</param>
	/// <param name="greenery">Mossy only: multiplier on how thickly the greenery grows; 1 is the tuned default.</param>
	/// <param name="roots">Mossy only: the same for the root mats, which have a dial of their own.</param>
	/// <param name="rooms">Grammar.Generate's rooms factor: how many tiers the floor has, 1 = the classic floor.</param>
	public static DungeonLayout Generate( Archetype archetype, int seed, double size = 1.5, int depth = 1, int lightBudget = 104, bool decorate = true, int maxAttempts = 80, TrapBudget traps = null,
		DungeonTheme theme = DungeonTheme.Stone, int minBreaches = 2, int maxBreaches = 4, double greenery = 1.0, double roots = 1.0, double rooms = 1.0 )
	{
		for ( int attempt = 0; attempt < maxAttempts; attempt++ )
		{
			var rng = new DungeonRng( unchecked(seed * 1000 + attempt) );
			Layout L;
			try
			{
				L = Grammar.Generate( archetype, rng, size, rooms );
			}
			catch ( Exception e )
			{
				// A layout that paints itself into a corner is a retry, not a crash.
				Log.Warning( $"[Dungeon] {archetype} seed {seed} attempt {attempt} threw: {e.Message}" );
				continue;
			}
			if ( L == null ) continue;
			L.WallOffUnlinked( rng );
			if ( !L.G.Connected() ) continue;
			// Connected() passes a stair that hangs off a tier by one end; the player walks off the world at the other
			if ( L.G.DanglingStairs().Count > 0 )
			{
				Log.Warning( $"[Dungeon] {archetype} seed {seed} attempt {attempt} left a stair leading nowhere, retrying" );
				continue;
			}
			// the secret room is deliberately disconnected: placed after the check, never checked again
			var secret = decorate && SecretRooms ? DecorSecret.Place( L, new DungeonRng( DungeonRng.Mix( seed, 0x5EC0 ) ) ) : null;
			L.TagSpecial( rng );
			// breaches only ever ADD routes, and they run after the connectivity check and the tags, so no
			// seed's layout or tagging changes because of them
			var breachStats = new BreachReport();
			var (breaches, breachEdges) = theme == DungeonTheme.Mossy
				? DungeonBreach.Open( L, new DungeonRng( DungeonRng.Mix( seed, 0xB2EA ) ), minBreaches, maxBreaches, breachStats )
				: (new List<Breach>(), new List<EdgePiece>());
			var edges = Derive.EdgesEnclosed( L.G );
			// Supports uses the wall list to SUPPRESS a pillar where a wall from below already holds the
			// vertex; a breached wall still holds its two end vertices, so it has to stay visible here or a
			// pillar grows in the middle of the hole. layout.Edges keeps the real list, without them.
			var pillars = Derive.Supports( L.G, breachEdges.Count == 0 ? edges : edges.Concat( breachEdges ).ToList() );
			var layout = new DungeonLayout
			{
				Grid = L.G, Layout = L, Edges = edges, Pillars = pillars, Ceilings = L.Ceilings(), Doors = L.Doors,
				Attempt = attempt, Archetype = archetype, Seed = seed, SecretWall = secret,
				Theme = theme, Breaches = breaches, BreachStats = breachStats,
			};
			if ( decorate ) layout.Decor = Decor.Run( layout, depth, lightBudget, traps, theme, greenery, roots );
			return layout;
		}
		throw new InvalidOperationException( $"could not generate a valid {archetype} layout for seed {seed}" );
	}
}
