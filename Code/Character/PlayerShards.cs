using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The void shards the player carries through a run: picked up from dead enemies and broken props, banked in the
/// village when the run ends, however it ends (a death keeps them since 2026-09-20). The banked total lives in the
/// save (RunSave), not here. The bank never passes <see cref="Cap"/> (the bag is free to, during a run; what does not
/// fit when it is banked is gone): shards only pile up now, so the purse has a top and the shop's prices pace it. RunManager adds this to the player it spawns.
/// </summary>
[Title( "Player Shards" ), Category( "Void Mage" ), Icon( "diamond" )]
public sealed class PlayerShards : Component
{
	/// <summary>The most shards a player can own, bank and bag together (user, 2026-09-21: 9999, it was a hard 999).</summary>
	[ConVar( "shard_cap" )] public static int Cap { get; set; } = 9999;

	[Sync] public int Carried { get; set; }

	/// <summary>Bank and bag together are at the cap: what is picked up from here on only serves this run (rerolls, Avarice).</summary>
	public bool Full => Carried + VoidMage.Run.RunSave.Data.BankedShards >= Cap;

	public static PlayerShards Local { get; private set; }

	/// <summary>Time of the last pickup, for the HUD counter's pulse.</summary>
	public float LastPickupAt { get; private set; } = float.NegativeInfinity;

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	public void Add( int amount )
	{
		if ( IsProxy || amount == 0 ) return;
		// NOT capped here: Avarice feeds on what is carried and a boon reroll spends it, and both would die with a
		// full bank. The cap bites when the bag is banked (what does not fit is gone).
		Carried = Math.Max( 0, Carried + amount );
		if ( amount > 0 ) LastPickupAt = Time.Now;
	}

	/// <summary>Home safely: everything carried goes into the save. Returns what was banked.</summary>
	public int Bank()
	{
		if ( IsProxy ) return 0;
		int amount = Carried;
		Carried = 0;
		if ( amount > 0 )
		{
			// never past the cap, and never DOWN to it either (a save from before the cap keeps what it had)
			int banked = VoidMage.Run.RunSave.Data.BankedShards;
			VoidMage.Run.RunSave.Data.BankedShards = Math.Min( Math.Max( Cap, banked ), banked + amount );
			VoidMage.Run.RunSave.Save();
		}
		return amount;
	}

	/// <summary>Death: everything carried is gone. Returns what was lost.</summary>
	public int Lose()
	{
		if ( IsProxy ) return 0;
		int amount = Carried;
		Carried = 0;
		return amount;
	}

	/// <summary>Dev: `run_shards 25` hands the local player shards to test the bank path.</summary>
	[ConCmd( "run_shards" )]
	public static void GiveCommand( int amount = 10 )
	{
		Local?.Add( amount );
	}
}
