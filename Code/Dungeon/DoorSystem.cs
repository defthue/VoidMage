using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Npc;

namespace VoidMage.Dungeon;

/// <summary>
/// Drives every door from one place each frame (the TrapSystem model): one cached rescan of the bodies that
/// can open a door every half second, then each door's own step. A door nobody is near settles and is
/// skipped. `door_debug 1` draws the opening and a state label.
///
/// Players and skeletons open doors by exactly the same test, which is what makes monsters able to use them
/// — a skeleton's NavMeshAgent paths straight through the doorway (the navmesh is baked with the doors not
/// yet solid, see DoorNet.SetSolid) and the door is already swinging by the time it arrives.
/// </summary>
public sealed class DoorSystem : GameObjectSystem
{
	private readonly List<PlayerHealth> _players = new();
	private readonly List<SkeletonNpc> _skeletons = new();
	private float _nextScan = float.NegativeInfinity;

	public DoorSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 2, Tick, "Doors" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		var doors = DoorNet.All;
		if ( doors.Count == 0 ) return;
		float now = Time.Now, dt = Time.Delta;
		if ( now >= _nextScan )
		{
			_nextScan = now + BodyScan.Interval;
			BodyScan.Rescan( Scene, _players, _skeletons );
		}

		float active2 = DoorNet.ActiveRadius * DoorNet.ActiveRadius;
		foreach ( var door in doors )
		{
			if ( !door.IsValid() ) continue;
			var centre = door.Centre;
			bool near = false;
			foreach ( var p in _players )
				if ( p.IsValid() && p.FeetPosition.DistanceSquared( centre ) < active2 ) { near = true; break; }
			// a skeleton counts too: one that chased a player out of range still has to get through the door
			// it is walking at, and a door that stopped ticking would just stall its agent against the leaf
			if ( !near )
				foreach ( var n in _skeletons )
					if ( n.IsValid() && n.WorldPosition.DistanceSquared( centre ) < active2 ) { near = true; break; }
			if ( !near && door.State == DoorState.Closed ) continue;

			var opener = Opener( door );
			if ( opener.HasValue ) door.Open( opener.Value );
			door.Step( dt, opener.HasValue || Occupied( door ), now );

			if ( DoorNet.Debug ) Draw( door );
		}
	}

	/// <summary>The first body standing in front of this doorway, whose side decides which way the leaves swing.</summary>
	private Vector3? Opener( Door door )
	{
		foreach ( var p in _players )
			if ( p.IsValid() && door.InFront( p.FeetPosition, DoorNet.OpenRange, Slack( door ) ) ) return p.FeetPosition;
		foreach ( var n in _skeletons )
			if ( n.IsValid() && door.InFront( n.WorldPosition, DoorNet.OpenRange, Slack( door ) ) ) return n.WorldPosition;
		return null;
	}

	/// <summary>An open door holds open over a slightly wider box than it opened for, so a body loitering on the edge does not make it flap.</summary>
	private static float Slack( Door door ) => door.State == DoorState.Closed ? 0f : DoorNet.CloseSlack;

	/// <summary>Anyone actually in the opening keeps it open regardless of the approach box.</summary>
	private bool Occupied( Door door )
	{
		foreach ( var p in _players )
			if ( p.IsValid() && door.InDoorway( p.FeetPosition ) ) return true;
		foreach ( var n in _skeletons )
			if ( n.IsValid() && door.InDoorway( n.WorldPosition ) ) return true;
		return false;
	}

	private void Draw( Door door )
	{
		var overlay = Scene.DebugOverlay;
		var t = door.WorldTransform;
		float h = door.HalfOpening, c = door.OpeningCentre;
		var a = t.PointToWorld( new Vector3( 0f, c - h, 0f ) );
		var b = t.PointToWorld( new Vector3( 0f, c + h, 0f ) );
		overlay.Line( a, b, door.State == DoorState.Closed ? Color.Red : Color.Green );
		overlay.Line( door.Centre, t.PointToWorld( new Vector3( DoorNet.OpenRange, c, 0f ) ), Color.Yellow.WithAlpha( 0.4f ) );
		overlay.Line( door.Centre, t.PointToWorld( new Vector3( -DoorNet.OpenRange, c, 0f ) ), Color.Yellow.WithAlpha( 0.4f ) );
		overlay.Text( door.Centre + Vector3.Up * 120f, door.DebugLabel(), 12f );
	}
}
