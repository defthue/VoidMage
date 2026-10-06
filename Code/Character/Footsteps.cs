using System;
using Sandbox;

namespace VoidMage.Character;

public enum FootstepSurfaceType { Stone, Wood, Metal, Dirt, Grass, Glass }

/// <summary>
/// heden's footstep tuning: steps come from travelled distance, not animation events (the clips carry
/// no footstep tags). Stride follows speed between the walk and sprint anchors.
/// </summary>
public static class FootstepTuning
{
	public const float MinStepSpeed = 20f;
	public const float MinStepInterval = 0.18f;
	public const float StrideSpeedLow = 110f;
	public const float StrideSpeedHigh = 450f;
	public const float StrideAtWalk = 55f;
	public const float StrideAtSprint = 130f;
	public const float StrideFloor = 45f;
	public const float LandResetFraction = 0.5f;
	public const float PitchJitter = 0.06f;
	public const float LandMinFallSpeed = 250f;
	public const float LandMaxFallSpeed = 900f;
	public const float LandMinVolume = 0.5f;
	public const float LandMaxVolume = 1.3f;
	public const float MinAirTimeForLand = 0.15f;
	public const float TraceUp = 16f;
	public const float TraceDown = 24f;

	public static float StrideFor( float speed )
	{
		var stride = speed.Remap( StrideSpeedLow, StrideSpeedHigh, StrideAtWalk, StrideAtSprint );
		return stride.Clamp( StrideFloor, StrideAtSprint );
	}
}

/// <summary>
/// What the feet stand on and which footstep event that means. The sounds are the base game's citizen
/// set: s&box mounts core/ as a root asset path, so "sounds/footsteps/*.sound" resolves from any project
/// with nothing copied. The dungeon has no surface assets, so names decide: stone by default.
/// </summary>
public static class FootstepSurfaceResolver
{
	private static readonly (string Keyword, FootstepSurfaceType Surface)[] NameKeywords =
	{
		( "wood", FootstepSurfaceType.Wood ), ( "plank", FootstepSurfaceType.Wood ), ( "bridge", FootstepSurfaceType.Wood ),
		( "crate", FootstepSurfaceType.Wood ), ( "barrel", FootstepSurfaceType.Wood ), ( "table", FootstepSurfaceType.Wood ),
		( "metal", FootstepSurfaceType.Metal ), ( "iron", FootstepSurfaceType.Metal ), ( "grate", FootstepSurfaceType.Metal ),
		( "cage", FootstepSurfaceType.Metal ),
		( "glass", FootstepSurfaceType.Glass ),
		( "dirt", FootstepSurfaceType.Dirt ), ( "sand", FootstepSurfaceType.Dirt ), ( "mud", FootstepSurfaceType.Dirt ),
		( "grass", FootstepSurfaceType.Grass ),
	};

	public static FootstepSurfaceType Resolve( Scene scene, Vector3 position )
	{
		if ( !scene.IsValid() ) return FootstepSurfaceType.Stone;

		var tr = scene.Trace
			.Ray( position + Vector3.Up * FootstepTuning.TraceUp, position + Vector3.Down * FootstepTuning.TraceDown )
			.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger" )
			.Run();
		if ( !tr.Hit || !tr.GameObject.IsValid() ) return FootstepSurfaceType.Stone;

		var surfaceName = tr.Surface?.ResourceName;
		if ( !string.IsNullOrEmpty( surfaceName ) && surfaceName != "default" && surfaceName != "generic" && TryMatch( surfaceName.ToLowerInvariant(), out var fromSurface ) )
			return fromSurface;

		if ( TryMatch( tr.GameObject.Name?.ToLowerInvariant() ?? "", out var fromName ) )
			return fromName;

		var model = tr.GameObject.Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants )?.Model;
		if ( TryMatch( model?.ResourceName?.ToLowerInvariant() ?? "", out var fromModel ) )
			return fromModel;

		return FootstepSurfaceType.Stone;
	}

	private static bool TryMatch( string haystack, out FootstepSurfaceType surface )
	{
		foreach ( var (keyword, mapped) in NameKeywords )
		{
			if ( haystack.Contains( keyword, StringComparison.Ordinal ) ) { surface = mapped; return true; }
		}
		surface = FootstepSurfaceType.Stone;
		return false;
	}

	public static string StepSound( FootstepSurfaceType surface ) => surface switch
	{
		FootstepSurfaceType.Grass => "sounds/footsteps/footstep-grass.sound",
		FootstepSurfaceType.Dirt => "sounds/footsteps/footstep-dirt.sound",
		FootstepSurfaceType.Wood => "sounds/footsteps/footstep-wood.sound",
		FootstepSurfaceType.Metal => "sounds/footsteps/footstep-metal.sound",
		FootstepSurfaceType.Glass => "sounds/footsteps/footstep-glass.sound",
		_ => "sounds/footsteps/footstep-concrete.sound",
	};

	/// <summary>Only concrete and wood ship land variants.</summary>
	public static string LandSound( FootstepSurfaceType surface ) => surface == FootstepSurfaceType.Wood
		? "sounds/footsteps/footstep-wood-land.sound"
		: "sounds/footsteps/footstep-concrete-land.sound";

	/// <summary>The one play path: at the feet, the event's volume scaled, the pitch jittered so repeats never loop.</summary>
	public static SoundHandle Play( string eventPath, Vector3 position, float volume, float pitchScale = 1f )
	{
		var handle = VoidMage.Audio.GameAudio.Play( eventPath, position );
		if ( handle is null ) return null;
		handle.Volume *= volume;
		handle.Pitch *= pitchScale + Game.Random.Float( -FootstepTuning.PitchJitter, FootstepTuning.PitchJitter );
		return handle;
	}
}

/// <summary>
/// Footsteps and landing thuds for a player, heden's distance accumulator on the engine controller.
/// Runs on every peer from what it can see (the owner's velocity, a proxy's position delta), so
/// nothing is networked for it. The owner's camera bobs on the same stride: one dip per step, lowest the
/// moment the step sounds.
/// </summary>
[Title( "Footsteps" ), Category( "Void Mage" ), Icon( "directions_walk" )]
public sealed class Footsteps : Component, ICameraModifier
{
	[Property] public float CrouchVolume { get; set; } = 0.35f;
	[Property] public float WalkVolume { get; set; } = 0.7f;
	[Property] public float RunVolume { get; set; } = 1.0f;
	[Property] public float SprintVolume { get; set; } = 1.15f;

	/// <summary>Camera dip per step at run speed, units; walking dips less, sprinting a bit more. 0 turns the bob off.</summary>
	[ConVar( "cam_bob" )] public static float BobUnits { get; set; } = 0.9f;
	/// <summary>How fast the bob fades in when moving and out when stopping or leaving the ground.</summary>
	private const float BobFade = 8f;

	public int CameraOrder => 940;

	private float _bobWeight;

	private PlayerController _controller;
	private CharacterLocomotionAnimator _locomotion;
	private Vector3 _lastPosition;
	private bool _hasLast;
	private float _accumulator;
	private float _lastStep;
	private bool _wasGrounded = true;
	private float _leftGroundAt;
	private float _peakFall;

	protected override void OnUpdate()
	{
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_locomotion.IsValid() ) _locomotion = Components.Get<CharacterLocomotionAnimator>( FindMode.EverythingInSelfAndAncestors );

		var velocity = Velocity();
		var grounded = !_controller.IsValid() || _controller.IsOnGround;
		var speed = velocity.WithZ( 0f ).Length;

		if ( !IsProxy )
		{
			var bobTarget = grounded && speed >= FootstepTuning.MinStepSpeed ? BobScale( speed ) : 0f;
			_bobWeight = _bobWeight.LerpTo( bobTarget, 1f - MathF.Exp( -BobFade * Time.Delta ) );
		}

		// Landing: the thud, then the stride restarts halfway so it is not chased by a step.
		if ( !grounded )
		{
			if ( _wasGrounded ) { _wasGrounded = false; _leftGroundAt = Time.Now; _peakFall = 0f; }
			_peakFall = MathF.Max( _peakFall, -velocity.z );
			return;
		}
		if ( !_wasGrounded )
		{
			_wasGrounded = true;
			if ( Time.Now - _leftGroundAt >= FootstepTuning.MinAirTimeForLand )
			{
				var volume = _peakFall.Remap( FootstepTuning.LandMinFallSpeed, FootstepTuning.LandMaxFallSpeed, FootstepTuning.LandMinVolume, FootstepTuning.LandMaxVolume );
				var landSurface = FootstepSurfaceResolver.Resolve( Scene, WorldPosition );
				FootstepSurfaceResolver.Play( FootstepSurfaceResolver.LandSound( landSurface ), WorldPosition, volume );
				_accumulator = FootstepTuning.StrideFor( speed ) * FootstepTuning.LandResetFraction;
				_lastStep = Time.Now;
			}
		}

		if ( speed < FootstepTuning.MinStepSpeed ) return;

		_accumulator += speed * Time.Delta;
		var stride = FootstepTuning.StrideFor( speed );
		if ( _accumulator < stride ) return;
		if ( Time.Now - _lastStep < FootstepTuning.MinStepInterval ) return;

		_accumulator -= stride;
		_lastStep = Time.Now;

		var surface = FootstepSurfaceResolver.Resolve( Scene, WorldPosition );
		FootstepSurfaceResolver.Play( FootstepSurfaceResolver.StepSound( surface ), WorldPosition, StepVolume( speed ) );
	}

	private Vector3 Velocity()
	{
		if ( !IsProxy && _controller.IsValid() )
		{
			_lastPosition = WorldPosition; _hasLast = true;
			return _controller.Velocity;
		}
		var v = _hasLast && Time.Delta > 0f ? (WorldPosition - _lastPosition) / Time.Delta : Vector3.Zero;
		_lastPosition = WorldPosition; _hasLast = true;
		return v;
	}

	/// <summary>Bob size relative to a run: half at a walk, a quarter more at a sprint, small while crouched.</summary>
	private float BobScale( float speed )
	{
		if ( _controller.IsValid() && _controller.IsDucking ) return 0.5f;
		if ( !_locomotion.IsValid() ) return 1f;
		if ( speed <= _locomotion.RunSpeed ) return speed.Remap( _locomotion.WalkSpeed, _locomotion.RunSpeed, 0.5f, 1f );
		return speed.Remap( _locomotion.RunSpeed, _locomotion.SprintSpeed, 1f, 1.25f );
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera || BobUnits <= 0f || _bobWeight < 0.001f ) return;
		if ( _controller.IsValid() && _controller.ThirdPerson ) return;
		if ( PlayerHealth.Local.IsValid() && PlayerHealth.Local.IsDead ) return;   // the death camera owns the view

		// the stride's progress to the next step: 0 is the footfall (lowest), halfway is the top of the stride
		var stride = FootstepTuning.StrideFor( _controller.IsValid() ? _controller.Velocity.WithZ( 0f ).Length : 0f );
		var phase = MathF.Min( _accumulator / MathF.Max( stride, 1f ), 1f );
		var dip = 0.5f + 0.5f * MathF.Cos( phase * MathF.PI * 2f );
		view.Position += Vector3.Down * dip * BobUnits * _bobWeight;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }

	private float StepVolume( float speed )
	{
		if ( _controller.IsValid() && _controller.IsDucking ) return CrouchVolume;
		if ( !_locomotion.IsValid() ) return RunVolume;
		if ( speed > _locomotion.SprintThreshold ) return SprintVolume;
		if ( speed > (_locomotion.WalkSpeed + _locomotion.RunSpeed) * 0.5f ) return RunVolume;
		return WalkVolume;
	}
}
