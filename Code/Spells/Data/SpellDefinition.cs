namespace VoidMage.Spells;

/// <summary>How a spell leaves the staff: the shape of the cast state machine and of its delivery.</summary>
public enum SpellDelivery
{
	/// <summary>Charge, release, a bolt flies (fireball, ice shard).</summary>
	Projectile,
	/// <summary>Charge, then hold: a ray that burns while the button is down (laser).</summary>
	Beam,
	/// <summary>A short wind-up, then it resolves at once from the staff (chain lightning).</summary>
	Instant,
	/// <summary>Charge, release, a slow floating body with an area around it (void orb).</summary>
	Orb,
}

/// <summary>What the placeholder VFX builds for the spell's body, charge orb and burst.</summary>
public enum SpellBodyStyle { Flame, Crystal, Arc, Beam, Orb, Swarm, Necrotic }

/// <summary>The streak a projectile draws behind itself.</summary>
public enum SpellTrailStyle { None, Smoke, Frost, Sparks, Wisps }

/// <summary>
/// One spell's numbers and look (heden's SpellDefinition idiom). Placeholders live in <see cref="SpellDatabase"/>;
/// the cast pipeline reads these, modifiers scale them, nothing here is ever mutated at runtime.
/// </summary>
public sealed class SpellDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public SpellDelivery Delivery { get; init; }
	public SpellBodyStyle BodyStyle { get; init; }

	// ---- economy and timing (seconds)
	public float ManaCost { get; init; }
	/// <summary>How long the staff arm raises before a release is legal (the ChannelBegin/Loop hold).</summary>
	public float ChargeSeconds { get; init; } = 0.3f;
	/// <summary>How long after the throw the cast still owns the arm and paces the next cast.</summary>
	public float CastDurationSeconds { get; init; } = 0.8f;
	public float MaxHoldSeconds { get; init; } = 3f;
	/// <summary>Instant spells: fire the moment the charge completes, no release needed.</summary>
	public bool AutoRelease { get; init; }

	// ---- damage: per hit for projectiles and chains, per tick for beams and zones
	public float Damage { get; init; }

	// ---- projectile / orb
	public float Speed { get; init; }
	public float Radius { get; init; } = 8f;
	/// <summary>Units per second squared pulling the bolt down; 0 flies straight.</summary>
	public float Gravity { get; init; }
	public float LifetimeSeconds { get; init; } = 3f;
	public float RangeUnits { get; init; } = 2000f;
	public float SplashRadius { get; init; }
	public float SplashFraction { get; init; } = 0.5f;
	public int SplashMaxTargets { get; init; } = 4;
	/// <summary>Bolts per cast before Multiplication (the swarm's six).</summary>
	public int ProjectileCount { get; init; } = 1;
	/// <summary>A random cone per bolt instead of the even fan (degrees off the aim); 0 = the fan.</summary>
	public float SpreadDegrees { get; init; }
	/// <summary>Innate homing: how fast the bolt turns toward a hostile, how far off its heading one may be, how far away. 0 = none; The Hound raises these.</summary>
	public float HomingTurnDegreesPerSecond { get; init; }
	public float HomingConeDegrees { get; init; }
	public float HomingRadius { get; init; }
	/// <summary>A heading random walk (bees): the most it strays from its course, and how fast it walks (degrees per second).</summary>
	public float WobbleDegrees { get; init; }
	public float WobbleRate { get; init; }
	/// <summary>The caster is healed this much when this spell KILLS (Siphon Bolt; it leeched half of every hit until 2026-09-21: "kinda overpowered").</summary>
	public float KillHeal { get; init; }

	// ---- statuses the hit applies on the host
	public float SlowFraction { get; init; }
	public float SlowSeconds { get; init; }
	public float BurnDps { get; init; }
	public float BurnSeconds { get; init; }

	// ---- orb zone
	public float ZoneRadius { get; init; }
	public float ZoneSeconds { get; init; }

	// ---- chain
	public int ChainJumps { get; init; }
	public float ChainRange { get; init; }
	public float ChainJumpRange { get; init; }
	public float ChainConeDegrees { get; init; } = 30f;
	/// <summary>Damage multiplier per jump (0.75: the third link takes 56 %).</summary>
	public float ChainFalloff { get; init; } = 0.75f;

	// ---- beam
	public float BeamRange { get; init; }
	public float BeamRadius { get; init; }
	/// <summary>How long the burst burns after the release. A beam is paid for once, like every other cast.</summary>
	public float BeamSeconds { get; init; } = 1.2f;
	public float BeamTickSeconds { get; init; } = 0.2f;

	// ---- look
	public SpellPalette Palette { get; init; } = SpellPalette.Fire;
	/// <summary>Size of the effect relative to heden's authored recipe (the fireball shipped at 0.65).</summary>
	public float VfxScale { get; init; } = 0.65f;
	public SpellTrailStyle TrailStyle { get; init; } = SpellTrailStyle.Smoke;

	// ---- sounds (.sound paths)
	public string SoundCharge { get; init; }
	public string SoundCast { get; init; }
	public string SoundImpact { get; init; }
	/// <summary>A held loop (the beam's hum); null when the spell has none.</summary>
	public string SoundLoop { get; init; }

	/// <summary>The damage kind every hit of this spell carries (`spell_fireball`), the purple number.</summary>
	public string Kind => "spell_" + Id;

	public bool IsProjectileLike => Delivery is SpellDelivery.Projectile or SpellDelivery.Orb;
}
