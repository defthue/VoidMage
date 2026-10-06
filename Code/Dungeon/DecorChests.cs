using System;
using System.Collections.Generic;

namespace VoidMage.Dungeon;

/// <summary>
/// Takes the chests out of the floor (user, 2026-09-20: "only having chests in the secret room"; it was two in three
/// since 2026-09-16, with one always kept for the floor's guaranteed scroll). The floor's scrolls are carried by
/// monsters now (Run.ScrollDrops) and the secret room is switched off (DungeonGenerator.SecretRooms), so a floor has no chests at all until something brings them back. Runs after every placement
/// pass that can put a chest down (the fill, the sets, the beds' foot chests, the globals, the breakables) and before
/// the potions, so a removed chest never holds one. A removed chest takes what stood on its lid with it (the candles
/// and books the fill's OnTop put there) so nothing is left floating. The space it held stays claimed and simply
/// stays empty. Engine-free and deterministic: its own RNG stream.
/// </summary>
public static class DecorChests
{
	/// <summary>The share of chests removed outside the secret room.</summary>
	public const double RemoveChance = 1.0;

	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs )
	{
		var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, 0xC4E5 ) );
		var props = res.Props;

		// first the rolls: which chests outside the secret room would go
		var dropped = new List<int>();
		int outside = 0;
		for ( int i = 0; i < props.Count; i++ )
		{
			var p = props[i];
			if ( p.Anchor != AnchorKind.Floor || !p.Prop.Contains( "chest", StringComparison.Ordinal ) ) continue;
			// the roll is spent for every chest, secret or not, so one room's chests do not shift another's
			bool drop = rng.NextDouble() < RemoveChance;
			if ( ctxs.TryGetValue( p.TierId, out var ctx ) && ctx.Role == DecorRole.Secret ) continue;
			if ( !res.Kit.Props.ContainsKey( p.Prop ) ) continue;
			outside++;
			if ( drop ) dropped.Add( i );
		}
		if ( dropped.Count == 0 ) return;

		var remove = new HashSet<int>();
		foreach ( var i in dropped )
		{
			var p = props[i];
			var def = res.Kit.Props[p.Prop];
			remove.Add( i );

			// what stands on its lid: small floor-anchored props over its footprint, above its base and below its top
			float reach = MathF.Max( def.Width, def.Depth ) / 2f / 5f;
			for ( int j = 0; j < props.Count; j++ )
			{
				if ( j == i ) continue;
				var q = props[j];
				if ( q.TierId != p.TierId || q.Level != p.Level || q.Anchor != AnchorKind.Floor ) continue;
				if ( q.Height < p.Height + def.Height * 0.5f || q.Height > p.Height + def.Height + 0.3f ) continue;
				if ( MathF.Abs( q.GX - p.GX ) > reach || MathF.Abs( q.GZ - p.GZ ) > reach ) continue;
				remove.Add( j );
			}
		}
		var kept = new List<PropPlacement>( props.Count - remove.Count );
		for ( int i = 0; i < props.Count; i++ )
			if ( !remove.Contains( i ) ) kept.Add( props[i] );
		res.Props = kept;
	}
}
