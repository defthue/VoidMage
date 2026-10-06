using System;

namespace VoidMage.Spells;

/// <summary>
/// The numbers a spell resolves to on a given staff, computed without side effects: what the HUD shows and what
/// `spell_debug` logs, from the same arithmetic the pipeline's Cost/Emission/Damage stages run, so the two can never
/// disagree. Not the cast itself: no mana is spent, nothing is scheduled. It is a TAP: Gluttony's hold, an
/// overcharged cast and The Last Drop are moments, not the staff's resting numbers.
/// </summary>
public readonly record struct ResolvedCast(
	string SpellId,
	float ManaCost,
	float HealthCost,
	float DamageMultiplier,
	float Damage,
	int Count,
	float FanDegrees,
	float CastSeconds,
	int Rings,
	int Bounces,
	int Echoes,
	bool Mine,
	bool Wake,
	bool Primed,
	bool Hound,
	float WrathPct,
	float KillHeal )
{
	/// <summary>
	/// `primed`: a sword hit has primed the staff, so Kindling's discount applies to this cast.
	/// `missing01`: the caster's missing health fraction (Wrath's fuel).
	/// </summary>
	public static ResolvedCast Preview( SpellDefinition spell, ModifierStacks stacks, int shards, bool primed = false, float missing01 = 0f )
	{
		if ( spell is null ) return default;
		var kindling = stacks[ModifierId.Kindling];
		var hollow = stacks[ModifierId.Hollow];

		var cost = CostStage.BaseCost( spell, stacks, 0f );
		var primedNow = kindling > 0 && primed;
		if ( primedNow ) cost *= 1f - ModifierTables.KindlingDiscount[kindling];
		var mana = hollow > 0 ? 0f : cost;
		var health = hollow > 0 ? cost * ModifierTables.HollowHealthPerMana : 0f;

		var mult = DamageStage.Multiplier( stacks, shards, missing01, 0f, false, false, false );
		var m = stacks[ModifierId.Multiplication];
		return new ResolvedCast(
			spell.Id,
			mana,
			health,
			mult,
			spell.Damage * mult * ModifierTables.MultiplicationBoltDamage[m] * (spell.Delivery == SpellDelivery.Beam ? 1f : ModifierTables.OrbitBoltDamage[stacks[ModifierId.Orbit]]),   // per bolt, as the Damage stage resolves it
			Math.Max( 1, spell.ProjectileCount ) * ModifierTables.MultiplicationCount[m],
			ModifierTables.MultiplicationFanDegrees[m],
			CastSecondsOf( spell, stacks ),
			ModifierTables.OrbitRings[stacks[ModifierId.Orbit]],
			ModifierTables.RicochetBounces[stacks[ModifierId.Ricochet]],
			ModifierTables.EchoRepeats[stacks[ModifierId.Echo]],
			stacks.Has( ModifierId.Amber ),
			stacks.Has( ModifierId.Wake ),
			primedNow,
			stacks.Has( ModifierId.Hound ),
			ModifierTables.WrathPct( missing01, stacks[ModifierId.Wrath] ),
			spell.KillHeal );
	}

	/// <summary>The beat between two casts with the button held: the spell's follow-through, halved and more by The Hummingbird, scaled by the boons.</summary>
	public static float CastSecondsOf( SpellDefinition spell, ModifierStacks stacks )
		=> spell.CastDurationSeconds * ModifierTables.HummingbirdCastTimeMul[stacks[ModifierId.Hummingbird]] * SpellBoons.TimeScale;

	public override string ToString()
	{
		var extras = "";
		if ( Rings > 0 ) extras += $" orbit x{Rings}";
		if ( Bounces > 0 ) extras += $" bounces {Bounces}";
		if ( Echoes > 0 ) extras += $" echo x{Echoes}";
		if ( Mine ) extras += " amber";
		if ( Wake ) extras += " wake";
		if ( Primed ) extras += " primed";
		if ( Hound ) extras += " hound";
		if ( WrathPct > 0f ) extras += $" wrath +{WrathPct * 100f:0}%";
		if ( KillHeal > 0f ) extras += $" kill heals {KillHeal:0}";
		var cost = HealthCost > 0f ? $"{HealthCost:0.#} hp" : $"{ManaCost:0.#} mana";
		return $"{SpellId}: {cost}, dmg {Damage:0.#} (x{DamageMultiplier:0.00}), count {Count}{(FanDegrees > 0f ? $" fan {FanDegrees:0}°" : "")}, cast {CastSeconds:0.00}s{extras}";
	}
}
