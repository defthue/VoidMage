using System;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Combat;

/// <summary>
/// The empty potion bottle after the drink's flick: a real body thrown with the hand's speed that shatters on the
/// first thing it hits. Built by the Debris recipe (created disabled, Rigidbody BEFORE the collider, a box round the
/// model's bounds because the pack's potion carries a mesh collision and a mesh cannot be a dynamic body, the
/// `debris` tag so it never bumps the player). The Rigidbody has no continuous collision, so each frame a trace
/// from the last position to the current one catches a wall the body would have passed through at throw speed;
/// the contact callback is the backup. The shatter: the clay break sound (a glass sample can replace it), a few
/// small vase shards, a splash of the potion's liquid (BloodVfx.Splash in the potion's colour with its own marks)
/// and a breath of dust. Client-local like debris; parented under the dungeon so a floor change sweeps it.
/// </summary>
public sealed class ThrownBottle : Component, Component.ICollisionListener
{
	/// <summary>The shatter sound (a random take of the vase shatter until a glass sample exists).</summary>
	[ConVar( "potion_shatter_sound" )] public static string ShatterSound { get; set; } = BreakableProp.SoundClay;
	/// <summary>Seconds a bottle may fly before it shatters wherever it is (a bottle wedged somewhere).</summary>
	[ConVar( "potion_bottle_life" )] public static float LifeSeconds { get; set; } = 6f;
	/// <summary>Shards a bottle throws (LooseScale on the vase shards makes them bottle-sized).</summary>
	[ConVar( "potion_shards_min" )] public static int ShardsMin { get; set; } = 4;
	[ConVar( "potion_shards_max" )] public static int ShardsMax { get; set; } = 6;
	[ConVar( "potion_shard_scale" )] public static float ShardScale { get; set; } = 0.3f;
	/// <summary>Spin on the thrown bottle, radians per second.</summary>
	[ConVar( "potion_throw_spin" )] public static float Spin { get; set; } = 9f;

	public static readonly Color HealBright = new( 0.85f, 0.12f, 0.16f );
	public static readonly Color HealDark = new( 0.45f, 0.04f, 0.08f );
	public static readonly Color ManaBright = new( 0.25f, 0.45f, 1f );
	public static readonly Color ManaDark = new( 0.1f, 0.15f, 0.55f );
	private static readonly string[] HealMarks = { "shaders/potion_splat_heal_01.vmat", "shaders/potion_splat_heal_02.vmat", "shaders/potion_splat_heal_03.vmat", "shaders/potion_splat_heal_04.vmat" };
	private static readonly string[] ManaMarks = { "shaders/potion_splat_mana_01.vmat", "shaders/potion_splat_mana_02.vmat", "shaders/potion_splat_mana_03.vmat", "shaders/potion_splat_mana_04.vmat" };
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", Run.PotionPickup.Tag };

	private Rigidbody _body;
	private Collider _collider;
	private ModelRenderer _renderer;
	private PlayerPotions.Kind _kind;
	private Vector3 _last;
	private float _bornAt;
	private bool _done;
	private GameObject _thrower;

	/// <summary>Let the bottle go from `at` (the hand's bottle transform) with `velocity` (u/s).</summary>
	public static void Throw( Scene scene, Model model, Transform at, Vector3 velocity, PlayerPotions.Kind kind, GameObject parent, GameObject thrower )
	{
		if ( scene is null || model is null || model.IsError ) return;
		var go = new GameObject( false, "thrown_bottle" ) { WorldTransform = at };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( parent.IsValid() ) go.SetParent( parent, true );
		go.Tags.Add( Debris.Tag );

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		var body = go.Components.Create<Rigidbody>();
		body.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
		body.EnableImpactDamage = false;
		body.GravityScale = 1f;
		body.AngularDamping = 0.2f;
		body.CollisionEventsEnabled = true;
		body.StartAsleep = false;

		var box = go.Components.Create<BoxCollider>();
		box.Center = model.Bounds.Center;
		box.Scale = model.Bounds.Size;
		box.Static = false;
		box.Elasticity = 0.05f;
		box.Friction = 0.6f;

		var bottle = go.Components.Create<ThrownBottle>();
		bottle._body = body;
		bottle._collider = box;
		bottle._renderer = renderer;
		bottle._kind = kind;
		bottle._thrower = thrower;
		bottle._last = at.Position;
		bottle._bornAt = Time.Now;

		go.Enabled = true;

		body.MassOverride = 1.2f;
		body.Velocity = velocity;
		var axis = Vector3.Cross( velocity.IsNearZeroLength ? Vector3.Forward : velocity.Normal, Vector3.Up );
		if ( axis.IsNearZeroLength ) axis = Vector3.Random.Normal;
		body.AngularVelocity = axis.Normal * Spin * Game.Random.Float( 0.7f, 1.3f );
	}

	protected override void OnUpdate()
	{
		if ( _done ) return;
		var now = WorldPosition;
		if ( Time.Now - _bornAt > LifeSeconds ) { Shatter( now, Vector3.Up, Vector3.Down ); return; }
		if ( now != _last )
		{
			var tr = Scene.Trace.Ray( _last, now ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( _thrower ).Run();
			if ( tr.Hit ) { Shatter( tr.HitPosition, tr.Normal, (now - _last).Normal ); return; }
		}
		_last = now;
	}

	public void OnCollisionStart( Collision collision )
	{
		if ( _done ) return;
		var contact = collision.Contact;
		var travel = _body.IsValid() && !_body.Velocity.IsNearZeroLength ? _body.Velocity.Normal : Vector3.Down;
		Shatter( contact.Point, contact.Normal, travel );
	}

	public void OnCollisionUpdate( Collision collision ) { }
	public void OnCollisionStop( CollisionStop collision ) { }

	private void Shatter( Vector3 at, Vector3 normal, Vector3 travel )
	{
		if ( _done ) return;
		_done = true;
		if ( normal.IsNearZeroLength ) normal = Vector3.Up;
		normal = normal.Normal;

		if ( !string.IsNullOrEmpty( ShatterSound ) ) VoidMage.Audio.GameAudio.Play( ShatterSound, at );

		// glass on stone: the shards skid off the surface the way the bottle was going, a little out along the normal
		var reflected = Vector3.Reflect( travel, normal );
		var push = (reflected * 0.5f + normal * 0.5f).WithZ( 0f );
		if ( push.IsNearZeroLength ) push = Vector3.Random.WithZ( 0f );
		push = push.Normal;
		var centre = at + normal * 4f;
		BreakVfx.Puff( Scene, centre, 6f );
		var parent = GameObject.Parent.IsValid() ? GameObject.Parent : GameObject;
		var kit = BreakableKits.GlassBottle( ShardsMin, ShardsMax, ShardScale );
		if ( kit is not null )
		{
			var t = new Transform( centre, Rotation.Identity, 1f );
			Debris.Burst( Scene, t, BBox.FromPositionAndSize( centre, 8f ), kit, push, false, null, parent );
		}

		var heal = _kind == PlayerPotions.Kind.Heal;
		BloodVfx.Splash( Scene, at, normal, heal ? HealBright : ManaBright, heal ? HealDark : ManaDark, heal ? HealMarks : ManaMarks, 0.9f );

		if ( _body.IsValid() ) { _body.MotionEnabled = false; _body.CollisionEventsEnabled = false; }
		if ( _collider.IsValid() ) _collider.Enabled = false;
		if ( _renderer.IsValid() ) _renderer.Enabled = false;
		GameObject.Destroy();
	}
}
