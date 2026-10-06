using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Inventory;

namespace VoidMage.Run;

/// <summary>
/// A scroll lying in the dungeon (a kill reached one of the floor's scroll counts, Run.ScrollDrops; a spell was swapped off the
/// staff): floating, turning and glowing like a shard, taken with E like a potion (the white outline shows which one
/// E takes), never on its own: taking a scroll is for good, so it is always the player's press (user, 2026-09-20).
/// A scroll the player has never had lies SEALED, the closed Dungeon Realms bundle: E breaks the seal and it is on
/// the staff, whatever it was. One they have had before lies OPEN, and the prompt names it, so taking it is a choice.
/// Spell scrolls wear the gold-sealed atlas (shaders/realmsscrollspell.vmat), enchantments the pack's red seal; a
/// black-sealed enchantment (one that costs or replaces something) glows a cold violet instead of warm, sealed or
/// not. Solo for now like the shards and potions: dropped by the host, taken by the local player, swept with the floor.
/// </summary>
[Title( "Scroll Pickup" ), Category( "Void Mage" ), Icon( "receipt_long" )]
public sealed class ScrollPickup : Component
{
	public const string Tag = "scroll";
	public const string ModelPath = "dungeonrealms/sm_prop_scroll_01.vmdl";
	public const string OpenModelPath = "dungeonrealms/sm_prop_scroll_open_01.vmdl";
	/// <summary>The gold-sealed atlas for a spell scroll, at a full ambient floor (fully lit: user, 2026-09-17).</summary>
	public const string SpellMaterial = "shaders/realmsscrollspell.vmat";
	/// <summary>The pack's own red-sealed paper, at a full ambient floor: the enchantment scroll.</summary>
	public const string PaperMaterial = "shaders/scrollfull.vmat";

	[ConVar( "scroll_hover_height" )] public static float HoverHeight { get; set; } = 18f;
	[ConVar( "scroll_scale" )] public static float ModelScale { get; set; } = 0.75f;
	[ConVar( "scroll_pickup_sound" )] public static string PickupSound { get; set; } = VoidMage.Audio.GameAudio.ReceiveItemSound;
	/// <summary>The runes on an open scroll: their width as a share of the paper's, their lift off it, how bright the ink is.</summary>
	[ConVar( "scroll_rune_width" )] public static float RuneWidth { get; set; } = 0.62f;
	[ConVar( "scroll_rune_lift" )] public static float RuneLift { get; set; } = 0.3f;
	[ConVar( "scroll_rune_brightness" )] public static float RuneBrightness { get; set; } = 0.9f;

	// Measured in Blender off SM_Prop_Scroll_Open_01.fbx (the shop's numbers, VillageShop.ShopItem): the paper lies flat
	// in local XY around the pivot, 15.9 across, its top 1.41 up; the rollers run along the model's LONGER side.
	private const float PaperAcross = 15.9f;
	private const float PaperTop = 1.41f;
	private const string RuneFont = "Noto Sans Runic";
	private TextRenderer _runes;

	/// <summary>The scroll it holds (never the sealed key: whether it LIES sealed is what the player knows, asked live).</summary>
	public Scroll Scroll { get; set; }
	/// <summary>The local player has never had this scroll: it lies closed and nameless.</summary>
	public bool Sealed => PlayerInventory.Local is not { } inv || !inv.IsDiscovered( Scroll );
	/// <summary>A black seal: this enchantment costs or replaces something, and it can never be taken off again.</summary>
	public bool Black => Scroll.ModifierDef?.Black ?? false;

	public static readonly List<ScrollPickup> All = new();
	public static ScrollPickup Focused { get; private set; }

	/// <summary>The prompt under the crosshair: a sealed scroll says only what kind of seal it wears; a known one says its name and its one line.</summary>
	public string Label
	{
		get
		{
			if ( Sealed ) return Scroll.IsSpell ? "Break the gold seal" : Black ? "Break the black seal" : "Break the seal";
			if ( Scroll.Spell is { } spell ) return $"Take {spell.Name} · replaces your spell";
			var copies = PlayerInventory.Local?.Copies( Scroll.Modifier ) ?? 0;
			var tier = copies switch { 1 => " II", 2 => " III", _ => "" };
			return $"Take {Scroll.Name}{tier} · {Scroll.ModifierDef?.Banner}";
		}
	}
	/// <summary>Why E would do nothing ("" when it would): three copies already, or the spell already on the staff.</summary>
	public string WhyNot => PlayerInventory.Local is { } inv && !inv.CanTake( Scroll ) ? (Scroll.IsSpell ? "already your spell" : "three copies already") : "";

	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", PotionPickup.Tag, Tag };

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
	private int _paperSlot = -2;
	private bool _builtSealed;
	private bool _built;
	private static Material _focusOutline;
	private static Material _spellMaterial;
	private static Material _paperMaterial;

	/// <summary>Host: a scroll hops out of `at` with a little of the blow's push, lands and starts to float.</summary>
	public static ScrollPickup Drop( Scene scene, Vector3 at, Scroll scroll, GameObject parent, Vector3 push )
	{
		var pickup = Spawn( scene, at, scroll, parent, push );
		if ( pickup is null ) return null;
		var side = push.WithZ( 0f );
		side = side.IsNearZeroLength ? Vector3.Random.WithZ( 0f ).Normal : side.Normal;
		pickup._velocity = side * Game.Random.Float( 40f, 80f ) + Vector3.Up * Game.Random.Float( 220f, 260f );
		return pickup;
	}

	/// <summary>A scroll put down at `at` (a swapped spell at the player's feet, a dev drop): it falls to the floor and floats.</summary>
	public static ScrollPickup Spawn( Scene scene, Vector3 at, Scroll scroll, GameObject parent, Vector3 push )
	{
		if ( !scene.IsValid() || scroll.IsEmpty ) return null;
		var go = new GameObject( true, "scroll pickup" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		if ( parent.IsValid() ) go.SetParent( parent, false );
		go.WorldPosition = at;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );
		go.Tags.Add( Tag );
		var pickup = go.Components.Create<ScrollPickup>();
		pickup.Scroll = scroll.Opened;
		pickup._velocity = push;
		pickup._falling = true;
		return pickup;
	}

	protected override void OnStart()
	{
		_bornAt = Time.Now;
		_rest = WorldPosition;
		_fallStarted = Time.Now;
		_phase = Game.Random.Float( 0f, MathF.PI * 2f );
		_renderer = Components.Create<ModelRenderer>();
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		WorldScale = new Vector3( ModelScale );
		// a breath of light under it: gold for a spell, warm for an enchantment, a cold violet for a black seal
		_light = Components.Create<PointLight>();
		_light.LightColor = Scroll.IsSpell ? new Color( 1f, 0.78f, 0.3f ) * 0.55f
			: Black ? new Color( 0.5f, 0.3f, 1f ) * 0.5f
			: new Color( 1f, 0.62f, 0.4f ) * 0.4f;
		_light.Radius = 60f;
		_light.Shadows = false;
		BuildModel();
		if ( !All.Contains( this ) ) All.Add( this );
	}

	/// <summary>Closed while the player has never had it, open once they have (the first copy taken opens every other one lying about).</summary>
	private void BuildModel()
	{
		var isSealed = Sealed;
		if ( _built && isSealed == _builtSealed ) return;
		_built = true;
		_builtSealed = isSealed;
		_renderer.Model = Model.Load( isSealed ? ModelPath : OpenModelPath );
		_paperSlot = -2;
		_outlineSlot = -2;
		ShowRunes( !isSealed );
		if ( Focused == this ) Highlight( true );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		if ( Focused == this ) Focused = null;
	}

	protected override void OnUpdate()
	{
		var dt = MathF.Min( Time.Delta, 0.05f );
		BuildModel();
		ApplySpellMaterial();

		if ( _falling ) FallStep( dt );
		_lift = MathX.Approach( _lift, _falling ? 0f : HoverHeight, 40f * dt );
		float settled = HoverHeight > 0f ? _lift / HoverHeight : 0f;
		float bob = MathF.Sin( Time.Now * 2.2f + _phase ) * 2.5f * settled;
		// a slow tilt so the seal catches the light as it turns
		WorldPosition = _rest + Vector3.Up * (_lift + bob);
		if ( _builtSealed || !FacePlayer( dt, settled ) )
			WorldRotation = Rotation.FromYaw( (Time.Now - _bornAt) * 70f + _phase * 57f ) * Rotation.FromRoll( 12f * settled );
		if ( _light.IsValid() )
		{
			// the light rides the scroll (a component on its object): only its reach breathes
			float pulse = 0.85f + 0.15f * MathF.Sin( Time.Now * 3f + _phase );
			_light.Radius = 60f * pulse;
		}
	}

	/// <summary>
	/// An open scroll does not spin like a sealed bundle: it stands up and turns its written side to the player, so the
	/// runes can be read from across the room (a slow sway keeps it alive). False while there is no camera to face.
	/// </summary>
	private bool FacePlayer( float dt, float settled )
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return false;
		var toCamera = (camera.WorldPosition - WorldPosition).WithZ( 0f );
		if ( toCamera.IsNearZeroLength ) return false;
		toCamera = toCamera.Normal;
		// the paper's normal (local up) toward the reader, the writing's top (across the rollers) toward the ceiling
		var along = PaperAlong();
		var forward = along == Vector3.Forward ? Vector3.Up : Vector3.Cross( Vector3.Up, toCamera );
		var facing = Rotation.LookAt( forward, toCamera ) * Rotation.FromRoll( MathF.Sin( Time.Now * 1.3f + _phase ) * 6f * settled );
		// lying flat while it falls, standing once it floats
		var flat = Rotation.FromYaw( _phase * 57f );
		var target = Rotation.Slerp( flat, facing, settled );
		WorldRotation = Rotation.Slerp( WorldRotation, target, 1f - MathF.Exp( -dt * 8f ) );
		return true;
	}

	private void FallStep( float dt )
	{
		_velocity += Vector3.Down * Gravity * dt;
		var to = _rest + _velocity * dt;
		var tr = Scene.Trace.Ray( _rest + Vector3.Up * 4f, to ).WithoutTags( PassThrough ).Run();
		if ( tr.Hit && tr.Normal.z > 0.5f && _velocity.z < 0f )
		{
			_rest = tr.HitPosition;
			_falling = false;
			return;
		}
		if ( tr.Hit && !tr.StartedSolid )
		{
			// a wall (or the ceiling) in the way of the hop: it stops against it and drops straight down, never
			// through it (user, 2026-09-20: a scroll went through the wall behind its chest)
			_velocity -= tr.Normal * MathF.Min( 0f, Vector3.Dot( _velocity, tr.Normal ) );
			to = _rest + Vector3.Up * (_velocity.z * dt);
		}
		_rest = to;
		if ( Time.Now - _fallStarted > 3f ) _falling = false;
	}

	/// <summary>Across the rollers, in the open model's own frame: the way the writing's top points.</summary>
	private Vector3 PaperAlong()
	{
		var size = _renderer.IsValid() && _renderer.Model is not null ? _renderer.Model.Bounds.Size : Vector3.One;
		return size.x >= size.y ? Vector3.Left : Vector3.Forward;
	}

	/// <summary>
	/// A known scroll lies open, and an open scroll is only recognisable by what is written on it (user, 2026-09-20:
	/// "otherwise its just a unrecognizable blank scroll"): its two-rune code on the paper, in the HUD's colour for it,
	/// exactly as on the shop's table. World text reads right when its forward points AWAY from the reader, so on the
	/// paper forward is straight down into it and up runs across the rollers.
	/// </summary>
	private void ShowRunes( bool show )
	{
		var text = show ? ScrollText.Runes( Scroll ) : "";
		if ( string.IsNullOrEmpty( text ) )
		{
			if ( _runes.IsValid() ) _runes.GameObject.Enabled = false;
			return;
		}
		if ( !_runes.IsValid() )
		{
			var go = new GameObject( true, "scroll runes" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			go.SetParent( GameObject, false );
			_runes = go.Components.Create<TextRenderer>();
			_runes.FontFamily = RuneFont;
			_runes.FontSize = 64f;
			_runes.FontWeight = 400;
		}
		_runes.GameObject.Enabled = true;
		_runes.GameObject.LocalPosition = new Vector3( 0f, 0f, PaperTop + RuneLift );
		_runes.GameObject.LocalRotation = Rotation.LookAt( Vector3.Down, PaperAlong() );
		_runes.Text = text;
		_runes.Color = ((Color.Parse( ScrollText.Colour( Scroll ) ) ?? Color.Black) * RuneBrightness).WithAlpha( 1f );
		// one text pixel is one unit before scaling: fitted to a share of the paper's width
		var measured = new TextRendering.Scope( text, Color.White, 64f, RuneFont ).Measure().x;
		if ( measured < 1f ) measured = 64f * text.Length * 0.7f;
		_runes.Scale = PaperAcross * RuneWidth / measured;
	}

	/// <summary>
	/// The paper slot swapped for the scroll's own material: the gold-sealed atlas for a spell, the pack's red seal for
	/// an enchantment, both at a full ambient floor so a scroll on the floor is lit wherever it lies (user, 2026-09-17).
	/// Re-applied until the scene object exists, like the potion outline.
	/// </summary>
	private void ApplySpellMaterial()
	{
		if ( _paperSlot == -1 || !_renderer.IsValid() || _renderer.Model is null ) return;
		var materials = _renderer.Materials;
		if ( _paperSlot == -2 )
		{
			_paperSlot = -1;
			for ( int i = 0; i < materials.Count; i++ )
			{
				var m = materials.GetOriginal( i );
				var name = m?.ResourcePath ?? m?.Name ?? "";
				if ( name.Contains( "realmsshader", StringComparison.OrdinalIgnoreCase ) ) { _paperSlot = i; break; }
			}
		}
		if ( _paperSlot < 0 ) return;
		if ( Scroll.IsSpell ) materials.SetOverride( _paperSlot, _spellMaterial ??= Material.Load( SpellMaterial ) );
		else materials.SetOverride( _paperSlot, _paperMaterial ??= Material.Load( PaperMaterial ) );
	}

	/// <summary>Owner, every frame: the nearest scroll in reach and in view with a clear line, or none (same rules as the potions).</summary>
	public static ScrollPickup UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		ScrollPickup best = null;
		float bestScore = float.MaxValue;
		float cosCone = MathF.Cos( PotionPickup.UseCone.Clamp( 1f, 89f ) * 1.3f * MathF.PI / 180f );
		float range = MathF.Max( 10f, PotionPickup.UseRange * 1.2f );
		for ( int i = All.Count - 1; i >= 0; i-- )
		{
			var p = All[i];
			if ( !p.IsValid() ) { All.RemoveAt( i ); continue; }
			if ( p._falling ) continue;
			var centre = p.WorldPosition + Vector3.Up * 5f;
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
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( ScrollPickup p )
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

	/// <summary>
	/// Taken with E: onto the staff, for good. A spell it pushed off the staff is left lying here in its place, so the
	/// swap can be undone. False (and the scroll stays) when the staff cannot take it.
	/// </summary>
	public bool Take()
	{
		var inventory = PlayerInventory.Local;
		if ( !inventory.IsValid() ) return false;
		var took = inventory.Take( Scroll );
		if ( !took.Ok ) { LastRefusedAt = Time.Now; return false; }
		if ( Focused == this ) SetFocused( null );
		All.Remove( this );
		if ( !string.IsNullOrEmpty( PickupSound ) ) VoidMage.Audio.GameAudio.Play( PickupSound, WorldPosition );
		if ( !took.Dropped.IsEmpty ) Spawn( Scene, _rest + Vector3.Up * 24f, took.Dropped, GameObject.Parent, Vector3.Up * 120f );
		GameObject.Destroy();
		return true;
	}

	/// <summary>When E was last pressed on a scroll the staff could not take (the prompt shakes).</summary>
	public static float LastRefusedAt { get; private set; } = float.NegativeInfinity;
}
