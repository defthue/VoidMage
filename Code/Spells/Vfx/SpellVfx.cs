using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// The one door the cast code uses for looks, keyed by the spell: the fireball's rig (FireballVfx) in the spell's
/// palette, plus what the body style adds (StyleVfx: a crystal, lightning flashes, the void sphere), so no caller
/// ever knows which spell it is drawing. The bespoke pass replaces bodies and bursts behind these signatures.
/// </summary>
public static class SpellVfx
{
	/// <summary>A bolt's look in flight: the core, the trail, the ribbon, the void sphere when there is one, the bee's wings when it is one.</summary>
	public sealed class ProjectileLook
	{
		public FireballVfx.Rig Core;
		public FireballVfx.Rig Smoke;
		public TrailRenderer Ribbon;
		public ModelRenderer VoidSphere;
		public List<GameObject> Wings;
		public float Born;
	}

	private static SpellPalette PaletteOf( SpellDefinition spell ) => spell?.Palette ?? SpellPalette.Fire;

	// ------------------------------------------------------------------ the charge orb

	/// <summary>The charge at the gem: a bespoke recipe per spell (<see cref="ChargeVfx"/>), never the bolt parked in the air.</summary>
	public static FireballVfx.Rig BuildCharge( GameObject parent, SpellDefinition spell ) => ChargeVfx.Build( parent, spell );

	// ------------------------------------------------------------------ the bolt

	public static ProjectileLook BuildProjectile( GameObject go, SpellDefinition spell, float scale )
	{
		var p = PaletteOf( spell );
		var style = spell?.BodyStyle ?? SpellBodyStyle.Flame;
		var look = new ProjectileLook { Born = Time.Now };
		// the necrotic wisp is built from nothing else: the fire rig under it would make it a green fireball
		if ( style == SpellBodyStyle.Necrotic )
		{
			look.Core = FireballVfx.NewRig( go, "bolt_" + (spell?.Id ?? "spell"), p, 150f );
			look.Core.LightBrightness = 0.65f;
			StyleVfx.AddNecroticComet( look.Core, p, scale, MathF.Max( 6f, spell?.Radius ?? 9f ) );
		}
		else
		{
			look.Core = FireballVfx.BuildCore( go, "bolt_" + (spell?.Id ?? "spell"), shards: style is SpellBodyStyle.Flame or SpellBodyStyle.Orb, p );
			FireballVfx.SetScale( look.Core, scale );
			FireballVfx.SetSpread( look.Core, scale );
			switch ( style )
			{
				case SpellBodyStyle.Crystal: StyleVfx.AddCrystal( look.Core, p, scale ); break;
				case SpellBodyStyle.Arc: StyleVfx.AddLightningFlashes( look.Core, p ); break;
				case SpellBodyStyle.Orb:
					look.VoidSphere = StyleVfx.AddVoidSphere( look.Core, p, MathF.Max( 6f, spell?.Radius ?? 10f ) );
					look.Core.LightBrightness = 0.5f;
					break;
				case SpellBodyStyle.Swarm:
					look.Wings = StyleVfx.AddBee( look.Core, p, scale );
					look.Core.LightBrightness = 0.25f;
					break;
			}
		}

		var trail = spell?.TrailStyle ?? SpellTrailStyle.Smoke;
		if ( trail != SpellTrailStyle.None )
		{
			look.Smoke = FireballVfx.BuildSmokeTrail( go, p );
			FireballVfx.SetScale( look.Smoke, scale * (trail == SpellTrailStyle.Frost ? 0.6f : 1f) );
			FireballVfx.SetSpread( look.Smoke, scale );
		}

		// The ribbon: a short unlit streak filling the gaps between the tail's puffs at speed.
		var anchor = new GameObject( true, "spell_trail" ) { Parent = go };
		anchor.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var ribbon = anchor.Components.Create<TrailRenderer>();
		ribbon.Emitting = true;
		ribbon.MaxPoints = 96;
		ribbon.PointDistance = 6f;
		ribbon.CastShadows = false;
		ribbon.BlendMode = BlendMode.Normal;
		ribbon.Opaque = false;
		var texturing = TrailTextureConfig.Default;
		texturing.Material = Material.Load( "materials/blade_trail.vmat" );
		ribbon.Texturing = texturing;
		ribbon.LifeTime = style switch { SpellBodyStyle.Orb => 0.5f, SpellBodyStyle.Necrotic => 0.45f, SpellBodyStyle.Swarm => 0.08f, _ => 0.2f };
		var width = 8f * scale * 0.5f;
		ribbon.Width = new Curve( new Curve.Frame( 0f, width ), new Curve.Frame( 1f, width * 0.25f ) );
		var color = p.Trail.WithAlpha( 0.85f );
		ribbon.Color = new Gradient( new Gradient.ColorFrame( 0f, color ), new Gradient.ColorFrame( 1f, color.WithAlpha( 0f ) ) );
		look.Ribbon = ribbon;
		return look;
	}

	public static void Drive( ProjectileLook look, SpellProjectile.FlightPhase phase )
	{
		if ( look?.Core is null ) return;
		var age = Time.Now - look.Born;
		if ( phase == SpellProjectile.FlightPhase.Mine )
		{
			// parked: a slow breath
			look.Core.LightBrightness = 0.6f + 0.4f * MathF.Sin( Time.Now * 4f );
		}
		if ( look.VoidSphere.IsValid() ) StyleVfx.SetVoid( look.VoidSphere, 0.5f + 0.3f * MathF.Sin( age * 2.1f ), 0f );
		if ( look.Wings is not null ) StyleVfx.FlapWings( look.Wings, age );
		FireballVfx.Drive( look.Core );
	}

	/// <summary>The bolt is gone: the trail and the tail outlive it on their own clocks.</summary>
	public static void Release( ProjectileLook look )
	{
		if ( look is null ) return;
		if ( look.Ribbon.IsValid() ) look.Ribbon.Emitting = false;
		if ( look.Smoke is not null ) { FireballVfx.Release( look.Smoke, 2f ); look.Smoke = null; }
		look.Core = null;
		look.Ribbon = null;
		look.VoidSphere = null;
		look.Wings = null;
	}

	public static void Drive( FireballVfx.Rig rig ) => FireballVfx.Drive( rig );
	public static void SetSize( FireballVfx.Rig rig, float k ) => FireballVfx.SetSize( rig, k );
	public static void StopEmitting( FireballVfx.Rig rig ) => FireballVfx.StopEmitting( rig );
	public static void Destroy( FireballVfx.Rig rig ) => FireballVfx.Destroy( rig );

	// ------------------------------------------------------------------ one-shots

	/// <summary>Mana blue, the colour of the hotbar's mana bottle and its glow.</summary>
	public static readonly Color ManaColour = new( 0.22f, 0.42f, 1f );

	/// <summary>
	/// The staff sparks and nothing comes out: the cast was refused for want of mana (user, 2026-09-17, in place of the
	/// old refusal click - the sound comes later). A handful of mana-coloured pixel sparks spitting off the gem and
	/// dying at once: no flash, no ring, no light, so it reads as a misfire rather than a cast.
	/// </summary>
	public static void DenySparks( Scene scene, Vector3 at, float scale = 1f )
	{
		if ( !scene.IsValid() ) return;
		var root = new GameObject( true, "cast_deny" ) { WorldPosition = at };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = 0.6f;

		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, "deny_sparks", "vfx/polygonparticles_circle_01.png", additive: true, bloom: false );
		renderer.FaceVelocity = true;
		var em = go.Components.Create<ParticleSphereEmitter>();
		// out of the gem in every direction and falling: a fizzle, not a shot
		em.Radius = 1.5f * scale; em.Velocity = 70f * scale; em.Rate = 0f; em.Burst = 9f; em.Loop = false;
		fx.MaxParticles = 12; fx.Damping = 2.4f;
		fx.Force = true; fx.ForceDirection = Vector3.Down * 220f;
		fx.Stretch = 2.4f;
		fx.Lifetime = new ParticleFloat( 0.12f, 0.26f );
		fx.Scale = FireballVfx.OverLife( 1.5f * scale, 0.5f * scale );
		fx.Brightness = FireballVfx.Flicker( 0.9f, 1.4f );
		fx.Gradient = FireballVfx.GradientOverLife(
			new Gradient.ColorFrame( 0f, new Color( 0.62f, 0.76f, 1f ) ),
			new Gradient.ColorFrame( 0.5f, ManaColour ),
			new Gradient.ColorFrame( 1f, ManaColour * 0.25f ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0.9f ), new Curve.Frame( 0.5f, 0.7f ), new Curve.Frame( 1f, 0f ) );
	}

	public static void LaunchFlash( Scene scene, Vector3 at, SpellDefinition spell, float scale ) => FireballVfx.LaunchFlash( scene, at, scale, PaletteOf( spell ) );
	public static void CancelPoof( Scene scene, Vector3 at, SpellDefinition spell, float scale ) => FireballVfx.CancelPoof( scene, at, scale, PaletteOf( spell ) );

	/// <summary>The bolt spending itself, in the spell's palette, with the style's own touch (ice throws crystal chips, the void collapses, the necrotic bolt gives its souls up).</summary>
	public static void Burst( Scene scene, Vector3 position, Vector3 normal, SpellDefinition spell, float scale, ImpactKind kind )
	{
		var p = PaletteOf( spell );
		var style = spell?.BodyStyle ?? SpellBodyStyle.Flame;

		// The necrotic bolt does not explode: it bursts open and lets its gas out, so it gets its own set, not the fire's.
		if ( style == SpellBodyStyle.Necrotic )
		{
			NecroticBurst( scene, position, normal, p, scale, kind );
			if ( kind == ImpactKind.Fizzle ) return;
			VoidMage.Audio.GameAudio.Play( spell?.SoundImpact ?? FireballVfx.SoundImpact, position );
			if ( kind == ImpactKind.Body ) VoidMage.Audio.GameAudio.Play( VoidMage.Character.SwordSounds.HitBody, position );
			return;
		}

		FireballVfx.Burst( scene, position, normal, scale, kind, p );
		if ( kind != ImpactKind.Fizzle )
		{
			switch ( style )
			{
				case SpellBodyStyle.Crystal:
					StyleVfx.MeshBurst( scene, position, normal, "fx_crystalshard_01", 7, p.Core.WithAlpha( 0.9f ), 0.4f * scale / 0.5f, 260f, 0.7f, additive: false );
					break;
				case SpellBodyStyle.Orb:
					// the orb falls into its own point before the spikes throw out
					StyleVfx.VoidCollapse( scene, position, p, MathF.Max( 6f, (spell?.Radius ?? 10f) ) );
					StyleVfx.MeshBurst( scene, position, normal, "fx_sphere_spikes_01", 1, p.Core.WithAlpha( 0.7f ), 0.25f * scale, 0f, 0.35f, additive: true );
					break;
				case SpellBodyStyle.Arc:
					StyleVfx.SpawnArc( scene, new[] { position + Vector3.Up * 30f, position, position + normal * 40f }, p, scale, 0.15f );
					break;
				case SpellBodyStyle.Swarm:
					StyleVfx.MeshBurst( scene, position, normal, "fx_spark_01", 3, p.Core.WithAlpha( 0.9f ), 0.08f * scale / 0.3f, 200f, 0.3f, additive: true );
					break;
			}
		}
		if ( kind == ImpactKind.Fizzle ) return;
		var impact = spell?.SoundImpact ?? FireballVfx.SoundImpact;
		VoidMage.Audio.GameAudio.Play( impact, position );
		if ( kind == ImpactKind.Body ) VoidMage.Audio.GameAudio.Play( VoidMage.Character.SwordSounds.HitBody, position );
	}

	/// <summary>
	/// The siphon bolt landing: the wisp comes apart and gives up what it was carrying. A low cloud of green gas
	/// that spreads along the ground instead of blowing outward, the stolen motes let go upward, one cold ring,
	/// and a green stain on the floor. No fire chunks, no sparks raining down: nothing here is hot.
	/// </summary>
	private static void NecroticBurst( Scene scene, Vector3 position, Vector3 normal, SpellPalette p, float scale, ImpactKind kind )
	{
		var fizzle = kind == ImpactKind.Fizzle;
		var body = kind == ImpactKind.Body;

		if ( kind == ImpactKind.World ) GroundScorch.Place( position, normal, 18f * scale, tint: p.Core );

		var root = FireballVfx.NewRoot( null, "necrotic_impact" );
		root.WorldPosition = position;
		root.Components.Create<VfxLifetime>().Seconds = 2.6f;

		// The gas: a heavy cloud that sinks and crawls out along the floor, the whole read of the impact.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "gas", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 6f * scale; em.Velocity = (fizzle ? 60f : body ? 130f : 180f) * scale; em.Rate = 0f; em.Burst = fizzle ? 5f : 14f; em.Loop = false;
			fx.MaxParticles = 20; fx.Damping = 3f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -120f );
			fx.Lifetime = new ParticleFloat( 0.9f, 1.6f );
			fx.Scale = FireballVfx.OverLife( 12f * scale, 34f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.7f ) ),
				new Gradient.ColorFrame( 0.4f, Color.Lerp( p.Smoke, p.Core, 0.3f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.12f, 0.8f ), new Curve.Frame( 1f, 0f ) );
		}

		// The souls let go: bright motes drifting straight up out of the cloud and winking out.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "released", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 5f * scale; em.Velocity = (fizzle ? 40f : 90f) * scale; em.Rate = 0f; em.Burst = fizzle ? 5f : 14f; em.Loop = false;
			fx.MaxParticles = 20; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 260f );
			fx.Lifetime = new ParticleFloat( 0.7f, 1.4f );
			fx.Scale = FireballVfx.OverLife( 7f * scale, 1f * scale );
			fx.Brightness = FireballVfx.Flicker( 0.5f, 2.2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 1f ), new Curve.Frame( 1f, 0f ) );
		}

		if ( fizzle ) return;

		// Witchfire off the surface: a short lick of cold flame where it struck.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "lick", FireballVfx.TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 4f * scale; em.Velocity = (body ? 140f : 200f) * scale; em.Rate = 0f; em.Burst = 10f; em.Loop = false;
			fx.MaxParticles = 14; fx.Damping = 4f;
			fx.Force = true; fx.ForceDirection = normal * 200f * scale;
			fx.Lifetime = new ParticleFloat( 0.25f, 0.45f );
			fx.Scale = FireballVfx.OverLife( 16f * scale, 3f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 1.5f );
			fx.Gradient = FireballVfx.Banded( p.Hot, p.Core, p.Core.Darken( 0.6f ) );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
		}

		// One cold ring, gone fast: the only bright beat in the whole impact.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "ring", FireballVfx.TexRing, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 4; fx.Lifetime = 0.3f;
			fx.Scale = FireballVfx.OverLife( 8f * scale, 80f * scale );
			fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, p.Hot ), new Gradient.ColorFrame( 1f, p.Core ) );
			fx.Alpha = FireballVfx.OverLife( 0.9f, 0f );

			var light = go.Components.Create<ParticleLightRenderer>();
			light.Brightness = 1.5f;
			light.Scale = 44f * scale;
			light.LightColor = p.Core;
		}
	}

	/// <summary>A bolt reflected off the world: a small burst, no sound of a hit.</summary>
	public static void Bounce( Scene scene, Vector3 position, Vector3 normal, SpellDefinition spell, float scale )
	{
		FireballVfx.Burst( scene, position, normal, scale * 0.4f, ImpactKind.World, PaletteOf( spell ) );
	}


	/// <summary>A chain's arc through `points`.</summary>
	public static void ChainArc( Scene scene, SpellDefinition spell, IReadOnlyList<Vector3> points, float scale )
	{
		StyleVfx.SpawnArc( scene, points, PaletteOf( spell ), scale );
		if ( points.Count >= 2 && spell?.SoundImpact is { } snap )
		{
			for ( var i = 1; i < points.Count; i++ )
			{
				var h = VoidMage.Audio.GameAudio.Play( snap, points[i] );
				if ( h is not null ) h.Volume = 1f / i;
			}
		}
	}

	// ------------------------------------------------------------------ beam

	/// <summary>
	/// The Void Lance's look (redesigned 2026-09-21; it was a red laser: a constant tube, ten bright beads, orange sparks
	/// and smoke at the strike, which no longer matched the name). A LANCE: thin at the staff, fullest two thirds
	/// along, drawn to a point on what it hits. The ribbon is beam_void.vmat (a near-black core inside a violet body,
	/// alpha blended) inside a wider additive violet halo. It STABS out from the staff in LanceReachSeconds, throbs
	/// on every damage tick, and at the end of its window collapses from the staff end so the last thing seen is
	/// the point on the enemy. Four shedders slide along it dropping dark wisps and a few violet motes (they also
	/// give the flat billboard some volume end-on). The strike is a small dark sphere with a violet rim: no sparks, no
	/// smoke, no light pops (the user's hit-VFX taste).
	/// </summary>
	public sealed class BeamLook
	{
		public GameObject Root;
		public LineRenderer Halo;
		/// <summary>Seconds between damage ticks, for the throb.</summary>
		public float TickSeconds = 0.2f;
		public LineRenderer Ribbon;
		public PointLight StrikeLight;
		public PointLight MuzzleLight;
		public FireballVfx.Rig Strike;
		public FireballVfx.Rig Muzzle;
		public readonly List<GameObject> Beads = new();
		public float Width;
		public Color Tint;
	}

	/// <summary>Unparented on purpose: a LineRenderer's VectorPoints are object space, so an identity root keeps object == world.</summary>
	public static BeamLook BuildBeam( GameObject beam, SpellDefinition spell, float scale )
	{
		var palette = PaletteOf( spell );
		var root = new GameObject( true, "beam_ribbon" );
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );

		var line = root.Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
		line.CastShadows = false;
		line.Opaque = false;
		var texturing = TrailTextureConfig.Default;
		texturing.Material = Material.Load( "shaders/beam_void.vmat" );
		line.Texturing = texturing;
		var width = 17f * scale;
		line.Width = LanceWidth( width );
		line.Color = RibbonGradient( Color.White );   // the colours are the material's: a tint would muddy the dark core

		// the glow around it: additive, wider, faint
		var haloGo = new GameObject( true, "beam_halo" ) { Parent = root };
		haloGo.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var halo = haloGo.Components.Create<LineRenderer>();
		halo.UseVectorPoints = true;
		halo.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
		halo.CastShadows = false;
		halo.Opaque = false;
		halo.Additive = true;
		var haloTexturing = TrailTextureConfig.Default;
		haloTexturing.Material = Material.Load( "shaders/beam_white.vmat" );
		halo.Texturing = haloTexturing;
		halo.Width = LanceWidth( width * HaloWidthMul );
		halo.Color = RibbonGradient( palette.Core.WithAlpha( 0.22f ) );

		var strike = new GameObject( true, "beam_strike" ) { Parent = root };
		strike.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var strikeLight = strike.Components.Create<PointLight>();
		strikeLight.LightColor = palette.Core * 0.7f;
		strikeLight.Radius = 130f * scale + 50f;
		strikeLight.Shadows = false;

		var muzzle = new GameObject( true, "beam_muzzle" ) { Parent = root };
		muzzle.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var muzzleLight = muzzle.Components.Create<PointLight>();
		muzzleLight.LightColor = palette.Core * 0.6f;
		muzzleLight.Radius = 90f * scale + 30f;
		muzzleLight.Shadows = false;

		var strikeRig = BuildStrike( strike, palette, scale );
		var muzzleRig = BuildMuzzle( muzzle, palette, width );

		var look = new BeamLook { Root = root, Halo = halo, TickSeconds = MathF.Max( 0.05f, spell?.BeamTickSeconds ?? 0.2f ), Ribbon = line, StrikeLight = strikeLight, MuzzleLight = muzzleLight, Strike = strikeRig, Muzzle = muzzleRig, Width = width, Tint = palette.Core };
		for ( var i = 0; i < BeadCount; i++ ) look.Beads.Add( BuildBead( root, palette, width ) );
		return look;
	}

	private const int BeadCount = 4;
	private const float HaloWidthMul = 2.8f;
	/// <summary>Seconds the lance takes to stab from the staff to what it hits.</summary>
	private const float LanceReachSeconds = 0.07f;
	/// <summary>Seconds the lance takes to collapse onto its point at the end of the window.</summary>
	private const float LanceCloseSeconds = 0.16f;

	/// <summary>The lance's silhouette along its length: thin at the staff, fullest two thirds along, a point at the end.</summary>
	private static Curve LanceWidth( float w ) => new(
		new Curve.Frame( 0f, w * 0.22f ),
		new Curve.Frame( 0.3f, w * 0.6f ),
		new Curve.Frame( 0.66f, w ),
		new Curve.Frame( 0.9f, w * 0.5f ),
		new Curve.Frame( 1f, w * 0.03f ) );

	/// <summary>Where the lance lands: a small sphere of nothing with a violet rim, and motes that hang about it. Nothing flies, nothing smokes.</summary>
	private static FireballVfx.Rig BuildStrike( GameObject parent, SpellPalette p, float scale )
	{
		var rig = FireballVfx.NewRig( parent, "beam_strike", p, 150f );
		rig.LightBrightness = 0.5f;
		var root = rig.Root;

		{
			// the rim first, so the dark heart draws over its middle
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "strike_rim", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f * scale; em.Velocity = 0f; em.Rate = 16f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 12; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.16f, 0.26f );
			fx.Scale = new ParticleFloat( 44f * scale, 58f * scale );
			fx.Brightness = FireballVfx.Flicker( 0.5f, 0.9f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Ember );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "strike_void", FireballVfx.TexSoftSpot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 1f * scale; em.Velocity = 0f; em.Rate = 20f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 14; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.18f, 0.3f );
			fx.Scale = new ParticleFloat( 24f * scale, 32f * scale );
			fx.Gradient = FireballVfx.RandomBetween( p.Smoke, Color.Black );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "strike_motes", FireballVfx.TexCircleSmall, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 20f * scale; em.Velocity = 0f; em.Rate = 12f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 14; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 26f );
			fx.Lifetime = new ParticleFloat( 0.35f, 0.7f );
			fx.Scale = FireballVfx.OverLife( 3.5f * scale, 0.5f * scale );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Ember );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.8f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}
		return rig;
	}

	/// <summary>Where the lance leaves the staff: a small dark knot with a violet breath round it.</summary>
	private static FireballVfx.Rig BuildMuzzle( GameObject parent, SpellPalette p, float width )
	{
		var rig = FireballVfx.NewRig( parent, "beam_muzzle", p, 80f );
		rig.LightBrightness = 0.35f;
		var root = rig.Root;
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "breath", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = width * 0.2f; em.Velocity = 0f; em.Rate = 20f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 12; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.1f, 0.18f );
			fx.Scale = new ParticleFloat( width * 1.0f, width * 1.5f );
			fx.Brightness = FireballVfx.Flicker( 0.5f, 0.9f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Ember );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "knot", FireballVfx.TexSoftSpot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0.5f; em.Velocity = 0f; em.Rate = 16f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 8; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.12f, 0.2f );
			fx.Scale = new ParticleFloat( width * 0.5f, width * 0.7f );
			fx.Gradient = FireballVfx.RandomBetween( p.Smoke, Color.Black );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}
		return rig;
	}

	/// <summary>
	/// One shedder sliding along the lance: dark wisps that drift off it and sink, and the odd violet mote. World-space
	/// particles, so what it sheds stays where it was shed as the lance steers on. No light of its own (the old beads
	/// carried ten point lights between them).
	/// </summary>
	private static GameObject BuildBead( GameObject parent, SpellPalette p, float width )
	{
		var root = new GameObject( true, "beam_shed" ) { Parent = parent };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "wisp", FireballVfx.TexSoftSpot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = width * 0.25f; em.Velocity = 10f; em.Rate = 16f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 16; fx.LocalSpace = 0f; fx.Damping = 2.5f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -18f );
			fx.Lifetime = new ParticleFloat( 0.4f, 0.8f );
			fx.Scale = FireballVfx.OverLife( width * 0.55f, width * 1.1f );
			fx.Gradient = FireballVfx.RandomBetween( p.Smoke, (p.Trail * 0.35f).WithAlpha( 1f ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.45f ), new Curve.Frame( 1f, 0f ) );
		}
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "mote", FireballVfx.TexCircleSmall, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = width * 0.5f; em.Velocity = 14f; em.Rate = 7f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 8; fx.LocalSpace = 0f; fx.Damping = 3f;
			fx.Lifetime = new ParticleFloat( 0.3f, 0.6f );
			fx.Scale = FireballVfx.OverLife( 2.6f, 0.4f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Ember );
			fx.Alpha = FireballVfx.OverLife( 0.9f, 0f );
		}
		return root;
	}

	private static Gradient RibbonGradient( Color tint )
	{
		return new Gradient(
			new Gradient.ColorFrame( 0f, tint.WithAlpha( 0f ) ),
			new Gradient.ColorFrame( 0.035f, tint ),
			new Gradient.ColorFrame( 0.93f, tint ),
			new Gradient.ColorFrame( 1f, tint.WithAlpha( 0f ) ) );
	}

	/// <summary>
	/// Lay the lance along the ray's segments, between two fractions of its length: it stabs out from the staff when
	/// it is born and collapses onto its point as the window runs out (`window` 0 keeps it open). The width throbs on
	/// every damage tick. 16 points per leg so the silhouette and the end fades have vertices.
	/// </summary>
	public static void Drive( BeamLook look, IReadOnlyList<Vector3> points, bool active, float age, float window = 0f )
	{
		if ( look?.Ribbon is null || !look.Ribbon.IsValid() ) return;

		// A dead ray keeps its ends where they were: nothing snaps back to the world origin, and what was shed
		// finishes fading where the lance actually was.
		if ( !active || points is null || points.Count < 2 )
		{
			look.Ribbon.Enabled = false;
			if ( look.Halo.IsValid() ) look.Halo.Enabled = false;
			if ( look.StrikeLight.IsValid() ) look.StrikeLight.Enabled = false;
			if ( look.MuzzleLight.IsValid() ) look.MuzzleLight.Enabled = false;
			foreach ( var bead in look.Beads ) if ( bead.IsValid() ) StopShedding( bead );
			if ( look.Strike is not null ) { look.Strike.LightBrightness = 0f; FireballVfx.StopEmitting( look.Strike ); FireballVfx.Drive( look.Strike ); }
			if ( look.Muzzle is not null ) FireballVfx.StopEmitting( look.Muzzle );
			return;
		}

		// how much of the path is drawn: the stab out, then the collapse onto the point
		var reach = Math.Clamp( age / LanceReachSeconds, 0f, 1f );
		reach = 1f - (1f - reach) * (1f - reach);
		var closing = window > 0f ? 1f - Math.Clamp( (window - age) / LanceCloseSeconds, 0f, 1f ) : 0f;
		var from01 = closing * closing * 0.96f;
		var to01 = MathF.Max( from01 + 0.02f, reach );

		var total = 0f;
		for ( var i = 1; i < points.Count; i++ ) total += points[i - 1].Distance( points[i] );
		var start = PointAlong( points, total * from01 );
		var end = PointAlong( points, total * to01 );
		var landed = reach >= 1f;

		if ( look.StrikeLight.IsValid() )
		{
			look.StrikeLight.GameObject.WorldPosition = end;
			look.StrikeLight.Enabled = landed;
		}
		if ( look.MuzzleLight.IsValid() )
		{
			look.MuzzleLight.GameObject.WorldPosition = points[0];
			look.MuzzleLight.Enabled = closing < 0.5f;
		}
		if ( look.Strike is not null && look.Strike.Root.IsValid() )
		{
			look.Strike.Root.WorldPosition = end;
			look.Strike.LightBrightness = landed ? 0.5f : 0f;
			FireballVfx.Drive( look.Strike );
		}
		if ( look.Muzzle is not null && look.Muzzle.Root.IsValid() )
		{
			look.Muzzle.Root.WorldPosition = points[0];
			if ( closing > 0.5f ) FireballVfx.StopEmitting( look.Muzzle );
			FireballVfx.Drive( look.Muzzle );
		}

		// the drawn stretch, leg by leg
		var list = new List<Vector3>( points.Count * 16 );
		var along = 0f;
		float lo = total * from01, hi = total * to01;
		for ( var i = 1; i < points.Count; i++ )
		{
			var a = points[i - 1]; var b = points[i];
			var length = a.Distance( b );
			float legLo = MathF.Max( lo, along ), legHi = MathF.Min( hi, along + length );
			if ( legHi > legLo && length > 0.01f )
				for ( var k = 0; k < 16; k++ ) list.Add( Vector3.Lerp( a, b, (legLo + (legHi - legLo) * (k / 15f) - along) / length ) );
			along += length;
		}
		if ( list.Count < 2 ) { list.Clear(); list.Add( start ); list.Add( end ); }
		look.Ribbon.Enabled = true;
		look.Ribbon.VectorPoints = list;

		// a throb on every damage tick: it swells as the tick lands and settles before the next
		var sinceTick = age % look.TickSeconds;
		var throb = 1f + 0.35f * MathF.Exp( -sinceTick / 0.045f );
		var shiver = 1f + 0.05f * MathF.Sin( age * 47f );
		var thin = 1f - closing * 0.5f;
		var w = look.Width * throb * shiver * thin;
		look.Ribbon.Width = LanceWidth( w );
		if ( look.Halo.IsValid() )
		{
			look.Halo.Enabled = true;
			look.Halo.VectorPoints = list;
			look.Halo.Width = LanceWidth( w * HaloWidthMul );
		}

		// the shedders slide down the drawn stretch toward the point, wrapping, so the wisps are left all along it
		for ( var i = 0; i < look.Beads.Count; i++ )
		{
			var bead = look.Beads[i];
			if ( !bead.IsValid() ) continue;
			bead.Enabled = true;
			var f = ((i + 0.5f) / look.Beads.Count + age * 0.9f) % 1f;
			bead.WorldPosition = PointAlong( points, total * (from01 + (to01 - from01) * (0.06f + f * 0.88f)) );
		}
	}

	/// <summary>The point `distance` along a polyline.</summary>
	private static Vector3 PointAlong( IReadOnlyList<Vector3> points, float distance )
	{
		for ( var i = 1; i < points.Count; i++ )
		{
			var length = points[i - 1].Distance( points[i] );
			if ( distance <= length || i == points.Count - 1 )
				return length < 0.01f ? points[i] : Vector3.Lerp( points[i - 1], points[i], Math.Clamp( distance / length, 0f, 1f ) );
			distance -= length;
		}
		return points[^1];
	}

	/// <summary>A shedder stops shedding but what it shed lives out its life.</summary>
	private static void StopShedding( GameObject bead )
	{
		foreach ( var em in bead.Components.GetAll<ParticleEmitter>( FindMode.EverythingInSelfAndDescendants ) ) em.Enabled = false;
	}

	public static void Release( BeamLook look )
	{
		if ( look is null ) return;
		FireballVfx.Destroy( look.Strike );
		FireballVfx.Destroy( look.Muzzle );
		look.Strike = null;
		look.Muzzle = null;
		look.Beads.Clear();
		if ( look.Root.IsValid() ) look.Root.Destroy();
		look.Root = null;
		look.Ribbon = null;
	}
}
