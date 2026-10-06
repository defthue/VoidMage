using System;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.UI;

namespace VoidMage.Dev;

/// <summary>
/// The director: every dev tool the trailer needs, as buttons (DevPanel) and a few keys, never console commands
/// (user, 2026-09-23: "i cant remember them all"). This component is the hub: it owns TIME (pause, slow motion, a
/// frame step, applied to Scene.TimeScale every frame so nothing else can quietly put it back), the FREE CAMERA
/// (an ICameraModifier above every other, flown with the movement keys on real time so it flies while the game is
/// paused), the HUD switch (every other ScreenPanel's opacity), the keys, and the panel object itself. The
/// actions with no state of their own live in DevActions. One per scene, made by DevDirectorSystem on play.
///
/// Keys (binds nothing else reads): F free camera, R pause, G hide the HUD, mouse 4/5 slower/faster (a frame
/// forward while paused), 0 the panel, Q the cursor while the panel is up. Only while <see cref="Armed"/>.
/// </summary>
[Title( "Dev Director" ), Category( "Void Mage" ), Icon( "movie" )]
public sealed class DevDirector : Component, ICameraModifier
{
	/// <summary>
	/// The director exists only in an editor session (Application.IsEditor = tools mode). In the published game
	/// nothing makes it, the keys read nothing, the panel never opens and the convar below cannot bring it back.
	/// </summary>
	public static bool Available => Application.IsEditor;

	/// <summary>The keys and the dock are live (editor only, see <see cref="Available"/>). Saved: it stays on between plays.</summary>
	[ConVar( "dev_director", ConVarFlags.Saved )] public static bool Armed { get; set; } = true;
	/// <summary>What "pause" sets the time scale to. 0 stops everything; a hair above it keeps fixed updates ticking, should something need that.</summary>
	[ConVar( "dev_pause_scale" )] public static float PauseScale { get; set; } = 0f;
	/// <summary>The free camera's base speed, units per second (shift x4, alt x0.25, the wheel scales it).</summary>
	[ConVar( "dev_cam_speed", ConVarFlags.Saved )] public static float CamSpeed { get; set; } = 700f;
	/// <summary>The free camera's lens; 0 keeps the player's own field of view.</summary>
	[ConVar( "dev_cam_fov", ConVarFlags.Saved )] public static float CamFov { get; set; } = 0f;
	/// <summary>Look sensitivity of the free camera relative to the player's.</summary>
	[ConVar( "dev_cam_look", ConVarFlags.Saved )] public static float CamLook { get; set; } = 1f;
	/// <summary>Camera roll allowed on the free camera (the user's rule for gameplay is no roll; a director may want a dutch angle).</summary>
	[ConVar( "dev_cam_roll" )] public static bool CamRollAllowed { get; set; } = false;
	/// <summary>Scales every gait of the player (a slow walk for a shot, a dash to a spot).</summary>
	[ConVar( "dev_move_scale" )] public static float MoveScale { get; set; } = 1f;
	/// <summary>No camera shake, no hit ripples, no aberration pulses: a clean plate.</summary>
	[ConVar( "dev_clean_fx" )] public static bool CleanFx { get; set; } = false;

	public static DevDirector Current { get; private set; }

	// ------------------------------------------------------------------ time

	/// <summary>The game clock stands still (Scene.TimeScale = PauseScale). The free camera and the panel run on real time.</summary>
	public static bool Paused { get; private set; }
	/// <summary>The time scale while not paused (slow motion below 1, faster above).</summary>
	public static float Speed { get; private set; } = 1f;
	/// <summary>The speed presets the mouse buttons step through.</summary>
	public static readonly float[] SpeedSteps = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f };
	private static int _stepFrames;
	private static float _stepSeconds;

	public static void TogglePause() => SetPaused( !Paused );

	public static void SetPaused( bool paused )
	{
		Paused = paused;
		Log.Info( paused ? "[Director] paused" : "[Director] running" );
	}

	public static void SetSpeed( float speed )
	{
		Speed = Math.Clamp( speed, 0.01f, 4f );
		Paused = false;
		Log.Info( $"[Director] time x{Speed:0.##}" );
	}

	/// <summary>The next preset up or down from the current speed.</summary>
	public static void StepSpeed( int direction )
	{
		var i = 0;
		var best = float.MaxValue;
		for ( var k = 0; k < SpeedSteps.Length; k++ )
		{
			var d = MathF.Abs( SpeedSteps[k] - Speed );
			if ( d < best ) { best = d; i = k; }
		}
		SetSpeed( SpeedSteps[Math.Clamp( i + direction, 0, SpeedSteps.Length - 1 )] );
	}

	/// <summary>While paused: advance the game by exactly this much (one frame at 60 by default) and stand still again.</summary>
	public static void StepFrame( float seconds = 1f / 60f )
	{
		if ( !Paused ) SetPaused( true );
		_stepFrames = 1;
		_stepSeconds = MathF.Max( 0.001f, seconds );
	}

	private void ApplyTime()
	{
		var scene = Scene;
		if ( !scene.IsValid() ) return;
		float scale;
		if ( _stepFrames > 0 )
		{
			// one real frame carries exactly the step: the scale is the step over this frame's real length
			_stepFrames--;
			scale = _stepSeconds / MathF.Max( 0.0001f, RealTime.Delta );
		}
		else scale = Paused ? MathF.Max( 0f, PauseScale ) : Speed;
		if ( MathF.Abs( scene.TimeScale - scale ) > 0.0001f ) scene.TimeScale = scale;
	}

	// ------------------------------------------------------------------ the free camera

	public static bool FreeCam { get; private set; }
	private Vector3 _camPos;
	private Angles _camAngles;
	private bool _wasThirdPerson;
	private bool _hadInput;
	/// <summary>The player's look when the free camera took over: held there every frame, so the mouse turns the camera and not the mage.</summary>
	private Angles _heldEye;

	private static PlayerController Controller
	{
		get
		{
			var health = PlayerHealth.Local;
			return health.IsValid() ? health.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
		}
	}

	public static void ToggleFreeCam() => SetFreeCam( !FreeCam );

	public static void SetFreeCam( bool on )
	{
		var d = Current;
		if ( !d.IsValid() || on == FreeCam ) return;
		var camera = d.Scene.Camera;
		var c = Controller;
		if ( on )
		{
			if ( !camera.IsValid() ) return;
			d._camPos = camera.WorldPosition;
			var a = camera.WorldRotation.Angles();
			d._camAngles = new Angles( a.pitch, a.yaw, CamRollAllowed ? a.roll : 0f );
			if ( c.IsValid() )
			{
				d._wasThirdPerson = c.ThirdPerson;
				d._hadInput = c.UseInputControls;
				d._heldEye = c.EyeAngles;
				// the controller stops writing the view (it is order 0 in the camera chain) and stops walking; the
				// body shows (third person) so the mage can be filmed from outside
				c.UseCameraControls = false;
				c.UseInputControls = false;
				c.WishVelocity = Vector3.Zero;
				if ( c.Body.IsValid() ) c.Body.Velocity = Vector3.Zero;
				c.ThirdPerson = true;
			}
			FreeCam = true;
			Log.Info( "[Director] free camera (F again to return; Bring player here in the panel)" );
		}
		else
		{
			FreeCam = false;
			if ( c.IsValid() )
			{
				c.UseCameraControls = true;
				c.ThirdPerson = d._wasThirdPerson;
				var health = PlayerHealth.Local;
				var dead = health.IsValid() && health.IsDead;
				c.UseInputControls = d._hadInput && !Run.RunUi.Blocking && !dead;
			}
			Log.Info( "[Director] player camera" );
		}
	}

	/// <summary>The free camera's pose right now (position, angles), for the paths and the readouts.</summary>
	public static (Vector3 pos, Angles angles) CamPose => Current.IsValid() ? (Current._camPos, Current._camAngles) : (Vector3.Zero, Angles.Zero);

	public static void SetCamPose( Vector3 pos, Angles angles )
	{
		if ( !Current.IsValid() ) return;
		Current._camPos = pos;
		Current._camAngles = angles;
	}

	/// <summary>The player's feet under the free camera (the floor below it, or the eye height down), looking the camera's way; the camera stays free.</summary>
	public static void BringPlayerHere()
	{
		var d = Current;
		if ( !d.IsValid() ) return;
		var eye = FreeCam ? d._camPos : (d.Scene.Camera.IsValid() ? d.Scene.Camera.WorldPosition : Vector3.Zero);
		var yaw = FreeCam ? d._camAngles.yaw : (d.Scene.Camera.IsValid() ? d.Scene.Camera.WorldRotation.Yaw() : 0f);
		var feet = eye - Vector3.Up * 64f;
		var tr = d.Scene.Trace.Ray( eye, eye - Vector3.Up * 600f ).WithoutTags( "player", "viewmodel", "posesource", "trigger", "npc", "enemy", "ally", "debris", "ragdoll", "spellfire" ).Run();
		if ( tr.Hit ) feet = tr.HitPosition + Vector3.Up * 2f;
		PlayerHealth.TeleportLocal( feet, new Angles( 0f, yaw, 0f ) );
		d._heldEye = new Angles( 0f, yaw, 0f );
		Log.Info( $"[Director] player brought to {feet:0}" );
	}

	/// <summary>The free camera onto the player's eyes, looking their way.</summary>
	public static void SnapCamToPlayer()
	{
		var d = Current;
		var c = Controller;
		if ( !d.IsValid() || !c.IsValid() ) return;
		d._camPos = c.EyePosition;
		d._camAngles = new Angles( d._heldEye.pitch, d._heldEye.yaw, 0f );
	}

	private void TickFreeCam()
	{
		if ( !FreeCam ) return;
		HoldPlayerLook();
		var dt = RealTime.Delta;
		var rot = _camAngles.ToRotation();
		// look only with the cursor locked (Q toggles it while the panel is up)
		if ( !MouseLock.Release )
		{
			var look = Input.AnalogLook * CamLook;
			_camAngles.pitch = Math.Clamp( _camAngles.pitch + look.pitch, -89f, 89f );
			_camAngles.yaw += look.yaw;
			if ( CamRollAllowed && Input.Down( "Walk" ) ) _camAngles.roll += Input.MouseWheel.y * 2f;
		}
		var wheel = Input.MouseWheel.y;
		if ( wheel != 0f && !(CamRollAllowed && Input.Down( "Walk" )) ) CamSpeed = Math.Clamp( CamSpeed * (1f + wheel * 0.12f), 20f, 20000f );
		var move = Input.AnalogMove;
		var wish = rot.Forward * move.x + rot.Left * move.y;
		if ( Input.Down( "Jump" ) ) wish += Vector3.Up;
		if ( Input.Down( "Duck" ) ) wish -= Vector3.Up;
		var speed = CamSpeed * (Input.Down( "Run" ) ? 4f : 1f) * (Input.Down( "Walk" ) ? 0.25f : 1f);
		_camPos += wish * speed * dt;
		// a path, an orbit or a follow takes over the pose (the hand's input above is then only a nudge that is overwritten)
		CameraPath.Tick( dt );
	}

	/// <summary>The controller still reads the mouse into its eye angles with its camera controls off: put the look it had back, every frame, in the update and again after the camera.</summary>
	private void HoldPlayerLook()
	{
		var c = Controller;
		if ( c.IsValid() && c.EyeAngles != _heldEye ) c.EyeAngles = _heldEye;
	}

	int ICameraModifier.CameraOrder => 6000;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !FreeCam || camera != Scene.Camera ) return;
		HoldPlayerLook();
		view.Position = _camPos;
		view.Rotation = _camAngles.ToRotation();
		if ( CamFov > 1f ) view.FieldOfView = CamFov;
		view.ZNear = 2f;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }

	// ------------------------------------------------------------------ the hud

	public static bool HudHidden { get; private set; }

	/// <summary>Every screen panel but the director's own goes clear (their state stays: a menu still opens, unseen).</summary>
	public static void SetHud( bool hidden )
	{
		HudHidden = hidden;
		Current?.ApplyHud();
		Log.Info( hidden ? "[Director] hud hidden" : "[Director] hud shown" );
	}

	private void ApplyHud()
	{
		foreach ( var p in Scene.GetAllComponents<ScreenPanel>() )
		{
			if ( !p.IsValid() || p.GameObject == GameObject ) continue;
			p.Opacity = HudHidden ? 0f : 1f;
		}
	}

	// ------------------------------------------------------------------ the object

	/// <summary>The panel's own screen layer: above the run's cards (500) and the boon screen (510), untouched by the hud switch.</summary>
	public const int PanelZ = 900;

	/// <summary>One director in the scene, on its own object with its own ScreenPanel and the DevPanel.</summary>
	public static DevDirector Ensure( Scene scene )
	{
		if ( !Available ) return null;
		if ( Current.IsValid() && Current.Scene == scene ) return Current;
		var go = new GameObject( true, "DevDirector" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		go.Components.Create<ScreenPanel>().ZIndex = PanelZ;
		var d = go.Components.Create<DevDirector>();
		go.Components.Create<DevPanel>();
		return d;
	}

	protected override void OnAwake()
	{
		Current = this;
	}

	protected override void OnDestroy()
	{
		if ( Current != this ) return;
		Current = null;
		FreeCam = false;
		Paused = false;
		Speed = 1f;
		_stepFrames = 0;
		HudHidden = false;
		if ( Scene.IsValid() ) Scene.TimeScale = 1f;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying || Scene.IsEditor || !Available ) return;
		ApplyTime();
		if ( Armed ) TickKeys();
		TickFreeCam();
		// a panel made after the switch (the player spawning) comes up clear too
		if ( HudHidden && Time.Now > _nextHudSweep ) { _nextHudSweep = Time.Now + 0.5f; ApplyHud(); }
	}

	private float _nextHudSweep;

	private void TickKeys()
	{
		// the Tab menu has its own keys and a text field one day; the director's keys stay out of it
		if ( SpellLoadout.IsOpen ) return;
		if ( Input.Pressed( "Slot0" ) ) DevPanel.Toggle();
		if ( Input.Pressed( "Flashlight" ) ) ToggleFreeCam();
		if ( Input.Pressed( "Drop" ) ) SetHud( !HudHidden );
		if ( WorldTape.Replaying )
		{
			// on the tape: R plays or holds it, the mouse buttons scrub it
			if ( Input.Pressed( "Reload" ) ) { if ( WorldTape.Playing ) WorldTape.Pause(); else WorldTape.Play( WorldTape.PlaySpeed == 0f ? 1f : MathF.Abs( WorldTape.PlaySpeed ) ); }
			if ( Input.Pressed( "SlotNext" ) ) WorldTape.Step( 0.5f );
			if ( Input.Pressed( "SlotPrev" ) ) WorldTape.Step( -0.5f );
		}
		else
		{
			if ( Input.Pressed( "Reload" ) ) TogglePause();
			if ( Input.Pressed( "SlotNext" ) ) { if ( Paused ) StepFrame(); else StepSpeed( 1 ); }
			if ( Input.Pressed( "SlotPrev" ) ) StepSpeed( -1 );
		}
		// a camera path saved to a number key: that key flies it
		for ( var k = CameraPath.FirstKey; k <= CameraPath.LastKey; k++ )
			if ( Input.Pressed( $"Slot{k}" ) && CameraPath.KeyUsed( k ) ) { CameraPath.LoadKey( k ); CameraPath.Play(); }
		if ( DevPanel.IsOpen && Input.Pressed( "Menu" ) ) MouseLock.Release = !MouseLock.Release;
	}
}

/// <summary>On play, in any scene (the run's, the boss test): the director exists. Editor sessions only.</summary>
public sealed class DevDirectorSystem : GameObjectSystem
{
	public DevDirectorSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, -5, Tick, "DevDirector" );
	}

	private void Tick()
	{
		if ( !Game.IsPlaying || Scene.IsEditor || !DevDirector.Available ) return;
		if ( !DevDirector.Current.IsValid() || DevDirector.Current.Scene != Scene ) DevDirector.Ensure( Scene );
	}
}
