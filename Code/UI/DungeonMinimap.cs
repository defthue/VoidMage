using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.UI;

/// <summary>
/// The orthographic reveal-as-you-go minimap. Lives on the Dungeon object next to DungeonBuilder, which
/// calls Rebuild after every generate. The map is a set of flat toon-shaded slabs built from the layout
/// grid (one model per tier, one per stair or landing cell, each with an inverted-hull outline) placed
/// in the dungeon's own coordinates and tagged "minimap": the main camera excludes that tag, a second
/// orthographic camera renders only it into a small transparent render target that the Minimap panel
/// blows up with point sampling in the top-right corner, so it shares the retro filter's pixel grid.
/// Tiers start hidden and are switched on the moment the local player's feet enter one of their cells,
/// so the map only ever shows where the player has been. The camera yaws with the player (up on the
/// map is the way the player faces) and tilts with the player's view at a reduced rate: looking down
/// flattens toward top-down, looking up leans toward a side view.
/// Levels stack on world Z, so deep in the dungeon the floors overhead would cover the player's own
/// position: the map is cut into three bands at LowerSplit/UpperSplit and every band above the
/// player's is alpha blended, which keeps the stack readable without hiding where the player is.
/// </summary>
[Title( "Dungeon Minimap" ), Category( "Void Mage" ), Icon( "map" )]
public sealed class DungeonMinimap : Component
{
	public const string Tag = "minimap";
	public const string MaterialPath = "materials/minimap.vmat";
	public const string OutlineMaterialPath = "materials/minimap_outline.vmat";
	/// <summary>Shader attribute both minimap shaders dither against, one value per piece.</summary>
	public const string OpacityAttribute = "MinimapOpacity";

	/// <summary>Cells visible across the panel's height (zoom).</summary>
	[Property, Range( 6f, 40f )] public float MapCells { get; set; } = 14f;
	/// <summary>Camera tilt in degrees while the player looks level: 90 is straight down, lower is more oblique.</summary>
	[Property, Range( 15f, 90f )] public float BasePitch { get; set; } = 40f;
	/// <summary>How much of the player's own view pitch is added to the tilt (0.5: look 40° down, the map tilts 20° further).</summary>
	[Property, Range( 0f, 1f )] public float PitchFollow { get; set; } = 0.5f;
	[Property, Range( 10f, 90f )] public float MinPitch { get; set; } = 18f;
	[Property, Range( 10f, 90f )] public float MaxPitch { get; set; } = 85f;
	/// <summary>Render target size in pixels. Small on purpose: the panel scales it up with point sampling for the retro look.</summary>
	[Property, Range( 48, 512 )] public int Resolution { get; set; } = 128;
	[Property, Range( 4f, 60f )] public float SlabThickness { get; set; } = 24f;
	/// <summary>Outline hull growth in world units; about one map pixel at the default zoom and resolution.</summary>
	[Property, Range( 0f, 60f )] public float OutlineWidth { get; set; } = 20f;
	/// <summary>The outline draws the hull's far inside, which depends on the engine's front-face winding. If slabs render as solid black blocks, flip this.</summary>
	[Property] public bool FlipOutlineWinding { get; set; } = false;

	// Backing fields rather than auto-properties so changing one of these while the game runs pushes the
	// new value to the pieces on the next frame instead of waiting for the player to change level.
	private int _lowerSplit = 2;
	private int _upperSplit = 5;
	private float _aboveOpacity = 0.18f;

	/// <summary>
	/// Top level of the bottom band. The dungeon is cut into three bands (Grid.MaxLevel is 8, so the
	/// defaults give 0..2 / 3..5 / 6..8); every band above the one the player stands in goes see-through.
	/// </summary>
	[Property, Group( "Levels above" ), Range( 0, 8 )]
	public int LowerSplit { get => _lowerSplit; set { _lowerSplit = value; _opacityDirty = true; } }
	/// <summary>Top level of the middle band; everything above it is the top band.</summary>
	[Property, Group( "Levels above" ), Range( 0, 8 )]
	public int UpperSplit { get => _upperSplit; set { _upperSplit = value; _opacityDirty = true; } }
	/// <summary>Map opacity of the bands above the player's. Below 1 those pieces blend and drop their
	/// outlines, so the band the player is in reads through the stack overhead.</summary>
	[Property, Group( "Levels above" ), Range( 0f, 1f )]
	public float AboveOpacity { get => _aboveOpacity; set { _aboveOpacity = value; _opacityDirty = true; } }

	[Property, Group( "Colours" )] public Color CorridorColor { get; set; } = new( 0.34f, 0.33f, 0.42f );
	[Property, Group( "Colours" )] public Color RoomColor { get; set; } = new( 0.50f, 0.46f, 0.66f );
	[Property, Group( "Colours" )] public Color HallColor { get; set; } = new( 0.62f, 0.56f, 0.78f );
	[Property, Group( "Colours" )] public Color TowerColor { get; set; } = new( 0.70f, 0.48f, 0.82f );
	[Property, Group( "Colours" )] public Color StairColor { get; set; } = new( 0.66f, 0.58f, 0.44f );
	[Property, Group( "Colours" )] public Color SpawnColor { get; set; } = new( 0.42f, 0.66f, 0.48f );
	[Property, Group( "Colours" )] public Color ExitColor { get; set; } = new( 0.90f, 0.72f, 0.30f );
	[Property, Group( "Colours" )] public Color MarkerColor { get; set; } = new( 1.00f, 0.94f, 0.72f );
	/// <summary>A doorway the player has been through: both spaces it joins are on the map.</summary>
	[Property, Group( "Colours" )] public Color DoorColor { get; set; } = new( 0.94f, 0.86f, 0.62f );
	/// <summary>A doorway not yet gone through (user, 2026-09-22): only one side of it is on the map, so it leads somewhere new.</summary>
	[Property, Group( "Colours" )] public Color UnexploredDoorColor { get; set; } = new( 1.00f, 0.55f, 0.12f );

	/// <summary>Doorway mark measured along the wall, as a fraction of a cell.</summary>
	[Property, Group( "Doorways" ), Range( 0.1f, 0.8f )] public float DoorWidth { get; set; } = 0.40f;
	/// <summary>Doorway mark measured across the wall: it straddles the boundary, so half pokes into each space.</summary>
	[Property, Group( "Doorways" ), Range( 0.05f, 0.5f )] public float DoorDepth { get; set; } = 0.20f;

	/// <summary>Input action that toggles the large map (ProjectSettings/Input.config binds it to M).</summary>
	[Property] public string ToggleAction { get; set; } = "Map";
	/// <summary>Panel size, cells shown and render target all grow by this when the large map is toggled on, so the pixels stay the same size and three times the dungeon fits.</summary>
	[Property, Range( 1f, 5f )] public float EnlargeScale { get; set; } = 3f;

	/// <summary>The rendered map (transparent where nothing was drawn); null until the first frame.</summary>
	public Texture MapTexture => _rt;

	/// <summary>Large map toggled on (M).</summary>
	public bool Enlarged { get; private set; }

	/// <summary>Panel scale the Minimap panel should apply right now.</summary>
	public float PanelScale => Enlarged ? EnlargeScale : 1f;

	/// <summary>Tiers and stair/landing cells the player has walked into so far.</summary>
	public int RevealedCount { get; private set; }

	/// <summary>The grid level of the cell the player is standing in; -1 until they first enter the dungeon.</summary>
	public int CurrentLevel { get; private set; } = -1;

	private DungeonLayout _layout;
	private GameObject _root;
	private GameObject _marker;
	private CameraComponent _camera;
	private Texture _rt;
	private VoidMage.Character.PlayerHealth _player;
	private readonly Dictionary<int, GameObject> _tiers = new();
	private readonly Dictionary<(int x, int z, int l), GameObject> _cells = new();
	/// <summary>
	/// Doorway marks with the two pieces they join: shown as soon as either side is revealed. Each doorway is
	/// two slabs, the orange "not been through" one and the pale "been through" one; RevealDoors enables the
	/// one that applies (the colour is baked into the vertices, so a mark is swapped rather than recoloured).
	/// </summary>
	private readonly List<(GameObject unexplored, GameObject explored, GameObject a, GameObject b)> _doors = new();
	/// <summary>Every map piece with the grid level it sits on, for the see-through pass.</summary>
	private readonly List<(ModelRenderer renderer, int level)> _pieces = new();
	private bool _opacityDirty = true;
	private float _nextPanelCheck;
	private bool _panelReady;

	/// <summary>Vertex and index lists for one piece: the coloured body and its outline hull.</summary>
	private sealed class PieceMesh
	{
		public readonly List<Vertex> Body = new();
		public readonly List<int> BodyIdx = new();
		public readonly List<Vertex> Hull = new();
		public readonly List<int> HullIdx = new();
	}

	/// <summary>Throws the old map away and builds the slabs for a new layout (all hidden except the marker).</summary>
	public void Rebuild( DungeonLayout layout )
	{
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
		_marker = null;
		_secretMarker = null;
		_tiers.Clear();
		_cells.Clear();
		_doors.Clear();
		_pieces.Clear();
		_icons.Clear();
		RevealedCount = 0;
		CurrentLevel = -1;
		_opacityDirty = true;
		_layout = layout;
		if ( layout is null )
		{
			// no floor (the village): the panel hides on a null texture and the offscreen camera stops rendering
			_rt = null;
			if ( _camera.IsValid() ) _camera.Enabled = false;
			return;
		}

		var material = Material.Load( MaterialPath );
		var outline = Material.Load( OutlineMaterialPath );
		_root = new GameObject( GameObject, true, "minimap" ) { Flags = GameObjectFlags.NotSaved };

		foreach ( var tier in layout.Grid.Tiers )
		{
			if ( tier.Cells.Count == 0 ) continue;
			var color = TierColor( tier );
			var piece = new PieceMesh();
			foreach ( var (x, z) in tier.Cells ) AddSlab( piece, x, z, tier.Level, color );
			_tiers[tier.Id] = MakePiece( $"tier {tier.Id} {tier.Kind}", material, outline, piece, false, tier.Level );
		}

		foreach ( var (key, cell) in layout.Grid.Cells )
		{
			if ( cell.Tier is not null ) continue;
			var piece = new PieceMesh();
			if ( cell.Kind == CellKind.Stair ) AddRamp( piece, key.x, key.z, key.l, cell.SDir, StairColor );
			else AddSlab( piece, key.x, key.z, key.l, StairColor );
			_cells[key] = MakePiece( $"{cell.Kind} {key.x},{key.z},{key.l}", material, outline, piece, false, key.l );
		}

		// Adjacent tiers merge into one silhouette on the map, so the only way to tell a shared wall from a
		// way through is to mark the openings. Each doorway is a small raised threshold straddling the
		// boundary; the half sticking into the space you have not been to yet reads as a nub pointing out.
		foreach ( var (a, b) in layout.Doors )
		{
			var pa = PieceFor( a );
			var pb = PieceFor( b );
			if ( !pa.IsValid() && !pb.IsValid() ) continue;
			_doors.Add( (MakeDoor( $"door {a.x},{a.z},{a.l}", material, outline, (a.x, a.z), (b.x, b.z), a.l, UnexploredDoorColor ),
				MakeDoor( $"door {a.x},{a.z},{a.l} explored", material, outline, (a.x, a.z), (b.x, b.z), a.l, DoorColor ), pa, pb) );
		}

		// The openings onto staircases (user): layout.Doors only joins tier to tier, and a stair or landing cell
		// belongs to no tier, so where a room or corridor steps onto a stair run there was no mark. Every tier cell
		// the grid connects to a stair foot, a stair top or a landing gets the same threshold, at the tier's floor.
		foreach ( var (key, cell) in layout.Grid.Cells )
		{
			if ( cell.Tier is null ) continue;
			foreach ( var n in layout.Grid.Neighbours( key ) )
			{
				if ( layout.Grid.Cells[n].Tier is not null ) continue;
				var pa = PieceFor( key );
				var pb = PieceFor( n );
				if ( !pa.IsValid() && !pb.IsValid() ) continue;
				_doors.Add( (MakeDoor( $"stair opening {key.x},{key.z},{key.l}", material, outline, (key.x, key.z), (n.x, n.z), key.l, UnexploredDoorColor ),
					MakeDoor( $"stair opening {key.x},{key.z},{key.l} explored", material, outline, (key.x, key.z), (n.x, n.z), key.l, DoorColor ), pa, pb) );
			}
		}

		{
			// the player: a dart pointing along +X (the marker is yawed to the player every frame)
			var piece = new PieceMesh();
			var foot = new[] { new Vector2( 52f, 0f ), new Vector2( -34f, 36f ), new Vector2( -12f, 0f ), new Vector2( -34f, -36f ) };
			AddPrism( piece, foot, new[] { 16f, 16f, 16f, 16f }, 0f, MarkerColor );
			// int.MinValue: the marker is never a level above the player, so it always draws solid
			_marker = MakePiece( "player marker", material, outline, piece, true, int.MinValue );
		}

		if ( layout.SecretWall is { } sw )
		{
			// Dungeonwise: a gold diamond standing on the secret wall's edge, shown from the start (unexplored or not)
			float c = DungeonBuilder.Cell;
			var centre = new Vector2( (sw.x + 0.5f + sw.d.Dx() * 0.5f) * c, (sw.z + 0.5f + sw.d.Dz() * 0.5f) * c );
			float r = c * 0.32f;
			var foot = new[] { centre + new Vector2( r, 0f ), centre + new Vector2( 0f, r ), centre + new Vector2( -r, 0f ), centre + new Vector2( 0f, -r ) };
			float top = sw.l * DungeonBuilder.LevelH + 34f;
			var piece = new PieceMesh();
			AddPrism( piece, foot, new[] { top, top, top, top }, top - 14f, SecretColor );
			_secretMarker = MakePiece( "secret wall marker", material, outline, piece, false, sw.l );
		}
		RefreshSecretMarker();

		EnsureCamera();
		Log.Info( $"[Minimap] built {_tiers.Count} tiers, {_cells.Count} stair/landing cells, {_doors.Count} doorways" );
	}

	[Property, Group( "Colours" )] public Color SecretColor { get; set; } = new( 1.00f, 0.80f, 0.25f );

	private GameObject _secretMarker;

	/// <summary>The secret wall's marker follows the local player's Dungeonwise boon.</summary>
	private void RefreshSecretMarker()
	{
		if ( !_secretMarker.IsValid() ) return;
		bool on = VoidMage.Boons.PlayerBoons.Local.IsValid() && VoidMage.Boons.PlayerBoons.Local.Has( VoidMage.Boons.BoonStat.SecretWallsOnMap );
		if ( _secretMarker.Enabled == on ) return;
		_secretMarker.Enabled = on;
		_opacityDirty = true;
	}

	/// <summary>A boon changed: every minimap re-checks its secret wall marker.</summary>
	public static void RefreshSecretMarkers()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		foreach ( var map in scene.GetAllComponents<DungeonMinimap>() ) map.RefreshSecretMarker();
	}

	private Rotation _livingView;
	private bool _hasLivingView;

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying || _layout is null || !_root.IsValid() ) return;
		HideFromMainCamera();
		EnsurePanel();
		if ( !string.IsNullOrEmpty( ToggleAction ) && Input.Pressed( ToggleAction ) ) Enlarged = !Enlarged;

		var player = LocalPlayer;
		if ( !player.IsValid() ) return;
		var feet = player.WorldPosition;
		Reveal( WorldTransform.PointToLocal( feet ) );
		if ( _opacityDirty ) RefreshOpacity();

		// dead, the main camera tumbles with the ragdoll's head: the map keeps the last living view
		var mainCamera = Scene.Camera;
		var view = mainCamera.IsValid() ? mainCamera.WorldRotation : player.WorldRotation;
		if ( player.IsDead && _hasLivingView ) view = _livingView;
		else { _livingView = view; _hasLivingView = true; }
		float yaw = view.Yaw();
		if ( _marker.IsValid() )
		{
			_marker.WorldPosition = feet + Vector3.Up * 30f;
			_marker.WorldRotation = Rotation.FromYaw( yaw );
		}

		EnsureCamera();
		// positive pitch is looking down: the map flattens toward top-down as the player looks at the floor
		float pitch = Math.Clamp( BasePitch + PitchFollow * view.Pitch(), MinPitch, MaxPitch );
		var rot = Rotation.From( pitch, yaw, 0f );
		_camera.WorldRotation = rot;
		_camera.WorldPosition = feet - rot.Forward * 4000f;
		float scale = PanelScale;
		_camera.OrthographicHeight = MapCells * scale * DungeonBuilder.Cell;

		// A camera with a RenderTarget is rendered into that texture by the engine's own camera pass
		// (CameraComponent.AddToRenderList) instead of the screen; it must keep a non-zero viewport or
		// that pass skips it entirely.
		int size = Math.Max( 16, (int)MathF.Round( Resolution * scale ) );
		if ( _rt is null || _rt.Width != size )
		{
			_rt = Texture.CreateRenderTarget( "minimap", ImageFormat.RGBA8888, new Vector2( size, size ) );
			Log.Info( $"[Minimap] render target {size}x{size}" );
		}
		if ( _camera.RenderTarget != _rt ) _camera.RenderTarget = _rt;
		_camera.CustomSize = new Vector2( size, size );

		RefreshIcons( rot, _camera.WorldPosition, _camera.OrthographicHeight, size );
	}

	// ------------------------------------------------------------------ pickup icons

	/// <summary>What a mark on the map stands for; the panel has one pixel-art icon per kind (tools/gen_minimap_icons.py).</summary>
	public enum IconKind { Enchantment, Spell, Shard, Weapon }

	/// <summary>One mark: where it sits on the panel, 0..1 from the top-left corner.</summary>
	public readonly struct Icon
	{
		public readonly IconKind Kind;
		public readonly float U;
		public readonly float V;
		public Icon( IconKind kind, float u, float v ) { Kind = kind; U = u; V = v; }
	}

	/// <summary>
	/// Everything still lying on the floor, placed for this frame: scrolls (enchantment and spell apart), void shards
	/// and weapon skins (user, 2026-09-21). They are panels over the map rather than slabs in it: a 128 px render
	/// target leaves a shape five pixels to be told apart in, and an icon that turns with the map is harder to read
	/// than one that stays upright.
	/// </summary>
	public IReadOnlyList<Icon> Icons => _icons;

	/// <summary>Marks further than this from the panel's centre are dropped: the shaders' dithered rim starts at 0.40 and the map is gone by 0.49.</summary>
	[Property, Group( "Icons" ), Range( 0.2f, 0.5f )] public float IconRim { get; set; } = 0.44f;
	/// <summary>Shards nearer than this to each other share one mark (a knight alone drops eight).</summary>
	[Property, Group( "Icons" ), Range( 16f, 256f )] public float ShardCluster { get; set; } = 96f;

	private readonly List<Icon> _icons = new();
	private readonly Dictionary<(int x, int y, int z), (Vector3 sum, int n)> _shardBuckets = new();

	private void RefreshIcons( Rotation rot, Vector3 eye, float height, int size )
	{
		_icons.Clear();
		if ( height <= 0f ) return;
		var right = rot.Right;
		var up = rot.Up;

		void Add( IconKind kind, Vector3 world )
		{
			// orthographic and square: the panel is `height` world units across both ways
			var d = world - eye;
			float u = 0.5f + Vector3.Dot( d, right ) / height;
			float v = 0.5f - Vector3.Dot( d, up ) / height;
			float du = u - 0.5f, dv = v - 0.5f;
			if ( du * du + dv * dv > IconRim * IconRim ) return;
			// on the render target's pixel grid, so a mark steps with the map under it instead of sliding over it
			u = MathF.Round( u * size ) / size;
			v = MathF.Round( v * size ) / size;
			_icons.Add( new Icon( kind, u, v ) );
		}

		// shards first, so a scroll or a weapon lying among them is drawn over them
		_shardBuckets.Clear();
		float cluster = MathF.Max( 1f, ShardCluster );
		foreach ( var shard in VoidMage.Run.ShardPickup.All )
		{
			if ( !shard.IsValid() ) continue;
			var p = shard.WorldPosition;
			var key = ((int)MathF.Floor( p.x / cluster ), (int)MathF.Floor( p.y / cluster ), (int)MathF.Floor( p.z / cluster ));
			_shardBuckets[key] = _shardBuckets.TryGetValue( key, out var b ) ? (b.sum + p, b.n + 1) : (p, 1);
		}
		foreach ( var (sum, n) in _shardBuckets.Values ) Add( IconKind.Shard, sum / n );

		foreach ( var skin in VoidMage.Run.SkinPickup.All )
		{
			if ( !skin.IsValid() ) continue;
			// one standing in a rack was there before the player was: it shows once its room is on the map, like the
			// room itself. Anything that dropped did so because of the player, and is marked wherever it fell.
			if ( skin.InRack && !IsRevealedAt( WorldTransform.PointToLocal( skin.WorldPosition ) ) ) continue;
			Add( IconKind.Weapon, skin.WorldPosition );
		}

		foreach ( var scroll in VoidMage.Run.ScrollPickup.All )
		{
			if ( !scroll.IsValid() ) continue;
			Add( scroll.Scroll.IsSpell ? IconKind.Spell : IconKind.Enchantment, scroll.WorldPosition );
		}
	}

	/// <summary>The map piece under a point (dungeon space) has been walked into.</summary>
	private bool IsRevealedAt( Vector3 local )
	{
		int gx = (int)MathF.Floor( local.x / DungeonBuilder.Cell );
		int gz = (int)MathF.Floor( local.y / DungeonBuilder.Cell );
		int level = (int)MathF.Floor( local.z / DungeonBuilder.LevelH + 0.5f );
		for ( int dl = 0; dl <= 2; dl++ )
		{
			var go = PieceFor( (gx, gz, level - dl) );
			if ( go.IsValid() ) return go.Enabled;
		}
		return false;
	}

	// ------------------------------------------------------------------ reveal

	private VoidMage.Character.PlayerHealth LocalPlayer
	{
		get
		{
			if ( !_player.IsValid() || _player.IsProxy )
				_player = Scene.GetAllComponents<VoidMage.Character.PlayerHealth>().FirstOrDefault( p => !p.IsProxy );
			return _player;
		}
	}

	/// <summary>
	/// Switches on whatever the feet stand in: the whole tier, or the single stair / landing cell. Also
	/// notes the level being stood on, which is what the levels above are made see-through against.
	/// </summary>
	private void Reveal( Vector3 local )
	{
		int gx = (int)MathF.Floor( local.x / DungeonBuilder.Cell );
		int gz = (int)MathF.Floor( local.y / DungeonBuilder.Cell );
		// Round to the NEAREST level instead of flooring off a small epsilon. The controller's feet can
		// sit a few units under the floor plane on a slope or a ground snap, and an 8 unit tolerance read
		// that as a level down -- then two, because a headroom level carries no Cells entry so the loop
		// below stepped past it -- which strobed the map between halves while standing still.
		int level = (int)MathF.Floor( local.z / DungeonBuilder.LevelH + 0.5f );
		// a stair is keyed at its base level and spans three, so walk down a couple of levels
		for ( int dl = 0; dl <= 2; dl++ )
		{
			var key = (gx, gz, level - dl);
			if ( !_layout.Grid.Cells.TryGetValue( key, out var cell ) ) continue;
			// Always the level the found cell is keyed at, stairs included. A stair spans three levels, so
			// taking the player's own height on one would flip the halves halfway up a staircase; keyed at
			// its base, the map instead commits once, when the feet reach a cell that belongs to the level.
			if ( level - dl != CurrentLevel )
			{
				CurrentLevel = level - dl;
				_opacityDirty = true;
			}
			var go = PieceFor( key );
			if ( go.IsValid() && !go.Enabled )
			{
				go.Enabled = true;
				RevealedCount++;
				_opacityDirty = true;
				RevealDoors();
			}
			return;
		}
		// stepped outside the grid: keep the last level, so the map does not flicker at the seams
	}

	/// <summary>
	/// How much of a piece on this level draws. The dungeon is cut into three bands at LowerSplit and
	/// UpperSplit: every band above the player's goes see-through at once, their own band and everything
	/// below stay solid. Deliberately fixed boundaries rather than a fade that follows the player level by
	/// level: the map then only ever changes when one of those two lines is crossed, instead of re-grading
	/// on every step of a staircase.
	/// </summary>
	private float OpacityFor( int level )
	{
		if ( CurrentLevel < 0 ) return 1f;
		return BandOf( level ) > BandOf( CurrentLevel ) ? AboveOpacity : 1f;
	}

	private int BandOf( int level ) => level > UpperSplit ? 2 : level > LowerSplit ? 1 : 0;

	/// <summary>
	/// Pushes each revealed piece's see-through amount to its shader. A piece has no SceneObject until
	/// its object is enabled, so the dirty flag is held until every one of them has taken a value.
	/// </summary>
	private void RefreshOpacity()
	{
		bool all = true;
		foreach ( var (renderer, level) in _pieces )
		{
			if ( !renderer.IsValid() || !renderer.Active ) continue;
			var so = renderer.SceneObject;
			if ( !so.IsValid() ) { all = false; continue; }
			float opacity = OpacityFor( level );
			so.Attributes.Set( OpacityAttribute, opacity );
			// Bucket the piece by hand instead of letting the shader's translucent flag put every slab in
			// the sorted pass. Sorting there is per object, by distance, so an upper floor whose centroid
			// happened to sort ahead of a solid one below would write depth first and punch the lower floor
			// out entirely -- and which way that fell changed as the map turned, which read as flicker.
			// Solid pieces in the opaque pass are drawn first and settled by depth alone; the see-through
			// ones then blend over a map that is already complete, whatever order they arrive in.
			bool faded = opacity < 1f;
			so.Flags.IsOpaque = !faded;
			so.Flags.IsTranslucent = faded;
		}
		_opacityDirty = !all;
	}

	/// <summary>The map piece a cell belongs to: its whole tier, or the single stair / landing cell.</summary>
	private GameObject PieceFor( (int x, int z, int l) key )
	{
		if ( _layout is null || !_layout.Grid.Cells.TryGetValue( key, out var cell ) ) return null;
		GameObject go = null;
		if ( cell.Tier is not null ) _tiers.TryGetValue( cell.Tier.Id, out go );
		else _cells.TryGetValue( key, out go );
		return go;
	}

	/// <summary>
	/// A doorway shows as soon as either of the two spaces it joins has been walked into, in orange while the
	/// other side is still unknown (it leads somewhere new) and in the pale door colour once both sides are on
	/// the map (the player has been through it, or reached the far side another way).
	/// </summary>
	private void RevealDoors()
	{
		foreach ( var (unexplored, explored, a, b) in _doors )
		{
			if ( !unexplored.IsValid() || !explored.IsValid() ) continue;
			bool aOn = a.IsValid() && a.Enabled;
			bool bOn = b.IsValid() && b.Enabled;
			if ( !aOn && !bOn ) continue;
			bool through = aOn && bOn;
			if ( unexplored.Enabled == !through && explored.Enabled == through ) continue;
			unexplored.Enabled = !through;
			explored.Enabled = through;
			_opacityDirty = true;
		}
	}

	// ------------------------------------------------------------------ camera / panel

	private void EnsureCamera()
	{
		if ( _camera.IsValid() ) { if ( !_camera.Enabled ) _camera.Enabled = true; return; }
		// not a child of the dungeon: DungeonBuilder.Clear() destroys every child on a regenerate
		var go = new GameObject( true, "minimap camera" ) { Flags = GameObjectFlags.NotSaved };
		_camera = go.Components.Create<CameraComponent>();
		_camera.IsMainCamera = false;
		_camera.Priority = -100;
		_camera.Orthographic = true;
		_camera.ClearFlags = ClearFlags.All;
		_camera.BackgroundColor = Color.Transparent;
		_camera.RenderTags.Add( Tag );
		_camera.EnablePostProcessing = false;
		_camera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_camera.ZNear = 10f;
		_camera.ZFar = 12000f;
	}

	private void HideFromMainCamera()
	{
		var main = Scene.Camera;
		if ( main.IsValid() && !main.RenderExcludeTags.Has( Tag ) ) main.RenderExcludeTags.Add( Tag );
	}

	/// <summary>The panel belongs on the player's hud object; if the prefab lost it, add it so the map still shows.</summary>
	private void EnsurePanel()
	{
		if ( _panelReady || Time.Now < _nextPanelCheck ) return;
		_nextPanelCheck = Time.Now + 0.5f;
		// the player's hud panel, not the run loop's overlays (RunUi, BoonScreen), which are their own ScreenPanels
		var screen = Scene.GetAllComponents<ScreenPanel>().FirstOrDefault( p => p.Active && !p.IsProxy
			&& p.GameObject.Components.Get<VoidMage.Run.RunUi>( FindMode.EverythingInSelf ) is null
			&& p.GameObject.Components.Get<VoidMage.Boons.BoonScreen>( FindMode.EverythingInSelf ) is null );
		if ( !screen.IsValid() ) return;
		if ( screen.GameObject.Components.Get<Minimap>( FindMode.EverythingInSelf ) is null )
		{
			screen.GameObject.Components.Create<Minimap>();
			Log.Info( "[Minimap] hud had no Minimap panel; created one (add it to player.prefab to tune it)" );
		}
		_panelReady = true;
	}

	// ------------------------------------------------------------------ geometry

	private Color TierColor( Tier tier )
	{
		if ( tier.Tag == TierTag.Spawn ) return SpawnColor;
		if ( tier.Tag == TierTag.Exit ) return ExitColor;
		return tier.Kind switch
		{
			TierKind.Room => RoomColor,
			TierKind.Hall => HallColor,
			TierKind.Tower => TowerColor,
			_ => CorridorColor,
		};
	}

	private static Vector2[] CellCorners( int x, int z )
	{
		float c = DungeonBuilder.Cell;
		return new[]
		{
			new Vector2( x * c, z * c ),
			new Vector2( (x + 1) * c, z * c ),
			new Vector2( (x + 1) * c, (z + 1) * c ),
			new Vector2( x * c, (z + 1) * c ),
		};
	}

	private void AddSlab( PieceMesh piece, int x, int z, int level, Color color )
	{
		float top = level * DungeonBuilder.LevelH + 2f;
		AddPrism( piece, CellCorners( x, z ), new[] { top, top, top, top }, top - SlabThickness, color );
	}

	/// <summary>How far a doorway mark stands above the floor slab, so it draws over both slabs and their outlines.</summary>
	private const float DoorLift = 10f;

	/// <summary>
	/// One hidden doorway slab in the given colour: a small block centred on the wall the two cells share.
	/// Blocks.AddDoorsBetweenTiers always orders a tier-to-tier pair so b is one cell east or north of a; the
	/// stair openings come in either order, which AddThreshold handles.
	/// </summary>
	private GameObject MakeDoor( string name, Material material, Material outline, (int x, int z) a, (int x, int z) b, int level, Color color )
	{
		var piece = new PieceMesh();
		AddThreshold( piece, a, b, level, color );
		return MakePiece( name, material, outline, piece, false, level );
	}

	/// <summary>
	/// The threshold block on the wall between two neighbouring cells, standing on `level`'s floor. Either order
	/// works: `along` is `across` turned a quarter, so the footprint keeps its winding whichever way it faces.
	/// </summary>
	private void AddThreshold( PieceMesh piece, (int x, int z) a, (int x, int z) b, int level, Color color )
	{
		float c = DungeonBuilder.Cell;
		var mid = new Vector2( (a.x + b.x) * 0.5f + 0.5f, (a.z + b.z) * 0.5f + 0.5f ) * c;
		var across = new Vector2( b.x - a.x, b.z - a.z );      // crosses the wall
		var along = new Vector2( -across.y, across.x );        // runs along it
		var halfAcross = across * (DoorDepth * 0.5f * c);
		var halfAlong = along * (DoorWidth * 0.5f * c);
		var foot = new[]
		{
			mid - halfAcross - halfAlong,
			mid + halfAcross - halfAlong,
			mid + halfAcross + halfAlong,
			mid - halfAcross + halfAlong,
		};
		float top = level * DungeonBuilder.LevelH + 2f + DoorLift;
		// no outline hull: a black ring would swallow a mark this small
		AddPrism( piece, foot, new[] { top, top, top, top }, top - DoorLift - 6f, color, outlined: false );
	}

	/// <summary>A stair cell: the slab climbs one level across the cell in the direction the stair rises.</summary>
	private void AddRamp( PieceMesh piece, int x, int z, int level, Dir rise, Color color )
	{
		var corners = CellCorners( x, z );
		float baseZ = level * DungeonBuilder.LevelH + 2f;
		var tops = new float[4];
		float c = DungeonBuilder.Cell;
		for ( int i = 0; i < 4; i++ )
		{
			// -0.5 at the low edge, +0.5 at the high edge
			float along = rise.Dx() * (corners[i].x / c - x - 0.5f) + rise.Dz() * (corners[i].y / c - z - 0.5f);
			tops[i] = baseZ + (along + 0.5f) * DungeonBuilder.LevelH;
		}
		AddPrism( piece, corners, tops, baseZ - SlabThickness, color );
	}

	/// <summary>
	/// A convex (or tip-star-shaped) footprint extruded from bottomZ up to a per-corner top height, plus
	/// the same shape grown by OutlineWidth on every side for the inverted-hull outline.
	/// </summary>
	private void AddPrism( PieceMesh piece, Vector2[] foot, float[] topZ, float bottomZ, Color color, bool outlined = true )
	{
		AddPrismGeometry( piece.Body, piece.BodyIdx, foot, topZ, bottomZ, color.ToColor32() );
		if ( !outlined || OutlineWidth <= 0f ) return;

		int n = foot.Length;
		var centroid = Vector2.Zero;
		foreach ( var f in foot ) centroid += f;
		centroid /= n;
		var grown = new Vector2[n];
		var grownTop = new float[n];
		for ( int i = 0; i < n; i++ )
		{
			var dir = foot[i] - centroid;
			// pushed along the corner diagonal so an axis-aligned square grows by OutlineWidth per side
			grown[i] = foot[i] + (dir.Length > 0.001f ? dir.Normal : Vector2.Zero) * OutlineWidth * 1.4142f;
			// sideways and down only: a hull that rose above the top would poke out over the neighbouring
			// cell's top face and draw a seam between every two cells of one room
			grownTop[i] = topZ[i] - 1f;
		}
		AddPrismGeometry( piece.Hull, piece.HullIdx, grown, grownTop, bottomZ - OutlineWidth, Color.Black.ToColor32(), FlipOutlineWinding );
	}

	/// <summary>
	/// Closed prism with every face wound the same way (counter-clockwise seen from outside, for a
	/// counter-clockwise footprint). The body material is two-sided so it never cares; the outline hull
	/// is front-culled and does.
	/// Every vertex carries a real outward normal. The shader used to recover one from screen derivatives
	/// instead, which was fine while the map was opaque but is not good enough now: a slab is 24 units
	/// thick and a map pixel is about 21 units across, so the side walls are about ONE pixel tall and a
	/// derivative quad over them straddles two faces. That gave a garbage normal, which made both the
	/// toon tone and the see-through pass's "is this a wall" test flicker as the map turned.
	/// </summary>
	private static void AddPrismGeometry( List<Vertex> verts, List<int> idx, Vector2[] foot, float[] topZ, float bottomZ, Color32 color, bool flip = false )
	{
		int n = foot.Length;

		void Tri( int a, int b, int c )
		{
			idx.Add( a );
			if ( flip ) { idx.Add( c ); idx.Add( b ); }
			else { idx.Add( b ); idx.Add( c ); }
		}

		Vector3 At( int i, float z ) => new( foot[i].x, foot[i].y, z );

		// the top is one plane whether it is flat or a stair ramp, so take it off the first triangle
		var topN = Vector3.Cross( At( 1, topZ[1] ) - At( 0, topZ[0] ), At( 2, topZ[2] ) - At( 0, topZ[0] ) );
		topN = topN.Length > 0.001f ? topN.Normal : Vector3.Up;
		if ( topN.z < 0f ) topN = -topN;

		int top = verts.Count;
		for ( int i = 0; i < n; i++ ) verts.Add( new Vertex( At( i, topZ[i] ), color ) { Normal = topN } );
		for ( int i = 1; i + 1 < n; i++ ) Tri( top, top + i, top + i + 1 );

		int bottom = verts.Count;
		for ( int i = 0; i < n; i++ ) verts.Add( new Vertex( At( i, bottomZ ), color ) { Normal = Vector3.Down } );
		for ( int i = 1; i + 1 < n; i++ ) Tri( bottom, bottom + i + 1, bottom + i );

		for ( int i = 0; i < n; i++ )
		{
			int j = (i + 1) % n;
			int s = verts.Count;
			// outward for a counter-clockwise footprint: turn the edge right
			var d = foot[j] - foot[i];
			var sideN = new Vector3( d.y, -d.x, 0f );
			sideN = sideN.Length > 0.001f ? sideN.Normal : Vector3.Up;
			verts.Add( new Vertex( At( i, topZ[i] ), color ) { Normal = sideN } );   // s: i top
			verts.Add( new Vertex( At( i, bottomZ ), color ) { Normal = sideN } );   // s+1: i bottom
			verts.Add( new Vertex( At( j, bottomZ ), color ) { Normal = sideN } );   // s+2: j bottom
			verts.Add( new Vertex( At( j, topZ[j] ), color ) { Normal = sideN } );   // s+3: j top
			Tri( s, s + 1, s + 2 );
			Tri( s, s + 2, s + 3 );
		}
	}

	private GameObject MakePiece( string name, Material material, Material outline, PieceMesh piece, bool enabled, int level )
	{
		var builder = Model.Builder;

		var body = new Mesh( material );
		body.CreateVertexBuffer( piece.Body.Count, piece.Body );
		body.CreateIndexBuffer( piece.BodyIdx.Count, piece.BodyIdx );
		body.Bounds = BBox.FromPoints( piece.Body.Select( v => v.Position ) );
		builder.AddMesh( body );

		if ( piece.Hull.Count > 0 )
		{
			var hull = new Mesh( outline );
			hull.CreateVertexBuffer( piece.Hull.Count, piece.Hull );
			hull.CreateIndexBuffer( piece.HullIdx.Count, piece.HullIdx );
			hull.Bounds = BBox.FromPoints( piece.Hull.Select( v => v.Position ) );
			builder.AddMesh( hull );
		}

		var go = new GameObject( _root, enabled, name ) { Flags = GameObjectFlags.NotSaved };
		go.Tags.Add( Tag );
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = builder.Create();
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_pieces.Add( (renderer, level) );
		return go;
	}
}
