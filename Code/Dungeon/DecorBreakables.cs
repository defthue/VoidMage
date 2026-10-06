using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The breakables pass: crates, barrels and vases after everything else is placed, so the room's own furniture
/// keeps the best spots. Corners first (a stack in a corner reads as storage), then the wall halves, through the
/// same lane and occupancy rules as the static fill; crates stack 40% of the time, and a vase comes as a cluster
/// of 1-5 (mostly 1-2) huddled along the wall. Spread through the whole dungeon, storage rooms packed, the spawn
/// room empty. Engine-free and deterministic like the rest of Decor: its own RNG stream per tier.
/// </summary>
public static class DecorBreakables
{
	/// <summary>How many vases stand together when one is placed: 1 to 5, weighted toward the low end.</summary>
	private static readonly double[] ClusterWeights = { 40, 28, 17, 10, 5 };

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ )
	{
		var defs = new[] { res.Kit.Props["crate"], res.Kit.Props["barrel"], res.Kit.Props["vase"] };
		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			var t = ctx.T;
			if ( t.Cells.Count == 0 || t.Tag == TierTag.Spawn ) continue;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( t.Id, 0xB4EA ) ) );
			var (want, weights) = Plan( ctx, rng );
			if ( want <= 0 ) continue;
			var placed = 0;

			var corners = ctx.Corners.ToList();
			rng.Shuffle( corners );
			foreach ( var s in corners )
			{
				if ( placed >= want ) break;
				if ( !ctx.Clear( s.X, s.Z ) || rng.NextDouble() > 0.6 ) continue;
				var def = rng.Choices( defs, weights );
				var before = res.Props.Count;
				Decor.PlaceCorner( ctx, res, occ, rng, s, def, 3 );
				if ( res.Props.Count == before ) continue;
				placed++;
				Companions( ctx, res, occ, rng, def, before );
			}

			var halves = new List<(Slot s, int half)>();
			foreach ( var run in ctx.Runs )
				foreach ( var s in run.Slots ) { halves.Add( (s, 0) ); halves.Add( (s, 1) ); }
			rng.Shuffle( halves );
			foreach ( var (s, half) in halves )
			{
				if ( placed >= want ) break;
				var def = rng.Choices( defs, weights );
				var before = res.Props.Count;
				if ( !Decor.TryPlaceInHalf( ctx, res, occ, rng, s, half, def ) ) continue;
				placed++;
				Companions( ctx, res, occ, rng, def, before );
			}
		}

		AroundPillars( lay, res, ctxs, defs );
	}

	/// <summary>
	/// Pottery huddled round the free-standing pillars of a hall (user: the rooms fill up better). The four quarters
	/// around an interior pillar are claimed for the column itself (DecorSlots.Build), so nothing else ever stands
	/// there, and every piece here is placed on a ring just outside the shaft, well inside those quarters. Mostly
	/// vases; a barrel or a crate only where the cell is off the walking line (they block). Own streams, after
	/// everything else in this pass, so the rest of the floor is unchanged.
	/// </summary>
	private static void AroundPillars( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, PropDef[] defs )
	{
		float shaft = res.Kit.InteriorPillarRadius;
		var byCell = new Dictionary<(int x, int z, int l), TierCtx>();
		foreach ( var ctx in ctxs.Values ) foreach ( var c in ctx.T.Cells ) byCell[(c.x, c.z, ctx.T.Level)] = ctx;
		foreach ( var p in lay.Pillars.Where( p => p.Interior ).OrderBy( p => (p.TopLevel, p.VX, p.VZ) ) )
		{
			// the tier that stands round it: all four cells at the pillar's floor in one tier, and never the spawn room
			if ( !byCell.TryGetValue( (p.VX, p.VZ, p.TopLevel), out var ctx ) || ctx.T.Tag == TierTag.Spawn ) continue;
			var around = new[] { (p.VX - 1, p.VZ - 1), (p.VX, p.VZ - 1), (p.VX - 1, p.VZ), (p.VX, p.VZ) };
			if ( !around.All( c => ctx.Has( c.Item1, c.Item2 ) ) ) continue;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( p.VX * 7919 + p.VZ * 104729 + p.TopLevel, 0x9111 ) ) );
			if ( rng.NextDouble() > PillarChance ) continue;
			int count = rng.Choices( new[] { 1, 2, 3, 4 }, PillarCountWeights );
			float angle = rng.RandRange( 360 );
			int L = p.TopLevel;
			for ( int n = 0; n < count; n++ )
			{
				angle += 55f + rng.RandRange( 50 );
				var def = rng.NextDouble() < 0.2 ? defs[rng.RandRange( 2 )] : defs[2];
				float half = MathF.Max( def.Width, def.Depth ) * (def.Id == "crate" ? 0.71f : 0.5f);
				float r = (shaft + half + 0.08f + (float)rng.NextDouble() * 0.12f) / 5f;
				float rad = angle * MathF.PI / 180f;
				float gx = p.VX + MathF.Cos( rad ) * r, gz = p.VZ + MathF.Sin( rad ) * r;
				int cx = (int)Math.Floor( gx ), cz = (int)Math.Floor( gz );
				if ( !ctx.Has( cx, cz ) || res.TrapCells.Contains( (cx, cz, L) ) ) continue;
				if ( def.Blocks && !ctx.Clear( cx, cz ) ) def = defs[2];
				if ( def.Blocks && !ctx.Clear( cx, cz ) ) continue;
				Decor.Add( res, ctx, def, rng, AnchorKind.Floor, cx, cz, L, Dir.N, gx, gz, 0, 0, rng.RandRange( 360 ), 3 );
			}
		}
	}

	/// <summary>Share of interior pillars that get pottery at their foot, and how many pieces (1-4, mostly 1-2).</summary>
	private const double PillarChance = 0.6;
	private static readonly double[] PillarCountWeights = { 35, 35, 20, 10 };

	/// <summary>How many a tier wants and the crate/barrel/vase weights, by role.</summary>
	private static (int want, double[] weights) Plan( TierCtx ctx, DungeonRng rng )
	{
		var t = ctx.T;
		int want;
		double[] w;
		switch ( ctx.Role )
		{
			case DecorRole.Storage: want = 10 + rng.RandRange( 7 ); w = new[] { 4.0, 4.0, 3.0 }; break;
			case DecorRole.Secret: want = 4 + rng.RandRange( 4 ); w = new[] { 3.0, 3.0, 3.0 }; break;
			case DecorRole.Alchemy:
			case DecorRole.Library:
			case DecorRole.Shrine:
			case DecorRole.Crypt: want = 3 + rng.RandRange( 4 ); w = new[] { 1.0, 1.0, 4.0 }; break;
			case DecorRole.MessHall:
			case DecorRole.Barracks: want = 3 + rng.RandRange( 4 ); w = new[] { 2.0, 4.0, 2.0 }; break;
			case DecorRole.Armory:
			case DecorRole.Torture:
			case DecorRole.Throne: want = 2 + rng.RandRange( 3 ); w = new[] { 3.0, 2.0, 2.0 }; break;
			default:
				w = new[] { 3.0, 3.0, 3.0 };
				if ( t.Kind == TierKind.Corridor )
				{
					want = t.Cells.Count / 5;
					if ( rng.NextDouble() < (t.Cells.Count % 5) / 5.0 ) want++;
				}
				else want = 1 + rng.RandRange( 2 );
				break;
		}
		if ( t.Kind == TierKind.Hall ) want += 3;
		return (want, w);
	}

	/// <summary>What comes with the prop just placed: a second crate on top, or a huddle of vases beside a vase.</summary>
	private static void Companions( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, PropDef def, int from )
	{
		PropPlacement? found = null;
		for ( var i = from; i < res.Props.Count; i++ )
			if ( res.Props[i].Prop == def.Id ) { found = res.Props[i]; break; }
		if ( found is not { } p ) return;

		if ( def.Id == "crate" )
		{
			if ( def.Top <= 0 || rng.NextDouble() > 0.4 ) return;
			Decor.Add( res, ctx, def, rng, AnchorKind.Floor, p.X, p.Z, p.Level, p.Edge, p.GX, p.GZ, p.S, def.Top, p.Yaw + rng.RandInt( -35, 35 ), 3 );
			return;
		}

		if ( def.Id != "vase" ) return;
		var extra = rng.Choices( new[] { 0, 1, 2, 3, 4 }, ClusterWeights );
		if ( extra == 0 ) return;
		// huddled along the wall the vase faces away from: alternating sides, a little further out each time,
		// a touch of inward jitter; only where the room is and the quarter is not somebody's furniture
		float rad = p.Yaw * MathF.PI / 180f;
		float fx = MathF.Cos( rad ), fz = MathF.Sin( rad );
		float ax = -fz, az = fx;
		int myCell = 0;
		int L = p.Level, cx0 = (int)Math.Floor( p.GX ), cz0 = (int)Math.Floor( p.GZ );
		int q0 = Occupancy.QuarterOf( p.GX, p.GZ, cx0, cz0 );
		_ = myCell;
		for ( var n = 0; n < extra; n++ )
		{
			int side = n % 2 == 0 ? 1 : -1;
			float dist = (0.13f + 0.11f * (n / 2)) * side + (float)(rng.NextDouble() - 0.5) * 0.04f;
			float inward = (float)rng.NextDouble() * 0.08f;
			float gx = p.GX + ax * dist + fx * inward, gz = p.GZ + az * dist + fz * inward;
			int cx = (int)Math.Floor( gx ), cz = (int)Math.Floor( gz );
			if ( !ctx.Has( cx, cz ) ) continue;
			int q = Occupancy.QuarterOf( gx, gz, cx, cz );
			bool own = (cx, cz) == (cx0, cz0) && q == q0;
			if ( !own && !occ.QuarterFree( cx, cz, L, q ) ) continue;
			Decor.Add( res, ctx, def, rng, AnchorKind.Floor, cx, cz, L, p.Edge, gx, gz, p.S, 0, rng.RandRange( 360 ), 3 );
		}
	}
}
