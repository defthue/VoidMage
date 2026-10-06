using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Inventory;
using VoidMage.Run;

namespace VoidMage.Village;

/// <summary>What a thing on the shop's table is.</summary>
public enum ShopItemKind { HealPotion, ManaPotion, Scroll, Weapon }

/// <summary>
/// One thing for sale on the shopkeeper's table (VillageShop puts this on the props the user stood there). Bought with
/// E like a pickup: the white outline shows which one E takes, the prompt names it and its price.
/// </summary>
[Title( "Shop Item" ), Category( "Void Mage" ), Icon( "sell" )]
public sealed class ShopItem : Component
{
	public ShopItemKind Kind { get; set; }
	/// <summary>The open enchantment a scroll slot sells this visit.</summary>
	public Scroll Scroll { get; set; }
	/// <summary>The skin the weapon slot sells this visit (WeaponSkins id).</summary>
	public string SkinId { get; set; }
	public bool Sold { get; set; }
	/// <summary>Where the user stood it: a restock puts it back exactly here.</summary>
	public Transform Home { get; set; }

	private ModelRenderer _renderer;
	private int _outlineSlot = -2;
	private static Material _focusOutline;

	public ModelRenderer Renderer => _renderer.IsValid() ? _renderer : (_renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelf ));

	public Vector3 Centre => Renderer.IsValid() ? Renderer.Bounds.Center : WorldPosition;

	public int Price => Kind switch
	{
		ShopItemKind.Scroll => VillageShop.ScrollPrice,
		ShopItemKind.Weapon => VillageShop.WeaponPrice,
		_ => VillageShop.PotionPrice,
	};

	/// <summary>What the prompt calls it: a scroll by its enchantment, so nobody has to read the runes (user).</summary>
	public string Label => Kind switch
	{
		ShopItemKind.HealPotion => "Healing potion",
		ShopItemKind.ManaPotion => "Mana potion",
		ShopItemKind.Scroll => $"Scroll of {Scroll.Name}",
		_ => WeaponSkins.Get( SkinId )?.Name ?? "Weapon",
	};

	// ------------------------------------------------------------------ the runes on a scroll slot

	// Measured in Blender off SM_Prop_Scroll_Open_01.fbx (model inches = cm x 0.3937): the paper lies flat in local XY
	// around the pivot, 15.9 across (along the rollers) by 20.9 between them, its wavy top at 0.95..1.41 up. The rollers
	// run along the model's LONGER side (31.2 against 27.4), which is how the axis is told at runtime.
	private const float PaperAcross = 15.9f;
	private const float PaperTop = 1.41f;
	private const string RuneFont = "Noto Sans Runic";

	private TextRenderer _runes;
	/// <summary>+1 / -1: which way along the paper the writing's top points. Turned to face whoever reads it.</summary>
	private float _runesUp = 1f;

	/// <summary>
	/// The enchantment's two-rune code (the one its HUD tile wears) written on the open paper, in the HUD's colour for
	/// it. World text lies in its object's YZ plane and reads right when that object's forward points AWAY from the
	/// reader (decompiled TextRenderer: its fixed matrix is the billboard's with forward = +X), so flat on the paper
	/// forward is straight down into it and up runs along the paper, between the rollers.
	/// </summary>
	public void ShowRunes()
	{
		var text = ScrollText.Runes( Scroll.Opened );
		if ( string.IsNullOrEmpty( text ) )
		{
			if ( _runes.IsValid() ) _runes.GameObject.Enabled = false;
			return;
		}
		if ( !_runes.IsValid() )
		{
			var go = new GameObject( true, "shop runes" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			go.SetParent( GameObject, false );
			go.LocalPosition = new Vector3( 0f, 0f, PaperTop + VillageShop.RuneLift );
			_runes = go.Components.Create<TextRenderer>();
			_runes.FontFamily = RuneFont;
			_runes.FontSize = 64f;
			_runes.FontWeight = 400;
		}
		_runes.GameObject.Enabled = true;
		_runes.Text = text;
		_runes.Color = (Color.Parse( ScrollText.Colour( Scroll.Opened ) ) ?? Color.Black) * VillageShop.RuneBrightness;
		_runes.Color = _runes.Color.WithAlpha( 1f );
		// one text pixel is one unit before scaling: fitted to a share of the paper's width
		var measured = new TextRendering.Scope( text, Color.White, 64f, RuneFont ).Measure().x;
		if ( measured < 1f ) measured = 64f * text.Length * 0.7f;
		_runes.Scale = PaperAcross * VillageShop.RuneWidth / measured;
		FaceReader( force: true );
	}

	protected override void OnUpdate()
	{
		if ( _runes.IsValid() ) FaceReader( force: false );
	}

	/// <summary>The writing's top points away from the camera, so it reads from whichever side of the table you stand.</summary>
	private void FaceReader( bool force )
	{
		var r = Renderer;
		if ( !r.IsValid() || r.Model is null ) return;
		var size = r.Model.Bounds.Size;
		var along = size.x >= size.y ? Vector3.Left : Vector3.Forward;   // local: across the rollers
		var camera = Scene.Camera;
		if ( camera.IsValid() )
		{
			var away = (WorldPosition - camera.WorldPosition).WithZ( 0f );
			if ( !away.IsNearZeroLength )
			{
				// only turns when the reader is clearly on the other side, never flickers from the table's end
				float facing = Vector3.Dot( WorldRotation * along, away.Normal ) * _runesUp;
				if ( facing < -0.35f || (force && facing < 0f) ) { _runesUp = -_runesUp; force = true; }
			}
		}
		if ( force ) _runes.GameObject.LocalRotation = Rotation.LookAt( Vector3.Down, along * _runesUp );
	}

	/// <summary>The white outline of the thing E would buy: the outline slot swapped, as the pickups do.</summary>
	public void Highlight( bool on )
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
		_focusOutline ??= Material.Load( PotionPickup.FocusOutlineMaterial );
		materials.SetOverride( _outlineSlot, on ? _focusOutline : null );
	}
}

/// <summary>
/// The shopkeeper's table (user, 2026-09-18). The user dressed it in the editor with plain props: healing and mana
/// potions, open scrolls and one sword. Nothing in the scene names them, so this finds them by their models when the
/// village starts and sells them for banked shards:
/// - every potion standing there is one potion for sale; bought, it is gone
/// - every open scroll is a SLOT: a random enchantment each visit, sold open and named in the prompt
/// - the sword is a SLOT: each visit a random sword or staff no monster carries (the crate finds), never one the
///   player already owns; the user's sword only marks where it lies
/// A visit is one stay between two trips into the dungeon: coming home (alive or dead) restocks everything, and a slot
/// that was bought stays empty until then. The table is kept in the save, so closing the game does not re-roll it.
/// Solo like the pickups: the stock and the purse are the local player's own.
/// </summary>
[Title( "Village Shop" ), Category( "Void Mage" ), Icon( "storefront" )]
public sealed class VillageShop : Component
{
	public const string ItemTag = "shopitem";
	private const string HealModelName = "sm_item_potion_07";
	private const string ManaModelName = "sm_item_potion_06";
	private const string ScrollModelName = "sm_prop_scroll_open_01";
	private const string WeaponModelPrefix = "sm_wep_";

	// against a purse of 9999 and a full clear paying roughly 700-900 (user's prices; 2026-09-21: scrolls 99 -> 499 now
	// that a bought scroll is replaced on the spot and the purse holds ten times as much)
	[ConVar( "shop_potion_price" )] public static int PotionPrice { get; set; } = 35;
	[ConVar( "shop_scroll_price" )] public static int ScrollPrice { get; set; } = 499;
	/// <summary>The keeper's point at a bought scroll's empty place: the clip, and how far into it the new scroll appears.</summary>
	[ConVar( "shop_point_clip" )] public static string PointClip { get; set; } = "pointhand_index_f_small";
	[ConVar( "shop_point_peak" )] public static float PointPeak { get; set; } = 0.6f;
	[ConVar( "shop_weapon_price" )] public static int WeaponPrice { get; set; } = 299;
	/// <summary>Props further than this from the table's potions are decor, not stock (units).</summary>
	[ConVar( "shop_radius" )] public static float Radius { get; set; } = 250f;
	/// <summary>The weapon for sale may be this much longer than the sword the user laid down before it is shrunk to fit the table.</summary>
	[ConVar( "shop_weapon_fit" )] public static float WeaponFit { get; set; } = 1.35f;
	[ConVar( "shop_buy_sound" )] public static string BuySound { get; set; } = "";
	/// <summary>How much of the paper's width the rune code on a scroll slot fills.</summary>
	[ConVar( "shop_rune_width" )] public static float RuneWidth { get; set; } = 0.6f;
	/// <summary>How far over the paper's highest ripple the runes float (model inches): world text is not part of the mesh.</summary>
	[ConVar( "shop_rune_lift" )] public static float RuneLift { get; set; } = 0.25f;
	/// <summary>World text is unlit: the ink is dimmed by this so it does not glow in the night village.</summary>
	[ConVar( "shop_rune_brightness" )] public static float RuneBrightness { get; set; } = 0.6f;

	public static VillageShop Instance { get; private set; }
	/// <summary>The thing the local player would buy with E right now.</summary>
	public static ShopItem Focused { get; private set; }
	/// <summary>Time of the last E the shop refused (no shards, no room), for the prompt's bump.</summary>
	public static float LastRefusedAt { get; private set; } = float.NegativeInfinity;

	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", ItemTag };

	private readonly List<ShopItem> _items = new();
	private readonly List<ShopItem> _scrolls = new();
	private bool _found;
	private bool _applied;
	private bool _wantRoll;

	// the weapon slot: measured off the sword the user laid on the table, which is then put away for good
	private bool _hasWeaponSlot;
	private Rotation _slotRotation;
	private Vector3 _slotCentre;
	private float _slotBottom;
	private float _slotLength;
	private ShopItem _weapon;

	public static int Purse => RunSave.Data.BankedShards;

	public static VillageShop EnsureOn( Scene scene )
	{
		if ( !scene.IsValid() ) return null;
		if ( Instance.IsValid() ) return Instance;
		foreach ( var s in scene.GetAllComponents<VillageShop>() ) { Instance = s; return s; }
		var go = new GameObject( true, "village_shop" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		return Instance = go.Components.Create<VillageShop>();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		Focused = null;
	}

	/// <summary>Home from the dungeon (RunManager.GoHome): a new visit, a new table.</summary>
	public static void Restock()
	{
		if ( Instance.IsValid() ) Instance._wantRoll = true;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;
		// the owned skins decide the weapon, so nothing is rolled or shown before the inventory has read its save
		var inventory = PlayerInventory.Local;
		if ( !inventory.IsValid() || !inventory.IsLoaded ) return;
		if ( !_found ) FindStock();
		if ( _wantRoll || RunSave.Data.Shop is null ) Roll( inventory );
		if ( !_applied ) Apply( inventory );
	}

	// ------------------------------------------------------------------ the table

	private static string ModelName( Model model )
	{
		var path = (model?.ResourcePath ?? "").Replace( '\\', '/' ).ToLowerInvariant();
		int slash = path.LastIndexOf( '/' );
		if ( slash >= 0 ) path = path.Substring( slash + 1 );
		int dot = path.IndexOf( '.' );
		return dot >= 0 ? path.Substring( 0, dot ) : path;
	}

	private static ShopItemKind? KindOf( string modelName )
	{
		if ( modelName == HealModelName ) return ShopItemKind.HealPotion;
		if ( modelName == ManaModelName ) return ShopItemKind.ManaPotion;
		if ( modelName == ScrollModelName ) return ShopItemKind.Scroll;
		if ( modelName.StartsWith( WeaponModelPrefix, StringComparison.Ordinal ) ) return ShopItemKind.Weapon;
		return null;
	}

	/// <summary>The props the user stood on the table, found by model: the potions anchor the table, everything else has to be near them.</summary>
	private void FindStock()
	{
		_found = true;
		var found = new List<(Prop Prop, ShopItemKind Kind)>();
		foreach ( var prop in Scene.GetAllComponents<Prop>() )
		{
			if ( !prop.IsValid() || prop.GameObject.Flags.HasFlag( GameObjectFlags.NotSaved ) ) continue;
			if ( KindOf( ModelName( prop.Model ) ) is not { } kind ) continue;
			found.Add( (prop, kind) );
		}
		var potions = found.Where( f => f.Kind is ShopItemKind.HealPotion or ShopItemKind.ManaPotion ).ToList();
		if ( potions.Count > 0 )
		{
			var middle = potions.Aggregate( Vector3.Zero, ( sum, f ) => sum + f.Prop.WorldPosition ) / potions.Count;
			found = found.Where( f => f.Prop.WorldPosition.Distance( middle ) <= Radius ).ToList();
		}
		// a stable order, so the save's "scroll 0" is the same scroll every session
		found = found.OrderBy( f => f.Prop.GameObject.Name, StringComparer.Ordinal ).ThenBy( f => f.Prop.WorldPosition.x ).ToList();

		foreach ( var (prop, kind) in found )
		{
			var go = prop.GameObject;
			if ( kind == ShopItemKind.Weapon )
			{
				if ( _hasWeaponSlot ) continue;
				MeasureWeaponSlot( prop );
				go.Enabled = false;
				continue;
			}
			go.Tags.Add( ItemTag );
			Freeze( go );
			var item = go.Components.Create<ShopItem>();
			item.Kind = kind;
			item.Home = go.WorldTransform;
			_items.Add( item );
			if ( kind == ShopItemKind.Scroll ) _scrolls.Add( item );
		}
		Log.Info( $"[Shop] table found: {_items.Count( i => i.Kind == ShopItemKind.HealPotion )} healing, {_items.Count( i => i.Kind == ShopItemKind.ManaPotion )} mana, {_scrolls.Count} scroll slots, weapon slot {(_hasWeaponSlot ? "yes" : "NO")}" );
	}

	/// <summary>Stock never gets kicked off the table: the prop's body stops moving (its collider stays).</summary>
	private static void Freeze( GameObject go )
	{
		var body = go.Components.Get<Rigidbody>( FindMode.EverythingInSelf );
		if ( body.IsValid() ) body.MotionEnabled = false;
	}

	private void MeasureWeaponSlot( Prop prop )
	{
		var model = prop.Model;
		if ( model is null ) return;
		var go = prop.GameObject;
		float scale = go.WorldScale.z;
		_slotRotation = go.WorldRotation;
		_slotCentre = go.WorldPosition + _slotRotation * (model.Bounds.Center * scale);
		_slotBottom = go.WorldPosition.z + LowestZ( model.Bounds, _slotRotation, scale );
		// every sword and staff here is modelled along +Z (WeaponSkins)
		_slotLength = model.Bounds.Size.z * scale;
		_hasWeaponSlot = true;
	}

	/// <summary>The lowest point of a model's box once turned and scaled, relative to its pivot.</summary>
	private static float LowestZ( BBox box, Rotation rotation, float scale )
	{
		float lowest = float.MaxValue;
		foreach ( var corner in box.Corners ) lowest = MathF.Min( lowest, (rotation * (corner * scale)).z );
		return lowest;
	}

	// ------------------------------------------------------------------ the stock

	/// <summary>A new visit: a weapon the player does not own, a different enchantment per scroll slot, every potion back.</summary>
	private void Roll( PlayerInventory inventory )
	{
		_wantRoll = false;
		var weapon = WeaponSkins.CrateDrops.Where( s => !inventory.HasSkin( s.Id ) ).OrderBy( _ => Game.Random.Float() ).FirstOrDefault();
		var scrolls = Scroll.AllModifiers.OrderBy( _ => Game.Random.Float() ).Take( _scrolls.Count ).ToList();
		RunSave.Data.Shop = new ShopSaveData
		{
			Weapon = weapon?.Id ?? "",
			Scrolls = scrolls.Select( s => s.Key ).ToList(),
			ScrollsSold = scrolls.Select( _ => false ).ToList(),
		};
		RunSave.Save();
		_applied = false;
		Log.Info( $"[Shop] restocked: {weapon?.Name ?? "no weapon left to sell"}; {string.Join( ", ", scrolls.Select( s => s.Name ) )}" );
	}

	/// <summary>The table made to look like the save says.</summary>
	private void Apply( PlayerInventory inventory )
	{
		_applied = true;
		var save = RunSave.Data.Shop;
		if ( save is null ) return;
		if ( Focused.IsValid() ) { Focused.Highlight( false ); Focused = null; }

		// a save from before a slot was added or a modifier was cut: those slots are rolled again
		while ( save.Scrolls.Count < _scrolls.Count ) save.Scrolls.Add( "" );
		while ( save.ScrollsSold.Count < save.Scrolls.Count ) save.ScrollsSold.Add( false );
		for ( int i = 0; i < _scrolls.Count; i++ )
		{
			var scroll = Scroll.FromKey( save.Scrolls[i] ).Opened;
			if ( !scroll.IsModifier )
			{
				var taken = save.Scrolls.Select( k => Scroll.FromKey( k ).Opened ).ToHashSet();
				scroll = Scroll.AllModifiers.Where( s => !taken.Contains( s ) ).OrderBy( _ => Game.Random.Float() ).FirstOrDefault();
				save.Scrolls[i] = scroll.Key ?? "";
			}
			_scrolls[i].Scroll = scroll;
			_scrolls[i].ShowRunes();
			SetSold( _scrolls[i], save.ScrollsSold[i] || scroll.IsEmpty );
		}

		int heal = 0, mana = 0;
		foreach ( var item in _items )
		{
			if ( item.Kind == ShopItemKind.HealPotion ) SetSold( item, heal++ < save.HealSold );
			else if ( item.Kind == ShopItemKind.ManaPotion ) SetSold( item, mana++ < save.ManaSold );
		}

		// found in a crate since the table was laid: the slot stays empty until the next visit, like a bought one
		if ( !save.WeaponSold && inventory.HasSkin( save.Weapon ) ) save.WeaponSold = true;
		ShowWeapon( save.WeaponSold ? null : WeaponSkins.Get( save.Weapon ) );
	}

	private static void SetSold( ShopItem item, bool sold )
	{
		if ( !item.IsValid() ) return;
		item.Sold = sold;
		item.GameObject.Enabled = !sold;
		if ( sold ) return;
		// back where the user stood it, and still not a thing to be kicked about
		item.GameObject.WorldTransform = item.Home;
		Freeze( item.GameObject );
	}

	/// <summary>
	/// The weapon for sale lies where the user's sword lay: the same heading, its middle over the sword's middle, its
	/// lowest point on the table. It is the player's own copy of the model (what they will hold), at the size of a
	/// racked weapon, shrunk only when it would hang too far off the table (a long staff).
	/// </summary>
	private void ShowWeapon( WeaponSkin skin )
	{
		if ( _weapon.IsValid() )
		{
			_items.Remove( _weapon );
			_weapon.GameObject.Destroy();
			_weapon = null;
		}
		if ( skin is null || !_hasWeaponSlot ) return;
		var model = WeaponSkins.ModelOf( skin );
		if ( model is null ) return;

		var go = new GameObject( true, "shop weapon" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		go.SetParent( GameObject, false );
		go.Tags.Add( ItemTag );
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;

		float scale = SkinPickup.RackScale;
		float length = model.Bounds.Size.z;
		float longest = _slotLength * MathF.Max( 1f, WeaponFit );
		if ( length * scale > longest && length > 0f ) scale = longest / length;
		// turned in the hand's way (FirstPersonArms), so a blade modelled across X still lies flat
		var rotation = _slotRotation * Rotation.FromYaw( skin.Spin ) * Rotation.FromRoll( skin.Flip ? 180f : 0f );
		var position = _slotCentre - rotation * (model.Bounds.Center * scale);
		position.z = _slotBottom - LowestZ( model.Bounds, rotation, scale );
		go.WorldRotation = rotation;
		go.WorldScale = new Vector3( scale );
		go.WorldPosition = position;

		_weapon = go.Components.Create<ShopItem>();
		_weapon.Kind = ShopItemKind.Weapon;
		_weapon.SkinId = skin.Id;
		_weapon.Home = go.WorldTransform;
		_items.Add( _weapon );
	}

	// ------------------------------------------------------------------ buying (PotionDrinker.TickPickup)

	/// <summary>Owner, every frame in the village: the thing on the table nearest the crosshair, in reach, with a clear line.</summary>
	public static ShopItem UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		var shop = Instance;
		ShopItem best = null;
		if ( shop.IsValid() && shop._applied && PlayerInventory.AtHome )
		{
			float bestScore = float.MaxValue;
			float cosCone = MathF.Cos( PotionPickup.UseCone.Clamp( 1f, 89f ) * 1.3f * MathF.PI / 180f );
			float range = MathF.Max( 10f, PotionPickup.UseRange * 1.2f );
			foreach ( var item in shop._items )
			{
				if ( !item.IsValid() || item.Sold ) continue;
				var centre = item.Centre;
				var to = centre - eye;
				var dist = to.Length;
				if ( dist > range || dist < 1f ) continue;
				var dot = Vector3.Dot( to / dist, forward );
				if ( dot < cosCone ) continue;
				// the table is crowded: what the crosshair is on wins, distance only breaks ties
				var score = (1f - dot) * 4000f + dist * 0.25f;
				if ( score >= bestScore ) continue;
				var tr = scene.Trace.Ray( eye, centre ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
				if ( tr.Hit && tr.Distance < dist - 8f ) continue;
				best = item;
				bestScore = score;
			}
		}
		SetFocused( best );
		// re-applied every frame while in focus: an override set on a scene object that is not built yet is lost
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( ShopItem item )
	{
		if ( Focused == item ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = item;
		if ( item.IsValid() ) item.Highlight( true );
	}

	/// <summary>Why the local player cannot buy it right now ("" when they can): the prompt prints it beside the price.</summary>
	public static string WhyNot( ShopItem item )
	{
		if ( !item.IsValid() || item.Sold ) return "sold";
		switch ( item.Kind )
		{
			case ShopItemKind.HealPotion or ShopItemKind.ManaPotion:
				var potions = PlayerPotions.Local;
				if ( potions.IsValid() && potions.IsFull( PotionKind( item ) ) ) return "full";
				break;
			case ShopItemKind.Scroll:
				var inventory = PlayerInventory.Local;
				if ( inventory.IsValid() && !inventory.CanTake( item.Scroll ) ) return "three copies already";
				break;
		}
		return Purse < item.Price ? "not enough shards" : "";
	}

	private static PlayerPotions.Kind PotionKind( ShopItem item ) => item.Kind == ShopItemKind.HealPotion ? PlayerPotions.Kind.Heal : PlayerPotions.Kind.Mana;

	/// <summary>E on the focused item. True when it was sold: paid from the bank, handed over, gone from the table, saved.</summary>
	public bool TryBuy( ShopItem item )
	{
		var save = RunSave.Data.Shop;
		var inventory = PlayerInventory.Local;
		var potions = PlayerPotions.Local;
		if ( save is null || !inventory.IsValid() || WhyNot( item ) != "" )
		{
			LastRefusedAt = Time.Now;
			return false;
		}
		bool handed = item.Kind switch
		{
			ShopItemKind.Scroll => inventory.Buy( item.Scroll ),
			ShopItemKind.Weapon => WeaponSkins.Get( item.SkinId ) is not null,
			_ => potions.IsValid() && potions.TryAdd( PotionKind( item ) ),
		};
		if ( !handed )
		{
			LastRefusedAt = Time.Now;
			return false;
		}

		RunSave.Data.BankedShards = Math.Max( 0, Purse - item.Price );
		int refillSlot = -1;
		switch ( item.Kind )
		{
			case ShopItemKind.HealPotion: save.HealSold++; potions.Bank(); break;
			case ShopItemKind.ManaPotion: save.ManaSold++; potions.Bank(); break;
			case ShopItemKind.Scroll:
				// the slot is never left empty (user, 2026-09-21): the next enchantment is chosen and saved now, never one
				// lying on the table or the one just bought, and shows up when the keeper points at the empty place
				int slot = _scrolls.IndexOf( item );
				if ( slot >= 0 && slot < save.Scrolls.Count )
				{
					var taken = save.Scrolls.Select( k => Scroll.FromKey( k ).Opened ).ToHashSet();
					var next = Scroll.AllModifiers.Where( s => !taken.Contains( s ) ).OrderBy( _ => Game.Random.Float() ).FirstOrDefault();
					save.Scrolls[slot] = next.Key ?? "";
					save.ScrollsSold[slot] = next.IsEmpty;
					refillSlot = slot;
				}
				break;
			case ShopItemKind.Weapon: save.WeaponSold = true; break;
		}
		RunSave.Save();
		// the skin saves itself; after the shop's own save so the purse it writes is already the paid one
		if ( item.Kind == ShopItemKind.Weapon ) inventory.DiscoverSkin( item.SkinId );

		Log.Info( $"[Shop] sold {item.Label} for {item.Price}; {Purse} shards left in the bank" );
		if ( !string.IsNullOrEmpty( BuySound ) ) VoidMage.Audio.GameAudio.Play( BuySound, item.Centre );
		if ( Focused == item ) SetFocused( null );
		SetSold( item, true );
		if ( refillSlot >= 0 )
		{
			int slot = refillSlot;
			var keeper = Shopkeeper.EnsureOn( Scene );
			if ( keeper.IsValid() ) keeper.Point( PointClip, PointPeak, () => Refill( slot ) );
			else Refill( slot );
		}
		return true;
	}

	/// <summary>A bought scroll's place is filled with what the save says lies there now.</summary>
	private void Refill( int slot )
	{
		var save = RunSave.Data.Shop;
		if ( !this.IsValid() || save is null || slot < 0 || slot >= _scrolls.Count || slot >= save.Scrolls.Count ) return;
		var scroll = Scroll.FromKey( save.Scrolls[slot] ).Opened;
		if ( save.ScrollsSold[slot] || !scroll.IsModifier ) return;
		_scrolls[slot].Scroll = scroll;
		SetSold( _scrolls[slot], false );
		_scrolls[slot].ShowRunes();
		Log.Info( $"[Shop] slot {slot} refilled: {_scrolls[slot].Label}" );
	}

	/// <summary>Dev: lay the table again, as if the player had just come home.</summary>
	[ConCmd( "shop_restock" )]
	public static void RestockCommand()
	{
		if ( !Instance.IsValid() ) { Log.Warning( "[Shop] no shop in this scene" ); return; }
		Restock();
	}
}
