using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>
/// The seven stages (MODIFIER_spec.md section 3), owner side: Cost, Trigger, Emission, Damage, Flight, Impact,
/// then delivery. Modifiers in different stages compose for free; the same-stage rules live inside each stage.
/// Stage 7 (Death) runs on the host from <see cref="SpellDeathStage"/>. The recursion guard: a stage that would
/// add a lineage flag skips when the context already carries it, and nothing past depth 1 is cast from here.
/// </summary>
public static class CastPipeline
{
	public const int MaxOwnerDepth = 1;

	/// <summary>Runs every stage and delivers. False when the cast was refused (cannot pay) or guarded.</summary>
	public static bool Run( CastContext ctx ) => RunFrom( ctx, CastStage.Cost );

	/// <summary>Starts at `from` (an Echo repeat re-enters at Emission; a Mirror or Stranger copy at Damage).</summary>
	public static bool RunFrom( CastContext ctx, CastStage from )
	{
		if ( ctx?.Spell is null || !ctx.CasterRoot.IsValid() ) return false;
		if ( ctx.Depth > MaxOwnerDepth ) return false;

		if ( from <= CastStage.Cost && !CostStage.Run( ctx ) ) return false;
		if ( from <= CastStage.Trigger ) TriggerStage.Run( ctx );
		if ( from <= CastStage.Emission ) EmissionStage.Run( ctx );
		if ( from <= CastStage.Damage ) DamageStage.Run( ctx );
		if ( from <= CastStage.Flight ) FlightStage.Run( ctx );
		if ( from <= CastStage.Impact ) ImpactStage.Run( ctx );
		if ( ctx.Cancelled ) return false;

		if ( SpellDebug.Verbose ) Log.Info( $"[Spell] cast {ctx}" );
		Deliver( ctx );

		if ( ctx.Companions is not null )
		{
			foreach ( var companion in ctx.Companions )
				RunFrom( companion, CastStage.Damage );
		}
		return true;
	}

	private static void Deliver( CastContext ctx )
	{
		switch ( ctx.Spell.Delivery )
		{
			case SpellDelivery.Projectile:
			case SpellDelivery.Orb:
				ProjectileEmitter.Emit( ctx );
				break;
			case SpellDelivery.Instant:
				ChainLightningCast.Emit( ctx );
				break;
			case SpellDelivery.Beam:
				BeamEmitter.Emit( ctx );
				break;
		}
	}
}

/// <summary>
/// Stage 1. The Hummingbird makes the cast cheap, Gluttony's hold makes it dear, Kindling (a sword hit primed the
/// staff) discounts what is left, The Hollow converts that to health. The Last Drop is decided here: it is the cast
/// that leaves the pool too low for another.
/// </summary>
public static class CostStage
{
	/// <summary>What a paid cast of `spell` costs on this staff, before Kindling (the HUD and the affordability check share it).</summary>
	public static float BaseCost( SpellDefinition spell, ModifierStacks stacks, float chargePower )
	{
		var gluttony = stacks[ModifierId.Gluttony];
		return spell.ManaCost
			* ModifierTables.HummingbirdCostMul[stacks[ModifierId.Hummingbird]]
			* (1f + ModifierTables.GluttonyCostPct[gluttony] * Math.Clamp( chargePower, 0f, 1f ))
			* SpellBoons.CostScale;
	}

	public static bool Run( CastContext ctx )
	{
		if ( ctx.IsFree ) return true;   // procs, echoes and copies are free, and never spend Kindling's primer

		var spell = ctx.Spell;
		var kindling = ctx.Stack( ModifierId.Kindling );
		var hollow = ctx.Stack( ModifierId.Hollow );
		var primed = kindling > 0 && ctx.Caster.IsValid() && ctx.Caster.Primed;
		var discount = primed ? 1f - ModifierTables.KindlingDiscount[kindling] : 1f;

		var cost = BaseCost( spell, ctx.Stacks, ctx.ChargePower ) * discount;
		// the pool drained while the hold was charging: the swell is dropped rather than the cast (the discount counts
		// toward the swell too: it used to be judged at full price and dropped when the primed swell was affordable)
		if ( ctx.ChargePower > 0f && !CanPay( ctx, cost, hollow ) )
		{
			ctx.ChargePower = 0f;
			cost = BaseCost( spell, ctx.Stacks, 0f ) * discount;
		}
		// The Last Drop is judged against a plain tap at today's price: what is left would not pay for one more. A
		// primed cast that cost nothing is never the last drop (audit 2026-09-22: Kindling x3 made every empty-pool
		// cast a free x4), and a Hollow staff has its last drop in blood.
		var tap = BaseCost( spell, ctx.Stacks, 0f ) * discount;
		var lastDrop = ctx.Stack( ModifierId.LastDrop ) > 0 && tap > 0f;

		if ( hollow > 0 )
		{
			ctx.HealthCost = cost * ModifierTables.HollowHealthPerMana;
			ctx.ManaCost = 0f;
			if ( !ctx.Health.IsValid() || !ctx.Health.TrySpendHealth( ctx.HealthCost ) ) { ctx.Cancelled = true; return false; }
			if ( lastDrop && ctx.Health.Health - tap * ModifierTables.HollowHealthPerMana < 1f ) ctx.LastDrop = true;
		}
		else
		{
			ctx.ManaCost = cost;
			if ( !ctx.Mana.IsValid() || !ctx.Mana.TrySpend( ctx.ManaCost ) ) { ctx.Cancelled = true; return false; }
			if ( lastDrop && !ctx.Mana.CanAfford( tap ) ) ctx.LastDrop = true;
		}

		if ( primed ) ctx.Caster.Primed = false;
		return true;
	}

	private static bool CanPay( CastContext ctx, float cost, int hollow )
	{
		if ( hollow > 0 ) return ctx.Health.IsValid() && ctx.Health.Health - cost * ModifierTables.HollowHealthPerMana >= 1f;
		return ctx.Mana.IsValid() && ctx.Mana.CanAfford( cost );
	}
}

/// <summary>
/// Stage 2, a PAID cast only (never an echo, a proc or a copy): The Echo schedules free repeats, The Familiar's
/// wisps and The Hail throw their miniatures. The Conduit's roll lives at the sword (SpellCaster.TryConduit), The
/// Retort's at the wound (SpellCaster.OnHurt).
/// </summary>
public static class TriggerStage
{
	public static void Run( CastContext ctx )
	{
		if ( ctx.IsFree || !ctx.Caster.IsValid() ) return;
		var echo = ctx.Stack( ModifierId.Echo );
		if ( echo > 0 ) ctx.Caster.ScheduleEchoes( ctx, ModifierTables.EchoRepeats[echo], ModifierTables.EchoBeatSeconds );
		ctx.Caster.CastFamiliars( ctx );
		ctx.Caster.CastHail( ctx );
	}
}

/// <summary>
/// Stage 3. Multiplication sets the count, the fan and what each bolt keeps; The Mirror throws the whole pattern
/// again at other yaws; The Stranger adds a random known spell. All three as companion casts run from the Damage stage.
/// </summary>
public static class EmissionStage
{
	public static void Run( CastContext ctx )
	{
		var m = ctx.Stack( ModifierId.Multiplication );
		var proc = ctx.IsProcLike;
		// the spell's own count (a swarm's six) times Multiplication; a proc keeps the spell's count but never multiplies
		ctx.Count = Math.Max( 1, ctx.Spell.ProjectileCount ) * (proc ? 1 : ModifierTables.MultiplicationCount[m]);
		ctx.FanDegrees = proc ? 0f : ModifierTables.MultiplicationFanDegrees[m];
		ctx.BoltScale = (proc ? 1f : ModifierTables.MultiplicationBoltDamage[m]) * RingScale( ctx );
		if ( proc ) return;

		var mirror = ctx.Stack( ModifierId.Mirror );
		if ( mirror > 0 && !ctx.Has( SpellHitFlags.Mirror ) && !ctx.Has( SpellHitFlags.StrangerCopy ) )
		{
			foreach ( var yaw in ModifierTables.MirrorYaws[mirror] )
			{
				var copy = ctx.Clone();
				copy.Flags |= SpellHitFlags.Mirror;
				copy.Direction = (Rotation.FromYaw( yaw ) * ctx.Direction).Normal;
				copy.AimPoint = ctx.Origin + copy.Direction * 600f;
				copy.SourceScale = ctx.SourceScale * ModifierTables.MirrorDamage[mirror];
				// pattern scrolls multiply each other: the mirrored cast keeps the whole fan
				copy.Count = ctx.Count;
				copy.FanDegrees = ctx.FanDegrees;
				copy.BoltScale = ctx.BoltScale;
				copy.BeamWindowSeconds = 1f;
				(ctx.Companions ??= new List<CastContext>()).Add( copy );
			}
		}

		var stranger = ctx.Stack( ModifierId.Stranger );
		if ( stranger > 0 && !ctx.Has( SpellHitFlags.StrangerCopy ) && !ctx.Has( SpellHitFlags.Mirror ) && ctx.Caster.IsValid() )
		{
			var other = ctx.Caster.RandomKnownSpell( ctx.Spell.Id );
			if ( other is not null )
			{
				var copy = ctx.Clone();
				copy.Spell = other;
				copy.Flags |= SpellHitFlags.StrangerCopy;
				// worth at most what the cast that brought it cost: a 6-mana shard does not throw a 22-mana orb for free
				var worth = MathF.Min( 1f, ctx.Spell.ManaCost / MathF.Max( 1f, other.ManaCost ) );
				copy.SourceScale = ctx.SourceScale * ModifierTables.StrangerDamage[stranger] * worth;
				copy.SizeScale = ctx.SizeScale * (ModifierTables.StrangerDamage[stranger] * worth < 1f ? 0.7f : 1f);
				copy.Count = Math.Max( 1, other.ProjectileCount );
				copy.FanDegrees = 0f;
				copy.BoltScale = RingScale( copy );
				copy.BeamWindowSeconds = 1f;
				(ctx.Companions ??= new List<CastContext>()).Add( copy );
			}
		}
	}

	/// <summary>
	/// What one bolt keeps when The Orbit's rings each take the whole fan (ProjectileEmitter): three rings were three
	/// times the staff's output at full damage (audit 2026-09-22), the very thing Multiplication is priced for. A beam
	/// sweeps instead of multiplying, and a miniature born away from the staff never rings, so neither pays.
	/// </summary>
	public static float RingScale( CastContext ctx )
	{
		if ( ctx.FixedOrigin || ctx.Spell.Delivery == SpellDelivery.Beam ) return 1f;
		return ModifierTables.OrbitBoltDamage[ctx.Stack( ModifierId.Orbit )];
	}
}

/// <summary>
/// Stage 4. All damage bonuses are additive percentages against base, applied once; The Hummingbird alone scales the
/// sum (it is a smaller bolt, not a weaker staff). The Hollow's bonus skips procs (free stays free); the rest do not.
/// The bolt's size is settled here too (SizeScale feeds the flight radius, the splash, the zone and the look).
/// </summary>
public static class DamageStage
{
	/// <summary>The additive sum the stage and the HUD's preview share.</summary>
	public static float Multiplier( ModifierStacks stacks, int shards, float missing01, float chargePower, bool overcharged, bool lastDrop, bool proc )
	{
		var mult = 1f
			+ (overcharged ? ModifierTables.OverchargeDamagePct : 0f)
			+ ModifierTables.HollowDamagePct[proc ? 0 : stacks[ModifierId.Hollow]]
			+ ModifierTables.AvaricePct( shards, stacks[ModifierId.Avarice] )
			+ ModifierTables.GluttonyDamagePct[stacks[ModifierId.Gluttony]] * Math.Clamp( chargePower, 0f, 1f )
			+ ModifierTables.WrathPct( missing01, stacks[ModifierId.Wrath] )
			+ ModifierTables.WildPageDamagePct[stacks[ModifierId.WildPage]]
			+ ModifierTables.SlothDamagePct[stacks[ModifierId.Sloth]]
			+ ModifierTables.AnvilDamagePct[stacks[ModifierId.Anvil]]
			+ (lastDrop ? ModifierTables.LastDropDamagePct[stacks[ModifierId.LastDrop]] : 0f)
			+ SpellBoons.DamagePct;
		return MathF.Max( 0.1f, mult ) * ModifierTables.HummingbirdDamageMul[stacks[ModifierId.Hummingbird]];
	}

	public static void Run( CastContext ctx )
	{
		var missing = ctx.Health.IsValid() ? 1f - ctx.Health.Health01 : 0f;
		ctx.DamageMultiplier = Multiplier( ctx.Stacks, ctx.Shards, missing, ctx.ChargePower, ctx.Overcharged, ctx.LastDrop, ctx.Has( SpellHitFlags.Proc ) );
		ctx.Damage = ctx.Spell.Damage * ctx.DamageMultiplier * ctx.SourceScale * ctx.BoltScale;

		var gluttony = ctx.Stack( ModifierId.Gluttony );
		ctx.SizeScale *= 1f + (ModifierTables.GluttonySizeMul[gluttony] - 1f) * Math.Clamp( ctx.ChargePower, 0f, 1f );
		ctx.SizeScale *= ModifierTables.HummingbirdSizeMul[ctx.Stack( ModifierId.Hummingbird )];
		if ( ctx.Overcharged ) ctx.SizeScale *= ModifierTables.OverchargeSizeMul;
		if ( ctx.LastDrop ) ctx.SizeScale *= ModifierTables.LastDropSizeMul;
		// a full hold, overcharged, on the last drop was x5.6: a fireball splashing 840 units, most of a room
		ctx.SizeScale = MathF.Min( ctx.SizeScale, ModifierTables.MaxSizeScale );
	}
}

/// <summary>
/// Stage 5. Priority coded once: The Orbit owns the first phase; Amber owns deceleration (after the orbit, when both);
/// Ricochet applies to whatever path survives; The Wake always applies; The Hound steers whatever is flying (it
/// takes the max of the spell's own homing); The Anvil lobs it, Sloth slows it, The Needle lets it through bodies,
/// The Return turns it round at the end. The projectile reads the profile in that order.
/// </summary>
public static class FlightStage
{
	public static void Run( CastContext ctx )
	{
		var spell = ctx.Spell;
		var f = new FlightProfile
		{
			Base = spell.Delivery == SpellDelivery.Orb ? FlightBase.Drift : spell.Gravity > 0f ? FlightBase.Arc : FlightBase.Straight,
			Speed = spell.Speed,
			Gravity = spell.Gravity,
			Radius = spell.Radius * MathF.Max( 0.5f, ctx.SizeScale ),
			LifetimeSeconds = spell.LifetimeSeconds,
			RangeUnits = spell.RangeUnits,
			HomingTurnDegreesPerSecond = spell.HomingTurnDegreesPerSecond,
			HomingConeDegrees = spell.HomingConeDegrees,
			HomingRadius = spell.HomingRadius,
			WobbleDegrees = spell.WobbleDegrees,
			WobbleRate = spell.WobbleRate,
		};

		var hound = ctx.Stack( ModifierId.Hound );
		if ( hound > 0 )
		{
			f.HomingTurnDegreesPerSecond = MathF.Max( f.HomingTurnDegreesPerSecond, ModifierTables.HoundTurnDegreesPerSecond[hound] );
			f.HomingConeDegrees = MathF.Max( f.HomingConeDegrees, ModifierTables.HoundConeDegrees[hound] );
			f.HomingRadius = MathF.Max( f.HomingRadius, ModifierTables.HoundRadius );
		}

		var orbit = ctx.Stack( ModifierId.Orbit );
		if ( orbit > 0 )
		{
			f.OrbitRings = ModifierTables.OrbitRings[orbit];
			f.OrbitSeconds = ModifierTables.OrbitSeconds;
			f.OrbitDegreesPerSecond = ModifierTables.OrbitDegreesPerSecond;
		}

		var amber = ctx.Stack( ModifierId.Amber );
		if ( amber > 0 )
		{
			f.AmberHaltSeconds = ModifierTables.AmberHaltSeconds;
			f.MineSeconds = ModifierTables.AmberMineSeconds[amber];
			f.MineTriggerRadius = ModifierTables.AmberTriggerRadius;
		}

		var ricochet = ctx.Stack( ModifierId.Ricochet );
		if ( ricochet > 0 )
		{
			f.Bounces = ModifierTables.RicochetBounces[ricochet];
			f.BounceSpeedKeep = ModifierTables.RicochetSpeedKeep;
		}

		var wake = ctx.Stack( ModifierId.Wake );
		if ( wake > 0 )
		{
			f.WakeWidth = ModifierTables.WakeWidth[wake];
			f.WakeSeconds = ModifierTables.WakeSeconds[wake];
		}

		// The Anvil: a lobbed spell (an orb drifts as it always did; its zone grows in the Impact stage)
		if ( ctx.Stack( ModifierId.Anvil ) > 0 && f.Base != FlightBase.Drift )
		{
			f.Base = FlightBase.Arc;
			f.Gravity = MathF.Max( f.Gravity, ModifierTables.AnvilGravity );
			f.Speed *= ModifierTables.AnvilSpeedMul;
		}

		if ( ctx.Stack( ModifierId.Sloth ) > 0 )
		{
			f.Speed *= ModifierTables.SlothSpeedMul;
			f.Gravity *= ModifierTables.SlothSpeedMul * ModifierTables.SlothSpeedMul;   // the same arc, flown slower
			// a bolt lasts longer so it still reaches; an orb's zone rides its lifetime, and three times the zone on
			// top of Sloth's damage was x6 a cast (audit 2026-09-22), so the orb keeps its own clock and drifts less far
			if ( f.Base != FlightBase.Drift ) f.LifetimeSeconds *= ModifierTables.SlothLifeMul;
		}

		// bodies only: nothing takes a bolt through a wall any more (user, 2026-09-22), so PierceWalls stays 0
		f.PierceBodies = ModifierTables.NeedleBodies[ctx.Stack( ModifierId.Needle )];

		var ret = ctx.Stack( ModifierId.Return );
		f.ReturnTurns = ModifierTables.ReturnTurns[ret];
		f.ReturnDamage = ret > 0 ? ModifierTables.ReturnDamage[ret] : 1f;

		f.LatticeRange = ModifierTables.LatticeRange[ctx.Stack( ModifierId.Lattice )];
		f.HorizonStacks = ctx.Stack( ModifierId.Horizon );

		ctx.Flight = f;
	}
}

/// <summary>Stage 6. The spell's own splash and zone; The Anvil's and an overcharged cast's burst; The Fracture's pieces.</summary>
public static class ImpactStage
{
	public static void Run( CastContext ctx )
	{
		var spell = ctx.Spell;
		var size = MathF.Max( 0.5f, ctx.SizeScale );
		var impact = new ImpactProfile
		{
			SplashRadius = spell.SplashRadius * size,
			SplashFraction = spell.SplashFraction,
			SplashMaxTargets = spell.SplashMaxTargets,
			ZoneRadius = spell.ZoneRadius * size,
			ZoneSeconds = spell.ZoneSeconds,
		};

		var anvil = ctx.Stack( ModifierId.Anvil );
		if ( anvil > 0 ) Widen( impact, ModifierTables.AnvilSplashRadius[anvil] * size, ModifierTables.AnvilSplashFraction, 8 );
		if ( ctx.Overcharged ) Widen( impact, ModifierTables.OverchargeBlastRadius * size, 0.5f, 6 );
		if ( anvil > 0 && impact.ZoneRadius > 0f ) impact.ZoneRadius *= 1f + 0.25f * anvil;

		if ( !ctx.Has( SpellHitFlags.Shard ) ) impact.FractureShards = ModifierTables.FractureShards[ctx.Stack( ModifierId.Fracture )];
		ctx.Impact = impact;
	}

	private static void Widen( ImpactProfile impact, float radius, float fraction, int targets )
	{
		impact.SplashRadius = MathF.Max( impact.SplashRadius, radius );
		impact.SplashFraction = MathF.Max( impact.SplashFraction, fraction );
		impact.SplashMaxTargets = Math.Max( impact.SplashMaxTargets, targets );
	}
}
