using System;
using System.Linq;

namespace VoidMage.Npc;

/// <summary>
/// The Quaternius monsters: eight small creatures (bat, dragon, frog, rat, snake, snake_angry, spider, wasp)
/// that reuse the skeleton's components wholesale — <see cref="SkeletonNpc"/> for the host truth and
/// <see cref="SkeletonBody"/> for what every peer sees — with their own vmdl, animgraph, ragdoll and brain.
/// They are fodder: 20 health and 5 damage, arriving in groups of one species. The dragon is the exception,
/// alone and a little stronger (40 / 9), still under a skeleton slave's 60 / 12.
///
/// The table itself is <c>MonsterKinds.Generated.cs</c>, written by tools/gen_monster_prefabs.py out of
/// tools/monster_kinds.py, which is also where the prefabs, brains, graphs and vmdls come from: change a
/// number there and rerun, never here.
///
/// Two of them are not enemies at all: a `Critter` (the frog) has <see cref="SkeletonNpc.Passive"/> set, so it
/// never looks for a target and only hops about. It is still killable by every path that damages anything else.
/// </summary>
public static partial class MonsterKinds
{
	/// <summary>By bare name (`bat`), or with the spawner's prefix (`monster_bat`); null if there is no such monster.</summary>
	public static Entry Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;
		var n = name.Trim().ToLowerInvariant();
		if ( n.StartsWith( "monster_" ) && All.All( m => m.Name != n ) ) n = n["monster_".Length..];
		return All.FirstOrDefault( m => string.Equals( m.Name, n, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>The ones that fight. Critters are placed as ambient life, never as an encounter.</summary>
	public static Entry[] Hostile => All.Where( m => !m.Critter ).ToArray();
}
