using System;
using System.Linq;
using Sandbox;
using VoidMage.Village;

namespace VoidMage.UI;

/// <summary>
/// The hotbar's frog (user, 2026-09-19: "like the portrait for the elkdemon shopkeeper, but for frogs"): a live frog
/// on a hidden stage far under the world, breathing on the monsters' own frog graph, filmed by a camera that
/// whitelists the stage's tag into a small transparent texture the frog slot wears. Same build as the keeper's
/// portrait and the ArmoryStage. It only renders while the slot is on screen (the panel asks every frame).
/// </summary>
[Title( "Frog Portrait" ), Category( "Void Mage" ), Icon( "pets" )]
public sealed class FrogPortrait : Component
{
	public const string Tag = "frogportrait";
	public const string StageTag = "frogportrait_stage";

	/// <summary>Degrees the frog is turned off facing the camera: a three-quarter view, looking into the hotbar.</summary>
	[ConVar( "frog_portrait_yaw" )] public static float Yaw { get; set; } = 32f;
	[ConVar( "frog_portrait_pitch" )] public static float Pitch { get; set; } = 14f;
	[ConVar( "frog_portrait_fov" )] public static float Fov { get; set; } = 22f;
	/// <summary>Room around the frog (1 = its bounding sphere touches the edges).</summary>
	[ConVar( "frog_portrait_padding" )] public static float Padding { get; set; } = 0.82f;
	[ConVar( "frog_portrait_key" )] public static float KeyBrightness { get; set; } = 1.6f;
	[ConVar( "frog_portrait_fill" )] public static float FillBrightness { get; set; } = 0.7f;

	// clear of the wardrobe (0), the keeper (4000) and the armory (6000): lights are not tag-filtered
	private static readonly Vector3 StageOrigin = new( 8000f, 0f, -30000f );
	private const int Size = 192;

	public static FrogPortrait Instance { get; private set; }

	public Texture Texture => _rt;

	/// <summary>The hotbar sets this every frame the frog slot is showing; nothing renders otherwise.</summary>
	public bool Active { get; set; }

	private GameObject _frog;
	private SkinnedModelRenderer _body;
	private CameraComponent _camera;
	private PointLight _key;
	private PointLight _fill;
	private Texture _rt;
	private Vector3 _centre;
	private float _radius = 12f;

	public static FrogPortrait Ensure( Scene scene )
	{
		if ( Instance.IsValid() && Instance.Scene == scene ) return Instance;
		var existing = scene.GetAllComponents<FrogPortrait>().FirstOrDefault();
		if ( existing.IsValid() ) return Instance = existing;

		var go = new GameObject( true, "frog portrait" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
			WorldPosition = StageOrigin,
		};
		go.Tags.Add( Tag );
		return go.Components.Create<FrogPortrait>();
	}

	protected override void OnAwake()
	{
		Instance = this;
		GameObject.Tags.Add( Tag );

		_frog = new GameObject( true, "frog" ) { Parent = GameObject };
		_frog.Tags.Add( StageTag );
		_body = _frog.Components.Create<SkinnedModelRenderer>();
		_body.Model = Model.Load( VillageFrogs.FrogModel );
		_body.AnimationGraph = AnimationGraph.Load( VillageFrogs.FrogAnimGraph );
		_body.RenderType = ModelRenderer.ShadowRenderType.Off;
		if ( _body.Model is { IsError: false } model )
		{
			_centre = model.Bounds.Center;
			_radius = MathF.Max( model.Bounds.Size.Length * 0.5f, 1f );
		}

		_key = BuildLight( "key", new Vector3( 60f, 40f, 50f ), new Color( 1f, 0.97f, 0.9f ) );
		_fill = BuildLight( "fill", new Vector3( 30f, -60f, 10f ), new Color( 0.7f, 0.8f, 1f ) );

		var cameraObject = new GameObject( true, "camera" ) { Parent = GameObject };
		_camera = cameraObject.Components.Create<CameraComponent>();
		_camera.IsMainCamera = false;
		_camera.Priority = -103;
		_camera.ClearFlags = ClearFlags.All;
		_camera.BackgroundColor = Color.Transparent;
		_camera.RenderTags.Add( StageTag );
		_camera.EnablePostProcessing = false;
		_camera.FovAxis = CameraComponent.Axis.Vertical;
		_camera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_camera.ZNear = 1f;
		_camera.ZFar = 1000f;
		_camera.Enabled = false;
	}

	private PointLight BuildLight( string name, Vector3 at, Color colour )
	{
		var go = new GameObject( true, name ) { Parent = GameObject, LocalPosition = at };
		go.Tags.Add( StageTag );
		var light = go.Components.Create<PointLight>();
		light.Shadows = false;
		light.Radius = 300f;
		light.LightColor = colour;
		return light;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		_rt?.Dispose();
		_rt = null;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;

		var main = Scene.Camera;
		if ( main.IsValid() && !main.RenderExcludeTags.Has( Tag ) ) main.RenderExcludeTags.Add( Tag );

		bool render = Active;
		Active = false;   // the hotbar asks again every frame it shows the slot
		if ( _camera.Enabled != render ) _camera.Enabled = render;
		if ( _frog.Enabled != render ) _frog.Enabled = render;
		if ( !render ) return;

		// the frog faces the camera (it looks down +x at the origin, the camera sits on +x looking back), turned a little
		_frog.LocalRotation = Rotation.FromYaw( Yaw );
		var rotation = Rotation.From( Pitch, 180f, 0f );
		float halfFov = MathF.Tan( Math.Clamp( Fov, 5f, 90f ) * 0.5f * MathF.PI / 180f );
		float distance = _radius * MathF.Max( Padding, 0.3f ) / halfFov;
		var focus = StageOrigin + _frog.LocalRotation * _centre;
		_camera.WorldRotation = rotation;
		_camera.WorldPosition = focus - rotation.Forward * distance;
		_camera.FieldOfView = Fov;
		_key.LightColor = new Color( 1f, 0.97f, 0.9f ) * KeyBrightness;
		_fill.LightColor = new Color( 0.7f, 0.8f, 1f ) * FillBrightness;

		// A camera with a RenderTarget is rendered into that texture by the engine's own camera pass; it must
		// keep a non-zero viewport or that pass skips it (see DungeonMinimap).
		var size = new Vector2( Size, Size );
		_rt = Texture.CreateRenderTarget( "frog_portrait", ImageFormat.RGBA8888, size, _rt );
		if ( _camera.RenderTarget != _rt ) _camera.RenderTarget = _rt;
		_camera.CustomSize = size;
	}
}
