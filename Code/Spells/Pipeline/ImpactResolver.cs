using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>
/// Where a spell lands, owner side: the local things a blast does (breakables fly, pressure plates go off, a prop
/// that was hit directly takes the blow) and ONE request to the host for the bodies (the direct victim, the splash). The host never hears about props; the owner never enumerates bodies.
/// </summary>
public static class ImpactResolver
{
	/// <param name="damageScale">What this landing is worth of the cast's resolved damage (The Return's way back, The Horizon's distance).</param>
	public static void Resolve( CastContext ctx, Vector3 position, Vector3 normal, GameObject hitObject, Vector3 pushDir, float damageScale = 1f )
	{
		if ( ctx is null ) return;
		var damage = ctx.Damage * damageScale;
		LocalExtras( ctx, position, normal );

		// a prop hit square on takes the blow itself (BreakableProp is an IHittable and relays through BreakableNet)
		if ( hitObject.IsValid() && !DamageRouter.IsBody( hitObject ) && hitObject.Components.Get<BreakableProp>( FindMode.EverythingInSelfAndAncestors ) is { } prop )
		{
			prop.ReceiveHit( new DamageRouter.Hit { Damage = damage, Position = position, Push = pushDir, Attacker = ctx.CasterRoot, Kind = ctx.Spell.Kind, Spell = ctx.ToHitInfo() } );
		}

		// the secret room's tell, hit square on: any spell opens it, one hit. Looked up explicitly rather than as an
		// IHittable, because IsBody is false for the player root too and a generic lookup would let a bolt hit its
		// own caster through this path.
		if ( hitObject.IsValid() && !DamageRouter.IsBody( hitObject ) && hitObject.Components.Get<SecretWall>( FindMode.EverythingInSelfAndAncestors ) is { } secret )
		{
			secret.ReceiveHit( new DamageRouter.Hit { Damage = damage, Position = position, Push = pushDir, Attacker = ctx.CasterRoot, Kind = ctx.Spell.Kind, Spell = ctx.ToHitInfo() } );
		}

		var direct = DamageRouter.IsBody( hitObject ) ? hitObject : null;
		var impact = ctx.Impact;
		SpellNet.Impact( position, normal, direct, damage, pushDir, ctx.CasterRoot, ctx.ToHitInfo(), impact.SplashRadius, impact.SplashFraction, impact.SplashMaxTargets );
	}

	/// <summary>The blast's effect on the dungeon, run by the owner: an explosive spell throws crates and sets off plates.</summary>
	public static void LocalExtras( CastContext ctx, Vector3 position, Vector3 normal )
	{
		if ( ctx?.Impact is null || ctx.Impact.SplashRadius <= 0f ) return;
		BreakableNet.Blast( position, BreakableNet.BlastRadius, localAttacker: true );
		Traps.TrapNet.Blast( position, Traps.TrapNet.FireballRadius );
		SecretWallNet.Blast( position, SecretWallNet.SplashRadius );
	}
}
