using System;
using Sandbox;

namespace VoidMage.Boss;

/// <summary>
/// The wrist. The hand model is palm + fingers; the forearm is a child object (`forearm`, tools/rig_hell_hand.py
/// split it off with the wrist as its origin) that this component hangs from the wrist and swings on every peer
/// from the hand's observed motion, nothing networked. The elbow direction is a unit vector on a spring-damper
/// chasing a blend of the sculpt's straight-wrist direction, gravity and the trail of the hand's velocity, clamped
/// to a cone so the wrist never breaks: at rest the forearm hangs, in a swing it lags behind the palm, on a slam it
/// stacks above the fist. It ignores the scenery entirely. Off while the boss is dormant (the sculpt pose on the pillar).
/// </summary>
[Title( "Boss Arm" ), Category( "Void Mage" ), Icon( "back_hand" )]
public sealed class BossArm : Component
{
	/// <summary>The wrist point in the hand's model units: where the forearm child sits.</summary>
	[Property] public Vector3 Wrist { get; set; }
	/// <summary>The forearm MESH's own axis from the wrist toward the elbow, unit, model frame (the sculpt's bent wrist): what the child is turned from.</summary>
	[Property] public Vector3 ElbowDir { get; set; } = Vector3.Backward;
	/// <summary>The straight wrist: the forearm direction opposite the fingers, unit, model frame. The spring's base; zero falls back to ElbowDir.</summary>
	[Property] public Vector3 RestDir { get; set; }
	/// <summary>The palm centre in model units (the part's pivot; here for the debug draw).</summary>
	[Property] public Vector3 Pivot { get; set; }

	/// <summary>
	/// The pulls on the forearm. The hand is dominant: the straight wrist is the base, and the forearm only deviates
	/// (within MaxBend) toward where a shoulder would be on the skull's side, a little gravity if wanted, and a little
	/// trail behind the hand's velocity (per 1000 u/s), all lagging on the spring.
	/// </summary>
	[ConVar( "boss_arm_natural", ConVarFlags.Saved )] public static float NaturalWeight { get; set; } = 1f;
	[ConVar( "boss_arm_shoulder", ConVarFlags.Saved )] public static float ShoulderWeight { get; set; } = 0.6f;
	[ConVar( "boss_arm_gravity", ConVarFlags.Saved )] public static float GravityWeight { get; set; } = 0f;
	[ConVar( "boss_arm_trail", ConVarFlags.Saved )] public static float TrailWeight { get; set; } = 0.3f;
	/// <summary>The spring on the elbow direction: stiffness (1/s²) and damping (1/s); lower damping = more swing.</summary>
	[ConVar( "boss_arm_stiffness", ConVarFlags.Saved )] public static float Stiffness { get; set; } = 45f;
	[ConVar( "boss_arm_damping", ConVarFlags.Saved )] public static float Damping { get; set; } = 9f;
	/// <summary>How far the wrist may bend from the sculpt's straight wrist, degrees.</summary>
	[ConVar( "boss_arm_max_bend", ConVarFlags.Saved )] public static float MaxBend { get; set; } = 30f;
	/// <summary>The virtual shoulder: this far to the hand's side of the skull's centre (skull model units x its scale) and this far up.</summary>
	[ConVar( "boss_arm_shoulder_side", ConVarFlags.Saved )] public static float ShoulderSide { get; set; } = 260f;
	[ConVar( "boss_arm_shoulder_up", ConVarFlags.Saved )] public static float ShoulderUp { get; set; } = 40f;
	/// <summary>The forearm's length from the wrist to the elbow end, model units (the split mesh's extent).</summary>
	[Property] public float Length { get; set; } = 360f;

	/// <summary>True forces the swing on even while the boss is dormant (the brain's phase drives it otherwise).</summary>
	public bool Articulate { get; set; }

	private GameObject _forearm;
	private Vector3 _dir;
	private Vector3 _dirVel;
	private Vector3 _lastPos;
	private Vector3 _handVel;
	private bool _init;

	protected override void OnStart()
	{
		foreach ( var c in GameObject.Children ) if ( c.Name == "forearm" ) { _forearm = c; break; }
		if ( !_forearm.IsValid() ) return;
		_forearm.LocalPosition = Wrist;
		_dir = MeshAxis;
		_lastPos = WorldPosition;
	}

	/// <summary>The straight-wrist forearm direction in the world, for the hand's current rotation.</summary>
	private Vector3 Natural => (WorldRotation * (RestDir.IsNearZeroLength ? ElbowDir : RestDir)).Normal;
	/// <summary>Where the forearm mesh's axis points for the hand's current rotation with no swing applied (the sculpt).</summary>
	private Vector3 MeshAxis => (WorldRotation * ElbowDir).Normal;

	protected override void OnUpdate()
	{
		if ( !_forearm.IsValid() ) return;
		var dt = Time.Delta;
		if ( dt <= 0f ) return;
		var natural = Natural;
		var brain = BossBrain.Current;
		var active = Articulate || (brain.IsValid() && brain.Phase != BossPhase.Dormant);
		if ( !active || !_init )
		{
			// dormant: the sculpt as it lies on the pillar (bent wrist and all); the spring starts from there on the wake-up
			_dir = MeshAxis;
			_dirVel = Vector3.Zero;
			_handVel = Vector3.Zero;
			_lastPos = WorldPosition;
			_init = true;
			if ( !active ) { Apply( _dir ); return; }
		}
		// the hand's velocity, smoothed, from what every peer can see: its transform
		var v = (WorldPosition - _lastPos) / dt;
		_lastPos = WorldPosition;
		_handVel = Vector3.Lerp( _handVel, v, Math.Clamp( dt * 12f, 0f, 1f ) );

		// where a shoulder would be: on the skull's side, so the forearm leans back toward the body like a real arm
		var wristNow = WorldTransform.PointToWorld( Wrist );
		var shoulderDir = Vector3.Zero;
		if ( brain.IsValid() )
		{
			var part = Components.Get<BossPart>();
			var side = part.IsValid() && part.Kind == BossPartKind.HandR ? -brain.WorldRotation.Left : brain.WorldRotation.Left;
			var shoulder = brain.WorldPosition + (side.WithZ( 0f ).Normal * ShoulderSide + Vector3.Up * ShoulderUp) * brain.S;
			var to = shoulder - wristNow;
			if ( !to.IsNearZeroLength ) shoulderDir = to.Normal;
		}
		var target = natural * NaturalWeight + shoulderDir * ShoulderWeight + Vector3.Down * GravityWeight - _handVel / 1000f * TrailWeight;
		target = target.IsNearZeroLength ? natural : target.Normal;
		// the cone: never further from the straight wrist than MaxBend
		var angle = MathF.Acos( Math.Clamp( Vector3.Dot( natural, target ), -1f, 1f ) ).RadianToDegree();
		if ( angle > MaxBend )
		{
			var axis = Vector3.Cross( natural, target );
			target = axis.IsNearZeroLength ? natural : Rotation.FromAxis( axis.Normal, MaxBend ) * natural;
		}
		// no scenery avoidance: the arm goes through props and the floor rather than climbing over them (the boss
		// colliders push nothing, and a forearm riding up over a pillar looked far worse than one passing through it)
		// the spring on the direction vector, renormalised (a point on the sphere chasing another)
		var acc = (target - _dir) * Stiffness - _dirVel * Damping;
		_dirVel += acc * dt;
		_dir = (_dir + _dirVel * dt).Normal;
		Apply( _dir );
	}

	/// <summary>Turn the forearm so its mesh axis points along `dir`, keeping its roll with the hand.</summary>
	private void Apply( Vector3 dir )
	{
		var rot = Rotation.FromToRotation( MeshAxis, dir ) * WorldRotation;
		_forearm.WorldPosition = WorldTransform.PointToWorld( Wrist );
		_forearm.WorldRotation = rot;
	}
}
