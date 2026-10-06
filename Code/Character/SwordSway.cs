using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The head moving with the sword: a swing is not made from a rigid neck. Every attack clip leans the camera
/// against the swing during its wind-up, whips it with the cut over the cut's measured frames (SwordSlash's
/// crescent rows: the same direction the sparks fly and the stagger goes), then lets a damped spring settle it
/// back with a hint of overshoot. Whether the blade lands or not. When it does land, <see cref="Connect"/> adds a
/// short counter-jolt against the swing, the feel of resistance, and a heavy that lands widens the field of view
/// for a beat. Owner only, applied as a camera modifier between the hit punch (900) and the arms (1000), so the
/// arms ride the moved view. Angles in degrees; the spring runs on scene time, which hitstop never touches.
/// </summary>
[Title( "Sword Sway" ), Category( "Void Mage" ), Icon( "vibration" )]
public sealed class SwordSway : Component, ICameraModifier
{
	/// <summary>Lean against the swing while the arms wind up.</summary>
	// Halved 2026-09-07 (user: the attack head motion read too strong); the shape is unchanged.
	[Property, Group( "Sway" )] public float WindupDegrees { get; set; } = 0.18f;
	/// <summary>Lean with the swing during the cut.</summary>
	[Property, Group( "Sway" )] public float WhipDegrees { get; set; } = 0.8f;
	/// <summary>Heavies move the head this much more.</summary>
	[Property, Group( "Sway" )] public float HeavyScale { get; set; } = 1.4f;
	/// <summary>Roll per degree of sideways lean (the head tilts into a lateral swing).</summary>
	[Property, Group( "Sway" )] public float RollPerDegree { get; set; } = 0.45f;
	/// <summary>Flip if the roll tilts away from the swing instead of into it.</summary>
	[Property, Group( "Sway" )] public bool FlipRoll { get; set; }
	/// <summary>Spring toward the target lean: higher snaps faster.</summary>
	[Property, Group( "Sway" )] public float Stiffness { get; set; } = 380f;
	/// <summary>Velocity damping of the spring: lower overshoots more.</summary>
	[Property, Group( "Sway" )] public float Damping { get; set; } = 16f;

	/// <summary>Counter-jolt against the swing when the blade meets something.</summary>
	[Property, Group( "Connect" )] public float JoltDegrees { get; set; } = 0.6f;
	[Property, Group( "Connect" )] public float JoltHeavyDegrees { get; set; } = 1.0f;
	/// <summary>Field of view added the instant a hit lands (light / heavy), gone after FovPunchSeconds.</summary>
	[Property, Group( "Connect" )] public float FovPunchLightDegrees { get; set; } = 1.0f;
	[Property, Group( "Connect" )] public float FovPunchDegrees { get; set; } = 1.5f;
	[Property, Group( "Connect" )] public float FovPunchSeconds { get; set; } = 0.15f;

	public int CameraOrder => 950;

	private ClipPoseSource _source;
	private int _seenVersion = -1;
	private SwordSlash.SlashProfile _profile;
	private Vector3 _dir;          // eye space: x forward, y left, z up
	private bool _heavy;
	private Vector2 _lean;         // yaw (x, left positive), pitch (y, down positive)
	private Vector2 _leanVel;
	private float _fovPunchAt = float.NegativeInfinity;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !_source.IsValid() ) _source = ClipPoseSource.Find( this );
		if ( _source.IsValid() && _source.Version != _seenVersion )
		{
			_seenVersion = _source.Version;
			var clip = _source.Clip ?? "";
			_profile = null;
			if ( clip.StartsWith( "anim_attack_", StringComparison.Ordinal ) && SwordSlash.TryBladeDirection( clip, Rotation.Identity, out _, out var eyeDir ) )
			{
				SwordSlash.Profiles.TryGetValue( clip, out _profile );
				_dir = eyeDir;
				_heavy = clip.Contains( "heavy", StringComparison.Ordinal );
			}
		}

		var target = Vector2.Zero;
		if ( _profile is not null && _source.IsValid() )
		{
			var t = _source.Elapsed;
			var cutStart = _profile.StartFrame / SwordSlash.ClipFps;
			var cutEnd = MathF.Max( _profile.EndFrame / SwordSlash.ClipFps, cutStart + 0.08f );
			var k = _heavy ? HeavyScale : 1f;
			var lean = new Vector2( _dir.y, -_dir.z );   // left -> yaw left, up -> pitch up
			if ( t < cutStart )
			{
				var ease = MathF.Min( 1f, t / MathF.Max( cutStart, 0.05f ) );
				target = -lean * WindupDegrees * k * ease;
			}
			else if ( t < cutEnd + 0.05f )
			{
				target = lean * WhipDegrees * k;
			}
		}

		// a damped spring: it lags into the lean and overshoots a hair on the way back
		var dt = MathF.Min( Time.Delta, 0.05f );
		_leanVel += (target - _lean) * Stiffness * dt;
		_leanVel *= MathF.Exp( -Damping * dt );
		_lean += _leanVel * dt;
	}

	/// <summary>The blade met something: a jolt against the swing (world direction) and, for a heavy, the FOV punch.</summary>
	public void Connect( Vector3 bladeWorld, Rotation eye, bool heavy )
	{
		if ( IsProxy ) return;
		var local = eye.Inverse * bladeWorld;                 // x forward, y left, z up
		var against = -new Vector2( local.y, -local.z );
		if ( against.LengthSquared < 1e-4f ) against = new Vector2( 0f, 1f );   // a straight thrust: a nod down
		// an impulse whose peak lean is about the jolt size for this spring (peak ~ v0 / sqrt(k))
		_leanVel += against.Normal * (heavy ? JoltHeavyDegrees : JoltDegrees) * MathF.Sqrt( MathF.Max( Stiffness, 1f ) );
		_fovPunchAt = Time.Now;
		_fovPunchDegrees = heavy ? FovPunchDegrees : FovPunchLightDegrees;
	}

	private float _fovPunchDegrees;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		if ( PlayerHealth.Local.IsValid() && PlayerHealth.Local.IsDead ) return;   // the death camera owns the view
		if ( _lean.LengthSquared > 1e-6f )
		{
			var angles = view.Rotation.Angles();
			angles.yaw += _lean.x;
			angles.pitch += _lean.y;
			angles.roll += (FlipRoll ? 1f : -1f) * _lean.x * RollPerDegree;
			view.Rotation = angles.ToRotation();
		}
		var f = (Time.Now - _fovPunchAt) / MathF.Max( 0.01f, FovPunchSeconds );
		if ( f >= 0f && f < 1f )
			view.FieldOfView += _fovPunchDegrees * (1f - f) * (1f - f);
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
