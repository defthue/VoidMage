using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// The doors of the current dungeon by index, plus their tuning.
///
/// Unlike TrapNet there are no RPCs here, and deliberately so. Every peer rebuilds the identical dungeon
/// from the seed, and every body that opens a door is already replicated (players are networked, skeletons
/// are host-spawned and networked), so each peer runs the same proximity test and reaches the same state
/// on its own — no round trip, so a door is never late for the player who walked up to it. That stays
/// correct under the split authority the rest of the game uses: player collision is owner-authoritative and
/// NPC movement is host-authoritative, so each side is judged against the door state on the machine that
/// owns that decision. The index registry is still here for a lock or barricade that would need a host call.
/// </summary>
public static class DoorNet
{
	/// <summary>Share of doorways that get a door at all.</summary>
	[ConVar( "door_chance" )] public static float Chance { get; set; } = 0.8f;
	/// <summary>
	/// How far in front of a doorway a body opens it. A walk is ~130 u/s, so this is about half a second of
	/// warning — the swing takes a touch longer than that, but a door that is not fully Closed has no collider,
	/// so walking into one still mid-swing passes through it rather than bumping.
	/// </summary>
	[ConVar( "door_open_range" )] public static float OpenRange { get; set; } = 75f;
	/// <summary>
	/// Once open, the door holds open for bodies this far to either side of the opening as well as in front of
	/// it, so someone working along the wall beside a doorway does not make it flap. Sideways only: the approach
	/// distance stays OpenRange either way, and CloseDelay is what absorbs a body hovering right on that edge.
	/// </summary>
	[ConVar( "door_close_slack" )] public static float CloseSlack { get; set; } = 60f;
	/// <summary>Seconds after the last body leaves before it swings shut.</summary>
	[ConVar( "door_close_delay" )] public static float CloseDelay { get; set; } = 2f;
	/// <summary>Seconds for a full 90 degree swing.</summary>
	[ConVar( "door_swing_time" )] public static float SwingTime { get; set; } = 0.55f;
	/// <summary>Half the depth of the doorway itself: a body inside this never gets the door shut on it.</summary>
	[ConVar( "door_doorway_depth" )] public static float DoorwayDepth { get; set; } = 45f;
	/// <summary>Doors past this from every player keep their state but stop being ticked.</summary>
	[ConVar( "door_active_radius" )] public static float ActiveRadius { get; set; } = 2600f;
	/// <summary>Draw every door's opening, state and swing direction.</summary>
	[ConVar( "door_debug" )] public static bool Debug { get; set; }

	// No clips for these yet: GameAudio logs one warning per missing event and is otherwise silent.
	public const string OpenSound = "sounds/dooropen.sound";
	public const string CloseSound = "sounds/doorclose.sound";

	private static readonly List<Door> _doors = new();
	/// <summary>Colliders of the boarded-up doorways' plank panels: dead through the navmesh bake like the leaves,
	/// solid after it (the boards are a wall until smashed; what is behind them is reached by breaking in).</summary>
	private static readonly List<Collider> _barricades = new();

	/// <summary>
	/// Every doorway of the floor, hung with a door or not: the frame's world transform (local +Y along the wall) and
	/// where the opening sits on it. A skeleton asks this so it never makes its stand IN one — a body holding its
	/// ground in a single doorway corks it for everyone behind it, and the player fights the pack one at a time.
	/// </summary>
	private readonly record struct Doorway( Transform Frame, float Centre, float HalfOpening );
	private static readonly List<Doorway> _doorways = new();

	public static IReadOnlyList<Door> All => _doors;
	public static int Count => _doors.Count;

	/// <summary>Forget the previous dungeon's doors (a rebuild registers a fresh list).</summary>
	public static void Clear() { _doors.Clear(); _barricades.Clear(); _doorways.Clear(); }

	public static void RegisterDoorway( Transform frame, float centre, float halfOpening ) => _doorways.Add( new Doorway( frame, centre, halfOpening ) );

	/// <summary>Is <paramref name="world"/> in the throat of any doorway: within <paramref name="depth"/> of the wall plane, between the jambs, on that floor?</summary>
	public static bool InAnyDoorway( Vector3 world, float depth )
	{
		foreach ( var d in _doorways )
		{
			var l = d.Frame.PointToLocal( world );
			if ( l.z < -60f || l.z > 120f ) continue;
			if ( MathF.Abs( l.x ) <= depth && MathF.Abs( l.y - d.Centre ) <= d.HalfOpening + 8f ) return true;
		}
		return false;
	}

	public static void RegisterBarricade( Collider collider ) => _barricades.Add( collider );

	public static int Register( Door door )
	{
		door.Index = _doors.Count;
		_doors.Add( door );
		return door.Index;
	}

	/// <summary>
	/// Let the leaves collide, once the navmesh has baked. Until then every door is a renderer with a dead
	/// collider: nav.GenerateTiles reads the whole physics world, so a solid closed door at bake time would
	/// cut the navmesh at the doorway and leave the tiers behind it unreachable.
	/// </summary>
	public static void SetSolid( bool solid )
	{
		foreach ( var d in _doors ) if ( d.IsValid() ) d.SetSolid( solid );
		foreach ( var c in _barricades ) if ( c.IsValid() ) c.Enabled = solid;
	}

	/// <summary>Every door of the floor swung open or shut (the director's buttons).</summary>
	public static void SetAll( bool open )
	{
		foreach ( var d in _doors )
		{
			if ( !d.IsValid() ) continue;
			if ( open ) d.Open( d.Centre + d.WorldRotation.Forward * 100f );
			else d.Close();
		}
	}

	/// <summary>door_list: every door with its index, state and position.</summary>
	[ConCmd( "door_list" )]
	public static void ListCommand()
	{
		if ( _doors.Count == 0 ) { Log.Info( "[Door] none" ); return; }
		foreach ( var d in _doors )
		{
			if ( !d.IsValid() ) { Log.Info( "[Door] (destroyed)" ); continue; }
			Log.Info( $"[Door] {d.DebugLabel()} at {d.Centre:0} with {d.Leaves.Count} leaf/leaves" );
		}
	}

	/// <summary>door_open [index]: swing a door open. No index = the one nearest the local player.</summary>
	[ConCmd( "door_open" )]
	public static void OpenCommand( string index = "" )
	{
		var d = Resolve( index );
		if ( d is null ) return;
		d.Open( d.Centre + d.WorldRotation.Forward * 100f );
		Log.Info( $"[Door] opened #{d.Index}" );
	}

	/// <summary>door_close [index]: swing a door shut. No index = the one nearest the local player.</summary>
	[ConCmd( "door_close" )]
	public static void CloseCommand( string index = "" )
	{
		var d = Resolve( index );
		if ( d is null ) return;
		d.Close();
		Log.Info( $"[Door] closed #{d.Index}" );
	}

	private static Door Resolve( string index )
	{
		if ( int.TryParse( index, out var i ) )
		{
			if ( i < 0 || i >= _doors.Count || !_doors[i].IsValid() ) { Log.Info( $"[Door] no door #{i}" ); return null; }
			return _doors[i];
		}
		var local = Character.PlayerHealth.Local;
		if ( !local.IsValid() ) { Log.Info( "[Door] no local player" ); return null; }
		var at = local.FeetPosition;
		var nearest = _doors.Where( d => d.IsValid() ).OrderBy( d => d.Centre.DistanceSquared( at ) ).FirstOrDefault();
		if ( nearest is null ) { Log.Info( "[Door] none" ); return null; }
		return nearest;
	}
}
