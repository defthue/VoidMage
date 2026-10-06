using System;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Drives heden's human_base animgraph from the engine PlayerController: locomotion blend, ground
/// state, crouch, jump, and the body turn. Every parameter the graph declares is written with a safe
/// idle value so stances the game doesn't use yet (sword, bow, tools) stay dormant.
/// The combat stances (staff casting, beam channel) come later with the spell system.
///
/// It also owns the movement speeds, heden's scheme: W runs, Shift sprints while the input is mostly
/// forward, Alt walks, Ctrl crouches. The engine controller only knows walk/run and a toggle button, so
/// the two speeds it reads are rewritten every frame from these four.
/// The body turn is applied to the visual child, never to the physics root: rotating the rigidbody's
/// object from Update fights the fixed-step interpolation and reads as micro-stutter.
/// </summary>
[Title( "Character Locomotion Animator" ), Category( "Void Mage" ), Icon( "directions_walk" )]
public sealed class CharacterLocomotionAnimator : Component, PlayerController.IEvents
{
	[Property] public SkinnedModelRenderer Renderer { get; set; }
	// Animation-true (measured 2026-09-06): the body clips' stance-foot speed is Walk_F ~64, Run_F ~115, Sprint_F ~300
	// u/s and the first-person arm clips bob at the same cadence (run 2.86, sprint 3.33 steps/s). Faster than this and
	// the feet slide / the world outruns the arms. heden shipped 80 / 160 / 300.
	[Property, Group( "Speeds" )] public float WalkSpeed { get; set; } = 60f;
	[Property, Group( "Speeds" )] public float RunSpeed { get; set; } = 120f;
	[Property, Group( "Speeds" )] public float SprintSpeed { get; set; } = 240f;
	[Property, Group( "Speeds" )] public float CrouchSpeed { get; set; } = 45f;
	/// <summary>Sprint only while this much of the move input points forward (heden's SprintForwardDot).</summary>
	[Property, Group( "Speeds" )] public float SprintForwardDot { get; set; } = 0.4f;
	/// <summary>
	/// Shift while backing up or strafing gives this fraction of the run-to-sprint gap on top of RunSpeed
	/// (0 = plain run, 1 = full sprint). Half by default: quicker than a jog, never a full backwards sprint.
	/// </summary>
	[Property, Group( "Speeds" ), Range( 0f, 1f )] public float SideSprintFraction { get; set; } = 0.5f;
	/// <summary>The Shift-while-not-forward speed, derived from SideSprintFraction.</summary>
	public float SideSprintSpeed => RunSpeed + (SprintSpeed - RunSpeed) * SideSprintFraction;
	/// <summary>
	/// Actual speed above which the arms and footsteps treat the player as sprinting: midway between the
	/// side sprint and the full sprint, so a side sprint still reads as running.
	/// </summary>
	public float SprintThreshold => (SideSprintSpeed + SprintSpeed) * 0.5f;
	[Property] public float BlendDamping { get; set; } = 12f;
	[Property] public float TurnDamping { get; set; } = 12f;
	/// <summary>Standing still, the body only turns once the look is this many degrees off its facing.</summary>
	[Property] public float IdleTurnThreshold { get; set; } = 30f;

	private bool _idleCatchingUp;
	private VoidMage.Spells.SpellCaster _caster;

	/// <summary>Speed the owner is asking for this frame (walk, run or sprint), for the first-person arms.</summary>
	public float TargetSpeed { get; private set; }
	public bool IsSprinting { get; private set; }

	/// <summary>Staff carried in hand: switches the graph to its staff idle/gait branch.</summary>
	[Property] public bool StaffArmed { get; set; } = false;

	/// <summary>Ground jumps, so proxies can fire the jump wind-up without seeing the key press.</summary>
	[Sync] public int JumpCount { get; set; }

	// armed_amount and friends feed 1D blends; at an exact 0 or 1 the engine frees the waning
	// child's pose and then still touches it (invalid pose handle), so blend floats never sit
	// on an endpoint.
	public const float BlendFloor = 0.005f;

	private SkinnedModelRenderer _constantsFor;

	/// <summary>The graph parameters Void Mage never animates: no swimming/climbing, no slopes, and of the weapon stances only the staff exists (everything sword/bow/tool idles).</summary>
	private static void SetConstants( SkinnedModelRenderer r )
	{
		r.Set( "swimming", false );
		r.Set( "climbing", false );
		r.Set( "climb_move", 0f );
		r.Set( "swim_move", 0f );
		r.Set( "slope_up", BlendFloor );
		r.Set( "slope_down", BlendFloor );
		r.Set( "armed_amount", BlendFloor );
		r.Set( "action_weight", BlendFloor );
		r.Set( "override_weight", BlendFloor );
		r.Set( "bow_carry", BlendFloor );
		r.Set( "armed", false );
		r.Set( "blocking", false );
		r.Set( "dead", false );
		r.Set( "bow_armed", false );
		r.Set( "bow_drawing", false );
		r.Set( "gathering", false );
	}
	public const float BlendCeiling = 0.995f;

	private PlayerController _controller;
	private float _moveX, _moveY, _moveSpeed, _staffHold;
	private bool _jumpPulse;
	private int _lastJumpCount;
	private bool _jumpCountSeeded;
	private Vector3 _lastPosition;

	void PlayerController.IEvents.OnJumped()
	{
		if ( IsProxy ) return;
		_jumpPulse = true;
		JumpCount++;
	}

	protected override void OnStart()
	{
		_lastPosition = WorldPosition;
	}

	protected override void OnUpdate()
	{
		if ( !_controller.IsValid() )
			_controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );

		if ( !Renderer.IsValid() )
			Renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );

		if ( !Renderer.IsValid() ) return;

		var velocity = GetHorizontalVelocity();
		_lastPosition = WorldPosition;

		// dead: the controller's input is off (PlayerHealth), the speeds stay where they were
		if ( !IsProxy && !(PlayerHealth.Local.IsValid() && PlayerHealth.Local.IsDead) ) DriveSpeeds();

		// The body faces where the eyes look (EyeAngles is synced, so proxies turn the same way);
		// strafes come out of the compass blend below. The visual turns, the physics root never does.
		// Standing still it holds its facing until the look is IdleTurnThreshold away (heden's idle turn), so
		// the HeadLookController leads instead of the whole body swivelling with every mouse nudge.
		var body = Renderer.GameObject;
		if ( _controller.IsValid() )
		{
			var eyeYaw = _controller.EyeAngles.yaw;
			if ( !_caster.IsValid() )
				_caster = Components.Get<VoidMage.Spells.SpellCaster>( FindMode.EverythingInSelfAndDescendants );
			var tracking = velocity.Length > 10f || (_caster.IsValid() && _caster.IsCharging);

			if ( tracking )
			{
				_idleCatchingUp = false;
			}
			else
			{
				var offset = MathF.Abs( MathX.DeltaDegrees( eyeYaw, body.WorldRotation.Angles().yaw ) );
				if ( offset > IdleTurnThreshold ) _idleCatchingUp = true;
				else if ( offset < 3f ) _idleCatchingUp = false;
			}

			if ( tracking || _idleCatchingUp )
			{
				var turn = 1f - MathF.Exp( -TurnDamping * Time.Delta );
				body.WorldRotation = Rotation.Slerp( body.WorldRotation, Rotation.FromYaw( eyeYaw ), turn );
			}
		}

		var rotation = body.WorldRotation;
		var forward = rotation.Forward.Dot( velocity );
		var right = rotation.Right.Dot( velocity );
		var speed = velocity.Length;

		var wishDir = !IsProxy && _controller.IsValid() ? _controller.WishVelocity.WithZ( 0f ) : Vector3.Zero;
		if ( !wishDir.IsNearlyZero() ) wishDir = wishDir.Normal;
		var blocked = !wishDir.IsNearlyZero() && speed < WalkSpeed * 0.5f;

		float targetX, targetY;
		if ( blocked )
		{
			targetX = rotation.Right.Dot( wishDir ).Clamp( -1f, 1f );
			targetY = rotation.Forward.Dot( wishDir ).Clamp( -1f, 1f );
		}
		else if ( speed > 0.01f )
		{
			targetX = (right / speed).Clamp( -1f, 1f );
			targetY = (forward / speed).Clamp( -1f, 1f );
		}
		else
		{
			targetX = 0f; targetY = 0f;
		}

		var crouching = _controller.IsValid() && _controller.IsDucking;
		var targetSpeed = MapMoveSpeed( speed );
		if ( crouching ) targetSpeed = MathF.Min( targetSpeed, 0.5f );

		var forwardDominant = targetY > 0f && targetY > MathF.Abs( targetX ) * 1.5f;
		if ( !forwardDominant ) targetSpeed = MathF.Min( targetSpeed, 1f );

		var blend = 1f - MathF.Exp( -BlendDamping * Time.Delta );
		_moveX = _moveX.LerpTo( targetX, blend );
		_moveY = _moveY.LerpTo( targetY, blend );
		_moveSpeed = _moveSpeed.LerpTo( targetSpeed, blend );
		_staffHold = _staffHold.LerpTo( StaffArmed ? 1f : 0f, blend );

		var r = Renderer;
		r.Set( "move_x", Sane( _moveX ).Clamp( -1f, 1f ) );
		r.Set( "move_y", Sane( _moveY ).Clamp( -1f, 1f ) );
		r.Set( "move_speed", Sane( _moveSpeed ).Clamp( 0f, 1.5f ) );
		r.Set( "grounded", !_controller.IsValid() || _controller.IsOnGround );
		r.Set( "jump_trigger", ConsumeJump() );
		r.Set( "crouching", crouching );
		// the parameters that never change are written once per renderer, not every frame
		if ( r != _constantsFor ) { SetConstants( r ); _constantsFor = r; }
		r.Set( "staff_armed", StaffArmed );
		r.Set( "staff_hold", _staffHold.Clamp( BlendFloor, BlendCeiling ) );
	}

	/// <summary>
	/// heden's speed scheme on the engine controller. With RunByDefault the controller uses RunSpeed
	/// unless its AltMoveButton ("run", Shift) is held, then WalkSpeed; so RunSpeed carries run/walk and
	/// WalkSpeed carries the sprint.
	/// </summary>
	private void DriveSpeeds()
	{
		if ( !_controller.IsValid() ) return;

		var wantsWalk = Input.Down( "walk" );
		var move = Input.AnalogMove;
		var forwardDominant = move.Length > 0.1f && move.Normal.x >= SprintForwardDot;
		var shift = Input.Down( "run" );

		// the run's boons: Move Speed scales every gait (Juggernaut's cost slows them), Sprint Speed adds to the sprints
		var boons = VoidMage.Boons.PlayerBoons.Find( _controller.GameObject );
		var moveScale = (boons?.Scale( VoidMage.Boons.BoonStat.MoveSpeed, 0.3f ) ?? 1f) * MathF.Max( 0.05f, VoidMage.Dev.DevDirector.MoveScale );
		var sprintScale = MathF.Max( 0.3f, moveScale + (boons?.Get( VoidMage.Boons.BoonStat.SprintSpeed ) ?? 0f) );

		_controller.RunByDefault = true;
		_controller.AltMoveButton = "run";
		_controller.RunSpeed = (wantsWalk ? WalkSpeed : RunSpeed) * moveScale;
		// Shift: full sprint when the input is mostly forward, the side sprint (half the gap) otherwise.
		_controller.WalkSpeed = wantsWalk ? WalkSpeed * moveScale : (forwardDominant ? SprintSpeed : SideSprintSpeed) * sprintScale;
		_controller.DuckedSpeed = CrouchSpeed * moveScale;

		// drinking a potion: every gait capped at crouch speed, sprint included (user)
		if ( PotionDrinker.IsDrinking )
		{
			var cap = CrouchSpeed * moveScale;
			_controller.RunSpeed = MathF.Min( _controller.RunSpeed, cap );
			_controller.WalkSpeed = MathF.Min( _controller.WalkSpeed, cap );
			IsSprinting = false;
			TargetSpeed = MathF.Min( _controller.IsDucking ? CrouchSpeed : RunSpeed, CrouchSpeed );
			return;
		}

		var shifting = shift && !wantsWalk && !_controller.IsDucking;
		IsSprinting = shifting && forwardDominant;
		TargetSpeed = _controller.IsDucking ? CrouchSpeed
			: wantsWalk ? WalkSpeed
			: IsSprinting ? SprintSpeed
			: shifting ? SideSprintSpeed
			: RunSpeed;
	}

	private bool ConsumeJump()
	{
		if ( !IsProxy )
		{
			var pulse = _jumpPulse;
			_jumpPulse = false;
			return pulse;
		}

		// A late joiner sees whatever the count already is; that is history, not a jump.
		if ( !_jumpCountSeeded )
		{
			_jumpCountSeeded = true;
			_lastJumpCount = JumpCount;
			return false;
		}
		if ( JumpCount == _lastJumpCount ) return false;
		_lastJumpCount = JumpCount;
		return true;
	}

	private Vector3 GetHorizontalVelocity()
	{
		if ( !IsProxy && _controller.IsValid() ) return _controller.Velocity.WithZ( 0f );
		if ( Time.Delta <= 0f ) return Vector3.Zero;
		return ((WorldPosition - _lastPosition) / Time.Delta).WithZ( 0f );
	}

	private float MapMoveSpeed( float speed )
	{
		if ( speed <= 0f ) return 0f;
		if ( speed <= WalkSpeed ) return Remap( speed, 0f, WalkSpeed, 0f, 0.5f );
		if ( speed <= RunSpeed ) return Remap( speed, WalkSpeed, RunSpeed, 0.5f, 1f );
		return Remap( speed, RunSpeed, SprintSpeed, 1f, 1.5f );
	}

	private static float Remap( float value, float fromMin, float fromMax, float toMin, float toMax )
	{
		if ( fromMax - fromMin <= 0.001f ) return toMax;
		var frac = ((value - fromMin) / (fromMax - fromMin)).Clamp( 0f, 1f );
		return toMin + (toMax - toMin) * frac;
	}

	private static float Sane( float v ) => float.IsNaN( v ) || float.IsInfinity( v ) ? 0f : v;
}
