using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The frogs in the player's bag. Unlike potions there is no carry cap: a run can end with any number of them, and
/// every one that comes home is released into the village and kept in the save for good (RunSaveData.Frogs). Dying
/// keeps the bag, like the shards (since 2026-09-20; it used to lose both). Same shape as PlayerShards / PlayerPotions; RunManager puts it on the player.
/// </summary>
[Title( "Player Frogs" ), Category( "Void Mage" ), Icon( "pets" )]
public sealed class PlayerFrogs : Component
{
	/// <summary>Frogs in the bag right now. No cap: the whole point is to hoard them.</summary>
	[Sync] public int Carried { get; set; }

	public static PlayerFrogs Local { get; private set; }

	/// <summary>Time of the last frog caught, for the hotbar's pulse.</summary>
	public float LastCatchAt { get; private set; } = float.NegativeInfinity;

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	/// <summary>Owner: one more frog in the bag.</summary>
	public void Add()
	{
		if ( IsProxy ) return;
		Carried++;
		LastCatchAt = Time.Now;
	}

	/// <summary>
	/// Home safely: the bag is emptied into the village, which both adds them to the saved population and hops them
	/// out on the ground where the player arrived. Returns how many made it home.
	/// </summary>
	public int Bank()
	{
		if ( IsProxy ) return 0;
		int amount = Carried;
		Carried = 0;
		if ( amount > 0 ) VoidMage.Village.VillageFrogs.Release( Scene, amount, WorldPosition );
		return amount;
	}

	/// <summary>Died: the bag is gone (the village keeps what was already released there).</summary>
	public int Lose()
	{
		if ( IsProxy ) return 0;
		int amount = Carried;
		Carried = 0;
		return amount;
	}

	/// <summary>Dev: `run_frogs 12` puts frogs in the local player's bag.</summary>
	[ConCmd( "run_frogs" )]
	public static void GiveCommand( int count = 5 )
	{
		var f = Local;
		if ( !f.IsValid() ) return;
		f.Carried = System.Math.Max( 0, count );
		f.LastCatchAt = Time.Now;
		Log.Info( $"[Frog] carrying {f.Carried}" );
	}
}
