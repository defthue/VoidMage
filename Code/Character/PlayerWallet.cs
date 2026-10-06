using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The gold the player carries. Until the inventory exists this is a bare number: Avarice reads it, the dev panel
/// and `spell_gold` write it. Owner-authoritative; synced so the host can price things later.
/// </summary>
[Title( "Player Wallet" ), Category( "Void Mage" ), Icon( "paid" )]
public sealed class PlayerWallet : Component
{
	[Sync] public int Gold { get; set; }

	public static PlayerWallet Local { get; private set; }

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
		if ( IsProxy ) return;
		Gold = Math.Max( 0, Gold + amount );
	}

	public void Set( int amount )
	{
		if ( IsProxy ) return;
		Gold = Math.Max( 0, amount );
	}
}
