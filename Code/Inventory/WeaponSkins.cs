using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Inventory;

/// <summary>A cosmetic sword or staff: found once, chosen in the Armory tab, kept for good.</summary>
public sealed class WeaponSkin
{
	public string Id { get; init; }
	public string Name { get; init; }
	public HandSide Hand { get; init; }
	/// <summary>The pack's own model this skin is built from (NPCs and props keep using it, untouched).</summary>
	public string Source { get; init; }
	/// <summary>
	/// What the player holds: a player-only copy in Assets/PlayerWeapons (tools/gen_player_weapons.py: its own FBX with a clean
	/// outline shell, its own import_scale), so resizing a skin never touches the pack or the monsters. The starting sword and
	/// staff are already player-only models.
	/// </summary>
	public string ModelPath => Starter ? Source : $"playerweapons/{Id}.vmdl";
	/// <summary>Staffs: grip to gem along the shaft (inches), where spells leave from. Null keeps FirstPersonArms.StaffTipLength.</summary>
	public float? TipLength { get; init; }
	/// <summary>Degrees the model is turned about its own blade/shaft axis in the hand: a blade whose crossguard was modelled across X instead of Y.</summary>
	public float Spin { get; init; }
	/// <summary>Modelled upside down (the head at -Z): turned end over end in the hand.</summary>
	public bool Flip { get; init; }
	/// <summary>Model units the model slides along its (flipped) shaft so the hand holds it where the default staff is held: for staffs pivoted in their middle.</summary>
	public float Shift { get; init; }
	/// <summary>Owned from the start (the weapons the player walks in with).</summary>
	public bool Starter { get; init; }
	/// <summary>Carried by a monster, for the Armory card ("Carried by skeleton slaves"); <see cref="Carriers"/> is what actually unlocks it.</summary>
	public string CarriedBy { get; init; } = "";
	/// <summary>
	/// The NPC kinds that carry this weapon (SkeletonSpawner.Kinds names): killing one unlocks the skin (user,
	/// 2026-09-17). A kind that rolls two swords lists both its weapons, told apart by <see cref="CarrierSword"/>.
	/// </summary>
	public string[] Carriers { get; init; } = System.Array.Empty<string>();
	/// <summary>Which of the carrier's weapon choices this is (SkeletonNpc.SwordIndex; 0 for a kind with only one).</summary>
	public int CarrierSword { get; init; }

	/// <summary>Nobody carries it: it comes out of a crate or barrel (1%), or off the shop's shelf.</summary>
	public bool FromCrates => !Starter && Carriers.Length == 0;

	public bool IsSword => Hand == HandSide.Left;
}

/// <summary>
/// The skins. Every vmdl here is at import_scale 0.2, the size the default sword and staff use (user, 2026-09-17).
/// Pivots measured in Blender from the FBXs: swords have the origin at the guard and the blade up +Z; staffs have the
/// origin where the hand holds them. Staff tip lengths are the FBX's top (cm) × 0.2 × 0.78, the fraction of the default
/// gem staff's top where its gem sits (StaffTipLength 20 on the prefab). The Realms and Dark Fantasy staffs are pivoted in
/// their middle: Centred slides them so the hand sits 34% up the shaft, where it holds the gem staff, and the Dark
/// Fantasy staff's head is at its bottom (Flip). Every staff's head end measured in Blender (2026-09-17).
/// </summary>
public static class WeaponSkins
{
	public const string DefaultSword = "sword_ornate";
	public const string DefaultStaff = "staff_gem";

	/// <summary>Grip-to-gem of the default staff against its model's top, used to place every other staff's muzzle.</summary>
	private const float TipOfTop = 20f / (127.9f * 0.2f);
	private static float Tip( float topCm ) => topCm * 0.2f * TipOfTop;
	/// <summary>Where the default gem staff is held, as a fraction of its length from the butt (z -66.5 .. 127.9 cm, grip at 0).</summary>
	private const float GripFraction = 66.5f / 194.4f;

	/// <summary>A staff pivoted in its middle (FBX z from `lowCm` to `highCm`, head up after any flip): its shift and tip.</summary>
	private static WeaponSkin Centred( string id, string name, string model, float lowCm, float highCm, bool flip = false )
	{
		if ( flip ) (lowCm, highCm) = (-highCm, -lowCm);
		float grip = lowCm + (highCm - lowCm) * GripFraction;
		return new WeaponSkin
		{
			Id = id, Name = name, Hand = HandSide.Right, Source = model, Flip = flip,
			Shift = -grip * 0.2f,
			TipLength = (highCm - grip) * 0.2f * TipOfTop,
		};
	}

	// who carries what (tools/skeleton_kinds.py: the weapons are body groups on the skeleton's own model, so a kind and
	// its sword index name the weapon). Declared before All: static initialisers run in the order they are written.
	/// <summary>Every kind built on the slave's body: the broken sword (0) and the whole one (1).</summary>
	private static readonly string[] SlaveKinds = { "slave", "corpse", "df_skeleton", "df_flesh" };
	/// <summary>The armored tiers: greatsword (0) and curved greatsword (1).</summary>
	private static readonly string[] SoldierKinds = { "soldier_01", "soldier_02", "df_light", "df_heavy" };
	/// <summary>The forge-hold skeletons: FantasyKingdom swords 03 (0) and 04 (1).</summary>
	private static readonly string[] RealmsKinds = { "realms_01", "realms_02", "realms_03" };
	/// <summary>The swordsman's two skins, both on the Dark Fantasy blade.</summary>
	private static readonly string[] SwordsmanKinds = { "spirit_demon", "dark_lord" };

	public static readonly WeaponSkin[] All =
	{
		new() { Id = DefaultSword, Name = "Ornate Blade", Hand = HandSide.Left, Source = FirstPersonArms.DefaultSword, Starter = true },
		new() { Id = "sword_chapel", Name = "Chapel Sword", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_ornate_sword_01.vmdl" },
		new() { Id = "sword_warden", Name = "Warden's Straightsword", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_straightsword_01.vmdl" },
		new() { Id = "sword_forge", Name = "Forge-Hold Blade", Hand = HandSide.Left, Source = "dungeonrealms/sm_wep_sword_medium_03.vmdl" },
		new() { Id = "sword_kingsguard", Name = "Kingsguard Sword", Hand = HandSide.Left, Source = "fantasykingdom/sm_wep_sword_01.vmdl" },
		new() { Id = "sword_sidearm", Name = "Knight's Sidearm", Hand = HandSide.Left, Source = "fantasykingdom/sm_wep_sword_14.vmdl" },
		new() { Id = "sword_gaoler", Name = "Gaoler's Blade", Hand = HandSide.Left, Source = "darkfantasy/sm_wep_sword_03.vmdl" },

		// the monsters' own swords (tools/skeleton_kinds.py): each is unlocked by killing one that carries it
		new() { Id = "sword_broken", Name = "Broken Oath", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_brokensword_01.vmdl", CarriedBy = "Skeleton slaves", Carriers = SlaveKinds, CarrierSword = 0 },
		new() { Id = "sword_slave", Name = "Slave's Sword", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_unbrokensword_01.vmdl", CarriedBy = "Skeleton slaves", Carriers = SlaveKinds, CarrierSword = 1 },
		new() { Id = "sword_soldier", Name = "Soldier's Greatsword", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_greatsword_01.vmdl", CarriedBy = "Skeleton soldiers", Carriers = SoldierKinds, CarrierSword = 0 },
		new() { Id = "sword_soldier_curved", Name = "Curved Greatsword", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_greatsword_curved_01.vmdl", CarriedBy = "Skeleton soldiers", Carriers = SoldierKinds, CarrierSword = 1 },
		new() { Id = "sword_soul", Name = "Soul Dagger", Hand = HandSide.Left, Source = "dungeonpack/sm_wep_dagger_07.vmdl", CarriedBy = "Tormented souls", Carriers = new[] { "tormented_soul" } },
		new() { Id = "sword_grave", Name = "Grave Sword", Hand = HandSide.Left, Source = "fantasykingdom/sm_wep_sword_03.vmdl", CarriedBy = "Realms skeletons", Carriers = RealmsKinds, CarrierSword = 0 },
		new() { Id = "sword_grave_long", Name = "Grave Longsword", Hand = HandSide.Left, Source = "fantasykingdom/sm_wep_sword_04.vmdl", CarriedBy = "Realms skeletons", Carriers = RealmsKinds, CarrierSword = 1 },
		new() { Id = "sword_knight", Name = "Forge-Hold Longsword", Hand = HandSide.Left, Source = "dungeonrealms/sm_wep_sword_medium_08.vmdl", CarriedBy = "Undead knights", Carriers = new[] { "undead_knight" } },
		// modelled with the crossguard across X: turned a quarter so it sits in the fist like the rest
		new() { Id = "sword_dark_lord", Name = "Dark Lord's Blade", Hand = HandSide.Left, Source = "darkfantasy/sm_wep_sword_01.vmdl", Spin = 90f, CarriedBy = "The swordsmen", Carriers = SwordsmanKinds },

		new() { Id = DefaultStaff, Name = "Gem Staff", Hand = HandSide.Right, Source = FirstPersonArms.DefaultStaff, Starter = true },
		// FantasyKingdom (toonified 2026-09-17 with tools/toonify_adventure.py, TOON_BASE_MAT=FantasyKingdomShader)
		new() { Id = "staff_kingdom_02", Name = "Court Wand", Hand = HandSide.Right, Source = "fantasykingdom/sm_wep_staff_02.vmdl", TipLength = Tip( 109.2f ) },
		new() { Id = "staff_kingdom_05", Name = "Moonspire Staff", Hand = HandSide.Right, Source = "fantasykingdom/sm_wep_staff_05.vmdl", TipLength = Tip( 114.8f ) },
		new() { Id = "staff_kingdom_07", Name = "Royal Sceptre", Hand = HandSide.Right, Source = "fantasykingdom/sm_wep_staff_07.vmdl", TipLength = Tip( 117.0f ) },
		new() { Id = "staff_kingdom_09", Name = "Hedge Wizard's Staff", Hand = HandSide.Right, Source = "fantasykingdom/sm_wep_staff_09.vmdl", TipLength = Tip( 103.8f ) },
		// Dungeon Realms 1-6, pivoted in the middle
		Centred( "staff_realms_01", "Forge Rod", "dungeonrealms/sm_wep_staff_01.vmdl", -77.4f, 77.4f ),
		Centred( "staff_realms_02", "Runecarver's Staff", "dungeonrealms/sm_wep_staff_02.vmdl", -83.9f, 83.9f ),
		Centred( "staff_realms_03", "Great Antler", "dungeonrealms/sm_wep_staff_03.vmdl", -127.7f, 127.7f ),
		Centred( "staff_realms_04", "Deepstone Staff", "dungeonrealms/sm_wep_staff_04.vmdl", -122.9f, 122.9f ),
		Centred( "staff_realms_05", "Emberkeeper", "dungeonrealms/sm_wep_staff_05.vmdl", -101.4f, 101.4f ),
		Centred( "staff_realms_06", "Mountain King's Staff", "dungeonrealms/sm_wep_staff_06.vmdl", -157.7f, 138.9f ),
		// Dark Fantasy: middle pivot, head at the bottom
		Centred( "staff_darkfantasy", "Bone Totem", "darkfantasy/sm_wep_staff_01.vmdl", -88.1f, 88.1f, flip: true ),
		// Dark Fortress 1 and 3 (not 2): the pack's own grip pivots
		new() { Id = "staff_fortress", Name = "Fortress Rod", Hand = HandSide.Right, Source = "darkfortress/sm_wep_staff_01.vmdl", TipLength = Tip( 155.9f ) },
		new() { Id = "staff_crook", Name = "Warden's Crook", Hand = HandSide.Right, Source = "darkfortress/sm_wep_staff_03.vmdl", TipLength = Tip( 133.0f ) },
	};

	private static readonly Dictionary<string, WeaponSkin> _byId = All.ToDictionary( s => s.Id );
	private static readonly Dictionary<string, Model> _models = new();

	public static WeaponSkin Get( string id ) => string.IsNullOrEmpty( id ) ? null : _byId.GetValueOrDefault( id );

	public static IEnumerable<WeaponSkin> For( HandSide hand ) => All.Where( s => s.Hand == hand );

	/// <summary>
	/// The weapon an NPC was carrying, from its kind name and sword index (SkeletonNpc.KindName / SwordIndex): killing
	/// it unlocks this skin. Null for everything that carries nothing (the monsters, the Colossus).
	/// </summary>
	public static WeaponSkin CarriedWeapon( string kindName, int swordIndex )
	{
		if ( string.IsNullOrEmpty( kindName ) ) return null;
		return All.FirstOrDefault( s => s.CarrierSword == swordIndex && s.Carriers.Contains( kindName ) );
	}

	/// <summary>The skins nobody carries: a 1% find in a crate or barrel, or bought in the shop (user, 2026-09-17).</summary>
	public static IEnumerable<WeaponSkin> CrateDrops => All.Where( s => s.FromCrates );

	public static string DefaultFor( HandSide hand ) => hand == HandSide.Left ? DefaultSword : DefaultStaff;

	/// <summary>The skin's model, loaded once; null when it is missing or failed to compile (the caller falls back).</summary>
	public static Model ModelOf( WeaponSkin skin )
	{
		if ( skin is null ) return null;
		if ( !_models.TryGetValue( skin.ModelPath, out var model ) )
		{
			model = Model.Load( skin.ModelPath );
			if ( model is null || model.IsError )
			{
				Log.Warning( $"[Inventory] skin {skin.Id}: {skin.ModelPath} did not load" );
				model = null;
			}
			_models[skin.ModelPath] = model;
		}
		return model;
	}

	private static readonly Dictionary<string, Texture> _icons = new();

	/// <summary>
	/// The Armory's catalogue picture (tools/render_weapon_icons.py, baked from the skin's own FBX and materials):
	/// the weapon itself once found, its bare silhouette until then.
	/// </summary>
	public static Texture Icon( WeaponSkin skin, bool found )
	{
		if ( skin is null ) return null;
		var path = $"ui/weapons/{skin.Id}{(found ? "" : "_shade")}.png";
		if ( !_icons.TryGetValue( path, out var tex ) )
		{
			tex = Texture.Load( path );
			_icons[path] = tex;
		}
		return tex;
	}

	/// <summary>By id or name, prefix allowed.</summary>
	public static WeaponSkin Find( string text )
	{
		var t = (text ?? "").Trim().ToLowerInvariant();
		if ( t.Length == 0 ) return null;
		return All.FirstOrDefault( s => s.Id == t || s.Name.ToLowerInvariant() == t )
			?? All.FirstOrDefault( s => s.Id.StartsWith( t ) || s.Id.Contains( t ) || s.Name.ToLowerInvariant().StartsWith( t ) );
	}
}
