using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The third-person view (the PlayerController's C toggle) kept close and out of the walls. The engine's own camera
/// collision traces with IgnoreGameObjectHierarchy(Root), and the dungeon builder parents the player under the
/// dungeon, so its trace ignores every wall: this modifier re-traces from the eye to where the engine put the camera
/// and stops it at the first wall. The orbit distance is set here too (half the engine default), so the prefab's
/// value does not matter. Created at runtime by <see cref="FirstPersonView"/>.
/// </summary>
[Title( "Third Person Camera" ), Category( "Void Mage" ), Icon( "videocam" )]
public sealed class ThirdPersonCamera : Component, ICameraModifier
{
	/// <summary>How far behind the eye the third-person camera sits, units (the engine default is 256).</summary>
	[ConVar( "cam_third_distance" )] public static float Distance { get; set; } = 128f;
	/// <summary>Sphere radius of the wall trace: keeps the near plane out of the wall it stops at.</summary>
	[ConVar( "cam_third_radius" )] public static float Radius { get; set; } = 8f;

	/// <summary>
	/// V in third person: the camera goes round to the other side and looks back at the character's face (for checking
	/// what the hands hold, the potion grip tuning first of all). Mouse look still orbits: yaw turns the body with the
	/// camera staying in front, pitch raises or lowers the camera. Leaving third person turns it off.
	/// </summary>
	public static bool FrontView { get; private set; }

	private PlayerController _controller;
	private bool _ignoreSet;

	/// <summary>What the camera passes through: the player's own things, effects, and every monster (skeletons, the boss's parts): only the world pushes the camera in.</summary>
	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "boss", VoidMage.Combat.Debris.Tag, VoidMage.Npc.SkeletonCorpse.RagdollTag };

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_controller.IsValid() ) return;
		if ( !_controller.ThirdPerson ) FrontView = false;
		else if ( Input.Pressed( "FrontView" ) ) FrontView = !FrontView;
		var offset = _controller.CameraOffset;
		if ( offset.x != Distance ) _controller.CameraOffset = offset.WithX( Distance );
		if ( !_ignoreSet )
		{
			// the engine's own camera collision trace honours this set: monsters never shove the camera forward
			_ignoreSet = true;
			foreach ( var t in PassThrough ) _controller.CameraCollisionIgnore.Add( t );
		}
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		if ( !_controller.IsValid() || !_controller.ThirdPerson ) return;

		var eye = _controller.EyePosition;
		if ( FrontView )
		{
			// the mirror of the engine's orbit: the same distance and height, in front of the look instead of behind it,
			// facing back at the eye (pitch inverted, so moving the mouse up still lifts the camera)
			var look = _controller.EyeAngles;
			var orbit = Rotation.From( -look.pitch, look.yaw, 0f );
			var height = Vector3.Up * _controller.CameraOffset.z;
			view.Position = eye + orbit.Forward * Distance + height;
			view.Rotation = Rotation.LookAt( (eye + height * 0.5f - view.Position).Normal, Vector3.Up );
		}
		if ( (view.Position - eye).IsNearZeroLength ) return;
		var trace = Scene.Trace.Ray( eye, view.Position )
			.Radius( Radius )
			.WithoutTags( PassThrough )
			.IgnoreGameObjectHierarchy( _controller.GameObject )
			.Run();
		if ( trace.StartedSolid ) view.Position = eye;
		else if ( trace.Hit ) view.Position = trace.EndPosition;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
