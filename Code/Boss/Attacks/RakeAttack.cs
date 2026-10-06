using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The rake: a hand comes down claw-first on the floor well in front of the player (toward the skull), tracks,
/// freezes trembling (the tell) and DRAGS along the floor through them and on past, throwing dust the whole way,
/// then lies where the drag ended. A line at ground level: a sidestep clears it, a jump does too if the feet clear
/// the claw. Either hand.
/// </summary>
public sealed class RakeAttack : BossAttack
{
	public override string Name => "rake";
	public override bool UsesHand => true;

	[ConVar( "boss_rake_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.4f;
	[ConVar( "boss_rake_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.2f;
	[ConVar( "boss_rake_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.6f;
	[ConVar( "boss_rake_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.7f;
	[ConVar( "boss_rake_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.4f;
	[ConVar( "boss_rake_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.8f;

	[ConVar( "boss_rake_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 22f;
	/// <summary>Where the drag starts: this far from the player toward the skull, and how far past them it runs.</summary>
	[ConVar( "boss_rake_distance", ConVarFlags.Saved )] public static float StartDistance { get; set; } = 650f;
	[ConVar( "boss_rake_through", ConVarFlags.Saved )] public static float Through { get; set; } = 400f;

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
		var rotDrag = f.HandRot( hand, Vector3.Down, -back );    // palm down, the claws pointing at the player
		var rotDown = f.HandRot( hand, Vector3.Down, -back );
		// the claw drags with its tips a hair into the floor, whatever the curl: measured from the live pose every tick
		var z = FloorPivot( f, hand, ctx.FloorZ );
		var startPt = new Vector3( target.x, target.y, z ) + back * StartDistance;
		var pulled = startPt + back * 200f;
		var endPt = new Vector3( target.x, target.y, z ) - back * Through;
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				var look = BossGeometry.PalmLead( f, hand, rotDrag, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, startPt, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( 4 ).InPhase( 0 );
			}
			case BossStage.Tell:
				return HandTell( f, hand, ctx, t, pulled, rotDrag, 4, 1, windup: back );
			case BossStage.Strike:
			{
				// the drag, on a rail along the floor from where the tell left the hand to the far point: gathering speed the whole way
				if ( ctx.AuxF < 1f ) { ctx.Aux = f.Position( hand ); ctx.AuxF = 1f; }
				var q = MathF.Pow( p, 1.3f );
				var pos = Vector3.Lerp( ctx.Aux, endPt, q ).WithZ( z );
				return MoveIntent.To( pos, BossDynamics.Strike, rotDrag * Rotation.FromPitch( 8f ), BossDynamics.TurnStrike ).OnRail().NoGround().WithPose( 4 ).InPhase( 2 );
			}
			case BossStage.Rest:
			{
				// lies flat where the drag ended (clear of the rim's stones): the floor height follows the hand as its fingers open (the settle is a real contact)
				var lie = f.ClearSpot( new Vector3( endPt.x, endPt.y, ctx.FloorZ - 120f ) );
				return MoveIntent.To( lie, BossDynamics.Approach, rotDown, BossDynamics.TurnIdle )
					.PivotOverGround( FloorClamp( f, hand ) ).StopOnGround().WithPose( 1 ).InPhase( 3 );
			}
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
		var stage = StageAt( ctx, t, out _ );
		if ( stage != BossStage.Strike ) return;
		// the dust the claws throw, every peer, a picture
		if ( t >= hz.NextChip )
		{
			hz.NextChip = t + 0.09f;
			var at = hand.PivotWorld.WithZ( ctx.FloorZ + 10f );
			BreakVfx.Puff( brain.Scene, at, 55f );
		}
		var p = OwnPlayer();
		if ( p is null || hz.Hit ) return;
		if ( !BodyInStrike( p, hand ) ) return;
		var back = Back( brain, ctx.Target, ctx.Facing );
		hz.Hit = HitLocal( p, hand, Damage, -back * 320f + Vector3.Up * 100f, "boss_rake" );
	}

	public override void Present( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage entered, float t )
	{
		// the drag itself is on the floor already (a rail): its start is a real scrape
		if ( entered == BossStage.Strike )
		{
			Sound( "sounds/debristhud.sound", hand.AimPoint );
			HitFeel.Shake( 1.5f, 0.6f );
		}
	}

	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage != BossStage.Rest ) return;
		Sound( "sounds/debristhud.sound", at );
		HitFeel.Shake( 2.5f, 0.35f );
	}
}
