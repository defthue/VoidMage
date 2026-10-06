using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Boss;

/// <summary>Where an attack is in its timeline. Every attack has the same five beats, so the player can learn one grammar.</summary>
public enum BossStage { Approach, Tell, Strike, Rest, Recover, Done }

/// <summary>
/// The frozen inputs of one attack, the same on every peer: the host broadcasts them when the attack begins and
/// again when it locks (the target point stops tracking the player). Motion on the host and the hazard judging on
/// every peer are pure functions of these and the local clock.
/// </summary>
public struct BossAttackCtx
{
	public int Seq;
	public int AttackId;
	public BossPartKind Part;
	/// <summary>The locked point: the player's feet at the lock (the shake: the skull's own floor point).</summary>
	public Vector3 Target;
	/// <summary>The ground under the target.</summary>
	public float FloorZ;
	/// <summary>The skull's facing (yaw only) at the lock.</summary>
	public Rotation Facing;
	public Vector3 StartPos;
	public Rotation StartRot;
	/// <summary>The tell runs at this rate: below 1 is slower (the opener), above 1 a follow-up.</summary>
	public float Speed;
	/// <summary>The approach's length in seconds, planned from the part's flight path when the move began (BossAttack.PlanApproach).</summary>
	public float ApproachSeconds;
	public bool Locked;
	/// <summary>Host-only scratch a move may keep between ticks (a second aim point, a sampled position); never broadcast, peers judge against the parts' real transforms.</summary>
	public Vector3 Aux;
	public float AuxF;
}

/// <summary>Per-peer bookkeeping for judging the local player against one attack: one hit per attack, ring crossings, chip timer.</summary>
public sealed class BossHazardState
{
	public int Seq = -1;
	public float PrevT = -1f;
	public bool Hit;
	public float NextChip;
	public int RingsCrossed;
	/// <summary>The last stage presented, as an int so -1 means "none yet".</summary>
	public int PrevStage = -1;
	/// <summary>Where the business end was last frame (the swept test: a fast pass never skips over the body between frames).</summary>
	public Vector3 PrevPoint;
	public bool HasPrev;
	/// <summary>A move's own sub-beat counter (the march's landings): Judge runs every frame on every peer, so it presents its beats itself.</summary>
	public int Beat = -1;
	/// <summary>The local player is held by the boss (the grab): while set the move drives the player.</summary>
	public bool Held;
	/// <summary>The hand's fly-by woosh has been started for this move on this peer.</summary>
	public bool Wooshed;
	/// <summary>The move's impact (a real contact) has been presented on this peer.</summary>
	public bool Impacted;
}

/// <summary>
/// One move of the Bone Colossus. Timing: approach (the part travels to its start pose at a readable speed), tell
/// (a held, distinct pose; the target tracks the player until <see cref="LockAt"/> of it has passed, then freezes),
/// strike (fast, committed), rest (the part lies at floor level in sword reach, the reward window), recover (back
/// to the stance). <see cref="Move"/> writes the part's <see cref="MoveIntent"/> for a time (the host steps the
/// mover with it; the offline simulation does the same); <see cref="Judge"/> runs on every peer against that peer's
/// own player (the trap model: owner-authoritative health); <see cref="Present"/> runs on every peer for the feel
/// (shake, sounds) keyed on stage entry. Every timing is a saved convar on the concrete attack.
/// </summary>
public abstract class BossAttack
{
	public abstract string Name { get; }
	/// <summary>True for a hand move (the brain picks which hand); false for a skull move.</summary>
	public abstract bool UsesHand { get; }
	/// <summary>A hand move only one hand does (the right hand's low sweep, the left hand's palm strike); null = either.</summary>
	public virtual BossPartKind? OnlyHand => null;
	/// <summary>A move both hands do together (the clap): the brain calls <see cref="Move"/> for each hand with the same ctx (Part = HandL), needs both alive.</summary>
	public virtual bool UsesBothHands => false;
	/// <summary>No click at the lock (a move with no aim to freeze: the reposition).</summary>
	public virtual bool SilentLock => false;

	/// <summary>How much every strike zone is pulled in, world units: the benefit of the doubt for a dodge that looked clear.</summary>
	[ConVar( "boss_hit_forgive", ConVarFlags.Saved )] public static float HitForgive { get; set; } = 20f;
	/// <summary>
	/// How long a hand trembles frozen on its aim between the lock and the strike, seconds (divided by the move's
	/// speed): the window the player reads the direction in and gets out of. A hand move's lock is this long before
	/// its tell ends; before it the hand tracks the player. Close to a second: the hands are big and a player without
	/// boons is not fast (the tells grew by the same half second so the tracking part stayed as long).
	/// </summary>
	[ConVar( "boss_tell_shake", ConVarFlags.Saved )] public static float ShakeSeconds { get; set; } = 0.95f;
	/// <summary>How far into the platform a hand's lowest point may go on a floor contact, world units ("a handful of units inside is fine").</summary>
	[ConVar( "boss_floor_sink", ConVarFlags.Saved )] public static float FloorSink { get; set; } = 8f;

	/// <summary>
	/// The pivot height that puts the hand's lowest point (as it is posed and turned RIGHT NOW) `FloorSink` into the
	/// floor at `floorZ`. Recomputed every tick, so a hand that turns flat while it lands settles onto the floor
	/// rather than floating on the sculpt's hanging claws or sinking half in. `extra` lifts it further.
	/// </summary>
	protected static float FloorPivot( IBossFrame f, BossPartKind hand, float floorZ, float extra = 0f )
		=> floorZ - f.LiveBottom( hand ) - FloorSink + extra;

	/// <summary>The pivot's clearance over the ground that puts the hand's live lowest point `FloorSink` into it: for `MoveIntent.PivotOverGround`, so a landing makes a real contact.</summary>
	protected static float FloorClamp( IBossFrame f, BossPartKind hand ) => -f.LiveBottom( hand ) - FloorSink;

	/// <summary>How far up the return arc after a pass (the bite, the ram) rises over the straight line, world units x the skull's scale.</summary>
	[ConVar( "boss_return_lift", ConVarFlags.Saved )] public static float ReturnLift { get; set; } = 150f;

	/// <summary>The skull's way home after a pass: from where the Recover stage found it (sampled once into ctx.Aux) round a wide arc to the hover, looking along the flight, facing the player only over the last stretch.</summary>
	protected static MoveIntent ReturnHome( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float p, int phase )
	{
		if ( ctx.AuxF < 2f ) { ctx.Aux = f.Position( skull ); ctx.AuxF = 2f; }
		var hover = f.SkullHover();
		var q = Ease( p );
		var pos = BossGeometry.ReturnArc( f, ctx.Aux, hover.Position, ReturnLift * f.S, q, out var tangent );
		var flat = tangent.WithZ( tangent.z * 0.4f );
		var along = (flat.IsNearZeroLength ? hover.Rotation : Rotation.LookAt( flat.Normal ) * Rotation.FromPitch( f.SkullPitch ));
		var look = Rotation.Slerp( along, hover.Rotation, Ease( (q - 0.6f) / 0.4f ) );
		return MoveIntent.To( pos, BossDynamics.Strike, look, BossDynamics.TurnStrike ).OnRail().NoGround().InPhase( phase );
	}

	public abstract float Approach { get; }
	public abstract float Tell { get; }
	public abstract float Strike { get; }
	public abstract float Rest { get; }
	public abstract float Recover { get; }
	/// <summary>Fraction of the tell after which the target is frozen.</summary>
	public abstract float LockAt { get; }
	/// <summary>Damage the skull takes while this move rests (the shake's exposed stall doubles it).</summary>
	public virtual float RestMultiplier => 1f;

	/// <summary>The approach's length for a part `dist` world units from its approach point: the nominal, or long enough to get there at the approach speed with a settle.</summary>
	public float PlanApproach( float dist, float speed )
		=> MathF.Max( Approach / speed, dist / MathF.Max( 100f, BossDynamics.ApproachSpeed ) * 1.15f + 0.35f );

	/// <summary>Plans the approach from the move's own first intent: the 3D path the part must fly (via its waypoint if it has one).</summary>
	public float PlanApproach( IBossFrame f, BossPartKind part, ref BossAttackCtx ctx, Vector3 playerFeet )
	{
		var first = Move( f, part, ref ctx, 0f, playerFeet );
		var pos = f.Position( part );
		var dist = first.HasWaypoint ? pos.Distance( first.Waypoint ) + first.Waypoint.Distance( first.Target ) : pos.Distance( first.Target );
		return PlanApproach( dist, ctx.Speed );
	}

	public float ApproachEnd( in BossAttackCtx ctx ) => ctx.ApproachSeconds > 0f ? ctx.ApproachSeconds : Approach / ctx.Speed;
	public float TellEnd( in BossAttackCtx ctx ) => ApproachEnd( ctx ) + Tell / ctx.Speed;
	public float StrikeEnd( in BossAttackCtx ctx ) => TellEnd( ctx ) + Strike;
	public float RestEnd( in BossAttackCtx ctx ) => StrikeEnd( ctx ) + Rest;
	public float Total( in BossAttackCtx ctx ) => RestEnd( ctx ) + Recover;
	/// <summary>A hand move locks <see cref="ShakeSeconds"/> before its tell ends (the tremble window); a skull move at its LockAt fraction. A move with no tracking (the grab's grounded wind-up) locks as its approach ends.</summary>
	public virtual float LockTime( in BossAttackCtx ctx )
		=> UsesHand ? MathF.Max( ApproachEnd( ctx ), TellEnd( ctx ) - ShakeSeconds / ctx.Speed ) : ApproachEnd( ctx ) + Tell * LockAt / ctx.Speed;

	/// <summary>Every peer: the part made a REAL contact (a hard stop on the ground or at its stop point) at `at` during `stage`: the thud, the debris, the ring, the shake belong here, never on the clock. Once per move unless <see cref="MultiImpact"/>.</summary>
	public virtual void OnImpact( BossBrain brain, BossPart part, in BossAttackCtx ctx, BossStage stage, Vector3 at ) { }
	/// <summary>A move with several landings (the march): every contact is presented.</summary>
	public virtual bool MultiImpact => false;

	public BossStage StageAt( in BossAttackCtx ctx, float t, out float p )
	{
		var a = ApproachEnd( ctx );
		if ( t < a ) { p = a > 0f ? t / a : 1f; return BossStage.Approach; }
		var tell = Tell / ctx.Speed;
		t -= a;
		if ( t < tell ) { p = tell > 0f ? t / tell : 1f; return BossStage.Tell; }
		t -= tell;
		if ( t < Strike ) { p = Strike > 0f ? t / Strike : 1f; return BossStage.Strike; }
		t -= Strike;
		if ( t < Rest ) { p = Rest > 0f ? t / Rest : 1f; return BossStage.Rest; }
		t -= Rest;
		if ( t < Recover ) { p = Recover > 0f ? t / Recover : 1f; return BossStage.Recover; }
		p = 1f;
		return BossStage.Done;
	}

	/// <summary>Selection weight for the current situation; 0 = never. `dist` is the player's horizontal distance from the skull's stance point.</summary>
	public abstract float Weight( BossBrain brain, float dist );

	/// <summary>What the lock freezes: the nearest player's feet by default.</summary>
	public virtual Vector3 LockTarget( IBossFrame f, BossPartKind part, Vector3 playerFeet ) => playerFeet;

	/// <summary>The part's intent for time `t` (seconds since the attack began). `tracked` is the live target until the lock.</summary>
	public abstract MoveIntent Move( IBossFrame f, BossPartKind part, ref BossAttackCtx ctx, float t, Vector3 tracked );

	/// <summary>Every peer: hit the local player if the attack touches it at time `t`.</summary>
	public abstract void Judge( BossBrain brain, BossPart part, in BossAttackCtx ctx, float t, BossHazardState hz );

	/// <summary>Every peer: feel on stage entry (the impact shake, the sounds). Default: nothing.</summary>
	public virtual void Present( BossBrain brain, BossPart part, in BossAttackCtx ctx, BossStage entered, float t ) { }

	/// <summary>Host: where a HAND move wants the skull while it runs (the march: hanging back behind the line, walking up with the hands), or null to leave it to the stance. The formation hands follow the skull wherever this puts it.</summary>
	public virtual MoveIntent? Escort( IBossFrame f, in BossAttackCtx ctx, float t ) => null;

	/// <summary>Every peer: the move was cut short by the host (a stagger, a death). Default: nothing (the grab lets go).</summary>
	public virtual void Cancelled( BossBrain brain, in BossAttackCtx ctx, BossHazardState hz ) { }

	/// <summary>A skull move's frozen tell: the settled body trembling on the spot with growing amplitude (a visual offset, the body never moves).</summary>
	protected static MoveIntent SkullTremble( IBossFrame f, BossPartKind skull, float t, float amp01, Rotation look, int phase )
	{
		var (offset, jitter) = BossGeometry.Tremble( t, amp01, f.S );
		return MoveIntent.To( f.Position( skull ), BossDynamics.Settle, look * jitter, BossDynamics.TurnIdle )
			.NoGround().Braked().Shifted( offset ).WithLean( 0f ).InPhase( phase );
	}

	/// <summary>Host, every tick of the running move after its intents: things the host has to author and broadcast (the shake's eye bolts). Default: nothing.</summary>
	public virtual void HostTick( BossBrain brain, in BossAttackCtx ctx, float t ) { }

	// ------------------------------------------------------------------ helpers for the moves

	public static float Ease( float t ) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);
	public static float EaseIn( float t ) => Math.Clamp( t, 0f, 1f ) * Math.Clamp( t, 0f, 1f );
	public static float EaseOut( float t ) { t = Math.Clamp( t, 0f, 1f ); return 1f - (1f - t) * (1f - t); }

	/// <summary>The local player, only if this peer owns it and it is alive.</summary>
	protected static PlayerHealth OwnPlayer()
	{
		var p = PlayerHealth.Local;
		return p.IsValid() && !p.IsProxy && !p.IsDead ? p : null;
	}

	/// <summary>One hit on the local player. Per-attacker grace lives in PlayerHealth; `attacker` defaults to the part.</summary>
	protected static bool HitLocal( PlayerHealth p, BossPart part, float damage, Vector3 push, string kind, GameObject attacker = null )
	{
		if ( p is null || !part.IsValid() ) return false;
		p.TakeHit( damage, push, part.AimPoint, kind, attacker ?? part.GameObject );
		return true;
	}

	/// <summary>Is the player's body (feet to head) inside the part's box? Ducking lowers the head, so a low sweep clears a crouched player.</summary>
	protected static bool BodyInPart( PlayerHealth p, BossPart part )
	{
		var feet = p.FeetPosition;
		var h = p.BodyHeight;
		return part.Contains( feet + Vector3.Up * 8f ) || part.Contains( feet + Vector3.Up * h * 0.5f ) || part.Contains( feet + Vector3.Up * (h - 4f) );
	}

	/// <summary>Is the player's body inside a box given in the part's local (model) units? Forgiving: the body centre, or the low point and the head both (the box spans the body).</summary>
	protected static bool BodyInLocalBox( PlayerHealth p, BossPart part, BBox localModelUnits )
	{
		var feet = p.FeetPosition;
		var h = p.BodyHeight;
		if ( part.ContainsLocal( feet + Vector3.Up * h * 0.5f, localModelUnits ) ) return true;
		return part.ContainsLocal( feet + Vector3.Up * h * 0.25f, localModelUnits ) && part.ContainsLocal( feet + Vector3.Up * (h - 4f), localModelUnits );
	}

	/// <summary>
	/// Is the player's body in a hand's strike surfaces (the palm, fingers and wrist boxes riding the animated
	/// bones, <see cref="BossPart.InStrikeShapes"/>, each pulled in by <see cref="HitForgive"/>)? Hit on the body
	/// centre, or on the low point and the head together.
	/// </summary>
	protected static bool BodyInStrike( PlayerHealth p, BossPart hand )
	{
		var feet = p.FeetPosition;
		var h = p.BodyHeight;
		if ( hand.InStrikeShapes( feet + Vector3.Up * h * 0.5f, HitForgive ) ) return true;
		return hand.InStrikeShapes( feet + Vector3.Up * h * 0.25f, HitForgive ) && hand.InStrikeShapes( feet + Vector3.Up * (h - 4f), HitForgive );
	}

	/// <summary>How far a hand draws back against its strike direction while it trembles, world units x the boss's scale: the wind-up that tells which way it will fly.</summary>
	[ConVar( "boss_tell_windup", ConVarFlags.Saved )] public static float WindupUnits { get; set; } = 180f;

	/// <summary>
	/// A hand move's tell: before the lock the hand tracks `aim` firmly (the aim reads); from the lock it trembles
	/// (an offset, the body never moves) until the strike, so the player sees the direction and the moment. With a
	/// `windup` direction (opposite to the strike's flight) it also draws back that way slowly through the tremble:
	/// the wind-up before the blow. `phase` is the tell's phase id; the frozen half uses phase + 10.
	/// </summary>
	protected MoveIntent HandTell( IBossFrame f, BossPartKind hand, in BossAttackCtx ctx, float t, Vector3 aim, Rotation look, int pose, int phase, Vector3 windup = default )
	{
		if ( !ctx.Locked )
			return MoveIntent.To( aim, BossDynamics.Track, look, BossDynamics.TurnIdle ).NoGround().WithPose( pose ).InPhase( phase );
		var window = MathF.Max( 0.05f, ShakeSeconds / ctx.Speed );
		var k = Math.Clamp( (t - LockTime( ctx )) / window, 0f, 1f );
		var (offset, jitter) = BossGeometry.Tremble( t, k, f.S );
		if ( windup.IsNearZeroLength )
			return MoveIntent.To( f.Position( hand ), BossDynamics.Settle, look * jitter, BossDynamics.TurnIdle )
				.NoGround().Braked().Shifted( offset ).WithLean( 0f ).WithPose( pose ).InPhase( phase + 10 );
		var drawn = aim + windup.Normal * (WindupUnits * f.S * Ease( k ));
		return MoveIntent.To( drawn, BossDynamics.Tell, look * jitter, BossDynamics.TurnIdle )
			.NoGround().Braked().Shifted( offset ).WithLean( 0f ).WithPose( pose ).InPhase( phase + 10 );
	}

	/// <summary>
	/// The swept test for a fast business end: did the point sweep from `prev` to `now` pass within `radius` of the
	/// player's spine (feet to head)? Frame-rate proof: a mouth at 4200 u/s crosses a body in a frame or two.
	/// </summary>
	protected static bool SweptHits( PlayerHealth p, Vector3 prev, Vector3 now, float radius )
	{
		var a0 = p.FeetPosition + Vector3.Up * 6f;
		var a1 = p.FeetPosition + Vector3.Up * (p.BodyHeight - 4f);
		// closest points between two segments (the spine and the sweep), coarse but plenty: sample the sweep
		var best = float.MaxValue;
		for ( var i = 0; i <= 8; i++ )
		{
			var q = Vector3.Lerp( prev, now, i / 8f );
			var seg = a1 - a0;
			var k = Math.Clamp( Vector3.Dot( q - a0, seg ) / MathF.Max( 1f, seg.LengthSquared ), 0f, 1f );
			best = MathF.Min( best, (a0 + seg * k).Distance( q ) );
		}
		return best <= radius;
	}

	/// <summary>A model-units box shrunk by the forgiveness (in world units, through the part's scale).</summary>
	protected static BBox Forgiven( BossPart part, BBox localModelUnits )
	{
		var f = HitForgive / MathF.Max( 0.01f, part.WorldScale.x );
		var mins = localModelUnits.Mins + new Vector3( f );
		var maxs = localModelUnits.Maxs - new Vector3( f );
		return new BBox( Vector3.Min( mins, maxs ), Vector3.Max( mins, maxs ) );
	}

	/// <summary>
	/// A floor shockwave: a ring of radius r(t) growing from `centre`. The player is hit when the ring's front crosses
	/// their horizontal distance between the previous and the current frame, unless their feet are above the band
	/// (a jump clears it). `rBefore`/`rNow` are the radii at the previous and current frame.
	/// </summary>
	protected static bool RingCrosses( PlayerHealth p, Vector3 centre, float floorZ, float rBefore, float rNow, float band )
	{
		if ( rNow <= rBefore ) return false;
		var feet = p.FeetPosition;
		if ( feet.z > floorZ + band ) return false;
		var d = (feet - centre).WithZ( 0f ).Length;
		return d > rBefore && d <= rNow;
	}

	protected static float RingRadius( float sinceStart, float r0, float speed, float max ) => sinceStart < 0f ? -1f : MathF.Min( max, r0 + speed * sinceStart );

	protected static void Sound( string path, Vector3 at )
	{
		if ( !string.IsNullOrEmpty( path ) ) Audio.GameAudio.Play( path, at );
	}
}

/// <summary>The move table: index = attack id in the RPCs. Order is stable; add at the end.</summary>
public static class BossAttacks
{
	public static readonly IReadOnlyList<BossAttack> All = new BossAttack[]
	{
		new SlamAttack( hammer: false ), new SweepAttack(), new BiteAttack(), new ShakeAttack(),
		new SlamAttack( hammer: true ), new PalmStrikeAttack(), new ClapAttack(),
		new RamAttack(), new RakeAttack(), new MarchAttack(), new RainAttack(), new GrabAttack(), new SummonAttack(),
		new RepositionAttack(),
	};

	public static BossAttack Find( string name )
	{
		foreach ( var a in All ) if ( string.Equals( a.Name, name, StringComparison.OrdinalIgnoreCase ) ) return a;
		return null;
	}

	public static int IdOf( BossAttack a )
	{
		for ( var i = 0; i < All.Count; i++ ) if ( All[i] == a ) return i;
		return -1;
	}

	public static BossAttack ById( int id ) => id >= 0 && id < All.Count ? All[id] : null;
}
