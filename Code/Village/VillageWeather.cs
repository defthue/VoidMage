using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Audio;
using VoidMage.Combat;
using VoidMage.Dungeon;

namespace VoidMage.Village;

/// <summary>
/// The village's night weather: rain, its splashes, a rain bed, and now and then a lightning flash with thunder
/// after it. Camera-following like DungeonHaze (the cost is constant whatever the village's size): a box emitter
/// rides above and a little ahead of the eye, its drops live in world space and fall straight through it, so a
/// drop born at the box's top reaches the ground on its own. The column is seeded at once on the first frame and
/// after any eye jump (spawn, return home), every drop already part-way down its fall, so coming home is raining,
/// not starting to rain. Lightning is the sun and the fog colour thrown up for a few frames, then put back;
/// thunder follows a second or more later. Sits on the scene camera object; RunManager enables it at home and
/// disables it on a floor. One per client, nothing networked.
///
/// Built around what the engine's particle system actually costs (decompiled 2026-09-15):
///  - a drop is ONE sprite: the ribbon the trail renderer drew (a strip along the drop's real path, turned about
///    that path to face the eye, tapering to the tail) as a particle-rotated quad whose angles the RainController
///    sets per drop per step (the effect's own rotation write is off), carrying a tall narrow streak texture (the
///    renderer scales a sprite's width by the texture's aspect), plus a second renderer drawing a small
///    camera-facing head dot from the same particles so a drop coming straight at the eye still reads. Each
///    strip faces from its own position, so turning the camera does not swivel the field (RotateToCamera would:
///    it yaws every sprite to the camera's heading). No ParticleTrailRenderer: that one allocates a native scene
///    object and two heap objects per particle BIRTH and rebuilds every trail every step, and rain is thousands
///    of births a second — it was the GC pause. No MotionBlur (up to 17 instances per sprite), no sorting (a GPU
///    bitonic sort per frame); additive rain needs neither.
///  - the ground is found with ONE ray per drop on the particle worker threads (Scene.Trace.Run is thread-safe,
///    the engine's own collision does the same), on the drop's first step, instead of the engine's Collision
///    feature (a physics sphere sweep per particle per step). The hit point lives in the particle's own
///    HitPos/HitNormal (unused by the engine while Collision is off); the drop is retired when it reaches it, and
///    the landing spot comes back on the main thread through OnParticleDestroyed to become a splash.
/// </summary>
[Title( "Village Weather" ), Category( "Void Mage" ), Icon( "thunderstorm" )]
public sealed class VillageWeather : Component
{
	/// <summary>Scales the drop count (live). 0 stops the rain.</summary>
	[ConVar( "rain_density" )] public static float DensityScale { get; set; } = 1f;
	/// <summary>Drops stop on roofs and the ground (one ray per drop at birth); off, they fall through and die by age.</summary>
	[ConVar( "rain_collide" )] public static bool Collide { get; set; } = true;
	[ConVar( "rain_lightning" )] public static bool LightningEnabled { get; set; } = true;
	/// <summary>Which way the streak quad's axes are handed to the sprite shader (its angle convention is not documented): 0 = face the eye, up along the projected fall; 1 = the two swapped; 2 = as 0 rolled a quarter turn. Change only if streaks draw crosswise.</summary>
	[ConVar( "rain_strip_axis" )] public static int StripAxis { get; set; } = 0;

	/// <summary>How far ahead of the eye the rain box sits (plus up to 150 more with movement speed).</summary>
	[Property] public float ForwardBias { get; set; } = 220f;

	/// <summary>The box drops are born in. At 480 lines a streak 700 u out is under a pixel, so the box is not wider than that; the density is what reads.</summary>
	[Property, Group( "Rain" )] public Vector3 RainVolume { get; set; } = new( 1200f, 1200f, 120f );
	/// <summary>Height of the box centre above the eye.</summary>
	[Property, Group( "Rain" )] public float RainHeight { get; set; } = 650f;
	/// <summary>Drops alive at density 1.</summary>
	[Property, Group( "Rain" )] public int RainCount { get; set; } = 1300;
	/// <summary>Fall speed, units per second.</summary>
	[Property, Group( "Rain" )] public float FallSpeed { get; set; } = 1700f;
	/// <summary>Seconds a drop lives if it hits nothing: at 1700 u/s, 0.9 s is a fall from the box to ~880 u below the eye.</summary>
	[Property, Group( "Rain" )] public float RainLife { get; set; } = 0.9f;
	/// <summary>The streak's length along the fall, in units. On screen it is this times the sine of the view angle to the fall: a drop coming straight at the eye is a dot the streak's width.</summary>
	[Property, Group( "Rain" )] public float StreakLength { get; set; } = 110f;
	/// <summary>The streak's width in units (the trail renderer's was 2.2 at the head).</summary>
	[Property, Group( "Rain" )] public float StreakWidth { get; set; } = 2.4f;
	[Property, Group( "Rain" )] public float RainAlpha { get; set; } = 0.45f;
	[Property, Group( "Rain" )] public Color RainColor { get; set; } = new( 0.75f, 0.85f, 1f );
	/// <summary>Sideways drift, units per second; its direction turns slowly.</summary>
	[Property, Group( "Rain" )] public float Wind { get; set; } = 220f;

	[Property, Group( "Splashes" )] public bool Splashes { get; set; } = true;
	/// <summary>Splashes alive at once; landings past this are dropped (and only landings within SplashRange of the eye are drawn).</summary>
	[Property, Group( "Splashes" )] public int SplashMax { get; set; } = 400;
	[Property, Group( "Splashes" )] public float SplashRange { get; set; } = 700f;
	[Property, Group( "Splashes" )] public float SplashSize { get; set; } = 3f;
	[Property, Group( "Splashes" )] public float SplashAlpha { get; set; } = 0.5f;

	[Property, Group( "Lightning" )] public bool Lightning { get; set; } = true;
	[Property, Group( "Lightning" )] public float FlashMinWait { get; set; } = 20f;
	[Property, Group( "Lightning" )] public float FlashMaxWait { get; set; } = 60f;
	/// <summary>The sun during a flash (times FlashIntensity).</summary>
	[Property, Group( "Lightning" )] public Color FlashColor { get; set; } = new( 0.85f, 0.9f, 1f );
	[Property, Group( "Lightning" )] public float FlashIntensity { get; set; } = 4f;
	/// <summary>The fog (and so the sky) during a flash: the whole village lit grey-blue for a frame or two.</summary>
	[Property, Group( "Lightning" )] public Color FlashFog { get; set; } = new( 0.35f, 0.4f, 0.55f );
	[Property, Group( "Lightning" )] public float ThunderMinDelay { get; set; } = 1.5f;
	[Property, Group( "Lightning" )] public float ThunderMaxDelay { get; set; } = 4f;

	/// <summary>The rain bed, 2D, looped by restarting when it runs out. Silent (one warning) until the clip exists.</summary>
	[Property, Group( "Audio" )] public string RainSound { get; set; } = "sounds/rain.sound";
	[Property, Group( "Audio" )] public float RainSoundVolume { get; set; } = 1f;
	[Property, Group( "Audio" )] public string ThunderSound { get; set; } = "sounds/thunder.sound";
	/// <summary>The one strike that breaks the storm when Play is clicked on the main menu: its own clip, not the village's thunder.</summary>
	[Property, Group( "Audio" )] public string MenuThunderSound { get; set; } = "sounds/menuthunder.sound";

	/// <summary>A tall narrow tapered streak (tools/gen_rain_streak.py, 6x256): the renderer folds its aspect into the sprite's width.</summary>
	private const string TexStreak = "vfx/rain_streak.png";
	private const string TexDot = "vfx/polygonparticles_soft_spot.png";
	/// <summary>An eye move larger than this in one frame is a teleport: the column is re-seeded there.</summary>
	private const float ReseedJump = 150f;
	/// <summary>What a drop passes through (it is weather, not a hit): the player and their view, characters, triggers, debris, the wardrobe stage.</summary>
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", "ragdoll", Debris.Tag, "wardrobe", "wardrobe_stage", "wardrobe_icon", "minimap" };
	/// <summary>A flash: (seconds, on) steps. Two strikes, the second longer, like a real bolt's return strokes.</summary>
	private static readonly (float seconds, bool on)[] FlashPattern = { (0.05f, true), (0.07f, false), (0.13f, true) };
	/// <summary>HitPos.z of a drop that found no ground: never reached.</summary>
	private const float NoFloor = -1e9f;

	/// <summary>What the drops need this frame. Plain data: written by OnUpdate, read by the threaded particle step.</summary>
	public sealed class RainState
	{
		public SceneTrace Trace;   // built on the main thread each frame (the tag filter is builder work); Ray().Run() from the workers is thread-safe
		public bool TraceValid;
		public bool Collide;
		public float Life;
		public Vector3 Eye;
		public int StripAxis;
		public float Width;
		public float Length;
	}

	/// <summary>
	/// Per drop, on the worker threads: on its first step one ray along its whole fall finds the ground (or a
	/// roof) and remembers it in the particle's own hit fields; every step after, a drop that has reached its
	/// floor is put on it and retired (the engine's own kill idiom). Every step the streak quad is faced at the
	/// eye and sized to what a line along the fall would cover on screen: its up is the fall direction with the
	/// view component removed, its length the streak length times the sine of the view angle, its width fixed.
	/// Seen from the side that is the trail renderer's ribbon; seen end-on it is a dot the streak's width, never
	/// a bar rolled across the screen (the effect's own Size and Angles writes are off; these are the only ones).
	/// Nothing is allocated.
	/// </summary>
	public sealed class RainController : ParticleController
	{
		public RainState State;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null ) return;

			if ( p.HitTime <= 0f )
			{
				p.HitTime = 1f;
				p.HitPos = new Vector3( 0f, 0f, NoFloor );
				p.HitNormal = Vector3.Up;
				if ( s.Collide && s.TraceValid )
				{
					var r = s.Trace.Ray( p.Position, p.Position + p.Velocity * s.Life ).Run();
					if ( r.Hit ) { p.HitPos = r.HitPosition; p.HitNormal = r.Normal; }
				}
			}

			if ( p.Position.z <= p.HitPos.z )
			{
				p.Position = p.HitPos;
				p.Age = float.MaxValue;
				return;
			}

			// the ribbon as the eye sees it: the quad faces the eye; its up is the fall direction projected onto
			// the view plane (head at the bottom); its length is the fall's projected extent
			var along = -p.Velocity;
			var len2 = along.LengthSquared;
			along = len2 > 0.0001f ? along / MathF.Sqrt( len2 ) : Vector3.Up;
			var toEye = s.Eye - p.Position;
			var d2 = toEye.LengthSquared;
			var view = d2 > 0.01f ? toEye / MathF.Sqrt( d2 ) : Vector3.Up;
			var up = along - view * Vector3.Dot( along, view );
			var sin = up.Length;
			up = sin > 0.02f ? up / sin : Vector3.Cross( view, Vector3.Forward ).Normal;
			var rot = s.StripAxis switch
			{
				1 => Rotation.LookAt( up, view ),
				2 => Rotation.LookAt( view, up ) * Rotation.FromRoll( 90f ),
				_ => Rotation.LookAt( view, up ),
			};
			p.Angles = rot.Angles();
			p.Size = new Vector3( s.Width, MathF.Max( s.Width, s.Length * sin ), 1f );
		}
	}

	private GameObject _root;
	private ParticleEffect _rainFx, _splashFx;
	private ParticleBoxEmitter _rainEmitter;
	private readonly RainState _state = new();
	private Vector3 _lastEye, _lastRoot;
	private bool _hasLastEye;
	private float _rainOwed;
	private float _rateSet;
	private float _windAngle;
	private Vector3 _velocity;
	private int _frame;

	private readonly object _lock = new();
	private Queue<Vector3> _landed = new();
	private Queue<Vector3> _landedSpare = new();

	private SoundHandle _rainSound;
	private float _nextRainStart;

	private float _nextFlash;
	private int _flashStep = -1;
	private float _flashStepEnds;
	private float _thunderAt;
	private DirectionalLight _sun;
	private Color _sunWas;
	private Rendering.RetroFilter _retro;
	private Color _fogWas;
	private CameraComponent _cameraWas;
	private Color _skyWas;

	protected override void OnEnabled()
	{
		if ( Scene.IsEditor ) return;
		Build();
		_nextFlash = Time.Now + Game.Random.Float( FlashMinWait * 0.5f, FlashMaxWait * 0.5f );
		_nextRainStart = 0f;
	}

	protected override void OnDisabled()
	{
		EndFlash();
		_thunderAt = 0f;
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
		_rainFx = _splashFx = null;
		_rainEmitter = null;
		_hasLastEye = false;
		lock ( _lock ) { _landed.Clear(); _landedSpare.Clear(); }
		_rainSound?.Stop( 1.5f );
		_rainSound = null;
	}

	// ------------------------------------------------------------------ build

	private void Build()
	{
		if ( _root.IsValid() ) return;
		_root = new GameObject( true, "village_weather" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden };

		// drops
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( _root, "rain", TexStreak, additive: true, bloom: false );
			// the streak is rotated and sized by the particle's own angles and size (set per drop by the
			// RainController): a camera-facing quad covering exactly what a line along the fall would on screen
			renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Particle;
			renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.Unsorted;
			renderer.MotionBlur = false;
			renderer.LeadingTrail = false;
			renderer.FaceVelocity = false;
			renderer.DepthFeather = 2f;
			// never a streak across the eye
			renderer.CameraFadeNear = 20f;
			renderer.CameraFadeFar = 60f;

			var em = go.Components.Create<ParticleBoxEmitter>();
			em.Size = RainVolume;
			em.OnEdge = false;
			em.Loop = true;
			em.Burst = 0f;
			em.Rate = _rateSet = RainRate;
			_rainEmitter = em;

			fx.MaxParticles = RainCap;
			fx.LocalSpace = 0f;
			fx.Damping = 0f;
			fx.Force = false;
			fx.StartVelocity = 0f;
			fx.InitialVelocity = _velocity = new Vector3( 0f, 0f, -FallSpeed );
			fx.Lifetime = RainLife;
			fx.Scale = StreakWidth;
			fx.Alpha = RainAlpha;
			fx.Gradient = FireVfxRig.RandomBetween( RainColor, Dim( RainColor, 0.7f ) );
			fx.Collision = false;
			// the effect would write every drop's angles (from its zero pitch/yaw/roll curves) and size (uniform,
			// from Scale) each step, on top of the controller's: the controller owns both
			fx.ApplyRotation = false;
			fx.ApplyShape = false;
			fx.OnParticleDestroyed = OnDropDied;
			_rainFx = fx;

			var steer = go.Components.Create<RainController>();
			steer.State = _state;
		}

		// splashes: a short-lived dot thrown up from the landing spot
		if ( Splashes )
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( _root, "splashes", TexDot, additive: true, bloom: false );
			renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.Unsorted;
			renderer.LeadingTrail = false;
			renderer.DepthFeather = 2f;
			renderer.CameraFadeNear = 12f;
			renderer.CameraFadeFar = 30f;

			fx.MaxParticles = Math.Max( 1, SplashMax + 8 );
			fx.LocalSpace = 0f;
			fx.Damping = 0f;
			fx.Force = true;
			fx.ForceDirection = Vector3.Down * 700f;
			fx.StartVelocity = new ParticleFloat( 10f, 30f );
			fx.Lifetime = new ParticleFloat( 0.18f, 0.3f );
			fx.Scale = FireVfxRig.OverLife( SplashSize, SplashSize * 0.3f );
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, SplashAlpha ), new Curve.Frame( 0.6f, SplashAlpha * 0.6f ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( RainColor, Color.White );
			_splashFx = fx;
		}
	}

	/// <summary>Particle callback (main thread, from the effect's post-step): a drop that reached its floor leaves a landing spot for a splash.</summary>
	private void OnDropDied( Particle p )
	{
		if ( p is null || !Splashes || p.HitTime <= 0f || p.HitPos.z <= NoFloor * 0.5f ) return;
		if ( p.Position.z > p.HitPos.z + 1f ) return;   // aged out in the air
		if ( _landed.Count >= SplashMax ) return;
		lock ( _lock ) _landed.Enqueue( p.HitPos + p.HitNormal * 1.5f );
	}

	// ------------------------------------------------------------------ the column

	/// <summary>
	/// The whole column at once: every drop placed where it would be part-way through its fall from the box,
	/// so the air is full of rain from the first frame instead of a sheet arriving from above.
	/// </summary>
	private void Reseed()
	{
		if ( !_rainFx.IsValid() ) return;
		_rainFx.Clear();
		var origin = _root.WorldPosition;
		var n = RainTarget;
		for ( var i = 0; i < n; i++ ) Drop( origin + Random.Shared.VectorInCube( 0.5f ) * RainVolume, Random.Shared.Float( 0f, 1f ) );
		_lastRoot = origin;
		_rainOwed = 0f;
	}

	/// <summary>A drop born in the box `life` of the way through its fall, emitted where it would be by now; its first step finds its floor like any other.</summary>
	private void Drop( Vector3 born, float life )
	{
		var seconds = life * RainLife;
		var p = _rainFx.Emit( born + _velocity * seconds, Random.Shared.Float( 0f, 1f ) );
		if ( p is null ) return;
		p.Velocity = _velocity;
		p.Age = seconds;
	}

	/// <summary>The box moved: the slice not covered last frame is filled to density at once (DungeonHaze's idea, by rejection).</summary>
	private void FillUncovered( Vector3 prev, Vector3 now )
	{
		if ( !_rainFx.IsValid() ) return;
		var volume = RainVolume;
		var total = volume.x * volume.y * volume.z;
		if ( total <= 0f ) return;
		var d = now - prev;
		var ax = MathF.Min( MathF.Abs( d.x ), volume.x );
		var ay = MathF.Min( MathF.Abs( d.y ), volume.y );
		var az = MathF.Min( MathF.Abs( d.z ), volume.z );
		var overlap = (volume.x - ax) * (volume.y - ay) * (volume.z - az);
		_rainOwed += RainTarget * (total - overlap) / total;
		var n = (int)_rainOwed;
		if ( n <= 0 ) return;
		_rainOwed -= n;
		if ( _rainFx.ParticleCount + n > _rainFx.MaxParticles ) return;

		var half = volume * 0.5f;
		var oldMin = prev - half; var oldMax = prev + half;
		for ( var i = 0; i < n; i++ )
		{
			Vector3 pos = default;
			var found = false;
			for ( var t = 0; t < 4 && !found; t++ )
			{
				pos = now + Random.Shared.VectorInCube( 0.5f ) * volume;
				found = pos.x < oldMin.x || pos.x > oldMax.x || pos.y < oldMin.y || pos.y > oldMax.y || pos.z < oldMin.z || pos.z > oldMax.z;
			}
			if ( found ) Drop( pos, Random.Shared.Float( 0f, 1f ) );
		}
	}

	/// <summary>Drops left far behind are retired so the cap stays for the rain around the eye. Walks the whole list, so every fourth frame.</summary>
	private void Cull( Vector3 eye )
	{
		if ( !_rainFx.IsValid() ) return;
		var distance = MathF.Max( RainVolume.x, RainVolume.y ) * 1.25f + ForwardBias + 150f;
		var d2 = distance * distance;
		foreach ( var p in _rainFx.Particles )
		{
			var flat = (p.Position - eye).WithZ( 0f );
			if ( flat.LengthSquared > d2 ) p.Age = float.MaxValue;
		}
	}

	private int RainTarget => (int)(RainCount * MathF.Max( 0f, DensityScale ));
	private int RainCap => Math.Max( 1, RainTarget * 2 + 8 );
	private float RainRate => RainTarget / MathF.Max( 0.1f, RainLife );

	private static Color Dim( Color c, float k ) => new( c.r * k, c.g * k, c.b * k, c.a );

	// ------------------------------------------------------------------ per frame

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor ) return;
		if ( !_root.IsValid() ) Build();
		var camera = Scene.Camera;
		if ( !camera.IsValid() || !_root.IsValid() ) return;
		_frame++;

		var eye = camera.WorldPosition;
		var jump = _hasLastEye ? (eye - _lastEye).Length : 0f;
		var speed = _hasLastEye && Time.Delta > 0f ? jump / Time.Delta : 0f;
		var reseed = !_hasLastEye || jump > ReseedJump;
		_lastEye = eye;
		_hasLastEye = true;

		// the wind: turns over a minute or so and breathes
		var now = Time.Now;
		_windAngle = now * 0.03f + MathF.Sin( now * 0.017f ) * 1.5f;
		var wind = new Vector3( MathF.Cos( _windAngle ), MathF.Sin( _windAngle ), 0f ) * (Wind * (0.8f + 0.3f * MathF.Sin( now * 0.09f )));
		_velocity = wind + Vector3.Down * FallSpeed;
		if ( _rainFx.IsValid() ) _rainFx.InitialVelocity = _velocity;

		// what the drops' first step needs: the tag filter is built here, on the main thread
		_state.Trace = Scene.Trace.WithoutTags( PassThrough );
		_state.TraceValid = true;
		_state.Collide = Collide;
		_state.Life = RainLife;
		_state.Eye = eye;
		_state.StripAxis = StripAxis;
		_state.Width = StreakWidth;
		_state.Length = StreakLength;

		// the box leads the view and sits upwind, so the rain that lands ahead of the eye was born inside it
		var forward = camera.WorldRotation.Forward.WithZ( 0f );
		forward = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
		var lead = ForwardBias + Math.Clamp( reseed ? 0f : speed * 0.4f, 0f, 150f );
		var fallSeconds = RainHeight / MathF.Max( 1f, FallSpeed );
		_root.WorldPosition = eye + forward * lead - wind * fallSeconds + Vector3.Up * RainHeight;
		if ( reseed ) Reseed();
		else
		{
			FillUncovered( _lastRoot, _root.WorldPosition );
			_lastRoot = _root.WorldPosition;
		}
		if ( (_frame & 3) == 0 ) Cull( eye );

		// live tuning (only on change: these are engine properties, not free to write)
		if ( _rainEmitter.IsValid() )
		{
			var rate = RainRate;
			if ( _rateSet != rate ) { _rainEmitter.Rate = rate; _rateSet = rate; }
			if ( _rainEmitter.Size != RainVolume ) _rainEmitter.Size = RainVolume;
		}
		if ( _rainFx.IsValid() )
		{
			var cap = RainCap;
			if ( _rainFx.MaxParticles != cap ) _rainFx.MaxParticles = cap;
		}

		DrainSplashes( eye );
		TickRainSound();
		TickLightning( now );
	}

	private void DrainSplashes( Vector3 eye )
	{
		// swap the queues under one lock, then work on the swapped-out one outside it
		Queue<Vector3> landed;
		lock ( _lock )
		{
			if ( _landed.Count == 0 ) return;
			landed = _landed;
			_landed = _landedSpare;
			_landedSpare = landed;
		}
		if ( !_splashFx.IsValid() ) { landed.Clear(); return; }
		var r2 = SplashRange * SplashRange;
		while ( landed.Count > 0 )
		{
			var at = landed.Dequeue();
			if ( _splashFx.IsFull ) continue;
			if ( (at - eye).LengthSquared > r2 ) continue;
			var p = _splashFx.Emit( at, Random.Shared.Float( 0f, 1f ) );
			if ( p is null ) continue;
			p.Velocity = Vector3.Up * Random.Shared.Float( 40f, 90f ) + Random.Shared.VectorInCube( 0.5f ).WithZ( 0f ) * 60f;
		}
	}

	// ------------------------------------------------------------------ audio

	private void TickRainSound()
	{
		if ( _rainSound is not null && _rainSound.IsValid() && _rainSound.IsPlaying ) return;
		if ( Time.Now < _nextRainStart ) return;
		// a missing clip is reported once by GameAudio; try again now and then in case it is dropped in while playing
		_nextRainStart = Time.Now + 10f;
		var h = GameAudio.Play2D( RainSound );
		if ( h is null || !h.IsValid() ) { _rainSound = null; return; }
		h.Volume *= RainSoundVolume;
		_rainSound = h;
	}

	// ------------------------------------------------------------------ lightning

	[ConCmd( "village_lightning_now" )]
	public static void LightningNow()
	{
		foreach ( var w in Game.ActiveScene?.GetAllComponents<VillageWeather>() ?? Enumerable.Empty<VillageWeather>() )
			if ( w.Enabled ) w._nextFlash = 0f;
	}

	/// <summary>
	/// A bolt right overhead, now: the flash and its thunder together (the main menu's Play, where the storm breaks
	/// on top of the player). Call it AFTER the sky has gone to night, because the flash puts back what it found.
	/// </summary>
	public void StrikeOverhead()
	{
		EndFlash();
		BeginFlash( Time.Now );
		// no wait for the sound to arrive: this one is on top of the listener, so it cracks with the light, full voice
		_thunderAt = 0f;
		// played AT the listener: the user's menuthunder event is a 3D one (UI off, distance attenuation on), and a
		// positionless play of a 3D event sits at the world origin, a thousand units from the campfire
		GameAudio.Play( MenuThunderSound, Sound.Listener.Position );
		_nextFlash = Time.Now + Game.Random.Float( 6f, 12f );
	}

	private void TickLightning( float now )
	{
		if ( _thunderAt > 0f && now >= _thunderAt )
		{
			_thunderAt = 0f;
			var h = GameAudio.Play2D( ThunderSound );
			if ( h is not null && h.IsValid() ) { h.Volume *= Game.Random.Float( 0.7f, 1f ); h.Pitch *= Game.Random.Float( 0.9f, 1.05f ); }
		}

		if ( _flashStep >= 0 )
		{
			if ( now < _flashStepEnds ) return;
			_flashStep++;
			if ( _flashStep >= FlashPattern.Length ) { EndFlash(); return; }
			SetFlash( FlashPattern[_flashStep].on );
			_flashStepEnds = now + FlashPattern[_flashStep].seconds;
			return;
		}

		if ( !Lightning || !LightningEnabled ) return;
		if ( now < _nextFlash ) return;
		_nextFlash = now + Game.Random.Float( FlashMinWait, FlashMaxWait );
		BeginFlash( now );
	}

	private void BeginFlash( float now )
	{
		_sun = Scene.GetAllComponents<DirectionalLight>().FirstOrDefault( l => l.IsValid() && l.Active );
		_retro = Scene.GetAllComponents<Rendering.RetroFilter>().FirstOrDefault();
		_cameraWas = Scene.Camera;
		if ( _sun.IsValid() ) _sunWas = _sun.LightColor;
		if ( _retro.IsValid() ) _fogWas = _retro.FogColor;
		if ( _cameraWas.IsValid() ) _skyWas = _cameraWas.BackgroundColor;
		_flashStep = 0;
		_flashStepEnds = now + FlashPattern[0].seconds;
		SetFlash( FlashPattern[0].on );
		_thunderAt = now + Game.Random.Float( ThunderMinDelay, ThunderMaxDelay );
	}

	private void SetFlash( bool on )
	{
		if ( _sun.IsValid() ) _sun.LightColor = on ? FlashColor * FlashIntensity : _sunWas;
		if ( _retro.IsValid() ) _retro.FogColor = on ? FlashFog : _fogWas;
		if ( _cameraWas.IsValid() ) _cameraWas.BackgroundColor = on ? FlashFog : _skyWas;
	}

	private void EndFlash()
	{
		if ( _flashStep < 0 ) return;
		SetFlash( false );
		_flashStep = -1;
		_sun = null;
		_retro = null;
		_cameraWas = null;
	}
}
