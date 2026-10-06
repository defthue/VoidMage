using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// Stage 7, host only: what happens when a spell kills. The Turncoat (a fresh skeleton of the victim's kind rises on
/// our side; never from a minion's blast), then The Hollow's refund to the caster. Nothing here runs on the stack of
/// the death: everything is read now and queued, because the victim's object is already on its way out.
/// </summary>
public static class SpellDeathStage
{
	private sealed class PendingRaise { public float DueAt; public Vector3 Position; public Rotation Rotation; public GameObject Parent; public string Kind, Brain; public int Sword, Shield, Stacks; public GameObject Summoner; }
	private static readonly List<PendingRaise> _raises = new();

	public static void OnKill( SkeletonNpc victim, in KillContext ctx )
	{
		if ( !Networking.IsHost || !victim.IsValid() ) return;
		var info = ctx.Spell;
		if ( SpellDebug.Verbose ) Log.Info( $"[Spell] kill: {victim.KindName} ({victim.Team}) by {ctx.Kind} ({info})" );
		if ( info.Depth > SpellHostTuning.MaxProcDepth ) return;   // a minion's blast: nothing more
		if ( victim.Team != Team.Hostile ) return;                   // our own minion dying procs nothing

		Turncoat( victim, ctx );
		HollowRefund( ctx );
		KillHeal( victim, ctx );
		SpellHostEffects.OnKill( victim, ctx );
	}

	// ------------------------------------------------------------------ the Siphon Bolt

	/// <summary>A spell that heals on a kill (the Siphon Bolt): any kill by it counts, its splash, copies and procs too, never a minion's blast.</summary>
	private static void KillHeal( SkeletonNpc victim, in KillContext ctx )
	{
		var info = ctx.Spell;
		if ( !info.IsSpell || info.Has( SpellHitFlags.FromMinion ) ) return;
		var amount = info.Spell?.KillHeal ?? 0f;
		if ( amount <= 0f ) return;
		PlayerHealth.Find( ctx.Attacker )?.Refund( amount, "siphon" );
		if ( SpellDebug.Verbose ) Log.Info( $"[Spell] siphon: killed {victim.KindName}, {amount:0.#} back to the caster" );
	}

	// ------------------------------------------------------------------ The Turncoat

	private static void Turncoat( SkeletonNpc victim, in KillContext ctx )
	{
		var info = ctx.Spell;
		var stacks = info.Stack( ModifierId.Turncoat );
		if ( stacks <= 0 || info.Has( SpellHitFlags.FromMinion ) ) return;
		var brain = victim.BrainName;
		_raises.Add( new PendingRaise
		{
			DueAt = Time.Now + SpellHostTuning.TurncoatRaiseDelay,
			Position = victim.WorldPosition,
			Rotation = victim.WorldRotation,
			Parent = victim.GameObject.Parent,
			Kind = victim.KindName,
			Brain = brain == "default" ? "" : brain,
			Sword = victim.SwordIndex,
			Shield = victim.ShieldIndex,
			Stacks = stacks,
			Summoner = ctx.Attacker,
		} );
	}

	private static void Raise( Scene scene, PendingRaise r )
	{
		// the cap: the oldest minion crumbles (no blast) to make room
		var allies = CombatTargets.Skeletons.Where( n => n.IsValid() && !n.IsDead && n.Team == Team.Ally ).OrderBy( n => n.RaisedUntil ).ToList();
		while ( allies.Count >= Math.Max( 1, SpellHostTuning.TurncoatMax ) )
		{
			allies[0].Expire( allowBlast: false );
			allies.RemoveAt( 0 );
		}
		var pos = scene.NavMesh.GetClosestPoint( r.Position, 64f ) ?? r.Position;
		var go = SkeletonSpawner.Spawn( scene, pos, r.Rotation, r.Parent, r.Kind, r.Brain, r.Sword, r.Shield, false, Team.Ally, r.Summoner, r.Stacks );
		if ( SpellDebug.Verbose ) Log.Info( $"[Spell] turncoat raised {r.Kind} ({r.Brain}) x{r.Stacks}: {(go.IsValid() ? go.Name : "failed")}" );
	}

	// ------------------------------------------------------------------ The Hollow

	private static void HollowRefund( in KillContext ctx )
	{
		var info = ctx.Spell;
		var stacks = info.Stack( ModifierId.Hollow );
		if ( stacks < 2 || info.Has( SpellHitFlags.Proc ) ) return;   // free stays free: a proc's kill refunds nothing
		var amount = SpellHostTuning.HollowRefund( stacks );
		if ( amount <= 0f ) return;
		PlayerHealth.Find( ctx.Attacker )?.Refund( amount, "hollow" );
	}

	// ------------------------------------------------------------------ the queue

	/// <summary>Host, per tick: deliver due raises.</summary>
	public static void Tick( Scene scene, float now )
	{
		for ( var i = _raises.Count - 1; i >= 0; i-- )
		{
			var r = _raises[i];
			if ( now < r.DueAt ) continue;
			_raises.RemoveAt( i );
			Raise( scene, r );
		}
	}
}
