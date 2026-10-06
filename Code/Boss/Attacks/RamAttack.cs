using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The ram: the skull draws far back across from the player at its flying height, gapes, tracks them, then the
/// jaw clicks (the lock), it rears and CHARGES flat across the arena with its chin skimming the floor: the cranium
/// rides a scripted line (a rail) through where the player stood and on past them, then crashes down on the
/// platform and lies stunned (the reward window, exposed), the formation hands landing beside it. Then the way
/// home: a wide arc back round to the hover in front of the player. Answer: a sidestep out of the line once it
/// clicks, then run to the crash.
/// </summary>
public sealed class RamAttack : BossAttack
{
	public override string Name => "ram";
	public override bool UsesHand => false;

	[ConVar( "boss_ram_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_ram_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 0.8f;
	[ConVar( "boss_ram_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.4f;
	[ConVar( "boss_ram_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 1.1f;
	[ConVar( "boss_ram_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.6f;
	[ConVar( "boss_ram_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 1.3f;

	[ConVar( "boss_ram_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 40f;
	/// <summary>Where the skull stands before the charge: this far from the player on the skull's side, world units.</summary>
	[ConVar( "boss_ram_distance", ConVarFlags.Saved )] public static float ApproachDistance { get; set; } = 1300f;
	/// <summary>How far past the player the charge carries on before the crash (clamped to the platform).</summary>
	[ConVar( "boss_ram_through", ConVarFlags.Saved )] public static float Through { get; set; } = 1500f;
	/// <summary>The crash lands within this of the arena's centre: on the platform, in sword reach.</summary>
	[ConVar( "boss_ram_crash_radius", ConVarFlags.Saved )] public static float CrashRadius { get; set; } = 1300f;
	/// <summary>The cranium's reach around the skull centre for the swept hit, model units (x the scale).</summary>
	[ConVar( "boss_ram_radius", ConVarFlags.Saved )] public static float Radius { get; set; } = 190f;
	/// <summary>Damage multiplier on the skull while it lies crashed.</summary>
	[ConVar( "boss_ram_exposed", ConVarFlags.Saved )] public static float Exposed { get; set; } = 1.5f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;
	public override float RestMultiplier => Exposed;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>From the player toward the skull, flat (the stance side on the host; the facing on every peer).</summary>
	private static Vector3 Back( IBossFrame f, Vector3 target, Rotation facing )
	{
		var b = (f.StancePoint - target).WithZ( 0f );
		return b.IsNearZeroLength ? -facing.Forward : b.Normal;
	}

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var target = ctx.Locked ? ctx.Target : tracked;
		var back = Back( f, target, ctx.Facing );
		var face = Rotation.LookAt( -back ) * Rotation.FromPitch( f.SkullPitch );
		var chargeLook = Rotation.LookAt( -back ) * Rotation.FromPitch( f.SkullPitch + 10f );
		var flyZ = f.SkullHover().Position.z;
		var stand = new Vector3( target.x, target.y, flyZ ) + back * ApproachDistance;
		var reared = stand + back * 250f + Vector3.Up * 120f;
		// the charge height: the chin skims the floor (the same rule as the shake's low stand)
		var chargeZ = ctx.FloorZ + BiteAttack.LowClearance - f.BottomOffset( skull, chargeLook );
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
				f.JawOpen01 = BossGeometry.Laugh( t ) * Ease( p * 3f );
				f.EyeFire01 = 1f;
				return MoveIntent.To( stand, BossDynamics.Approach, face, BossDynamics.TurnIdle ).NoGround().InPhase( 0 );
			case BossStage.Tell:
			{
				if ( p < LockAt )
				{
					f.JawOpen01 = BossGeometry.Laugh( t );
					return MoveIntent.To( stand, BossDynamics.Track, face, BossDynamics.TurnIdle ).NoGround().InPhase( 1 );
				}
				// rears back with the face tipped up, gaping, then freezes trembling until the charge
				var rear = Ease( (p - LockAt) / MathF.Max( 0.01f, 1f - LockAt ) );
				f.JawOpen01 = MathX.Lerp( BossGeometry.Laugh( t ), 1f, rear );
				f.EyeFire01 = 1f + rear;
				if ( rear < 0.5f )
					return MoveIntent.To( reared, BossDynamics.Tell, face * Rotation.FromPitch( -12f * rear ), BossDynamics.TurnIdle ).NoGround().InPhase( 2 );
				return SkullTremble( f, skull, t, (rear - 0.5f) * 2f, face * Rotation.FromPitch( -6f ), 3 );
			}
			case BossStage.Strike:
			{
				// the charge, on a rail from wherever the tell left the skull to the crash point: slow out of the rear, flat out at the end, and the crash is the stop
				if ( ctx.AuxF < 1f ) { ctx.Aux = f.Position( skull ); ctx.AuxF = 1f; }
				var start = ctx.Aux;
				var far = f.ClearSpot( f.ClampToArena( new Vector3( target.x, target.y, chargeZ ) - back * Through, CrashRadius ), 250f ).WithZ( chargeZ );
				var q = MathF.Pow( p, 1.6f );
				var flat = Vector3.Lerp( start, far, q );
				var dive = 0.5f - 0.5f * MathF.Cos( Math.Clamp( q / 0.25f, 0f, 1f ) * MathF.PI );
				var pos = new Vector3( flat.x, flat.y, MathX.Lerp( start.z, chargeZ, dive ) );
				var total = (far - start).WithZ( 0f ).Length;
				var qHit = total > 1f ? (target - start).WithZ( 0f ).Length / total : 0.5f;
				// the jaw: wide until the mouth passes the player, snaps shut through them, hangs a little after
				f.JawOpen01 = q < qHit - 0.05f ? 1f : q < qHit + 0.03f ? 1f - (q - (qHit - 0.05f)) / 0.08f : 0.2f;
				f.EyeFire01 = 2f;
				return MoveIntent.To( pos, BossDynamics.Strike, chargeLook, BossDynamics.TurnStrike ).OnRail().NoGround().InPhase( 4 );
			}
			case BossStage.Rest:
			{
				// crashed: drops onto the floor where the rail ended, chin down, a little rolled (the contact is the crash)
				f.JawOpen01 = 0.7f;
				f.EyeFire01 = 0.6f;
				var here = f.Position( skull );
				var lie = new Vector3( here.x, here.y, ctx.FloorZ - 400f );
				var rot = Rotation.LookAt( -back ) * Rotation.FromPitch( f.SkullPitch + 22f ) * Rotation.FromRoll( 12f );
				return MoveIntent.To( lie, BossDynamics.Strike, rot, BossDynamics.TurnStrike )
					.WithClearance( BiteAttack.LowClearance ).StopOnGround().Braked().InPhase( 5 );
			}
			default:
				f.JawOpen01 = 0.7f * (1f - Ease( p ));
				f.EyeFire01 = 1f;
				return ReturnHome( f, skull, ref ctx, p, 6 );
		}
	}

	public override void Judge( BossBrain brain, BossPart skull, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		var p = OwnPlayer();
		if ( p is null || hz.Hit ) return;
		var stage = StageAt( ctx, t, out _ );
		if ( stage != BossStage.Strike ) { hz.HasPrev = false; return; }
		// the cranium's path since last frame (the pass is a frame or two long) plus the shapes at this frame
		var s = MathF.Max( 0.01f, skull.WorldScale.x );
		var front = skull.WorldPosition + skull.WorldRotation.Forward * 60f * s;
		var swept = hz.HasPrev && SweptHits( p, hz.PrevPoint, front, Radius * s - HitForgive );
		hz.PrevPoint = front;
		hz.HasPrev = true;
		if ( !swept && !BodyInPart( p, skull ) ) return;
		var heading = skull.WorldRotation.Forward.WithZ( 0f ).Normal;
		hz.Hit = HitLocal( p, skull, Damage, heading * 520f + Vector3.Up * 160f, "boss_ram" );
	}

	public override void Present( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Tell:
				Sound( "sounds/ambienceknock.sound", skull.AimPoint );
				HitFeel.Shake( 0.8f, 0.6f );
				break;
			case BossStage.Strike:
				Sound( "sounds/ambiencehorror.sound", skull.AimPoint );
				break;
		}
	}

	/// <summary>The crash: the skull hit the floor for real.</summary>
	public override void OnImpact( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage < BossStage.Rest ) return;
		var floor = at.WithZ( ctx.FloorZ );
		Sound( "sounds/debristhud.sound", floor );
		HitFeel.Shake( 6f, 0.7f );
		Rendering.RetroFilter.TriggerRipple( floor, 1.4f );
		BreakVfx.Stone( brain.Scene, floor + Vector3.Up * 30f, 200f, Vector3.Up * 160f, false );
		BossVfx.Shockwave( floor, 120f, SlamAttack.RingSpeed, SlamAttack.RingMax, 0.8f );
	}
}
