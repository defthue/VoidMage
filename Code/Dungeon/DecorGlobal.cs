using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The global pass: torches at stairs, doors and along corridors (the dungeon's light), hallway clutter,
/// the secret wall, decay, each room's key light, and the light budget. Runs on every tier including
/// corridors and landings.
/// </summary>
public static class DecorGlobal
{
	private const int PriorityStair = 0, PriorityDoor = 1, PriorityCorridor = 2, PriorityUnlit = 9;
	/// <summary>Share of dressed rooms whose best light is guaranteed a place in the budget.</summary>
	private const double KeyLightChance = 0.5;

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ, DungeonRng rng, int lightBudget,
		DungeonTheme theme = DungeonTheme.Stone, double greenery = 1.0 )
	{
		var g = lay.Grid;
		var wallSet = new HashSet<(int x, int z, int l, Dir d)>();
		foreach ( var e in lay.Edges ) if ( e.Kind == EdgeKind.Wall ) wallSet.Add( (e.X, e.Z, e.Level, e.D) );
		var torch = res.Kit.Props["torch"];
		var byCell = new Dictionary<(int x, int z, int l), TierCtx>();
		foreach ( var ctx in ctxs.Values ) foreach ( var c in ctx.T.Cells ) byCell[(c.x, c.z, ctx.T.Level)] = ctx;

		bool TryTorch( int x, int z, int l, Dir d, int priority )
		{
			if ( !wallSet.Contains( (x, z, l, d) ) ) return false;
			if ( !occ.WallFree( x, z, l, d ) || occ.BandHeight( x, z, l, d ) > 1.3f ) return false;
			// no torch on the neighbouring segments of the same wall
			var (ax, az) = d.Turn()[0].Step( x, z );
			var (bx, bz) = d.Turn()[1].Step( x, z );
			if ( !occ.FaceFree( ax, az, l, d, WallLayer.Mount ) || !occ.FaceFree( bx, bz, l, d, WallLayer.Mount ) ) return false;
			occ.ClaimWall( x, z, l, d );
			var ctx = byCell.TryGetValue( (x, z, l), out var c ) ? c : null;
			var stub = ctx ?? new TierCtx { T = new Tier( -1, Array.Empty<(int, int)>(), l, TierKind.Corridor ), DepthScore = DecorSlots.DepthScore( res.Depth, l ) };
			Decor.Add( res, stub, torch, rng, AnchorKind.WallMounted, x, z, l, d, x + 0.5f, z + 0.5f, 0, torch.MountHeight, Decor.YawOf( d.Opp() ), priority );
			return true;
		}

		// 1. stairs: a torch beside the foot of every run, and one on every landing
		foreach ( var (x, z, l, d) in g.Stairs.OrderBy( s => s ) )
		{
			int bx = x - d.Dx(), bz = z - d.Dz();
			if ( !TryTorch( bx, bz, l, d.Turn()[0], PriorityStair ) ) TryTorch( bx, bz, l, d.Turn()[1], PriorityStair );
		}
		foreach ( var key in g.SortedKeys() )
		{
			var c = g.Cells[key];
			if ( c.Kind != CellKind.Landing ) continue;
			var dirs = key.l % 2 == 0 ? Dirs.All : Dirs.All.Reverse().ToArray();
			foreach ( var d in dirs ) if ( TryTorch( key.x, key.z, key.l, d, PriorityStair ) ) break;
		}

		// 2. doors: 30% get a torch beside the frame
		foreach ( var (a, b) in lay.Doors )
		{
			var d = b.x > a.x ? Dir.E : Dir.N;
			if ( rng.NextDouble() >= 0.3 ) continue;
			if ( !TryTorch( a.x, a.z, a.l, d.Turn()[0], PriorityDoor ) ) TryTorch( b.x, b.z, b.l, d.Turn()[1], PriorityDoor );
		}

		// 3. corridors: every 3 cells on the spine, 4 on spurs, opposite phase on the opposite wall
		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			var t = ctx.T;
			if ( t.Kind != TierKind.Corridor && ctx.Role != DecorRole.None ) continue;
			int period = t.Spur ? 4 : 3;
			foreach ( var run in ctx.Runs )
			{
				int phase = run.D is Dir.N or Dir.E ? 0 : 1;
				for ( int i = 0; i < run.Slots.Count; i++ )
				{
					if ( (i + phase) % period != 0 ) continue;
					var s = run.Slots[i];
					TryTorch( s.X, s.Z, s.Level, s.D, PriorityCorridor );
				}
			}
		}

		// 4. the secret wall: the reserved piece facing the host, a plain single-sided wall inside
		if ( lay.SecretWall.HasValue )
		{
			var sw = lay.SecretWall.Value;
			var line = Walls.LineOf( sw.x, sw.z, sw.d );
			foreach ( var seg in Walls.Merge( lay.Edges ) )
			{
				if ( seg.Line != line || seg.K != sw.l || seg.Kind != SegKind.Full ) continue;
				bool hostSide = (seg.CX, seg.CZ) == (sw.x, sw.z);
				// A kit whose tell is finished on both faces (Realms) gets the one piece and nothing behind it; it
				// still stands on the HOST side's transform so its forward points down the corridor the player
				// comes from (the burst goes that way). A null Model means "spawn nothing on this face".
				if ( res.Kit.DoubleFaced )
					res.WallSwaps.Add( hostSide
						? new WallSwap( seg, res.Kit.SecretWallModel, null, null, false, false, false )
						: new WallSwap( seg, null, null, res.Kit.SecretWallModel, false, false, false ) );
				else
					res.WallSwaps.Add( hostSide
						? new WallSwap( seg, res.Kit.SecretWallModel, null, res.Kit.PlainWallModel, false, false, false )
						: new WallSwap( seg, res.Kit.PlainWallModel, null, res.Kit.SecretWallModel, false, false, false ) );
				break;
			}
		}

		// 4b. the kit's wall sigils (the forge-hold's hell symbols): a couple per floor, on wall faces with nothing
		// mounted and nothing taller than a metre standing in front (the sigil hangs from 0.85 m up), rooms first
		// and corridors when the rooms are too full, one per tier, never the secret wall. Own stream, so a kit
		// without any draws nothing.
		if ( res.Kit.SigilProp != null && res.Kit.Props.TryGetValue( res.Kit.SigilProp, out var sigil ) )
		{
			var srng = new DungeonRng( DungeonRng.Mix( lay.Seed, 0x5161 ) );
			var rooms = new List<(TierCtx ctx, Slot s)>();
			var halls = new List<(TierCtx ctx, Slot s)>();
			foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
			{
				if ( ctx.T.Kind == TierKind.Tower ) continue;
				int l = ctx.T.Level;
				foreach ( var s in ctx.WallSlots )
				{
					if ( s.NearDoor || !wallSet.Contains( (s.X, s.Z, l, s.D) ) ) continue;
					if ( !occ.WallFree( s.X, s.Z, l, s.D ) || occ.BandHeight( s.X, s.Z, l, s.D ) > 1.0f ) continue;
					if ( DecorSlots.IsSecretWall( lay, s.X, s.Z, l, s.D ) ) continue;
					(ctx.Role != DecorRole.None ? rooms : halls).Add( (ctx, s) );
				}
			}
			srng.Shuffle( rooms );
			srng.Shuffle( halls );
			int want = srng.RandInt( res.Kit.SigilsMin, res.Kit.SigilsMax );
			var used = new HashSet<int>();
			foreach ( var (ctx, s) in rooms.Concat( halls ) )
			{
				if ( want <= 0 ) break;
				if ( !used.Add( ctx.T.Id ) ) continue;
				int l = ctx.T.Level;
				if ( !occ.WallFree( s.X, s.Z, l, s.D ) ) continue;
				occ.ClaimWall( s.X, s.Z, l, s.D );
				Decor.Add( res, ctx, sigil, srng, AnchorKind.WallMounted, s.X, s.Z, l, s.D, s.X + 0.5f, s.Z + 0.5f, 0, sigil.MountHeight, Decor.YawOf( s.D.Opp() ), 3 );
				want--;
			}
		}

		// 5. hallway clutter, denser with depth; the doc's rubble/bones/odd crate, never on the walking line
		var hallway = res.Kit.HallwayClutter ?? DecorCatalog.HallwayClutter;
		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			if ( ctx.Role != DecorRole.None || ctx.T.Kind == TierKind.Tower ) continue;
			var crng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0xC1A7 ) ) );
			double density = 0.15 + 0.25 * ctx.DepthScore;
			var entries = ctx.DepthScore > 0.4 ? hallway.Concat( DecorCatalog.DecayClutter ).ToArray() : hallway;
			foreach ( var s in ctx.Corners )
			{
				if ( !ctx.Clear( s.X, s.Z ) || crng.NextDouble() >= density ) continue;
				var id = Decor.Pick( crng, entries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( def.Blocks && ctx.Walk.Contains( (s.X, s.Z) ) ) continue;
				Decor.PlaceCorner( ctx, res, occ, crng, s, def, 3 );
			}
			Decor.Scatter( ctx, res, occ, crng, entries, density * 0.5, 0 );
			// rubble underfoot in the corridors as well, thicker the deeper you go
			Decor.Litter( ctx, res, occ, crng, res.Kit.HallwayLitter ?? DecorCatalog.Debris, (0.2 + 0.2 * ctx.DepthScore) * res.Kit.LitterScale );
			// 5a. the gaol hangs its cages and chains in the corridors too: its own stream, so a kit without the
			// list draws nothing and everything else here stays byte-identical
			if ( res.Kit.HallwayCeiling is { } hung )
			{
				var hrng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0xCE11 ) ) );
				Decor.FillCeiling( ctx, res, occ, hrng, hung, 0.3, 0, firstAlways: false, PriorityCorridor );
			}
		}

		// 5b. the mossy floors' greenery. Here on purpose: after every torch, trap and piece of furniture has
		// claimed its slots, so occ.WallFree and occ.TileFree mean something. It draws from its own per-tier
		// streams, so the rest of this pass is unchanged byte for byte on a stone floor. (The root mats are NOT
		// part of this — they run dead last, after the breakables, from Decor.Run.)
		if ( theme == DungeonTheme.Mossy ) DecorGreenery.Run( lay, res, ctxs, occ, greenery );

		// 6. decay: deep torches sometimes burnt out
		for ( int i = 0; i < res.Props.Count; i++ )
		{
			var p = res.Props[i];
			if ( p.Light != LightKind.Torch || p.Priority < PriorityCorridor ) continue;
			double ds = DecorSlots.DepthScore( res.Depth, p.Level );
			if ( rng.NextDouble() < 0.3 * ds ) res.Props[i] = p with { Priority = PriorityUnlit };
		}

		// 7. about half the dressed rooms keep their key light (chandelier before brazier before torch
		// before candle) ahead of the corridor torches; the rest are lit only if the budget reaches them,
		// so roughly 60% of rooms end up with a light and the others stay dark
		static int Rank( LightKind k ) => k switch { LightKind.Chandelier => 0, LightKind.Brazier => 1, LightKind.Glow => 2, LightKind.Torch => 3, _ => 4 };
		foreach ( var ctx in ctxs.Values.Where( c => c.Role != DecorRole.None ).OrderBy( c => c.T.Id ) )
		{
			if ( rng.NextDouble() >= KeyLightChance ) continue;
			int best = -1;
			for ( int i = 0; i < res.Props.Count; i++ )
			{
				var p = res.Props[i];
				if ( p.TierId != ctx.T.Id || p.Light == LightKind.None || p.Priority == PriorityUnlit ) continue;
				if ( best < 0 || Rank( p.Light ) < Rank( res.Props[best].Light ) ) best = i;
			}
			if ( best >= 0 ) res.Props[best] = res.Props[best] with { Priority = PriorityDoor };
		}

		// 8. light budget: stairs, doors and room key lights, corridors, rooms, then the rest; top of the floor first
		var order = res.Props.Select( ( p, i ) => (p, i) ).Where( x => x.p.Light != LightKind.None )
			.OrderBy( x => x.p.Priority ).ThenByDescending( x => x.p.Level ).ThenBy( x => x.p.GX ).ThenBy( x => x.p.GZ ).ThenBy( x => x.i ).ToList();
		for ( int n = 0; n < order.Count; n++ )
		{
			var (p, i) = order[n];
			if ( n < lightBudget && p.Priority != PriorityUnlit ) res.Props[i] = p with { Lit = true };
		}
	}
}
