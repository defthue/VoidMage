using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The player's mana: the only thing that limits casting (no cooldowns). A slow passive regeneration always ticks,
/// paused for a moment after every spend so a spam of cheap casts still runs the pool dry. Owner-authoritative like
/// the rest of the player. `spell_infinite_mana 1` makes every spend free for testing.
/// </summary>
[Title( "Player Mana" ), Category( "Void Mage" ), Icon( "water_drop" )]
public sealed class PlayerMana : Component
{
	/// <summary>The base pool; <see cref="EffectiveMaxMana"/> adds the run's boons.</summary>
	[Property] public float MaxMana { get; set; } = 100f;
	/// <summary>Points per second, always ticking. MAGIC_concept: slow, a burst resource you cycle back into (user, 2026-09-17: 1/s; 2026-09-23: one point every 0.75 s).</summary>
	[Property] public float RegenPerSecond { get; set; } = 1f / 0.75f;
	/// <summary>Seconds of no regeneration after a spend.</summary>
	[Property] public float RegenDelaySeconds { get; set; } = 1f;
	/// <summary>
	/// Mana for every body a sword swing lands on, before any boon (user, 2026-09-19): the sword feeds the staff, so
	/// a fight is slash to charge, cast to clear. Deliberately small and nothing per kill, so the blue potions and
	/// the altar stay worth finding; Spellsword and Blood Mana add theirs on top.
	/// </summary>
	[Property] public float ManaPerSwordHit { get; set; } = 1f;

	[Sync] public float Mana { get; set; } = 100f;

	/// <summary>Every spend succeeds and costs nothing.</summary>
	[ConVar( "spell_infinite_mana" )] public static bool Infinite { get; set; }

	/// <summary>The local player's, for HUD panels and the caster.</summary>
	public static PlayerMana Local { get; private set; }

	/// <summary>The pool with the run's boons (Wellspring, Archmage).</summary>
	public float EffectiveMaxMana => MathF.Max( 1f, MaxMana + (RunBoons?.Get( VoidMage.Boons.BoonStat.MaxMana ) ?? 0f) );

	/// <summary>Regeneration per second with the run's boons; nothing under Blood Mana.</summary>
	public float EffectiveRegen
	{
		get
		{
			var boons = RunBoons;
			if ( boons is null ) return RegenPerSecond;
			if ( boons.Has( VoidMage.Boons.BoonStat.NoManaRegen ) ) return 0f;
			return RegenPerSecond * boons.Scale( VoidMage.Boons.BoonStat.ManaRegen, 0f );
		}
	}

	private VoidMage.Boons.PlayerBoons _boons;
	private VoidMage.Boons.PlayerBoons RunBoons => _boons.IsValid() ? _boons : (_boons = VoidMage.Boons.PlayerBoons.Find( GameObject ));

	public float Mana01 => EffectiveMaxMana > 0f ? Math.Clamp( Mana / EffectiveMaxMana, 0f, 1f ) : 0f;
	public float LastSpendAt { get; private set; } = float.NegativeInfinity;

	protected override void OnStart()
	{
		Mana = MaxMana;
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( Infinite ) { Mana = EffectiveMaxMana; return; }
		if ( Time.Now < LastSpendAt + RegenDelaySeconds ) return;
		if ( Mana > EffectiveMaxMana ) { Mana = EffectiveMaxMana; return; }   // the pool shrank (boons cleared)
		var regen = EffectiveRegen;
		if ( Mana < EffectiveMaxMana ) Mana = MathF.Min( EffectiveMaxMana, Mana + regen * Time.Delta );
		// The Hollow: spells are paid in blood, so the mana that comes back is health that comes back too, at the same
		// rate (user, 2026-09-23: a lone Hollow "costs way too much without anything else").
		if ( regen > 0f && (Spells.StaffLoadout.Local?.HasModifier( Spells.ModifierId.Hollow ) ?? false) )
		{
			var health = Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndAncestors );
			if ( health.IsValid() && !health.IsDead && health.Health < health.EffectiveMaxHealth ) health.Heal( regen * Time.Delta );
		}
	}

	public bool CanAfford( float amount ) => Infinite || amount <= 0f || Mana >= amount - 0.001f;

	/// <summary>Owner: pay `amount`; false and nothing spent when the pool cannot cover it.</summary>
	public bool TrySpend( float amount )
	{
		if ( IsProxy ) return false;
		if ( Infinite || amount <= 0f ) return true;
		if ( Mana < amount - 0.001f ) return false;
		Mana = MathF.Max( 0f, Mana - amount );
		LastSpendAt = Time.Now;
		return true;
	}

	/// <summary>Owner, per frame while a beam burns: drains `perSecond`; false the frame the pool runs dry.</summary>
	public bool Drain( float perSecond )
	{
		if ( IsProxy ) return false;
		if ( Infinite || perSecond <= 0f ) return true;
		if ( Mana <= 0f ) return false;
		Mana = MathF.Max( 0f, Mana - perSecond * Time.Delta );
		LastSpendAt = Time.Now;
		return Mana > 0f;
	}

	public void Refill()
	{
		if ( IsProxy ) return;
		Mana = EffectiveMaxMana;
	}

	/// <summary>The host's reward for a kill (Arcane Might): runs on every peer, only the owner applies it.</summary>
	[Rpc.Broadcast]
	public void Refund( float amount )
	{
		if ( IsProxy ) return;
		Restore( amount );
	}

	/// <summary>Owner: `amount` back into the pool, capped at the max (a mana potion).</summary>
	public void Restore( float amount )
	{
		if ( IsProxy || amount <= 0f ) return;
		Mana = MathF.Min( EffectiveMaxMana, Mana + amount );
	}
}
