using System;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// The dungeon's stale air (the Fell &amp; Sell touch): dust motes drifting through every room and a few
/// huge, barely-there haze wisps. Camera-following, so the cost is constant whatever the floor: a box
/// emitter rides just ahead of the eye and its particles live in world space, so they stay where they
/// were born while the box moves on. Whatever slice of the box a move uncovers (a step, or a turn
/// swinging the lead) is filled to the layer's density the same frame with motes already part-way
/// through life, so a sprint runs through dust instead of ahead of it; motes left more than a box
/// behind are retired. Every emitter-born mote is born at zero alpha and fades in over the first fifth
/// of its life, which is what keeps the box's edge from ever showing; the box leads the view a little
/// (more while sprinting) so a room is seeded before it is looked at. The full population is seeded
/// around the eye at random points of their lives on the first frame and again whenever the eye jumps
/// (player spawn, respawn), so a room is dusty at once instead of filling over ten seconds.
///
/// Motion is per particle (MoteController, threaded like FireVfxRig.FlameController): each mote eases
/// its velocity toward its own slow sine wander plus a shared draft that turns over tens of seconds and
/// a small sink. No shared force, so the crowd never marches. Sprites are FireVfxRig's layer helpers.
/// At the retro filter's 480 lines with Bayer dither a faint mote quantises to nothing, so motes are
/// small but fairly opaque and additive; the wisps rely on the dither to become the sparse "musty"
/// pattern. Sits on the scene camera object, one per client, nothing networked.
/// </summary>
[Title( "Dungeon Haze" ), Category( "Void Mage" ), Icon( "blur_on" )]
public sealed class DungeonHaze : Component
{
	/// <summary>Scales both layers' counts (live).</summary>
	[Property] public float Density { get; set; } = 1f;
	/// <summary>How far ahead of the eye the emitters sit (plus up to 150 more with movement speed).</summary>
	[Property] public float ForwardBias { get; set; } = 120f;

	[Property, Group( "Motes" )] public Vector3 MoteVolume { get; set; } = new( 480f, 480f, 80f );
	[Property, Group( "Motes" )] public int MoteCount { get; set; } = 160;
	[Property, Group( "Motes" )] public float MoteLifeMin { get; set; } = 6f;
	[Property, Group( "Motes" )] public float MoteLifeMax { get; set; } = 10f;
	/// <summary>Sprite size in units; under ~1.5 a mote drops below a 480-line pixel a few hundred units out.</summary>
	[Property, Group( "Motes" )] public float MoteSizeMin { get; set; } = 1.5f;
	[Property, Group( "Motes" )] public float MoteSizeMax { get; set; } = 3f;
	[Property, Group( "Motes" )] public float MoteAlpha { get; set; } = 0.45f;
	[Property, Group( "Motes" )] public Color MoteColor { get; set; } = new( 0.95f, 0.88f, 0.7f );
	/// <summary>Own wander speed (units/s).</summary>
	[Property, Group( "Motes" )] public float MoteWander { get; set; } = 4f;
	/// <summary>Settling speed (units/s).</summary>
	[Property, Group( "Motes" )] public float MoteSink { get; set; } = 0.5f;
	/// <summary>Lit by the torches (dark in dark corners) instead of self-lit.</summary>
	[Property, Group( "Motes" )] public bool MoteLit { get; set; }

	[Property, Group( "Wisps" )] public bool Wisps { get; set; } = true;
	/// <summary>Wide on purpose: a wisp is born at the far edge, so the edge must be well past the card's own radius plus the near fade (~670 u ahead at 1000).</summary>
	[Property, Group( "Wisps" )] public Vector3 WispVolume { get; set; } = new( 1000f, 1000f, 60f );
	[Property, Group( "Wisps" )] public int WispCount { get; set; } = 36;
	[Property, Group( "Wisps" )] public float WispLifeMin { get; set; } = 14f;
	[Property, Group( "Wisps" )] public float WispLifeMax { get; set; } = 20f;
	[Property, Group( "Wisps" )] public float WispSizeMin { get; set; } = 160f;
	[Property, Group( "Wisps" )] public float WispSizeMax { get; set; } = 260f;
	[Property, Group( "Wisps" )] public float WispAlpha { get; set; } = 0.07f;
	[Property, Group( "Wisps" )] public Color WispColor { get; set; } = new( 0.5f, 0.47f, 0.42f );
	[Property, Group( "Wisps" )] public float WispDrift { get; set; } = 2.5f;

	private const string TexMote = "vfx/polygonparticles_soft_spot.png";
	private const string TexWisp = "vfx/polygonparticles_fumes_02.png";
	/// <summary>An eye move larger than this in one frame is a teleport (spawn, respawn): the air is re-seeded there. A sprint at a 10 fps hitch is ~40 u.</summary>
	private const float ReseedJump = 150f;
	/// <summary>Particle cap as a multiple of a layer's count: room for the trail a run leaves behind (culled by distance).</summary>
	private const int TrailCapacity = 6;

	/// <summary>What the air is doing this frame. Plain data: written by OnUpdate, read by the threaded particle step.</summary>
	public sealed class HazeState
	{
		public float Time;
		public Vector3 Draft;    // shared slow current, units/s
		public float Wander;     // own wander speed, units/s
		public float Sink;       // settling speed, units/s
		public float Ease = 1.5f; // 1/s, how fast velocity follows its target
	}

	/// <summary>Per-particle drift: velocity eases toward the particle's own slow sine wander plus the shared draft.</summary>
	public sealed class MoteController : ParticleController
	{
		public HazeState State;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null || delta <= 0f ) return;

			float r1 = FireVfxRig.Hash( p.Random01 ), r2 = FireVfxRig.Hash( p.Random02 + 1f ), r3 = FireVfxRig.Hash( p.Random03 + 2f );
			float t = s.Time;
			// periods of roughly 3 to 12 seconds: a mote turns lazily, never twitches
			float f1 = 0.5f + 1.5f * r1, f2 = 0.4f + 1.4f * r2, f3 = 0.45f + 1.2f * r3;
			float nx = MathF.Sin( t * f1 + r2 * 31f ) + 0.5f * MathF.Sin( t * f3 * 1.7f + r1 * 17f );
			float ny = MathF.Sin( t * f2 + r3 * 23f ) + 0.5f * MathF.Sin( t * f1 * 1.3f + r3 * 29f );
			float nz = 0.5f * MathF.Sin( t * f3 + r1 * 13f );
			var target = new Vector3( nx, ny, nz ) * (s.Wander * (0.5f + r1) / 1.5f) + s.Draft;
			target.z -= s.Sink;

			var v = p.Velocity;
			v += (target - v) * MathF.Min( 1f, delta * s.Ease );
			p.Velocity = v;
		}
	}

	private GameObject _root;
	private ParticleEffect _moteFx, _wispFx;
	private ParticleBoxEmitter _moteEmitter, _wispEmitter;
	private readonly HazeState _moteState = new();
	private readonly HazeState _wispState = new();
	private Vector3 _lastEye;
	private bool _hasLastEye;
	private Vector3 _lastRoot;
	private float _moteOwed, _wispOwed;
	private float _draftAngle;

	protected override void OnEnabled()
	{
		if ( Scene.IsEditor ) return;
		Build();
	}

	protected override void OnDisabled()
	{
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
		_moteFx = _wispFx = null;
		_moteEmitter = _wispEmitter = null;
		_hasLastEye = false;
	}

	private void Build()
	{
		if ( _root.IsValid() ) return;

		_root = new GameObject( true, "dungeon_haze" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden };

		// motes
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( _root, "motes", TexMote, additive: true, bloom: false );
			renderer.Lighting = MoteLit;
			renderer.DepthFeather = 4f;
			renderer.CameraFadeNear = 12f;
			renderer.CameraFadeFar = 30f;

			var em = go.Components.Create<ParticleBoxEmitter>();
			em.Size = MoteVolume;
			em.OnEdge = false;
			em.Loop = true;
			em.Burst = 0f;
			em.Rate = MoteRate;
			_moteEmitter = em;

			fx.MaxParticles = Math.Max( 1, MoteCount * TrailCapacity );
			fx.LocalSpace = 0f;
			fx.Damping = 0f;
			fx.Force = false;
			fx.Lifetime = new ParticleFloat( MoteLifeMin, MoteLifeMax );
			fx.Scale = new ParticleFloat( MoteSizeMin, MoteSizeMax );
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, MoteAlpha ), new Curve.Frame( 0.8f, MoteAlpha ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( MoteColor, Dim( MoteColor, 0.7f ) );
			_moteFx = fx;

			var steer = go.Components.Create<MoteController>();
			steer.State = _moteState;
		}

		// wisps
		if ( Wisps )
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( _root, "wisps", TexWisp, additive: false, bloom: false );
			renderer.DepthFeather = 40f;
			// a card this size fills the screen when close: fully there only past 320 u, gone inside 120,
			// so walking into a cloud thins it instead of greying the whole view
			renderer.CameraFadeNear = 120f;
			renderer.CameraFadeFar = 320f;

			var em = go.Components.Create<ParticleBoxEmitter>();
			em.Size = WispVolume;
			em.OnEdge = false;
			em.Loop = true;
			em.Burst = 0f;
			em.Rate = WispRate;
			_wispEmitter = em;

			fx.MaxParticles = Math.Max( 1, WispCount * TrailCapacity );
			fx.LocalSpace = 0f;
			fx.Damping = 0f;
			fx.Force = false;
			fx.Lifetime = new ParticleFloat( WispLifeMin, WispLifeMax );
			fx.Scale = new ParticleFloat( WispSizeMin, WispSizeMax );
			fx.Roll = new ParticleFloat( 0f, 360f );
			// fade-in of about 3-4 s (a fifth of 14-20 s): a wisp born at the box edge is still arriving when
			// a running player reaches it, never a pop
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, WispAlpha ), new Curve.Frame( 0.8f, WispAlpha ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( WispColor, Dim( WispColor, 0.8f ) );
			_wispFx = fx;

			var steer = go.Components.Create<MoteController>();
			steer.State = _wispState;
		}
	}

	/// <summary>
	/// The full population at once, spread through the box around the current root at random points of
	/// their lives (Age drives the life fraction, so an aged mote is past its fade-in). Anything still
	/// alive from a previous spot is dropped first. Not left to the emitters: at Rate they need a whole
	/// lifetime to fill a room, and PreWarm would fill wherever the camera happened to be at scene load.
	/// </summary>
	private void Reseed()
	{
		Seed( _moteFx, MoteVolume, MoteTarget );
		Seed( _wispFx, WispVolume, WispTarget );
		_lastRoot = _root.WorldPosition;
		_moteOwed = _wispOwed = 0f;
	}

	/// <summary>
	/// The box moved: the slice of it that was not covered last frame is filled to the layer's density
	/// at once, so the air ahead is already dusty when the eye gets there instead of trickling in at the
	/// standing-still rate. Fractions carry over between frames, so a slow walk fills as evenly as a
	/// sprint. Each new particle is placed in one of the three uncovered bands (chosen by band volume)
	/// and starts at a life fraction in [minLife, maxLife]: motes skip their fade-in (tiny, at the far
	/// side of the box, never seen appearing), wisps start near zero and rely on their slow fade-in.
	/// </summary>
	private void FillUncovered( ParticleEffect fx, Vector3 volume, int count, float minLife, float maxLife, Vector3 prev, Vector3 now, ref float owed )
	{
		if ( !fx.IsValid() || count <= 0 ) return;
		var total = volume.x * volume.y * volume.z;
		if ( total <= 0f ) return;
		var d = now - prev;
		var ax = MathF.Min( MathF.Abs( d.x ), volume.x );
		var ay = MathF.Min( MathF.Abs( d.y ), volume.y );
		var az = MathF.Min( MathF.Abs( d.z ), volume.z );
		var overlap = (volume.x - ax) * (volume.y - ay) * (volume.z - az);
		owed += count * (total - overlap) / total;
		var n = (int)owed;
		if ( n <= 0 ) return;
		owed -= n;
		if ( fx.ParticleCount + n > fx.MaxParticles ) return;

		// band volumes: the x band is |dx| deep across the full y/z face, and so on
		var bx = ax * volume.y * volume.z;
		var by = volume.x * ay * volume.z;
		var bz = volume.x * volume.y * az;
		var sum = bx + by + bz;
		if ( sum <= 0f ) return;
		var half = volume * 0.5f;

		for ( var i = 0; i < n; i++ )
		{
			var pos = now + Random.Shared.VectorInCube( 0.5f ) * volume;
			var pick = Random.Shared.Float( 0f, sum );
			if ( pick < bx ) pos.x = Band( prev.x, now.x, half.x );
			else if ( pick < bx + by ) pos.y = Band( prev.y, now.y, half.y );
			else pos.z = Band( prev.z, now.z, half.z );

			var p = fx.Emit( pos, Random.Shared.Float( 0f, 1f ) );
			p.Age = Random.Shared.Float( minLife, maxLife ) * (p.DeathTime - p.BornTime);
		}
	}

	/// <summary>A random coordinate inside the new box but outside the old one along this axis.</summary>
	private static float Band( float prev, float now, float half )
	{
		return now >= prev
			? Random.Shared.Float( MathF.Max( prev + half, now - half ), now + half )
			: Random.Shared.Float( now - half, MathF.Min( prev - half, now + half ) );
	}

	/// <summary>Motes left far behind can no longer be seen (tiny, and past the fog): retire them so the cap stays for the air around the eye.</summary>
	private static void Cull( ParticleEffect fx, Vector3 eye, float distance )
	{
		if ( !fx.IsValid() ) return;
		var d2 = distance * distance;
		foreach ( var p in fx.Particles )
		{
			// life fraction 1: the effect deletes it on its next step
			if ( (p.Position - eye).LengthSquared > d2 ) p.Age = p.DeathTime - p.BornTime;
		}
	}

	private int MoteTarget => (int)(MoteCount * MathF.Max( 0f, Density ));
	private int WispTarget => (int)(WispCount * MathF.Max( 0f, Density ));
	private float CullDistance( Vector3 volume ) => MathF.Max( volume.x, volume.y ) * 1.25f + ForwardBias + 150f;

	private void Seed( ParticleEffect fx, Vector3 volume, int count )
	{
		if ( !fx.IsValid() ) return;
		fx.Clear();
		var origin = _root.WorldPosition;
		for ( var i = 0; i < count; i++ )
		{
			var p = fx.Emit( origin + Random.Shared.VectorInCube( 0.5f ) * volume, Random.Shared.Float( 0f, 1f ) );
			p.Age = Random.Shared.Float( 0f, 1f ) * (p.DeathTime - p.BornTime);
		}
	}

	private float MoteRate => MoteCount / MathF.Max( 0.5f, (MoteLifeMin + MoteLifeMax) * 0.5f ) * MathF.Max( 0f, Density );
	private float WispRate => WispCount / MathF.Max( 0.5f, (WispLifeMin + WispLifeMax) * 0.5f ) * MathF.Max( 0f, Density );

	private static Color Dim( Color c, float k ) => new( c.r * k, c.g * k, c.b * k, c.a );

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor ) return;
		if ( !_root.IsValid() ) Build();
		var camera = Scene.Camera;
		if ( !camera.IsValid() || !_root.IsValid() ) return;

		var eye = camera.WorldPosition;
		var jump = _hasLastEye ? (eye - _lastEye).Length : 0f;
		var speed = _hasLastEye && Time.Delta > 0f ? jump / Time.Delta : 0f;
		// first frame, or a teleport (the player is dropped a frame or two after the scene camera is
		// first seen): seed the air here, not where the camera used to be
		var reseed = !_hasLastEye || jump > ReseedJump;
		_lastEye = eye;
		_hasLastEye = true;

		var forward = camera.WorldRotation.Forward.WithZ( 0f );
		forward = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
		var lead = ForwardBias + Math.Clamp( reseed ? 0f : speed * 0.4f, 0f, 150f );
		_root.WorldPosition = eye + forward * lead;
		if ( reseed ) Reseed();
		else
		{
			var root = _root.WorldPosition;
			FillUncovered( _moteFx, MoteVolume, MoteTarget, 0.2f, 0.75f, _lastRoot, root, ref _moteOwed );
			FillUncovered( _wispFx, WispVolume, WispTarget, 0f, 0.08f, _lastRoot, root, ref _wispOwed );
			_lastRoot = root;
		}
		Cull( _moteFx, eye, CullDistance( MoteVolume ) );
		Cull( _wispFx, eye, CullDistance( WispVolume ) );

		// the draft: a current that turns over tens of seconds and breathes in strength
		var now = Time.Now;
		_draftAngle = now * 0.05f + MathF.Sin( now * 0.023f ) * 2f;
		var draft = new Vector3( MathF.Cos( _draftAngle ), MathF.Sin( _draftAngle ), 0f ) * (1f + 0.6f * MathF.Sin( now * 0.11f ));

		_moteState.Time = now;
		_moteState.Draft = draft * 1.5f;
		_moteState.Wander = MoteWander;
		_moteState.Sink = MoteSink;
		_moteState.Ease = 1.5f;

		_wispState.Time = now;
		_wispState.Draft = draft * WispDrift * 0.5f;
		_wispState.Wander = WispDrift;
		_wispState.Sink = 0f;
		_wispState.Ease = 0.4f;

		// live tuning
		if ( _moteEmitter.IsValid() ) { _moteEmitter.Rate = MoteRate; _moteEmitter.Size = MoteVolume; }
		if ( _wispEmitter.IsValid() ) { _wispEmitter.Rate = WispRate; _wispEmitter.Size = WispVolume; }
		if ( _moteFx.IsValid() ) _moteFx.MaxParticles = Math.Max( 1, MoteTarget * TrailCapacity + 1 );
		if ( _wispFx.IsValid() ) _wispFx.MaxParticles = Math.Max( 1, WispTarget * TrailCapacity + 1 );
	}
}
