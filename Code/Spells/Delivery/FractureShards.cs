using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>
/// The Fracture: where a spell lands it throws smaller pieces of itself on from there. A piece is the same cast
/// through the Damage, Flight and Impact stages again (so it homes, bounces, burns a wake, becomes a mine with
/// everything else on the staff), flagged Shard so it never fractures, never multiplied, and never put on The
/// Orbit's ring (a piece born at a wall has no business circling the caster).
///
/// Off a BODY (user, 2026-09-20: "it doesnt fracture upon hitting an enemy"): the pieces used to be born 8 units off
/// the enemy's collider, inside their own trace radius, so every one landed on that same enemy in its first frame and
/// nothing was seen to break. Now they fan out level from the hit, all the way round, and pass through the body they
/// broke on (CastContext.IgnoreBody), so they go looking for the enemies standing around it. The same for the short
/// rays off the enemy a beam stops on (SpellBeam.IgnoreBodyId).
/// </summary>
public static class FractureShards
{
	public static void Spawn( CastContext ctx, Vector3 position, Vector3 normal, GameObject body = null )
	{
		var shards = ctx?.Impact?.FractureShards ?? 0;
		if ( shards <= 0 || ctx.Has( SpellHitFlags.Shard ) ) return;
		var offBody = DamageRouter.IsBody( body );
		if ( offBody || normal.IsNearZeroLength ) normal = Vector3.Up;
		var lean = offBody ? 0.08f : 0.45f;
		var start = Game.Random.Float( 0f, 360f );
		for ( var i = 0; i < shards; i++ )
		{
			// fanned round the surface normal, leaning away from it, mostly level so they reach the next body
			var around = Rotation.FromAxis( normal, start + 360f * i / shards ) * Tangent( normal );
			var direction = (around + normal * lean).Normal;
			var shard = ctx.Clone();
			shard.Flags |= SpellHitFlags.Shard;
			shard.Origin = offBody ? position : position + normal * 8f;
			shard.IgnoreBody = offBody ? body : null;
			shard.Direction = direction;
			shard.AimPoint = shard.Origin + direction * 300f;
			shard.FixedOrigin = true;
			shard.SourceScale = ctx.SourceScale * ModifierTables.FractureDamage;
			shard.SizeScale = ctx.SizeScale * ModifierTables.FractureSize;
			shard.ChargePower = ctx.ChargePower;
			shard.BeamWindowSeconds = 0.3f;
			shard.Count = 1;
			shard.FanDegrees = 0f;

			DamageStage.Run( shard );
			FlightStage.Run( shard );
			ImpactStage.Run( shard );
			shard.Flight.OrbitRings = 0;
			switch ( shard.Spell.Delivery )
			{
				case SpellDelivery.Instant: ChainLightningCast.Resolve( shard, shard.Origin, direction, null ); break;
				case SpellDelivery.Beam: BeamEmitter.Emit( shard ); break;
				default: ProjectileEmitter.Emit( shard ); break;
			}
		}
	}

	private static Vector3 Tangent( Vector3 normal )
	{
		var t = Vector3.Cross( normal, MathF.Abs( normal.z ) > 0.9f ? Vector3.Forward : Vector3.Up );
		return t.IsNearZeroLength ? Vector3.Forward : t.Normal;
	}
}
