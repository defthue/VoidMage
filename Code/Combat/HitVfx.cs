using System;
using Sandbox;
using VoidMage.Rendering;
using VoidMage.Spells;

namespace VoidMage.Combat;

/// <summary>
/// What a sword hit looks like at the point of contact, on every peer: a brief soft flash and a handful of
/// white-grey pixel sparks, hard little discs stretched into bars along their travel, thrown along the swing
/// and falling as they fade (no collision, no glow, no light: the flash on the body is the light). The local
/// attacker's own hits also ripple the screen through the retro filter, one per swing. Three entry points
/// differ only in where the sparks go: along the swing off a body, off the shield (spawned by the skeleton's
/// body at the shield bone), off the surface normal when the swing meets the dungeon. Heavies run 1.4x.
/// Flesh (the Fortress corpse, the monsters, the corpse props) gets exactly the same hit PLUS BloodVfx's spray
/// (user, 2026-09-12: "always normal sparks with each hit, but added blood").
/// Built with the fireball's sprite-layer recipe; each hit is one root object that destroys itself.
/// </summary>
public static class HitVfx
{
	private const string TexStar = "vfx/polygonparticles_sparkle.png";
	private const string TexDot = "vfx/polygonparticles_circle_01.png";
	private const string TexRing = "vfx/polygonparticles_ring_02.png";

	private static readonly Color SparkWhite = new( 1f, 1f, 0.96f );
	private static readonly Color SparkGrey = new( 0.72f, 0.72f, 0.7f );

	public static float HeavyScale { get; set; } = 1.4f;

	/// <summary>Degrees added to a spark's velocity-facing: set 90 if the pills sit crosswise to their travel.</summary>
	[ConVar( "hit_spark_rot" )] public static float SparkRotationOffset { get; set; } = 0f;
	/// <summary>Length-to-width of a spark pill.</summary>
	[ConVar( "hit_spark_stretch" )] public static float SparkStretch { get; set; } = 3f;
	/// <summary>Launch speed of a spark per unit of throw (320 along the swing off a body): they are born at this pace.</summary>
	[ConVar( "hit_spark_launch" )] public static float SparkLaunch { get; set; } = 0.5f;
	/// <summary>Random outward speed on top of the launch: the width of the spray.</summary>
	[ConVar( "hit_spark_scatter" )] public static float SparkScatter { get; set; } = 60f;
	/// <summary>Peak opacity of the impact ring (0 turns it off).</summary>
	[ConVar( "hit_ring_alpha" )] public static float RingAlpha { get; set; } = 0.42f;
	/// <summary>Set 90 if the ring shows edge-on instead of flat in the blade's plane.</summary>
	[ConVar( "hit_ring_rot" )] public static float RingRotationOffset { get; set; } = 0f;

	/// <summary>The blade landed on a body. `swing` is the world direction the blade travelled.</summary>
	public static void BoneHit( Scene scene, Vector3 position, Vector3 swing, bool heavy, bool localAttacker )
	{
		var k = heavy ? HeavyScale : 1f;
		var root = Root( scene, "hit_bone", position );
		Flash( root, k );
		Ring( root, swing, k );
		Sparks( root, swing * 320f, 8, k, SparkWhite, SparkGrey, 0.22f, 0.35f );
		if ( localAttacker ) RetroFilter.TriggerRipple( position, k );
	}

	/// <summary>
	/// The blade landed on flesh (a body whose SkeletonBody.Bleeds is set, its ragdoll, or a corpse prop): the bone
	/// hit as it is, and the blood on top.
	/// </summary>
	public static void FleshHit( Scene scene, Vector3 position, Vector3 swing, bool heavy, bool localAttacker )
	{
		BoneHit( scene, position, swing, heavy, localAttacker );
		BloodVfx.Spray( scene, position, swing, heavy );
	}

	/// <summary>The shield took the blade (spawned by the skeleton's body at the shield).</summary>
	public static void ShieldHit( Scene scene, Vector3 position, Vector3 swing, bool heavy )
	{
		var k = heavy ? HeavyScale : 1f;
		var root = Root( scene, "hit_shield", position );
		Flash( root, k );
		Ring( root, swing, k );
		Sparks( root, swing * 380f, 10, k, SparkWhite, SparkGrey, 0.22f, 0.35f );
	}

	/// <summary>The swing met a wall or a prop: sparks off the surface.</summary>
	public static void StoneHit( Scene scene, Vector3 position, Vector3 normal, Vector3 swing, bool heavy, bool localAttacker )
	{
		var k = heavy ? HeavyScale : 1f;
		var root = Root( scene, "hit_stone", position + normal * 2f );
		Flash( root, k * 0.7f );
		Sparks( root, normal * 260f + swing * 160f, 6, k * 0.8f, SparkWhite, SparkGrey, 0.22f, 0.35f );
		if ( localAttacker ) RetroFilter.TriggerRipple( position, k * 0.6f );
	}

	// ------------------------------------------------------------------ the parts

	private static GameObject Root( Scene scene, string name, Vector3 position )
	{
		var go = new GameObject( true, name ) { WorldPosition = position };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "spellfire" );
		go.Components.Create<VfxLifetime>().Seconds = 0.7f;
		return go;
	}

	/// <summary>
	/// The strike mark at the contact: a hard four-point star popping in big and shrinking over a flat white disc
	/// that pops and shrinks under it. Solid white, alpha-blended, no glow and no light: it covers the sparks'
	/// birth and is where the screen ripple starts.
	/// </summary>
	private static void Flash( GameObject root, float k )
	{
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "flash_disc", TexDot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			// Velocity 0: a sphere emitter throws at 100 u/s by default, and a static mark must not drift
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 2; fx.Lifetime = 0.12f;
			fx.Scale = FireballVfx.OverLife( 22f * k, 4f * k );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, Color.White ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.7f, 1f ), new Curve.Frame( 1f, 0f ) );
		}
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "flash_star", TexStar, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 2; fx.Lifetime = 0.14f;
			fx.Scale = FireballVfx.OverLife( 46f * k, 10f * k );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, Color.White ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.75f, 1f ), new Curve.Frame( 1f, 0f ) );
		}
	}

	/// <summary>
	/// The shockwave: one thin grey ring opening from the contact in the blade's plane (a lateral slash throws a
	/// vertical ring, an overhead a horizontal one), very transparent, alpha-blended, no glow. The plane is spanned
	/// by the swing and the line to the camera, so it always reads as a disc, never edge-on.
	/// </summary>
	private static void Ring( GameObject root, Vector3 swing, float k )
	{
		if ( RingAlpha <= 0f ) return;
		var camera = root.Scene?.Camera;
		var toCamera = camera.IsValid() ? (camera.WorldPosition - root.WorldPosition).Normal : Vector3.Up;
		var normal = Vector3.Cross( swing.Normal, toCamera );
		if ( normal.IsNearZeroLength ) normal = toCamera;
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "ring", TexRing, additive: false, bloom: false );
		go.WorldRotation = Rotation.LookAt( normal.Normal ) * Rotation.FromRoll( RingRotationOffset );
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.Object;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
		fx.MaxParticles = 2; fx.Lifetime = 0.17f;
		fx.Scale = FireballVfx.OverLife( 14f * k, 70f * k );
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, SparkGrey ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, RingAlpha ), new Curve.Frame( 0.5f, RingAlpha * 0.6f ), new Curve.Frame( 1f, 0f ) );
	}

	/// <summary>
	/// White-grey pixel sparks: small hard discs squashed into pills (Stretch is an aspect ratio) and faced along
	/// their velocity, born already flying along `throwDir` (a launch velocity, never a force: a force bends them
	/// into the swing over their first frames and the velocity-faced pills visibly turn), scattered a little around
	/// it and pulled down by gravity alone, shrinking and dimming to grey as they fade in the air. Alpha-blended and unlit, no bloom: they stay crisp under the retro filter instead of hazing.
	/// `light` -> `dark` is the colour over life (white to grey for bone, red to near-black for blood).
	/// </summary>
	private static void Sparks( GameObject root, Vector3 throwDir, int count, float k, Color light, Color dark, float lifeMin, float lifeMax )
	{
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "sparks", TexDot, additive: false, bloom: false );
		renderer.FaceVelocity = true;
		renderer.RotationOffset = SparkRotationOffset;
		var em = go.Components.Create<ParticleSphereEmitter>();
		// the emitter's radial speed is only the scatter around the throw, kept well under it so none fly backwards
		em.Radius = 2f * k; em.Velocity = SparkScatter * k; em.Rate = 0f; em.Burst = count; em.Loop = false;
		fx.MaxParticles = count + 2; fx.Damping = 1.2f;
		fx.InitialVelocity = throwDir * SparkLaunch * k;
		fx.Force = true; fx.ForceDirection = Vector3.Down * 325f * k;
		fx.Stretch = SparkStretch;
		fx.Lifetime = new ParticleFloat( lifeMin, lifeMax );
		fx.Scale = FireballVfx.OverLife( 1.4f * k, 0.7f * k );
		fx.Brightness = FireballVfx.Flicker( 1f, 1.3f );
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, light ), new Gradient.ColorFrame( 0.5f, light ), new Gradient.ColorFrame( 1f, dark ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.6f, 1f ), new Curve.Frame( 1f, 0f ) );
	}
}
