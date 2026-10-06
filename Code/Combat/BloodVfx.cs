using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Dungeon;
using VoidMage.Spells;

namespace VoidMage.Combat;

/// <summary>
/// Blood on top of the normal hit (the sparks stay: user, 2026-09-12, "always normal sparks with each hit, but
/// added blood, and gorey"). One spray is three particle effects and a handful of surface marks:
///   drops    fat dark-red droplets thrown along the blade, pulled down hard, that COLLIDE with the world and
///            die on contact leaving a small splatter where they landed (floor or wall);
///   strings  a few faster drops trailing a ribbon: the ropes of blood;
///   gibs     the Particle FX pack's gore chunks (vfx/models/sm_gorechunk_0N, toon-lit meat), tumbling out,
///            landing and lying there for a while, one or two on a light, more on a heavy, a handful on a kill;
///   marks    the engine's own blood splatter silhouettes stamped as surface quads (heden_ground_splat.shader
///            through blood_splat_0N.vmat, the scorch mark's plumbing): a big one on the floor under the hit,
///            one on the wall the swing points at, small ones where drops and chunks land. They stay for the
///            run under a cap (the oldest goes first, like debris), and die with the dungeon.
/// No lights, no bloom, alpha-blended (hit-vfx taste). Engine decals are NOT used: heden_toon.shader never runs
/// the engine's decal pass, so they would be invisible on the dungeon.
/// </summary>
public static class BloodVfx
{
	public const string TexDrop = "vfx/blood_drop.png";
	private static readonly string[] SplatMaterials = { "shaders/blood_splat_01.vmat", "shaders/blood_splat_02.vmat", "shaders/blood_splat_03.vmat", "shaders/blood_splat_04.vmat" };
	private static readonly string[] GibModelPaths = { "vfx/models/sm_gorechunk_01.vmdl", "vfx/models/sm_gorechunk_02.vmdl", "vfx/models/sm_gorechunk_03.vmdl", "vfx/models/sm_gorechunk_04.vmdl" };
	/// <summary>What the drops and chunks fly through: bodies, the player, corpses, other effects. They land on the dungeon and its props.</summary>
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", "ragdoll", Debris.Tag };

	public static readonly Color BloodDark = new( 0.55f, 0.05f, 0.05f );
	public static readonly Color BloodBlack = new( 0.2f, 0.02f, 0.02f );

	/// <summary>Drops per light hit (a heavy throws 1.4x, a death 2x).</summary>
	[ConVar( "blood_drops" )] public static float Drops { get; set; } = 12f;
	/// <summary>Multiplier on the gore chunk counts (0 turns the chunks off).</summary>
	[ConVar( "blood_gibs_scale" )] public static float GibsScale { get; set; } = 1f;
	/// <summary>Blood marks kept in the world; past this the oldest is removed.</summary>
	[ConVar( "blood_marks_max" )] public static int MarksMax { get; set; } = 150;
	/// <summary>Multiplier on every mark's size.</summary>
	[ConVar( "blood_mark_scale" )] public static float MarkScale { get; set; } = 1f;
	/// <summary>Seconds a gore chunk lies there before it shrinks away.</summary>
	[ConVar( "blood_gib_seconds" )] public static float GibSeconds { get; set; } = 8f;

	private static Model[] _gibs;

	/// <summary>The blade opened flesh: the spray for one hit, along the blade's travel.</summary>
	public static void Spray( Scene scene, Vector3 position, Vector3 swing, bool heavy )
	{
		var k = heavy ? 1.3f : 1f;
		Build( scene, position, swing, k,
			drops: (int)MathF.Round( Drops * (heavy ? 1.4f : 1f) ),
			strings: heavy ? 4 : 3,
			gibs: RollGibs( heavy ? 2 : 1, heavy ? 3 : 2 ),
			splat: Game.Random.Float( 14f, 20f ) * k,
			wall: Game.Random.Float( 10f, 16f ) * k );
	}

	/// <summary>The killing blow (every peer, at the corpse's spawn): a bigger spray, a handful of chunks, a big splat.</summary>
	public static void Burst( Scene scene, Vector3 position, Vector3 push )
	{
		var swing = push.IsNearZeroLength ? Vector3.Random.WithZ( 0f ).Normal : push.Normal;
		Build( scene, position, swing, 1.4f,
			drops: (int)MathF.Round( Drops * 2f ),
			strings: 5,
			gibs: RollGibs( 5, 7 ),
			splat: Game.Random.Float( 26f, 34f ),
			wall: Game.Random.Float( 14f, 20f ) );
	}

	private static int RollGibs( int min, int max )
	{
		if ( GibsScale <= 0f ) return 0;
		return (int)MathF.Round( Game.Random.Int( min, max ) * GibsScale );
	}

	private static void Build( Scene scene, Vector3 position, Vector3 swing, float k, int drops, int strings, int gibs, float splat, float wall )
	{
		if ( scene is null ) return;
		swing = swing.IsNearZeroLength ? Vector3.Forward : swing.Normal;

		var root = new GameObject( true, "blood" ) { WorldPosition = position };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = GibSeconds + 1.5f;
		var spray = root.Components.Create<BloodSpray>();

		BuildDrops( root, spray, "drops", swing, k, drops, strings: false, BloodDark, BloodBlack );
		if ( strings > 0 ) BuildDrops( root, spray, "strings", swing, k, strings, strings: true, BloodDark, BloodBlack );
		if ( gibs > 0 ) spray.Gibs = BuildGibs( root, swing, k, gibs );

		// the main splat: the floor under the contact (a hit high on a hanging corpse still marks the floor)
		var down = scene.Trace.Ray( position + Vector3.Up * 8f, position + Vector3.Down * 120f ).WithoutTags( PassThrough ).Run();
		if ( down.Hit && down.Normal.z >= 0.6f ) Mark( scene, down.HitPosition, down.Normal, splat );
		// the wall the swing points at, if one is close: the spray behind the body
		var ahead = scene.Trace.Ray( position, position + swing * 70f ).WithoutTags( PassThrough ).Run();
		if ( ahead.Hit ) Mark( scene, ahead.HitPosition, ahead.Normal, wall );
	}

	/// <summary>
	/// A liquid other than blood bursting on a surface (a thrown potion shattering): the same drops in `bright`
	/// fading to `dark`, thrown out along the surface normal and up, a mark of `marks` (vmats on the splat shader)
	/// where it hit and on the floor under it, and the drops' own small marks where they land. No chunks.
	/// `position` is the point ON the surface (the drops start a little out from it).
	/// </summary>
	public static void Splash( Scene scene, Vector3 position, Vector3 normal, Color bright, Color dark, string[] marks, float k = 1f )
	{
		if ( scene is null ) return;
		normal = normal.IsNearZeroLength ? Vector3.Up : normal.Normal;

		var root = new GameObject( true, "splash" ) { WorldPosition = position + normal * 4f };
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Tags.Add( "spellfire" );
		root.Components.Create<VfxLifetime>().Seconds = 3f;
		var spray = root.Components.Create<BloodSpray>();
		spray.MarkMaterials = marks;

		var dir = (normal * 0.7f + Vector3.Up * 0.45f).Normal;
		BuildDrops( root, spray, "drops", dir, k, (int)MathF.Round( Drops * 0.9f ), strings: false, bright, dark );
		BuildDrops( root, spray, "strings", dir, k, 2, strings: true, bright, dark );

		Mark( scene, position, normal, Game.Random.Float( 9f, 13f ) * k, marks );
		if ( normal.z < 0.6f )
		{
			var down = scene.Trace.Ray( position + normal * 6f, position + normal * 6f + Vector3.Down * 120f ).WithoutTags( PassThrough ).Run();
			if ( down.Hit && down.Normal.z >= 0.6f ) Mark( scene, down.HitPosition, down.Normal, Game.Random.Float( 7f, 10f ) * k, marks );
		}
	}

	/// <summary>
	/// Droplets: the engine's blood drop sprite faced along its travel, motion-blurred into a streak, thrown with the
	/// blade and a random spread, gravity well above the sparks' so they arc down within a body length. They collide,
	/// stick (bounce 0) and die on contact; the landing spot is queued for a small mark. The strings are the same
	/// with fewer, faster drops trailing a ribbon.
	/// </summary>
	private static void BuildDrops( GameObject root, BloodSpray spray, string name, Vector3 swing, float k, int count, bool strings, Color bright, Color dark )
	{
		if ( count <= 0 ) return;
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( root, name, TexDrop, additive: false, bloom: false );
		renderer.FaceVelocity = true;
		renderer.MotionBlur = true;
		renderer.LeadingTrail = true;
		renderer.BlurAmount = 0.6f;
		renderer.DepthFeather = 1f;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 3f; em.Velocity = 0f; em.Rate = 0f; em.Burst = count; em.Loop = false;
		fx.MaxParticles = count + 2;
		fx.InitialVelocity = strings ? swing * 230f + Vector3.Up * 70f : swing * 170f + Vector3.Up * 40f;
		fx.StartVelocity = strings ? new ParticleFloat( 30f, 80f ) : new ParticleFloat( 40f, 120f );
		fx.Force = true; fx.ForceDirection = Vector3.Down * 900f;
		fx.Damping = 0.5f;
		fx.Lifetime = strings ? new ParticleFloat( 0.45f, 0.7f ) : new ParticleFloat( 0.6f, 1f );
		fx.Scale = strings ? FireballVfx.OverLife( 2f * k, 1.6f * k ) : FireballVfx.OverLife( 3.2f * k, 2.4f * k );
		fx.Stretch = 1.5f;
		fx.Gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, bright ), new Gradient.ColorFrame( 0.6f, bright ), new Gradient.ColorFrame( 1f, dark ) );
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.85f, 1f ), new Curve.Frame( 1f, 0f ) );
		fx.Collision = true;
		fx.CollisionRadius = 1f;
		fx.Bounce = 0f;
		fx.Friction = 1f;
		fx.DieOnCollisionChance = 1f;
		fx.CollisionIgnore = new TagSet( PassThrough );
		fx.OnParticleDestroyed = spray.OnDropDied;

		if ( strings )
		{
			var trail = go.Components.Create<ParticleTrailRenderer>();
			trail.MaxPoints = 10;
			trail.PointDistance = 2f;
			trail.LifeTime = 0.2f;
			trail.Width = new Curve( new Curve.Frame( 0f, 2.2f * k ), new Curve.Frame( 1f, 0.4f * k ) );
			trail.Color = new Gradient( new Gradient.ColorFrame( 0f, bright ), new Gradient.ColorFrame( 1f, dark ) );
			trail.TintFromParticle = true;
			trail.ScaleFromParticle = false;
			trail.Opaque = false;
			trail.BlendMode = BlendMode.Normal;
		}
	}

	/// <summary>
	/// The chunks: model particles picking one of the four gore meshes, tumbling out with the blade, landing (bounce a
	/// touch, then lie) and shrinking away at the end of their life. The spin is a curve over life that reaches zero
	/// early, so a chunk stops rolling about a second after it lands instead of spinning on the floor.
	/// </summary>
	private static ParticleEffect BuildGibs( GameObject root, Vector3 swing, float k, int count )
	{
		var models = Gibs();
		if ( models.Length == 0 ) return null;
		var go = new GameObject( true, "gibs" ) { Parent = root };
		var fx = go.Components.Create<ParticleEffect>();
		fx.ApplyColor = true; fx.ApplyAlpha = true; fx.ApplyRotation = true; fx.ApplyShape = true;
		fx.MaxParticles = count + 2;
		fx.Tint = Color.White;
		fx.Gradient = Color.White;
		fx.InitialVelocity = swing * 150f + Vector3.Up * 90f;
		fx.StartVelocity = new ParticleFloat( 40f, 110f );
		fx.Force = true; fx.ForceDirection = Vector3.Down * 800f;
		fx.Damping = 0.3f;
		fx.Lifetime = GibSeconds;
		fx.InitialPitch = new ParticleFloat( 0f, 360f );
		fx.InitialYaw = new ParticleFloat( 0f, 360f );
		fx.InitialRoll = new ParticleFloat( 0f, 360f );
		fx.Pitch = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 300f ), new Curve.Frame( 0.12f, 0f ), new Curve.Frame( 1f, 0f ) );
		fx.Yaw = FireballVfx.CurveOverLife( new Curve.Frame( 0f, -250f ), new Curve.Frame( 0.12f, 0f ), new Curve.Frame( 1f, 0f ) );
		fx.Roll = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 200f ), new Curve.Frame( 0.12f, 0f ), new Curve.Frame( 1f, 0f ) );
		fx.Scale = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.85f, 1f ), new Curve.Frame( 1f, 0f ) );
		fx.Collision = true;
		fx.CollisionRadius = 2f;
		fx.Bounce = 0.2f;
		fx.Friction = 1f;
		fx.DieOnCollisionChance = 0f;
		fx.CollisionIgnore = new TagSet( PassThrough );
		var renderer = go.Components.Create<ParticleModelRenderer>();
		renderer.Choices = models.Select( m => (ParticleModelRenderer.ModelEntry)m ).ToList();
		renderer.Scale = new ParticleFloat( 1f * k, 1.5f * k );
		renderer.CastShadows = false;
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 4f; em.Velocity = 0f; em.Rate = 0f; em.Burst = count; em.Loop = false;
		return fx;
	}

	private static Model[] Gibs()
	{
		if ( _gibs is not null ) return _gibs;
		var list = new List<Model>();
		foreach ( var path in GibModelPaths )
		{
			var m = Model.Load( path );
			if ( m is not null && !m.IsError ) list.Add( m );
		}
		if ( list.Count == 0 ) Log.Warning( "[Blood] no gore chunk models (vfx/models/sm_gorechunk_0N.vmdl: run tools/gen_fx_assets.py, compiled in the editor?)" );
		_gibs = list.ToArray();
		return _gibs;
	}

	/// <summary>A blood splatter stamped on a surface, registered against the cap. `materials` picks another liquid's splat vmats (a potion).</summary>
	/// <remarks>
	/// The mark is snapped onto the real surface first (a short trace along the normal: a particle's hit point sits a
	/// collision radius off it, and a spot with no surface near it gets no mark at all), then drawn projected onto what
	/// is behind it, so where it overhangs an edge or its surface breaks away it simply is not there.
	/// </remarks>
	public static void Mark( Scene scene, Vector3 position, Vector3 normal, float radius, string[] materials = null )
	{
		if ( scene is null || MarkScale <= 0f ) return;
		normal = normal.IsNearZeroLength ? Vector3.Up : normal.Normal;
		var snap = scene.Trace.Ray( position + normal * 6f, position - normal * 8f ).WithoutTags( PassThrough ).Run();
		// a contact normal can come in pointing into the surface (a physics contact's sign): try the other side
		if ( !snap.Hit || snap.StartedSolid )
			snap = scene.Trace.Ray( position - normal * 6f, position + normal * 8f ).WithoutTags( PassThrough ).Run();
		if ( !snap.Hit || snap.StartedSolid ) return;
		position = snap.HitPosition;
		if ( !snap.Normal.IsNearZeroLength ) normal = snap.Normal;

		var set = materials is { Length: > 0 } ? materials : SplatMaterials;
		var material = set[Game.Random.Int( 0, set.Length - 1 )];
		var parent = scene.GetAllComponents<DungeonBuilder>().FirstOrDefault()?.GameObject;
		var go = GroundScorch.Place( position, normal, radius * MarkScale, parent, permanent: true, tint: null, material: material, project: true );
		go.Name = "blood_mark";
		BloodMarks.Add( go );
	}
}

/// <summary>The blood marks of the run: the oldest is removed past the cap; a dungeon rebuild sweeps them all.</summary>
public static class BloodMarks
{
	private static readonly List<GameObject> _marks = new();

	public static int Count => _marks.Count;

	public static void Add( GameObject mark )
	{
		if ( !mark.IsValid() ) return;
		_marks.Add( mark );
		while ( _marks.Count > Math.Max( 1, BloodVfx.MarksMax ) )
		{
			var oldest = _marks[0];
			_marks.RemoveAt( 0 );
			if ( oldest.IsValid() ) oldest.Destroy();
		}
		if ( _marks.Count % 32 == 0 ) _marks.RemoveAll( m => !m.IsValid() );
	}

	public static void Clear()
	{
		foreach ( var m in _marks ) if ( m.IsValid() ) m.Destroy();
		_marks.Clear();
	}
}

/// <summary>
/// One spray's bookkeeping: the drops' landing spots come in from the particle thread through OnDropDied and are
/// turned into marks on the main thread here; the chunks are polled for their first contact.
/// </summary>
public sealed class BloodSpray : Component
{
	/// <summary>Small marks one spray may leave from drops and chunks landing (the main splat and the wall are extra).</summary>
	public const int LandingMarks = 8;

	public ParticleEffect Gibs { get; set; }
	/// <summary>The splat vmats the landing marks use; null = blood.</summary>
	public string[] MarkMaterials { get; set; }

	private readonly object _lock = new();
	private readonly Queue<(Vector3 pos, Vector3 normal)> _landed = new();
	private int _marks;

	/// <summary>Particle callback (thread unknown): only remember where it hit; the mark is placed in OnUpdate.</summary>
	public void OnDropDied( Particle p )
	{
		if ( p is null || p.HitTime <= 0f ) return;
		lock ( _lock ) _landed.Enqueue( (p.HitPos, p.HitNormal) );
	}

	protected override void OnUpdate()
	{
		while ( _marks < LandingMarks )
		{
			(Vector3 pos, Vector3 normal) hit;
			lock ( _lock )
			{
				if ( _landed.Count == 0 ) break;
				hit = _landed.Dequeue();
			}
			BloodVfx.Mark( Scene, hit.pos, hit.normal, Game.Random.Float( 3f, 7f ), MarkMaterials );
			_marks++;
		}
		if ( Gibs.IsValid() && _marks < LandingMarks )
		{
			foreach ( var p in Gibs.Particles )
			{
				if ( p.HitTime <= 0f || p.Get<bool>( "marked" ) ) continue;
				p.Set( "marked", true );
				BloodVfx.Mark( Scene, p.HitPos, p.HitNormal, Game.Random.Float( 4f, 6f ), MarkMaterials );
				if ( ++_marks >= LandingMarks ) break;
			}
		}
	}
}
