using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Boss;

/// <summary>How a body chases its target: a speed cap, an acceleration cap, velocity drag, and the time it would like to take to close the distance (the gain of the controller).</summary>
public struct Dynamics
{
	public float MaxSpeed, Accel, Drag, ArriveTime;

	public Dynamics( float maxSpeed, float accel, float drag, float arriveTime )
	{
		MaxSpeed = maxSpeed; Accel = accel; Drag = drag; ArriveTime = arriveTime;
	}
}

/// <summary>
/// The dynamics presets and feel numbers, all saved convars: the user tunes them live (`boss_tune` prints the lot).
/// Floaty idle = low accel and speed; heavy strike = huge accel, a hard stop on contact and a lean into the acceleration.
/// </summary>
public static class BossDynamics
{
	[ConVar( "boss_idle_speed", ConVarFlags.Saved )] public static float IdleSpeed { get; set; } = 2200f;
	[ConVar( "boss_idle_accel", ConVarFlags.Saved )] public static float IdleAccel { get; set; } = 6000f;
	[ConVar( "boss_idle_drag", ConVarFlags.Saved )] public static float IdleDrag { get; set; } = 0.8f;
	[ConVar( "boss_idle_arrive", ConVarFlags.Saved )] public static float IdleArrive { get; set; } = 0.22f;

	[ConVar( "boss_approach_speed", ConVarFlags.Saved )] public static float ApproachSpeed { get; set; } = 2200f;
	[ConVar( "boss_approach_accel", ConVarFlags.Saved )] public static float ApproachAccel { get; set; } = 6000f;
	[ConVar( "boss_approach_drag", ConVarFlags.Saved )] public static float ApproachDrag { get; set; } = 0.6f;
	[ConVar( "boss_approach_arrive", ConVarFlags.Saved )] public static float ApproachArrive { get; set; } = 0.4f;

	[ConVar( "boss_tell_speed", ConVarFlags.Saved )] public static float TellSpeed { get; set; } = 400f;
	[ConVar( "boss_tell_accel", ConVarFlags.Saved )] public static float TellAccel { get; set; } = 1400f;
	[ConVar( "boss_tell_drag", ConVarFlags.Saved )] public static float TellDrag { get; set; } = 1.5f;
	[ConVar( "boss_tell_arrive", ConVarFlags.Saved )] public static float TellArrive { get; set; } = 0.5f;
	/// <summary>The aim: a hand tracking the player firmly before its lock (quick, little lag), so where it will strike reads.</summary>
	[ConVar( "boss_track_speed", ConVarFlags.Saved )] public static float TrackSpeed { get; set; } = 900f;
	[ConVar( "boss_track_accel", ConVarFlags.Saved )] public static float TrackAccel { get; set; } = 4000f;
	[ConVar( "boss_track_drag", ConVarFlags.Saved )] public static float TrackDrag { get; set; } = 1f;
	[ConVar( "boss_track_arrive", ConVarFlags.Saved )] public static float TrackArrive { get; set; } = 0.25f;
	/// <summary>Damping ratio of the orientation spring: under 1 overshoots a little (alive), 1 is dead-on.</summary>
	[ConVar( "boss_turn_damping", ConVarFlags.Saved )] public static float TurnDamping { get; set; } = 0.7f;

	[ConVar( "boss_strike_speed", ConVarFlags.Saved )] public static float StrikeSpeed { get; set; } = 5200f;
	[ConVar( "boss_strike_accel", ConVarFlags.Saved )] public static float StrikeAccel { get; set; } = 22000f;
	[ConVar( "boss_strike_drag", ConVarFlags.Saved )] public static float StrikeDrag { get; set; } = 0f;
	[ConVar( "boss_strike_arrive", ConVarFlags.Saved )] public static float StrikeArrive { get; set; } = 0.08f;

	[ConVar( "boss_settle_drag", ConVarFlags.Saved )] public static float SettleDrag { get; set; } = 8f;

	[ConVar( "boss_recover_speed", ConVarFlags.Saved )] public static float RecoverSpeed { get; set; } = 2600f;
	[ConVar( "boss_recover_accel", ConVarFlags.Saved )] public static float RecoverAccel { get; set; } = 6000f;
	[ConVar( "boss_recover_drag", ConVarFlags.Saved )] public static float RecoverDrag { get; set; } = 1f;
	[ConVar( "boss_recover_arrive", ConVarFlags.Saved )] public static float RecoverArrive { get; set; } = 0.3f;

	/// <summary>Lean into acceleration, degrees per 1000 u/s², and the cap.</summary>
	[ConVar( "boss_lean", ConVarFlags.Saved )] public static float Lean { get; set; } = 1.2f;
	[ConVar( "boss_lean_max", ConVarFlags.Saved )] public static float LeanMax { get; set; } = 18f;
	/// <summary>Fraction of the impact speed kept as an upward settle bounce after a hard stop.</summary>
	[ConVar( "boss_bounce", ConVarFlags.Saved )] public static float Bounce { get; set; } = 0.05f;
	[ConVar( "boss_turn_idle", ConVarFlags.Saved )] public static float TurnIdle { get; set; } = 360f;
	[ConVar( "boss_turn_strike", ConVarFlags.Saved )] public static float TurnStrike { get; set; } = 1000f;
	/// <summary>The stance sway: amplitude (world units) and rate (Hz) of the slow drift on every hovering part.</summary>
	[ConVar( "boss_sway_amp", ConVarFlags.Saved )] public static float SwayAmp { get; set; } = 40f;
	[ConVar( "boss_sway_hz", ConVarFlags.Saved )] public static float SwayHz { get; set; } = 0.12f;
	/// <summary>How much a travelling hand turns its palm into its velocity (0 none, 1 fully).</summary>
	[ConVar( "boss_palm_leads", ConVarFlags.Saved )] public static float PalmLeads { get; set; } = 0.6f;
	/// <summary>Height of the arc waypoint over the straight line when a hand lifts out of the floor or crosses the skull.</summary>
	[ConVar( "boss_arc_height", ConVarFlags.Saved )] public static float ArcHeight { get; set; } = 300f;

	public static Dynamics Idle => new( IdleSpeed, IdleAccel, IdleDrag, IdleArrive );
	public static Dynamics Approach => new( ApproachSpeed, ApproachAccel, ApproachDrag, ApproachArrive );
	public static Dynamics Tell => new( TellSpeed, TellAccel, TellDrag, TellArrive );
	public static Dynamics Track => new( TrackSpeed, TrackAccel, TrackDrag, TrackArrive );
	public static Dynamics Strike => new( StrikeSpeed, StrikeAccel, StrikeDrag, StrikeArrive );
	/// <summary>Dead weight: no drive, heavy drag.</summary>
	public static Dynamics Settle => new( 0f, 0f, SettleDrag, 1f );
	/// <summary>The formation: a hand holding its place beside the LIVE skull. Idle = in step (a lag of speed x 0.06 s); trailing = while the skull is in a move, it follows the flight a beat behind.</summary>
	[ConVar( "boss_form_speed", ConVarFlags.Saved )] public static float FormSpeed { get; set; } = 4500f;
	[ConVar( "boss_form_accel", ConVarFlags.Saved )] public static float FormAccel { get; set; } = 40000f;
	[ConVar( "boss_form_arrive", ConVarFlags.Saved )] public static float FormArrive { get; set; } = 0.06f;
	[ConVar( "boss_form_trail_arrive", ConVarFlags.Saved )] public static float FormTrailArrive { get; set; } = 0.14f;
	public static Dynamics Formation => new( FormSpeed, FormAccel, 0f, FormArrive );
	public static Dynamics FormationTrail => new( FormSpeed, FormAccel * 0.75f, 0f, FormTrailArrive );
	/// <summary>The return to the stance after a move: brisk, so the reward window is not followed by dead time.</summary>
	public static Dynamics Recover => new( RecoverSpeed, RecoverAccel, RecoverDrag, RecoverArrive );
}

/// <summary>What a part wants this tick. Filled fresh every tick by the stance, the wake-up or the running move; nothing in it is stateful.</summary>
public struct MoveIntent
{
	public Vector3 Target;
	public Dynamics Dyn;
	/// <summary>Taken before the target while set and not yet reached (the arc rule).</summary>
	public Vector3 Waypoint;
	public bool HasWaypoint;
	public float WaypointRadius;
	/// <summary>The box bottom stays at least this far over the ground (negative: may sink); off with ClampGround false.</summary>
	public float Clearance;
	public bool ClampGround;
	/// <summary>The ground clamp holds the PIVOT (the palm centre) at Clearance over the ground instead of the shapes' lowest point: a landing judged by the palm, not by the claw tips.</summary>
	public bool ClampPivot;
	public Rotation Look;
	public float TurnRate;
	public float Lean;
	/// <summary>Kill the velocity on the first ground contact / on arriving within ArriveRadius (the hit lands and stays).</summary>
	public bool StopOnContact;
	public bool StopOnArrive;
	public float ArriveRadius;
	/// <summary>Kill all velocity the tick this intent's phase is entered (a rest after a swing that may not have finished).</summary>
	public bool StopOnEnter;
	/// <summary>An additive visual offset on the output pose (the convulsion), not part of the state.</summary>
	public Vector3 Offset;
	/// <summary>A hand's finger pose (-1 leaves it).</summary>
	public int Pose;
	/// <summary>Changing this resets the waypoint/stop flags: one per stage.</summary>
	public int Phase;
	/// <summary>Scripted: the body is put ON the target every tick (velocity = the step, so the lean and the trail still read); the orientation spring still turns it.</summary>
	public bool Rail;

	public static MoveIntent To( Vector3 target, Dynamics dyn, Rotation look, float turnRate ) => new()
	{
		Target = target, Dyn = dyn, Look = look, TurnRate = turnRate, ClampGround = true, Clearance = 0f, Lean = 1f, Pose = -1,
	};

	public MoveIntent Over( Vector3 waypoint, float radius = 180f ) { Waypoint = waypoint; HasWaypoint = true; WaypointRadius = radius; return this; }
	public MoveIntent WithClearance( float clearance ) { Clearance = clearance; ClampGround = true; return this; }
	public MoveIntent NoGround() { ClampGround = false; return this; }
	/// <summary>Clamp the pivot itself to `height` over the ground (the shapes may go into it).</summary>
	public MoveIntent PivotOverGround( float height ) { Clearance = height; ClampGround = true; ClampPivot = true; return this; }
	public MoveIntent StopOnGround() { StopOnContact = true; return this; }
	public MoveIntent StopAt( float radius ) { StopOnArrive = true; ArriveRadius = radius; return this; }
	public MoveIntent Braked() { StopOnEnter = true; return this; }
	public MoveIntent Shifted( Vector3 offset ) { Offset = offset; return this; }
	public MoveIntent WithLean( float lean ) { Lean = lean; return this; }
	public MoveIntent WithPose( int pose ) { Pose = pose; return this; }
	public MoveIntent InPhase( int phase ) { Phase = phase; return this; }
	public MoveIntent OnRail() { Rail = true; return this; }
}

/// <summary>
/// A body with velocity chasing a target (Terraria's Skeletron moves this way): it leads, overshoots and settles by
/// construction, the per-stage dynamics decide how softly or violently. Pure maths over Vector3/Rotation so it runs
/// in the offline simulation as well as on the host.
/// </summary>
public sealed class BossMover
{
	public Vector3 Pos;
	public Vector3 Vel;
	public Rotation Rot = Rotation.Identity;
	/// <summary>The ground under the part (refreshed by the owner).</summary>
	public float Ground;
	public Vector3 Accel;
	/// <summary>Angular velocity, axis x degrees per second: the orientation is a spring too, so a turn eases in and settles.</summary>
	public Vector3 AngVel;
	public bool WaypointDone;
	public bool Stopped;
	/// <summary>True on the tick a hard stop happened.</summary>
	public bool Contacted;
	/// <summary>The last two seconds of positions, for the debug trail.</summary>
	public readonly List<(float T, Vector3 P)> Trail = new();
	private int _phase = int.MinValue;
	/// <summary>The waypoint as it was when the phase began: intents recompute theirs from the moving body, the mover flies to a fixed one.</summary>
	private Vector3 _waypoint;

	public float Speed => Vel.Length;

	public void Teleport( Vector3 pos, Rotation rot )
	{
		Pos = pos; Rot = rot; Vel = Vector3.Zero; Accel = Vector3.Zero; AngVel = Vector3.Zero;
		WaypointDone = false; Stopped = false; Contacted = false;
	}

	/// <summary>One tick: drive, then turn (with the lean), then clamp the body, in its new orientation, to the ground. `bottomOffset(rot)` is the part's lowest point relative to its origin for a rotation.</summary>
	public void Step( in MoveIntent it, float dt, Func<Rotation, float> bottomOffset, float now )
	{
		if ( it.Phase != _phase )
		{
			_phase = it.Phase;
			WaypointDone = false;
			Stopped = false;
			_waypoint = it.Waypoint;
			if ( it.StopOnEnter ) Vel = Vector3.Zero;
		}
		Contacted = false;
		var target = it.Target;
		if ( it.HasWaypoint && !WaypointDone )
		{
			if ( Pos.Distance( _waypoint ) <= it.WaypointRadius ) WaypointDone = true;
			else target = _waypoint;
		}
		if ( it.Rail )
		{
			// scripted: on the target, with the velocity and acceleration the step implies
			var newVel = dt > 0f ? (it.Target - Pos) / dt : Vector3.Zero;
			Accel = dt > 0f ? (newVel - Vel) / dt : Vector3.Zero;
			Vel = newVel;
			Pos = it.Target;
		}
		else
		{
			var to = target - Pos;
			var desired = it.Dyn.MaxSpeed <= 0f ? Vector3.Zero : to / MathF.Max( 0.02f, it.Dyn.ArriveTime );
			if ( desired.Length > it.Dyn.MaxSpeed ) desired = desired.Normal * it.Dyn.MaxSpeed;
			var dv = desired - Vel;
			var maxDv = it.Dyn.Accel * dt;
			if ( dv.Length > maxDv ) dv = dv.Normal * maxDv;
			Accel = dt > 0f ? dv / dt : Vector3.Zero;
			Vel += dv;
			Vel *= MathF.Max( 0f, 1f - it.Dyn.Drag * dt );
			Pos += Vel * dt;
		}

		var look = it.Look;
		if ( it.Lean > 0f && BossDynamics.Lean > 0f )
		{
			var local = look.Inverse * Accel;
			var k = it.Lean * BossDynamics.Lean / 1000f;
			var pitch = Math.Clamp( local.x * k, -BossDynamics.LeanMax, BossDynamics.LeanMax );
			var roll = Math.Clamp( -local.y * k, -BossDynamics.LeanMax, BossDynamics.LeanMax );
			look = look * Rotation.FromPitch( pitch ) * Rotation.FromRoll( roll );
		}
		// the orientation spring: the error as axis x degrees, a slightly under-damped spring whose natural
		// frequency comes from the intent's turn rate, integrated as a world-space delta
		var q = look * Rot.Inverse;
		if ( q.w < 0f ) q = new Rotation( -q.x, -q.y, -q.z, -q.w );
		var sinHalf = MathF.Sqrt( q.x * q.x + q.y * q.y + q.z * q.z );
		var errDeg = 2f * MathF.Atan2( sinHalf, q.w ).RadianToDegree();
		var err = sinHalf > 1e-6f ? new Vector3( q.x, q.y, q.z ) / sinHalf * errDeg : Vector3.Zero;
		var w0 = MathF.Max( 0.5f, it.TurnRate / 60f );
		AngVel += (err * (w0 * w0) - AngVel * (2f * BossDynamics.TurnDamping * w0)) * dt;
		var step = AngVel * dt;
		var mag = step.Length;
		if ( mag > 1e-4f ) Rot = Rotation.FromAxis( step / mag, mag ) * Rot;

		if ( it.ClampGround )
		{
			var minZ = Ground + it.Clearance - (it.ClampPivot ? 0f : bottomOffset( Rot ));
			if ( Pos.z < minZ )
			{
				Pos.z = minZ;
				if ( Vel.z < 0f )
				{
					if ( it.StopOnContact && !Stopped )
					{
						Stopped = true; Contacted = true;
						Vel = new Vector3( 0f, 0f, -Vel.z * BossDynamics.Bounce );
					}
					else Vel.z = 0f;
				}
			}
		}
		if ( it.StopOnArrive && !Stopped && (it.Target - Pos).Length <= it.ArriveRadius )
		{
			Stopped = true; Contacted = true;
			Vel = Vector3.Zero;
		}

		if ( Trail.Count == 0 || now - Trail[^1].T >= 0.05f )
		{
			Trail.Add( (now, Pos) );
			while ( Trail.Count > 0 && now - Trail[0].T > 2f ) Trail.RemoveAt( 0 );
		}
	}
}

/// <summary>
/// What a move needs to know about the boss to write its intents: geometry and the shared visible state. The brain
/// implements it on the host; the offline simulation implements it with plain numbers.
/// </summary>
public interface IBossFrame
{
	float S { get; }
	float Now { get; }
	Vector3 StancePoint { get; }
	Rotation Facing { get; }
	float SkullPitch { get; }
	Vector3 SkullCenter { get; }
	Vector3 Position( BossPartKind kind );
	Rotation Orientation( BossPartKind kind );
	Vector3 Velocity( BossPartKind kind );
	Transform SkullHover();
	Transform HandStance( BossPartKind kind );
	Rotation HandRot( BossPartKind kind, Vector3 palmToward, Vector3 fingersToward );
	float BottomOffset( BossPartKind kind, Rotation rot );
	/// <summary>Radius around the skull's centre a hand's centre keeps out of.</summary>
	float KeepOut( BossPartKind hand );
	float JawOpen01 { get; set; }
	float EyeFire01 { get; set; }
	/// <summary>The skull's live yaw: the frame the hands hold formation in.</summary>
	float FormationYaw { get; }
	/// <summary>The fight's centre (the platform's middle).</summary>
	Vector3 ArenaCenter { get; }
	/// <summary>A point pulled inside the arena's radius (flat): a charge never ends in the lava.</summary>
	Vector3 ClampToArena( Vector3 p );
	/// <summary>A point pulled inside `radius` of the arena's centre (flat): the platform, for a crash that must land in sword reach.</summary>
	Vector3 ClampToArena( Vector3 p, float radius );
	/// <summary>The lowest point of the part's hit shapes RIGHT NOW (its pose and rotation as they are) relative to its pivot, world units: negative = below the pivot. What a floor contact is planned from.</summary>
	float LiveBottom( BossPartKind kind );
	/// <summary>The lowest point of one named hit shape of the part (the palm) right now, relative to its pivot: a pass judged by the palm alone is planned from it.</summary>
	float ShapeBottom( BossPartKind kind, string shape );
	/// <summary>The nearest spot to `at` (flat, z kept) with no rim prop under it or under `footprint` around it: where a part may lie without lingering inside a stone.</summary>
	Vector3 ClearSpot( Vector3 at, float footprint = 120f );
}

/// <summary>The path rules and pose helpers shared by the moves, the stance and the wake-up.</summary>
public static class BossGeometry
{
	/// <summary>The z of a box's lowest point relative to its origin for a rotation, in world units.</summary>
	public static float BottomOffset( Vector3 boxCenter, Vector3 boxSize, float scale, Rotation rot )
	{
		var h = boxSize * 0.5f * scale;
		var ext = MathF.Abs( (rot * Vector3.Forward).z ) * h.x + MathF.Abs( (rot * Vector3.Left).z ) * h.y + MathF.Abs( (rot * Vector3.Up).z ) * h.z;
		return (rot * (boxCenter * scale)).z - ext;
	}

	/// <summary>A hand's stance position beside the skull: the boss's left hand on its left.</summary>
	public static Vector3 HandStancePos( Vector3 stance, Rotation facing, BossPartKind kind, float spread, float forward, float height, float s )
	{
		var side = kind == BossPartKind.HandL ? facing.Left : -facing.Left;
		return stance + (side * spread + facing.Forward * forward + Vector3.Up * height) * s;
	}

	/// <summary>
	/// The tremble of a hand frozen on its aim before it strikes: a visual offset and a rotation jitter growing with
	/// `amp01` (0 at the lock, 1 at the strike), scaled by the boss's scale. An offset only: the body never moves.
	/// </summary>
	public static (Vector3 Offset, Rotation Jitter) Tremble( float t, float amp01, float scale )
	{
		var a = MathX.Lerp( 14f, 28f, amp01 ) * scale;
		var offset = new Vector3( MathF.Sin( t * 43f ) * a, MathF.Sin( t * 37f + 1f ) * a, MathF.Sin( t * 51f + 2f ) * a * 0.6f );
		var d = 1.5f + 3f * amp01;
		var jitter = Rotation.From( MathF.Sin( t * 47f ) * d, MathF.Sin( t * 31f + 0.5f ) * d, MathF.Sin( t * 59f + 1f ) * d * 0.7f );
		return (offset, jitter);
	}

	/// <summary>The laughing jaw with no sound under it (the bite's swoop, the summon): open/closed at 3.5 Hz around a third open.</summary>
	public static float Laugh( float t ) => 0.3f + 0.35f * MathF.Sin( t * MathF.Tau * 3.5f );

	/// <summary>
	/// The wake-up's laugh, baked from the user's clip (sounds/BoneColussusLaugh.mp3, 3.3 s: ten "ha"s at about 4.5
	/// a second, then a low rumble): its loudness every 20 ms, so the jaw opens on every "ha" exactly as it sounds.
	/// Read with <see cref="LaughClip"/> by the seconds since the clip started; 0 after it ends.
	/// </summary>
	private static readonly float[] LaughTable =
	{
		0.00f, 0.78f, 0.64f, 0.19f, 0.11f, 0.17f, 0.37f, 0.49f, 0.68f, 0.76f, 0.89f, 0.95f, 0.89f, 0.40f, 0.14f, 0.06f, 0.10f, 0.16f, 0.21f, 0.23f,
		0.66f, 1.00f, 1.00f, 0.51f, 0.26f, 0.08f, 0.05f, 0.05f, 0.06f, 0.30f, 0.51f, 0.80f, 0.89f, 0.75f, 0.35f, 0.16f, 0.09f, 0.05f, 0.04f, 0.10f,
		0.33f, 0.54f, 0.77f, 0.86f, 0.77f, 0.37f, 0.18f, 0.07f, 0.04f, 0.03f, 0.03f, 0.12f, 0.41f, 0.62f, 0.76f, 0.72f, 0.50f, 0.31f, 0.11f, 0.04f,
		0.02f, 0.02f, 0.03f, 0.14f, 0.39f, 0.56f, 0.62f, 0.65f, 0.64f, 0.17f, 0.04f, 0.03f, 0.02f, 0.04f, 0.14f, 0.25f, 0.51f, 0.66f, 0.52f, 0.32f,
		0.10f, 0.04f, 0.02f, 0.02f, 0.02f, 0.03f, 0.14f, 0.31f, 0.49f, 0.52f, 0.45f, 0.19f, 0.05f, 0.02f, 0.01f, 0.02f, 0.02f, 0.05f, 0.10f, 0.19f,
		0.42f, 0.53f, 0.35f, 0.15f, 0.07f, 0.04f, 0.03f, 0.01f, 0.03f, 0.07f, 0.16f, 0.27f, 0.32f, 0.30f, 0.17f, 0.07f, 0.05f, 0.03f, 0.03f, 0.02f,
		0.02f, 0.04f, 0.05f, 0.12f, 0.20f, 0.18f, 0.24f, 0.22f, 0.22f, 0.19f, 0.19f, 0.17f, 0.15f, 0.13f, 0.11f, 0.10f, 0.08f, 0.10f, 0.13f, 0.13f,
		0.11f, 0.12f, 0.09f, 0.09f, 0.06f, 0.06f, 0.06f, 0.05f, 0.04f, 0.03f, 0.02f, 0.02f, 0.02f, 0.02f, 0.01f, 0.01f, 0.00f, 0.00f, 0.00f, 0.00f,
		0.00f, 0.00f, 0.00f, 0.00f, 0.00f,
	};
	private const float LaughStep = 0.02f;
	/// <summary>How long the laugh clip's voice lasts (the rumble after it is the reverb's).</summary>
	public const float LaughClipSeconds = 3.3f;

	/// <summary>The jaw for the laugh clip `seconds` after it started: the clip's loudness, interpolated.</summary>
	public static float LaughClip( float seconds )
	{
		if ( seconds <= 0f ) return 0f;
		var u = seconds / LaughStep;
		var i = (int)u;
		if ( i >= LaughTable.Length - 1 ) return 0f;
		return MathX.Lerp( LaughTable[i], LaughTable[i + 1], u - i );
	}

	/// <summary>The slow drift every hovering part gets: two incommensurate sines per axis.</summary>
	public static Vector3 Sway( float now, float phase, float amp, float hz )
	{
		var t = now * MathF.Tau * hz + phase;
		return new Vector3( MathF.Sin( t * 0.7f ) * 0.6f, MathF.Sin( t + 1.3f ), MathF.Sin( t * 1.31f + 0.4f ) * 0.8f ) * amp;
	}

	/// <summary>
	/// A hand travelling to `target`: if the straight line cuts through the skull's keep-out sphere the intent gets a
	/// detour waypoint, the line's closest point pushed radially out of the sphere (around the skull, never through
	/// it); if `arc` the intent gets a raised midpoint (lifting out of the floor). A target inside the keep-out is
	/// pushed out horizontally. The mover latches the waypoint when the phase begins, so it is a fixed point.
	/// </summary>
	public static MoveIntent HandTravel( IBossFrame f, BossPartKind hand, Vector3 target, Dynamics dyn, Rotation look, float turnRate, bool arc )
	{
		var from = f.Position( hand );
		var skull = f.SkullCenter;
		var keep = f.KeepOut( hand );
		var toSkull = (target - skull).WithZ( 0f );
		if ( (target - skull).Length < keep )
		{
			var dir = toSkull.IsNearZeroLength ? -f.Facing.Forward : toSkull.Normal;
			target = new Vector3( skull.x, skull.y, target.z ) + dir * keep;
		}
		var it = MoveIntent.To( target, dyn, look, turnRate );
		var seg = target - from;
		var len = seg.Length;
		if ( len > 1f )
		{
			var u = Math.Clamp( Vector3.Dot( skull - from, seg ) / (len * len), 0f, 1f );
			var closest = from + seg * u;
			if ( closest.Distance( skull ) < keep && u > 0.02f && u < 0.98f )
			{
				var outward = closest - skull;
				if ( outward.IsNearZeroLength ) outward = Vector3.Cross( seg.Normal, Vector3.Up );
				var detour = skull + outward.Normal * (keep + 120f);
				if ( arc ) detour += Vector3.Up * BossDynamics.ArcHeight * 0.5f;
				return it.Over( detour, 220f );
			}
		}
		if ( arc ) it = it.Over( (from + target) * 0.5f + Vector3.Up * BossDynamics.ArcHeight, 200f );
		return it;
	}

	/// <summary>The floaty idle: the skull drifts around its stance pose with soft dynamics and a slow sway; a hand holds FORMATION beside the live skull (stiff, in step), trailing it a beat while the skull is in a move.</summary>
	public static MoveIntent StanceIntent( IBossFrame f, BossPartKind kind, float now, bool skullBusy = false )
	{
		if ( kind == BossPartKind.Skull )
		{
			var hover = f.SkullHover();
			var sway = Sway( now, 0f, BossDynamics.SwayAmp, BossDynamics.SwayHz );
			sway.z *= 2f;   // the head bobs more than it drifts
			return MoveIntent.To( hover.Position + sway, BossDynamics.Idle, hover.Rotation, BossDynamics.TurnIdle )
				.WithClearance( 50f ).InPhase( -1 );
		}
		var st = f.HandStance( kind );
		var phase = kind == BossPartKind.HandL ? 1.1f : 2.6f;
		var sway2 = skullBusy ? Vector3.Zero : Sway( now, phase, BossDynamics.SwayAmp * 0.75f, BossDynamics.SwayHz * 1.3f );
		return MoveIntent.To( st.Position + sway2, skullBusy ? BossDynamics.FormationTrail : BossDynamics.Formation, st.Rotation, BossDynamics.TurnStrike )
			.WithClearance( 40f ).WithPose( 0 ).InPhase( -1 );
	}

	/// <summary>
	/// The way home after a pass (the bite's swoop, the ram's charge): from the pass end `from` to the hover `to`
	/// over a quadratic Bezier whose control point sits half the gap out to the side of the line (the side nearer
	/// the arena's centre, so the arc stays over the platform) and `lift` up, so the skull swings round in a wide,
	/// readable turn instead of spinning on the spot. `q` 0..1 along the arc; `tangent` is the flight direction there.
	/// </summary>
	public static Vector3 ReturnArc( IBossFrame f, Vector3 from, Vector3 to, float lift, float q, out Vector3 tangent )
	{
		var mid = (from + to) * 0.5f;
		var line = (to - from).WithZ( 0f );
		var n = line.IsNearZeroLength ? Vector3.Left : Vector3.Cross( Vector3.Up, line.Normal );
		if ( Vector3.Dot( n, (f.ArenaCenter - mid).WithZ( 0f ) ) < 0f ) n = -n;
		var control = f.ClampToArena( mid + n * (line.Length * 0.5f) ) + Vector3.Up * lift;
		q = Math.Clamp( q, 0f, 1f );
		var u = 1f - q;
		var p = from * (u * u) + control * (2f * u * q) + to * (q * q);
		tangent = (control - from) * (2f * u) + (to - control) * (2f * q);
		if ( tangent.IsNearZeroLength ) tangent = line.IsNearZeroLength ? Vector3.Forward : line.Normal;
		return p;
	}

	/// <summary>
	/// The wake-up, on a fixed 8 s script (`t8` = seconds on that script): the skull holds its rest until 1.5 then
	/// rises heavily to the stance turning to the player; a hand holds until 2, launches off its pillar (2-2.6),
	/// then flies over an arc to its stance, settling with the idle dynamics from 5.5.
	/// </summary>
	public static MoveIntent WakeIntent( IBossFrame f, BossPartKind kind, float t8, Transform rest, float handLaunchHeight )
	{
		if ( kind == BossPartKind.Skull )
		{
			// the tremble: a small rattle from the first rumble, a violent one for a second when the eyes ignite, dying off as it rises
			var tremble = t8 < 1.5f ? 6f * Math.Clamp( t8 / 1.5f, 0f, 1f )
				: t8 < 2.7f ? 26f * (1f - (t8 - 1.5f) / 1.2f * 0.5f)
				: MathF.Max( 0f, 13f * (1f - (t8 - 2.7f) / 2f) );
			var noise = new Vector3( MathF.Sin( t8 * 41f ) * tremble, MathF.Sin( t8 * 33f + 1f ) * tremble, MathF.Sin( t8 * 47f + 2f ) * tremble * 0.6f ) * f.S;
			var jitter = Rotation.From( MathF.Sin( t8 * 51f ) * tremble * 0.25f, MathF.Sin( t8 * 37f ) * tremble * 0.3f, MathF.Sin( t8 * 29f ) * tremble * 0.2f );
			return t8 < 1.5f
				? MoveIntent.To( rest.Position, BossDynamics.Settle, rest.Rotation * jitter, 400f ).NoGround().Shifted( noise ).WithLean( 0f ).InPhase( 100 )
				: MoveIntent.To( f.StancePoint, BossDynamics.Tell, f.Facing * Rotation.FromPitch( f.SkullPitch ) * jitter, 40f + tremble * 12f ).WithClearance( 0f ).Shifted( noise ).InPhase( 101 );
		}
		if ( t8 < 2f )
			return MoveIntent.To( rest.Position, BossDynamics.Settle, rest.Rotation, BossDynamics.TurnIdle ).NoGround().WithPose( 0 ).InPhase( 100 );
		if ( t8 < 2.6f )
		{
			var launch = new Dynamics( 900f, 5000f, 0f, 0.2f );
			return MoveIntent.To( rest.Position + Vector3.Up * handLaunchHeight, launch, rest.Rotation, BossDynamics.TurnIdle ).NoGround().WithPose( 1 ).InPhase( 101 );
		}
		var stance = f.HandStance( kind );
		var close = f.Position( kind ).Distance( stance.Position ) < 150f;
		return HandTravel( f, kind, stance.Position, t8 < 5.5f ? BossDynamics.Approach : BossDynamics.Idle, stance.Rotation, BossDynamics.TurnIdle, arc: true )
			.WithClearance( 40f ).WithPose( close ? 0 : 1 ).InPhase( 102 );
	}

	/// <summary>A travelling hand's orientation: the intended look blended toward palm-first flight by its speed.</summary>
	public static Rotation PalmLead( IBossFrame f, BossPartKind hand, Rotation look, Vector3 vel )
	{
		var speed = vel.Length;
		if ( speed < 150f || BossDynamics.PalmLeads <= 0f ) return look;
		var lead = f.HandRot( hand, vel.Normal, Vector3.Up );
		return Rotation.Slerp( look, lead, Math.Clamp( (speed - 150f) / 600f, 0f, 1f ) * BossDynamics.PalmLeads );
	}
}
