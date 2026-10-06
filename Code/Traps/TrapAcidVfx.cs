using Sandbox;
using VoidMage.Spells;

namespace VoidMage.Traps;

/// <summary>
/// The acid head's effects: a lazy drip from the nozzle at rest (the tell, with a faint stain on the floor), a
/// spray of droplets arcing onto the floor when it fires, and the pool: a flat green disc on the tile that
/// bubbles while it lasts. Same sprite-layer idiom as the fire; world space, unlit, no bloom.
/// </summary>
public static class TrapAcidVfx
{
	public static readonly Color Acid = new( 0.45f, 0.95f, 0.2f );
	public static readonly Color AcidDark = new( 0.2f, 0.5f, 0.12f );

	/// <summary>Size multiplier on the droplets and bubbles.</summary>
	[ConVar( "trap_acid_vfx_size" )] public static float Size { get; set; } = 1f;
	/// <summary>Opacity of the pool disc while it lasts.</summary>
	[ConVar( "trap_acid_pool_alpha" )] public static float PoolAlpha { get; set; } = 0.55f;

	public sealed class Rig
	{
		public GameObject Root;
		public ParticleSphereEmitter Drip;
		public ParticleSphereEmitter Spray;
		public ParticleSphereEmitter Bubbles;
		public ParticleEffect PoolFx;
		public ParticleSphereEmitter Pool;
	}

	/// <param name="nozzle">World point the acid leaves from.</param>
	/// <param name="floor">World point on the floor the spray lands on and the pool spreads from.</param>
	/// <param name="poolRadius">Pool disc radius, units.</param>
	public static Rig Build( GameObject parent, Vector3 nozzle, Vector3 floor, float poolRadius )
	{
		var rig = new Rig();
		var root = new GameObject( true, "trap_acid" ) { Parent = parent };
		root.WorldPosition = nozzle;
		root.WorldRotation = Rotation.Identity;
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		rig.Root = root;
		float k = Size;
		var toFloor = floor - nozzle;
		float drop = toFloor.z;
		// a droplet leaving at v reaches the floor in about t = sqrt(2h/g) with the engine's gravity of 800
		float fall = System.MathF.Sqrt( 2f * System.MathF.Max( 10f, -drop ) / 800f );
		var lateral = new Vector3( toFloor.x, toFloor.y, 0f ) / System.MathF.Max( 0.1f, fall );

		// the drip: one slow droplet now and then, falling straight down from the nozzle
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "acid_drip", FireballVfx.TexCircleSmall, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 1f; em.Velocity = 0f; em.Rate = 1.2f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 8; fx.LocalSpace = 0f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -800f );
			fx.Lifetime = fall * 1.05f;
			fx.Scale = new ParticleFloat( 2.2f * k, 3f * k );
			fx.Gradient = FireballVfx.RandomBetween( Acid, AcidDark );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0.9f ), new Curve.Frame( 0.9f, 0.9f ), new Curve.Frame( 1f, 0f ) );
			rig.Drip = em;
		}
		// the spray: a fan of droplets thrown at the floor point, off while idle
		{
			var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "acid_spray", FireballVfx.TexCircleSmall, additive: false, bloom: false );
			renderer.FaceVelocity = true;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f; em.Velocity = 45f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 120; fx.LocalSpace = 0f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -800f );
			fx.InitialVelocity = lateral;
			fx.Stretch = 2.2f;
			fx.Lifetime = new ParticleFloat( fall * 0.95f, fall * 1.1f );
			fx.Scale = new ParticleFloat( 2.5f * k, 4f * k );
			fx.Gradient = FireballVfx.RandomBetween( Acid, AcidDark );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.85f, 1f ), new Curve.Frame( 1f, 0f ) );
			rig.Spray = em;
		}
		// the pool: one flat disc laid on the tile (object-aligned, facing up), emitted per firing and living the pool's life
		{
			var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "acid_pool", FireballVfx.TexSoftSpot, additive: false, bloom: false );
			go.WorldPosition = floor + Vector3.Up * 1.5f;
			go.WorldRotation = Rotation.LookAt( Vector3.Up );
			renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Object;
			renderer.DepthFeather = 0.5f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 2; fx.LocalSpace = 0f;
			fx.Lifetime = 6f;
			fx.Scale = FireballVfx.CurveOverLife( new Curve.Frame( 0f, poolRadius * 0.6f ), new Curve.Frame( 0.08f, poolRadius * 2f ), new Curve.Frame( 1f, poolRadius * 1.6f ) );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, Acid ), new Gradient.ColorFrame( 0.7f, Acid ), new Gradient.ColorFrame( 1f, AcidDark ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, PoolAlpha ), new Curve.Frame( 0.75f, PoolAlpha ), new Curve.Frame( 1f, 0f ) );
			rig.PoolFx = fx;
			rig.Pool = em;
		}
		// bubbles rising off the pool while it lasts
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "acid_bubbles", FireballVfx.TexCircleSmall, additive: false, bloom: false );
			go.WorldPosition = floor + Vector3.Up * 3f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = poolRadius * 0.8f; em.Velocity = 4f; em.Rate = 0f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 40; fx.LocalSpace = 0f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 18f );
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = FireballVfx.OverLife( 1.5f * k, 3.5f * k );
			fx.Gradient = FireballVfx.RandomBetween( Acid, Color.Lerp( Acid, Color.White, 0.3f ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.8f ), new Curve.Frame( 1f, 0f ) );
			rig.Bubbles = em;
		}
		return rig;
	}

	public static void Spray( Rig rig, bool on )
	{
		if ( rig?.Spray.IsValid() != true ) return;
		rig.Spray.Rate = on ? 110f : 0f;
	}

	/// <summary>Lay the pool for `seconds` and start it bubbling.</summary>
	public static void PoolStart( Rig rig, float seconds )
	{
		if ( rig is null ) return;
		if ( rig.PoolFx.IsValid() ) rig.PoolFx.Lifetime = seconds;
		if ( rig.Pool.IsValid() ) rig.Pool.Rate = 0f;
		// one disc: a burst of one on an emitter that then rests
		if ( rig.Pool.IsValid() ) { rig.Pool.Burst = 1f; rig.Pool.Loop = false; rig.Pool.Enabled = false; rig.Pool.Enabled = true; }
		if ( rig.Bubbles.IsValid() ) rig.Bubbles.Rate = 22f;
	}

	public static void PoolStop( Rig rig )
	{
		if ( rig?.Bubbles.IsValid() == true ) rig.Bubbles.Rate = 0f;
	}
}
