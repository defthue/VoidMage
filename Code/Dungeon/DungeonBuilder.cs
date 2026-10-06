using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// Generates a floor from (archetype, seed, size) and spawns the Synty DungeonPack models for it as
/// children of this object. Everything visual is derived from the grid; all pivot/yaw knowledge about
/// the kit pieces lives in the small tables at the bottom so it can be corrected in one place.
///
/// Grid -> world: grid X = world X, grid Z = world Y, level = world Z. One cell is 5 m, one level 2.5 m.
/// </summary>
[Title( "Dungeon Builder" ), Category( "Void Mage" ), Icon( "castle" )]
public sealed partial class DungeonBuilder : Component
{
	[Property] public Archetype Archetype { get; set; } = Archetype.Warren;
	[Property] public int Seed { get; set; } = 1;
	[Property, Range( 0.5f, 3f )] public float Size { get; set; } = 1.5f;
	/// <summary>How many tiers the floor has, independently of Size (which only stretches the box): 1 is the classic
	/// floor; the run ramps this from 0.6 on floor 1 to 1.6 on floor 12 (Grammar.Generate).</summary>
	[Property, Range( 0.5f, 2f )] public float Rooms { get; set; } = 1f;

	/// <summary>Build the floor when the game starts (the editor buttons work regardless).</summary>
	[Property] public bool BuildOnStart { get; set; } = true;

	/// <summary>Drop a player on the spawn tier when the game starts.</summary>
	[Property] public bool SpawnPlayer { get; set; } = true;

	/// <summary>The player prefab to clone (prefabs/player.prefab). Without one a bare PlayerController is used.</summary>
	[Property] public GameObject PlayerPrefab { get; set; }

	/// <summary>Coloured point lights on tagged tiers: green spawn, purple exit, blue shrine, red ambush, yellow loot.</summary>
	[Property] public bool ShowTagLights { get; set; } = false;

	/// <summary>Dress the rooms: roles, props, torches, the secret room. Off gives the bare layout.</summary>
	[Property, Group( "Decoration" )] public bool Decorate { get; set; } = true;

	/// <summary>Floor number in a run (1 = first). Deeper floors roll darker roles and more decay (saturating at 5); 11+ is the forge-hold.</summary>
	[Property, Group( "Decoration" ), Range( 1, 20 )] public int Depth { get; set; } = 1;

	/// <summary>Most point lights on the floor; torches past the budget are placed unlit. Only the ones near the player are awake at any time.</summary>
	[Property, Group( "Decoration" ), Range( 0, 256 )] public int LightBudget { get; set; } = 104;

	/// <summary>Synty tile = 5 m, wall = 5 m tall = two levels. s&amp;box units are inches.</summary>
	public const float Cell = 196.85f;
	public const float LevelH = 98.425f;

	/// <summary>Skeletons in the ambush room (they stand still until they see you). Host only.</summary>
	[Property, Group( "Enemies" ), Range( 0, 6 )] public int SkeletonsPerAmbush { get; set; } = 4;
	/// <summary>Chance a room or hall (not the spawn, ambush or secret tier) holds a group of RoomGroupMin..RoomGroupMax.</summary>
	[Property, Group( "Enemies" ), Range( 0f, 1f )] public float RoomSkeletonChance { get; set; } = 0.85f;
	[Property, Group( "Enemies" ), Range( 1, 5 )] public int RoomGroupMin { get; set; } = 2;
	[Property, Group( "Enemies" ), Range( 1, 5 )] public int RoomGroupMax { get; set; } = 4;
	/// <summary>Chance a corridor holds one skeleton. The corridors were the empty half of the floor at 0.4 (user, 2026-09-17).</summary>
	[Property, Group( "Enemies" ), Range( 0f, 1f )] public float CorridorSkeletonChance { get; set; } = 0.8f;
	/// <summary>No skeletons on tiers whose centre is within this many cells of the player spawn: a quiet start.</summary>
	[Property, Group( "Enemies" ), Range( 0, 20 )] public int SpawnClearCells { get; set; } = 3;
	/// <summary>No skeletons on tiers whose centre is within this many cells of the exit tier: the portal room is a safe zone.</summary>
	[Property, Group( "Enemies" ), Range( 0, 20 )] public int ExitClearCells { get; set; } = 3;
	/// <summary>
	/// Cells a group tries to keep between its bodies inside one room: without it every body in a pack stood on the
	/// room's walking line in one pile, which is what made a floor read as empty rooms and then a wall of enemies.
	/// The gap relaxes when the room is too small to honour it.
	/// </summary>
	[Property, Group( "Enemies" ), Range( 0, 5 )] public int SpreadCells { get; set; } = 3;
	/// <summary>
	/// Bodies one 5 m cell may stand (1-3). At one per cell a floor's head count was capped by its free cells, about
	/// a third of the grid once the decor has taken its share: gentest's ramp put the smaller floors of 2026-09-19 at
	/// room for 30-60 against targets of 80-120. A room still fills one body per cell in its spread order first;
	/// only a pack larger than the room's free cells walks the cells again at <see cref="CellSlots"/>' offsets.
	/// </summary>
	[Property, Group( "Enemies" ), Range( 1, 3 )] public int EnemiesPerCell { get; set; } = 3;
	/// <summary>Where in its cell body 1, 2 and 3 stands (fractions of a cell from the centre): 1.9 m apart on the diagonal.</summary>
	private static readonly Vector2[] CellSlots = { new( 0f, 0f ), new( 0.27f, 0.27f ), new( -0.27f, -0.27f ) };
	/// <summary>Cap on the floor's skeletons (ambush rooms are filled first). 0 disables all spawning.</summary>
	[Property, Group( "Enemies" ), Range( 0, 300 )] public int MaxSkeletons { get; set; } = 80;
	/// <summary>
	/// The head count the floor should carry (critters not counted). 0 = the chance rolls above as they fall.
	/// Above 0 the rolled plan is scaled up to it (groups grow to their room's cell count) and rooms that rolled
	/// empty get a pack until it is met; the run sets this per floor (RunFloors, 101 on floor 1 to 230 on floor 4).
	/// </summary>
	[Property, Group( "Enemies" ), Range( 0, 300 )] public int TargetSkeletons { get; set; } = 0;
	/// <summary>
	/// The FULL skeleton tiers: every kind that exists at each strength, which is what the `skeleton_spawn
	/// base|light|heavy|soldier` console aliases roll from. Which of them a given floor actually uses, and how
	/// often, is the biome's business (<see cref="BiomeEnemies"/>, user's roster 2026-09-17).
	/// Base: the slave's stats (the Fortress corpse has no shield and bleeds, the Dark Fantasy skeleton and flesh
	/// skeleton are skins). Light armor: the soldier's stats (DamageScale 1.5, GuardPoints 3, soldier_* brains).
	/// Heavy armor: a little stronger (1.7, 4, heavy_* brains).
	/// </summary>
	public static readonly string[] BaseSkeletonKinds = { "slave", "df_skeleton", "df_flesh", "corpse" };
	public static readonly string[] LightArmorKinds = { "soldier_01", "df_light", "realms_01", "realms_02", "realms_03" };
	public static readonly string[] HeavyArmorKinds = { "soldier_02", "df_heavy" };
	/// <summary>The swordsman's two skins (tools/gen_swordsman_prefab.py): one enemy, one brain, picked at random per body.</summary>
	public static readonly string[] SwordsmanKinds = { "spirit_demon", "dark_lord" };

	/// <summary>Who this floor's theme fields, and in what mix (<see cref="BiomeEnemies"/>).</summary>
	public BiomeEnemies.Roster Roster => BiomeEnemies.For( Theme );

	public DungeonLayout Layout { get; private set; }

	/// <summary>Where the player is dropped on this floor (world space): computed on every Generate whether or not
	/// this builder spawns the player itself (the run loop keeps its own player and teleports it here).</summary>
	public Vector3? PlayerSpawnWorld { get; private set; }

	/// <summary>The exit portal's object (the kit's exit prop carrying a RunPortal), null until built.</summary>
	public GameObject ExitPortal { get; private set; }

	/// <summary>Counts Generate calls so a floor's in-flight async work (the navmesh bake) can tell it has been superseded.</summary>
	private int _generation;

	private readonly Dictionary<string, Model> _models = new();

	protected override void OnStart()
	{
		if ( BuildOnStart && Game.IsPlaying ) Generate();
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying || Scene.NavMesh is null ) return;
		if ( Networking.IsHost ) TickRespawn();
		// `skeleton_navdraw 1`: the engine's own mesh drawing only works from the editor camera, so in game the
		// overlay draws its own probes (a green/red sphere where the navmesh is closest to the player, the
		// chunks that were built, every skeleton's move line and state label).
		var draw = VoidMage.Npc.SkeletonSpawner.NavDraw;
		if ( Scene.NavMesh.DrawMesh != draw ) Scene.NavMesh.DrawMesh = draw;
		if ( draw ) DrawNavOverlay();
		// a late tile: keep probing once a second until the spawn has polygons under it
		if ( !NavMeshReady && PlayerSpawnWorld.HasValue && Time.Now > _nextReadyProbe )
		{
			_nextReadyProbe = Time.Now + 1f;
			NavMeshReady = Scene.NavMesh.GetClosestPoint( PlayerSpawnWorld.Value, 300f ).HasValue;
		}
	}

	private float _nextReadyProbe;

	private void DrawNavOverlay()
	{
		var overlay = Scene.DebugOverlay;
		var player = Scene.GetAllComponents<VoidMage.Character.PlayerHealth>().FirstOrDefault( p => !p.IsProxy );
		if ( player.IsValid() )
		{
			// probed 120 u ahead of the player, where a first-person camera can see it
			var camera = Scene.Camera;
			var ahead = camera.IsValid() ? camera.WorldRotation.Forward.WithZ( 0f ).Normal : Vector3.Forward;
			var at = player.WorldPosition + ahead * 120f;
			var probe = Scene.NavMesh.GetClosestPoint( at, 300f );
			overlay.Sphere( new Sphere( probe ?? at + Vector3.Up * 20f, 8f ), probe.HasValue ? Color.Green : Color.Red );
			overlay.Text( at + Vector3.Up * 50f, probe.HasValue ? $"navmesh {probe.Value.Distance( at ):0} u" : "navmesh MISS", 14f, color: probe.HasValue ? Color.Green : Color.Red );
		}
		if ( Time.Now < _navChunksDrawnUntil )
		{
			foreach ( var b in _navChunks ) overlay.Box( b, Color.Cyan.WithAlpha( 0.4f ) );
		}
		foreach ( var npc in Scene.GetAllComponents<VoidMage.Npc.SkeletonNpc>() )
		{
			var head = npc.WorldPosition + Vector3.Up * 80f;
			if ( npc.MoveTarget.HasValue ) overlay.Line( npc.WorldPosition + Vector3.Up * 20f, npc.MoveTarget.Value + Vector3.Up * 20f, npc.SteeringStraight ? Color.Orange : Color.Yellow );
			var near = Scene.NavMesh.GetClosestPoint( npc.WorldPosition, 300f );
			overlay.Sphere( new Sphere( near ?? npc.WorldPosition + Vector3.Up * 4f, 6f ), near.HasValue ? Color.Green : Color.Red );
			overlay.Text( head, npc.DebugLabel(), 12f, color: Color.White );
		}
	}

	[Button( "Generate", "casino" )]
	public void Generate()
	{
		Clear();
		_generation++;
		Layout = DungeonGenerator.Generate( Archetype, Seed, Size, Depth, LightBudget, Decorate, traps: TrapBudgetFor(),
			theme: Theme, minBreaches: BreachesMin, maxBreaches: BreachesMax, greenery: Greenery, roots: Roots, rooms: Rooms );
		Log.Info( "[Dungeon] " + Layout.Stats() );
		if ( Layout.Decor != null ) Log.Info( "[Dungeon] decor " + Layout.Decor.Stats() );
		Build( Layout );
		LogSkin();
		EnsureExitPortal( Layout );
		if ( ShowTagLights ) BuildTagLights( Layout );
		PlayerSpawnWorld = SpawnPointFor( Layout );
		if ( SpawnPlayer && Game.IsPlaying ) SpawnPlayerAt( Layout );
		if ( Game.IsPlaying )
		{
			EnsureHaze();
			GameObject.Components.GetOrCreate<VoidMage.UI.DungeonMinimap>().Rebuild( Layout );
			NavMeshReady = false;
			_ = BuildNavMeshAsync( LayoutBounds( Layout ), 0, _generation );
			if ( Networking.IsHost && MaxSkeletons > 0 ) _ = SpawnSkeletonsAsync( Layout );
		}
	}

	/// <summary>Every tier of the built floor (the director's teleport list); empty without a layout.</summary>
	public IEnumerable<Tier> Tiers => Layout?.Grid?.Tiers ?? Enumerable.Empty<Tier>();

	/// <summary>A tier's middle in world space (its centroid cell, on its level), the way the exit is found.</summary>
	public Vector3 TierWorld( Tier t )
	{
		var (cx, cz) = t.Centroid();
		return WorldTransform.PointToWorld( W( (float)cx + 0.5f, (float)cz + 0.5f, t.Level ) );
	}

	/// <summary>The exit tier's centre, world space (the cell nearest its centroid), or null without an exit tier.</summary>
	public Vector3? ExitWorld
	{
		get
		{
			if ( ExitPortal.IsValid() ) return ExitPortal.WorldPosition;
			var exit = Layout?.Find( TierTag.Exit );
			if ( exit is null ) return null;
			var (cx, cz) = exit.Centroid();
			return WorldTransform.PointToWorld( W( (float)cx + 0.5f, (float)cz + 0.5f, exit.Level ) );
		}
	}

	/// <summary>
	/// Every kit names an exit prop and Decor.PlaceExitProp stands it in the exit room, where BuildProps hands it
	/// its RunPortal. When the room had no free centre block (or a layout has no exit tier at all) nothing was
	/// placed, and a floor without a way out is a run-ending bug, so this stands the portal on the exit tier's
	/// centre cell regardless, on top of whatever decor is there.
	/// </summary>
	private void EnsureExitPortal( DungeonLayout layout )
	{
		if ( ExitPortal.IsValid() ) return;
		var exit = layout.Find( TierTag.Exit ) ?? layout.Layout?.Spine.LastOrDefault( t => t.Cells.Count > 0 );
		if ( exit is null ) { Log.Warning( "[Dungeon] no exit tier: the floor has no exit portal" ); return; }
		var model = _kit.Props.TryGetValue( _kit.ExitProp ?? "portal", out var def ) ? def.Models[0] : "darkfortress/sm_prop_portal_01.vmdl";
		var (cx, cz) = exit.Centroid();
		var cell = exit.Sorted().OrderBy( c => Math.Abs( c.x - cx ) + Math.Abs( c.z - cz ) ).First();
		float top = _floorTop.TryGetValue( (cell.x, cell.z, exit.Level), out var t ) ? t : 0f;
		var go = Spawn( model, W( cell.x + 0.5f, cell.z + 0.5f, exit.Level ) + Vector3.Up * (top + FloorClearance), 0f, "exit portal", ColliderKind.Box, isStatic: false, themed: false );
		MarkExitPortal( go );
		Log.Info( $"[Dungeon] exit portal placed by the builder on tier {exit.Id} (the decor pass found no room for it)" );
	}

	private void MarkExitPortal( GameObject go )
	{
		if ( !go.IsValid() ) return;
		var portal = go.Components.GetOrCreate<VoidMage.Run.RunPortal>();
		portal.Kind = VoidMage.Run.PortalKind.Exit;
		portal.Radius = 45f;   // half the default reach: it opens when you walk into it, not past it (user)
		ExitPortal = go;
	}

	/// <summary>
	/// The dungeon air (DungeonHaze) lives on the scene camera and once got lost when the editor re-saved
	/// the scene over a disk edit. If the camera has none at all, create one with defaults so the haze
	/// can never silently vanish; a deliberately disabled one is left alone.
	/// </summary>
	private void EnsureHaze()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		if ( camera.GameObject.Components.Get<DungeonHaze>( FindMode.EverythingInSelf ) is not null ) return;
		camera.GameObject.Components.Create<DungeonHaze>();
		Log.Info( "[Dungeon] no DungeonHaze on the camera; created one with defaults (add it to the scene to tune it)" );
	}

	/// <summary>True once the navmesh has polygons under the player spawn (skeletons steer straight until then).</summary>
	public bool NavMeshReady { get; private set; }

	private readonly List<BBox> _navChunks = new();
	private float _navChunksDrawnUntil;
	private const float NavChunk = 1024f;
	/// <summary>The navmesh bake's wall clearance (see BuildNavMeshAsync). Set before any tile is generated.</summary>
	private const float NavAgentRadius = 12f;

	/// <summary>World box around every cell of the layout, padded a cell sideways and a level up.</summary>
	private BBox LayoutBounds( DungeonLayout layout )
	{
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		foreach ( var (x, z, l) in layout.Grid.Cells.Keys )
		{
			var p = WorldTransform.PointToWorld( W( x, z, l ) );
			min = Vector3.Min( min, p );
			max = Vector3.Max( max, p );
		}
		return new BBox( min - new Vector3( Cell, Cell, LevelH ), max + new Vector3( Cell * 2f, Cell * 2f, LevelH * 3f ) );
	}

	/// <summary>
	/// The floor is built at runtime, so the scene's (deferred) navmesh knows nothing about it. The
	/// fire-and-forget tile request never produced polygons here, so this does what heden does: awaits
	/// GenerateTiles chunk by chunk (nearest the player first, two in flight), then probes the result and
	/// says so in the log. Static box colliders on floors and model colliders on stairs are the input.
	/// </summary>
	private async System.Threading.Tasks.Task BuildNavMeshAsync( BBox bounds, int attempt, int generation )
	{
		var nav = Scene.NavMesh;
		// no bake at all: the doors and the boarded doorways still have to become solid
		if ( nav is null || bounds.Size.x <= 0f ) { DoorNet.SetSolid( true ); return; }
		var started = Time.Now;
		await GameTask.DelaySeconds( 0.05f );   // let this frame's colliders reach the physics world
		// A rebuild while this floor's bake was in flight (the run loop's next floor): stop here. Finishing would
		// stomp the new floor's nav bounds and solidify ITS doors before its own bake has run through them.
		if ( !Scene.IsValid() || !GameObject.IsValid() || generation != _generation ) return;
		nav.IsEnabled = true;
		// Every tile request is clamped to NavMesh.Bounds, which the scene computed at load from the static
		// bodies present THEN: none, the floor is built here. Without this every chunk collapsed onto one empty
		// tile at the origin (the editor works because it enables the navmesh after Generate).
		nav.CustomBounds = true;
		nav.Bounds = bounds;
		// The bake erodes the walkable floor by ceil(AgentRadius / 4 u voxel) voxels from every wall. At the scene's 16
		// that is 4 voxels a side: a 52 u stone doorway kept a 16-20 u strip, and a 39 u gaol doorway kept ONE voxel
		// or none at all, depending on where the jambs fell on the voxel grid -- a pinched doorway is a partial path,
		// and the chasing skeleton runs to the wall beside the door and stands there. 12 is 3 voxels: every doorway
		// stays connected and the stone ones are wide enough for a body to be shouldered past another. The agents'
		// own Radius (their spacing from each other) is the prefab's and is not touched.
		nav.AgentRadius = NavAgentRadius;

		var origin = PlayerSpawnWorld ?? bounds.Center;
		var chunks = new List<BBox>();
		for ( var x = bounds.Mins.x; x < bounds.Maxs.x; x += NavChunk )
			for ( var y = bounds.Mins.y; y < bounds.Maxs.y; y += NavChunk )
				chunks.Add( new BBox( new Vector3( x, y, bounds.Mins.z ), new Vector3( MathF.Min( x + NavChunk, bounds.Maxs.x ), MathF.Min( y + NavChunk, bounds.Maxs.y ), bounds.Maxs.z ) ) );
		chunks.Sort( ( a, b ) => a.Center.WithZ( 0f ).DistanceSquared( origin.WithZ( 0f ) ).CompareTo( b.Center.WithZ( 0f ).DistanceSquared( origin.WithZ( 0f ) ) ) );

		var failed = 0;
		for ( var i = 0; i < chunks.Count; i += 2 )
		{
			var batch = new List<System.Threading.Tasks.Task>();
			for ( var j = i; j < Math.Min( i + 2, chunks.Count ); j++ )
			{
				var chunk = chunks[j];
				batch.Add( GenerateChunk( nav, chunk ) );
			}
			try { await GameTask.WhenAll( batch ); }   // Task.WhenAll itself is outside the s&box whitelist
			catch ( Exception e ) { failed++; Log.Warning( $"[Dungeon] navmesh chunk failed: {e.Message}" ); }
			if ( !Scene.IsValid() || generation != _generation ) return;
		}
		_navChunks.Clear(); _navChunks.AddRange( chunks ); _navChunksDrawnUntil = Time.Now + 8f;

		var probe = nav.GetClosestPoint( origin, 300f );
		var ambush = Layout?.Grid.Tiers.FirstOrDefault( t => t.Tag == TierTag.Ambush );
		string ambushProbe = "none";
		if ( ambush is not null )
		{
			var (cx, cz) = ambush.Centroid();
			var p = WorldTransform.PointToWorld( W( (float)cx + 0.5f, (float)cz + 0.5f, ambush.Level ) );
			ambushProbe = nav.GetClosestPoint( p, 300f ).HasValue ? "HIT" : "MISS";
		}
		NavMeshReady = probe.HasValue;
		Log.Info( $"[Dungeon] navmesh: {chunks.Count} chunks ({failed} failed) in {Time.Now - started:0.0} s over {nav.Bounds.Mins:0}..{nav.Bounds.Maxs:0}, spawn probe {(probe.HasValue ? "HIT" : "MISS")}, ambush probe {ambushProbe}" );
		if ( !probe.HasValue && attempt == 0 )
		{
			await GameTask.DelaySeconds( 1f );
			if ( Scene.IsValid() && GameObject.IsValid() && generation == _generation ) _ = BuildNavMeshAsync( bounds, 1, generation );
			return;   // the retry bakes again, so the doors stay open to it and it solidifies them itself
		}
		if ( !probe.HasValue )
			Log.Warning( "[Dungeon] navmesh still empty after a retry: skeletons steer in straight lines" );

		// The bake reads the whole physics world, so the doors were spawned with dead colliders and the mesh
		// runs straight through every doorway. Now that it is built they can be solid.
		DoorNet.SetSolid( true );
	}

	private static async System.Threading.Tasks.Task GenerateChunk( Sandbox.Navigation.NavMesh nav, BBox chunk )
	{
		await nav.GenerateTiles( Game.ActiveScene.PhysicsWorld, chunk );
	}

	/// <summary>
	/// Populates the floor: the ambush room gets its group, then every other room, hall and corridor rolls for
	/// one (nothing near the player spawn, nothing in the secret room), up to MaxSkeletons. A skeleton stands on
	/// a cell the decoration kept clear (the room's walking line first, then its approach cells, then any cell
	/// without a blocking floor prop), and the spot is sweep-checked against the built colliders before the
	/// spawn so nobody starts life on a table. Deferred a tick so those colliders exist in the physics world.
	/// </summary>
	private async System.Threading.Tasks.Task SpawnSkeletonsAsync( DungeonLayout layout )
	{
		await GameTask.DelaySeconds( 0.1f );
		if ( !Scene.IsValid() || !GameObject.IsValid() || Layout != layout ) return;

		var rng = new Random( layout.Seed * 7919 + 13 );
		var roster = Roster;
		var spawnTier = layout.Find( TierTag.Spawn );
		var (sx, sz) = spawnTier?.Centroid() ?? (double.NaN, double.NaN);
		// the exit room is a safe zone too (the run's home-or-continue choice is made standing in it), with a
		// smaller quiet ring than the spawn's so the last fight still happens near the way out
		var exitTier = layout.Find( TierTag.Exit );
		var (ex, ez) = exitTier?.Centroid() ?? (double.NaN, double.NaN);

		// what each tier gets: ambush rooms first so the cap never starves them. `monster` empty = skeletons.
		var plan = new List<(Tier tier, int count, bool ambush, string monster, bool knight, bool swordsman)>();
		var eligible = new List<Tier>();
		foreach ( var t in layout.Grid.Tiers.OrderBy( t => t.Tag == TierTag.Ambush ? 0 : 1 ).ThenBy( _ => rng.Next() ) )
		{
			if ( t.Cells.Count == 0 || t.Tag is TierTag.Spawn or TierTag.Secret or TierTag.Exit ) continue;
			var (cx, cz) = t.Centroid();
			if ( !double.IsNaN( sx ) && Math.Abs( cx - sx ) + Math.Abs( cz - sz ) < SpawnClearCells && t.Tag != TierTag.Ambush ) continue;
			if ( !double.IsNaN( ex ) && Math.Abs( cx - ex ) + Math.Abs( cz - ez ) < ExitClearCells && t.Tag != TierTag.Ambush ) continue;
			eligible.Add( t );
			int count;
			var monster = "";
			var knight = false;
			var swordsman = false;
			if ( t.Tag == TierTag.Ambush ) count = SkeletonsPerAmbush;
			else if ( t.Kind == TierKind.Corridor ) count = rng.NextDouble() < CorridorSkeletonChance ? 1 : 0;
			else if ( rng.NextDouble() < roster.KnightChance ) { knight = true; count = 1; }   // the knight comes alone
			else if ( rng.NextDouble() < roster.SwordsmanChance ) { swordsman = true; count = rng.Next( 1, 3 ); }   // the swordsman: alone or a pair
			else if ( rng.NextDouble() < roster.MonsterGroupChance )
			{
				// one species per room, and its own group size: the dragon's is 1-1, so it always comes alone
				var pick = RollMonster( roster, rng );
				if ( pick is not null )
				{
					monster = pick.Name;
					count = rng.Next( pick.GroupMin, pick.GroupMax + 1 );
				}
				else count = rng.Next( Math.Min( RoomGroupMin, RoomGroupMax ), Math.Max( RoomGroupMin, RoomGroupMax ) + 1 );
			}
			else count = rng.NextDouble() < RoomSkeletonChance ? rng.Next( Math.Min( RoomGroupMin, RoomGroupMax ), Math.Max( RoomGroupMin, RoomGroupMax ) + 1 ) : 0;
			if ( count > 0 ) plan.Add( (t, count, t.Tag == TierTag.Ambush, monster, knight, swordsman) );
			// ambient life is independent of the encounter: a room may hold a monster group AND a couple of frogs
			if ( t.Kind != TierKind.Corridor && rng.NextDouble() < roster.CritterChance )
			{
				var critter = RollCritter( rng );
				if ( critter is not null ) plan.Add( (t, rng.Next( critter.GroupMin, critter.GroupMax + 1 ), false, critter.Name, false, false) );
			}
		}
		// cells with a blocking prop standing on the floor are out; the walking line and approach cells are in first
		var occupied = new HashSet<(int x, int z, int l)>();
		if ( layout.Decor != null )
		{
			foreach ( var p in layout.Decor.Props )
				if ( p.Blocks && p.Anchor == AnchorKind.Floor ) occupied.Add( (p.X, p.Z, p.Level) );
			// nobody starts life on a spike plate or under a blade
			foreach ( var c in layout.Decor.TrapCells ) occupied.Add( c );
		}
		var slots = Math.Clamp( EnemiesPerCell, 1, CellSlots.Length );
		int Standing( Tier t ) => t.Cells.Count( c => !occupied.Contains( (c.x, c.z, t.Level) ) ) * slots;
		RaiseToTarget( plan, eligible, rng, Standing );
		// and nothing stands INSIDE a blocking prop: the cell rule above only knows props anchored to the floor, and a
		// bed is anchored to its wall, reaching 2.7 m into a 5 m cell -- the centre slot sat on its foot end and the
		// wall-side slot in its middle, and bodies woke up wedged in beds (user, 2026-09-22)
		_footprints = BlockingFootprints( layout.Decor );
		var walk = layout.Decor?.Walk ?? new HashSet<(int x, int z, int l)>();
		var approach = layout.Decor?.Approach ?? new HashSet<(int x, int z, int l)>();

		int total = 0, ambushCount = 0, roomCount = 0, corridorCount = 0, rejected = 0, inProp = 0, monsterCount = 0, critterCount = 0;
		VoidMage.Npc.SkeletonSpawner.SetFloor( Depth );
		// the floor's scrolls fall at kill counts measured against this (Run.ScrollDrops): anything hostile that is not ambient life
		int bodies = 0;
		foreach ( var (t, wanted, ambush, monster, knight, swordsman) in plan )
		{
			if ( total >= MaxSkeletons ) break;
			var cells = t.Cells.Where( c => !occupied.Contains( (c.x, c.z, t.Level) ) )
				.OrderBy( c => walk.Contains( (c.x, c.z, t.Level) ) ? 0 : approach.Contains( (c.x, c.z, t.Level) ) ? 1 : 2 )
				.ThenBy( _ => rng.Next() ).ToList();
			cells = Spread( cells );
			var placed = 0;
			// one body per cell first, in the spread order; a pack bigger than the room walks the cells again off-centre
			foreach ( var (cell, slot) in Enumerable.Range( 0, slots ).SelectMany( s => cells.Select( c => (c, CellSlots[s]) ) ) )
			{
				if ( placed >= wanted || total >= MaxSkeletons ) break;
				if ( InsideProp( _footprints, cell.x + 0.5f + slot.x, cell.z + 0.5f + slot.y, t.Level ) ) { inProp++; continue; }
				var local = W( cell.x + 0.5f + slot.x, cell.z + 0.5f + slot.y, t.Level );
				var world = WorldTransform.PointToWorld( local );
				if ( !SpawnSpotClear( world, out var floor ) ) { rejected++; continue; }
				// kind, brain, sword and shield are independent rolls (the spawner wraps the weapon indices to the kind's choices);
				// WHICH kinds are on the table is the biome's (BiomeEnemies), and a tier the biome leaves empty is simply never rolled
				var kind = knight ? "undead_knight"
					: swordsman ? SwordsmanKinds[rng.Next( SwordsmanKinds.Length )]
					: monster.Length > 0 ? "monster_" + monster
					: rng.NextDouble() < roster.SoulChance ? "tormented_soul"
					: ArmorRoll( roster, rng ) is { } armored ? armored
					: BiomeEnemies.OneOf( roster.Base, BaseSkeletonKinds, rng );
				// a monster, the knight and the swordsman have exactly one brain of their own; only the skeletons roll a personality
				var brain = knight || swordsman || monster.Length > 0 ? "" : rng.Next( 2 ) == 0 ? "aggressive" : "defensive";
				var go = VoidMage.Npc.SkeletonSpawner.Spawn( Scene, floor + Vector3.Up * 4f, Rotation.FromYaw( rng.Next( 360 ) ), GameObject, kind, brain, rng.Next( 2 ), rng.Next( 2 ), ambush && monster.Length == 0 );
				if ( !go.IsValid() ) continue;
				placed++; total++;
				if ( !IsCritter( monster ) ) bodies++;
				if ( monster.Length > 0 )
				{
					if ( VoidMage.Npc.MonsterKinds.Find( monster )?.Critter == true ) critterCount++; else monsterCount++;
				}
				else if ( ambush ) ambushCount++;
				else if ( t.Kind == TierKind.Corridor ) corridorCount++;
				else roomCount++;
			}
		}
		VoidMage.Run.ScrollDrops.BeginFloor( bodies, rng );
		ArmRespawn( eligible, occupied, total - critterCount );
		Log.Info( $"[Dungeon] spawned {total} over {plan.Count} tiers: skeletons {ambushCount} ambush / {roomCount} rooms / {corridorCount} corridors, monsters {monsterCount}, critters {critterCount}, {rejected} spots rejected (occupied), {inProp} inside props" );
	}

	private static bool IsCritter( string monster ) => monster.Length > 0 && VoidMage.Npc.MonsterKinds.Find( monster )?.Critter == true;

	/// <summary>
	/// An armored kind for this biome, or null for a base one. A biome with no armor at all (Stone) always
	/// returns null, and one with no heavy tier (Mossy) never rolls heavy however the share falls.
	/// </summary>
	private static string ArmorRoll( BiomeEnemies.Roster roster, Random rng )
	{
		var light = roster.Light is { Length: > 0 };
		var heavy = roster.Heavy is { Length: > 0 };
		if ( !light && !heavy ) return null;
		if ( rng.NextDouble() >= roster.SoldierChance ) return null;
		var takeHeavy = heavy && (!light || rng.NextDouble() < roster.HeavyShare);
		return takeHeavy ? roster.Heavy[rng.Next( roster.Heavy.Length )] : roster.Light[rng.Next( roster.Light.Length )];
	}

	/// <summary>
	/// The same cells, reordered so a pack fills the room rather than piling onto its walking line: each next cell
	/// is the one furthest from those already taken (capped at <see cref="SpreadCells"/>), ties going to whatever
	/// the walk/approach ranking preferred. A room too small to honour the gap simply runs out of distance and
	/// falls back to that ranking. No RNG is consumed, so a seed still rebuilds the same floor.
	/// </summary>
	private List<(int x, int z)> Spread( List<(int x, int z)> ranked )
	{
		if ( SpreadCells <= 0 || ranked.Count < 3 ) return ranked;
		var pool = new List<(int x, int z)>( ranked );
		var order = new List<(int x, int z)>( ranked.Count );
		while ( pool.Count > 0 )
		{
			var best = 0;
			var bestScore = int.MinValue;
			for ( var i = 0; i < pool.Count; i++ )
			{
				var gap = SpreadCells;
				foreach ( var taken in order )
				{
					var d = Math.Abs( taken.x - pool[i].x ) + Math.Abs( taken.z - pool[i].z );
					if ( d < gap ) gap = d;
				}
				// distance first, then the original ranking (earlier is better, hence -i)
				var score = gap * 1000 - i;
				if ( score > bestScore ) { bestScore = score; best = i; }
			}
			order.Add( pool[best] );
			pool.RemoveAt( best );
		}
		return order;
	}

	/// <summary>
	/// TargetSkeletons: the rolled plan brought up to the head count the run asked for, SPREAD OVER THE WHOLE FLOOR
	/// (user, 2026-09-20: "i now get empty rooms sometimes and than find a room with like 8 enemys"). It used to
	/// multiply the groups that had rolled, so a tier that rolled nothing stayed empty while its neighbour grew
	/// fivefold. Now every eligible tier that rolled empty gets a pack first, and what is still missing is dealt out
	/// one body at a time to whichever tier is emptiest for its size (bodies per free cell), never past what its free
	/// cells can stand, the knight always alone, a monster room never past twice its species' own group. Critters are
	/// ambient life and count for nothing. Same rng stream, so a seed still gives the same floor.
	/// </summary>
	private void RaiseToTarget( List<(Tier tier, int count, bool ambush, string monster, bool knight, bool swordsman)> plan, List<Tier> eligible, Random rng, Func<Tier, int> standing )
	{
		if ( TargetSkeletons <= 0 ) return;
		int Have() => plan.Where( p => !IsCritter( p.monster ) ).Sum( p => p.count );

		// nobody's room stands empty: a pack in every room, a body in every other corridor
		var used = plan.Where( p => !IsCritter( p.monster ) ).Select( p => p.tier ).ToHashSet();
		foreach ( var t in eligible.Where( t => !used.Contains( t ) ).OrderBy( t => t.Kind == TierKind.Corridor ? 1 : 0 ).ThenBy( _ => rng.Next() ) )
		{
			if ( Have() >= TargetSkeletons ) break;
			if ( standing( t ) <= 0 ) continue;
			int n = t.Kind == TierKind.Corridor ? rng.Next( 2 ) : rng.Next( Math.Min( RoomGroupMin, RoomGroupMax ), Math.Max( RoomGroupMin, RoomGroupMax ) + 1 );
			n = Math.Min( n, standing( t ) );
			if ( n > 0 ) plan.Add( (t, n, false, "", false, false) );
		}

		// the rest, one at a time, to the tier with the fewest bodies per standing place
		int Cap( (Tier tier, int count, bool ambush, string monster, bool knight, bool swordsman) p )
		{
			if ( p.knight || IsCritter( p.monster ) ) return p.count;
			int cap = Math.Max( p.count, standing( p.tier ) );
			if ( p.monster.Length > 0 && VoidMage.Npc.MonsterKinds.Find( p.monster ) is { } kind ) cap = Math.Min( cap, Math.Max( p.count, kind.GroupMax * 2 ) );
			if ( p.swordsman ) cap = Math.Min( cap, Math.Max( p.count, 3 ) );
			return cap;
		}
		int have = Have();
		while ( have < TargetSkeletons )
		{
			int best = -1;
			double bestFill = double.MaxValue;
			for ( int i = 0; i < plan.Count; i++ )
			{
				var p = plan[i];
				if ( p.count >= Cap( p ) ) continue;
				double fill = (p.count + rng.NextDouble() * 0.5) / Math.Max( 1, standing( p.tier ) );
				if ( fill < bestFill ) { bestFill = fill; best = i; }
			}
			if ( best < 0 ) break;
			var b = plan[best];
			plan[best] = (b.tier, b.count + 1, b.ambush, b.monster, b.knight, b.swordsman);
			have++;
		}
		if ( have < TargetSkeletons ) Log.Info( $"[Dungeon] target {TargetSkeletons} skeletons, the floor only has room for {have}" );
	}

	/// <summary>
	/// A hostile monster kind from the ones this biome keeps (BiomeEnemies.Monsters), by weight
	/// (MonsterKinds.Generated.cs). Uses the layout's own rng, so a seed still rebuilds the same floor.
	/// Null when the biome fields no monsters at all: the room falls back to a skeleton pack.
	/// </summary>
	private static VoidMage.Npc.MonsterKinds.Entry RollMonster( BiomeEnemies.Roster roster, Random rng )
	{
		var pool = VoidMage.Npc.MonsterKinds.All
			.Where( m => !m.Critter && roster.Monsters.Contains( m.Name ) ).ToList();
		if ( pool.Count == 0 ) return null;
		var total = pool.Sum( m => m.Weight );
		var roll = rng.NextDouble() * total;
		foreach ( var m in pool )
		{
			roll -= m.Weight;
			if ( roll <= 0 ) return m;
		}
		return pool[^1];
	}

	private static VoidMage.Npc.MonsterKinds.Entry RollCritter( Random rng )
	{
		var pool = VoidMage.Npc.MonsterKinds.All.Where( m => m.Critter ).ToList();
		return pool.Count == 0 ? null : pool[rng.Next( pool.Count )];
	}

	private static readonly string[] SpawnSweepIgnore = { "player", "npc", "enemy", "viewmodel", "posesource", "spellfire", "trigger" };

	/// <summary>
	/// A skeleton-sized sphere swept down onto the cell: the first thing it meets must be the floor itself (within a
	/// few units of the cell's level), not a table, a crate or a coffin. `floor` is where the feet go.
	/// </summary>
	/// <summary>
	/// The floor space one blocking prop really takes, in cell units: its centre, half extents across its facing
	/// (width) and along it (depth), and its yaw. Built from the catalog's measured extents the same way the decorator
	/// reserves space, because the spawn cell rule only sees props anchored to the FLOOR: a bed hangs off its wall
	/// slot (WallFlush) and reaches 2.7 m into a 5 m cell, so bodies were stood in beds and woke up stuck in them.
	/// </summary>
	private readonly record struct Footprint( float GX, float GZ, int Level, float HalfW, float HalfD, float Yaw );

	/// <summary>Every blocking prop's footprint on the floor being built (the respawn trickle reads it too).</summary>
	private List<Footprint> _footprints = new();

	/// <summary>Clearance a body keeps from a prop's edge, in cells: its capsule (about 15 u) and a hair.</summary>
	private const float PropClearanceCells = 0.1f;

	private List<Footprint> BlockingFootprints( DecorResult decor )
	{
		var list = new List<Footprint>();
		if ( decor is null ) return list;
		foreach ( var p in decor.Props )
		{
			if ( !p.Blocks || p.Anchor is not (AnchorKind.Floor or AnchorKind.WallFlush) ) continue;
			if ( !decor.Kit.Props.TryGetValue( p.Prop, out var def ) ) continue;
			// Width/Depth already carry PropDef.Scale; the placement's Scale multiplies that by the pass's own roll
			float roll = def.Scale > 0f ? (p.Scale > 0f ? p.Scale : 1f) / def.Scale : 1f;
			float hw = def.Width * roll / 10f, hd = def.Depth * roll / 10f;
			float rad = p.Yaw * MathF.PI / 180f;
			float fx = MathF.Cos( rad ), fz = MathF.Sin( rad );   // the facing, in grid axes (W maps x to gx, y to gz)
			float gx = p.GX, gz = p.GZ;
			if ( p.Anchor == AnchorKind.WallFlush )
			{
				// exactly BuildProps' push: the back face lands on the wall face, so the box starts WallHalf into the
				// cell (less the embed) and runs Depth into the room, centred S along the wall to the facing's left
				float wallGap = (WallHalf - p.Embed * M) / Cell;
				float lx = -fz, lz = fx;
				gx = p.X + 0.5f + p.Edge.Dx() * 0.5f + fx * (wallGap + hd) + lx * p.S;
				gz = p.Z + 0.5f + p.Edge.Dz() * 0.5f + fz * (wallGap + hd) + lz * p.S;
			}
			list.Add( new Footprint( gx, gz, p.Level, hw, hd, rad ) );
		}
		return list;
	}

	/// <summary>Does a body at (gx, gz) on this level stand inside (or within its own radius of) a blocking prop?</summary>
	private static bool InsideProp( List<Footprint> footprints, float gx, float gz, int level )
	{
		foreach ( var f in footprints )
		{
			if ( f.Level != level ) continue;
			float dx = gx - f.GX, dz = gz - f.GZ;
			float reach = f.HalfW + f.HalfD + PropClearanceCells;
			if ( MathF.Abs( dx ) > reach || MathF.Abs( dz ) > reach ) continue;   // cheap reject, most props are far
			float fx = MathF.Cos( f.Yaw ), fz = MathF.Sin( f.Yaw );
			float along = dx * fx + dz * fz;          // on the facing (depth) axis
			float across = -dx * fz + dz * fx;        // on the left (width) axis
			if ( MathF.Abs( along ) <= f.HalfD + PropClearanceCells && MathF.Abs( across ) <= f.HalfW + PropClearanceCells ) return true;
		}
		return false;
	}

	private bool SpawnSpotClear( Vector3 cellCentre, out Vector3 floor )
	{
		floor = cellCentre;
		var top = cellCentre + Vector3.Up * 100f;
		var bottom = cellCentre - Vector3.Up * 20f;
		var tr = Scene.Trace.Ray( top, bottom ).Radius( 14f ).WithoutTags( SpawnSweepIgnore ).Run();
		if ( !tr.Hit ) return true;   // nothing under it at all: trust the layout (the floor collider may be a frame late)
		if ( tr.HitPosition.z > cellCentre.z + 12f ) return false;
		floor = cellCentre.WithZ( tr.HitPosition.z );
		return true;
	}

	[Button( "Clear", "delete" )]
	public void Clear()
	{
		foreach ( var child in GameObject.Children.ToList() ) child.Destroy();
		ClearRespawn();
		VoidMage.Traps.TrapNet.Clear();
		VoidMage.Traps.VineNet.Clear();
		VoidMage.Combat.SecretWallNet.Clear();
		VoidMage.Combat.BreakableNet.Clear();
		VoidMage.Combat.FleshNet.Clear();
		VoidMage.Combat.BloodMarks.Clear();
		DoorNet.Clear();
		ResetSkin();
		Layout = null;
		ExitPortal = null;
		PlayerSpawnWorld = null;
		NavMeshReady = false;
	}

	// ------------------------------------------------------------------ geometry

	private void Build( DungeonLayout layout )
	{
		var g = layout.Grid;
		_kit = DungeonKit.For( layout.Theme );
		var variant = new Random( layout.Seed );   // cosmetic variety only; never touches the layout RNG
		// before ANY breakable registers: the boarded doorways register in BuildDoorways, the props later, and a
		// clear between the two left the boards out of the registry (the sword only strikes what is in it)
		VoidMage.Combat.BreakableNet.Clear();
		VoidMage.Combat.FleshNet.Clear();
		VoidMage.Combat.BloodMarks.Clear();

		BuildFloors( layout, variant );
		BuildWalls( layout );
		BuildBreaches( layout );
		BuildDoorways( layout, variant );
		BuildCeilings( layout, variant );
		BuildPillars( layout );
		BuildCorners( layout );
		if ( layout.Decor != null ) BuildProps( layout );
		if ( layout.Decor != null ) BuildTraps( layout );
	}

	private void BuildFloors( DungeonLayout layout, Random variant )
	{
		var g = layout.Grid;
		var kit = _kit;
		_floorTop.Clear();
		_kitBounds.Clear();
		float colliderTop = kit.FloorModels.Min( m => KitBounds( m ).Maxs.z );
		var swaps = new Dictionary<(int, int, int), string>();
		if ( layout.Decor != null )
			foreach ( var f in layout.Decor.FloorSwaps ) swaps[(f.X, f.Z, f.Level)] = f.Model;
		foreach ( var (key, c) in g.Cells.OrderBy( kv => kv.Key ) )
		{
			var (x, z, l) = key;
			if ( c.Kind == CellKind.Stair )
			{
				var (px, pz, yaw) = kit.StairMirrorY ? StairPivotMirrored( c.SDir ) : StairPivot( c.SDir );
				Spawn( kit.StairModel, W( x + px, z + pz, l ), yaw, $"stair {x},{z},{l} {c.SDir}" );
				// sloped ceiling over the run: low end at wall height above the bottom step, high end one level up
				if ( kit.StairCeilingFlipped )
				{
					// the stair mesh itself upside down: pivot at the HIGH end at l+3, its stepped face descending 2.5 m
					// toward the low end, where it meets the lower room's ceiling at l+2 and the side wedges
					var (fx, fz, fyaw) = StairCeilingPivotFlipped( c.SDir );
					Spawn( kit.StairCeilingModel, W( x + fx, z + fz, l + 3 ), fyaw + 180f, $"stairceiling {x},{z},{l}", pitch: 180f );
				}
				else
				{
					var (cx, cz, cyaw) = StairCeilingPivot( c.SDir );
					Spawn( kit.StairCeilingModel, W( x + cx, z + cz, l + 2 ), cyaw, $"stairceiling {x},{z},{l}" );
				}
				continue;
			}
			// the variant roll always happens so existing seeds keep their tile pattern when a swap is added
			var floor = c.Kind == CellKind.Landing ? kit.FloorModels[0] : kit.FloorModels[variant.Next( kit.FloorModels.Length )];
			if ( swaps.TryGetValue( key, out var swapped ) ) floor = swapped;
			var go = Spawn( floor, W( x, z, l ), 0, $"floor {x},{z},{l}", ColliderKind.Box );
			// The tiles are 10-18 cm thick, so a box per model would leave up to 8 cm steps between
			// neighbouring tiles and the player would bump over them. Every floor collider tops out at
			// the same height instead (the thinnest tile's surface); the thick tiles swallow the feet by
			// a few cm, which reads far better than feet floating above the thin ones.
			var bounds = KitBounds( floor );
			var box = go.Components.Get<BoxCollider>();
			box.Center = new Vector3( bounds.Center.x, bounds.Center.y, (bounds.Mins.z + colliderTop) / 2f );
			box.Scale = new Vector3( bounds.Size.x, bounds.Size.y, colliderTop - bounds.Mins.z );
			// The walking surface is 10 cm up on every tile (measured: the dominant upward faces); Tiles_01/02
			// carry raised detail to 16-18 cm that is NOT floor, so the bounds top would float props.
			_floorTop[key] = colliderTop;
			// underside of a raised floor (a kit whose slabs have a modelled underside needs nothing here)
			if ( !kit.FloorHasUnderside && l > 0 && !g.Cells.ContainsKey( (x, z, l - 1) ) )
				SpawnCeiling( kit.CeilingModels[0], x, z, l, $"underside {x},{z},{l}", ColliderKind.None );
		}
	}

	/// <summary>
	/// A ceiling slab over cell (x, z) hanging from grid level l. Stone's ceiling pieces are modelled hanging below
	/// their pivot and go in as they are. A kit with no ceiling pieces (Realms) uses its floor slabs upside down:
	/// yaw 180 · pitch 180 mirrors Y and Z about the pivot, so the pivot moves to the (x, z+1) corner to keep the
	/// footprint in the cell, and the piece is lowered by its own depth so it sits in the band just under the plane
	/// with its detailed face pointing down. The box collider is sized from the raw bounds in the object's own
	/// frame, which already carries the flip, so it matches the mesh (see the Spawn doc).
	/// </summary>
	private GameObject SpawnCeiling( string model, int x, int z, int l, string name, ColliderKind collider )
	{
		if ( !_kit.CeilingFlipped ) return Spawn( model, W( x, z, l ), 0, name, collider );
		float depth = -KitBounds( model ).Mins.z;
		return Spawn( model, W( x, z + 1, l ) - Vector3.Up * depth, 180f, name, collider, pitch: 180f );
	}

	/// <summary>
	/// Walls are merged per wall LINE (the plane between two grid vertices) in half-level slabs, so two
	/// cells that both want a wall on the same line (an interior wall, or rooms at offset levels) never
	/// spawn two overlapping meshes. Two consecutive slabs become one 5 m double-sided wall; a lone slab
	/// becomes two single-sided quarter pieces facing whichever cell asked for it. Stairs ask for a full
	/// wall on both sides plus the wedge piece on top that follows the slope.
	/// </summary>
	private void BuildWalls( DungeonLayout layout )
	{
		// also cleared by Clear(), which Generate() runs first; this covers a Build() that did not come through it
		VoidMage.Combat.SecretWallNet.Clear();
		var g = layout.Grid;
		var kit = _kit;
		foreach ( var e in layout.Edges )
		{
			if ( e.Kind == EdgeKind.StairWall )
			{
				var sdir = g.Cells[(e.X, e.Z, e.Level)].SDir;
				var (px, pz, yaw) = StairWallPivot( sdir, e.D );
				float wyaw = yaw + kit.StairWallYawOffset;
				// a borrowed wedge is fitted to the kit's walls: scaled about its pivot and pushed along its own +X so its
				// slab is centred on the grid line (the gaol's stone wedge is 5.05 m long and off-centre: it poked through)
				var wpos = W( e.X + px, e.Z + pz, e.Level + 2 ) + Rotation.FromYaw( wyaw ).Forward * (kit.StairWallNudge * M);
				Vector3? wscale = kit.StairWallScale is { } ws ? new Vector3( ws.x, ws.y, ws.z ) : null;
				Spawn( kit.StairWallModel, wpos, wyaw, $"stairwedge {e.X},{e.Z},{e.Level} {e.D}", scale: wscale );
			}
		}

		var swaps = new Dictionary<WallSegment, WallSwap>();
		if ( layout.Decor != null )
			foreach ( var s in layout.Decor.WallSwaps ) swaps[s.Seg] = s;

		// a kit whose lone-slab piece is the full 5 m gets one per LINE and slab: Walls.Merge emits a Quarter segment
		// per side that asked, and two double-faced 5 m slabs back to back would interpenetrate
		var quarterDone = new HashSet<(WallLine line, int k)>();
		var quarterScale = kit.QuarterWallScale is { } qs ? new Vector3( qs.x, qs.y, qs.z ) : (Vector3?)null;
		foreach ( var seg in Walls.Merge( layout.Edges ) )
		{
			var (px, pz, yaw) = EdgePivot( seg.D );
			var rot = Rotation.FromYaw( yaw );
			// a wall whose thickness is not centred on its pivot is pulled back onto the grid line
			var p = W( seg.CX + px, seg.CZ + pz, seg.K ) - rot.Forward * (kit.WallSetback * M);
			if ( seg.Kind == SegKind.Full )
			{
				var front = (seg.CX, seg.CZ, seg.K);
				var back = (seg.CX + seg.D.Dx(), seg.CZ + seg.D.Dz(), seg.K);
				if ( swaps.TryGetValue( seg, out var swap ) )
				{
					PlaceWallSwap( seg, swap );
					SpawnWallTrim( layout, p, yaw, front, back, swap.Model, $"{seg.CX},{seg.CZ},{seg.K} {seg.D}" );
					continue;
				}
				Spawn( kit.WallModel, p, yaw, $"wall {seg.CX},{seg.CZ},{seg.K} {seg.D}", ColliderKind.Box );
				SpawnWallTrim( layout, p, yaw, front, back, kit.WallModel, $"{seg.CX},{seg.CZ},{seg.K} {seg.D}" );
				continue;
			}
			if ( kit.QuarterFullLength )
			{
				if ( !quarterDone.Add( (seg.Line, seg.K) ) ) continue;
				Spawn( kit.QuarterWallModel, p, yaw, $"halfwall {seg.CX},{seg.CZ},{seg.K} {seg.D}", ColliderKind.Box, quarterScale );
				continue;
			}
			var along = rot.Left * (Cell / 2f);   // local +Y: the direction the piece runs
			Spawn( kit.QuarterWallModel, p, yaw, $"halfwall {seg.CX},{seg.CZ},{seg.K} {seg.D} a", ColliderKind.Box, quarterScale );
			Spawn( kit.QuarterWallModel, p + along, yaw, $"halfwall {seg.CX},{seg.CZ},{seg.K} {seg.D} b", ColliderKind.Box, quarterScale );
		}
	}

	/// <summary>
	/// The kit's moulding along the top of a 5 m wall or doorway, one per face: at the segment's own transform for the
	/// face into `front` and turned round at the far end of the piece for the face into `back`. A face into a room
	/// whose role the kit lists in WallTrimSkipRoles gets none (the gaol's throne room: the moulding crossed the
	/// banners); a segment model in WallTrimSkipModels has no top row for it to sit on. No collider: it is relief.
	/// </summary>
	private void SpawnWallTrim( DungeonLayout layout, Vector3 p, float yaw, (int x, int z, int l) front, (int x, int z, int l) back, string segModel, string where )
	{
		var kit = _kit;
		if ( kit.WallTrimModel == null ) return;
		if ( segModel != null && kit.WallTrimSkipModels != null && kit.WallTrimSkipModels.Contains( segModel ) ) return;
		var rot = Rotation.FromYaw( yaw );
		var up = Vector3.Up * (kit.WallTrimHeight * M);
		if ( !TrimSkipped( layout, front ) ) Spawn( kit.WallTrimModel, p + up, yaw, $"walltrim {where} a", ColliderKind.None );
		if ( !TrimSkipped( layout, back ) ) Spawn( kit.WallTrimModel, p + up + rot.Left * Cell, yaw + 180f, $"walltrim {where} b", ColliderKind.None );
	}

	/// <summary>Whether the room on this side of a wall slab refuses the trim. The slab's level first; a room half a
	/// level up or down shares the slab and is looked for next. No cell, or a stair or landing there: the trim stays.</summary>
	private bool TrimSkipped( DungeonLayout layout, (int x, int z, int l) cell )
	{
		var skip = _kit.WallTrimSkipRoles;
		if ( skip == null || skip.Length == 0 || layout.Decor == null ) return false;
		foreach ( int dl in new[] { 0, 1, -1 } )
		{
			if ( !layout.Grid.Cells.TryGetValue( (cell.x, cell.z, cell.l + dl), out var c ) ) continue;
			if ( c.Tier == null ) return false;
			return layout.Decor.Roles.TryGetValue( c.Tier.Id, out var role ) && Array.IndexOf( skip, role ) >= 0;
		}
		return false;
	}

	/// <summary>
	/// A decorated wall segment: the swap model (alcove, window, secret-room tell) in place of the plain
	/// wall, its insert at the same transform, and for a shared wall a plain single-sided wall facing the
	/// far cell, both nudged apart like the door frames so they never share a plane.
	/// </summary>
	private void PlaceWallSwap( WallSegment seg, WallSwap swap )
	{
		var (px, pz, yaw) = EdgePivot( seg.D );
		var rot = Rotation.FromYaw( yaw );
		var p = W( seg.CX + px, seg.CZ + pz, seg.K );
		if ( swap.Centred ) p += rot.Left * (Cell / 2f);
		if ( swap.BackModel != null ) p += rot.Forward * DoorFrameOffset;
		if ( swap.Setback > 0 ) p -= rot.Forward * (swap.Setback * M);   // pivot inside the recess: pull it back to the wall plane
		// The secret room's tell breaks open, so neither of its two faces may be Static: a static renderer's shadow
		// is baked into a shadowed light's cached pass and would still be standing there after the wall is gone.
		// Whichever face is the tell gets a mesh collider as well, so a bolt aimed into the inset doorway goes off
		// in the recess rather than on a flat box out at the wall plane.
		string tell = _kit.SecretWallModel;
		bool secret = swap.Model == tell || swap.BackModel == tell;
		var collider = swap.MeshCollider ? ColliderKind.Model : ColliderKind.Box;
		// a null Model is a face with nothing on it: a double-faced kit's secret tell standing on the far side's transform
		GameObject front = null;
		if ( swap.Model != null )
			front = Spawn( swap.Model, p, yaw, $"wallswap {seg.CX},{seg.CZ},{seg.K} {seg.D}",
				secret && swap.Model == tell ? ColliderKind.Model : collider, isStatic: !secret );
		if ( swap.Insert != null )
		{
			// the insert has its own pivot convention: the rock plug behind a sealed doorway is centred on the opening
			// while the doorway wall pivots at its end; ignoring that put the plug half a cell off and the opening open
			var ip = p;
			if ( swap.InsertCentred && !swap.Centred ) ip += rot.Left * (Cell / 2f);
			else if ( !swap.InsertCentred && swap.Centred ) ip -= rot.Left * (Cell / 2f);
			Spawn( swap.Insert, ip, yaw, $"wallinsert {seg.CX},{seg.CZ},{seg.K} {seg.D}", ColliderKind.Model );
		}
		if ( swap.Recessed )
		{
			// the recess is a hole in the rock: give it the floor tile and the ceiling slab of the cell behind
			var (rx, rz) = Walls.CellOf( seg.Line, seg.D.Opp() );
			var floor = Spawn( _kit.FloorModels[0], W( rx, rz, seg.K ), 0, $"alcovefloor {rx},{rz},{seg.K}", ColliderKind.Box );
			var fb = KitBounds( _kit.FloorModels[0] );
			var box = floor.Components.Get<BoxCollider>();
			float top = _floorTop.Values.DefaultIfEmpty( fb.Maxs.z ).First();
			box.Center = new Vector3( fb.Center.x, fb.Center.y, (fb.Mins.z + top) / 2f );
			box.Scale = new Vector3( fb.Size.x, fb.Size.y, top - fb.Mins.z );
			SpawnCeiling( _kit.CeilingModels[0], rx, rz, seg.K + 2, $"alcoveceiling {rx},{rz},{seg.K}", ColliderKind.Box );
		}
		if ( swap.BackModel != null )
		{
			var bd = seg.D.Opp();
			var (bx, bz) = Walls.CellOf( seg.Line, bd );
			var (bpx, bpz, byaw) = EdgePivot( bd );
			var bp = W( bx + bpx, bz + bpz, seg.K ) + Rotation.FromYaw( byaw ).Forward * DoorFrameOffset;
			var back = Spawn( swap.BackModel, bp, byaw, $"wallback {bx},{bz},{seg.K} {bd}",
				secret && swap.BackModel == tell ? ColliderKind.Model : ColliderKind.Box, isStatic: !secret );
			if ( secret ) SetUpSecretWall( seg, swap, front, p, yaw, back, bp, byaw );
		}
		else if ( secret ) SetUpSecretWall( seg, swap, front, p, yaw, null, Vector3.Zero, 0f );   // one double-faced tell, nothing behind
	}

	/// <summary>
	/// Arm the secret room's tell. Which of the segment's two faces carries sm_env_wall_05 depends on which cell
	/// Walls.Merge gave the segment to (DecorGlobal picks by hostSide), so the tell is the front piece about half
	/// the time and the back piece the other half; either way it is the face standing in the HOST corridor, and its
	/// own forward points down that corridor.
	///
	/// Both faces get an sm_env_wall_07 twin — the same wall with the doorway as a real opening — in the same plane,
	/// parented under the tell with its renderer off. They are spawned here, enabled, rather than at the moment of
	/// the break: DungeonBuilder.Spawn always creates an enabled object and adds the collider at once, so spawning
	/// them disabled would cook the arch hull, throw it away and cook it again on the break frame. Their mesh
	/// colliders sitting live costs nothing while the solid wall is up (the arch is a subset of it), and it is the
	/// tell's own collider going away that opens the doorway. Parenting also means a bolt whose trace happens to
	/// catch the arch mesh still finds SecretWall by walking its ancestors.
	/// </summary>
	private void SetUpSecretWall( WallSegment seg, WallSwap swap, GameObject front, Vector3 fp, float fyaw, GameObject back, Vector3 bp, float byaw )
	{
		bool frontIsTell = swap.Model == _kit.SecretWallModel;
		var tell = frontIsTell ? front : back;
		var other = frontIsTell ? back : front;   // null for a double-faced kit: one tell, one arch
		if ( !tell.IsValid() ) return;
		var (tp, tyaw) = frontIsTell ? (fp, fyaw) : (bp, byaw);
		var (op, oyaw) = frontIsTell ? (bp, byaw) : (fp, fyaw);

		var arch = SpawnArch( tp, tyaw, $"wallarch {seg.CX},{seg.CZ},{seg.K} {seg.D}", tell );
		var archBack = other.IsValid() ? SpawnArch( op, oyaw, $"wallarchback {seg.CX},{seg.CZ},{seg.K} {seg.D.Opp()}", tell ) : null;

		var wall = tell.Components.Create<VoidMage.Combat.SecretWall>();
		wall.Back = other;
		wall.Arch = arch;
		wall.ArchBack = archBack;
		wall.Seed = DungeonRng.Mix( Layout?.Seed ?? 0, 0x5EC0 );
		// the opening the arch has and the tell's front face, per kit, BEFORE registering: a late joiner may be
		// told to break it on registration, and that reads the opening
		wall.OpenAlongMin = _kit.SecretOpenAlongMin;
		wall.OpenAlongMax = _kit.SecretOpenAlongMax;
		wall.OpenTop = _kit.SecretOpenTop;
		wall.FaceForward = _kit.SecretFaceForward;
		wall.RubbleModels = _kit.SecretRubble;
		VoidMage.Combat.SecretWallNet.Register( wall );
	}

	private GameObject SpawnArch( Vector3 pos, float yaw, string name, GameObject parent )
	{
		var go = Spawn( _kit.BrokenSecretWallModel, pos, yaw, name, ColliderKind.Model, isStatic: false );
		if ( go.Components.Get<ModelRenderer>() is { } mr ) mr.Enabled = false;
		go.SetParent( parent, true );
		return go;
	}

	/// <summary>
	/// The doorway frames are single-sided, so each opening gets the same frame twice, back to back,
	/// nudged toward its own room so the two never share a plane. Every opening is centred in all
	/// five frames, so the mirrored copy lines up. The matching door frame (the jambs and lintel the
	/// door itself will hang in later) goes in the middle: it is centred on its pivot and thicker than
	/// the pair of walls, so it wraps both. Synty's demo scene places them the same way.
	/// </summary>
	private void BuildDoorways( DungeonLayout layout, Random variant )
	{
		DoorNet.Clear();
		// A stream of its own: the shared `variant` also draws the floor and ceiling tiles, so rolling doors
		// off it would reshuffle the look of every seed that already exists.
		var doorRng = new DungeonRng( DungeonRng.Mix( layout.Seed, 0x0D00_4201 ) );
		var kit = _kit;
		int hung = 0, boarded = 0;
		// Doorways per tier, for the boarded-up dead ends: a dressed room whose ONLY door this is may get planks
		// instead of a leaf. Never the spawn or exit room, never a corridor (the rest of the floor stays reachable).
		var doorsPerTier = new Dictionary<int, int>();
		if ( kit.BoardModels != null )
			foreach ( var (a, b) in layout.Doors )
				foreach ( var key in new[] { a, b } )
					if ( layout.Grid.Cells.TryGetValue( key, out var c ) && c.Tier != null )
						doorsPerTier[c.Tier.Id] = doorsPerTier.GetValueOrDefault( c.Tier.Id ) + 1;
		bool DeadEnd( (int x, int z, int l) key )
		{
			if ( !layout.Grid.Cells.TryGetValue( key, out var c ) || c.Tier == null ) return false;
			var t = c.Tier;
			if ( t.Kind == TierKind.Corridor || t.Tag is TierTag.Spawn or TierTag.Exit or TierTag.Secret ) return false;
			if ( layout.Decor == null || !layout.Decor.Roles.TryGetValue( t.Id, out var role ) || role == DecorRole.None ) return false;
			return doorsPerTier.GetValueOrDefault( t.Id ) == 1;
		}
		foreach ( var (a, b) in layout.Doors )
		{
			int i = variant.Next( kit.DoorFrameModels.Length );
			var d = b.x > a.x ? Dir.E : Dir.N;
			var (px, pz, yaw) = EdgePivot( d );
			if ( kit.DoubleFaced )
			{
				// one finished piece, symmetric about the plane: spawned once, right on the grid line
				Spawn( kit.DoorFrameModels[i], W( a.x + px, a.z + pz, a.l ), yaw, $"doorway {a.x},{a.z},{a.l} {d}" );
				SpawnWallTrim( layout, W( a.x + px, a.z + pz, a.l ), yaw, a, b, kit.DoorFrameModels[i], $"door {a.x},{a.z},{a.l} {d}" );
			}
			else
			{
				PlaceFrame( kit.DoorFrameModels[i], a.x, a.z, a.l, d );
				PlaceFrame( kit.DoorFrameModels[i], b.x, b.z, b.l, d.Opp() );
			}

			// The door rig's anchor: the middle of the segment on the grid plane, local +X into cell a. Jambs stand on
			// the lower of the two tiles: sunk a few cm into the thicker one rather than floating over the thinner.
			float up = Math.Min( _floorTop.GetValueOrDefault( a, 0f ), _floorTop.GetValueOrDefault( b, 0f ) );
			var pos = W( a.x + px, a.z + pz, a.l ) + Rotation.FromYaw( yaw ).Left * (Cell / 2f) + Vector3.Up * up;
			if ( kit.DoorInnerFrameModels != null )
			{
				var inner = kit.DoorInnerFrameModels[i];
				Vector3? scale = kit.DoorInnerFrameScale != null && kit.DoorInnerFrameScale.TryGetValue( inner, out var s ) ? new Vector3( s.x, s.y, s.z ) : null;
				Spawn( inner, pos, yaw, $"doorframe {a.x},{a.z},{a.l} {d}", scale: scale );
			}

			bool deadEnd = kit.BoardModels != null && (DeadEnd( a ) || DeadEnd( b ));
			int built = BuildDoor( i, pos, yaw, doorRng, $"{a.x},{a.z},{a.l} {d}", deadEnd );
			if ( built == 1 ) hung++;
			else if ( built == 2 ) boarded++;
			// every doorway a body can walk through, door or no door, so a skeleton can tell it is standing in one
			// (the open arches have no fit: a generous default opening)
			if ( built != 2 )
			{
				var fit = i < kit.DoorFits.Length ? kit.DoorFits[i] : null;
				DoorNet.RegisterDoorway( WorldTransform.ToWorld( new Transform( pos, Rotation.FromYaw( yaw ) ) ), fit?.Centre ?? 0f, fit?.HalfOpening ?? 50f );
			}
		}
		if ( layout.Doors.Count > 0 ) Log.Info( $"[Dungeon] {hung} doors hung in {layout.Doors.Count} doorways{(boarded > 0 ? $", {boarded} boarded up" : "")}" );
	}

	/// <summary>
	/// A dead-end room's doorway boarded shut: one breakable plank panel per slot of the fit's layout, standing in
	/// the opening where the leaf would hang. Each panel is its own BreakableProp (the boards come down one by one)
	/// with a box collider that, like a leaf's, stays dead through the navmesh bake and goes solid with DoorNet.SetSolid:
	/// the navmesh runs through the doorway, so whatever is inside paths at you and waits behind the boards.
	/// </summary>
	private bool BuildBarricade( DoorCatalog.DoorFit fit, Vector3 pos, float yaw, DungeonRng rng, string where )
	{
		var rot = Rotation.FromYaw( yaw );
		int placed = 0;
		foreach ( var (dy, dz) in fit.Boards )
		{
			var model = _kit.BoardModels[rng.RandRange( _kit.BoardModels.Length )];
			if ( !VoidMage.Combat.BreakableKits.TryGet( model, out var kit ) )
			{
				Log.Warning( $"[Dungeon] board {model} has no debris kit: doorway {where} left open" );
				return false;
			}
			var bpos = pos + rot.Left * (fit.Centre + dy * M) + Vector3.Up * (dz * M);
			var go = Spawn( model, bpos, yaw, $"boards {where}", ColliderKind.None, isStatic: false, themed: false );
			go.Tags.Add( VoidMage.Combat.BreakableProp.Tag );
			var prop = go.Components.Create<VoidMage.Combat.BreakableProp>();
			prop.Kit = kit;
			// the planks are nailed over the opening, so their ends are inside the jambs: the pieces are born past the
			// frame on the side the blow travels (half the 0.48 m double frame + half a plank + a margin), or the end
			// pieces would sleep wedged in the wall (user)
			prop.ClearPush = 18f;
			var bounds = KitBounds( model );
			var box = go.Components.Create<BoxCollider>();
			box.Center = bounds.Center;
			box.Scale = bounds.Size;
			box.Static = false;
			box.Enabled = false;
			DoorNet.RegisterBarricade( box );
			VoidMage.Combat.BreakableNet.Register( prop );
			placed++;
		}
		return placed > 0;
	}

	/// <summary>
	/// Hangs a leaf (or a pair, for the double frames) in the doorway just built. The door root takes the inner
	/// frame's own transform, so local +X points into cell a, local +Y runs along the wall, and the jambs sit at
	/// the fit's Centre +/- HalfOpening (DoorCatalog holds those, measured from the meshes).
	///
	/// Each leaf hangs off a "hinge" child at its jamb, whose yaw IS the swing — the same root/child rig the
	/// swing blade uses. A hinge-pivoted mesh gets a base yaw of 180 when its body runs the wrong way from its
	/// origin to reach the doorway centre, which is how one +x mesh serves both jambs without a mirroring
	/// negative scale; a centre-pivoted one never does (see DoorCatalog.DoorLeaf.BaseYaw).
	/// The leaf's collider starts disabled; DoorNet.SetSolid enables it once the navmesh has baked.
	/// </summary>
	/// <returns>0 nothing hung, 1 a door, 2 boards.</returns>
	private int BuildDoor( int frame, Vector3 pos, float yaw, DungeonRng rng, string where, bool deadEnd )
	{
		var fits = _kit.DoorFits;
		var fit = frame >= 0 && frame < fits.Length ? fits[frame] : null;
		if ( fit is null ) return 0;
		// the boarded-up dead end rolls first, from the same stream, only on a kit that has boards at all
		if ( deadEnd && fit.Boards != null && _kit.BoardModels != null && rng.NextDouble() < _kit.BoardChance )
			return BuildBarricade( fit, pos, yaw, rng, where ) ? 2 : 0;
		if ( rng.NextDouble() >= DoorNet.Chance ) return 0;
		var leaves = rng.Choice( fit.Options );

		var root = new GameObject( GameObject, true, $"door {where}" ) { Flags = GameObjectFlags.NotSaved };
		root.LocalPosition = pos;
		root.LocalRotation = Rotation.FromYaw( yaw );
		var door = root.Components.Create<Door>();
		door.HalfOpening = fit.HalfOpening;
		door.OpeningCentre = fit.Centre;

		float outset = DoorCatalog.Outset( fit, leaves );
		for ( int k = 0; k < leaves.Length; k++ )
		{
			var leaf = leaves[k];
			int side = leaves.Length == 1 || k == 0 ? -1 : +1;             // a single leaf takes the min-Y jamb; a pair one each
			float hingeY = fit.Centre + side * (fit.HalfOpening + outset + (leaves.Length > 1 ? LeafSeam : 0f));
			float baseYaw = leaf.BaseYaw( side );

			var hinge = new GameObject( root, true, "hinge" ) { Flags = GameObjectFlags.NotSaved };
			hinge.LocalPosition = new Vector3( 0f, hingeY, 0f );
			hinge.LocalRotation = Rotation.FromYaw( baseYaw );

			var go = Spawn( leaf.Model, pos, yaw, $"doorleaf {where}", ColliderKind.None, isStatic: false );
			go.SetParent( hinge, false );
			go.LocalPosition = new Vector3( 0f, leaf.Offset( side ), 0f );
			go.LocalRotation = Rotation.Identity;

			// the leaf's own box: not Static (it swings) and not enabled yet (the navmesh has to bake through the doorway first)
			var bounds = LoadModel( leaf.Model ).Bounds;
			var box = go.Components.Create<BoxCollider>();
			box.Center = bounds.Center;
			box.Scale = bounds.Size;
			box.Static = false;
			box.Enabled = false;

			door.Leaves.Add( new Door.Leaf { Hinge = hinge, Collider = box, BaseYaw = baseYaw, Side = side } );
		}
		DoorNet.Register( door );
		return 1;
	}

	private void PlaceFrame( string model, int x, int z, int l, Dir d )
	{
		var (px, pz, yaw) = EdgePivot( d );
		var pos = W( x + px, z + pz, l ) + Rotation.FromYaw( yaw ).Forward * DoorFrameOffset;
		Spawn( model, pos, yaw, $"doorway {x},{z},{l} {d}" );
	}

	private void BuildCeilings( DungeonLayout layout, Random variant )
	{
		var models = _kit.CeilingModels;
		foreach ( var (x, z, l) in layout.Ceilings )
		{
			// the roll always happens, so a kit with one slab keeps other seeds' variety intact
			bool plain = variant.NextDouble() < _kit.PlainCeilingChance;
			var model = plain || models.Length < 2 ? models[0] : models[1 + variant.Next( models.Length - 1 )];
			SpawnCeiling( model, x, z, l, $"ceiling {x},{z},{l}", ColliderKind.Box );
		}
	}

	/// <summary>
	/// Support pillars run from their base up to the CEILING of the floor they hold (floor + 2 levels),
	/// tiled from the top down with 5 m pieces so the piece in the room always reaches the ceiling; an
	/// odd half level left at the bottom is a pillar squashed to half height. A neighbouring room one
	/// level higher around the same vertex raises the top to that room's ceiling, so the pillar never
	/// ends halfway up a wall. Square pillars on outlines, round ones free-standing inside rooms.
	/// </summary>
	private void BuildPillars( DungeonLayout layout )
	{
		var g = layout.Grid;
		_pillarSlabs.Clear();
		foreach ( var p in layout.Pillars )
		{
			int bottom = p.BaseLevel < 0 ? 0 : p.BaseLevel;
			int highest = p.TopLevel;
			foreach ( var (cx, cz) in new[] { (p.VX - 1, p.VZ - 1), (p.VX, p.VZ - 1), (p.VX - 1, p.VZ), (p.VX, p.VZ) } )
				if ( g.Cells.ContainsKey( (cx, cz, p.TopLevel + 1) ) ) highest = p.TopLevel + 1;
			int top = highest + 2;
			var model = p.Interior ? _kit.RoundPillarModel : _kit.SquarePillarModel;
			for ( int k = top - 2; k >= bottom; k -= 2 )
			{
				Spawn( model, W( p.VX, p.VZ, k ), 0, $"pillar {p.VX},{p.VZ},{k}", ColliderKind.Box );
				_pillarSlabs.Add( (p.VX, p.VZ, k) ); _pillarSlabs.Add( (p.VX, p.VZ, k + 1) );
			}
			if ( (top - bottom) % 2 == 1 )
			{
				Spawn( model, W( p.VX, p.VZ, bottom ), 0, $"pillar {p.VX},{p.VZ},{bottom} half", ColliderKind.Box, scale: new Vector3( 1, 1, 0.5f ) );
				_pillarSlabs.Add( (p.VX, p.VZ, bottom) );
			}
		}
	}

	/// <summary>Vertex slabs (vx, vz, k) a pillar piece occupies; corner posts never double up on these.</summary>
	private readonly HashSet<(int vx, int vz, int k)> _pillarSlabs = new();

	/// <summary>
	/// Corner posts, judged per room cell and per vertex from what physically stands on the four half
	/// wall lines meeting at that vertex (the merged wall slabs, stair side walls and wedges, doorway
	/// frames), not from which cell asked for the wall. A cell whose two own lines at a vertex are solid
	/// has an INNER corner there; a cell whose two own lines are open while the two far lines are solid
	/// looks at an OUTER corner, a wall corner protruding into it, whatever is on the far side: another
	/// room, a stair, a room at an offset level. Pillars have priority, one post per vertex quadrant per
	/// 5 m of height, inner before outer.
	/// </summary>
	private void BuildCorners( DungeonLayout layout )
	{
		var g = layout.Grid;
		// solid (line, slab) pairs: exactly the wall geometry BuildWalls and BuildDoorways spawned
		var solid = new HashSet<(WallLine line, int k)>();
		foreach ( var seg in Walls.Merge( layout.Edges ) )
		{
			solid.Add( (seg.Line, seg.K) );
			if ( seg.Kind == SegKind.Full ) solid.Add( (seg.Line, seg.K + 1) );
		}
		foreach ( var e in layout.Edges )
			if ( e.Kind == EdgeKind.StairWall ) solid.Add( (Walls.LineOf( e.X, e.Z, e.D ), e.Level + 2) );
		foreach ( var (a, b) in layout.Doors )
		{
			var line = Walls.LineOf( a.x, a.z, b.x > a.x ? Dir.E : Dir.N );
			solid.Add( (line, a.l) ); solid.Add( (line, a.l + 1) );
		}
		// A breach has no merged segment (the wall is gone from the grid), but its two ragged stubs stand at
		// the ends of the line and still meet the perpendicular walls there. Without this the corner posts at
		// both ends vanish AND the outer pass, which only runs where a cell's own lines are open, starts
		// placing spurious posts poking into the room.
		foreach ( var br in layout.Breaches )
		{
			solid.Add( (br.Line, br.Level) ); solid.Add( (br.Line, br.Level + 1) );
		}
		bool Solid( WallLine line, int k ) => solid.Contains( (line, k) );
		// the half line leaving vertex (vx,vz) along X toward sx / along Y toward sz
		static WallLine LineX( int vx, int vz, int sx ) => new( sx > 0 ? vx : vx - 1, vz, false );
		static WallLine LineY( int vx, int vz, int sz ) => new( vx, sz > 0 ? vz : vz - 1, true );
		bool Floor( int x, int z, int l ) => g.Cells.TryGetValue( (x, z, l), out var c ) && c.Kind != CellKind.Stair;

		// Posts in different quadrants of one vertex only overlap inside the wall junction, so several
		// rooms meeting at a point each get theirs; the same quadrant is taken once per 5 m of height.
		var posts = new HashSet<(int vx, int vz, int l, int sx, int sz)>();
		bool Free( int vx, int vz, int l, int sx, int sz )
		{
			if ( _pillarSlabs.Contains( (vx, vz, l) ) || _pillarSlabs.Contains( (vx, vz, l + 1) ) ) return false;
			if ( posts.Contains( (vx, vz, l - 1, sx, sz) ) || posts.Contains( (vx, vz, l + 1, sx, sz) ) ) return false;
			return posts.Add( (vx, vz, l, sx, sz) );
		}
		// A centred column (Realms) is one post per VERTEX, whichever rooms meet there, taken per half-level slab
		// like the pillars: a room one level up sharing the vertex finds its lower slab covered and gets a half post
		// for the upper one, instead of being refused and left with a bare corner.
		var cornerSlabs = new HashSet<(int vx, int vz, int k)>();
		var kit = _kit;
		// sx,sz: the quadrant (relative to the vertex) of the room cell that SEES the post. Both stone pieces
		// are modelled with the pivot on the wall junction, the back edge buried just inside the wall
		// thickness, and the body protruding into their local +X/+Y quadrant (tools/fbx_shape.py), so
		// that quadrant is aimed at the viewing cell: the room's own corner for an inner post, the room
		// the wall corner pokes into for an outer post. A centred column ignores the quadrant.
		void Place( int vx, int vz, int l, bool inner, int sx, int sz )
		{
			var model = inner ? kit.InnerCornerModel : kit.OuterCornerModel;
			string name = $"corner {vx},{vz},{l} {(inner ? "inner" : "outer")}";
			if ( kit.CornerCentred )
			{
				bool lo = !_pillarSlabs.Contains( (vx, vz, l) ) && cornerSlabs.Add( (vx, vz, l) );
				bool hi = !_pillarSlabs.Contains( (vx, vz, l + 1) ) && cornerSlabs.Add( (vx, vz, l + 1) );
				if ( lo && hi ) Spawn( model, W( vx, vz, l ), 0f, name, ColliderKind.Box );
				else if ( lo ) Spawn( model, W( vx, vz, l ), 0f, name + " half", ColliderKind.Box, scale: new Vector3( 1, 1, 0.5f ) );
				else if ( hi ) Spawn( model, W( vx, vz, l + 1 ), 0f, name + " half", ColliderKind.Box, scale: new Vector3( 1, 1, 0.5f ) );
				return;
			}
			if ( !Free( vx, vz, l, sx, sz ) ) return;
			float yaw = (sx, sz) switch { (> 0, > 0) => 0f, (< 0, > 0) => 90f, (< 0, < 0) => 180f, _ => 270f };
			Spawn( model, W( vx, vz, l ), yaw, name, ColliderKind.Box );
		}

		var sides = new[] { -1, 1 };
		foreach ( var pass in new[] { true, false } )   // inner corners first, then outer
		{
			foreach ( var key in g.SortedKeys() )
			{
				var (x, z, l) = key;
				if ( !Floor( x, z, l ) ) continue;
				foreach ( var sx in sides )
					foreach ( var sz in sides )
					{
						// the vertex this cell sits at quadrant (sx, sz) of
						int vx = sx > 0 ? x : x + 1, vz = sz > 0 ? z : z + 1;
						bool own1 = Solid( LineX( vx, vz, sx ), l ), own2 = Solid( LineY( vx, vz, sz ), l );
						if ( pass )
						{
							if ( own1 && own2 ) Place( vx, vz, l, true, sx, sz );
							continue;
						}
						if ( own1 || own2 ) continue;
						if ( Solid( LineX( vx, vz, -sx ), l ) && Solid( LineY( vx, vz, -sz ), l ) )
							Place( vx, vz, l, false, sx, sz );
					}
			}
		}
	}

	// ------------------------------------------------------------------ decoration

	/// <summary>Metres to inches.</summary>
	private const float M = 39.37f;
	/// <summary>The fabric being built: set from the layout's theme at the top of Build, read by every pass after it.</summary>
	private DungeonKit _kit = DungeonKit.Stone;
	/// <summary>Half the wall mesh thickness: where a prop's back face lands when it stands against a wall.</summary>
	private float WallHalf => _kit.WallHalf;
	/// <summary>The ceiling's visible underside hangs this far below the grid plane.</summary>
	private float CeilingDrop => _kit.CeilingDrop;
	/// <summary>Keeps a prop's underside just clear of the tile surface so coplanar faces never fight.</summary>
	private const float FloorClearance = 0.25f;

	/// <summary>Top surface height of each floor cell above the grid plane, from the tile model actually spawned there.</summary>
	private readonly Dictionary<(int x, int z, int l), float> _floorTop = new();

	/// <summary>
	/// The floor surface a prop stands on. Floor props use the cell under their resolved position, wall props
	/// their cell, pillar props the highest floor around their vertex. Stairs and missing cells fall back to
	/// the grid plane.
	/// </summary>
	private float FloorTopFor( PropPlacement p )
	{
		float At( int x, int z ) => _floorTop.TryGetValue( (x, z, p.Level), out var t ) ? t : 0f;
		return p.Anchor switch
		{
			AnchorKind.Floor => At( (int)MathF.Floor( p.GX ), (int)MathF.Floor( p.GZ ) ),
			AnchorKind.PillarMounted => Math.Max( Math.Max( At( p.X, p.Z ), At( p.X - 1, p.Z ) ), Math.Max( At( p.X, p.Z - 1 ), At( p.X - 1, p.Z - 1 ) ) ),
			_ => At( p.X, p.Z ),
		};
	}

	/// <summary>
	/// Props from the decoration stage. Floor props arrive resolved; wall props are pushed flush using the
	/// model's own bounds (the back face at local -X lands on the wall's inner face), so the catalog never
	/// stores offsets. Lit props get a child PointLight with a flicker.
	/// </summary>
	private void BuildProps( DungeonLayout layout )
	{
		var decor = layout.Decor;
		// the face a pillar torch hangs on: the kit's measured shaft radius, else the model's bounds (a plinth wider than
		// the shaft would leave the torch floating off the column)
		float pillarR = _kit.PillarMountRadius > 0f ? _kit.PillarMountRadius * M : KitBounds( _kit.RoundPillarModel ).Maxs.x;
		int breakables = 0;
		int potions = 0;
		var unbreakable = new HashSet<string>();
		// the chests, and the crates and barrels, for the floor's guaranteed scrolls (ChestLoot.Assign after the loop)
		var chests = new List<VoidMage.Combat.BreakableProp>();
		var crates = new List<VoidMage.Combat.BreakableProp>();
		// a weapon left in a rack, 5% per rack: its own stream off the floor's seed, so every peer places the same one
		var rackRng = new DungeonRng( DungeonRng.Mix( layout.Seed, 0x7AC4 ) );
		int rackWeapons = 0;
		var secretTiers = layout.Grid.Tiers.Where( t => t.Tag == TierTag.Secret ).Select( t => t.Id ).ToHashSet();
		foreach ( var p in decor.Props )
		{
			// a prop may be a composite too (a pack piece recentred on its pivot): its bounds are the assembly's
			var modelBounds = KitBounds( p.Model );
			var size = modelBounds.Size;
			if ( size.x > 10 * M || size.y > 10 * M || size.z > 10 * M )
			{
				Log.Warning( $"[Dungeon] skipping {p.Model}: bounds {size} look like a skinned mesh" );
				continue;
			}
			// The PLACEMENT frame, yaw only. A tilted prop's own pitch is applied by Spawn and must never
			// get in here: rot.Forward is what pushes a wall prop out to the wall plane, and a pitched
			// forward axis would drive it into the floor instead.
			var rot = Rotation.FromYaw( p.Yaw );
			float surface = FloorTopFor( p );
			_kit.Props.TryGetValue( p.Prop, out var pdef );
			// A flat piece that lies on a wall or a ceiling (the root mats) is pitched at spawn, so every
			// offset below has to be measured on the TILTED box, not the model's own. BBox.Rotate returns an
			// axis-aligned box, which is exact for the multiples of 90 the field is restricted to. Untilted
			// props take the model's bounds untouched, so nothing that existed before moves by an epsilon.
			float tilt = pdef?.Tilt ?? 0f;
			// Turn is the same idea about the up axis: the mesh is spawned turned within the placement frame, and
			// the bounds are turned with it so the flush push measures the side that actually faces the wall.
			float turn = pdef?.Turn ?? 0f;
			var bounds = tilt == 0f && turn == 0f ? modelBounds : modelBounds.Rotate( Rotation.FromYaw( turn ) * Rotation.FromPitch( tilt ) );
			// Two size sources multiply: the placing pass's own roll (greenery grows to different sizes) and
			// PropDef.MaxHeight, a hard cap measured against this variant's real bounds — the pack's trees reach
			// 8.2 m and would grow through a 5 m ceiling, and its "giant" mushrooms are tree-sized. Every bounds
			// offset below is multiplied through, because the model shrinks about its own pivot.
			// the cap is applied AFTER the roll, never multiplied into it: a tree rolled at 1.25 would otherwise
			// come out taller than the ceiling the cap exists to keep it under
			float sc = p.Scale > 0f ? p.Scale : 1f;
			if ( pdef != null && pdef.MaxHeight > 0f && bounds.Size.z > 0f ) sc = MathF.Min( sc, pdef.MaxHeight * M / bounds.Size.z );
			var propScale = sc == 1f ? (Vector3?)null : new Vector3( sc, sc, sc );
			float sink = (pdef?.Sink ?? 0f) * M;
			// Heights are measured from the tile's top surface, not the grid plane the tile's pivot sits on.
			// A prop whose lowest point sits above its pivot (a shard) is always brought down onto the surface.
			// Geometry BELOW the pivot stays under by PropDef.Bury: 1 is the pack's half-bedded look (pebbles,
			// bone piles, the moss lump — Synty models those sunk in on purpose), 0 stands the whole model on
			// the floor. Sink pushes the pivot further into the floor on top of that.
			float low = bounds.Mins.z * sc;
			float standing = surface - (low > 0f ? low : low * (1f - (pdef?.Bury ?? 1f))) - sink + FloorClearance;
			Vector3 pos;
			switch ( p.Anchor )
			{
				case AnchorKind.WallFlush:
				case AnchorKind.WallMounted:
				{
					var edgeMid = W( p.X + 0.5f, p.Z + 0.5f, p.Level ) + new Vector3( p.Edge.Dx(), p.Edge.Dz(), 0 ) * (Cell / 2f);
					float push = WallHalf - bounds.Mins.x * sc - p.Embed * M;
					float up = p.Anchor == AnchorKind.WallFlush ? standing : surface;
					pos = edgeMid + rot.Forward * push + rot.Left * (p.S * Cell) + Vector3.Up * (p.Height * M + up);
					break;
				}
				case AnchorKind.CeilingHung:
					// the drop scales with the prop: a vine cut to half its length has to hang half as far
					// below the slab, or its top floats in the gap
					pos = W( p.GX, p.GZ, p.Level + 2 ) - Vector3.Up * (CeilingDrop + p.Height * M * sc);
					// a hook that sits off the pivot (the skeleton cage): slide along the prop's own forward axis
					if ( pdef != null && pdef.Nudge != 0f ) pos += rot.Forward * (pdef.Nudge * M);
					break;
				case AnchorKind.PillarMounted:
				{
					var f = new Vector3( p.Edge.Dx(), p.Edge.Dz(), 0 );
					pos = W( p.X, p.Z, p.Level ) + f * (pillarR - bounds.Mins.x * sc) + Vector3.Up * (p.Height * M + surface);
					break;
				}
				default:
					pos = W( p.GX, p.GZ, p.Level ) + Vector3.Up * (p.Height * M + standing);
					break;
			}
			// a recentred composite prop (a pew, a bookshelf) breaks by the kit of the model behind it
			var leafModel = DungeonKit.IsComposite( p.Model ) ? _kit.Expand( p.Model ).First() : p.Model;
			if ( VoidMage.Combat.BreakableKits.TryGet( leafModel, out var debris ) )
			{
				// anything with a debris kit breaks (crates, barrels, vases, the wooden furniture): not Static (its renderer
				// goes away when it breaks and a cached static shadow would stay), tagged, with the component that turns it
				// into debris; the sword and the fireball find it by index. A companion mesh (a coffin lid) rides under it
				// and breaks with it when it has a kit of its own.
				var bgo = Spawn( p.Model, pos, p.Yaw + turn, p.Name, p.Blocks ? ColliderKind.Box : ColliderKind.None, propScale, isStatic: false, themed: false, pitch: tilt );
				bgo.Tags.Add( VoidMage.Combat.BreakableProp.Tag );
				var prop = bgo.Components.Create<VoidMage.Combat.BreakableProp>();
				prop.Kit = debris;
				prop.DropsShards = ShardContainers.Contains( p.Prop );
				prop.DropsSkins = SkinContainers.Contains( p.Prop );
				prop.InSecretRoom = secretTiers.Contains( p.TierId );
				if ( p.Prop.Contains( "chest", StringComparison.Ordinal ) && p.Anchor == AnchorKind.Floor )
				{
					prop.IsChest = true;
					chests.Add( prop );
				}
				// the crates and barrels (the skin containers: big enough to hide something) can carry a scroll too
				else if ( prop.DropsSkins ) crates.Add( prop );
				if ( p.Attach != null )
				{
					var ago = Spawn( p.Attach, pos, p.Yaw + turn, p.Name + " attach", ColliderKind.None, propScale, isStatic: false, themed: false, pitch: tilt );
					ago.SetParent( bgo, true );
					prop.Attach = ago;
					if ( VoidMage.Combat.BreakableKits.TryGet( p.Attach, out var attachKit ) ) prop.AttachKit = attachKit;
				}
				VoidMage.Combat.BreakableNet.Register( prop );
				breakables++;
				continue;
			}
			// A prop the catalog says breaks but no kit matches (a model from another pack, a renamed path) would
			// otherwise turn into static scenery without a word: the only symptom was the breakables count.
			if ( pdef?.Breakable == true && unbreakable.Add( p.Model ) )
				Log.Warning( $"[Dungeon] {p.Prop} variant {p.Model} has no debris kit: spawned as scenery (BreakableKits.Build)" );
			var kind = p.Blocks || BoxHungProps.Contains( p.Prop ) ? ColliderKind.Box : CapsuleProps.Contains( p.Prop ) ? ColliderKind.Capsule : ColliderKind.None;
			// the consumable potions are pickups: not Static (they get destroyed on pickup), tagged, with the component E finds
			var potion = p.Prop == DecorCatalog.PotionHealId || p.Prop == DecorCatalog.PotionManaId;
			var go = Spawn( p.Model, pos, p.Yaw + turn, p.Name, kind, propScale, isStatic: !potion, themed: false, pitch: tilt );
			if ( potion )
			{
				go.Tags.Add( VoidMage.Run.PotionPickup.Tag );
				var pickup = go.Components.Create<VoidMage.Run.PotionPickup>();
				pickup.Kind = p.Prop == DecorCatalog.PotionHealId ? VoidMage.Character.PlayerPotions.Kind.Heal : VoidMage.Character.PlayerPotions.Kind.Mana;
				// shut in a chest: hidden until the chest is broken open (BreakableProp -> PotionPickup.OpenInside)
				if ( p.Inside ) pickup.ShutInside();
				potions++;
			}
			// the shrine room's altar is the mana shrine: E fills the staff, for nothing (ManaShrine)
			if ( p.Prop == DecorCatalog.AltarId ) go.Components.Create<ManaShrine>();
			// a weapon left standing in a rack, now and then: rot.Forward is the way the rack faces the room
			if ( p.Prop == DecorCatalog.WeaponRackId
				&& VoidMage.Run.ChestLoot.RackSkin( Scene, modelBounds.Transform( go.WorldTransform ), p.Yaw, go.Parent, rackRng ) )
				rackWeapons++;
			// the kit's exit prop IS the run's exit portal: the trigger the run loop watches
			if ( _kit.ExitProp != null && p.Prop == _kit.ExitProp ) MarkExitPortal( go );
			if ( HurtingProps.Contains( p.Prop ) ) AddVineHurt( go, modelBounds, sc );
			if ( IsFleshModel( p.Model ) ) VoidMage.Combat.FleshNet.Register( go.Components.Create<VoidMage.Combat.FleshProp>() );
			GameObject attach = null;
			if ( p.Attach != null )
			{
				attach = Spawn( p.Attach, pos, p.Yaw + turn, p.Name + " attach", ColliderKind.None, propScale, themed: false, pitch: tilt );
				// the body in the stocks is flesh while the stocks are wood
				if ( IsFleshModel( p.Attach ) ) VoidMage.Combat.FleshNet.Register( attach.Components.Create<VoidMage.Combat.FleshProp>() );
			}
			if ( NoShadowProps.Contains( p.Prop ) && go.Components.Get<ModelRenderer>() is { } nr ) nr.RenderType = ModelRenderer.ShadowRenderType.Off;
			if ( p.Lit )
			{
				// AddLight works in the prop's own frame (rot.Inverse * offset, flame offsets off the model
				// bounds), and `rot` here is yaw-only, so a pitched or turned prop would put its light and its
				// flame in the wrong place. Nothing like that carries a light today; say so rather than misplace one.
				if ( tilt != 0f || turn != 0f ) Log.Warning( $"[Dungeon] {p.Prop} is tilted or turned AND lit: AddLight assumes the placement frame" );
				// The light sits in or on this very mesh; with shadowed lights its own shadow would be a
				// black cone behind the torch cup or under the candle. The prop still receives light.
				if ( go.Components.Get<ModelRenderer>() is { } mr ) mr.RenderType = ModelRenderer.ShadowRenderType.Off;
				if ( attach.IsValid() && attach.Components.Get<ModelRenderer>() is { } ar ) ar.RenderType = ModelRenderer.ShadowRenderType.Off;
				AddLight( go, p, modelBounds, rot, attach );
			}
		}
		// a new player's first floor (the fixed first run) gives a spell and an enchantment from its scroll chest
		VoidMage.Run.ChestLoot.Assign( chests, crates, layout.Seed );
		if ( breakables > 0 ) Log.Info( $"[Dungeon] {breakables} breakables" );
		if ( potions > 0 ) Log.Info( $"[Dungeon] {potions} potions" );
		if ( rackWeapons > 0 ) Log.Info( $"[Dungeon] {rackWeapons} weapons left in racks" );
		if ( VoidMage.Combat.FleshNet.Count > 0 ) Log.Info( $"[Dungeon] {VoidMage.Combat.FleshNet.Count} flesh props (the sword bleeds them)" );
		if ( VoidMage.Traps.VineNet.Count > 0 ) Log.Info( $"[Dungeon] {VoidMage.Traps.VineNet.Count} vine hurt volumes (breach curtains + spiky floor vines)" );
	}

	/// <summary>
	/// A spiky vine hurts to walk into. It keeps no collider — you push straight through the plant — and gets a
	/// VineCurtain hurt volume instead, cut from the model's own bounds and carried in the prop's own frame, so
	/// a vine trailing three metres diagonally across a room pricks you where it actually is. A world-aligned
	/// box round the same model would be half empty air and would sting you walking past it.
	///
	/// Trimmed in on the horizontal axes because the plant is a sprawl of tendrils, not a solid block: its
	/// bounding box touches you before the spikes do. Full height, though — the spikes are at shin level and
	/// the body test already only asks that the body's span crosses the box's.
	/// </summary>
	private static void AddVineHurt( GameObject go, BBox b, float scale )
	{
		var mins = b.Mins * scale;
		var maxs = b.Maxs * scale;
		float insetX = (maxs.x - mins.x) * VineHurtInset / 2f;
		float insetY = (maxs.y - mins.y) * VineHurtInset / 2f;
		var curtain = go.Components.Create<VoidMage.Traps.VineCurtain>();
		curtain.Hurt = new BBox(
			new Vector3( mins.x + insetX, mins.y + insetY, mins.z ),
			new Vector3( maxs.x - insetX, maxs.y - insetY, maxs.z ) );
		VoidMage.Traps.VineNet.Register( curtain );
	}

	/// <summary>Share of a spiky vine's width and depth trimmed off its hurting box, because the model's bounds
	/// are a box round a sprawl and the leaves do not fill the corners.</summary>
	private const float VineHurtInset = 0.25f;

	/// <summary>
	/// The point light of a lit prop plus its FireLight: the flicker and the flame recipe. The light
	/// sits where the fire is (torch head, candle wick, brazier bowl); the flame offset then pulls the
	/// sprite flame back onto the prop from there, in the prop's frame (+X into the room, +Z up).
	/// </summary>
	private void AddLight( GameObject parent, PropPlacement p, BBox modelBounds, Rotation rot, GameObject attach )
	{
		// a torch's fire is where its model puts it (the kit's TorchFlame, by model); the stone torch's 0.48 / 0.46 m otherwise
		var flame = _kit.TorchFlame != null && _kit.TorchFlame.TryGetValue( p.Model, out var tf ) ? tf : (fwd: 0.483f, up: 0.457f);
		var (offset, radius, color) = p.Light switch
		{
			LightKind.Torch => (rot.Forward * (flame.fwd * M) + Vector3.Up * (flame.up * M), 260f, new Color( 1f, 0.62f, 0.15f ) * 2f),
			LightKind.Candle => (Vector3.Up * (modelBounds.Maxs.z + 4f), 150f, new Color( 1f, 0.75f, 0.35f ) * 1.4f),
			LightKind.Brazier => (Vector3.Up * (modelBounds.Maxs.z + 12f), 400f, new Color( 1f, 0.55f, 0.12f ) * 2.5f),
			LightKind.Chandelier => (Vector3.Up * (modelBounds.Mins.z + 20f), 320f, new Color( 1f, 0.7f, 0.3f ) * 2f),
			_ => (Vector3.Up * (modelBounds.Maxs.z * 0.6f), 220f, new Color( 0.35f, 0.6f, 1f ) * 1.6f),
		};
		// glowing potions are blue; 03 and 07 glow red (user: the blue on potion_05 is the look, a red twin wanted)
		if ( p.Light == LightKind.Glow && (p.Model.Contains( "potion_03", StringComparison.Ordinal ) || p.Model.Contains( "potion_07", StringComparison.Ordinal )) )
			color = new Color( 1f, 0.22f, 0.16f ) * 1.6f;
		var go = new GameObject( parent, true, "light" ) { Flags = GameObjectFlags.NotSaved };
		go.LocalPosition = rot.Inverse * offset;   // parent is already rotated; offset was computed in world axes
		// The shadow mapper only caches a light's static geometry when the light's own object is
		// static; FireLight keeps it in place (flicker goes through colour and falloff, never the
		// transform or radius) so the cache survives.
		go.Flags |= GameObjectFlags.Static;
		var light = go.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;
		// Shadowed: without them a torch lights the corridor on the far side of its wall. Every light
		// starts shadowed; FireLightSystem then sleeps the far ones and hands out a small shadow budget.
		light.Shadows = true;
		var fire = go.Components.Create<FireLight>();
		fire.Kind = p.Light;
		fire.BasePosition = go.LocalPosition;
		fire.BaseSet = true;
		// Room-side face of the wall/pillar in the prop's frame: the prop's back face (Mins.x) lands on
		// it, then the embed sinks the prop that much further in (see BuildProps). Pillar props embed 0.
		fire.WallFaceX = modelBounds.Mins.x + p.Embed * M;
		switch ( p.Light )
		{
			case LightKind.Torch:
				fire.FlameScale = 1f;
				fire.FlameOffset = new Vector3( -4f, 0f, -5f );     // back into the cup
				fire.WallGlow = p.Anchor is AnchorKind.WallMounted or AnchorKind.PillarMounted;
				break;
			case LightKind.Candle:
				if ( p.Prop is "lantern" or "stone_lantern" )
				{
					// the flame burns inside the cage, at mid-height; the light stays on top
					fire.FlameScale = 0.4f;
					fire.FlameOffset = new Vector3( 0f, 0f, modelBounds.Center.z - (modelBounds.Maxs.z + 4f) );
				}
				else
				{
					fire.FlameScale = 0.45f;
					fire.FlameOffset = new Vector3( 0f, 0f, -3f );
					if ( p.Prop == "candles" && attach.IsValid() ) fire.FlameCard = attach.Components.Get<ModelRenderer>();
				}
				break;
			case LightKind.Brazier:
				if ( p.Prop == "bonfire" ) { fire.FlameScale = 2.2f; fire.FlameOffset = new Vector3( 0f, 0f, -16f ); fire.Smoke = true; fire.Sparks = true; }
				else if ( p.Prop == "fireplace_wall" )
				{
					// hearth about 16 in up the wall piece; the light sits at the top of the 2.4 m chimney
					fire.FlameScale = 1.6f;
					fire.FlameOffset = new Vector3( -6f, 0f, 16f - (modelBounds.Maxs.z + 12f) );
					fire.Smoke = true;
					fire.WallGlow = true;
				}
				else { fire.FlameScale = 2f; fire.FlameOffset = new Vector3( 0f, 0f, -13f ); fire.Smoke = true; fire.Sparks = true; }
				break;
			case LightKind.Chandelier:
				fire.FlameScale = 0.35f;
				fire.FlameOffset = new Vector3( 0f, 0f, -6f );
				fire.Tongues = 4;
				fire.TongueRadius = 16f;
				break;
			default:
				fire.FlameScale = 1f;
				break;
		}
		// indoors the column is cut short: a tier is 98 units floor to floor and the smoke must fade under the ceiling
		if ( fire.Smoke ) fire.SmokeHeight = 0.5f;
		fire.BaseColor = color;
		fire.BaseRadius = radius;
		fire.Phase = (p.GX * 12.9898f + p.GZ * 78.233f + p.Level * 37.719f) % 10f;
		fire.Amount = p.Light == LightKind.Glow ? 0.08f : p.Light == LightKind.Candle ? 0.25f : 0.4f;
	}

	private void BuildTagLights( DungeonLayout layout )
	{
		foreach ( var t in layout.Tagged )
		{
			var (cx, cz) = t.Centroid();
			var go = new GameObject( GameObject, true, $"marker {t.Tag}" ) { Flags = GameObjectFlags.NotSaved };
			go.LocalPosition = W( (float)cx + 0.5f, (float)cz + 0.5f, t.Level ) + Vector3.Up * 70f;
			var light = go.Components.Create<PointLight>();
			light.LightColor = TagColor( t.Tag );
			light.Radius = 600f;
			light.Shadows = false;   // debug markers: a 600 in shadowed light per tier would be pure cost
		}
	}

	/// <summary>The spawn tier's cell nearest its centroid, 20 u up, in world space.</summary>
	private Vector3 SpawnPointFor( DungeonLayout layout )
	{
		var spawn = layout.Find( TierTag.Spawn ) ?? layout.Grid.Tiers.First( t => t.Cells.Count > 0 );
		var (cx, cz) = spawn.Centroid();
		var cell = spawn.Sorted().OrderBy( c => Math.Abs( c.x - cx ) + Math.Abs( c.z - cz ) ).First();
		return WorldTransform.PointToWorld( W( cell.x + 0.5f, cell.z + 0.5f, spawn.Level ) + Vector3.Up * 20f );
	}

	/// <summary>Test scenes only: a player of this builder's own, as its child (so Clear() takes it too). The run
	/// loop keeps SpawnPlayer off and owns the player itself (RunManager).</summary>
	private void SpawnPlayerAt( DungeonLayout layout )
	{
		var world = PlayerSpawnWorld ?? SpawnPointFor( layout );
		var local = WorldTransform.PointToLocal( world );
		if ( PlayerPrefab.IsValid() )
		{
			// CloneConfig.Transform is LOCAL to CloneConfig.Parent (see SkeletonSpawner.Spawn): clone at the root, then reparent
			var player = PlayerPrefab.Clone( new CloneConfig
			{
				Transform = new Transform( world ),
				StartEnabled = true,
				Name = "Player",
			} );
			player.Flags |= GameObjectFlags.NotSaved;
			player.SetParent( GameObject, true );
			VoidMage.Character.PotionDrinker.EnsureOn( player );
			VoidMage.Character.CharacterAppearance.ApplySaved( player );
			return;
		}
		var go = new GameObject( GameObject, true, "Player" ) { Flags = GameObjectFlags.NotSaved };
		go.LocalPosition = local;
		var pc = go.Components.Create<PlayerController>();
		pc.ThirdPerson = false;
		pc.UseCameraControls = true;
		pc.UseInputControls = true;
		pc.UseAnimatorControls = false;
		pc.EnableFootstepSounds = false;
	}

	// ------------------------------------------------------------------ helpers

	private static Vector3 W( float gx, float gz, float level ) => new( gx * Cell, gz * Cell, level * LevelH );

	private enum ColliderKind { None, Box, Model, Capsule }

	/// <summary>Non-blocking props that still get a vertical capsule collider fitted a little inside their bounds.</summary>
	/// <summary>Non-blocking props that still get a collider, as a vertical cylinder fitted inside their bounds.
	/// The trees are here because a box around one is an invisible wall out at the edge of the canopy; the
	/// cylinder lands on the trunk and shrinks with the MaxHeight cap. If it reads wrong in-engine the fix is a
	/// hand-authored footprint per tree model in <see cref="BoxOverrides"/>.</summary>
	private static readonly HashSet<string> CapsuleProps = new() { "candle_stand", "chandelier", "tree", "tree_dandelion" };

	/// <summary>The breakables that may hide a void shard (every kit's ids): the containers, never the furniture or the boards.</summary>
	private static readonly HashSet<string> ShardContainers = new() { "crate", "crate_metal", "crate_ornate", "barrel", "vase" };

	/// <summary>Big enough to hide a weapon: the crates and barrels (a vase is not; user, 2026-09-17).</summary>
	private static readonly HashSet<string> SkinContainers = new() { "crate", "crate_metal", "crate_ornate", "barrel" };

	/// <summary>Hung, non-blocking props that still get a box collider: the cages hang low enough to walk into (user).</summary>
	private static readonly HashSet<string> BoxHungProps = new() { "skeleton_cage", "cage_hung_stone" };

	/// <summary>Props that cast no shadow: the cobwebs, whose shadow lay on the wall right behind them as a dark smear
	/// (user). Set at spawn, never later: the props are Static, so a shadow already in a light's static cache would
	/// stay on the wall after the renderer stopped casting (which is why turning it off in the inspector did nothing).</summary>
	private static readonly HashSet<string> NoShadowProps = new() { "web_corner_high", "web_corner" };

	/// <summary>
	/// Model-name words of the props that are flesh (the Gaol corpses and burnt bodies, the Realms dead dwarves and
	/// nomads, the limbs and heads on spikes, the body in the stocks): the sword bleeds them (FleshProp) instead of
	/// clanging. Bone is never flesh: a "body_skeleton" or a bone pile keeps the stone clang (or nothing).
	/// </summary>
	private static readonly string[] FleshWords = { "corpse", "deadbody", "body_burnt", "body_pile", "gallows_01_body", "gibbet_01_body", "stock_01_body", "animal_dead", "limb_", "head_spike" };
	private static readonly string[] NotFleshWords = { "skeleton", "bone" };

	public static bool IsFleshModel( string model )
	{
		if ( string.IsNullOrEmpty( model ) ) return false;
		var m = model.ToLowerInvariant();
		foreach ( var w in NotFleshWords ) if ( m.Contains( w, StringComparison.Ordinal ) ) return false;
		foreach ( var w in FleshWords ) if ( m.Contains( w, StringComparison.Ordinal ) ) return true;
		return false;
	}

	/// <summary>Props that hurt to walk into: the spiky vines trailing out of a wall. They keep their
	/// collider-free spawn — you push through them — and get a VineCurtain hurt volume instead.</summary>
	private static readonly HashSet<string> HurtingProps = new() { "vine_ground" };

	/// <summary>Box colliders authored by hand (size, centre in the prop's frame, units) for models whose bounds are wrong.</summary>
	private static readonly Dictionary<string, (Vector3 size, Vector3 center)> BoxOverrides = new()
	{
		["dungeonpack/sm_env_alter_01.vmdl"] = (new Vector3( 60f, 80f, 80f ), new Vector3( 0f, 0f, 40f )),   // the shrine altar (user-measured)
	};

	/// <summary>
	/// Models whose box collider is sized from ANOTHER model's bounds: decorated variants of a kit piece
	/// whose dressing must not block (the root-hung ceiling: its roots reach 2.3 m below the slab, so its own
	/// bounds would take most of the headroom; it gets the plain slab's 15 cm sheet like every other ceiling).
	/// </summary>
	private static readonly Dictionary<string, string> BoxBoundsFrom = new()
	{
		["dungeonpack/sm_env_ceiling_stone_flat_05.vmdl"] = "dungeonpack/sm_env_ceiling_stone_flat_01.vmdl",
	};

	/// <summary>
	/// Box: a BoxCollider sized to the model's bounds, for the slabs and posts (floors, walls, ceilings,
	/// pillars). Model: the mesh itself, for anything a box would misrepresent (stairs, wedges, the
	/// sloped stair ceiling, doorway frames with their opening).
	/// </summary>
	/// <param name="themed">Whether the mossy re-skin applies. True for the dungeon's own fabric (walls, floors,
	/// ceilings, pillars, stairs, doorways, the breach stubs); FALSE for everything standing in it — props,
	/// traps, debris and greenery keep the stone atlas so they look the same on every floor.</param>
	/// <param name="pitch">Degrees the piece is tilted about its own left axis, for the flat props that lie on a
	/// wall or a ceiling instead of the floor (PropDef.Tilt), and for the flipped ceiling slabs. The colliders
	/// below are built from the model's raw bounds in this object's own space, the same space the renderer draws
	/// in, so a box or a mesh collider follows the tilt exactly. What must NOT be fed in here is a box already
	/// rotated by the caller (the way BuildProps rotates the bounds for its placement maths): that one would come
	/// out tilted twice.</param>
	private GameObject Spawn( string modelPath, Vector3 localPos, float yaw, string name, ColliderKind collider = ColliderKind.Model, Vector3? scale = null, bool isStatic = true, bool themed = true, float pitch = 0f )
	{
		var rot = pitch == 0f ? Rotation.FromYaw( yaw ) : Rotation.FromYaw( yaw ) * Rotation.FromPitch( pitch );
		if ( DungeonKit.IsComposite( modelPath ) ) return SpawnComposite( modelPath, localPos, rot, name, collider, scale, isStatic, themed );
		return SpawnOne( GameObject, modelPath, localPos, rot, name, collider, scale, isStatic, themed );
	}

	/// <summary>
	/// A composite kit piece (DungeonKit.Composites): its pieces spawned together in the frame this call places the
	/// composite in. A composite of ONE plain piece is that piece spawned directly, turned and scaled as the piece
	/// says — that is how a whole model is scaled (a 6 m wall to 5) or recentred, and it returns the piece itself,
	/// so the secret-wall rig and the breakables keep working on single-piece composites. Several pieces go under
	/// an empty holder at the composite's transform, with the requested scale baked into their offsets and scales
	/// (nested composites walked); a Box collider goes on the holder, sized to the whole assembly, and the pieces
	/// get none unless a piece asks for its own (the bar panels keep a mesh collider so bolts pass between bars).
	/// </summary>
	private GameObject SpawnComposite( string path, Vector3 localPos, Rotation rot, string name, ColliderKind collider, Vector3? scale, bool isStatic, bool themed )
	{
		var s = scale ?? Vector3.One;
		var pieces = _kit.Pieces( path );
		if ( pieces.Length == 1 && !DungeonKit.IsComposite( pieces[0].Model ) )
		{
			var pc = pieces[0];
			var ps = new Vector3( s.x * pc.Sx, s.y * pc.Sy, s.z * pc.Sz );
			var pcol = pc.Collider switch { PieceCollider.None => ColliderKind.None, PieceCollider.Box => ColliderKind.Box, PieceCollider.Model => ColliderKind.Model, _ => collider };
			return SpawnOne( GameObject, pc.Model, localPos + rot * (new Vector3( pc.Dx, pc.Dy, pc.Dz ) * M * s), rot * PieceRot( pc ), name, pcol, ps == Vector3.One ? null : ps, isStatic, themed );
		}
		var holder = new GameObject( GameObject, true, name ) { Flags = GameObjectFlags.NotSaved };
		holder.LocalPosition = localPos;
		holder.LocalRotation = rot;
		if ( isStatic ) holder.Flags |= GameObjectFlags.Static;
		int n = 0;
		SpawnPieces( holder, path, Vector3.Zero, Rotation.Identity, s, name, collider == ColliderKind.Box ? ColliderKind.None : collider, isStatic, themed, ref n );
		if ( collider == ColliderKind.Box )
		{
			var kb = KitBounds( path );
			var box = holder.Components.Create<BoxCollider>();
			box.Center = kb.Center * s;
			box.Scale = kb.Size * s;
			box.Static = true;
		}
		return holder;
	}

	private void SpawnPieces( GameObject parent, string path, Vector3 pos, Rotation rot, Vector3 scale, string name, ColliderKind inherit, bool isStatic, bool themed, ref int n )
	{
		foreach ( var pc in _kit.Pieces( path ) )
		{
			var ppos = pos + rot * (new Vector3( pc.Dx, pc.Dy, pc.Dz ) * M * scale);
			var prot = rot * PieceRot( pc );
			var ps = new Vector3( scale.x * pc.Sx, scale.y * pc.Sy, scale.z * pc.Sz );
			var col = pc.Collider switch { PieceCollider.None => ColliderKind.None, PieceCollider.Box => ColliderKind.Box, PieceCollider.Model => ColliderKind.Model, _ => inherit };
			if ( DungeonKit.IsComposite( pc.Model ) ) { SpawnPieces( parent, pc.Model, ppos, prot, ps, name, col, isStatic, themed, ref n ); continue; }
			SpawnOne( parent, pc.Model, ppos, prot, $"{name} #{n++}", col, ps == Vector3.One ? null : ps, isStatic, themed );
		}
	}

	private static Rotation PieceRot( KitPiece pc ) => pc.Pitch == 0f ? Rotation.FromYaw( pc.Yaw ) : Rotation.FromYaw( pc.Yaw ) * Rotation.FromPitch( pc.Pitch );

	/// <summary>Bounds of a kit path in its own frame: the model's, or the union of a composite's pieces as placed (unscaled by any request).</summary>
	private BBox KitBounds( string path )
	{
		if ( !DungeonKit.IsComposite( path ) ) return LoadModel( path ).Bounds;
		if ( _kitBounds.TryGetValue( path, out var cached ) ) return cached;
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );
		void Walk( string p, Vector3 pos, Rotation rot, Vector3 scale )
		{
			foreach ( var pc in _kit.Pieces( p ) )
			{
				var ppos = pos + rot * (new Vector3( pc.Dx, pc.Dy, pc.Dz ) * M * scale);
				var prot = rot * PieceRot( pc );
				var ps = new Vector3( scale.x * pc.Sx, scale.y * pc.Sy, scale.z * pc.Sz );
				if ( DungeonKit.IsComposite( pc.Model ) ) { Walk( pc.Model, ppos, prot, ps ); continue; }
				var b = LoadModel( pc.Model ).Bounds;
				for ( int i = 0; i < 8; i++ )
				{
					var c = new Vector3( (i & 1) == 0 ? b.Mins.x : b.Maxs.x, (i & 2) == 0 ? b.Mins.y : b.Maxs.y, (i & 4) == 0 ? b.Mins.z : b.Maxs.z );
					var q = ppos + prot * (c * ps);
					mins = Vector3.Min( mins, q );
					maxs = Vector3.Max( maxs, q );
				}
			}
		}
		Walk( path, Vector3.Zero, Rotation.Identity, Vector3.One );
		var result = new BBox( mins, maxs );
		_kitBounds[path] = result;
		return result;
	}

	private readonly Dictionary<string, BBox> _kitBounds = new();

	private GameObject SpawnOne( GameObject parent, string modelPath, Vector3 localPos, Rotation localRot, string name, ColliderKind collider, Vector3? scale, bool isStatic, bool themed )
	{
		var model = LoadModel( modelPath );
		var go = new GameObject( parent, true, name ) { Flags = GameObjectFlags.NotSaved };
		go.LocalPosition = localPos;
		go.LocalRotation = localRot;
		if ( scale.HasValue ) go.LocalScale = scale.Value;
		// Dungeon pieces never move. Static renderers go into a shadowed light's cached static pass
		// (rendered once per light) instead of into all six cube faces every frame. Set before the
		// renderer exists so it picks the flag up on creation. Breakables are the exception.
		if ( isStatic ) go.Flags |= GameObjectFlags.Static;
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		// every piece of the dungeon comes through here, so this is the only hook the mossy re-skin needs
		if ( themed ) ApplySkin( renderer );
		switch ( collider )
		{
			case ColliderKind.Box:
				var box = go.Components.Create<BoxCollider>();
				if ( BoxOverrides.TryGetValue( modelPath, out var fixedBox ) )
				{
					// authored by hand where the model's bounds lie (the altar: rescaled, its bounds still huge)
					box.Center = fixedBox.center;
					box.Scale = fixedBox.size;
				}
				else
				{
					var bounds = (BoxBoundsFrom.TryGetValue( modelPath, out var source ) ? LoadModel( source ) : model).Bounds;
					box.Center = bounds.Center;
					box.Scale = bounds.Size;
				}
				box.Static = true;
				break;
			case ColliderKind.Model:
				var mesh = go.Components.Create<ModelCollider>();
				mesh.Model = model;
				mesh.Static = true;
				break;
			case ColliderKind.Capsule:
			{
				// a vertical cylinder fitted inside the bounds: 80 % of the narrower footprint, capped a little inside
				// the top and bottom (candle stands, hanging chandeliers)
				var b = model.Bounds;
				var capsule = go.Components.Create<CapsuleCollider>();
				var radius = MathF.Max( 4f, MathF.Min( b.Size.x, b.Size.y ) * 0.5f * 0.8f );
				var inset = MathF.Min( radius, MathF.Max( 2f, b.Size.z * 0.05f ) );
				capsule.Radius = radius;
				capsule.Start = new Vector3( b.Center.x, b.Center.y, b.Mins.z + inset + radius );
				capsule.End = new Vector3( b.Center.x, b.Center.y, MathF.Max( b.Mins.z + inset + radius, b.Maxs.z - inset - radius ) );
				capsule.Static = true;
				break;
			}
		}
		return go;
	}

	private Model LoadModel( string path )
	{
		if ( !_models.TryGetValue( path, out var m ) )
		{
			m = Model.Load( path );
			_models[path] = m;
		}
		return m;
	}

	private static Color TagColor( TierTag tag ) => tag switch
	{
		TierTag.Spawn => new Color( 0.18f, 0.8f, 0.44f ),
		TierTag.Exit => new Color( 0.56f, 0.27f, 0.68f ),
		TierTag.Shrine => new Color( 0.2f, 0.6f, 0.86f ),
		TierTag.Ambush => new Color( 0.9f, 0.3f, 0.24f ),
		TierTag.Loot => new Color( 0.95f, 0.77f, 0.06f ),
		_ => Color.White,
	};

	// ------------------------------------------------------------------ kit knowledge
	// Every model path and measured number of the fabric lives in DungeonKit (one instance per theme), with the
	// pivot conventions of each pack written up there. What stays here is the geometry that is the same for every
	// kit: the grid, the pivot tables, and the two z-fighting nudges.

	/// <summary>Inches each of the two back-to-back frames is pushed toward its own room: just enough to stop z-fighting.</summary>
	public const float DoorFrameOffset = 2f;
	/// <summary>
	/// The same trick for a double door: each leaf of a pair stops this far short of the centre line, so the two
	/// end faces are 2 mm apart instead of landing on the same plane and z-fighting down the join.
	/// </summary>
	public const float LeafSeam = 0.1f;

	/// <summary>Pivot corner (in cell units) and yaw for a +Y-running edge piece on edge d of a cell, front facing into the cell.</summary>
	private static (int px, int pz, float yaw) EdgePivot( Dir d ) => d switch
	{
		Dir.W => (0, 0, 0f),
		Dir.E => (1, 1, 180f),
		Dir.S => (1, 0, 90f),
		_ => (0, 1, 270f),      // N
	};

	/// <summary>Pivot corner and yaw so a stair modelled in the -X/-Y quadrant of its pivot fills its cell and climbs toward sdir.</summary>
	private static (int px, int pz, float yaw) StairPivot( Dir sdir ) => sdir switch
	{
		Dir.E => (0, 0, 180f),
		Dir.W => (1, 1, 0f),
		Dir.N => (1, 0, 270f),
		_ => (0, 1, 90f),       // S
	};

	/// <summary>The same for a stair modelled in the -X/+Y quadrant (Realms): the same yaws, the pivot on the other corner along the run's side.</summary>
	private static (int px, int pz, float yaw) StairPivotMirrored( Dir sdir ) => sdir switch
	{
		Dir.E => (0, 1, 180f),
		Dir.W => (1, 0, 0f),
		Dir.N => (0, 0, 270f),
		_ => (1, 1, 90f),       // S
	};

	/// <summary>Same corners as the stair, yaw flipped: the ceiling piece rises toward its local +X.</summary>
	private static (int px, int pz, float yaw) StairCeilingPivot( Dir sdir ) => sdir switch
	{
		Dir.E => (0, 0, 0f),
		Dir.W => (1, 1, 180f),
		Dir.N => (1, 0, 90f),
		_ => (0, 1, 270f),      // S
	};

	/// <summary>
	/// The Realms stair flipped upside down as the sloped ceiling. Flipped (yaw 180 · pitch 180, applied by the
	/// caller), the mesh covers the -X/-Y quadrant of its pivot with its stepped face at pivot height at x = 0
	/// descending 2.5 m toward -X. The pivot therefore goes on the HIGH-end corner, with this yaw turning local +X
	/// onto sdir so the descent runs back toward the low end.
	/// </summary>
	private static (int px, int pz, float yaw) StairCeilingPivotFlipped( Dir sdir ) => sdir switch
	{
		Dir.E => (1, 1, 0f),
		Dir.W => (0, 0, 180f),
		Dir.N => (0, 1, 90f),
		_ => (1, 0, 270f),      // S
	};

	/// <summary>Stair side wedge on edge d of a stair cell rising toward sdir: pivot at the low end of that edge.</summary>
	private static (int px, int pz, float yaw) StairWallPivot( Dir sdir, Dir d )
	{
		var (a, b) = d switch
		{
			Dir.W => ((0, 0), (0, 1)),
			Dir.E => ((1, 0), (1, 1)),
			Dir.S => ((0, 0), (1, 0)),
			_ => ((0, 1), (1, 1)),   // N
		};
		int dotA = a.Item1 * sdir.Dx() + a.Item2 * sdir.Dz();
		int dotB = b.Item1 * sdir.Dx() + b.Item2 * sdir.Dz();
		var low = dotA <= dotB ? a : b;
		float yaw = sdir switch { Dir.E => 270f, Dir.W => 90f, Dir.N => 0f, _ => 180f };
		return (low.Item1, low.Item2, yaw);
	}
}
