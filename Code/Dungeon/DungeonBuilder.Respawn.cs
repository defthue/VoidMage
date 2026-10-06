using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Npc;

namespace VoidMage.Dungeon;

/// <summary>
/// The slow trickle back: a floor somebody lingers on (or idles on) never runs dry. Host only. Once the living
/// head count has dropped under a share of what the floor opened with, one enemy at a time is stood on a free
/// cell of a tier the first pass was allowed to use (so never near the spawn, the exit or in the secret room),
/// far from every player and out of their sight, where it waits dormant like any other. It tops the floor back up
/// to that share and no further, so a floor being cleared at a normal pace still empties ahead of the player.
/// Singles only: no ambush groups, no knight, no swordsman, no critters. Not seeded (the first pass is the floor's
/// identity; this is weather).
/// </summary>
public sealed partial class DungeonBuilder
{
	[ConVar( "npc_respawn" )] public static bool RespawnEnabled { get; set; } = true;
	/// <summary>Seconds between two bodies coming back.</summary>
	[ConVar( "npc_respawn_interval" )] public static float RespawnInterval { get; set; } = 30f;
	/// <summary>Share of the floor's opening head count the trickle tops up to: nothing comes back above it.</summary>
	[ConVar( "npc_respawn_fill" )] public static float RespawnFill { get; set; } = 0.6f;
	/// <summary>Units every player must be from the spot (a cell is 197).</summary>
	[ConVar( "npc_respawn_distance" )] public static float RespawnDistance { get; set; } = 1500f;
	[ConVar( "npc_respawn_debug" )] public static bool RespawnDebug { get; set; }

	private const int RespawnTries = 12;
	/// <summary>Seconds until the next go when no spot passed (everybody is standing in the only free rooms).</summary>
	private const float RespawnRetry = 3f;
	/// <summary>Units a fresh body keeps from a living one: nobody starts life inside somebody else.</summary>
	private const float RespawnElbowRoom = 70f;

	private readonly List<(Tier tier, (int x, int z) cell)> _respawnCells = new();
	private readonly Random _respawnRng = new();
	private int _respawnTarget;
	private float _respawnNext;

	/// <summary>From the first pass: where bodies may stand and how many the floor opened with (critters not counted).</summary>
	private void ArmRespawn( List<Tier> eligible, HashSet<(int x, int z, int l)> occupied, int opened )
	{
		_respawnCells.Clear();
		foreach ( var t in eligible )
			foreach ( var c in t.Cells )
				if ( !occupied.Contains( (c.x, c.z, t.Level) ) ) _respawnCells.Add( (t, c) );
		_respawnTarget = opened;
		_respawnNext = Time.Now + RespawnInterval;
	}

	private void ClearRespawn()
	{
		_respawnCells.Clear();
		_footprints.Clear();
		_respawnTarget = 0;
	}

	private void TickRespawn()
	{
		if ( !RespawnEnabled || _respawnTarget <= 0 || _respawnCells.Count == 0 || Time.Now < _respawnNext ) return;
		_respawnNext = Time.Now + MathF.Max( 1f, RespawnInterval );
		if ( !NavMeshReady ) return;

		var living = Scene.GetAllComponents<SkeletonNpc>()
			.Where( n => n.IsValid() && !n.IsDead && !n.Raised && n.Team == VoidMage.Combat.Team.Hostile && MonsterKinds.Find( n.KindName )?.Critter != true )
			.ToList();
		int want = Math.Min( MaxSkeletons, (int)MathF.Round( _respawnTarget * Math.Clamp( RespawnFill, 0f, 1f ) ) );
		if ( living.Count >= want ) return;

		var players = Scene.GetAllComponents<PlayerHealth>().Where( p => p.IsValid() ).ToList();
		if ( players.Count == 0 ) return;

		for ( int i = 0; i < RespawnTries; i++ )
		{
			var (tier, cell) = _respawnCells[_respawnRng.Next( _respawnCells.Count )];
			var slot = CellSlots[_respawnRng.Next( Math.Clamp( EnemiesPerCell, 1, CellSlots.Length ) )];
			if ( InsideProp( _footprints, cell.x + 0.5f + slot.x, cell.z + 0.5f + slot.y, tier.Level ) ) continue;   // not in a bed either
			var world = WorldTransform.PointToWorld( W( cell.x + 0.5f + slot.x, cell.z + 0.5f + slot.y, tier.Level ) );
			if ( players.Any( p => p.WorldPosition.Distance( world ) < RespawnDistance ) ) continue;
			if ( living.Any( n => n.WorldPosition.Distance( world ) < RespawnElbowRoom ) ) continue;
			if ( !SpawnSpotClear( world, out var floor ) ) continue;
			if ( players.Any( p => Sees( p, floor ) ) ) continue;

			var roster = Roster;
			var kind = _respawnRng.NextDouble() < roster.MonsterGroupChance && RollMonster( roster, _respawnRng ) is { } monster ? "monster_" + monster.Name
				: _respawnRng.NextDouble() < roster.SoulChance ? "tormented_soul"
				: ArmorRoll( roster, _respawnRng ) is { } armored ? armored
				: BiomeEnemies.OneOf( roster.Base, BaseSkeletonKinds, _respawnRng );
			var brain = kind.StartsWith( "monster_" ) ? "" : _respawnRng.Next( 2 ) == 0 ? "aggressive" : "defensive";
			var go = SkeletonSpawner.Spawn( Scene, floor + Vector3.Up * 4f, Rotation.FromYaw( _respawnRng.Next( 360 ) ), GameObject, kind, brain, _respawnRng.Next( 2 ), _respawnRng.Next( 2 ), false );
			if ( !go.IsValid() ) continue;
			if ( go.Components.Get<SkeletonNpc>( FindMode.EverythingInSelfAndDescendants ) is { } fresh ) fresh.Respawned = true;
			if ( RespawnDebug ) Log.Info( $"[Dungeon] respawned {kind} on tier {tier.Id} ({living.Count + 1}/{want} living, floor opened with {_respawnTarget})" );
			return;
		}
		_respawnNext = Time.Now + RespawnRetry;
	}

	/// <summary>A clear line from the player's head to where the body's chest would be.</summary>
	private bool Sees( PlayerHealth player, Vector3 floor )
	{
		var eye = player.WorldPosition + Vector3.Up * 60f;
		return !Scene.Trace.Ray( eye, floor + Vector3.Up * 45f ).WithoutTags( SpawnSweepIgnore ).Run().Hit;
	}
}
