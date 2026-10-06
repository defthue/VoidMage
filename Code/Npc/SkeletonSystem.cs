using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Npc;

/// <summary>
/// Sleeps the skeletons nobody is near (the FireLightSystem model): a floor carries dozens of them and an idle
/// skeleton still costs a crowd agent, an animgraph evaluation, bone transforms and a draw every frame. Past
/// SleepRadius from every player an idle skeleton goes dormant (the host skips its brain and agent, every peer's
/// body hides the renderer); inside WakeRadius it wakes. The gap keeps the boundary from strobing, and the radii
/// sit past the retro fog (black at 1000), so nothing is ever seen freezing or popping. Only skeletons that are
/// Idle sleep: one that is chasing keeps running until it loses the player and idles again. Host only; peers
/// follow the synced flag.
/// </summary>
public sealed class SkeletonSystem : GameObjectSystem
{
	public const float SleepRadius = 1900f;
	public const float WakeRadius = 1500f;

	private readonly List<SkeletonNpc> _npcs = new();
	private readonly List<PlayerHealth> _players = new();
	private readonly List<SkeletonNpc> _allies = new();
	private float _nextScan = float.NegativeInfinity;
	private int _reportedDormant = -1;
	private int _reportedTotal = -1;

	public SkeletonSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 1, Tick, "SkeletonGate" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor || !Networking.IsHost ) return;
		// the village has no skeletons: nothing to gate, and no reason to walk the scene twice a second for them
		if ( !VoidMage.Run.RunManager.InDungeon )
		{
			if ( _npcs.Count > 0 ) { _npcs.Clear(); _players.Clear(); _allies.Clear(); }
			_nextScan = float.NegativeInfinity;
			return;
		}
		var t = Time.Now;
		if ( t >= _nextScan )
		{
			_nextScan = t + 0.5f;
			_npcs.Clear();
			_npcs.AddRange( Scene.GetAllComponents<SkeletonNpc>() );
			_players.Clear();
			foreach ( var p in Scene.GetAllComponents<PlayerHealth>() ) if ( !p.IsDead ) _players.Add( p );
			// a raised ally counts as a player here: the hostile it is chasing must not fall asleep mid-fight
			_allies.Clear();
			foreach ( var n in _npcs ) if ( n.IsValid() && !n.IsDead && n.Team == VoidMage.Combat.Team.Ally ) _allies.Add( n );
		}
		if ( _npcs.Count == 0 ) return;

		int dormant = 0;
		foreach ( var npc in _npcs )
		{
			if ( !npc.IsValid() || npc.IsDead ) continue;
			if ( npc.Team == VoidMage.Combat.Team.Ally ) { if ( npc.Dormant ) npc.SetDormant( false ); continue; }   // allies have a lifetime; they never sleep
			var nearest = float.MaxValue;
			foreach ( var p in _players )
				if ( p.IsValid() ) nearest = System.MathF.Min( nearest, p.WorldPosition.Distance( npc.WorldPosition ) );
			foreach ( var a in _allies )
				if ( a.IsValid() ) nearest = System.MathF.Min( nearest, a.WorldPosition.Distance( npc.WorldPosition ) );

			if ( npc.Dormant )
			{
				if ( nearest < WakeRadius ) npc.SetDormant( false );
				else dormant++;
			}
			// AtRest, not Idle: a hopping critter is mid-one-shot half the time and would never qualify to sleep
			else if ( nearest > SleepRadius && npc.AtRest )
			{
				npc.SetDormant( true );
				dormant++;
			}
		}

		if ( dormant != _reportedDormant || _npcs.Count != _reportedTotal )
		{
			_reportedDormant = dormant;
			_reportedTotal = _npcs.Count;
			if ( SkeletonNpc.DebugLog ) Log.Info( $"[Skeleton] {_npcs.Count - dormant} awake, {dormant} dormant" );
		}
	}
}
