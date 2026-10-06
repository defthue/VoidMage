using System;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// Which enemies live in which biome, and in what mix. Until 2026-09-17 every floor rolled the same table, so
/// the gaol and the first stone floor held the same bestiary; the user's roster (below) gives each theme its
/// own cast and lets the armored skeletons take over as the run goes down.
///
/// The kind names are <see cref="VoidMage.Npc.SkeletonSpawner"/> kinds and the monsters are bare
/// MonsterKinds names (`bat`, not `monster_bat`). DungeonBuilder.BaseSkeletonKinds / LightArmorKinds /
/// HeavyArmorKinds stay the FULL tier rosters — the `skeleton_spawn base|light|heavy` console aliases roll
/// from those — and a biome names the subset of them it actually uses.
///
/// This is the one place to tune who shows up where. The head count itself is not here: that is the run's
/// (<see cref="RunFloors"/> Enemies) and the builder's density knobs.
/// </summary>
public static class BiomeEnemies
{
	/// <param name="Base">Base-tier skeletons (slave stats) this biome uses.</param>
	/// <param name="Light">Light-armor skeletons (soldier stats); empty = the tier never appears here.</param>
	/// <param name="Heavy">Heavy-armor skeletons; empty = the tier never appears here.</param>
	/// <param name="Monsters">Hostile Quaternius monsters, bare names. Empty = no monster groups at all.</param>
	/// <param name="SoldierChance">Chance an ordinary body is armored rather than base (0 with no armored kinds).</param>
	/// <param name="HeavyShare">Of that armored roll, the share that is the heavy tier.</param>
	/// <param name="SoulChance">Chance an ordinary body is a Tormented Soul instead (rolled first).</param>
	/// <param name="KnightChance">Chance a room's encounter is one Undead Knight.</param>
	/// <param name="SwordsmanChance">Chance a room's encounter is the swordsman, alone or a pair.</param>
	/// <param name="MonsterGroupChance">Chance a room's group is a monster nest rather than skeletons.</param>
	/// <param name="CritterChance">Chance a room also holds ambient frogs (never an encounter).</param>
	public sealed record Roster(
		string[] Base, string[] Light, string[] Heavy, string[] Monsters,
		float SoldierChance, float HeavyShare, float SoulChance, float KnightChance, float SwordsmanChance,
		float MonsterGroupChance, float CritterChance );

	/// <summary>The user's roster, 2026-09-17. One entry per theme; every theme must have one.</summary>
	private static readonly Roster StoneRoster = new(
		// the shallow floor: fodder and the fliers, no armor anywhere, plenty of frogs
		Base: new[] { "slave", "df_flesh", "corpse" },
		Light: Array.Empty<string>(),
		Heavy: Array.Empty<string>(),
		Monsters: new[] { "bat", "rat", "spider", "wasp" },
		SoldierChance: 0f, HeavyShare: 0f, SoulChance: 0.15f,
		KnightChance: 0f, SwordsmanChance: 0f,
		MonsterGroupChance: 0.45f, CritterChance: 0.35f );

	private static readonly Roster MossyRoster = new(
		// the overgrown halls: the snakes arrive, the Realms skeletons are the only armor, and the place crawls with frogs.
		// The Tormented Soul haunts every floor (user, 2026-09-18), so its chance is the same 0.15 everywhere.
		Base: new[] { "slave", "df_skeleton", "df_flesh", "corpse" },
		Light: new[] { "realms_01", "realms_02", "realms_03" },
		Heavy: Array.Empty<string>(),
		Monsters: new[] { "bat", "rat", "snake", "snake_angry", "spider", "wasp" },
		SoldierChance: 0.25f, HeavyShare: 0f, SoulChance: 0.15f,
		KnightChance: 0f, SwordsmanChance: 0f,
		MonsterGroupChance: 0.4f, CritterChance: 0.5f );

	private static readonly Roster RealmsRoster = new(
		// the forge-hold: everything but the Undead Knight, no fliers of the small kind (no bats, no wasps),
		// fewer frogs than the stone floor and the balance tipping towards armor
		Base: new[] { "slave", "df_skeleton", "df_flesh", "corpse" },
		Light: new[] { "soldier_01", "df_light", "realms_01", "realms_02", "realms_03" },
		Heavy: new[] { "soldier_02", "df_heavy" },
		Monsters: new[] { "rat", "snake", "snake_angry", "spider", "dragon" },
		SoldierChance: 0.45f, HeavyShare: 0.4f, SoulChance: 0.15f,
		KnightChance: 0f, SwordsmanChance: 0.08f,
		MonsterGroupChance: 0.35f, CritterChance: 0.15f );

	private static readonly Roster GaolRoster = new(
		// the gaol: the whole bestiary minus the wasps and the snakes, the Undead Knight included, and nothing
		// as alive as a frog left in it
		Base: new[] { "slave", "df_skeleton", "df_flesh", "corpse" },
		Light: new[] { "soldier_01", "df_light", "realms_01", "realms_02", "realms_03" },
		Heavy: new[] { "soldier_02", "df_heavy" },
		Monsters: new[] { "bat", "rat", "spider", "dragon" },
		SoldierChance: 0.55f, HeavyShare: 0.5f, SoulChance: 0.15f,
		KnightChance: 0.05f, SwordsmanChance: 0.1f,
		MonsterGroupChance: 0.3f, CritterChance: 0f );

	public static Roster For( DungeonTheme theme ) => theme switch
	{
		DungeonTheme.Mossy => MossyRoster,
		DungeonTheme.Realms => RealmsRoster,
		DungeonTheme.Gaol => GaolRoster,
		_ => StoneRoster,
	};

	/// <summary>One of the biome's kinds at random; the fallback when a tier is empty (stone has no armor at all).</summary>
	public static string OneOf( string[] kinds, string[] fallback, Random rng )
	{
		var pool = kinds is { Length: > 0 } ? kinds : fallback;
		return pool[rng.Next( pool.Length )];
	}

	/// <summary>Every kind and monster named anywhere in the table, for the asset check in tools/gentest.</summary>
	public static string[] AllNamed()
	{
		var rosters = new[] { StoneRoster, MossyRoster, RealmsRoster, GaolRoster };
		return rosters.SelectMany( r => r.Base.Concat( r.Light ).Concat( r.Heavy ).Concat( r.Monsters ) ).Distinct().ToArray();
	}
}
