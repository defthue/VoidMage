using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Boss;
using VoidMage.Character;
using VoidMage.Dungeon;
using VoidMage.Npc;
using VoidMage.Run;

namespace VoidMage.Dev;

/// <summary>
/// The director's stateless actions, one static method per button (DevPanel): where to go, what to spawn, what
/// the world does, what the player carries, the shots, and the trailer's intro. Anything with state of its own
/// (time, the free camera, the hud) is on DevDirector. Console commands that already did these things stay as
/// they were; these call the same code.
/// </summary>
public static class DevActions
{
	private static Scene Scene => Game.ActiveScene;
	private static RunManager Run => RunManager.Instance;
	private static DungeonBuilder Builder => Run.IsValid() && Run.Builder.IsValid() ? Run.Builder : Scene?.GetAllComponents<DungeonBuilder>().FirstOrDefault();

	// ------------------------------------------------------------------ where the player looks

	/// <summary>The point the camera looks at (the free camera's when it flies), or null looking into nothing.</summary>
	public static Vector3? AimPoint( float reach = 6000f )
	{
		var scene = Scene;
		if ( !scene.IsValid() ) return null;
		Vector3 from; Vector3 dir;
		if ( DevDirector.FreeCam ) { var (p, a) = DevDirector.CamPose; from = p; dir = a.Forward; }
		else
		{
			var cam = scene.Camera;
			if ( !cam.IsValid() ) return null;
			from = cam.WorldPosition; dir = cam.WorldRotation.Forward;
		}
		var tr = scene.Trace.Ray( from, from + dir * reach ).WithoutTags( "player", "viewmodel", "posesource", "trigger", "spellfire" ).Run();
		return tr.Hit ? tr.HitPosition : null;
	}

	/// <summary>The floor under `at` (a trace down), or `at` itself.</summary>
	private static Vector3 Ground( Vector3 at, float up = 150f, float down = 400f )
	{
		var scene = Scene;
		if ( !scene.IsValid() ) return at;
		var tr = scene.Trace.Ray( at + Vector3.Up * up, at - Vector3.Up * down ).WithoutTags( "player", "viewmodel", "posesource", "trigger", "npc", "enemy", "ally", "debris", "ragdoll", "spellfire" ).Run();
		return tr.Hit ? tr.HitPosition : at;
	}

	// ------------------------------------------------------------------ teleports

	/// <summary>Home, banking (the fade and the village as a return would).</summary>
	public static void GoVillage() => RunManager.HomeCommand();

	/// <summary>A floor of the run (1..4) or the boss (5), from wherever you stand. `SkipCards` makes it instant: the descending card is not waited on.</summary>
	public static void GoFloor( int floor )
	{
		if ( !Run.IsValid() ) { Log.Warning( "[Director] no run in this scene" ); return; }
		RunManager.StartCommand( Math.Clamp( floor, 1, RunManager.BossFloor ) );
	}

	public static void GoNextFloor() => RunManager.NextCommand();

	/// <summary>The descending cards skipped (run_autocontinue): the floor opens the moment it is ready.</summary>
	public static bool SkipCards { get => RunManager.AutoContinue; set => RunManager.AutoContinue = value; }

	/// <summary>The seed the next floors are laid out with (0 = the run's own): the same floor again for another take.</summary>
	public static int Seed { get => RunManager.SeedOverride; set => RunManager.SeedOverride = Math.Max( 0, value ); }

	/// <summary>The current floor's seed, to copy into <see cref="Seed"/> and come back to it.</summary>
	public static int CurrentSeed => Builder.IsValid() ? Builder.Seed : 0;

	public static void UseCurrentSeed() => Seed = CurrentSeed;

	/// <summary>The player's feet to `at`, looking `yaw` (or where they look now).</summary>
	public static void TeleportTo( Vector3 at, float? yaw = null )
	{
		var c = PlayerHealth.Local.IsValid() ? PlayerHealth.Local.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
		Angles? look = yaw.HasValue ? new Angles( 0f, yaw.Value, 0f ) : (c.IsValid() ? c.EyeAngles : null);
		PlayerHealth.TeleportLocal( at, look );
		if ( DevDirector.FreeCam ) DevDirector.SnapCamToPlayer();
	}

	/// <summary>Where the camera looks: the player lands there.</summary>
	public static void TeleportToAim()
	{
		if ( AimPoint() is not { } p ) { Log.Info( "[Director] looking at nothing" ); return; }
		TeleportTo( p + Vector3.Up * 4f );
	}

	/// <summary>Every tier of the floor with a tag (spawn, exit, shrine, ambush, loot), for the panel's list.</summary>
	public static Tier[] TaggedTiers => Builder.IsValid() && Builder.Layout is { } l ? l.Tagged.ToArray() : Array.Empty<Tier>();

	/// <summary>The rooms and halls of the floor, biggest first.</summary>
	public static Tier[] Rooms => Builder.IsValid() ? Builder.Tiers.Where( t => t.Kind is TierKind.Room or TierKind.Hall or TierKind.Tower && t.Cells.Count > 0 ).OrderByDescending( t => t.Cells.Count ).ToArray() : Array.Empty<Tier>();

	public static void TeleportToTier( Tier t )
	{
		if ( !Builder.IsValid() || t is null ) return;
		var at = Builder.TierWorld( t );
		TeleportTo( Ground( at, 120f, 300f ) + Vector3.Up * 4f );
	}

	public static void TeleportToTag( TierTag tag )
	{
		var t = Builder.IsValid() ? Builder.Layout?.Find( tag ) : null;
		if ( t is null ) { Log.Info( $"[Director] this floor has no {tag} tier" ); return; }
		TeleportToTier( t );
	}

	/// <summary>The exit portal itself (the prop, not the tier's middle).</summary>
	public static void TeleportToExit()
	{
		if ( Builder.IsValid() && Builder.ExitWorld is { } exit ) TeleportTo( Ground( exit + Vector3.Up * 10f ) + Vector3.Up * 4f );
		else Log.Info( "[Director] no exit on this floor" );
	}

	public static void TeleportToShrine()
	{
		var shrine = ManaShrine.All.FirstOrDefault( s => s.IsValid() );
		if ( shrine is null ) { Log.Info( "[Director] no shrine on this floor" ); return; }
		var at = shrine.WorldPosition + shrine.WorldRotation.Forward * 120f;
		TeleportTo( Ground( at ) + Vector3.Up * 4f, (shrine.WorldPosition - at).WithZ( 0f ).Normal.EulerAngles.yaw );
	}

	public static void TeleportToArenaSpawn() => HellArena.TeleportLocalPlayer();

	// ------------------------------------------------------------------ spawning

	public static string[] SkeletonKinds => SkeletonSpawner.Kinds.Where( k => !k.Name.StartsWith( "monster_" ) ).Select( k => k.Name ).ToArray();
	public static string[] MonsterKindNames => SkeletonSpawner.Kinds.Where( k => k.Name.StartsWith( "monster_" ) ).Select( k => k.Name ).ToArray();

	/// <summary>`count` of a kind where the camera looks (or 300 in front of the player), fanned out, hostile or on the player's side.</summary>
	public static void SpawnEnemies( string kind, int count, bool ally = false )
	{
		var scene = Scene;
		var player = PlayerHealth.Local;
		if ( !scene.IsValid() || !player.IsValid() ) return;
		if ( !Networking.IsHost ) { Log.Warning( "[Director] host only" ); return; }
		var cam = scene.Camera;
		var forward = cam.IsValid() ? cam.WorldRotation.Forward.WithZ( 0f ).Normal : Vector3.Forward;
		var centre = AimPoint( 2500f ) ?? player.FeetPosition + forward * 300f;
		var dungeon = Builder;
		var team = ally ? Combat.Team.Ally : Combat.Team.Hostile;
		var n = Math.Max( 1, count );
		var spawned = 0;
		for ( var i = 0; i < n; i++ )
		{
			var ring = i == 0 ? 0f : 70f + 40f * (i / 6);
			var pos = centre + Rotation.FromYaw( i * 360f / Math.Max( 1, Math.Min( n, 6 ) ) + (i / 6) * 30f ).Forward * ring;
			var nav = scene.NavMesh?.GetClosestPoint( pos, 250f );
			if ( nav.HasValue ) pos = nav.Value;
			var go = SkeletonSpawner.Spawn( scene, pos + Vector3.Up * 4f, Rotation.LookAt( -forward ), dungeon.IsValid() ? dungeon.GameObject : null,
				kind, "", -1, -1, false, team, ally ? player.GameObject : null, ally ? 2 : 0 );
			if ( go.IsValid() ) spawned++;
		}
		Log.Info( $"[Director] spawned {spawned}x {kind}{(ally ? " (ally)" : "")} at {centre:0}" );
	}

	/// <summary>A mixed wave: the floor's own roster when on a floor, else a spread of kinds.</summary>
	public static void SpawnWave( int count )
	{
		var kinds = new[] { "slave", "soldier_01", "soldier_02", "corpse", "monster_bat", "monster_rat", "monster_spider" };
		for ( var i = 0; i < Math.Max( 1, count ); i++ ) SpawnEnemies( kinds[i % kinds.Length], 1 );
	}

	public static void KillAllEnemies() => SkeletonSpawner.KillAllCommand();

	/// <summary>Every skeleton asleep (their idle) or awake.</summary>
	public static void SleepAll( bool asleep )
	{
		var scene = Scene;
		if ( !scene.IsValid() || !Networking.IsHost ) return;
		foreach ( var npc in scene.GetAllComponents<SkeletonNpc>() ) if ( npc.IsValid() && !npc.IsDead ) npc.SetDormant( asleep );
	}

	public static int EnemiesAlive => Scene.IsValid() ? Scene.GetAllComponents<SkeletonNpc>().Count( n => n.IsValid() && !n.IsDead ) : 0;

	// ------------------------------------------------------------------ pickups

	private static (Vector3 at, GameObject parent) DropSpot()
	{
		var player = PlayerHealth.Local;
		var at = player.IsValid() ? player.WorldPosition + player.WorldRotation.Forward.WithZ( 0f ).Normal * 80f + Vector3.Up * 30f : Vector3.Zero;
		if ( AimPoint( 600f ) is { } aim ) at = aim + Vector3.Up * 30f;
		return (at, Builder.IsValid() ? Builder.GameObject : null);
	}

	public static void DropScroll( Inventory.Scroll scroll )
	{
		var (at, parent) = DropSpot();
		ScrollPickup.Spawn( Scene, at, scroll, parent, Vector3.Zero );
	}

	public static void DropRandomScroll() => Inventory.PlayerInventory.DropCommand( "random" );
	public static void DropSpellScroll() => Inventory.PlayerInventory.DropCommand( "spell" );

	public static void DropPotion( PlayerPotions.Kind kind )
	{
		var (at, parent) = DropSpot();
		PotionPickup.SpawnLoose( Scene, at, kind, parent, Vector3.Zero );
	}

	public static void DropShards( int count )
	{
		var (at, parent) = DropSpot();
		ShardPickup.Drop( Scene, at, count, parent );
	}

	public static void DropSkin() => SkinPickup.DropCommand( "" );

	// ------------------------------------------------------------------ the boss

	public static bool BossHere => BossBrain.Current.IsValid();
	public static string BossState => BossBrain.Current.IsValid() ? $"{BossBrain.Current.Phase}, fight {BossBrain.Current.Fight}, {BossBrain.Current.RunningNames}" : "no boss";

	/// <summary>The stage built at the dev origin with the boss asleep, the player on its spawn (boss_arena).</summary>
	public static void BuildArena() => HellArena.Command( "" );
	public static void ClearArena() => HellArena.Command( "clear" );
	public static void WakeBoss() => BossNet.WakeCommand();
	/// <summary>The three bones spawned afresh, asleep at their markers (the fight from the top without rebuilding the stage).</summary>
	public static void ResetBoss()
	{
		if ( !HellArena.Built ) { Log.Info( "[Director] no arena" ); return; }
		HellArena.SpawnBossHost( Scene );
	}
	public static void BossAttack( string name, string hand = "" ) => BossNet.AttackCommand( name, hand );
	public static void BossPhase( int phase ) => BossNet.PhaseCommand( phase );
	public static void BossKill( string which ) => BossNet.KillCommand( which );
	public static void BossLaugh() => BossBrain.Current?.LaughNow();
	public static void BossJaw( float open ) => BossNet.JawCommand( open );
	public static string[] BossMoves => BossAttacks.All.Select( a => a.Name ).ToArray();

	// ------------------------------------------------------------------ the world

	public static void OpenAllDoors() => DoorNet.SetAll( true );
	public static void CloseAllDoors() => DoorNet.SetAll( false );
	public static void FireAllTraps() => Traps.TrapNet.FireAll();
	public static void BreakAllSecretWalls() => Combat.SecretWallNet.BreakAll();
	public static void SetNight( bool night ) => RunManager.VillageNightCommand( night );
	public static bool Night => Village.VillageSky.Night;
	public static void LightningNow() => Village.VillageWeather.LightningNow();

	public static bool Raining
	{
		get
		{
			var cam = Scene?.Camera;
			return cam.IsValid() && cam.GameObject.Components.GetAll<Village.VillageWeather>( FindMode.EverythingInSelf ).Any( w => w.Enabled );
		}
	}

	public static void SetRain( bool on )
	{
		var cam = Scene?.Camera;
		if ( !cam.IsValid() ) return;
		foreach ( var w in cam.GameObject.Components.GetAll<Village.VillageWeather>( FindMode.EverythingInSelf ).ToList() ) w.Enabled = on;
	}

	public static void Frogs( int count ) => Village.VillageFrogs.SetCommand( count );
	public static void RestockShop() => Village.VillageShop.RestockCommand();

	public static Rendering.RetroFilter Retro => Scene?.GetAllComponents<Rendering.RetroFilter>().FirstOrDefault();

	/// <summary>The background music on or off; off stops whatever is playing right now (the menu's song too), not on the director's next fade.</summary>
	public static void SetMusic( bool on )
	{
		Audio.MusicDirector.Enabled = on;
		if ( !on ) Game.Music.Stop( 0.3f );
	}

	// ------------------------------------------------------------------ the player

	public static void Heal()
	{
		PlayerHealth.Local?.Refill();
		PlayerMana.Local?.Refill();
	}

	public static void Kill() => PlayerHealth.KillLocal();
	public static void Potions( int heal, int mana ) => PlayerPotions.GiveCommand( heal, mana );

	public static bool ThirdPerson
	{
		get
		{
			var c = PlayerHealth.Local.IsValid() ? PlayerHealth.Local.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
			return c.IsValid() && c.ThirdPerson;
		}
		set
		{
			var c = PlayerHealth.Local.IsValid() ? PlayerHealth.Local.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
			if ( c.IsValid() && !DevDirector.FreeCam ) c.ThirdPerson = value;
		}
	}

	/// <summary>The first-person arms rig off (a clean plate through the player's eyes).</summary>
	public static bool ArmsHidden { get => FirstPersonArms.Hidden; set => FirstPersonArms.Hidden = value; }

	private static readonly List<SkinnedModelRenderer> _hiddenBody = new();

	/// <summary>The player's body renderers off (a free-camera shot with nobody in it), or back on.</summary>
	public static bool BodyHidden
	{
		get => _hiddenBody.Count > 0;
		set
		{
			if ( value == BodyHidden ) return;
			if ( !value )
			{
				foreach ( var r in _hiddenBody ) if ( r.IsValid() ) r.Enabled = true;
				_hiddenBody.Clear();
				return;
			}
			var player = PlayerHealth.Local;
			if ( !player.IsValid() ) return;
			var root = player.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors )?.GameObject ?? player.GameObject;
			foreach ( var r in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( !r.Enabled || r.GameObject.Tags.Has( "viewmodel" ) || r.GameObject.Tags.Has( "posesource" ) ) continue;
				r.Enabled = false;
				_hiddenBody.Add( r );
			}
		}
	}

	// ------------------------------------------------------------------ the loadout

	public static void GiveScroll( Inventory.Scroll s, int copies = 1 ) => Inventory.PlayerInventory.GiveCommand( s.Name, copies );
	public static void GiveAllScrolls() => Inventory.PlayerInventory.GiveCommand( "all", 1 );
	public static void ClearStaff() => Inventory.PlayerInventory.ClearCommand();
	public static void UnlockAll() => Inventory.PlayerInventory.UnlockAllCommand();
	public static void SetSpell( string id ) => Spells.SpellDebug.SetCommand( id );
	public static void GiveBoon( string id ) => Boons.PlayerBoons.GiveCommand( id );
	public static void ClearBoons() => Boons.PlayerBoons.ClearCommand();
	public static void Shards( int amount ) => PlayerShards.GiveCommand( amount );

	public static void GiveRandomScrolls( int count ) => Inventory.PlayerInventory.GiveCommand( "random", count );
	public static void FindAllSkins() => Inventory.PlayerInventory.SkinCommand( "all" );
	public static void ResetInventory() => Inventory.PlayerInventory.ResetCommand();
	public static void RefillMana() => PlayerMana.Local?.Refill();
	public static string ManaText => PlayerMana.Local is { } m ? $"{(int)m.Mana}/{(int)m.EffectiveMaxMana}" : "-";

	/// <summary>What the staff would throw right now: the resolved cast's numbers (the old Dev tab's readout).</summary>
	public static string CastReadout
	{
		get
		{
			var l = Spells.StaffLoadout.Local;
			if ( l?.ActiveSpell is null ) return "no spell equipped";
			var caster = Spells.SpellCaster.Local;
			var health = PlayerHealth.Local;
			return Spells.ResolvedCast.Preview( l.ActiveSpell, l.Stacks, PlayerShards.Local?.Carried ?? 0, caster?.KindlingActive ?? false, health.IsValid() ? 1f - health.Health01 : 0f ).ToString();
		}
	}

	/// <summary>A strong end-of-run build in one press: four boons and a full staff of the loud enchantments.</summary>
	public static void TrailerBuild()
	{
		GiveBoon( "honed_edge" ); GiveBoon( "arcane_might" ); GiveBoon( "vigor" ); GiveBoon( "bladestorm" );
		foreach ( var name in new[] { "Multiplication", "The Echo", "The Hound", "Sloth", "The Anvil", "Gluttony", "Overcharge" } )
			Inventory.PlayerInventory.GiveCommand( name, 3 );
		Shards( 200 );
		Heal();
	}

	// ------------------------------------------------------------------ shots

	private static int _shot;

	/// <summary>The screen as it is, HUD and all, to data/trailer/shots (Bitmap from the scene camera at the screen's size).</summary>
	public static void Screenshot()
	{
		var scene = Scene;
		var cam = scene?.Camera;
		if ( !cam.IsValid() ) return;
		var w = Math.Max( 320, (int)Screen.Width );
		var h = Math.Max( 180, (int)Screen.Height );
		var bmp = new Bitmap( w, h );
		try
		{
			cam.RenderToBitmap( bmp, !DevDirector.HudHidden );
			FileSystem.Data.CreateDirectory( "trailer/shots" );
			var name = $"trailer/shots/shot_{DateTime.Now:yyyyMMdd_HHmmss}_{_shot++:000}.png";
			FileSystem.Data.WriteAllBytes( name, bmp.ToPng() );
			Log.Info( $"[Director] shot saved: {name} ({w}x{h})" );
		}
		catch ( Exception e ) { Log.Warning( $"[Director] shot failed: {e.Message}" ); }
	}

	/// <summary>The engine's own high-resolution screenshot (twice the screen), to its screenshots folder.</summary>
	public static void ScreenshotHiRes() => Game.TakeHighResScreenshot( (int)Screen.Width * 2, (int)Screen.Height * 2 );

	// ------------------------------------------------------------------ the trailer intro

	/// <summary>
	/// The scene from the top, the main menu presses Play by itself, and the menu's walk does not stop at the eye:
	/// the camera keeps flying into the portal under its opening (MainMenuPanel.TrailerIntro), to black. Record it,
	/// then Back to menu.
	/// </summary>
	public static void PlayTrailerIntro() => ReloadScene( trailer: true );

	public static void BackToMenu() => ReloadScene( trailer: false );

	/// <summary>The scene the intro reloads: the project's startup scene (the published game's Scene.Source is not a scene file, so the path is what works there).</summary>
	[ConVar( "dev_intro_scene" )] public static string IntroScene { get; set; } = "scenes/adventure_demo.scene";

	private static void ReloadScene( bool trailer )
	{
		var scene = Scene;
		if ( !scene.IsValid() ) return;
		if ( !Networking.IsHost ) { Log.Warning( "[Director] only the host can reload the scene" ); return; }
		var options = new SceneLoadOptions { ShowLoadingScreen = false };
		var loaded = false;
		var name = "";
		// the scene's own file when it is one (the editor), else the source's path, else the project's startup scene
		if ( scene.Source is SceneFile file ) { options.SetScene( file ); loaded = true; name = file.ResourcePath; }
		if ( !loaded && scene.Source is { ResourcePath: { Length: > 0 } path } && path.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) ) { loaded = options.SetScene( path ); name = path; }
		if ( !loaded ) { loaded = options.SetScene( IntroScene ); name = IntroScene; }
		if ( !loaded ) { Log.Warning( $"[Director] no scene to reload: {IntroScene} was not found (dev_intro_scene)" ); return; }
		MainMenuPanel.TrailerIntro = trailer;
		MainMenuPanel.AutoPlay = trailer;
		RunManager.SkipMenu = false;
		DevDirector.SetPaused( false );
		DevDirector.SetSpeed( 1f );
		if ( WorldTape.Armed ) WorldTape.Disarm();
		if ( DevDirector.FreeCam ) DevDirector.SetFreeCam( false );
		DevPanel.SetOpen( false );
		Log.Info( $"[Director] reloading {name}{(trailer ? " for the trailer intro" : "")}" );
		if ( !Game.ChangeScene( options ) ) Log.Warning( "[Director] the scene change was refused" );
	}
}
