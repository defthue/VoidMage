using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The potions the player carries: healing (DungeonPack potion 07) and mana (potion 06), picked up with E off the
/// dungeon's tables (PotionPickup), drunk from the 1 / 2 quick slots (PotionDrinker), at most CarryMax of each.
/// A won run brings them home (kept in the save); a death or an abandoned run loses them, unlike the shards. Same shape as
/// PlayerShards; RunManager adds this to the player it spawns.
/// </summary>
[Title( "Player Potions" ), Category( "Void Mage" ), Icon( "science" )]
public sealed class PlayerPotions : Component
{
	public enum Kind { Heal, Mana }

	/// <summary>Most potions of one kind the player can carry; a pickup past it is refused.</summary>
	[ConVar( "potion_carry_max" )] public static int CarryMax { get; set; } = 3;

	[Sync] public int Heal { get; set; }
	[Sync] public int Mana { get; set; }

	/// <summary>What a fresh player carries of each (the run's save overrides it in the village).</summary>
	[Property] public int StartCount { get; set; } = 2;

	/// <summary>What the player respawns with of each after a death.</summary>
	[Property] public int RespawnCount { get; set; } = 1;

	public static PlayerPotions Local { get; private set; }

	/// <summary>Time of the last pickup, for the hotbar's pulse.</summary>
	public float LastPickupAt { get; private set; } = float.NegativeInfinity;
	public Kind LastPickupKind { get; private set; }
	/// <summary>Time of the last potion drunk (consumed), for the hotbar's pulse.</summary>
	public float LastDrinkAt { get; private set; } = float.NegativeInfinity;
	public Kind LastDrinkKind { get; private set; }

	/// <summary>The save already gave this player its potions (RunManager calls LoadSaved right after the clone).</summary>
	private bool _loaded;

	protected override void OnStart()
	{
		if ( IsProxy ) return;
		Local = this;
		// OnStart runs a frame after the spawner's LoadSaved: only a player nobody loaded gets the fresh two
		if ( _loaded ) return;
		Set( Kind.Heal, StartCount );
		Set( Kind.Mana, StartCount );
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	public int Count( Kind kind ) => kind == Kind.Heal ? Heal : Mana;

	/// <summary>Most of one kind this player can carry: the convar plus the run's boons (Deep Pockets); 0 under Vow of Emptiness.</summary>
	public int Capacity
	{
		get
		{
			var boons = VoidMage.Boons.PlayerBoons.Find( GameObject );
			if ( boons.IsValid() && boons.Has( VoidMage.Boons.BoonStat.NoPotions ) ) return 0;
			return Math.Max( 0, CarryMax + (boons.IsValid() ? (int)boons.Get( VoidMage.Boons.BoonStat.PotionCapacity ) : 0) );
		}
	}

	public bool IsFull( Kind kind ) => Count( kind ) >= Capacity;

	/// <summary>Owner: one more of `kind`; false (nothing added) when the slot is full.</summary>
	public bool TryAdd( Kind kind )
	{
		if ( IsProxy || IsFull( kind ) ) return false;
		Set( kind, Count( kind ) + 1 );
		LastPickupAt = Time.Now;
		LastPickupKind = kind;
		return true;
	}

	/// <summary>Owner: one of `kind` consumed; false when there is none.</summary>
	public bool TryTake( Kind kind )
	{
		if ( IsProxy || Count( kind ) <= 0 ) return false;
		Set( kind, Count( kind ) - 1 );
		LastDrinkAt = Time.Now;
		LastDrinkKind = kind;
		return true;
	}

	private void Set( Kind kind, int value )
	{
		value = Math.Clamp( value, 0, Capacity );
		if ( kind == Kind.Heal ) Heal = value; else Mana = value;
	}

	/// <summary>Owner: every slot topped up, both kinds (Deep Pockets). Counts as a pickup so the hotbar flashes.</summary>
	public void Fill()
	{
		if ( IsProxy ) return;
		var cap = Capacity;
		if ( cap <= 0 || (Heal >= cap && Mana >= cap) ) return;
		Set( Kind.Heal, cap );
		Set( Kind.Mana, cap );
		LastPickupAt = Time.Now;
	}

	/// <summary>Owner: Vow of Emptiness. Every bottle carried is gone (the save is untouched; the run's end decides that).</summary>
	public void Discard()
	{
		if ( IsProxy ) return;
		Heal = 0; Mana = 0;
	}

	/// <summary>Owner: the capacity shrank (boons cleared): anything past it is dropped.</summary>
	public void ClampToCapacity()
	{
		if ( IsProxy ) return;
		Set( Kind.Heal, Heal );
		Set( Kind.Mana, Mana );
	}

	/// <summary>Home safely: what is carried goes into the save with the player.</summary>
	public void Bank()
	{
		if ( IsProxy ) return;
		var save = VoidMage.Run.RunSave.Data;
		save.HealPotions = Heal;
		save.ManaPotions = Mana;
	}

	/// <summary>Death: everything carried is gone; the player wakes up at home with RespawnCount of each.</summary>
	public void Lose()
	{
		if ( IsProxy ) return;
		Set( Kind.Heal, RespawnCount );
		Set( Kind.Mana, RespawnCount );
		Bank();
	}

	/// <summary>A death that loses nothing (in the village itself): never respawn with less than RespawnCount of each.</summary>
	public void TopUpForRespawn()
	{
		if ( IsProxy ) return;
		Set( Kind.Heal, Math.Max( Heal, RespawnCount ) );
		Set( Kind.Mana, Math.Max( Mana, RespawnCount ) );
		Bank();
	}

	/// <summary>Village spawn: pick the carried potions back up from the save.</summary>
	public void LoadSaved()
	{
		if ( IsProxy ) return;
		var save = VoidMage.Run.RunSave.Data;
		Set( Kind.Heal, save.HealPotions );
		Set( Kind.Mana, save.ManaPotions );
		_loaded = true;
	}

	/// <summary>Dev: `run_potions 3 3` hands the local player potions.</summary>
	[ConCmd( "run_potions" )]
	public static void GiveCommand( int heal = 3, int mana = 3 )
	{
		var p = Local;
		if ( !p.IsValid() ) return;
		p.Set( Kind.Heal, heal );
		p.Set( Kind.Mana, mana );
		p.LastPickupAt = Time.Now;
	}
}
