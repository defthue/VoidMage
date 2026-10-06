using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Dungeon;

namespace VoidMage.Run;

/// <summary>
/// A portal that looks alive: void motes spiral in toward its heart and vanish there, and a faint purple light
/// breathes on the floor. It notices the player: within NoticeRange the spiral quickens, the motes lean toward
/// them and the light swells. Held shut by a chase (RunManager.PortalBlocked) the light dims to a sick red flicker
/// and the motes stall. The sheet inside the frame is its own model (the FBX gives frame and sheet one material, so
/// the sheet lives in sm_prop_portal_01_surface.vmdl with void_portal.shader on it) and gets the same three moods
/// through shader attributes. Subtle first (fire VFX lessons). Built at runtime by RunPortal on every portal, village and
/// exits alike; the particle step runs threaded and reads only the plain state object.
/// </summary>
[Title( "Portal Vfx" ), Category( "Void Mage" ), Icon( "blur_circular" )]
public sealed class PortalVfx : Component
{
	[Property] public float HeartHeight { get; set; } = 70f;
	[Property] public float SpiralRadius { get; set; } = 60f;
	[Property] public float NoticeRange { get; set; } = 260f;
	[Property] public Color VoidColor { get; set; } = new( 0.55f, 0.3f, 0.95f );
	[Property] public Color BlockedColor { get; set; } = new( 0.75f, 0.12f, 0.12f );
	[Property] public float LightRadius { get; set; } = 170f;
	[Property] public float LightBrightness { get; set; } = 0.55f;
	[Property] public float MoteRate { get; set; } = 10f;

	/// <summary>The skull on the frame: green fire in its sockets, the Bone Colossus's eyes in small (BossEyes' colours).</summary>
	[Property, Group( "Skull Eyes" )] public bool SkullEyes { get; set; } = true;
	[Property, Group( "Skull Eyes" )] public Color EyeCore { get; set; } = new( 0.55f, 1f, 0.55f );
	[Property, Group( "Skull Eyes" )] public Color EyeDeep { get; set; } = new( 0.08f, 0.8f, 0.3f );
	[Property, Group( "Skull Eyes" )] public Color EyeLightColor { get; set; } = new( 0.35f, 1f, 0.45f );
	[Property, Group( "Skull Eyes" )] public float EyeLightBrightness { get; set; } = 0.6f;
	/// <summary>Flame size in the sockets, units. The sockets are about 2 units wide.</summary>
	[Property, Group( "Skull Eyes" )] public float EyeFlameSize { get; set; } = 5f;
	/// <summary>
	/// The sockets in the frame model's own space, measured from the FBX (the faces on the atlas's red swatch, which
	/// DarkFortressPortal.vmat turns green): s&box X = -blender y (the skull's face), Y = blender x, Z = up, x 39.37.
	/// Pushed 1 unit out of the bone so the flame is never inside the stone.
	/// </summary>
	private static readonly Vector3[] EyeSockets = { new( 9.3f, -3.43f, 108.8f ), new( 9.3f, 3.0f, 108.6f ) };

	private const string TexMote = "vfx/polygonparticles_soft_spot.png";
	private const string FrameModel = "sm_prop_portal_01";
	private const string SurfaceModel = "darkfortress/sm_prop_portal_01_surface.vmdl";

	/// <summary>What the spiral is doing this frame; written on the main thread, read by the threaded step.</summary>
	public sealed class SpiralState
	{
		public Vector3 Heart;
		public Vector3 Toward;      // unit direction to the player, zero when far
		public float Notice;        // 0..1
		public float Stall;         // 0..1, the portal is held shut
		public float Radius = 60f;
	}

	private sealed class SpiralController : ParticleController
	{
		public SpiralState State;

		protected override void OnParticleStep( Particle p, float delta )
		{
			var s = State;
			if ( s == null || delta <= 0f ) return;
			var offset = p.Position - s.Heart;
			var flat = offset.WithZ( 0f );
			var dist = MathF.Max( 1f, offset.Length );
			// tangential swirl around the vertical axis, a pull to the heart that grows as the mote ages
			var tangent = new Vector3( -flat.y, flat.x, 0f ).Normal;
			float life = p.LifeDelta;
			float swirl = (40f + 90f * s.Notice) * (1f - s.Stall * 0.85f);
			float pull = (20f + 60f * life + 70f * s.Notice) * (1f - s.Stall * 0.9f);
			var target = tangent * swirl - offset / dist * pull + s.Toward * 25f * s.Notice * (1f - life);
			var v = p.Velocity;
			v += (target - v) * MathF.Min( 1f, delta * 3f );
			p.Velocity = v;
		}
	}

	/// <summary>0..1, the portal is held shut by a chase: it stops drawing things in.</summary>
	public float Stall => _stall;
	/// <summary>World position of the heart, where everything it draws in ends up.</summary>
	public Vector3 Heart => _root.IsValid() ? _root.WorldPosition : WorldPosition + Vector3.Up * HeartHeight;

	private GameObject _root;
	private ParticleEffect _fx;
	private ParticleSphereEmitter _emitter;
	private PointLight _light;
	private RunPortal _portal;
	private readonly List<(ParticleEffect Fire, ParticleEffect Glow, ParticleSphereEmitter FireEm)> _eyes = new();
	private GameObject _eyesGo;
	private PointLight _eyeLight;
	private float _eyeStall = -1f;
	private GameObject _surfaceGo;
	private ModelRenderer _surface;
	private float _phase;
	private readonly SpiralState _state = new();
	private float _notice;
	private float _stall;

	protected override void OnEnabled()
	{
		if ( Scene.IsEditor ) return;
		Build();
	}

	protected override void OnDisabled()
	{
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
		if ( _eyesGo.IsValid() ) _eyesGo.Destroy();
		_eyesGo = null;
		_eyes.Clear();
		if ( _surfaceGo.IsValid() ) _surfaceGo.Destroy();
		_surfaceGo = null;
		_surface = null;
	}

	private void Build()
	{
		if ( _root.IsValid() ) return;
		_portal = Components.Get<RunPortal>( FindMode.EverythingInSelf );
		_root = new GameObject( GameObject, true, "portal_vfx" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		_root.LocalPosition = Vector3.Up * HeartHeight;

		_light = _root.Components.Create<PointLight>();
		_light.Shadows = false;
		_light.Radius = LightRadius;
		_light.LightColor = VoidColor * LightBrightness;

		var (go, fx, renderer) = FireVfxRig.AddSpriteLayer( _root, "motes", TexMote, additive: true, bloom: false );
		renderer.DepthFeather = 4f;
		_emitter = go.Components.Create<ParticleSphereEmitter>();
		_emitter.Radius = SpiralRadius;
		_emitter.Velocity = 0f;   // the emitter's default 100 flings everything outward
		_emitter.Rate = MoteRate;
		_emitter.Burst = 0f;
		_emitter.Loop = true;

		fx.MaxParticles = 80;
		fx.LocalSpace = 0f;
		fx.Damping = 0f;
		fx.Force = false;
		fx.Lifetime = new ParticleFloat( 1.6f, 2.6f );
		fx.Scale = FireVfxRig.OverLife( 3.5f, 0.5f );
		fx.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.25f, 0.7f ), new Curve.Frame( 0.8f, 0.6f ), new Curve.Frame( 1f, 0f ) );
		fx.Gradient = FireVfxRig.RandomBetween( VoidColor, new Color( 0.85f, 0.7f, 1f ) );
		_fx = fx;

		var steer = go.Components.Create<SpiralController>();
		steer.State = _state;

		BuildSurface();
	}

	/// <summary>The void sheet, at the frame's own pivot (both models import the same FBX, so they line up at zero).</summary>
	private void BuildSurface()
	{
		var frame = Components.Get<ModelRenderer>( FindMode.EverythingInSelf );
		var path = frame?.Model?.ResourcePath;
		if ( path == null || !path.Contains( FrameModel, StringComparison.OrdinalIgnoreCase ) ) return;
		_surfaceGo = new GameObject( GameObject, true, "portal_surface" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		_surface = _surfaceGo.Components.Create<ModelRenderer>();
		_surface.Model = Model.Load( SurfaceModel );
		_surface.RenderType = ModelRenderer.ShadowRenderType.Off;
		_phase = Game.Random.Float( 0f, 40f );   // no two portals swirl in step
		if ( SkullEyes ) BuildEyes();
	}

	/// <summary>BossEyes' socket fire in small: a flame layer and a glow per socket, one faint light between them.</summary>
	private void BuildEyes()
	{
		_eyesGo = new GameObject( GameObject, true, "portal_eyes" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		float s = MathF.Max( 0.01f, WorldScale.x );
		float size = EyeFlameSize * s;
		foreach ( var socket in EyeSockets )
		{
			var eye = new GameObject( _eyesGo, true, "eye" ) { LocalPosition = socket };
			var (fgo, fire, frend) = FireVfxRig.AddSpriteLayer( eye, "fire", "vfx/flame_tongue.png", additive: true, bloom: true );
			frend.DepthFeather = 1f;
			var fem = fgo.Components.Create<ParticleSphereEmitter>();
			fem.Radius = 0.6f * s;
			fem.Velocity = 0f;
			fem.Rate = 14f;
			fem.Burst = 0f;
			fem.Loop = true;
			fire.MaxParticles = 16;
			fire.LocalSpace = 1f;
			fire.Lifetime = new ParticleFloat( 0.3f, 0.55f );
			fire.Scale = new ParticleFloat( size * 0.7f, size );
			fire.Force = true;
			fire.ForceDirection = Vector3.Up * 16f * s;
			fire.Damping = 0.2f;
			fire.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.15f, 1f ), new Curve.Frame( 0.7f, 0.8f ), new Curve.Frame( 1f, 0f ) );

			var (ggo, glow, grend) = FireVfxRig.AddSpriteLayer( eye, "glow", "vfx/flame_glow.png", additive: true, bloom: true );
			grend.DepthFeather = 1f;
			var gem = ggo.Components.Create<ParticleSphereEmitter>();
			gem.Radius = 0.2f * s;
			gem.Velocity = 0f;
			gem.Rate = 8f;
			gem.Burst = 0f;
			gem.Loop = true;
			glow.MaxParticles = 8;
			glow.LocalSpace = 1f;
			glow.Lifetime = new ParticleFloat( 0.25f, 0.4f );
			glow.Scale = new ParticleFloat( size * 1.6f, size * 2.2f );
			glow.Alpha = FireVfxRig.CurveOverLife( new Curve.Frame( 0f, 0f ), new Curve.Frame( 0.3f, 0.5f ), new Curve.Frame( 1f, 0f ) );
			_eyes.Add( (fire, glow, fem) );
		}

		var lightGo = new GameObject( _eyesGo, true, "eye_light" ) { LocalPosition = (EyeSockets[0] + EyeSockets[1]) * 0.5f + Vector3.Forward * 6f };
		_eyeLight = lightGo.Components.Create<PointLight>();
		_eyeLight.Shadows = false;
		_eyeLight.Radius = 60f * s;
	}

	/// <summary>Green while the portal is open, the blocked red while a chase holds it shut; they flare as the player comes close.</summary>
	private void DriveEyes()
	{
		if ( _eyes.Count == 0 ) return;
		if ( MathF.Abs( _stall - _eyeStall ) > 0.02f )
		{
			_eyeStall = _stall;
			var core = Color.Lerp( EyeCore, new Color( 1f, 0.5f, 0.35f ), _stall );
			var deep = Color.Lerp( EyeDeep, BlockedColor, _stall );
			foreach ( var (fire, glow, _) in _eyes )
			{
				if ( fire.IsValid() ) fire.Gradient = FireVfxRig.RandomBetween( core, deep );
				if ( glow.IsValid() ) glow.Gradient = FireVfxRig.RandomBetween( deep, deep.Darken( 0.3f ) );
			}
		}
		float t = Time.Now;
		float flicker = 1f + 0.18f * MathF.Sin( t * 13f ) + 0.12f * MathF.Sin( t * 29f + 1f );
		foreach ( var (fire, _, em) in _eyes )
		{
			if ( em.IsValid() ) em.Rate = 14f + 14f * _notice;
			if ( fire.IsValid() ) fire.Brightness = flicker * (1f + 0.8f * _notice);
		}
		if ( _eyeLight.IsValid() )
			_eyeLight.LightColor = Color.Lerp( EyeLightColor, BlockedColor, _stall ) * (EyeLightBrightness * flicker * (1f + _notice));
	}

	protected override void OnUpdate()
	{
		if ( !_root.IsValid() ) return;
		var heart = _root.WorldPosition;
		_state.Heart = heart;
		_state.Radius = SpiralRadius;

		// notice: eased on the distance to the local player's chest
		var player = PlayerHealth.Local;
		float wantNotice = 0f;
		var toward = Vector3.Zero;
		if ( player.IsValid() && !player.IsDead )
		{
			var chest = player.FeetPosition + Vector3.Up * 40f;
			var d = chest.Distance( heart );
			wantNotice = 1f - Ease.Clamp01( (d - 60f) / MathF.Max( 1f, NoticeRange - 60f ) );
			if ( d > 1f ) toward = (chest - heart) / d;
		}
		_notice = MathX.Approach( _notice, wantNotice * wantNotice, Time.Delta * 1.5f );
		_state.Notice = _notice;
		_state.Toward = toward;

		// held shut: only an exit portal on the floor the player is fighting on
		var run = RunManager.Instance;
		bool blocked = run.IsValid() && run.PortalBlocked && _portal.IsValid() && _portal.Kind == PortalKind.Exit && wantNotice > 0.5f;
		_stall = MathX.Approach( _stall, blocked ? 1f : 0f, Time.Delta * 3f );
		_state.Stall = _stall;

		if ( _emitter.IsValid() ) _emitter.Rate = MoteRate * (1f + 1.2f * _notice) * (1f - 0.8f * _stall);

		if ( _surface.IsValid() && _surface.SceneObject.IsValid() )
		{
			// the phase is integrated here: a speed change must quicken the swirl, never jump the pattern
			_phase += Time.Delta * (1f + 1.4f * _notice) * (1f - 0.85f * _stall);
			var c = Color.Lerp( VoidColor, BlockedColor, _stall );
			var attr = _surface.SceneObject.Attributes;
			attr.Set( "PortalPhase", _phase );
			attr.Set( "PortalFreeRun", 0f );
			attr.Set( "PortalNotice", _notice );
			attr.Set( "PortalStall", _stall );
			attr.Set( "PortalColor", new Vector3( c.r, c.g, c.b ) );
		}

		DriveEyes();

		if ( _light.IsValid() )
		{
			float breath = 0.8f + 0.2f * MathF.Sin( Time.Now * 1.3f + heart.x * 0.01f );
			float flicker = _stall > 0.01f ? 0.55f + 0.45f * MathF.Abs( MathF.Sin( Time.Now * 17f ) * MathF.Sin( Time.Now * 5.3f ) ) : 1f;
			var color = Color.Lerp( VoidColor, BlockedColor, _stall );
			_light.LightColor = color * LightBrightness * breath * (1f + 0.8f * _notice) * Ease.Lerp( 1f, flicker * 0.6f, _stall );
			_light.Radius = LightRadius * (1f + 0.25f * _notice);
		}
	}
}
