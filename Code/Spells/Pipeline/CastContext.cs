using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>How a projectile moves before any modifier touches it.</summary>
public enum FlightBase
{
	/// <summary>A straight line at a fixed speed (ice shard).</summary>
	Straight,
	/// <summary>Gravity pulls it down (fireball).</summary>
	Arc,
	/// <summary>Slow and level; sticks where it lands instead of bursting (void orb).</summary>
	Drift,
}

/// <summary>Stage 5's output: everything a projectile needs to know about its own flight, priorities already resolved.</summary>
public sealed class FlightProfile
{
	public FlightBase Base;
	public float Speed, Gravity, Radius, LifetimeSeconds, RangeUnits;
	/// <summary>The Orbit: rings around the caster before the bolt leaves (0 = none); odd rings counter-rotate.</summary>
	public int OrbitRings;
	public float OrbitSeconds, OrbitDegreesPerSecond;
	/// <summary>Amber: seconds over which the bolt halts, then how long it sits as a mine (0 = no amber).</summary>
	public float AmberHaltSeconds, MineSeconds, MineTriggerRadius;
	public int Bounces;
	public float BounceSpeedKeep = 1f;
	/// <summary>The Wake: width and life of the trail hazard (0 = none).</summary>
	public float WakeWidth, WakeSeconds;
	/// <summary>Homing (the spell's own, raised by The Hound): turn rate, the cone off the heading a target may sit in, the reach. 0 = none.</summary>
	public float HomingTurnDegreesPerSecond, HomingConeDegrees, HomingRadius;
	/// <summary>A heading random walk (bees): the most it strays, and how fast it walks. 0 = none.</summary>
	public float WobbleDegrees, WobbleRate;

	/// <summary>The Needle: bodies the bolt may still pass through. Walls: kept for the ghost-flight path, but no enchantment grants it since 2026-09-22.</summary>
	public int PierceBodies, PierceWalls;
	/// <summary>The Return: how many times the bolt turns round, and what it hits for afterwards.</summary>
	public int ReturnTurns;
	public float ReturnDamage = 1f;
	/// <summary>The Lattice: how near another of the caster's spells must be to be joined to this one (0 = none).</summary>
	public float LatticeRange;
	/// <summary>The Horizon's copies: damage grows with the distance flown.</summary>
	public int HorizonStacks;

	public bool Orbits => OrbitRings > 0;
	public bool Halts => MineSeconds > 0f;
	public bool Wakes => WakeWidth > 0f && WakeSeconds > 0f;
	public bool Homes => HomingTurnDegreesPerSecond > 0f && HomingRadius > 0f;
	public bool Wobbles => WobbleDegrees > 0f && WobbleRate > 0f;
	public float OrbitRadius( int ring ) => ModifierTables.OrbitRingRadius[Math.Clamp( ring, 0, ModifierTables.OrbitRingRadius.Length - 1 )];
}

/// <summary>Stage 6's output: what happens where the spell lands.</summary>
public sealed class ImpactProfile
{
	public float SplashRadius, SplashFraction;
	public int SplashMaxTargets;
	public float ZoneRadius, ZoneSeconds;
	/// <summary>The Fracture: pieces thrown on from where the spell lands (0 = none; a shard never fractures).</summary>
	public int FractureShards;
}

/// <summary>
/// One cast on its way through the seven stages: the spell, the staff, where it comes from and what each stage
/// decided. Owner-side only (the host gets <see cref="SpellHitInfo"/>). Cloned for an Echo repeat, a Mirror or Stranger copy, a shard.
/// </summary>
public sealed class CastContext
{
	public SpellDefinition Spell;
	public ModifierStacks Stacks;
	/// <summary>The cast's lineage: a proc, an echo, a copy. Any flag makes it free; a flag already set stops the same stage re-applying.</summary>
	public SpellHitFlags Flags;
	public int Depth;
	public int CastId;

	public SpellCaster Caster;
	/// <summary>The player's own object (the PlayerController's), what traces ignore and what hits credit.</summary>
	public GameObject CasterRoot;
	public Scene Scene;
	public PlayerMana Mana;
	public PlayerHealth Health;
	/// <summary>Void shards the caster is carrying: what Avarice feeds on.</summary>
	public int Shards;
	/// <summary>Gluttony: how far the hold got, 0 (a tap) to 1 (the full charge).</summary>
	public float ChargePower;
	/// <summary>Overcharge: this is the Nth cast (set by the caster, which keeps the count).</summary>
	public bool Overcharged;
	/// <summary>The Last Drop: this cast emptied the pool (set by the Cost stage).</summary>
	public bool LastDrop;

	public Vector3 Origin, Direction;
	/// <summary>Where the crosshair pointed (aim assist for chains and the orbit's exit).</summary>
	public Vector3 AimPoint;

	// ---- stage outputs
	public float ManaCost, HealthCost;
	public float DamageMultiplier = 1f;
	/// <summary>A copy's or a proc's strength relative to the real cast (Stranger 0.5, Conduit 0.5).</summary>
	public float SourceScale = 1f;
	/// <summary>What one bolt of a multiplied cast keeps (ModifierTables.MultiplicationBoltDamage); set by the Emission stage, never accumulated.</summary>
	public float BoltScale = 1f;
	public float Damage;
	/// <summary>Visual size relative to the spell's own (Conduit minis 0.6).</summary>
	public float SizeScale = 1f;
	public int Count = 1;
	public float FanDegrees;
	/// <summary>Beam: how long a free copy burns; 0 = held by the button.</summary>
	public float BeamWindowSeconds;
	/// <summary>A free beam that does NOT leave from the staff (a wisp's, the hail's, a shard's): it keeps the origin it was cast from.</summary>
	public bool FixedOrigin;
	/// <summary>A body this cast's bolts pass through as if already struck: the enemy a shard of The Fracture broke off.</summary>
	public GameObject IgnoreBody;
	public FlightProfile Flight;
	public ImpactProfile Impact;
	/// <summary>Casts fired alongside this one (The Mirror's, The Stranger's), run through the pipeline from the Damage stage.</summary>
	public List<CastContext> Companions;
	public bool Cancelled;

	public bool IsFree => Flags != SpellHitFlags.None;
	public int Stack( ModifierId id ) => Stacks[id];
	public bool Has( SpellHitFlags flag ) => (Flags & flag) != 0;

	/// <summary>Never multiplied, never echoed, never the source of another proc: a free miniature, a shard.</summary>
	public bool IsProcLike => (Flags & (SpellHitFlags.Proc | SpellHitFlags.Shard)) != 0;

	public SpellHitInfo ToHitInfo() => new() { SpellId = Spell.Id, Mods = Stacks.Lo, Mods2 = Stacks.Hi, Depth = Depth, CastId = CastId, Flags = Flags };

	private static int _nextCastId = Game.Random.Next( 1, int.MaxValue / 2 );
	public static int NewCastId() => _nextCastId++;

	/// <summary>A fresh context for the same caster and staff, before any stage ran; the spell and lineage are the caller's to set.</summary>
	public CastContext Clone()
	{
		return new CastContext
		{
			Spell = Spell,
			Stacks = Stacks,
			Flags = Flags,
			Depth = Depth,
			CastId = NewCastId(),
			Caster = Caster,
			CasterRoot = CasterRoot,
			Scene = Scene,
			Mana = Mana,
			Health = Health,
			Shards = Shards,
			ChargePower = ChargePower,
			Overcharged = Overcharged,
			LastDrop = LastDrop,
			Origin = Origin,
			Direction = Direction,
			AimPoint = AimPoint,
			SourceScale = SourceScale,
			BoltScale = BoltScale,
			SizeScale = SizeScale,
			BeamWindowSeconds = BeamWindowSeconds,
			FixedOrigin = FixedOrigin,
		};
	}

	public override string ToString()
	{
		var cost = HealthCost > 0f ? $"{HealthCost:0.#} hp" : $"{ManaCost:0.#} mana";
		var f = Flight;
		var flight = f is null ? "" : $", {f.Base}{(f.Orbits ? $" orbit x{f.OrbitRings}" : "")}{(f.Halts ? $" mine {f.MineSeconds:0}s" : "")}{(f.Bounces > 0 ? $" bounces {f.Bounces}" : "")}{(f.Wakes ? $" wake {f.WakeWidth:0}/{f.WakeSeconds:0}s" : "")}";
		return $"{Spell.Id} [{Stacks}] {Flags}: {cost}, dmg {Damage:0.#} (x{DamageMultiplier:0.00} src {SourceScale:0.##}), count {Count}{(FanDegrees > 0f ? $" fan {FanDegrees:0}°" : "")}{flight}{(Companions is { Count: > 0 } ? $", +{Companions.Count} companion" : "")}";
	}
}
