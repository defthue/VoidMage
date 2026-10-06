using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Boons;

public sealed class BoonDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public BoonFamily Family { get; init; }
	/// <summary>The card, top to bottom: the bonuses, then any cost (drawn red).</summary>
	public BoonLine[] Lines { get; init; } = Array.Empty<BoonLine>();
	/// <summary>Cards this one takes out of the pool once owned (a pact that makes them useless).</summary>
	public string[] Excludes { get; init; } = Array.Empty<string>();

	public bool IsPact => Family == BoonFamily.Pact;
}

/// <summary>
/// The boons and pacts (designed with the user 2026-09-16): every card is two plain stat lines, pacts add a cost.
/// Same-stat bonuses add together (PlayerBoons sums them). Numbers are the tuning; the one-line-a-stat shape is the design.
///
/// Retuned 2026-09-20 (user: "they were made with 1 floor per biome in mind but now were gonna do 2 per biome"): a pick
/// comes after every floor, so a full run is EIGHT picks where the numbers were written for four. Every card is worth
/// about 0.6 of what it was (pact costs with it), so eight picks land a little above where four used to. The per-kill
/// lines came down further: a floor carries three times the bodies it did when they were written.
/// </summary>
public static class BoonDatabase
{
	private static BoonLine L( BoonStat stat, float value ) => new( stat, value );
	private static BoonLine Cost( BoonStat stat, float value = 0f ) => new( stat, value, IsCost: true );

	public static readonly BoonDefinition[] All =
	{
		// ⚔ Edge: the sword
		new() { Id = "honed_edge", Name = "Honed Edge", Family = BoonFamily.Edge, Lines = new[] { L( BoonStat.SwordDamage, 0.25f ), L( BoonStat.SwordCritChance, 0.12f ) } },
		new() { Id = "long_blade", Name = "Long Blade", Family = BoonFamily.Edge, Lines = new[] { L( BoonStat.SwordReach, 0.30f ), L( BoonStat.SwordDamage, 0.15f ) } },
		new() { Id = "bladestorm", Name = "Bladestorm", Family = BoonFamily.Edge, Lines = new[] { L( BoonStat.AttackSpeed, 0.18f ), L( BoonStat.SwordDamage, 0.12f ) } },
		new() { Id = "brute", Name = "Brute", Family = BoonFamily.Edge, Lines = new[] { L( BoonStat.HeavyDamage, 0.60f ), Cost( BoonStat.AttackSpeed, -0.10f ) } },

		// ✦ Font: mana and the staff
		new() { Id = "wellspring", Name = "Wellspring", Family = BoonFamily.Font, Lines = new[] { L( BoonStat.MaxMana, 35f ), L( BoonStat.ManaRegen, 0.50f ) } },
		new() { Id = "swift_weave", Name = "Swift Weave", Family = BoonFamily.Font, Lines = new[] { L( BoonStat.CastSpeed, 0.25f ), L( BoonStat.ManaCost, -0.15f ) } },
		new() { Id = "spellsword", Name = "Spellsword", Family = BoonFamily.Font, Lines = new[] { L( BoonStat.ManaPerSwordHit, 2f ), L( BoonStat.SpellDamage, 0.12f ) } },
		new() { Id = "arcane_might", Name = "Arcane Might", Family = BoonFamily.Font, Lines = new[] { L( BoonStat.SpellDamage, 0.25f ), L( BoonStat.ManaPerKill, 3f ) } },

		// ❤ Vein: health
		new() { Id = "ironblood", Name = "Ironblood", Family = BoonFamily.Vein, Lines = new[] { L( BoonStat.MaxHealth, 30f ), L( BoonStat.DamageTaken, -0.10f ) } },
		new() { Id = "mending", Name = "Mending", Family = BoonFamily.Vein, Lines = new[] { L( BoonStat.HealthRegen, 0.2f ), L( BoonStat.HealthPerKill, 1f ) } },
		new() { Id = "vampiric", Name = "Vampiric", Family = BoonFamily.Vein, Lines = new[] { L( BoonStat.SwordLifeSteal, 0.04f ), L( BoonStat.MaxHealth, 12f ) } },
		new() { Id = "vigor", Name = "Vigor", Family = BoonFamily.Vein, Lines = new[] { L( BoonStat.MaxHealth, 25f ), L( BoonStat.Damage, 0.12f ) } },

		// ◈ Hoard: potions, shards, the dungeon
		// two more slots of each kind and every slot filled on the spot (user, 2026-09-17: not just two health potions)
		new() { Id = "deep_pockets", Name = "Deep Pockets", Family = BoonFamily.Hoard, Lines = new[] { L( BoonStat.PotionCapacity, 1f ), L( BoonStat.FillPotions, 1f ) } },
		new() { Id = "alchemist", Name = "Alchemist", Family = BoonFamily.Hoard, Lines = new[] { L( BoonStat.PotionStrength, 0.60f ), L( BoonStat.DrinkSpeed, 0.60f ) } },
		new() { Id = "dungeonwise", Name = "Dungeonwise", Family = BoonFamily.Hoard, Lines = new[] { L( BoonStat.TrapDamage, -0.50f ), L( BoonStat.ShardGain, 0.30f ) } },
		new() { Id = "hoarder", Name = "Hoarder", Family = BoonFamily.Hoard, Lines = new[] { L( BoonStat.ShardGain, 0.60f ), L( BoonStat.PickupRange, 0.60f ) } },

		// ➤ Step: movement
		new() { Id = "fleetfoot", Name = "Fleetfoot", Family = BoonFamily.Step, Lines = new[] { L( BoonStat.MoveSpeed, 0.15f ), L( BoonStat.ManaRegen, 0.25f ) } },
		new() { Id = "sprinter", Name = "Sprinter", Family = BoonFamily.Step, Lines = new[] { L( BoonStat.SprintSpeed, 0.25f ), L( BoonStat.SwordDamage, 0.12f ) } },
		new() { Id = "nimble", Name = "Nimble", Family = BoonFamily.Step, Lines = new[] { L( BoonStat.MoveSpeed, 0.10f ), L( BoonStat.AttackSpeed, 0.12f ) } },
		new() { Id = "ghost", Name = "Ghost", Family = BoonFamily.Step, Lines = new[] { L( BoonStat.HitInvulnerability, 0.3f ), L( BoonStat.MoveSpeed, 0.10f ) } },

		// 🩸 Pacts: two gains and a cost
		new() { Id = "glass_blade", Name = "Glass Blade", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.SwordDamage, 0.50f ), L( BoonStat.CritDamage, 0.60f ), Cost( BoonStat.DamageTaken, 0.25f ) } },
		new() { Id = "berserker", Name = "Berserker", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.AttackSpeed, 0.30f ), L( BoonStat.SwordDamage, 0.25f ), Cost( BoonStat.MaxHealth, -25f ) } },
		new() { Id = "archmage", Name = "Archmage", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.SpellDamage, 0.50f ), L( BoonStat.MaxMana, 35f ), Cost( BoonStat.SwordDamage, -0.30f ) } },
		new() { Id = "blood_mana", Name = "Blood Mana", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.ManaPerSwordHit, 4f ), L( BoonStat.SpellDamage, 0.30f ), Cost( BoonStat.NoManaRegen ) }, Excludes = new[] { "wellspring", "fleetfoot" } },
		new() { Id = "juggernaut", Name = "Juggernaut", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.MaxHealth, 60f ), L( BoonStat.DamageTaken, -0.15f ), Cost( BoonStat.MoveSpeed, -0.15f ) } },
		new() { Id = "vow_of_emptiness", Name = "Vow of Emptiness", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.MaxHealth, 35f ), L( BoonStat.HealthRegen, 0.3f ), Cost( BoonStat.NoPotions ) }, Excludes = new[] { "deep_pockets", "alchemist", "bloodthirst" } },
		new() { Id = "bloodthirst", Name = "Bloodthirst", Family = BoonFamily.Pact, Lines = new[] { L( BoonStat.LifeSteal, 0.06f ), L( BoonStat.Damage, 0.18f ), Cost( BoonStat.PotionStrength, -0.30f ) } },
	};

	private static readonly Dictionary<string, BoonDefinition> ById = All.ToDictionary( b => b.Id, StringComparer.OrdinalIgnoreCase );

	public static BoonDefinition Get( string id ) => id is not null && ById.TryGetValue( id, out var def ) ? def : null;

	/// <summary>
	/// The placeholder card art: one elder futhark rune per family, drawn in Noto Sans Runic (already shipped for
	/// the death screen; Almendra has no ⚔-style symbols). Tiwaz the warrior, Laguz the water, Uruz the strength,
	/// Fehu the wealth, Ehwaz the horse, Thurisaz the thorn.
	/// </summary>
	public static string Glyph( BoonFamily family ) => family switch
	{
		BoonFamily.Edge => "ᛏ",
		BoonFamily.Font => "ᛚ",
		BoonFamily.Vein => "ᚢ",
		BoonFamily.Hoard => "ᚠ",
		BoonFamily.Step => "ᛖ",
		_ => "ᚦ",
	};
}
