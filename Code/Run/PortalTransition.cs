using System;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Run;

/// <summary>
/// The camera side of going through a portal. The pull: the eye drifts into the portal's heart and sinks as the floor
/// gives way, the field of view stretches wide, the void's pulses wash over the screen and the retro filter's
/// aberration smears the edges while the screen irises to void (RunUi). The arrival is the same motion let go: it
/// starts wide and smeared and settles back. Over both rides the portal's own vortex as a screen pass
/// (Rendering.PortalVeil): it grows out of the portal and swallows the view, then opens from the middle on arrival.
///
/// NOTHING HERE TURNS THE CAMERA. The first version rolled the view and swung it round toward the portal, which the
/// user found nauseating (2026-09-17); the pull is translation, field of view and screen effects only, so the player
/// keeps looking exactly where they were looking.
///
/// Owner only, a camera modifier after the sword sway (950) and before the arms (1000). RunManager drives it; every
/// number is a property on the player's instance.
/// </summary>
[Title( "Portal Transition" ), Category( "Void Mage" ), Icon( "cyclone" )]
public sealed class PortalTransition : Component, ICameraModifier
{
	[Property, Group( "Pull" )] public float PullFovDegrees { get; set; } = 26f;
	/// <summary>How far the eye drifts toward the portal, units.</summary>
	[Property, Group( "Pull" )] public float PullDriftUnits { get; set; } = 44f;
	/// <summary>How far the eye sinks on the way in, units: the floor giving way, in place of the old roll.</summary>
	[Property, Group( "Pull" )] public float PullSinkUnits { get; set; } = 7f;
	/// <summary>Retro filter aberration at the peak, source pixels.</summary>
	[Property, Group( "Pull" )] public float PullAberration { get; set; } = 11f;
	/// <summary>Height above the portal's base the pull aims at (the model's heart, not its feet).</summary>
	[Property, Group( "Pull" )] public float PortalCentreHeight { get; set; } = 70f;
	/// <summary>How many pulses wash out of the portal on the way in (the ripple the retro filter already does for hits).</summary>
	[Property, Group( "Pull" ), Range( 0f, 5f )] public int PullPulses { get; set; } = 3;

	[Property, Group( "Arrive" )] public float ArriveFovDegrees { get; set; } = 18f;
	/// <summary>How far the eye is still sunk on arrival, units: it rises back to standing.</summary>
	[Property, Group( "Arrive" )] public float ArriveSinkUnits { get; set; } = 5f;
	[Property, Group( "Arrive" )] public float ArriveAberration { get; set; } = 7f;

	public int CameraOrder => 970;

	public static PortalTransition Local { get; private set; }

	private enum Mode { None, Pull, Hold, Arrive }

	private Mode _mode;
	private float _start;
	private float _duration = 1f;
	private Vector3 _target;
	private int _pulsesFired;

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
		Rendering.RetroFilter.TransitionAberration = 0f;
	}

	/// <summary>0..1 through the current pull or arrival.</summary>
	private float Progress => Ease.Window( Time.Now, _start, _duration );

	/// <summary>Start the pull into a portal whose base stands at `portalBase`; it holds at full strength once done.</summary>
	public void BeginPull( Vector3 portalBase, float seconds )
	{
		_mode = Mode.Pull;
		_start = Time.Now;
		_duration = MathF.Max( 0.05f, seconds );
		_target = portalBase + Vector3.Up * PortalCentreHeight;
		_pulsesFired = 1;
		Rendering.RetroFilter.TriggerRipple( _target, 1.6f );
		EnsureVeil();
		Rendering.PortalVeil.BeginPull( _target, _duration );
	}

	/// <summary>The veil is a post process on the scene camera; a scene without one gets it with defaults (add it to the camera to tune it).</summary>
	private void EnsureVeil()
	{
		var camera = Scene.Camera;
		if ( camera.IsValid() ) camera.GameObject.Components.GetOrCreate<Rendering.PortalVeil>();
	}

	/// <summary>Start the let-go after a teleport: wide and smeared, settling back to nothing.</summary>
	public void BeginArrive( float seconds )
	{
		_mode = Mode.Arrive;
		_start = Time.Now;
		_duration = MathF.Max( 0.05f, seconds );
		EnsureVeil();
		Rendering.PortalVeil.BeginArrive( _duration );
	}

	/// <summary>When pulse `n` (1-based) goes off: 0.42, 0.7, 0.88... bunching up as the pull tightens.</summary>
	private static float PulseAt( int n ) => 1f - 0.58f * MathF.Pow( 0.62f, n - 1 );

	public void Stop()
	{
		_mode = Mode.None;
		Rendering.RetroFilter.TransitionAberration = 0f;
		Rendering.PortalVeil.Stop();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		switch ( _mode )
		{
			case Mode.Pull:
				Rendering.RetroFilter.TransitionAberration = PullAberration * Ease.InQuad( Progress );
				// pulses out of the portal's heart, closer together as it takes hold: the beat the roll used to give
				while ( _pulsesFired < PullPulses + 1 && Progress >= PulseAt( _pulsesFired ) )
				{
					Rendering.RetroFilter.TriggerRipple( _target, 1.6f + _pulsesFired * 0.5f );
					_pulsesFired++;
				}
				if ( Progress >= 1f ) _mode = Mode.Hold;
				break;
			case Mode.Hold:
				Rendering.RetroFilter.TransitionAberration = PullAberration;
				break;
			case Mode.Arrive:
				Rendering.RetroFilter.TransitionAberration = ArriveAberration * (1f - Ease.OutCubic( Progress ));
				if ( Progress >= 1f ) Stop();
				break;
		}
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || _mode == Mode.None || camera != Scene.Camera ) return;
		if ( PlayerHealth.Local.IsValid() && PlayerHealth.Local.IsDead ) return;

		// view.Rotation is never touched here: see the note at the top of the file
		if ( _mode is Mode.Pull or Mode.Hold )
		{
			float p = _mode == Mode.Hold ? 1f : Ease.InOutCubic( Progress );
			var toPortal = _target - view.Position;
			// straight in, however the player happens to be facing, and sinking as it takes them
			if ( !toPortal.IsNearZeroLength )
				view.Position += toPortal.Normal * MathF.Min( PullDriftUnits, toPortal.Length * 0.5f ) * p;
			view.Position += Vector3.Down * PullSinkUnits * Ease.InQuad( p );
			view.FieldOfView += PullFovDegrees * Ease.InQuad( p );
		}
		else if ( _mode == Mode.Arrive )
		{
			float left = 1f - Ease.OutCubic( Progress );
			view.FieldOfView += ArriveFovDegrees * left;
			view.Position += Vector3.Down * ArriveSinkUnits * left;
		}
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
