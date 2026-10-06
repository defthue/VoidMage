using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// What a hazard looks like on every peer, built from the same registration the host ticks: stations of the
/// element along a wake's line, a ring of them around an orbiting caster, a dark pool riding the void orb, a
/// pulsing floor spot under a mine. Subscribes to HazardField; a Ray grows its stations as the bolt draws it.
/// </summary>
public static class HazardVisuals
{
	private sealed class Look
	{
		public Hazard Hazard;
		public GameObject Root;
		public readonly List<GameObject> Stations = new();
		public int RayStations;
	}

	private static readonly Dictionary<Guid, Look> _looks = new();
	private static bool _hooked;
	private const float StationSpacing = 40f;

	public static void Hook()
	{
		if ( _hooked ) return;
		_hooked = true;
		HazardField.Added += OnAdded;
		HazardField.Removed += OnRemoved;
	}

	private static void OnAdded( Hazard h )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var root = new GameObject( true, "hazard_" + h.Kind.ToString().ToLowerInvariant() );
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		var look = new Look { Hazard = h, Root = root };
		_looks[h.Key] = look;
		var spell = h.Spell;
		var p = spell?.Palette ?? SpellPalette.Fire;
		var style = spell?.BodyStyle ?? SpellBodyStyle.Flame;

		switch ( h.Kind )
		{
			case HazardKind.Sphere:
				look.Stations.Add( Station( root, h.A, p, style, MathF.Max( 0.6f, h.Radius / 40f ) ) );
				break;
			case HazardKind.Capsule:
			{
				var len = h.A.Distance( h.B );
				var n = Math.Max( 1, (int)(len / StationSpacing) );
				for ( var i = 0; i <= n; i++ ) look.Stations.Add( Station( root, Vector3.Lerp( h.A, h.B, i / (float)n ), p, style, h.Width / 40f ) );
				break;
			}
			case HazardKind.Ring:
			{
				var n = Math.Max( 6, (int)(h.Radius * MathF.Tau / StationSpacing) );
				for ( var i = 0; i < n; i++ ) look.Stations.Add( Station( root, h.Centre, p, style, h.Width / 40f ) );
				break;
			}
			case HazardKind.FollowSphere:
				look.Stations.Add( Pool( root, h.Centre, p, h.Radius ) );
				break;
			case HazardKind.Mine:
				look.Stations.Add( MineSpot( root, h.A, p, h.Radius ) );
				break;
			case HazardKind.Ray:
				break;   // grown in Tick
		}
	}

	private static void OnRemoved( Hazard h )
	{
		if ( !_looks.TryGetValue( h.Key, out var look ) ) return;
		_looks.Remove( h.Key );
		if ( !look.Root.IsValid() ) return;
		// the emitters stop and the last sprites die on their own clocks
		foreach ( var fx in look.Root.Components.GetAll<ParticleEffect>( FindMode.EverythingInSelfAndDescendants ) )
		{
			var em = fx.Components.Get<ParticleSphereEmitter>();
			if ( em.IsValid() ) { em.Rate = 0f; em.Loop = false; }
		}
		foreach ( var sr in look.Root.Components.GetAll<SpriteRenderer>( FindMode.EverythingInSelfAndDescendants ) ) sr.Enabled = false;
		foreach ( var mr in look.Root.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) mr.Enabled = false;
		foreach ( var light in look.Root.Components.GetAll<PointLight>( FindMode.EverythingInSelfAndDescendants ) ) light.Enabled = false;
		look.Root.Components.GetOrCreate<VfxLifetime>().Seconds = 1.6f;
	}

	/// <summary>Every peer, per frame: rings follow their caster, pools their orb, rays grow.</summary>
	public static void Tick( float now )
	{
		if ( _looks.Count == 0 ) return;
		foreach ( var look in _looks.Values )
		{
			var h = look.Hazard;
			if ( !look.Root.IsValid() ) continue;
			switch ( h.Kind )
			{
				case HazardKind.Ring:
				{
					var c = h.Centre;
					var n = look.Stations.Count;
					for ( var i = 0; i < n; i++ )
					{
						var s = look.Stations[i];
						if ( !s.IsValid() ) continue;
						var a = 360f * i / n + now * 25f;
						s.WorldPosition = c + Rotation.FromYaw( a ).Forward * h.Radius + Vector3.Up * 8f;
					}
					break;
				}
				case HazardKind.FollowSphere:
					if ( look.Stations.Count > 0 && look.Stations[0].IsValid() ) look.Stations[0].WorldPosition = h.Centre;
					break;
				case HazardKind.Ray:
				{
					var len = h.RayLength( now );
					var want = (int)(len / StationSpacing);
					var spell = h.Spell;
					var p = spell?.Palette ?? SpellPalette.Fire;
					var style = spell?.BodyStyle ?? SpellBodyStyle.Flame;
					var dir = (h.B - h.A).Normal;
					while ( look.RayStations <= want )
					{
						var at = h.A + dir * (look.RayStations * StationSpacing);
						look.Stations.Add( Station( look.Root, at, p, style, h.Width / 40f ) );
						look.RayStations++;
					}
					break;
				}
			}
		}
	}

	// ------------------------------------------------------------------ builders

	/// <summary>One looping patch of the element on the ground: flames for fire, frost crystals for ice, sparks for lightning, a dark whorl for void.</summary>
	private static GameObject Station( GameObject parent, Vector3 at, SpellPalette p, SpellBodyStyle style, float k )
	{
		var go = new GameObject( true, "station" ) { Parent = parent };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = at;
		k = MathF.Max( 0.5f, k );
		switch ( style )
		{
			case SpellBodyStyle.Crystal:
			{
				// frost: a few small crystals at random yaw growing out of the floor, and cold puffs
				for ( var i = 0; i < 3; i++ )
				{
					var r = StyleVfx.AddMesh( go, "crystal", i == 0 ? "fx_crystal_03" : "fx_crystal_04", p.Core.WithAlpha( 0.9f ), 0.5f * k, additive: false );
					if ( r is null ) break;
					r.GameObject.LocalPosition = new Vector3( Game.Random.Float( -14f, 14f ) * k, Game.Random.Float( -14f, 14f ) * k, 0f );
					r.GameObject.LocalRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) ) * Rotation.FromPitch( Game.Random.Float( -15f, 15f ) );
				}
				var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "frost", FireballVfx.TexCircleSmall, additive: false, bloom: false );
				var em = sgo.Components.Create<ParticleSphereEmitter>();
				em.Radius = 14f * k; em.Velocity = 6f; em.Rate = 5f; em.Loop = true;
				fx.MaxParticles = 10; fx.LocalSpace = 0f; fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 12f );
				fx.Lifetime = new ParticleFloat( 0.8f, 1.4f );
				fx.Scale = FireballVfx.OverLife( 6f * k, 12f * k );
				fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, p.Hot ), new Gradient.ColorFrame( 1f, p.Smoke ) );
				fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.5f ), new Curve.Frame( 1f, 0f ) );
				break;
			}
			case SpellBodyStyle.Arc:
			{
				var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "sparks", StyleVfx.TexLightning, additive: true, bloom: true );
				var em = sgo.Components.Create<ParticleSphereEmitter>();
				em.Radius = 16f * k; em.Velocity = 0f; em.Rate = 8f; em.Loop = true;
				fx.MaxParticles = 8; fx.LocalSpace = 0f;
				fx.Lifetime = new ParticleFloat( 0.06f, 0.12f );
				fx.Scale = new ParticleFloat( 12f * k, 22f * k );
				fx.Roll = new ParticleFloat( 0f, 360f );
				fx.Brightness = FireballVfx.Flicker( 1f, 2.5f );
				fx.Gradient = FireballVfx.RandomBetween( p.Hot, p.Core );
				fx.Alpha = FireballVfx.OverLife( 1f, 0f );
				break;
			}
			case SpellBodyStyle.Orb:
			{
				var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "void_whorl", FireballVfx.TexCircle, additive: false, bloom: false );
				var em = sgo.Components.Create<ParticleSphereEmitter>();
				em.Radius = 10f * k; em.Velocity = 10f; em.Rate = 6f; em.Loop = true;
				fx.MaxParticles = 10; fx.LocalSpace = 0f; fx.Damping = 1f; fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 18f );
				fx.Lifetime = new ParticleFloat( 0.6f, 1.1f );
				fx.Scale = FireballVfx.OverLife( 10f * k, 22f * k );
				fx.Roll = new ParticleFloat( 0f, 360f );
				fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, p.Core ), new Gradient.ColorFrame( 1f, p.Smoke ) );
				fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 0.7f ), new Curve.Frame( 1f, 0f ) );
				break;
			}
			default:
			{
				// fire: tongues rising off the floor (the campfire recipe: zero emitter velocity, the rise force moves them) and thin smoke
				var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "flames", FireballVfx.TexCircle, additive: true, bloom: true );
				var em = sgo.Components.Create<ParticleSphereEmitter>();
				em.Radius = 12f * k; em.Velocity = 0f; em.Rate = 16f; em.Loop = true;
				fx.MaxParticles = 20; fx.LocalSpace = 0f; fx.Force = true; fx.ForceDirection = new Vector3( 0f, 0f, 70f );
				fx.Lifetime = new ParticleFloat( 0.35f, 0.6f );
				fx.Scale = FireballVfx.OverLife( 12f * k, 5f * k );
				fx.Roll = new ParticleFloat( 0f, 360f );
				fx.Gradient = FireballVfx.Banded( p.Hot, p.Core, p.Core.Darken( 0.6f ) );
				fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.1f, 1f ), new Curve.Frame( 0.8f, 0f ), new Curve.Frame( 1f, 0f ) );
				var (sgo2, fx2, _) = FireballVfx.AddSpriteLayer( go, "smoke", FireballVfx.TexCircle, additive: false, bloom: false );
				var em2 = sgo2.Components.Create<ParticleSphereEmitter>();
				em2.Radius = 8f * k; em2.Velocity = 8f; em2.Rate = 2f; em2.Loop = true;
				fx2.MaxParticles = 5; fx2.LocalSpace = 0f; fx2.Damping = 2f; fx2.Force = true; fx2.ForceDirection = new Vector3( 0f, 0f, 40f );
				fx2.Lifetime = new ParticleFloat( 0.8f, 1.3f );
				fx2.Scale = FireballVfx.OverLife( 8f * k, 20f * k );
				fx2.Roll = new ParticleFloat( 0f, 360f );
				fx2.Gradient = FireballVfx.SmokeRamp( p );
				fx2.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.35f ), new Curve.Frame( 1f, 0f ) );
				var light = go.Components.Create<PointLight>();
				light.LightColor = p.Core * 0.6f;
				light.Radius = 90f * k;
				light.Shadows = false;
				break;
			}
		}
		return go;
	}

	/// <summary>The void orb's zone: a dark disc under it and rings drifting inward.</summary>
	private static GameObject Pool( GameObject parent, Vector3 at, SpellPalette p, float radius )
	{
		var go = new GameObject( true, "pool" ) { Parent = parent };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = at;
		var disc = go.Components.Create<SpriteRenderer>();
		disc.Sprite = Sprite.FromTexture( Texture.Load( StyleVfx.TexSoftDark ) );
		disc.Size = radius * 2.2f;
		disc.Color = p.Smoke.WithAlpha( 0.55f );
		disc.Additive = false;
		disc.Lighting = false;
		disc.Shadows = false;
		disc.Billboard = SpriteRenderer.BillboardMode.Always;
		disc.DepthFeather = 8f;

		var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "rings", FireballVfx.TexRing, additive: true, bloom: false );
		var em = sgo.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0f; em.Velocity = 0f; em.Rate = 2.5f; em.Loop = true;
		fx.MaxParticles = 6; fx.LocalSpace = 1f;
		fx.Lifetime = new ParticleFloat( 0.9f, 1.2f );
		fx.Scale = FireballVfx.OverLife( radius * 2.4f, radius * 0.3f );
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, p.Core ), new Gradient.ColorFrame( 1f, p.Hot ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.2f, 0.5f ), new Curve.Frame( 1f, 0f ) );
		return go;
	}

	/// <summary>Amber: a pulsing spot on the floor under the parked bolt so the trigger radius reads.</summary>
	private static GameObject MineSpot( GameObject parent, Vector3 at, SpellPalette p, float radius )
	{
		var go = new GameObject( true, "mine_spot" ) { Parent = parent };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var scene = Game.ActiveScene;
		var floor = at;
		if ( scene.IsValid() )
		{
			var tr = scene.Trace.Ray( at, at + Vector3.Down * 200f ).WithoutTags( AimSolver.IgnoreTags ).WithoutTags( "npc", "enemy" ).Run();
			if ( tr.Hit ) floor = tr.HitPosition + Vector3.Up * 2f;
		}
		go.WorldPosition = floor;
		var (sgo, fx, _) = FireballVfx.AddSpriteLayer( go, "pulse", FireballVfx.TexRing, additive: true, bloom: false );
		var em = sgo.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0f; em.Velocity = 0f; em.Rate = 1.2f; em.Loop = true;
		fx.MaxParticles = 3; fx.LocalSpace = 1f;
		fx.Lifetime = 0.8f;
		fx.Scale = FireballVfx.OverLife( radius * 0.4f, radius * 2f );
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, p.Hot ), new Gradient.ColorFrame( 1f, p.Core ) );
		fx.Alpha = FireballVfx.OverLife( 0.7f, 0f );
		return go;
	}
}
