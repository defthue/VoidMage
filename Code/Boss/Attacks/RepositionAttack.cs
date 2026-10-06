using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The reposition: a move that attacks nothing. The whole formation flies over a low arc to a new spot around the
/// player (a flank, or behind them: the decider chooses and hands the destination in `ctx.Aux`; the brain has
/// already made it the stance), the skull looking at the player the whole way, and the next move comes from there.
/// A Souls boss's sidestep, deliberate and readable, instead of a drift.
/// </summary>
public sealed class RepositionAttack : BossAttack
{
	public override string Name => "reposition";
	public override bool UsesHand => false;
	public override bool SilentLock => true;

	[ConVar( "boss_repo_seconds", ConVarFlags.Saved )] public static float Seconds { get; set; } = 1f;

	public override float Approach => 0f;
	public override float Tell => 0f;
	public override float LockAt => 0f;
	public override float Strike => Seconds;
	public override float Rest => 0f;
	public override float Recover => 0.2f;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var dest = ctx.AuxF > 0f ? ctx.Aux.WithZ( f.SkullHover().Position.z ) : f.SkullHover().Position;
		var to = (tracked - f.Position( skull )).WithZ( 0f );
		var look = (to.IsNearZeroLength ? ctx.Facing : Rotation.LookAt( to.Normal )) * Rotation.FromPitch( f.SkullPitch );
		var stage = StageAt( ctx, t, out _ );
		if ( stage == BossStage.Strike )
		{
			var from = f.Position( skull );
			var mid = (from + dest) * 0.5f + Vector3.Up * 200f * f.S;
			return MoveIntent.To( dest, BossDynamics.Approach, look, BossDynamics.TurnIdle ).Over( mid, 220f ).NoGround().InPhase( 0 );
		}
		return MoveIntent.To( f.SkullHover().Position, BossDynamics.Idle, look, BossDynamics.TurnIdle ).WithClearance( 50f ).InPhase( 1 );
	}

	public override void Judge( BossBrain brain, BossPart skull, in BossAttackCtx ctx, float t, BossHazardState hz ) { }
}
