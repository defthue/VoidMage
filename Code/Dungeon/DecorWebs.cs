using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// Cobwebs in the ceiling corners, every biome (user). One roll per room corner under a ceiling slab, likelier deep
/// down; the web hangs from the kit's WebTop on whichever of the corner's two walls has the corner at its LEFT end,
/// because the sheet only spreads to the right of its pivot (see the web_corner_high def). A wall face that already
/// carries a mounted prop (a banner, a sigil, a root mat) or a trap head is left alone. Runs dead last, on its own
/// streams, and claims nothing.
/// </summary>
public static class DecorWebs
{
	public const string CornerId = "web_corner_high";
	/// <summary>How far the flipped sheet reaches above its mount, metres per unit of scale (the model's z -0.28).</summary>
	private const float Rise = 0.28f;
	/// <summary>How far behind the pivot the flipped sheet starts, metres per unit of scale (the model's x -0.76): the
	/// embed that brings the pivot to PivotOffWall from the wall face.</summary>
	private const float Behind = 0.76f;
	private const float PivotOffWall = 0.1f;
	/// <summary>The pivot along the wall, cell units from the middle of the 5 m face: 2.5 m less the side wall's half
	/// thickness and a hand's width.</summary>
	private const float CornerS = 0.426f;

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs )
	{
		var kit = res.Kit;
		if ( kit.WebTop <= 0f || !kit.Props.TryGetValue( CornerId, out var def ) ) return;
		var wallSet = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var e in lay.Edges ) if ( e.Kind == EdgeKind.Wall ) wallSet.Add( (e.X, e.Z, e.Level, e.D) );
		var slabs = lay.Ceilings.ToHashSet();
		var busy = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var p in res.Props ) if ( p.Anchor == AnchorKind.WallMounted && p.Light == LightKind.None ) busy.Add( (p.X, p.Z, p.Level, p.Edge) );
		foreach ( var t in res.Traps ) busy.Add( (t.WallX, t.WallZ, t.Level, t.WallD) );

		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			if ( ctx.T.Kind == TierKind.Tower ) continue;
			int L = ctx.T.Level;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0x3EB5 ) ) );
			double chance = (ctx.Role == DecorRole.None ? 0.2 : 0.3) + 0.35 * ctx.DepthScore;
			foreach ( var s in ctx.Corners )
			{
				if ( rng.NextDouble() >= chance ) continue;
				if ( !slabs.Contains( (s.X, s.Z, L + 2) ) ) continue;
				var wall = LeftOf( s.D ) == s.D2 ? s.D : s.D2;
				if ( !wallSet.Contains( (s.X, s.Z, L, wall) ) || busy.Contains( (s.X, s.Z, L, wall) ) ) continue;
				if ( DecorSlots.IsSecretWall( lay, s.X, s.Z, L, wall ) ) continue;
				float sc = 0.85f + 0.45f * (float)rng.NextDouble();
				busy.Add( (s.X, s.Z, L, wall) );
				Decor.Add( res, ctx, def, rng, AnchorKind.WallMounted, s.X, s.Z, L, wall, s.X + 0.5f, s.Z + 0.5f, CornerS,
					kit.WebTop - Rise * sc, Decor.YawOf( wall.Opp() ), 3, scale: sc, embed: Behind * sc - PivotOffWall );
			}
		}
	}

	/// <summary>The side a wall prop's left hand points to, standing on wall `d` facing into the room (yaw of d.Opp()).</summary>
	private static Dir LeftOf( Dir d ) => d switch { Dir.N => Dir.E, Dir.S => Dir.W, Dir.E => Dir.S, _ => Dir.N };
}
