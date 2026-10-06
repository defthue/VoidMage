using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The decoration stage: roles, then per-tier fill from the role tables, then the global pass.
/// Engine-free and deterministic: its own RNG streams (never the layout's), sorted iteration only.
/// Yaw convention matches the builder: 0 faces +X (grid east), 90 faces +Y (grid north).
/// </summary>
public static class Decor
{
	/// <param name="traps">Trap count and kinds for this floor; null = the depth default. Traps claim their cells before any room is dressed.</param>
	/// <param name="theme">Mossy adds the greenery pass inside the global pass; everything else is identical.</param>
	public static DecorResult Run( DungeonLayout lay, int depth, int lightBudget, TrapBudget traps = null, DungeonTheme theme = DungeonTheme.Stone,
		double greenery = 1.0, double roots = 1.0 )
	{
		// the kit goes in before the first roll: the banner roll below already reads it
		var res = new DecorResult { Depth = depth, SecretWall = lay.SecretWall, Kit = DungeonKit.For( theme ) };
		var floorRng = new DungeonRng( DungeonRng.Mix( lay.Seed, 0x5EC1 ) );
		res.BannerVariant = floorRng.RandRange( res.Kit.Banners.Length );
		var occ = new Occupancy();
		var ctxs = DecorSlots.Build( lay, res, occ, depth );

		AssignRoles( lay, ctxs, floorRng, depth );
		DecorTraps.Run( lay, res, ctxs, occ, traps ?? TrapBudget.ForDepth( depth ), new DungeonRng( DungeonRng.Mix( lay.Seed, 0x7A4B ) ) );
		foreach ( var kv in ctxs.OrderBy( kv => kv.Key ) )
		{
			var ctx = kv.Value;
			res.Roles[ctx.T.Id] = ctx.Role;
			if ( ctx.Role == DecorRole.None ) continue;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0xDEC0 ) ) );
			Fill( lay, ctx, res, occ, rng );
		}
		PlaceExitProp( lay, ctxs, res, occ );
		DecorGlobal.Run( lay, res, ctxs, occ, floorRng, lightBudget, theme, greenery );
		DecorBreakables.Run( lay, res, ctxs, occ );
		// two in three chests go (the secret room keeps its hoard), before the potions pick the ones that stay
		DecorChests.Run( lay, res, ctxs );
		// the consumable potions: on the tops the fill and the breakables left, own RNG stream (older floors keep their layout)
		DecorPotions.Run( lay, res, ctxs, occ );
		// The mossy floors' root mats go LAST of everything, after the breakables. They are the one pass whose
		// rule is "do not touch anything", and they claim nothing — so a crate placed after them would be placed
		// straight through one, and then break into debris inside it.
		if ( theme == DungeonTheme.Mossy ) DecorGreenery.RootPass( lay, res, ctxs, occ, roots );
		// Two passes after even that, both claiming nothing on their own streams, so every floor before them is
		// unchanged: the bones every biome's skeletons leave about, and the cobwebs in the ceiling corners.
		DecorBoneLitter.Run( lay, res, ctxs, occ );
		DecorWebs.Run( lay, res, ctxs );
		return res;
	}

	// ------------------------------------------------------------------ roles

	public static float YawOf( Dir d ) => d switch { Dir.E => 0, Dir.N => 90, Dir.W => 180, _ => 270 };

	private static void AssignRoles( DungeonLayout lay, Dictionary<int, TierCtx> ctxs, DungeonRng rng, int depth )
	{
		var g = lay.Grid;
		bool throne = false, shrine = false;
		var adjacency = new Dictionary<int, HashSet<int>>();
		foreach ( var (a, b) in lay.Layout.Links )
		{
			if ( !adjacency.TryGetValue( a, out var sa ) ) adjacency[a] = sa = new();
			if ( !adjacency.TryGetValue( b, out var sb ) ) adjacency[b] = sb = new();
			sa.Add( b ); sb.Add( a );
		}
		DecorRole RoleOfNeighbour( int id ) => ctxs.TryGetValue( id, out var c ) ? c.Role : DecorRole.None;
		bool Taken( TierCtx ctx, DecorRole r ) => adjacency.TryGetValue( ctx.T.Id, out var ns ) && ns.Any( n => RoleOfNeighbour( n ) == r );

		// largest hall first so the throne room claims the best space
		var halls = ctxs.Values.Where( c => c.T.Kind == TierKind.Hall && c.T.Tag == TierTag.None ).OrderByDescending( c => c.T.Cells.Count ).ThenBy( c => c.T.Id ).ToList();
		if ( depth >= 3 && halls.Count > 0 && halls[0].T.Cells.Count >= 16 ) { halls[0].Role = DecorRole.Throne; throne = true; }

		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			var t = ctx.T;
			if ( ctx.Role != DecorRole.None ) continue;
			switch ( t.Tag )
			{
				case TierTag.Shrine: ctx.Role = shrine ? DecorRole.Storage : DecorRole.Shrine; shrine = true; continue;
				case TierTag.Spawn:
				case TierTag.Exit: ctx.Role = DecorRole.None; continue;
				case TierTag.Secret: ctx.Role = DecorRole.Secret; continue;
				case TierTag.Loot: ctx.Role = rng.NextDouble() < 0.6 ? DecorRole.Storage : DecorRole.Armory; continue;
				case TierTag.Ambush: ctx.Role = rng.NextDouble() < 0.6 ? DecorRole.Crypt : DecorRole.MessHall; continue;
			}
			if ( t.Kind == TierKind.Corridor || t.Kind == TierKind.Tower ) { ctx.Role = DecorRole.None; continue; }
			var pool = t.Kind == TierKind.Hall ? DecorCatalog.HallRoles : DecorCatalog.RoomRoles;
			var weights = pool.Select( r =>
			{
				var tb = DecorCatalog.Tables[r];
				return Math.Max( 0.05, tb.Weight * (1 + tb.DepthBias * (2 * ctx.DepthScore - 1)) );
			} ).ToList();
			var role = rng.Choices( pool, weights );
			for ( int i = 0; i < 3 && Taken( ctx, role ); i++ ) role = rng.Choices( pool, weights );
			ctx.Role = role;
		}
		_ = throne;
	}

	// ------------------------------------------------------------------ fill

	/// <summary>
	/// Hung pieces, one per ceiling cell at most, the middle of the tier first. In a room the middle always gets
	/// its piece and the rest roll against the density (the room tables); a corridor (the kit's HallwayCeiling)
	/// rolls every cell. A piece with a Hanger gets its chain first, from the ceiling down to where it starts.
	/// </summary>
	internal static void FillCeiling( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, FillEntry[] entries, double density, int maxLights, bool firstAlways, int priority )
	{
		var t = ctx.T;
		int L = t.Level;
		// prefer the middle of the room: sort by distance to the centroid
		var (cx, cz) = t.Centroid();
		bool first = firstAlways;
		foreach ( var s in ctx.Ceilings.OrderBy( s => Math.Abs( s.X + 0.5 - cx - 0.5 ) + Math.Abs( s.Z + 0.5 - cz - 0.5 ) ).ThenBy( s => (s.X, s.Z) ) )
		{
			if ( s.NearDoor || !occ.CeilFree( s.X, s.Z, L ) ) continue;
			if ( !first && rng.NextDouble() >= density ) continue;
			first = false;
			var id = Pick( rng, entries );
			if ( id == "" ) continue;
			var def = res.Kit.Props[id];
			if ( def.Light != LightKind.None && ctx.Lights >= maxLights ) continue;
			// only the really wide pieces (the 2.4 m broken chandelier, the 5 m swag chains) need room on every side
			if ( def.Width > 2 && !Dirs.All.All( d => t.Cells.Contains( (s.X + d.Dx(), s.Z + d.Dz()) ) ) ) continue;
			occ.ClaimCeil( s.X, s.Z, L );
			float cyaw = rng.RandRange( 4 ) * 90;
			// a hung piece gets its chain first, from the ceiling down to where the piece starts
			if ( def.Hanger != null && res.Kit.Props.TryGetValue( def.Hanger, out var hanger ) )
				Add( res, ctx, hanger, rng, AnchorKind.CeilingHung, s.X, s.Z, L, Dir.N, s.X + 0.5f, s.Z + 0.5f, 0, 0, cyaw, priority );
			Add( res, ctx, def, rng, AnchorKind.CeilingHung, s.X, s.Z, L, Dir.N, s.X + 0.5f, s.Z + 0.5f, 0, def.MountHeight, cyaw, priority );
		}
	}

	private static void Fill( DungeonLayout lay, TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng )
	{
		var table = DecorCatalog.Tables[ctx.Role];
		var t = ctx.T;
		int L = t.Level;

		// whole-room floor tiles
		if ( table.FloorTiles != null )
			foreach ( var c in t.Sorted() )
			{
				var tile = table.FloorTiles[rng.RandRange( table.FloorTiles.Length )];
				// a trap's cell keeps the trap's own tile (the roll still happens so the room's pattern is stable)
				if ( res.TrapCells.Contains( (c.x, c.z, L) ) ) continue;
				res.FloorSwaps.Add( new FloorSwap( c.x, c.z, L, tile ) );
			}

		// 1. the signature first: enough of the defining prop that the room reads as what it is. Before the
		// centre piece on purpose: in a small room the beds matter more than the table.
		if ( table.Signature != null ) FillSignature( ctx, res, occ, rng, table.Signature );

		// 1b. centre set piece
		if ( Entries( res, ctx.Role, SlotKind.Centre ) is { } centreEntries )
			FillCentre( lay, ctx, res, occ, rng, centreEntries );
		// 2. wall swaps: recessed pieces go on outside walls with rock behind, windows only between two rooms
		if ( Entries( res, ctx.Role, SlotKind.WallSwap ) is { } swapEntries && table.Density[SlotKind.WallSwap] > 0 )
		{
			int max = t.Kind == TierKind.Hall ? 4 : 2;
			var recessed = ctx.SwapCandidates.OrderBy( s => (s.Line, s.D, s.K) ).ToList();
			rng.Shuffle( recessed );
			var shared = ctx.SharedWalls.OrderBy( s => (s.Seg.Line, s.Seg.D, s.Seg.K) ).ToList();
			rng.Shuffle( shared );
			for ( int n = 0; n < max; n++ )
			{
				if ( rng.NextDouble() >= table.Density[SlotKind.WallSwap] ) continue;
				var id = Pick( rng, swapEntries );
				if ( id == "" ) continue;
				var def = res.Kit.WallSwaps[id];
				if ( def.Shared )
				{
					// both faces must still be bare: the other room may have been dressed first
					int i = shared.FindIndex( s => occ.WallFree( s.X, s.Z, L, s.D ) && occ.WallFree( s.FX, s.FZ, L, s.FD ) );
					if ( i < 0 ) continue;
					var sw = shared[i];
					shared.RemoveAt( i );
					// single-sided piece: the same model on both faces, nudged apart like the door frames. A kit whose
					// walls are finished on both sides (Realms) spawns the one piece on the grid plane instead.
					res.WallSwaps.Add( new WallSwap( sw.Seg, def.Model, def.Insert, res.Kit.DoubleFaced ? null : def.Model, def.Centred, def.InsertCentred, def.MeshCollider, Setback: def.Setback ) );
					occ.ClaimWall( sw.X, sw.Z, L, sw.D );
					occ.ClaimWall( sw.FX, sw.FZ, L, sw.FD );
					continue;
				}
				int j = recessed.FindIndex( s => occ.WallFree( s.CX, s.CZ, L, s.D ) );
				if ( j < 0 ) continue;
				var seg = recessed[j];
				recessed.RemoveAt( j );
				// pieces that reach behind the plane are safe: these candidates require an empty column behind
				res.WallSwaps.Add( new WallSwap( seg, def.Model, def.Insert, null, def.Centred, def.InsertCentred, def.MeshCollider, def.Recessed, def.Setback ) );
				occ.ClaimWall( seg.CX, seg.CZ, L, seg.D );
				if ( def.Item != null && res.Kit.Props.TryGetValue( def.Item, out var item ) )
				{
					// inside the recess, just behind the wall plane
					float gx = seg.CX + 0.5f + seg.D.Dx() * 0.62f, gz = seg.CZ + 0.5f + seg.D.Dz() * 0.62f;
					Add( res, ctx, item, rng, AnchorKind.Floor, seg.CX, seg.CZ, L, seg.D.Opp(), gx, gz, 0, 0, YawOf( seg.D.Opp() ), 3 );
				}
			}
		}

		// 3. edge strips
		if ( Entries( res, ctx.Role, SlotKind.EdgeStrip ) is { } stripEntries )
			foreach ( var run in ctx.Runs )
				FillRun( lay, ctx, res, occ, rng, run, stripEntries, table.Density[SlotKind.EdgeStrip] );

		// 4. corners
		if ( Entries( res, ctx.Role, SlotKind.Corner ) is { } cornerEntries )
			foreach ( var s in ctx.Corners )
			{
				if ( !ctx.Clear( s.X, s.Z ) ) continue;
				if ( rng.NextDouble() >= table.Density[SlotKind.Corner] ) continue;
				var id = Pick( rng, cornerEntries );
				if ( id == "" ) continue;
				PlaceCorner( ctx, res, occ, rng, s, res.Kit.Props[id], 3 );
			}

		// 5. ceilings, before the wall torches so a chandelier is never squeezed out by the light cap
		if ( Entries( res, ctx.Role, SlotKind.Ceiling ) is { } ceilEntries && table.Density.TryGetValue( SlotKind.Ceiling, out var cd ) && cd > 0 )
			FillCeiling( ctx, res, occ, rng, ceilEntries, cd, table.MaxLights, firstAlways: true, 3 );

		// 6. mounted wall props (torches, banners)
		if ( Entries( res, ctx.Role, SlotKind.Wall ) is { } wallEntries )
			foreach ( var s in ctx.WallSlots )
			{
				if ( rng.NextDouble() >= table.Density[SlotKind.Wall] ) continue;
				var id = Pick( rng, wallEntries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( def.Light != LightKind.None && ctx.Lights >= table.MaxLights ) continue;
				PlaceMounted( ctx, res, occ, rng, s, def, 3 );
			}

		// 7. pillar faces: a torch on one face of each interior pillar, if the room is lit
		foreach ( var group in ctx.PillarFaces.GroupBy( s => (s.X, s.Z) ).OrderBy( gr => gr.Key ) )
		{
			if ( ctx.Lights >= table.MaxLights ) break;
			if ( rng.NextDouble() > 0.5 ) continue;
			var face = group.OrderBy( s => s.D ).ElementAt( rng.RandRange( group.Count() ) );
			var torch = res.Kit.Props["torch"];
			Add( res, ctx, torch, rng, AnchorKind.PillarMounted, face.X, face.Z, L, face.D, face.X, face.Z, 0, torch.MountHeight, YawOf( face.D ), 3 );
		}

		// 7b. a role the kit fills further in (the gaol's throne court): more 1 x 1 sets on the interior cells. After
		// the ceilings and the torches on purpose: a set's candles count against the light cap, and stamped first they
		// cost the throne room its chandelier
		if ( res.Kit.ExtraCentres != null && res.Kit.ExtraCentres.TryGetValue( ctx.Role, out var extra ) )
			FillExtraCentres( ctx, res, occ, rng, extra.entries, extra.count );

		// 8. small floor scatter in whatever quarters are still free
		if ( Entries( res, ctx.Role, SlotKind.Scatter ) is { } scatter && table.Density.TryGetValue( SlotKind.Scatter, out var sd ) )
			Scatter( ctx, res, occ, rng, scatter, sd, table.MaxLights );

		// 9. litter: rubble, bones and odds and ends that claim nothing and may lie on the walking line
		var litter = Entries( res, ctx.Role, SlotKind.Litter ) ?? DecorCatalog.Debris;
		Litter( ctx, res, occ, rng, litter, (table.Density.TryGetValue( SlotKind.Litter, out var ld ) ? ld : 0.35) * res.Kit.LitterScale );
	}

	/// <summary>
	/// More centre pieces for a room the kit wants busier than one set in the middle (the gaol's throne room, whose
	/// hall stood empty around the dais): up to `count` 1 x 1 sets on the room's interior cells, nearest the middle
	/// first, each allowed on the walking line only if every doorway still reaches every other around it.
	/// </summary>
	private static void FillExtraCentres( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, FillEntry[] entries, int count )
	{
		var t = ctx.T;
		int L = t.Level;
		var (cx, cz) = t.Centroid();
		var cells = t.Sorted()
			.Where( c => Dirs.All.All( d => t.Cells.Contains( (c.x + d.Dx(), c.z + d.Dz()) ) ) )
			.OrderBy( c => Math.Abs( c.x + 0.5 - cx - 0.5 ) + Math.Abs( c.z + 0.5 - cz - 0.5 ) ).ThenBy( c => c ).ToList();
		int placed = 0;
		foreach ( var c in cells )
		{
			if ( placed >= count ) break;
			if ( !occ.TileFree( c.x, c.z, L ) ) continue;
			var entry = PickEntry( rng, entries );
			if ( entry.Id == "" || !entry.IsSet ) continue;
			var set = res.Kit.Sets[entry.Id];
			if ( set.W != 1 || set.Dp != 1 || set.WallBehind ) continue;
			var block = new List<(int x, int z)> { c };
			if ( !ctx.Clear( c.x, c.z ) && !PathsSurvive( ctx, res, block ) ) continue;
			RerouteWalk( ctx, res, block );
			Stamp( ctx, res, occ, rng, set, block, FacingFor( ctx, block ) );
			placed++;
		}
	}

	/// <summary>
	/// The kit's exit-room prop (the gaol's portal) in the middle of the exit room, which otherwise has no role and is
	/// dressed like a corridor. Its own stream and its own step, so a kit without one changes nothing.
	/// </summary>
	private static void PlaceExitProp( DungeonLayout lay, Dictionary<int, TierCtx> ctxs, DecorResult res, Occupancy occ )
	{
		if ( res.Kit.ExitProp == null || !res.Kit.Props.TryGetValue( res.Kit.ExitProp, out var def ) ) return;
		foreach ( var ctx in ctxs.Values.Where( c => c.T.Tag == TierTag.Exit ).OrderBy( c => c.T.Id ) )
		{
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0xE817 ) ) );
			int L = ctx.T.Level;
			foreach ( var block in ctx.Centres.Where( b => b.Count == 1 ) )
			{
				var c = block[0];
				if ( !occ.TileFree( c.x, c.z, L ) ) continue;
				if ( !ctx.Clear( c.x, c.z ) && !PathsSurvive( ctx, res, block ) ) continue;
				if ( def.Blocks ) RerouteWalk( ctx, res, block );
				PlaceFloor( ctx, res, occ, rng, def, c.x + 0.5f, c.z + 0.5f, YawOf( FacingFor( ctx, block ) ), 0, 3 );
				break;
			}
		}
	}

	/// <summary>
	/// The room's defining prop, placed before anything else competes for the walls: as many as the room
	/// size asks for, best slots first (away from the doors and the walking line), then the rest.
	/// </summary>
	private static void FillSignature( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, SignatureDef sig )
	{
		int target = Math.Max( sig.Min, (int)Math.Round( sig.PerCell * ctx.T.Cells.Count ) );
		// the centre block is left for the centre set piece (the feast table must fit after the benches)
		var centre = ctx.Centres.Count > 0 && Entries( res, ctx.Role, SlotKind.Centre ) is { } ce && ce.Any( e => e.IsSet )
			? new HashSet<(int x, int z)>( ctx.Centres[0] ) : new HashSet<(int x, int z)>();
		var all = ctx.Runs.SelectMany( r => r.Slots ).Where( s => !centre.Contains( (s.X, s.Z) ) ).ToList();
		var good = all.Where( s => !s.NearDoor && ctx.Clear( s.X, s.Z ) ).ToList();
		var rest = all.Where( s => s.NearDoor || !ctx.Clear( s.X, s.Z ) ).ToList();
		rng.Shuffle( good );
		rng.Shuffle( rest );
		int placed = 0;
		foreach ( var s in good.Concat( rest ) )
		{
			for ( int half = 0; half < 2 && placed < target; half++ )
			{
				var id = Pick( rng, sig.Entries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( !TryPlaceInHalf( ctx, res, occ, rng, s, half, def ) ) continue;
				placed++;
				if ( def.Wide || def.Tile ) break;
			}
			if ( placed >= target ) break;
		}
	}

	/// <summary>
	/// Layer 5: debris on the floor. Litter claims no quarter (only avoids stacking on itself), so it fills
	/// in around everything else, and being non-blocking it may lie on the walking line at half density.
	/// </summary>
	public static void Litter( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, FillEntry[] entries, double density )
	{
		int L = ctx.T.Level;
		foreach ( var c in ctx.T.Sorted() )
		{
			double d = ctx.Clear( c.x, c.z ) ? density : density * 0.5;
			for ( int q = 0; q < 4; q++ )
			{
				if ( !occ.LitterFree( c.x, c.z, L, q ) ) continue;
				if ( rng.NextDouble() >= d ) continue;
				var id = Pick( rng, entries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( !def.Litter || def.Light != LightKind.None ) continue;
				var (gx, gz) = JitterInQuarter( ctx, rng, c.x, c.z, q, def.Width );
				occ.NoteLitter( c.x, c.z, L, q );
				Add( res, ctx, def, rng, AnchorKind.Floor, c.x, c.z, L, Dir.N, gx, gz, 0, def.Lift, rng.RandRange( 360 ), 3 );
			}
		}
	}

	/// <summary>Layer 4: small things on the open floor, one roll per free quarter cell off the walking line.</summary>
	public static void Scatter( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, FillEntry[] entries, double density, int maxLights )
	{
		int L = ctx.T.Level;
		foreach ( var c in ctx.T.Sorted() )
		{
			if ( !ctx.Clear( c.x, c.z ) ) continue;
			for ( int q = 0; q < 4; q++ )
			{
				if ( !occ.QuarterFree( c.x, c.z, L, q ) ) continue;
				if ( rng.NextDouble() >= density ) continue;
				var id = Pick( rng, entries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( def.Tile || def.Wide ) continue;
				if ( def.Light != LightKind.None && ctx.Lights >= maxLights ) continue;
				// anywhere in the quarter that keeps the prop inside it, so nothing reads as a grid
				var (gx, gz) = JitterInQuarter( ctx, rng, c.x, c.z, q, def.Width );
				float yaw = rng.RandRange( 360 );
				occ.ClaimQuarter( c.x, c.z, L, q );
				var model = Add( res, ctx, def, rng, AnchorKind.Floor, c.x, c.z, L, Dir.N, gx, gz, 0, def.Lift, yaw, 3 );
				Dress( ctx, res, occ, rng, def, model, gx, gz, yaw, (1, 0), (1.25f - def.Width / 2f) / 5f - 0.05f );
			}
		}
	}

	/// <summary>
	/// A random spot inside quarter q of a cell that keeps a prop of the given width inside the quarter,
	/// and clear of a corner post when the quarter's outer corner carries one.
	/// </summary>
	private static (float gx, float gz) JitterInQuarter( TierCtx ctx, DungeonRng rng, int x, int z, int q, float width )
	{
		float cx = (q & 1) == 1 ? 0.75f : 0.25f, cz = (q & 2) == 2 ? 0.75f : 0.25f;
		float slack = Math.Max( 0f, 0.5f - width / 5f );
		float jx = (float)(rng.NextDouble() - 0.5) * slack, jz = (float)(rng.NextDouble() - 0.5) * slack;
		if ( ctx.PillarAtQuarter( x, z, q ) )
		{
			// only ever jitter toward the cell centre, plus 20 cm: the prop's centre stays 1.4 m+ from the post
			float inX = (q & 1) == 1 ? -1 : 1, inZ = (q & 2) == 2 ? -1 : 1;
			jx = inX * (Math.Abs( jx ) * 0.7f + 0.04f);
			jz = inZ * (Math.Abs( jz ) * 0.7f + 0.04f);
		}
		// The slack keeps the prop inside its quarter but knew nothing about the wall's own thickness: on a
		// quarter whose outer edge IS a wall (no tier cell beyond it) the prop's edge could sit 27 cm inside the
		// masonry. Only that side is pulled in, by the wall's half thickness, so interior quarters do not change.
		int ox = (q & 1) == 1 ? 1 : -1, oz = (q & 2) == 2 ? 1 : -1;
		float hw = width / 10f, room = 0.25f - hw - WallHalfCells;
		if ( !ctx.Has( x + ox, z ) ) jx = ox > 0 ? Math.Min( jx, room ) : Math.Max( jx, -room );
		if ( !ctx.Has( x, z + oz ) ) jz = oz > 0 ? Math.Min( jz, room ) : Math.Max( jz, -room );
		return (x + cx + jx, z + cz + jz);
	}

	/// <summary>
	/// Layers 2 and 3: things on a prop's top surface and things beside it along `along` (unit vector in
	/// cell space), within `freeAlong` cells of free space on each side, so they stay inside the area the
	/// parent already claimed. Companions may carry a top item of their own.
	/// </summary>
	public static void Dress( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, PropDef def, string model, float gx, float gz, float yaw, (float x, float z) along, float freeAlong )
	{
		int L = ctx.T.Level;
		int maxLights = ctx.Role == DecorRole.None ? 0 : DecorCatalog.Tables[ctx.Role].MaxLights;

		void OnTop( PropDef parent, string parentModel, float px, float pz, float pyaw )
		{
			var tops = res.Kit.Tops ?? DecorCatalog.Tops;
			if ( parentModel == null || parent.Top <= 0 || !tops.TryGetValue( parent.Id, out var list ) ) return;
			// a decayed twin (the broken table, the collapsed bookcase) carries nothing: its surface is gone (user).
			// Before any roll, so a seed whose table did not decay is untouched.
			if ( parent.Decay != null && Array.IndexOf( parent.Decay, parentModel ) >= 0 ) return;
			// a variant whose usable surface is lower / further back than the def says (recessed shelf top)
			res.Kit.TopFix.TryGetValue( parentModel, out var fix );
			int n = TopSlots( parent );
			for ( int i = 0; i < n; i++ )
			{
				if ( rng.NextDouble() >= 0.6 ) continue;
				var id = Pick( rng, list );
				if ( id == "" ) continue;
				var item = res.Kit.Props[id];
				if ( item.Light != LightKind.None && ctx.Lights >= maxLights ) continue;
				PlaceOnTop( ctx, res, rng, parent, px, pz, pyaw, fix, item, i );
			}
		}

		OnTop( def, model, gx, gz, yaw );
		if ( !DecorCatalog.Companions.TryGetValue( def.Id, out var comps ) ) return;
		foreach ( int side in new[] { -1, 1 } )
		{
			if ( rng.NextDouble() >= 0.5 ) continue;
			var id = Pick( rng, comps );
			if ( id == "" ) continue;
			var c = res.Kit.Props[id];
			if ( c.Tile || c.Wide || c.Overlay ) continue;
			if ( c.Light != LightKind.None && ctx.Lights >= maxLights ) continue;
			float need = (def.Width / 2f + c.Width / 2f + 0.1f) / 5f;   // centre to centre
			float cx = gx + along.x * need * side, cz = gz + along.z * need * side;
			int cellx = (int)Math.Floor( cx ), cellz = (int)Math.Floor( cz );
			// the whole companion inside the tier, not just its centre (its yaw is rolled after this, so the
			// larger extent is the conservative reach); in place of the old centre-only ctx.Has, so the rolls
			// above are unchanged and only the placements that were wrong move
			if ( !FootprintInside( ctx, cx, cz, ReachOf( c ) ) ) continue;
			if ( c.Blocks && !ctx.Clear( cellx, cellz ) ) continue;
			// inside the space the parent already claimed, or in the parent's own quarter: no claim needed;
			// anything further out takes the quarter it lands in, if that quarter is still free
			bool inside = (c.Width + 0.1f) / 5f <= freeAlong;
			int px = (int)Math.Floor( gx ), pz = (int)Math.Floor( gz );
			int q = Occupancy.QuarterOf( cx, cz, cellx, cellz );
			bool ownQuarter = (cellx, cellz) == (px, pz) && q == Occupancy.QuarterOf( gx, gz, px, pz );
			if ( !inside && !ownQuarter )
			{
				if ( !occ.QuarterFree( cellx, cellz, L, q ) ) continue;
				occ.ClaimQuarter( cellx, cellz, L, q );
			}
			float cyaw = yaw + rng.RandInt( -30, 30 );
			var cmodel = Add( res, ctx, c, rng, AnchorKind.Floor, cellx, cellz, L, Dir.N, cx, cz, 0, c.Lift, cyaw, 3 );
			if ( rng.NextDouble() < 0.4 ) OnTop( c, cmodel, cx, cz, cyaw );
		}
	}

	/// <summary>Item slots on a top surface: a long table takes one per half, everything else one.</summary>
	public static int TopSlots( PropDef parent ) => parent.Width > 2f ? 2 : 1;

	/// <summary>
	/// One small item onto a placed prop's Top surface, into slot `i` of <see cref="TopSlots"/>: the jitter is
	/// rolled here (two doubles and a yaw) whether or not the item fits, so a caller's roll sequence is the same
	/// either way. Shared by the fill's OnTop and the potion pass. `fix` is the kit's TopFix for the parent model.
	/// The parent's OWN axes, from the yaw it was spawned with: its Width runs along its left, its Depth along its
	/// forward (spreading along the wall direction put items off the side of narrow, turned tables).
	/// Returns false when the item is bigger than the surface (a platter on a narrow altar).
	/// </summary>
	public static bool PlaceOnTop( TierCtx ctx, DecorResult res, DungeonRng rng, PropDef parent, float px, float pz, float pyaw, (float drop, float back) fix, PropDef item, int i )
	{
		int L = ctx.T.Level;
		int n = TopSlots( parent );
		// a long table takes one item per half: each spreads within its half only, so the pair neither
		// overhangs the ends nor lands in each other
		float slotW = n == 2 ? parent.Width / 2f : parent.Width;
		float top = parent.Top - fix.drop;
		float rad = pyaw * MathF.PI / 180f;
		float fx = MathF.Cos( rad ), fz = MathF.Sin( rad );
		float ax = -fz, az = fx;
		float bx = -fx * fix.back / 5f, bz = -fz * fix.back / 5f;   // toward the model's rear, cell units
		float ru = (float)(rng.NextDouble() - 0.5), rv = (float)(rng.NextDouble() - 0.5);
		float iyaw = rng.RandRange( 360 );
		// an item bigger than the surface it would sit on hangs off it whatever the jitter: skipped, its rolls
		// spent, so the rest of the room does not move
		if ( Math.Max( item.Width, item.Depth ) > Math.Min( slotW, parent.Depth ) ) return false;
		float ju = ru * Math.Max( 0, slotW - item.Width ) / 5f * 0.8f;
		// a recessed shelf's back offset eats into the depth the item may wander over
		float jv = rv * Math.Max( 0, parent.Depth - item.Depth - 2f * Math.Abs( fix.back ) ) / 5f * 0.8f;
		if ( n == 2 ) ju += (i == 0 ? -1 : 1) * parent.Width / 20f;
		float ix = px + ax * ju + fx * jv + bx, iz = pz + az * ju + fz * jv + bz;
		Add( res, ctx, item, rng, AnchorKind.Floor, (int)Math.Floor( ix ), (int)Math.Floor( iz ), L, Dir.N, ix, iz, 0, top + item.Lift - item.TopSink, iyaw, 3 );
		return true;
	}

	private static void FillCentre( DungeonLayout lay, TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, FillEntry[] entries )
	{
		var t = ctx.T;
		int L = t.Level;
		var entry = PickEntry( rng, entries );
		if ( entry.Id == "" ) return;
		if ( !entry.IsSet )
		{
			// a single prop in the middle of the first free centre cell
			var def = res.Kit.Props[entry.Id];
			foreach ( var block in ctx.Centres.Where( b => b.Count == 1 ) )
			{
				var c = block[0];
				if ( !occ.TileFree( c.x, c.z, L ) ) continue;
				if ( !ctx.Clear( c.x, c.z ) && !PathsSurvive( ctx, res, block ) ) continue;
				if ( def.Blocks ) RerouteWalk( ctx, res, block );
				PlaceFloor( ctx, res, occ, rng, def, c.x + 0.5f, c.z + 0.5f, YawOf( FacingFor( ctx, block ) ), 0, 3 );
				return;
			}
			return;
		}
		var set = res.Kit.Sets[entry.Id];
		var tries = new List<SetPiece> { set };
		// Shrine offers grand and small variants; if the chosen set does not fit, fall back to any that does
		foreach ( var e in entries.Where( e => e.IsSet && e.Id != entry.Id ) ) tries.Add( res.Kit.Sets[e.Id] );
		foreach ( var sp in tries )
			if ( TryStampSet( ctx, res, occ, rng, sp ) ) return;
	}

	/// <summary>
	/// Whether the room stays passable with `block` filled: no portal cell is in the block, and every portal
	/// cell still reaches every other through cells that are neither in the block nor already fully claimed.
	/// Lets a feast table stand in the middle of a small hall whose walking line runs straight through it.
	/// </summary>
	private static bool PathsSurvive( TierCtx ctx, DecorResult res, List<(int x, int z)> block )
	{
		var blocked = Impassable( ctx, res, block );
		var portals = ctx.Portals.Select( p => p.cell ).Distinct().ToList();
		if ( portals.Any( block.Contains ) ) return false;   // never the doorway cell itself
		if ( portals.Count < 2 ) return true;
		var seen = new HashSet<(int x, int z)> { portals[0] };
		var q = new Queue<(int x, int z)>();
		q.Enqueue( portals[0] );
		while ( q.Count > 0 )
		{
			var c = q.Dequeue();
			foreach ( var d in Dirs.All )
			{
				var n = (c.x + d.Dx(), c.z + d.Dz());
				if ( !ctx.Has( n.Item1, n.Item2 ) || blocked.Contains( n ) || !seen.Add( n ) ) continue;
				q.Enqueue( n );
			}
		}
		return portals.All( seen.Contains );
	}

	/// <summary>
	/// After a centre piece lands on the walking line: the line moves around it. The block's cells leave
	/// the walk set and the shortest portal-to-portal paths that avoid the block join it, so the later
	/// passes (corners, litter density, the harness invariant) see the real lane.
	/// </summary>
	/// <summary>Cells the walking line may not use: the block itself and any cell already holding a heavy blocking prop.</summary>
	private static HashSet<(int x, int z)> Impassable( TierCtx ctx, DecorResult res, List<(int x, int z)> block )
	{
		var set = new HashSet<(int x, int z)>( block );
		foreach ( var p in res.Props )
		{
			if ( p.TierId != ctx.T.Id || !p.Blocks || p.Anchor != AnchorKind.Floor ) continue;
			// the same idea of "heavy" as the harness: not a small thing tucked against a wall
			bool small = p.S != 0 || res.Kit.Props.TryGetValue( p.Prop, out var d ) && d.Depth <= 1.0f && !d.Tile;
			if ( !small ) set.Add( (p.X, p.Z) );
		}
		return set;
	}

	private static void RerouteWalk( TierCtx ctx, DecorResult res, List<(int x, int z)> block )
	{
		if ( block.All( c => !ctx.Walk.Contains( c ) ) ) return;
		int L = ctx.T.Level;
		var blocked = Impassable( ctx, res, block );
		foreach ( var c in block ) { ctx.Walk.Remove( c ); res.Walk.Remove( (c.x, c.z, L) ); }
		var portals = ctx.Portals.Select( p => p.cell ).Distinct().ToList();
		List<(int x, int z)> PathAround( (int x, int z) a, (int x, int z) b )
		{
			var prev = new Dictionary<(int x, int z), (int x, int z)> { [a] = a };
			var q = new Queue<(int x, int z)>();
			q.Enqueue( a );
			while ( q.Count > 0 && !prev.ContainsKey( b ) )
			{
				var c = q.Dequeue();
				foreach ( var d in Dirs.All )
				{
					var n = (c.x + d.Dx(), c.z + d.Dz());
					if ( !ctx.Has( n.Item1, n.Item2 ) || blocked.Contains( n ) || prev.ContainsKey( n ) ) continue;
					prev[n] = c;
					q.Enqueue( n );
				}
			}
			var path = new List<(int x, int z)>();
			if ( !prev.ContainsKey( b ) ) return path;
			for ( var c = b; c != a; c = prev[c] ) path.Add( c );
			path.Add( a );
			return path;
		}
		for ( int i = 0; i < portals.Count; i++ )
			for ( int j = i + 1; j < portals.Count; j++ )
				foreach ( var c in PathAround( portals[i], portals[j] ) ) { ctx.Walk.Add( c ); res.Walk.Add( (c.x, c.z, L) ); }
		// `blocked`, not `block`: a room's second centre piece must not hand the first one's approach cells back to the line
		foreach ( var c in ctx.Approach.Where( c => !blocked.Contains( c ) ) ) { ctx.Walk.Add( c ); res.Walk.Add( (c.x, c.z, L) ); }
	}

	/// <summary>Which way a centre block faces: toward the nearest portal, else back along the spine.</summary>
	private static Dir FacingFor( TierCtx ctx, List<(int x, int z)> block )
	{
		double bx = block.Average( c => c.x ) + 0.5, bz = block.Average( c => c.z ) + 0.5;
		if ( ctx.Portals.Count == 0 ) return ctx.T.Heading.Opp();
		var p = ctx.Portals.OrderBy( p => Math.Abs( p.cell.x + 0.5 - bx ) + Math.Abs( p.cell.z + 0.5 - bz ) ).ThenBy( p => p ).First();
		double dx = p.cell.x + 0.5 - bx, dz = p.cell.z + 0.5 - bz;
		if ( Math.Abs( dx ) < 1e-6 && Math.Abs( dz ) < 1e-6 ) return p.outDir;
		return Math.Abs( dx ) >= Math.Abs( dz ) ? (dx > 0 ? Dir.E : Dir.W) : (dz > 0 ? Dir.N : Dir.S);
	}

	private static bool TryStampSet( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, SetPiece set )
	{
		var t = ctx.T;
		int L = t.Level;
		if ( set.WallBehind )
		{
			// a 1-cell block against the wall farthest from the portals, facing into the room
			var options = new List<((int x, int z) c, Dir f, double score)>();
			foreach ( var c in t.Sorted() )
			{
				if ( !ctx.Clear( c.x, c.z ) || !occ.TileFree( c.x, c.z, L ) ) continue;
				foreach ( var f in Dirs.All )
				{
					var back = (c.x - f.Dx(), c.z - f.Dz());
					if ( t.Cells.Contains( back ) ) continue;                                   // needs a wall behind
					if ( !t.Cells.Contains( (c.x + f.Dx(), c.z + f.Dz()) ) ) continue;         // and room in front
					double dist = ctx.Portals.Count == 0 ? 0 : ctx.Portals.Min( p => Math.Abs( p.cell.x - c.x ) + Math.Abs( p.cell.z - c.z ) );
					// prefer the middle of that wall
					var wallCells = t.Cells.Where( o => !t.Cells.Contains( (o.x - f.Dx(), o.z - f.Dz()) ) && (f.Dx() != 0 ? o.x == c.x : o.z == c.z) ).ToList();
					double mid = f.Dx() != 0 ? wallCells.Average( o => o.z ) : wallCells.Average( o => o.x );
					double off = f.Dx() != 0 ? Math.Abs( c.z - mid ) : Math.Abs( c.x - mid );
					options.Add( (c, f, dist - off * 0.5) );
				}
			}
			if ( options.Count == 0 ) return false;
			var best = options.OrderByDescending( o => o.score ).ThenBy( o => o.c ).ThenBy( o => o.f ).First();
			Stamp( ctx, res, occ, rng, set, new List<(int x, int z)> { best.c }, best.f );
			return true;
		}
		if ( set.W == 2 && set.Dp == 2 )
		{
			// a 2x2 group goes on any 2x2 of the room, nearest the middle first: an exact centre 2x2 only exists
			// in even-by-even rooms, which is why the grand sets never appeared
			var (cx0, cz0) = t.Centroid();
			var origins = t.Sorted()
				.Where( c => t.Cells.Contains( (c.x + 1, c.z) ) && t.Cells.Contains( (c.x, c.z + 1) ) && t.Cells.Contains( (c.x + 1, c.z + 1) ) )
				.OrderBy( c => Math.Abs( c.x + 1 - cx0 - 0.5 ) + Math.Abs( c.z + 1 - cz0 - 0.5 ) ).ThenBy( c => c ).ToList();
			foreach ( var o in origins )
			{
				var block = new List<(int x, int z)> { (o.x, o.z), (o.x + 1, o.z), (o.x, o.z + 1), (o.x + 1, o.z + 1) };
				if ( block.Any( c => !occ.TileFree( c.x, c.z, L ) ) ) continue;
				if ( !block.All( c => ctx.Clear( c.x, c.z ) ) && !PathsSurvive( ctx, res, block ) ) continue;
				Stamp( ctx, res, occ, rng, set, block, FacingFor( ctx, block ) );
				return true;
			}
			return false;
		}
		foreach ( var block in ctx.Centres )
		{
			if ( block.Any( c => !occ.TileFree( c.x, c.z, L ) ) ) continue;
			// the block may sit on the walking line as long as every doorway still reaches every other one around it
			if ( !block.All( c => ctx.Clear( c.x, c.z ) ) && !PathsSurvive( ctx, res, block ) ) continue;
			int bw = block.Max( c => c.x ) - block.Min( c => c.x ) + 1, bh = block.Max( c => c.z ) - block.Min( c => c.z ) + 1;
			var primary = FacingFor( ctx, block );
			foreach ( var f in new[] { primary, primary.Opp(), primary.Turn()[0], primary.Turn()[1] } )
			{
				int along = f.Dx() != 0 ? bw : bh, across = f.Dx() != 0 ? bh : bw;
				if ( set.Dp != along || set.W != across ) continue;
				Stamp( ctx, res, occ, rng, set, block, f );
				return true;
			}
		}
		return false;
	}

	private static void Stamp( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, SetPiece set, List<(int x, int z)> block, Dir f )
	{
		int L = ctx.T.Level;
		float ax = (float)block.Average( c => c.x ) + 0.5f, az = (float)block.Average( c => c.z ) + 0.5f;
		float fx = f.Dx(), fz = f.Dz(), rx = fz, rz = -fx;
		float yaw0 = YawOf( f );
		res.SetsUsed.Add( (ctx.T.Id, set.Id) );
		RerouteWalk( ctx, res, block );
		foreach ( var c in block ) occ.ClaimTile( c.x, c.z, L );
		if ( set.FloorTile == "rune" )
			foreach ( var c in block ) res.FloorSwaps.Add( new FloorSwap( c.x, c.z, L, res.Kit.RuneFloorModels[rng.RandRange( res.Kit.RuneFloorModels.Length )] ) );
		foreach ( var item in set.Items )
		{
			if ( item.Chance < 1 && rng.NextDouble() >= item.Chance ) continue;
			var def = res.Kit.Props[item.Prop];
			float gx = ax + item.U * fx + item.V * rx, gz = az + item.U * fz + item.V * rz;
			int cx = (int)Math.Floor( gx ), cz = (int)Math.Floor( gz );
			// an item at the edge of a small room's block would otherwise stand in the wall: no spatial test existed here
			if ( !FootprintInside( ctx, gx, gz, ReachOf( def ) ) ) continue;
			if ( def.Light != LightKind.None ) ctx.Lights++;
			// hanging items of a set (the forge hood, the pit's chain) hang from the ceiling above their spot
			if ( def.Anchor == AnchorKind.CeilingHung )
			{
				if ( def.Hanger != null && res.Kit.Props.TryGetValue( def.Hanger, out var hanger ) )
					Add( res, ctx, hanger, rng, AnchorKind.CeilingHung, cx, cz, L, f, gx, gz, 0, 0, yaw0 + item.Yaw, 3, countLight: false );
				Add( res, ctx, def, rng, AnchorKind.CeilingHung, cx, cz, L, f, gx, gz, 0, def.MountHeight, yaw0 + item.Yaw, 3, countLight: false );
				continue;
			}
			// items on a top surface sink by their TopSink (candles into a table)
			float sink = item.H > 0 ? def.TopSink : 0;
			Add( res, ctx, def, rng, AnchorKind.Floor, cx, cz, L, f, gx, gz, 0, item.H + def.Lift - sink, yaw0 + item.Yaw, 3, countLight: false );
		}
	}

	private static void FillRun( DungeonLayout lay, TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, WallRun run, FillEntry[] entries, double density )
	{
		foreach ( var s in run.Slots )
		{
			// two half slots along the wall; a wide prop takes both
			for ( int half = 0; half < 2; half++ )
			{
				if ( rng.NextDouble() >= density ) continue;
				var id = Pick( rng, entries );
				if ( id == "" ) continue;
				var def = res.Kit.Props[id];
				if ( TryPlaceInHalf( ctx, res, occ, rng, s, half, def ) && (def.Wide || def.Tile) ) break;
			}
		}
	}

	/// <summary>The half of a doorway slot beside the frame itself, which stays clear (0 or 1), or -1.</summary>
	private static int ForbidHalf( TierCtx ctx, Slot s )
	{
		if ( !s.NearDoor ) return -1;
		var f = s.D.Opp();
		float lx = -f.Dz(), lz = f.Dx();
		int forbid = -1;
		foreach ( var (cell, od) in ctx.Portals )
		{
			if ( cell != (s.X, s.Z) ) continue;
			float dot = od.Dx() * lx + od.Dz() * lz;
			if ( dot > 0 ) forbid = 1; else if ( dot < 0 ) forbid = 0;
		}
		return forbid;
	}

	/// <summary>
	/// One prop into one half of a wall slot (or the whole slot for wide pieces), if the lane rules and the
	/// occupancy allow it. Deep props take the half-cell column behind their half, so two beds share a cell.
	/// Small pieces get a little slide along the wall and a little turn, so no two walls look alike.
	/// </summary>
	internal static bool TryPlaceInHalf( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, Slot s, int half, PropDef def )
	{
		int L = ctx.T.Level;
		// doorway and approach cells keep the lane: shallow props only, and never the half beside the frame
		bool lane = s.NearDoor || ctx.Approach.Contains( (s.X, s.Z) ) || ctx.Walk.Contains( (s.X, s.Z) );
		if ( half == ForbidHalf( ctx, s ) ) return false;
		// the lane stays passable: nothing that spans the cell, and anything deeper than a metre only if
		// it is a narrow deep piece (a bed against the side wall leaves 2.3 m to walk past)
		if ( lane && (def.Tile || def.Wide || (def.Depth > 1.0f && !def.Deep)) ) return false;
		if ( def.ExteriorOnly && !ctx.SwapCandidates.Any( seg => seg.CX == s.X && seg.CZ == s.Z && seg.D == s.D ) ) return false;
		// there is no fill table for DecorRole.None (corridors, spawn, exit), so a light-carrying prop coming
		// from a pass that is not the role fill would throw here; same guard Dress already uses
		if ( def.Light != LightKind.None && ctx.Lights >= (ctx.Role == DecorRole.None ? 0 : DecorCatalog.Tables[ctx.Role].MaxLights) ) return false;
		if ( def.Wide || def.Tile )
		{
			if ( half == 1 ) return false;
			var qs = Occupancy.QuartersOn( s.D );
			if ( !(def.Tile ? occ.TileFree( s.X, s.Z, L ) : qs.All( q => occ.QuarterFree( s.X, s.Z, L, q ) )) ) return false;
			// a piece that fills the wall may not run into the corner posts
			if ( def.Width > 4.2f && (ctx.PillarVerts.Contains( EndVertex( s, 0 ) ) || ctx.PillarVerts.Contains( EndVertex( s, 1 ) )) ) return false;
			PlaceAgainstWall( ctx, res, occ, rng, s, def, 0, 3 );
			return true;
		}
		// which half: the quarter toward -along first, then +along
		float sOff = half == 0 ? -0.25f : 0.25f;
		int q = QuarterAt( s, sOff );
		var need = def.Deep ? Occupancy.ColumnOf( s.D, q ) : new[] { q };
		if ( need.Any( c => !occ.QuarterFree( s.X, s.Z, L, c ) ) ) return false;
		// a deep piece on the lane needs the other half of the cell free to walk through
		if ( lane && def.Deep && Enumerable.Range( 0, 4 ).Except( need ).Any( c => !occ.QuarterFree( s.X, s.Z, L, c ) ) ) return false;
		// a corner post at this end of the wall: long pieces don't fit beside it, the rest keep 20 cm off
		bool post = ctx.PillarVerts.Contains( EndVertex( s, half ) );
		if ( post && def.Width > 1.9f ) return false;
		if ( post ) sOff -= Math.Sign( sOff ) * 0.04f;
		// slide up to 15 cm along the wall, staying well inside the half
		sOff += (float)(rng.NextDouble() - 0.5) * 0.06f;
		// nothing pokes into the side wall at the segment's end: a wide piece in a half (the weapon rack, 2.1 m, sat
		// 20 cm from the grid line and inside the side wall's thickness) is pulled toward the segment's middle until
		// its end keeps SideClearance off the side wall's face
		float limit = 0.5f - (def.Width / 2f + SideClearance + WallHalfM) / 5f;
		if ( limit > 0f && Math.Abs( sOff ) > limit ) sOff = Math.Sign( sOff ) * limit;
		PlaceAgainstWall( ctx, res, occ, rng, s, def, sOff, 3 );
		return true;
	}

	/// <summary>Metres a wall piece's end keeps off the side wall's face (15 u) and the wall's half thickness (matches DungeonBuilder.WallHalf).</summary>
	private const float SideClearance = 0.38f;
	private const float WallHalfM = 0.27f;   // 10.6 u

	/// <summary>The grid vertex at the -along (half 0) or +along (half 1) end of a wall slot's segment.</summary>
	private static (int vx, int vz) EndVertex( Slot s, int half )
	{
		var f = s.D.Opp();
		float lx = -f.Dz(), lz = f.Dx();
		int sign = half == 0 ? -1 : 1;
		return (s.X + (s.D.Dx() > 0 || lx * sign > 0 ? 1 : 0), s.Z + (s.D.Dz() > 0 || lz * sign > 0 ? 1 : 0));
	}

	/// <summary>The quarter of a wall slot's cell that a prop at offset s along the wall (cell units) stands in.</summary>
	private static int QuarterAt( Slot s, float sOff )
	{
		// along-wall axis: Left of the facing yaw = (-fz, fx) for facing f = d.Opp()
		var f = s.D.Opp();
		float lx = -f.Dz(), lz = f.Dx();
		float gx = s.X + 0.5f + s.D.Dx() * 0.3f + lx * sOff, gz = s.Z + 0.5f + s.D.Dz() * 0.3f + lz * sOff;
		return Occupancy.QuarterOf( gx, gz, s.X, s.Z );
	}

	/// <summary>A flush prop against wall d of the slot's cell; sOff is the offset along the wall in cell units.</summary>
	private static void PlaceAgainstWall( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, Slot s, PropDef def, float sOff, int priority )
	{
		int L = ctx.T.Level;
		var f = s.D.Opp();
		float lx = -f.Dz(), lz = f.Dx();
		// a little turn for the smaller pieces; flush furniture stays nearly square so its back doesn't leave the wall
		float turn = !def.Wide && !def.Tile ? (float)(rng.NextDouble() - 0.5) * (def.Anchor == AnchorKind.WallFlush ? 6f : 16f) : 0f;
		// approximate centre (the builder does the exact push from the model bounds for WallFlush props); a floor
		// prop's reach toward the wall grows with its turn (a crate turned 8 degrees reaches 7 cm further), so the
		// turned half extent is what keeps it off the wall. A negative Embed stands it that much further off.
		float trad = turn * MathF.PI / 180f;
		float halfDepth = def.Anchor == AnchorKind.Floor ? def.Depth / 2f * MathF.Abs( MathF.Cos( trad ) ) + def.Width / 2f * MathF.Abs( MathF.Sin( trad ) ) : def.Depth / 2f;
		float inward = 0.055f + halfDepth / 5f - (def.Anchor == AnchorKind.Floor ? def.Embed / 5f : 0f);
		float gx = s.X + 0.5f + s.D.Dx() * (0.5f - inward) + lx * sOff, gz = s.Z + 0.5f + s.D.Dz() * (0.5f - inward) + lz * sOff;
		var mine = new HashSet<int>();
		if ( def.Tile ) { occ.ClaimTile( s.X, s.Z, L ); mine.UnionWith( new[] { 0, 1, 2, 3 } ); }
		else if ( def.Wide ) foreach ( var q in Occupancy.QuartersOn( s.D ) ) { occ.ClaimQuarter( s.X, s.Z, L, q ); mine.Add( q ); }
		else if ( def.Deep ) foreach ( var q in Occupancy.ColumnOf( s.D, Occupancy.QuarterOf( gx, gz, s.X, s.Z ) ) ) { occ.ClaimQuarter( s.X, s.Z, L, q ); mine.Add( q ); }
		else { int q = Occupancy.QuarterOf( gx, gz, s.X, s.Z ); occ.ClaimQuarter( s.X, s.Z, L, q ); mine.Add( q ); }
		occ.NoteBand( s.X, s.Z, L, s.D, def.Height );
		float yaw = YawOf( f ) + turn;
		var model = def.Anchor == AnchorKind.WallFlush
			? Add( res, ctx, def, rng, AnchorKind.WallFlush, s.X, s.Z, L, s.D, gx, gz, sOff, def.Lift, yaw, priority )
			: Add( res, ctx, def, rng, AnchorKind.Floor, s.X, s.Z, L, s.D, gx, gz, sOff, def.Lift, yaw, priority );
		// free space along the wall on each side of the prop: half of its half-slot (2.5 m) or of the whole segment
		float freeAlong = (sOff == 0 ? 2.5f - def.Width / 2f : 1.25f - def.Width / 2f) / 5f;
		// the yaw the prop was actually spawned with (turn included), so its top items follow its real axes
		Dress( ctx, res, occ, rng, def, model, gx, gz, yaw, (lx, lz), freeAlong );
		if ( def.Foot != null && rng.NextDouble() < 0.6 && res.Kit.Props.TryGetValue( def.Foot, out var foot ) )
		{
			float fgx = gx + f.Dx() * (def.Depth / 10f + 0.12f), fgz = gz + f.Dz() * (def.Depth / 10f + 0.12f);
			int cx = (int)Math.Floor( fgx ), cz = (int)Math.Floor( fgz );
			if ( FootprintInside( ctx, fgx, fgz, ReachOf( foot ) ) && ctx.Clear( cx, cz ) )
			{
				int q = Occupancy.QuarterOf( fgx, fgz, cx, cz );
				// the foot of a bed lands in the column the bed itself claimed; anywhere else it needs a free quarter
				bool own = (cx, cz) == (s.X, s.Z) && mine.Contains( q );
				if ( own || occ.QuarterFree( cx, cz, L, q ) )
				{
					if ( !own ) occ.ClaimQuarter( cx, cz, L, q );
					Add( res, ctx, foot, rng, AnchorKind.Floor, cx, cz, L, s.D, fgx, fgz, 0, 0, YawOf( f ) + 90, priority );
				}
			}
		}
	}

	public static void PlaceCorner( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, Slot s, PropDef def, int priority )
	{
		int L = ctx.T.Level;
		int q = Occupancy.QuarterOf( s.D, s.D2 );
		if ( !occ.QuarterFree( s.X, s.Z, L, q ) ) return;
		if ( def.Tile && !occ.TileFree( s.X, s.Z, L ) ) return;
		// facing out of the corner (a diagonal), with a little turn
		float yaw = (float)(Math.Atan2( -s.D.Dz(), -s.D2.Dx() ) * 180 / Math.PI) + rng.RandInt( -20, 20 );
		// the prop stands diagonally, so its reach toward each wall is the ROTATED half extent (a 1.15 m crate at
		// 45 degrees reaches 0.81 m, not 0.57): crates in corners used to sit through the wall. Clear of the 60 cm
		// corner post when there is one, otherwise snug into the corner (0.06 cells = the wall's half thickness; a kit
		// whose corner posts are centred columns asks for more, DungeonKit.CornerGapCells).
		float rad = yaw * MathF.PI / 180f, c = MathF.Abs( MathF.Cos( rad ) ), sn = MathF.Abs( MathF.Sin( rad ) );
		float reachX = (def.Width / 2f * c + def.Depth / 2f * sn) / 5f, reachZ = (def.Width / 2f * sn + def.Depth / 2f * c) / 5f;
		float gap = s.HasPillar ? 0.16f : res.Kit.CornerGapCells;
		float insetX = Math.Max( gap + reachX, 0.12f ), insetZ = Math.Max( gap + reachZ, 0.12f );
		float gx = s.X + (s.D2 == Dir.E ? 1 - insetX : insetX), gz = s.Z + (s.D == Dir.N ? 1 - insetZ : insetZ);
		float inset = Math.Max( insetX, insetZ );
		if ( def.Tile ) occ.ClaimTile( s.X, s.Z, L ); else occ.ClaimQuarter( s.X, s.Z, L, q );
		var model = Add( res, ctx, def, rng, AnchorKind.Floor, s.X, s.Z, L, s.D, gx, gz, 0, def.Lift, yaw, priority );
		// companions run along the north/south wall, away from the east/west one, inside the corner quarter
		Dress( ctx, res, occ, rng, def, model, gx, gz, yaw, (-s.D2.Dx(), 0), Math.Max( 0, (2.5f - inset * 5f - def.Width / 2f) / 5f ) );
	}

	public static bool PlaceMounted( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, Slot s, PropDef def, int priority )
	{
		int L = ctx.T.Level;
		// one mounted prop per wall segment: a banner hangs down through a torch's height
		if ( !occ.WallFree( s.X, s.Z, L, s.D ) ) return false;
		if ( occ.BandHeight( s.X, s.Z, L, s.D ) > 1.3f ) return false;
		if ( def.Id == "banner" && occ.BandHeight( s.X, s.Z, L, s.D ) > 0 ) return false;
		occ.ClaimWall( s.X, s.Z, L, s.D );
		Add( res, ctx, def, rng, AnchorKind.WallMounted, s.X, s.Z, L, s.D, s.X + 0.5f, s.Z + 0.5f, 0, def.MountHeight, YawOf( s.D.Opp() ), priority );
		return true;
	}

	public static void PlaceFloor( TierCtx ctx, DecorResult res, Occupancy occ, DungeonRng rng, PropDef def, float gx, float gz, float yaw, float height, int priority )
	{
		int L = ctx.T.Level;
		int cx = (int)Math.Floor( gx ), cz = (int)Math.Floor( gz );
		if ( !def.Overlay )
		{
			if ( def.Tile ) occ.ClaimTile( cx, cz, L );
			else occ.ClaimQuarter( cx, cz, L, Occupancy.QuarterOf( gx, gz, cx, cz ) );
		}
		Add( res, ctx, def, rng, AnchorKind.Floor, cx, cz, L, Dir.N, gx, gz, 0, height + def.Lift, yaw, priority );
	}

	/// <summary>Resolve the model variant (with decay) and record the placement. Counts the room's lights. Returns the model chosen.</summary>
	/// <param name="embed">Overrides PropDef.Embed for this one placement, in metres into the wall. The greenery's
	/// wall mats roll their own bedding depth per mat, which a def constant cannot express.</param>
	public static string Add( DecorResult res, TierCtx ctx, PropDef def, DungeonRng rng, AnchorKind anchor, int x, int z, int l, Dir edge,
		float gx, float gz, float s, float height, float yaw, int priority, bool countLight = true, float scale = 1f, float? embed = null )
	{
		// a prop reserved for one role (potions in the alchemy room) is dropped anywhere else, whichever
		// table, top, companion or set piece asked for it; a kit may bar a prop outright (no stone fountain in
		// the forge-hold), which is cheaper than editing every table that names it
		if ( def.RoleOnly != DecorRole.None && ctx.Role != def.RoleOnly ) return null;
		if ( res.Kit.Excluded != null && res.Kit.Excluded.Contains( def.Id ) ) return null;
		int v = rng.RandRange( def.Models.Length );
		string model = def.Models[v];
		string attach = def.Attach != null && v < def.Attach.Length ? def.Attach[v] : null;
		if ( def.Id == "banner" ) { model = res.Kit.Banners[res.BannerVariant]; attach = null; }
		if ( def.Decay != null && rng.NextDouble() < 0.1 + 0.5 * ctx.DepthScore )
		{
			model = def.Decay[rng.RandRange( def.Decay.Length )];
			attach = null;
		}
		if ( def.Light != LightKind.None && countLight ) ctx.Lights++;
		yaw = ((yaw % 360) + 360) % 360;
		res.Props.Add( new PropPlacement( def.Id, model, attach, anchor, x, z, l, edge, gx, gz, s, height, yaw, def.Blocks, def.Light, priority, false, ctx.T.Id, embed ?? def.Embed, scale * (def.Scale > 0f ? def.Scale : 1f) ) );
		return model;
	}

	/// <summary>
	/// Whether a prop centred at (gx, gz) with this reach (cell units, from the centre to its farthest edge plus the
	/// wall's half thickness) stays inside the tier: all four corners of its footprint land on the tier's own cells.
	/// ctx.Has tests only the CENTRE, which is how a candle stand beside a bed at the end of a run ended up half
	/// inside the side wall. A tier's edge is a wall or a doorway, and neither wants furniture through it.
	/// </summary>
	public static bool FootprintInside( TierCtx ctx, float gx, float gz, float reach )
	{
		bool In( float x, float z ) => ctx.Has( (int)Math.Floor( x ), (int)Math.Floor( z ) );
		return In( gx - reach, gz - reach ) && In( gx + reach, gz - reach ) && In( gx - reach, gz + reach ) && In( gx + reach, gz + reach );
	}

	/// <summary>A prop's reach for FootprintInside: half its larger extent plus the wall's half thickness, in cells.</summary>
	public static float ReachOf( PropDef def ) => Math.Max( def.Width, def.Depth ) / 2f / 5f + WallHalfCells;

	/// <summary>The wall's half thickness in cell units (0.27 m of a 5 m cell); the same number PlaceAgainstWall's `inward` uses.</summary>
	private const float WallHalfCells = 0.055f;

	/// <summary>
	/// A role table's entries for one slot kind, through the kit: a kit may override a role's table slot by slot
	/// (the forge-hold's own centre pieces and edge props), and anything it does not name is the stone table's.
	/// Both the fill and FillSignature's centre reservation read through here so they agree on what the centre holds.
	/// </summary>
	public static FillEntry[] Entries( DecorResult res, DecorRole role, SlotKind kind )
	{
		if ( res.Kit.Tables != null && res.Kit.Tables.TryGetValue( role, out var over ) && over.TryGetValue( kind, out var own ) ) return own;
		return DecorCatalog.Tables[role].Entries.TryGetValue( kind, out var stone ) ? stone : null;
	}

	public static string Pick( DungeonRng rng, FillEntry[] entries ) => PickEntry( rng, entries ).Id;

	public static FillEntry PickEntry( DungeonRng rng, FillEntry[] entries )
		=> rng.Choices( entries, entries.Select( e => e.Weight ).ToList() );
}
