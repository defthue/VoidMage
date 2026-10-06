using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>Host only: damage and pulls over an area, against the host's own skeleton positions, nearest first.</summary>
public static class SpellArea
{
	/// <summary>What a line of sight from a blast to a body passes through: never other bodies, corpses, debris, spell effects.</summary>
	public static readonly string[] LosIgnore = { "npc", "enemy", "ally", "player", "viewmodel", "posesource", "spellfire", "trigger", "ragdoll", Debris.Tag };

	private static readonly List<(ICombatant body, Vector3 point, float d)> _candidates = new();

	/// <summary>
	/// Every live, awake body of `hitTeam` (skeletons; the boss's parts when hostile) inside `radius` of `center`
	/// (nearest first, at most `maxTargets`, `exclude` skipped) takes `template` with the push away from the centre;
	/// `falloff` scales the damage down to SplashRim at the edge; `needLos` drops bodies behind a wall. A walker is
	/// judged by its chest, a boss part by the point of its box nearest the blast. Returns how many were hit.
	/// </summary>
	public static int Damage( Vector3 center, float radius, in DamageRouter.Hit template, Team hitTeam, GameObject exclude = null, bool falloff = true, bool needLos = true, int maxTargets = int.MaxValue, Action<ICombatant> onHit = null )
	{
		if ( !Networking.IsHost || radius <= 0f ) return 0;
		var scene = Game.ActiveScene;
		_candidates.Clear();
		var reach = radius + 14f;
		foreach ( var body in CombatTargets.Bodies( hitTeam ) )
		{
			if ( exclude.IsValid() && body.GameObject == exclude ) continue;
			var point = body is Boss.BossPart part ? part.ClosestPoint( center ) : body.AimPoint;
			var d = point.Distance( center );
			if ( d > reach ) continue;
			_candidates.Add( (body, point, d) );
		}
		if ( _candidates.Count == 0 ) return 0;
		_candidates.Sort( ( x, y ) => x.d.CompareTo( y.d ) );
		// snapshot: a hit below may kill, and a kill (Pyre, a death blast) re-enters Damage and refills _candidates
		var targets = _candidates.ToArray();

		var hits = 0;
		foreach ( var (body, point, d) in targets )
		{
			if ( hits >= maxTargets ) break;
			if ( needLos && scene.IsValid() && d > 20f )
			{
				var los = scene.Trace.Ray( center, point ).WithoutTags( LosIgnore ).Run();
				if ( los.Hit ) continue;
			}
			var away = (point - center).WithZ( 0f );
			var push = away.IsNearZeroLength ? Vector3.Zero : away.Normal;
			var scale = falloff ? MathX.Lerp( 1f, SpellHostTuning.SplashRim, Math.Clamp( d / MathF.Max( 1f, radius ), 0f, 1f ) ) : 1f;
			body.ApplyHitHost( new DamageRouter.Hit
			{
				Damage = template.Damage * scale,
				Position = point,
				Push = push,
				Attacker = template.Attacker,
				Kind = template.Kind,
				Spell = template.Spell.With( SpellHitFlags.Area ),
			} );
			onHit?.Invoke( body );
			hits++;
		}
		return hits;
	}

}
