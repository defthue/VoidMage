using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// The pieces that make one spell's placeholder look different from another's beyond the palette: a spinning
/// crystal on the ice shard, lightning flashes and jagged arcs for the chain, the void orb's black sphere
/// (heden_void_orb.shader), and a burst of thrown FX meshes. Meshes come from Assets/VFX/models (gen_fx_assets.py)
/// and are skipped quietly when a model has not been compiled yet.
/// </summary>
public static class StyleVfx
{
	public const string MatAdd = "shaders/fx_unlit_add.vmat";
	public const string MatAlpha = "shaders/fx_unlit_alpha.vmat";
	public const string TexLightning = "vfx/polygonparticles_lightning_02.png";
	public const string TexRitual = "vfx/polygonparticles_ritualcircle_01.png";
	public const string TexSoftDark = "vfx/polygonparticles_soft_spot_dark.png";

	private static readonly Dictionary<string, Model> _models = new();
	private static Material _add, _alpha, _voidOrb;

	/// <summary>A compiled FX model, or null when it is missing or failed (the caller then skips the mesh layer).</summary>
	public static Model FxModel( string name )
	{
		if ( _models.TryGetValue( name, out var cached ) ) return cached;
		Model model = null;
		try { model = Model.Load( "vfx/models/" + name + ".vmdl" ); } catch { model = null; }
		if ( model is null || model.IsError ) model = null;
		_models[name] = model;
		return model;
	}

	public static Material AddMaterial => _add ??= Material.Load( MatAdd );
	public static Material AlphaMaterial => _alpha ??= Material.Load( MatAlpha );

	/// <summary>A tinted unlit mesh under `parent`; null when the model is not there.</summary>
	public static ModelRenderer AddMesh( GameObject parent, string name, string model, Color tint, float scale, bool additive )
	{
		var m = FxModel( model );
		if ( m is null ) return null;
		var go = new GameObject( true, name ) { Parent = parent };
		go.LocalPosition = Vector3.Zero;
		go.LocalScale = Vector3.One * scale;
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var r = go.Components.Create<ModelRenderer>();
		r.Model = m;
		r.MaterialOverride = additive ? AddMaterial : AlphaMaterial;
		r.Tint = tint;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		return r;
	}

	/// <summary>The same, registered with the rig so it grows with the charge and goes when the rig stops.</summary>
	public static ModelRenderer AddMesh( FireballVfx.Rig rig, string name, string model, Color tint, float scale, bool additive )
		=> rig.AddMesh( AddMesh( rig.Root, name, model, tint, scale, additive ) );

	// ------------------------------------------------------------------ the crystal (ice shard)

	/// <summary>The ice shard's body: a crystal mesh spun about the flight axis, over the palette's glow. Adds a mesh layer to the rig.</summary>
	public static void AddCrystal( FireballVfx.Rig rig, SpellPalette p, float scale )
	{
		var r = AddMesh( rig, "crystal", "fx_crystal_02", p.Core.WithAlpha( 0.95f ), 0.9f * scale, additive: false );
		if ( r is null ) return;
		var spin = rig.Root.Components.GetOrCreate<Spinner>();
		spin.DegreesPerSecond = 540f;
		spin.Children = true;
		AddMesh( rig, "crystal_glow", "fx_crystal_02", p.Hot.WithAlpha( 0.5f ), 1.05f * scale, additive: true );
	}

	/// <summary>
	/// Spins about an axis. On the rig root with <see cref="Children"/> it turns the crystal pieces under it (their
	/// long axis rides the bolt's forward); on a piece itself it turns that piece, keeping the tilt it was built with.
	/// </summary>
	public sealed class Spinner : Component
	{
		public float DegreesPerSecond { get; set; } = 360f;
		public Vector3 Axis { get; set; } = Vector3.Forward;
		/// <summary>Turn the "crystal" children instead of this object.</summary>
		public bool Children { get; set; }
		private float _angle;
		private Rotation _rest = Rotation.Identity;
		private bool _restTaken;

		protected override void OnUpdate()
		{
			_angle += DegreesPerSecond * Time.Delta;
			if ( Children )
			{
				foreach ( var child in GameObject.Children )
					if ( child.IsValid() && child.Name.StartsWith( "crystal" ) ) child.LocalRotation = Rotation.FromAxis( Axis, _angle ) * Rotation.FromPitch( 90f );
				return;
			}
			if ( !_restTaken ) { _rest = LocalRotation; _restTaken = true; }
			LocalRotation = _rest * Rotation.FromAxis( Axis, _angle );
		}
	}

	// ------------------------------------------------------------------ the bee (swarm)

	/// <summary>
	/// A bee: a dark little body with an amber glow behind it and two pale wings that the caller flaps. Returns the
	/// wing objects (null when the meshes are not compiled yet, in which case the palette's core carries the look).
	/// </summary>
	public static List<GameObject> AddBee( FireballVfx.Rig rig, SpellPalette p, float scale, GameObject under = null )
	{
		var parent = under ?? rig.Root;
		var k = scale / 0.3f;   // the swarm ships at VfxScale 0.3
		var body = AddMesh( parent, "bee_body", "fx_sphere_01", new Color( 0.08f, 0.06f, 0.03f, 1f ), 0.12f * k, additive: false );
		if ( body is null ) return null;
		// the final local scale is set before the rig captures it, or the charge would grow from the wrong base
		body.GameObject.LocalScale = new Vector3( 0.17f, 0.11f, 0.11f ) * k;
		rig.AddMesh( body );
		var glow = AddMesh( parent, "bee_glow", "fx_sphere_01", p.Core.WithAlpha( 0.55f ), 0.16f * k, additive: true );
		if ( glow is not null ) { glow.GameObject.LocalPosition = Vector3.Backward * 2f * k; rig.AddMesh( glow ); }
		var wings = new List<GameObject>( 2 );
		for ( var side = -1; side <= 1; side += 2 )
		{
			var wing = AddMesh( parent, side < 0 ? "bee_wing_l" : "bee_wing_r", "fx_ring_01", p.Hot.WithAlpha( 0.35f ), 0.06f * k, additive: true );
			if ( wing is null ) continue;
			wing.GameObject.LocalPosition = Vector3.Left * side * 2.2f * k + Vector3.Up * 1.5f * k;
			wing.GameObject.LocalScale = new Vector3( 0.05f, 0.09f, 0.01f ) * k;
			rig.AddMesh( wing );
			wings.Add( wing.GameObject );
		}
		return wings;
	}

	/// <summary>Flap the wings: a fast roll about the body's forward axis, mirrored per side.</summary>
	public static void FlapWings( List<GameObject> wings, float time )
	{
		if ( wings is null ) return;
		var flap = MathF.Sin( time * MathF.PI * 2f * 28f ) * 40f;
		for ( var i = 0; i < wings.Count; i++ )
		{
			var w = wings[i];
			if ( !w.IsValid() ) continue;
			var side = i == 0 ? -1f : 1f;
			w.LocalRotation = Rotation.FromRoll( 90f + flap * side ) ;
		}
	}

	/// <summary>
	/// Carries its object around the parent's origin on a tilted ring, facing the way it travels. Its ring is a real
	/// distance, so it closes in with the charge (IRigScaled) instead of staying wide around a tiny orb.
	/// </summary>
	public sealed class Orbiter : Component, FireballVfx.IRigScaled
	{
		public float Radius { get; set; } = 10f;
		public float DegreesPerSecond { get; set; } = 360f;
		public float Phase { get; set; }
		public float Tilt { get; set; }
		/// <summary>Point the object along its own path (a bee flies nose first; a mote does not care).</summary>
		public bool FaceTravel { get; set; }
		/// <summary>A small bob on top of the ring, so a swarm does not read as clockwork.</summary>
		public float Bob { get; set; }
		public float BobRate { get; set; } = 7f;
		private float _angle;
		private float _scale = 1f;

		public void SetRigScale( float k ) => _scale = MathF.Max( 0.05f, k );

		protected override void OnUpdate()
		{
			_angle += DegreesPerSecond * Time.Delta;
			var r = Radius * _scale;
			var ring = Rotation.FromPitch( Tilt );
			var at = ring * (Rotation.FromYaw( _angle + Phase ).Forward * r);
			if ( Bob > 0f ) at += Vector3.Up * (MathF.Sin( Time.Now * BobRate + Phase ) * Bob * _scale);
			LocalPosition = at;
			if ( !FaceTravel ) return;
			var ahead = ring * (Rotation.FromYaw( _angle + Phase + 8f ).Forward * r) - at;
			if ( !ahead.IsNearZeroLength ) LocalRotation = Rotation.LookAt( ahead.Normal );
		}
	}

	// ------------------------------------------------------------------ the arc (chain lightning)

	/// <summary>Crackling flashes of the lightning sprite around a core: the chain's charge orb and carrier.</summary>
	public static void AddLightningFlashes( FireballVfx.Rig rig, SpellPalette p )
	{
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( rig.Root, "arc_flashes", TexLightning, additive: true, bloom: true );
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 6f; em.Velocity = 0f; em.Rate = 14f; em.Burst = 0f; em.Loop = true;
		fx.PreWarm = 0f; fx.MaxParticles = 8; fx.LocalSpace = 1f;
		fx.Lifetime = new ParticleFloat( 0.05f, 0.1f );
		fx.Scale = new ParticleFloat( 16f, 26f );
		fx.Roll = new ParticleFloat( 0f, 360f );
		fx.Brightness = FireballVfx.Flicker( 1f, 2.5f );
		fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
		fx.Alpha = FireballVfx.OverLife( 1f, 0f );
	}

	/// <summary>A bolt through `points`: per leg a jittered ribbon, a flash and a light at every node. Gone in `life` seconds.</summary>
	public static void SpawnArc( Scene scene, IReadOnlyList<Vector3> points, SpellPalette p, float scale, float life = 0.22f )
	{
		if ( points is null || points.Count < 2 ) return;
		var root = new GameObject( true, "chain_arc" );
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = life + 0.3f;
		var arc = root.Components.Create<ArcJitter>();
		arc.Life = life;
		arc.Width = 4.5f * MathF.Max( 0.5f, scale / 0.55f );
		arc.Tint = p.Core;

		for ( var i = 1; i < points.Count; i++ )
		{
			var go = new GameObject( true, "leg" ) { Parent = root };
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var line = go.Components.Create<LineRenderer>();
			line.UseVectorPoints = true;
			line.CastShadows = false;
			line.Opaque = false;
			line.Additive = true;
			var texturing = TrailTextureConfig.Default;
			texturing.Material = Material.Load( "materials/blade_trail.vmat" );
			line.Texturing = texturing;
			line.Width = new Curve( new Curve.Frame( 0f, arc.Width ), new Curve.Frame( 1f, arc.Width * 0.6f ) );
			line.Color = new Gradient( new Gradient.ColorFrame( 0f, p.Hot ), new Gradient.ColorFrame( 0.5f, p.Core ), new Gradient.ColorFrame( 1f, p.Hot ) );
			arc.Legs.Add( (line, points[i - 1], points[i]) );
		}

		for ( var i = 0; i < points.Count; i++ )
		{
			var node = new GameObject( true, "node" ) { Parent = root };
			node.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			node.WorldPosition = points[i];
			var (go, fx, _) = FireballVfx.AddSpriteLayer( node, "node_flash", TexLightning, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f; em.Rate = 0f; em.Burst = 3f; em.Loop = false;
			fx.MaxParticles = 4; fx.Lifetime = new ParticleFloat( 0.1f, 0.16f );
			fx.Scale = new ParticleFloat( 22f * scale / 0.55f, 34f * scale / 0.55f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = 2f;
			fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
			var (go2, fx2, _) = FireballVfx.AddSpriteLayer( node, "node_glow", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em2 = go2.Components.Create<ParticleSphereEmitter>();
			em2.Radius = 0f; em2.Rate = 0f; em2.Burst = 1f; em2.Loop = false;
			fx2.MaxParticles = 2; fx2.Lifetime = 0.18f;
			fx2.Scale = FireballVfx.OverLife( 30f * scale / 0.55f, 8f );
			fx2.Brightness = 2f;
			fx2.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, Color.White ), new Gradient.ColorFrame( 1f, p.Core ) );
			fx2.Alpha = FireballVfx.OverLife( 1f, 0f );
			var light = go2.Components.Create<ParticleLightRenderer>();
			light.Brightness = 2f;
			light.Scale = 40f * scale / 0.55f;
			light.LightColor = p.Core;
		}
	}

	/// <summary>Re-jitters every leg's points a few times over its life so the bolt crackles instead of standing still.</summary>
	public sealed class ArcJitter : Component
	{
		public float Life = 0.2f;
		public float Width = 4f;
		public Color Tint = Color.White;
		public readonly List<(LineRenderer line, Vector3 a, Vector3 b)> Legs = new();
		private float _born = -1f;
		private float _nextJitter;

		protected override void OnUpdate()
		{
			if ( _born < 0f ) _born = Time.Now;
			var age = Time.Now - _born;
			var k = Math.Clamp( 1f - age / Life, 0f, 1f );
			if ( Time.Now >= _nextJitter )
			{
				_nextJitter = Time.Now + 0.03f;
				foreach ( var (line, a, b) in Legs )
				{
					if ( !line.IsValid() ) continue;
					var len = a.Distance( b );
					var n = Math.Clamp( (int)(len / 35f) + 2, 3, 14 );
					var dir = (b - a).Normal;
					var side = Vector3.Cross( dir, Vector3.Up ).Normal;
					if ( side.IsNearlyZero() ) side = Vector3.Cross( dir, Vector3.Forward ).Normal;
					var up = Vector3.Cross( dir, side ).Normal;
					var amp = MathF.Min( 12f, len * 0.08f );
					var list = new List<Vector3>( n );
					for ( var i = 0; i < n; i++ )
					{
						var t = i / (n - 1f);
						var p = Vector3.Lerp( a, b, t );
						if ( i > 0 && i < n - 1 ) p += side * Game.Random.Float( -amp, amp ) + up * Game.Random.Float( -amp, amp );
						list.Add( p );
					}
					line.VectorPoints = list;
				}
			}
			foreach ( var (line, _, _) in Legs )
			{
				if ( !line.IsValid() ) continue;
				var w = Width * (0.4f + 0.6f * k);
				line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w * 0.6f ) );
				line.Enabled = k > 0f;
			}
		}
	}

	// ------------------------------------------------------------------ the necrotic wisp (siphon bolt)

	/// <summary>
	/// The siphon bolt's body: witchfire. A dark hollow heart wrapped in cold green flame, two sigil rings turning
	/// across the shot, stolen motes held in orbit, and a vapour that hangs in the air behind it rather than falling.
	/// The dark centre is the whole trick: green fire around a hole reads as necromancy, where a solid green ball
	/// would only read as a fireball that changed colour.
	/// </summary>
	public static void AddNecroticComet( FireballVfx.Rig rig, SpellPalette p, float scale, float radius )
	{
		var root = rig.Root;
		var hollow = new Color( 0.03f, 0.07f, 0.04f, 1f );

		// The rings, across the flight (the bolt's object turns with its direction, so a pitched ring stands up in the shot).
		for ( var i = 0; i < 2; i++ )
		{
			var ring = AddMesh( root, "comet_ring_" + i, "fx_ring_01", p.Core.WithAlpha( 0.55f ), (0.16f + 0.06f * i) * scale / 0.6f, additive: true );
			if ( ring is null ) break;
			ring.GameObject.LocalRotation = Rotation.FromPitch( 90f ) * Rotation.FromRoll( i == 0 ? 30f : -30f );
			rig.AddMesh( ring );
			ring.GameObject.Components.Create<Spinner>().DegreesPerSecond = i == 0 ? 220f : -160f;
		}

		// The hollow: a dark centre the fire never fills, so the bolt has a hole in it.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "necro_hollow", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = radius * 0.15f; em.Velocity = 0f; em.Rate = 18f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 14; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.15f, 0.28f );
			fx.Scale = new ParticleFloat( radius * 1.1f, radius * 1.5f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.RandomBetween( hollow, p.Smoke );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.95f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Cold flame: green tongues rolling around the hollow, thinner and slower than fire's boil.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "witchfire", FireballVfx.TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = radius * 0.7f; em.Velocity = 14f; em.Rate = 30f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 34; fx.LocalSpace = 1f; fx.Damping = 3f;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.38f );
			fx.Scale = FireballVfx.OverLife( radius * 1.7f, radius * 0.4f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 1.4f );
			fx.Gradient = FireballVfx.Banded( p.Hot, p.Core, p.Core.Darken( 0.55f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		// Stolen motes held in orbit: what the bolt has already taken, riding along with it.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "held_motes", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 10f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 9; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = new ParticleFloat( radius * 0.3f, radius * 0.55f );
			fx.Brightness = FireballVfx.Flicker( 0.7f, 2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 1f ), new Curve.Frame( 0.8f, 1f ), new Curve.Frame( 1f, 0f ) );
			rig.Orbit = new FireballVfx.OrbitState { Center = root.WorldPosition, Radius = radius * 1.5f };
			rig.BaseOrbitRadius = rig.Orbit.Radius;
			go.Components.Create<FireballVfx.OrbitController>().State = rig.Orbit;
			FireballVfx.Register( rig, fx, em );
		}

		// The wake: heavy green vapour shed per distance that sinks a little and hangs, so the shot leaves a corridor
		// of gas rather than a streak. Non-additive, so it dims the room behind it instead of lighting it.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "necro_vapour", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = radius * 0.6f; em.Velocity = 10f; em.Rate = 4f; em.RateOverDistance = 0.05f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 40; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -25f );
			fx.Lifetime = new ParticleFloat( 0.7f, 1.2f );
			fx.Scale = FireballVfx.OverLife( radius * 0.7f, radius * 2.2f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.55f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.55f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Souls coming off it: bright motes drifting UP out of the wake, the one thing that rises.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "souls", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = radius * 0.8f; em.Velocity = 18f; em.Rate = 5f; em.RateOverDistance = 0.03f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 24; fx.LocalSpace = 0f; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 110f );
			fx.Lifetime = new ParticleFloat( 0.5f, 1f );
			fx.Scale = FireballVfx.OverLife( radius * 0.4f, radius * 0.08f );
			fx.Brightness = FireballVfx.Flicker( 0.5f, 2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 1f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}
	}

	// ------------------------------------------------------------------ the void orb

	/// <summary>The black sphere with the violet rim (heden_void_orb.shader on the dev sphere) at the rig's centre; the rig's own fire fades to a dark halo.</summary>
	public static ModelRenderer AddVoidSphere( FireballVfx.Rig rig, SpellPalette p, float radius )
	{
		var go = new GameObject( true, "void_sphere" ) { Parent = rig.Root };
		go.LocalPosition = Vector3.Zero;
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var r = go.Components.Create<ModelRenderer>();
		r.Model = Model.Load( "models/dev/sphere.vmdl" );
		_voidOrb ??= Material.FromShader( "shaders/heden_void_orb.shader" );
		if ( _voidOrb is not null ) r.MaterialOverride = _voidOrb;
		else r.Tint = Color.Black;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		var size = r.Model?.Bounds.Size.x ?? 1f;
		go.LocalScale = Vector3.One * (radius * 2f / MathF.Max( 1f, size ));
		if ( r.SceneObject.IsValid() )
		{
			r.SceneObject.Attributes.Set( "VoidColor", new Vector3( p.Core.r, p.Core.g, p.Core.b ) );
			r.SceneObject.Attributes.Set( "VoidCharge", 0.6f );
			r.SceneObject.Attributes.Set( "VoidCollapse", 0f );
		}
		return rig.AddMesh( r );
	}

	public static void SetVoid( ModelRenderer sphere, float charge, float collapse )
	{
		if ( !sphere.IsValid() || !sphere.SceneObject.IsValid() ) return;
		sphere.SceneObject.Attributes.Set( "VoidCharge", charge );
		sphere.SceneObject.Attributes.Set( "VoidCollapse", collapse );
	}

	/// <summary>The orb spending itself: the sphere whitens and falls into its own point over `seconds`, then is gone.</summary>
	public static void VoidCollapse( Scene scene, Vector3 position, SpellPalette p, float radius, float seconds = 0.14f )
	{
		var go = new GameObject( true, "void_collapse" ) { WorldPosition = position };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "spellfire" );
		var r = go.Components.Create<ModelRenderer>();
		r.Model = Model.Load( "models/dev/sphere.vmdl" );
		_voidOrb ??= Material.FromShader( "shaders/heden_void_orb.shader" );
		if ( _voidOrb is not null ) r.MaterialOverride = _voidOrb;
		else r.Tint = Color.Black;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		var size = r.Model?.Bounds.Size.x ?? 1f;
		var full = radius * 2f / MathF.Max( 1f, size );
		go.LocalScale = Vector3.One * full;
		if ( r.SceneObject.IsValid() ) r.SceneObject.Attributes.Set( "VoidColor", new Vector3( p.Core.r, p.Core.g, p.Core.b ) );
		var c = go.Components.Create<VoidCollapseMotion>();
		c.Renderer = r;
		c.FullScale = full;
		c.Seconds = seconds;
	}

	/// <summary>Drives a collapsing void sphere and destroys it at the end.</summary>
	public sealed class VoidCollapseMotion : Component
	{
		public ModelRenderer Renderer;
		public float FullScale = 1f;
		public float Seconds = 0.14f;
		private float _born = -1f;
		protected override void OnUpdate()
		{
			if ( _born < 0f ) _born = Time.Now;
			var k = Math.Clamp( (Time.Now - _born) / MathF.Max( 0.01f, Seconds ), 0f, 1f );
			LocalScale = Vector3.One * FullScale * (1f - k);
			SetVoid( Renderer, 1f, k );
			if ( k >= 1f ) GameObject.Destroy();
		}
	}

	// ------------------------------------------------------------------ thrown meshes

	/// <summary>`count` copies of an FX mesh flung from `position`, spinning, falling under gravity and shrinking away.</summary>
	public static void MeshBurst( Scene scene, Vector3 position, Vector3 normal, string model, int count, Color tint, float scale, float speed, float life, bool additive )
	{
		if ( FxModel( model ) is null ) return;
		var root = new GameObject( true, "mesh_burst" );
		root.WorldPosition = position;
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = life + 0.2f;
		for ( var i = 0; i < count; i++ )
		{
			var r = AddMesh( root, "chip", model, tint, scale * Game.Random.Float( 0.7f, 1.2f ), additive );
			if ( r is null ) return;
			var dir = (normal * 0.8f + Vector3.Random.Normal).Normal;
			var motion = r.GameObject.Components.Create<FxMeshMotion>();
			motion.Velocity = dir * speed * Game.Random.Float( 0.5f, 1f );
			motion.Spin = new Angles( Game.Random.Float( -540f, 540f ), Game.Random.Float( -540f, 540f ), Game.Random.Float( -540f, 540f ) );
			motion.Life = life * Game.Random.Float( 0.7f, 1f );
			motion.BaseScale = r.GameObject.LocalScale.x;
			r.GameObject.WorldRotation = Rotation.Random;
		}
	}

	/// <summary>Velocity, gravity, spin and shrink for a thrown FX mesh.</summary>
	public sealed class FxMeshMotion : Component
	{
		public Vector3 Velocity;
		public Angles Spin;
		public float Life = 0.8f;
		public float Gravity = 700f;
		public float BaseScale = 1f;
		private float _born = -1f;
		protected override void OnUpdate()
		{
			if ( _born < 0f ) _born = Time.Now;
			var age = Time.Now - _born;
			var k = 1f - Math.Clamp( age / Life, 0f, 1f );
			var dt = Time.Delta;
			Velocity += Vector3.Down * Gravity * dt;
			WorldPosition += Velocity * dt;
			WorldRotation = Rotation.From( Spin * dt ) * WorldRotation;
			LocalScale = Vector3.One * BaseScale * MathF.Max( 0.01f, k );
			if ( k <= 0f ) GameObject.Destroy();
		}
	}
}
