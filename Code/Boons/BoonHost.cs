using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Boons;

/// <summary>
/// The boons the host pays out, because the host is where damage comes off a bar and where things die: life steal
/// (capped at PlayerBoons.MaxLifeStealPerSecond per player, so a swarm or a laser cannot make anyone immortal), the
/// health and mana a kill is worth, and the extra shards Hoarder finds. Payments go through the owner-guarded RPCs.
/// </summary>
public static class BoonHost
{
	private static readonly Dictionary<Guid, (float windowStart, float healed)> _lifeSteal = new();

	/// <summary>Host: `dealt` just came off an enemy's bar from this hit.</summary>
	public static void OnDamageDealt( in DamageRouter.Hit hit, float dealt )
	{
		if ( dealt <= 0f || !hit.Attacker.IsValid() ) return;
		var boons = PlayerBoons.Find( hit.Attacker );
		if ( boons is null ) return;
		var fraction = boons.Get( BoonStat.LifeSteal );
		if ( DamageRouter.IsSword( hit.Kind ) ) fraction += boons.Get( BoonStat.SwordLifeSteal );
		if ( fraction <= 0f ) return;
		var health = PlayerHealth.Find( hit.Attacker );
		if ( health is null ) return;

		// a rolling one-second budget per attacker
		var key = hit.Attacker.Id;
		var now = Time.Now;
		if ( !_lifeSteal.TryGetValue( key, out var bucket ) || now - bucket.windowStart >= 1f ) bucket = (now, 0f);
		var amount = MathF.Min( dealt * fraction, PlayerBoons.MaxLifeStealPerSecond - bucket.healed );
		if ( amount <= 0f ) { _lifeSteal[key] = bucket; return; }
		bucket.healed += amount;
		_lifeSteal[key] = bucket;
		health.Refund( amount, "life steal" );
	}

	/// <summary>Host: a hostile died to this blow. Mending and Arcane Might pay per kill, whatever landed it.</summary>
	public static void OnKill( in KillContext ctx )
	{
		if ( !ctx.Attacker.IsValid() ) return;
		var boons = PlayerBoons.Find( ctx.Attacker );
		if ( boons is null ) return;
		var heal = boons.Get( BoonStat.HealthPerKill );
		if ( heal > 0f ) PlayerHealth.Find( ctx.Attacker )?.Refund( heal, "kill" );
		var mana = boons.Get( BoonStat.ManaPerKill );
		if ( mana > 0f ) ctx.Attacker.Components.Get<PlayerMana>( FindMode.EverythingInSelfAndDescendants )?.Refund( mana );
	}

	/// <summary>Host: a kill's shard count with the killer's Hoarder bonus (a fractional remainder is a roll).</summary>
	public static int ShardsFor( int count, GameObject attacker )
	{
		if ( count <= 0 ) return count;
		var gain = PlayerBoons.Find( attacker )?.Get( BoonStat.ShardGain ) ?? 0f;
		if ( gain <= 0f ) return count;
		var scaled = count * (1f + gain);
		var whole = (int)MathF.Floor( scaled );
		return whole + (Game.Random.Float( 0f, 1f ) < scaled - whole ? 1 : 0);
	}
}
