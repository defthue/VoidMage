using System;
using Sandbox;
using VoidMage.Rendering;
using VoidMage.Spells;

namespace VoidMage.Combat;

/// <summary>
/// What a prop breaking looks like at the prop, on every peer: a low, wide puff of grey-white pixel dust that
/// covers the frame the intact mesh is swapped for its pieces, and a handful of chips (wood: warm grey; clay: pale
/// grey) thrown along the blow as stretched pixel bars that fall and fade. Same construction as HitVfx: hard
/// alpha-blended discs, no glow, no light, one root object that destroys itself. The local attacker's own breaks
/// ripple the screen a little through the retro filter.
/// </summary>
public static class BreakVfx
{
	private const string TexDot = "vfx/polygonparticles_circle_01.png";
	private const string TexSmoke = "vfx/polygonparticles_smoke_01.png";

	private static readonly Color DustLight = new( 0.86f, 0.84f, 0.8f );
	private static readonly Color DustDark = new( 0.55f, 0.53f, 0.5f );
	private static readonly Color WoodLight = new( 0.66f, 0.55f, 0.42f );
	private static readonly Color WoodDark = new( 0.36f, 0.3f, 0.24f );
	private static readonly Color ClayLight = new( 0.8f, 0.76f, 0.7f );
	private static readonly Color ClayDark = new( 0.5f, 0.47f, 0.43f );

	/// <summary>Smoke sprites in the puff.</summary>
	[ConVar( "break_dust" )] public static int DustCount { get; set; } = 14;
	/// <summary>Grit: small hard motes thrown out of the cloud.</summary>
	[ConVar( "break_grit" )] public static int GritCount { get; set; } = 10;
	[ConVar( "break_chips" )] public static int ChipCount { get; set; } = 9;
	[ConVar( "break_dust_alpha" )] public static float DustAlpha { get; set; } = 0.9f;
	/// <summary>Size of one smoke sprite at birth (units); it blooms to ~2.8x that as it thins. heden's boulder cloud starts at 12.</summary>
	[ConVar( "break_dust_size" )] public static float DustSize { get; set; } = 8f;
	/// <summary>How fast the cloud pushes outward at birth before the damping stalls it.</summary>
	[ConVar( "break_dust_speed" )] public static float DustSpeed { get; set; } = 90f;
	/// <summary>Speed (units/s) the cloud is born with along the blow, on top of its radial spread: it drifts the way the swing went and the damping bleeds it off.</summary>
	[ConVar( "break_dust_drift" )] public static float DustDrift { get; set; } = 70f;

	/// <summary>A crate or a barrel came apart. `size` is half the prop's diagonal, `push` the blow's flat direction.</summary>
	public static void Wood( Scene scene, Vector3 centre, float size, Vector3 push, bool heavy, bool localAttacker )
	{
		var k = heavy ? 1.35f : 1f;
		var root = Root( scene, "break_wood", centre );
		Dust( root, size, k, push );
		Chips( root, push * 320f + Vector3.Down * 300f, ChipCount, k, WoodLight, WoodDark );
		if ( localAttacker ) RetroFilter.TriggerRipple( centre, k * 0.6f );
	}

	/// <summary>A vase shattered.</summary>
	public static void Clay( Scene scene, Vector3 centre, float size, Vector3 push, bool heavy, bool localAttacker )
	{
		var k = heavy ? 1.2f : 0.9f;
		var root = Root( scene, "break_clay", centre );
		Dust( root, size, k * 0.8f, push );
		Chips( root, push * 280f + Vector3.Down * 320f, ChipCount - 2, k, ClayLight, ClayDark );
		if ( localAttacker ) RetroFilter.TriggerRipple( centre, k * 0.5f );
	}

	/// <summary>Masonry came apart: the secret wall's doorway. A bigger, greyer cloud than a vase and no colour in it.</summary>
	public static void Stone( Scene scene, Vector3 centre, float size, Vector3 push, bool localAttacker )
	{
		var root = Root( scene, "break_stone", centre );
		Dust( root, size, 1.6f, push );
		Chips( root, push * 300f + Vector3.Down * 300f, ChipCount + 3, 1.3f, DustLight, DustDark );
		if ( localAttacker ) RetroFilter.TriggerRipple( centre, 0.9f );
	}

	/// <summary>A blow the furniture survives: chips off the contact and a breath of dust, no cloud.</summary>
	public static void WoodHit( Scene scene, Vector3 position, Vector3 push, bool localAttacker )
	{
		var root = Root( scene, "hit_wood", position );
		Chips( root, push * 260f + Vector3.Down * 280f, 6, 0.8f, WoodLight, WoodDark );
		Dust( root, 10f, 0.35f, push );
		if ( localAttacker ) RetroFilter.TriggerRipple( position, 0.4f );
	}

	/// <summary>A little dust where a piece shrinks away.</summary>
	public static void Puff( Scene scene, Vector3 at, float size )
	{
		var root = Root( scene, "break_puff", at );
		Dust( root, size, 0.55f, Vector3.Zero );
	}

	private static GameObject Root( Scene scene, string name, Vector3 position )
	{
		var go = new GameObject( true, name ) { WorldPosition = position };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "spellfire" );
		go.Components.Create<VfxLifetime>().Seconds = 1.6f;
		return go;
	}

	/// <summary>
	/// The cloud, heden's boulder-shatter recipe made bigger: soft smoke sprites born inside the prop that shoot
	/// outward for a few frames, stall under heavy damping and bloom to nearly three times their birth size while
	/// they rise, turn and thin from a pale grey to the dust's darker tone, plus a spray of small hard grit motes
	/// that fly out of it and fall. `size` is half the prop's diagonal: a barrel makes a barrel-sized cloud.
	/// </summary>
	private static void Dust( GameObject root, float size, float k, Vector3 push )
	{
		var count = (int)(DustCount * k);
		if ( count <= 0 ) return;
		var reach = MathF.Max( size, 20f ) / 40f;   // 1 for a crate, smaller for a vase, larger for the wide crate
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "dust", TexSmoke, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = size * 0.35f; em.Velocity = DustSpeed * k * reach; em.Rate = 0f; em.Burst = count; em.Loop = false;
			fx.MaxParticles = count + 2;
			// heavy damping so the cloud blooms and stalls rather than expanding like a blast (heden)
			fx.Damping = 5f;
			// the drift is a birth velocity, not a force: a force fights the damping that keeps the cloud from blasting
			// out and never showed; a velocity the sprites are born with reads at once and then bleeds off
			fx.InitialVelocity = push * DustDrift * k;
			fx.Force = true; fx.ForceScale = 1f; fx.ForceDirection = Vector3.Up * 45f;
			fx.Lifetime = new ParticleFloat( 0.7f, 1.2f );
			fx.Scale = FireballVfx.OverLife( DustSize * k * reach, DustSize * 2.8f * k * reach );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, DustLight ), new Gradient.ColorFrame( 0.4f, Color.Lerp( DustLight, DustDark, 0.5f ) ), new Gradient.ColorFrame( 1f, DustDark ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, DustAlpha ), new Curve.Frame( 0.3f, DustAlpha * 0.85f ), new Curve.Frame( 1f, 0f ) );
		}
		var grit = (int)(GritCount * k);
		if ( grit <= 0 ) return;
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "grit", TexDot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = size * 0.25f; em.Velocity = 110f * k * reach; em.Rate = 0f; em.Burst = grit; em.Loop = false;
			fx.MaxParticles = grit + 2; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = push * 60f * k + Vector3.Down * 220f;
			fx.Lifetime = new ParticleFloat( 0.4f, 0.7f );
			fx.Scale = FireballVfx.OverLife( 2.4f * k, 1.2f * k );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, DustLight ), new Gradient.ColorFrame( 1f, DustDark ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.6f, 1f ), new Curve.Frame( 1f, 0f ) );
		}
	}

	/// <summary>Chips: HitVfx's pixel sparks in the material's colour, thrown by `force`, falling as they fade.</summary>
	private static void Chips( GameObject root, Vector3 force, int count, float k, Color light, Color dark )
	{
		if ( count <= 0 ) return;
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "chips", TexDot, additive: false, bloom: false );
		renderer.FaceVelocity = true;
		renderer.RotationOffset = HitVfx.SparkRotationOffset;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 6f * k; em.Velocity = 70f * k; em.Rate = 0f; em.Burst = count; em.Loop = false;
		fx.MaxParticles = count + 2; fx.Damping = 1f;
		fx.Force = true; fx.ForceDirection = force * k;
		fx.Stretch = 2.4f;
		fx.Lifetime = new ParticleFloat( 0.3f, 0.5f );
		fx.Scale = FireballVfx.OverLife( 1.5f * k, 0.8f * k );
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, light ), new Gradient.ColorFrame( 0.5f, light ), new Gradient.ColorFrame( 1f, dark ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.6f, 1f ), new Curve.Frame( 1f, 0f ) );
	}
}
