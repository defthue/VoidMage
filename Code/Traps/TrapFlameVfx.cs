using Sandbox;
using VoidMage.Spells;

namespace VoidMage.Traps;

/// <summary>
/// The flame head's fire breath: a jet of hard orange polygon discs shot from the nozzle along the aim, with
/// smaller smeared tongues, a little smoke lifting off it and a few embers, plus a light that is only on while
/// it burns. Built once per head and switched on and off by emitter rate (the fireball's sprite-layer idiom,
/// the torch's taste: orange, small, never white). World space, so the spray keeps flying when it stops.
/// </summary>
public static class TrapFlameVfx
{
	public sealed class Jet
	{
		public GameObject Root;
		public ParticleSphereEmitter[] Emitters = System.Array.Empty<ParticleSphereEmitter>();
		public float[] Rates = System.Array.Empty<float>();
		public PointLight Light;
		public bool On;
	}

	/// <summary>Speed (units/s) the fire leaves the nozzle at; with the damping it dies out around the jet's length.</summary>
	[ConVar( "trap_flame_jet_speed" )] public static float JetSpeed { get; set; } = 640f;
	/// <summary>Size multiplier on every sprite of the jet.</summary>
	[ConVar( "trap_flame_jet_size" )] public static float JetSize { get; set; } = 1f;
	/// <summary>Particle rate multiplier on every layer of the jet.</summary>
	[ConVar( "trap_flame_jet_density" )] public static float JetDensity { get; set; } = 1f;

	public static Jet Build( GameObject parent, Vector3 worldNozzle, Vector3 aim )
	{
		var jet = new Jet();
		var root = new GameObject( true, "trap_flame_jet" ) { Parent = parent };
		root.WorldPosition = worldNozzle;
		root.WorldRotation = Rotation.Identity;   // unrotated: every velocity below is a world vector
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		jet.Root = root;
		float k = JetSize;
		var emitters = new System.Collections.Generic.List<ParticleSphereEmitter>();
		var rates = new System.Collections.Generic.List<float>();

		// the body of the breath: fat orange discs, banded, growing as they fly and slowing to a stop
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "jet_core", FireballVfx.TexCircle, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f; em.Velocity = 30f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 80; fx.LocalSpace = 0f; fx.Damping = 2.5f;
			fx.InitialVelocity = aim * JetSpeed;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.32f );
			fx.Scale = FireballVfx.OverLife( 8f * k, 24f * k );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.Banded( FireballVfx.Hot, FireballVfx.Core, FireballVfx.Core.Darken( 0.5f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			emitters.Add( em ); rates.Add( 90f );
		}
		// tongues: small smeared discs racing ahead of the body
		{
			var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "jet_tongues", FireballVfx.TexCircleSmall, additive: true, bloom: false );
			renderer.MotionBlur = true; renderer.LeadingTrail = true; renderer.BlurAmount = 0.5f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f; em.Velocity = 40f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 48; fx.LocalSpace = 0f; fx.Damping = 1.8f;
			fx.InitialVelocity = aim * (JetSpeed * 1.15f);
			fx.Lifetime = new ParticleFloat( 0.14f, 0.24f );
			fx.Scale = FireballVfx.OverLife( 12f * k, 3f * k );
			fx.Gradient = FireballVfx.Banded( FireballVfx.Hot, FireballVfx.Core, FireballVfx.Core.Darken( 0.6f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			emitters.Add( em ); rates.Add( 40f );
		}
		// smoke: translucent puffs lifting off the jet as it cools
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "jet_smoke", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 6f; em.Velocity = 20f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 32; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 45f );
			fx.InitialVelocity = aim * (JetSpeed * 0.65f);
			fx.Lifetime = new ParticleFloat( 0.45f, 0.8f );
			fx.Scale = FireballVfx.OverLife( 10f * k, 28f * k );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.SmokeRamp();
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.4f ), new Curve.Frame( 1f, 0f ) );
			emitters.Add( em ); rates.Add( 22f );
		}
		// embers: a few flickering sparks thrown further than the fire
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "jet_embers", FireballVfx.TexSparkle, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f; em.Velocity = 90f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 24; fx.LocalSpace = 0f; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -60f );
			fx.InitialVelocity = aim * (JetSpeed * 0.95f);
			fx.Lifetime = new ParticleFloat( 0.3f, 0.5f );
			fx.Scale = new ParticleFloat( 3f * k, 5f * k );
			fx.Brightness = FireballVfx.Flicker( 0.5f, 1.6f );
			fx.Gradient = FireballVfx.RandomBetween( FireballVfx.Ember, FireballVfx.Hot );
			fx.Alpha = FireballVfx.SoftAlpha();
			emitters.Add( em ); rates.Add( 18f );
		}

		var lightGo = new GameObject( true, "jet_light" ) { Parent = root };
		lightGo.WorldPosition = worldNozzle + aim * 80f;
		jet.Light = lightGo.Components.Create<PointLight>();
		jet.Light.LightColor = FireballVfx.Core * 1.2f;
		jet.Light.Radius = 200f;
		jet.Light.Shadows = false;
		jet.Light.Enabled = false;

		jet.Emitters = emitters.ToArray();
		jet.Rates = rates.ToArray();
		return jet;
	}

	public static void Set( Jet jet, bool on )
	{
		if ( jet is null ) return;
		jet.On = on;
		for ( int i = 0; i < jet.Emitters.Length; i++ )
			if ( jet.Emitters[i].IsValid() ) jet.Emitters[i].Rate = on ? jet.Rates[i] * JetDensity : 0f;
		if ( jet.Light.IsValid() ) jet.Light.Enabled = on;
	}

	/// <summary>A little life in the light while it burns.</summary>
	public static void Drive( Jet jet, float t )
	{
		if ( jet is null || !jet.On || !jet.Light.IsValid() ) return;
		float flick = 0.85f + 0.1f * System.MathF.Sin( t * 23f ) + 0.05f * System.MathF.Sin( t * 7.3f );
		jet.Light.LightColor = FireballVfx.Core * (1.2f * flick);
	}
}
