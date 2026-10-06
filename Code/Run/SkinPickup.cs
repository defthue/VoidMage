using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Inventory;

namespace VoidMage.Run;

/// <summary>
/// A weapon lying in the dungeon: the 1% find out of a broken crate or barrel (user, 2026-09-17). The weapons monsters
/// carry are never here - those are unlocked by killing the monster holding one - so this is only the rest: the swords
/// and staffs that will also be on the shop's shelf.
///
/// It is the player's own copy of the model (Assets/PlayerWeapons, so it is exactly the thing you will hold), turning
/// on the spot with a light under it like a scroll, with the white outline and E of the potions. Picked up it unlocks
/// at once and is kept for good: it is cosmetic, so there is nothing to carry home and nothing to lose on a death.
/// Solo for now like the shards, potions and scrolls: dropped by the host, taken by the local player, swept with the floor.
/// </summary>
[Title( "Weapon Pickup" ), Category( "Void Mage" ), Icon( "swords" )]
public sealed class SkinPickup : Component
{
	public const string Tag = "weaponpickup";

	[ConVar( "skin_hover_height" )] public static float HoverHeight { get; set; } = 26f;
	/// <summary>On top of the model's own (baked) size: the weapons are held at 0.2, a pickup wants to read from across the room.</summary>
	[ConVar( "skin_pickup_scale" )] public static float ModelScale { get; set; } = 1.6f;
	/// <summary>The same for one standing in a rack, where it has to pass for a real weapon beside the pack's own (0.2 x 2 is about the props' 0.3937).</summary>
	[ConVar( "skin_rack_scale" )] public static float RackScale { get; set; } = 2f;
	[ConVar( "skin_pickup_sound" )] public static string PickupSound { get; set; } = VoidMage.Audio.GameAudio.ReceiveItemSound;

	/// <summary>The skin it is (WeaponSkins id).</summary>
	public string SkinId { get; set; }

	/// <summary>
	/// Standing in a weapon rack (user, 2026-09-17): it stays exactly where the dungeon put it - no floating, no
	/// turning, no light of its own - and is grabbed with E like anything else. Only its lit materials and the white
	/// focus outline make it stand out.
	/// </summary>
	public bool InRack { get; set; }

	public static readonly List<SkinPickup> All = new();
	public static SkinPickup Focused { get; private set; }

	public WeaponSkin Skin => WeaponSkins.Get( SkinId );
	public string Label => Skin?.Name ?? "Weapon";

	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", PotionPickup.Tag, ScrollPickup.Tag, Tag };

	private const float Gravity = 800f;
	private ModelRenderer _renderer;
	private PointLight _light;
	private Vector3 _rest;
	private Vector3 _velocity;
	private bool _falling;
	private float _fallStarted;
	private float _lift;
	private float _phase;
	private float _bornAt;
	private int _outlineSlot = -2;
	private bool _lit;
	private static Material _focusOutline;

	/// <summary>One of this skin is already lying on the floor (the roll never puts out two of the same weapon).</summary>
	public static bool AlreadyOut( string id )
	{
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			if ( !All[i].IsValid() ) { All.RemoveAt( i ); continue; }
			if ( All[i].SkinId == id ) return true;
		}
		return false;
	}

	/// <summary>Host: the weapon hops out of `at` with a little of the blow's push, lands and starts to turn.</summary>
	public static SkinPickup Drop( Scene scene, Vector3 at, string skinId, GameObject parent, Vector3 push )
	{
		var pickup = Make( scene, at, skinId, parent );
		if ( pickup is null ) return null;
		var side = push.WithZ( 0f );
		side = side.IsNearZeroLength ? Vector3.Random.WithZ( 0f ).Normal : side.Normal;
		pickup._velocity = side * Game.Random.Float( 40f, 80f ) + Vector3.Up * Game.Random.Float( 220f, 260f );
		pickup._falling = true;
		return pickup;
	}

	/// <summary>
	/// A weapon standing in a rack: put there by the dungeon builder, keeping the rotation it is given, and it stays
	/// put (see <see cref="InRack"/>). The model's own lowest point is what `at` places, so a sword and a staff both
	/// stand on the floor rather than sinking into it.
	/// </summary>
	public static SkinPickup Rack( Scene scene, Vector3 at, Rotation rotation, string skinId, GameObject parent )
	{
		var pickup = Make( scene, at, skinId, parent );
		if ( pickup is null ) return null;
		pickup.InRack = true;
		pickup.GameObject.WorldRotation = rotation;
		return pickup;
	}

	private static SkinPickup Make( Scene scene, Vector3 at, string skinId, GameObject parent )
	{
		if ( !scene.IsValid() || WeaponSkins.Get( skinId ) is null ) return null;
		var go = new GameObject( true, "weapon pickup" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		if ( parent.IsValid() ) go.SetParent( parent, false );
		go.WorldPosition = at;
		go.Tags.Add( Tag );
		var pickup = go.Components.Create<SkinPickup>();
		pickup.SkinId = skinId;
		return pickup;
	}

	protected override void OnStart()
	{
		_bornAt = Time.Now;
		_rest = WorldPosition;
		_fallStarted = Time.Now;
		_phase = Game.Random.Float( 0f, MathF.PI * 2f );
		_renderer = Components.Create<ModelRenderer>();
		_renderer.Model = WeaponSkins.ModelOf( Skin );
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		WorldScale = new Vector3( InRack ? RackScale : ModelScale );
		// cold and bright, so it is never mistaken for a potion or a scroll: this one is a weapon. A racked one gets
		// no light and no float at all (user): it is furniture until you look at it.
		if ( !InRack )
		{
			_light = Components.Create<PointLight>();
			_light.LightColor = new Color( 0.7f, 0.85f, 1f ) * 0.5f;
			_light.Radius = 70f;
			_light.Shadows = false;
		}
		if ( !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	protected override void OnUpdate()
	{
		// its own lighting, so a weapon in a dark corner or a rack is not a black silhouette (kept asking until the model is up)
		if ( !_lit ) _lit = VoidMage.Rendering.PickupLit.Apply( _renderer );
		if ( InRack ) return;
		var dt = MathF.Min( Time.Delta, 0.05f );
		if ( _falling ) FallStep( dt );
		_lift = MathX.Approach( _lift, _falling ? 0f : HoverHeight, 40f * dt );
		float settled = HoverHeight > 0f ? _lift / HoverHeight : 0f;
		float bob = MathF.Sin( Time.Now * 2.2f + _phase ) * 3f * settled;
		WorldPosition = _rest + Vector3.Up * (_lift + bob);
		// stood on its point and turning, tipped a little off vertical so the blade catches the light
		WorldRotation = Rotation.FromYaw( (Time.Now - _bornAt) * 55f + _phase * 57f ) * Rotation.FromPitch( -18f * settled );
		if ( _light.IsValid() )
		{
			float pulse = 0.85f + 0.15f * MathF.Sin( Time.Now * 2.6f + _phase );
			_light.Radius = 70f * pulse;
		}
	}

	private void FallStep( float dt )
	{
		_velocity += Vector3.Down * Gravity * dt;
		var to = _rest + _velocity * dt;
		if ( _velocity.z < 0f )
		{
			var tr = Scene.Trace.Ray( _rest + Vector3.Up * 4f, to ).WithoutTags( PassThrough ).Run();
			if ( tr.Hit && tr.Normal.z > 0.5f )
			{
				_rest = tr.HitPosition;
				_falling = false;
				return;
			}
		}
		_rest = to;
		if ( Time.Now - _fallStarted > 3f ) _falling = false;
	}

	/// <summary>Owner, every frame: the nearest weapon in reach and in view with a clear line (the potions' rules).</summary>
	public static SkinPickup UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		SkinPickup best = null;
		float bestScore = float.MaxValue;
		float cosCone = MathF.Cos( PotionPickup.UseCone.Clamp( 1f, 89f ) * 1.3f * MathF.PI / 180f );
		float range = MathF.Max( 10f, PotionPickup.UseRange * 1.2f );
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			var p = All[i];
			if ( !p.IsValid() ) { All.RemoveAt( i ); continue; }
			if ( p._falling ) continue;
			// a racked weapon is held by its grip near the floor: aim at the model, not at the pivot
			var centre = p.InRack && p._renderer.IsValid() ? p._renderer.Bounds.Center : p.WorldPosition + Vector3.Up * 8f;
			var to = centre - eye;
			var dist = to.Length;
			if ( dist > range || dist < 1f ) continue;
			var dot = Vector3.Dot( to / dist, forward );
			if ( dot < cosCone ) continue;
			var score = (1f - dot) * 400f + dist;
			if ( score >= bestScore ) continue;
			var tr = scene.Trace.Ray( eye, centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
			if ( tr.Hit && tr.Distance < dist - 8f ) continue;
			best = p;
			bestScore = score;
		}
		SetFocused( best );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( SkinPickup p )
	{
		if ( Focused == p ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = p;
		if ( p.IsValid() ) p.Highlight( true );
	}

	/// <summary>The white outline of the pickup E would take: the outline slot swapped, as the potions do.</summary>
	private void Highlight( bool on )
	{
		if ( !_renderer.IsValid() || _renderer.Model is null ) return;
		var materials = _renderer.Materials;
		if ( _outlineSlot == -2 )
		{
			_outlineSlot = -1;
			for ( int i = 0; i < materials.Count; i++ )
			{
				var m = materials.GetOriginal( i );
				var name = m?.ResourcePath ?? m?.Name ?? "";
				if ( name.Contains( "outline", StringComparison.OrdinalIgnoreCase ) ) { _outlineSlot = i; break; }
			}
		}
		if ( _outlineSlot < 0 ) return;
		_focusOutline ??= Material.Load( PotionPickup.FocusOutlineMaterial );
		materials.SetOverride( _outlineSlot, on ? _focusOutline : null );
	}

	/// <summary>Taken with E: unlocked there and then (cosmetic, so there is nothing to carry home), gone from the floor.</summary>
	public void Take()
	{
		var inventory = PlayerInventory.Local;
		if ( !inventory.IsValid() ) return;
		if ( Focused == this ) SetFocused( null );
		All.Remove( this );
		inventory.DiscoverSkin( SkinId );
		if ( !string.IsNullOrEmpty( PickupSound ) ) VoidMage.Audio.GameAudio.Play( PickupSound, WorldPosition );
		GameObject.Destroy();
	}

	/// <summary>Dev: drop one in front of the player. No name picks a random one not found yet.</summary>
	[ConCmd( "skin_drop" )]
	public static void DropCommand( string which = "" )
	{
		var scene = Game.ActiveScene;
		var player = scene.IsValid() ? scene.GetAllComponents<VoidMage.Character.PlayerHealth>().FirstOrDefault( p => !p.IsProxy ) : null;
		if ( !player.IsValid() || !scene.Camera.IsValid() ) { Log.Warning( "[Loot] no local player" ); return; }
		var inv = PlayerInventory.Local;
		var skin = string.IsNullOrWhiteSpace( which )
			? WeaponSkins.CrateDrops.Where( s => !(inv.IsValid() && inv.HasSkin( s.Id )) ).OrderBy( _ => Game.Random.Float() ).FirstOrDefault()
			: WeaponSkins.Find( which );
		if ( skin is null ) { Log.Warning( $"[Loot] no weapon for '{which}' (everything found?)" ); return; }
		var at = player.WorldPosition + scene.Camera.WorldRotation.Forward.WithZ( 0f ).Normal * 70f + Vector3.Up * 40f;
		var dungeon = scene.GetAllComponents<VoidMage.Dungeon.DungeonBuilder>().FirstOrDefault();
		Drop( scene, at, skin.Id, dungeon.IsValid() ? dungeon.GameObject : null, Vector3.Zero );
		Log.Info( $"[Loot] dropped {skin.Name}" );
	}
}
