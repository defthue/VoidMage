using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>What a fireball ended on. Decides the burst and its sounds on every peer.</summary>
public enum ImpactKind
{
	/// <summary>A wall, floor or prop: the full burst and a scorch mark.</summary>
	World,
	/// <summary>Something tagged enemy or npc: no scorch, a hit spray along the surface, the body hit layered on the impact.</summary>
	Body,
	/// <summary>Range or lifetime ran out: a quiet dissipate, no flash and no sound.</summary>
	Fizzle,
}

/// <summary>
/// The fireball's look: heden's layered ball of fire (white-hot heart, boiling flame discs, a fire
/// tail, fumes and embers around a point light; a smoke trail behind the bolt; flash, fireburst,
/// smoke, ring, embers and a scorch mark on impact) rebuilt at Void Mage's size with its own flair:
///   - a persistent body (two billboard sprites) under the particles, so the bolt is a solid glowing
///     disc for its whole flight whatever the emitters are doing that frame,
///   - the tail and embers shed per distance travelled, so a fast bolt draws a continuous streak,
///   - toon-banded colour ramps on the tail and the burst (three hard tones, the way the dungeon's
///     torches are drawn, so the retro dither gets flat islands instead of gradients),
///   - a ring of hard polygon shards orbiting the core (the arcane part of the fire),
///   - void-tinged smoke (violet grey, not soot) drawn only with the hexagon circle sprites,
///   - a light that flickers.
/// Every layer is code, no particle assets. A Rig remembers each layer's authored numbers so the
/// growing charge orb can be resized every frame without compounding. Nothing here is networked.
/// </summary>
public static class FireballVfx
{
	public static readonly Color Hot = new( 1f, 0.95f, 0.75f );
	public static readonly Color Core = new( 1f, 0.55f, 0.15f );
	public static readonly Color Ember = new( 1f, 0.8f, 0.3f );
	/// <summary>Void Mage's smoke: violet grey, the void behind the fire.</summary>
	public static readonly Color Smoke = new( 0.36f, 0.30f, 0.44f );
	public static readonly Color Tint = Core;

	internal const string TexSoftSpot = "vfx/polygonparticles_soft_spot.png";
	/// <summary>The big hexagon: flame chunks, shards and every fat smoke puff.</summary>
	internal const string TexCircle = "vfx/polygonparticles_circle_01.png";
	/// <summary>The small hexagon: the tail, wisps and hit chips.</summary>
	internal const string TexCircleSmall = "vfx/polygonparticles_circle_02.png";
	internal const string TexSparkle = "vfx/polygonparticles_sparkle.png";
	internal const string TexRing = "vfx/polygonparticles_ring_02.png";

	internal const float CoreLightRadius = 220f;
	public const string SoundCharge = "sounds/fireballcharge.sound";
	public const string SoundCast = "sounds/fireballcastfast.sound";
	public const string SoundImpact = "sounds/fireballimpact.sound";

	// ------------------------------------------------------------------ rig

	/// <summary>One layer with its authored numbers, so scaling is always relative to them. A particle layer or a body sprite.</summary>
	public sealed class Layer
	{
		public ParticleEffect Fx;
		public ParticleSphereEmitter Emitter;
		public ParticleFloat BaseScale;
		public float BaseRadius, BaseVelocity;
		public Vector3 BaseForce;
		public ParticleFloat BaseRate;
		public ParticleFloat BaseRateOverDistance;
		public int BaseMax;

		public SpriteRenderer Sprite;
		public float BaseSize;
		public Color BaseColor;
	}

	/// <summary>What the orbiting shards read each frame (written by Drive, read on the particle thread).</summary>
	public sealed class OrbitState
	{
		public Vector3 Center;
		public float Radius = 14f;
		public float Time;
	}

	/// <summary>
	/// A mesh piece of the rig (the ice crystal, the void sphere, a bee) with its authored local scale, so the rig's
	/// SetScale grows it with everything else and StopEmitting takes it away with everything else. Without this the
	/// meshes are invisible to the rig API and hang in the world after the rig is done with them.
	/// </summary>
	public sealed class MeshLayer
	{
		public GameObject Go;
		public ModelRenderer Renderer;
		public Vector3 BaseScale = Vector3.One;
	}

	/// <summary>A moving part of a rig that owns distances of its own (an orbiting bee, a spiralling wisp) and must grow with the charge.</summary>
	public interface IRigScaled
	{
		void SetRigScale( float k );
	}

	public sealed class Rig
	{
		public GameObject Root;
		public PointLight Light;
		public float BaseLightRadius;
		public float LightBrightness = 1f;
		public readonly List<Layer> Layers = new();
		public readonly List<MeshLayer> Meshes = new();
		public readonly List<IRigScaled> Scaled = new();
		public OrbitState Orbit;
		public float BaseOrbitRadius;
		public GatherState Gather;
		public float BaseGatherRadius;
		public float Scale = 1f;
		public float Seed;
		public SpellPalette Palette = SpellPalette.Fire;

		/// <summary>Registers a mesh child so the rig can scale and hide it. Safe with a null renderer (an uncompiled model).</summary>
		public ModelRenderer AddMesh( ModelRenderer r )
		{
			if ( !r.IsValid() ) return r;
			Meshes.Add( new MeshLayer { Go = r.GameObject, Renderer = r, BaseScale = r.GameObject.LocalScale } );
			return r;
		}
	}

	// ------------------------------------------------------------------ shards

	/// <summary>Carries a shard around the core on its own tilted ring. Position is owned here, not by velocity.</summary>
	public sealed class OrbitController : ParticleController
	{
		public OrbitState State;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null ) return;

			float r1 = Hash( p.Random01 ), r2 = Hash( p.Random01 * 3.1f + 0.7f ), r3 = Hash( p.Random01 * 7.7f + 1.3f );
			// Each shard: its own ring tilt and phase, all spinning the same way at slightly different speeds.
			float tilt = (r1 - 0.5f) * 1.6f;
			var axisA = new Vector3( MathF.Cos( r2 * 6.2832f ), MathF.Sin( r2 * 6.2832f ), 0f );
			var axisB = Vector3.Cross( Vector3.Up, axisA ).Normal * MathF.Cos( tilt ) + Vector3.Up * MathF.Sin( tilt );
			float angle = r3 * 6.2832f + s.Time * (4.5f + 2f * r1);
			p.Position = s.Center + (axisA * MathF.Cos( angle ) + axisB * MathF.Sin( angle )) * s.Radius;
			p.Velocity = Vector3.Zero;
		}

		private static float Hash( float x )
		{
			float v = MathF.Sin( x * 127.1f + 311.7f ) * 43758.5453f;
			return v - MathF.Floor( v );
		}

		internal static float Hash01( float x ) => Hash( x );
	}

	// ------------------------------------------------------------------ gathering

	/// <summary>What the gathering motes read each frame (written by Drive, read on the particle thread).</summary>
	public sealed class GatherState
	{
		public Vector3 Center;
		/// <summary>Where a mote is born, as a distance from the centre.</summary>
		public float Radius = 55f;
		public float Time;
	}

	/// <summary>
	/// Pulls each particle in from a shell to the centre over its life, with a slow twist on the way, so a charge
	/// reads as the element being drawn out of the air rather than sprayed. Position is owned here: the emitter's
	/// own velocity is ignored. Each particle keeps one stable direction, hashed from its randoms, so it follows
	/// the centre when the staff moves instead of streaking off its birth point.
	/// </summary>
	public sealed class GatherController : ParticleController
	{
		public GatherState State;
		/// <summary>Degrees of twist over the whole fall in.</summary>
		public float SpiralDegrees { get; set; } = 90f;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null ) return;

			var life = MathF.Max( 0.01f, p.DeathTime - p.BornTime );
			var t = Math.Clamp( p.Age / life, 0f, 1f );

			// its own point on the shell, stable for the particle's whole life
			float r1 = OrbitController.Hash01( p.Random01 ), r2 = OrbitController.Hash01( p.Random01 * 5.3f + 2.1f );
			var yaw = r1 * 360f + t * SpiralDegrees;
			var pitch = (r2 - 0.5f) * 150f;
			var dir = Rotation.From( pitch, yaw, 0f ).Forward;

			// fast at the rim, slowing as it closes: the element is drawn in, not thrown
			var fall = MathF.Pow( 1f - t, 1.6f );
			p.Position = s.Center + dir * (s.Radius * fall);
			p.Velocity = Vector3.Zero;
		}
	}

	// ------------------------------------------------------------------ helpers (heden's sprite layer idiom)

	private static readonly Dictionary<string, Sprite> _sprites = new();

	private static Sprite SpriteFor( string path )
	{
		if ( _sprites.TryGetValue( path, out var cached ) ) return cached;
		var sprite = Sprite.FromTexture( Texture.Load( path ) );
		_sprites[path] = sprite;
		return sprite;
	}

	internal static (GameObject Go, ParticleEffect Fx, ParticleSpriteRenderer Renderer) AddSpriteLayer( GameObject parent, string name, string texturePath, bool additive, bool bloom )
	{
		var go = new GameObject( true, name ) { Parent = parent };
		var fx = go.Components.Create<ParticleEffect>();
		fx.ApplyColor = true;
		fx.ApplyAlpha = true;
		fx.ApplyRotation = true;
		fx.ApplyShape = true;
		var renderer = go.Components.Create<ParticleSpriteRenderer>();
		renderer.Sprite = SpriteFor( texturePath );
		renderer.Additive = additive;
		renderer.Lighting = false;
		renderer.Shadows = false;
		renderer.Alignment = ParticleSpriteRenderer.BillboardAlignment.LookAtCamera;
		renderer.DepthFeather = 4f;
		renderer.RenderOptions.Bloom = bloom;
		return (go, fx, renderer);
	}

	/// <summary>A single camera-facing sprite that is simply always there: the bolt's body under the particles.</summary>
	internal static Layer AddBodySprite( Rig rig, string name, string texturePath, float size, Color color, bool bloom )
	{
		var go = new GameObject( true, name ) { Parent = rig.Root };
		go.LocalPosition = Vector3.Zero;
		var sr = go.Components.Create<SpriteRenderer>();
		sr.Sprite = SpriteFor( texturePath );
		sr.Size = size;
		sr.Color = color;
		sr.Additive = true;
		sr.Lighting = false;
		sr.Shadows = false;
		sr.Billboard = SpriteRenderer.BillboardMode.Always;
		sr.DepthFeather = 4f;
		sr.RenderOptions.Bloom = bloom;

		var layer = new Layer { Sprite = sr, BaseSize = size, BaseColor = color };
		rig.Layers.Add( layer );
		return layer;
	}

	internal static ParticleFloat OverLife( float from, float to ) => new()
	{
		Type = ParticleFloat.ValueType.Range,
		Evaluation = ParticleFloat.EvaluationType.Life,
		ConstantA = from,
		ConstantB = to,
	};

	internal static ParticleFloat CurveOverLife( params Curve.Frame[] frames ) => new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( frames ),
	};

	internal static ParticleFloat Flicker( float min, float max ) => new()
	{
		Type = ParticleFloat.ValueType.Range,
		Evaluation = ParticleFloat.EvaluationType.Frame,
		ConstantA = min,
		ConstantB = max,
	};

	internal static ParticleGradient RandomBetween( Color a, Color b ) => new()
	{
		Type = ParticleGradient.ValueType.Range,
		Evaluation = ParticleGradient.EvaluationType.Particle,
		ConstantA = a,
		ConstantB = b,
	};

	internal static ParticleGradient GradientOverLife( params Gradient.ColorFrame[] frames ) => new()
	{
		Type = ParticleGradient.ValueType.Gradient,
		Evaluation = ParticleGradient.EvaluationType.Life,
		GradientA = new Gradient( frames ),
	};

	/// <summary>Void Mage's ramp: three flat tones with hard cuts, no gradient for the dither to chew.</summary>
	internal static ParticleGradient Banded( Color a, Color b, Color c ) => GradientOverLife(
		new Gradient.ColorFrame( 0f, a ), new Gradient.ColorFrame( 0.32f, a ),
		new Gradient.ColorFrame( 0.34f, b ), new Gradient.ColorFrame( 0.66f, b ),
		new Gradient.ColorFrame( 0.68f, c ), new Gradient.ColorFrame( 1f, c ) );

	/// <summary>The smoke ramp: lit by the fire at birth, a paler puff, then the void's violet.</summary>
	internal static ParticleGradient SmokeRamp() => SmokeRamp( SpellPalette.Fire );

	internal static ParticleGradient SmokeRamp( SpellPalette p ) => GradientOverLife(
		new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.5f ) ),
		new Gradient.ColorFrame( 0.25f, Color.Lerp( p.Smoke, Color.White, 0.2f ) ),
		new Gradient.ColorFrame( 1f, p.Smoke ) );

	internal static ParticleFloat SoftAlpha() => CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.12f, 1f ), new Curve.Frame( 1f, 0f ) );

	internal static Layer Register( Rig rig, ParticleEffect fx, ParticleSphereEmitter em )
	{
		var layer = new Layer
		{
			Fx = fx,
			Emitter = em,
			BaseScale = fx.Scale,
			BaseRadius = em.IsValid() ? em.Radius : 0f,
			BaseVelocity = em.IsValid() ? em.Velocity : 0f,
			BaseForce = fx.ForceDirection,
			BaseRate = em.IsValid() ? em.Rate : default,
			BaseRateOverDistance = em.IsValid() ? em.RateOverDistance : default,
			BaseMax = fx.MaxParticles,
		};
		rig.Layers.Add( layer );
		return layer;
	}

	internal static GameObject NewRoot( GameObject parent, string name )
	{
		var go = new GameObject( true, name );
		if ( parent.IsValid() ) { go.Parent = parent; go.LocalPosition = Vector3.Zero; }
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "spellfire" );
		return go;
	}

	/// <summary>An empty rig with its root and its light: what every bespoke recipe (ChargeVfx) starts from.</summary>
	internal static Rig NewRig( GameObject parent, string name, SpellPalette p, float lightRadius = CoreLightRadius )
	{
		var rig = new Rig { Root = NewRoot( parent, name ), Seed = Game.Random.Float( 0f, 100f ), Palette = p };
		rig.Light = rig.Root.Components.Create<PointLight>();
		rig.Light.LightColor = p.Core;
		rig.Light.Radius = lightRadius;
		rig.Light.Shadows = false;
		rig.BaseLightRadius = lightRadius;
		return rig;
	}

	// ------------------------------------------------------------------ the ball

	/// <summary>The fireball itself, at authored size 1: the charge orb and the bolt's body. Scale it with SetSize.</summary>
		public static Rig BuildCore( GameObject parent, string name, bool shards ) => BuildCore( parent, name, shards, SpellPalette.Fire );

public static Rig BuildCore( GameObject parent, string name, bool shards, SpellPalette p )
	{
		var rig = new Rig { Root = NewRoot( parent, name ), Seed = Game.Random.Float( 0f, 100f ), Palette = p };
		var root = rig.Root;

		// The body: a glow and a hard disc that never depend on an emitter having fired this frame.
		// This is what the eye tracks down a hallway; the particles dress it.
		AddBodySprite( rig, "body_glow", TexSoftSpot, 40f, p.Hot.WithAlpha( 0.9f ), bloom: true );
		AddBodySprite( rig, "body_disc", TexCircle, 22f, p.Core, bloom: true );

		// White-hot heart: small, dense, additive.
		{
			var (go, fx, _) = AddSpriteLayer( root, "core_hot", TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 24f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 32; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.08f, 0.14f );
			fx.Scale = new ParticleFloat( 26f, 34f );
			fx.Gradient = RandomBetween( p.Hot, p.Core );
			fx.Alpha = SoftAlpha();
			Register( rig, fx, em );
		}

		// Flame body: hard polygon discs boiling outward and shrinking, the cartoon ball silhouette.
		{
			var (go, fx, _) = AddSpriteLayer( root, "core_flame", TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 6f; em.Velocity = 15f; em.Rate = 30f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 48; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.15f, 0.3f );
			fx.Scale = OverLife( 24f, 6f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = RandomBetween( p.Core, p.Core.Darken( 0.35f ) );
			fx.Alpha = SoftAlpha();
			Register( rig, fx, em );
		}

		// Fire tail: world space, shed per distance so flight strings it out into one continuous
		// streak (a per-second rate at 1800 u/s leaves a dotted line). The small rate keeps the
		// standing charge orb breathing. Banded ramp; the sprites smear along their motion.
		{
			var (go, fx, renderer) = AddSpriteLayer( root, "tail_fire", TexCircleSmall, additive: true, bloom: false );
			renderer.MotionBlur = true; renderer.LeadingTrail = true; renderer.BlurAmount = 0.5f;
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 4f; em.Velocity = 10f; em.Rate = 10f; em.RateOverDistance = 0.08f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 96; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.12f, 0.22f );
			fx.Scale = OverLife( 16f, 2f );
			fx.Gradient = Banded( p.Hot, p.Core, p.Core.Darken( 0.6f ) );
			fx.Alpha = SoftAlpha();
			Register( rig, fx, em );
		}

		// Fumes: translucent hexagon puffs rolling off the ball, void-tinged.
		{
			var (go, fx, _) = AddSpriteLayer( root, "fumes", TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 8f; em.Velocity = 18f; em.Rate = 10f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 16; fx.LocalSpace = 0f; fx.Damping = 1f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 30f );
			fx.Lifetime = new ParticleFloat( 0.4f, 0.7f );
			fx.Scale = OverLife( 8f, 22f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.4f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.45f ), new Curve.Frame( 1f, 0f ) );
			Register( rig, fx, em );
		}

		// Embers: sparse flickering sparks drifting off the ball, more of them the further it flies.
		{
			var (go, fx, _) = AddSpriteLayer( root, "embers", TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 8f; em.Velocity = 40f; em.Rate = 6f; em.RateOverDistance = 0.02f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 32; fx.LocalSpace = 0f; fx.Damping = 1f;
			fx.Lifetime = new ParticleFloat( 0.3f, 0.6f );
			fx.Scale = new ParticleFloat( 3f, 6f );
			fx.Brightness = Flicker( 0.5f, 2f );
			fx.Gradient = RandomBetween( p.Ember, p.Hot );
			fx.Alpha = SoftAlpha();
			Register( rig, fx, em );
		}

		// Shards: hard polygon chips circling the core on tilted rings, each living under a second so
		// the ring is always renewing. The arcane part of the fire.
		if ( shards )
		{
			var (go, fx, _) = AddSpriteLayer( root, "shards", TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 7f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 6; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.6f, 0.9f );
			fx.Scale = new ParticleFloat( 4f, 6f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = Flicker( 0.9f, 1.6f );
			fx.Gradient = RandomBetween( p.Ember, p.Hot );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 1f ), new Curve.Frame( 0.8f, 1f ), new Curve.Frame( 1f, 0f ) );
			rig.Orbit = new OrbitState { Center = root.WorldPosition, Radius = 14f };
			rig.BaseOrbitRadius = rig.Orbit.Radius;
			var orbit = go.Components.Create<OrbitController>();
			orbit.State = rig.Orbit;
			Register( rig, fx, em );
		}

		rig.Light = root.Components.Create<PointLight>();
		rig.Light.LightColor = p.Core;
		rig.Light.Radius = CoreLightRadius;
		rig.Light.Shadows = false;
		rig.BaseLightRadius = CoreLightRadius;

		return rig;
	}

	/// <summary>Void smoke hexagons shed along the flight path (rate per distance, not per second).</summary>
		public static Rig BuildSmokeTrail( GameObject parent ) => BuildSmokeTrail( parent, SpellPalette.Fire );

public static Rig BuildSmokeTrail( GameObject parent, SpellPalette p )
	{
		var rig = new Rig { Root = NewRoot( parent, "vfx_smoke_trail" ), Palette = p };
		var (go, fx, _) = AddSpriteLayer( rig.Root, "smoke_puffs", TexCircle, additive: false, bloom: false );
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 4f; em.Velocity = 15f; em.Rate = 0f; em.Burst = 0f; em.RateOverDistance = 0.012f; em.Loop = true;
		fx.PreWarm = 0f; fx.MaxParticles = 32; fx.LocalSpace = 0f; fx.Damping = 2f;
		fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 40f );
		fx.Lifetime = new ParticleFloat( 0.45f, 0.8f );
		fx.Scale = OverLife( 7f, 15f );
		fx.Roll = new ParticleFloat( 0f, 360f );
		fx.Gradient = SmokeRamp( p );
		fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.12f, 0.55f ), new Curve.Frame( 1f, 0f ) );
		Register( rig, fx, em );
		return rig;
	}

	// ------------------------------------------------------------------ per frame

	/// <summary>One frame: the light and the body flicker together, the shards know where the core is.</summary>
	public static void Drive( Rig rig )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		var t = Time.Now;
		var flick = 0.88f + 0.08f * MathF.Sin( t * 23f + rig.Seed ) + 0.06f * MathF.Sin( t * 7.3f + rig.Seed * 1.7f );
		if ( rig.Light.IsValid() )
			rig.Light.LightColor = rig.Palette.Core * (rig.LightBrightness * flick);
		foreach ( var l in rig.Layers )
		{
			if ( !l.Sprite.IsValid() ) continue;
			var c = l.BaseColor * (0.85f + 0.15f * flick);
			l.Sprite.Color = c.WithAlpha( l.BaseColor.a );
		}
		if ( rig.Orbit is not null )
		{
			rig.Orbit.Center = rig.Root.WorldPosition;
			rig.Orbit.Time = t;
		}
		if ( rig.Gather is not null )
		{
			rig.Gather.Center = rig.Root.WorldPosition;
			rig.Gather.Time = t;
		}
	}

	// ------------------------------------------------------------------ sizing

	private static ParticleFloat Multiply( ParticleFloat b, float k )
	{
		var p = b;
		p.ConstantA *= k;
		p.ConstantB *= k;
		p.ConstantValue *= k;
		if ( p.Type == ParticleFloat.ValueType.Curve && p.CurveA.Length > 0 )
		{
			var frames = new Curve.Frame[p.CurveA.Length];
			for ( var i = 0; i < frames.Length; i++ )
			{
				var f = p.CurveA[i];
				frames[i] = new Curve.Frame( f.Time, f.Value * k );
			}
			p.CurveA = new Curve( frames );
		}
		return p;
	}

	/// <summary>Sprite sizes, the body, the light and the shard ring, relative to the authored size.</summary>
	public static void SetScale( Rig rig, float k )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		rig.Scale = k;
		foreach ( var l in rig.Layers )
		{
			if ( l.Fx.IsValid() ) l.Fx.Scale = Multiply( l.BaseScale, k );
			if ( l.Sprite.IsValid() ) l.Sprite.Size = l.BaseSize * k;
		}
		foreach ( var m in rig.Meshes )
		{
			if ( m.Go.IsValid() ) m.Go.LocalScale = m.BaseScale * k;
		}
		foreach ( var s in rig.Scaled ) s.SetRigScale( k );
		if ( rig.Light.IsValid() ) rig.Light.Radius = rig.BaseLightRadius * k;
		if ( rig.Orbit is not null ) rig.Orbit.Radius = rig.BaseOrbitRadius * k;
		if ( rig.Gather is not null ) rig.Gather.Radius = rig.BaseGatherRadius * k;
	}

	/// <summary>Emission geometry (radius, launch speed, drift force): a small ball must not scatter over a big volume.</summary>
	public static void SetSpread( Rig rig, float k )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		foreach ( var l in rig.Layers )
		{
			if ( l.Emitter.IsValid() ) { l.Emitter.Radius = l.BaseRadius * k; l.Emitter.Velocity = l.BaseVelocity * k; }
			if ( l.Fx.IsValid() ) l.Fx.ForceDirection = l.BaseForce * k;
		}
	}

	/// <summary>Particle population: a tiny orb spawns a few sparks, not the full swarm.</summary>
	public static void SetEmission( Rig rig, float k )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		foreach ( var l in rig.Layers )
		{
			if ( !l.Emitter.IsValid() || !l.Fx.IsValid() ) continue;
			l.Emitter.Rate = Multiply( l.BaseRate, k );
			l.Emitter.RateOverDistance = Multiply( l.BaseRateOverDistance, k );
			l.Fx.MaxParticles = Math.Clamp( (int)MathF.Ceiling( l.BaseMax * k ), 1, l.BaseMax );
		}
	}

	/// <summary>Everything at once (the growing charge orb).</summary>
	public static void SetSize( Rig rig, float k )
	{
		SetScale( rig, k );
		SetSpread( rig, k );
		SetEmission( rig, MathF.Max( k, 0.25f ) );
	}

	// ------------------------------------------------------------------ lifecycle

	/// <summary>Emitters stop, the body and every mesh go; live particles die on their own clocks.</summary>
	public static void StopEmitting( Rig rig )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		foreach ( var l in rig.Layers )
		{
			if ( l.Sprite.IsValid() ) l.Sprite.Enabled = false;
			if ( !l.Emitter.IsValid() ) continue;
			l.Emitter.Rate = 0f;
			l.Emitter.RateOverDistance = 0f;
			l.Emitter.Loop = false;
		}
		// a mesh is not a particle: it would otherwise hang in the air, whole and opaque, until the root is destroyed
		foreach ( var m in rig.Meshes )
		{
			if ( m.Renderer.IsValid() ) m.Renderer.Enabled = false;
		}
	}

	/// <summary>Let go of a rig so it outlives its owner: unparented, no more emission, gone after `seconds`.</summary>
	public static void Release( Rig rig, float seconds )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		// the scene is going down (play stopped with bolts in the air): nothing may be re-parented or given a
		// component now, the engine asserts on it ("active scene is ... not") — the rig goes with its bolt
		var scene = rig.Root.Scene;
		if ( !scene.IsValid() || Game.ActiveScene != scene ) { rig.Root.Destroy(); return; }
		rig.Root.SetParent( null, true );
		StopEmitting( rig );
		if ( rig.Light.IsValid() ) rig.Light.Enabled = false;
		var life = rig.Root.Components.GetOrCreate<VfxLifetime>();
		life.Seconds = seconds;
	}

	public static void Destroy( Rig rig )
	{
		if ( rig is null || !rig.Root.IsValid() ) return;
		rig.Root.Destroy();
	}

	// ------------------------------------------------------------------ one-shots

	/// <summary>The pop at the staff when the bolt leaves: a shrinking flash and a few sparks.</summary>
		public static void LaunchFlash( Scene scene, Vector3 position, float scale ) => LaunchFlash( scene, position, scale, SpellPalette.Fire );

public static void LaunchFlash( Scene scene, Vector3 position, float scale, SpellPalette p )
	{
		var root = NewRoot( null, "fireball_launch" );
		root.WorldPosition = position;
		root.Components.Create<VfxLifetime>().Seconds = 0.6f;

		{
			var (go, fx, _) = AddSpriteLayer( root, "flash", TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 4; fx.Lifetime = 0.2f;
			fx.Scale = OverLife( 30f * scale, 8f * scale );
			fx.Brightness = 2f;
			fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, p.Core ) );
			fx.Alpha = OverLife( 1f, 0f );
		}
		{
			var (go, fx, _) = AddSpriteLayer( root, "sparks", TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 4f * scale; em.Velocity = 120f * scale; em.Rate = 0f; em.Burst = 5f; em.Loop = false;
			fx.MaxParticles = 8; fx.Damping = 2f;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.35f );
			fx.Scale = new ParticleFloat( 3f * scale, 6f * scale );
			fx.Brightness = Flicker( 0.6f, 2f );
			fx.Gradient = RandomBetween( p.Ember, p.Hot );
			fx.Alpha = OverLife( 1f, 0f );
		}
	}

	/// <summary>A let-down charge: a puff of void smoke where the orb was.</summary>
		public static void CancelPoof( Scene scene, Vector3 position, float scale ) => CancelPoof( scene, position, scale, SpellPalette.Fire );

public static void CancelPoof( Scene scene, Vector3 position, float scale, SpellPalette p )
	{
		var root = NewRoot( null, "fireball_cancel" );
		root.WorldPosition = position;
		root.Components.Create<VfxLifetime>().Seconds = 1.2f;

		var (go, fx, _) = AddSpriteLayer( root, "poof", TexCircle, additive: false, bloom: false );
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 2f * scale; em.Velocity = 40f * scale; em.Rate = 0f; em.Burst = 3f; em.Loop = false;
		fx.MaxParticles = 4; fx.Damping = 2f;
		fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 30f );
		fx.Lifetime = new ParticleFloat( 0.5f, 0.7f );
		fx.Scale = OverLife( 7f * scale, 15f * scale );
		fx.Roll = new ParticleFloat( 0f, 360f );
		fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.3f ) ), new Gradient.ColorFrame( 1f, p.Smoke ) );
		fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.5f ), new Curve.Frame( 1f, 0f ) );
	}

	/// <summary>
	/// The bolt spending itself. World: flash, fireburst, void smoke, wisps, a ring, arcing embers,
	/// shards and a scorch mark. Body: the flash and a tighter burst, a spray of chips off the surface,
	/// no ring, no scorch (a mark belongs on the ground, not mid-air where a monster stood). Fizzle:
	/// a few puffs and embers, nothing bright.
	/// </summary>
		public static void Burst( Scene scene, Vector3 position, Vector3 normal, float scale, ImpactKind kind ) => Burst( scene, position, normal, scale, kind, SpellPalette.Fire );

public static void Burst( Scene scene, Vector3 position, Vector3 normal, float scale, ImpactKind kind, SpellPalette p )
	{
		var root = NewRoot( null, "fireball_impact" );
		root.WorldPosition = position;
		root.Components.Create<VfxLifetime>().Seconds = 2.2f;

		var world = kind == ImpactKind.World;
		var body = kind == ImpactKind.Body;
		var fizzle = kind == ImpactKind.Fizzle;

		if ( world ) GroundScorch.Place( position, normal, 15f * scale, tint: p == SpellPalette.Fire ? null : p.Ember );

		// The overexposed pop of contact, with the blast's light riding it.
		if ( !fizzle )
		{
			var (go, fx, _) = AddSpriteLayer( root, "flash", TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 4; fx.Lifetime = 0.15f;
			fx.Scale = OverLife( 30f * scale, (body ? 50f : 70f) * scale );
			fx.Brightness = 2f;
			fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, p.Hot ) );
			fx.Alpha = OverLife( 1f, 0f );

			var light = go.Components.Create<ParticleLightRenderer>();
			light.Brightness = 2f;
			light.Scale = 48f * scale;
			light.LightColor = p.Core;
		}

		// Chunks of flame thrown outward, banded ramp, cooling as they shrink. Tighter on a body.
		if ( !fizzle )
		{
			var (go, fx, _) = AddSpriteLayer( root, "fireburst", TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 4f * scale; em.Velocity = (body ? 160f : 260f) * scale; em.Rate = 0f; em.Burst = 16f; em.Loop = false;
			fx.MaxParticles = 24; fx.Damping = 3f;
			fx.Lifetime = new ParticleFloat( 0.25f, 0.45f );
			fx.Scale = OverLife( 22f * scale, 4f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = Banded( p.Hot, p.Core, p.Core.Darken( 0.6f ) );
			fx.Alpha = OverLife( 1f, 0f );
		}

		// Hit spray: hard chips off the struck surface along its normal, the fire splashing off flesh.
		if ( body )
		{
			var (go, fx, _) = AddSpriteLayer( root, "hit_spray", TexCircleSmall, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f * scale; em.Velocity = 90f * scale; em.Rate = 0f; em.Burst = 12f; em.Loop = false;
			fx.MaxParticles = 16; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = normal * 500f * scale + Vector3.Down * 150f;
			fx.Stretch = 0.15f;
			fx.Lifetime = new ParticleFloat( 0.3f, 0.5f );
			fx.Scale = OverLife( 9f * scale, 2f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = Flicker( 0.9f, 1.8f );
			fx.Gradient = Banded( p.Hot, p.Core, Color.Lerp( p.Smoke, p.Core, 0.4f ) );
			fx.Alpha = OverLife( 1f, 0f );
		}

		// Fat void hexagons shoved out of the blast, drifting up.
		{
			var (go, fx, _) = AddSpriteLayer( root, "smoke_puffs", TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 10f * scale; em.Velocity = (fizzle ? 50f : 130f) * scale; em.Rate = 0f; em.Burst = world ? 10f : body ? 5f : 6f; em.Loop = false;
			fx.MaxParticles = 16; fx.Damping = 4f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 60f * scale );
			fx.Lifetime = new ParticleFloat( 0.8f, 1.4f );
			fx.Scale = OverLife( 12f * scale, 29f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = fizzle
				? GradientOverLife( new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.25f ) ), new Gradient.ColorFrame( 1f, p.Smoke ) )
				: SmokeRamp( p );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.1f, fizzle ? 0.55f : 0.85f ), new Curve.Frame( 1f, 0f ) );
		}

		// Thin wisps riding the puffs.
		if ( world )
		{
			var (go, fx, _) = AddSpriteLayer( root, "fume_wisps", TexCircleSmall, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 12f * scale; em.Velocity = 90f * scale; em.Rate = 0f; em.Burst = 6f; em.Loop = false;
			fx.MaxParticles = 8; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 90f * scale );
			fx.Lifetime = new ParticleFloat( 1f, 1.6f );
			fx.Scale = OverLife( 14f * scale, 34f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, Color.White, 0.2f ) ), new Gradient.ColorFrame( 1f, p.Smoke.Darken( 0.25f ) ) );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.4f ), new Curve.Frame( 1f, 0f ) );
		}

		// Shockwave ring, camera-facing.
		if ( world )
		{
			var (go, fx, _) = AddSpriteLayer( root, "ring", TexRing, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 0f; em.Burst = 1f; em.Loop = false;
			fx.MaxParticles = 4; fx.Lifetime = 0.35f;
			fx.Scale = OverLife( 10f * scale, 110f * scale );
			fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, p.Hot ), new Gradient.ColorFrame( 1f, p.Core ) );
			fx.Alpha = OverLife( 0.9f, 0f );
		}

		// Sparks arcing out and raining down.
		{
			var (go, fx, _) = AddSpriteLayer( root, "embers", TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f * scale; em.Velocity = (fizzle ? 120f : 350f) * scale; em.Rate = 0f; em.Burst = fizzle ? 6f : 14f; em.Loop = false;
			fx.MaxParticles = 20;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -600f );
			fx.Stretch = 0.1f;
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = new ParticleFloat( 4f * scale, 7f * scale );
			fx.Brightness = Flicker( 0.5f, 2f );
			fx.Gradient = RandomBetween( p.Ember, p.Hot );
			fx.Alpha = OverLife( 1f, 0f );
		}

		// Void shards: a few hard chips flung out and spinning, the arcane residue.
		if ( !fizzle )
		{
			var (go, fx, _) = AddSpriteLayer( root, "shards", TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f * scale; em.Velocity = 180f * scale; em.Rate = 0f; em.Burst = 7f; em.Loop = false;
			fx.MaxParticles = 8; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -250f );
			fx.Lifetime = new ParticleFloat( 0.5f, 0.8f );
			fx.Scale = OverLife( 6f * scale, 2f * scale );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = Flicker( 0.8f, 1.6f );
			fx.Gradient = RandomBetween( p.Ember, Color.Lerp( p.Smoke, p.Core, 0.5f ) );
			fx.Alpha = OverLife( 1f, 0f );
		}
	}
}

/// <summary>Destroys its object after a number of seconds. The one-shots and released trails use it.</summary>
public sealed class VfxLifetime : Component
{
	public float Seconds { get; set; } = 2f;
	private float _born = -1f;

	protected override void OnUpdate()
	{
		if ( _born < 0f ) _born = Time.Now;
		if ( Time.Now - _born >= Seconds ) GameObject.Destroy();
	}
}

/// <summary>
/// The mark a fireball leaves: Synty's GroundBreak cracks on a quad laid on the surface through the
/// splat shader, ember-hot for the first moment, cooling to soot, then fading out (heden's).
/// </summary>
public sealed class GroundScorch : Component
{
	[Property] public float LifeSeconds { get; set; } = 7f;
	[Property] public float EmberSeconds { get; set; } = 1.1f;
	[Property] public float FadeSeconds { get; set; } = 2.5f;

	public const string ScorchMaterial = "shaders/ground_scorch.vmat";

	/// <summary>One unit quad per material (the scorch, the blood splatters): the mesh bakes its material in.</summary>
	private static readonly Dictionary<string, Model> _quads = new();
	private float _born = -1f;
	private ModelRenderer _renderer;

	private static Model Quad( string material )
	{
		if ( _quads.TryGetValue( material, out var cached ) ) return cached;
		var mesh = new Mesh( Material.Load( material ) );
		mesh.CreateVertexBuffer( 4, new List<SimpleVertex>
		{
			new() { position = new Vector3( -0.5f, -0.5f, 0f ), normal = Vector3.Up, tangent = Vector3.Forward, texcoord = new Vector2( 0f, 0f ) },
			new() { position = new Vector3( 0.5f, -0.5f, 0f ), normal = Vector3.Up, tangent = Vector3.Forward, texcoord = new Vector2( 1f, 0f ) },
			new() { position = new Vector3( 0.5f, 0.5f, 0f ), normal = Vector3.Up, tangent = Vector3.Forward, texcoord = new Vector2( 1f, 1f ) },
			new() { position = new Vector3( -0.5f, 0.5f, 0f ), normal = Vector3.Up, tangent = Vector3.Forward, texcoord = new Vector2( 0f, 1f ) },
		} );
		mesh.CreateIndexBuffer( 6, new List<int> { 0, 1, 2, 0, 2, 3 } );
		mesh.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 1f, 1f, 0.1f ) );
		var quad = Model.Builder.AddMesh( mesh ).Create();
		_quads[material] = quad;
		return quad;
	}

	/// <param name="parent">Under this object instead of the scene root (a dungeon's own marks go under the dungeon, so a rebuild sweeps them).</param>
	/// <param name="permanent">Never fades, never ember-hot: cold soot from the start (a trap's floor tell).</param>
	/// <param name="tint">A permanent mark that keeps its ember term lit in this colour instead (an acid-eaten stain).</param>
	/// <param name="material">A vmat on heden_ground_splat.shader; the scorch cracks by default, a blood splatter for BloodVfx.</param>
	/// <param name="project">
	/// Draw the mark ONTO the surface behind the quad (the splat shader's depth projection) instead of as the quad itself:
	/// `position` must then be ON the surface. Where the quad hangs past an edge, or the thing it landed on is gone, nothing draws.
	/// </param>
	public static GameObject Place( Vector3 position, Vector3 normal, float radius, GameObject parent = null, bool permanent = false, Color? tint = null, string material = ScorchMaterial, bool project = false )
	{
		var go = parent.IsValid() ? new GameObject( parent, true, "ground_scorch" ) : new GameObject( true, "ground_scorch" );
		go.WorldPosition = position + normal * 4f;
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

		var forward = Vector3.Cross( normal, Vector3.Right );
		if ( forward.IsNearlyZero() ) forward = Vector3.Cross( normal, Vector3.Forward );
		go.WorldRotation = Rotation.FromAxis( normal, Game.Random.Float( 0f, 360f ) ) * Rotation.LookAt( forward.Normal, normal );
		// a projected mark's quad is drawn bigger than the mark: lifted off the surface, it must still cover all of it on screen
		var size = radius * 2f * (project ? ProjectOversize : 1f);
		go.WorldScale = new Vector3( size, size, 1f );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = Quad( material ?? ScorchMaterial );
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		if ( renderer.SceneObject.IsValid() )
		{
			var ember = tint ?? FireballVfx.Ember;
			renderer.SceneObject.Attributes.Set( "g_vEmberColor", new Vector3( ember.r, ember.g, ember.b ) );
			if ( permanent )
			{
				renderer.SceneObject.Attributes.Set( "g_flEmber", tint.HasValue ? 1f : 0f );
				renderer.SceneObject.Attributes.Set( "g_flFade", 1f );
			}
		}

		var scorch = go.Components.Create<GroundScorch>();
		if ( project )
		{
			// the quad's local x/y carry the texture's u/v: the same axes, divided by the mark's diameter
			var diameter = MathF.Max( 0.01f, radius * 2f );
			scorch._projection = new Projection( position, normal.Normal, go.WorldRotation.Forward / diameter, go.WorldRotation.Left / diameter );
			scorch.ApplyProjection( renderer );
		}
		if ( permanent )
		{
			scorch.LifeSeconds = float.PositiveInfinity;
			scorch.EmberSeconds = 0.01f;
			scorch.HoldEmber = tint.HasValue;
		}
		return go;
	}

	/// <summary>Keep the ember term as set at placement (a tinted stain) instead of cooling it.</summary>
	public bool HoldEmber { get; set; }

	/// <summary>The quad's size over a projected mark's.</summary>
	private const float ProjectOversize = 1.3f;
	/// <summary>How far off the mark's plane a surface still takes a projected mark (units).</summary>
	private const float ProjectDepth = 5f;

	private readonly record struct Projection( Vector3 Origin, Vector3 Normal, Vector3 U, Vector3 V );
	private Projection? _projection;
	private bool _projectionApplied;

	/// <summary>Hands the projection to the shader; retried from OnUpdate if the scene object did not exist yet.</summary>
	private void ApplyProjection( ModelRenderer renderer )
	{
		if ( _projectionApplied || _projection is not { } p ) return;
		var so = renderer.IsValid() ? renderer.SceneObject : null;
		if ( !so.IsValid() ) return;
		so.Attributes.Set( "g_flProject", 1f );
		so.Attributes.Set( "g_vSplatOrigin", p.Origin );
		so.Attributes.Set( "g_vSplatNormal", p.Normal );
		so.Attributes.Set( "g_vSplatU", p.U );
		so.Attributes.Set( "g_vSplatV", p.V );
		so.Attributes.Set( "g_flSplatDepth", ProjectDepth );
		so.Attributes.Set( "g_flSplatOversize", ProjectOversize );
		_projectionApplied = true;
	}

	protected override void OnUpdate()
	{
		if ( _born < 0f ) { _born = Time.Now; _renderer = Components.Get<ModelRenderer>(); }
		var age = Time.Now - _born;
		if ( age >= LifeSeconds ) { GameObject.Destroy(); return; }
		if ( !_projectionApplied && _projection.HasValue ) ApplyProjection( _renderer );

		var attributes = _renderer.IsValid() ? _renderer.SceneObject?.Attributes : null;
		if ( attributes is null ) return;
		if ( !HoldEmber ) attributes.Set( "g_flEmber", MathF.Exp( -3f * age / EmberSeconds ) );
		attributes.Set( "g_flFade", Math.Clamp( (LifeSeconds - age) / FadeSeconds, 0f, 1f ) );
	}
}
