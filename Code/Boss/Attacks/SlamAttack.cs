using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The hand rises high over the player, hangs there (the tell), drops, and a floor shockwave runs outward from
/// the impact. Answer: leave the spot under the hand during the hang, then jump the ring. The hand stays on the
/// floor for the rest: the sword's window. Two variants share the timeline: `slam`, the open flat palm clapping
/// the ground, and `hammer`, a fist turned on its side coming down like a fist on a table. The opener after the
/// wake-up is the slam at 0.7 speed.
/// </summary>
public sealed class SlamAttack : BossAttack
{
	private readonly bool _hammer;

	public SlamAttack( bool hammer ) { _hammer = hammer; }

	public override string Name => _hammer ? "hammer" : "slam";
	public override bool UsesHand => true;

	[ConVar( "boss_slam_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.4f;
	[ConVar( "boss_slam_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.2f;
	[ConVar( "boss_slam_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.5f;
	[ConVar( "boss_slam_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.36f;
	[ConVar( "boss_slam_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.6f;
	[ConVar( "boss_slam_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.6f;

	[ConVar( "boss_slam_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 25f;
	/// <summary>The hang height of the palm centre over the floor, world units.</summary>
	[ConVar( "boss_slam_height", ConVarFlags.Saved )] public static float HangHeight { get; set; } = 620f;
	/// <summary>
	/// Where the PALM CENTRE stops over the floor on impact, world units: the flat palm lands with the palm bones on
	/// the ground (the claws go into it), the sideways fist with its pinky edge on the ground. The hit shapes are the
	/// sculpt's claw, whose tips hang 147 u under the palm, so the lowest point is no use as the contact.
	/// </summary>
	[ConVar( "boss_slam_palm_height", ConVarFlags.Saved )] public static float SlamPalmHeight { get; set; } = 25f;
	[ConVar( "boss_hammer_palm_height", ConVarFlags.Saved )] public static float HammerPalmHeight { get; set; } = 80f;
	/// <summary>The impact's floor ring, a picture only (no damage): its front speed and how far it runs.</summary>
	[ConVar( "boss_ring_speed", ConVarFlags.Saved )] public static float RingSpeed { get; set; } = 720f;
	[ConVar( "boss_ring_max", ConVarFlags.Saved )] public static float RingMax { get; set; } = 1100f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>The hitting pose: the flat palm faces down with the fingers away from the skull; the hammer fist faces inward with the pinky side down.</summary>
	private Rotation HitRot( IBossFrame f, BossPartKind hand, Rotation facing )
	{
		if ( !_hammer ) return f.HandRot( hand, Vector3.Down, facing.Forward );
		var inward = hand == BossPartKind.HandL ? -facing.Left : facing.Left;
		return f.HandRot( hand, inward, facing.Forward );
	}

	private int HitPose => _hammer ? 2 : 5;

	/// <summary>The lock stamps the spot UNDER THE HAND (its tracking lag included), never the player, pulled clear of the rim's stones: the drop is straight down from where it freezes, so what the player sees is what lands (a player hugging a boulder is cover from the slam).</summary>
	public override Vector3 LockTarget( IBossFrame f, BossPartKind hand, Vector3 playerFeet )
	{
		var pos = f.Position( hand );
		return f.ClearSpot( new Vector3( pos.x, pos.y, playerFeet.z ) );
	}

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var s = f.S;
		var rotHit = HitRot( f, hand, ctx.Facing );
		var target = ctx.Locked ? ctx.Target : tracked;
		var over = new Vector3( target.x, target.y, ctx.FloorZ + HangHeight );
		// the contact is the palm centre reaching palmHeight over the ground (the ground clamp on the pivot), not
		// the claw tips touching: the strike aims well under the floor so the controller never brakes
		var palmHeight = (_hammer ? HammerPalmHeight : SlamPalmHeight) * s;
		var floorPose = new Vector3( ctx.Target.x, ctx.Target.y, ctx.FloorZ + palmHeight );
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				// the fist / the flat hand forms on the way up, so it hangs over the player already made
				var look = BossGeometry.PalmLead( f, hand, rotHit, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, over, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.WithClearance( 60f ).WithPose( HitPose ).InPhase( 0 );
			}
			case BossStage.Tell:
				// hangs over the player tracking them, then freezes on the spot and trembles until the drop
				return HandTell( f, hand, ctx, t, over + Vector3.Up * 140f, rotHit, HitPose, 1, windup: Vector3.Up );
			case BossStage.Strike:
				return MoveIntent.To( floorPose + Vector3.Down * 600f, BossDynamics.Strike, rotHit, BossDynamics.TurnStrike )
					.PivotOverGround( palmHeight ).StopOnGround().WithPose( HitPose ).InPhase( 2 );
			case BossStage.Rest:
				// on the floor: the strike's velocity is killed on entry, and whatever height is left drops and lands
				return MoveIntent.To( floorPose + Vector3.Down * 200f, BossDynamics.Approach, rotHit, BossDynamics.TurnIdle )
					.PivotOverGround( palmHeight ).Braked().StopOnGround().WithPose( HitPose ).InPhase( 3 );
			default:
			{
				// no ground clamp on the way up: the claw shapes sit in the floor and the clamp would snap the hand up
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
		var stage = StageAt( ctx, t, out var sp );
		if ( stage == BossStage.Strike && sp > 0.55f && BodyInStrike( p, hand ) )
		{
			var push = (p.FeetPosition - hand.AimPoint).WithZ( 0f ).Normal * 220f + Vector3.Up * 60f;
			hz.Hit = HitLocal( p, hand, Damage, push, "boss_slam" );
		}
	}

	/// <summary>The landing, where the hand really came down.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage < BossStage.Strike || stage > BossStage.Rest ) return;
		var floor = at.WithZ( ctx.FloorZ );
		Sound( "sounds/debristhud.sound", floor );
		HitFeel.Shake( 4f, 0.5f );
		BossVfx.FloorDust( floor, hand.WorldBounds.Size.x * 0.45f );
		BossVfx.Shockwave( floor, hand.WorldBounds.Size.x * 0.35f, RingSpeed, RingMax, 0.6f );
	}
}
