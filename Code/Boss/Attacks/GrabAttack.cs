using System;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The grab, the punish for hugging a hand: a low sideways sweep that catches. The open hand comes down to the
/// player's side at floor level EDGE-ON (palm toward them, fingers pointing away from the skull, the forearm reaching
/// out from the skull's side), tracks, then trembles and draws back to its side (the wind-up, the same read as the
/// sweep), and swings along the floor through where they stood; the instant the palm passes them the fist SNAPS shut,
/// the fingers wrapping the body sideways so the head sticks out of the top. A step out of its line clears it, like
/// the sweep. Caught, the player is held in the fist (controls off, carried): the body sits in the fist with the head
/// out of its top and rides it as it turns knuckles-out, lifts and squeezes (the crush), then is hurled away for the
/// damage; the hand then drops palm-down onto the floor and lies there (the sword window) before returning. The
/// catch is judged on every peer for its own player (the trap model): the host's hand plays the whole thing regardless.
/// </summary>
public sealed class GrabAttack : BossAttack
{
	public override string Name => "grab";
	public override bool UsesHand => true;

	[ConVar( "boss_grab_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.4f;
	[ConVar( "boss_grab_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1.2f;
	[ConVar( "boss_grab_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.6f;
	/// <summary>The sweep and the close.</summary>
	[ConVar( "boss_grab_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 0.5f;
	/// <summary>The hold: lifted and squeezed, thrown at 80 % of it.</summary>
	[ConVar( "boss_grab_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.4f;
	/// <summary>The hand falls palm-down and lies for the first two thirds of this (the sword window), then flies home.</summary>
	[ConVar( "boss_grab_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 1.9f;

	[ConVar( "boss_grab_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 30f;
	/// <summary>Where the sweep starts: this far from the player to the hand's own side; how far past them it runs; the hold's height; the throw's speed.</summary>
	[ConVar( "boss_grab_distance", ConVarFlags.Saved )] public static float StartDistance { get; set; } = 600f;
	[ConVar( "boss_grab_through", ConVarFlags.Saved )] public static float Through { get; set; } = 220f;
	[ConVar( "boss_grab_hold_height", ConVarFlags.Saved )] public static float HoldHeight { get; set; } = 420f;
	[ConVar( "boss_grab_throw", ConVarFlags.Saved )] public static float Throw { get; set; } = 650f;
	/// <summary>The PALM's lower edge over the floor for the sweep, world units: over a crouched head, through a standing chest (the crouch is the dodge; a step out of the line too). The palm alone: a thumb or finger hanging under its edge goes into the floor rather than lifting the palm.</summary>
	[ConVar( "boss_grab_height", ConVarFlags.Saved )] public static float Height { get; set; } = 24f;
	/// <summary>How far the held player's head top stands out of the top of the fist, world units (the camera rides just over the knuckles).</summary>
	[ConVar( "boss_grab_head", ConVarFlags.Saved )] public static float HeadOut { get; set; } = 14f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	/// <summary>The hold's fraction at which the throw happens.</summary>
	private const float ThrowAt = 0.8f;
	/// <summary>The recover's fraction the hand lies on the floor for.</summary>
	private const float LieUntil = 0.67f;
	/// <summary>The sweep's window in which the fingers close and a body in the palm is caught.</summary>
	private const float CatchFrom = 0.3f;
	private const float CatchUntil = 0.8f;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	/// <summary>Each hand's own side of the player: the boss's left hand on its left.</summary>
	private static Vector3 Side( BossPartKind hand, Rotation facing ) => hand == BossPartKind.HandL ? facing.Left : -facing.Left;

	/// <summary>From the player toward the skull, flat: the facing on every peer, so host and peers agree on the throw.</summary>
	private static Vector3 Back( Rotation facing ) => -facing.Forward;

	public override MoveIntent Move( IBossFrame f, BossPartKind hand, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var target = ctx.Locked ? ctx.Target : tracked;
		var side = Side( hand, ctx.Facing );
		var back = Back( ctx.Facing );
		var rotSwipe = f.HandRot( hand, -side, -back );           // edge-on: palm leads the sweep, fingers away from the skull, so the fist wraps a standing body sideways
		var rotFist = f.HandRot( hand, back, -side );              // the hold: knuckles out; the same vertical hand axis as the sweep, so the turn is round the held body
		var rotOpen = f.HandRot( hand, Vector3.Up, -back );        // the throw: palm up, fingers onward, letting go
		var rotDown = f.HandRot( hand, Vector3.Down, -side );
		var z = ctx.FloorZ - f.ShapeBottom( hand, "palm" ) - FloorSink + Height;   // the palm's lower edge Height over the floor, whatever the fingers do
		var through = new Vector3( target.x, target.y, z );                  // the pass: the player's body, the palm's edge over a crouched head
		var startPt = through + side * StartDistance;
		var endPt = through - side * Through;
		var holdPt = new Vector3( target.x, target.y, ctx.FloorZ + HoldHeight );
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
			{
				// down to the player's side at floor level, open, palm to them
				var look = BossGeometry.PalmLead( f, hand, rotSwipe, f.Velocity( hand ) );
				return BossGeometry.HandTravel( f, hand, startPt, BossDynamics.Approach, look, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( 1 ).InPhase( 0 );
			}
			case BossStage.Tell:
				// tracks, then trembles drawing back to its side: the same read as the sweep
				return HandTell( f, hand, ctx, t, startPt, rotSwipe, 5, 1, windup: side );
			case BossStage.Strike:
			{
				// the sweep along the floor through the body, aimed past its end and stopped there; the fist snaps shut the
				// moment the palm passes the body (open until then: what you see coming is an open hand)
				var passed = Vector3.Dot( f.Position( hand ) - through, -side ) > 0f;
				return MoveIntent.To( endPt - side * 260f, BossDynamics.Strike, rotSwipe, BossDynamics.TurnStrike )
					.Over( through, 140f ).NoGround().StopAt( 260f ).WithPose( passed ? 2 : 5 ).InPhase( 2 );
			}
			case BossStage.Rest:
			{
				if ( p < ThrowAt )
				{
					// the hold: the closed fist turns knuckles-out and lifts to the hold height, trembling harder as it squeezes (the crush)
					var (offset, jitter) = BossGeometry.Tremble( t, p / ThrowAt, f.S * 0.6f );
					return MoveIntent.To( holdPt, BossDynamics.Approach, rotFist * jitter, BossDynamics.TurnIdle )
						.NoGround().Shifted( offset ).WithLean( 0f ).WithPose( 2 ).InPhase( 3 );
				}
				// the throw: the hand whips forward past the player's spot, opening
				return MoveIntent.To( holdPt - back * 900f + Vector3.Up * 100f, BossDynamics.Strike, rotOpen, BossDynamics.TurnStrike )
					.NoGround().StopAt( 200f ).WithPose( 5 ).InPhase( 4 );
			}
			default:
			{
				if ( p < LieUntil )
				{
					// falls palm-down onto the floor under it and lies there: the sword's window (a real contact)
					var here = f.ClearSpot( f.Position( hand ) );
					return MoveIntent.To( new Vector3( here.x, here.y, ctx.FloorZ - 200f ), BossDynamics.Approach, rotDown, BossDynamics.TurnIdle )
						.PivotOverGround( FloorClamp( f, hand ) ).Braked().StopOnGround().WithPose( 1 ).InPhase( 5 );
				}
				var stance = f.HandStance( hand );
				return BossGeometry.HandTravel( f, hand, stance.Position, BossDynamics.Recover, stance.Rotation, BossDynamics.TurnIdle, arc: true )
					.NoGround().WithPose( p > 0.8f ? 0 : 1 ).InPhase( 6 );
			}
		}
	}

	public override void Judge( BossBrain brain, BossPart hand, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		var p = OwnPlayer();
		var stage = StageAt( ctx, t, out var sp );
		if ( hz.Held )
		{
			// carried in the fist; let go when the throw comes, the move ends, or the player dies
			var alive = PlayerHealth.Local;
			if ( alive is null || !alive.IsValid() || alive.IsDead ) { Release( hz, null, hand, ctx, false ); return; }
			if ( stage > BossStage.Rest || (stage == BossStage.Rest && sp >= ThrowAt) ) { Release( hz, alive, hand, ctx, true ); return; }
			if ( stage == BossStage.Strike || stage == BossStage.Rest ) Ride( alive, hand );
			return;
		}
		if ( p is null || hz.Hit ) return;
		// the catch: only while the fingers close on the pass, only a body in the PALM itself (never a finger or the
		// thumb trailing under it): its centre, or its head (the palm runs over a crouched head and through a standing
		// chest, so the head is what a standing body offers it)
		if ( stage != BossStage.Strike || sp < CatchFrom || sp > CatchUntil ) return;
		var feet = p.FeetPosition;
		var caught = hand.InShape( "palm", feet + Vector3.Up * (p.BodyHeight * 0.5f), HitForgive ) || hand.InShape( "palm", feet + Vector3.Up * (p.BodyHeight - 4f), HitForgive );
		if ( !caught ) return;
		hz.Held = true;
		p.Hold( true );
		Ride( p, hand );
		Sound( "sounds/bonebreak.sound", hand.AimPoint );
		HitFeel.Shake( 2f, 0.3f );
	}

	/// <summary>
	/// The held body in the fist, every frame: over the palm's centre, with the top of the head <see cref="HeadOut"/>
	/// above the top of the fist as it stands right now (the fist's own shapes, so the head keeps standing out of the
	/// knuckles however the hand turns and squeezes; the camera rides just over the fist, never inside it).
	/// </summary>
	private static void Ride( PlayerHealth p, BossPart hand )
	{
		var pivot = hand.PivotWorld;
		var top = pivot.z + hand.LiveTopFromPivot();
		PlayerHealth.TeleportLocal( new Vector3( pivot.x, pivot.y, top + HeadOut - p.BodyHeight ) );
	}

	/// <summary>The fist opens: the player gets their controls back and, on the throw, the hurl and the damage.</summary>
	private static void Release( BossHazardState hz, PlayerHealth p, BossPart hand, in BossAttackCtx ctx, bool thrown )
	{
		hz.Held = false;
		hz.Hit = true;
		if ( p is null || !p.IsValid() ) return;
		p.Hold( false );
		if ( !thrown ) return;
		var push = -Back( ctx.Facing ) * Throw + Vector3.Up * 320f;
		HitLocal( p, hand, Damage, push, "boss_grab" );
	}

	public override void Cancelled( BossBrain brain, in BossAttackCtx ctx, BossHazardState hz )
	{
		if ( !hz.Held ) return;
		var hand = BossNet.Find( ctx.Part );
		Release( hz, PlayerHealth.Local, hand, ctx, false );
	}

	public override void Present( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Tell:
				Sound( "sounds/ambienceknock.sound", hand.AimPoint );
				break;
		}
	}

	/// <summary>The hand falling palm-down after the throw: the thud of the sword window opening.</summary>
	public override void OnImpact( BossBrain brain, BossPart hand, in BossAttackCtx ctx, BossStage stage, Vector3 at )
	{
		if ( stage != BossStage.Recover ) return;
		Sound( "sounds/debristhud.sound", at );
		HitFeel.Shake( 2.5f, 0.35f );
	}
}
