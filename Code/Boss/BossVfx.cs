using System;
using Sandbox;
using VoidMage.Dungeon;
using VoidMage.Spells;

namespace VoidMage.Boss;

/// <summary>
/// The boss fight's own pictures, every peer, fire-and-forget, no damage in any of them: the floor ring running
/// out of a slam, and the wave standing up out of a clap between the palms.
/// </summary>
public static class BossVfx
{
	private const string TexRing = "vfx/polygonparticles_ring_02.png";

	/// <summary>`boss_ring_alpha`: the ring sprites' peak opacity (0 hides them).</summary>
	[ConVar( "boss_ring_alpha", ConVarFlags.Saved )] public static float RingAlpha { get; set; } = 0.55f;

	/// <summary>A flat ring on the floor at `centre`: radius r0 growing at `speed` to `max`; `strength` scales the opacity.</summary>
	public static void Shockwave( Vector3 centre, float r0, float speed, float max, float strength )
	{
		var life = MathF.Max( 0.1f, (max - r0) / MathF.Max( 50f, speed ) );
		Ring( "boss_shockwave", centre + Vector3.Up * 6f, Vector3.Up, r0, max, life, strength );
	}

	/// <summary>A ring standing in the vertical plane between two clapping palms: its normal along `axis` (the hands' meeting line), growing from the clap point to `max` over `seconds`.</summary>
	public static void ClapWave( Vector3 centre, Vector3 axis, float r0, float max, float seconds )
	{
		var n = axis.WithZ( 0f );
		Ring( "boss_clapwave", centre, n.IsNearZeroLength ? Vector3.Forward : n.Normal, r0, max, MathF.Max( 0.1f, seconds ), 1f );
	}

	private const string TexSmoke = "vfx/polygonparticles_smoke_01.png";
	private static readonly Color DustLight = new( 0.86f, 0.84f, 0.8f );
	private static readonly Color DustDark = new( 0.55f, 0.53f, 0.5f );

	/// <summary>
	/// The dust a hand stomps out from under itself: born on a flat ring at floor level around `at` (the palm's
	/// footprint, `radius`) and squirting OUTWARD along the floor, pressed down as it goes, never a column rising
	/// over the hand. The puff lives between the hand and the floor, where the air was.
	/// </summary>
	public static void FloorDust( Vector3 at, float radius )
	{
		var root = new GameObject( true, "boss_floordust" ) { WorldPosition = at + Vector3.Up * 8f };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = 2f;
		var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( root, "dust", TexSmoke, additive: false, bloom: false );
		renderer.DepthFeather = 6f;
		var em = go.Components.Create<ParticleRingEmitter>();
		em.Radius = radius * 0.6f; em.Thickness = radius * 0.5f; em.Flatness = 1f;
		em.VelocityFromCenter = 260f; em.Rate = 0f; em.Burst = 14f; em.Loop = false;
		fx.MaxParticles = 16;
		fx.Damping = 4f;
		fx.Force = true; fx.ForceScale = 1f; fx.ForceDirection = Vector3.Down * 60f;
		fx.Lifetime = new ParticleFloat( 0.55f, 0.9f );
		fx.Scale = FireVfxRig.OverLife( radius * 0.35f, radius * 0.9f );
		fx.Roll = new ParticleFloat( 0f, 360f );
		fx.Gradient = FireVfxRig.RandomBetween( DustLight, DustDark );
		fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0.7f ), new Curve.Frame( 0.4f, 0.45f ), new Curve.Frame( 1f, 0f ) );
	}

	/// <summary>A bone bursting where it hangs: a big spray of chips and dust out of `centre` (`size` = the bone's half-diagonal) and a standing ring that runs out of it.</summary>
	public static void Burst( Scene scene, Vector3 centre, float size )
	{
		Combat.BreakVfx.Stone( scene, centre, size, Vector3.Up * 220f, false );
		Combat.BreakVfx.Stone( scene, centre, size * 0.7f, Vector3.Zero, false );
		Ring( "boss_burst", centre, Vector3.Up, size * 0.4f, size * 3.2f, 0.55f, 1f );
		Ring( "boss_burst", centre, Vector3.Forward, size * 0.4f, size * 2.6f, 0.5f, 0.8f );
	}

	/// <summary>A landing mark on the floor: a ring closing from twice `radius` down to it over `seconds` (the rain's tell: where a bolt will fall).</summary>
	public static void Mark( Vector3 at, float radius, float seconds )
	{
		Ring( "boss_mark", at + Vector3.Up * 6f, Vector3.Up, radius * 2f, radius, MathF.Max( 0.1f, seconds ), 1.2f );
	}

	private static void Ring( string name, Vector3 centre, Vector3 normal, float r0, float max, float life, float strength )
	{
		if ( RingAlpha <= 0f ) return;
		var root = new GameObject( true, name ) { WorldPosition = centre };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = life + 1f;
		var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( root, "ring", TexRing, additive: false, bloom: false );
		go.WorldRotation = Rotation.LookAt( normal );
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Object;
		renderer.DepthFeather = 2f;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
		fx.MaxParticles = 2;
		fx.Lifetime = life;
		fx.Scale = FireVfxRig.OverLife( r0 * 2f, max * 2f );
		fx.Gradient = FireVfxRig.RandomBetween( new Color( 0.85f, 0.82f, 0.75f ), new Color( 0.7f, 0.66f, 0.6f ) );
		var a = RingAlpha * strength;
		fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, a ), new Curve.Frame( 0.5f, a * 0.6f ), new Curve.Frame( 1f, 0f ) );
	}
}
