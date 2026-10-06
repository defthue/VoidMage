using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Dungeon;
using VoidMage.Npc;

namespace VoidMage.Traps;

/// <summary>
/// Drives every trap from one place each frame (the FireLightSystem model): a cached rescan of the bodies that
/// can trigger or be hurt every half second, host-side trigger detection (a body standing in a trigger box arms
/// the trap through TrapNet), then each trap's own step. Far traps keep their logic (a blade's phase must stay
/// coherent) but skip sounds and effects. `trap_debug 1` draws the boxes and a label per trap.
/// </summary>
public sealed class TrapSystem : GameObjectSystem
{
	/// <summary>Who can set a trap off and who it can hurt: living players and awake skeletons.</summary>
	public sealed class Targets
	{
		public readonly List<PlayerHealth> Players = new();
		public readonly List<SkeletonNpc> Skeletons = new();
	}

	private readonly Targets _targets = new();
	private float _nextScan = float.NegativeInfinity;

	public TrapSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 2, Tick, "Traps" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		var traps = TrapNet.All;
		if ( traps.Count == 0 ) return;
		float now = Time.Now, dt = Time.Delta;
		if ( now >= _nextScan )
		{
			_nextScan = now + BodyScan.Interval;
			BodyScan.Rescan( Scene, _targets.Players, _targets.Skeletons );
		}

		bool host = Networking.IsHost;
		float active2 = TrapNet.ActiveRadius * TrapNet.ActiveRadius;
		foreach ( var trap in traps )
		{
			if ( !trap.IsValid() ) continue;
			var centre = trap.Centre;
			float nearest2 = float.MaxValue;
			foreach ( var p in _targets.Players )
				if ( p.IsValid() ) nearest2 = MathF.Min( nearest2, p.FeetPosition.DistanceSquared( centre ) );
			bool near = nearest2 < active2;

			if ( host && trap.Trigger != TrapTrigger.Cycling && trap.State == TrapState.Armed && !trap.Frozen && Occupied( trap.TriggerBoxes ) )
				TrapNet.Trigger( trap.Index );

			trap.Step( now, dt, _targets, near );

			if ( TrapNet.Debug ) Draw( trap );
		}
	}

	/// <summary>
	/// Whether the monsters set traps off too. Off (user, 2026-09-20: "make it so npcs dont activate traps but they do get
	/// damaged by them"): only a player arms a plate or a head; a body caught in a trap that is firing, or in the path
	/// of a blade, is still hurt (TrapBase.HitInside).
	/// </summary>
	[ConVar( "trap_npc_trigger" )] public static bool NpcTrigger { get; set; } = false;

	private bool Occupied( BBox[] boxes )
	{
		if ( boxes.Length == 0 ) return false;
		foreach ( var p in _targets.Players )
			if ( p.IsValid() && TrapBase.Inside( boxes, p.FeetPosition, p.BodyHeight ) ) return true;
		if ( !NpcTrigger ) return false;
		foreach ( var n in _targets.Skeletons )
		{
			// small animals are too light to set anything off (SkeletonNpc.TriggersTraps, off for every
			// Quaternius monster). They can still be caught by a trap already firing — see TrapBase.HitInside.
			if ( !n.IsValid() || !n.TriggersTraps ) continue;
			if ( TrapBase.Inside( boxes, n.WorldPosition, n.BodyHeight ) ) return true;
		}
		return false;
	}

	private void Draw( TrapBase trap )
	{
		var overlay = Scene.DebugOverlay;
		foreach ( var b in trap.TriggerBoxes ) overlay.Box( b, Color.Yellow.WithAlpha( 0.5f ) );
		foreach ( var b in trap.DamageBoxes ) overlay.Box( b, (trap.State == TrapState.Fire ? Color.Red : Color.Orange).WithAlpha( 0.6f ) );
		overlay.Text( trap.Centre + Vector3.Up * 120f, trap.DebugLabel(), 12f, color: trap.Frozen ? Color.Cyan : Color.White );
	}
}
