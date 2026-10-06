using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Dungeon;

namespace VoidMage.Run;

public enum RunState
{
	/// <summary>The main menu: a sunny village seen from the menu camera, no player yet, no portal. Play breaks the storm (MainMenu).</summary>
	MainMenu,
	/// <summary>Home. The village portal starts a run.</summary>
	Village,
	/// <summary>Between floors: the screen is black or showing the descending card while the next floor builds.</summary>
	Descending,
	/// <summary>On a floor. The exit portal opens the menu.</summary>
	InFloor,
	/// <summary>Standing in the exit portal with the home-or-continue menu up.</summary>
	ExitMenu,
	/// <summary>In the boss arena.</summary>
	Boss,
	/// <summary>On the way home (a return or a death), under black.</summary>
	Homeward,
}

/// <summary>
/// The run loop (decided 2026-09-13): the village and the dungeon share one scene. This component owns the
/// player (cloned once at VillageSpawn, at the scene root, so a floor rebuild never touches it and health carries
/// over), drives the DungeonBuilder through the floor table (4 biomes x 3 floors, then the Bone Colossus), and
/// moves the player between the village and the floors with a fade. Death sends the player home with nothing;
/// a return through an exit portal banks the shards carried. Everything between floors happens under black at
/// the village spawn, so the player never stands on geometry that is being destroyed.
/// </summary>
[Title( "Run Manager" ), Category( "Void Mage" ), Icon( "route" )]
public sealed class RunManager : Component
{
	/// <summary>The player prefab (prefabs/player.prefab), cloned once at VillageSpawn.</summary>
	[Property] public GameObject PlayerPrefab { get; set; }
	/// <summary>Where the player stands in the village: spawn, return, death, and the hold position between floors.</summary>
	[Property] public GameObject VillageSpawn { get; set; }
	/// <summary>Degrees added to the VillageSpawn object's own yaw for the way the player faces there. 180 (user, 2026-09-21): they come home looking the other way; the object itself is this component's, so it is not turned.</summary>
	[Property, Range( -180f, 180f )] public float VillageSpawnTurn { get; set; } = 180f;
	/// <summary>The dungeon builder object, placed far from the village (say 0, 0, -40000). BuildOnStart and SpawnPlayer are forced off.</summary>
	[Property] public DungeonBuilder Builder { get; set; }
	/// <summary>Objects that exist only in the village (the sun, the sky, village ambience): disabled while on a floor.</summary>
	[Property] public List<GameObject> VillageOnly { get; set; } = new();
	/// <summary>Objects that exist only in the dungeon: enabled while on a floor.</summary>
	[Property] public List<GameObject> DungeonOnly { get; set; } = new();
	/// <summary>The retro filter's depth fog at home: on, with the village ranges below (distant houses sink to black under the night sky). On a floor the scene's own fog setting applies.</summary>
	[Property, Group( "Village Night" )] public bool VillageFog { get; set; } = true;
	[Property, Group( "Village Night" )] public float VillageFogStart { get; set; } = 900f;
	[Property, Group( "Village Night" )] public float VillageFogEnd { get; set; } = 3200f;
	[Property, Range( 0.1f, 2f )] public float FadeSeconds { get; set; } = 0.4f;
	/// <summary>Longest the descending card waits for the floor's navmesh before offering Continue anyway.</summary>
	[Property, Range( 2f, 60f )] public float ReadyTimeout { get; set; } = 20f;
	/// <summary>Seconds the "you died" card stays up before the village fades in.</summary>
	[Property, Range( 0f, 6f )] public float DeathCardSeconds { get; set; } = 2f;
	/// <summary>Go deeper: the camera is drawn into the exit portal while the view irises shut and the void closes over it.</summary>
	[Property, Group( "Portal Transition" ), Range( 0.3f, 3f )] public float PullSeconds { get; set; } = 1.1f;
	/// <summary>When the void starts closing over the pull (seconds into it); the iris starts at once.</summary>
	[Property, Group( "Portal Transition" ), Range( 0f, 2f )] public float PullFadeDelay { get; set; } = 0.45f;
	/// <summary>Arriving on the next floor: the void and the iris open while the camera settles.</summary>
	[Property, Group( "Portal Transition" ), Range( 0.2f, 3f )] public float ArriveSeconds { get; set; } = 0.9f;
	/// <summary>Optional sound events (none shipped yet): the pull, and the arrival.</summary>
	[Property, Group( "Portal Transition" )] public string PullSound { get; set; } = "";
	[Property, Group( "Portal Transition" )] public string ArriveSound { get; set; } = "";

	/// <summary>The fixed seed a brand-new player's floor 1 uses (until their first run ends); pick it by playing.</summary>
	[ConVar( "run_first_seed" )] public static int FirstSeed { get; set; } = 44;
	/// <summary>Skip the Continue click on the descending card: the floor opens the moment it is ready.</summary>
	[ConVar( "run_autocontinue" )] public static bool AutoContinue { get; set; } = false;
	/// <summary>Dev: no main menu, straight into the stormy village at VillageSpawn as before.</summary>
	[ConVar( "menu_skip" )] public static bool SkipMenu { get; set; } = false;

	public static RunManager Instance { get; private set; }

	/// <summary>Cloned when no PlayerPrefab is assigned.</summary>
	public const string DefaultPlayerPrefab = "prefabs/player.prefab";

	/// <summary>False only in the village of a scene with a RunManager. A test scene without one is all dungeon.</summary>
	public static bool InDungeon => !Instance.IsValid() || Instance._inDungeon;

	public RunState State { get; private set; } = RunState.Village;
	/// <summary>1-based floor of the run; 0 in the village.</summary>
	public int Floor { get; private set; }
	/// <summary>Descending: the floor is built and walkable, the Continue button may show.</summary>
	public bool Ready { get; private set; }
	/// <summary>Homeward: why we are going home, for the card.</summary>
	public bool Died { get; private set; }
	/// <summary>Homeward: shards banked (a return) or lost (a death), for the card.</summary>
	public int ShardsMoved { get; private set; }
	/// <summary>Frogs the last run ended with: released into the village on a return, lost on a death.</summary>
	public int FrogsMoved { get; private set; }
	/// <summary>Descending with a boon to pick: the boon screen is (or is about to be) the card, not the floor title.</summary>
	public bool OfferingBoon { get; private set; }
	/// <summary>Homeward after a death: the death card is waiting on the player to pick the one enchantment they keep.</summary>
	public bool OfferingKeep { get; private set; }
	/// <summary>The enchantments the run ended with, in the order they were taken: the death card's choice. Empty when there was nothing to keep.</summary>
	public IReadOnlyList<Spells.ModifierId> KeepChoices => _keepChoices;
	/// <summary>Homeward after a death: the enchantment the player chose to keep (None: nothing to choose from).</summary>
	public Spells.ModifierId KeptScroll { get; private set; }
	private readonly List<Spells.ModifierId> _keepChoices = new();

	/// <summary>The floor table lives in RunFloors (engine-free, so gentest proves every row generates).</summary>
	public static int LastFloor => RunFloors.LastFloor;
	public static int BossFloor => RunFloors.BossFloor;
	public static string FloorName( int floor ) => RunFloors.Name( floor );

	private GameObject _player;
	private bool _inDungeon = true;   // set properly in OnStart; true so the first SetInDungeon(false) applies
	private int _runSeed;
	private int _transition;
	private float _fadeTarget;
	private bool _continueClicked;
	private bool _insidePortal;
	private bool _bossDone;
	private float _nextBossCheck;
	private GameObject _ui;
	private Rendering.RetroFilter _retro;
	private (bool on, float start, float end) _retroWas;
	private bool _retroStored;
	private Village.VillageSky _sky;
	private readonly List<GameObject> _enterPortals = new();

	protected override void OnStart()
	{
		Instance = this;
		if ( !Game.IsPlaying ) return;
		// the user's "MenuCamera" marker is a real camera (so the framing can be previewed in the editor), and a main
		// one: left on it fights the scene camera for Scene.Camera and draws the game without the retro filter, the
		// weather or the HUD. It is only ever a pose (MainMenuPanel reads it), so its camera goes off before anything looks.
		foreach ( var mark in Scene.Directory.FindByName( MainMenuPanel.CameraMarker ).ToList() )
			foreach ( var cam in mark.Components.GetAll<CameraComponent>( FindMode.EverythingInSelfAndDescendants ).ToList() ) cam.Enabled = false;
		RunSave.Load();
		if ( !Builder.IsValid() ) Builder = Scene.GetAllComponents<DungeonBuilder>().FirstOrDefault();
		if ( Builder.IsValid() )
		{
			Builder.BuildOnStart = false;
			Builder.SpawnPlayer = false;
		}
		else Log.Warning( "[Run] no DungeonBuilder in the scene: the portal cannot start a run" );
		_sky = Village.VillageSky.EnsureOn( GameObject );
		// found now, while they are enabled: a portal switched off for the menu is invisible to every search
		_enterPortals.AddRange( RunPortal.All.Where( p => p.IsValid() && p.Kind == PortalKind.Enter ).Select( p => p.GameObject ) );
		EnsureVillageLife();
		CreateUi();
		SetInDungeon( false );
		if ( SkipMenu )
		{
			SetNight( true );
			SpawnPlayerInVillage();
		}
		else
		{
			// the main menu: a sunny afternoon, no portal, no player; MainMenuPanel calls StormBreaks and EnterFromMenu
			State = RunState.MainMenu;
			SetNight( false );
			SetEnterPortals( false );
			var menu = new GameObject( GameObject, true, "MainMenu" ) { Flags = GameObjectFlags.NotSaved };
			menu.Components.Create<ScreenPanel>().ZIndex = 490;   // under the run's fade (500), so the launch fades in over it
			menu.Components.Create<MainMenuPanel>();
		}
		RunUi.FadeAlpha = 1f;
		_fadeTarget = 0f;
	}

	// ------------------------------------------------------------------ the main menu's hand-over

	/// <summary>Play was clicked: under the flash the village goes to night, the rain is already falling, the portal stands there.</summary>
	internal void StormBreaks()
	{
		SetNight( true );
		SetEnterPortals( true );
		var camera = Scene.Camera;
		if ( camera.IsValid() ) camera.GameObject.Components.Get<Village.VillageWeather>( FindMode.EverythingInSelf )?.StrikeOverhead();
	}

	/// <summary>The menu camera has arrived at the eye: the player is put down right there, looking the same way.</summary>
	internal void EnterFromMenu( Vector3 feet, Angles look )
	{
		if ( State != RunState.MainMenu ) return;
		SpawnPlayerInVillage( new Transform( feet, Rotation.FromYaw( look.yaw ) ) );
		var controller = _player.IsValid() ? _player.Components.Get<PlayerController>( FindMode.EverythingInSelfAndDescendants ) : null;
		if ( controller.IsValid() ) controller.EyeAngles = look;
		State = RunState.Village;
		_insidePortal = true;   // cleared by the next tick unless the spawn really is inside it
	}

	/// <summary>The player object, once there is one.</summary>
	internal GameObject PlayerObject => _player;

	/// <summary>Where the village's portal stands, enabled or not.</summary>
	internal Vector3? EnterPortalPosition => _enterPortals.FirstOrDefault( go => go.IsValid() )?.WorldPosition;

	/// <summary>Day or night at home: the sky and the materials (VillageSky), the rain, the fog.</summary>
	private void SetNight( bool night )
	{
		if ( _inDungeon ) return;
		if ( !_sky.IsValid() ) _sky = Village.VillageSky.EnsureOn( GameObject );
		_sky.Set( night );
		ToggleWeather<Village.VillageWeather>( night );
		ApplyVillageFog();
	}

	private void SetEnterPortals( bool on )
	{
		foreach ( var go in _enterPortals ) if ( go.IsValid() ) go.Enabled = on;
	}

	[ConCmd( "village_night" )]
	public static void VillageNightCommand( bool night )
	{
		if ( Instance.IsValid() ) Instance.SetNight( night );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;
		RunUi.FadeAlpha = MathX.Approach( RunUi.FadeAlpha, _fadeTarget, Time.Delta / MathF.Max( 0.05f, FadeSeconds ) );
		TickPortals();
		TickBoss();
	}

	// ------------------------------------------------------------------ the player

	private void SpawnPlayerInVillage( Transform? where = null )
	{
		if ( _player.IsValid() ) return;
		var at = where.HasValue ? where.Value : VillageSpawn.IsValid() ? new Transform( VillageSpawn.WorldPosition, Rotation.FromYaw( VillageSpawn.WorldRotation.Yaw() + VillageSpawnTurn ) ) : new Transform( WorldPosition + Vector3.Up * 20f );
		if ( PlayerPrefab.IsValid() )
		{
			_player = PlayerPrefab.Clone( new CloneConfig { Transform = at, StartEnabled = true, Name = "Player" } );
		}
		else if ( GameObject.Clone( DefaultPlayerPrefab, new CloneConfig { Transform = at, StartEnabled = true, Name = "Player" } ) is { } fromPath && fromPath.IsValid() )
		{
			_player = fromPath;
		}
		else
		{
			Log.Warning( $"[Run] no PlayerPrefab assigned and {DefaultPlayerPrefab} did not clone: a bare PlayerController is used" );
			_player = new GameObject( true, "Player" ) { WorldTransform = at };
			var pc = _player.Components.Create<PlayerController>();
			pc.ThirdPerson = false;
			pc.UseCameraControls = true;
			pc.UseInputControls = true;
			pc.UseAnimatorControls = false;
		}
		_player.Flags |= GameObjectFlags.NotSaved;
		_player.Components.GetOrCreate<PlayerShards>();
		_player.Components.GetOrCreate<PlayerFrogs>();
		_player.Components.GetOrCreate<Boons.PlayerBoons>();
		_player.Components.GetOrCreate<PortalTransition>();
		Inventory.PlayerInventory.EnsureOn( _player );
		PotionDrinker.EnsureOn( _player );
		_player.Components.Get<PlayerPotions>()?.LoadSaved();
		VoidMage.Character.CharacterAppearance.ApplySaved( _player );
	}

	/// <summary>What lives in the village whether or not a player stands in it yet (the main menu shows it too).</summary>
	private void EnsureVillageLife()
	{
		// the frogs brought home on earlier runs are already hopping about out there
		VoidMage.Village.VillageFrogs.EnsureOn( Scene )?.EnsureLoaded();
		// the shopkeeper's table: finds the props stood on it and lays out this visit's stock
		VoidMage.Village.VillageShop.EnsureOn( Scene );
		// and the shopkeeper behind it: the elkdemon the user stood there, leaning on his boxes until someone walks up
		VoidMage.Village.Shopkeeper.EnsureOn( Scene );
	}

	private static PlayerController Controller
	{
		get
		{
			var health = PlayerHealth.Local;
			return health.IsValid() ? health.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
		}
	}

	private static void SetControls( bool on )
	{
		var c = Controller;
		if ( !c.IsValid() ) return;
		c.UseInputControls = on;
		if ( on ) return;
		c.WishVelocity = Vector3.Zero;
		if ( c.Body.IsValid() ) c.Body.Velocity = Vector3.Zero;
	}

	private Vector3 VillageSpawnPosition => VillageSpawn.IsValid() ? VillageSpawn.WorldPosition : WorldPosition + Vector3.Up * 20f;

	/// <summary>Where the village puts the player down: the frogs let out of the bag scatter from here too.</summary>
	public Vector3 VillageSpawnPoint => VillageSpawnPosition;

	private void TeleportHome()
	{
		Angles? look = VillageSpawn.IsValid() ? new Angles( 0f, VillageSpawn.WorldRotation.Yaw() + VillageSpawnTurn, 0f ) : null;
		PlayerHealth.TeleportLocal( VillageSpawnPosition, look );
	}

	// ------------------------------------------------------------------ portals

	private void TickPortals()
	{
		if ( State is not (RunState.Village or RunState.InFloor) )
		{
			// The exit menu IS the portal in use: "inside" stays latched while it is up, so backing out with "Not yet"
			// does not re-open the card on the same spot the player is standing on (they cannot move while it is open).
			// Every other state forgets, so arriving on a floor inside a portal cannot fire it.
			if ( State != RunState.ExitMenu ) _insidePortal = false;
			PortalBlocked = false;
			return;
		}
		var player = PlayerHealth.Local;
		if ( !player.IsValid() || player.IsDead ) { PortalBlocked = false; return; }
		var wanted = State == RunState.Village ? PortalKind.Enter : PortalKind.Exit;
		var feet = player.FeetPosition;
		bool inside = false;
		foreach ( var portal in RunPortal.All )
		{
			if ( !portal.IsValid() || portal.Kind != wanted || !portal.Active ) continue;
			// nothing is missed (user, 2026-09-20): scrolls the floor still owes a player who fought little wait by the way out
			if ( State == RunState.InFloor && ScrollDrops.Pending > 0 && MathF.Abs( feet.z - portal.WorldPosition.z ) < portal.HalfHeight
				&& feet.WithZ( 0f ).Distance( portal.WorldPosition.WithZ( 0f ) ) < ScrollDrops.ExitRange )
				ScrollDrops.FlushAt( Scene, portal.WorldPosition, feet, portal.GameObject.Parent );
			if ( portal.Contains( feet ) ) { inside = true; break; }
		}
		// Monsters in a fight with the player hold the exit shut (user): the card says so, and the portal stays
		// un-entered so it opens by itself the moment the last one drops the chase, without stepping out and back in.
		PortalBlocked = inside && State == RunState.InFloor && VoidMage.Npc.SkeletonNpc.AnyHunting( player.GameObject );
		if ( PortalBlocked ) { _insidePortal = false; return; }
		// edge-triggered: stepping out and back in after "not yet" re-opens the menu
		if ( inside && !_insidePortal )
		{
			if ( State == RunState.Village ) StartRun();
			else OpenExitMenu();
		}
		_insidePortal = inside;
	}

	/// <summary>The player stands in the exit portal but monsters are hunting them: RunUi shows why nothing happens.</summary>
	public bool PortalBlocked { get; private set; }

	// ------------------------------------------------------------------ the run

	/// <summary>No enemies on tiers whose centre is within this many cells of where the player lands (user, 2026-09-19: 3).</summary>
	public const int SpawnClearCells = 3;
	/// <summary>Chance a corridor holds one enemy (user, 2026-09-17: 0.8, the corridors were the empty half of a floor).</summary>
	public const float CorridorSkeletonChance = 0.8f;

	public void StartRun( int floor = 1 )
	{
		if ( State is RunState.Descending or RunState.Homeward ) return;
		_runSeed = Game.Random.Int( 1, 900000 );
		_bossDone = false;
		if ( State == RunState.Village )
		{
			Inventory.PlayerInventory.Local?.OnRunStart();
			// every run starts clean (user, 2026-09-19): whatever the village did to you stays in the village
			PlayerHealth.Local?.Refill();
			PlayerMana.Local?.Refill();
		}
		// one spell scroll per run at most
		Log.Info( $"[Run] run {RunSave.Data.RunsPlayed + 1} starts at floor {floor}, run seed {_runSeed}" );
		// walked into the village portal: it takes the player the way an exit portal does (dev starts just fade)
		var enter = NearestPortal( PortalKind.Enter );
		var feet = PlayerHealth.Local.IsValid() ? PlayerHealth.Local.FeetPosition : Vector3.Zero;
		bool pulled = State == RunState.Village && enter.IsValid() && PlayerHealth.Local.IsValid() && enter.Contains( feet );
		Guard( Descend( floor, pullInto: pulled ? PortalKind.Enter : null ), "the descent" );
	}

	/// <summary>Dev (the director): every floor laid out with this seed while it is set (0 = the run's own), so a shot can be taken again on the same floor.</summary>
	public static int SeedOverride { get; set; }

	private int SeedFor( int floor )
	{
		if ( SeedOverride > 0 ) return SeedOverride;
		if ( floor == 1 && !RunSave.Data.FirstRunDone ) return FirstSeed;
		return Math.Abs( DungeonRng.Mix( _runSeed, floor * 31 ) % 900000 ) + 1;
	}

	private void OpenExitMenu()
	{
		if ( State != RunState.InFloor ) return;
		State = RunState.ExitMenu;
		SetControls( false );
		RunUi.Blocking = true;
		UI.MouseLock.Release = true;
	}

	/// <summary>Exit menu: back out; the portal re-arms once the player steps out of it.</summary>
	public void CloseExitMenu()
	{
		if ( State != RunState.ExitMenu ) return;
		State = RunState.InFloor;
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
		SetControls( true );
	}

	/// <summary>
	/// The run given up from the Tab panel. The exit portal's "Return home" is gone (user, 2026-09-20): a death keeps
	/// the shards and the frogs anyway, so home was no longer a choice, and the portal only ever goes deeper. This is
	/// the polite way out of a run: what a death does, without the dying.
	/// </summary>
	public void AbandonRun()
	{
		if ( State is not (RunState.InFloor or RunState.ExitMenu or RunState.Boss) ) return;
		Log.Info( $"[Run] abandoned on floor {Floor}" );
		Guard( GoHome( bank: false, died: false ), "the abandon" );
	}

	/// <summary>True while there is a run to give up (the Tab panel shows the button).</summary>
	public bool CanAbandon => State is RunState.InFloor or RunState.Boss;

	public void ChooseContinue()
	{
		if ( State != RunState.ExitMenu ) return;
		Guard( Descend( Floor + 1, throughPortal: true ), "the descent" );
	}

	/// <summary>Descending card: the Continue button.</summary>
	public void ClickContinue() => _continueClicked = true;

	/// <summary>
	/// To a floor under the void. Through an exit portal (Go deeper) the camera is pulled into the portal first and
	/// a boon is picked while the floor below bakes; the pick is the continue. A run's first floor waits on
	/// the Continue button: the village portal pulls the camera in the same way (pullInto), a dev start fades plainly.
	/// </summary>
	private async System.Threading.Tasks.Task Descend( int floor, bool throughPortal = false, PortalKind? pullInto = null )
	{
		if ( throughPortal ) pullInto ??= PortalKind.Exit;
		int token = ++_transition;
		State = RunState.Descending;
		Floor = floor;
		Ready = false;
		_continueClicked = false;
		OfferingBoon = throughPortal && !AutoContinue && Boons.PlayerBoons.Local.IsValid();
		RunUi.Blocking = true;
		UI.MouseLock.Release = true;
		SetControls( false );
		if ( pullInto.HasValue ) { if ( !await PullIntoPortal( token, pullInto.Value ) ) return; }
		else if ( !await FadeTo( 1f, token ) ) return;

		// under black: park the player at home so nothing it stands on is about to be destroyed
		PortalTransition.Local?.Stop();
		TeleportHome();
		Spells.HazardField.Clear();
		if ( floor >= BossFloor )
		{
			await EnterBoss( token );
			return;
		}

		if ( !Builder.IsValid() ) { Log.Warning( "[Run] no DungeonBuilder: cannot descend" ); await GoHome( false, false ); return; }
		Builder.Seed = SeedFor( floor );
		var row = RunFloors.For( floor, Builder.Seed );   // the seed also picks warren, pit or cathedral
		Builder.Depth = floor;
		Builder.Theme = row.Theme;
		Builder.Archetype = row.Archetype;
		Builder.Size = row.Size;
		Builder.Rooms = row.Rooms;
		Builder.SpawnPlayer = false;
		// the head count is the run's, not the scene's: the cap leaves room for the critters on top
		Builder.TargetSkeletons = row.Enemies;
		// so are the spread rules. The scene serialises the builder's own copies, and an old scene value silently beat
		// the code default: the game ran SpawnClearCells 8 and CorridorSkeletonChance 0.4 long after both were "changed".
		Builder.SpawnClearCells = SpawnClearCells;
		Builder.CorridorSkeletonChance = CorridorSkeletonChance;
		Builder.MaxSkeletons = Math.Max( Builder.MaxSkeletons, row.Enemies + 12 );
		Log.Info( $"[Run] floor {floor}: {row.Theme} {row.Archetype} size {row.Size} rooms {row.Rooms} enemies {row.Enemies} seed {Builder.Seed}" );
		Builder.Generate();   // synchronous; the frame hitch is behind the black, before any card starts moving
		RunSave.Data.DeepestFloor = Math.Max( RunSave.Data.DeepestFloor, floor );

		if ( !await WaitToEnter( $"Floor {floor - 1} cleared", () => Builder.NavMeshReady, token ) ) return;
		if ( !Builder.NavMeshReady ) Log.Warning( $"[Run] floor {floor}: navmesh not ready after {ReadyTimeout:0} s; opening anyway" );

		EnterFloor();
		await Arrive( token );
	}

	/// <summary>
	/// Under the void, until the floor may open: with a boon on offer the pick screen deals while the floor bakes and
	/// the run moves on once a card is taken and the floor is ready (the spinner covers any wait after the pick);
	/// otherwise the old card waits on Continue. False when another transition took over.
	/// </summary>
	private async System.Threading.Tasks.Task<bool> WaitToEnter( string offerTitle, Func<bool> floorReady, int token )
	{
		if ( OfferingBoon && !Boons.BoonScreen.Open( offerTitle ) ) OfferingBoon = false;
		float deadline = Time.Now + ReadyTimeout;
		while ( true )
		{
			if ( !Ready && (floorReady() || Time.Now >= deadline) ) Ready = true;
			Boons.BoonScreen.Waiting = !Ready;
			if ( Ready && (OfferingBoon ? Boons.BoonScreen.IsDone : _continueClicked || AutoContinue) ) break;
			await GameTask.DelaySeconds( 0.05f );
			if ( token != _transition || !Scene.IsValid() ) return false;
		}
		Boons.BoonScreen.Waiting = false;
		if ( OfferingBoon ) Boons.BoonScreen.Close();
		return true;
	}

	/// <summary>Go deeper: the camera is drawn into the exit portal, the iris closes, then the void.</summary>
	private async System.Threading.Tasks.Task<bool> PullIntoPortal( int token, PortalKind kind )
	{
		var portal = NearestPortal( kind );
		var transition = PortalTransition.Local;
		if ( portal.IsValid() && transition.IsValid() ) transition.BeginPull( portal.WorldPosition, PullSeconds );
		if ( !string.IsNullOrEmpty( PullSound ) ) Audio.GameAudio.Play2D( PullSound );
		float start = Time.Now;
		float fadeLength = MathF.Max( 0.05f, PullSeconds - PullFadeDelay );
		while ( true )
		{
			float t = Time.Now - start;
			RunUi.IrisAlpha = Ease.InQuad( Ease.Window( t, 0f, PullSeconds * 0.8f ) );
			SetFadeNow( Ease.InCubic( Ease.Window( t, PullFadeDelay, fadeLength ) ) );
			if ( t >= PullSeconds ) break;
			await GameTask.DelaySeconds( 0.01f );
			if ( token != _transition || !Scene.IsValid() || !GameObject.IsValid() ) return false;
		}
		SetFadeNow( 1f );
		RunUi.IrisAlpha = 1f;
		return true;
	}

	/// <summary>On the new floor: the camera settles out of the pull while the void and the iris open.</summary>
	private async System.Threading.Tasks.Task Arrive( int token )
	{
		PortalTransition.Local?.BeginArrive( ArriveSeconds );
		if ( !string.IsNullOrEmpty( ArriveSound ) ) Audio.GameAudio.Play2D( ArriveSound );
		float start = Time.Now;
		RunUi.IrisAlpha = MathF.Max( RunUi.IrisAlpha, 0.6f );
		float irisFrom = RunUi.IrisAlpha;
		while ( true )
		{
			float t = Time.Now - start;
			SetFadeNow( 1f - Ease.OutCubic( Ease.Window( t, 0f, ArriveSeconds * 0.75f ) ) );
			RunUi.IrisAlpha = irisFrom * (1f - Ease.OutQuad( Ease.Window( t, ArriveSeconds * 0.15f, ArriveSeconds * 0.85f ) ));
			if ( t >= ArriveSeconds ) break;
			await GameTask.DelaySeconds( 0.01f );
			if ( token != _transition || !Scene.IsValid() || !GameObject.IsValid() ) return;
		}
		SetFadeNow( 0f );
		RunUi.IrisAlpha = 0f;
	}

	/// <summary>The fade written directly for this frame (a shaped timeline), with the per-frame approach parked on it. Internal for the menu's trailer dive.</summary>
	internal void SetFadeNow( float alpha )
	{
		RunUi.FadeAlpha = alpha;
		_fadeTarget = alpha;
	}

	private RunPortal NearestPortal( PortalKind kind )
	{
		var player = PlayerHealth.Local;
		if ( !player.IsValid() ) return null;
		var feet = player.FeetPosition;
		return RunPortal.All.Where( p => p.IsValid() && p.Kind == kind ).OrderBy( p => p.WorldPosition.DistanceSquared( feet ) ).FirstOrDefault();
	}

	private void EnterFloor()
	{
		SetInDungeon( true );
		var spawn = Builder.PlayerSpawnWorld ?? Builder.WorldPosition + Vector3.Up * 20f;
		Angles? look = null;
		var exit = Builder.ExitWorld;
		if ( exit.HasValue )
		{
			var toExit = (exit.Value - spawn).WithZ( 0f );
			if ( !toExit.IsNearZeroLength ) look = Rotation.LookAt( toExit.Normal ).Angles();
		}
		PlayerHealth.TeleportLocal( spawn, look );
		State = RunState.InFloor;
		OfferingBoon = false;
		_insidePortal = false;
		SetControls( true );
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
		// a brand-new player's first floor: a scroll waits right in front of them, so the run opens with E, Tab and
		// the inventory tour (user, 2026-09-22). Swept with the floor like every other scroll (a child of the builder).
		if ( Floor == 1 && !RunSave.Data.FirstRunDone )
		{
			var forward = look.HasValue ? look.Value.Forward : (PlayerHealth.Local.IsValid() ? PlayerHealth.Local.WorldRotation.Forward : Vector3.Forward);
			ScrollDrops.GiftAt( Scene, spawn, forward, Builder.GameObject );
		}
	}

	private async System.Threading.Tasks.Task EnterBoss( int token )
	{
		if ( Builder.IsValid() ) Builder.Clear();
		SetInDungeon( true );
		// the arena's own presentation (haze off, its fog) runs inside Build and is undone by its Clear, so it
		// comes AFTER the dungeon toggles and its Clear runs BEFORE the village toggles on the way home
		Boss.HellArena.Build( Scene, new Vector3( Boss.HellArena.DevOriginX, 0f, 0f ) );
		Boss.HellArena.SpawnBossHost( Scene );
		_ = Boss.HellArena.BakeNavMeshAsync( Scene );   // under the card: the summon's minions walk on it
		if ( !await WaitToEnter( "Before the Colossus", () => true, token ) ) return;
		Boss.HellArena.TeleportLocalPlayer();
		State = RunState.Boss;
		OfferingBoon = false;
		_bossDone = false;
		SetControls( true );
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
		await Arrive( token );
	}

	private void TickBoss()
	{
		if ( State != RunState.Boss || _bossDone || Time.Now < _nextBossCheck ) return;
		_nextBossCheck = Time.Now + 0.5f;
		var brain = Scene.GetAllComponents<Boss.BossBrain>().FirstOrDefault();
		if ( !brain.IsValid() || brain.Phase != Boss.BossPhase.Dead ) return;
		_bossDone = true;
		Guard( BossWon(), "the victory" );
	}

	/// <summary>
	/// Every transition runs under this: one that throws (a generator, the arena, a save) used to vanish with the run
	/// stuck under the black, controls off and no card. Now it is logged and the run goes home; if the way home throws
	/// too, the village is forced back by hand.
	/// </summary>
	private async void Guard( System.Threading.Tasks.Task step, string what )
	{
		try { await step; }
		catch ( Exception e )
		{
			Log.Error( $"[Run] {what} failed; going home: {e}" );
			if ( !Scene.IsValid() || !GameObject.IsValid() ) return;
			try { await GoHome( bank: false, died: false ); }
			catch ( Exception again )
			{
				Log.Error( $"[Run] the way home failed too; forcing the village: {again}" );
				ForceHome();
			}
		}
	}

	/// <summary>The village by force, no card and no fade: the last resort under <see cref="Guard"/>.</summary>
	private void ForceHome()
	{
		_transition++;
		OfferingBoon = false;
		OfferingKeep = false;
		try { Boons.BoonScreen.Abort(); PortalTransition.Local?.Stop(); } catch ( Exception e ) { Log.Warning( $"[Run] force home: {e.Message}" ); }
		try { if ( Boss.HellArena.Built ) Boss.HellArena.Clear(); } catch ( Exception e ) { Log.Warning( $"[Run] force home, arena: {e.Message}" ); }
		try { if ( Builder.IsValid() ) Builder.Clear(); Spells.HazardField.Clear(); } catch ( Exception e ) { Log.Warning( $"[Run] force home, floor: {e.Message}" ); }
		try { SetInDungeon( false ); TeleportHome(); } catch ( Exception e ) { Log.Warning( $"[Run] force home, village: {e.Message}" ); }
		State = RunState.Village;
		Floor = 0;
		VictoryAt = 0f;
		_insidePortal = true;
		SetControls( true );
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
		SetFadeNow( 0f );
		RunUi.IrisAlpha = 0f;
	}

	/// <summary>When the Colossus's victory runes came up (Time.Now); 0 while they are down.</summary>
	public float VictoryAt { get; private set; }
	/// <summary>How long the victory runes stand over the fallen skull before the way home begins.</summary>
	[Property, Range( 2f, 8f )] public float VictoryHoldSeconds { get; set; } = 4.5f;

	/// <summary>The skull is dead: the death scene plays out (the brain's DeathDone; 14 s at the outside), the runes stand where the bones burst, then home with everything banked.</summary>
	private async System.Threading.Tasks.Task BossWon()
	{
		Log.Info( "[Run] the Colossus is dead" );
		var brain = Scene.GetAllComponents<Boss.BossBrain>().FirstOrDefault();
		var deadline = Time.Now + 14f;
		while ( brain.IsValid() && !brain.DeathDone && Time.Now < deadline )
		{
			await GameTask.DelaySeconds( 0.1f );
			if ( !Scene.IsValid() || State != RunState.Boss ) return;
		}
		VictoryAt = Time.Now;
		await GameTask.DelaySeconds( VictoryHoldSeconds );
		if ( !Scene.IsValid() || State != RunState.Boss ) return;
		await GoHome( bank: true, died: false );
	}

	/// <summary>
	/// The way home, from anywhere. SHARDS AND FROGS ALWAYS COME HOME (user, 2026-09-20: shards saved up buy the shop's
	/// scrolls, which stand in for the ones left lying in the dungeon; frogs are a quirk nobody should be punished
	/// for); the run's scrolls and boons never do. `bank` is only the potions now: a won run keeps the belt, a death
	/// or an abandoned run goes back to the starting one of each. The floor is torn down, the village comes back and
	/// the save is written. `died` cuts to black at once (the death camera has already played) and holds the card a
	/// moment; anything else fades.
	/// </summary>
	private async System.Threading.Tasks.Task GoHome( bool bank, bool died )
	{
		int token = ++_transition;
		State = RunState.Homeward;
		Died = died;
		Ready = false;
		RunUi.Blocking = true;
		UI.MouseLock.Release = true;
		SetControls( false );
		var shards = PlayerShards.Local;
		ShardsMoved = shards.IsValid() ? shards.Bank() : 0;
		// counted now for the card; the bag itself is emptied after the way home (below)
		FrogsMoved = PlayerFrogs.Local?.Carried ?? 0;
		// the run's boons end with it (a return or a death); cleared before the potions bank, so a Deep Pockets
		// surplus is dropped rather than saved past the normal capacity
		OfferingBoon = false;
		Boons.BoonScreen.Abort();
		Boons.PlayerBoons.Local?.Clear();
		var potions = PlayerPotions.Local;
		if ( potions.IsValid() ) { if ( bank ) potions.Bank(); else potions.Lose(); }
		// the run's scrolls come off the staff either way. A DEATH keeps one of them: the choice is made on the death
		// card, under the black, so the staff is only stripped once the pick is in (below); a run with nothing on the
		// staff has nothing to choose and goes straight on (user, 2026-09-22: "1 to keep, thats it nothing more").
		var inventory = Inventory.PlayerInventory.Local;
		KeptScroll = Spells.ModifierId.None;
		_keepChoices.Clear();
		if ( inventory.IsValid() )
		{
			if ( died ) _keepChoices.AddRange( inventory.RunScrolls );
			if ( bank ) inventory.OnReturnHome();
			else if ( _keepChoices.Count == 0 ) inventory.OnDeath( Spells.ModifierId.None );
		}
		if ( died ) { RunUi.FadeAlpha = 1f; _fadeTarget = 1f; }
		else if ( !await FadeTo( 1f, token ) ) return;
		PortalTransition.Local?.Stop();
		RunUi.IrisAlpha = 0f;

		bool wasFirstRun = !RunSave.Data.FirstRunDone;
		RunSave.Data.FirstRunDone = true;
		RunSave.Data.RunsPlayed++;
		RunSave.Save();
		Log.Info( $"[Run] home from floor {Floor}: {(died ? "died" : "returned")}, {ShardsMoved} shards and {FrogsMoved} frogs kept, {RunSave.Data.BankedShards} in the bank{(wasFirstRun ? " (first run done: floor 1 is random from now on)" : "")}" );

		if ( Boss.HellArena.Built ) Boss.HellArena.Clear();
		if ( Builder.IsValid() ) Builder.Clear();
		Spells.HazardField.Clear();
		SetInDungeon( false );
		TeleportHome();
		// in and out of the dungeon is a new visit: the shop's table is laid again (under the black)
		VoidMage.Village.VillageShop.Restock();
		// the bag is emptied AFTER the way home, so the frogs hop out where the player lands rather than where the
		// run ended
		PlayerFrogs.Local?.Bank();

		if ( died && _keepChoices.Count > 0 )
		{
			// the pick: the card holds until one tile is clicked (no way past it; there is always something to keep)
			OfferingKeep = true;
			while ( KeptScroll == Spells.ModifierId.None )
			{
				await GameTask.DelaySeconds( 0.05f );
				if ( token != _transition || !Scene.IsValid() ) { OfferingKeep = false; return; }
			}
			OfferingKeep = false;
			if ( inventory.IsValid() ) inventory.OnDeath( KeptScroll );
		}
		if ( died && DeathCardSeconds > 0f )
		{
			await GameTask.DelaySeconds( DeathCardSeconds );
			if ( token != _transition || !Scene.IsValid() ) return;
		}
		State = RunState.Village;
		Floor = 0;
		VictoryAt = 0f;
		_insidePortal = true;   // the village portal must be left and re-entered; never fire on arrival
		SetControls( true );
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
		await FadeTo( 0f, token );
	}

	/// <summary>Death card: the one enchantment that stays on the staff. Only while the card is asking, only one of the run's own.</summary>
	public void ChooseKeep( Spells.ModifierId id )
	{
		if ( !OfferingKeep || KeptScroll != Spells.ModifierId.None || !_keepChoices.Contains( id ) ) return;
		KeptScroll = id;
		Log.Info( $"[Run] keeping {Inventory.Scroll.ForModifier( id ).Name} from the run" );
	}

	// ------------------------------------------------------------------ death (called by PlayerHealth)

	/// <summary>Owner died: any menu closes, any transition in flight is abandoned; the death camera plays as before.</summary>
	public void OnPlayerDied()
	{
		_transition++;
		if ( State == RunState.ExitMenu ) State = RunState.InFloor;
		OfferingBoon = false;
		Boons.BoonScreen.Abort();
		PortalTransition.Local?.Stop();
		RunUi.IrisAlpha = 0f;
		RunUi.Blocking = false;
		UI.MouseLock.Release = UI.SpellLoadout.IsOpen;
	}

	/// <summary>PlayerHealth.Respawn, after the death camera: home with nothing. True when this took over the respawn
	/// (and its controls); a death in the village itself is an ordinary respawn on the spot.</summary>
	public bool OnPlayerRespawned()
	{
		if ( State == RunState.Village ) { TeleportHome(); PlayerPotions.Local?.TopUpForRespawn(); return false; }
		Guard( GoHome( bank: false, died: true ), "the death" );
		return true;
	}

	// ------------------------------------------------------------------ village / dungeon toggles

	private void SetInDungeon( bool on )
	{
		if ( _inDungeon == on ) return;
		_inDungeon = on;
		foreach ( var go in VillageOnly ) if ( go.IsValid() ) go.Enabled = !on;
		foreach ( var go in DungeonOnly ) if ( go.IsValid() ) go.Enabled = on;
		// the dungeon air lives on the camera; EnsureHaze leaves a disabled one alone, so both directions are ours
		ToggleWeather<DungeonHaze>( on );
		// the village's night rain is its mirror image: on the same camera, on only at home (and only once the storm has broken)
		EnsureWeather();
		ToggleWeather<Village.VillageWeather>( !on && Village.VillageSky.Night );
		// the retro filter's depth fog: the scene's own setting on a floor, the village night ranges at home
		if ( !_retro.IsValid() ) _retro = Scene.GetAllComponents<Rendering.RetroFilter>().FirstOrDefault();
		if ( _retro.IsValid() )
		{
			if ( !_retroStored ) { _retroWas = (_retro.DepthFog, _retro.FogStart, _retro.FogEnd); _retroStored = true; }
			if ( on )
			{
				_retro.DepthFog = _retroWas.on;
				_retro.FogStart = _retroWas.start;
				_retro.FogEnd = _retroWas.end;
			}
			else ApplyVillageFog();
		}
		if ( !on && Builder.IsValid() ) Builder.Components.Get<UI.DungeonMinimap>()?.Rebuild( null );
	}

	/// <summary>The depth fog at home: the night ranges that sink the far houses to black, and none at all on the menu's sunny day.</summary>
	private void ApplyVillageFog()
	{
		if ( _inDungeon || !_retro.IsValid() || !_retroStored ) return;
		_retro.DepthFog = VillageFog && Village.VillageSky.Night;
		_retro.FogStart = VillageFogStart;
		_retro.FogEnd = VillageFogEnd;
	}

	/// <summary>
	/// Enable or disable every component of one weather kind. Scene.GetAllComponents only ever returns ENABLED
	/// components (the engine says so), so a haze switched off at home could never be found again to switch
	/// back on for a floor: the camera object, where both weathers live, is searched with disabled ones included.
	/// </summary>
	private void ToggleWeather<T>( bool on ) where T : Component
	{
		var camera = Scene.Camera;
		if ( camera.IsValid() )
			foreach ( var c in camera.GameObject.Components.GetAll<T>( FindMode.EverythingInSelf ).ToList() ) c.Enabled = on;
		foreach ( var c in Scene.GetAllComponents<T>().ToList() ) c.Enabled = on;
	}

	/// <summary>
	/// The village weather (VillageWeather) lives on the scene camera like the haze and would be lost the same
	/// way to an editor re-save over a disk edit: if the camera has none, create one with defaults.
	/// </summary>
	private void EnsureWeather()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		if ( camera.GameObject.Components.Get<Village.VillageWeather>( FindMode.EverythingInSelf ) is not null ) return;
		camera.GameObject.Components.Create<Village.VillageWeather>();
		Log.Info( "[Run] no VillageWeather on the camera; created one with defaults (add it to the scene to tune it)" );
	}

	// ------------------------------------------------------------------ ui

	private void CreateUi()
	{
		if ( _ui.IsValid() ) return;
		_ui = new GameObject( GameObject, true, "RunUi" ) { Flags = GameObjectFlags.NotSaved };
		var screen = _ui.Components.Create<ScreenPanel>();
		screen.ZIndex = 500;   // above the player's HUD panel (100)
		_ui.Components.Create<RunUi>();

		// the boon pick sits over the run's void (its own panel, so its cards draw above the fade)
		var boons = new GameObject( _ui, true, "BoonScreen" ) { Flags = GameObjectFlags.NotSaved };
		boons.Components.Create<ScreenPanel>().ZIndex = 510;
		boons.Components.Create<Boons.BoonScreen>();
	}

	/// <summary>Drives the fade toward `target`; false when another transition took over meanwhile.</summary>
	private async System.Threading.Tasks.Task<bool> FadeTo( float target, int token )
	{
		_fadeTarget = target;
		while ( MathF.Abs( RunUi.FadeAlpha - target ) > 0.01f )
		{
			await GameTask.DelaySeconds( 0.02f );
			if ( token != _transition || !Scene.IsValid() || !GameObject.IsValid() ) return false;
		}
		RunUi.FadeAlpha = target;
		return true;
	}

	// ------------------------------------------------------------------ dev commands

	/// <summary>`run_start [floor]`: start a run at a floor (default 1) from wherever you stand.</summary>
	[ConCmd( "run_start" )]
	public static void StartCommand( int floor = 1 )
	{
		if ( !Instance.IsValid() ) { Log.Warning( "[Run] no RunManager in the scene" ); return; }
		if ( Instance.State is RunState.Descending or RunState.Homeward ) return;
		if ( Instance.State == RunState.Village ) Instance.StartRun( floor );
		else Instance.Guard( Instance.Descend( floor ), "the descent" );
	}

	/// <summary>`run_next`: the next floor, as if Continue had been chosen.</summary>
	[ConCmd( "run_next" )]
	public static void NextCommand()
	{
		if ( !Instance.IsValid() ) return;
		if ( Instance.State is RunState.InFloor or RunState.ExitMenu ) Instance.Guard( Instance.Descend( Instance.Floor + 1, throughPortal: true ), "the descent" );
		else if ( Instance.State == RunState.Village ) Instance.StartRun();
	}

	/// <summary>`run_debug`: where the camera, the player, the skeletons and the nearest flame rigs actually are (the log).</summary>
	[ConCmd( "run_debug" )]
	public static void DebugCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var cam = scene.Camera;
		var eye = cam.IsValid() ? cam.WorldPosition : Vector3.Zero;
		var health = PlayerHealth.Local;
		var run = Instance;
		var feet = health.IsValid() ? health.FeetPosition : Vector3.Zero;
		Log.Info( $"[RunDebug] state {(run.IsValid() ? run.State.ToString() : "no RunManager")} floor {run?.Floor ?? 0} inDungeon {InDungeon}; camera {eye:0}; player {(health.IsValid() ? $"{feet:0}" : "none")}; PlayerHealth components {scene.GetAllComponents<PlayerHealth>().Count()}" );
		if ( run.IsValid() && run.Builder.IsValid() )
		{
			var spawn = run.Builder.PlayerSpawnWorld ?? Vector3.Zero;
			var exit = run.Builder.ExitWorld ?? Vector3.Zero;
			Log.Info( $"[RunDebug] builder at {run.Builder.WorldPosition:0}, spawn {spawn:0}, exit {exit:0}, navmesh ready {run.Builder.NavMeshReady}" );
		}
		var npcs = scene.GetAllComponents<Npc.SkeletonNpc>().Where( n => n.IsValid() ).OrderBy( n => n.WorldPosition.Distance( eye ) ).ToList();
		Log.Info( $"[RunDebug] skeletons {npcs.Count}, dormant {npcs.Count( n => n.Dormant )}, dead {npcs.Count( n => n.IsDead )}" );
		foreach ( var n in npcs.Take( 5 ) )
			Log.Info( $"[RunDebug]   {n.KindName,-14} at {n.WorldPosition:0} d {n.WorldPosition.Distance( eye ):0}{(n.Dormant ? " DORMANT" : "")}{(n.IsDead ? " dead" : "")} parent {n.GameObject.Parent?.Name}" );
		var fires = scene.GetAllComponents<FireLight>().Where( f => f.IsValid() && !f.GameObject.Tags.Has( "spellfire" ) ).OrderBy( f => f.WorldPosition.Distance( eye ) ).ToList();
		Log.Info( $"[RunDebug] fire lights {fires.Count}, with rigs {fires.Count( f => f.Rig != null && f.Rig.Root.IsValid() )}, lights enabled {fires.Count( f => f.Light?.Enabled == true )}" );
		foreach ( var f in fires.Take( 4 ) )
		{
			var rig = f.Rig;
			var root = rig?.Root;
			var line = $"[RunDebug]   {f.Kind} at {f.WorldPosition:0} d {f.WorldPosition.Distance( eye ):0} light {(f.Light?.Enabled == true ? "on" : "off")}";
			if ( !root.IsValid() ) { Log.Info( line + " NO RIG" ); continue; }
			line += $" rig at {root.WorldPosition:0} (d {root.WorldPosition.Distance( eye ):0}) enabled {root.Enabled} active {root.Active} layers {root.Children.Count}:";
			foreach ( var child in root.Children.Take( 6 ) )
			{
				var fx = child.Components.Get<ParticleEffect>( FindMode.EverythingInSelfAndDescendants );
				var r = child.Components.Get<ParticleSpriteRenderer>( FindMode.EverythingInSelfAndDescendants );
				line += $" [{child.Name} particles {fx?.ParticleCount ?? -1} renderer {(r.IsValid() ? (r.Enabled ? "on" : "off") : "none")}]";
			}
			Log.Info( line );
		}
	}

	/// <summary>`run_home`: back to the village, banking what is carried.</summary>
	[ConCmd( "run_home" )]
	public static void HomeCommand()
	{
		if ( !Instance.IsValid() || Instance.State is RunState.Village or RunState.Homeward ) return;
		Instance.Guard( Instance.GoHome( bank: true, died: false ), "the way home" );
	}
}
