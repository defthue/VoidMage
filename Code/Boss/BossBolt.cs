using System;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Dungeon;
using VoidMage.Spells;

namespace VoidMage.Boss;

/// <summary>
/// One bolt of the shake's volley: green eye fire that leaves a socket and flies slowly over an arc to where the
/// player stood when it left. Nothing is networked: the host broadcasts the launch (from, to), every peer spawns
/// its own bolt, flies it on the same analytic path and judges its own player against it (the trap model). Slow
/// enough to walk out of: the arc is the tell.
/// </summary>
public sealed class BossBolt : Component
{
	/// <summary>Seconds in the air, the arc's height over the straight line, the hit radius and the damage.</summary>
	[ConVar( "boss_bolt_flight", ConVarFlags.Saved )] public static float Flight { get; set; } = 1.3f;
	[ConVar( "boss_bolt_arc", ConVarFlags.Saved )] public static float Arc { get; set; } = 260f;
	[ConVar( "boss_bolt_radius", ConVarFlags.Saved )] public static float Radius { get; set; } = 55f;
	[ConVar( "boss_bolt_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 14f;

	private static readonly Color Core = new( 0.55f, 1f, 0.55f );
	private static readonly Color Deep = new( 0.08f, 0.8f, 0.3f );
	private const string TexFlame = "vfx/flame_tongue.png";
	private const string TexGlow = "vfx/flame_glow.png";

	private Vector3 _from, _to;
	private float _launch;
	private bool _hit;
	private bool _landed;
	private bool _harmless;
	private PointLight _light;
	private GameObject _attacker;

	/// <summary>Every peer: a bolt from `from` to `to` starting now. `mark` draws its landing spot on the floor for the flight (the rain's tell); `harmless` is a picture only (the summon's).</summary>
	public static void Spawn( Scene scene, Vector3 from, Vector3 to, GameObject attacker, bool mark = false, bool harmless = false )
	{
		if ( !scene.IsValid() ) return;
		var go = new GameObject( true, "boss_bolt" ) { WorldPosition = from };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "spellfire" );
		var bolt = go.Components.Create<BossBolt>();
		bolt._from = from;
		bolt._to = to;
		bolt._launch = Time.Now;
		bolt._attacker = attacker;
		bolt._harmless = harmless;
		bolt.Build();
		if ( mark ) BossVfx.Mark( to, Radius * 2.2f, Flight );
		Audio.GameAudio.Play( "sounds/fireballcastslow.sound", from );
	}

	/// <summary>The path: a straight line lifted by a parabola, `u` 0..1.</summary>
	public static Vector3 PathPoint( Vector3 from, Vector3 to, float u )
	{
		u = Math.Clamp( u, 0f, 1f );
		return Vector3.Lerp( from, to, u ) + Vector3.Up * (Arc * 4f * u * (1f - u));
	}

	private void Build()
	{
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( GameObject, "fire", TexFlame, additive: true, bloom: true );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.LocalPosition = Vector3.Zero;
			renderer.DepthFeather = 8f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 10f; em.Velocity = 0f; em.Loop = true; em.Burst = 0f; em.Rate = 45f;
			fx.MaxParticles = 60;
			fx.LocalSpace = 0f;   // the tail hangs in the world behind the bolt
			fx.Lifetime = new ParticleFloat( 0.25f, 0.45f );
			fx.Scale = new ParticleFloat( 26f, 40f );
			fx.StartVelocity = new ParticleFloat( 0f, 10f );
			fx.Force = true;
			fx.ForceDirection = Vector3.Up * 90f;
			fx.Damping = 0.3f;
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 1f ), new Curve.Frame( 0.7f, 0.7f ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( Core, Deep );
		}
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( GameObject, "glow", TexGlow, additive: true, bloom: true );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.LocalPosition = Vector3.Zero;
			renderer.DepthFeather = 10f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f; em.Velocity = 0f; em.Loop = true; em.Burst = 0f; em.Rate = 14f;
			fx.MaxParticles = 14;
			fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.35f );
			fx.Scale = new ParticleFloat( 60f, 80f );
			fx.StartVelocity = new ParticleFloat( 0f, 0f );
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.3f, 0.45f ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( Deep, Deep.Darken( 0.3f ) );
		}
		_light = Components.Create<PointLight>();
		_light.Radius = 260f;
		_light.LightColor = new Color( 0.35f, 1f, 0.45f ) * 2.5f;
		_light.Shadows = false;
	}

	protected override void OnUpdate()
	{
		var u = (Time.Now - _launch) / MathF.Max( 0.1f, Flight );
		var pos = PathPoint( _from, _to, u );
		WorldPosition = pos;
		if ( _light.IsValid() ) _light.LightColor = new Color( 0.35f, 1f, 0.45f ) * (2.2f + 0.6f * MathF.Sin( Time.Now * 23f ));
		if ( !_hit && !_harmless ) Judge( pos, u );
		if ( u >= 1f && !_landed ) Land( pos );
	}

	private void Judge( Vector3 pos, float u )
	{
		var p = PlayerHealth.Local;
		if ( !p.IsValid() || p.IsProxy || p.IsDead ) return;
		var feet = p.FeetPosition;
		var head = feet + Vector3.Up * p.BodyHeight;
		// distance from the bolt to the body's spine segment
		var seg = head - feet;
		var k = Math.Clamp( Vector3.Dot( pos - feet, seg ) / MathF.Max( 1f, seg.LengthSquared ), 0f, 1f );
		var closest = feet + seg * k;
		if ( closest.Distance( pos ) > Radius ) return;
		_hit = true;
		var dir = (_to - _from).WithZ( 0f );
		var push = (dir.IsNearZeroLength ? Vector3.Zero : dir.Normal * 160f) + Vector3.Up * 60f;
		p.TakeHit( Damage, push, pos, "boss_bolt", _attacker ?? GameObject );
		Land( pos );
	}

	private void Land( Vector3 pos )
	{
		_landed = true;
		Audio.GameAudio.Play( "sounds/fireballimpact.sound", pos );
		BreakVfx.Puff( Scene, pos, 50f );
		// the splash: a burst of the same fire thrown outward and up, a fading green flash
		var root = new GameObject( true, "boss_bolt_land" ) { WorldPosition = pos };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Components.Create<VfxLifetime>().Seconds = 1.2f;
		var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( root, "splash", TexFlame, additive: true, bloom: true );
		renderer.DepthFeather = 8f;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 16f; em.Velocity = 220f; em.Loop = false; em.Rate = 0f; em.Burst = 22f;
		fx.MaxParticles = 26;
		fx.LocalSpace = 0f;
		fx.Lifetime = new ParticleFloat( 0.3f, 0.6f );
		fx.Scale = FireVfxRig.OverLife( 34f, 8f );
		fx.Force = true;
		fx.ForceDirection = Vector3.Up * 120f;
		fx.Damping = 1.6f;
		fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.6f, 0.7f ), new Curve.Frame( 1f, 0f ) );
		fx.Gradient = FireVfxRig.RandomBetween( Core, Deep );
		var light = root.Components.Create<PointLight>();
		light.Radius = 300f;
		light.LightColor = new Color( 0.35f, 1f, 0.45f ) * 4f;
		light.Shadows = false;
		root.Components.Create<BoltFlash>();
		VoidMage.Dev.WorldTape.Retire( GameObject );
	}

	/// <summary>The landing light dying over its root's life.</summary>
	private sealed class BoltFlash : Component
	{
		private float _born = -1f;
		protected override void OnUpdate()
		{
			if ( _born < 0f ) _born = Time.Now;
			var light = Components.Get<PointLight>();
			if ( !light.IsValid() ) return;
			var k = 1f - Math.Clamp( (Time.Now - _born) / 0.4f, 0f, 1f );
			light.LightColor = new Color( 0.35f, 1f, 0.45f ) * (4f * k);
			light.Enabled = k > 0f;
		}
	}
}
