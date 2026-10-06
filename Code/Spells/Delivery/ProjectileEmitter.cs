using System;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// Puts the cast's bolts in the world: Count of them fanned over FanDegrees (or thrown into a random cone when the
/// spell spreads, the swarm), times the rings of The Orbit (each ring takes the whole fan, spread evenly around the
/// caster). Configured before NetworkSpawn so the snapshot carries the spell id and the direction. Per-bolt random
/// numbers are rolled here, on the owner, because every bolt of a cast shares the one CastContext.
/// </summary>
public static class ProjectileEmitter
{
	public static void Emit( CastContext ctx )
	{
		var f = ctx.Flight;
		// a miniature born away from the staff (a wisp's, the hail's, the retort's, a shard) flies; it does not circle the caster (the hail never fell under The Orbit)
		var orbits = f.Orbits && !ctx.FixedOrigin;
		var rings = orbits ? f.OrbitRings : 1;
		var count = Math.Max( 1, ctx.Count );
		var baseYaw = ctx.Direction.WithZ( 0f ).IsNearZeroLength ? 0f : ctx.Direction.WithZ( 0f ).Normal.EulerAngles.yaw;
		var spread = ctx.Spell.SpreadDegrees;
		// The Anvil: thrown, not shot. A cast that was already falling from the ceiling (the hail) is left alone.
		var loft = ctx.Stack( ModifierId.Anvil ) > 0 && f.Base == FlightBase.Arc && ctx.Direction.z > -0.5f ? ModifierTables.AnvilLoftDegrees : 0f;

		for ( var ring = 0; ring < rings; ring++ )
		{
			for ( var i = 0; i < count; i++ )
			{
				var direction = spread > 0f
					? SpreadDirection( ctx.Direction, MathF.Max( spread, ctx.FanDegrees * 0.5f ) )
					: FanDirection( ctx.Direction, i, count, ctx.FanDegrees );
				if ( loft > 0f )
				{
					var lofted = direction.EulerAngles;
					lofted.pitch -= loft;
					direction = lofted.Forward;
				}
				var go = new GameObject( true, $"spell {ctx.Spell.Id}" ) { WorldPosition = ctx.Origin, WorldRotation = Rotation.LookAt( direction ) };
				go.Flags |= GameObjectFlags.NotSaved;
				go.Tags.Add( "spellfire" );
				var bolt = go.Components.Create<SpellProjectile>();
				bolt.SpellId = ctx.Spell.Id;
				bolt.Direction = direction;
				bolt.Scale = ctx.Spell.VfxScale * ctx.SizeScale;
				bolt.RingIndex = ring;
				bolt.Phase = orbits ? (int)SpellProjectile.FlightPhase.Orbit : (int)SpellProjectile.FlightPhase.Flight;
				bolt.Context = ctx;
				bolt.PassThrough( ctx.IgnoreBody );
				bolt.OrbitStartDegrees = baseYaw + 360f * i / count + ring * (180f / Math.Max( 1, rings ));
				bolt.Seed = Game.Random.Float( 0f, 1000f );
				go.NetworkSpawn();
			}
		}
	}

	/// <summary>A random direction inside a cone of `degrees` around `aim` (uniform over the disc, so the middle is not favoured).</summary>
	public static Vector3 SpreadDirection( Vector3 aim, float degrees )
	{
		if ( degrees <= 0f ) return aim;
		var r = degrees * MathF.Sqrt( Game.Random.Float( 0f, 1f ) );
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		var angles = aim.EulerAngles;
		angles.yaw += MathF.Cos( a ) * r;
		angles.pitch += MathF.Sin( a ) * r;
		return angles.Forward;
	}

	/// <summary>Bolt `i` of `count` spread over `fan` degrees of yaw around `aim`; eight bolts also split into two rows of pitch.</summary>
	public static Vector3 FanDirection( Vector3 aim, int i, int count, float fan )
	{
		if ( count <= 1 || fan <= 0f ) return aim;
		var rows = count >= 8 ? 2 : 1;
		var perRow = count / rows;
		var row = i / perRow;
		var col = i % perRow;
		var t = perRow <= 1 ? 0f : (col / (float)(perRow - 1)) - 0.5f;
		var yaw = t * fan;
		var pitch = rows == 1 ? 0f : (row == 0 ? -4f : 4f);
		var angles = aim.EulerAngles;
		angles.yaw += yaw;
		angles.pitch += pitch;
		return angles.Forward;
	}
}
