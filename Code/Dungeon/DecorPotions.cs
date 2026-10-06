using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// The consumable potions (DecorCatalog.PotionHealId / PotionManaId). No per-floor budget (user 2026-09-16): EVERY
/// table and EVERY chest rolls <see cref="HoldChance"/> to hold one, healing <see cref="HealShare"/> of the time,
/// mana otherwise. A TABLE is any floor def with "table" in its id and a Top surface (not a decayed twin); its potion
/// stands in a free spot on the top, clear of the candles and books the fill put there first (skipped only when the
/// top is full). A CHEST is
/// any def with "chest" in its id: the potion is placed at the chest's own spot with PropPlacement.Inside, spawned
/// hidden, and comes out when the chest is broken open (DungeonBuilder, PotionPickup). A broken table drops its potion
/// to the floor instead of flinging it away. A floor holds <see cref="MinPerFloor"/> to <see cref="MaxPerFloor"/>
/// potions (user 2026-09-16): past the cap the extra winners are dropped at random, under the minimum tables and chests
/// that lost their roll are drawn at random until it is met (or every table and chest is used). No table names these
/// ids: this pass is the only spawner.
/// Engine-free and deterministic like the rest of Decor: its own RNG stream, added after every other pass.
/// </summary>
public static class DecorPotions
{
	/// <summary>The chance a table or a chest holds a potion.</summary>
	public const double HoldChance = 0.5;
	/// <summary>Of the potions, the share that heal (the rest restore mana).</summary>
	public const double HealShare = 0.6;
	/// <summary>Potions per floor: at least this many when the floor has the tables and chests for them, never more than the max.</summary>
	public const int MinPerFloor = 8, MaxPerFloor = 15;

	private readonly record struct Spot( PropPlacement Parent, PropDef Def, TierCtx Ctx, bool Chest, List<(float gx, float gz, float r)> Items );

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ )
	{
		if ( !res.Kit.Props.TryGetValue( DecorCatalog.PotionHealId, out var heal ) || !res.Kit.Props.TryGetValue( DecorCatalog.PotionManaId, out var mana ) ) return;
		var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, 0x9071 ) );
		// every candidate rolls in placement order, both rolls taken whether or not it holds one, so a table's result
		// does not shift every roll after it
		var won = new List<(Spot spot, PropDef def)>();
		var lost = new List<(Spot spot, PropDef def)>();
		foreach ( var spot in Candidates( res, ctxs ) )
		{
			bool holds = rng.NextDouble() < HoldChance;
			var def = rng.NextDouble() < HealShare ? heal : mana;
			(holds ? won : lost).Add( (spot, def) );
		}
		// the winners in a random order go first, then the losers in a random order only while under the minimum: a
		// table whose top is full places nothing, so the counts are of potions actually placed
		rng.Shuffle( won );
		rng.Shuffle( lost );
		int placed = 0;
		// every table in an alchemy room holds one, whatever it rolled (user 2026-09-16), before the cap is counted
		// down; its top is usually crowded with vials and books, so a full top gives up its smallest item for it
		foreach ( var list in new[] { won, lost } )
		{
			for ( int i = list.Count - 1; i >= 0; i-- )
			{
				var (spot, def) = list[i];
				if ( spot.Chest || spot.Ctx.Role != DecorRole.Alchemy ) continue;
				list.RemoveAt( i );
				if ( TryOnTop( res, rng, spot, def ) || ReplaceSmallest( res, rng, spot, def ) ) placed++;
			}
		}
		foreach ( var (spot, def) in won )
		{
			if ( placed >= MaxPerFloor ) break;
			if ( Place( res, rng, spot, def ) ) placed++;
		}
		foreach ( var (spot, def) in lost )
		{
			if ( placed >= MinPerFloor ) break;
			if ( Place( res, rng, spot, def ) ) placed++;
		}
	}

	/// <summary>A full table top: the smallest thing on it (a vial, a candle) makes way and the potion stands where it stood.</summary>
	private static bool ReplaceSmallest( DecorResult res, DungeonRng rng, Spot s, PropDef def )
	{
		if ( s.Items is not { Count: > 0 } ) return false;
		var p = s.Parent;
		var smallest = s.Items.OrderBy( it => it.r ).First();
		int idx = res.Props.FindIndex( q => q.TierId == p.TierId && q.Level == p.Level && q.Anchor == AnchorKind.Floor && q.GX == smallest.gx && q.GZ == smallest.gz );
		if ( idx < 0 ) return false;
		var old = res.Props[idx];
		res.Props.RemoveAt( idx );
		res.Kit.TopFix.TryGetValue( p.Model, out var fix );
		float top = s.Def.Top - fix.drop;
		int before = res.Props.Count;
		Decor.Add( res, s.Ctx, def, rng, AnchorKind.Floor, old.X, old.Z, p.Level, Dir.N, old.GX, old.GZ, 0, top + def.Lift - def.TopSink, rng.RandRange( 360 ), 3 );
		return res.Props.Count > before;
	}

	private static bool Place( DecorResult res, DungeonRng rng, Spot spot, PropDef def )
		=> spot.Chest ? InChest( res, rng, spot, def ) : TryOnTop( res, rng, spot, def );

	/// <summary>A table: a floor prop with a top surface and "table" in its id (long, round, low, stone, altar tables).</summary>
	private static bool IsTable( PropDef def ) => def.Top > 0f && def.Id.Contains( "table", StringComparison.Ordinal );

	/// <summary>A chest: "chest" in its id (the pack chests, the wooden chest).</summary>
	private static bool IsChest( PropDef def ) => def.Id.Contains( "chest", StringComparison.Ordinal );

	/// <summary>Every placed table and every placed chest, in placement order; a table carries what already stands on its top.</summary>
	private static List<Spot> Candidates( DecorResult res, Dictionary<int, TierCtx> ctxs )
	{
		var spots = new List<Spot>();
		var props = res.Props;
		for ( int i = 0; i < props.Count; i++ )
		{
			var p = props[i];
			if ( !res.Kit.Props.TryGetValue( p.Prop, out var def ) ) continue;
			if ( p.Anchor != AnchorKind.Floor ) continue;
			if ( def.Decay != null && Array.IndexOf( def.Decay, p.Model ) >= 0 ) continue;
			if ( !ctxs.TryGetValue( p.TierId, out var ctx ) ) continue;
			if ( IsChest( def ) )
			{
				spots.Add( new Spot( p, def, ctx, Chest: true, null ) );
				continue;
			}
			if ( !IsTable( def ) ) continue;
			res.Kit.TopFix.TryGetValue( p.Model, out var fix );
			float top = def.Top - fix.drop;
			// the items already on it (candles, books): the fill's OnTop put them at exactly the top height
			float reach = MathF.Max( def.Width, def.Depth ) / 2f / 5f + 0.02f;
			var items = new List<(float gx, float gz, float r)>();
			for ( int j = 0; j < props.Count; j++ )
			{
				if ( j == i ) continue;
				var q = props[j];
				if ( q.TierId != p.TierId || q.Level != p.Level || q.Anchor != AnchorKind.Floor ) continue;
				if ( MathF.Abs( q.Height - top ) > 0.15f ) continue;
				if ( MathF.Abs( q.GX - p.GX ) > reach || MathF.Abs( q.GZ - p.GZ ) > reach ) continue;
				float r = res.Kit.Props.TryGetValue( q.Prop, out var qd ) ? MathF.Max( qd.Width, qd.Depth ) / 2f : 0.2f;
				items.Add( (q.GX, q.GZ, r) );
			}
			spots.Add( new Spot( p, def, ctx, Chest: false, items ) );
		}
		return spots;
	}

	/// <summary>Tries at finding a clear spot on a table top before giving up on it.</summary>
	private const int TopTries = 12;
	/// <summary>Gap kept between the potion and anything already on the top (metres).</summary>
	private const float TopGap = 0.04f;

	/// <summary>Shut inside a chest: at the chest's own spot and floor height, marked Inside so it spawns hidden.</summary>
	private static bool InChest( DecorResult res, DungeonRng rng, Spot s, PropDef def )
	{
		var p = s.Parent;
		int before = res.Props.Count;
		Decor.Add( res, s.Ctx, def, rng, AnchorKind.Floor, p.X, p.Z, p.Level, p.Edge, p.GX, p.GZ, 0, p.Height + def.Lift, rng.RandRange( 360 ), 3 );
		if ( res.Props.Count == before ) return false;
		res.Props[^1] = res.Props[^1] with { Inside = true };
		return true;
	}

	/// <summary>
	/// A free spot on the table top, clear of what already stands there: random spots over the whole top in the
	/// table's own axes (PlaceOnTop's: Width along its left, Depth along its forward, the kit's back offset), the first
	/// one far enough from every item wins. A full top gets no potion. Always spends TopTries rolls.
	/// </summary>
	private static bool TryOnTop( DecorResult res, DungeonRng rng, Spot s, PropDef def )
	{
		var p = s.Parent;
		var t = s.Def;
		res.Kit.TopFix.TryGetValue( p.Model, out var fix );
		float top = t.Top - fix.drop;
		float rad = p.Yaw * MathF.PI / 180f;
		float fx = MathF.Cos( rad ), fz = MathF.Sin( rad );
		float ax = -fz, az = fx;
		float bx = -fx * fix.back / 5f, bz = -fz * fix.back / 5f;
		float potionR = MathF.Max( def.Width, def.Depth ) / 2f;
		// metres the centre may wander from the middle, keeping the bottle on the top with a small margin
		float spanU = MathF.Max( 0f, t.Width / 2f - potionR - 0.05f );
		float spanV = MathF.Max( 0f, t.Depth / 2f - potionR - 0.05f - MathF.Abs( fix.back ) );

		(float gx, float gz)? found = null;
		for ( int k = 0; k < TopTries; k++ )
		{
			float u = (float)(rng.NextDouble() * 2 - 1) * spanU;
			float v = (float)(rng.NextDouble() * 2 - 1) * spanV;
			if ( found.HasValue ) continue;
			float gx = p.GX + (ax * u + fx * v) / 5f + bx;
			float gz = p.GZ + (az * u + fz * v) / 5f + bz;
			bool clear = true;
			foreach ( var (ix, iz, r) in s.Items )
			{
				float dx = (gx - ix) * 5f, dz = (gz - iz) * 5f;
				float need = potionR + r + TopGap;
				if ( dx * dx + dz * dz < need * need ) { clear = false; break; }
			}
			if ( clear ) found = (gx, gz);
		}
		float yaw = rng.RandRange( 360 );
		if ( found is not { } at ) return false;
		int before = res.Props.Count;
		Decor.Add( res, s.Ctx, def, rng, AnchorKind.Floor, (int)MathF.Floor( at.gx ), (int)MathF.Floor( at.gz ), p.Level, Dir.N, at.gx, at.gz, 0, top + def.Lift - def.TopSink, yaw, 3 );
		return res.Props.Count > before;
	}
}
