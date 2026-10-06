using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The rain: the skull rises high over its stance, trembles (the tell) and screams, and for a couple of seconds
/// green bolts pour out of its eyes onto the floor all around where the player stood at the lock, every third one
/// at where they are now. Each bolt marks its landing spot on the floor for its flight (the tell of each drop).
/// Answer: keep moving, and never stand on a mark. Then it sags, spent and exposed.
/// </summary>
public sealed class RainAttack : BossAttack
{
	public override string Name => "rain";
	public override bool UsesHand => false;

	[ConVar( "boss_rain_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_rain_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 0.8f;
	[ConVar( "boss_rain_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.3f;
	[ConVar( "boss_rain_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 2f;
	[ConVar( "boss_rain_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.6f;
	[ConVar( "boss_rain_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 1f;

	/// <summary>How many bolts fall over the strike, and the radius of the patch around the locked spot they fall in.</summary>
	[ConVar( "boss_rain_bolts", ConVarFlags.Saved )] public static int Bolts { get; set; } = 12;
	[ConVar( "boss_rain_radius", ConVarFlags.Saved )] public static float Radius { get; set; } = 900f;
	/// <summary>How far over its hover the skull rises to rain, world units.</summary>
	[ConVar( "boss_rain_rise", ConVarFlags.Saved )] public static float Rise { get; set; } = 350f;
	[ConVar( "boss_rain_exposed", ConVarFlags.Saved )] public static float Exposed { get; set; } = 1.5f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;
	public override float RestMultiplier => Exposed;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>The lock only stamps the patch's centre (the player's feet); the skull stays over its stance.</summary>
	public override Vector3 LockTarget( IBossFrame f, BossPartKind skull, Vector3 playerFeet ) => playerFeet;

	// host: which bolts of the running rain have left (per attack sequence)
	private int _launchedSeq = -1;
	private int _launched;

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var hover = f.SkullHover();
		var high = hover.Position + Vector3.Up * Rise;
		var face = hover.Rotation;
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
				f.JawOpen01 = 0.3f * Ease( p );
				f.EyeFire01 = 1f;
				return MoveIntent.To( high, BossDynamics.Approach, face * Rotation.FromPitch( -10f * Ease( p ) ), BossDynamics.TurnIdle ).NoGround().InPhase( 0 );
			case BossStage.Tell:
			{
				var amp = Ease( p );
				f.JawOpen01 = 0.4f + 0.4f * MathF.Sin( t * 40f ) * amp;
				f.EyeFire01 = 1f + amp;
				return SkullTremble( f, skull, t, amp, face * Rotation.FromPitch( -10f ), 1 );
			}
			case BossStage.Strike:
			{
				// the scream: mouth wide, eyes boiling, the head thrown back a little, a small shiver with every bolt
				f.JawOpen01 = 1f;
				f.EyeFire01 = 2f;
				var (offset, jitter) = BossGeometry.Tremble( t, 0.3f, f.S * 0.5f );
				return MoveIntent.To( high + Vector3.Up * 60f, BossDynamics.Approach, face * Rotation.FromPitch( -18f ) * jitter, BossDynamics.TurnIdle )
					.NoGround().Shifted( offset ).WithLean( 0f ).InPhase( 2 );
			}
			case BossStage.Rest:
				f.JawOpen01 = 0.8f;
				f.EyeFire01 = 0.6f;
				return MoveIntent.To( hover.Position + Vector3.Down * 80f, BossDynamics.Tell, face * Rotation.FromPitch( 12f ), BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 3 );
			default:
				f.JawOpen01 = 0.8f * (1f - Ease( p ));
				f.EyeFire01 = 1f;
				return MoveIntent.To( hover.Position, BossDynamics.Recover, hover.Rotation, BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 4 );
		}
	}

	/// <summary>Host: the bolts leave on the scream's clock, spread over the strike; every third at the nearest player's feet right now, the rest at random spots of the patch.</summary>
	public override void HostTick( BossBrain brain, in BossAttackCtx ctx, float t )
	{
		if ( _launchedSeq != ctx.Seq ) { _launchedSeq = ctx.Seq; _launched = 0; }
		var n = Math.Clamp( Bolts, 1, 30 );
		var screamAt = TellEnd( ctx );
		var gap = Strike / n;
		for ( var i = 0; i < n; i++ )
		{
			var bit = 1 << i;
			if ( (_launched & bit) != 0 ) continue;
			if ( t < screamAt + i * gap ) break;
			_launched |= bit;
			Vector3 to;
			var player = brain.Nearest( out _ );
			if ( i % 3 == 2 && player is not null ) to = player.FeetPosition;
			else
			{
				var r = MathF.Sqrt( Game.Random.Float( 0.05f, 1f ) ) * Radius;
				var flat = ctx.Target + Rotation.FromYaw( Game.Random.Float( 0f, 360f ) ).Forward * r;
				to = flat.WithZ( brain.GroundZ( flat ) );
			}
			brain.LaunchBoltTo( i, to + Vector3.Up * 30f, mark: true, harmless: false );
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
				HitFeel.Shake( 1f, 1f );
				break;
			case BossStage.Strike:
				Sound( "sounds/ambiencehorror.sound", skull.AimPoint );
				HitFeel.Shake( 3f, 0.6f );
				break;
		}
	}
}
