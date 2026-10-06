using System;
using Sandbox;

namespace VoidMage.Npc;

/// <summary>
/// A personality for the skeleton: how eager it attacks, how much it hides behind the shield, how it moves
/// around the player. Assets live in Assets/Npc/*.brain; the spawner picks one per skeleton and jitters
/// every number a little so two skeletons with the same profile still differ.
/// </summary>
[GameResource( "Skeleton Brain", "brain", "How a skeleton fights: attack cadence, guarding, shield bashing, footwork.", Icon = "psychology" )]
public sealed class SkeletonBrainProfile : GameResource
{
	// ---------------------------------------------------------------- attacking
	/// <summary>Seconds between attack decisions once in range (rolled between min and max each time).</summary>
	[Property, Group( "Attacking" )] public float AttackCooldownMin { get; set; } = 1.2f;
	[Property, Group( "Attacking" )] public float AttackCooldownMax { get; set; } = 2.0f;
	/// <summary>Chance the attack is the telegraphed charge heavy instead of a plain forward sword attack.</summary>
	[Property, Group( "Attacking" ), Range( 0f, 1f )] public float ChargeChance { get; set; } = 0.3f;
	/// <summary>Chance the attack is the shield bash (guard_attack_parry: a swing of the shield, short reach, armored); the rest are the two sword swings.</summary>
	[Property, Group( "Attacking" ), Range( 0f, 1f )] public float ShieldBashChance { get; set; } = 0.2f;

	// ---------------------------------------------------------------- guarding
	/// <summary>Chance per decision tick (when not attacking) to raise the shield.</summary>
	[Property, Group( "Guarding" ), Range( 0f, 1f )] public float GuardChance { get; set; } = 0.15f;
	[Property, Group( "Guarding" )] public float GuardHoldMin { get; set; } = 1.0f;
	[Property, Group( "Guarding" )] public float GuardHoldMax { get; set; } = 2.0f;
	/// <summary>After taking a hit, chance to raise the shield for a hold.</summary>
	[Property, Group( "Guarding" ), Range( 0f, 1f )] public float GuardAfterHurtChance { get; set; } = 0.3f;
	/// <summary>On the approach, the shield may come up (GuardChance per tick) once the player is inside this; 0 = only once engaged.</summary>
	[Property, Group( "Guarding" )] public float GuardApproachRange { get; set; } = 0f;

	// ---------------------------------------------------------------- footwork
	/// <summary>Distance it likes to fight at (stops approaching here; the sword attacks start at 70-80, the charge at 100).</summary>
	[Property, Group( "Footwork" )] public float PreferredRange { get; set; } = 60f;
	/// <summary>Chance per decision tick to circle the player instead of standing.</summary>
	[Property, Group( "Footwork" ), Range( 0f, 1f )] public float StrafeChance { get; set; } = 0.35f;
	[Property, Group( "Footwork" )] public float StrafeSecondsMin { get; set; } = 0.8f;
	[Property, Group( "Footwork" )] public float StrafeSecondsMax { get; set; } = 1.8f;
	/// <summary>Walk while engaged (defensive footwork) instead of jogging everywhere.</summary>
	[Property, Group( "Footwork" )] public bool WalkWhileEngaged { get; set; } = false;
	/// <summary>Chance per decision tick to step back when the player is closer than PreferredRange * 0.6.</summary>
	[Property, Group( "Footwork" ), Range( 0f, 1f )] public float BackOffChance { get; set; } = 0.2f;

	// ---------------------------------------------------------------- senses and idle
	[Property, Group( "Senses" )] public float AggroRange { get; set; } = 750f;
	/// <summary>Wander around the spawn point while nobody is in sight.</summary>
	[Property, Group( "Senses" )] public bool Wander { get; set; } = false;

	// ---------------------------------------------------------------- vitals
	[Property, Group( "Vitals" )] public float Health { get; set; } = 60f;

	/// <summary>Per-spawn variation: every number above scaled by 1 ± this.</summary>
	[Property, Group( "Vitals" ), Range( 0f, 0.5f )] public float Jitter { get; set; } = 0.15f;

	/// <summary>A jittered copy for one skeleton (plain values; the asset itself is never modified).</summary>
	public SkeletonBrainValues Roll( Random rng )
	{
		float J( float v ) => v * (1f + ((float)rng.NextDouble() * 2f - 1f) * Jitter);
		float C( float v ) => Math.Clamp( J( v ), 0f, 1f );
		return new SkeletonBrainValues
		{
			AttackCooldownMin = J( AttackCooldownMin ),
			AttackCooldownMax = J( AttackCooldownMax ),
			ChargeChance = C( ChargeChance ),
			ShieldBashChance = C( ShieldBashChance ),
			GuardChance = C( GuardChance ),
			GuardHoldMin = J( GuardHoldMin ),
			GuardHoldMax = J( GuardHoldMax ),
			GuardAfterHurtChance = C( GuardAfterHurtChance ),
			GuardApproachRange = J( GuardApproachRange ),
			PreferredRange = J( PreferredRange ),
			StrafeChance = C( StrafeChance ),
			StrafeSecondsMin = J( StrafeSecondsMin ),
			StrafeSecondsMax = J( StrafeSecondsMax ),
			WalkWhileEngaged = WalkWhileEngaged,
			BackOffChance = C( BackOffChance ),
			AggroRange = AggroRange,
			Wander = Wander,
			Health = MathF.Round( J( Health ) ),
		};
	}

	public static SkeletonBrainValues Defaults() => new SkeletonBrainProfile().Roll( new Random( 0 ) );
}

/// <summary>The rolled numbers one skeleton lives by.</summary>
public sealed class SkeletonBrainValues
{
	public float AttackCooldownMin, AttackCooldownMax, ChargeChance, ShieldBashChance;
	public float GuardChance, GuardHoldMin, GuardHoldMax, GuardAfterHurtChance, GuardApproachRange;
	public float PreferredRange, StrafeChance, StrafeSecondsMin, StrafeSecondsMax, BackOffChance;
	public bool WalkWhileEngaged, Wander;
	public float AggroRange, Health;
}
