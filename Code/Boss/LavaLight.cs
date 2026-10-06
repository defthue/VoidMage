using System;
using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.Boss;

/// <summary>
/// The glow a lava piece throws: a shadowless orange point light over it, breathing slowly, and a drift of embers
/// rising off its surface. Not a FireLight (the fire system would park a torch flame on it); the same rule applies
/// though: the light never moves and never changes radius, only colour, so it is free. HellArena makes one per
/// lava mesh and one huge one for the lava sea under the stage, so the cliffs are lit from below like a night
/// over a caldera.
/// </summary>
[Title( "Lava Light" ), Category( "Void Mage" ), Icon( "whatshot" )]
public sealed class LavaLight : Component
{
	[Property] public Color BaseColor { get; set; } = new( 1f, 0.42f, 0.1f );
	/// <summary>Slow breathing, fraction of the base brightness.</summary>
	[Property, Range( 0f, 0.5f )] public float Breathe { get; set; } = 0.12f;
	[Property] public float Phase { get; set; }

	private const string TexEmber = "vfx/polygonparticles_soft_spot.png";

	/// <summary>Embers per second per square metre of lava; `boss_lava_embers 0` turns them off.</summary>
	[ConVar( "boss_lava_embers" )] public static float EmberDensity { get; set; } = 0.35f;

	private PointLight _light;

	protected override void OnAwake() => _light = Components.Get<PointLight>();

	protected override void OnUpdate()
	{
		if ( !_light.IsValid() ) return;
		var t = Time.Now + Phase;
		var f = MathF.Sin( t * 1.1f ) * 0.5f + MathF.Sin( t * 2.7f + 1.3f ) * 0.3f + MathF.Sin( t * 0.4f ) * 0.2f;
		_light.LightColor = BaseColor * (1f + f * Breathe);
	}

	/// <summary>
	/// Embers over a lava surface: a box emitter across `bounds`' top, small additive sparks that rise slowly, wander
	/// and fade to dark red. Population from the surface area, capped, so a lava line gets a few and the pool a lot.
	/// </summary>
	public static void AddEmbers( GameObject parent, BBox bounds )
	{
		if ( EmberDensity <= 0f ) return;
		var areaM2 = MathF.Max( 1f, bounds.Size.x * bounds.Size.y / (39.37f * 39.37f) );
		var rate = Math.Clamp( areaM2 * EmberDensity, 1.5f, 60f );
		var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( parent, "embers", TexEmber, additive: true, bloom: true );
		go.WorldPosition = new Vector3( bounds.Center.x, bounds.Center.y, bounds.Maxs.z + 10f );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		renderer.DepthFeather = 6f;
		renderer.CameraFadeNear = 10f;
		renderer.CameraFadeFar = 40f;

		var em = go.Components.Create<ParticleBoxEmitter>();
		em.Size = new Vector3( bounds.Size.x, bounds.Size.y, 6f );
		em.OnEdge = false;
		em.Loop = true;
		em.Burst = 0f;
		em.Rate = rate;

		fx.MaxParticles = (int)MathF.Ceiling( rate * 5.5f ) + 4;
		fx.LocalSpace = 0f;
		fx.Lifetime = new ParticleFloat( 3f, 5.5f );
		fx.Scale = new ParticleFloat( 3f, 7f );
		fx.StartVelocity = new ParticleFloat( 8f, 26f );
		// up, with a lean that changes by ember; damping keeps the rise lazy
		fx.Force = true;
		fx.ForceDirection = Vector3.Up * 22f;
		fx.Damping = 0.35f;
		fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.1f, 0.9f ), new Curve.Frame( 0.6f, 0.7f ), new Curve.Frame( 1f, 0f ) );
		fx.Gradient = FireVfxRig.RandomBetween( new Color( 1f, 0.75f, 0.3f ), new Color( 1f, 0.28f, 0.05f ) );
	}
}
