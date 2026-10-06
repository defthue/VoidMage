using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.UI;

/// <summary>
/// The wardrobe's hidden photo stage: a dressed mannequin, a second body that wears one piece at a time for
/// the catalogue icons, their own sun and fill light, and two cameras rendering into textures the Wardrobe
/// panel shows. It sits far under the map in the SAME scene (the toon shader and outline then look exactly
/// as in play), everything under it tagged "wardrobe" so the main camera never draws it, and each camera
/// whitelists its own tag ("wardrobe_stage" for the preview, "wardrobe_icon" for the icon rig's pieces), so
/// the icon body itself is never seen: a piece floats alone, the way Synty's own catalogue shows them.
/// A camera's tag whitelist filters lights too, so the stage lights carry both tags.
///
/// Icons bake lazily: the panel asks for an option's icon, gets null until it exists, and the queue dresses
/// the icon rig, waits a frame for the bone merge to settle, then lets the engine's camera pass render it
/// into that icon's own texture. Textures are kept for the session, keyed by option and colour set.
/// </summary>
[Title( "Wardrobe Stage" ), Category( "Void Mage" ), Icon( "checkroom" )]
public sealed class WardrobeStage : Component
{
	public const string Tag = "wardrobe";
	public const string StageTag = "wardrobe_stage";
	public const string IconTag = "wardrobe_icon";

	/// <summary>Vertical field of view of the preview camera. Narrower flattens the figure, wider fattens the ink (outline width grows with distance).</summary>
	[ConVar( "wardrobe_fov" )] public static float Fov { get; set; } = 32f;
	/// <summary>Camera pitch in degrees (positive looks down).</summary>
	[ConVar( "wardrobe_pitch" )] public static float Pitch { get; set; } = 4f;
	/// <summary>Stage sun pitch / yaw relative to the camera (yaw positive = from the camera's left).</summary>
	[ConVar( "wardrobe_sun_pitch" )] public static float SunPitch { get; set; } = 38f;
	[ConVar( "wardrobe_sun_yaw" )] public static float SunYaw { get; set; } = 30f;
	[ConVar( "wardrobe_sun_brightness" )] public static float SunBrightness { get; set; } = 1.1f;
	[ConVar( "wardrobe_fill_brightness" )] public static float FillBrightness { get; set; } = 0.35f;
	/// <summary>Icon texture edge in pixels.</summary>
	[ConVar( "wardrobe_icon_size" )] public static int IconSize { get; set; } = 256;
	/// <summary>Extra room around a piece in its icon (1 = the piece touches the edges).</summary>
	[ConVar( "wardrobe_icon_padding" )] public static float IconPadding { get; set; } = 1.3f;
	/// <summary>Icon camera yaw off the piece's front (a 3/4 view) and pitch above it.</summary>
	[ConVar( "wardrobe_icon_yaw" )] public static float IconYaw { get; set; } = 35f;
	[ConVar( "wardrobe_icon_pitch" )] public static float IconPitch { get; set; } = 18f;
	/// <summary>Preview render target = panel pixels times this (2 = supersampled, the panel downscales it).</summary>
	[ConVar( "wardrobe_supersample" )] public static float Supersample { get; set; } = 2f;

	private static readonly Vector3 StageOrigin = new( 0f, 0f, -30000f );
	private const float FigureHeight = 74f;
	private const float HeadHeight = 64f;
	private const float DragDegreesPerPixel = 0.5f;
	private const float StartWarmupSeconds = 0.4f;

	public static WardrobeStage Instance { get; private set; }

	/// <summary>The preview render (transparent where nothing was drawn); null until the first frame.</summary>
	public Texture PreviewTexture => _previewRt;

	/// <summary>The dressed figure the panel edits.</summary>
	public CharacterAppearance Mannequin { get; private set; }

	/// <summary>Bumped every time an icon finishes, so panels showing icons know to rebuild.</summary>
	public static int IconVersion { get; private set; }

	private GameObject _mannequinObject;
	private SkinnedModelRenderer _mannequinBody;
	private CameraComponent _previewCamera;
	private Texture _previewRt;
	private Vector2 _panelSize = new( 520f, 780f );
	private float _yaw;
	private float _zoom;
	private float _builtAt;

	private SkinnedModelRenderer _iconBody;
	private CameraComponent _iconCamera;
	private GameObject _iconHolder;
	private readonly Queue<string> _iconQueue = new();
	private readonly HashSet<string> _iconQueued = new();
	private string _baking;
	private Texture _bakingRt;
	private int _bakeStep;
	private static readonly Dictionary<string, Texture> _icons = new();

	// ------------------------------------------------------------------ lifetime

	/// <summary>The scene's stage, built on first use.</summary>
	public static WardrobeStage Ensure( Scene scene )
	{
		if ( Instance.IsValid() && Instance.Scene == scene ) return Instance;
		var existing = scene.GetAllComponents<WardrobeStage>().FirstOrDefault();
		if ( existing.IsValid() ) return Instance = existing;

		var go = new GameObject( true, "wardrobe stage" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
			WorldPosition = StageOrigin,
		};
		go.Tags.Add( Tag );
		return go.Components.Create<WardrobeStage>();
	}

	// Built on awake, not start: Ensure() hands the stage back the same frame, and the panel dresses the
	// mannequin right away.
	protected override void OnAwake()
	{
		Instance = this;
		GameObject.Tags.Add( Tag );
		_builtAt = Time.Now;
		BuildLights();
		BuildMannequin();
		BuildIconRig();
		BuildCameras();
		Reset();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		_previewRt?.Dispose();
		_previewRt = null;
		_bakingRt?.Dispose();
		_bakingRt = null;
		_iconQueue.Clear();
		_iconQueued.Clear();
		_baking = null;
	}

	private void BuildLights()
	{
		var sunObject = new GameObject( true, "sun" ) { Parent = GameObject };
		sunObject.Tags.Add( StageTag );
		sunObject.Tags.Add( IconTag );
		var sun = sunObject.Components.Create<DirectionalLight>();
		sun.Shadows = false;
		sun.LightColor = Color.White * SunBrightness;
		sun.SkyColor = new Color( 0.42f, 0.46f, 0.54f );

		var fillObject = new GameObject( true, "fill" ) { Parent = GameObject };
		fillObject.Tags.Add( StageTag );
		fillObject.Tags.Add( IconTag );
		fillObject.LocalPosition = new Vector3( -90f, 90f, 40f );
		var fill = fillObject.Components.Create<PointLight>();
		fill.Shadows = false;
		fill.Radius = 400f;
		fill.LightColor = new Color( 0.85f, 0.9f, 1f ) * FillBrightness;
	}

	/// <summary>The same shape as the player prefab's `visual`: body + hair/eyes/brows children, dressed by a CharacterAppearance.</summary>
	private void BuildMannequin()
	{
		_mannequinObject = new GameObject( true, "mannequin" ) { Parent = GameObject };
		_mannequinObject.Tags.Add( StageTag );
		_mannequinBody = BuildBody( _mannequinObject, withFace: true );
		Mannequin = _mannequinObject.Components.Create<CharacterAppearance>();
	}

	private void BuildIconRig()
	{
		var rig = new GameObject( true, "icon rig" ) { Parent = GameObject };
		// no StageTag: the preview camera never sees it, and the icon camera only sees the piece holder
		_iconBody = BuildBody( rig, withFace: false );
	}

	/// <summary>A human_base body playing its idle; with the face, the same shape as the player prefab's `visual` (hair/eyes/brows children).</summary>
	private static SkinnedModelRenderer BuildBody( GameObject parent, bool withFace )
	{
		var body = parent.Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( "charactertoon/human_base.vmdl" );
		body.RenderType = ModelRenderer.ShadowRenderType.Off;
		body.UseAnimGraph = true;
		body.AnimationGraph = AnimationGraph.Load( "animgraphs/human_base.vanmgrph" );
		if ( !withFace ) return body;

		foreach ( var (name, model) in new[] { ("hair", "charactertoon/human_hair.vmdl"), ("eyes", "charactertoon/human_eyes.vmdl"), ("brows", "charactertoon/human_brows.vmdl") } )
		{
			var child = new GameObject( true, name ) { Parent = parent };
			var r = child.Components.Create<SkinnedModelRenderer>();
			r.Model = Model.Load( model );
			r.BoneMergeTarget = body;
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		return body;
	}

	private void BuildCameras()
	{
		var previewObject = new GameObject( true, "preview camera" ) { Parent = GameObject };
		_previewCamera = previewObject.Components.Create<CameraComponent>();
		_previewCamera.IsMainCamera = false;
		_previewCamera.Priority = -100;
		_previewCamera.ClearFlags = ClearFlags.All;
		_previewCamera.BackgroundColor = Color.Transparent;
		_previewCamera.RenderTags.Add( StageTag );
		_previewCamera.EnablePostProcessing = false;
		_previewCamera.FovAxis = CameraComponent.Axis.Vertical;
		_previewCamera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_previewCamera.ZNear = 4f;
		_previewCamera.ZFar = 2000f;

		var iconObject = new GameObject( true, "icon camera" ) { Parent = GameObject };
		_iconCamera = iconObject.Components.Create<CameraComponent>();
		_iconCamera.IsMainCamera = false;
		_iconCamera.Priority = -101;
		_iconCamera.Orthographic = true;
		_iconCamera.ClearFlags = ClearFlags.All;
		_iconCamera.BackgroundColor = Color.Transparent;
		_iconCamera.RenderTags.Add( IconTag );
		_iconCamera.EnablePostProcessing = false;
		_iconCamera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_iconCamera.ZNear = 4f;
		_iconCamera.ZFar = 2000f;
		_iconCamera.Enabled = false;
	}

	// ------------------------------------------------------------------ preview control

	/// <summary>Turn the figure (drag).</summary>
	public void Turn( float pixels ) => _yaw = (_yaw + pixels * DragDegreesPerPixel) % 360f;

	/// <summary>Zoom toward the head (wheel up) or back to the full figure.</summary>
	public void Zoom( float delta ) => _zoom = Math.Clamp( _zoom + delta, 0f, 1f );

	public void Reset()
	{
		_yaw = 0f;
		_zoom = 0f;
	}

	/// <summary>The panel tells the stage how big it draws the preview; the render target follows.</summary>
	public void SetPanelSize( Vector2 pixels )
	{
		if ( pixels.x >= 16f && pixels.y >= 16f ) _panelSize = pixels;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;
		HideFromMainCamera();
		FramePreview();
		TickIcons();
	}

	private void HideFromMainCamera()
	{
		var main = Scene.Camera;
		if ( main.IsValid() && !main.RenderExcludeTags.Has( Tag ) ) main.RenderExcludeTags.Add( Tag );
	}

	private void FramePreview()
	{
		if ( !_previewCamera.IsValid() || !_mannequinObject.IsValid() ) return;

		// the figure faces the camera (it stands at the origin looking down -x, the camera sits on -x looking +x)
		_mannequinObject.WorldRotation = Rotation.FromYaw( 180f + _yaw );

		float halfFov = MathF.Tan( Math.Clamp( Fov, 10f, 90f ) * 0.5f * MathF.PI / 180f );
		float fullDistance = FigureHeight * 0.56f / halfFov;
		float headDistance = 22f / halfFov;
		float focusZ = MathX.Lerp( FigureHeight * 0.5f, HeadHeight, _zoom );
		float distance = MathX.Lerp( fullDistance, headDistance, _zoom );
		var rotation = Rotation.From( Pitch, 0f, 0f );
		var focus = StageOrigin + Vector3.Up * focusZ;
		_previewCamera.WorldRotation = rotation;
		_previewCamera.WorldPosition = focus - rotation.Forward * distance;
		_previewCamera.FieldOfView = Fov;

		var sun = GameObject.Children.FirstOrDefault( c => c.Name == "sun" );
		if ( sun.IsValid() )
		{
			sun.WorldRotation = Rotation.From( SunPitch, SunYaw, 0f );
			var light = sun.Components.Get<DirectionalLight>();
			if ( light.IsValid() ) light.LightColor = Color.White * SunBrightness;
		}

		// A camera with a RenderTarget is rendered into that texture by the engine's own camera pass; it must
		// keep a non-zero viewport or that pass skips it (see DungeonMinimap).
		var size = new Vector2( MathF.Round( _panelSize.x * Supersample ), MathF.Round( _panelSize.y * Supersample ) );
		size = new Vector2( Math.Clamp( size.x, 16f, 4096f ), Math.Clamp( size.y, 16f, 4096f ) );
		_previewRt = Texture.CreateRenderTarget( "wardrobe_preview", ImageFormat.RGBA8888, size, _previewRt );
		if ( _previewCamera.RenderTarget != _previewRt ) _previewCamera.RenderTarget = _previewRt;
		_previewCamera.CustomSize = size;
	}

	// ------------------------------------------------------------------ icons

	/// <summary>The icon for an option in the mannequin's current colour set, or null while it bakes (it is queued by asking).</summary>
	public Texture Icon( string optionId )
	{
		var option = AppearanceOptions.Get( optionId );
		if ( option is null || option.IsNone || option.Slot == AppearanceSlot.Palette ) return null;
		var key = IconKey( optionId );
		if ( _icons.TryGetValue( key, out var icon ) && icon.IsValid() ) return icon;
		if ( _iconQueued.Add( key ) ) _iconQueue.Enqueue( optionId );
		return null;
	}

	private string PaletteGroup => AppearanceOptions.Get( Mannequin?.Current?.OptionFor( AppearanceSlot.Palette ) )?.MaterialGroup ?? "";

	private string IconKey( string optionId ) => $"{optionId}@{PaletteGroup}";

	private void TickIcons()
	{
		if ( !_iconBody.IsValid() || !_iconCamera.IsValid() ) return;
		if ( Time.Now < _builtAt + StartWarmupSeconds ) return;

		if ( _baking is null )
		{
			if ( _iconQueue.Count == 0 )
			{
				if ( _iconCamera.Enabled ) _iconCamera.Enabled = false;
				return;
			}
			_baking = _iconQueue.Dequeue();
			_bakeStep = 0;
			DressIconRig( _baking );
			return; // bones settle during this frame's UpdateBones
		}

		if ( _bakeStep == 0 )
		{
			// bounds are last frame's bone merge: frame the piece and let the camera pass render it this frame
			if ( !FrameIcon() )
			{
				FinishIcon( null );
				return;
			}
			int size = Math.Clamp( IconSize, 32, 1024 );
			_bakingRt = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ).Create( $"wardrobe_icon_{_baking}" );
			_iconCamera.RenderTarget = _bakingRt;
			_iconCamera.CustomSize = new Vector2( size, size );
			_iconCamera.Enabled = true;
			_bakeStep = 1;
			return;
		}

		// rendered at the end of last frame
		var done = _bakingRt;
		_bakingRt = null;
		FinishIcon( done );
	}

	private void FinishIcon( Texture texture )
	{
		var key = IconKey( _baking );
		if ( texture is not null )
		{
			_icons[key] = texture;
			IconVersion++;
		}
		_iconQueued.Remove( key );
		_baking = null;
		_iconCamera.RenderTarget = null;
		if ( _iconHolder.IsValid() ) _iconHolder.DestroyImmediate();
		_iconHolder = null;
	}

	private void DressIconRig( string optionId )
	{
		if ( _iconHolder.IsValid() ) _iconHolder.DestroyImmediate();
		_iconHolder = new GameObject( true, "icon piece" ) { Parent = _iconBody.GameObject };
		_iconHolder.Tags.Add( IconTag );

		var option = AppearanceOptions.Get( optionId );
		var group = PaletteGroup;
		var hairTint = Color.White;
		if ( Mannequin?.Current is { } current && AppearanceData.TryParseTint( current.TintFor( AppearanceOptions.TintParts.Hair ), out var r, out var g, out var b ) )
			hairTint = new Color( r, g, b, 1f );

		foreach ( var path in option?.Models ?? Array.Empty<string>() )
		{
			var model = Model.Load( path );
			if ( model is null || model.IsError ) continue;
			var renderer = _iconHolder.Components.Create<SkinnedModelRenderer>();
			renderer.Model = model;
			renderer.BoneMergeTarget = _iconBody;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			renderer.MaterialGroup = string.IsNullOrEmpty( group ) ? null : group;
			if ( option.Slot is AppearanceSlot.Hair or AppearanceSlot.FacialHair ) renderer.Tint = hairTint;
		}
	}

	/// <summary>Points the orthographic icon camera at the piece from a 3/4 view (from behind for back pieces).</summary>
	private bool FrameIcon()
	{
		if ( !_iconHolder.IsValid() ) return false;
		var option = AppearanceOptions.Get( _baking );
		BBox? bounds = null;
		foreach ( var renderer in _iconHolder.Components.GetAll<SkinnedModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
		{
			var b = renderer.Bounds;
			if ( b.Size.Length < 0.5f ) continue;
			bounds = bounds is null ? b : bounds.Value.AddBBox( b );
		}
		if ( bounds is null ) return false;

		var box = bounds.Value;
		float extent = MathF.Max( box.Size.x, MathF.Max( box.Size.y, box.Size.z ) );
		// the rig faces -x; a front 3/4 view looks along +x, turned IconYaw to the side, from IconPitch above
		float yaw = IconYaw;
		var rotation = Rotation.From( IconPitch, yaw, 0f );
		_iconCamera.WorldRotation = rotation;
		_iconCamera.WorldPosition = box.Center - rotation.Forward * 400f;
		_iconCamera.OrthographicHeight = MathF.Max( 4f, extent * IconPadding );
		return true;
	}

	// ------------------------------------------------------------------ dev: look at the results

	/// <summary>Bakes every catalogue icon and writes a contact sheet PNG to data/voidmage/wardrobe/icons.png.</summary>
	[ConCmd( "wardrobe_icon_sheet" )]
	public static void IconSheetCommand()
	{
		var stage = Instance;
		if ( !stage.IsValid() ) { Log.Warning( "[Wardrobe] open the wardrobe first (C)" ); return; }
		var ids = AppearanceOptions.All.Values.Where( o => !o.IsNone && o.Slot != AppearanceSlot.Palette ).Select( o => o.Id ).ToList();
		foreach ( var id in ids ) stage.Icon( id );
		stage._sheetPending = ids;
		Log.Info( $"[Wardrobe] baking {ids.Count} icons for the sheet" );
	}

	/// <summary>Writes the current preview render to data/voidmage/wardrobe/preview.png.</summary>
	[ConCmd( "wardrobe_shot" )]
	public static void ShotCommand()
	{
		var stage = Instance;
		if ( !stage.IsValid() || stage._previewRt is null ) { Log.Warning( "[Wardrobe] open the wardrobe first (C)" ); return; }
		WritePng( stage._previewRt, "voidmage/wardrobe/preview.png" );
	}

	private List<string> _sheetPending;

	protected override void OnPreRender()
	{
		if ( _sheetPending is null ) return;
		if ( _sheetPending.Any( id => Icon( id ) is null ) ) return;
		var ids = _sheetPending;
		_sheetPending = null;
		WriteSheet( ids );
	}

	private void WriteSheet( List<string> ids )
	{
		const int cell = 128, columns = 10;
		int rows = (ids.Count + columns - 1) / columns;
		var bmp = new Bitmap( cell * columns, cell * rows );
		var backdrop = new Color32( 40, 42, 48, 255 );
		for ( var y = 0; y < bmp.Height; y++ )
			for ( var x = 0; x < bmp.Width; x++ )
				bmp.SetPixel( x, y, backdrop );
		for ( var i = 0; i < ids.Count; i++ )
		{
			var tex = Icon( ids[i] );
			if ( tex is null ) continue;
			var px = tex.GetPixels();
			int w = tex.Width, h = tex.Height;
			int ox = (i % columns) * cell, oy = (i / columns) * cell;
			for ( var y = 0; y < cell; y++ )
				for ( var x = 0; x < cell; x++ )
				{
					var p = px[(y * h / cell) * w + (x * w / cell)];
					if ( p.a < 8 ) continue;
					bmp.SetPixel( ox + x, oy + y, p );
				}
		}
		Write( bmp, "voidmage/wardrobe/icons.png" );
		Log.Info( $"[Wardrobe] icon sheet: {ids.Count} icons, {columns}x{rows}" );
	}

	private static void WritePng( Texture tex, string path )
	{
		var px = tex.GetPixels();
		var bmp = new Bitmap( tex.Width, tex.Height );
		for ( var y = 0; y < tex.Height; y++ )
			for ( var x = 0; x < tex.Width; x++ )
				bmp.SetPixel( x, y, px[y * tex.Width + x] );
		Write( bmp, path );
	}

	private static void Write( Bitmap bmp, string path )
	{
		FileSystem.Data.CreateDirectory( "voidmage/wardrobe" );
		FileSystem.Data.WriteAllBytes( path, bmp.ToPng() );
		Log.Info( $"[Wardrobe] wrote {path} ({bmp.Width}x{bmp.Height})" );
	}
}
