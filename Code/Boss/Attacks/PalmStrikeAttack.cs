using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The LEFT hand's frontal palm strike: the open hand comes down in front of the skull with the palm's heel on the
/// platform, palm to the player, draws back (the tell) and thrusts along the floor straight at them, stopping just
/// past where they stood: a hit player is sent flying away from the skull (knocked back, not run through), then the
/// hand drops palm-down onto the floor for the rest. A line, not a sweep: a sidestep clears it. The ground clamp is off for the thrust.
/// </summary>
public sealed class PalmStrikeAttack : BossAttack
{
	public override string Name => "palm";
	public override bool UsesHand => true;
	public override BossPartKind? OnlyHand => BossPartKind.HandL;

	[ConVar( "boss_palm_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.4f;
	[ConVar( "boss_palm_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.2f;
	[ConVar( "boss_palm_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.6f;
	[ConVar( "boss_palm_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.5f;
	[ConVar( "boss_palm_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.2f;
	[ConVar( "boss_palm_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.6f;

	[ConVar( "boss_palm_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 18f;
	/// <summary>Where the thrust starts, this far from the player toward the skull, and how far past them it runs.</summary>
	[ConVar( "boss_palm_distance", ConVarFlags.Saved )] public static float StartDistance { get; set; } = 600f;
	[ConVar( "boss_palm_through", ConVarFlags.Saved )] public static float Through { get; set; } = 160f;
	/// <summary>The blow: how hard a hit player is sent flying away from the skull (u/s) and up. The palm stops just past where they stood: they are knocked back, not run through.</summary>
	[ConVar( "boss_palm_knockback", ConVarFlags.Saved )] public static float Knockback { get; set; } = 900f;
	[ConVar( "boss_palm_knockup", ConVarFlags.Saved )] public static float Knockup { get; set; } = 200f;
	/// <summary>How far the palm's heel starts over the floor: 0 = on the platform, the thrust runs along the floor through the player (higher: it dives from there).</summary>
	[ConVar( "boss_palm_start_height", ConVarFlags.Saved )] public static float StartHeight { get; set; } = 0f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>From the player toward the skull, flat.</summary>
	private static Vector3 Back( IBossFrame f, Vector3 target, Rotation facing )
	{
		var b = (f.StancePoint - target).WithZ( 0f );
		return b.IsNearZeroLength ? -facing.Forward : b.Normal;
	}

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var target = ctx.Locked ? ctx.Target : tracked;
		var back = Back( f, target, ctx.Facing );
		var rotPalm = f.HandRot( hand, -back, Vector3.Up );      // palm to the player, fingers up
		var rotDown = f.HandRot( hand, Vector3.Down, -back );    // lands palm down, fingers onward
		// the thrust runs with the standing palm's heel a hair into the floor: through the body
		var z = FloorPivot( f, hand, ctx.FloorZ );
		var through = new Vector3( target.x, target.y, z );                               // the low point: the player's body
		var startPt = new Vector3( target.x, target.y, z + StartHeight ) + back * StartDistance;   // in front of the skull, heel on the platform
		var pulled = startPt + back * 220f;
		var endPt = through - back * Through;
		var lie = f.ClearSpot( new Vector3( endPt.x, endPt.y, FloorPivot( f, hand, ctx.FloorZ ) ) );   // recomputed as it turns flat: it settles onto the floor, clear of the rim's stones
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				// straight in under the skull's face over a low arc: the start point sits inside the skull's keep-out
				// on purpose (the hand comes down right in front of the mouth), so no detour logic here
				var look = BossGeometry.PalmLead( f, hand, rotPalm, f.Velocity( hand ) );
				var from = f.Position( hand );
				return MoveIntent.To( startPt, BossDynamics.Approach, look, BossDynamics.TurnIdle )
					.Over( (from + startPt) * 0.5f + Vector3.Up * BossDynamics.ArcHeight * 0.5f, 200f ).NoGround().WithPose( 1 ).InPhase( 0 );
			}
			case BossStage.Tell:
				// trembles and draws back toward the skull: the wind-up shows the thrust is coming straight at you
				return HandTell( f, hand, ctx, t, pulled, rotPalm, 5, 1, windup: back );
			case BossStage.Strike:
				// along the floor through the player (the waypoint at their body) and on past them
				return MoveIntent.To( endPt - back * 260f, BossDynamics.Strike, rotPalm, BossDynamics.TurnStrike )
					.Over( through, 140f ).NoGround().StopAt( 260f ).WithPose( 5 ).InPhase( 2 );
			case BossStage.Rest:
				// settles flat onto the floor past the player (its landing is a real contact)
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
		var back = Back( brain, ctx.Target, ctx.Facing );
		hz.Hit = HitLocal( p, hand, Damage, -back * Knockback + Vector3.Up * Knockup, "boss_palm" );
	}

	/// <summary>The thrust's end (its hard stop past the player) or the landing after it.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage is not (BossStage.Strike or BossStage.Rest) ) return;
		Sound( "sounds/debristhud.sound", at );
		HitFeel.Shake( 2f, 0.35f );
		BossVfx.FloorDust( at.WithZ( ctx.FloorZ ), hand.WorldBounds.Size.x * 0.3f );
	}
}
