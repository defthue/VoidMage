using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// Bones underfoot on the floors that are not the gaol (user: skeletons walk every biome, so every floor gets about a
/// quarter of the gaol's bone carpet). A second litter layer over the first, drawn from DecorCatalog.BoneLitter at the
/// kit's BoneLitterScale times the same densities the ordinary litter uses: the role table's in a room, the depth ramp
/// in a corridor. It runs after every other pass on its own streams and only takes quarters nothing else littered, so
/// everything placed before it is byte-identical.
/// </summary>
public static class DecorBoneLitter
{
	public static void Run( DungeonLayout lay, DecorResult res, Dictionary<int, TierCtx> ctxs, Occupancy occ )
	{
		double scale = res.Kit.BoneLitterScale;
		if ( scale <= 0 ) return;
		foreach ( var ctx in ctxs.Values.OrderBy( c => c.T.Id ) )
		{
			if ( ctx.T.Kind == TierKind.Tower ) continue;
			double density = ctx.Role == DecorRole.None
				? 0.2 + 0.2 * ctx.DepthScore
				: DecorCatalog.Tables[ctx.Role].Density.TryGetValue( SlotKind.Litter, out var ld ) ? ld : 0.35;
			var rng = new DungeonRng( DungeonRng.Mix( lay.Seed, DungeonRng.Mix( ctx.T.Id, 0xB0E5 ) ) );
			Decor.Litter( ctx, res, occ, rng, DecorCatalog.BoneLitter, density * scale );
		}
	}
}
