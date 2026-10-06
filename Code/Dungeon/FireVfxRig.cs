using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// Builds the sprite flame that FireLightSystem parks on a FireLight. Pure code, no particle assets:
/// each layer is a child GameObject with a ParticleEffect and a ParticleSpriteRenderer (the sprite
/// layer helpers are heden's SpellVfxRig). The layers:
///   heart  - hard-edged polygon discs at the wick, blooms
///   body   - the tongue: teardrop sprites with drawn colour bands, rising from a point at the base
///   licks  - tips thrown off by hand on a flicker spike, along the gust
///   halo   - soft ambient glow, no bloom; the retro dither turns it into a Bayer halo
///   wall   - one long-lived glow parked on the wall behind a wall torch
///   smoke  - braziers: lighter than heden's so it reads over the black fog
///   sparks - braziers and bonfires only, sparse; never on torches (reads as electrical)
///
/// Motion is owned per particle by FlameController, not by a force: every particle's velocity comes
/// from its age, so the base of the flame stays anchored on the torch and the tip bends toward a
/// lean target. The target is the light's wander scaled by FlameLeanRatio, so the light leads and
/// the fire follows at half the distance. A flicker spike (a gust) flings the older particles
/// outward along the gust direction and throws licks the same way.
/// Rules kept from heden: emitter Velocity is always 0, sprite sizes scale sub-linearly (pow 0.8)
/// while geometry scales linearly, no per-frame Flicker on heart or body.
/// </summary>
public static class FireVfxRig
{
	public const string RigName = "fire_vfx_rig";

	/// <summary>The flame tip displaces this fraction of the light's wander (0.5 = the light moves twice as far).</summary>
	private const float FlameLeanRatio = 0.5f;
	/// <summary>Rise speed relative to the FireLight's rise value. Kept low: a tongue is about twice as tall as it is wide, not a column.</summary>
	private const float FlameSpeed = 0.15f;
	/// <summary>How much a flicker spike brightens the flame (the light uses its own curve).</summary>
	private const float FlameFlicker = 2.5f;
	/// <summary>Seconds for the tip to follow a new lean target: short, so the light's fast flicker reaches the flame.</summary>
	private const float LeanFollow = 0.03f;
	/// <summary>How hard a flicker spike scatters the upper particles in random directions.</summary>
	private const float Turbulence = 150f;
	/// <summary>How hard the tongue's tip follows the light's wander (the heart stays near the wick).</summary>
	private const float TongueLean = 3f;

	private const string TexTongue = "vfx/flame_tongue.png";
	private const string TexLick = "vfx/flame_lick.png";
	private const string TexGlow = "vfx/flame_glow.png";
	/// <summary>A soft lumpy puff (generated: radial falloff eroded by noise). The pack's smoke sprite is a hard heptagon.</summary>
	private const string TexSmoke = "vfx/smoke_wisp.png";
	private const string TexSparkle = "vfx/polygonparticles_sparkle.png";
	/// <summary>heden's faceted polygon disc: the hard-edged core particles.</summary>
	private const string TexPolygon = "vfx/polygonparticles_circle_01.png";

	/// <summary>
	/// What the flame is doing this frame. Plain data: written only by Drive on the main thread, read
	/// by FlameController's threaded particle step. World space.
	/// </summary>
	public sealed class FlameState
	{
		public Vector3 Base;         // the anchored base of the tongue
		public Vector3 Up = Vector3.Up;
		public Vector3 LeanTarget;   // where the tip wants to be, relative to straight up
		public Vector3 GustDir = Vector3.Forward;
		public float Rise;           // rise speed at the base
		public float Scale = 1f;
		public float Width = 1f;     // sideways spread multiplier (FireLight.FlameWidth)
		public bool Gust;
		public float Turb;           // |flicker| this frame: random scatter strength
		public float Seed;           // changes a few dozen times a second: re-rolls the scatter directions
		public float Time;           // for each particle's own drift
		// a portal close enough to drag this fire into itself (Drive finds it): 0 = none
		public Vector3 PullPoint;    // the portal's heart
		public float Pull;           // FireLight.PortalPull, already faded by distance and by a portal held shut
		public float PullRange = 1f;
	}

	/// <summary>Cheap deterministic hash to 0..1, safe on worker threads.</summary>
	internal static float Hash( float x )
	{
		float v = MathF.Sin( x * 127.1f + 311.7f ) * 43758.5453f;
		return v - MathF.Floor( v );
	}

	/// <summary>Handles the system writes to every frame.</summary>
	public sealed class Rig
	{
		public GameObject Root;
		public readonly FlameState State = new();
		public readonly List<ParticleEffect> Bodies = new();
		public readonly List<ParticleSphereEmitter> BodyEmitters = new();   // rate follows the flicker: bursty birth
		public readonly List<ParticleEffect> Hearts = new();
		public readonly List<ParticleEffect> Glows = new();      // halo + wall glow: breathe softly
		public readonly List<(ParticleEffect fx, GameObject tongue)> Licks = new();
		public ParticleEffect SparkFx;
		/// <summary>The portal this fire is being drawn into, re-found about once a second.</summary>
		public VoidMage.Run.RunPortal Portal;
		public VoidMage.Run.PortalVfx PortalFx;
		public float NextPortalScan;
		public bool SparksPulled;
		/// <summary>Every tongue layer's controller and the tongue it is anchored on: Drive feeds each its own base.</summary>
		public readonly List<(FlameController Controller, GameObject Tongue)> Controllers = new();
		/// <summary>The FireLight.BuildKey this rig was built from; a mismatch means rebuild.</summary>
		public int BuildKey;
		public float Scale;
		public bool Pulse;   // Glow kind: slow pulse instead of fire flicker
		/// <summary>Set to make the flame stream along another direction than up (a flying fireball's tail).</summary>
		public Vector3? UpOverride;
	}

	// ------------------------------------------------------------------ per-particle motion

	/// <summary>
	/// Steers one flame layer's particles from the shared FlameState. Runs threaded: no scene access,
	/// only the state object and the particle. Age is the height proxy (particles rise steadily), so
	/// a young particle sits on the anchored base and an old one rides the bent tip.
	/// </summary>
	public sealed class FlameController : ParticleController
	{
		public FlameState State;
		public float LeanFactor = 1f;
		public float RiseFactor = 1f;
		/// <summary>World position of this layer's own tongue base (written by Drive): multi-tongue flames stay on their own wicks.</summary>
		public Vector3 Anchor;

		// Motion is a velocity random walk, not a position on a curve: each particle keeps its own
		// sideways velocity from frame to frame and is pushed by its own smooth random turbulence,
		// a shared wind from the light, and only a weak pull back toward the axis. Blobs born a
		// moment apart take different paths, so the flame is a crowd, never a trail.
		private const float Noise = 55f;       // own turbulence, units/s^2 at scale 1 near the tip
		private const float Wind = 25f;        // shared push per unit of the light's wander (kept low: the crowd leans, it does not march)
		private const float Confine = 5f;      // pull toward the axis, 1/s^2 per unit of offset (weak: never a return to a line)
		private const float Damp = 3.5f;       // 1/s, smooths the random walk
		private const float MaxSideways = 14f; // units/s at scale 1

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null || delta <= 0f ) return;
			float life = p.DeathTime - p.BornTime;
			float a = life > 0f ? Math.Clamp( p.Age / life, 0f, 1f ) : 1f;
			var rel = p.Position - Anchor;
			float w = s.Width;
			float h = Vector3.Dot( rel, s.Up );
			var lat = rel - s.Up * h;

			// the velocity this particle already has (set by us last frame): sideways part persists
			var v = p.Velocity;
			var vSide = v - s.Up * Vector3.Dot( v, s.Up );

			float r1 = Hash( p.Random01 ), r2 = Hash( p.Random01 * 3.7f + 1f ), r3 = Hash( p.Random01 * 5.9f + 2f );
			float grow = (0.05f + a * a) * LeanFactor;   // calm at the wick, lively only well away from it

			// born: a small random sideways nudge so paths diverge from the first frame
			if ( p.Age < delta * 1.5f )
			{
				float ka = r2 * MathF.PI * 2f;
				vSide = new Vector3( MathF.Cos( ka ), MathF.Sin( ka ), 0f ) * (2.5f * s.Scale * w * LeanFactor * (0.3f + r3));
			}

			// own turbulence: slow smooth noise from sines at this particle's own frequencies and phases
			float t = s.Time;
			float f1 = 1.5f + 3.5f * r1, f2 = 1.2f + 4f * r2, f3 = 2f + 3f * r3;
			float nx = MathF.Sin( t * f1 + r2 * 31f ) + 0.5f * MathF.Sin( t * f3 * 1.7f + r1 * 17f );
			float ny = MathF.Sin( t * f2 + r3 * 23f ) + 0.5f * MathF.Sin( t * f1 * 1.3f + r3 * 29f );
			var accel = new Vector3( nx, ny, 0f ) * (Noise * s.Scale * w * grow * (0.6f + 0.8f * r1));

			// shared wind: the light's wander leans the crowd a little, more so away from the core
			accel += s.LeanTarget * (Wind * grow);

			// the light's flicker (smoothed) nudges the upper particles in random directions, re-rolled
			// per particle a few times a second: unpredictable licks in step with the light, not jitter
			if ( s.Turb > 0.005f && a > 0.25f )
			{
				float ka = Hash( p.Random01 * 7.3f + s.Seed ) * MathF.PI * 2f;
				accel += new Vector3( MathF.Cos( ka ), MathF.Sin( ka ), 0f ) * (s.Turb * Turbulence * s.Scale * grow * (0.6f + 0.8f * r3) * 3f);
			}

			// weak confinement keeps the teardrop; the heart (low LeanFactor) is held tighter, the
			// tongue barely at all so a blob that wandered left stays left and fades there
			float hold = LeanFactor < 1f ? 3f : 0.6f;
			accel -= lat * (Confine * hold * (1f - 0.4f * a) / MathF.Max( w, 0.1f ));

			// a portal nearby drags the flame over: the wick stays put, the tip streams toward the heart
			if ( s.Pull > 0f )
			{
				var to = s.PullPoint - p.Position;
				var toSide = to - s.Up * Vector3.Dot( to, s.Up );
				if ( toSide.LengthSquared > 1f ) accel += toSide.Normal * (FlamePull * s.Pull * s.Scale * (0.1f + a));
			}

			vSide = vSide * MathF.Max( 0f, 1f - Damp * delta ) + accel * delta;
			float maxSide = MaxSideways * s.Scale * w * (0.4f + a) * (1f + 1.5f * s.Pull);
			if ( vSide.LengthSquared > maxSide * maxSide ) vSide = vSide.Normal * maxSide;

			// fast off the wick, slowing toward the tip where it fades; each particle at its own pace,
			// and the paces differ a lot, so blobs overtake each other instead of rising as a stack
			float vUp = s.Rise * RiseFactor * (1.3f - 0.8f * a) * (0.65f + 0.7f * r2);
			if ( s.Gust && a > 0.55f )
			{
				// flicked off the tip: outward along its own offset and along the gust
				var outward = lat.LengthSquared > 0.01f ? lat.Normal * 0.5f : Vector3.Zero;
				vSide += (outward + s.GustDir) * (25f * s.Scale * (a - 0.55f) * delta * 6f);
			}
			p.Velocity = s.Up * vUp + vSide;
		}
	}

	private const float FlamePull = 90f;   // sideways units/s^2 at scale 1 on the flame's tip, per unit of pull

	/// <summary>
	/// A campfire's smoke: many small soft wisps that leave the top of the fire fast, slow and swell as they climb,
	/// each on its own lazy wander while the whole column bends to one shared breeze. Owns the velocity outright
	/// (the effect's Force and Damping are off): every step eases it toward where the wisp wants to go. Threaded,
	/// state object only. A portal in range bends the column into its heart, where the wisps are retired.
	/// </summary>
	public sealed class SmokeController : ParticleController
	{
		public FlameState State;
		public float Rise = 38f;
		public float Spread = 1f;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null || delta <= 0f ) return;
			float life = p.DeathTime - p.BornTime;
			float a = life > 0f ? Math.Clamp( p.Age / life, 0f, 1f ) : 1f;
			float r1 = Hash( p.Random01 ), r2 = Hash( p.Random01 * 3.7f + 1f ), r3 = Hash( p.Random01 * 5.9f + 2f );
			float t = s.Time;

			// own wander: slow, and only once it is clear of the fire
			float f1 = 0.35f + 0.6f * r1, f2 = 0.3f + 0.7f * r2;
			var sway = new Vector3(
				MathF.Sin( t * f1 + r2 * 31f ) + 0.5f * MathF.Sin( t * f2 * 2.3f + r1 * 17f ),
				MathF.Sin( t * f2 + r3 * 23f ) + 0.5f * MathF.Sin( t * f1 * 1.9f + r3 * 29f ), 0f ) * (9f * Spread * (0.15f + a));
			// the breeze every wisp shares: the column leans as one, more the higher it is
			var breeze = new Vector3( MathF.Sin( t * 0.23f ), MathF.Sin( t * 0.17f + 1.3f ), 0f ) * (7f * Spread * a);
			// and it fans out: each wisp keeps drifting its own way
			float ka = r3 * MathF.PI * 2f;
			var fan = new Vector3( MathF.Cos( ka ), MathF.Sin( ka ), 0f ) * (5f * Spread * a);
			float vUp = Rise * (1.05f - 0.5f * a) * (0.8f + 0.4f * r2);
			var target = Vector3.Up * vUp + sway + breeze + fan;

			if ( s.Pull > 0f )
			{
				var to = s.PullPoint - p.Position;
				float dist = to.Length;
				if ( dist < 9f ) { p.Age = float.MaxValue; return; }   // swallowed (the engine's own kill idiom)
				float near = 1f - Math.Clamp( dist / s.PullRange, 0f, 1f );
				// young smoke still rises; the older and nearer it is, the more the portal owns it
				float w = Math.Clamp( s.Pull * (0.15f + 1.2f * a) * (0.4f + near), 0f, 1f );
				// the last stretch always wins, so nothing orbits the heart
				if ( dist < 45f ) w = MathF.Max( w, MathF.Min( 1f, s.Pull * 2f ) * (1f - dist / 90f) );
				float speed = 45f + 110f * near + (dist < 45f ? 60f : 0f);
				target = Vector3.Lerp( target, to / dist * speed, w );
			}

			var v = p.Velocity;
			v += (target - v) * MathF.Min( 1f, delta * 2.5f );
			p.Velocity = v;
		}
	}

	/// <summary>Adds a portal's drag to a layer the engine otherwise moves by itself (the sparks). Threaded, state object only.</summary>
	public sealed class PullController : ParticleController
	{
		public FlameState State;
		public float Strength = 300f;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null || s.Pull <= 0f || delta <= 0f ) return;
			var to = s.PullPoint - p.Position;
			float dist = to.Length;
			if ( dist < 9f ) { p.Age = float.MaxValue; return; }
			float near = 1f - Math.Clamp( dist / s.PullRange, 0f, 1f );
			p.Velocity += to / dist * (Strength * s.Pull * (0.5f + 1.5f * near) * delta);
		}
	}

	// ------------------------------------------------------------------ sprite layers (heden helpers)

	private static readonly Dictionary<string, Sprite> _sprites = new();

	internal static Sprite SpriteFor( string path )
	{
		if ( _sprites.TryGetValue( path, out var cached ) ) return cached;
		var sprite = Sprite.FromTexture( Texture.Load( path ) );
		_sprites[path] = sprite;
		return sprite;
	}

	/// <summary>One particle layer: a child GameObject carrying the effect and its sprite renderer. Unlit, unshadowed, camera-facing. Shared with DungeonHaze.</summary>
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

	/// <summary>Re-rolled every frame: the twinkle of sparks. Never on the flame body.</summary>
	private static ParticleFloat Flicker( float min, float max ) => new()
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

	private static ParticleFloat SoftAlpha() => CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.12f, 1f ), new Curve.Frame( 1f, 0f ) );

	private static FlameController Steer( GameObject go, GameObject tongue, Rig rig, float lean, float rise )
	{
		var c = go.Components.Create<FlameController>();
		c.State = rig.State;
		c.LeanFactor = lean;
		c.RiseFactor = rise;
		c.Anchor = tongue.WorldPosition;
		rig.Controllers.Add( (c, tongue) );
		return c;
	}

	/// <summary>Where the tongues go, relative to the flame base: the explicit offsets (one per wick), else a ring.</summary>
	private static List<Vector3> TonguePoints( FireLight f, float ringAngle )
	{
		var points = new List<Vector3>();
		if ( f.TongueOffsets is { Count: > 0 } )
		{
			points.AddRange( f.TongueOffsets );
			return points;
		}
		int n = Math.Max( 1, f.Tongues );
		if ( n == 1 ) { points.Add( Vector3.Zero ); return points; }
		for ( int i = 0; i < n; i++ )
		{
			float a = MathF.PI * 2f * i / n + ringAngle;
			points.Add( new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * f.TongueRadius );
		}
		return points;
	}

	// ------------------------------------------------------------------ build

	/// <summary>Build the rig under `parent` (the prop) with its root at `localBase` in the prop's frame.</summary>
	public static Rig Build( GameObject parent, FireLight f, Vector3 localBase )
	{
		var rig = new Rig { Scale = f.FlameScale, BuildKey = f.BuildKey };
		rig.State.Scale = f.FlameScale;
		rig.State.Width = f.FlameWidth;
		rig.Root = new GameObject( true, RigName ) { Parent = parent, LocalPosition = localBase };
		rig.Root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;

		var tint = f.FlameTint;
		// only a hint of yellow at the hottest point: a fire is orange and red, not white
		var hot = Color.Lerp( tint, new Color( 1f, 0.85f, 0.45f ), 0.3f );
		var deep = tint.Darken( 0.45f );
		float scale = f.FlameScale;

		switch ( f.Kind )
		{
			case LightKind.Glow:
				rig.Pulse = true;
				Halo( rig, rig.Root, Vector3.Zero, tint, deep, scale, 28f, 36f, 0.28f );
				break;

			case LightKind.Chandelier:
			{
				foreach ( var at in TonguePoints( f, MathF.PI / 4f ) )
					Tongue( rig, rig.Root, at, tint, hot, deep, scale, f.Amount, f.FlameWidth, licks: false );
				Halo( rig, rig.Root, Vector3.Up * 2f, tint, deep, scale * 2.2f, 36f, 46f, 0.2f );
				break;
			}

			case LightKind.Candle:
				foreach ( var at in TonguePoints( f, 0f ) )
					Tongue( rig, rig.Root, at, tint, hot, deep, scale, f.Amount, f.FlameWidth, licks: true );
				if ( scale < 0.42f ) Halo( rig, rig.Root, Vector3.Zero, tint, deep, scale, 36f, 46f, 0.18f );   // lanterns: glass glow
				break;

			default:   // Torch, Brazier
				foreach ( var at in TonguePoints( f, 0f ) )
					Tongue( rig, rig.Root, at, tint, hot, deep, scale, f.Amount, f.FlameWidth, licks: true );
				Halo( rig, rig.Root, Vector3.Zero, tint, deep, scale, 17.5f, 22.5f, 0.16f );
				if ( f.WallGlow ) WallGlow( rig, rig.Root, f, deep );
				if ( f.Smoke ) Smoke( rig, rig.Root, f, tint, scale );
				if ( f.Sparks ) Sparks( rig, rig.Root, tint, hot, scale );
				break;
		}
		return rig;
	}

	/// <summary>Heart + body (+ licks) at `at`: one flame tongue, anchored at its base.</summary>
	private static void Tongue( Rig rig, GameObject parent, Vector3 at, Color tint, Color hot, Color deep, float scale, float amount, float width, bool licks )
	{
		float sprite = MathF.Pow( scale, 0.8f );
		var root = new GameObject( true, "tongue" ) { Parent = parent, LocalPosition = at };

		// heart: hard-edged polygon discs, small and tightly overlapped, barely leaning
		{
			var (go, fx, _) = AddSpriteLayer( root, "heart", TexPolygon, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 1f * scale * width;
			em.Velocity = 0f;
			em.Rate = 12f;
			em.Burst = 0f;
			em.Loop = true;
			fx.MaxParticles = 12;
			fx.LocalSpace = 0f;
			fx.Force = false;
			fx.Damping = 0f;
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Scale = new ParticleFloat( 5f * sprite, 7.5f * sprite );
			fx.Gradient = RandomBetween( hot, tint );
			fx.Lifetime = new ParticleFloat( MathX.Lerp( 0.5f, 0.07f, amount ), MathX.Lerp( 0.8f, 0.12f, amount ) );
			fx.Alpha = SoftAlpha();
			fx.Brightness = 0.85f;
			Steer( go, root, rig, lean: 0.3f, rise: 0.4f );
			rig.Hearts.Add( fx );
		}

		// body: the drawn tongue, rising from a point; grows then tapers, faded by 80% of life
		{
			var (go, fx, _) = AddSpriteLayer( root, "body", TexTongue, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2.2f * scale * width;
			em.Velocity = 0f;   // never: radial fling reads as sparks
			em.Rate = 15f;
			em.Burst = 0f;
			em.Loop = true;
			fx.MaxParticles = 20;
			fx.LocalSpace = 0f;
			fx.Force = false;
			fx.Damping = 0f;
			fx.Roll = new ParticleFloat( -12f, 12f );
			fx.Lifetime = new ParticleFloat( 0.7f, 1.5f );
			fx.Scale = CurveOverLife( new Curve.Frame( 0f, 7f * sprite ), new Curve.Frame( 0.25f, 10f * sprite ), new Curve.Frame( 1f, 5f * sprite ) );
			fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, hot ), new Gradient.ColorFrame( 0.3f, tint ), new Gradient.ColorFrame( 1f, deep ) );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.1f, 1f ), new Curve.Frame( 0.55f, 0.85f ), new Curve.Frame( 0.8f, 0f ), new Curve.Frame( 1f, 0f ) );
			fx.Brightness = 0.75f;
			Steer( go, root, rig, lean: TongueLean, rise: 1f );
			rig.Bodies.Add( fx );
			rig.BodyEmitters.Add( em );
		}

		// licks: no emitter; Drive throws them by hand on a gust, along the gust direction
		if ( licks )
		{
			var (go, fx, _) = AddSpriteLayer( root, "licks", TexLick, additive: true, bloom: true );
			fx.MaxParticles = 4;
			fx.LocalSpace = 0f;
			fx.Force = false;
			fx.Damping = 1.5f;
			fx.Roll = new ParticleFloat( -10f, 10f );
			fx.Lifetime = new ParticleFloat( 0.25f, 0.4f );
			fx.Scale = OverLife( 6.25f * sprite, 3.75f * sprite );
			fx.Gradient = RandomBetween( hot, tint );
			fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 1f ), new Curve.Frame( 0.7f, 0f ), new Curve.Frame( 1f, 0f ) );
			fx.Brightness = 0.8f;
			rig.Licks.Add( (fx, root) );
		}
	}

	/// <summary>Big soft ambient glow, no bloom. Under the retro dither this becomes a Bayer halo.</summary>
	private static void Halo( Rig rig, GameObject parent, Vector3 at, Color tint, Color deep, float scale, float from, float to, float alpha )
	{
		float sprite = MathF.Pow( scale, 0.8f );
		var (go, fx, _) = AddSpriteLayer( parent, "halo", TexGlow, additive: true, bloom: false );
		go.LocalPosition = at;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 1f;
		em.Velocity = 0f;
		em.Rate = 2f;
		em.Burst = 0f;
		em.Loop = true;
		fx.MaxParticles = 4;
		fx.LocalSpace = 1f;
		fx.Lifetime = new ParticleFloat( 1.2f, 1.8f );
		fx.Scale = OverLife( from * sprite, to * sprite );
		fx.Gradient = RandomBetween( tint, deep );
		fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, alpha ), new Curve.Frame( 1f, 0f ) );
		rig.Glows.Add( fx );
	}

	/// <summary>The warm patch on the wall behind a wall torch: one immortal sprite parked just in front of the wall face.</summary>
	private static void WallGlow( Rig rig, GameObject parent, FireLight f, Color deep )
	{
		var (go, fx, _) = AddSpriteLayer( parent, "wall", TexGlow, additive: true, bloom: false );
		// The rig root sits at rig.Root.LocalPosition in the prop's frame; the wall face is at
		// f.WallFaceX in that same frame (the builder measures it from the model bounds and the
		// embed depth, since a sunk torch has its pivot INSIDE the wall). Park the sprite a hair in
		// front of the face: any deeper and it sits inside the 21 in wall and shows from the far side.
		const float standoff = 1.5f;
		go.LocalPosition = new Vector3( f.WallFaceX + standoff, 0f, -2f ) - rig.Root.LocalPosition;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0f;
		em.Velocity = 0f;
		em.Rate = 0f;
		em.Burst = 1f;
		em.Loop = false;
		fx.MaxParticles = 1;
		fx.LocalSpace = 1f;
		fx.Lifetime = new ParticleFloat( 3600f, 3600f );
		fx.Scale = new ParticleFloat( 90f * f.FlameScale, 90f * f.FlameScale );
		fx.Gradient = RandomBetween( deep, deep );
		fx.Alpha = new ParticleFloat( 0.12f, 0.12f );
		rig.Glows.Add( fx );
	}

	/// <summary>
	/// A rising column, not puffs: a steady stream of small soft wisps off the top of the fire, swelling and thinning
	/// as they climb (SmokeController moves them). Every number has a FireLight "Smoke" slider on top of it.
	/// </summary>
	private static void Smoke( Rig rig, GameObject parent, FireLight f, Color tint, float scale )
	{
		var (go, fx, renderer) = AddSpriteLayer( parent, "smoke", TexSmoke, additive: false, bloom: false );
		go.LocalPosition = Vector3.Up * f.SmokeStart * scale;
		renderer.DepthFeather = 10f;
		// a big fire's smoke is wider, not proportionally huge
		float size = MathF.Sqrt( MathF.Max( scale, 0.1f ) ) * f.SmokeSize;
		float height = MathF.Max( 0.05f, f.SmokeHeight );
		float rate = MathF.Max( 1f, MathF.Round( 9f * f.SmokeDensity ) );   // the emitter truncates to whole particles
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = MathF.Max( 1f, 2.5f * scale * f.FlameWidth );
		em.Velocity = 0f;   // the emitter's default 100 flings everything outward
		em.Rate = rate;
		em.Burst = 0f;
		em.Loop = true;
		fx.MaxParticles = (int)(rate * 4.6f * height) + 6;
		fx.LocalSpace = 0f;
		fx.Damping = 0f;
		fx.Force = false;
		fx.Lifetime = new ParticleFloat( 3.2f * height, 4.6f * height );
		fx.Scale = CurveOverLife( new Curve.Frame( 0f, 5f * size ), new Curve.Frame( 0.3f, 13f * size ), new Curve.Frame( 1f, 26f * size ) );
		fx.Roll = new ParticleFloat( 0f, 360f );
		// unlit sprites: kept dark so the column does not glow at night; warm where it leaves the fire
		var from = Color.Lerp( new Color( 0.17f, 0.15f, 0.14f ), tint * 0.5f, 0.35f );
		var to = new Color( 0.36f, 0.36f, 0.38f );
		if ( f.SmokeColor.a > 0.01f ) { from = f.SmokeColor * 0.6f; to = f.SmokeColor; }
		fx.Gradient = GradientOverLife( new Gradient.ColorFrame( 0f, from.WithAlpha( 1f ) ), new Gradient.ColorFrame( 0.45f, Color.Lerp( from, to, 0.7f ).WithAlpha( 1f ) ), new Gradient.ColorFrame( 1f, to.WithAlpha( 1f ) ) );
		float o = f.SmokeOpacity;
		fx.Alpha = CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.12f, 0.16f * o ), new Curve.Frame( 0.5f, 0.1f * o ), new Curve.Frame( 1f, 0f ) );

		var steer = go.Components.Create<SmokeController>();
		steer.State = rig.State;
		steer.Rise = 38f * f.SmokeSpeed;
		steer.Spread = f.SmokeSpread;
	}

	/// <summary>Sparse lazy sparks for open fires; Drive throws extra ones along a gust.</summary>
	private static void Sparks( Rig rig, GameObject parent, Color tint, Color hot, float scale )
	{
		var (go, fx, _) = AddSpriteLayer( parent, "sparks", TexSparkle, additive: true, bloom: true );
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 6f * scale;
		em.Velocity = 12f;
		em.Rate = 3f;
		em.Burst = 0f;
		em.Loop = true;
		fx.MaxParticles = 12;
		fx.LocalSpace = 0f;
		fx.Damping = 1f;
		fx.Force = true;
		fx.ForceDirection = new Vector3( 0f, 0f, 60f );
		fx.Lifetime = new ParticleFloat( 0.5f, 1.0f );
		fx.Scale = new ParticleFloat( 3f, 5f );   // fixed: sub-linear sizes vanish at 240 lines
		fx.Brightness = Flicker( 0.6f, 2f );
		fx.Gradient = RandomBetween( tint, hot );
		fx.Alpha = SoftAlpha();
		rig.SparkFx = fx;
		go.Components.Create<PullController>().State = rig.State;
	}

	// ------------------------------------------------------------------ per frame

	/// <summary>
	/// A portal within its SuctionRange drags this fire into itself: the pull fades with distance and dies while
	/// the portal is held shut. The nearest portal is re-found about once a second; the rest is two distances.
	/// </summary>
	private static void DrivePull( Rig rig, FireLight f, float t )
	{
		var s = rig.State;
		if ( t >= rig.NextPortalScan )
		{
			rig.NextPortalScan = t + 1f;
			rig.Portal = null;
			float best = float.MaxValue;
			foreach ( var portal in VoidMage.Run.RunPortal.All )
			{
				if ( !portal.IsValid() || portal.SuctionRange <= 0f ) continue;
				float d = portal.WorldPosition.DistanceSquared( s.Base );
				if ( d < best && d < portal.SuctionRange * portal.SuctionRange ) { best = d; rig.Portal = portal; }
			}
			rig.PortalFx = rig.Portal.IsValid() ? rig.Portal.Components.Get<VoidMage.Run.PortalVfx>() : null;
		}

		float pull = 0f;
		if ( rig.Portal.IsValid() && f.PortalPull > 0f )
		{
			var heart = rig.PortalFx.IsValid() ? rig.PortalFx.Heart : rig.Portal.WorldPosition + Vector3.Up * 70f;
			float range = rig.Portal.SuctionRange;
			float near = 1f - Math.Clamp( heart.Distance( s.Base ) / range, 0f, 1f );
			float open = rig.PortalFx.IsValid() ? 1f - rig.PortalFx.Stall : 1f;
			pull = f.PortalPull * open * near * (2f - near);   // eased: still a clear drag well inside the range
			s.PullPoint = heart;
			s.PullRange = range;
		}
		s.Pull = pull;

		// sparks live half a second on their own, too short to reach a portal: drawn in, they get the time to arrive
		bool pulled = pull > 0.05f;
		if ( rig.SparkFx.IsValid() && pulled != rig.SparksPulled )
		{
			rig.SparksPulled = pulled;
			rig.SparkFx.Lifetime = pulled ? new ParticleFloat( 1.2f, 2f ) : new ParticleFloat( 0.5f, 1.0f );
		}
	}

	/// <summary>
	/// Steer a rig from its light: the lean target is the light's wander scaled down (low-passed so
	/// the tip whips but never jitters), brightness from the flicker, licks and sparks on a gust.
	/// Main thread only: this is the one writer of the shared state.
	/// </summary>
	public static void Drive( Rig rig, FireLight f, float t, float dt )
	{
		if ( rig == null || !rig.Root.IsValid() ) return;
		float flick = f.Flicker;
		float amount = f.Amount;
		if ( rig.Pulse )
		{
			float pulse = 1f + 0.35f * MathF.Sin( f.LocalTime * 1.7f + f.Phase );
			foreach ( var g in rig.Glows ) g.Brightness = pulse;
			return;
		}

		var s = rig.State;
		var prop = f.GameObject.Parent;
		var rot = prop.IsValid() ? prop.WorldRotation : f.WorldRotation;
		var dispW = rot * f.Displacement;
		var target = dispW * FlameLeanRatio;
		float k = 1f - MathF.Exp( -MathF.Max( dt, 0f ) / LeanFollow );
		s.Base = rig.Root.WorldPosition;
		foreach ( var (c, tongue) in rig.Controllers )
			if ( tongue.IsValid() ) c.Anchor = tongue.WorldPosition;
		s.Up = rig.UpOverride ?? Vector3.Up;
		s.LeanTarget = Vector3.Lerp( s.LeanTarget, target, k );
		s.Rise = f.Rise * FlameSpeed * f.FlameHeight;
		s.Width = f.FlameWidth;
		s.Gust = f.Gust;
		// the flicker strength reaches the flame smoothed (about 100 ms), so it stirs rather than jitters
		s.Turb = MathX.Lerp( s.Turb, MathF.Abs( flick ), 1f - MathF.Exp( -MathF.Max( dt, 0f ) / 0.1f ) );
		// this fire's own clock: two fires never share a turbulence pattern or re-roll on the same frame
		s.Seed = MathF.Floor( f.LocalTime * 8f );
		s.Time = f.LocalTime;
		if ( dispW.LengthSquared > 0.05f ) s.GustDir = dispW.Normal;
		DrivePull( rig, f, t );

		float b = MathF.Max( 0.15f, 1f + flick * amount * FlameFlicker );
		foreach ( var body in rig.Bodies ) body.Brightness = b * 0.75f;
		// birth is bursty too: a spike in the light spits a clump of new blobs, a lull starves the tip
		foreach ( var em in rig.BodyEmitters ) if ( em.IsValid() ) em.Rate = 11f + 40f * s.Turb + (flick > 0f ? 15f * flick : 0f);
		foreach ( var heart in rig.Hearts ) heart.Brightness = b * 0.85f;
		float g2 = 1f + flick * amount * 1.5f;
		foreach ( var g in rig.Glows ) g.Brightness = g2;

		if ( !s.Gust ) return;
		float scale = rig.Scale;
		// licks: one per tongue per gust frame, thrown along the gust from just above the base
		foreach ( var (fx, tongue) in rig.Licks )
		{
			if ( !fx.IsValid() || fx.ParticleCount >= fx.MaxParticles ) continue;
			var p = fx.Emit( tongue.WorldPosition + Vector3.Up * 6f * scale );
			if ( p == null ) continue;
			p.Velocity = (Vector3.Up * 30f + s.GustDir * 25f) * scale;
		}
		// sparks: a couple extra flung along the gust
		if ( rig.SparkFx.IsValid() )
		{
			for ( int i = 0; i < 2; i++ )
			{
				var p = rig.SparkFx.Emit( s.Base + Vector3.Up * 4f * scale );
				if ( p == null ) break;
				p.Velocity = s.GustDir * 30f + Vector3.Up * 50f;
			}
		}
	}
}
