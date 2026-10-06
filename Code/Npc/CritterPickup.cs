using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Npc;

/// <summary>
/// A live critter the player can catch with E: the dungeon's frogs. Put on every critter the spawner makes
/// (SkeletonSpawner.Spawn), it works exactly like PotionPickup — a cone test from the eye plus a line of sight,
/// the one in focus wearing the white outline and named by the HUD prompt — except the thing in focus moves and
/// can be killed. A dead frog leaves the list for good: it is meat, not a pet.
/// </summary>
[Title( "Critter Pickup" ), Category( "Void Mage" ), Icon( "pets" )]
public sealed class CritterPickup : Component
{
	/// <summary>How close the player has to be (eye to frog, units). Frogs are small and hop, so it is a bit shorter than a potion's.</summary>
	[ConVar( "frog_catch_range" )] public static float UseRange { get; set; } = 120f;
	/// <summary>How closely the player has to look at it (degrees off the view centre). Wider than a potion's: it is a moving target.</summary>
	[ConVar( "frog_catch_cone" )] public static float UseCone { get; set; } = 24f;

	public static readonly List<CritterPickup> All = new();
	/// <summary>The critter the local player could catch right now.</summary>
	public static CritterPickup Focused { get; private set; }

	// the frog's own body is an npc/enemy capsule, so those tags have to pass or the line of sight hits itself
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", "potion" };

	private SkeletonNpc _npc;
	private ModelRenderer _renderer;

	private SkeletonNpc Npc => _npc.IsValid() ? _npc : (_npc = Components.Get<SkeletonNpc>( FindMode.EverythingInSelfAndDescendants ));
	private ModelRenderer Renderer => _renderer.IsValid() ? _renderer : (_renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ));

	public string Label => "Frog";

	/// <summary>The animal's visible middle (it hops, so this is read fresh every frame).</summary>
	public Vector3 Centre
	{
		get
		{
			var r = Renderer;
			return r.IsValid() ? r.Bounds.Center : WorldPosition + Vector3.Up * 6f;
		}
	}

	/// <summary>Alive and catchable: a killed frog can never be picked up (the user's rule).</summary>
	public bool Catchable => Npc.IsValid() && !Npc.IsDead;

	protected override void OnEnabled()
	{
		if ( !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDisabled()
	{
		All.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	/// <summary>Owner, every frame: the nearest live critter inside the range and the cone with a clear line from the eye, or none.</summary>
	public static CritterPickup UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		CritterPickup best = null;
		float bestScore = float.MaxValue;
		float cosCone = MathF.Cos( UseCone.Clamp( 1f, 89f ) * MathF.PI / 180f );
		float range = MathF.Max( 10f, UseRange );
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			var c = All[i];
			if ( !c.IsValid() ) { All.RemoveAt( i ); continue; }
			if ( !c.Catchable ) continue;
			var to = c.Centre - eye;
			var dist = to.Length;
			if ( dist > range || dist < 1f ) continue;
			var dot = Vector3.Dot( to / dist, forward );
			if ( dot < cosCone ) continue;
			var score = (1f - dot) * 400f + dist;
			if ( score >= bestScore ) continue;
			var tr = scene.Trace.Ray( eye, c.Centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
			if ( tr.Hit && tr.Distance < dist - 6f ) continue;
			best = c;
			bestScore = score;
		}
		SetFocused( best );
		// re-applied every frame while in focus: an override set on a scene object that is not built yet is lost
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( CritterPickup c )
	{
		if ( Focused == c ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = c;
		if ( c.IsValid() ) c.Highlight( true );
	}

	/// <summary>The model's outline slot (the material whose path names an outline), -2 until looked up, -1 for none.</summary>
	private int _outlineSlot = -2;
	private static Material _focusOutline;

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
		}
		if ( _outlineSlot < 0 ) return;
		_focusOutline ??= Material.Load( VoidMage.Run.PotionPickup.FocusOutlineMaterial );
		materials.SetOverride( _outlineSlot, on ? _focusOutline : null );
	}

	/// <summary>
	/// Claimed by a catch whose hand is still reaching: out of focus and out of the pick list, so E cannot take it
	/// twice, but the frog is still sitting there until the hand closes on it (<see cref="Catch"/>). Mirrors
	/// PotionPickup.Reserve; PotionDrinker holds the pair apart by PickupHideFrame.
	/// </summary>
	public void Reserve()
	{
		if ( Focused == this ) Focused = null;
		All.Remove( this );
		Highlight( false );
	}

	/// <summary>
	/// Caught: into the bag it goes and off the floor. Solo for now, like the shards and the potions — the frog is a
	/// host-owned networked object, so a client catching one would need an RPC; it leaves the pick list either way,
	/// so the press never reads as nothing happening.
	/// </summary>
	public void Catch()
	{
		Reserve();
		if ( !Networking.IsHost ) return;
		var npc = Npc;
		if ( npc.IsValid() ) npc.GameObject.Destroy();
		else GameObject.Destroy();
	}
}
