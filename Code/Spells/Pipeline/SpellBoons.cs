using System;
using VoidMage.Boons;

namespace VoidMage.Spells;

/// <summary>
/// What the run's boons do to a spell, in one place for the pipeline's stages and the preview (ResolvedCast) so the
/// two stay in step. Casting is owner-simulated, so these read the local player's boons.
/// </summary>
public static class SpellBoons
{
	/// <summary>Added to the Damage stage's additive percentage (Arcane Might, Archmage, Vigor, Bloodthirst).</summary>
	public static float DamagePct => PlayerBoons.LocalGet( BoonStat.SpellDamage ) + PlayerBoons.LocalGet( BoonStat.Damage );

	/// <summary>Multiplies the mana (or Hollow health) a cast costs (Swift Weave).</summary>
	public static float CostScale => PlayerBoons.LocalScale( BoonStat.ManaCost, 0.1f );

	/// <summary>Divides the charge and cast times (Swift Weave's +40% cast speed = 1 / 1.4 of the time).</summary>
	public static float TimeScale => 1f / PlayerBoons.LocalScale( BoonStat.CastSpeed, 0.2f );
}
