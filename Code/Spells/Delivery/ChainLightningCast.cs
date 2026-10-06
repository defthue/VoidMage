using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// Chain Lightning: near-instant, from the staff to the nearest enemy the crosshair is near, then jumping to the
/// nearest unvisited enemy within reach, each link weaker. Multiplication forks it (distinct first targets),
/// Ricochet adds jumps AND banks a bolt that found nobody off the walls until a leg does (it stops at anything
/// breakable, like every other bolt), The Wake leaves each arc on the ground as a live line. The Hound widens the cone
/// the first link is looked for in (every link needs a clear line: The Needle stopped waiving that on 2026-09-22, it is
/// bodies only and the line already ignores bodies), The Horizon pays for reach, The Anvil
/// makes every link burst, The Return sends the bolt back down its own links, The Lattice leaves them burning a
/// moment, The Fracture throws forks on from the last link. With The Orbit, Amber or The Anvil on the staff the chain
/// rides a small carrier bolt (SpellProjectile) and resolves from wherever that stops or is triggered.
/// Every link is one SpellNet.Impact; the arcs go out once for the look.
/// </summary>
public static class ChainLightningCast
{
	public static void Emit( CastContext ctx )
	{
		var f = ctx.Flight;
		// a lobbed chain needs something to lob: the carrier, like a ring or a mine. Sloth rides one too: a slow bolt
		// is the only slowness an instant spell can be given (it was +100% damage for nothing, audit 2026-09-22).
		if ( f.Orbits || f.Halts || ctx.Stack( ModifierId.Anvil ) > 0 || ctx.Stack( ModifierId.Sloth ) > 0 )
		{
			ProjectileEmitter.Emit( ctx );
			return;
		}
		Resolve( ctx, ctx.Origin, ctx.Direction, null );
	}

	private static readonly List<ICombatant> _candidates = new();

	/// <summary>Resolve the chain from `from` looking along `direction`; `firstTarget` (a body the carrier touched) is the first link when given.</summary>
	/// <param name="damageScale">What this resolve is worth of the cast's damage (a carrier's Return and Horizon).</param>
	/// <param name="carried">Resolved from a carrier bolt: the emitter already threw Count of them, so this one is a single chain (Count carriers each forking Count times was x64 on three copies, audit 2026-09-22).</param>
	public static void Resolve( CastContext ctx, Vector3 from, Vector3 direction, GameObject firstTarget, float damageScale = 1f, bool carried = false )
	{
		var spell = ctx.Spell;
		var scene = ctx.Scene ?? Game.ActiveScene;
		if ( spell is null || !scene.IsValid() ) return;
		var jumps = spell.ChainJumps + ModifierTables.RicochetExtraJumps[ctx.Stack( ModifierId.Ricochet )];
		// The Needle on a chain that leaves the staff: the bolt starts on the enemies behind its first as well
		var forks = carried ? 1 : Math.Max( 1, ctx.Count ) + ModifierTables.NeedleChainStarts[ctx.Stack( ModifierId.Needle )];
		var cone = MathF.Max( spell.ChainConeDegrees, spell.ChainConeDegrees + ModifierTables.HoundSnapDegrees[ctx.Stack( ModifierId.Hound )] );
		var cosCone = MathF.Cos( cone.DegreeToRadian() );
		var caster = ctx.CasterRoot.IsValid() ? ctx.CasterRoot.WorldPosition : from;
		var impact = ctx.Impact;
		var ret = ctx.Stack( ModifierId.Return );
		var lattice = ctx.Flight?.LatticeRange ?? 0f;
		var visited = new HashSet<Guid>();
		var info = ctx.ToHitInfo();
		var scale = spell.VfxScale * ctx.SizeScale;

		// the first links: the bodies nearest the aim inside the cone, with a line of sight from the staff
		void Gather()
		{
			_candidates.Clear();
			foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
			{
				var chest = Chest( npc );
				var to = chest - from;
				var d = to.Length;
				if ( d > spell.ChainRange || d < 1f ) continue;
				var isFirst = firstTarget.IsValid() && npc.GameObject == firstTarget;
				if ( !isFirst && Vector3.Dot( to / d, direction ) < cosCone ) continue;
				if ( !isFirst && !Clear( scene, from, chest ) ) continue;
				_candidates.Add( npc );
			}
			_candidates.Sort( ( a, b ) =>
			{
				// the touched body first, then by how close to the aim line, then by distance
				var fa = firstTarget.IsValid() && a.GameObject == firstTarget; var fb = firstTarget.IsValid() && b.GameObject == firstTarget;
				if ( fa != fb ) return fa ? -1 : 1;
				var da = Vector3.Dot( (Chest( a ) - from).Normal, direction ); var db = Vector3.Dot( (Chest( b ) - from).Normal, direction );
				var c = db.CompareTo( da );
				return c != 0 ? c : Chest( a ).DistanceSquared( from ).CompareTo( Chest( b ).DistanceSquared( from ) );
			} );
		}
		Gather();

		// Nothing to zap: the bolt crosses the room. With Ricochet it banks off whatever cannot be destroyed and looks
		// again from there, along the reflected line, so a bolt thrown at a wall finds the pack round the corner.
		// `path` is the way it came; the arcs below start from it.
		var path = new List<Vector3> { from };
		var bounces = ModifierTables.RicochetBounces[ctx.Stack( ModifierId.Ricochet )];
		while ( _candidates.Count == 0 )
		{
			var leg = scene.Trace.Ray( from, from + direction * spell.ChainRange ).WithoutTags( AimSolver.IgnoreTags ).IgnoreGameObjectHierarchy( ctx.CasterRoot ).Run();
			var end = leg.Hit ? leg.HitPosition : leg.EndPosition;
			var prop = leg.Hit && leg.GameObject.IsValid() ? leg.GameObject.Components.Get<BreakableProp>( FindMode.EverythingInSelfAndAncestors ) : null;
			var tell = leg.Hit && leg.GameObject.IsValid() && leg.GameObject.Components.Get<SecretWall>( FindMode.EverythingInSelfAndAncestors ) is not null;
			if ( !leg.Hit || bounces <= 0 || prop is not null || tell )
			{
				// the end of the line: a crate it ran into takes the bolt (the carrier's Land opens the secret wall itself)
				prop?.ReceiveHit( new DamageRouter.Hit { Damage = ctx.Damage * damageScale, Position = end, Push = direction.WithZ( 0f ).Normal, Attacker = ctx.CasterRoot, Kind = spell.Kind, Spell = info } );
				if ( tell && path.Count > 1 ) SecretWallNet.Blast( end, 24f );
				path.Add( end );
				SpellNet.ChainArc( spell.Id, path, scale );
				return;
			}
			bounces--;
			path.Add( end );
			direction = Vector3.Reflect( direction, leg.Normal ).Normal;
			from = end + leg.Normal * 2f;
			Gather();
		}

		var starts = Math.Min( forks, _candidates.Count );
		var firstLinks = new List<ICombatant>();
		for ( var i = 0; i < starts; i++ ) { firstLinks.Add( _candidates[i] ); visited.Add( _candidates[i].GameObject.Id ); }

		foreach ( var first in firstLinks )
		{
			var points = new List<Vector3>( path );
			var struck = new List<(GameObject body, Vector3 chest, float damage)>();
			var damage = ctx.Damage * damageScale;
			var current = first;
			var prev = from;
			for ( var link = 0; link <= jumps && current.IsValid(); link++ )
			{
				var chest = Chest( current );
				points.Add( chest );
				var push = (chest - prev).WithZ( 0f );
				// The Horizon pays for how far from the caster the link lands; The Anvil makes every link burst
				var dealt = damage * ModifierTables.HorizonScale( ctx.Flight?.HorizonStacks ?? 0, caster.Distance( chest ) );
				SpellNet.Impact( chest, Vector3.Up, current.GameObject, dealt, push.IsNearZeroLength ? Vector3.Zero : push.Normal, ctx.CasterRoot, info,
					impact.SplashRadius * 0.5f, impact.SplashFraction, impact.SplashMaxTargets );
				struck.Add( (current.GameObject, chest, dealt) );
				if ( ctx.Flight.Wakes )
					SpellNet.AddHazard( HazardKind.Capsule, prev, chest, ctx.Flight.WakeWidth * 0.5f, ctx.Flight.WakeWidth, 0f, ctx.Flight.WakeSeconds, null,
						SpellHostTuning.WakeDps( spell ), SpellHostTuning.WakeSlow( spell ), ctx.CasterRoot, info );
				else if ( lattice > 0f )
					// The Lattice: the link stays in the air a moment and burns what crosses it
					SpellNet.AddHazard( HazardKind.Capsule, prev, chest, 14f, 28f, 0f, 1f + 0.5f * ctx.Stack( ModifierId.Lattice ), null,
						dealt * ModifierTables.LatticeDamageFraction * SpellHostTuning.DotHz, 0f, ctx.CasterRoot, info );
				prev = chest;
				damage *= spell.ChainFalloff;
				current = link < jumps ? Nearest( scene, chest, spell.ChainJumpRange, visited ) : null;
				if ( current.IsValid() ) visited.Add( current.GameObject.Id );
			}
			SpellNet.ChainArc( spell.Id, points, scale );

			// The Return: the bolt runs back down its own links
			if ( ret > 0 && struck.Count > 0 && !ctx.Has( SpellHitFlags.Shard ) )
			{
				var back = new List<Vector3>();
				for ( var i = struck.Count - 1; i >= 0; i-- )
				{
					back.Add( struck[i].chest );
					if ( i == struck.Count - 1 && struck.Count > 1 ) continue;   // the last link was only just struck; alone, it takes the echo
					if ( struck[i].body.IsValid() )
						SpellNet.Impact( struck[i].chest, Vector3.Up, struck[i].body, struck[i].damage * ModifierTables.ReturnDamage[ret] * 0.5f, Vector3.Zero, ctx.CasterRoot, info, 0f, 0f, 0 );
				}
				back.Add( from );
				SpellNet.ChainArc( spell.Id, back, scale * 0.7f );
			}

			// The Fracture: forks thrown on from the last link
			if ( struck.Count > 0 ) FractureShards.Spawn( ctx, struck[^1].chest, Vector3.Up );
		}
	}

	private static Vector3 Chest( ICombatant body ) => body.AimPoint;

	private static bool Clear( Scene scene, Vector3 a, Vector3 b )
	{
		var tr = scene.Trace.Ray( a, b ).WithoutTags( SpellArea.LosIgnore ).Run();
		return !tr.Hit;
	}

	private static ICombatant Nearest( Scene scene, Vector3 at, float range, HashSet<Guid> visited )
	{
		ICombatant best = null;
		var bestD = range * range;
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
		{
			if ( visited.Contains( npc.GameObject.Id ) ) continue;
			var chest = Chest( npc );
			var d = chest.DistanceSquared( at );
			if ( d >= bestD ) continue;
			if ( !Clear( scene, at, chest ) ) continue;
			bestD = d; best = npc;
		}
		return best;
	}
}
