using System;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>
/// The bite, a bird's swoop: the skull comes to a stand in front of the player at its flying height with its jaw
/// open, tracks them, then the jaw clicks (the lock), it rears back and dives: the MOUTH rides a scripted curve
/// (a rail, exact) that sweeps down through the player at the character's height, on past them and up again to
/// the flying height, the head looking along its path the whole way (no spin). A short glide, then the way home:
/// a wide arc back round to the hover in front of the player, turning to face them only on the last stretch, the
/// formation hands trailing the flight. Only the mouth bites: a sidestep at the click clears it.
/// </summary>
public sealed class BiteAttack : BossAttack
{
	public override string Name => "bite";
	public override bool UsesHand => false;

	[ConVar( "boss_bite_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_bite_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 0.6f;
	[ConVar( "boss_bite_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.5f;
	/// <summary>The swoop's length: rear point to the far point, dive, pass and climb.</summary>
	[ConVar( "boss_bite_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 1.2f;
	/// <summary>The glide after the climb: no drive, the speed bleeds off.</summary>
	[ConVar( "boss_bite_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 0.7f;
	/// <summary>The arc home.</summary>
	[ConVar( "boss_bite_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 1.1f;

	[ConVar( "boss_bite_damage", ConVarFlags.Saved )] public static float Damage { get; set; } = 35f;
	/// <summary>Where the skull stands in front of the player before the dive, world units.</summary>
	[ConVar( "boss_bite_distance", ConVarFlags.Saved )] public static float ApproachDistance { get; set; } = 850f;
	/// <summary>How far past the player the swoop carries the mouth before it is back at the flying height.</summary>
	[ConVar( "boss_bite_through", ConVarFlags.Saved )] public static float Through { get; set; } = 700f;
	/// <summary>The mouth centre's height over the floor at the bottom of the swoop: the character's body (the chin is in the floor there).</summary>
	[ConVar( "boss_bite_mouth_height", ConVarFlags.Saved )] public static float MouthHeight { get; set; } = 40f;
	/// <summary>How much of the flying height the climb regains by the far point (1 = all of it).</summary>
	[ConVar( "boss_bite_climb", ConVarFlags.Saved )] public static float Climb { get; set; } = 1f;
	/// <summary>The skull's lowest point relative to the floor while low (the shake): negative = the chin in the floor.</summary>
	[ConVar( "boss_skull_low_clearance", ConVarFlags.Saved )] public static float LowClearance { get; set; } = -60f;

	/// <summary>The mouth in skull model units (face +x): the bite zone, from the upper teeth down to the jaw as placed (boss_jaw_offset moves it 40 forward and 40 down).</summary>
	public static readonly BBox MouthBox = new( new Vector3( 40f, -100f, -120f ), new Vector3( 250f, 100f, 50f ) );
	/// <summary>The mouth's centre in model units: the swoop puts it on the target.</summary>
	private static readonly Vector3 MouthCentre = new( 125f, 0f, -15f );
	/// <summary>The mouth's reach around its centre for the swept test, model units (the box's half width).</summary>
	private const float MouthRadius = 100f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	private static Rotation FaceTo( Vector3 from, Vector3 to, Rotation fallback )
	{
		var d = (to - from).WithZ( 0f );
		return d.IsNearZeroLength ? fallback : Rotation.LookAt( d.Normal );
	}

	/// <summary>The swoop's geometry for a locked target: the mouth's rear point, low point and far point, and the flat heading.</summary>
	private readonly struct Swoop
	{
		public readonly Vector3 Rear, Low, Far, Heading;
		/// <summary>The low point's fraction of the way from the rear point to the far point.</summary>
		public readonly float Q0;

		public Swoop( IBossFrame f, in BossAttackCtx ctx, Vector3 target, Rotation face, Vector3 back, Vector3 rearedPivot )
		{
			var s = f.S;
			Heading = -back;
			var mouthFlyZ = (f.SkullHover().Position + face * (MouthCentre * s)).z;
			Low = new Vector3( target.x, target.y, ctx.FloorZ + MouthHeight );
			// the curve starts where the mouth sits in the rear pose (the tell's end), so the rail takes over without a jump
			Rear = rearedPivot + face * (MouthCentre * s);
			var farZ = MathX.Lerp( Low.z, mouthFlyZ, Math.Clamp( Climb, 0f, 1.5f ) );
			Far = new Vector3( target.x, target.y, farZ ) - back * Through;
			var total = (Far - Rear).WithZ( 0f ).Length;
			Q0 = total > 1f ? (Low - Rear).WithZ( 0f ).Length / total : 0.5f;
		}

		/// <summary>The mouth centre at progress q (0 rear, Q0 the player, 1 far): flat along the line, a cosine dive then a cosine climb.</summary>
		public Vector3 Mouth( float q )
		{
			q = Math.Clamp( q, 0f, 1f );
			var flat = Vector3.Lerp( Rear, Far, q );
			float z;
			if ( q <= Q0 )
			{
				var u = Q0 > 0.001f ? q / Q0 : 1f;
				z = MathX.Lerp( Rear.z, Low.z, 0.5f - 0.5f * MathF.Cos( u * MathF.PI ) );
			}
			else
			{
				var u = Q0 < 0.999f ? (q - Q0) / (1f - Q0) : 1f;
				z = MathX.Lerp( Low.z, Far.z, 0.5f - 0.5f * MathF.Cos( u * MathF.PI ) );
			}
			return new Vector3( flat.x, flat.y, z );
		}
	}

	/// <summary>Progress along the swoop for the strike's fraction p: slow out of the rear, fastest at the player, easing out on the climb.</summary>
	private static float Progress( float p ) => Ease( p );

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var target = ctx.Locked ? ctx.Target : tracked;
		var s = f.S;
		// the approach point: between the player and the skull's stance, at the flying height
		var back = (f.StancePoint - target).WithZ( 0f );
		back = back.IsNearZeroLength ? -ctx.Facing.Forward : back.Normal;
		var face = FaceTo( target + back, target, ctx.Facing ) * Rotation.FromPitch( f.SkullPitch );
		var flyZ = f.SkullHover().Position.z;
		var stand = new Vector3( target.x, target.y, flyZ ) + back * ApproachDistance;
		var reared = stand + back * 170f;
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
				// to the stand point in front of the player at the flying height, laughing on the way
				f.JawOpen01 = BossGeometry.Laugh( t ) * Ease( p * 3f );
				return MoveIntent.To( stand, BossDynamics.Approach, face, BossDynamics.TurnIdle ).NoGround().InPhase( 0 );
			case BossStage.Tell:
			{
				// tracks firmly until the lock, then rears back with the face tipped up a touch; the laugh turns into the gape
				var rear = Ease( (p - LockAt) / MathF.Max( 0.01f, 1f - LockAt ) );
				f.JawOpen01 = MathX.Lerp( BossGeometry.Laugh( t ), 1f, rear );
				return MoveIntent.To( p < LockAt ? stand : reared, p < LockAt ? BossDynamics.Track : BossDynamics.Tell, face * Rotation.FromPitch( -8f * rear ), BossDynamics.TurnIdle )
					.NoGround().InPhase( 1 );
			}
			case BossStage.Strike:
			{
				// the swoop, on a rail: the mouth rides the curve; the head looks along its path (the pivot hangs off THAT look, so the body follows the mouth cleanly)
				var sw = new Swoop( f, ctx, target, face, back, reared );
				var q = Progress( p );
				var mouth = sw.Mouth( q );
				var ahead = sw.Mouth( MathF.Min( 1f, q + 0.02f ) );
				var tangent = ahead - mouth;
				if ( tangent.IsNearZeroLength ) tangent = sw.Heading;
				var look = Rotation.LookAt( tangent.Normal ) * Rotation.FromPitch( f.SkullPitch );
				// the jaw: wide on the dive, snaps shut through the player, hangs half open after
				var passed = Math.Clamp( (q - sw.Q0) / MathF.Max( 0.05f, 1f - sw.Q0 ), 0f, 1f );
				f.JawOpen01 = q < sw.Q0 - 0.06f ? 1f : q < sw.Q0 + 0.04f ? 1f - (q - (sw.Q0 - 0.06f)) / 0.1f : MathF.Min( 0.45f, passed * 1.2f );
				// onto the rail over the first part of the dive (the tell may not have quite reached the rear point)
				var pivot = mouth - look * (MouthCentre * s);
				pivot = Vector3.Lerp( f.Position( skull ), pivot, Ease( p / 0.15f ) );
				return MoveIntent.To( pivot, BossDynamics.Strike, look, BossDynamics.TurnStrike ).OnRail().NoGround().InPhase( 2 );
			}
			case BossStage.Rest:
			{
				// the glide: the rail's speed bleeds off past the far point, still heading away
				f.JawOpen01 = 0.45f;
				var sw = new Swoop( f, ctx, target, face, back, reared );
				var look = Rotation.LookAt( sw.Heading ) * Rotation.FromPitch( f.SkullPitch );
				return MoveIntent.To( f.Position( skull ), BossDynamics.Settle, look, BossDynamics.TurnIdle ).NoGround().InPhase( 3 );
			}
			default:
				f.JawOpen01 = 0.45f * (1f - Ease( p ));
				return ReturnHome( f, skull, ref ctx, p, 4 );
		}
	}

	public override void Judge( BossBrain brain, BossPart skull, in BossAttackCtx ctx, float t, BossHazardState hz )
	{
		var p = OwnPlayer();
		if ( p is null || hz.Hit ) return;
		var stage = StageAt( ctx, t, out _ );
		if ( stage != BossStage.Strike ) { hz.HasPrev = false; return; }
		// the mouth's path since last frame (the pass is a frame or two long) plus the box at this frame
		var mouth = skull.WorldTransform.PointToWorld( MouthCentre );
		var s = MathF.Max( 0.01f, skull.WorldScale.x );
		var swept = hz.HasPrev && SweptHits( p, hz.PrevPoint, mouth, MouthRadius * s - HitForgive );
		hz.PrevPoint = mouth;
		hz.HasPrev = true;
		if ( !swept && !BodyInLocalBox( p, skull, Forgiven( skull, MouthBox ) ) ) return;
		hz.Hit = HitLocal( p, skull, Damage, skull.WorldRotation.Forward.WithZ( 0f ).Normal * 320f + Vector3.Up * 120f, "boss_bite" );
	}

	public override void Present( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Rest:
				// the jaw shut through the pass: the click, nothing heavier (a rail touches nothing)
				Click( skull.AimPoint );
				break;
		}
	}

	/// <summary>The lock's click: called by the brain on every peer when the lock arrives.</summary>
	public static void Click( Vector3 at ) => Audio.GameAudio.Play( "sounds/arcsnap.sound", at );
}
