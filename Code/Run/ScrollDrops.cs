using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Inventory;
using VoidMage.Npc;

namespace VoidMage.Run;

/// <summary>
/// The floor's scrolls fall out of its monsters AS THE PLAYER KILLS THEM (user, 2026-09-20, the same day the
/// pre-chosen carriers went in: "need a system that makes the scrolls drop from enemies throughout the dungeon, not
/// just chosen enemies ahead of time in case you dont kill every single enemies every floor"; the secret room and
/// its chest went with them, "we dont want the player to miss anything in their run").
///
/// Not a drop chance either: a run hands out a curated number of scrolls. When the floor is spawned every scroll gets a
/// KILL COUNT, spread over the first half of the floor's bodies with some jitter (so the first comes after a handful
/// of kills and the last before the floor is half cleared), and the kill that reaches a count drops that scroll where
/// the body fell, on the nearest walkable spot. Which enemies the player fights does not matter, only that they fight.
/// A player who slips past nearly everything still misses nothing: whatever has not dropped yet is laid out by the
/// exit portal when they reach it (RunManager). What is in a scroll is rolled when it drops, from what the staff
/// can still take.
/// </summary>
public static class ScrollDrops
{
	/// <summary>
	/// Enchantment scrolls on every floor: 5 since the run went to four floors (user, 2026-09-22, "+2 sounds better";
	/// was 3 over eight floors, so a run now hands out 24 scrolls where it handed out 32). Plus the spell scroll below.
	/// </summary>
	[ConVar( "floor_scrolls" )] public static int Scrolls { get; set; } = 5;
	/// <summary>Spell scrolls on every floor (a floor with nothing to offer drops an enchantment instead).</summary>
	[ConVar( "floor_spells" )] public static int Spells { get; set; } = 1;
	/// <summary>The share of the floor's bodies by which the first and the last scroll have dropped.</summary>
	[ConVar( "floor_scroll_first" )] public static float FirstShare { get; set; } = 0.06f;
	[ConVar( "floor_scroll_last" )] public static float LastShare { get; set; } = 0.5f;
	/// <summary>How near the exit portal the player has to come for the scrolls they never earned to be laid out by it.</summary>
	[ConVar( "floor_scroll_exit_range" )] public static float ExitRange { get; set; } = 380f;

	private static readonly Queue<(int atKill, bool spell)> _pending = new();
	private static int _kills;

	public static int Pending => _pending.Count;

	/// <summary>Host, from the builder once the floor is spawned: `bodies` is the hostile head count (ambient life not counted).</summary>
	public static void BeginFloor( int bodies, Random rng )
	{
		_pending.Clear();
		_kills = 0;
		if ( !Networking.IsHost || bodies <= 0 ) return;
		int total = Math.Max( 0, Scrolls ) + Math.Max( 0, Spells );
		if ( total == 0 ) return;
		// the spell scroll takes any place in the line
		var kinds = Enumerable.Range( 0, total ).Select( i => i < Spells ).OrderBy( _ => rng.Next() ).ToList();
		float first = Math.Clamp( FirstShare, 0f, 1f ), last = Math.Clamp( MathF.Max( FirstShare, LastShare ), 0f, 1f );
		int previous = 0;
		for ( int k = 0; k < total; k++ )
		{
			// its own slice of the span, somewhere inside it
			float t = (k + 0.15f + (float)rng.NextDouble() * 0.7f) / total;
			int at = Math.Max( previous + 1, (int)MathF.Round( bodies * (first + (last - first) * t) ) );
			at = Math.Min( at, Math.Max( previous + 1, bodies ) );
			_pending.Enqueue( (at, kinds[k]) );
			previous = at;
		}
		Log.Info( $"[Loot] floor scrolls: {total} ({Spells} spell) at kills {string.Join( ", ", _pending.Select( p => p.atKill ) )} of {bodies} bodies" );
	}

	/// <summary>Host, from SkeletonNpc.Die: a hostile body fell. The kill that reaches a scroll's count drops it here.</summary>
	public static void OnKill( SkeletonNpc npc, Vector3 push )
	{
		if ( !Networking.IsHost || !npc.IsValid() || _pending.Count == 0 ) return;
		if ( MonsterKinds.Find( npc.KindName )?.Critter == true ) return;   // a frog is nobody's kill
		_kills++;
		while ( _pending.Count > 0 && _pending.Peek().atKill <= _kills )
		{
			var (_, spell) = _pending.Dequeue();
			Drop( npc.Scene, npc.WorldPosition, spell, npc.GameObject.Parent, push );
		}
	}

	/// <summary>Host: the player has reached the exit with scrolls still owed (they fought little): laid out between them and the portal.</summary>
	public static void FlushAt( Scene scene, Vector3 portal, Vector3 player, GameObject parent )
	{
		if ( !Networking.IsHost || _pending.Count == 0 ) return;
		var toward = (player - portal).WithZ( 0f );
		toward = toward.IsNearZeroLength ? Vector3.Forward : toward.Normal;
		var side = Vector3.Cross( toward, Vector3.Up );
		int n = _pending.Count, i = 0;
		while ( _pending.Count > 0 )
		{
			var (_, spell) = _pending.Dequeue();
			var at = portal + toward * 130f + side * ((i - (n - 1) * 0.5f) * 60f);
			Drop( scene, at, spell, parent, Vector3.Zero );
			i++;
		}
	}

	/// <summary>
	/// Host, the moment a brand-new player's first floor opens (user, 2026-09-22: "the first thing they do in a run is
	/// grab a scroll, open inventory and read the tutorial"): one sealed enchantment laid on the floor a few steps ahead
	/// of them, on top of the floor's own scrolls. Taking it plays the reveal with "Press Tab" under it, and the first
	/// Tab plays the inventory tour (SpellLoadout). `feet` is where the player stands, `forward` the way they look.
	/// </summary>
	public static void GiftAt( Scene scene, Vector3 feet, Vector3 forward, GameObject parent )
	{
		if ( !Networking.IsHost || !scene.IsValid() ) return;
		var inventory = PlayerInventory.Local;
		if ( !inventory.IsValid() ) return;
		var scroll = inventory.RollEnchantment();
		if ( scroll.IsEmpty ) return;
		var ahead = forward.WithZ( 0f );
		ahead = ahead.IsNearZeroLength ? Vector3.Forward : ahead.Normal;
		var where = feet + ahead * 90f;
		var at = scene.NavMesh.GetClosestPoint( where, 160f ) ?? where;
		ScrollPickup.Spawn( scene, at + Vector3.Up * 30f, scroll, parent, Vector3.Zero );
		Log.Info( $"[Loot] first-run gift scroll at the player's feet: {scroll.Name}" );
	}

	private static void Drop( Scene scene, Vector3 where, bool spell, GameObject parent, Vector3 push )
	{
		var inventory = PlayerInventory.Local;
		if ( !inventory.IsValid() || !scene.IsValid() ) return;
		var scroll = spell ? inventory.RollSpell() : Scroll.Empty;
		if ( scroll.IsEmpty ) scroll = inventory.RollEnchantment();
		if ( scroll.IsEmpty ) return;   // the whole pool at three copies: a staff that full needs nothing
		// the nearest walkable spot: a flier killed over a pit does not take the scroll down with it
		var at = scene.NavMesh.GetClosestPoint( where, 160f ) ?? where;
		ScrollPickup.Drop( scene, at + Vector3.Up * 40f, scroll, parent, push );
		Log.Info( $"[Loot] scroll at kill {_kills}: {(scroll.IsSpell ? "SPELL" : "enchantment")} {scroll.Name}" );
	}
}
