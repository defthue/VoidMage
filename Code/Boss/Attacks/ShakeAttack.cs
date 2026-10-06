using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The demonic shake: the skull drops low near the player and trembles on the spot, jaw clattering, eyes flaring,
/// the tremble growing (the tell), then screams: it rears, the eye fire boils over and a volley of slow green bolts
/// arcs out of the eye sockets at the player, one after another, each dodgeable on foot (<see cref="BossBolt"/>:
/// the host launches, every peer flies and judges its own). Then it sags to the floor, spent, and takes double
/// damage for the rest. The risk/reward window of the fight. The tremble is a small offset on a settled body,
/// never a rotation the orientation spring has to chase.
/// </summary>
public sealed class ShakeAttack : BossAttack
{
	public override string Name => "shake";
	public override bool UsesHand => false;

	[ConVar( "boss_shake_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_shake_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 0.8f;
	[ConVar( "boss_shake_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.2f;
	[ConVar( "boss_shake_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 1.2f;
	[ConVar( "boss_shake_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.2f;
	[ConVar( "boss_shake_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.7f;

	/// <summary>The volley: how many bolts leave the eyes during the scream, and the gap between them.</summary>
	[ConVar( "boss_shake_bolts", ConVarFlags.Saved )] public static int Bolts { get; set; } = 4;
	[ConVar( "boss_bolt_gap", ConVarFlags.Saved )] public static float BoltGap { get; set; } = 0.28f;
	[ConVar( "boss_shake_exposed", ConVarFlags.Saved )] public static float Exposed { get; set; } = 2f;
	[ConVar( "boss_shake_distance", ConVarFlags.Saved )] public static float ApproachDistance { get; set; } = 650f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;
	public override float RestMultiplier => Exposed;

	// host: which bolts of the running volley have left (per attack sequence)
	private int _launchedSeq = -1;
	private int _launched;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>The skull stands where it is when the lock comes (the lock only stamps the spot).</summary>
	public override Vector3 LockTarget( IBossFrame f, BossPartKind skull, Vector3 playerFeet ) => new( f.SkullCenter.x, f.SkullCenter.y, playerFeet.z );

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var s = f.S;
		var face = ctx.Facing * Rotation.FromPitch( f.SkullPitch );
		var lowZ = ctx.FloorZ + BiteAttack.LowClearance - f.BottomOffset( skull, face );
		// the approach point is fixed at the start (the lock only stamps where the skull sits)
		var toPlayer = (tracked - f.StancePoint).WithZ( 0f );
		var stand = ctx.Locked ? new Vector3( ctx.Target.x, ctx.Target.y, lowZ )
			: (toPlayer.IsNearZeroLength ? f.StancePoint.WithZ( lowZ ) : (tracked - toPlayer.Normal * ApproachDistance).WithZ( lowZ ));
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
				f.JawOpen01 = 0.2f * Ease( p );
				f.EyeFire01 = 1f;
				return MoveIntent.To( stand, BossDynamics.Approach, face, BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 0 );
			case BossStage.Tell:
			{
				// the convulsion: trembling on the spot, harder and harder, the jaw clattering
				var amp = Ease( p );
				f.JawOpen01 = 0.35f + 0.35f * MathF.Sin( t * 40f );
				f.EyeFire01 = 1f + amp;
				return SkullTremble( f, skull, t, amp, face, 1 );
			}
			case BossStage.Strike:
			{
				// the scream: rears up, mouth wide, eyes boiling, the tremble dying away as the volley leaves
				var amp = 1f - Ease( p );
				f.JawOpen01 = 1f;
				f.EyeFire01 = 2f;
				var (offset, jitter) = BossGeometry.Tremble( t, amp, s * 0.6f );
				return MoveIntent.To( stand + Vector3.Up * 130f * s, BossDynamics.Approach, face * Rotation.FromPitch( -14f ) * jitter, BossDynamics.TurnIdle )
					.WithClearance( BiteAttack.LowClearance ).Shifted( offset ).WithLean( 0f ).InPhase( 2 );
			}
			case BossStage.Rest:
				// spent: sags onto the floor, jaw hanging (the sag's contact is the thud)
				f.JawOpen01 = 0.8f;
				f.EyeFire01 = 0.6f;
				return MoveIntent.To( stand + Vector3.Down * 10f * s, BossDynamics.Tell, face * Rotation.FromPitch( 10f ), BossDynamics.TurnIdle )
					.WithClearance( MathF.Max( 0f, BiteAttack.LowClearance - 10f * s ) ).StopOnGround().InPhase( 3 );
			default:
			{
				var hover = f.SkullHover();
				f.JawOpen01 = 0.8f * (1f - Ease( p ));
				f.EyeFire01 = 1f;
				return MoveIntent.To( hover.Position, BossDynamics.Recover, hover.Rotation, BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 4 );
			}
		}
	}

	/// <summary>Host: the volley leaves the eyes on the scream's clock, each bolt aimed at the nearest player as it leaves (the bolts judge themselves).</summary>
	public override void HostTick( BossBrain brain, in BossAttackCtx ctx, float t )
	{
		if ( _launchedSeq != ctx.Seq ) { _launchedSeq = ctx.Seq; _launched = 0; }
		var screamAt = TellEnd( ctx );
		for ( var i = 0; i < Bolts; i++ )
		{
			var bit = 1 << i;
			if ( (_launched & bit) != 0 ) continue;
			if ( t < screamAt + i * BoltGap ) break;
			_launched |= bit;
			brain.LaunchBolt( ctx.Seq, i );
		}
	}

	/// <summary>Nothing to judge here: the bolts carry their own hazard on every peer.</summary>
	public override void Judge( BossBrain brain, BossPart skull, in BossAttackCtx ctx, float t, BossHazardState hz ) { }

	public override void Present( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Tell:
				Sound( "sounds/ambienceknock.sound", skull.AimPoint );
				HitFeel.Shake( 1.2f, 1.4f );
				break;
			case BossStage.Strike:
				Sound( "sounds/ambiencehorror.sound", skull.AimPoint );
				HitFeel.Shake( 5f, 0.7f );
				break;
		}
	}

	public override void OnImpact( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage == BossStage.Rest ) Sound( "sounds/debristhud.sound", at );
	}
}
