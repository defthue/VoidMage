using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// One physical piece of a broken prop: a board, a stave, a hoop or a shard that tumbles out of the break and lies
/// where it lands. heden's NodeDebris with the ore paths cut out and the tuning moved from rock to wood. Pieces are
/// client-local (nothing they do matters to anyone; the break itself was the host's call), built a few per frame
/// behind the dust, thrown with the blade's direction on top of a small radial burst, and evicted farthest from the
/// camera first when the scene cap is hit. heden's hard-won rules stay: the Rigidbody is created BEFORE the collider
/// (or every hull cooks twice), low bounce and high friction or it reads as styrofoam, a terminal fall speed, a
/// landing brake, spin in radians per second scaled down by size, one thud per piece with a scene-wide gap.
/// </summary>
public sealed class Debris : Component, Component.ICollisionListener
{
	public const string Tag = "debris";

	/// <summary>Hard ceiling on pieces in the scene; over it the farthest from the camera go first.</summary>
	[ConVar( "debris_max" )] public static int MaxDebris { get; set; } = 320;
	/// <summary>Past this from the camera a break plays its dust and sound but spawns no bodies.</summary>
	[ConVar( "debris_distance" )] public static float MaxDistance { get; set; } = 1500f;
	/// <summary>Most pieces one break throws (largest first; the slivers are the ones nobody misses).</summary>
	[ConVar( "debris_burst_max" )] public static int MaxPerBurst { get; set; } = 40;
	/// <summary>Most bodies built per frame; the rest wait a frame behind the dust.</summary>
	[ConVar( "debris_per_frame" )] public static int PerFrameBudget { get; set; } = 10;
	/// <summary>Milliseconds a frame may spend building bodies (one is always built, so the queue always empties); 0 = by count only.</summary>
	[ConVar( "debris_frame_ms" )] public static float FrameMilliseconds { get; set; } = 1.5f;
	/// <summary>Bodies built on the break's own frame (the largest come first), which already carries the sound, the dust and the loot.</summary>
	[ConVar( "debris_inline" )] public static int InlineBudget { get; set; } = 6;
	/// <summary>A piece the cap evicts farther than this from the camera, or off to the side of the view, just goes: the puff and the shrink are for pieces somebody could see.</summary>
	[ConVar( "debris_puff_distance" )] public static float PuffDistance { get; set; } = 700f;
	/// <summary>Milliseconds a frame may spend loading the floor's piece models ahead of the first break; 0 = no prewarm (they load on the break's frame).</summary>
	[ConVar( "debris_prewarm_ms" )] public static float PrewarmMilliseconds { get; set; } = 2f;
	/// <summary>Seconds a piece lies around before it shrinks away; 0 = it stays for the run (the cap still evicts).</summary>
	[ConVar( "debris_life" )] public static float LifeSeconds { get; set; } = 0f;
	/// <summary>Terminal fall speed (u/s): a board arriving faster than its own size per step is a deep-penetration solve.</summary>
	[ConVar( "debris_maxfall" )] public static float MaxFallSpeed { get; set; } = 420f;
	/// <summary>Bounciness. Wood may hop a little; rock never does.</summary>
	[ConVar( "debris_bounce" )] public static float Bounce { get; set; } = 0.08f;
	[ConVar( "debris_friction" )] public static float Friction { get; set; } = 0.8f;
	/// <summary>Radial burst speed out of the prop's centre, scaled by mass so a board and a block read alike.</summary>
	[ConVar( "debris_burst" )] public static float BurstSpeed { get; set; } = 70f;
	/// <summary>How much of the burst is up rather than out.</summary>
	[ConVar( "debris_lift" )] public static float BurstUpBias { get; set; } = 0.35f;
	/// <summary>Velocity every piece carries in the blade's direction, on top of the burst: what makes the crate fly AWAY from the swing.</summary>
	[ConVar( "debris_push" )] public static float PushSpeed { get; set; } = 170f;
	[ConVar( "debris_push_heavy" )] public static float HeavyPushScale { get; set; } = 1.4f;
	/// <summary>Spin on spawn for a piece of SpinReferenceRadius, RADIANS per second (Rigidbody.AngularVelocity is radians).</summary>
	[ConVar( "debris_spin" )] public static float SpinSpeed { get; set; } = 2.5f;
	[ConVar( "debris_spin_pivot" )] public static float SpinReferenceRadius { get; set; } = 12f;
	[ConVar( "debris_damping" )] public static float BodyAngularDamping { get; set; } = 0.5f;
	/// <summary>Speed under which a body stops simulating: well above the default so a landed board commits to lying still.</summary>
	[ConVar( "debris_sleep" )] public static float BodySleepThreshold { get; set; } = 3f;
	/// <summary>Linear damping slammed on at the first ground contact after a real fall, so pieces stop instead of skating.</summary>
	[ConVar( "debris_brake" )] public static float LandingBrake { get; set; } = 1.5f;
	[ConVar( "debris_brake_spin" )] public static float LandingBrakeSpin { get; set; } = 4f;
	/// <summary>Closing speed a landing needs to make a sound.</summary>
	[ConVar( "debris_thud_speed" )] public static float MinImpactSpeed { get; set; } = 90f;
	/// <summary>Shortest gap between two thuds anywhere in the scene.</summary>
	[ConVar( "debris_thud_gap" )] public static float ImpactSoundGap { get; set; } = 0.09f;
	/// <summary>Most thuds in the air at once (a thud clip is about half a second): the rest of a landing pile is the smash's own tail. Keeps a break from eating voice slots.</summary>
	[ConVar( "debris_thud_max" )] public static int MaxLiveThuds { get; set; } = 4;
	[ConVar( "debris_debug" )] public static bool DebugLogging { get; set; } = false;

	/// <summary>Pieces younger than this at first contact keep their spawn tuning (they were touching the floor from the start).</summary>
	private const float BrakeMinAirSeconds = 0.3f;
	private const float ShrinkSeconds = 0.4f;
	/// <summary>Mass per cubic unit of bounding box: the vmdls declare none and a weightless body is stiff, not floaty.</summary>
	private const float Density = 0.0014f;

	private static readonly List<Debris> _live = new();
	private static readonly List<Pending> _pending = new();
	private static readonly Dictionary<string, Model> _models = new();
	private static readonly Queue<string> _prewarm = new();
	private static readonly HashSet<string> _prewarmQueued = new();
	private static readonly List<(float Dist, Debris Piece)> _byDistance = new();
	private static int _coldLoads;
	private static double _coldMs;
	private static float _nextThud;
	private static readonly List<SoundHandle> _thuds = new();
	private static bool _warnedNoPhysics;

	/// <summary>One piece waiting to be built: nothing is allocated in the physics engine until its turn comes.</summary>
	private sealed class Pending
	{
		public Model Model;
		public Transform At;
		public GameObject Parent;
		public Vector3 Origin;
		public Vector3 Push;
		public string Thud;
		/// <summary>Collide as a box round the model's bounds: a static prop's mesh collision cannot be a dynamic body.</summary>
		public bool Box;
	}

	/// <summary>
	/// Something that stood on a prop that just broke (a candle, a book, a jar): it becomes a piece of its own and
	/// tumbles off. The prop's own object is destroyed (its light with it: a candle goes out when the table goes).
	/// </summary>
	public static void Fling( GameObject rider, Vector3 origin, Vector3 push, string thud, GameObject parent )
	{
		if ( !rider.IsValid() ) return;
		var renderer = rider.Components.Get<ModelRenderer>();
		var model = renderer?.Model;
		if ( model is not null && !model.IsError && parent.IsValid() )
		{
			_pending.Add( new Pending
			{
				Model = model, At = rider.WorldTransform, Parent = parent, Origin = origin,
				Push = push.IsNearZeroLength ? Vector3.Zero : push.Normal * PushSpeed * 0.6f, Thud = thud, Box = true
			} );
		}
		rider.Destroy();
	}

	private ModelRenderer _renderer;
	private Rigidbody _body;
	private Collider _collider;
	private Vector3 _centerLocal;
	private Vector3 _spawnScale = Vector3.One;
	private string _thud;
	private float _spawnTime;
	private float _dieAt = float.PositiveInfinity;
	private bool _thudded;
	private bool _shrinking;
	private float _shrinkStart;
	private Vector3 _pinned;

	public static int LiveCount => _live.Count;
	public static int PendingCount => _pending.Count;

	public static Model LoadModel( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( !_models.TryGetValue( path, out var m ) )
		{
			var timer = Sandbox.Diagnostics.FastTimer.StartNew();
			m = Model.Load( path );
			_models[path] = m;
			_coldLoads++;
			_coldMs += timer.ElapsedMilliSeconds;
		}
		return m;
	}

	/// <summary>
	/// A kit stands on this floor: its piece models are queued to load a few a frame (<see cref="PrewarmTick"/>).
	/// Model.Load blocks on the disk and the upload, and Burst loads every piece of a kit before it queues one, so
	/// the first break of each kind of prop paid for some twenty-five loads on the very frame of the blow.
	/// </summary>
	public static void Prewarm( BreakableKits.Kit kit )
	{
		if ( kit is null ) return;
		foreach ( var piece in kit.InPlace ) QueuePrewarm( piece.Model );
		foreach ( var piece in kit.Loose ) QueuePrewarm( piece.Model );
		QueuePrewarm( kit.Remnant );
	}

	private static void QueuePrewarm( string path )
	{
		if ( string.IsNullOrEmpty( path ) || _models.ContainsKey( path ) || !_prewarmQueued.Add( path ) ) return;
		_prewarm.Enqueue( path );
	}

	/// <summary>The previous floor's unloaded pieces are not worth loading any more (another biome has other kits).</summary>
	internal static void ClearPrewarm()
	{
		_prewarm.Clear();
		_prewarmQueued.Clear();
	}

	/// <summary>Load queued piece models for <see cref="PrewarmMilliseconds"/>; never on a frame that is building bodies.</summary>
	internal static void PrewarmTick()
	{
		if ( _prewarm.Count == 0 || _pending.Count > 0 || PrewarmMilliseconds <= 0f ) return;
		var timer = Sandbox.Diagnostics.FastTimer.StartNew();
		var loaded = 0;
		do
		{
			var path = _prewarm.Dequeue();
			_prewarmQueued.Remove( path );
			// the physics description too: Usable reads it on the break's frame
			_ = LoadModel( path )?.Physics?.Parts.Count;
			loaded++;
		}
		while ( _prewarm.Count > 0 && timer.ElapsedMilliSeconds < PrewarmMilliseconds );
		if ( DebugLogging )
			Log.Info( $"[Debris] prewarm loaded={loaded} in {timer.ElapsedMilliSeconds:0.00} ms, {_prewarm.Count} to go" );
	}

	private static bool Usable( Model model )
	{
		if ( model is null || model.IsError ) return false;
		var physics = model.Physics;
		if ( physics is null || physics.Parts.Count == 0 )
		{
			if ( !_warnedNoPhysics )
			{
				_warnedNoPhysics = true;
				Log.Warning( $"[Debris] {model.Name} has no physics parts: it cannot be a dynamic body. Its vmdl needs PhysicsHullFromRender." );
			}
			return false;
		}
		return true;
	}

	/// <summary>
	/// Break a prop into its pieces. `prop` is the prop's transform (the in-place pieces are authored at its origin),
	/// `bounds` its world box (the loose pieces are scattered inside it, the burst radiates from its centre), `push`
	/// the world direction the blow travelled (unit, flat).
	/// </summary>
	public static void Burst( Scene scene, Transform prop, BBox bounds, BreakableKits.Kit kit, Vector3 push, bool heavy, string thud, GameObject parent )
	{
		if ( scene is null || kit is null ) return;
		var camera = scene.Camera;
		if ( camera.IsValid() && camera.WorldPosition.Distance( bounds.Center ) > MaxDistance ) return;

		var pushVel = push.IsNearZeroLength ? Vector3.Zero : push.Normal * PushSpeed * (heavy ? HeavyPushScale : 1f);
		var origin = bounds.Center;
		// the whole burst always fits: past the scene cap Drain evicts the pieces FARTHEST from the camera, and this
		// break is the nearest thing there is. (Sizing the burst to the room left in the cap starved bookcases of their
		// own boards once the run had a few hundred pieces lying about.)
		var budget = MaxPerBurst;
		var queued = 0;
		var coldLoads = _coldLoads;
		var coldMs = _coldMs;

		var size = bounds.Size;

		void Loose( BreakableKits.Piece piece )
		{
			var model = LoadModel( piece.Model );
			if ( !Usable( model ) ) return;
			var at = bounds.Mins + new Vector3( size.x * Game.Random.Float( 0.2f, 0.8f ), size.y * Game.Random.Float( 0.2f, 0.8f ), size.z * Game.Random.Float( 0.35f, 0.85f ) );
			var rot = Rotation.Random;
			var t = prop;
			t.Rotation = rot;
			t.Scale = prop.Scale * kit.LooseScale;
			// the loose models pivot wherever the pack put it: place them by their visible centre
			t.Position = at - rot * (model.Bounds.Center * t.Scale);
			_pending.Add( new Pending { Model = model, At = t, Parent = parent, Origin = origin, Push = pushVel, Thud = thud } );
			queued++;
		}

		// rings are rationed: a barrel throws RingMin..RingMax of its hoops and ring bits, wherever they come from
		// (a hoop that is not thrown simply vanishes under the dust)
		var rings = new List<(BreakableKits.Piece piece, bool inPlace)>();
		foreach ( var piece in kit.InPlace ) if ( piece.IsRing ) rings.Add( (piece, true) );
		foreach ( var piece in kit.Loose ) if ( piece.IsRing ) rings.Add( (piece, false) );
		var ringCount = rings.Count == 0 ? 0 : Math.Min( rings.Count, Game.Random.Int( kit.RingMin, kit.RingMax ) );
		for ( var i = 0; i < ringCount; i++ )
		{
			var j = Game.Random.Int( i, rings.Count - 1 );
			(rings[i], rings[j]) = (rings[j], rings[i]);
		}

		// the prop's own parts first, largest first (the tool sorted them): they ARE the prop coming apart, so they are
		// never the ones a budget cuts; whatever gets cut is a sliver or an extra
		foreach ( var piece in kit.InPlace )
		{
			if ( queued >= budget ) break;
			if ( piece.IsRing ) continue;
			var model = LoadModel( piece.Model );
			if ( !Usable( model ) ) continue;
			_pending.Add( new Pending { Model = model, At = prop, Parent = parent, Origin = origin, Push = pushVel, Thud = thud } );
			queued++;
		}
		for ( var i = 0; i < ringCount && queued < budget; i++ )
		{
			var (piece, inPlace) = rings[i];
			if ( !inPlace ) { Loose( piece ); continue; }
			var model = LoadModel( piece.Model );
			if ( !Usable( model ) ) continue;
			_pending.Add( new Pending { Model = model, At = prop, Parent = parent, Origin = origin, Push = pushVel, Thud = thud } );
			queued++;
		}
		// then the loose extras: bent staves, shards, planks, books
		var loosePool = new List<BreakableKits.Piece>();
		foreach ( var piece in kit.Loose ) if ( !piece.IsRing ) loosePool.Add( piece );
		var looseCount = loosePool.Count == 0 ? 0 : Game.Random.Int( kit.LooseMin, kit.LooseMax );
		for ( var i = 0; i < looseCount && queued < budget; i++ )
			Loose( loosePool[Game.Random.Int( 0, loosePool.Count - 1 )] );

		if ( DebugLogging )
			Log.Info( $"[Debris] burst queued={queued} cold-loads={_coldLoads - coldLoads} ({_coldMs - coldMs:0.00} ms) push={pushVel} pending={_pending.Count} live={_live.Count}/{MaxDebris}" );
		Drain( InlineBudget );
	}

	/// <summary>
	/// Build queued pieces, up to `budget` of them or until <see cref="FrameMilliseconds"/> is spent: once per frame
	/// from <see cref="DebrisSystem"/>, once inline from Burst.
	/// </summary>
	internal static int Drain( int budget )
	{
		if ( _pending.Count == 0 || budget <= 0 ) return 0;
		var timer = Sandbox.Diagnostics.FastTimer.StartNew();
		var evicted = MakeRoom( Math.Min( budget, _pending.Count ) );
		var built = 0;
		while ( built < budget && _pending.Count > 0 )
		{
			var next = _pending[0];
			_pending.RemoveAt( 0 );
			Build( next );
			built++;
			if ( FrameMilliseconds > 0f && timer.ElapsedMilliSeconds >= FrameMilliseconds ) break;
		}
		if ( DebugLogging )
			Log.Info( $"[Debris] drain built={built} evicted={evicted} in {timer.ElapsedMilliSeconds:0.00} ms pending={_pending.Count} live={_live.Count}/{MaxDebris}" );
		return built;
	}

	/// <summary>
	/// Room under the scene cap for `want` new pieces: the farthest from the camera go, measured and sorted ONCE (it
	/// was a scan of the whole list per evicted piece, and a crate at the cap evicts thirty). Retire destroys via
	/// GameObject.Destroy, which is DEFERRED, so the list is rebuilt here and never waited on: a loop conditioned on
	/// _live.Count while retirement never shrinks it would be the hard freeze.
	/// </summary>
	private static int MakeRoom( int want )
	{
		var over = Math.Min( _live.Count + want - MaxDebris, _live.Count );
		if ( over <= 0 ) return 0;
		var camera = Game.ActiveScene?.Camera;
		var eye = camera.IsValid() ? camera.WorldPosition : Vector3.Zero;
		var forward = camera.IsValid() ? camera.WorldRotation.Forward : Vector3.Forward;
		_byDistance.Clear();
		foreach ( var piece in _live )
			_byDistance.Add( (piece.IsValid() ? piece.WorldPosition.DistanceSquared( eye ) : float.MaxValue, piece) );
		_byDistance.Sort( static ( a, b ) => b.Dist.CompareTo( a.Dist ) );
		_live.Clear();
		var far = PuffDistance * PuffDistance;
		for ( var i = 0; i < _byDistance.Count; i++ )
		{
			var (dist, piece) = _byDistance[i];
			if ( i >= over ) { _live.Add( piece ); continue; }
			if ( !piece.IsValid() ) continue;
			// the puff and the shrink cost three objects and seven components a piece: only where they can be seen
			// (0.35 = 70 degrees off the view's axis, outside any field of view the game uses)
			var quiet = dist > far || Vector3.Dot( (piece.WorldPosition - eye).Normal, forward ) < 0.35f;
			piece.Retire( quiet );
		}
		_byDistance.Clear();
		return over;
	}

	private static void Build( Pending p )
	{
		if ( !p.Parent.IsValid() ) return;
		// Created DISABLED: Rigidbody and the collider build their physics in OnEnabled, and the model and the tag have
		// to be in place before that runs.
		var go = new GameObject( false, "debris" ) { WorldTransform = p.At };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.SetParent( p.Parent, true );
		go.Tags.Add( Tag );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = p.Model;

		// The Rigidbody MUST be created before the collider: Collider.ChangeBody finds no body, makes a keyframe one
		// and cooks the hull, then the Rigidbody enables and it is all done again (heden measured 34 builds a boulder
		// instead of 17, on one frame).
		var body = go.Components.Create<Rigidbody>();
		body.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
		body.EnableImpactDamage = false;
		body.AngularDamping = BodyAngularDamping;
		body.GravityScale = 1f;
		body.SleepThreshold = BodySleepThreshold;
		// contact reporting marshals every contact into managed code for the piece's whole life: only for the thud,
		// and switched off again the moment it has played
		body.CollisionEventsEnabled = p.Thud is not null;
		body.StartAsleep = false;

		Collider collider;
		if ( p.Box )
		{
			var box = go.Components.Create<BoxCollider>();
			box.Center = p.Model.Bounds.Center;
			box.Scale = p.Model.Bounds.Size;
			collider = box;
		}
		else
		{
			var mesh = go.Components.Create<ModelCollider>();
			mesh.Model = p.Model;
			collider = mesh;
		}
		collider.Static = false;
		collider.Elasticity = Bounce;
		collider.Friction = Friction;

		var debris = go.Components.Create<Debris>();
		debris._renderer = renderer;
		debris._body = body;
		debris._collider = collider;
		debris._centerLocal = p.Model.Bounds.Center;
		debris._spawnScale = p.At.Scale;
		debris._thud = p.Thud;

		go.Enabled = true;

		debris.EnsureMass();
		debris.Launch( p.Origin, p.Push );
		debris._spawnTime = Time.Now;
		if ( LifeSeconds > 0f ) debris._dieAt = Time.Now + LifeSeconds + Game.Random.Float( 0f, 0.8f );
		_live.Add( debris );
	}

	/// <summary>Theatre mass by bounding volume, clamped into one class: a board still outweighs a block, nothing weighs a tonne.</summary>
	private void EnsureMass()
	{
		if ( !_body.IsValid() ) return;
		var size = _renderer.IsValid() && _renderer.Model is not null ? _renderer.Model.Bounds.Size * _spawnScale : Vector3.One * 10f;
		var volume = MathF.Max( size.x * size.y * size.z, 1f );
		_body.MassOverride = (volume * Density).Clamp( 1f, 40f );
	}

	/// <summary>
	/// Throw the piece: outward from the prop's centre, lifted, plus the blow's own direction for every piece so the
	/// whole prop leaves as a wave away from the swing. Applied at the visible centre (the object origin is the prop's
	/// pivot, nowhere near a top board) so the impulse gives sensible torque; higher pieces fly a touch farther.
	/// </summary>
	private void Launch( Vector3 origin, Vector3 push )
	{
		if ( !_body.IsValid() ) return;
		var center = VisualCenter();
		var away = (center - origin).WithZ( 0f );
		if ( away.IsNearZeroLength ) away = Vector3.Random.WithZ( 0f );
		if ( away.IsNearZeroLength ) away = Vector3.Forward;
		var dir = (away.Normal + Vector3.Up * BurstUpBias).Normal;
		var mass = _body.Mass;
		if ( mass <= 0f ) mass = 5f;
		var lift = 1f + ((center.z - origin.z) / 60f).Clamp( 0f, 0.6f );
		_body.ApplyImpulseAt( center, (dir * BurstSpeed + push) * mass * lift );

		// spin scaled DOWN by size (a shove turns a shard fast and a lid slowly), never flat: flat spin is confetti
		var model = _renderer.IsValid() ? _renderer.Model : null;
		var radius = model is not null ? (model.Bounds.Size * _spawnScale).Length * 0.5f : SpinReferenceRadius;
		var inertia = (SpinReferenceRadius / MathF.Max( radius, 1f )).Clamp( 0.25f, 1.5f );
		_body.AngularVelocity = Vector3.Random.Normal * SpinSpeed * inertia * Game.Random.Float( 0.5f, 1f );
	}

	private Vector3 VisualCenter()
	{
		if ( _renderer.IsValid() ) return _renderer.Bounds.Center;
		return WorldTransform.PointToWorld( _centerLocal );
	}

	/// <summary>Scale about the visible centre, not the far-away origin (a shrinking board must not slide across the room).</summary>
	private void PlaceCenter( Vector3 worldCenter, float scale )
	{
		var rot = WorldRotation;
		WorldScale = _spawnScale * scale;
		WorldPosition = worldCenter - rot * (_centerLocal * _spawnScale * scale);
	}

	protected override void OnUpdate()
	{
		if ( _shrinking )
		{
			var t = ((Time.Now - _shrinkStart) / ShrinkSeconds).Clamp( 0f, 1f );
			if ( t >= 1f ) { VoidMage.Dev.WorldTape.Retire( GameObject, () => _live.Remove( this ) ); return; }
			PlaceCenter( _pinned, 1f - t * t );
			return;
		}

		if ( _body.IsValid() && !_body.Sleeping )
		{
			var v = _body.Velocity;
			if ( v.z < -MaxFallSpeed ) _body.Velocity = v.WithZ( -MaxFallSpeed );
		}

		if ( Time.Now >= _dieAt ) Retire();
	}

	/// <summary>Cut short (the cap, or its time): a puff and a shrink rather than a blink, unless nobody can see it (`quiet`).</summary>
	public void Retire( bool quiet = false )
	{
		if ( !GameObject.IsValid() || _shrinking ) return;
		if ( quiet ) { VoidMage.Dev.WorldTape.Retire( GameObject, () => _live.Remove( this ) ); return; }
		_shrinking = true;
		_shrinkStart = Time.Now;
		_pinned = VisualCenter();
		if ( _body.IsValid() ) _body.MotionEnabled = false;
		if ( _collider.IsValid() ) _collider.Enabled = false;
		BreakVfx.Puff( Scene, _pinned, 10f );
	}

	/// <summary>
	/// The thud when a piece first lands: the FIRST contact ends event reporting loud or not (a board settling on a
	/// pile would otherwise keep marshalling contacts for its whole life), only above a closing speed, and only one
	/// thud in the scene per <see cref="ImpactSoundGap"/> so twenty boards read as a crate coming apart, not as
	/// twenty copies of one sample. The brake is only for pieces that actually fell here.
	/// </summary>
	public void OnCollisionStart( Collision collision )
	{
		if ( _thudded ) return;
		_thudded = true;
		if ( _body.IsValid() )
		{
			_body.CollisionEventsEnabled = false;
			if ( LandingBrake > 0f && Time.Now - _spawnTime >= BrakeMinAirSeconds )
			{
				_body.LinearDamping = LandingBrake;
				_body.AngularDamping = LandingBrakeSpin;
			}
		}
		if ( _thud is null || _shrinking ) return;
		if ( collision.Contact.NormalSpeed < MinImpactSpeed ) return;
		if ( Time.Now < _nextThud ) return;
		for ( var i = _thuds.Count - 1; i >= 0; i-- )
			if ( _thuds[i] is null || !_thuds[i].IsValid() || !_thuds[i].IsPlaying ) _thuds.RemoveAt( i );
		if ( _thuds.Count >= Math.Max( 1, MaxLiveThuds ) ) return;
		_nextThud = Time.Now + ImpactSoundGap;
		var h = VoidMage.Audio.GameAudio.Play( _thud, collision.Contact.Point );
		if ( h is not null && h.IsValid() ) _thuds.Add( h );
	}

	public void OnCollisionUpdate( Collision collision ) { }

	public void OnCollisionStop( CollisionStop collision ) { }

	protected override void OnDestroy()
	{
		_live.Remove( this );
	}
}

/// <summary>Drains the debris spawn queue a few pieces per frame and runs the breakables' delayed chain collapses.</summary>
public sealed class DebrisSystem : GameObjectSystem
{
	public DebrisSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Tick, "Debris" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		Debris.Drain( Debris.PerFrameBudget );
		Debris.PrewarmTick();
		BreakableNet.Tick();
	}
}
