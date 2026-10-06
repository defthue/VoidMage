using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// The shrine room's altar, made useful (user, 2026-09-18): walk up to it, press E, and the staff is full again.
/// It costs nothing and it never runs out — the shrine is a place to top up before the next fight, not a resource.
///
/// It finds the player exactly the way the potions, scrolls and frogs do (a cone from the eye, a range, and a clear
/// line of sight), wears the same white focus outline while it is the thing E would use, and is offered by the same
/// prompt. <see cref="VoidMage.Character.PotionDrinker"/> drives the focus and the press; the altar is placed by
/// <see cref="DungeonBuilder"/> wherever the decoration put one (<see cref="DecorCatalog.AltarId"/>).
///
/// Nothing is allowed to stand on an altar — its catalog entry deliberately has no Top — so the surface the player
/// is aiming at is always the shrine itself.
/// </summary>
[Title( "Mana Shrine" ), Category( "Void Mage" ), Icon( "auto_awesome" )]
public sealed class ManaShrine : Component
{
	/// <summary>How close the player has to be (eye to shrine, units). Generous: it is a big prop you walk up to.</summary>
	[ConVar( "shrine_use_range" )] public static float UseRange { get; set; } = 170f;
	/// <summary>How closely the player has to look at it (degrees off the view centre).</summary>
	[ConVar( "shrine_use_cone" )] public static float UseCone { get; set; } = 30f;
	/// <summary>Seconds before the same shrine may be used again: only so one press is one fill, not a held-down hum.</summary>
	[ConVar( "shrine_use_cooldown" )] public static float UseCooldown { get; set; } = 0.6f;

	public static readonly List<ManaShrine> All = new();
	/// <summary>The shrine the local player could use right now.</summary>
	public static ManaShrine Focused { get; private set; }

	// the altar is a solid prop, so the line of sight has to be allowed through everything that is not the dungeon
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", "potion", "critter" };

	private ModelRenderer _renderer;
	private float _usedAt = float.NegativeInfinity;

	private ModelRenderer Renderer => _renderer.IsValid() ? _renderer : (_renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ));

	public string Label => "Shrine";

	/// <summary>Where the player is aiming when they look at it: the prop's own middle, read from the model.</summary>
	public Vector3 Centre
	{
		get
		{
			var r = Renderer;
			return r.IsValid() ? r.Bounds.Center : WorldPosition + Vector3.Up * 40f;
		}
	}

	/// <summary>Whether using it right now would do anything: false while the pool is already full, so the prompt can say so.</summary>
	public static bool PlayerIsFull
	{
		get
		{
			var mana = VoidMage.Character.PlayerMana.Local;
			return mana.IsValid() && mana.Mana >= mana.EffectiveMaxMana - 0.01f;
		}
	}

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

	/// <summary>Owner, every frame: the nearest shrine inside the range and the cone with a clear line from the eye, or none.</summary>
	public static ManaShrine UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		ManaShrine best = null;
		float bestScore = float.MaxValue;
		float cosCone = MathF.Cos( UseCone.Clamp( 1f, 89f ) * MathF.PI / 180f );
		float range = MathF.Max( 10f, UseRange );
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			var s = All[i];
			if ( !s.IsValid() ) { All.RemoveAt( i ); continue; }
			var to = s.Centre - eye;
			var dist = to.Length;
			if ( dist > range || dist < 1f ) continue;
			var dot = Vector3.Dot( to / dist, forward );
			if ( dot < cosCone ) continue;
			var score = (1f - dot) * 400f + dist;
			if ( score >= bestScore ) continue;
			var tr = scene.Trace.Ray( eye, s.Centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
			// the altar itself is what the ray is allowed to end on
			if ( tr.Hit && tr.Distance < dist - 6f && tr.GameObject != s.GameObject ) continue;
			best = s;
			bestScore = score;
		}
		SetFocused( best );
		// re-applied every frame while in focus: an override set on a scene object that is not built yet is lost
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( ManaShrine s )
	{
		if ( Focused == s ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = s;
		if ( s.IsValid() ) s.Highlight( true );
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

	/// <summary>E on the shrine: the staff is full again, for nothing. Local only — mana is the owner's own pool.</summary>
	public void Use()
	{
		if ( Time.Now < _usedAt + UseCooldown ) return;
		_usedAt = Time.Now;
		var mana = VoidMage.Character.PlayerMana.Local;
		if ( !mana.IsValid() ) return;
		mana.Refill();
		if ( !string.IsNullOrEmpty( UseSound ) ) VoidMage.Audio.GameAudio.Play( UseSound, Centre );
	}

	/// <summary>Played where the shrine stands when it is used; empty for none. heden's heal-circle choir (the holy aura).</summary>
	[ConVar( "shrine_use_sound" )] public static string UseSound { get; set; } = "sounds/holyaura.sound";
}
