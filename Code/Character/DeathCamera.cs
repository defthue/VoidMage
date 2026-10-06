using System;
using Sandbox;
using VoidMage.Npc;

namespace VoidMage.Character;

/// <summary>
/// The dead player's view. First person: the camera sits between the ragdoll's eye bones (eye_l/eye_r, the eyeballs
/// themselves; the controller's eye point is 8 cm behind them, inside the skull) and looks the way the head does,
/// raw: position AND rotation come from the head, you tumble with the body. Third person (the controller's C
/// toggle still works while dead): the usual orbit, but around the corpse instead of the standing controller, mouse
/// look kept so the body can be looked at. Runs after the punch (900) and the sway (950) so nothing alive leaks
/// into the view, and before the arms (1000), which are hidden while dead anyway. Created at runtime by
/// <see cref="FirstPersonView"/> (the editor rewrites the prefab on its own save).
/// </summary>
[Title( "Death Camera" ), Category( "Void Mage" ), Icon( "visibility_off" )]
public sealed class DeathCamera : Component, ICameraModifier
{
	/// <summary>Units forward of the eyeballs. The brows and the hair fringe reach 2.8 units past the eyes on this rig.</summary>
	[ConVar( "death_cam_nudge" )] public static float Nudge { get; set; } = 4f;
	/// <summary>Near plane while dead (the scene's 10 would also clip the floor the head lies on).</summary>
	[ConVar( "death_cam_near" )] public static float NearClip { get; set; } = 2f;
	/// <summary>1: the eye position still rides the head but the rotation is smoothed and roll is removed (no spinning).</summary>
	[ConVar( "death_cam_steady" )] public static bool Steady { get; set; } = false;
	[ConVar( "death_cam_steady_speed" )] public static float SteadySpeed { get; set; } = 6f;
	/// <summary>Third person: how high above the corpse's pelvis the orbit is centred.</summary>
	[ConVar( "death_cam_orbit_lift" )] public static float OrbitLift { get; set; } = 16f;

	public int CameraOrder => 960;
	public bool Riding { get; private set; }

	private PlayerController _controller;
	private SkeletonCorpse _corpse;
	private BoneCollection.Bone _head, _eyeL, _eyeR, _pelvis;
	private Transform _eyeLocal;
	private Transform _eye;
	private Rotation _steady;

	/// <summary>
	/// Start riding <paramref name="corpse"/> (null is allowed: the view then holds <paramref name="eyeNow"/>, the eye
	/// as it was at death). <paramref name="eyeLocal"/> is the controller's eye in the head bone's frame: its rotation
	/// is how the view sits on the head, its position the fallback when the rig has no eye bones.
	/// </summary>
	public void Begin( SkeletonCorpse corpse, Transform eyeLocal, Transform eyeNow )
	{
		_corpse = corpse;
		_head = _eyeL = _eyeR = _pelvis = null;
		_eyeLocal = eyeLocal;
		_eye = eyeNow;
		_steady = eyeNow.Rotation;
		Riding = true;
	}

	public void End()
	{
		Riding = false;
		_corpse = null;
		_head = _eyeL = _eyeR = _pelvis = null;
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera || !Riding ) return;
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );

		// a corpse that is gone (expired, never built) leaves the last eye where it was; never the standing controller's view
		if ( TryEye( out var eye ) ) _eye = eye;

		if ( _controller.IsValid() && _controller.ThirdPerson )
		{
			Orbit( ref view );
			return;
		}

		var rotation = _eye.Rotation;
		if ( Steady )
		{
			var angles = rotation.Angles();
			angles.roll = 0f;
			_steady = Rotation.Slerp( _steady, angles.ToRotation(), 1f - MathF.Exp( -SteadySpeed * Time.Delta ) );
			rotation = _steady;
		}

		view.Position = _eye.Position + _eye.Rotation.Forward * Nudge;
		view.Rotation = rotation;
		if ( NearClip > 0f ) view.ZNear = NearClip;
	}

	/// <summary>Third person: the controller's orbit (mouse look, ThirdPersonCamera's distance) centred on the corpse.</summary>
	private void Orbit( ref CameraView view )
	{
		var focus = _eye.Position;
		if ( TryBone( ref _pelvis, "pelvis", out var pelvis ) ) focus = pelvis.Position + Vector3.Up * OrbitLift;

		var rotation = _controller.EyeAngles.ToRotation();
		var wanted = focus - rotation.Forward * ThirdPersonCamera.Distance + rotation.Up * _controller.CameraOffset.z;
		var trace = Scene.Trace.Ray( focus, wanted )
			.Radius( ThirdPersonCamera.Radius )
			.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", VoidMage.Combat.Debris.Tag, SkeletonCorpse.RagdollTag )
			.IgnoreGameObjectHierarchy( _controller.GameObject )
			.Run();
		view.Position = trace.StartedSolid ? focus : trace.Hit ? trace.EndPosition : wanted;
		view.Rotation = rotation;
	}

	/// <summary>The eye: between the eyeball bones, facing the way the head faces (the view's offset on the head from death).</summary>
	private bool TryEye( out Transform eye )
	{
		eye = default;
		if ( !TryBone( ref _head, "head", out var head ) ) return false;
		var onHead = head.ToWorld( _eyeLocal );
		if ( TryBone( ref _eyeL, "eye_l", out var left ) && TryBone( ref _eyeR, "eye_r", out var right ) )
			eye = new Transform( (left.Position + right.Position) * 0.5f, onHead.Rotation );
		else
			eye = onHead;
		return true;
	}

	private bool TryBone( ref BoneCollection.Bone bone, string name, out Transform world )
	{
		world = default;
		if ( !_corpse.IsValid() ) return false;
		var renderer = _corpse.Renderer;
		if ( !renderer.IsValid() ) return false;
		bone ??= renderer.Model?.Bones?.GetBone( name );
		return bone is not null && renderer.TryGetBoneTransform( bone, out world );
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
