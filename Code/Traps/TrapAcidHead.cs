using System;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The acid head: a nozzle low on the wall beside a room's door, dripping (the tell, with a stain on the tile).
/// Step onto the doorway cell and it gurgles, then sprays the cell: a direct splash on whoever stands there, and a
/// pool that lingers on the tile and eats at anyone standing in it. The way past is to wait for the pool to dry, or
/// to cross before the head has sprayed. Pressure is decided by feet on the cell; the pool hurts feet in it.
/// </summary>
[Title( "Trap: Acid Head" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapAcidHead : TrapBase
{
	/// <summary>Seconds of gurgle between the step and the spray.</summary>
	[ConVar( "trap_acid_windup" )] public static float Windup { get; set; } = 0.35f;
	/// <summary>Seconds the spray lasts.</summary>
	[ConVar( "trap_acid_spray" )] public static float SpraySeconds { get; set; } = 0.6f;
	/// <summary>Seconds the pool lingers on the tile.</summary>
	[ConVar( "trap_acid_pool" )] public static float PoolSeconds { get; set; } = 6f;
	/// <summary>Seconds after the pool dries before the head listens again.</summary>
	[ConVar( "trap_acid_reset" )] public static float ResetSeconds { get; set; } = 1.5f;
	/// <summary>Damage of the direct splash.</summary>
	[ConVar( "trap_acid_damage" )] public static float Damage { get; set; } = 10f;
	/// <summary>Damage per tick while standing in the pool, and the tick.</summary>
	[ConVar( "trap_acid_pool_damage" )] public static float PoolDamage { get; set; } = 4f;
	[ConVar( "trap_acid_pool_tick" )] public static float PoolTick { get; set; } = 0.5f;

	public const string DamageKind = "trap_acid";

	/// <summary>The fired trap's effects, built by the dungeon builder.</summary>
	public TrapAcidVfx.Rig Rig { get; set; }
	/// <summary>World box of the pool on the tile (feet in it burn).</summary>
	public BBox PoolBox { get; set; }

	private float _nextTick;
	private bool _pooling;

	public override float WindupSeconds => Windup;

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		switch ( State )
		{
			case TrapState.WindUp:
				if ( StateAge >= Windup )
				{
					Enter( TrapState.Fire );
					TrapAcidVfx.Spray( Rig, true );
					_nextTick = now;
					_pooling = false;
				}
				break;

			case TrapState.Fire:
				// the spray: a splash on everyone standing in the cell, once
				if ( !_pooling && StateAge >= 0.15f )
				{
					HitInside( targets, Damage, DamageKind, _ => Vector3.Zero, _ => Vector3.Zero );
				}
				if ( !_pooling && StateAge >= SpraySeconds * 0.6f )
				{
					_pooling = true;
					TrapAcidVfx.PoolStart( Rig, PoolSeconds );
					HitThisFire.Clear();
					_nextTick = now + PoolTick;
				}
				if ( StateAge >= SpraySeconds ) TrapAcidVfx.Spray( Rig, false );
				// the pool: ticks on feet in it (the pool box, not the trap's tall damage box)
				if ( _pooling && now >= _nextTick )
				{
					_nextTick = now + MathF.Max( 0.3f, PoolTick );
					HitThisFire.Clear();
					PoolHits( targets );
				}
				if ( StateAge >= SpraySeconds * 0.6f + PoolSeconds )
				{
					TrapAcidVfx.PoolStop( Rig );
					Enter( TrapState.Reset );
				}
				break;

			case TrapState.Reset:
				if ( StateAge >= ResetSeconds ) Enter( TrapState.Armed );
				break;
		}
	}

	private void PoolHits( TrapSystem.Targets targets )
	{
		foreach ( var p in targets.Players )
		{
			if ( !p.IsValid() || !Touches( PoolBox, p.FeetPosition, p.BodyHeight ) || HitThisFire.Contains( p.GameObject.Id ) ) continue;
			if ( HitPlayer( p, PoolDamage, Vector3.Zero, p.FeetPosition + Vector3.Up * 10f, DamageKind ) ) HitThisFire.Add( p.GameObject.Id );
		}
		foreach ( var n in targets.Skeletons )
		{
			if ( !n.IsValid() || !Touches( PoolBox, n.WorldPosition, n.BodyHeight ) || HitThisFire.Contains( n.GameObject.Id ) ) continue;
			if ( HitSkeleton( n, PoolDamage, Vector3.Zero, n.WorldPosition + Vector3.Up * 10f, DamageKind ) ) HitThisFire.Add( n.GameObject.Id );
		}
	}

	public override string DebugLabel() => base.DebugLabel() + (_pooling ? " pool" : "");
}
