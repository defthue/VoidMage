using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The march: both hands, flat palms, slamming the floor one after the other in quick steps walking up to the
/// player (the last step lands on where they stood at the lock), each landing a small shockwave picture. The
/// hands hover low over their first spots for the tell, freeze and draw BACK toward the skull (the wind-up of a
/// walk coming forward), and from the lock the footsteps are marked on the floor one after another from the skull's
/// side up to the player, each ring closing as its stomp lands: where and what, readable before the first drop.
/// Answer: leave the line of the march; the last drop is the one aimed at you. Both hands rest on the floor after
/// the last step. Every landing is a real contact: its thud, ring and debris fire where and when the palm meets the floor.
/// </summary>
public sealed class MarchAttack : BossAttack
{
	public override string Name => "march";
	public override bool UsesHand => true;
	public override bool UsesBothHands => true;
	public override BossPartKind? OnlyHand => BossPartKind.HandL;
	public override bool MultiImpact => true;

	/// <summary>The approach: the skull retreats behind the line with the hands riding beside it, then the hands peel down over their first spots.</summary>
	[ConVar( "boss_march_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 1.2f;
	[ConVar( "boss_march_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.1f;
	[ConVar( "boss_march_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.5f;
	/// <summary>The whole march: every step takes an equal share (hop, drop, hold).</summary>
	[ConVar( "boss_march_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 2.2f;
	[ConVar( "boss_march_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.5f;
	[ConVar( "boss_march_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 0.9f;

	[ConVar( "boss_march_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 22f;
	[ConVar( "boss_march_steps", ConVarFlags.Saved )] public static int Steps { get; set; } = 5;
	/// <summary>The gap between landings along the line, and the hang height a hand hops up to before each drop, world units.</summary>
	[ConVar( "boss_march_spacing", ConVarFlags.Saved )] public static float Spacing { get; set; } = 320f;
	[ConVar( "boss_march_hang", ConVarFlags.Saved )] public static float Hang { get; set; } = 320f;
	[ConVar( "boss_march_palm_height", ConVarFlags.Saved )] public static float PalmHeight { get; set; } = 25f;
	/// <summary>How far behind the current footstep the skull hangs, on the line, world units: it backs off behind the first steps and walks up with the hands.</summary>
	[ConVar( "boss_march_head_back", ConVarFlags.Saved )] public static float HeadBack { get; set; } = 650f;

	/// <summary>
	/// A step's beats: the step (scripted: the hand LIFTS straight off its last landing for the first LiftShare of
	/// it, then CARRIES over to the next spot, tilting heel-down as it goes and levelling to plant) ends at RiseUntil;
	/// the stomp lands by DropUntil (the hold runs to the step's end). The step is on a rail so every one takes the
	/// same time whatever the distance: a hand lifting off the floor with the chase dynamics never made its third
	/// spot in time, and slid. Landed hands are dead weight (no drive, no lean): a driven hold kept leaning and
	/// rocking a hand after it had landed.
	/// </summary>
	private const float RiseUntil = 0.40f;
	private const float DropUntil = 0.75f;
	private const float LiftShare = 0.35f;
	/// <summary>How far the carried hand tips heel-down at the top of its step, degrees.</summary>
	private const float StepTilt = 28f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	private static int N => Math.Clamp( Steps, 1, 6 );
	/// <summary>Step k is the left hand's on even k.</summary>
	private static BossPartKind HandOf( int k ) => k % 2 == 0 ? BossPartKind.HandL : BossPartKind.HandR;
	private static Vector3 Side( BossPartKind hand, Rotation facing ) => hand == BossPartKind.HandL ? facing.Left : -facing.Left;

	/// <summary>Where step k lands: along the line from the skull's side to the target, the last one on it, each a little to its hand's side (footsteps), pulled clear of the rim's stones.</summary>
	private static Vector3 Spot( IBossFrame f, in BossAttackCtx ctx, Vector3 target, int k )
	{
		var back = -ctx.Facing.Forward;
		return f.ClearSpot( new Vector3( target.x, target.y, ctx.FloorZ ) + back * (Spacing * (N - 1 - k)) + Side( HandOf( k ), ctx.Facing ) * 120f );
	}

	private static int FirstStepOf( BossPartKind hand ) => hand == BossPartKind.HandL ? 0 : 1;

	/// <summary>The last step this hand has landed by step `k` (exclusive), or -1.</summary>
	private static int LastLanded( BossPartKind hand, int k )
	{
		for ( var j = k - 1; j >= 0; j-- ) if ( HandOf( j ) == hand ) return j;
		return -1;
	}

	/// <summary>A landed hand: dead weight on its spot (the ground clamp holds it; nothing drives or leans it).</summary>
	private static MoveIntent Hold( Vector3 spot, Rotation rotFlat, int phase )
		=> MoveIntent.To( spot + Vector3.Up * PalmHeight, BossDynamics.Settle, rotFlat, BossDynamics.TurnIdle ).PivotOverGround( PalmHeight ).StopOnGround().WithLean( 0f ).WithPose( 5 ).InPhase( phase );

	/// <summary>The flat marching palm tipped heel-down by `tilt` degrees (the fingers rise, the heel hangs): the carried hand.</summary>
	private static Rotation Tipped( IBossFrame f, BossPartKind hand, Vector3 forward, float tilt )
	{
		var (sn, cs) = MathF.SinCos( tilt.DegreeToRadian() );
		return f.HandRot( hand, Vector3.Down * cs - forward * sn, forward * cs + Vector3.Up * sn );
	}

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var target = ctx.Locked ? ctx.Target : tracked;
		var rotFlat = f.HandRot( hand, Vector3.Down, ctx.Facing.Forward );
		var first = FirstStepOf( hand );
		var stage = StageAt( ctx, t, out var p );
		if ( first >= N )
		{
			// more hands than steps: this one just hangs in formation
			var st = f.HandStance( hand );
			return MoveIntent.To( st.Position, BossDynamics.Formation, st.Rotation, BossDynamics.TurnStrike ).WithClearance( 40f ).WithPose( 0 ).InPhase( -1 );
		}
		var firstOver = Spot( f, ctx, target, first ) + Vector3.Up * Hang;
		switch ( stage )
		{
			case BossStage.Approach:
			{
				// the skull backs off behind the line first (Escort) with the hands riding beside it, flat palms formed;
				// then the hands peel down over their first spots, now in FRONT of the skull, never behind or under it
				if ( p < 0.5f ) return BossGeometry.StanceIntent( f, hand, f.Now, skullBusy: true ).WithPose( 5 ).InPhase( 0 );
				var look = BossGeometry.PalmLead( f, hand, rotFlat, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, firstOver, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( 5 ).InPhase( 1 );
			}
			case BossStage.Tell:
				// both hands hover low over their first spots and, frozen, draw back toward the skull: the wind-up of a walk coming at you
				return HandTell( f, hand, ctx, t, firstOver, rotFlat, 5, 2, windup: -ctx.Facing.Forward );
			case BossStage.Strike:
			{
				var n = N;
				var k = Math.Min( n - 1, (int)(p * n) );
				var sub = p * n - k;
				if ( HandOf( k ) == hand )
				{
					var spot = Spot( f, ctx, target, k );
					var over = spot + Vector3.Up * Hang;
					if ( sub < RiseUntil )
					{
						var landedBefore = LastLanded( hand, k );
						if ( landedBefore < 0 )
							// the first step: the hand already hangs over its spot from the tell (drawn up a little by the
							// wind-up); it settles onto the hang point with a drive, not a rail, so nothing snaps
							return MoveIntent.To( over, BossDynamics.Strike, rotFlat, BossDynamics.TurnStrike ).NoGround().WithLean( 0f ).WithPose( 5 ).InPhase( 10 + k );
						// the step, scripted: lifts straight off its last landing, carries over to the next spot tipping
						// heel-down, levels again to plant
						var from = Spot( f, ctx, target, landedBefore ) + Vector3.Up * PalmHeight;
						var lifted = from + Vector3.Up * (Hang * 0.85f);
						var u = sub / RiseUntil;
						Vector3 pos;
						if ( u < LiftShare ) pos = Vector3.Lerp( from, lifted, Ease( u / LiftShare ) );
						else
						{
							var w = Ease( (u - LiftShare) / (1f - LiftShare) );
							pos = Vector3.Lerp( lifted, over, w ) + Vector3.Up * (MathF.Sin( w * MathF.PI ) * Hang * 0.15f);
						}
						var look = Tipped( f, hand, ctx.Facing.Forward, MathF.Sin( Math.Clamp( u, 0f, 1f ) * MathF.PI ) * StepTilt );
						return MoveIntent.To( pos, BossDynamics.Strike, look, BossDynamics.TurnStrike ).OnRail().NoGround().WithLean( 0f ).WithPose( 5 ).InPhase( 10 + k );
					}
					// the stomp: straight down onto the spot, flat, a hard stop on the floor; the hold keeps the drop's phase
					// so a drop still in the air finishes it and lands
					return MoveIntent.To( spot + Vector3.Down * 600f, BossDynamics.Strike, rotFlat, BossDynamics.TurnStrike ).PivotOverGround( PalmHeight ).StopOnGround().WithLean( 0f ).WithPose( 5 ).InPhase( 20 + k );
				}
				var landed = LastLanded( hand, k );
				if ( landed >= 0 ) return Hold( Spot( f, ctx, target, landed ), rotFlat, 20 + landed );
				// not its turn yet: hangs frozen over its first spot
				return MoveIntent.To( firstOver, BossDynamics.Tell, rotFlat, BossDynamics.TurnIdle ).NoGround().WithPose( 5 ).InPhase( 5 );
			}
			case BossStage.Rest:
			{
				var landed = Math.Max( 0, LastLanded( hand, N ) );
				return Hold( Spot( f, ctx, target, landed ), rotFlat, 20 + landed );
			}
			default:
			{
				var stance = f.HandStance( hand );
				return BossGeometry.HandTravel( f, hand, stance.Position, BossDynamics.Recover, stance.Rotation, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( p > 0.5f ? 0 : 1 ).InPhase( 40 );
			}
		}
	}

	/// <summary>Where the skull hangs for footstep k: on the line, HeadBack behind that step, at its hover height, inside the arena.</summary>
	private static Vector3 EscortPoint( IBossFrame f, in BossAttackCtx ctx, Vector3 target, int k )
	{
		var back = -ctx.Facing.Forward;
		var line = new Vector3( target.x, target.y, ctx.FloorZ ) + back * (Spacing * (N - 1 - k) + HeadBack);
		return f.ClampToArena( line ).WithZ( f.SkullHover().Position.z );
	}

	/// <summary>
	/// The skull during the march: it backs off behind the first footsteps (the hands come down in front of it, the
	/// whole picture reads as one creature about to walk at you), then advances a step behind each landing, and
	/// holds behind the last for the rest; the stance is moved there (HostTick), so it stays rather than flying home.
	/// </summary>
	public override MoveIntent? Escort( IBossFrame f, in BossAttackCtx ctx, float t )
	{
		var stage = StageAt( ctx, t, out var p );
		if ( stage >= BossStage.Recover ) return null;
		var k = stage == BossStage.Strike ? Math.Min( N - 1, (int)(p * N) ) : stage == BossStage.Rest ? N - 1 : 0;
		var pos = EscortPoint( f, ctx, ctx.Target, k );
		var toPlayer = (ctx.Target - pos).WithZ( 0f );
		var look = (toPlayer.IsNearZeroLength ? ctx.Facing : Rotation.LookAt( toPlayer.Normal )) * Rotation.FromPitch( f.SkullPitch );
		return MoveIntent.To( pos, BossDynamics.Approach, look, BossDynamics.TurnIdle ).WithClearance( 50f ).InPhase( 50 + k );
	}

	// host: the run the stance was moved for (the escort's last point becomes the guard spot)
	private int _stanceSeq = -1;

	public override void HostTick( BossBrain brain, in BossAttackCtx ctx, float t )
	{
		if ( _stanceSeq == ctx.Seq || StageAt( ctx, t, out _ ) < BossStage.Rest ) return;
		_stanceSeq = ctx.Seq;
		brain.SetStance( EscortPoint( brain, ctx, ctx.Target, N - 1 ), ctx.Target );
	}

	/// <summary>The footstep marks: from the lock, one ring per step drops on the floor in walking order (a beat apart), each closing exactly as its stomp lands.</summary>
	private void TickMarks( BossBrain brain, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		if ( !ctx.Locked ) return;
		var n = N;
		var next = hz.Beat + 1;
		if ( next >= n ) return;
		var lockAt = LockTime( ctx );
		var window = MathF.Max( 0.05f, TellEnd( ctx ) - lockAt );
		if ( t < lockAt + window * next / n ) return;
		hz.Beat = next;
		var spot = Spot( brain, ctx, ctx.Target, next );
		var lands = TellEnd( ctx ) + (next + DropUntil) * Strike / n;
		BossVfx.Mark( spot.WithZ( ctx.FloorZ ), MarkRadius, MathF.Max( 0.1f, lands - t ) );
	}

	/// <summary>A footstep mark's final radius, world units (it closes from twice this).</summary>
	private const float MarkRadius = 150f;

	public override void Judge( BossBrain brain, BossPart hand, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		var stage = StageAt( ctx, t, out var p );
		if ( stage == BossStage.Tell ) { TickMarks( brain, ctx, t, hz ); return; }
		if ( stage != BossStage.Strike ) return;
		var n = N;
		var k = Math.Min( n - 1, (int)(p * n) );
		var sub = p * n - k;
		var stepping = BossNet.Find( HandOf( k ) );
		var pl = OwnPlayer();
		if ( pl is null || hz.Hit || !stepping.IsValid() || stepping.Dead ) return;
		if ( sub < RiseUntil + 0.05f || sub > DropUntil + 0.25f ) return;
		if ( !BodyInStrike( pl, stepping ) ) return;
		var push = (pl.FeetPosition - stepping.AimPoint).WithZ( 0f ).Normal * 240f + Vector3.Up * 70f;
		hz.Hit = HitLocal( pl, stepping, Damage, push, "boss_march" );
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

	/// <summary>A landing: the thud, the ring, the debris, where the palm met the floor.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage is not (BossStage.Strike or BossStage.Rest) ) return;
		var floor = at.WithZ( ctx.FloorZ );
		Sound( "sounds/debristhud.sound", floor );
		HitFeel.Shake( 3f, 0.35f );
		BossVfx.FloorDust( floor, hand.WorldBounds.Size.x * 0.4f );
		BossVfx.Shockwave( floor, hand.WorldBounds.Size.x * 0.3f, SlamAttack.RingSpeed, SlamAttack.RingMax * 0.6f, 0.45f );
	}
}
