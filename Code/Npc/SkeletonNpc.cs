using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Npc;

/// <summary>`Hop` is appended LAST on purpose: the value goes over the wire, so inserting it would renumber Dead.</summary>
public enum SkeletonState { Idle, Approach, Engage, Attack, Hurt, Dead, Hop }

/// <summary>
/// The skeleton's truth, host only: what it is doing, its health, where it moves (a NavMeshAgent owns the
/// position), what it decided (a rolled <see cref="SkeletonBrainProfile"/>). Everything a peer needs to show
/// it is a [Sync] field; <see cref="SkeletonBody"/> turns those into animgraph parameters on every machine.
/// One-shot clips (attacks, hurts, death) are timed here from the measured clip lengths, and their authored
/// travel comes back through the renderer's RootMotion and is applied to the agent, kept on the navmesh.
/// Speeds are the clips' own root travel (walk 150 / jog 405 / run 617 cm/s at the model's 0.3937 scale), so the
/// feet never slide.
/// </summary>
[Title( "Skeleton NPC" ), Category( "Void Mage" ), Icon( "skull" )]
public sealed class SkeletonNpc : Component, DamageRouter.IHittable, ICombatant
{
	public enum Kind { None = 0, SwordA = 1, SwordB = 2, Charge = 3, ShieldBash = 4 }


	/// <summary>One attack's timing, read off the clips (60 fps, measured in Blender) and tuned live.</summary>
	public sealed class AttackSpec
	{
		[KeyProperty] public string Name { get; set; }
		/// <summary>Seconds into the clip when the blade lands (charge: into the whole start+chop sequence).</summary>
		public float HitTime { get; set; }
		/// <summary>Seconds the body is locked in the clip; shorter than the clip cuts the recovery (the graph follows `busy`).</summary>
		public float Duration { get; set; }
		/// <summary>Planar distance from the skeleton's root at the hit frame (after the clip's lunge) that the blade covers.</summary>
		public float HitRange { get; set; }
		/// <summary>Units the clip's root travel carries the body forward BEFORE the hit frame (measured; 0 for a stand-still swing).</summary>
		public float LungeBeforeHit { get; set; }
		/// <summary>Planar distance at which the attack is started: only what the lunge and the blade can reach, with slack for the agent's position lag, so a standing player is always hit.</summary>
		public float StartRange => HitRange + LungeBeforeHit * 0.75f - 4f;
		/// <summary>Half-angle of the cone in front of the skeleton that the hit covers.</summary>
		public float ArcDegrees { get; set; } = 45f;
		public float Damage { get; set; }
		public float Push { get; set; }
		/// <summary>Facing stops tracking the player this long before the hit.</summary>
		public float FaceLockBefore { get; set; } = 0.15f;
		/// <summary>Seconds into the attack from which hits no longer stagger it (hyper armor until the hit lands); negative = none.</summary>
		public float ArmorFrom { get; set; } = -1f;
		/// <summary>Code-driven travel toward the target until the hit frame, in units/s (a flier whose clips play in place); 0 = the clip's own root motion.</summary>
		public float LungeSpeed { get; set; } = 0f;
		/// <summary>The attack is never started closer than this (a leap that needs room); 0 = no minimum.</summary>
		public float MinRange { get; set; } = 0f;
		/// <summary>Above 0: the moment the player is first sighted inside this distance (and beyond MinRange) this attack opens the fight, straight out of the approach.</summary>
		public float OpenerRange { get; set; } = 0f;
		/// <summary>A second hit frame (0 = none): a two-strike clip lands twice, every player hittable again.</summary>
		public float HitTime2 { get; set; } = 0f;
		// ---- the attack table (SkeletonNpc.Attacks); the four fixed specs ignore these
		/// <summary>Selection weight in the table; 0 = reachable only through another entry's ComboInto.</summary>
		public float Weight { get; set; } = 1f;
		/// <summary>Seconds before the table may pick it again (counted from its start).</summary>
		public float Cooldown { get; set; } = 0f;
		/// <summary>The planar window it is picked in: SelectMin.. SelectMax (SelectMax 0 = up to StartRange). Leaps want a SelectMin.</summary>
		public float SelectMin { get; set; } = 0f;
		public float SelectMax { get; set; } = 0f;
		/// <summary>Names of entries it may flow straight into when it ends (comma separated), with ComboChance.</summary>
		public string ComboInto { get; set; } = "";
		public float ComboChance { get; set; } = 0f;
		/// <summary>The shield stays up through it (the bash); otherwise starting it drops the guard.</summary>
		public bool KeepsGuard { get; set; } = false;
		/// <summary>The damage kind on the wire ("skeleton_sword" / "skeleton_bash"): what the victim's feel and the block rule key on.</summary>
		public string HitTag { get; set; } = "";
	}

	[Property] public SkeletonBrainProfile Profile { get; set; }
	/// <summary>Which skeleton this prefab is (SkeletonSpawner.Kinds: slave, soldier_01, ...); labels and spawn names.</summary>
	[Property] public string KindName { get; set; } = "slave";
	/// <summary>
	/// What it holds, orthogonal to the brain: the vmdl's `sword` / `shield` body-group choices in the order of
	/// tools/skeleton_kinds.py (slave: broken sword 0, whole 1, chitin shield 0; soldiers: greatsword 0, curved 1,
	/// heater 01 / 02). These are what the prefab shows placed by hand; the spawner rolls them per skeleton.
	/// </summary>
	[Property, Group( "Weapons" )] public int DefaultSword { get; set; }
	[Property, Group( "Weapons" )] public int DefaultShield { get; set; }
	/// <summary>This kind's strength: every attack's damage is scaled by it (the soldiers hit harder than the slaves).</summary>
	[Property, Group( "Attacks" )] public float DamageScale { get; set; } = 1f;
	/// <summary>Sword choice 1 bites this much harder than choice 0 (the slave's whole sword over its broken one; 1 for the soldiers); the shield bash is unaffected.</summary>
	[Property, Group( "Attacks" )] public float SecondSwordDamageScale { get; set; } = 1.15f;
	/// <summary>Clip root travel: walk_* 150 cm/s, jog_* 405 cm/s, run 617 cm/s, in units at the model's scale.</summary>
	[Property, Group( "Movement" )] public float WalkSpeed { get; set; } = 59f;
	[Property, Group( "Movement" )] public float JogSpeed { get; set; } = 159f;
	[Property, Group( "Movement" )] public float RunSpeed { get; set; } = 243f;
	[Property, Group( "Movement" )] public float TurnRateEngaged { get; set; } = 720f;
	[Property, Group( "Movement" )] public float TurnRateGuarding { get; set; } = 300f;
	[Property, Group( "Movement" )] public float TurnRateMoving { get; set; } = 480f;
	/// <summary>Distance at which Approach hands over to the engage loop (the blade reaches ~60 u after a lunge).</summary>
	[Property, Group( "Combat" )] public float EngageRange { get; set; } = 90f;
	[Property, Group( "Combat" )] public float DecisionInterval { get; set; } = 0.1f;
	/// <summary>Seconds of a light hurt (0.97 s clip at the graph's 1.4 speed).</summary>
	[Property, Group( "Combat" )] public float HurtSeconds { get; set; } = 0.69f;
	/// <summary>Seconds the guard stays broken after the hit that breaks it (guard_heavy_hit is 1.16 s at 1.15x; the tail is cut).</summary>
	[Property, Group( "Combat" )] public float GuardBreakSeconds { get; set; } = 1.0f;
	/// <summary>Seconds of the light block reaction (guard_light_hit, the shield holds; the graph's `busy` cuts the tail).</summary>
	[Property, Group( "Combat" )] public float GuardHitSeconds { get; set; } = 0.45f;
	/// <summary>What a raised shield takes before it breaks: a light sword hit costs 1, a heavy or a fireball 2.</summary>
	[Property, Group( "Combat" )] public int GuardPoints { get; set; } = 2;
	/// <summary>A hit inside this long after the shield goes up is not blocked (gaurd_start takes 0.27 s).</summary>
	[Property, Group( "Combat" )] public float GuardRaiseGrace { get; set; } = 0.25f;
	/// <summary>Hits only land when the player's feet are within this height of the skeleton's (the player capsule is 72).</summary>
	[Property, Group( "Combat" )] public float HitHeightTolerance { get; set; } = 72f;
	/// <summary>
	/// How far up or down aggro reaches. Distances everywhere else are planar, which says nothing about a player
	/// standing a floor above: through any opening the skeletons up there locked on, then chased a planar gap they
	/// could never close, hammering the crowd solver and the nav agent's stall ladder (which cycles the agent off and
	/// on, re-registering it with the same crowd the skeletons in your room are trying to move through) for as long
	/// as you stood there. A dungeon level is 98 units, a stacked tier sits two levels (197 u) up, so this band takes
	/// in a step or a ramp inside the room and shuts out the floor above and below. Beyond the band a chase survives
	/// only on SIGHT (StillHunting / LoseSightSeconds): up an open stairwell it follows, through a slab it cannot.
	/// The old 160 / 2x-on-chase pair let a 197 u floor gap through, so monsters shadowed you from underneath.
	/// </summary>
	[Property, Group( "Combat" )] public float AggroHeightBand { get; set; } = 110f;
	/// <summary>A target above or below the band is hunted for this long after it was last seen, then dropped.</summary>
	[Property, Group( "Combat" )] public float LoseSightSeconds { get; set; } = 2f;
	/// <summary>Poise: this many staggers inside PoiseWindow and the skeleton shrugs off hits for PoiseArmorSeconds and swings back.</summary>
	[Property, Group( "Combat" )] public int PoiseHits { get; set; } = 2;
	[Property, Group( "Combat" )] public float PoiseWindow { get; set; } = 2f;
	/// <summary>Once poise triggers, hits do damage but no stagger for this long: room for one attack of its own.</summary>
	[Property, Group( "Combat" )] public float PoiseArmorSeconds { get; set; } = 1.1f;
	/// <summary>Coming out of a stagger the next attack is allowed this soon (instead of the brain's full cooldown): it answers.</summary>
	[Property, Group( "Combat" )] public float RetaliateMin { get; set; } = 0.1f;
	[Property, Group( "Combat" )] public float RetaliateMax { get; set; } = 0.4f;
	/// <summary>The networked body lingers this long after dying so every peer sees Dead (and the death push) before it goes; the ragdoll is each peer's own (SkeletonCorpse).</summary>
	[Property, Group( "Combat" )] public float DeathLingerSeconds { get; set; } = 0.3f;
	/// <summary>Apply the clips' authored travel (lunges, the hurt shove) to the body.</summary>
	[Property, Group( "Combat" )] public bool ApplyRootMotion { get; set; } = true;
	/// <summary>
	/// Whether a hit can knock it into the Hurt state at all. OFF for a body whose pack gave it no hit
	/// reaction: six of the eight Quaternius monsters have no such clip, and staggering them anyway would
	/// freeze them for HurtSeconds with nothing playing. They take the damage as a flinch instead (a flash
	/// and a shove, the way hyper armor and poise already do it).
	/// </summary>
	[Property, Group( "Combat" )] public bool CanStagger { get; set; } = true;
	/// <summary>How much of the blade's push a flinch (a hit with no hurt clip: CanStagger off, poise, armor) shoves the body; a boss body barely moves.</summary>
	[Property, Group( "Combat" )] public float FlinchShoveScale { get; set; } = 0.6f;
	/// <summary>
	/// Ambient life, not an enemy: it never looks for a target, so it never approaches, engages or attacks.
	/// It still takes damage and dies like anything else (the frog). Hostiles ignore it for free — they hunt
	/// players and raised allies, and it is on neither side.
	/// </summary>
	[Property, Group( "Combat" )] public bool Passive { get; set; }
	/// <summary>
	/// Whether this body is heavy enough to set a trap off. Off for every Quaternius monster: they are small
	/// animals, and a rat should not be able to drop a portcullis. It only stops them ARMING one — a monster
	/// standing in a trap that someone else set off still takes the hit, which is the difference between "too
	/// light to trigger it" and "immune to spikes".
	/// </summary>
	[Property, Group( "Combat" )] public bool TriggersTraps { get; set; } = true;

	// ---------------------------------------------------------------- hopping (the frog)
	/// <summary>
	/// A hopper travels in discrete jumps instead of walking: the whole clip is a one-shot, the body only moves
	/// during the clip's own airborne window, and it stands still between hops. Driving it any other way was the
	/// problem — a looping jump clip under a constant agent speed skates the frog along with an animation
	/// playing over the top, because a blend node loops on its own clock and cannot be phase-aligned to the
	/// travel. Numbers come from tools/measure_monster_clips.py (`air_start` / `air_end`) via the prefab.
	/// </summary>
	[Property, Group( "Hopping" )] public bool Hops { get; set; }
	/// <summary>Units per second while airborne. `distance / (HopLandTime - HopLiftTime)` in the generator.</summary>
	[Property, Group( "Hopping" )] public float HopSpeed { get; set; } = 100f;
	/// <summary>The whole clip, at the graph's playback speed: how long the body is committed to the hop.</summary>
	[Property, Group( "Hopping" )] public float HopSeconds { get; set; } = 0.875f;
	/// <summary>Seconds into the clip that the feet leave and meet the ground: the only window that travels.</summary>
	[Property, Group( "Hopping" )] public float HopLiftTime { get; set; } = 0.375f;
	[Property, Group( "Hopping" )] public float HopLandTime { get; set; } = 0.625f;
	/// <summary>Stillness between hops.</summary>
	[Property, Group( "Hopping" )] public float HopRestMin { get; set; } = 0.5f;
	[Property, Group( "Hopping" )] public float HopRestMax { get; set; } = 1.5f;
	// Seconds are at the graph's playback speeds (gen_skeleton_animgraph.py: attacks 1.15x). Measured in Blender:
	// attack_d_01 lunges 95 cm in its first 0.23 s slashing as it lands (frames 13-15), then raises the sword as a flourish; attack_d_02 is a
	// fast overhead chop at 0.23 s with a 70 cm lunge; the charge is charge_attack_start (0.55 s brace, steps back)
	// then charge_attack_to_attack (1.67 s, 132 cm lunge, chop at 0.57 s); guard_attack_parry (the pack's name; it is a
	// shield SWING, an attack, not a block) peaks at 0.35 s. The bash leads with the shield: hyper armor from the start.
	// attack_d_01: the STRIKE is the lunging slash at frames 13-15 (60 fps, 0.22-0.25 s at 1x, the sword's speed peak);
	// the overhead raise after it is a flourish, not a second hit.
	// LungeBeforeHit is what the engine can be TRUSTED to have delivered by the hit frame, which is not what the clip
	// holds. Re-measured 2026-09-18 (tools/measure_sword_clips.py --dir Assets/Animations/SkeletonSS, json in
	// tools/out/skeleton_ss_clips.json): attack_d_01 carries 94 cm = 37 u and attack_d_02 70 cm = 28 u, in both cases
	// smoothly over the first 14 frames and all of it before the strike -- the clips are exactly what the old numbers
	// said. In game they are not: `skeleton_debug` caught a corpse_defensive swinging from 72 u and standing at 70 u
	// at its own hit frame (2 u closed), and from 64 u having closed nothing, with 22-23 u of root motion arriving
	// across the whole clip instead of before the strike. Something between the animgraph and ApplyRootMotionDelta
	// spreads the travel out; until that is understood, an attack may not BET on the lunge to reach anybody, so these
	// are 0 and StartRange is what the blade alone covers. The lunge still happens, it just no longer buys reach.
	// Damage is HALF what it was until 2026-09-18: the floors went from 50-80 bodies to 100-160, so each one hits
	// for half as much and the danger comes from how many of them reach you. Traps were deliberately left alone.
	[Property, Group( "Attacks" )] public AttackSpec SwordA { get; set; } = new() { Name = "attack_d_01", HitTime = 0.25f, Duration = 0.85f, HitRange = 60f, LungeBeforeHit = 0f, ArcDegrees = 45f, Damage = 6f, Push = 160f, FaceLockBefore = 0.12f };
	[Property, Group( "Attacks" )] public AttackSpec SwordB { get; set; } = new() { Name = "attack_d_02", HitTime = 0.23f, Duration = 0.80f, HitRange = 60f, LungeBeforeHit = 0f, ArcDegrees = 45f, Damage = 6f, Push = 160f, FaceLockBefore = 0.12f };
	[Property, Group( "Attacks" )] public AttackSpec Charge { get; set; } = new() { Name = "charge", HitTime = 0.98f, Duration = 1.5f, HitRange = 70f, LungeBeforeHit = 0f, ArcDegrees = 40f, Damage = 9f, Push = 320f, FaceLockBefore = 0.35f, ArmorFrom = 0.48f };
	[Property, Group( "Attacks" )] public AttackSpec ShieldBash { get; set; } = new() { Name = "guard_attack_parry", HitTime = 0.30f, Duration = 0.75f, HitRange = 50f, LungeBeforeHit = 0f, ArcDegrees = 60f, Damage = 4f, Push = 260f, ArmorFrom = 0f, KeepsGuard = true, HitTag = "skeleton_bash" };
	/// <summary>
	/// The attack TABLE (the Undead Knight, tools/gen_knight_prefab.py): when it has entries it replaces the four fixed
	/// specs above. Entry N is the graph's attack_kind N (its state N in gen_knight_animgraph.py). Picked heden's way:
	/// one weighted roll over every entry whose cooldown is up and whose range window holds the player, the last
	/// pick at RepeatPenalty and the three before it at RecentPenalty; an entry with Weight 0 is reached only through
	/// another's ComboInto.
	/// </summary>
	[Property, Group( "Attacks" )] public List<AttackSpec> Attacks { get; set; } = new();
	[Property, Group( "Attacks" )] public float RepeatPenalty { get; set; } = 0.35f;
	[Property, Group( "Attacks" )] public float RecentPenalty { get; set; } = 0.7f;
	/// <summary>
	/// An attack's clip travel never carries the body closer than this to its target (0 = off): root motion plants
	/// AT the player, it never strides over them (heden's StepInStandoff; the knight's leaps are 120-140 u).
	/// </summary>
	[Property, Group( "Attacks" )] public float RootMotionStandoff { get; set; } = 0f;

	// ---------------------------------------------------------------- posture and the shield read (the knight)
	/// <summary>
	/// Posture (heden's boss): a body with PostureMax > 0 never flinches (CanStagger off) but every hit builds
	/// posture by PosturePerDamage x damage; full = a StaggerSeconds stagger (guard_heavy_hit, then dazed) taking
	/// StaggerDamageMultiplier. Decays PostureDecayPerSecond after PostureDecayDelay without a hit.
	/// </summary>
	[Property, Group( "Posture" )] public float PostureMax { get; set; } = 0f;
	[Property, Group( "Posture" )] public float PosturePerDamage { get; set; } = 0.6f;
	[Property, Group( "Posture" )] public float PostureDecayPerSecond { get; set; } = 6f;
	[Property, Group( "Posture" )] public float PostureDecayDelay { get; set; } = 2f;
	[Property, Group( "Posture" )] public float StaggerSeconds { get; set; } = 3.5f;
	[Property, Group( "Posture" )] public float StaggerDamageMultiplier { get; set; } = 1.5f;
	/// <summary>
	/// The shield read (heden's mind game), on when ShieldReactChance > 0: ShieldReactLightsNeeded frontal light hits
	/// inside ShieldReactWindow and he snaps the shield up for ShieldReactHoldSeconds (once per ShieldReactCooldown),
	/// but only when interruptible (between attacks, or an attack's recovery). Lights then bounce off it; a heavy on
	/// the raised shield BREAKS it: the blow lands at ShieldBreakDamageFactor, ShieldBreakPosture is added, and he is
	/// open for GuardBreakSeconds. Backstabs always land in full. A shield nobody tests just drops when the hold ends.
	/// </summary>
	[Property, Group( "Posture" )] public float ShieldReactChance { get; set; } = 0f;
	[Property, Group( "Posture" )] public float ShieldReactCooldown { get; set; } = 3f;
	[Property, Group( "Posture" )] public float ShieldReactHoldSeconds { get; set; } = 1.5f;
	[Property, Group( "Posture" )] public int ShieldReactLightsNeeded { get; set; } = 2;
	[Property, Group( "Posture" )] public float ShieldReactWindow { get; set; } = 1.6f;
	[Property, Group( "Posture" )] public float ShieldBreakDamageFactor { get; set; } = 1.25f;
	[Property, Group( "Posture" )] public float ShieldBreakPosture { get; set; } = 30f;
	/// <summary>Seconds the blade stays live after the hit frame: a sweep, so a player stepping through the arc is still caught.</summary>
	[Property, Group( "Combat" )] public float HitWindowSeconds { get; set; } = 0.12f;
	/// <summary>Where a code-driven lunge lands, as a share of the attack's HitRange: inside the reach, not in the player's face.</summary>
	[Property, Group( "Combat" )] public float LungeLandAt { get; set; } = 0.7f;
	/// <summary>How far past its LungeSpeed a lunge may speed up mid-flight to still land on a target that moved away.</summary>
	[Property, Group( "Combat" )] public float LungeCatchUp { get; set; } = 1.25f;
	private readonly HashSet<Guid> _hitPlayers = new();
	private float _closeInRange, _closeInUntil = -1f;
	private float _hitFrameDistance;
	/// <summary>Units the body lurches along the blade the instant a hit lands (before the hurt clip's own travel); blocked hits take half.</summary>
	[Property, Group( "Combat" )] public float ImpactShove { get; set; } = 10f;
	[Property, Group( "Combat" )] public float ImpactShoveSeconds { get; set; } = 0.05f;

	[ConVar( "skeleton_debug" )] public static bool DebugLog { get; set; }
	/// <summary>
	/// Everything a monster does in a fight runs this much faster (user: 10 %): the travel while approaching and
	/// engaged, the code lunges, and the attacks themselves -- the timings below read AttackT and SkeletonBody plays
	/// the clips at the same rate, so the blade still lands on its frame. Hurts, staggers and cooldowns are untouched.
	/// </summary>
	[ConVar( "npc_combat_speed" )] public static float CombatSpeed { get; set; } = 1.1f;
	public static float CombatRate => MathF.Max( 0.1f, CombatSpeed );
	/// <summary>
	/// The reaction window (user, 2026-09-22: "you open a door and you get immediately swarmed in the door opening"):
	/// a body that first sights the player stands where it is, turns to face them at npc_notice_turn degrees per
	/// second, and only sets off after a pause rolled between these two -- at least long enough to finish the turn,
	/// so one with its back to the door takes longer than one already looking at it. Each body rolls its own, which
	/// staggers a pack. A hit during the window skips it (Hurt hunts straight away), and an ambusher was already waiting.
	/// </summary>
	[ConVar( "npc_notice_min" )] public static float NoticeMin { get; set; } = 0.3f;
	[ConVar( "npc_notice_max" )] public static float NoticeMax { get; set; } = 0.5f;
	[ConVar( "npc_notice_turn" )] public static float NoticeTurnRate { get; set; } = 240f;
	/// <summary>
	/// This kind's own pace on top of npc_combat_speed: its attack clips (and with them the hit frames, the lunge and
	/// the busy time) and its chase speed. 1 for everything but the Tormented Soul, 0.8 (user, 2026-09-21: "he is just
	/// way too fast ... slow him down movement and attackspeed by 20%"; its walk/jog/run in the prefab went down with it).
	/// A prefab property, so every peer's SkeletonBody plays the clips at the same rate.
	/// </summary>
	[Property] public float PaceScale { get; set; } = 1f;
	/// <summary>The rate this body fights at: the global combat speed times its own pace.</summary>
	public float Pace => CombatRate * MathF.Max( 0.1f, PaceScale );
	/// <summary>In a fight: the states CombatSpeed applies to (SkeletonBody reads it for the playback rate on every peer).</summary>
	public bool InCombatPace => State is SkeletonState.Approach or SkeletonState.Engage or SkeletonState.Attack;
	/// <summary>Seconds into the current attack on the clip's own clock (sped up by CombatRate).</summary>
	private float AttackT => (Time.Now - _attackStart) * Pace;

	// ------------------------------------------------------------------ synced truth
	[Sync( SyncFlags.FromHost )] public SkeletonState State { get; set; }
	[Sync( SyncFlags.FromHost )] public float Health { get; set; } = 60f;
	[Sync( SyncFlags.FromHost )] public float MaxHealth { get; set; } = 60f;
	/// <summary>Host: topped up by the respawn trickle (DungeonBuilder.Respawn), not part of the floor's opening head count. Drops no shards.</summary>
	public bool Respawned { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Guard { get; set; }
	[Sync( SyncFlags.FromHost )] public int AttackCount { get; set; }
	[Sync( SyncFlags.FromHost )] public int AttackKind { get; set; }
	[Sync( SyncFlags.FromHost )] public int HurtCount { get; set; }
	/// <summary>0 f, 1 b, 2 l, 3 r light hurts, 4 the shield held (guard_light_hit), 5 guard broken (guard_heavy_hit).</summary>
	[Sync( SyncFlags.FromHost )] public int HurtKind { get; set; }
	/// <summary>Hits that did damage but did not stagger (hyper armor, poise): a flash, no clip.</summary>
	[Sync( SyncFlags.FromHost )] public int FlinchCount { get; set; }
	/// <summary>Bumped once per hop; SkeletonBody turns the change into the graph's `hop_trigger` pulse.</summary>
	[Sync( SyncFlags.FromHost )] public int HopCount { get; set; }
	/// <summary>The killing blow: unit direction the blade travelled (lifted a little) and where it touched, for the ragdoll's launch.</summary>
	[Sync( SyncFlags.FromHost )] public Vector3 DeathPush { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 DeathHitPosition { get; set; }
	[Sync( SyncFlags.FromHost )] public string BrainName { get; set; } = "";
	/// <summary>Which sword and shield it carries: body-group choices (SkeletonBody applies them on every peer).</summary>
	[Sync( SyncFlags.FromHost )] public int SwordIndex { get; set; }
	[Sync( SyncFlags.FromHost )] public int ShieldIndex { get; set; }
	/// <summary>
	/// Asleep: too far from every player to matter (SkeletonSystem decides). The host skips its brain, agent and
	/// steering; every peer's SkeletonBody hides the renderer so nothing animates. Wakes when a player comes near.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool Dormant { get; set; }
	/// <summary>Whose side (Team as an int on the wire, like AttackKind): hostile unless The Turncoat raised it.</summary>
	[Sync( SyncFlags.FromHost )] public int TeamId { get; set; }
	/// <summary>Raised by The Turncoat: fights for its summoner until RaisedUntil, then crumbles (or explodes).</summary>
	[Sync( SyncFlags.FromHost )] public bool Raised { get; set; }
	[Sync( SyncFlags.FromHost )] public float RaisedUntil { get; set; }
	/// <summary>0 a blow (the ragdoll launches), 1 crumbled (drops in place), 2 exploded.</summary>
	[Sync( SyncFlags.FromHost )] public int DeathKind { get; set; }
	/// <summary>The slow it is under (0..0.9) and the burn on it (0 none, 1 fire, 2 void): the body tints from these.</summary>
	[Sync( SyncFlags.FromHost )] public float SlowAmount { get; set; }
	[Sync( SyncFlags.FromHost )] public int BurnKind { get; set; }
	/// <summary>
	/// Which way the BODY points. It is synced as a number instead of being the root's rotation because the model
	/// now hangs on a child that is not networked, and because writing the root's transform at all would clear the
	/// NavMeshAgent's path (see OnAwake). SkeletonBody turns the model from this on every peer.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float BodyYaw { get; set; }

	public Team Team { get => (Team)TeamId; set => TeamId = (int)value; }

	/// <summary>Host only: the body it is fighting (the nearest enemy while idle, so pair it with State).</summary>
	public GameObject TargetObject => _target.IsValid() ? _target.GameObject : null;

	/// <summary>
	/// Host only: is any awake hostile monster in a fight with `player` right now (approaching, engaged, swinging or
	/// reeling from a hit)? The exit portal stays shut while one is (user).
	/// </summary>
	public static bool AnyHunting( GameObject player )
	{
		if ( !player.IsValid() ) return false;
		foreach ( var s in CombatTargets.Skeletons )
		{
			if ( !s.IsValid() || s.Dormant || s.Passive || s.Team != Team.Hostile ) continue;
			if ( s.State is not (SkeletonState.Approach or SkeletonState.Engage or SkeletonState.Attack or SkeletonState.Hurt) ) continue;
			if ( s.TargetObject == player ) return true;
		}
		return false;
	}
	/// <summary>Host only: who raised it (its follow target when idle).</summary>
	public GameObject Summoner { get; set; }
	/// <summary>Host only: The Turncoat's stack when it was raised (x2 explodes on expiry).</summary>
	public int RaisedStacks { get; set; }
	/// <summary>Host only: what spells left on it.</summary>
	public SkeletonStatus Status => _status ??= new SkeletonStatus();
	private SkeletonStatus _status;

	// ICombatant
	public bool CanBeTargeted => !IsDead && !Dormant;
	/// <summary>
	/// How tall this body stands, for everything that aims at its middle: spell homing and area damage take
	/// `BodyHeight * 0.55` as the chest, hazards test containment against it. It was 70 for every skeleton
	/// until the monsters arrived — a rat is 22 units tall, and a bolt homing 38 units over its head misses.
	/// A flier's includes its hover, since its root sits on the floor and its body does not.
	/// </summary>
	[Property, Group( "Combat" )] public float BodyHeight { get; set; } = 70f;
	/// <summary>Where its line-of-sight rays start, above the root. Same story: a rat does not see from 60 units up.</summary>
	[Property, Group( "Combat" )] public float EyeHeight { get; set; } = 60f;

	/// <summary>The shield took the hit and stays up (no damage).</summary>
	public const int HurtKindGuardHit = 4;
	/// <summary>The shield took the hit and the guard broke (no damage, open for GuardBreakSeconds).</summary>
	public const int HurtKindBlocked = 5;

	public bool IsDead => State == SkeletonState.Dead;
	/// <summary>A one-shot owns the body (the graph's `busy`): attack, hurt, or a hopper's hop.</summary>
	public bool Busy => State is SkeletonState.Attack or SkeletonState.Hurt or SkeletonState.Hop;
	/// <summary>Nothing is happening: idling, or between hops. Dormancy and the ambience both mean this by "Idle".</summary>
	public bool AtRest => State is SkeletonState.Idle or SkeletonState.Hop;
	public SkeletonBrainValues Brain => _brain;
	/// <summary>Where the host wants the body to go right now (overlay).</summary>
	public Vector3? MoveTarget => _wantMove ? _wantTarget : null;
	/// <summary>What the brain asked for this tick, what the agent was told, and how fast the body is really travelling (skeleton_speeds).</summary>
	public float AskedSpeed => _wantMove ? _wantSpeedRaw : 0f;
	public float AgentSpeed => _agent.IsValid() && _agent.Enabled ? _agent.MaxSpeed : 0f;
	public float RealSpeed => _hostVelocity.Length;
	/// <summary>The host is moving the body itself because the agent is not (no navmesh, or wedged).</summary>
	public bool SteeringStraight { get; private set; }

	public string DebugLabel()
	{
		var v = _agent.IsValid() && _agent.Enabled ? _agent.Velocity.WithZ( 0f ).Length : 0f;
		return $"{KindName} {BrainName}{(Team == Team.Ally ? " ALLY" : "")} w{SwordIndex}/s{ShieldIndex} {State}{(Guard ? " guard" : "")}{(Dormant ? " DORMANT" : "")} hp {Health:0}/{MaxHealth:0}\nagent {v:0} u/s{(SteeringStraight ? " FALLBACK" : "")}{(_ambush ? " ambush" : "")}{(Noticing ? $" noticing {_noticeUntil - Time.Now:0.0}s" : "")}";
	}

	/// <summary>Host: put the skeleton to sleep or wake it (the agent leaves the crowd while asleep).</summary>
	public void SetDormant( bool dormant )
	{
		if ( IsProxy || Dormant == dormant || State == SkeletonState.Dead ) return;
		Dormant = dormant;
		if ( dormant )
		{
			Stop();
			SteeringStraight = false;
			if ( _agent.IsValid() ) _agent.Enabled = false;
		}
		else
		{
			if ( _agent.IsValid() ) _agent.Enabled = true;
			_hostPrevPos = WorldPosition;
			_stalledSince = -1f;
			_nextDecision = Time.Now;
		}
	}

	private bool _wantMove;
	private Vector3 _wantTarget;
	private float _wantSpeed;
	private float _stalledSince = -1f;
	private int _stallRetries;
	private float _fallbackUntil = -1f;
	private Vector3 _hostPrevPos;
	private Vector3 _hostVelocity;
	private bool _fallbackLogged;

	private static readonly string[] LosIgnore = { "npc", "enemy", "ally", "player", "viewmodel", "posesource", "spellfire", "trigger", "ragdoll" };

	private NavMeshAgent _agent;
	private SkinnedModelRenderer _renderer;
	private CapsuleCollider _collider;
	private SkeletonBrainValues _brain;
	private Random _rng;

	private ICombatant _target;
	private float _nextDecision;
	private float _nextAttackAt;
	private float _guardUntil;
	private float _guardRaisedAt = float.NegativeInfinity;
	private float _strafeUntil;
	private float _strafeSign = 1f;
	private float _backOffUntil;
	private float _busyUntil;          // attack / hurt / death end
	private Kind _attack;
	private AttackSpec _spec;
	private float _attackStart;
	private bool _hitDone;
	private float _faceYaw;
	private bool _faceLocked;
	private bool _hitDone2;            // the second hit frame of a two-strike clip (AttackSpec.HitTime2)
	private Vector3 _lastMoveTo;
	private float _lastMoveAt;
	private Vector3 _home;
	private float _nextWander;
	private float _rootMotionTotal;
	private float _rootMotionAtHit;    // how much of it had arrived by the hit frame (skeleton_debug)
	private int _moveRejected;         // MoveBodyBy steps the navmesh projection threw away this hop (skeleton_debug)
	private float _swingDamage;        // this strike's rolled damage (RollSwingDamage)
	private float _diedAt;
	private float _lastHurtAt = float.NegativeInfinity;
	private int _hurtChain;
	private float _poiseUntil = float.NegativeInfinity;
	private int _guardLeft;
	private float _nextSeeCheck;
	private float _lastSeenAt = -100f;   // sight memory for a target beyond the height band (StillHunting)
	private bool _ambush;
	private bool _openerArmed;
	private float _noticeUntil;          // > 0 while standing in the reaction window (TickIdle); the approach starts when it passes
	private bool Noticing => _noticeUntil > 0f;

	protected override void OnAwake()
	{
		_agent = Components.Get<NavMeshAgent>( true );
		_renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		_collider = Components.Get<CapsuleCollider>( true );
		// The body turns on the MODEL's object, never on this one. NavMeshAgent subscribes to its GameObject's
		// OnTransformChanged and answers ANY write -- a pure rotation included -- with SetAgentPosition, and the
		// crowd's SetPosition does `Path.Clear(); NeedsPath = true` with no check that the position even moved.
		// Turning the root every frame therefore deleted the path every frame: the agent was never navigating, so
		// it could only creep on whatever corner data survived a single tick. That is the whole "struggles to run".
	}

	private VoidMage.Dungeon.DungeonBuilder _dungeon;

	protected override void OnStart()
	{
		_home = WorldPosition;
		_hostPrevPos = WorldPosition;
		_dungeon = Scene.GetAllComponents<VoidMage.Dungeon.DungeonBuilder>().FirstOrDefault();
		_faceYaw = WorldRotation.Yaw();
		_bodyYaw = _faceYaw;
		if ( !IsProxy ) BodyYaw = _bodyYaw;
		if ( IsProxy )
		{
			if ( _agent.IsValid() ) _agent.Enabled = false;
			return;
		}
		if ( _brain is null ) Configure( Profile, Game.Random.Next(), DefaultSword, DefaultShield );
	}

	/// <summary>Host: roll the brain and set the weapons (called by the spawner before NetworkSpawn, or by OnStart with the prefab's defaults).</summary>
	public void Configure( SkeletonBrainProfile profile, int seed, int sword, int shield )
	{
		_rng = new Random( seed );
		_brain = profile.IsValid() ? profile.Roll( _rng ) : SkeletonBrainProfile.Defaults();
		BrainName = profile.IsValid() ? profile.ResourceName : "default";
		SwordIndex = Math.Max( 0, sword );
		ShieldIndex = Math.Max( 0, shield );
		// deeper floors are tougher (SkeletonSpawner.FloorHealthScale): the staff only ever grows during a run
		// (not ambient life: a frog has its one hit point on every floor)
		MaxHealth = _brain.Health * (Passive ? 1f : SkeletonSpawner.FloorHealthScale);
		Health = MaxHealth;
		_floorDamage = SkeletonSpawner.FloorDamageScale;
		_nextAttackAt = Time.Now + Roll( _brain.AttackCooldownMin, _brain.AttackCooldownMax );
		_nextWander = Time.Now + Roll( 3f, 8f );
		if ( _agent.IsValid() )
		{
			_agent.UpdateRotation = false;
			_agent.MaxSpeed = JogSpeed;
			_agent.Acceleration = JogSpeed * 4f;
		}
	}

	/// <summary>Skeletons placed in an ambush room stand still until they see a player.</summary>
	public void SetAmbush( bool ambush ) => _ambush = ambush;

	// ------------------------------------------------------------------ host loop

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || _brain is null ) return;

		if ( State == SkeletonState.Dead )
		{
			if ( Time.Now > _diedAt + DeathLingerSeconds ) VoidMage.Dev.WorldTape.Retire( GameObject );
			return;
		}
		if ( Dormant ) return;
		if ( Raised && Team == Team.Ally && Time.Now > RaisedUntil ) { Expire(); return; }

		AcquireTarget();

		switch ( State )
		{
			case SkeletonState.Idle: TickIdle(); break;
			case SkeletonState.Approach: TickApproach(); break;
			case SkeletonState.Engage: TickEngage(); break;
			case SkeletonState.Attack: TickAttack(); break;
			case SkeletonState.Hurt: TickHurt(); break;
			case SkeletonState.Hop: TickHop(); break;
		}

		if ( Guard && Time.Now > _guardUntil && State != SkeletonState.Attack ) Guard = false;
		if ( PostureMax > 0f && _posture > 0f && Time.Now > _postureHitAt + PostureDecayDelay )
			_posture = MathF.Max( 0f, _posture - PostureDecayPerSecond * Time.Delta );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || _brain is null || Dormant ) return;
		// Per rendered frame on the host: turning and any self-steering in FixedUpdate strobe at 50 Hz against
		// the frame rate (heden's ApplyLineDrive exists for the same reason).
		if ( State == SkeletonState.Dead ) return;
		TickShove( Time.Delta );
		if ( ApplyRootMotion && Busy ) ApplyRootMotionDelta();
		if ( State == SkeletonState.Attack && _spec is { LungeSpeed: > 0f } ) TickLunge( Time.Delta );
		if ( State == SkeletonState.Hop ) TickHopTravel( Time.Delta );
		Steer( Time.Delta );
		TurnToward( Time.Delta );
	}

	// ------------------------------------------------------------------ steering: the agent first, a ladder under it

	/// <summary>
	/// The NavMeshAgent carries the body. heden's stall ladder sits under it: no progress for 0.5 s while a real
	/// distance remains -> the path is re-issued; three fruitless retries -> the body is pinned to the nearest navmesh
	/// point and the agent re-registered with the crowd, and the host walks it straight for a second (sphere sweep
	/// ahead, sliding along what it meets, projected onto the navmesh) before trusting the agent again.
	/// </summary>
	private void Steer( float dt )
	{
		var pos = WorldPosition;
		_hostVelocity = dt > 0f ? ((pos - _hostPrevPos) / dt).WithZ( 0f ) : Vector3.Zero;
		_hostPrevPos = pos;

		if ( !_wantMove || Busy || State == SkeletonState.Dead )
		{
			SteeringStraight = false;
			_stalledSince = -1f;
			_stallRetries = 0;
			return;
		}
		var flat = (_wantTarget - pos).WithZ( 0f );
		if ( flat.Length < 20f ) { SteeringStraight = false; _stalledSince = -1f; _stallRetries = 0; return; }

		var navReady = !_dungeon.IsValid() || _dungeon.NavMeshReady || VoidMage.Boss.HellArena.NavReady;
		var agentOk = _agent.IsValid() && _agent.Enabled;
		var agentMoving = agentOk && _agent.Velocity.WithZ( 0f ).LengthSquared > 25f;
		if ( agentMoving ) { _stalledSince = -1f; _stallRetries = 0; }
		else if ( _stalledSince < 0f ) _stalledSince = Time.Now;

		if ( navReady && agentOk && Time.Now >= _fallbackUntil && _stalledSince >= 0f && Time.Now - _stalledSince > 0.5f )
		{
			_stalledSince = Time.Now;
			_stallRetries++;
			if ( _stallRetries < 3 )
			{
				_lastMoveAt = -1f;                      // force a fresh MoveTo
				// the RAW speed: MoveTo applies the slow itself, and re-feeding it the already-slowed number
				// multiplied the slow in again on every retry until the body barely moved
				MoveTo( _wantTarget, _wantSpeedRaw );
			}
			else
			{
				// wedged: pin to the mesh and walk it straight for a moment. It no longer cycles _agent.Enabled --
				// that de-registers and re-registers the agent with the crowd, which is expensive and disturbs
				// every neighbour. The pin alone already clears the path and forces a fresh plan.
				_stallRetries = 0;
				var pin = Scene.NavMesh.GetClosestPoint( pos, 64f );
				if ( pin.HasValue ) _agent.SetAgentPosition( pin.Value );
				// The straight walk only helps when a straight line gets there. A body queueing behind another at a
				// doorway also reads as "stalled", and walking IT straight at a destination beyond the wall dragged it
				// sideways along the wall, away from the door it was waiting for. With something in the way the pin and
				// the fresh plan are the whole remedy.
				var chest = Vector3.Up * 50f;
				var blocked = Scene.Trace.Ray( pos + chest, _wantTarget + chest ).Radius( 8f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run().Hit;
				if ( !blocked ) _fallbackUntil = Time.Now + 1f;
				if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} wedged at {pos:0}, agent cycled" );
			}
		}

		SteeringStraight = !navReady || !agentOk || Time.Now < _fallbackUntil;
		if ( !SteeringStraight ) return;
		if ( !_fallbackLogged && DebugLog ) { _fallbackLogged = true; Log.Info( $"[Skeleton] {BrainName} steering straight ({(navReady ? "agent wedged" : "no navmesh")})" ); }

		var dir = flat.Normal;
		var step = dir * _wantSpeed * dt;
		var from = pos + Vector3.Up * 30f;
		var sweep = Scene.Trace.Ray( from, from + step ).Radius( 14f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( sweep.Hit )
		{
			// slide along the wall
			var slide = step - sweep.Normal * Vector3.Dot( step, sweep.Normal );
			var second = Scene.Trace.Ray( from, from + slide ).Radius( 14f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run();
			step = second.Hit ? second.EndPosition - from : slide;
		}
		var next = pos + step.WithZ( 0f );
		if ( navReady )
		{
			var onMesh = Scene.NavMesh.GetClosestPoint( next, 24f );
			if ( !onMesh.HasValue ) return;
			next = onMesh.Value;
		}
		else
		{
			var ground = Scene.Trace.Ray( next + Vector3.Up * 40f, next - Vector3.Up * 80f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( ground.Hit ) next = ground.HitPosition;
		}
		if ( agentOk ) _agent.SetAgentPosition( next );
		else WorldPosition = next;
	}

	private void AcquireTarget()
	{
		if ( Passive ) { _target = null; return; }   // a critter never has a target, so it never leaves Idle
		var held = _target.IsValid() && !_target.IsDead;
		if ( held && Time.Now < _nextDecision - DecisionInterval * 0.5f ) return;
		// a chase keeps its target; an idle body re-picks, so a player arriving on ITS floor beats one standing below
		if ( held && State != SkeletonState.Idle ) return;
		ICombatant best = null;
		var bestScore = float.MaxValue;
		// whoever is on the other side: a hostile hunts players and raised allies, an ally hunts hostiles. Someone on
		// this floor (inside the height band) always wins over someone nearer on another: the aggro gate cannot pass
		// a player through a slab, and holding that one would hide the reachable one
		foreach ( var c in CombatTargets.HostileTo( Team ) )
		{
			if ( !c.IsValid() || !c.CanBeTargeted || c.GameObject == GameObject ) continue;
			var score = c.WorldPosition.Distance( WorldPosition ) + (WithinHeightBand( c, 1f ) ? 0f : 100000f);
			if ( score < bestScore ) { bestScore = score; best = c; }
		}
		_target = best;
	}

	private void TickIdle()
	{
		// the line-of-sight trace runs a few times a second, not every physics tick: idle skeletons are the many
		if ( Noticing )
		{
			// the reaction window: rooted, turning to face them, until the roll passes. The target is kept even if
			// they step back out of sight -- it saw them, and the approach knows how to give up.
			if ( !_target.IsValid() || _target.IsDead ) { _noticeUntil = 0f; return; }
			if ( _wantMove ) Stop();
			FaceTarget();
			if ( Time.Now < _noticeUntil ) return;
			_noticeUntil = 0f;
			_lastSeenAt = Time.Now;
			_openerArmed = true;   // first sight: an attack with OpenerRange may open the fight from the approach
			Go( SkeletonState.Approach );
			return;
		}
		if ( _target.IsValid() && Time.Now >= _nextSeeCheck )
		{
			_nextSeeCheck = Time.Now + 0.2f;
			var d = Distance( _target );
			if ( d < _brain.AggroRange && WithinHeightBand( _target, 1f ) && CanSee( _target ) )
			{
				_lastSeenAt = Time.Now;
				if ( Team == Team.Hostile && !_ambush )
				{
					// noticed: stand and turn first (see NoticeMin). The window is at least the turn itself plus a beat
					var to = (_target.WorldPosition - WorldPosition).WithZ( 0f );
					var turn = to.LengthSquared < 1f ? 0f : MathF.Abs( ShortestDegrees( _bodyYaw, to.Normal.EulerAngles.yaw ) );
					var window = MathF.Max( Roll( NoticeMin, NoticeMax ), turn / MathF.Max( 30f, NoticeTurnRate ) + 0.15f );
					_noticeUntil = Time.Now + window;
					if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} noticed at {d:0} u, {turn:0} deg to turn, sets off in {window:0.00} s" );
					return;
				}
				_openerArmed = true;   // first sight: an attack with OpenerRange may open the fight from the approach
				Go( SkeletonState.Approach );
				return;
			}
		}
		if ( Team == Team.Ally )
		{
			// a raised minion with nothing to fight keeps to its summoner's heel
			if ( Summoner.IsValid() && Distance( Summoner.WorldPosition ) > 200f ) MoveTo( Toward( Summoner.WorldPosition, 120f ), JogSpeed );
			else if ( _wantMove && Summoner.IsValid() && Distance( Summoner.WorldPosition ) < 130f ) Stop();
			FaceVelocity();
			return;
		}
		if ( Hops ) { TickIdleHopping(); return; }
		if ( _brain.Wander && !_ambush && Time.Now > _nextWander )
		{
			_nextWander = Time.Now + Roll( 5f, 12f );
			var p = Scene.NavMesh.GetRandomPoint( _home, 300f );
			// only to somewhere it can see from where it lives: a wander never takes it through a doorway into the next
			// room, where the packs used to pile up (user, 2026-09-20)
			if ( p.HasValue && !Scene.Trace.Ray( _home + Vector3.Up * EyeHeight, p.Value + Vector3.Up * EyeHeight ).WithoutTags( LosIgnore ).Run().Hit ) MoveTo( p.Value, WalkSpeed );
		}
		FaceVelocity();
	}

	// ------------------------------------------------------------------ hopping

	private Vector3 _hopTarget;
	private bool _hasHopTarget;
	private float _nextHop, _hopStart;

	/// <summary>
	/// A hopper wanders in jumps: pick a spot, then hop toward it once per rest interval. The agent is never
	/// asked to walk there — `MoveTo` and `Stop` each throw the crowd path away, so driving a duty cycle
	/// through them is the documented recipe for an NPC that creeps and jitters.
	/// </summary>
	private void TickIdleHopping()
	{
		if ( _wantMove ) Stop();
		if ( !_hasHopTarget || WorldPosition.WithZ( 0f ).Distance( _hopTarget.WithZ( 0f ) ) < HopSpeed * 0.3f )
		{
			var p = Scene.NavMesh.GetRandomPoint( _home, 300f );
			_hasHopTarget = p.HasValue;
			if ( p.HasValue ) _hopTarget = p.Value;
		}
		if ( !_hasHopTarget || Time.Now < _nextHop ) return;
		var to = (_hopTarget - WorldPosition).WithZ( 0f );
		if ( to.LengthSquared > 1f ) _faceYaw = to.Normal.EulerAngles.yaw;   // hops go where it is already looking
		StartHop();
	}

	/// <summary>One hop: a one-shot exactly like an attack, so the graph restarts the clip from frame 0.</summary>
	private void StartHop()
	{
		_hopStart = Time.Now;
		_busyUntil = Time.Now + HopSeconds;
		HopCount++;
		Stop();
		_rootMotionTotal = 0f;
		Go( SkeletonState.Hop );
	}

	private void TickHop()
	{
		if ( Time.Now < _busyUntil ) return;
		// what the hop actually did: MoveBodyBy adds every step it managed to _rootMotionTotal (reset in StartHop),
		// and counts the frames the navmesh projection threw away, so "animates but does not move" has a number on it
		if ( DebugLog ) Log.Info( $"[Skeleton] {KindName} hop done: travelled {_rootMotionTotal:0} u of {HopSpeed * (HopLandTime - HopLiftTime):0}, {_moveRejected} frames refused by the navmesh, agent {(_agent.IsValid() && _agent.Enabled ? "on" : "off")}, yaw {_bodyYaw:0}" );
		_moveRejected = 0;
		_nextHop = Time.Now + Roll( HopRestMin, HopRestMax );
		Go( SkeletonState.Idle );
	}

	/// <summary>
	/// The travel, per rendered frame: only between the clip's measured lift and land, and through
	/// <see cref="MoveBodyBy"/> so it stays on the navmesh and the crowd agent keeps up (the flier lunges the
	/// same way). Outside that window the body does not move at all, which is the whole point.
	/// </summary>
	private void TickHopTravel( float dt )
	{
		var t = Time.Now - _hopStart;
		if ( t < HopLiftTime || t > HopLandTime ) return;
		MoveBodyBy( Rotation.FromYaw( _bodyYaw ).Forward * (HopSpeed * dt) );
	}

	private void TickApproach()
	{
		if ( !_target.IsValid() ) { Go( SkeletonState.Idle ); return; }
		TrackSight();
		var d = Distance( _target );
		// the opener (the soul's leap): fired once per sighting, from the approach, inside its window (it stays armed
		// while the player is on another floor: the leap would land on nothing)
		var (openerKind, opener) = Opener();
		if ( _openerArmed && opener is { OpenerRange: > 0f } && d <= opener.OpenerRange && OnMyLevel( _target ) )
		{
			_openerArmed = false;
			if ( d >= opener.MinRange && Time.Now >= _nextSeeCheck - 0.2f && CanSee( _target ) )
			{
				StartAttack( openerKind );
				return;
			}
		}
		// the engage loop (and its swings) only starts with the player on this floor: planar distance says nothing
		// about a player on the tier above or below, and it stood under them swinging at the ceiling. Until the
		// nav agent has walked it up or down to them it keeps approaching.
		//
		// And only with a walkable straight line to them. Planar distance says nothing about a wall either: a player
		// standing beside a doorway is "in range" through the masonry, and it made its stand against the wall, in or
		// next to the opening, swinging at stone while the player picked it off round the jamb (WalkLineClear).
		var clear = WalkLineClear( d );
		if ( d <= EngageRange && OnMyLevel( _target ) && clear ) { Go( SkeletonState.Engage ); return; }
		if ( d > _brain.AggroRange * 1.6f ) { LoseTarget( $"out of range ({d:0} u)" ); return; }
		if ( !StillHunting( _target ) ) { LoseTarget( $"another floor ({HeightGapToTarget:0} u), last seen {LastSeenAgo:0.0} s ago" ); return; }
		// NO SHIELD ON THE WAY IN. The guard locomotion node has no `run` item at all, so a skeleton that raised the
		// shield at GuardApproachRange 120 spent the rest of its charge travelling 243 u/s while playing a 159 u/s
		// guard jog: a 53 % foot slide that reads as a fast body failing to walk. The shield belongs to the engage
		// loop, where the clips for it exist.
		//
		// Then just run at them: one destination, on the straight line, `standoff` short of the player (heden's
		// standoff -- the crowd avoids other agents, not the player's capsule), led far enough ahead that the agent
		// runs to it instead of braking into it, at RunSpeed with no exceptions.
		//
		// The standoff point only means something with a clear line: through a wall it lands on THIS side of the wall
		// (or inside it, and the agent snaps it to the nearest floor, which is this side too), so the path ended at
		// the wall instead of going round through the door. Without the line the destination is the player's own
		// spot, which only a real route reaches.
		MoveTo( clear ? Lead( Toward( _target.WorldPosition, EngageRange * 0.6f ) ) : _target.WorldPosition, CloseSpeed( d ) );
		FaceTravel();
	}

	private void TickEngage()
	{
		if ( !_target.IsValid() ) { Go( SkeletonState.Idle ); return; }
		TrackSight();
		var d = Distance( _target );
		if ( d > _brain.AggroRange ) { LoseTarget( $"out of range ({d:0} u)" ); return; }
		if ( !StillHunting( _target ) ) { LoseTarget( $"another floor ({HeightGapToTarget:0} u), last seen {LastSeenAgo:0.0} s ago" ); return; }
		if ( d > EngageRange * 2.5f || !OnMyLevel( _target ) ) { Go( SkeletonState.Approach ); return; }
		// the player slipped round a jamb or behind a wall: stop fencing with the masonry and go round (the approach
		// paths to their own position until the line is clear again)
		if ( !WalkLineClear( d ) ) { Go( SkeletonState.Approach ); return; }

		FaceTarget();

		if ( Time.Now >= _nextDecision )
		{
			_nextDecision = Time.Now + DecisionInterval;
			if ( Decide( d ) ) return;
		}

		// footwork between decisions
		if ( Time.Now < _closeInUntil && d > _closeInRange )
		{
			_holdingGround = false;
			MoveTo( Toward( _target.WorldPosition, _closeInRange ), CloseSpeed( d ) );
		}
		else if ( Time.Now < _strafeUntil )
		{
			_holdingGround = false;
			var toMe = (WorldPosition - _target.WorldPosition).WithZ( 0f );
			var around = Rotation.FromYaw( _strafeSign * 40f ) * toMe.Normal;
			MoveTo( Lead( _target.WorldPosition + around * _brain.PreferredRange ), WalkSpeed );
		}
		else if ( Time.Now < _backOffUntil )
		{
			_holdingGround = false;
			var away = (WorldPosition - _target.WorldPosition).WithZ( 0f ).Normal;
			MoveTo( Lead( _target.WorldPosition + away * _brain.PreferredRange ), WalkSpeed );
		}
		// Standing at the preferred distance is a BAND, not a line. Judged against one threshold the skeleton
		// flipped between MoveTo and Stop at tick rate the instant the player drifted, and a crowd agent throws
		// its path away on every one of those calls: it never got to accelerate, so it crept forward in tiny
		// bursts and jittered. It now closes until it is INSIDE the preferred range and does not set off again
		// until the gap opens well past it.
		else if ( _holdingGround ? d > _brain.PreferredRange * 1.4f : d > _brain.PreferredRange )
		{
			_holdingGround = false;
			MoveTo( Toward( _target.WorldPosition, _brain.PreferredRange * 0.9f ), CloseSpeed( d ) );
		}
		// Never make a stand IN a doorway. A single opening passes one body at a time, so the one that stopped in it
		// at its preferred distance corked it, and the rest of the pack queued behind it while the player fought them
		// one by one. In the throat of a doorway it keeps coming until it is through or in the player's face.
		else if ( d > DoorwayPressRange && VoidMage.Dungeon.DoorNet.InAnyDoorway( WorldPosition, DoorwayThroat ) )
		{
			_holdingGround = false;
			MoveTo( Toward( _target.WorldPosition, DoorwayPressRange * 0.9f ), CloseSpeed( d ) );
		}
		else
		{
			_holdingGround = true;
			Stop();
		}
	}

	/// <summary>
	/// How fast it closes a gap. There is ONE rule and no exceptions: closing distance is always a run, whatever it
	/// is carrying and whatever its brain prefers. Walking is footwork -- circling and backing off between swings --
	/// and never travel.
	///
	/// This used to step down a ladder (run, then jog inside 250 u, then walk the moment the shield came up or the
	/// brain was defensive), all of which fire long before the skeleton can reach you. It also flipped the agent's
	/// MaxSpeed 243 to 159 and back on every fixed tick while the gap sat on EngageRange, since MoveTo rewrites
	/// MaxSpeed before its own re-path throttle.
	/// </summary>
	private float CloseSpeed( float gap ) => RunSpeed;

	/// <summary>One decision tick while engaged. True when it committed to an attack.</summary>
	private bool Decide( float distance )
	{
		var free = !Guard || Time.Now > _guardUntil;

		// attack: one roll over the rotation (charge, shield bash, else one of the two sword swings); the pick only
		// goes when the player is inside its own start range, otherwise it closes in and rolls again shortly
		//
		// Swing() and not a bare coin flip: a body whose two swings are the SAME clip (every monster with one
		// attack animation) must never pick SwordB, because the animgraph has no separate state for it and the
		// damage lands on a timer regardless. That combination hit the player for full damage with nothing
		// playing, half the time. The graph now covers all four kinds too, so this is the second line.
		if ( Time.Now >= _nextAttackAt && UsesTable )
		{
			// the knight: the weighted table pick (DecideFromTable), the brain's two chances mean nothing to it
			if ( DecideFromTable( distance ) ) return true;
		}
		else if ( Time.Now >= _nextAttackAt )
		{
			var r = _rng.NextDouble();
			var kind = r < _brain.ChargeChance ? Kind.Charge
				: r < _brain.ChargeChance + _brain.ShieldBashChance ? Kind.ShieldBash
				: Swing();
			var spec = Spec( kind );
			if ( distance < spec.MinRange )
			{
				// too close for that one (the leap wants room): a plain swing instead
				kind = Swing();
				spec = Spec( kind );
			}
			if ( distance <= spec.StartRange )
			{
				StartAttack( kind );
				return true;
			}
			if ( distance > spec.StartRange )
			{
				// close in first: for a moment the footwork aims INSIDE this attack's start range (the preferred
				// distance of a cautious brain can sit beyond the swords' reach, and it stood there re-rolling)
				_nextAttackAt = Time.Now + 0.15f;
				_closeInRange = spec.StartRange * 0.8f;
				_closeInUntil = Time.Now + 0.6f;
			}
		}

		if ( free && _rng.NextDouble() < _brain.GuardChance )
			RaiseGuard( Roll( _brain.GuardHoldMin, _brain.GuardHoldMax ) );

		// Footwork FILLS THE GAP between attacks; it never delays one. Rolled every decision tick regardless of the
		// cooldown, a skeleton standing in range with its swing ready would first wander off on a 0.8-1.8 s circle at
		// walking pace, and since a strafe re-rolls the moment it expires it almost always had one running. That is
		// the slow jittery shuffle: a walk clip creeping to a destination barely 30 units away, direction flipping
		// each time. It went unnoticed with one skeleton because a lone one spends its time attacking instead.
		if ( Time.Now < _nextAttackAt && Time.Now > _strafeUntil && Time.Now > _backOffUntil )
		{
			if ( distance < _brain.PreferredRange * 0.6f && _rng.NextDouble() < _brain.BackOffChance )
				_backOffUntil = Time.Now + Roll( 0.5f, 1.0f );
			else if ( _rng.NextDouble() < _brain.StrafeChance )
			{
				_strafeSign = _rng.Next( 2 ) == 0 ? -1f : 1f;
				_strafeUntil = Time.Now + Roll( _brain.StrafeSecondsMin, _brain.StrafeSecondsMax );
			}
		}
		return false;
	}

	private void RaiseGuard( float hold )
	{
		if ( !Guard ) { _guardRaisedAt = Time.Now; _guardLeft = GuardPoints; }
		Guard = true;
		_guardUntil = Time.Now + hold;
	}

	private void StartAttack( Kind kind )
	{
		_attack = kind;
		_spec = Spec( kind );
		_attackStart = Time.Now;
		_busyUntil = Time.Now + _spec.Duration / Pace;
		_hitDone = false;
		_hitDone2 = false;
		_faceLocked = false;
		if ( !(UsesTable ? _spec.KeepsGuard : kind == Kind.ShieldBash) ) Guard = false;
		if ( UsesTable ) NoteTablePick( (int)kind - 1 );
		AttackKind = (int)kind;
		AttackCount++;
		Stop();
		_rootMotionTotal = 0f;
		_rootMotionAtHit = 0f;
		RollSwingDamage();                 // re-rolled when the blade actually goes live; never left stale
		Go( SkeletonState.Attack );
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} attack {kind} at {Distance( _target ):0} u (start {_spec.StartRange:0}, lunge {_spec.LungeBeforeHit:0}, reach {_spec.HitRange:0})" );
	}

	private void TickAttack()
	{
		var t = AttackT;
		if ( !_faceLocked )
		{
			FaceTarget();
			if ( t >= _spec.HitTime - _spec.FaceLockBefore ) _faceLocked = true;
		}
		// the blade is live for a short window from the hit frame (a sweep, not an instant): every player it
		// reaches during the window is hit once
		if ( t >= _spec.HitTime && t <= _spec.HitTime + HitWindowSeconds )
		{
			if ( !_hitDone ) { _hitDone = true; _hitPlayers.Clear(); _hitFrameDistance = _target.IsValid() ? Distance( _target ) : -1f; _rootMotionAtHit = _rootMotionTotal; RollSwingDamage(); }
			LandHit();
		}
		// a two-strike clip (the knight's attack_c_02): a second window, every player hittable again
		if ( _spec.HitTime2 > 0f && t >= _spec.HitTime2 && t <= _spec.HitTime2 + HitWindowSeconds )
		{
			if ( !_hitDone2 ) { _hitDone2 = true; _hitPlayers.Clear(); RollSwingDamage(); }   // the second strike rolls its own damage
			LandHit();
		}
		if ( Time.Now >= _busyUntil )
		{
			if ( DebugLog )
			{
				// a whiff says where the player was at the hit frame, planar and in height, and how far the lunge went
				var whiff = _hitPlayers.Count == 0 && _target.IsValid();
				var dz = _target.IsValid() ? _target.WorldPosition.z - WorldPosition.z : 0f;
				var off = _target.IsValid() ? ShortestDegrees( _faceYaw, (_target.WorldPosition - WorldPosition).WithZ( 0f ).Normal.EulerAngles.yaw ) : 0f;
				// root motion is reported at the hit frame AND over the whole clip: the clips put all of their travel
				// before the strike (tools/out/skeleton_ss_clips.json), so "at hit" far below "total" is the engine
				// delivering the lunge late, which is why no attack's StartRange counts on it any more
				Log.Info( $"[Skeleton] {_spec.Name} done, root motion {_rootMotionAtHit:0} u at the hit / {_rootMotionTotal:0} u total{(whiff ? $", WHIFF: player at {_hitFrameDistance:0} u (reach {_spec.HitRange:0}), {off:0}° off the facing (arc {_spec.ArcDegrees:0}), dz {dz:0}" : "")}" );
			}
			// a combo flows straight into the next clip with no breath (the shield read may have cut this one short: no combo then)
			if ( UsesTable && !Guard && TryCombo() ) return;
			_nextAttackAt = Time.Now + Roll( _brain.AttackCooldownMin, _brain.AttackCooldownMax );
			Go( SkeletonState.Engage );
		}
	}

	/// <summary>The kind's strength, and the second sword's edge on sword attacks (the shield bash is the same shield either way).</summary>
	/// <summary>The depth's share of this body's damage, taken when it was configured (a raised ally hits as it always did).</summary>
	private float _floorDamage = 1f;
	private float DamageScaleFor( Kind kind ) => DamageScale * (Team == Team.Hostile ? _floorDamage : 1f) * (SwordIndex == 1 && HitTagOf( kind ) != "skeleton_bash" ? SecondSwordDamageScale : 1f);

	/// <summary>
	/// What this strike does, rolled once as the blade goes live (HitFeel.Vary, +-15 %) and shared by everyone it
	/// catches. A two-strike clip rolls again for its second window.
	/// </summary>
	private void RollSwingDamage() => _swingDamage = VoidMage.Combat.HitFeel.Vary( _spec.Damage * DamageScaleFor( _attack ) );

	private void LandHit()
	{
		var forward = Rotation.FromYaw( _faceYaw ).Forward;
		var cos = MathF.Cos( _spec.ArcDegrees.DegreeToRadian() );
		foreach ( var c in CombatTargets.HostileTo( Team ) )
		{
			if ( !c.IsValid() || !c.CanBeTargeted || c.GameObject == GameObject || _hitPlayers.Contains( c.GameObject.Id ) ) continue;
			var to = c.WorldPosition - WorldPosition;
			if ( MathF.Abs( to.z ) > HitHeightTolerance ) continue;      // no hits from the air or a level below
			to = to.WithZ( 0f );
			var d = to.Length;
			if ( d > _spec.HitRange ) continue;
			if ( d > 1f && Vector3.Dot( to / d, forward ) < cos ) continue;
			var push = d > 1f ? to / d : forward;
			_hitPlayers.Add( c.GameObject.Id );   // once per swing per body; every body in reach is hit (co-op)
			var kind = HitTagOf( _attack );
			var damage = _swingDamage;
			if ( c is PlayerHealth p ) p.TakeHit( damage, push * _spec.Push, WorldPosition + Vector3.Up * 50f, kind, GameObject );
			else if ( c is SkeletonNpc n ) n.ApplyHitHost( new DamageRouter.Hit { Damage = damage, Position = n.WorldPosition + Vector3.Up * 45f, Push = push, Attacker = GameObject, Kind = kind } );
			ShowHitLanded();
			if ( DebugLog ) Log.Info( $"[Skeleton] {_spec.Name} landed at {d:0} u" );
		}
	}

	private void TickHurt()
	{
		if ( Time.Now >= _busyUntil )
		{
			if ( HurtKind < HurtKindGuardHit )
			{
				// a real stagger: it answers, the brain's cooldown notwithstanding (a spammed player still gets swung at)
				_nextAttackAt = MathF.Min( _nextAttackAt, Time.Now + Roll( RetaliateMin, RetaliateMax ) );
				if ( _rng.NextDouble() < _brain.GuardAfterHurtChance )
					RaiseGuard( Roll( _brain.GuardHoldMin, _brain.GuardHoldMax ) );
			}
			// a hit is a reason to fight back only on this floor or with the attacker in sight (a balcony, a
			// stairwell); splash or a hazard through the slab leaves it idle rather than hunting the ceiling
			Go( HuntsAfterHit() ? SkeletonState.Engage : SkeletonState.Idle );
		}
	}

	// ------------------------------------------------------------------ damage in

	public void ReceiveHit( in DamageRouter.Hit hit )
	{
		if ( Networking.IsHost ) { ApplyHitHost( hit ); return; }
		var s = hit.Spell;
		RequestHit( hit.Damage, hit.Position, hit.Push, hit.Kind ?? "", hit.Attacker, s.SpellId ?? "", s.Mods, s.Mods2, s.Depth, s.CastId, (int)s.Flags );
	}

	/// <summary>The hit unpacked into plain values for the wire; the attacker rides as a GameObject like PlayerHealth.TakeHit's.</summary>
	[Rpc.Host]
	private void RequestHit( float damage, Vector3 position, Vector3 push, string kind, GameObject attacker, string spellId, long mods, long mods2, int depth, int castId, int flags )
	{
		ApplyHitHost( new DamageRouter.Hit
		{
			Damage = damage,
			Position = position,
			Push = push,
			Attacker = attacker,
			Kind = kind,
			Spell = new SpellHitInfo { SpellId = spellId, Mods = mods, Mods2 = mods2, Depth = depth, CastId = castId, Flags = (SpellHitFlags)flags },
		} );
	}

	/// <summary>The floating number on every peer's HUD. Unreliable on purpose: a dropped number is invisible, a late one pops up after the fight moved on.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void ShowDamage( int damage, Vector3 position, bool crit, bool magic )
	{
		UI.DamageNumbers.Push( damage, position, crit, magic );
	}

	private float _tickShown;
	private float _tickShowAt = float.NegativeInfinity;

	/// <summary>
	/// Host only: the one place damage lands. Called straight by the host's own systems (area damage, hazard and
	/// burn ticks, an ally's blade) and by <see cref="RequestHit"/> for a client's attacks.
	/// </summary>
	public void ApplyHitHost( in DamageRouter.Hit hit )
	{
		if ( State == SkeletonState.Dead || _brain is null ) return;
		var kind = hit.Kind ?? "";
		var push = hit.Push;
		var position = hit.Position;
		var attackerPos = hit.Attacker.IsValid() ? hit.Attacker.WorldPosition : position;
		var tick = hit.Spell.Has( SpellHitFlags.Tick );
		var forward = Rotation.FromYaw( _faceYaw ).Forward;
		// "from the front" is where the attacker STANDS, not which way the blade shoved (a lateral slash shoves sideways)
		var toAttacker = (attackerPos - WorldPosition).WithZ( 0f );
		if ( toAttacker.LengthSquared < 1f ) toAttacker = -push.WithZ( 0f );
		var fromFront = Vector3.Dot( toAttacker.Normal, forward ) > 0.5f;   // +-60 deg

		// The raised shield takes it: no damage. A light sword hit costs the guard 1 of its GuardPoints, a heavy or a
		// fireball 2; while points remain the shield holds (guard_light_hit, a beat, still guarding), the hit that
		// spends the last point knocks it aside and the skeleton is open for GuardBreakSeconds.
		// a shield stops swords and spells, not a floor spike or a swinging blade from above, nor a burn or a hazard tick
		var staggered = Time.Now < _staggerUntil;
		var broke = false;
		var shieldTakes = Guard && fromFront && !tick && State != SkeletonState.Attack && Time.Now > _guardRaisedAt + GuardRaiseGrace && !HitFeel.IsTrap( kind );
		if ( shieldTakes && ShieldReactChance > 0f && kind != "sword_light" )
		{
			// the counter-read (heden's boss): a heavy on the raised shield BREAKS it and lands harder; he is open for
			// GuardBreakSeconds (guard_heavy_hit) and his posture takes a lump. The damage code below runs on.
			broke = true;
			Guard = false;
			HurtKind = HurtKindBlocked;
			HurtCount++;
			_busyUntil = Time.Now + GuardBreakSeconds;
			_rootMotionTotal = 0f;
			Shove( push, 0.5f );
			Stop();
			Go( SkeletonState.Hurt );
			_nextDecision = _busyUntil;
			if ( DebugLog ) Log.Info( $"[Skeleton] {kind} broke the raised shield" );
		}
		else if ( shieldTakes )
		{
			var cost = kind == "sword_light" ? 1 : 2;
			_guardLeft -= cost;
			var held = _guardLeft > 0;
			HurtKind = held ? HurtKindGuardHit : HurtKindBlocked;
			HurtCount++;
			_busyUntil = Time.Now + (held ? GuardHitSeconds : GuardBreakSeconds);
			if ( held ) _guardUntil = MathF.Max( _guardUntil, _busyUntil + 0.3f );   // the guard outlives its own reaction
			else Guard = false;
			_rootMotionTotal = 0f;
			Shove( push, 0.5f );
			Stop();
			Go( SkeletonState.Hurt );
			_nextDecision = _busyUntil;
			if ( DebugLog ) Log.Info( $"[Skeleton] blocked {kind}: {(held ? $"guard holds ({_guardLeft} left)" : "guard broken")}" );
			return;
		}

		var trap = HitFeel.IsTrap( kind );
		var (crit, critMultiplier) = !trap && !tick ? DamageRouter.RollCrit( hit ) : (false, 1f);
		var applied = (trap ? HitFeel.AdjustTrap( hit.Damage ) : HitFeel.Adjust( hit.Damage )) * critMultiplier
			* (broke ? ShieldBreakDamageFactor : 1f) * (staggered ? StaggerDamageMultiplier : 1f);
		var dealt = MathF.Min( applied, MathF.Max( 0f, Health ) );   // what actually came off the bar: no leech on overkill
		Health -= applied;
		VoidMage.Spells.SpellHostTuning.OnDamageDealt( this, hit, dealt );
		// the number every peer sees is what was subtracted: blocked hits never get here, armored ones do. Ticks (a
		// burn, a hazard) are summed and shown twice a second so a zone never sprays a number per tick. A ray's own
		// strike (Pulse) is the exception: the Void Lance shows every one of its ticks.
		if ( tick && !hit.Spell.Has( SpellHitFlags.Pulse ) )
		{
			_tickShown += applied;
			if ( Time.Now >= _tickShowAt || Health <= 0f )
			{
				ShowDamage( (int)MathF.Round( _tickShown ), WorldPosition + Vector3.Up * 62f, false, DamageRouter.IsMagic( kind ) );
				_tickShown = 0f;
				_tickShowAt = Time.Now + 0.5f;
			}
		}
		else ShowDamage( (int)MathF.Round( applied ), position + Vector3.Up * 12f, crit, DamageRouter.IsMagic( kind ) );
		if ( DebugLog && crit ) Log.Info( $"[Skeleton] crit {kind}: {applied:0} damage" );
		if ( Health <= 0f ) { Die( push, position, KillContext.From( hit ) ); return; }
		VoidMage.Spells.SpellHostTuning.ApplyStatus( this, hit );
		// a tick does damage and nothing else: no flinch, no stagger, no poise bookkeeping. A TRAP is the same to an
		// NPC (user, 2026-09-18): spikes, blades, flame and the rest take health off and never move or interrupt
		// anybody, so a skeleton walks through a saw mid-swing. The player still gets the full reaction (PlayerHealth),
		// and a trap that KILLS one still throws the ragdoll, which is the corpse's business, not the body's.
		if ( tick || trap ) return;

		// posture (the knight): every hit builds it, a broken shield adds a lump; full = the long stagger
		if ( PostureMax > 0f && !staggered )
		{
			_posture += applied * PosturePerDamage + (broke ? ShieldBreakPosture : 0f);
			_postureHitAt = Time.Now;
			if ( _posture >= PostureMax ) { EnterStagger(); return; }
		}
		if ( broke ) return;                            // already in the break reaction
		if ( staggered ) { FlinchCount++; return; }     // a stagger is never interrupted: hits just land harder
		// the shield read: the second frontal light in a short window snaps the shield up, when he is free to
		if ( ShieldReactChance > 0f && !Guard && kind == "sword_light" && fromFront ) ConsiderShield();

		// Hyper armor (the charge past its telegraph, the shield bash throughout) and poise (a stun-lock chain):
		// damage, a flash, no stagger. Poise, once earned, lasts PoiseArmorSeconds and the skeleton swings back at
		// once, so a player who only spams still eats an attack.
		var t = AttackT;
		var armored = State == SkeletonState.Attack && _spec.ArmorFrom >= 0f && t >= _spec.ArmorFrom && t < _spec.HitTime;
		if ( Time.Now > _lastHurtAt + PoiseWindow ) _hurtChain = 0;
		var poise = Time.Now < _poiseUntil;
		if ( !poise && _hurtChain >= PoiseHits )
		{
			poise = true;
			_hurtChain = 0;
			_poiseUntil = Time.Now + PoiseArmorSeconds;
			// not this very tick: every peer's body is in the hit's freeze frame and would miss the attack pulse
			_nextAttackAt = Time.Now + 0.15f;
			if ( State == SkeletonState.Hurt )
			{
				// cut the stagger short and go: the next decision tick attacks if the player is in range
				_busyUntil = Time.Now;
				Go( HuntsAfterHit() ? SkeletonState.Engage : SkeletonState.Idle );
				_nextDecision = Time.Now;
			}
			if ( DebugLog ) Log.Info( $"[Skeleton] poise: shrugging off hits for {PoiseArmorSeconds:0.0} s" );
		}
		if ( armored || poise )
		{
			FlinchCount++;
			if ( DebugLog ) Log.Info( $"[Skeleton] {kind} absorbed ({(armored ? "armor" : "poise")}), health {Health:0}/{MaxHealth:0}" );
			return;
		}

		// no hit clip on this body: the damage still lands and it still gets shoved, but there is no state to
		// go to. Freezing it in Hurt with nothing playing would read as the monster ignoring the blow entirely.
		if ( !CanStagger )
		{
			FlinchCount++;
			Shove( push, FlinchShoveScale );
			if ( DebugLog ) Log.Info( $"[Skeleton] {kind} flinch (no hit clip), health {Health:0}/{MaxHealth:0}" );
			return;
		}

		HurtKind = HurtKindFor( push );
		HurtCount++;
		_hurtChain++;
		_lastHurtAt = Time.Now;
		Guard = false;
		_busyUntil = Time.Now + HurtSeconds;
		_rootMotionTotal = 0f;
		Shove( push, 1f );
		Stop();
		Go( SkeletonState.Hurt );
		if ( _target.IsValid() ) _nextDecision = _busyUntil;
		if ( DebugLog ) Log.Info( $"[Skeleton] hurt {kind} -> kind {HurtKind}, health {Health:0}/{MaxHealth:0}" );
	}

	/// <summary>0 f (shoved back), 1 b (shoved forward), 2 l (shoved to its right), 3 r (shoved to its left): the clip whose travel matches the shove.</summary>
	private int HurtKindFor( Vector3 push )
	{
		var rot = Rotation.FromYaw( _faceYaw );
		var p = push.WithZ( 0f );
		if ( p.IsNearlyZero() ) return 0;
		p = p.Normal;
		var best = 0; var bestDot = float.MinValue;
		Vector3[] travel = { -rot.Forward, rot.Forward, -rot.Left, rot.Left };
		for ( var k = 0; k < travel.Length; k++ )
		{
			var dot = Vector3.Dot( p, travel[k] );
			if ( dot > bestDot ) { bestDot = dot; best = k; }
		}
		return best;
	}

	/// <summary>The body ragdolls on every peer (SkeletonBody -> SkeletonCorpse), launched along the killing blow.</summary>
	private void Die( Vector3 push, Vector3 position, in KillContext ctx )
	{
		Health = 0f;
		Guard = false;
		// the blade's own travel (SwordSounds' measured crescent) with heden's lift so bodies arc; set BEFORE Dead
		var dir = push.IsNearZeroLength ? -Rotation.FromYaw( _faceYaw ).Forward : push.Normal;
		DeathPush = (dir + Vector3.Up * 0.35f).Normal;
		DeathHitPosition = position.IsNearZeroLength ? WorldPosition + Vector3.Up * 50f : position;
		_diedAt = Time.Now;
		_rootMotionTotal = 0f;
		Stop();
		Go( SkeletonState.Dead );
		GameObject.Tags.Remove( "enemy" );
		GameObject.Tags.Remove( "ally" );
		GameObject.Tags.Remove( "npc" );
		_status?.Clear();
		SlowAmount = 0f; BurnKind = 0;
		if ( _collider.IsValid() ) _collider.Enabled = false;
		if ( _agent.IsValid() ) _agent.Enabled = false;
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} died, push {DeathPush:0.00}" );
		// the run's currency: void shards fall where a hostile dies (host; the body self-destructs after the linger,
		// so the drop has to happen here, not later). A turned ally or a frog is worth nothing.
		if ( Networking.IsHost && Team == Team.Hostile )
		{
			// (a body from the respawn trickle is worth nothing: shards survive a death now, so a floor lingered on must not be a farm)
			var shards = Respawned ? 0 : VoidMage.Boons.BoonHost.ShardsFor( VoidMage.Run.ShardPickup.CountFor( KindName ), ctx.Attacker );
			VoidMage.Run.ShardPickup.Drop( Scene, WorldPosition + Vector3.Up * 40f, shards, GameObject.Parent );
			VoidMage.Boons.BoonHost.OnKill( ctx );   // health and mana per kill
			VoidMage.Run.ScrollDrops.OnKill( this, push );   // the floor's scrolls fall at set kill counts, whoever the kill is
			// the weapon it was carrying is the killer's to wear once TEN of its carriers are dead (user, 2026-09-21; it
			// was the first kill): a cosmetic unlock, kept for good, nothing to pick up. Only what it really held: kind + SwordIndex name the body group it wore.
			if ( VoidMage.Inventory.WeaponSkins.CarriedWeapon( KindName, SwordIndex ) is { } carried )
				VoidMage.Inventory.PlayerInventory.UnlockSkinFor( ctx.Attacker, carried.Id );
		}
		// stage 7 of the cast pipeline: The Turncoat, The Hollow's refund (everything it needs is read now)
		if ( ctx.IsSpell ) VoidMage.Spells.SpellDeathStage.OnKill( this, ctx );
	}

	// ------------------------------------------------------------------ movement helpers

	private void Go( SkeletonState state )
	{
		if ( state == State ) return;
		State = state;
		_holdingGround = false;
		_noticeUntil = 0f;   // a hit (Hurt) or anything else that moves it on ends the reaction window
		if ( state is SkeletonState.Engage or SkeletonState.Idle ) _nextDecision = Time.Now + DecisionInterval * 0.5f;
	}

	/// <summary>A point `standoff` short of `target` along the line from here.</summary>
	private Vector3 Toward( Vector3 target, float standoff )
	{
		var to = (target - WorldPosition).WithZ( 0f );
		var d = to.Length;
		if ( d <= standoff + 1f ) return WorldPosition;
		return target - to / d * standoff;
	}

	/// <summary>A destination far enough ahead that the crowd agent runs to it instead of arriving at it.</summary>
	private const float MinLead = 60f;

	// A DESTINATION MUST NEVER BE DERIVED FROM THIS SKELETON'S OWN POSITION. `SlotPoint` used to offset the
	// standoff point by a fixed angle around the ring from wherever the skeleton was standing, to fan a group into
	// an arc. Because the offset was measured from its own bearing, the destination slid another 45 degrees round
	// the moment it moved: there was no point it could ever arrive at, so it curved harder and harder the closer it
	// got (7 degrees off the straight line at 400 units, 13 at 200, 32 at 100, 60 at 60) and orbited instead of
	// closing. The same self-reference kept the destination moving under MoveTo's 24-unit throttle, so the agent's
	// path was rebuilt every 0.15 s the whole way in. That was the jittery run and the struggling walk that started
	// "a couple of hundred units out". Everything aims at `Toward` now: a point on the straight line to the target,
	// which only moves when the TARGET does. Spreading a pack out is the crowd's `Separation` job, and the prefab
	// already sets it.

	/// <summary>
	/// A crowd agent brakes as it nears its destination, so a destination a few units ahead is one it crawls to.
	/// While there is real distance to cover, aim at least MinLead ahead so the body keeps its speed and the state
	/// machine decides when to stand.
	/// </summary>
	private Vector3 Lead( Vector3 destination )
	{
		var to = (destination - WorldPosition).WithZ( 0f );
		var d = to.Length;
		if ( d >= MinLead || d < 1f ) return destination;
		return WorldPosition + to / d * MinLead;
	}

	private void MoveTo( Vector3 target, float speed )
	{
		if ( State == SkeletonState.Dead ) return;
		_wantSpeedRaw = speed;
		if ( State is SkeletonState.Approach or SkeletonState.Engage ) speed *= CombatRate;   // (PaceScale is in the prefab's speeds already)
		if ( _status is not null ) speed *= _status.SpeedScale;   // an ice shard's slow: every gait, never the clips' timing
		_wantMove = true; _wantTarget = target; _wantSpeed = speed;
		if ( !_agent.IsValid() || !_agent.Enabled ) return;
		if ( !_agent.MaxSpeed.AlmostEqual( speed ) ) { _agent.MaxSpeed = speed; _agent.Acceleration = speed * 4f; }
		// Re-path only when the destination has really moved. Every MoveTo throws the crowd agent's current path
		// away, so the old 0.15 s ceiling meant a skeleton chasing a standing player rebuilt its path nearly seven
		// times a second and never held a clean line; the ceiling is only here as a safety net now.
		if ( _agent.TargetPosition.HasValue && target.Distance( _lastMoveTo ) < 24f && Time.Now < _lastMoveAt + 1f ) return;
		_lastMoveTo = target;
		_lastMoveAt = Time.Now;
		_agent.MoveTo( target );
	}

	/// <summary>
	/// Stand still. Idempotent on purpose: `_agent.Stop()` throws the crowd agent's path away, so calling it every
	/// tick while already standing was enough to stop the body ever moving smoothly again.
	/// </summary>
	private void Stop()
	{
		if ( !_wantMove ) return;
		_wantMove = false;
		_lastMoveAt = -1f;   // the next MoveTo issues a fresh path rather than reusing the throttle window
		if ( _agent.IsValid() && _agent.Enabled && _agent.TargetPosition.HasValue ) _agent.Stop();
	}

	private float Distance( ICombatant c ) => c.WorldPosition.WithZ( 0f ).Distance( WorldPosition.WithZ( 0f ) );
	private float Distance( Vector3 p ) => p.WithZ( 0f ).Distance( WorldPosition.WithZ( 0f ) );

	/// <summary>
	/// Host: a raised minion's time is up. At two stacks of The Turncoat it explodes first (hostiles around it take the
	/// blast; the blast procs nothing), then it crumbles: a zero push, so every peer's ragdoll drops where it stands.
	/// </summary>
	public void Expire( bool allowBlast = true )
	{
		if ( IsProxy || State == SkeletonState.Dead ) return;
		var blast = allowBlast && RaisedStacks >= 2 ? VoidMage.Spells.SpellHostTuning.TurncoatBlastRadius( RaisedStacks ) : 0f;
		if ( blast > 0f )
		{
			DeathKind = 2;
			var info = new SpellHitInfo { SpellId = "turncoat_blast", Mods = 0, Depth = VoidMage.Spells.SpellHostTuning.MaxProcDepth + 1, CastId = 0, Flags = SpellHitFlags.FromMinion | SpellHitFlags.Area };
			var template = new DamageRouter.Hit { Damage = VoidMage.Spells.SpellHostTuning.TurncoatBlastDamage( RaisedStacks ), Position = WorldPosition, Push = Vector3.Zero, Attacker = Summoner.IsValid() ? Summoner : GameObject, Kind = "spell_turncoat_blast", Spell = info };
			VoidMage.Spells.SpellArea.Damage( WorldPosition + Vector3.Up * 40f, blast, template, Team.Hostile, GameObject, falloff: true, needLos: true );
			ShowBlast( WorldPosition + Vector3.Up * 40f, blast );
		}
		else DeathKind = 1;
		Die( Vector3.Zero, WorldPosition + Vector3.Up * 40f, KillContext.None );
	}

	[Rpc.Broadcast]
	private void ShowBlast( Vector3 at, float radius )
	{
		var spell = VoidMage.Spells.SpellDatabase.Get( VoidMage.Spells.SpellIds.VoidOrb );
		VoidMage.Spells.SpellVfx.Burst( Scene, at, Vector3.Up, spell, radius / 150f, VoidMage.Spells.ImpactKind.World );
	}

	/// <summary>The target's feet within HitHeightTolerance of ours (the same band LandHit accepts): a player on the floor above or below is chased, never swung at.</summary>
	private bool OnMyLevel( ICombatant c ) => MathF.Abs( c.WorldPosition.z - WorldPosition.z ) <= HitHeightTolerance;

	/// <summary>Close enough in HEIGHT to be worth hunting (see AggroHeightBand): `slack` widens the band.</summary>
	private bool WithinHeightBand( ICombatant c, float slack ) => MathF.Abs( c.WorldPosition.z - WorldPosition.z ) <= AggroHeightBand * slack;

	/// <summary>
	/// Whether a chase goes on. On my floor planar rules apply: walls and corners never break a chase. Above or
	/// below the band only sight does: up an open stairwell it follows for LoseSightSeconds after each glimpse,
	/// through a slab it never saw you, so it stops instead of shadowing you from underneath.
	/// </summary>
	private bool StillHunting( ICombatant c ) => WithinHeightBand( c, 1f ) || Time.Now - _lastSeenAt <= LoseSightSeconds;

	/// <summary>The chase's sight memory: one ray per 0.2 s, and only while the target is off this floor (on it, sight is moot).</summary>
	private void TrackSight()
	{
		if ( Time.Now < _nextSeeCheck ) return;
		_nextSeeCheck = Time.Now + 0.2f;
		if ( WithinHeightBand( _target, 1f ) ) return;
		if ( CanSee( _target ) ) _lastSeenAt = Time.Now;
	}

	/// <summary>After a hit: fight back if the target is on this floor, or in sight right now (a balcony, a stairwell).</summary>
	private bool HuntsAfterHit()
	{
		if ( !_target.IsValid() ) return false;
		if ( WithinHeightBand( _target, 1f ) ) return true;
		if ( !CanSee( _target ) ) return false;
		_lastSeenAt = Time.Now;
		return true;
	}

	/// <summary>Back to Idle with the reason on the log (`skeleton_debug 1`); the target is re-picked there.</summary>
	private void LoseTarget( string why )
	{
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} lost you: {why}" );
		Go( SkeletonState.Idle );
	}

	/// <summary>Target's feet above (+) or below (-) mine, for `skeleton_speeds`.</summary>
	public float HeightGapToTarget => _target.IsValid() ? _target.WorldPosition.z - WorldPosition.z : 0f;
	/// <summary>Seconds since the target was last in sight while off this floor (large = never), for `skeleton_speeds`.</summary>
	public float LastSeenAgo => Time.Now - _lastSeenAt;

	private void FaceTarget()
	{
		if ( !_target.IsValid() ) return;
		var to = (_target.WorldPosition - WorldPosition).WithZ( 0f );
		if ( to.LengthSquared < 1f ) return;
		_faceYaw = to.Normal.EulerAngles.yaw;
	}

	private void FaceVelocity()
	{
		var v = _hostVelocity;
		if ( v.LengthSquared < 25f && _agent.IsValid() && _agent.Enabled ) v = _agent.Velocity.WithZ( 0f );
		if ( v.LengthSquared < 25f ) return;
		_faceYaw = v.Normal.EulerAngles.yaw;
	}

	/// <summary>
	/// Face where the body is really going while it is really going somewhere, and square up on the target once it
	/// slows. The run clip only exists straight ahead, so a body turned to face the player while its path curves
	/// plays a strafe blend at full speed and the feet slide.
	///
	/// It reads the AGENT's velocity, not `_hostVelocity`. `_hostVelocity` is a raw position delta over the frame
	/// time, and the agent only moves the transform on the crowd's tick, so on a frame that carries no tick it
	/// measures zero -- which flipped the facing between the travel direction and the target every other frame.
	/// The agent's own velocity is the smoothed simulation value and is valid on every frame.
	/// </summary>
	private void FaceTravel()
	{
		var v = _agent.IsValid() && _agent.Enabled ? _agent.Velocity.WithZ( 0f ) : _hostVelocity;
		if ( v.LengthSquared > 400f ) _faceYaw = v.Normal.EulerAngles.yaw;   // > 20 u/s
		else FaceTarget();
	}

	/// <summary>
	/// Ease the body around to <see cref="_faceYaw"/>. The result is written to the MODEL's object, never to this
	/// one: see OnAwake -- a write to the object the NavMeshAgent lives on clears the agent's path, and this runs
	/// every rendered frame. `_bodyYaw` is tracked here rather than read back off the transform so nothing ever
	/// has to touch the root to know which way the skeleton is facing.
	/// </summary>
	private void TurnToward( float dt )
	{
		if ( State == SkeletonState.Dead ) return;
		var rate = State == SkeletonState.Attack || State == SkeletonState.Engage ? TurnRateEngaged : TurnRateMoving;
		if ( Noticing ) rate = MathF.Max( 30f, NoticeTurnRate );   // the slow look round of a body that just spotted you
		if ( Guard ) rate = MathF.Min( rate, TurnRateGuarding );
		var delta = ShortestDegrees( _bodyYaw, _faceYaw );
		var step = MathF.Min( MathF.Abs( delta ), rate * dt ) * MathF.Sign( delta );
		if ( step == 0f ) return;
		_bodyYaw += step;
		BodyYaw = _bodyYaw;   // SkeletonBody turns the model, on every peer
	}

	/// <summary>Which way the body is actually pointing, for anything that needs its facing (valid on every peer).</summary>
	public Rotation BodyRotation => Rotation.FromYaw( BodyYaw );

	/// <summary>Positive when `to` sits counter-clockwise of `from` (spelled out so the sign is never in doubt).</summary>
	private static float ShortestDegrees( float from, float to )
	{
		var delta = (to - from) % 360f;
		if ( delta > 180f ) delta -= 360f;
		else if ( delta < -180f ) delta += 360f;
		return delta;
	}

	// ------------------------------------------------------------------ statuses (host)

	/// <summary>Host, per tick (SpellHostSystem): burns tick, and the synced tint state follows the status bag.</summary>
	public void TickStatus( float now )
	{
		if ( _status is null ) return;
		_status.Tick( this, now );
		var slow = _status.SlowFraction;
		if ( MathF.Abs( slow - SlowAmount ) > 0.01f ) SlowAmount = slow;
		var burn = _status.BurnKind;
		if ( burn != BurnKind ) BurnKind = burn;
		// A slow that really changed re-plans the footwork at the new speed. It has to be a REAL change: a hazard
		// re-applies its slow every frame, so comparing exactly meant a skeleton standing in an ice wake forced
		// `_lastMoveAt = -1` and a fresh MoveTo every frame -- and every MoveTo throws the agent's path away.
		// `_lastSlowApplied` is updated whether or not it is moving, so a slow that expires while it stands still
		// cannot leave a stale value behind to fire one spurious re-path on the next step off.
		var slowChanged = MathF.Abs( slow - _lastSlowApplied ) > 0.01f;
		if ( slowChanged ) _lastSlowApplied = slow;
		if ( slowChanged && _wantMove ) { _lastMoveAt = -1f; MoveTo( _wantTarget, _wantSpeedRaw ); }
	}

	private float _lastSlowApplied;
	private float _wantSpeedRaw;
	/// <summary>Engage footwork: standing at the preferred distance, and not setting off again until the gap really opens.</summary>
	private bool _holdingGround;
	/// <summary>The body's current facing, owned here and written to the model's object (see TurnToward).</summary>
	private float _bodyYaw;


	private Vector3 _shoveLeft;
	private float _shoveUntil = -1f;

	/// <summary>The impact lurch: `ImpactShove` units along the blade, spent over ImpactShoveSeconds by OnUpdate.</summary>
	private void Shove( Vector3 push, float scale )
	{
		var flat = push.WithZ( 0f );
		if ( flat.IsNearZeroLength || ImpactShove <= 0f ) return;
		_shoveLeft = flat.Normal * ImpactShove * scale;
		_shoveUntil = Time.Now + MathF.Max( 0.01f, ImpactShoveSeconds );
	}

	/// <summary>Host: The Ram threw this body. The same lurch a blade gives, only metres of it (the wall is the caller's business).</summary>
	public void RamShove( Vector3 direction, float distance, float seconds )
	{
		var flat = direction.WithZ( 0f );
		if ( flat.IsNearZeroLength || distance <= 0f || State == SkeletonState.Dead ) return;
		_shoveLeft = flat.Normal * distance;
		_shoveUntil = Time.Now + MathF.Max( 0.05f, seconds );
	}

	private void TickShove( float dt )
	{
		if ( _shoveLeft.IsNearZeroLength || State == SkeletonState.Dead ) { _shoveLeft = Vector3.Zero; return; }
		var remaining = MathF.Max( _shoveUntil - Time.Now, 0f );
		var frac = remaining <= 0f ? 1f : MathF.Min( 1f, dt / (remaining + dt) );
		var step = _shoveLeft * frac;
		_shoveLeft -= step;
		if ( remaining <= 0f ) _shoveLeft = Vector3.Zero;
		MoveBodyBy( step );
	}

	/// <summary>
	/// A code-driven lunge (AttackSpec.LungeSpeed): the body flies at the target and is re-aimed every frame so it
	/// ARRIVES ON THE HIT FRAME, LungeLandAt of the reach away -- the speed is whatever the gap left and the time left
	/// ask for, so a leap from close in floats instead of ramming the player and waiting, and one from the edge of its
	/// range (or at a player backing off) speeds up instead of landing short. LungeSpeed is the top speed, with
	/// LungeCatchUp of headroom; running flat out away from it still dodges. The clips play in place; the travel is all here.
	/// </summary>
	private void TickLunge( float dt )
	{
		var t = AttackT;
		// keeps closing through the hit window too: a leap the player outran for a moment still lands
		if ( t >= _spec.HitTime + HitWindowSeconds || !_target.IsValid() ) return;
		var to = (_target.WorldPosition - WorldPosition).WithZ( 0f );
		var d = to.Length;
		var gap = d - _spec.HitRange * LungeLandAt;
		if ( gap <= 1f ) return;
		var top = _spec.LungeSpeed * LungeCatchUp * Pace;
		var left = (_spec.HitTime - t) / Pace;          // real seconds until the blade goes live
		var speed = left > dt ? MathF.Min( gap / left, top ) : top;
		MoveBodyBy( to / d * MathF.Min( speed * dt, gap ) );
	}

	/// <summary>The clip's authored travel this frame, applied through the agent and kept on the navmesh (a lunge stops at a wall).</summary>
	private void ApplyRootMotionDelta()
	{
		if ( !_renderer.IsValid() ) return;
		var local = _renderer.RootMotion.Position.WithZ( 0f );
		if ( local.LengthSquared < 1e-6f ) return;
		var delta = BodyRotation * local;   // the model's facing: the root's rotation is no longer written
		if ( State == SkeletonState.Attack && RootMotionStandoff > 0f && _target.IsValid() )
		{
			// motion warping, the cheap way: the travel toward the target stops RootMotionStandoff short of it
			var to = (_target.WorldPosition - WorldPosition).WithZ( 0f );
			var d = to.Length;
			if ( d > 1f )
			{
				var dir = to / d;
				var along = Vector3.Dot( delta, dir );
				var room = MathF.Max( 0f, d - RootMotionStandoff );
				if ( along > room ) delta -= dir * (along - room);
			}
		}
		MoveBodyBy( delta );
	}

	/// <summary>Move the body by a world delta, projected onto the navmesh (a wall stops it), through the agent.</summary>
	private void MoveBodyBy( Vector3 delta )
	{
		var next = WorldPosition + delta;
		var navReady = !_dungeon.IsValid() || _dungeon.NavMeshReady || VoidMage.Boss.HellArena.NavReady;
		if ( navReady )
		{
			var onMesh = Scene.NavMesh.GetClosestPoint( next, 24f );
			if ( !onMesh.HasValue ) { _moveRejected++; return; }
			next = onMesh.Value;
		}
		else
		{
			var from = WorldPosition + Vector3.Up * 24f;
			var tr = Scene.Trace.Ray( from, from + delta ).Radius( 12f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run();
			next = WorldPosition + (tr.Hit ? tr.EndPosition - from : delta);
		}
		_rootMotionTotal += (next - WorldPosition).WithZ( 0f ).Length;
		// BOTH the agent and the object, every time. Decompiled NavMeshAgent (2026-09-19): SetAgentPosition only moves
		// the simulated agent; the component's own OnUpdate then LERPS the GameObject toward it at dt*20 (about a
		// third of the gap per frame), and a runtime transform write does not touch the agent at all. Setting the
		// agent alone therefore re-based every next step on a WorldPosition that had not arrived yet, dragging the
		// agent back each frame: a frog's 65 u hop and a skeleton's 28 u lunge both shrank to a fraction and landed
		// late. That was the whole "animates but does not move" / "the lunge arrives after the hit frame" mystery.
		if ( _agent.IsValid() && _agent.Enabled ) _agent.SetAgentPosition( next );
		WorldPosition = next;
	}

	/// <summary>Inside this the engage loop trusts planar distance with no line check: a wall plus two bodies' clearance is always wider.</summary>
	private const float WalkLineTrustRange = 45f;
	/// <summary>Past this nobody asks: the approach paths to the player's own position, which needs no line.</summary>
	private const float WalkLineCheckRange = 400f;
	/// <summary>How close a body standing in a doorway presses before it may hold its ground, and how deep the throat is either side of the wall plane.</summary>
	private const float DoorwayPressRange = 48f;
	private const float DoorwayThroat = 40f;
	private float _nextWalkLineCheck;
	private bool _walkLineClear = true;

	/// <summary>
	/// Could it walk straight at the target? One thin sphere sweep per 0.2 s at chest height (over the tables, under
	/// the lintels) against the world: walls, closed door leaves, crates. NOT sight -- the barred doors and the gaol's
	/// bar walls are see-through and still in the way. A hit in the last stretch is the target's own surroundings
	/// (a player with their back to a wall), not something between us.
	/// </summary>
	private bool WalkLineClear( float distance )
	{
		if ( !_target.IsValid() ) return true;
		if ( distance <= WalkLineTrustRange ) return true;
		if ( distance > WalkLineCheckRange ) return false;
		if ( Time.Now < _nextWalkLineCheck ) return _walkLineClear;
		_nextWalkLineCheck = Time.Now + 0.2f;
		var from = WorldPosition + Vector3.Up * 50f;
		var to = _target.WorldPosition + Vector3.Up * 50f;
		var tr = Scene.Trace.Ray( from, to ).Radius( 8f ).WithoutTags( LosIgnore ).IgnoreGameObjectHierarchy( GameObject ).Run();
		_walkLineClear = !tr.Hit || tr.Distance >= from.Distance( to ) - 24f;
		return _walkLineClear;
	}

	private bool CanSee( ICombatant c )
	{
		var eye = WorldPosition + Vector3.Up * EyeHeight;
		var to = c.WorldPosition + Vector3.Up * c.BodyHeight * 0.7f;
		var tr = Scene.Trace.Ray( eye, to ).WithoutTags( LosIgnore ).Run();
		return !tr.Hit;
	}

	/// <summary>One of the two sword swings — or always the first, when the second is the same animation.</summary>
	private Kind Swing()
	{
		var same = SwordB is null || SwordA is null || string.Equals( SwordA.Name, SwordB.Name, StringComparison.OrdinalIgnoreCase );
		return same || _rng.Next( 2 ) == 0 ? Kind.SwordA : Kind.SwordB;
	}

	// ------------------------------------------------------------------ the attack table (the knight)

	private bool UsesTable => Attacks is { Count: > 0 };
	private float[] _attackCooldownUntil = System.Array.Empty<float>();
	private readonly Queue<int> _recentAttacks = new();
	private int _lastAttackIndex = -1;
	private const int RecentAttackMemory = 3;

	private void EnsureTable()
	{
		if ( _attackCooldownUntil.Length != Attacks.Count ) _attackCooldownUntil = new float[Attacks.Count];
	}

	private int AttackIndex( string name )
	{
		for ( var i = 0; i < Attacks.Count; i++ )
			if ( string.Equals( Attacks[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;
		return -1;
	}

	/// <summary>Whether table entry `i` may go at this planar distance: its cooldown is up and its range window holds the player.</summary>
	private bool Eligible( int i, float distance )
	{
		var a = Attacks[i];
		if ( Time.Now < _attackCooldownUntil[i] ) return false;
		if ( distance < MathF.Max( a.MinRange, a.SelectMin ) ) return false;
		var max = a.SelectMax > 0f ? a.SelectMax : a.StartRange;
		return distance <= max;
	}

	/// <summary>
	/// heden's boss pick: one weighted roll over every eligible entry, the last pick at RepeatPenalty and the three
	/// before it at RecentPenalty so the whole kit gets used. Nothing eligible = close in on the nearest start range
	/// and roll again shortly; he never stands and waits (heden: a player parked past every range starved him).
	/// </summary>
	private bool DecideFromTable( float distance )
	{
		EnsureTable();
		var score = new float[Attacks.Count];
		var total = 0f;
		for ( var i = 0; i < Attacks.Count; i++ )
		{
			var a = Attacks[i];
			if ( a.Weight <= 0f || !Eligible( i, distance ) ) continue;
			score[i] = a.Weight * (i == _lastAttackIndex ? RepeatPenalty : _recentAttacks.Contains( i ) ? RecentPenalty : 1f);
			total += score[i];
		}
		if ( total <= 0f )
		{
			var nearest = float.MaxValue;
			for ( var i = 0; i < Attacks.Count; i++ )
				if ( Attacks[i].Weight > 0f && Time.Now >= _attackCooldownUntil[i] ) nearest = MathF.Min( nearest, Attacks[i].StartRange );
			if ( nearest < float.MaxValue && distance > nearest )
			{
				_nextAttackAt = Time.Now + 0.15f;
				_closeInRange = nearest * 0.8f;
				_closeInUntil = Time.Now + 0.6f;
			}
			return false;
		}
		var r = (float)_rng.NextDouble() * total;
		for ( var i = 0; i < Attacks.Count; i++ )
		{
			if ( score[i] <= 0f ) continue;
			r -= score[i];
			if ( r <= 0f ) { StartAttack( (Kind)(i + 1) ); return true; }
		}
		return false;
	}

	private void NoteTablePick( int i )
	{
		EnsureTable();
		if ( i < 0 || i >= Attacks.Count ) return;
		_attackCooldownUntil[i] = Time.Now + Attacks[i].Cooldown;
		_lastAttackIndex = i;
		_recentAttacks.Enqueue( i );
		while ( _recentAttacks.Count > RecentAttackMemory ) _recentAttacks.Dequeue();
	}

	/// <summary>At the end of an attack: flow into one of its ComboInto entries (the eligible ones) with ComboChance.</summary>
	private bool TryCombo()
	{
		if ( !UsesTable || _spec is null || string.IsNullOrWhiteSpace( _spec.ComboInto ) || !_target.IsValid() ) return false;
		if ( _rng.NextDouble() >= _spec.ComboChance ) return false;
		EnsureTable();
		var d = Distance( _target );
		var options = new List<int>();
		foreach ( var name in _spec.ComboInto.Split( ',' ) )
		{
			var i = AttackIndex( name.Trim() );
			if ( i >= 0 && i != (int)_attack - 1 && Eligible( i, d ) ) options.Add( i );
		}
		if ( options.Count == 0 ) return false;
		var from = _spec.Name;
		StartAttack( (Kind)(options[_rng.Next( options.Count )] + 1) );
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} combo {from} -> {_spec.Name}" );
		return true;
	}

	/// <summary>The attack that may open a fight from the approach (OpenerRange > 0): the table's first such entry, else the charge.</summary>
	private (Kind kind, AttackSpec spec) Opener()
	{
		if ( !UsesTable ) return (Kind.Charge, Charge);
		for ( var i = 0; i < Attacks.Count; i++ )
			if ( Attacks[i].OpenerRange > 0f ) return ((Kind)(i + 1), Attacks[i]);
		return (Kind.None, null);
	}

	/// <summary>The damage kind an attack deals ("skeleton_sword" / "skeleton_bash"): the table entry's own tag, else by the fixed kind.</summary>
	private string HitTagOf( Kind kind )
	{
		if ( UsesTable ) return string.IsNullOrEmpty( _spec?.HitTag ) ? "skeleton_sword" : _spec.HitTag;
		return kind == Kind.ShieldBash ? "skeleton_bash" : "skeleton_sword";
	}

	// ------------------------------------------------------------------ posture and the shield read (the knight)

	private float _posture;
	private float _postureHitAt = float.NegativeInfinity;
	private float _staggerUntil = float.NegativeInfinity;
	private int _frontLights;
	private float _lastFrontLightAt = float.NegativeInfinity;
	private float _shieldReactReadyAt = float.NegativeInfinity;

	/// <summary>Posture 0..1 for anyone drawing it.</summary>
	public float PostureFraction => PostureMax > 0f ? _posture / PostureMax : 0f;

	private void EnterStagger()
	{
		_posture = 0f;
		_staggerUntil = Time.Now + StaggerSeconds;
		HurtKind = HurtKindBlocked;   // guard_heavy_hit: the shield knocked aside, then dazed to the end
		HurtCount++;
		Guard = false;
		_busyUntil = _staggerUntil;
		_rootMotionTotal = 0f;
		Stop();
		Go( SkeletonState.Hurt );
		_nextDecision = _busyUntil;
		_nextAttackAt = _busyUntil + 0.1f;
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} posture broken: staggered {StaggerSeconds:0.0} s" );
	}

	private void ConsiderShield()
	{
		if ( Time.Now > _lastFrontLightAt + ShieldReactWindow ) _frontLights = 0;
		_frontLights++;
		_lastFrontLightAt = Time.Now;
		if ( _frontLights < ShieldReactLightsNeeded || Time.Now < _shieldReactReadyAt ) return;
		// only when interruptible: between attacks, or in an attack's recovery (a wind-up or a live blade plays out)
		var free = State is SkeletonState.Engage or SkeletonState.Approach
			|| (State == SkeletonState.Attack && _spec is not null && AttackT > _spec.HitTime + HitWindowSeconds);
		if ( !free ) return;
		_frontLights = 0;
		_shieldReactReadyAt = Time.Now + ShieldReactCooldown;
		if ( _rng.NextDouble() >= ShieldReactChance ) return;
		if ( State == SkeletonState.Attack ) _busyUntil = Time.Now;   // cut the recovery: the raise must READ
		RaiseGuard( ShieldReactHoldSeconds );
		if ( DebugLog ) Log.Info( $"[Skeleton] {BrainName} reads the combo: shield up" );
	}

	private AttackSpec Spec( Kind kind )
	{
		if ( UsesTable )
		{
			// the table: kind N is entry N-1 (the graph's attack_kind N)
			var i = (int)kind - 1;
			return i >= 0 && i < Attacks.Count ? Attacks[i] : Attacks[0];
		}
		return kind switch
		{
			Kind.SwordA => SwordA,
			Kind.SwordB => SwordB,
			Kind.Charge => Charge,
			Kind.ShieldBash => ShieldBash,
			_ => SwordA,
		};
	}

	private float Roll( float min, float max ) => min + (float)_rng.NextDouble() * MathF.Max( 0f, max - min );

	[Rpc.Broadcast]
	private void ShowHitLanded()
	{
		var body = Components.Get<SkeletonBody>();
		body?.Hitstop( HitFeel.TakenHitstopSeconds );
	}
}
