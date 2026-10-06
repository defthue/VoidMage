using System;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// The one door for damage. Attacks are simulated by their owner (the player's sword and fireball run on the
/// player's client) while enemies are owned by the host, so a hit is described in plain values and handed to
/// whatever on the target knows how to take it: <see cref="IHittable"/> forwards to the host itself.
/// </summary>
public static class DamageRouter
{
	/// <summary>Where the hit came from, for the target's reaction clip and knock direction.</summary>
	public readonly struct Hit
	{
		/// <summary>Damage before the target's own modifiers (guarding).</summary>
		public float Damage { get; init; }
		/// <summary>World point of contact (sounds, flashes).</summary>
		public Vector3 Position { get; init; }
		/// <summary>World direction the hit shoves the target (unit), e.g. the player's right for a left-to-right slash.</summary>
		public Vector3 Push { get; init; }
		/// <summary>Root of whoever attacked (a player root, a skeleton root).</summary>
		public GameObject Attacker { get; init; }
		/// <summary>sword_light, sword_heavy, spell_&lt;id&gt; (spell_fireball...), trap_&lt;kind&gt;, skeleton_sword, skeleton_bash.</summary>
		public string Kind { get; init; }
		/// <summary>The cast this hit belongs to; default for anything that is not a spell (sword, traps, skeletons).</summary>
		public SpellHitInfo Spell { get; init; }
	}

	/// <summary>Something that can be hit. Implementations decide authority (forward to host, apply locally).</summary>
	public interface IHittable
	{
		void ReceiveHit( in Hit hit );
	}

	/// <summary>Finds the hittable on the object or its ancestors and hands it the hit. Returns false when nothing there takes damage.</summary>
	public static bool Apply( GameObject target, in Hit hit )
	{
		if ( !target.IsValid() ) return false;
		var hittable = target.Components.Get<IHittable>( FindMode.EverythingInSelfAndAncestors );
		if ( hittable is null ) return false;
		hittable.ReceiveHit( hit );
		return true;
	}

	public static bool IsBody( GameObject go ) => go.IsValid() && (go.Tags.Has( "enemy" ) || go.Tags.Has( "npc" ));

	/// <summary>A body on the other side from `attacker`: a player's blade and bolts pass over a raised minion.</summary>
	public static bool IsHostileBody( GameObject go, Team attacker ) => IsBody( go ) && CombatTargets.TeamOf( go ) != attacker;

	// ------------------------------------------------------------------ crits and damage classes
	// Only sword hits crit (2026-09-24): spells, traps and ticks never do. Rolled by whoever applies the damage (the
	// host for a skeleton), so the number every peer sees is the one that was subtracted. `combat_crit_chance 1`
	// forces every sword hit to crit for checking the look, 0 disables.
	[ConVar( "combat_crit_chance" )] public static float CritChance { get; set; } = 0.1f;
	[ConVar( "combat_crit_multiplier" )] public static float CritMultiplier { get; set; } = 1.5f;

	public static bool IsSword( string kind ) => kind is "sword_light" or "sword_heavy";

	/// <summary>
	/// A sword hit's crit, the attacker's boons included: the convars plus the attacker's Sword Crit Chance (Honed Edge)
	/// and Crit Damage (Glass Blade). Anything that is not a sword hit (spells, traps, ticks) never crits.
	/// Rolled where the damage is applied.
	/// </summary>
	public static (bool crit, float multiplier) RollCrit( in Hit hit )
	{
		if ( !IsSword( hit.Kind ) ) return (false, 1f);
		var chance = CritChance;
		var multiplier = CritMultiplier;
		if ( VoidMage.Boons.PlayerBoons.Find( hit.Attacker ) is { } boons )
		{
			chance += boons.Get( VoidMage.Boons.BoonStat.SwordCritChance );
			multiplier += boons.Get( VoidMage.Boons.BoonStat.CritDamage );
		}
		var crit = chance > 0f && Game.Random.Float( 0f, 1f ) < chance;
		return (crit, crit ? multiplier : 1f);
	}

	/// <summary>Magic damage (the purple number): every spell kind (`spell_` + spell id) and the flame trap.</summary>
	public static bool IsMagic( string kind ) => IsSpell( kind ) || kind == "trap_flame";

	/// <summary>Damage kinds the staff deals (spell_fireball, spell_ice_shard...), the `trap_` convention for spells.</summary>
	public static bool IsSpell( string kind ) => kind != null && kind.StartsWith( "spell_", StringComparison.Ordinal );
}
