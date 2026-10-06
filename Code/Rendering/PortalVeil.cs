using System;
using Sandbox;
using Sandbox.Rendering;
using VoidMage.Run;

namespace VoidMage.Rendering;

/// <summary>
/// The portal's vortex over the whole view, for going through one (void_portal_veil.shader). On the way in it grows
/// out of the portal's place on screen until its dark heart is all that is left; on arrival a clear hole opens from
/// the middle of the view and flings the void off the edges. PortalTransition starts both; while neither runs this
/// inserts nothing and costs nothing.
///
/// A post process on the scene camera (PortalTransition creates it when the scene has none), one grab and blit at
/// BeforePostProcess ahead of the retro filter's pass (order 500), so the vortex is pixelated, graded and bloomed
/// with the world and the run's black fade and menus draw over it.
/// </summary>
[Title( "Portal Veil" ), Category( "Void Mage" ), Icon( "cyclone" )]
public sealed class PortalVeil : BasePostProcess<PortalVeil>
{
	/// <summary>Rim radius when the pull ends, in screen heights. The heart is the inner 0.42 of it, and it has to cover the far corner.</summary>
	[Property, Group( "Pull" ), Range( 2f, 10f )] public float PullEndRadius { get; set; } = 5.4f;
	/// <summary>Rim radius when the pull starts: about the portal's own size on screen.</summary>
	[Property, Group( "Pull" ), Range( 0f, 0.5f )] public float PullStartRadius { get; set; } = 0.08f;
	/// <summary>Hole radius when the arrival ends, in screen heights (the corner is 1.02 from the middle at 16:9).</summary>
	[Property, Group( "Arrive" ), Range( 1f, 4f )] public float ArriveEndRadius { get; set; } = 1.7f;
	/// <summary>How far the frame next to the rim is bent toward it, in screen heights.</summary>
	[Property, Range( 0f, 0.2f )] public float Lens { get; set; } = 0.05f;
	[Property] public Color VoidColor { get; set; } = new( 0.55f, 0.3f, 0.95f );

	private enum Mode { None, Pull, Arrive }

	private static Mode _mode;
	private static float _start;
	private static float _duration = 1f;
	private static Vector3 _world;
	private static float _phase;

	/// <summary>True while a pull or an arrival is on screen.</summary>
	public static bool Active => _mode != Mode.None;

	public static void BeginPull( Vector3 portalHeart, float seconds )
	{
		_mode = Mode.Pull;
		_start = Time.Now;
		_duration = MathF.Max( 0.05f, seconds );
		_world = portalHeart;
	}

	public static void BeginArrive( float seconds )
	{
		_mode = Mode.Arrive;
		_start = Time.Now;
		_duration = MathF.Max( 0.05f, seconds );
	}

	public static void Stop() => _mode = Mode.None;

	private CommandList _commands;
	private Material _material;

	public override void Render()
	{
		if ( _mode == Mode.None || !Camera.IsValid() || Camera != Scene.Camera ) return;
		float p = Ease.Window( Time.Now, _start, _duration );
		// the pull holds full (under the run's black) until it is stopped; the arrival ends itself
		if ( _mode == Mode.Arrive && p >= 1f ) { _mode = Mode.None; return; }

		_material ??= Material.FromShader( "shaders/void_portal_veil.shader" );
		if ( _material is null ) return;

		var centre = new Vector2( 0.5f, 0.5f );
		float radius, strength;
		if ( _mode == Mode.Pull )
		{
			// out of the portal wherever it is on screen, sliding to the middle as it takes the view: you fall in straight
			var screen = Camera.PointToScreenNormal( _world, out var behind );
			if ( !behind ) centre = new Vector2( Math.Clamp( screen.x, 0.1f, 0.9f ), Math.Clamp( screen.y, 0.1f, 0.9f ) );
			centre = Vector2.Lerp( centre, new Vector2( 0.5f, 0.5f ), Ease.InQuad( p ) );
			radius = Ease.Lerp( PullStartRadius, PullEndRadius, Ease.InCubic( p ) );
			strength = Ease.OutQuad( p / 0.15f );
			_phase += Time.Delta * (1.5f + 5f * p);   // integrated: the swirl quickens without the pattern jumping
		}
		else
		{
			radius = MathF.Max( 0.001f, ArriveEndRadius * Ease.InOutCubic( p ) );
			strength = 1f - Ease.InCubic( p );
			_phase += Time.Delta * (6.5f - 4.5f * p);
		}

		_commands ??= new CommandList( "Portal Veil" );
		_commands.Reset();
		var c = _commands.Attributes;
		c.Set( "VeilMode", _mode == Mode.Arrive ? 1f : 0f );
		c.Set( "VeilCentreX", centre.x );
		c.Set( "VeilCentreY", centre.y );
		c.Set( "VeilRadius", radius );
		c.Set( "VeilStrength", strength );
		c.Set( "VeilPhase", _phase );
		c.Set( "VeilLens", Lens );
		c.Set( "VeilTwist", 2.2f );
		c.Set( "VeilColor", new Vector3( VoidColor.r, VoidColor.g, VoidColor.b ) );
		_commands.GrabFrameTexture( "ColorBuffer" );
		_commands.Blit( _material );
		InsertCommandList( _commands, Stage.BeforePostProcess, 400, "Portal Veil" );
	}
}
