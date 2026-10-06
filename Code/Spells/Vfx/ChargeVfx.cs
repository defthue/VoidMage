using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// What sits at the staff's gem while a spell charges: one bespoke recipe per body style, none of them the bolt's
/// look parked in mid-air. A charge is watched for a whole second from a foot away, so it is built from motion the
/// bolt cannot afford: the element being gathered out of the air (fire, necrotic), a body that turns with the aim
/// (ice, laser), things circling the hand (bees, void wisps). Every recipe returns a <see cref="FireballVfx.Rig"/>,
/// so the caster grows it, drives it and takes it away through the same four calls.
///
/// The caster owns the root's position AND rotation (SpellCaster.Presentation): the rig faces where the player
/// aims, which is what lets a shard point down the shot and a ring stand across it.
/// </summary>
public static class ChargeVfx
{
	public static FireballVfx.Rig Build( GameObject parent, SpellDefinition spell )
	{
		var p = spell?.Palette ?? SpellPalette.Fire;
		var style = spell?.BodyStyle ?? SpellBodyStyle.Flame;
		return style switch
		{
			SpellBodyStyle.Crystal => Ice( parent, p ),
			SpellBodyStyle.Arc => Arc( parent, p ),
			SpellBodyStyle.Beam => Laser( parent, p ),
			SpellBodyStyle.Orb => Void( parent, p ),
			SpellBodyStyle.Swarm => Swarm( parent, p ),
			SpellBodyStyle.Necrotic => Necrotic( parent, p ),
			_ => Flame( parent, p ),
		};
	}

	// ------------------------------------------------------------------ fireball: embers drawn in

	/// <summary>
	/// The fireball gathers: sparks and cinders stream in from a shell around the hand and feed a boiling ball of
	/// flame that licks upward. No static disc; the silhouette is made of moving pieces the whole time.
	/// </summary>
	private static FireballVfx.Rig Flame( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_flame", p );
		var root = rig.Root;

		// The gather: motes falling in from the air around the hand, the tension before the throw.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "gather_sparks", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 30f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 40; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.35f, 0.6f );
			fx.Scale = FireballVfx.OverLife( 7f, 2f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 2.2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 1f ), new Curve.Frame( 0.9f, 1f ), new Curve.Frame( 1f, 0f ) );
			rig.Gather = new FireballVfx.GatherState { Center = root.WorldPosition, Radius = 55f };
			rig.BaseGatherRadius = rig.Gather.Radius;
			go.Components.Create<FireballVfx.GatherController>().State = rig.Gather;
			FireballVfx.Register( rig, fx, em );
		}

		// Fatter cinders on the same fall, slower and darker: weight behind the sparks.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "gather_cinders", FireballVfx.TexCircleSmall, additive: true, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 12f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 18; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.5f, 0.8f );
			fx.Scale = FireballVfx.OverLife( 10f, 3f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core.Darken( 0.3f ), p.Ember );
			fx.Alpha = FireballVfx.SoftAlpha();
			var g = go.Components.Create<FireballVfx.GatherController>();
			g.State = rig.Gather;
			g.SpiralDegrees = 200f;
			FireballVfx.Register( rig, fx, em );
		}

		// The heart the motes feed: a small dense white core.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "core_hot", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 22f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 24; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.09f, 0.16f );
			fx.Scale = new ParticleFloat( 18f, 26f );
			fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		// The boil: flame discs turning over each other, the cartoon ball made of movement.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "core_flame", FireballVfx.TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 7f; em.Velocity = 12f; em.Rate = 34f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 48; fx.LocalSpace = 1f; fx.Damping = 2f;
			fx.Lifetime = new ParticleFloat( 0.18f, 0.34f );
			fx.Scale = FireballVfx.OverLife( 22f, 4f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Core.Darken( 0.4f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		// Tongues licking up off the ball: the heat wobble that tells it is not a sprite.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "licks", "vfx/flame_lick.png", additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 5f; em.Velocity = 26f; em.Rate = 14f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 16; fx.LocalSpace = 1f; fx.Damping = 1.5f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 90f );
			fx.Lifetime = new ParticleFloat( 0.2f, 0.4f );
			fx.Scale = FireballVfx.OverLife( 16f, 4f );
			fx.Roll = new ParticleFloat( -25f, 25f );
			fx.Brightness = FireballVfx.Flicker( 0.9f, 1.5f );
			fx.Gradient = FireballVfx.Banded( p.Hot, p.Core, p.Core.Darken( 0.5f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	// ------------------------------------------------------------------ ice: a shard being cut out of the air

	private static FireballVfx.Rig Ice( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_ice", p, 150f );
		var root = rig.Root;

		// The shard itself, along the aim (the rig root faces where the player looks).
		StyleVfx.AddCrystal( rig, p, 0.7f );

		// Chips circling it, the shard still assembling.
		for ( var i = 0; i < 3; i++ )
		{
			var chip = StyleVfx.AddMesh( root, "ice_chip_" + i, "fx_crystalshard_01", p.Core.WithAlpha( 0.8f ), 0.22f, additive: false );
			if ( chip is null ) break;
			rig.AddMesh( chip );
			var o = chip.GameObject.Components.Create<StyleVfx.Orbiter>();
			o.Radius = 13f; o.DegreesPerSecond = 150f + 40f * i; o.Phase = 120f * i; o.Tilt = 30f * (i - 1);
			o.Bob = 2f; o.FaceTravel = true;
			rig.Scaled.Add( o );
		}

		// Frost breath: pale mist sinking off the shard.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "frost", FireballVfx.TexSoftSpot, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 7f; em.Velocity = 8f; em.Rate = 12f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 20; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -35f );
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = FireballVfx.OverLife( 6f, 18f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Hot, Color.White, 0.4f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.35f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Glints: hard little sparkles catching on the facets.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "glints", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 11f; em.Velocity = 0f; em.Rate = 9f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 10; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.1f, 0.22f );
			fx.Scale = new ParticleFloat( 4f, 9f );
			fx.Brightness = FireballVfx.Flicker( 1f, 2.4f );
			fx.Gradient = FireballVfx.RandomBetween( Color.White, p.Hot );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	// ------------------------------------------------------------------ chain: a caged spark

	private static FireballVfx.Rig Arc( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_arc", p, 170f );
		var root = rig.Root;

		// A tight violet core that never sits still.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "spark_core", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 3f; em.Velocity = 0f; em.Rate = 26f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 20; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.05f, 0.11f );
			fx.Scale = new ParticleFloat( 12f, 22f );
			fx.Brightness = FireballVfx.Flicker( 1f, 2.6f );
			fx.Gradient = FireballVfx.RandomBetween( Color.White, p.Core );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
			FireballVfx.Register( rig, fx, em );
		}

		StyleVfx.AddLightningFlashes( rig, p );

		// Sparks spitting off and dying at once, the charge fighting the cage.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "spit", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 4f; em.Velocity = 90f; em.Rate = 14f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 18; fx.LocalSpace = 0f; fx.Damping = 6f;
			fx.Stretch = 0.2f;
			fx.Lifetime = new ParticleFloat( 0.1f, 0.25f );
			fx.Scale = FireballVfx.OverLife( 6f, 1f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 2.4f );
			fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
			FireballVfx.Register( rig, fx, em );
		}

		// Real little arcs jumping off the gem every few frames.
		var crackle = root.Components.Create<ArcCrackle>();
		crackle.Palette = p;
		rig.Scaled.Add( crackle );

		return rig;
	}

	/// <summary>Throws a short jagged arc from the charge to a point beside it, over and over.</summary>
	public sealed class ArcCrackle : Component, FireballVfx.IRigScaled
	{
		public SpellPalette Palette = SpellPalette.Lightning;
		public float Reach { get; set; } = 26f;
		private float _next;
		private float _scale = 1f;

		public void SetRigScale( float k ) => _scale = k;

		protected override void OnUpdate()
		{
			if ( Time.Now < _next ) return;
			_next = Time.Now + Game.Random.Float( 0.08f, 0.2f );
			var from = WorldPosition;
			var to = from + Vector3.Random.Normal * Reach * MathF.Max( 0.3f, _scale );
			StyleVfx.SpawnArc( Scene, new[] { from, Vector3.Lerp( from, to, 0.5f ) + Vector3.Random.Normal * 4f, to }, Palette, 0.35f * _scale, 0.1f );
		}
	}

	// ------------------------------------------------------------------ laser: a lens, not a fireball

	/// <summary>
	/// The laser does not carry a ball: the staff's gem lights, a ring stands across the aim and tightens, and the
	/// hairline (built by the caster, which knows where the crosshair points) shows the line the burst will take.
	/// </summary>
	private static FireballVfx.Rig Laser( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_laser", p, 130f );
		var root = rig.Root;

		// The gem: a tight flickering halo, no body.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "halo", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Rate = 24f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 16; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.1f, 0.18f );
			fx.Scale = new ParticleFloat( 11f, 16f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 1.8f );
			fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		// The focus ring: standing across the shot, tightening as the charge fills (SetRigScale shrinks it).
		var ring = StyleVfx.AddMesh( root, "focus_ring", "fx_ring_01", p.Core.WithAlpha( 0.7f ), 0.2f, additive: true );
		if ( ring is not null )
		{
			ring.GameObject.LocalRotation = Rotation.FromPitch( 90f );   // the ring's disc faces down the aim
			rig.AddMesh( ring );
			var spin = ring.GameObject.Components.Create<StyleVfx.Spinner>();
			spin.DegreesPerSecond = 220f;
		}

		// Charge drawn along the barrel: motes running up to the gem.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "feed", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 18f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 22; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.4f );
			fx.Scale = FireballVfx.OverLife( 5f, 1f );
			fx.Brightness = FireballVfx.Flicker( 1f, 2.2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.OverLife( 1f, 0f );
			rig.Gather = new FireballVfx.GatherState { Center = root.WorldPosition, Radius = 40f };
			rig.BaseGatherRadius = rig.Gather.Radius;
			var g = go.Components.Create<FireballVfx.GatherController>();
			g.State = rig.Gather;
			g.SpiralDegrees = 260f;
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	// ------------------------------------------------------------------ void: a small singularity

	private static FireballVfx.Rig Void( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_void", p, 200f );
		var root = rig.Root;

		// The sphere itself: small, black, with the violet horizon.
		StyleVfx.AddVoidSphere( rig, p, 5f );

		// Wisps falling in: what makes it read as pulling rather than glowing.
		var wisps = root.Components.Create<WispSpiral>();
		wisps.Palette = p;
		wisps.Count = 4;
		wisps.BaseRadius = 34f;
		rig.Scaled.Add( wisps );

		// Dark fumes crawling off it, lit at the rim only.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "void_fumes", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 7f; em.Velocity = 10f; em.Rate = 9f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 14; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = FireballVfx.OverLife( 5f, 16f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.5f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.5f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	/// <summary>heden's void wisps: ribbons spiralling in to the centre and respawning at the rim, on their own clocks.</summary>
	public sealed class WispSpiral : Component, FireballVfx.IRigScaled
	{
		public SpellPalette Palette = SpellPalette.Void;
		public int Count { get; set; } = 4;
		public float BaseRadius { get; set; } = 34f;

		private struct Wisp { public GameObject Go; public float Angle, Radius, Height, Spin, Fall; }
		private readonly List<Wisp> _wisps = new();
		private float _scale = 1f;

		public void SetRigScale( float k ) => _scale = MathF.Max( 0.05f, k );

		protected override void OnStart()
		{
			for ( var i = 0; i < Count; i++ ) _wisps.Add( Spawn( true ) );
		}

		private Wisp Spawn( bool anywhere )
		{
			var go = new GameObject( true, "void_wisp" ) { Parent = GameObject };
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var trail = go.Components.Create<TrailRenderer>();
			trail.LifeTime = 0.3f;
			trail.MaxPoints = 24;
			trail.PointDistance = 1f;
			trail.CastShadows = false;
			trail.Opaque = false;
			trail.BlendMode = BlendMode.Normal;
			var texturing = TrailTextureConfig.Default;
			texturing.Material = Material.Load( "materials/blade_trail.vmat" );
			trail.Texturing = texturing;
			trail.Width = new Curve( new Curve.Frame( 0f, 2.4f ), new Curve.Frame( 1f, 0.2f ) );
			var c = Palette.Core;
			trail.Color = new Gradient(
				new Gradient.ColorFrame( 0f, new Color( c.r, c.g, c.b, 0.9f ) * 1.5f ),
				new Gradient.ColorFrame( 1f, new Color( c.r, c.g, c.b, 0f ) ) );
			return new Wisp
			{
				Go = go,
				Angle = Game.Random.Float( 0f, MathF.Tau ),
				Radius = anywhere ? Game.Random.Float( 0.3f, 1f ) : Game.Random.Float( 0.85f, 1f ),
				Height = Game.Random.Float( -0.3f, 0.5f ),
				Spin = Game.Random.Float( 2.4f, 4.2f ) * (Game.Random.Float( 0f, 1f ) < 0.85f ? 1f : -1f),
				Fall = Game.Random.Float( 0.5f, 0.95f ),
			};
		}

		protected override void OnUpdate()
		{
			var dt = Time.Delta;
			var centre = WorldPosition;
			for ( var i = 0; i < _wisps.Count; i++ )
			{
				var w = _wisps[i];
				if ( !w.Go.IsValid() ) continue;
				// the spiral tightens as it closes, so the last stretch is the fastest
				var closeness = 1f - w.Radius;
				w.Angle += w.Spin * (1f + closeness * 2.5f) * dt;
				w.Radius -= w.Fall * (1f + closeness * 2f) * dt;
				w.Height = MathX.Lerp( w.Height, 0f, 1f - MathF.Exp( -3f * dt ) );
				if ( w.Radius <= 0.04f )
				{
					w.Radius = Game.Random.Float( 0.85f, 1f );
					w.Angle = Game.Random.Float( 0f, MathF.Tau );
					w.Height = Game.Random.Float( -0.3f, 0.5f );
				}
				var r = BaseRadius * _scale * w.Radius;
				w.Go.WorldPosition = centre + new Vector3( MathF.Cos( w.Angle ), MathF.Sin( w.Angle ), 0f ) * r
					+ Vector3.Up * (w.Height * BaseRadius * _scale);
				_wisps[i] = w;
			}
		}
	}

	// ------------------------------------------------------------------ swarm: bees around the hand

	private static FireballVfx.Rig Swarm( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_swarm", p, 110f );
		var root = rig.Root;
		rig.LightBrightness = 0.5f;

		// Five bees on their own tilted rings, nose first, bobbing: the swarm assembling in the hand.
		for ( var i = 0; i < 5; i++ )
		{
			var holder = new GameObject( true, "bee_" + i ) { Parent = root };
			holder.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			var wings = StyleVfx.AddBee( rig, p, 0.22f, holder );
			if ( wings is null ) { holder.Destroy(); break; }
			var o = holder.Components.Create<StyleVfx.Orbiter>();
			o.Radius = Game.Random.Float( 9f, 15f );
			o.DegreesPerSecond = Game.Random.Float( 200f, 340f ) * (i % 2 == 0 ? 1f : -1f);
			o.Phase = 72f * i;
			o.Tilt = Game.Random.Float( -50f, 50f );
			o.Bob = 3f; o.BobRate = Game.Random.Float( 5f, 9f );
			o.FaceTravel = true;
			rig.Scaled.Add( o );
			holder.Components.Create<BeeFlap>().Wings = wings;
		}

		// A warm haze the bees fly through, so the cloud has a middle.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "hive_glow", FireballVfx.TexSoftSpot, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 6f; em.Velocity = 0f; em.Rate = 10f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 10; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.2f, 0.35f );
			fx.Scale = new ParticleFloat( 14f, 22f );
			fx.Gradient = FireballVfx.RandomBetween( p.Core, p.Ember );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.3f, 0.5f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Loose motes: the rest of the swarm still on its way in.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "motes", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 16f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 22; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.3f, 0.55f );
			fx.Scale = FireballVfx.OverLife( 4f, 1.5f );
			fx.Brightness = FireballVfx.Flicker( 0.7f, 2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.SoftAlpha();
			rig.Gather = new FireballVfx.GatherState { Center = root.WorldPosition, Radius = 45f };
			rig.BaseGatherRadius = rig.Gather.Radius;
			var g = go.Components.Create<FireballVfx.GatherController>();
			g.State = rig.Gather;
			g.SpiralDegrees = 320f;
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	/// <summary>Flaps one bee's wings (the charge's bees drive themselves; the bolt's are driven by SpellVfx).</summary>
	public sealed class BeeFlap : Component
	{
		public List<GameObject> Wings;
		protected override void OnUpdate() => StyleVfx.FlapWings( Wings, Time.Now );
	}

	// ------------------------------------------------------------------ siphon: a soul lantern

	/// <summary>
	/// The necromancer's charge: a dark hollow wrapped in cold green flame inside two turning sigil rings, with
	/// motes of stolen life drawn in out of the room. The hole in the middle is the point of it: green fire around
	/// nothing reads as necromancy, where a solid green ball would read as a fireball in the wrong colour.
	/// </summary>
	private static FireballVfx.Rig Necrotic( GameObject parent, SpellPalette p )
	{
		var rig = FireballVfx.NewRig( parent, "charge_necrotic", p, 170f );
		var root = rig.Root;
		rig.LightBrightness = 0.8f;
		var hollow = new Color( 0.03f, 0.07f, 0.04f, 1f );

		// Two rings across the aim on opposite tilts: the sigil the life is pulled through.
		for ( var i = 0; i < 2; i++ )
		{
			var ring = StyleVfx.AddMesh( root, "necro_ring_" + i, "fx_ring_01", p.Core.WithAlpha( 0.6f ), 0.16f + 0.05f * i, additive: true );
			if ( ring is null ) break;
			ring.GameObject.LocalRotation = Rotation.FromPitch( 90f ) * Rotation.FromRoll( i == 0 ? 25f : -25f );
			rig.AddMesh( ring );
			ring.GameObject.Components.Create<StyleVfx.Spinner>().DegreesPerSecond = i == 0 ? 160f : -110f;
		}

		// The hollow at the centre, which the flame never fills.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "necro_hollow", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 2f; em.Velocity = 0f; em.Rate = 16f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 14; fx.LocalSpace = 1f;
			fx.Lifetime = new ParticleFloat( 0.18f, 0.32f );
			fx.Scale = new ParticleFloat( 12f, 17f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.RandomBetween( hollow, p.Smoke );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.95f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Cold flame licking around it, thinner and slower than the fireball's boil.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "witchfire", FireballVfx.TexCircle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 8f; em.Velocity = 16f; em.Rate = 26f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 32; fx.LocalSpace = 1f; fx.Damping = 3f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 40f );
			fx.Lifetime = new ParticleFloat( 0.22f, 0.42f );
			fx.Scale = FireballVfx.OverLife( 18f, 3f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Brightness = FireballVfx.Flicker( 0.8f, 1.5f );
			fx.Gradient = FireballVfx.Banded( p.Hot, p.Core, p.Core.Darken( 0.55f ) );
			fx.Alpha = FireballVfx.SoftAlpha();
			FireballVfx.Register( rig, fx, em );
		}

		// Green vapour rolling off it and sinking: the room's air going bad around the hand.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "vapour", FireballVfx.TexCircle, additive: false, bloom: false );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 7f; em.Velocity = 12f; em.Rate = 10f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 16; fx.LocalSpace = 0f; fx.Damping = 2f;
			fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, -30f );
			fx.Lifetime = new ParticleFloat( 0.5f, 0.9f );
			fx.Scale = FireballVfx.OverLife( 6f, 20f );
			fx.Roll = new ParticleFloat( 0f, 360f );
			fx.Gradient = FireballVfx.GradientOverLife(
				new Gradient.ColorFrame( 0f, Color.Lerp( p.Smoke, p.Core, 0.5f ) ),
				new Gradient.ColorFrame( 1f, p.Smoke ) );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.5f ), new Curve.Frame( 1f, 0f ) );
			FireballVfx.Register( rig, fx, em );
		}

		// Motes of life drawn in out of the air: the spell taking, not giving.
		{
			var (go, fx, _) = FireballVfx.AddSpriteLayer( root, "stolen", FireballVfx.TexSparkle, additive: true, bloom: true );
			var em = go.Components.Create<ParticleSphereEmitter>();
			em.Radius = 0f; em.Velocity = 0f; em.Rate = 22f; em.Burst = 0f; em.Loop = true;
			fx.PreWarm = 0f; fx.MaxParticles = 28; fx.LocalSpace = 0f;
			fx.Lifetime = new ParticleFloat( 0.4f, 0.75f );
			fx.Scale = FireballVfx.OverLife( 6f, 1.5f );
			fx.Brightness = FireballVfx.Flicker( 0.7f, 2.2f );
			fx.Gradient = FireballVfx.RandomBetween( p.Ember, p.Hot );
			fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.25f, 1f ), new Curve.Frame( 0.9f, 1f ), new Curve.Frame( 1f, 0f ) );
			rig.Gather = new FireballVfx.GatherState { Center = root.WorldPosition, Radius = 60f };
			rig.BaseGatherRadius = rig.Gather.Radius;
			var g = go.Components.Create<FireballVfx.GatherController>();
			g.State = rig.Gather;
			g.SpiralDegrees = 140f;
			FireballVfx.Register( rig, fx, em );
		}

		return rig;
	}

	// ------------------------------------------------------------------ the laser's aim hairline

	/// <summary>
	/// The thin line from the gem to where the crosshair points while the laser charges, so the burst is aimed and
	/// not guessed. Unparented on purpose: a LineRenderer's VectorPoints are object space, so an identity root is
	/// the only way object and world agree (heden learned this the hard way).
	/// </summary>
	public sealed class AimLine
	{
		public GameObject Root;
		public LineRenderer Line;

		public static AimLine Build( SpellPalette p )
		{
			var root = new GameObject( true, "charge_aim_line" );
			root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			root.Tags.Add( "spellfire" );
			var line = root.Components.Create<LineRenderer>();
			line.UseVectorPoints = true;
			line.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
			line.CastShadows = false;
			line.Opaque = false;
			var texturing = TrailTextureConfig.Default;
			texturing.Material = Material.Load( "shaders/beam_white.vmat" );   // tinted by the palette (beam_ray is the old red)
			line.Texturing = texturing;
			line.Width = new Curve( new Curve.Frame( 0f, 1.5f ), new Curve.Frame( 1f, 1.5f ) );
			var c = p.Core.WithAlpha( 0.35f );
			line.Color = new Gradient(
				new Gradient.ColorFrame( 0f, c.WithAlpha( 0f ) ),
				new Gradient.ColorFrame( 0.06f, c ),
				new Gradient.ColorFrame( 0.9f, c ),
				new Gradient.ColorFrame( 1f, c.WithAlpha( 0f ) ) );
			return new AimLine { Root = root, Line = line };
		}

		/// <summary>`charge01` widens the hairline as the shot readies. Sixteen points so the end fades have vertices to live on.</summary>
		public void Drive( Vector3 from, Vector3 to, float charge01 )
		{
			if ( !Line.IsValid() ) return;
			const int points = 16;
			var list = new List<Vector3>( points );
			for ( var i = 0; i < points; i++ ) list.Add( Vector3.Lerp( from, to, i / (points - 1f) ) );
			Line.VectorPoints = list;
			var w = MathX.Lerp( 1.2f, 4f, charge01 );
			Line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w ) );
		}

		public void Destroy()
		{
			if ( Root.IsValid() ) Root.Destroy();
			Root = null;
			Line = null;
		}
	}
}
