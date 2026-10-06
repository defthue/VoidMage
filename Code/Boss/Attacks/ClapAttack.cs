using System;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Spells;

namespace VoidMage.Boss;

/// <summary>
/// The clap, both hands while both live: they come to the player's two sides at body height, palms facing each
/// other, draw outward (the tell), lock, and fly at each other, meeting on the player with a shockwave that runs
/// up out of the clap (a picture). The hands stay pressed together low for the rest: both in sword reach. Answer:
/// leave the line between the hands once they freeze. The brain drives both hands with the same ctx.
/// </summary>
public sealed class ClapAttack : BossAttack
{
	public override string Name => "clap";
	public override bool UsesHand => true;
	public override bool UsesBothHands => true;
	public override BossPartKind? OnlyHand => BossPartKind.HandL;

	[ConVar( "boss_clap_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_clap_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.3f;
	[ConVar( "boss_clap_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.5f;
	[ConVar( "boss_clap_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.4f;
	[ConVar( "boss_clap_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.7f;
	[ConVar( "boss_clap_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.6f;

	[ConVar( "boss_clap_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 40f;
	/// <summary>How far to each side of the player the hands start, and half the gap left between the palms when they meet (hand model units x scale).</summary>
	[ConVar( "boss_clap_reach", ConVarFlags.Saved )] public static float Reach { get; set; } = 850f;
	[ConVar( "boss_clap_gap", ConVarFlags.Saved )] public static float Gap { get; set; } = 45f;
	/// <summary>The clap's wave, a picture only: how far the vertical ring runs out of the clap and how long it takes.</summary>
	[ConVar( "boss_clap_wave", ConVarFlags.Saved )] public static float WaveRadius { get; set; } = 900f;
	[ConVar( "boss_clap_wave_seconds", ConVarFlags.Saved )] public static float WaveSeconds { get; set; } = 0.5f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>Each hand's own side of the player: the boss's left hand on its left.</summary>
	private static Vector3 Side( BossPartKind hand, Rotation facing ) => hand == BossPartKind.HandL ? facing.Left : -facing.Left;

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var side = Side( hand, ctx.Facing );
		var rotFace = f.HandRot( hand, -side, Vector3.Up );   // palm toward the other hand, fingers up
		var target = ctx.Locked ? ctx.Target : tracked;
		// the standing palms' lower edges a hair into the floor: the clap closes on the body, not over it
		var z = FloorPivot( f, hand, ctx.FloorZ );
		var centre = new Vector3( target.x, target.y, z );
		var startPt = centre + side * Reach;
		var pulled = startPt + side * 220f;
		var stop = centre + side * Gap * f.S;   // its own side of the meeting point
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				var look = BossGeometry.PalmLead( f, hand, rotFace, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, startPt, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( 1 ).InPhase( 0 );
			}
			case BossStage.Tell:
				return HandTell( f, hand, ctx, t, pulled, rotFace, 5, 1, windup: side );
			case BossStage.Strike:
				// aims past its stop and stops there: full speed into the meeting, a hard stop at the other palm
				return MoveIntent.To( stop - side * 260f, BossDynamics.Strike, rotFace, BossDynamics.TurnStrike )
					.NoGround().StopAt( 260f ).WithPose( 5 ).InPhase( 2 );
			case BossStage.Rest:
				return MoveIntent.To( stop, BossDynamics.Tell, rotFace, BossDynamics.TurnIdle ).NoGround().Braked().WithPose( 5 ).InPhase( 3 );
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
		if ( p is null ) return;
		if ( hz.Hit ) return;
		var other = BossNet.Find( hand.Kind == BossPartKind.HandL ? BossPartKind.HandR : BossPartKind.HandL );
		var stage = StageAt( ctx, t, out _ );
		if ( stage != BossStage.Strike ) return;
		var caught = BodyInStrike( p, hand ) || (other.IsValid() && !other.Dead && BodyInStrike( p, other ));
		if ( !caught ) return;
		var push = (p.FeetPosition - ctx.Target).WithZ( 0f );
		push = (push.IsNearZeroLength ? ctx.Facing.Forward : push.Normal) * 200f + Vector3.Up * 160f;
		hz.Hit = HitLocal( p, hand, Damage, push, "boss_clap" );
	}

	public override void Present( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Tell:
				Sound( "sounds/ambienceknock.sound", ctx.Target );
				break;
		}
	}

	/// <summary>The clap: the first palm to reach its stop is the meeting; two massive hands (the user's clap clip), the arena jolts, the wave stands up between them.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage != BossStage.Strike ) return;
		var meet = ctx.Target.WithZ( hand.PivotWorld.z );
		Sound( "sounds/bossclap.sound", meet );
		HitFeel.Shake( 7f, 0.8f );
		Rendering.RetroFilter.TriggerRipple( meet, 1.6f );
		BreakVfx.Stone( brain.Scene, meet, 220f, Vector3.Up * 160f, false );
		BossVfx.ClapWave( meet, ctx.Facing.Left, 60f, WaveRadius, WaveSeconds );
	}
}
