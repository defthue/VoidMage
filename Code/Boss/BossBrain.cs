using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>Dormant on the rocks; Waking (the rise); Idle = awake and fighting; Dead.</summary>
public enum BossPhase { Dormant, Waking, Idle, Dead }

/// <summary>
/// The Bone Colossus's mind, on the skull. Host-only truth, every visible thing a synced field. The three parts are
/// network roots (their transforms reach every peer) driven by one <see cref="BossMover"/> each: bodies with
/// velocity chasing targets, so they lead, overshoot and settle; the stance, the wake-up and the moves only write
/// a <see cref="MoveIntent"/> per tick. The fight is a Souls grammar: the boss keeps a GUARD (the skull hovering
/// out of reach in front of the player, the hands in a tight formation beside the LIVE skull, in step with it
/// wherever it goes), a <see cref="BossDecider"/> picks moves from an authored table (range bands, cooldowns,
/// chains, a grab that punishes hugging a resting hand, repositions as moves of their own), and every move runs
/// the same beats (approach, tell, lock, strike, rest, recover; see <see cref="BossAttack"/>). The move's inputs go
/// out by RPC and every peer judges its own player against the hazards on its own clock (the trap model), and
/// presents impacts only when the host's mover made a REAL contact (<see cref="ImpactRpc"/>). The jaw is a plain child rotated on every peer from
/// <see cref="JawOpen01"/>; the eye fire is <see cref="BossEyes"/> reading <see cref="EyeFire01"/>.
/// </summary>
[Title( "Boss Brain" ), Category( "Void Mage" ), Icon( "psychology" )]
public sealed class BossBrain : Component, IBossFrame
{
	public static BossBrain Current { get; private set; }

	/// <summary>The jaw's hinge in skull-local units (its rear top; the model's face points +x).</summary>
	[Property, Group( "Jaw" )] public Vector3 JawHinge { get; set; } = new( -24f, 0f, 20f );
	[Property, Group( "Jaw" )] public float JawOpenDegrees { get; set; } = 32f;

	/// <summary>Skull centre height over the fighting floor while hovering, model units (x scale), and the bob on it.</summary>
	[Property, Group( "Stance" )] public float HoverHeight { get; set; } = 230f;
	[Property, Group( "Stance" )] public float HoverBob { get; set; } = 6f;
	[Property, Group( "Stance" )] public float HoverBobHz { get; set; } = 0.3f;
	/// <summary>How far in front of the player the skull keeps its stance, world units (it drifts there at `boss_stance_speed`).</summary>
	[Property, Group( "Stance" )] public float StanceDistance { get; set; } = 900f;
	/// <summary>The stance never leaves this radius around the arena's centre (the lava is out there); `boss_arena_radius` overrides.</summary>
	[Property, Group( "Stance" )] public float ArenaRadius { get; set; } = 1600f;

	/// <summary>How far a hand launches off its pillar before flying to the skull, model units (x its scale).</summary>
	[Property, Group( "Wake" )] public float HandHoverHeight { get; set; } = 60f;
	[Property, Group( "Wake" )] public float WakeSeconds { get; set; } = 8f;

	/// <summary>
	/// The vitals for a player at the end of the run (4 boons, about 20 enchantments; 2026-09-23: doubled after a
	/// play, "with 20 enchantments it's actually quite easy", then brought down by a third the same day). A hand; the
	/// skull's first layer (drained by hits on the skull, struck off whole when both hands are destroyed: its loss is
	/// phase 2); the skull's health under it (phase 2's pool). Written onto the parts at the wake-up.
	/// </summary>
	[ConVar( "boss_hp_hand", ConVarFlags.Saved )] public static float HandHealth { get; set; } = 600f;
	[ConVar( "boss_hp_guard", ConVarFlags.Saved )] public static float GuardHealth { get; set; } = 1350f;
	[ConVar( "boss_hp_skull", ConVarFlags.Saved )] public static float SkullHealth { get; set; } = 2150f;

	/// <summary>The dormant Colossus rises when a living player comes this close to the skull (flat distance); a hit wakes it sooner. The arena's spawn stands about 2800 u away, so the rise begins past the arena's centre.</summary>
	[ConVar( "boss_wake_distance", ConVarFlags.Saved )] public static float WakeDistance { get; set; } = 1800f;

	/// <summary>The formation: where the hands hold beside the live skull, in its yaw frame, skull model units (x its scale): to each side (outside the horns), ahead, and relative to the skull's centre.</summary>
	[ConVar( "boss_form_spread", ConVarFlags.Saved )] public static float FormSpread { get; set; } = 380f;
	[ConVar( "boss_form_forward", ConVarFlags.Saved )] public static float FormForward { get; set; } = 120f;
	[ConVar( "boss_form_height", ConVarFlags.Saved )] public static float FormHeight { get; set; } = -50f;
	/// <summary>The neutral gap between moves in phase 1 and 2, seconds (a random spread of +50 % on top).</summary>
	[ConVar( "boss_gap_1", ConVarFlags.Saved )] public static float GapPhase1 { get; set; } = 0.7f;
	[ConVar( "boss_gap_2", ConVarFlags.Saved )] public static float GapPhase2 { get; set; } = 0.4f;
	/// <summary>How fast the stance turns to face the player, degrees per second.</summary>
	[ConVar( "boss_turn_rate", ConVarFlags.Saved )] public static float TurnRate { get; set; } = 320f;
	/// <summary>A chained hand move may start while the previous hand's move rests (the one-two); off = strictly one move at a time.</summary>
	[ConVar( "boss_overlap", ConVarFlags.Saved )] public static bool Overlap { get; set; } = true;
	/// <summary>The fight's radius around the arena's centre marker (0: the prefab's ArenaRadius property). The stage's platform is 1500: he hovers out over the lava edge.</summary>
	[ConVar( "boss_arena_radius", ConVarFlags.Saved )] public static float ArenaRadiusOverride { get; set; } = 1900f;
	/// <summary>The hug punish: a player within this of a RESTING hand for this long gets the grab.</summary>
	[ConVar( "boss_hug_distance", ConVarFlags.Saved )] public static float HugDistance { get; set; } = 260f;
	[ConVar( "boss_hug_seconds", ConVarFlags.Saved )] public static float HugSeconds { get; set; } = 1.5f;
	private float Radius => ArenaRadiusOverride > 0f ? ArenaRadiusOverride : ArenaRadius;

	[Sync( SyncFlags.FromHost )] public BossPhase Phase { get; set; }
	[Sync( SyncFlags.FromHost )] public float JawOpen01 { get; set; }
	/// <summary>Eye fire: 0 dark, 1 burning, 2 flaring.</summary>
	[Sync( SyncFlags.FromHost )] public float EyeFire01 { get; set; }
	/// <summary>1 while the hands carry the fight, 2 once the skull does.</summary>
	[Sync( SyncFlags.FromHost )] public int Fight { get; set; } = 1;
	[Sync( SyncFlags.FromHost )] public int AttackSeq { get; set; }
	/// <summary>The move begun last (-1 once none runs): a status readout, the peers keep their own copies by sequence.</summary>
	[Sync( SyncFlags.FromHost )] public int AttackId { get; set; } = -1;
	[Sync( SyncFlags.FromHost )] public BossPartKind AttackPart { get; set; }

	public bool Awake => Phase is BossPhase.Idle;
	public BossPart Skull => _skull ??= Components.Get<BossPart>();
	public BossPart HandL => BossNet.Find( BossPartKind.HandL );
	public BossPart HandR => BossNet.Find( BossPartKind.HandR );
	public int HandsAlive => (HandL is { Dead: false } ? 1 : 0) + (HandR is { Dead: false } ? 1 : 0);
	public GameObject JawObject => _jaw;

	/// <summary>How long the nearest player has stood under the skull (baits the bite).</summary>
	public float UnderSkullSeconds => _underSince > 0f ? Time.Now - _underSince : 0f;
	public float LastBiteAt { get; private set; } = float.NegativeInfinity;
	/// <summary>The first running move (status), or null.</summary>
	public BossAttack CurrentAttack => _runs.Count > 0 ? _runs[0].Attack : null;
	/// <summary>Every running move's name, for the status line.</summary>
	public string RunningNames => _runs.Count == 0 ? "none" : string.Join( "+", _runs.Select( r => r.Attack.Name ) );
	/// <summary>The fighting floor's height.</summary>
	public float FloorZ => _floorZ;
	public Vector3 ArenaCenter => _arenaCenter;
	/// <summary>The orbit bearing from the player to the stance, degrees (the decider aims repositions off it).</summary>
	public float Bearing => _bearing;
	public BossDecider Decider => _decider;

	// ------------------------------------------------------------------ IBossFrame

	/// <summary>The skull's scale: model units to world units.</summary>
	public float S => MathF.Max( 0.01f, WorldScale.x );
	public float Now => Time.Now;
	/// <summary>The skull's hover point (the stance), host truth.</summary>
	public Vector3 StancePoint => _stance;
	/// <summary>The stance's facing (yaw only).</summary>
	public Rotation Facing => Rotation.FromYaw( _yaw );
	/// <summary>The skull's live yaw: the formation's frame.</summary>
	public float FormationYaw => Mover( BossPartKind.Skull ).Rot.Yaw();
	public float SkullPitch => BossTuning.SkullPitch;
	/// <summary>The skull's own stance rotation: the facing with its nose-down tilt.</summary>
	public Rotation SkullFacing => Facing * Rotation.FromPitch( SkullPitch );
	public Vector3 SkullCenter => Mover( BossPartKind.Skull ).Pos;
	public Vector3 Position( BossPartKind kind ) => Mover( kind ).Pos;
	public Rotation Orientation( BossPartKind kind ) => Mover( kind ).Rot;
	public Vector3 Velocity( BossPartKind kind ) => Mover( kind ).Vel;

	/// <summary>The skull's stance pose: the hover point, facing the player, bobbing.</summary>
	public Transform SkullHover()
	{
		if ( _bobPhase < 0f ) _bobPhase = Game.Random.Float( 0f, 10f );
		var bob = MathF.Sin( (Time.Now + _bobPhase) * MathF.Tau * HoverBobHz ) * HoverBob * S;
		return new Transform( _stance + Vector3.Up * bob, SkullFacing );
	}

	/// <summary>A hand's place in the formation: beside the LIVE skull in its yaw frame (the boss's left hand on its left), palm to the front, fingers up. Never inside the skull's keep-out.</summary>
	public Transform HandStance( BossPartKind kind )
	{
		var frame = Rotation.FromYaw( FormationYaw );
		var spread = MathF.Max( FormSpread, KeepOut( kind ) / S + 20f );
		return new Transform( BossGeometry.HandStancePos( SkullCenter, frame, kind, spread, FormForward, FormHeight, S ), HandRot( kind, frame.Forward, Vector3.Up ) );
	}

	/// <summary>A hand pose from what its palm and its fingers point at: the model's frame fix plus the user's pitch/yaw/roll tweak.</summary>
	public Rotation HandRot( BossPartKind kind, Vector3 palmToward, Vector3 fingersToward )
	{
		var part = BossNet.Find( kind );
		var fix = part.IsValid() ? part.StanceRotation : Rotation.Identity;
		return Rotation.LookAt( palmToward, fingersToward ) * BossTuning.HandTweak( kind ) * fix;
	}

	/// <summary>The part's lowest point relative to its pivot (the palm for a hand) for a rotation.</summary>
	public float BottomOffset( BossPartKind kind, Rotation rot )
	{
		var part = BossNet.Find( kind );
		return part.IsValid() ? part.BottomOffsetFromPivot( rot ) : 0f;
	}

	public float LiveBottom( BossPartKind kind )
	{
		var part = BossNet.Find( kind );
		return part.IsValid() ? part.LiveBottomFromPivot() : 0f;
	}

	public float ShapeBottom( BossPartKind kind, string shape )
	{
		var part = BossNet.Find( kind );
		return part.IsValid() ? part.ShapeBottomFromPivot( shape ) : 0f;
	}

	/// <summary>The skull's cranium half diagonal plus a palm's half extent plus a margin: a palm centre inside it is inside the skull.</summary>
	public float KeepOut( BossPartKind hand )
	{
		var part = BossNet.Find( hand );
		var handScale = part.IsValid() ? MathF.Max( 0.01f, part.WorldScale.x ) : 1.5f;
		return 190f * S + 130f * handScale + 40f;
	}

	/// <summary>A point pulled inside the arena's radius around its centre (flat).</summary>
	public Vector3 ClampToArena( Vector3 p ) => ClampToArena( p, Radius );

	public Vector3 ClampToArena( Vector3 p, float radius )
	{
		var from = (p - _arenaCenter).WithZ( 0f );
		return from.Length > radius ? new Vector3( _arenaCenter.x, _arenaCenter.y, p.z ) + from.Normal * radius : p;
	}

	/// <summary>An eye socket's world position (0 right, 1 left), for the bolts.</summary>
	public Vector3 EyePosition( int index )
	{
		var name = index % 2 == 0 ? "eye_r" : "eye_l";
		foreach ( var c in GameObject.Children ) if ( c.Name == name ) return c.WorldPosition;
		return WorldPosition + WorldRotation.Forward * 68f * S + Vector3.Up * 42f * S;
	}

	/// <summary>Host: one bolt of the shake's volley leaves an eye at the nearest player; every peer spawns and judges its own copy.</summary>
	public void LaunchBolt( int seq, int index )
	{
		if ( !Networking.IsHost ) return;
		var player = NearestPlayer( out _ );
		var to = player is not null ? player.FeetPosition + Vector3.Up * 36f : EyePosition( index ) + Facing.Forward * 900f;
		BoltRpc( index, EyePosition( index ), to, false, false );
	}

	/// <summary>Host: a bolt from an eye to a point (the rain marks its landing spot on the floor; a harmless one is a picture, the summon's).</summary>
	public void LaunchBoltTo( int index, Vector3 to, bool mark, bool harmless )
	{
		if ( !Networking.IsHost ) return;
		BoltRpc( index, EyePosition( index ), to, mark, harmless );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BoltRpc( int index, Vector3 from, Vector3 to, bool mark, bool harmless )
	{
		BossBolt.Spawn( Scene, from, to, GameObject, mark, harmless );
	}

	/// <summary>The nearest live player to the stance, or null.</summary>
	public PlayerHealth Nearest( out float dist ) => NearestPlayer( out dist );

	/// <summary>A part's dormant rest as a pivot pose (the arena's markers place the model origin).</summary>
	private Transform RestPivot( BossPart part, Transform rest )
		=> new( rest.Position + rest.Rotation * (part.Pivot * MathF.Max( 0.01f, part.WorldScale.x )), rest.Rotation );

	// ------------------------------------------------------------------ state

	// host-only: the rest poses the arena handed over (the demo's placements, tilt and all)
	private Transform _skullRest, _handLRest, _handRRest;
	private Vector3 _arenaCenter;
	private float _floorZ;
	private float _phaseStart;
	private BossPart _skull;
	private GameObject _jaw;
	private float _bobPhase = -1f;
	private Vector3 _stance;
	private float _yaw;
	private bool _wakeRoared;
	private bool _wakeLaughed;
	/// <summary>Where on the wake-up's 8 s script the laugh clip starts: the moment the eyes ignite and the skull lifts off the rocks, so it comes up laughing (the drums have been going since 0); the roar's drop at 5.5 follows the clip's voice.</summary>
	private const float LaughStart = 1.5f;
	/// <summary>The bearing from the player to the skull's stance spot, degrees: wanders slowly so he circles the player.</summary>
	private float _bearing;
	private readonly Dictionary<BossPartKind, BossMover> _movers = new();
	private readonly Dictionary<BossPartKind, MoveIntent> _lastIntent = new();
	private readonly Dictionary<BossPartKind, float> _groundAt = new();

	/// <summary>Host: one running move.</summary>
	private sealed class Run
	{
		public BossAttack Attack;
		public BossAttackCtx Ctx;
		public float Start;
		/// <summary>The strike stage has begun.</summary>
		public bool Struck;
	}

	/// <summary>Host: a move waiting its turn (a chain's next link, the hug punish).</summary>
	public struct Queued
	{
		public BossAttack Attack;
		public BossPartKind Part;
		public float Speed;
		public Vector3? Aux;
		public float At;
		/// <summary>May start while the previous hand's move still rests (the one-two).</summary>
		public bool Overlap;
	}

	private readonly List<Run> _runs = new();
	private readonly BossDecider _decider = new();
	private Queued? _queued;
	private float _nextAttackAt;
	private bool _opener;
	private float _gapScale = 1f;
	private float _underSince = -1f;
	private float _hug;
	private readonly MoveIntent[] _want = new MoveIntent[3];
	private readonly Run[] _wantRun = new Run[3];

	/// <summary>Every peer: the local copy of one running move, judged on this clock.</summary>
	private sealed class LocalRun
	{
		public BossAttackCtx Ctx;
		public float Start;
		public readonly BossHazardState Hz = new();
	}

	private readonly List<LocalRun> _local = new();

	private BossMover Mover( BossPartKind kind )
	{
		if ( !_movers.TryGetValue( kind, out var m ) ) _movers[kind] = m = new BossMover();
		return m;
	}

	/// <summary>Host, before the spawn snapshot: where the parts lie dormant, and rise from; `arenaCenter` is the fight's centre (the stage's marker).</summary>
	public void Configure( Transform skullRest, Transform handLRest, Transform handRRest, float floorZ, Vector3 arenaCenter )
	{
		_skullRest = skullRest;
		_handLRest = handLRest;
		_handRRest = handRRest;
		_floorZ = floorZ;
		_arenaCenter = arenaCenter;
		Phase = BossPhase.Dormant;
	}

	protected override void OnAwake()
	{
		Current = this;
		_jaw = GameObject.Children.FirstOrDefault( c => c.Name == "jaw" );
		if ( !Components.Get<BossEyes>().IsValid() ) Components.Create<BossEyes>();
	}

	protected override void OnDestroy()
	{
		if ( Current == this ) Current = null;
		// a held player is let go with the boss
		foreach ( var l in _local ) BossAttacks.ById( l.Ctx.AttackId )?.Cancelled( this, l.Ctx, l.Hz );
		_local.Clear();
		_voice?.Stop();
		_voice = null;
	}

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		BossTuning.EnsureFresh();
		if ( _skullRest.Position == Vector3.Zero && HellArena.Built )
			Configure( HellArena.SkullMarker, HellArena.HandLMarker, HellArena.HandRMarker, HellArena.FloorZ, HellArena.ArenaCenter );
		if ( _arenaCenter == Vector3.Zero ) _arenaCenter = _skullRest.Position;
		WorldPosition = _skullRest.Position;
		WorldRotation = _skullRest.Rotation;
		_yaw = _skullRest.Rotation.Yaw();
		_stance = new Vector3( _skullRest.Position.x, _skullRest.Position.y, HoverZ );
		JawOpen01 = 0f;
		EyeFire01 = 0f;
	}

	/// <summary>Host: from the rocks to the stance. Idempotent.</summary>
	public void Wake()
	{
		if ( !Networking.IsHost || Phase != BossPhase.Dormant ) return;
		Phase = BossPhase.Waking;
		_phaseStart = Time.Now;
		_wakeRoared = false;
		_wakeLaughed = false;
		ApplyVitals();
		WakeFeel( 0 );
		Log.Info( "[Boss] waking" );
	}

	/// <summary>Host: the shipped pools onto the parts (the `boss_hp_*` convars), full.</summary>
	private void ApplyVitals()
	{
		Skull?.SetVitalsHost( SkullHealth, GuardHealth );
		HandL?.SetVitalsHost( HandHealth, 0f );
		HandR?.SetVitalsHost( HandHealth, 0f );
	}

	/// <summary>Is this part moving in the running move (the attacking part, or either hand of a two-hand move)?</summary>
	private static bool Involves( Run run, BossPartKind kind )
		=> run.Ctx.Part == kind || (run.Attack.UsesBothHands && kind != BossPartKind.Skull);

	/// <summary>Is the part in any running move?</summary>
	public bool Busy( BossPartKind kind )
	{
		foreach ( var r in _runs ) if ( Involves( r, kind ) ) return true;
		return false;
	}

	/// <summary>Every peer: is a running move for this part in its strike stage (the hand trail's whoosh gate)?</summary>
	public bool IsStriking( BossPartKind kind )
	{
		foreach ( var l in _local )
		{
			var attack = BossAttacks.ById( l.Ctx.AttackId );
			if ( attack is null ) continue;
			var involved = l.Ctx.Part == kind || (attack.UsesBothHands && kind != BossPartKind.Skull);
			if ( involved && attack.StageAt( l.Ctx, Time.Now - l.Start, out _ ) == BossStage.Strike ) return true;
		}
		return false;
	}

	private BossStage StageOf( Run run, float now ) => run.Attack.StageAt( run.Ctx, now - run.Start, out _ );

	/// <summary>Damage factor on a part right now: the skull behind two live hands, and the exposed stall of a move.</summary>
	/// <summary>The skull's exposure: x1 hovering, more while it rests on the floor after one of its own moves (the move's RestMultiplier).</summary>
	public float DamageFactor( BossPart part )
	{
		if ( part.Kind != BossPartKind.Skull ) return 1f;
		var f = 1f;
		foreach ( var r in _runs )
			if ( !r.Attack.UsesHand && StageOf( r, Time.Now ) == BossStage.Rest ) f *= r.Attack.RestMultiplier;
		return f;
	}

	public void OnPartDied( BossPart part )
	{
		Log.Info( $"[Boss] {part.Kind} destroyed" );
		if ( !Networking.IsHost ) return;
		EndInvolving( part.Kind, 0.8f );
		if ( part.Kind == BossPartKind.Skull )
		{
			Phase = BossPhase.Dead;
			foreach ( var r in _runs.ToArray() ) EndAttack( r, 0f );
			_queued = null;
			// a hand still alive rises and bursts with the skull (TickDeath); the destroyed ones are long gone
			BossSummons.KillAll();
			// the death scene begins: the shaking rise under the cry, the burst as it ends (TickDeath carries it on)
			_deathStart = Time.Now;
			_deathBurst = false;
			_deathFrom.Clear();
			DeathDone = false;
			EyeFire01 = 2f;
			DeathBeat( 0 );
		}
		else
		{
			_gapScale = 0.7f;
			// both hands gone: the skull's first layer goes with them (OnGuardBroken follows, once)
			if ( HandsAlive == 0 ) Skull?.BreakGuardHost();
			UpdateFight();
		}
	}

	/// <summary>
	/// Host, once: the skull's first layer is gone (drained, or both hands destroyed). Phase 2 begins: the eyes flare,
	/// he laughs at the first free moment (his own laugh, the fight's), and every peer feels the beat.
	/// </summary>
	public void OnGuardBroken( BossPart skull )
	{
		if ( !Networking.IsHost || Phase != BossPhase.Idle ) return;
		Log.Info( "[Boss] skull guard broken" );
		EyeFire01 = 2f;
		_nextLaughAt = Time.Now;
		GuardBrokenRpc();
		UpdateFight();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void GuardBrokenRpc() => Combat.HitFeel.Shake( 5f, 0.7f );

	private void EndInvolving( BossPartKind kind, float gapScale )
	{
		foreach ( var r in _runs.ToArray() )
			if ( Involves( r, kind ) ) EndAttack( r, gapScale );
		if ( _queued is { } q && (q.Part == kind || (q.Attack.UsesBothHands && kind != BossPartKind.Skull)) ) _queued = null;
	}

	// ------------------------------------------------------------------ geometry

	private float HoverZ => _floorZ + HoverHeight * S;

	/// <summary>
	/// How far over the fighting floor something under a part may stand and still count as ground, world units. The
	/// rim's boulders, chains and spires on the platform stand far higher: they are scenery the parts pass THROUGH
	/// (a hand or the skull lifting over one read as a jump), never stand on, and never rest inside (<see cref="ClearSpot"/>).
	/// </summary>
	[ConVar( "boss_floor_step", ConVarFlags.Saved )] public static float FloorStep { get; set; } = 100f;

	/// <summary>Host: the ground under a point: the fighting floor under any prop standing on the platform, else the arena's statics (the roost, the pillars, the lava), else the floor.</summary>
	public float GroundZ( Vector3 at )
	{
		var z = RawGroundZ( at );
		return IsProp( at, z ) ? _floorZ : z;
	}

	/// <summary>The first static under a point, traced from well over the rim's props (a ray starting inside a boulder saw nothing). Stage pieces tagged `boss_scenery` (the colossal bones rising out of the lava) are never ground: a hand hovering out over one used to be lifted onto its fingers.</summary>
	private float RawGroundZ( Vector3 at )
	{
		var top = new Vector3( at.x, at.y, MathF.Max( at.z + 300f, _floorZ + 1500f ) );
		var tr = Scene.Trace.Ray( top, new Vector3( at.x, at.y, MathF.Min( at.z, _floorZ ) - 2000f ) ).WithoutTags( "player", "boss", "npc", "enemy", "debris", "boss_scenery" ).Run();
		return tr.Hit ? tr.EndPosition.z : _floorZ;
	}

	/// <summary>A hit inside the arena standing more than <see cref="FloorStep"/> over the fighting floor is a prop on the platform.</summary>
	private bool IsProp( Vector3 at, float z ) => z > _floorZ + FloorStep && (at - _arenaCenter).WithZ( 0f ).Length <= Radius;

	/// <summary>Is there a rim prop (a boulder, a chain, a spire) under this point?</summary>
	public bool PropAt( Vector3 at ) => IsProp( at, RawGroundZ( at ) );

	/// <summary>
	/// The nearest spot to `at` (flat, z kept) where a part can lie without a prop under it or under the `footprint`
	/// around it: rings of twelve directions out to 960 u, inside the arena. `at` itself when it is clear or nothing
	/// near it is. Every rest and landing spot goes through this, so nothing lingers inside a stone.
	/// </summary>
	public Vector3 ClearSpot( Vector3 at, float footprint = 120f )
	{
		if ( Clear( at, footprint ) ) return at;
		for ( var r = 120f; r <= 960f; r += 120f )
			for ( var i = 0; i < 12; i++ )
			{
				var p = ClampToArena( at + Rotation.FromYaw( i * 30f + r * 0.125f ).Forward * r );
				if ( Clear( p, footprint ) ) return p.WithZ( at.z );
			}
		return at;
	}

	private bool Clear( Vector3 at, float footprint )
	{
		if ( PropAt( at ) ) return false;
		if ( footprint <= 0f ) return true;
		for ( var i = 0; i < 4; i++ )
			if ( PropAt( at + Rotation.FromYaw( i * 90f ).Forward * footprint ) ) return false;
		return true;
	}

	private static float Ease( float t ) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

	private PlayerHealth NearestPlayer( out float dist )
	{
		PlayerHealth best = null;
		dist = float.MaxValue;
		foreach ( var p in CombatTargets.Players )
		{
			if ( !p.IsValid() || p.IsDead ) continue;
			var d = (p.FeetPosition - SkullCenter).WithZ( 0f ).Length;
			if ( d < dist ) { dist = d; best = p; }
		}
		return best;
	}

	private static Transform RestOf( BossPartKind kind, Transform skull, Transform l, Transform r ) => kind switch
	{
		BossPartKind.HandL => l,
		BossPartKind.HandR => r,
		_ => skull,
	};

	// ------------------------------------------------------------------ host motion

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !Networking.IsHost ) return;
		var now = Time.Now;
		var dt = Time.Delta;
		switch ( Phase )
		{
			case BossPhase.Dormant:
				foreach ( var part in BossNet.Parts )
				{
					if ( !part.IsValid() || part.Dead ) continue;
					var rest = RestOf( part.Kind, _skullRest, _handLRest, _handRRest );
					var pivotRest = RestPivot( part, rest );
					Mover( part.Kind ).Teleport( pivotRest.Position, pivotRest.Rotation );
					part.WorldPosition = rest.Position;
					part.WorldRotation = rest.Rotation;
				}
				// the approach wakes it: a run's player has no other hint than the sleeping skull ahead
				if ( WakeDistance > 0f && NearestPlayer( out var near ).IsValid() && near <= WakeDistance ) Wake();
				break;
			case BossPhase.Waking:
				TickWake( now, dt );
				break;
			case BossPhase.Idle:
				TickFight( now, dt );
				break;
			case BossPhase.Dead:
				TickDeath( now, dt );
				break;
		}
	}

	// ------------------------------------------------------------------ death

	/// <summary>Every peer: the Colossus has burst; the run may show its victory.</summary>
	[Sync( SyncFlags.FromHost )] public bool DeathDone { get; set; }
	/// <summary>The user's clips for the death (empty = that beat is silent): the cry over the rise (the burst comes exactly as it ends), and the burst itself.</summary>
	public const string DeathCrySound = "sounds/bonecoldeath.sound";
	public const string DeathBurstSound = "";
	/// <summary>How long the cry clip lasts (sounds/BoneColDeath.mp3, measured): the rise takes exactly this and the burst comes at its end.</summary>
	private const float DeathCrySeconds = 8.25f;
	/// <summary>How far the whole Colossus floats up over the cry, world units x the skull's scale.</summary>
	[ConVar( "boss_death_rise", ConVarFlags.Saved )] public static float DeathRise { get; set; } = 420f;
	/// <summary>The dying rise: a soft pull upward that the tremble rides on.</summary>
	private static readonly Dynamics RiseDyn = new( 900f, 700f, 0f, 0.5f );
	private float _deathStart = -1f;
	private bool _deathBurst;
	private readonly Dictionary<BossPartKind, (Vector3 pos, Rotation rot)> _deathFrom = new();

	/// <summary>
	/// Host, the death scene in game seconds since the last blow: every bone still standing (the skull, and a hand
	/// not yet destroyed; the destroyed ones burst when they died) SHAKES harder and harder and FLOATS UP, faster
	/// and faster, keeping the pose it died in, the eyes at their brightest, the jaw hanging open, for exactly the
	/// length of the cry; as the cry ends it all BURSTS (DeathBurstRpc: chips, dust, rings, the jolt) and is gone.
	/// DeathDone 1.8 s after the burst.
	/// </summary>
	private void TickDeath( float now, float dt )
	{
		if ( _deathStart < 0f ) return;
		var t = now - _deathStart;
		JawOpen01 = 1f;
		if ( _deathBurst )
		{
			if ( t >= DeathCrySeconds + 1.8f && !DeathDone ) DeathDone = true;
			return;
		}
		var k = Math.Clamp( t / DeathCrySeconds, 0f, 1f );
		var lift = DeathRise * S * k * k;   // slow at first, faster and faster: it is being pulled up
		EyeFire01 = 2f + k;
		foreach ( var part in BossNet.Parts )
		{
			if ( !part.IsValid() || (part.Dead && part.Kind != BossPartKind.Skull) ) continue;
			var m = Mover( part.Kind );
			if ( !_deathFrom.TryGetValue( part.Kind, out var from ) ) _deathFrom[part.Kind] = from = (m.Pos, m.Rot);
			var (offset, jitter) = BossGeometry.Tremble( t + (int)part.Kind * 1.7f, 0.3f + 0.7f * k, S * (part.Kind == BossPartKind.Skull ? 1.6f : 1.2f) );
			var it = MoveIntent.To( from.pos + Vector3.Up * lift, RiseDyn, from.rot * jitter, BossDynamics.TurnIdle )
				.NoGround().Shifted( offset ).WithLean( 0f ).WithPose( 2 ).InPhase( 1000 );
			Drive( part, it, now, dt );
		}
		if ( t >= DeathCrySeconds )
		{
			_deathBurst = true;
			EyeFire01 = 0f;
			DeathBurstRpc();
			// a hand that rose with him is dead now too (it burst in the RPC: no burst of its own)
			HandL?.KillHost();
			HandR?.KillHost();
		}
	}

	/// <summary>Every peer: the beats of the death scene. 0 the last blow: the shake over the whole rise and the cry from the jaw.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void DeathBeat( int beat )
	{
		switch ( beat )
		{
			case 0:
				HitFeel.Shake( 2.2f, DeathCrySeconds );
				if ( DeathCrySound.Length > 0 ) Voice( DeathCrySound );
				break;
		}
	}

	/// <summary>Every peer: the burst. Every bone still standing flies apart where it hangs (chips, dust, a standing ring each; the hands destroyed in the fight burst back then), a shock ring runs out over the floor under the skull, the arena jolts, and the bones are gone.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void DeathBurstRpc()
	{
		var floor = WorldPosition.WithZ( HellArena.Built ? HellArena.FloorZ : WorldPosition.z );
		foreach ( var part in BossNet.Parts )
		{
			if ( !part.IsValid() || (part.Dead && part.Kind != BossPartKind.Skull) ) continue;
			part.Burst();
		}
		HitFeel.Shake( 10f, 1.4f );
		Rendering.RetroFilter.TriggerRipple( WorldPosition, 2.6f );
		BossVfx.Shockwave( floor, 200f, SlamAttack.RingSpeed * 1.3f, SlamAttack.RingMax * 1.6f, 0.9f );
		BossVfx.FloorDust( floor, 380f );
		if ( DeathBurstSound.Length > 0 ) Audio.GameAudio.PlayLoop( DeathBurstSound, WorldPosition );
	}

	/// <summary>Step a part's mover with its intent and write the part's transform (plus the intent's visual offset and pose).</summary>
	private void Drive( BossPart part, in MoveIntent it, float now, float dt )
	{
		var m = Mover( part.Kind );
		if ( !_groundAt.TryGetValue( part.Kind, out var at ) || now - at > 0.1f )
		{
			_groundAt[part.Kind] = now;
			m.Ground = GroundZ( m.Pos );
		}
		m.Step( it, dt, part.BottomOffsetFromPivot, now );
		part.WorldRotation = m.Rot;
		part.WorldPosition = part.OriginFor( m.Pos + it.Offset, m.Rot );
		if ( it.Pose >= 0 && part.Kind != BossPartKind.Skull ) part.Pose = it.Pose;
		_lastIntent[part.Kind] = it;
	}

	/// <summary>
	/// The rise, on a fixed 8 s script scaled by WakeSeconds: rumble and finger twitch (0-1.5), the eyes ignite
	/// (1.5), the skull lifts off the rocks turning to face the player (1.5+), the hands launch off their pillars
	/// (2-2.6) and fly to its sides, the jaw drops with a roar (5.5), and it is awake at 8 with a slow slam.
	/// </summary>
	private void TickWake( float now, float dt )
	{
		var t = (now - _phaseStart) / MathF.Max( 0.1f, WakeSeconds ) * 8f;
		var player = NearestPlayer( out _ );
		var wantYaw = player is not null ? (player.FeetPosition - _skullRest.Position).WithZ( 0f ) : Vector3.Zero;
		_yaw = wantYaw.IsNearZeroLength ? _skullRest.Rotation.Yaw() : Rotation.LookAt( wantYaw.Normal ).Yaw();
		_stance = new Vector3( _skullRest.Position.x, _skullRest.Position.y, HoverZ );
		if ( t >= 1.5f ) EyeFire01 = MathF.Max( EyeFire01, 1f );

		foreach ( var part in BossNet.Parts )
		{
			if ( !part.IsValid() || part.Dead ) continue;
			var rest = RestPivot( part, RestOf( part.Kind, _skullRest, _handLRest, _handRRest ) );
			Drive( part, BossGeometry.WakeIntent( this, part.Kind, t, rest, HandHoverHeight * S * 4f ), now, dt );
		}

		if ( t >= LaughStart && !_wakeLaughed )
		{
			_wakeLaughed = true;
			WakeFeel( 2 );
		}
		if ( t >= 5.5f && !_wakeRoared )
		{
			_wakeRoared = true;
			WakeFeel( 1 );
		}
		// the jaw: shut until the eyes ignite, then the LAUGH all the way up (the jaw follows the clip's own loudness, in
		// real seconds since it started, so every "ha" is a bite of the jaw), a slack gape once the voice ends, then the roar's drop at 5.5
		var laughReal = (now - _phaseStart) - LaughStart * MathF.Max( 0.1f, WakeSeconds ) / 8f;
		JawOpen01 = t < LaughStart ? 0f
			: t < 5.5f ? MathF.Max( BossGeometry.LaughClip( laughReal ), laughReal > BossGeometry.LaughClipSeconds ? 0.2f : 0f )
			: Ease( (t - 5.5f) / 0.8f );
		if ( t >= 2.7f && t < 8f ) _bearing = Rotation.LookAt( (_skullRest.Position - (player?.FeetPosition ?? _skullRest.Position)).WithZ( 0f ).Normal ).Yaw();
		if ( t >= 8f )
		{
			Phase = BossPhase.Idle;
			_phaseStart = now;
			_opener = true;
			_nextAttackAt = now + 0.8f;
			_laughAt = -1f;
			_nextLaughAt = now + NextLaughGap();
			Fight = 1;
			_decider.Reset();
		}
	}

	/// <summary>The guard: face the player; the stance holds the fighting distance in front of them, circling slowly; frozen while the skull is in a move (it is the point the skull comes back to).</summary>
	private void TickStance( PlayerHealth player, float dist, float now, float dt )
	{
		var feet = player.FeetPosition;
		_underSince = dist < 380f ? (_underSince < 0f ? now : _underSince) : -1f;
		if ( Busy( BossPartKind.Skull ) ) return;
		var to = (feet - _stance).WithZ( 0f );
		if ( !to.IsNearZeroLength )
		{
			var wantYaw = Rotation.LookAt( to.Normal ).Yaw();
			var delta = (wantYaw - _yaw + 540f) % 360f - 180f;
			_yaw += Math.Clamp( delta, -TurnRate * dt, TurnRate * dt );
		}
		if ( _bobPhase < 0f ) _bobPhase = Game.Random.Float( 0f, 10f );
		// the stance spot circles the player slowly, reversing now and then: alive, never parked, never a blur
		_bearing += dt * BossTuning.OrbitRate * MathF.Sin( now * 0.55f + _bobPhase );
		var desired = ClampToArena( feet + Rotation.FromYaw( _bearing ).Forward * StanceDistance );
		var step = (desired - _stance).WithZ( 0f );
		var max = BossTuning.StanceSpeed * dt;
		_stance += step.Length > max ? step.Normal * max : step;
		_stance.z = HoverZ;
	}

	/// <summary>Host: a reposition's destination becomes the stance at once (the skull flies there, the formation with it).</summary>
	public void SetStance( Vector3 dest, Vector3 playerFeet )
	{
		_stance = ClampToArena( dest ).WithZ( HoverZ );
		var back = (_stance - playerFeet).WithZ( 0f );
		if ( !back.IsNearZeroLength ) { _bearing = Rotation.LookAt( back.Normal ).Yaw(); _yaw = Rotation.LookAt( -back.Normal ).Yaw(); }
	}

	private void TickFight( float now, float dt )
	{
		var player = NearestPlayer( out var dist );
		if ( player is not null ) TickStance( player, dist, now, dt );

		// the running moves: lock, intents for their parts, host ticks, and their ends
		Array.Clear( _wantRun, 0, _wantRun.Length );
		for ( var i = _runs.Count - 1; i >= 0; i-- )
		{
			var run = _runs[i];
			var attack = run.Attack;
			var t = now - run.Start;
			var tracked = player is not null ? player.FeetPosition : run.Ctx.Target;
			if ( !run.Ctx.Locked && t >= attack.LockTime( run.Ctx ) )
			{
				run.Ctx.Target = attack.LockTarget( this, run.Ctx.Part, tracked );
				run.Ctx.FloorZ = GroundZ( run.Ctx.Target );
				run.Ctx.Facing = Facing;
				run.Ctx.Locked = true;
				LockRpc( run.Ctx.Seq, run.Ctx.Target, run.Ctx.FloorZ, run.Ctx.Facing );
			}
			Want( run, run.Ctx.Part, attack.Move( this, run.Ctx.Part, ref run.Ctx, t, tracked ) );
			if ( attack.UsesBothHands ) Want( run, OtherHand( run.Ctx.Part ), attack.Move( this, OtherHand( run.Ctx.Part ), ref run.Ctx, t, tracked ) );
			attack.HostTick( this, run.Ctx, t );
			var stage = attack.StageAt( run.Ctx, t, out _ );
			if ( !run.Struck && stage >= BossStage.Strike ) run.Struck = true;
			// the hug punish: a player glued to a RESTING hand for a while gets grabbed by it
			if ( player is not null && attack.UsesHand && !attack.UsesBothHands && stage == BossStage.Rest ) TickHug( run, player, dt );
			// over once the timeline ran out AND the part is back at its stance (or it has had 3 s more to get there)
			var total = attack.Total( run.Ctx );
			if ( t >= total )
			{
				var back = AtStance( run.Ctx.Part ) && (!attack.UsesBothHands || AtStance( OtherHand( run.Ctx.Part ) ));
				if ( back || t >= total + 3f ) EndAttack( run, 1f );
			}
		}

		var skullBusy = Busy( BossPartKind.Skull );
		foreach ( var part in BossNet.Parts )
		{
			if ( !part.IsValid() ) continue;
			if ( part.Dead ) continue;   // a destroyed hand burst and is gone
			var k = (int)part.Kind;
			MoveIntent it;
			if ( _wantRun[k] is not null ) it = _want[k];
			else if ( part.Kind == BossPartKind.Skull && Escort( now, out var escort ) ) it = escort;
			else it = BossGeometry.StanceIntent( this, part.Kind, now, skullBusy );
			Drive( part, it, now, dt );
			// a real contact by a part in a move: the peers present the impact there, now
			var m = Mover( part.Kind );
			if ( m.Contacted && _wantRun[k] is { } run ) ImpactRpc( run.Ctx.Seq, part.Kind, (int)StageOf( run, now ), m.Pos );
		}
		if ( !skullBusy ) JawOpen01 = MathF.Max( 0f, JawOpen01 - dt * 0.8f );
		TickLaugh( now, skullBusy );
		if ( _runs.Count == 0 && EyeFire01 > 1f ) EyeFire01 = MathF.Max( 1f, EyeFire01 - dt );

		UpdateFight();
		if ( player is null ) return;
		// a queued move (a chain's next link, the punish) goes first; a fresh pick only from an empty neutral
		if ( _queued is { } q && now >= q.At && (_runs.Count == 0 || (q.Overlap && Overlap && q.Attack.UsesHand && CanOverlap( q.Part ))) )
		{
			_queued = null;
			if ( !Begin( q.Attack, q.Part, q.Speed, q.Aux ) ) _nextAttackAt = now + 0.3f;
			return;
		}
		if ( _runs.Count == 0 && _queued is null && now >= _nextAttackAt ) Select( player, dist );
	}

	// ------------------------------------------------------------------ the laugh

	/// <summary>The gap between his laughs in the fight (the wake-up's laugh clip, from the jaw), seconds: random between these two each time.</summary>
	[ConVar( "boss_laugh_min", ConVarFlags.Saved )] public static float LaughMin { get; set; } = 30f;
	[ConVar( "boss_laugh_max", ConVarFlags.Saved )] public static float LaughMax { get; set; } = 60f;
	private float _laughAt = -1f;
	private float _nextLaughAt;
	/// <summary>Every peer: the voice playing from the mouth right now (a laugh, the death cry); it rides the jaw wherever the skull goes.</summary>
	private SoundHandle _voice;

	private float NextLaughGap() => Game.Random.Float( MathF.Min( LaughMin, LaughMax ), MathF.Max( LaughMin, LaughMax ) );

	/// <summary>
	/// Host: now and then, while the skull is free (no skull move has its jaw), he laughs: the clip plays from the jaw
	/// on every peer and the jaw bites along the clip's own loudness, exactly as at the wake-up (the jaw IS the
	/// envelope here, no blending with the decay: a "ha" opens and shuts it in a tenth of a second).
	/// </summary>
	private void TickLaugh( float now, bool skullBusy )
	{
		if ( _laughAt >= 0f )
		{
			var s = now - _laughAt;
			if ( s > BossGeometry.LaughClipSeconds ) _laughAt = -1f;
			else if ( !skullBusy ) JawOpen01 = BossGeometry.LaughClip( s );
			return;
		}
		if ( skullBusy || now < _nextLaughAt ) return;
		_laughAt = now;
		_nextLaughAt = now + NextLaughGap();
		LaughRpc();
	}

	/// <summary>Host (the director's button): the laugh now, whatever the skull is doing (a running skull move keeps the jaw).</summary>
	public void LaughNow()
	{
		if ( !Networking.IsHost || Phase != BossPhase.Idle ) return;
		_laughAt = Time.Now;
		_nextLaughAt = Time.Now + NextLaughGap();
		LaughRpc();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void LaughRpc() => Voice( "sounds/bonecolossuslaugh.sound" );

	/// <summary>Every peer: a clip from the mouth, never culled by distance (the cave hears him from anywhere), following the jaw while it plays.</summary>
	private void Voice( string eventPath )
	{
		_voice?.Stop();
		_voice = Audio.GameAudio.PlayLoop( eventPath, _jaw.IsValid() ? _jaw.WorldPosition : WorldPosition );
	}

	/// <summary>Every peer, each frame: the playing voice stays in the mouth (a laugh mid-flight moves with him).</summary>
	private void FollowMouth()
	{
		if ( !_voice.IsValid() ) return;
		if ( _voice.IsPlaying ) _voice.Position = _jaw.IsValid() ? _jaw.WorldPosition : WorldPosition;
		else _voice = null;
	}

	/// <summary>A running hand move that places the skull (the march): its intent for it this tick.</summary>
	private bool Escort( float now, out MoveIntent it )
	{
		foreach ( var r in _runs )
		{
			if ( !r.Attack.UsesHand ) continue;
			if ( r.Attack.Escort( this, r.Ctx, now - r.Start ) is { } e ) { it = e; return true; }
		}
		it = default;
		return false;
	}

	private void Want( Run run, BossPartKind kind, in MoveIntent it )
	{
		_want[(int)kind] = it;
		_wantRun[(int)kind] = run;
	}

	/// <summary>A hand move may join while every running move is a hand move past its strike and none uses this part.</summary>
	private bool CanOverlap( BossPartKind part )
	{
		foreach ( var r in _runs )
		{
			if ( !r.Attack.UsesHand || !r.Struck || Involves( r, part ) ) return false;
		}
		return true;
	}

	private void TickHug( Run run, PlayerHealth player, float dt )
	{
		var hand = BossNet.Find( run.Ctx.Part );
		if ( !hand.IsValid() || hand.Dead ) return;
		var body = player.FeetPosition + Vector3.Up * 36f;
		var near = hand.ClosestPoint( body ).Distance( body ) <= HugDistance;
		_hug = near ? _hug + dt : MathF.Max( 0f, _hug - dt * 2f );
		if ( _hug < HugSeconds || _queued is not null ) return;
		var grab = BossAttacks.Find( "grab" );
		if ( grab is null || !_decider.Ready( "grab", Time.Now ) ) return;
		_hug = 0f;
		_queued = new Queued { Attack = grab, Part = run.Ctx.Part, Speed = 1.2f, At = Time.Now };
		if ( BossNet.Debug > 0 ) Log.Info( $"[Boss] punish: grab with {run.Ctx.Part} (hugged {HugSeconds:0.0} s)" );
	}

	private static BossPartKind OtherHand( BossPartKind kind ) => kind == BossPartKind.HandL ? BossPartKind.HandR : BossPartKind.HandL;

	private bool AtStance( BossPartKind kind )
	{
		var stance = kind == BossPartKind.Skull ? SkullHover().Position : HandStance( kind ).Position;
		return Position( kind ).Distance( stance ) < 150f;
	}

	/// <summary>Phase 2 is the skull without its first layer (drained by hits, or struck off with the second hand).</summary>
	private void UpdateFight()
	{
		if ( Fight >= 2 ) return;
		if ( HandsAlive == 0 || (Skull.IsValid() && !Skull.Guarded) )
		{
			Fight = 2;
			Log.Info( "[Boss] phase 2" );
		}
	}

	// ------------------------------------------------------------------ selection

	/// <summary>The opener is a slow slam; after it the decider's table picks.</summary>
	private void Select( PlayerHealth player, float dist )
	{
		if ( _opener )
		{
			_opener = false;
			var hand = NearerHand( player.FeetPosition );
			if ( hand.IsValid() && Begin( BossAttacks.Find( "slam" ), hand.Kind, 0.7f ) ) return;
		}
		var pick = _decider.Pick( this, player, dist );
		if ( pick is null ) { _nextAttackAt = Time.Now + 0.4f; return; }
		var p = pick.Value;
		if ( !Begin( p.Attack, p.Part, p.Speed, p.Aux ) ) _nextAttackAt = Time.Now + 0.4f;
	}

	private BossPart NearerHand( Vector3 to )
	{
		var l = HandL;
		var r = HandR;
		var lOk = l is { Dead: false } && !Busy( BossPartKind.HandL );
		var rOk = r is { Dead: false } && !Busy( BossPartKind.HandR );
		if ( lOk && rOk ) return l.WorldPosition.DistanceSquared( to ) <= r.WorldPosition.DistanceSquared( to ) ? l : r;
		return lOk ? l : rOk ? r : null;
	}

	/// <summary>Host: start a move with a part (refused while that part is in another move). `speed` scales the approach and the tell (below 1 = slower); `aux` is a destination for a reposition.</summary>
	public bool Begin( BossAttack attack, BossPartKind partKind, float speed, Vector3? aux = null )
	{
		if ( !Networking.IsHost || attack is null || Phase != BossPhase.Idle ) return false;
		var part = BossNet.Find( partKind );
		if ( !part.IsValid() || part.Dead ) return false;
		if ( attack.UsesBothHands )
		{
			partKind = BossPartKind.HandL;
			if ( HandL is not { Dead: false } || HandR is not { Dead: false } ) { Log.Warning( "[Boss] a two-hand move needs both hands" ); return false; }
			if ( Busy( BossPartKind.HandL ) || Busy( BossPartKind.HandR ) ) { Log.Warning( "[Boss] a hand is busy" ); return false; }
		}
		else if ( Busy( partKind ) ) { Log.Warning( $"[Boss] {partKind} is busy" ); return false; }
		var player = NearestPlayer( out _ );
		var feet = player is not null ? player.FeetPosition : _stance.WithZ( _floorZ );
		var s = MathF.Max( 0.2f, speed );
		var run = new Run
		{
			Attack = attack,
			Start = Time.Now,
			Ctx = new BossAttackCtx
			{
				Seq = ++AttackSeq, AttackId = BossAttacks.IdOf( attack ), Part = partKind,
				Target = feet, FloorZ = GroundZ( feet ), Facing = Facing,
				StartPos = part.WorldPosition, StartRot = part.WorldRotation, Speed = s, Locked = false,
			},
		};
		if ( aux is { } a )
		{
			run.Ctx.Aux = a;
			run.Ctx.AuxF = 1f;
			if ( attack is RepositionAttack ) SetStance( a, feet );
		}
		run.Ctx.ApproachSeconds = attack.PlanApproach( this, partKind, ref run.Ctx, feet );
		if ( attack.UsesBothHands ) run.Ctx.ApproachSeconds = MathF.Max( run.Ctx.ApproachSeconds, attack.PlanApproach( this, OtherHand( partKind ), ref run.Ctx, feet ) );
		_runs.Add( run );
		AttackId = run.Ctx.AttackId;
		AttackPart = partKind;
		_decider.Began( attack.Name, Time.Now );
		if ( attack.Name == "bite" ) LastBiteAt = Time.Now;
		AttackRpc( run.Ctx.Seq, run.Ctx.AttackId, partKind, run.Ctx.Target, run.Ctx.FloorZ, run.Ctx.Facing, run.Ctx.StartPos, run.Ctx.StartRot, run.Ctx.Speed, run.Ctx.ApproachSeconds );
		if ( BossNet.Debug > 0 ) Log.Info( $"[Boss] {attack.Name} with {partKind} at x{speed:0.00}, approach {run.Ctx.ApproachSeconds:0.00} s ({_runs.Count} running)" );
		return true;
	}

	/// <summary>Host: the move is over (or cancelled): tell the peers if it was cut short, set the neutral gap, and let the decider chain the next link.</summary>
	private void EndAttack( Run run, float gapScale )
	{
		var finished = run.Attack;
		var part = run.Ctx.Part;
		_runs.Remove( run );
		if ( Time.Now - run.Start < finished.Total( run.Ctx ) - 0.05f ) CancelRpc( run.Ctx.Seq );
		if ( _runs.Count == 0 ) AttackId = -1;
		var gap = (Fight >= 2 ? GapPhase2 : GapPhase1) * _gapScale * gapScale;
		_nextAttackAt = MathF.Max( _nextAttackAt, Time.Now + gap * Game.Random.Float( 1f, 1.5f ) );
		if ( gapScale <= 0f || _queued is not null ) return;
		var player = NearestPlayer( out var dist );
		if ( player is null ) return;
		var next = _decider.Chain( this, finished.Name, part, player, dist );
		if ( next is { } q )
		{
			_queued = q;
			if ( BossNet.Debug > 0 ) Log.Info( $"[Boss] chain {finished.Name} -> {q.Attack.Name} ({q.Part}) in {q.At - Time.Now:0.00} s{(q.Overlap ? " overlapping" : "")}" );
		}
	}

	// ------------------------------------------------------------------ every peer

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void AttackRpc( int seq, int id, BossPartKind part, Vector3 target, float floorZ, Rotation facing, Vector3 startPos, Rotation startRot, float speed, float approachSeconds )
	{
		var l = new LocalRun
		{
			Ctx = new BossAttackCtx { Seq = seq, AttackId = id, Part = part, Target = target, FloorZ = floorZ, Facing = facing, StartPos = startPos, StartRot = startRot, Speed = speed, ApproachSeconds = approachSeconds, Locked = false },
			Start = Time.Now,
		};
		l.Hz.Seq = seq;
		_local.Add( l );
	}

	private LocalRun LocalBySeq( int seq )
	{
		foreach ( var l in _local ) if ( l.Ctx.Seq == seq ) return l;
		return null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void LockRpc( int seq, Vector3 target, float floorZ, Rotation facing )
	{
		var l = LocalBySeq( seq );
		if ( l is null ) return;
		l.Ctx.Target = target;
		l.Ctx.FloorZ = floorZ;
		l.Ctx.Facing = facing;
		l.Ctx.Locked = true;
		// every lock clicks: the moment the aim freezes and the tremble starts
		var attack = BossAttacks.ById( l.Ctx.AttackId );
		var part = BossNet.Find( l.Ctx.Part );
		if ( attack is not null && !attack.SilentLock ) BiteAttack.Click( attack.UsesHand && part.IsValid() ? part.AimPoint : WorldPosition );
	}

	/// <summary>The host's mover made a real contact for a running move: the peers present the impact there (once, unless the move has many landings).</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ImpactRpc( int seq, BossPartKind partKind, int stage, Vector3 at )
	{
		var l = LocalBySeq( seq );
		if ( l is null ) return;
		var attack = BossAttacks.ById( l.Ctx.AttackId );
		var part = BossNet.Find( partKind );
		if ( attack is null || !part.IsValid() ) return;
		if ( l.Hz.Impacted && !attack.MultiImpact ) return;
		l.Hz.Impacted = true;
		attack.OnImpact( this, part, l.Ctx, (BossStage)stage, at );
	}

	/// <summary>The host cut a move short (a stagger, a death): the local copy stops judging, and lets go of anything it holds.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void CancelRpc( int seq )
	{
		var l = LocalBySeq( seq );
		if ( l is null ) return;
		BossAttacks.ById( l.Ctx.AttackId )?.Cancelled( this, l.Ctx, l.Hz );
		_local.Remove( l );
	}

	/// <summary>The wake-up's feel on every peer: 0 the first rumble, 1 the roar, 2 the laugh (the user's clip, cave reverb baked in) from the mouth.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void WakeFeel( int beat )
	{
		switch ( beat )
		{
			case 0:
				HitFeel.Shake( 1.2f, 1.6f );
				Audio.GameAudio.Play( "sounds/ambienceknock.sound", WorldPosition );
				// the drums in the deep (the user's 7 s clip) under the whole rise: from everywhere, not a spot
				Audio.GameAudio.Play2D( "sounds/drumsinthedeep.sound" );
				break;
			case 2:
				// never culled by distance: the player wakes him from the far side of the platform (3000+ u from the roost,
				// past audio_cull_distance), and this laugh is heard from anywhere in the cave
				Voice( "sounds/bonecolossuslaugh.sound" );
				break;
			case 1:
				HitFeel.Shake( 3.5f, 0.7f );
				Audio.GameAudio.Play( "sounds/ambiencehorror.sound", WorldPosition );
				break;
		}
	}

	protected override void OnUpdate()
	{
		BossNet.Draw( Scene );
		if ( _jaw.IsValid() )
		{
			// the jaw model sits off its socket: shifted by boss_jaw_offset and tilted by boss_jaw_tilt, then swung about the hinge
			var rot = Rotation.FromPitch( BossTuning.JawTilt + JawOpen01 * JawOpenDegrees );
			_jaw.LocalRotation = rot;
			_jaw.LocalPosition = BossTuning.JawOffsetVector + JawHinge - rot * JawHinge;
		}
		FollowMouth();
		for ( var i = _local.Count - 1; i >= 0; i-- )
		{
			var l = _local[i];
			var attack = BossAttacks.ById( l.Ctx.AttackId );
			var part = BossNet.Find( l.Ctx.Part );
			if ( attack is null || !part.IsValid() ) { _local.RemoveAt( i ); continue; }
			var t = Time.Now - l.Start;
			var stage = attack.StageAt( l.Ctx, t, out _ );
			if ( (int)stage != l.Hz.PrevStage )
			{
				// fire every stage entered since the last frame, in order, so a short stage never loses its sound
				for ( var s = l.Hz.PrevStage + 1; s <= (int)stage; s++ )
					attack.Present( this, part, l.Ctx, (BossStage)s, t );
				l.Hz.PrevStage = (int)stage;
			}
			// a hand's fly-by woosh, a beat BEFORE its strike flies (the clip's wind builds up): it rides the hand
			if ( attack.UsesHand && !l.Hz.Wooshed && stage < BossStage.Rest && t >= attack.TellEnd( l.Ctx ) - BossHandTrail.Lead )
			{
				l.Hz.Wooshed = true;
				part.Components.Get<BossHandTrail>()?.Woosh();
				if ( attack.UsesBothHands ) BossNet.Find( OtherHand( l.Ctx.Part ) )?.Components.Get<BossHandTrail>()?.Woosh();
			}
			if ( stage != BossStage.Done ) attack.Judge( this, part, l.Ctx, t, l.Hz );
			l.Hz.PrevT = t;
			if ( stage == BossStage.Done ) _local.RemoveAt( i );
		}
	}

	/// <summary>`boss_debug 2`, host: each part's trail, its target and waypoint, the formation places and the keep-out sphere.</summary>
	internal void DrawMotion( DebugOverlaySystem overlay )
	{
		if ( !Networking.IsHost ) return;
		foreach ( var (kind, m) in _movers )
		{
			var c = kind == BossPartKind.Skull ? Color.Magenta : kind == BossPartKind.HandL ? Color.Green : Color.Yellow;
			for ( var i = 1; i < m.Trail.Count; i++ ) overlay.Line( m.Trail[i - 1].P, m.Trail[i].P, c );
			if ( _lastIntent.TryGetValue( kind, out var it ) )
			{
				overlay.Sphere( new Sphere( it.Target, 12f ), c );
				overlay.Line( m.Pos, it.HasWaypoint && !m.WaypointDone ? it.Waypoint : it.Target, c.WithAlpha( 0.4f ) );
				if ( it.HasWaypoint && !m.WaypointDone ) overlay.Sphere( new Sphere( it.Waypoint, it.WaypointRadius ), c.WithAlpha( 0.25f ) );
			}
			overlay.Text( m.Pos + Vector3.Up * 60f, $"{kind} {m.Speed:0} u/s", 12f, color: c );
		}
		overlay.Sphere( new Sphere( SkullCenter, KeepOut( BossPartKind.HandL ) ), Color.Red.WithAlpha( 0.15f ) );
		foreach ( var kind in new[] { BossPartKind.HandL, BossPartKind.HandR } ) overlay.Sphere( new Sphere( HandStance( kind ).Position, 30f ), Color.White.WithAlpha( 0.4f ) );
		overlay.Text( _stance + Vector3.Up * 120f, $"stance {(Busy( BossPartKind.Skull ) ? "held" : "guard")} moves {RunningNames}{(_queued is { } q ? $" queued {q.Attack.Name}" : "")}", 12f, color: Color.Cyan );
	}
}
