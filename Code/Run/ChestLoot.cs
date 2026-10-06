using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Dungeon;
using VoidMage.Inventory;
using VoidMage.Spells;

namespace VoidMage.Run;

/// <summary>
/// What a broken chest gives. Chests are plain breakables: smashed, their loot pops out like shards. Since 2026-09-20
/// they stand in the SECRET ROOM only (DecorChests takes the rest out) and the floor's scrolls are carried by its
/// monsters (ScrollDrops); a chest is the reward for finding the room (which is itself switched off since later that day: DungeonGenerator.SecretRooms).
///
/// - Every chest: 3-6 shards and a 15% chance of a potion.
/// - The secret room's chests hold ONE guaranteed enchantment scroll between them (`chest_secret_scrolls`). The old
///   floor-chest and crate scrolls are still here as convars, at 0.
/// - What is in the scroll is rolled when the chest breaks, from what the staff can still take. Spell scrolls are the
///   carriers' business.
/// </summary>
public static class ChestLoot
{
	[ConVar( "chest_shards_min" )] public static int ShardsMin { get; set; } = 3;
	[ConVar( "chest_shards_max" )] public static int ShardsMax { get; set; } = 6;
	[ConVar( "chest_potion_chance" )] public static float PotionChance { get; set; } = 0.15f;
	/// <summary>Scrolls in chests outside the secret room: none, there are no such chests any more.</summary>
	[ConVar( "chest_floor_scrolls" )] public static int FloorScrolls { get; set; } = 0;
	/// <summary>Guaranteed scrolls in the secret room's chests.</summary>
	[ConVar( "chest_secret_scrolls" )] public static int SecretScrolls { get; set; } = 1;
	/// <summary>Scrolls hidden in the floor's crates and barrels: none, the monsters carry the floor's scrolls.</summary>
	[ConVar( "crate_floor_scrolls" )] public static int CrateScrolls { get; set; } = 0;
	/// <summary>A broken crate or barrel holding a weapon skin (user, 2026-09-17): the ones no monster carries.</summary>
	[ConVar( "crate_skin_chance" )] public static float CrateSkinChance { get; set; } = 0.05f;
	/// <summary>A weapon rack holding one instead (user, 2026-09-17): grabbed off the rack, not dropped.</summary>
	[ConVar( "rack_skin_chance" )] public static float RackSkinChance { get; set; } = 0.05f;

	/// <summary>
	/// Builder, after the props: pick the chests (and, if the convars ask, the crates) that hold scrolls. Deterministic
	/// from the seed so every peer marks the same containers (the drop itself is host-only). `crates` are the crates and
	/// barrels (BreakableProp.DropsSkins); the ones in the secret room are skipped, its scrolls are the chests'.
	/// </summary>
	public static void Assign( IReadOnlyList<BreakableProp> chests, IReadOnlyList<BreakableProp> crates, int seed )
	{
		var rng = new DungeonRng( DungeonRng.Mix( seed, 0x5C40 ) );
		int placed = Spread( chests.Where( c => !c.InSecretRoom ).ToList(), FloorScrolls, rng );
		int secret = Spread( chests.Where( c => c.InSecretRoom ).ToList(), SecretScrolls, rng );
		int inCrates = Spread( crates.Where( c => !c.InSecretRoom && !c.IsChest ).ToList(), CrateScrolls, rng );
		Log.Info( $"[Loot] {chests.Count} chests, {crates.Count} crates and barrels; scrolls: {secret} in the secret room, {placed} in other chests, {inCrates} in crates" );
	}

	/// <summary>`want` scrolls over `pool`, a container each while there are containers to spare. Returns how many were placed.</summary>
	private static int Spread( List<BreakableProp> pool, int want, DungeonRng rng )
	{
		int placed = 0;
		for ( int k = 0; k < Math.Max( 0, want ) && pool.Count > 0; k++ )
		{
			int i = rng.RandRange( pool.Count );
			var container = pool[i];
			if ( pool.Count > 1 ) pool.RemoveAt( i );
			container.LootScrolls += 1;
			placed++;
		}
		return placed;
	}

	/// <summary>Host, when a chest breaks (BreakableProp.BreakLocal): its shards, maybe a potion, and its scrolls.</summary>
	public static void Open( BreakableProp chest, Vector3 centre, Vector3 push, GameObject parent )
	{
		if ( !Networking.IsHost || !chest.IsValid() ) return;
		var scene = chest.Scene;
		int shards = Game.Random.Int( Math.Min( ShardsMin, ShardsMax ), Math.Max( ShardsMin, ShardsMax ) );
		ShardPickup.Drop( scene, centre, (int)MathF.Round( shards * VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.ShardGain ) ), parent );

		if ( Game.Random.Float() < PotionChance )
		{
			var kind = Game.Random.Int( 0, 1 ) == 0 ? VoidMage.Character.PlayerPotions.Kind.Heal : VoidMage.Character.PlayerPotions.Kind.Mana;
			PotionPickup.SpawnLoose( scene, centre, kind, parent, push );
		}

		DropScrolls( chest, centre, push, parent );
	}

	/// <summary>
	/// Host: whatever scrolls Assign marked this container with come out of the wreck. A chest's, or a crate's or
	/// barrel's (BreakableProp.BreakLocal calls it for any non-chest that was marked).
	/// </summary>
	public static void DropScrolls( BreakableProp container, Vector3 centre, Vector3 push, GameObject parent )
	{
		if ( !Networking.IsHost || !container.IsValid() || container.LootScrolls <= 0 ) return;
		var scene = container.Scene;
		for ( int k = 0; k < container.LootScrolls; k++ )
		{
			var scroll = PlayerInventory.Local is { } inventory ? inventory.RollEnchantment() : Scroll.Empty;
			if ( scroll.IsEmpty ) continue;
			ScrollPickup.Drop( scene, centre + Vector3.Up * 6f, scroll, parent, push );
			Log.Info( $"[Loot] {(container.IsChest ? "chest" : "crate")} scroll: {scroll.Name}" );
		}
	}

	/// <summary>
	/// Host, when a crate or barrel breaks (BreakableProp.DropsSkins): 1% of the time it held a weapon. Only the skins
	/// nobody carries are in here - a monster's weapon is unlocked by killing one holding it - and only ones not found
	/// yet, so the roll is never spent on something the player already owns.
	/// </summary>
	public static void CrateSkin( Scene scene, Vector3 centre, Vector3 push, GameObject parent )
	{
		if ( !Networking.IsHost || CrateSkinChance <= 0f || Game.Random.Float() >= CrateSkinChance ) return;
		var inventory = PlayerInventory.Local;
		var pool = WeaponSkins.CrateDrops
			.Where( s => !(inventory.IsValid() && inventory.HasSkin( s.Id )) && !SkinPickup.AlreadyOut( s.Id ) )
			.ToList();
		if ( pool.Count == 0 ) return;
		var skin = pool[Game.Random.Int( 0, pool.Count - 1 )];
		SkinPickup.Drop( scene, centre + Vector3.Up * 6f, skin.Id, parent, push );
		Log.Info( $"[Loot] crate weapon: {skin.Name}" );
	}

	/// <summary>
	/// Builder: a weapon left in a rack, 5% per rack (user, 2026-09-17). `bounds` is the rack's world box and `yaw` the
	/// way it faces the room; the weapon stands on the floor at the front of the rack, leaning back into it, and is
	/// grabbed with E - it does not float, glow or light anything. Returns true if one was placed.
	///
	/// Rolled off the floor's seed so every peer places the same weapon, and skipped for skins already found (solo:
	/// the inventory it asks is the local player's).
	/// </summary>
	public static bool RackSkin( Scene scene, BBox bounds, float yaw, GameObject parent, DungeonRng rng )
	{
		float Spread( float a, float b ) => a + (float)rng.NextDouble() * (b - a);
		if ( RackSkinChance <= 0f || rng.NextDouble() >= RackSkinChance ) return false;
		var inventory = PlayerInventory.Local;
		var pool = WeaponSkins.CrateDrops
			.Where( s => !(inventory.IsValid() && inventory.HasSkin( s.Id )) && !SkinPickup.AlreadyOut( s.Id ) )
			.ToList();
		if ( pool.Count == 0 ) return false;
		var skin = pool[rng.RandRange( pool.Count )];
		var model = WeaponSkins.ModelOf( skin );
		var facing = Rotation.FromYaw( yaw );
		// leaning back into the rack, turned a little so it is not dead flat to the wall
		var rotation = facing * Rotation.FromPitch( -RackLeanDegrees ) * Rotation.FromRoll( Spread( -8f, 8f ) );
		// stood on the floor the rack stands on, a little in front of its face and off its centre
		var at = bounds.Center.WithZ( bounds.Mins.z )
			+ facing.Forward * (bounds.Size.Length * 0.06f)
			+ facing.Right * Spread( -3f, 3f );
		// the model's own lowest point put on the floor, at the size the pickup will be drawn at
		if ( model is not null ) at -= rotation * new Vector3( 0f, 0f, model.Bounds.Mins.z * SkinPickup.RackScale );
		SkinPickup.Rack( scene, at, rotation, skin.Id, parent );
		Log.Info( $"[Loot] rack weapon: {skin.Name}" );
		return true;
	}

	/// <summary>How far a racked weapon leans back into the rack.</summary>
	private const float RackLeanDegrees = 12f;
}
