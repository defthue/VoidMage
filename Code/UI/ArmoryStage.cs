using System;
using System.Linq;
using Sandbox;
using VoidMage.Inventory;

namespace VoidMage.UI;

/// <summary>
/// The Armory tab's showpiece (user, 2026-09-18): the picked sword or staff in 3D, leaning 45 degrees and turning
/// slowly about an upright pole through its middle (a turntable; turning about its own length was tried first and
/// was not what the user meant), instead of a rune. Built like the WardrobeStage: a little stage far under the
/// world in the same scene, its own lights, one camera that whitelists the stage's tag and renders into a
/// texture the panel wears as a background. It only renders while the tab is showing a weapon.
///
/// The weapon is centred by its model bounds, not its pivot (swords pivot at the guard, staffs at the grip or the
/// middle), and framed by its bounding sphere, so every skin fills the picture whatever its size.
/// </summary>
[Title( "Armory Stage" ), Category( "Void Mage" ), Icon( "swords" )]
public sealed class ArmoryStage : Component
{
	public const string Tag = "armory";
	public const string StageTag = "armory_stage";

	[ConVar( "armory_fov" )] public static float Fov { get; set; } = 24f;
	/// <summary>Degrees the weapon leans off upright, tip towards the right of the picture (negative leans left).</summary>
	[ConVar( "armory_tilt" )] public static float Tilt { get; set; } = 45f;
	/// <summary>Degrees a second.</summary>
	[ConVar( "armory_spin_speed" )] public static float SpinSpeed { get; set; } = 36f;
	/// <summary>1 (default) turns the leaning weapon about the vertical like a turntable; 0 turns it about its own length, so the lean holds still.</summary>
	[ConVar( "armory_turntable" )] public static bool Turntable { get; set; } = true;
	/// <summary>Room around the weapon (1 = its bounding sphere touches the edges).</summary>
	/// <summary>
	/// Degrees a flat weapon is turned about its own length before it leans (user, 2026-09-19): as modelled, a sword
	/// leant over flat side down; a quarter turn puts the cutting edge down instead. Swords and the gem staff only,
	/// the other staffs are round enough not to care. -90 shows a one-edged blade's other edge.
	/// </summary>
	[ConVar( "armory_edge_yaw" )] public static float EdgeYaw { get; set; } = 90f;
	[ConVar( "armory_padding" )] public static float Padding { get; set; } = 1.04f;
	[ConVar( "armory_key_brightness" )] public static float KeyBrightness { get; set; } = 1.6f;
	[ConVar( "armory_rim_brightness" )] public static float RimBrightness { get; set; } = 0.9f;
	[ConVar( "armory_supersample" )] public static float Supersample { get; set; } = 2f;

	// well clear of the wardrobe's stage at (0, 0, -30000): lights are not tag-filtered
	private static readonly Vector3 StageOrigin = new( 6000f, 0f, -30000f );

	public static ArmoryStage Instance { get; private set; }

	/// <summary>What the panel wears; null until the first frame it is active.</summary>
	public Texture PreviewTexture => _rt;

	/// <summary>The panel sets this every frame it shows a weapon; nothing renders otherwise.</summary>
	public bool Active { get; set; }

	private GameObject _tilt;
	private GameObject _spin;
	private GameObject _modelObject;
	private ModelRenderer _renderer;
	private CameraComponent _camera;
	private PointLight _key;
	private PointLight _rim;
	private Texture _rt;
	private Vector2 _panelSize = new( 392f, 392f );
	private string _shownId;
	private float _radius = 16f;
	private float _yaw;

	/// <summary>The scene's stage, built on first use.</summary>
	public static ArmoryStage Ensure( Scene scene )
	{
		if ( Instance.IsValid() && Instance.Scene == scene ) return Instance;
		var existing = scene.GetAllComponents<ArmoryStage>().FirstOrDefault();
		if ( existing.IsValid() ) return Instance = existing;

		var go = new GameObject( true, "armory stage" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
			WorldPosition = StageOrigin,
		};
		go.Tags.Add( Tag );
		return go.Components.Create<ArmoryStage>();
	}

	protected override void OnAwake()
	{
		Instance = this;
		GameObject.Tags.Add( Tag );

		_tilt = new GameObject( true, "tilt" ) { Parent = GameObject };
		_tilt.Tags.Add( StageTag );
		_spin = new GameObject( true, "spin" ) { Parent = _tilt };
		_modelObject = new GameObject( true, "weapon" ) { Parent = _spin };
		_renderer = _modelObject.Components.Create<ModelRenderer>();
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		_key = BuildLight( "key", new Vector3( -70f, 45f, 60f ), new Color( 1f, 0.96f, 0.9f ) );
		_rim = BuildLight( "rim", new Vector3( 50f, -60f, 25f ), new Color( 0.62f, 0.72f, 1f ) );

		var cameraObject = new GameObject( true, "camera" ) { Parent = GameObject };
		_camera = cameraObject.Components.Create<CameraComponent>();
		_camera.IsMainCamera = false;
		_camera.Priority = -102;
		_camera.ClearFlags = ClearFlags.All;
		_camera.BackgroundColor = Color.Transparent;
		_camera.RenderTags.Add( StageTag );
		_camera.EnablePostProcessing = false;
		_camera.FovAxis = CameraComponent.Axis.Vertical;
		_camera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_camera.ZNear = 1f;
		_camera.ZFar = 2000f;
		_camera.Enabled = false;
	}

	private PointLight BuildLight( string name, Vector3 at, Color colour )
	{
		var go = new GameObject( true, name ) { Parent = GameObject, LocalPosition = at };
		go.Tags.Add( StageTag );
		var light = go.Components.Create<PointLight>();
		light.Shadows = false;
		light.Radius = 400f;
		light.LightColor = colour;
		return light;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		_rt?.Dispose();
		_rt = null;
	}

	/// <summary>Puts a skin on the stage (null clears it). The turn carries on across skins, so flicking through them is one motion.</summary>
	public void Show( WeaponSkin skin )
	{
		var id = skin?.Id;
		if ( id == _shownId ) return;
		_shownId = id;
		var model = skin is null ? null : WeaponSkins.ModelOf( skin );
		_renderer.Model = model;
		_renderer.Enabled = model is not null;
		if ( model is null ) return;

		// head down in the file (WeaponSkin.Flip): stood on its head here, as it is in the hand. A flat weapon is
		// then turned edge-down about its length; skin.Spin is the quarter turn a blade modelled crosswise already needs.
		var flip = skin.Flip ? Rotation.FromRoll( 180f ) : Rotation.Identity;
		bool flat = skin.IsSword || skin.Id == WeaponSkins.DefaultStaff;
		var turn = Rotation.FromYaw( (flat ? EdgeYaw : 0f) + skin.Spin ) * flip;
		var bounds = model.Bounds;
		_modelObject.LocalRotation = turn;
		_modelObject.LocalPosition = -(turn * bounds.Center);
		_radius = MathF.Max( bounds.Size.Length * 0.5f, 1f );
	}

	/// <summary>The panel tells the stage how big it draws the picture; the render target follows.</summary>
	public void SetPanelSize( Vector2 pixels )
	{
		if ( pixels.x >= 16f && pixels.y >= 16f ) _panelSize = pixels;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;

		var main = Scene.Camera;
		if ( main.IsValid() && !main.RenderExcludeTags.Has( Tag ) ) main.RenderExcludeTags.Add( Tag );

		bool render = Active && _renderer.Enabled;
		Active = false;   // the panel asks again every frame it needs the picture
		if ( _camera.Enabled != render ) _camera.Enabled = render;
		if ( !render ) return;

		_yaw = (_yaw + SpinSpeed * Time.Delta) % 360f;
		if ( Turntable )
		{
			_tilt.LocalRotation = Rotation.FromYaw( _yaw ) * Rotation.FromRoll( Tilt );
			_spin.LocalRotation = Rotation.Identity;
		}
		else
		{
			_tilt.LocalRotation = Rotation.FromRoll( Tilt );
			_spin.LocalRotation = Rotation.FromYaw( _yaw );
		}

		// the camera sits on -x looking down +x, level with the weapon's centre
		float halfFov = MathF.Tan( Math.Clamp( Fov, 5f, 90f ) * 0.5f * MathF.PI / 180f );
		float distance = _radius * MathF.Max( Padding, 0.5f ) / halfFov;
		_camera.WorldRotation = Rotation.Identity;
		_camera.WorldPosition = StageOrigin - Vector3.Forward * distance;
		_camera.FieldOfView = Fov;
		_key.LightColor = new Color( 1f, 0.96f, 0.9f ) * KeyBrightness;
		_rim.LightColor = new Color( 0.62f, 0.72f, 1f ) * RimBrightness;

		// A camera with a RenderTarget is rendered into that texture by the engine's own camera pass; it must
		// keep a non-zero viewport or that pass skips it (see DungeonMinimap).
		var size = new Vector2( MathF.Round( _panelSize.x * Supersample ), MathF.Round( _panelSize.y * Supersample ) );
		size = new Vector2( Math.Clamp( size.x, 16f, 2048f ), Math.Clamp( size.y, 16f, 2048f ) );
		_rt = Texture.CreateRenderTarget( "armory_preview", ImageFormat.RGBA8888, size, _rt );
		if ( _camera.RenderTarget != _rt ) _camera.RenderTarget = _rt;
		_camera.CustomSize = size;
	}
}
