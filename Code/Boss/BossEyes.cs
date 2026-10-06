using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.Boss;

/// <summary>
/// The green fire in the skull's eye sockets, on every peer: a small flame layer and a point light per `eye_*`
/// child, driven by the brain's synced <see cref="BossBrain.EyeFire01"/> (0 dark, 1 burning, 2 flaring). The
/// ignition is a hard flash with a sound: the moment the player learns the thing is alive.
/// </summary>
[Title( "Boss Eyes" ), Category( "Void Mage" ), Icon( "visibility" )]
public sealed class BossEyes : Component
{
	[Property] public Color Core { get; set; } = new( 0.55f, 1f, 0.55f );
	[Property] public Color Deep { get; set; } = new( 0.08f, 0.8f, 0.3f );
	[Property] public Color LightColor { get; set; } = new( 0.35f, 1f, 0.45f );
	[Property] public float LightBrightness { get; set; } = 3f;
	[Property] public float FlameScale { get; set; } = 0.6f;

	private const string TexFlame = "vfx/flame_tongue.png";
	private const string TexGlow = "vfx/flame_glow.png";

	private sealed class Eye
	{
		public GameObject Go;
		public PointLight Light;
		public float BaseRadius;
		public ParticleEffect Fire;
		public ParticleSphereEmitter FireEm;
		public ParticleEffect Glow;
		public ParticleSphereEmitter GlowEm;
		public float Phase;
	}

	private readonly List<Eye> _eyes = new();
	private float _prev = -1f;
	private float _flashAt = float.NegativeInfinity;
	private BossBrain _brain;

	protected override void OnStart()
	{
		_brain = Components.Get<BossBrain>();
		var s = MathF.Max( 0.01f, WorldScale.x );
		foreach ( var child in GameObject.Children )
		{
			if ( !child.Name.StartsWith( "eye", StringComparison.OrdinalIgnoreCase ) ) continue;
			var eye = new Eye { Go = child, Light = child.Components.Get<PointLight>(), Phase = _eyes.Count * 1.7f };
			if ( eye.Light.IsValid() )
			{
				eye.BaseRadius = eye.Light.Radius * s;
				eye.Light.Radius = eye.BaseRadius;
				eye.Light.LightColor = Color.Black;
			}
			BuildFlames( eye, s );
			_eyes.Add( eye );
		}
	}

	private void BuildFlames( Eye eye, float s )
	{
		var size = 34f * s * FlameScale;
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( eye.Go, "fire", TexFlame, additive: true, bloom: true );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.LocalPosition = Vector3.Zero;
			renderer.DepthFeather = 8f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 5f * s * FlameScale / 0.6f;
			em.Velocity = 0f;
			em.Loop = true;
			em.Burst = 0f;
			em.Rate = 0f;
			fx.MaxParticles = 40;
			fx.LocalSpace = 1f;   // rides the skull
			fx.Lifetime = new ParticleFloat( 0.3f, 0.55f );
			fx.Scale = new ParticleFloat( size * 0.7f, size );
			fx.StartVelocity = new ParticleFloat( 0f, 6f );
			fx.Force = true;
			fx.ForceDirection = Vector3.Up * 140f;
			fx.Damping = 0.2f;
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 1f ), new Curve.Frame( 0.7f, 0.8f ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( Core, Deep );
			eye.Fire = fx;
			eye.FireEm = em;
		}
		{
			var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( eye.Go, "glow", TexGlow, additive: true, bloom: true );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.LocalPosition = Vector3.Zero;
			renderer.DepthFeather = 10f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f * s;
			em.Velocity = 0f;
			em.Loop = true;
			em.Burst = 0f;
			em.Rate = 0f;
			fx.MaxParticles = 12;
			fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.25f, 0.4f );
			fx.Scale = new ParticleFloat( size * 1.6f, size * 2.2f );
			fx.StartVelocity = new ParticleFloat( 0f, 0f );
			fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.3f, 0.5f ), new Curve.Frame( 1f, 0f ) );
			fx.Gradient = FireVfxRig.RandomBetween( Deep, Deep.Darken( 0.3f ) );
			eye.Glow = fx;
			eye.GlowEm = em;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_brain.IsValid() ) return;
		var f = _brain.EyeFire01;
		if ( f > 0.01f && _prev <= 0.01f )
		{
			// ignition
			_flashAt = Time.Now;
			Audio.GameAudio.Play( "sounds/steamburst.sound", WorldPosition );
			Combat.HitFeel.Shake( 3f, 1.2f );
		}
		_prev = f;
		var flash = Math.Clamp( 1f - (Time.Now - _flashAt) / 0.45f, 0f, 1f );
		foreach ( var eye in _eyes )
		{
			var t = Time.Now + eye.Phase;
			var flicker = 1f + 0.18f * MathF.Sin( t * 13f ) + 0.12f * MathF.Sin( t * 29f + 1f );
			var level = f <= 0.01f ? 0f : MathF.Min( f, 2f ) * flicker + flash * 3f;
			if ( eye.Light.IsValid() )
			{
				eye.Light.Enabled = level > 0f;
				eye.Light.LightColor = LightColor * (LightBrightness * level);
			}
			if ( eye.FireEm.IsValid() ) eye.FireEm.Rate = level > 0f ? 28f * MathF.Min( level, 2.5f ) : 0f;
			if ( eye.GlowEm.IsValid() ) eye.GlowEm.Rate = level > 0f ? 10f : 0f;
			if ( eye.Fire.IsValid() ) eye.Fire.ForceDirection = Vector3.Up * (140f + 120f * MathF.Max( 0f, f - 1f ) + 400f * flash);
		}
	}
}
