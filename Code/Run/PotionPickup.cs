using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Run;

/// <summary>
/// A potion standing in the dungeon (DecorPotions placed it, DungeonBuilder.BuildProps put this on it): picked up
/// with E by the player looking at it from close by. No collider (the pack's potions never had one), so the aim is
/// a cone test from the eye plus a line-of-sight trace, not a hit on the bottle. The one in focus is what the HUD
/// prompt names (PickupPrompt). Solo for now, like the shards: nothing here is networked; a floor change sweeps
/// them with the dungeon.
/// </summary>
[Title( "Potion Pickup" ), Category( "Void Mage" ), Icon( "science" )]
public sealed class PotionPickup : Component
{
	public const string Tag = "potion";

	/// <summary>How close the player has to be (eye to bottle, units).</summary>
	[ConVar( "potion_use_range" )] public static float UseRange { get; set; } = 140f;
	/// <summary>How closely the player has to look at it (degrees off the view centre).</summary>
	[ConVar( "potion_use_cone" )] public static float UseCone { get; set; } = 18f;

	[Property] public PlayerPotions.Kind Kind { get; set; }

	public static readonly List<PotionPickup> All = new();
	/// <summary>The one the local player could pick up right now.</summary>
	public static PotionPickup Focused { get; private set; }

	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", Tag };

	/// <summary>The bottle's visible middle.</summary>
	public Vector3 Centre
	{
		get
		{
			var r = Components.Get<ModelRenderer>();
			return r.IsValid() ? r.Bounds.Center : WorldPosition + Vector3.Up * 7f;
		}
	}

	public string Label => Kind == PlayerPotions.Kind.Heal ? "Healing potion" : "Mana potion";

	/// <summary>The potions shut inside a chest on this floor: not drawn, not pickable, until the chest breaks (<see cref="OpenInside"/>).</summary>
	private static readonly List<PotionPickup> _shut = new();

	/// <summary>Shut inside the prop it was placed in (DecorPotions put it in a chest).</summary>
	public bool Shut { get; private set; }

	/// <summary>Gravity on a potion falling to the floor (u/s²).</summary>
	private const float Gravity = 800f;
	private bool _falling;
	private Vector3 _velocity;
	private float _fallStarted;

	/// <summary>Out of a chest: flies to the player by itself while there is room for its kind (user, 2026-09-20).</summary>
	public bool AutoCollect { get; private set; }
	/// <summary>Seconds of hop before it may turn for the player, and how long it counts as fresh (no range limit).</summary>
	private const float AutoDelay = 0.45f;
	private const float AutoFreshFor = 2f;
	private float _autoFrom;
	private bool _flying;
	private bool _reserved;
	private float _flySpeed;

	/// <summary>
	/// Same rules as a chest's scroll (ScrollPickup.FlyStep): once the hop has peaked it comes to the player while they
	/// can carry one more of its kind; from anywhere while fresh out of the wreck, later only from the shards' magnet
	/// range (one was drunk). Full, it stands there for E as before.
	/// </summary>
	private bool FlyStep( float dt )
	{
		var player = PlayerHealth.Local;
		var potions = PlayerPotions.Local;
		float age = Time.Now - _autoFrom;
		if ( !AutoCollect || _reserved || age < AutoDelay || !player.IsValid() || player.IsDead || !potions.IsValid() || potions.IsFull( Kind ) )
		{
			if ( _flying )
			{
				// the slot filled up on the way: it drops where it is
				_flying = false;
				_restKnown = false;
				_lift = 0f;
				Fall( Vector3.Zero );
				if ( Active && !All.Contains( this ) ) All.Add( this );
			}
			return false;
		}
		var target = player.FeetPosition + Vector3.Up * 40f;
		var pos = WorldPosition;
		if ( !_flying )
		{
			var magnet = ShardPickup.MagnetRange * VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.PickupRange );
			if ( age > AutoFreshFor && pos.Distance( target ) > magnet ) return false;
			_flying = true;
			_flySpeed = 0f;
			if ( Focused == this ) SetFocused( null );
			All.Remove( this );
		}
		_flySpeed = MathF.Min( 1400f, _flySpeed + dt * 2600f );
		pos += (target - pos).Normal * MathF.Min( _flySpeed * dt, pos.Distance( target ) );
		WorldPosition = pos;
		WorldRotation *= Rotation.FromYaw( dt * 720f );
		if ( pos.Distance( target ) < ShardPickup.CollectRange && potions.TryAdd( Kind ) )
		{
			if ( !string.IsNullOrEmpty( PotionDrinker.PickupSound ) ) VoidMage.Audio.GameAudio.Play( PotionDrinker.PickupSound, pos );
			Taken();
		}
		return true;
	}

	/// <summary>How high a potion floats over the surface it stands on, like a shard (units).</summary>
	[ConVar( "potion_hover_height" )] public static float HoverHeight { get; set; } = 10f;
	/// <summary>How far it bobs up and down (units).</summary>
	[ConVar( "potion_hover_bob" )] public static float HoverBob { get; set; } = 2f;
	/// <summary>How fast it turns (degrees per second).</summary>
	[ConVar( "potion_hover_spin" )] public static float HoverSpin { get; set; } = 60f;
	/// <summary>How fast it rises to its float height when it lands (units per second).</summary>
	private const float RiseSpeed = 30f;

	/// <summary>Where the bottle stands on its surface (the pivot): the float and the bob are drawn on top of this.</summary>
	private Vector3 _rest;
	private bool _restKnown;
	private float _lift;
	private float _phase;
	private ModelRenderer _renderer;
	private bool _lit;

	private ModelRenderer Renderer => _renderer.IsValid() ? _renderer : (_renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelf ));

	protected override void OnEnabled()
	{
		if ( !Shut && !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDisabled()
	{
		All.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		_shut.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	/// <summary>Spawned inside a chest: hidden and out of the pickup list until the chest is broken open.</summary>
	public void ShutInside()
	{
		Shut = true;
		All.Remove( this );
		if ( Focused == this ) Focused = null;
		if ( !_shut.Contains( this ) ) _shut.Add( this );
		foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.Enabled = false;
	}

	/// <summary>
	/// A prop broke (BreakableProp.BreakLocal): every potion shut inside its box comes out, hopping up out of the
	/// wreck with a little of the blow's push and landing on the floor.
	/// </summary>
	public static void OpenInside( BBox box, Vector3 push )
	{
		if ( _shut.Count == 0 ) return;
		var grown = new BBox( box.Mins - 4f, box.Maxs + 4f );
		for ( int i = _shut.Count - 1; i >= 0; i-- )
		{
			var p = _shut[i];
			if ( !p.IsValid() ) { _shut.RemoveAt( i ); continue; }
			if ( !grown.Contains( p.WorldPosition ) ) continue;
			_shut.RemoveAt( i );
			p.Shut = false;
			foreach ( var r in p.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.Enabled = true;
			if ( p.Active && !All.Contains( p ) ) All.Add( p );
			var side = push.WithZ( 0f );
			side = side.IsNearZeroLength ? Vector3.Random.WithZ( 0f ).Normal : side.Normal;
			p.Fall( side * Game.Random.Float( 20f, 50f ) + Vector3.Up * Game.Random.Float( 150f, 190f ) );
			p.AutoCollect = true;
			p._autoFrom = Time.Now;
		}
	}

	/// <summary>The table under it broke: it drops to the floor instead of being flung away with the other things on top.</summary>
	public void Fall( Vector3 velocity )
	{
		EnsureRest();
		_falling = true;
		_velocity = velocity;
		_fallStarted = Time.Now;
	}

	public const string HealModel = "dungeonpack/sm_item_potion_07.vmdl";
	public const string ManaModel = "dungeonpack/sm_item_potion_06.vmdl";

	/// <summary>Host: a potion that came out of a broken chest (ChestLoot): hops out with the blow, lands, floats.</summary>
	public static PotionPickup SpawnLoose( Scene scene, Vector3 at, PlayerPotions.Kind kind, GameObject parent, Vector3 push )
	{
		if ( !scene.IsValid() ) return null;
		var go = new GameObject( true, "potion pickup" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		if ( parent.IsValid() ) go.SetParent( parent, false );
		go.WorldPosition = at;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );
		go.Tags.Add( Tag );
		go.Components.Create<ModelRenderer>().Model = Model.Load( kind == PlayerPotions.Kind.Heal ? HealModel : ManaModel );
		var pickup = go.Components.Create<PotionPickup>();
		pickup.Kind = kind;
		var side = push.WithZ( 0f );
		side = side.IsNearZeroLength ? Vector3.Random.WithZ( 0f ).Normal : side.Normal;
		pickup.Fall( side * Game.Random.Float( 30f, 70f ) + Vector3.Up * Game.Random.Float( 170f, 210f ) );
		pickup.AutoCollect = true;
		pickup._autoFrom = Time.Now;
		return pickup;
	}

	/// <summary>How bright a potion's glow is (0 turns it off): red for healing, blue for mana, like the shards' purple.</summary>
	[ConVar( "potion_glow" )] public static float GlowStrength { get; set; } = 0.4f;
	/// <summary>How far the glow reaches (inches). Halved from 44 on the user's word: the pool of light was too wide.</summary>
	[ConVar( "potion_glow_radius" )] public static float GlowRadius { get; set; } = 22f;
	private PointLight _glow;

	/// <summary>The coloured breath of light under a potion that is out in the open (made on first show, never for a shut one).</summary>
	private void UpdateGlow()
	{
		if ( GlowStrength <= 0f )
		{
			if ( _glow.IsValid() ) _glow.Enabled = false;
			return;
		}
		if ( !_glow.IsValid() )
		{
			_glow = Components.Create<PointLight>();
			_glow.Shadows = false;
		}
		_glow.Enabled = true;
		var colour = Kind == PlayerPotions.Kind.Heal ? new Color( 1f, 0.18f, 0.14f ) : new Color( 0.22f, 0.42f, 1f );
		float pulse = 0.85f + 0.15f * MathF.Sin( Time.Now * 2.6f + _phase );
		_glow.LightColor = colour * GlowStrength * pulse;
		_glow.Radius = GlowRadius;
	}

	/// <summary>Forget the previous floor's shut potions (the dungeon rebuild destroys them anyway).</summary>
	public static void ClearShut() => _shut.Clear();

	private void EnsureRest()
	{
		if ( _restKnown ) return;
		_rest = WorldPosition;
		_restKnown = true;
		_phase = Game.Random.Float( 0f, MathF.PI * 2f );
	}

	/// <summary>
	/// Floats and turns like a shard, so a bottle on a table or one just out of a chest catches the eye; falls to the
	/// floor first when its table broke or its chest was opened. The resting spot is kept apart from the drawn one.
	/// </summary>
	protected override void OnUpdate()
	{
		if ( Shut ) return;
		EnsureRest();
		// the pickup's own lighting: the bottle never goes black in an unlit corner (kept asking until the model is up)
		if ( !_lit ) _lit = VoidMage.Rendering.PickupLit.Apply( Renderer );
		UpdateGlow();
		var dt = MathF.Min( Time.Delta, 0.05f );
		if ( FlyStep( dt ) ) return;

		if ( _falling ) FallStep( dt );

		// rises to its float height once it is standing somewhere, sinks back to its spot while it falls
		var hover = MathF.Max( 0f, HoverHeight );
		_lift = MathX.Approach( _lift, _falling ? 0f : hover, RiseSpeed * dt );
		var settled = hover > 0f ? _lift / hover : 0f;
		var bob = MathF.Sin( Time.Now * 2.4f + _phase ) * HoverBob * settled;
		WorldPosition = _rest + Vector3.Up * (_lift + bob);
		if ( HoverSpin != 0f ) WorldRotation *= Rotation.FromYaw( HoverSpin * dt );
	}

	private void FallStep( float dt )
	{
		_velocity += Vector3.Down * Gravity * dt;
		// the bottom of the bottle is what lands (potion 06's pivot is in its middle, 07's at its base)
		var r = Renderer;
		var drawn = WorldPosition;
		var bottomOffset = r.IsValid() ? drawn.z - r.Bounds.Mins.z : 0f;
		var from = _rest.WithZ( _rest.z - bottomOffset );
		var to = from + _velocity * dt;
		var tr = Scene.Trace.Ray( from + Vector3.Up * 2f, to ).WithoutTags( PassThrough ).Run();
		if ( tr.Hit && tr.Normal.z > 0.5f && _velocity.z < 0f )
		{
			_rest = new Vector3( tr.HitPosition.x, tr.HitPosition.y, tr.HitPosition.z + bottomOffset );
			_falling = false;
			return;
		}
		if ( tr.Hit && !tr.StartedSolid )
		{
			// a wall (or the ceiling) in the way of the hop: it stops against it and drops straight down, never through it
			_velocity -= tr.Normal * MathF.Min( 0f, Vector3.Dot( _velocity, tr.Normal ) );
			_rest += Vector3.Up * (_velocity.z * dt);
			if ( Time.Now - _fallStarted > 3f ) _falling = false;
			return;
		}
		_rest += _velocity * dt;
		// never falls forever (through a gap in the floor): it stops where it is
		if ( Time.Now - _fallStarted > 3f ) _falling = false;
	}

	/// <summary>Owner, every frame: the nearest potion inside the range and the cone with a clear line from the eye, or none.</summary>
	public static PotionPickup UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		PotionPickup best = null;
		float bestScore = float.MaxValue;
		float cosCone = MathF.Cos( UseCone.Clamp( 1f, 89f ) * MathF.PI / 180f );
		float range = MathF.Max( 10f, UseRange );
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			var p = All[i];
			if ( !p.IsValid() ) { All.RemoveAt( i ); continue; }
			var to = p.Centre - eye;
			var dist = to.Length;
			if ( dist > range || dist < 1f ) continue;
			var dot = Vector3.Dot( to / dist, forward );
			if ( dot < cosCone ) continue;
			// nearest to the view centre first, then nearest in distance
			var score = (1f - dot) * 400f + dist;
			if ( score >= bestScore ) continue;
			var tr = scene.Trace.Ray( eye, p.Centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
			if ( tr.Hit && tr.Distance < dist - 6f ) continue;
			best = p;
			bestScore = score;
		}
		SetFocused( best );
		// re-applied every frame while in focus: an override set on a scene object that is not built yet is lost
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	/// <summary>The white, bold outline the potion in focus wears in place of the model's own ink.</summary>
	public const string FocusOutlineMaterial = "shaders/potion_outline_focus.vmat";
	private static Material _focusOutline;

	/// <summary>The potion in focus draws its outline white (its outline material swapped); the one before it goes back to ink.</summary>
	private static void SetFocused( PotionPickup p )
	{
		if ( Focused == p ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = p;
		if ( p.IsValid() ) p.Highlight( true );
	}

	/// <summary>The model's outline slot (the material whose path names an outline), -2 until looked up, -1 for none.</summary>
	private int _outlineSlot = -2;

	private void Highlight( bool on )
	{
		var r = Renderer;
		if ( !r.IsValid() || r.Model is null ) return;
		var materials = r.Materials;
		if ( _outlineSlot == -2 )
		{
			_outlineSlot = -1;
			for ( int i = 0; i < materials.Count; i++ )
			{
				var m = materials.GetOriginal( i );
				var name = m?.ResourcePath ?? m?.Name ?? "";
				if ( name.Contains( "outline", StringComparison.OrdinalIgnoreCase ) ) { _outlineSlot = i; break; }
			}
			if ( _outlineSlot < 0 ) Log.Warning( $"[Potion] {r.Model.ResourcePath} has no outline material to turn white" );
		}
		if ( _outlineSlot < 0 ) return;
		_focusOutline ??= Material.Load( FocusOutlineMaterial );
		materials.SetOverride( _outlineSlot, on ? _focusOutline : null );
	}

	/// <summary>Diagnostics for a refused E: the nearest potion's distance, angle off the view and what blocks the line to it.</summary>
	public static string Describe( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		if ( All.Count == 0 ) return "no potions registered on this floor";
		PotionPickup nearest = null;
		float best = float.MaxValue;
		foreach ( var p in All )
		{
			if ( !p.IsValid() ) continue;
			var d = p.Centre.Distance( eye );
			if ( d < best ) { best = d; nearest = p; }
		}
		if ( nearest is null ) return "no valid potions";
		var to = nearest.Centre - eye;
		var angle = MathF.Acos( Math.Clamp( Vector3.Dot( to.Normal, forward ), -1f, 1f ) ) * 180f / MathF.PI;
		var tr = scene.Trace.Ray( eye, nearest.Centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
		var block = tr.Hit && tr.Distance < best - 6f ? $"blocked by {tr.GameObject?.Name ?? "?"} at {tr.Distance:0} u" : "line clear";
		return $"{All.Count} on the floor; nearest {nearest.Label} {best:0} u away (range {UseRange:0}), {angle:0}° off the view (cone {UseCone:0}), {block}";
	}

	/// <summary>Picked up: gone from the floor.</summary>
	/// <summary>
	/// Claimed by a pickup whose hand is still reaching for it: out of focus (its outline back to ink) and out of the
	/// pick list, so E cannot take it twice, but still standing on the table until the hand closes on it (Taken).
	/// </summary>
	public void Reserve()
	{
		if ( Focused == this ) SetFocused( null );
		All.Remove( this );
		_reserved = true;
	}

	public void Taken()
	{
		if ( Focused == this ) Focused = null;
		All.Remove( this );
		GameObject.Destroy();
	}
}
