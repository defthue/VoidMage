using System;
using Sandbox;

namespace VoidMage.Run;

/// <summary>What survives between game sessions: the shards banked in the village and whether the fixed first floor has been played.</summary>
public sealed class RunSaveData
{
	public int BankedShards { get; set; }
	/// <summary>Set when the player's first run ends (home or death). Until then floor 1 is the fixed introduction seed.</summary>
	public bool FirstRunDone { get; set; }
	public int RunsPlayed { get; set; }
	public int DeepestFloor { get; set; }
	/// <summary>The outfit chosen in the wardrobe (AppearanceData json); empty until the wardrobe was used.</summary>
	public string Appearance { get; set; } = "";
	/// <summary>Potions carried home (PlayerPotions): kept between runs, gone on a death. A new player starts with two of each.</summary>
	public int HealPotions { get; set; } = 2;
	public int ManaPotions { get; set; } = 2;
	/// <summary>Frogs living in the village (VillageFrogs): every one carried out of the dungeon alive, kept for good. Only the count is saved; where they sit is not.</summary>
	public int Frogs { get; set; }
	/// <summary>The inventory's first-open tour was read to the end or skipped (SpellLoadout). Closing the window mid-tour leaves it for the next open.</summary>
	public bool InventoryTourDone { get; set; }
	/// <summary>The inventory has been opened with a scroll on the staff: until then a taken scroll's reveal says "Press Tab" under its caption (ScrollReveal).</summary>
	public bool StaffLookedAt { get; set; }
	/// <summary>Scrolls, the library and the weapon skins (PlayerInventory). Null until the inventory first saves: a new player.</summary>
	public InventorySaveData Inventory { get; set; }
	/// <summary>What is on the shopkeeper's table this visit (VillageShop). Null until the shop first stocks: a new player.</summary>
	public ShopSaveData Shop { get; set; }
}

/// <summary>
/// The shop table as it stands: rolled when the player comes home from the dungeon, kept until the next return (so
/// closing the game never re-rolls it and a bought slot stays empty).
/// </summary>
public sealed class ShopSaveData
{
	/// <summary>The weapon slot's skin id ("" = nothing left to sell).</summary>
	public string Weapon { get; set; } = "";
	public bool WeaponSold { get; set; }
	/// <summary>The scroll slots' keys, in table order.</summary>
	public System.Collections.Generic.List<string> Scrolls { get; set; } = new();
	public System.Collections.Generic.List<bool> ScrollsSold { get; set; } = new();
	public int HealSold { get; set; }
	public int ManaSold { get; set; }
}

/// <summary>
/// The inventory as it stood the last time the player was home. The staff, satchel and library are only ever written
/// in the village, so this doubles as the run's snapshot: a death restores the staff from it. What was discovered and
/// the skins are written the moment they happen, in or out of a run (knowledge and cosmetics are never lost).
/// </summary>
public sealed class InventorySaveData
{
	public string SpellA { get; set; } = "";
	public string SpellB { get; set; } = "";
	/// <summary>Scroll keys, one per modifier slot ("" = empty).</summary>
	public System.Collections.Generic.List<string> Modifiers { get; set; } = new();
	public System.Collections.Generic.List<string> Satchel { get; set; } = new();
	public System.Collections.Generic.List<ShelfSaveData> Library { get; set; } = new();
	/// <summary>Spell ids unlocked for good (Fireball always, whether listed or not).</summary>
	public System.Collections.Generic.List<string> UnlockedSpells { get; set; } = new();
	/// <summary>Enchantment keys found at least once (the index).</summary>
	public System.Collections.Generic.List<string> Discovered { get; set; } = new();
	public System.Collections.Generic.List<string> Skins { get; set; } = new();
	/// <summary>Kills of the monsters carrying a weapon not owned yet, by skin id (PlayerInventory.SkinKillsNeeded unlocks it).</summary>
	public System.Collections.Generic.Dictionary<string, int> SkinKills { get; set; } = new();
	public string SwordSkin { get; set; } = "";
	public string StaffSkin { get; set; } = "";
}

public sealed class ShelfSaveData
{
	public string Scroll { get; set; } = "";
	public int Count { get; set; }
}

/// <summary>
/// The per-player save, a small JSON file in the game's data folder (FileSystem.Data, per Steam account). Loaded
/// once by RunManager, written whenever a run ends. Everything here is village meta-progression; nothing about a
/// run in progress is saved (a run is lost when the game closes, like a death).
/// </summary>
public static class RunSave
{
	public const string Path = "voidmage/run.json";

	public static RunSaveData Data { get; private set; } = new();
	private static bool _loaded;

	public static void Load()
	{
		if ( _loaded ) return;
		_loaded = true;
		try
		{
			if ( FileSystem.Data.FileExists( Path ) ) Data = FileSystem.Data.ReadJson<RunSaveData>( Path ) ?? new RunSaveData();
			Log.Info( $"[Run] save loaded: {Data.BankedShards} shards banked, {Data.RunsPlayed} runs, first run {(Data.FirstRunDone ? "done" : "pending")}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Run] could not read {Path}: {e.Message}; starting fresh" );
			Data = new RunSaveData();
		}
	}

	public static void Save()
	{
		try
		{
			FileSystem.Data.CreateDirectory( "voidmage" );
			FileSystem.Data.WriteJson( Path, Data );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Run] could not write {Path}: {e.Message}" );
		}
	}

	/// <summary>Dev: wipe the save (banked shards, the first-run flag), so the next run is a new player's again.</summary>
	[ConCmd( "run_save_reset" )]
	public static void ResetCommand()
	{
		Data = new RunSaveData();
		_loaded = true;
		Save();
		Log.Info( "[Run] save reset" );
	}
}
