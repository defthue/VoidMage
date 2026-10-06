using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Npc;

namespace VoidMage.Combat;

/// <summary>Whose side a body is on. Players are always Ally; skeletons are Hostile unless The Turncoat raised them.</summary>
public enum Team { Hostile = 0, Ally = 1 }

/// <summary>Anything a skeleton can hunt or hit: a player, another skeleton, a piece of the boss.</summary>
public interface ICombatant : IValid
{
	GameObject GameObject { get; }
	Vector3 WorldPosition { get; }
	Team Team { get; }
	bool IsDead { get; }
	/// <summary>Alive, awake, and on the field.</summary>
	bool CanBeTargeted { get; }
	float BodyHeight { get; }
	/// <summary>Where a bolt homes and a chain lands: the chest of a walker, the box centre of a boss part.</summary>
	Vector3 AimPoint => WorldPosition + Vector3.Up * BodyHeight * 0.55f;
	/// <summary>Host only: damage lands here (a skeleton's door, a player's broadcast, a boss part's door).</summary>
	void ApplyHitHost( in DamageRouter.Hit hit );
}

/// <summary>
/// The host's view of who is on the field, rebuilt once per tick (by SpellHostSystem; lazily when stale), shared by
/// targeting, area damage, hazards and dormancy so nobody re-walks the scene per skeleton per frame.
/// </summary>
public static class CombatTargets
{
	private static readonly List<PlayerHealth> _players = new();
	private static readonly List<SkeletonNpc> _skeletons = new();
	private static readonly List<Boss.BossPart> _bossParts = new();
	private static float _refreshedAt = float.NegativeInfinity;
	private static Scene _scene;

	// The concrete lists go out (read-only by convention): foreach over List&lt;T&gt; keeps its enumerator on the
	// stack, foreach over IReadOnlyList&lt;T&gt; boxes one per loop, and these are walked every host frame.
	public static List<PlayerHealth> Players { get { Ensure(); return _players; } }
	public static List<SkeletonNpc> Skeletons { get { Ensure(); return _skeletons; } }
	/// <summary>The Bone Colossus's parts (three at most, hostile, targetable once awake).</summary>
	public static List<Boss.BossPart> BossParts { get { Ensure(); return _bossParts; } }

	/// <summary>How long a list may stand before it is rebuilt. A rebuild walks the whole scene three times, so never per frame.</summary>
	private const float RefreshSeconds = 0.2f;
	/// <summary>At home nothing is spawned or killed between casts: the same three walks every two seconds are plenty.</summary>
	private const float VillageRefreshSeconds = 2f;
	private static float Interval => VoidMage.Run.RunManager.InDungeon ? RefreshSeconds : VillageRefreshSeconds;

	public static void Refresh( Scene scene )
	{
		_scene = scene;
		_refreshedAt = Time.Now;
		_players.Clear();
		_skeletons.Clear();
		_bossParts.Clear();
		if ( !scene.IsValid() ) return;
		_players.AddRange( scene.GetAllComponents<PlayerHealth>() );
		_skeletons.AddRange( scene.GetAllComponents<SkeletonNpc>() );
		_bossParts.AddRange( scene.GetAllComponents<Boss.BossPart>() );
	}

	/// <summary>Per tick from <see cref="Spells.SpellHostSystem"/>: rebuild only once the lists have gone stale.</summary>
	public static void Tick( Scene scene )
	{
		if ( scene != _scene || Time.Now - _refreshedAt > Interval ) Refresh( scene );
	}

	private static void Ensure()
	{
		var scene = Game.ActiveScene;
		if ( scene != _scene || Time.Now - _refreshedAt > Interval ) Refresh( scene );
	}

	/// <summary>Everyone `team` fights: Hostile hunts players and allies, Ally hunts hostiles.</summary>
	public static IEnumerable<ICombatant> HostileTo( Team team )
	{
		Ensure();
		if ( team == Team.Hostile )
		{
			foreach ( var p in _players ) if ( p.IsValid() ) yield return p;
			foreach ( var s in _skeletons ) if ( s.IsValid() && s.Team == Team.Ally ) yield return s;
		}
		else
		{
			foreach ( var s in _skeletons ) if ( s.IsValid() && s.Team == Team.Hostile ) yield return s;
			foreach ( var p in _bossParts ) if ( p.IsValid() ) yield return p;
		}
	}

	/// <summary>Every live, targetable body of `team`: the skeletons on that side and, when hostile, the boss parts. What area damage, hazards and chains iterate.</summary>
	public static IEnumerable<ICombatant> Bodies( Team team )
	{
		Ensure();
		foreach ( var s in _skeletons ) if ( s.IsValid() && s.Team == team && s.CanBeTargeted ) yield return s;
		if ( team == Team.Hostile )
			foreach ( var p in _bossParts ) if ( p.IsValid() && p.CanBeTargeted ) yield return p;
	}

	/// <summary>The nearest live body of `team` (by aim point) within `radius` of `at` that passes `filter`; skeletons and boss parts.</summary>
	public static ICombatant NearestBody( Vector3 at, Team team, float radius, Func<ICombatant, bool> filter = null )
	{
		ICombatant best = null;
		var bestD = radius * radius;
		foreach ( var c in Bodies( team ) )
		{
			if ( filter is not null && !filter( c ) ) continue;
			var d = c.AimPoint.DistanceSquared( at );
			if ( d < bestD ) { bestD = d; best = c; }
		}
		return best;
	}

	/// <summary>The nearest live, awake skeleton of `team` within `radius` of `at` that passes `filter`.</summary>
	public static SkeletonNpc NearestSkeleton( Vector3 at, Team team, float radius, Func<SkeletonNpc, bool> filter = null )
	{
		Ensure();
		SkeletonNpc best = null;
		var bestD = radius * radius;
		foreach ( var s in _skeletons )
		{
			if ( !s.IsValid() || s.Team != team || !s.CanBeTargeted ) continue;
			if ( filter is not null && !filter( s ) ) continue;
			var d = s.WorldPosition.DistanceSquared( at );
			if ( d < bestD ) { bestD = d; best = s; }
		}
		return best;
	}

	/// <summary>The side of whatever this object belongs to (walks the ancestors); unknown things are hostile.</summary>
	public static Team TeamOf( GameObject go )
	{
		if ( !go.IsValid() ) return Team.Hostile;
		if ( go.Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndAncestors ) is not null ) return Team.Ally;
		var npc = go.Components.Get<SkeletonNpc>( FindMode.EverythingInSelfAndAncestors );
		return npc is not null ? npc.Team : Team.Hostile;
	}

	/// <summary>The combatant this object belongs to (walks the ancestors): a player, a skeleton or a boss part; null for scenery.</summary>
	public static ICombatant CombatantOf( GameObject go )
		=> go.IsValid() ? go.Components.Get<ICombatant>( FindMode.EverythingInSelfAndAncestors ) : null;
}
