using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.Traps;

/// <summary>
/// The traps of the current dungeon by index and the relays between peers: a trigger (the host's own
/// detection, or a client's fireball) becomes one broadcast "arm trap N"; a freeze the same. Static RPCs
/// because the traps themselves are not networked: every peer built the same list in the same order
/// (the BreakableNet pattern). Also the test commands.
/// </summary>
public static class TrapNet
{
	/// <summary>Sounds and effects only play for traps within this of a player; the logic runs regardless.</summary>
	[ConVar( "trap_active_radius" )] public static float ActiveRadius { get; set; } = 2600f;
	/// <summary>A fireball sets off every pressure trap whose trigger cell comes within this of its impact.</summary>
	[ConVar( "trap_fireball_radius" )] public static float FireballRadius { get; set; } = 120f;
	/// <summary>Draw every trap's trigger (yellow) and damage (red) boxes with a state label.</summary>
	[ConVar( "trap_debug" )] public static bool Debug { get; set; }

	private static readonly List<TrapBase> _traps = new();

	public static IReadOnlyList<TrapBase> All => _traps;
	public static int Count => _traps.Count;

	/// <summary>Forget the previous dungeon's traps (a rebuild registers a fresh list).</summary>
	public static void Clear() => _traps.Clear();

	public static int Register( TrapBase trap )
	{
		trap.Index = _traps.Count;
		_traps.Add( trap );
		return trap.Index;
	}

	private static TrapBase Get( int index ) => index >= 0 && index < _traps.Count && _traps[index].IsValid() ? _traps[index] : null;

	/// <summary>Set trap `index` off: the host decides (it must be Armed and not frozen), everyone plays the wind-up.</summary>
	public static void Trigger( int index )
	{
		if ( Networking.IsHost ) TriggerHost( index );
		else RequestTrigger( index );
	}

	[Rpc.Host]
	private static void RequestTrigger( int index ) => TriggerHost( index );

	private static void TriggerHost( int index )
	{
		var t = Get( index );
		if ( t is null || t.State != TrapState.Armed || t.Frozen ) return;
		ArmRpc( index );
	}

	[Rpc.Broadcast]
	private static void ArmRpc( int index ) => Get( index )?.Arm();

	/// <summary>Host: every armed trap of the floor goes off at once (the director's button).</summary>
	public static void FireAll()
	{
		if ( !Networking.IsHost ) return;
		for ( var i = 0; i < _traps.Count; i++ ) TriggerHost( i );
	}

	/// <summary>The ice hook: relayed like a trigger, applied on every peer.</summary>
	public static void Freeze( int index, float seconds )
	{
		if ( Networking.IsHost ) FreezeRpc( index, seconds );
		else RequestFreeze( index, seconds );
	}

	[Rpc.Host]
	private static void RequestFreeze( int index, float seconds ) => FreezeRpc( index, seconds );

	[Rpc.Broadcast]
	private static void FreezeRpc( int index, float seconds ) => Get( index )?.Freeze( seconds );

	/// <summary>A fireball landed: every armed pressure trap whose trigger box comes within `radius` goes off. Returns how many.</summary>
	public static int Blast( Vector3 at, float radius )
	{
		int n = 0;
		float r2 = radius * radius;
		foreach ( var t in _traps )
		{
			if ( !t.IsValid() || t.Trigger != TrapTrigger.Pressure || t.State != TrapState.Armed ) continue;
			foreach ( var b in t.TriggerBoxes )
			{
				if ( b.ClosestPoint( at ).DistanceSquared( at ) > r2 ) continue;
				Trigger( t.Index );
				n++;
				break;
			}
		}
		return n;
	}

	// ------------------------------------------------------------------ test commands

	/// <summary>trap_list: every trap with its index, kind, state and cell.</summary>
	[ConCmd( "trap_list" )]
	public static void ListCommand()
	{
		if ( _traps.Count == 0 ) { Log.Info( "[Trap] none" ); return; }
		foreach ( var t in _traps )
		{
			if ( !t.IsValid() ) { Log.Info( "[Trap] (destroyed)" ); continue; }
			var p = t.Placement;
			Log.Info( $"[Trap] {t.DebugLabel()} at {p.X},{p.Z},{p.Level} facing {p.Facing} variant {p.Variant} walk {p.WalkDist} spine {p.SpineIndex}" );
		}
	}

	/// <summary>trap_fire [index]: set a trap off (host only). No index = the nearest to the local player.</summary>
	[ConCmd( "trap_fire" )]
	public static void FireCommand( string index = "" )
	{
		if ( !Networking.IsHost ) { Log.Info( "[Trap] host only" ); return; }
		var i = Resolve( index );
		if ( i < 0 ) return;
		TriggerHost( i );
		Log.Info( $"[Trap] fired #{i}" );
	}

	/// <summary>trap_freeze [index] [seconds]: freeze a trap (host only). No index = the nearest to the local player.</summary>
	[ConCmd( "trap_freeze" )]
	public static void FreezeCommand( string index = "", string seconds = "3" )
	{
		if ( !Networking.IsHost ) { Log.Info( "[Trap] host only" ); return; }
		var i = Resolve( index );
		if ( i < 0 ) return;
		if ( !float.TryParse( seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s ) ) s = 3f;
		Freeze( i, s );
		Log.Info( $"[Trap] froze #{i} for {s:0.0} s" );
	}

	private static int Resolve( string index )
	{
		if ( int.TryParse( index, out var i ) )
		{
			if ( Get( i ) is null ) { Log.Info( $"[Trap] no trap #{i}" ); return -1; }
			return i;
		}
		var local = Character.PlayerHealth.Local;
		if ( !local.IsValid() ) { Log.Info( "[Trap] no local player" ); return -1; }
		var at = local.FeetPosition;
		var nearest = _traps.Where( t => t.IsValid() ).OrderBy( t => t.Centre.DistanceSquared( at ) ).FirstOrDefault();
		if ( nearest is null ) { Log.Info( "[Trap] none" ); return -1; }
		return nearest.Index;
	}
}
