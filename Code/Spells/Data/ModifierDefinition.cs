using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Spells;

/// <summary>A scroll's identity: its name, the line the pickup banner prints, the stages it registers into, its seal and its rune code.</summary>
public sealed class ModifierDefinition
{
	public ModifierId Id { get; init; }
	/// <summary>The scroll's name ("Scroll of the Orbit" reads as "The Orbit").</summary>
	public string Name { get; init; }
	public CastStage[] Stages { get; init; } = Array.Empty<CastStage>();
	/// <summary>
	/// The pickup banner (user, 2026-09-20: Isaac's one line under the item name): what the scroll does in a breath.
	/// It has to be readable in the second the scroll unrolls on screen.
	/// </summary>
	public string Banner { get; init; } = "";
	/// <summary>What the scroll's Details card tells the player: what it does, and what it costs, in plain words. At most ~160 characters, three lines on the card. The numbers per copy are ScrollText.StackLine's job.</summary>
	public string Description { get; init; } = "";
	/// <summary>
	/// A black seal instead of the red one: this scroll takes something away or replaces how the staff shoots, and it
	/// can never be taken off again. Visible on a sealed scroll too, so a blind pickup is an informed gamble.
	/// </summary>
	public bool Black { get; init; }
	/// <summary>Two letters, written on the open scroll in Futhark. Distinct as RUNES: C/K/Q, V/W, J/Y and X/Z share a rune.</summary>
	public string Code { get; init; } = "??";

	public string StageTag => string.Join( "/", Stages.Select( s => s.ToString() ) );
}

public static class ModifierDatabase
{
	public static readonly ModifierDefinition[] All =
	{
		// ---- pattern
		new() { Id = ModifierId.Multiplication, Code = "MU", Name = "Multiplication", Stages = new[] { CastStage.Emission }, Banner = "More bolts.", Description = "Every cast splits into several projectiles fanned out in front of you, for no extra mana. Each one hits softer than a single bolt would, but all together they do far more." },
		new() { Id = ModifierId.Mirror, Code = "MI", Name = "The Mirror", Stages = new[] { CastStage.Emission }, Banner = "You also fire behind you.", Description = "Every cast is thrown a second time behind you, then to your sides, then all the way round. The extra casts are weaker than the one you aimed." },
		new() { Id = ModifierId.Stranger, Code = "ST", Name = "The Stranger", Stages = new[] { CastStage.Emission }, Banner = "A random spell casts with yours.", Description = "Every cast also throws a random spell you have learned, for free. The stranger's spell is weaker than a real cast, and never worth more than the cast that brought it." },
		new() { Id = ModifierId.Hummingbird, Code = "HB", Name = "The Hummingbird", Black = true, Stages = new[] { CastStage.Cost, CastStage.Damage }, Banner = "Tiny, fast, cheap.", Description = "Your casts come out much faster and cost far less, but every one of them is small and hits softly." },
		new() { Id = ModifierId.WildPage, Code = "WP", Name = "The Wild Page", Black = true, Stages = new[] { CastStage.Trigger, CastStage.Damage }, Banner = "Every cast is a random spell.", Description = "You no longer choose: every cast is a random spell you have learned. Whatever comes out hits harder." },

		// ---- triggers
		new() { Id = ModifierId.Echo, Code = "EC", Name = "The Echo", Stages = new[] { CastStage.Trigger }, Banner = "Every cast repeats.", Description = "Every spell you cast casts itself again a moment later. The repeats cost no mana, and each one is a little weaker than the last." },
		new() { Id = ModifierId.Familiar, Code = "FA", Name = "The Familiar", Stages = new[] { CastStage.Trigger }, Banner = "A wisp casts with you.", Description = "A wisp circles you. Whenever you cast, it throws a small copy of your spell at the nearest enemy. More copies, more wisps." },
		new() { Id = ModifierId.Hail, Code = "HA", Name = "The Hail", Stages = new[] { CastStage.Trigger }, Banner = "Spells also fall from above.", Description = "Whenever you cast, small copies of your spell drop from the ceiling onto enemies near you." },
		new() { Id = ModifierId.Conduit, Code = "CO", Name = "The Conduit", Stages = new[] { CastStage.Trigger }, Banner = "Sword hits sometimes cast your spell.", Description = "Every sword hit has a chance to fire your spell at that enemy for free, at half damage." },
		new() { Id = ModifierId.Retort, Code = "RT", Name = "The Retort", Stages = new[] { CastStage.Trigger }, Banner = "Being hit bursts your spell outward.", Description = "When something hurts you, a ring of small copies of your spell bursts out of you in every direction. It needs a moment between bursts." },
		new() { Id = ModifierId.Edge, Code = "ED", Name = "The Edge", Stages = new[] { CastStage.Trigger }, Banner = "Your sword carries your last spell.", Description = "For a few seconds after a cast your blade carries the spell: every sword hit also strikes with a share of its damage, and leaves whatever the spell leaves (except The Fuse's charge)." },

		// ---- cost
		new() { Id = ModifierId.Kindling, Code = "KI", Name = "Kindling", Stages = new[] { CastStage.Cost }, Banner = "A sword hit primes your next cast.", Description = "Landing a sword hit primes the staff: your next cast is much cheaper. Swing, cast, swing, cast." },
		new() { Id = ModifierId.Hollow, Code = "HO", Name = "The Hollow", Black = true, Stages = new[] { CastStage.Cost, CastStage.Damage }, Banner = "Spells cost blood.", Description = "Spells are paid for with your health instead of mana, half a health point per mana point, and hit 15% harder. Extra copies make kills give health back." },
		new() { Id = ModifierId.LastDrop, Code = "LD", Name = "The Last Drop", Stages = new[] { CastStage.Cost, CastStage.Damage }, Banner = "The cast that empties you is huge.", Description = "The cast that leaves you without enough mana for another comes out enormous and hits two or three times as hard." },

		// ---- damage
		new() { Id = ModifierId.Overcharge, Code = "OC", Name = "Overcharge", Stages = new[] { CastStage.Damage, CastStage.Impact }, Banner = "Every few casts is a monster.", Description = "The staff builds up between casts: every few casts one comes out overcharged, far stronger, bigger, and bursting where it lands." },
		new() { Id = ModifierId.Gluttony, Code = "GL", Name = "Gluttony", Stages = new[] { CastStage.Cost, CastStage.Damage }, Banner = "Hold to charge.", Description = "A tap still casts. Hold the button instead and the spell swells: a far bigger bolt that hits two or three times as hard, for some more mana." },
		new() { Id = ModifierId.Avarice, Code = "AV", Name = "Avarice", Stages = new[] { CastStage.Damage }, Banner = "Shards are power.", Description = "The more void shards you have gathered this run, the harder your spells hit. Most of the bonus comes by about 150 shards. Spending them on a reroll takes it away." },
		new() { Id = ModifierId.Wrath, Code = "WR", Name = "Wrath", Black = true, Stages = new[] { CastStage.Damage }, Banner = "Less life. Pain is power.", Description = "Lowers your maximum health. In return your spells hit harder the more health you are missing, strongest when you are nearly dead." },
		new() { Id = ModifierId.Horizon, Code = "HZ", Name = "The Horizon", Stages = new[] { CastStage.Damage, CastStage.Flight }, Banner = "Farther is stronger.", Description = "A spell gathers strength with distance: point blank it hits as always, at the end of its reach up to twice as hard. A beam's far end, a far link of lightning and an orb that drifted far all count." },

		// ---- flight
		new() { Id = ModifierId.Ricochet, Code = "RI", Name = "Ricochet", Stages = new[] { CastStage.Flight }, Banner = "Spells bounce.", Description = "Projectiles bounce off walls and floors instead of dying, but still burst on anything they can break. Beams reflect. Chain lightning leaps further, and banks off walls to find somebody." },
		new() { Id = ModifierId.Hound, Code = "HN", Name = "The Hound", Stages = new[] { CastStage.Flight }, Banner = "Spells hunt.", Description = "Your projectiles and orbs steer themselves toward the nearest enemy in front of them. Beams and lightning lean toward whoever stands nearest your aim." },
		new() { Id = ModifierId.Needle, Code = "NE", Name = "The Needle", Stages = new[] { CastStage.Flight }, Banner = "Spells pierce.", Description = "Projectiles and beams pass through the enemies they hit and carry on. Walls still stop them. Lightning starts on the enemies behind its first; an orb stings whoever it drifts through." },
		new() { Id = ModifierId.Return, Code = "RE", Name = "The Return", Stages = new[] { CastStage.Flight }, Banner = "Spells come back.", Description = "A projectile that has hit something, or reached the end of its flight, flies back to you through everything in its way. A beam fires a second ray back down its path, lightning runs back down its links, an orb drifts home." },
		new() { Id = ModifierId.Orbit, Code = "OR", Name = "The Orbit", Black = true, Stages = new[] { CastStage.Flight }, Banner = "Spells circle you.", Description = "Projectiles circle you like a shield and stay there until one hits an enemy or something breakable. Your next cast lets the old ring fly off. More copies add wider rings of bolts that each hit a little softer." },
		new() { Id = ModifierId.Amber, Code = "AM", Name = "Amber", Black = true, Stages = new[] { CastStage.Flight }, Banner = "Spells halt and wait as mines.", Description = "Half a second after the cast your projectiles stop in mid-air and wait as mines. A mine goes off when an enemy walks close to it." },
		new() { Id = ModifierId.Wake, Code = "WA", Name = "The Wake", Stages = new[] { CastStage.Flight }, Banner = "Spells leave a burning trail.", Description = "Projectiles paint a lingering trail of their element along their whole path: fire and void burn whoever stands in it, ice slows them." },
		new() { Id = ModifierId.Lattice, Code = "LA", Name = "The Lattice", Stages = new[] { CastStage.Flight }, Banner = "Lightning runs between your spells.", Description = "Your projectiles, mines and orbs that are near each other are joined by lightning that burns whatever crosses it. A beam's end throws lightning at enemies near it, and a chain's links hang in the air a moment." },
		new() { Id = ModifierId.Anvil, Code = "AN", Name = "The Anvil", Stages = new[] { CastStage.Flight, CastStage.Impact }, Banner = "Lobbed. Explosive.", Description = "Projectiles are thrown in an arc, slower and heavier, and burst over a wide area where they land. A beam bursts where it ends, every link of a chain bursts, an orb's void spreads wider." },
		new() { Id = ModifierId.Sloth, Code = "SL", Name = "Sloth", Black = true, Stages = new[] { CastStage.Damage, CastStage.Flight }, Banner = "Slow. Patient. Heavy.", Description = "Your projectiles crawl through the air and last three times as long, and every spell hits harder for it. Beams burn longer. Lightning rides a slow bolt." },

		// ---- impact
		new() { Id = ModifierId.Fracture, Code = "FR", Name = "The Fracture", Stages = new[] { CastStage.Impact }, Banner = "Spells split on impact.", Description = "Where a spell lands it breaks into smaller pieces of itself: bolts throw shards, a beam's end sprays short rays, lightning forks again, an orb sheds small orbs. The pieces never split again." },
		new() { Id = ModifierId.Fuse, Code = "FU", Name = "The Fuse", Stages = new[] { CastStage.Impact }, Banner = "Spells lodge, then detonate.", Description = "A spell that hits an enemy leaves a charge in it. A moment later the charge goes off, hurting it again and everything beside it. A beam or an orb's void keeps lodging new ones. Only a cast spell lodges a charge, never the sword." },
		new() { Id = ModifierId.Ram, Code = "RA", Name = "The Ram", Stages = new[] { CastStage.Impact }, Banner = "Spells shove. Walls hurt.", Description = "Your spells throw enemies back, and one thrown into a wall is hurt by it. A beam or an orb's void shoves again and again, more gently." },
		new() { Id = ModifierId.BrandEmbers, Code = "BE", Name = "Brand of Embers", Stages = new[] { CastStage.Impact }, Banner = "Everything burns.", Description = "Whatever your spell is, the enemies it hits catch fire and keep burning for a few seconds." },
		new() { Id = ModifierId.BrandFrost, Code = "BF", Name = "Brand of Frost", Stages = new[] { CastStage.Impact }, Banner = "Everything slows.", Description = "Whatever your spell is, the enemies it hits are chilled and move slowly for a few seconds." },
		new() { Id = ModifierId.BrandStorm, Code = "BS", Name = "Brand of Storm", Stages = new[] { CastStage.Impact }, Banner = "Hits arc to a neighbour.", Description = "Whatever your spell is, a hit throws a small arc of lightning from that enemy to the ones standing near it." },

		// ---- death
		new() { Id = ModifierId.Turncoat, Code = "TC", Name = "The Turncoat", Stages = new[] { CastStage.Death }, Banner = "Your kills rise for you.", Description = "Enemies killed by your spells rise again and fight for you for 20 seconds. Up to 4 at once; the oldest crumbles to make room for a new one." },
		new() { Id = ModifierId.Pyre, Code = "PY", Name = "The Pyre", Stages = new[] { CastStage.Death }, Banner = "Spell kills explode.", Description = "An enemy killed by your spells bursts, hurting everything around it. The burst of one pyre never lights another." },
		new() { Id = ModifierId.Harvest, Code = "HV", Name = "The Harvest", Stages = new[] { CastStage.Death }, Banner = "Spell kills bleed mana.", Description = "An enemy killed by your spells gives up a mote of mana that flies back to you. On a Hollow staff the mote is blood instead." },
	};

	private static readonly Dictionary<ModifierId, ModifierDefinition> _byId = All.ToDictionary( m => m.Id );

	public static ModifierDefinition Get( ModifierId id ) => _byId.GetValueOrDefault( id );

	/// <summary>By enum name or scroll name, case-insensitive, prefix allowed ("mult", "the orbit", "orbit").</summary>
	public static bool TryParse( string text, out ModifierId id )
	{
		id = ModifierId.None;
		var t = (text ?? "").Trim().ToLowerInvariant().Replace( "the ", "" ).Replace( "_", "" ).Replace( " ", "" );
		if ( t.Length == 0 ) return false;
		if ( t is "none" or "clear" or "empty" or "-" ) return true;
		foreach ( var m in All )
		{
			var name = m.Name.ToLowerInvariant().Replace( "the ", "" ).Replace( " ", "" );
			var key = m.Id.ToString().ToLowerInvariant();
			if ( name == t || key == t ) { id = m.Id; return true; }
		}
		foreach ( var m in All )
		{
			var name = m.Name.ToLowerInvariant().Replace( "the ", "" ).Replace( " ", "" );
			var key = m.Id.ToString().ToLowerInvariant();
			if ( name.StartsWith( t, StringComparison.Ordinal ) || key.StartsWith( t, StringComparison.Ordinal ) ) { id = m.Id; return true; }
		}
		return false;
	}
}

/// <summary>
/// Every modifier's numbers by stack count, index 0..3 where index 0 is "not on the staff" and always the identity, so
/// a lookup is `Table[stacks]` and never branches. Placeholders for tuning. The host-side numbers (Turncoat, Hollow
/// refund, statuses) live in SpellHostTuning as convars.
///
/// The balance rule of the 2026-09-20 pool: PATTERN scrolls (Multiplication, The Mirror) multiply each other; PROC
/// scrolls (Familiar, Hail, Retort, Conduit, Stranger, Fracture shards) inherit every flight and impact scroll but
/// are never multiplied or echoed, so a full staff grows by sums rather than by powers, and mana stays the governor.
/// </summary>
public static class ModifierTables
{
	public static readonly int[] MultiplicationCount = { 1, 2, 4, 8 };
	/// <summary>
	/// What each bolt of a multiplied cast hits for (user, 2026-09-19): the scroll costs no mana, so at full damage a
	/// copy doubled the staff's output outright and three copies made it x8. Now the whole cast is worth x1.5 / x2.2 /
	/// x3.2 when every bolt lands. A proc keeps full damage: it is not multiplied.
	/// </summary>
	public static readonly float[] MultiplicationBoltDamage = { 1f, 0.75f, 0.55f, 0.4f };
	public static readonly float[] MultiplicationFanDegrees = { 0f, 14f, 36f, 60f };

	/// <summary>The Mirror: the yaws (degrees off the aim) the whole pattern is thrown again at, and what those casts keep.</summary>
	public static readonly float[][] MirrorYaws =
	{
		Array.Empty<float>(),
		new[] { 180f },
		new[] { 180f, 90f, -90f },
		new[] { 45f, 90f, 135f, 180f, -135f, -90f, -45f },
	};
	public static readonly float[] MirrorDamage = { 0f, 0.6f, 0.6f, 0.5f };

	/// <summary>The Stranger's random spell is never worth a whole cast of its own until three copies.</summary>
	public static readonly float[] StrangerDamage = { 0f, 0.5f, 0.75f, 1f };

	/// <summary>The Hummingbird: every cast small, fast and cheap. Damage per mana comes out a little ahead; per second well ahead while the pool lasts.</summary>
	public static readonly float[] HummingbirdCostMul = { 1f, 0.45f, 0.36f, 0.3f };
	public static readonly float[] HummingbirdDamageMul = { 1f, 0.5f, 0.42f, 0.36f };
	public static readonly float[] HummingbirdSizeMul = { 1f, 0.75f, 0.65f, 0.55f };
	public static readonly float[] HummingbirdCastTimeMul = { 1f, 0.5f, 0.4f, 0.333f };

	// Damage tables were roughly halved 2026-09-23 (user: a 20-scroll staff walked into the boss "so damn broken";
	// The Anvil's +25% per copy was the example). The additive sum in DamageStage tops out near x6 at rest now, not x9.
	public static readonly float[] WildPageDamagePct = { 0f, 0.2f, 0.35f, 0.5f };

	public static readonly int[] RicochetBounces = { 0, 2, 4, 6 };
	public const float RicochetSpeedKeep = 0.85f;
	public static readonly int[] RicochetExtraJumps = { 0, 1, 2, 3 };

	/// <summary>Overcharge (reworked 2026-09-20; it was +damage for +cost, a number): every Nth paid cast comes out overcharged.</summary>
	public static readonly int[] OverchargeEvery = { 0, 4, 3, 2 };
	public const float OverchargeDamagePct = 0.75f;
	public const float OverchargeSizeMul = 1.5f;
	public const float OverchargeBlastRadius = 110f;

	/// <summary>Kindling (reworked 2026-09-20; it alternated two spells): what a sword hit takes off the next cast.</summary>
	public static readonly float[] KindlingDiscount = { 0f, 0.5f, 0.75f, 1f };

	public static readonly float[] HollowDamagePct = { 0f, 0.15f, 0.15f, 0.15f };
	/// <summary>Health paid per point of mana the cast would have cost.</summary>
	public const float HollowHealthPerMana = 0.5f;

	/// <summary>The Last Drop: the cast that leaves less mana than another would cost.</summary>
	public static readonly float[] LastDropDamagePct = { 0f, 1f, 1.5f, 2f };
	public const float LastDropSizeMul = 1.5f;

	/// <summary>
	/// Avarice feeds on the void shards CARRIED this run: pct = Coefficient x Cap x (1 - exp(-shards / HalfShards)),
	/// most of the benefit by ~2.5x HalfShards, flat after. The bag starts every run empty (it is banked when the
	/// run ends, however it ends), so the bonus is only ever as big as this run's haul, less what the rerolls ate.
	/// </summary>
	public static readonly float[] AvariceCoefficient = { 0f, 0.5f, 1f, 1.5f };
	public const float AvariceCapPct = 1f;
	public const float AvariceHalfShards = 60f;   // was 150 when every kill paid (ShardPickup.FodderChance cut the income to a third)

	public static float AvaricePct( int shards, int stacks )
	{
		if ( stacks <= 0 || shards <= 0 ) return 0f;
		return AvariceCoefficient[stacks] * AvariceCapPct * (1f - MathF.Exp( -shards / AvariceHalfShards ));
	}

	public static readonly int[] OrbitRings = { 0, 1, 2, 3 };
	/// <summary>What each bolt of a ringed cast hits for: every ring takes the whole fan, so two rings are x1.5 and three x1.65 rather than x2 / x3 (audit 2026-09-22).</summary>
	public static readonly float[] OrbitBoltDamage = { 1f, 1f, 0.75f, 0.55f };
	/// <summary>How long an ORB circles, and how long a beam sweeps; a bolt circles until it hits something or the caster casts again.</summary>
	public const float OrbitSeconds = 2f;
	/// <summary>The safety net under "until it hits something": a forgotten ring lets go after this long.</summary>
	public const float OrbitMaxSeconds = 90f;
	/// <summary>
	/// Radius of ring k (0-based); odd rings counter-rotate. The first ring sits where an attacker stands: a skeleton
	/// starts its swing at about 56 u root-to-root (AttackSpec.StartRange, HitRange 60) and its footwork holds
	/// PreferredRange 60, so 65 passes through the attacker's body. It was 90 (the bolts passed BEHIND whatever was
	/// attacking the caster, user 2026-09-19), then 55 ("a little too close", user 2026-09-24).
	/// </summary>
	public static readonly float[] OrbitRingRadius = { 65f, 110f, 160f };
	public const float OrbitDegreesPerSecond = 180f;

	public const float AmberHaltSeconds = 0.5f;
	public static readonly float[] AmberMineSeconds = { 0f, 8f, 16f, 24f };
	public const float AmberTriggerRadius = 60f;

	public static readonly float[] WakeWidth = { 0f, 40f, 60f, 80f };
	public static readonly float[] WakeSeconds = { 0f, 2f, 4f, 6f };

	public static readonly float[] ConduitChance = { 0f, 0.1f, 0.2f, 0.3f };
	public const float ConduitDamageScale = 0.5f;
	public const float ConduitSizeScale = 0.6f;

	public static readonly int[] EchoRepeats = { 0, 1, 2, 3 };
	/// <summary>Each repeat hits for this much of the one before it, like a chain's jumps: the free casts total x1.75 / x2.3 / x2.7, not x2 / x3 / x4.</summary>
	public const float EchoFalloff = 0.75f;
	public const float EchoBeatSeconds = 0.35f;

	/// <summary>
	/// Gluttony (reworked 2026-09-20 when the cast time went): a tap casts as always; HOLDING charges over HoldSeconds
	/// toward this much more damage, this much more mana and this size. The bespoke charge looks live here now.
	/// </summary>
	public const float GluttonyHoldSeconds = 1.2f;
	public static readonly float[] GluttonyDamagePct = { 0f, 1f, 1.5f, 2f };
	public static readonly float[] GluttonyCostPct = { 0f, 0.5f, 0.75f, 1f };
	public static readonly float[] GluttonySizeMul = { 1f, 1.5f, 2f, 2.5f };
	/// <summary>The most a bolt grows however the moments stack (a hold, an overcharge, the last drop compounded to x5.6).</summary>
	public const float MaxSizeScale = 3f;

	/// <summary>The Hound: how fast a bolt turns toward a hostile, and how far off its heading a hostile may be to count.</summary>
	public static readonly float[] HoundTurnDegreesPerSecond = { 0f, 90f, 180f, 300f };
	public static readonly float[] HoundConeDegrees = { 0f, 70f, 100f, 140f };
	public const float HoundRadius = 700f;
	/// <summary>A beam leans toward a hostile this far off the crosshair; a chain's first-link cone widens to it.</summary>
	public static readonly float[] HoundSnapDegrees = { 0f, 12f, 20f, 30f };

	/// <summary>Wrath: max health per copy, and damage% = Coefficient x the missing fraction of that max.</summary>
	public static readonly float[] WrathMaxHealthMul = { 1f, 0.8f, 0.6f, 0.4f };
	public static readonly float[] WrathCoefficient = { 0f, 0.5f, 1f, 1.5f };

	public static float WrathPct( float missing01, int stacks )
	{
		if ( stacks <= 0 ) return 0f;
		return WrathCoefficient[Math.Clamp( stacks, 0, 3 )] * Math.Clamp( missing01, 0f, 1f );
	}

	/// <summary>
	/// The Needle: bodies a bolt or beam passes through. Enemies only (user, 2026-09-22): it used to take beams and
	/// orbs through walls from the first copy, bolts from the third, and let lightning skip the clear-line check. The
	/// wall machinery (FlightProfile.PierceWalls, SpellBeam.WallPierces, the bolt's ghost flight) is still there
	/// with nothing granting it.
	/// </summary>
	public static readonly int[] NeedleBodies = { 0, 2, 99, 99 };
	/// <summary>Chain Lightning has no body to pass through, so The Needle lets the bolt start on this many more enemies along the aim (the ones "behind" the first).</summary>
	public static readonly int[] NeedleChainStarts = { 0, 1, 2, 3 };
	/// <summary>How far a bolt flies blind once it has entered a wall (a wall pierce); still inside something after that, it bursts there.</summary>
	public const float WallPierceDepth = 140f;

	/// <summary>The Return: what the way back hits for, and how many times the bolt turns (2 = back, then out again).</summary>
	public static readonly float[] ReturnDamage = { 0f, 0.75f, 1f, 1f };
	public static readonly int[] ReturnTurns = { 0, 1, 1, 2 };

	/// <summary>The Anvil: every spell lobbed like a heavier fireball, bursting wide.</summary>
	public static readonly float[] AnvilSplashRadius = { 0f, 120f, 170f, 220f };
	public static readonly float[] AnvilDamagePct = { 0f, 0.1f, 0.2f, 0.3f };
	public const float AnvilGravity = 900f;
	public const float AnvilSpeedMul = 0.75f;
	public const float AnvilLoftDegrees = 5f;
	public const float AnvilSplashFraction = 0.6f;

	/// <summary>The Lattice: how far apart two of the caster's spells may be and still be joined, and what a line burns for per tick (of the bolt's damage, 5 Hz).</summary>
	public static readonly float[] LatticeRange = { 0f, 250f, 400f, 550f };
	public const float LatticeDamageFraction = 0.15f;
	public const float LatticeTickSeconds = 0.2f;
	public const int LatticeMaxLinks = 16;

	public const float SlothSpeedMul = 0.4f;
	public const float SlothLifeMul = 3f;
	public static readonly float[] SlothDamagePct = { 0f, 0.2f, 0.35f, 0.5f };
	/// <summary>A beam burns longer AND harder, so the window grows less than the damage (it was x2 on x2: four times a bolt's gain, audit 2026-09-22).</summary>
	public static readonly float[] SlothBeamWindowMul = { 1f, 1.15f, 1.3f, 1.5f };

	/// <summary>The Horizon: +pct at HorizonRange units flown (or along a beam, or from the caster to a chain's link), linear from zero.</summary>
	public static readonly float[] HorizonPct = { 0f, 0.5f, 0.9f, 1.25f };
	public const float HorizonRange = 1500f;
	public static float HorizonScale( int stacks, float distance ) => 1f + HorizonPct[Math.Clamp( stacks, 0, 3 )] * Math.Clamp( distance / HorizonRange, 0f, 1f );

	public static readonly int[] FractureShards = { 0, 2, 3, 4 };
	public const float FractureDamage = 0.35f;
	public const float FractureSize = 0.55f;

	public static readonly int[] FamiliarWisps = { 0, 1, 2, 3 };
	public const float FamiliarDamage = 0.35f;
	public const float FamiliarSize = 0.5f;
	public const float FamiliarReach = 900f;

	public static readonly int[] HailBolts = { 0, 2, 4, 6 };
	/// <summary>The hail aims itself, so it pays less per bolt than the wisps (0.4 was +240% free on top of the cast at three copies).</summary>
	public const float HailDamage = 0.3f;
	public const float HailSize = 0.6f;
	public const float HailReach = 750f;
	public const float HailBeatSeconds = 0.1f;

	public static readonly int[] RetortBolts = { 0, 4, 8, 12 };
	public const float RetortDamage = 0.5f;
	public const float RetortSize = 0.6f;
	public const float RetortCooldown = 2f;

	/// <summary>The Edge: share of the spell's resolved damage a sword hit adds, for EdgeSeconds after a paid cast.</summary>
	public static readonly float[] EdgePct = { 0f, 0.3f, 0.6f, 0.9f };
	public const float EdgeSeconds = 3f;
}
