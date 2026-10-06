using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Spells;

public static class SpellIds
{
	public const string Fireball = "fireball";
	public const string IceShard = "ice_shard";
	public const string ChainLightning = "chain_lightning";
	public const string Laser = "laser";
	public const string VoidOrb = "void_orb";
	public const string Swarm = "swarm";
	public const string SiphonBolt = "siphon_bolt";
}

/// <summary>
/// The spells (MAGIC_concept.md section 6 plus the user's additions) with placeholder numbers. Every number here is for tuning;
/// the structure (which delivery, which statuses, which palette) is the design.
/// </summary>
public static class SpellDatabase
{
	public static readonly SpellDefinition[] All =
	{
		new()
		{
			Id = SpellIds.Fireball, Name = "Fireball", Delivery = SpellDelivery.Projectile, BodyStyle = SpellBodyStyle.Flame,
			ManaCost = 15f, ChargeSeconds = 0.35f, CastDurationSeconds = 0.8f, MaxHoldSeconds = 3f,
			Damage = 30f,
			Speed = 1400f, Radius = 8f, Gravity = 500f, LifetimeSeconds = 3f, RangeUnits = 2000f,
			SplashRadius = 150f, SplashFraction = 0.5f, SplashMaxTargets = 4,
			Palette = SpellPalette.Fire, VfxScale = 0.65f, TrailStyle = SpellTrailStyle.Smoke,
			SoundCharge = "sounds/fireballcharge.sound", SoundCast = "sounds/fireballcastfast.sound", SoundImpact = "sounds/fireballimpact.sound",
		},
		new()
		{
			Id = SpellIds.IceShard, Name = "Ice Shard", Delivery = SpellDelivery.Projectile, BodyStyle = SpellBodyStyle.Crystal,
			ManaCost = 6f, ChargeSeconds = 0.15f, CastDurationSeconds = 0.4f, MaxHoldSeconds = 3f,
			Damage = 12f,
			Speed = 2600f, Radius = 5f, Gravity = 0f, LifetimeSeconds = 2f, RangeUnits = 2500f,
			SlowFraction = 0.5f, SlowSeconds = 2.5f,
			Palette = SpellPalette.Ice, VfxScale = 0.5f, TrailStyle = SpellTrailStyle.Frost,
			SoundCharge = "sounds/icecharge.sound", SoundCast = "sounds/lightspell.sound", SoundImpact = "sounds/vaseshatter.sound",
		},
		new()
		{
			Id = SpellIds.ChainLightning, Name = "Chain Lightning", Delivery = SpellDelivery.Instant, BodyStyle = SpellBodyStyle.Arc,
			ManaCost = 14f, ChargeSeconds = 0.35f, CastDurationSeconds = 0.45f, MaxHoldSeconds = 3f,
			// 18 then one arc of 13.5 for a long-range no-aim cast (it was 22 → 16.5 → 12 → 9 over four bodies; more arcs are Ricochet's job)
			Damage = 18f,
			// the carrier used when a flight modifier puts the chain on a projectile
			Speed = 900f, Radius = 6f, LifetimeSeconds = 2f, RangeUnits = 750f,
			ChainJumps = 1, ChainRange = 750f, ChainJumpRange = 350f, ChainConeDegrees = 30f, ChainFalloff = 0.75f,
			Palette = SpellPalette.Lightning, VfxScale = 0.55f, TrailStyle = SpellTrailStyle.Sparks,
			SoundCharge = "sounds/chargecrackling.sound", SoundCast = "sounds/magicmissile.sound", SoundImpact = "sounds/arcsnap.sound",
		},
		new()
		{
			Id = SpellIds.Laser, Name = "Void Lance", Delivery = SpellDelivery.Beam, BodyStyle = SpellBodyStyle.Beam,
			ManaCost = 16f, ChargeSeconds = 0.5f, CastDurationSeconds = 0.5f, MaxHoldSeconds = 3f,
			// 5 ticks of 5 on the one enemy it stops on (it was 8 of 9 through everything in line: "jesus christ its so strong")
			Damage = 5f,
			BeamRange = 1500f, BeamRadius = 14f, BeamSeconds = 1f, BeamTickSeconds = 0.2f,
			Palette = SpellPalette.Laser, VfxScale = 0.6f, TrailStyle = SpellTrailStyle.None,
			SoundCharge = "sounds/magiccharge.sound", SoundCast = "", SoundImpact = "sounds/laserforreal.sound",   // "" = silent (null would fall back to the fireball's): no cast sound, "the impact sound is enough on its own" (user, 2026-09-20)
		},
		new()
		{
			Id = SpellIds.VoidOrb, Name = "Void Orb", Delivery = SpellDelivery.Orb, BodyStyle = SpellBodyStyle.Orb,
			ManaCost = 22f, ChargeSeconds = 0.5f, CastDurationSeconds = 0.9f, MaxHoldSeconds = 3f,
			Damage = 6f,
			// the sphere is drawn at Radius: half of what it shipped at, which is the size that reads as a held void
			Speed = 220f, Radius = 9f, Gravity = 0f, LifetimeSeconds = 7f, RangeUnits = 1200f,
			ZoneRadius = 120f, ZoneSeconds = 7f, BurnDps = 8f, BurnSeconds = 3f,
			Palette = SpellPalette.Void, VfxScale = 0.55f, TrailStyle = SpellTrailStyle.Wisps,
			SoundCharge = "sounds/voidcharge.sound", SoundCast = "sounds/fireballcastslow.sound", SoundImpact = "sounds/singularity.sound",
		},
		new()
		{
			// six bees: low damage each, an enormous hit count, the home of anything that procs per hit
			Id = SpellIds.Swarm, Name = "Swarm", Delivery = SpellDelivery.Projectile, BodyStyle = SpellBodyStyle.Swarm,
			ManaCost = 10f, ChargeSeconds = 0.3f, CastDurationSeconds = 0.6f, MaxHoldSeconds = 3f,
			Damage = 6f,   // per bee (user, 2026-09-22: 6 not 5; six bees = 36, over a fireball's 30)
			Speed = 1800f, Radius = 4f, Gravity = 0f, LifetimeSeconds = 2.5f, RangeUnits = 1800f,
			ProjectileCount = 6, SpreadDegrees = 10f,
			HomingTurnDegreesPerSecond = 70f, HomingConeDegrees = 50f, HomingRadius = 450f,
			WobbleDegrees = 14f, WobbleRate = 220f,
			Palette = SpellPalette.Swarm, VfxScale = 0.3f, TrailStyle = SpellTrailStyle.Sparks,
			SoundCharge = "sounds/chargecrackling.sound", SoundCast = "sounds/magicmissile.sound", SoundImpact = "sounds/arrowimpact.sound",
		},
		new()
		{
			// slow, and every KILL with it heals the caster (it gave back half of every hit until 2026-09-21: overpowered)
			Id = SpellIds.SiphonBolt, Name = "Siphon Bolt", Delivery = SpellDelivery.Projectile, BodyStyle = SpellBodyStyle.Necrotic,
			ManaCost = 18f, ChargeSeconds = 0.45f, CastDurationSeconds = 0.9f, MaxHoldSeconds = 3f,
			Damage = 15f,   // was 28 until 2026-09-22 (user: too strong for a spell that heals)
			// short: about 13 m (user, 2026-09-22: 700 then 500; it flew a fireball's 2000 before)
			Speed = 650f, Radius = 9f, Gravity = 0f, LifetimeSeconds = 4f, RangeUnits = 500f,
			KillHeal = 1f,   // was 8 until 2026-09-22 (user: "far too op")
			// no smoke trail: the wisp sheds its own vapour, and fire's puffs behind it would read as a green fireball
			Palette = SpellPalette.Necrotic, VfxScale = 0.6f, TrailStyle = SpellTrailStyle.None,
			// its own copy of the slow cast sound at 0.6 instead of the shared 0.8 (user, 2026-09-22: too loud)
			SoundCharge = "sounds/voidcharge.sound", SoundCast = "sounds/siphoncast.sound", SoundImpact = "sounds/steamburst.sound",
		},
	};

	private static readonly Dictionary<string, SpellDefinition> _byId = All.ToDictionary( s => s.Id );

	/// <summary>Null for an unknown or empty id.</summary>
	public static SpellDefinition Get( string id ) => string.IsNullOrEmpty( id ) ? null : _byId.GetValueOrDefault( id );

	public static bool IsValid( string id ) => Get( id ) is not null;

	/// <summary>By id or name, case-insensitive, prefix allowed ("ice", "chain", "void").</summary>
	public static SpellDefinition Find( string text )
	{
		var t = (text ?? "").Trim().ToLowerInvariant().Replace( " ", "_" );
		if ( t.Length == 0 ) return null;
		return All.FirstOrDefault( s => s.Id == t || s.Name.ToLowerInvariant().Replace( " ", "_" ) == t )
			?? All.FirstOrDefault( s => s.Id.StartsWith( t, StringComparison.Ordinal ) || s.Name.ToLowerInvariant().StartsWith( t, StringComparison.Ordinal ) );
	}
}
