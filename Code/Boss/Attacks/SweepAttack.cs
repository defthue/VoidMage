using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The RIGHT hand's swipe: it comes down to the player's side with the palm's heel on the platform, palm toward
/// them, tracks, freezes trembling (the tell) and swings along the floor through the character and on to the far
/// side, through anything else in the way (the ground clamp is off for the whole swing). It lands on the far side
/// and lies there for the rest. Answer: step out of the line once it freezes.
/// </summary>
public sealed class SweepAttack : BossAttack
{
	public override string Name => "sweep";
	public override bool UsesHand => true;
	public override BossPartKind? OnlyHand => BossPartKind.HandR;

	[ConVar( "boss_sweep_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.4f;
	[ConVar( "boss_sweep_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.2f;
	[ConVar( "boss_sweep_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.6f;
	[ConVar( "boss_sweep_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.7f;
	[ConVar( "boss_sweep_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.2f;
	[ConVar( "boss_sweep_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.6f;

	[ConVar( "boss_sweep_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 15f;
	/// <summary>How far to each side of the player the swipe starts and ends, world units.</summary>
	[ConVar( "boss_sweep_reach", ConVarFlags.Saved )] public static float Reach { get; set; } = 700f;
	/// <summary>How far the palm's heel starts over the floor: 0 = on the platform, the swing runs along the floor through the player (higher: it scoops down from there).</summary>
	[ConVar( "boss_sweep_start_height", ConVarFlags.Saved )] public static float StartHeight { get; set; } = 0f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	private static Vector3 Side( BossPartKind hand, Rotation facing ) => hand == BossPartKind.HandL ? facing.Left : -facing.Left;

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var side = Side( hand, ctx.Facing );
		var rotSwipe = f.HandRot( hand, -side, Vector3.Up );          // palm leads the swipe, fingers up
		var rotDown = f.HandRot( hand, Vector3.Down, -side );          // lands palm down, fingers along the travel
		var target = ctx.Locked ? ctx.Target : tracked;
		// the swipe runs with the standing palm's heel a hair into the floor: through the body, not over it
		var swipeZ = FloorPivot( f, hand, ctx.FloorZ );
		var through = new Vector3( target.x, target.y, swipeZ );                       // the low point: the player's body
		var startPt = new Vector3( target.x, target.y, swipeZ + StartHeight ) + side * Reach;   // to the side, heel on the platform
		var endPt = through - side * Reach;
		var pulled = startPt + side * 240f;
		var lie = f.ClearSpot( new Vector3( endPt.x, endPt.y, FloorPivot( f, hand, ctx.FloorZ ) ) );   // recomputed as it turns flat: it settles onto the floor, clear of the rim's stones
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				var look = BossGeometry.PalmLead( f, hand, rotSwipe, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, startPt, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( 1 ).InPhase( 0 );
			}
			case BossStage.Tell:
				// trembles and draws back to its own side: the wind-up shows which way it will swing
				return HandTell( f, hand, ctx, t, pulled, rotSwipe, 5, 1, windup: side );
			case BossStage.Strike:
				// along the floor through the player (the waypoint at their body), aims past the end and stops there: a hard stop at the far side
				return MoveIntent.To( endPt - side * 260f, BossDynamics.Strike, rotSwipe, BossDynamics.TurnStrike )
					.Over( through, 140f ).NoGround().StopAt( 260f ).WithPose( 5 ).InPhase( 2 );
			case BossStage.Rest:
				// settles flat onto the floor at the far side (its landing is a real contact)
				return MoveIntent.To( lie + Vector3.Down * 120f, BossDynamics.Approach, rotDown, BossDynamics.TurnIdle )
					.PivotOverGround( FloorClamp( f, hand ) ).StopOnGround().WithPose( 1 ).InPhase( 3 );
			default:
			{
				var stance = f.HandStance( hand );
				return BossGeometry.HandTravel( f, hand, stance.Position, BossDynamics.Recover, stance.Rotation, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( p > 0.5f ? 0 : 1 ).InPhase( 4 );
			}
		}
	}

	public override void Judge( BossBrain brain, BossPart hand, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		var p = OwnPlayer();
		if ( p is null || hz.Hit ) return;
		var stage = StageAt( ctx, t, out _ );
		if ( stage != BossStage.Strike ) return;
		if ( !BodyInStrike( p, hand ) ) return;
		var side = Side( hand.Kind, ctx.Facing );
		hz.Hit = HitLocal( p, hand, Damage, -side * 260f + Vector3.Up * 80f, "boss_sweep" );
	}

	/// <summary>The swing's end (its hard stop at the far side) or the landing after it.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage is not (BossStage.Strike or BossStage.Rest) ) return;
		Sound( "sounds/debristhud.sound", at );
		HitFeel.Shake( 2f, 0.35f );
		BossVfx.FloorDust( at.WithZ( ctx.FloorZ ), hand.WorldBounds.Size.x * 0.3f );
	}
}
