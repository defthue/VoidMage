using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace VoidMage.Boons;

/// <summary>
/// One offer between floors: boon · pact · boon (the pact in the middle), any one of the three can be taken.
/// Cards already owned or taken out by an owned pact never show; cards passed over go back into the pool.
/// A reroll costs RerollCost carried shards and avoids the three just shown whenever the pool allows.
/// </summary>
public sealed class BoonOffer
{
	public const int RerollCost = 30;

	/// <summary>Left, middle (the pact), right. An entry is null only when the pool ran dry.</summary>
	public BoonDefinition[] Cards { get; } = new BoonDefinition[3];

	public static BoonOffer Roll( PlayerBoons owner, BoonOffer previous = null )
	{
		var offer = new BoonOffer();
		var shown = previous?.Cards.Where( c => c is not null ).Select( c => c.Id ).ToHashSet() ?? new HashSet<string>();

		var boons = Pick( Eligible( owner, pacts: false ), 2, shown );
		var pact = Pick( Eligible( owner, pacts: true ), 1, shown );
		offer.Cards[0] = boons.ElementAtOrDefault( 0 );
		offer.Cards[1] = pact.ElementAtOrDefault( 0 );
		offer.Cards[2] = boons.ElementAtOrDefault( 1 );
		return offer;
	}

	private static List<BoonDefinition> Eligible( PlayerBoons owner, bool pacts )
	{
		var excluded = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( owner.IsValid() )
			foreach ( var b in owner.Owned )
				foreach ( var id in b.Excludes ) excluded.Add( id );
		return BoonDatabase.All
			.Where( b => b.IsPact == pacts )
			.Where( b => !excluded.Contains( b.Id ) )
			.Where( b => !owner.IsValid() || !owner.Owns( b.Id ) )
			.ToList();
	}

	/// <summary>`count` random cards, preferring ones not in `avoid`; topped up from `avoid` when the fresh ones run out.</summary>
	private static List<BoonDefinition> Pick( List<BoonDefinition> pool, int count, HashSet<string> avoid )
	{
		var fresh = pool.Where( b => !avoid.Contains( b.Id ) ).ToList();
		var stale = pool.Where( b => avoid.Contains( b.Id ) ).ToList();
		Shuffle( fresh );
		Shuffle( stale );
		return fresh.Concat( stale ).Take( count ).ToList();
	}

	private static void Shuffle<T>( List<T> list )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = Game.Random.Int( 0, i );
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
