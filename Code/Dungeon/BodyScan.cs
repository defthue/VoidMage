using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Npc;

namespace VoidMage.Dungeon;

/// <summary>
/// The bodies a dungeon fixture reacts to: living players and awake skeletons. Both TrapSystem and
/// DoorSystem rescan on the same half-second cadence, so the query lives here rather than in each of them.
/// </summary>
public static class BodyScan
{
	/// <summary>How often either system refreshes its lists.</summary>
	public const float Interval = 0.5f;

	public static void Rescan( Scene scene, List<PlayerHealth> players, List<SkeletonNpc> skeletons )
	{
		players.Clear();
		foreach ( var p in scene.GetAllComponents<PlayerHealth>() ) if ( !p.IsDead ) players.Add( p );
		skeletons.Clear();
		foreach ( var n in scene.GetAllComponents<SkeletonNpc>() ) if ( !n.IsDead && !n.Dormant ) skeletons.Add( n );
	}
}
