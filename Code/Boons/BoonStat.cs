using System;
using System.Globalization;

namespace VoidMage.Boons;

/// <summary>Every number a boon can change. Percent stats are stored as fractions (0.4 = +40%).</summary>
public enum BoonStat
{
	SwordDamage,
	HeavyDamage,
	SwordCritChance,
	CritDamage,
	SwordReach,
	AttackSpeed,
	SpellDamage,
	CastSpeed,
	ManaCost,
	MaxMana,
	ManaRegen,
	ManaPerSwordHit,
	ManaPerKill,
	MaxHealth,
	DamageTaken,
	HealthRegen,
	HealthPerKill,
	SwordLifeSteal,
	LifeSteal,
	Damage,
	PotionCapacity,
	HealthPotions,
	PotionStrength,
	DrinkSpeed,
	TrapDamage,
	ShardGain,
	PickupRange,
	MoveSpeed,
	SprintSpeed,
	HitInvulnerability,
	// flags: present or not, the value is ignored
	NoManaRegen,
	NoPotions,
	SecretWallsOnMap,
	/// <summary>Every potion slot topped up when the card is taken (Deep Pockets, user 2026-09-17).</summary>
	FillPotions,
}

public enum BoonFamily
{
	Edge,
	Font,
	Vein,
	Hoard,
	Step,
	Pact,
}

/// <summary>One line on a card: a stat and how much. Costs are drawn in red.</summary>
public readonly record struct BoonLine( BoonStat Stat, float Value, bool IsCost = false )
{
	public string Text => BoonText.Format( Stat, Value );
}

/// <summary>
/// Card text is generated from the numbers, never typed by hand, so a retune can never leave a card lying.
/// Every line reads "+40% Sword Damage" / "+60 Max Mana" / "No Potions": a number and what it boosts.
/// </summary>
public static class BoonText
{
	private enum Unit { Percent, Flat, PerSecond, Seconds, Flag }

	private static (string label, Unit unit) Describe( BoonStat stat ) => stat switch
	{
		BoonStat.SwordDamage => ("Sword Damage", Unit.Percent),
		BoonStat.HeavyDamage => ("Heavy Attack Damage", Unit.Percent),
		BoonStat.SwordCritChance => ("Sword Crit Chance", Unit.Percent),
		BoonStat.CritDamage => ("Crit Damage", Unit.Percent),
		BoonStat.SwordReach => ("Sword Reach", Unit.Percent),
		BoonStat.AttackSpeed => ("Attack Speed", Unit.Percent),
		BoonStat.SpellDamage => ("Spell Damage", Unit.Percent),
		BoonStat.CastSpeed => ("Cast Speed", Unit.Percent),
		BoonStat.ManaCost => ("Mana Cost", Unit.Percent),
		BoonStat.MaxMana => ("Max Mana", Unit.Flat),
		BoonStat.ManaRegen => ("Mana Regen", Unit.Percent),
		BoonStat.ManaPerSwordHit => ("Mana per Sword Hit", Unit.Flat),
		BoonStat.ManaPerKill => ("Mana per Kill", Unit.Flat),
		BoonStat.MaxHealth => ("Max Health", Unit.Flat),
		BoonStat.DamageTaken => ("Damage Taken", Unit.Percent),
		BoonStat.HealthRegen => ("Health / Sec", Unit.PerSecond),
		BoonStat.HealthPerKill => ("Health per Kill", Unit.Flat),
		BoonStat.SwordLifeSteal => ("Sword Life Steal", Unit.Percent),
		BoonStat.LifeSteal => ("Life Steal", Unit.Percent),
		BoonStat.Damage => ("Damage", Unit.Percent),
		BoonStat.PotionCapacity => ("Potion Capacity", Unit.Flat),
		BoonStat.HealthPotions => ("Health Potions", Unit.Flat),
		BoonStat.PotionStrength => ("Potion Strength", Unit.Percent),
		BoonStat.DrinkSpeed => ("Drink Speed", Unit.Percent),
		BoonStat.TrapDamage => ("Trap Damage", Unit.Percent),
		BoonStat.ShardGain => ("Shards", Unit.Percent),
		BoonStat.PickupRange => ("Pickup Range", Unit.Percent),
		BoonStat.MoveSpeed => ("Move Speed", Unit.Percent),
		BoonStat.SprintSpeed => ("Sprint Speed", Unit.Percent),
		BoonStat.HitInvulnerability => ("Hit Invulnerability", Unit.Seconds),
		BoonStat.NoManaRegen => ("No Mana Regen", Unit.Flag),
		BoonStat.NoPotions => ("No Potions", Unit.Flag),
		BoonStat.SecretWallsOnMap => ("Secret Walls on Minimap", Unit.Flag),
		BoonStat.FillPotions => ("Potions Filled", Unit.Flag),
		_ => (stat.ToString(), Unit.Flat),
	};

	public static bool IsFlag( BoonStat stat ) => Describe( stat ).unit == Unit.Flag;

	public static string Format( BoonStat stat, float value )
	{
		var (label, unit) = Describe( stat );
		if ( unit == Unit.Flag ) return label;
		var sign = value < 0f ? "-" : "+";
		var abs = MathF.Abs( value );
		var ci = CultureInfo.InvariantCulture;
		return unit switch
		{
			Unit.Percent => $"{sign}{MathF.Round( abs * 100f ).ToString( "0", ci )}% {label}",
			Unit.Seconds => $"{sign}{abs.ToString( "0.##", ci )}s {label}",
			_ => $"{sign}{abs.ToString( "0.##", ci )} {label}",
		};
	}
}
