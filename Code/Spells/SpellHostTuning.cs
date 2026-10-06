using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// The numbers the host reads when a spell hit lands (statuses, The Turncoat, The Hollow's refund), every one a
/// convar so they tune live. Placeholders (MODIFIER_spec.md section 8).
/// </summary>
public static class SpellHostTuning
{
	/// <summary>A cast is depth 0, a proc 1, a minion blast adds 1; nothing procs past this.</summary>
	[ConVar( "spell_max_proc_depth" )] public static int MaxProcDepth { get; set; } = 2;

	[ConVar( "spell_turncoat_seconds" )] public static float TurncoatSeconds { get; set; } = 20f;
	[ConVar( "spell_turncoat_raise_delay" )] public static float TurncoatRaiseDelay { get; set; } = 0.6f;
	[ConVar( "spell_turncoat_max" )] public static int TurncoatMax { get; set; } = 4;
	[ConVar( "spell_turncoat_blast_radius_2" )] public static float TurncoatBlastRadius2 { get; set; } = 120f;
	[ConVar( "spell_turncoat_blast_radius_3" )] public static float TurncoatBlastRadius3 { get; set; } = 180f;
	[ConVar( "spell_turncoat_blast_damage_2" )] public static float TurncoatBlastDamage2 { get; set; } = 25f;
	[ConVar( "spell_turncoat_blast_damage_3" )] public static float TurncoatBlastDamage3 { get; set; } = 40f;
	public static float TurncoatBlastRadius( int stacks ) => stacks >= 3 ? TurncoatBlastRadius3 : stacks == 2 ? TurncoatBlastRadius2 : 0f;
	public static float TurncoatBlastDamage( int stacks ) => stacks >= 3 ? TurncoatBlastDamage3 : stacks == 2 ? TurncoatBlastDamage2 : 0f;

	/// <summary>Under a fireball's 7.5 hp, so a staff that kills with every cast still bleeds (10 at three copies paid for the cast and more, audit 2026-09-22).</summary>
	[ConVar( "spell_hollow_refund_2" )] public static float HollowRefund2 { get; set; } = 3f;
	[ConVar( "spell_hollow_refund_3" )] public static float HollowRefund3 { get; set; } = 6f;
	public static float HollowRefund( int stacks ) => stacks >= 3 ? HollowRefund3 : stacks == 2 ? HollowRefund2 : 0f;


	/// <summary>The Wake's element on the ground: fire burns, ice slows (fraction while standing in it).</summary>
	[ConVar( "spell_wake_fire_dps" )] public static float WakeFireDps { get; set; } = 6f;
	[ConVar( "spell_wake_ice_slow" )] public static float WakeIceSlow { get; set; } = 0.4f;
	[ConVar( "spell_wake_void_dps" )] public static float WakeVoidDps { get; set; } = 4f;

	/// <summary>Burns and hazards tick this many times a second.</summary>
	[ConVar( "spell_dot_hz" )] public static float DotHz { get; set; } = 4f;
	/// <summary>Splash damage at the rim relative to the centre.</summary>
	[ConVar( "spell_splash_rim" )] public static float SplashRim { get; set; } = 0.5f;

	/// <summary>What The Wake of this spell does to whoever stands in it: fire and void burn, ice slows, lightning stings.</summary>
	public static float WakeDps( SpellDefinition spell ) => spell?.Id switch
	{
		SpellIds.Fireball => WakeFireDps,
		SpellIds.VoidOrb => WakeVoidDps,
		SpellIds.ChainLightning => WakeFireDps * 0.5f,
		SpellIds.Laser => WakeFireDps,
		SpellIds.Swarm => WakeFireDps * 0.5f,   // stings
		_ => 0f,
	};

	/// <summary>
	/// Host: damage has just come off a skeleton's bar: the boons' life steal. (The Siphon Bolt leeched here until
	/// 2026-09-21; it heals on a kill now, SpellDeathStage.)
	/// </summary>
	public static void OnDamageDealt( SkeletonNpc npc, in DamageRouter.Hit hit, float dealt ) => OnDamageDealtTo( npc.KindName, hit, dealt );

	/// <summary>The same for anything else that bleeds spell damage (a boss part); `victim` is only for the log.</summary>
	public static void OnDamageDealtTo( string victim, in DamageRouter.Hit hit, float dealt )
	{
		if ( dealt <= 0f ) return;
		VoidMage.Boons.BoonHost.OnDamageDealt( hit, dealt );   // life steal, sword or spell
	}

	public static float WakeSlow( SpellDefinition spell ) => spell?.Id == SpellIds.IceShard ? WakeIceSlow : 0f;

	/// <summary>Host: the statuses this hit leaves on a skeleton (an ice shard's slow, a void orb's burn). Ticks never re-apply.</summary>
	public static void ApplyStatus( SkeletonNpc npc, in DamageRouter.Hit hit )
	{
		var info = hit.Spell;
		if ( !info.IsSpell ) return;
		// the scrolls first, ticks included: a beam and an orb's area only ever deal ticks (SpellHostEffects rates them itself)
		SpellHostEffects.OnHit( npc, hit );
		if ( info.Has( SpellHitFlags.Tick ) ) return;
		var spell = info.Spell;
		if ( spell is null ) return;
		var status = npc.Status;
		if ( spell.SlowFraction > 0f ) status.Slow( spell.SlowFraction, spell.SlowSeconds );
		if ( spell.BurnDps > 0f ) status.Burn( spell.BurnDps, spell.BurnSeconds, hit.Kind, hit.Attacker, info );
	}
}
