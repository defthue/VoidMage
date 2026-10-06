using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boss;

/// <summary>
/// The Bone Colossus's stage: the Hell hub of the Dungeon Realms demo scene, cut into prefabs/hell_arena.prefab by
/// tools/gen_boss_arena.py and edited in the editor from there. Built LOCALLY on every peer from one host broadcast
/// (the dungeon's own model: a static world is not networked, it is rebuilt from the same input), then the host
/// spawns the three boss parts at the prefab's marker children (`boss_skull`, `boss_hand_l`, `boss_hand_r`), sunk
/// into the ground, dormant. `spawn` is where a player lands.
///
/// Today a dev command builds it far from the dungeon (`boss_arena`); the run layer will call <see cref="Build"/>
/// on floor 13 in the dungeon's place.
/// </summary>
public static class HellArena
{
	/// <summary>The stage prefab the run builds on the boss floor. NOT saved (2026-09-24): a saved value from before the big-circle stage kept the published game on the old demo hub.</summary>
	[ConVar( "boss_arena_prefab" )] public static string PrefabPath { get; set; } = "prefabs/boss_stage.prefab";
	/// <summary>Swap every stage piece's base material for the hell variant at build time (the user's tuned look for the demo stage; off for a stage that carries its own materials).</summary>
	[ConVar( "boss_arena_hell_materials" )] public static bool HellMaterials { get; set; } = true;
	public const string SkullPrefab = "prefabs/boss_skull.prefab";
	public const string HandLPrefab = "prefabs/boss_hand_l.prefab";
	public const string HandRPrefab = "prefabs/boss_hand_r.prefab";

	/// <summary>Where `boss_arena` puts the stage: well clear of the dungeon, which sits around the origin.</summary>
	[ConVar( "boss_arena_x" )] public static float DevOriginX { get; set; } = -40000f;
	/// <summary>`boss_arena_autostart 1`: every time play starts, build the stage and put the player in it (saved between sessions; 0 to go back to the dungeon).</summary>
	[ConVar( "boss_arena_autostart", ConVarFlags.Saved )] public static bool AutoStart { get; set; }

	public static GameObject Root { get; private set; }
	public static Vector3 Origin { get; private set; }
	public static Transform Spawn { get; private set; }
	public static Transform SkullMarker { get; private set; }
	public static Transform HandLMarker { get; private set; }
	public static Transform HandRMarker { get; private set; }
	/// <summary>The fight's centre (the stage's `arena_center` marker; the platform's middle), the brain's roam and clamp centre.</summary>
	public static Vector3 ArenaCenter { get; private set; }
	/// <summary>The fighting floor's height: the markers sit on it.</summary>
	public static float FloorZ { get; private set; }
	public static bool Built => Root.IsValid();
	/// <summary>Host: the arena's navmesh is baked and answers (the summoned minions can path on it).</summary>
	public static bool NavReady { get; private set; }
	private static int _navGeneration;
	/// <summary>Half the side of the square the arena's navmesh covers around the spawn, world units.</summary>
	[ConVar( "boss_nav_half" )] public static float NavHalf { get; set; } = 3200f;

	/// <summary>
	/// Host: bakes the scene navmesh over the arena (the dungeon's bake covers only its own layout, and the
	/// arena stands 40k units away), the dungeon builder's recipe: bounds re-pointed, chunked, two in flight,
	/// then probed at the spawn. The minions of the summon walk on it; until it answers they steer straight.
	/// </summary>
	public static async System.Threading.Tasks.Task BakeNavMeshAsync( Scene scene )
	{
		if ( !Networking.IsHost || !Built || !scene.IsValid() ) return;
		var nav = scene.NavMesh;
		if ( nav is null ) return;
		var gen = ++_navGeneration;
		NavReady = false;
		var started = Time.Now;
		await GameTask.DelaySeconds( 0.1f );   // let the stage's colliders reach the physics world
		if ( !scene.IsValid() || !Built || gen != _navGeneration ) return;
		var c = Spawn.Position;
		var bounds = new BBox( c + new Vector3( -NavHalf, -NavHalf, -800f ), c + new Vector3( NavHalf, NavHalf, 900f ) );
		nav.IsEnabled = true;
		nav.CustomBounds = true;
		nav.Bounds = bounds;
		nav.AgentRadius = 12f;
		const float chunkSize = 1024f;
		var chunks = new List<BBox>();
		for ( var x = bounds.Mins.x; x < bounds.Maxs.x; x += chunkSize )
			for ( var y = bounds.Mins.y; y < bounds.Maxs.y; y += chunkSize )
				chunks.Add( new BBox( new Vector3( x, y, bounds.Mins.z ), new Vector3( MathF.Min( x + chunkSize, bounds.Maxs.x ), MathF.Min( y + chunkSize, bounds.Maxs.y ), bounds.Maxs.z ) ) );
		chunks.Sort( ( a, b ) => a.Center.WithZ( 0f ).DistanceSquared( c.WithZ( 0f ) ).CompareTo( b.Center.WithZ( 0f ).DistanceSquared( c.WithZ( 0f ) ) ) );
		var failed = 0;
		for ( var i = 0; i < chunks.Count; i += 2 )
		{
			var batch = new List<System.Threading.Tasks.Task>();
			for ( var j = i; j < Math.Min( i + 2, chunks.Count ); j++ )
			{
				var chunk = chunks[j];
				batch.Add( nav.GenerateTiles( scene.PhysicsWorld, chunk ) );
			}
			try { await GameTask.WhenAll( batch ); }
			catch ( Exception e ) { failed++; Log.Warning( $"[Arena] navmesh chunk failed: {e.Message}" ); }
			if ( !scene.IsValid() || !Built || gen != _navGeneration ) return;
		}
		var probe = nav.GetClosestPoint( c, 300f );
		NavReady = probe.HasValue;
		Log.Info( $"[Arena] navmesh: {chunks.Count} chunks ({failed} failed) in {Time.Now - started:0.0} s, spawn probe {(probe.HasValue ? "HIT" : "MISS")}" );
	}

	/// <summary>`boss_arena [clear|tp]`: host builds the stage on every peer and spawns the dormant boss; `tp` moves the local player to the spawn; `clear` tears it down.</summary>
	[ConCmd( "boss_arena" )]
	public static void Command( string arg = "" )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		if ( !Game.IsPlaying )
		{
			// in the editor's console the stage would be built into the edit scene and vanish on play (the scene
			// starts fresh); the player it teleports does not exist yet either
			Log.Warning( "[Arena] run this while PLAYING (press play, then boss_arena), or set boss_arena_autostart 1" );
			return;
		}
		switch ( arg.ToLowerInvariant() )
		{
			case "tp":
				TeleportLocalPlayer();
				return;
			case "clear":
				if ( !Networking.IsHost ) { Log.Warning( "[Arena] host only" ); return; }
				ClearRpc();
				return;
			default:
				if ( !Networking.IsHost ) { Log.Warning( "[Arena] host only (clients: boss_arena tp)" ); return; }
				BuildRpc( new Vector3( DevOriginX, 0f, 0f ) );
				SpawnBossHost( scene );
				_ = BakeNavMeshAsync( scene );
				TeleportLocalPlayer();
				return;
		}
	}

	[Rpc.Broadcast]
	private static void BuildRpc( Vector3 origin ) => Build( Game.ActiveScene, origin );

	[Rpc.Broadcast]
	private static void ClearRpc() => Clear();

	/// <summary>Every peer: the static stage prefab cloned at `origin`. Idempotent (a rebuild replaces the previous one).</summary>
	public static void Build( Scene scene, Vector3 origin )
	{
		Clear();
		var root = GameObject.Clone( PrefabPath, new CloneConfig
		{
			Transform = new Transform( origin ),
			StartEnabled = false,
			Name = "hell_arena",
		} );
		if ( !root.IsValid() ) { Log.Error( $"[Arena] {PrefabPath} did not clone" ); return; }
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.Enabled = true;
		Adopt( scene, root, owned: true );
	}

	/// <summary>The adopted stage belongs to the scene (a test scene's own object): Clear leaves it standing.</summary>
	private static bool _ownsRoot;

	/// <summary>
	/// Every peer: a stage root already in the scene becomes THE arena (the boss test scene: the stage is laid out
	/// and edited in place, or a prefab instance dropped in it). Markers are children named `spawn`, `boss_skull`,
	/// `boss_hand_l`, `boss_hand_r`; a missing one is placed relative to the spawn (skull 1500 ahead on the floor
	/// found by a trace, hands 1100 ahead and 700 to each side) so a bare stage still fights. `owned` = Clear may
	/// destroy it (a clone), else it is left standing.
	/// </summary>
	public static void Adopt( Scene scene, GameObject root, bool owned = false )
	{
		if ( !root.IsValid() ) return;
		if ( Root.IsValid() && Root != root ) Clear();
		Root = root;
		_ownsRoot = owned;
		Origin = root.WorldPosition;
		Spawn = Marker( root, "spawn", new Transform( Origin ) );
		var ahead = Spawn.Rotation.Forward.WithZ( 0f );
		if ( ahead.IsNearZeroLength ) ahead = Vector3.Forward;
		var left = Vector3.Cross( Vector3.Up, ahead ).Normal;
		Transform Relative( Vector3 offset )
		{
			var p = Spawn.Position + ahead * offset.x + left * offset.y;
			var tr = scene.Trace.Ray( p + Vector3.Up * 600f, p + Vector3.Down * 3000f ).WithoutTags( "player", "boss", "npc", "enemy", "debris" ).Run();
			return new Transform( tr.Hit ? tr.EndPosition + Vector3.Up * offset.z : p, Rotation.LookAt( -ahead ) );
		}
		SkullMarker = Marker( root, "boss_skull", Relative( new Vector3( 1500f, 0f, 60f ) ) );
		HandLMarker = Marker( root, "boss_hand_l", Relative( new Vector3( 1100f, 700f, 0f ) ) );
		HandRMarker = Marker( root, "boss_hand_r", Relative( new Vector3( 1100f, -700f, 0f ) ) );
		ArenaCenter = Marker( root, "arena_center", new Transform( (Spawn.Position + SkullMarker.Position) * 0.5f ) ).Position;
		FloorZ = Spawn.Position.z;   // the spawn stands on the fighting floor (the skull marker may sit on rocks above it)
		var lights = Light( root );
		EnterPresentation( scene );
		Log.Info( $"[Arena] {(owned ? "built" : "adopted")} at {Origin}: {root.Children.Count} objects, {lights} lava lights, spawn {Spawn.Position}, floor z {FloorZ:0}" );
	}

	// the dungeon's own atmosphere, switched off while the stage stands and put back when it goes
	private static readonly List<Dungeon.DungeonHaze> _hazeOff = new();
	private static Rendering.RetroFilter _retro;
	private static (bool on, float start, float end) _retroWas;

	/// <summary>
	/// The stage's look, the user's tuning (2026-09-12): the dungeon's haze motes off (they are a room's dust, not a
	/// caldera's), and the retro filter's depth fog ON from 3000 to 6000 units, which he found reads great here.
	/// </summary>
	private static void EnterPresentation( Scene scene )
	{
		foreach ( var haze in scene.GetAllComponents<Dungeon.DungeonHaze>() )
		{
			if ( !haze.IsValid() || !haze.Enabled ) continue;
			haze.Enabled = false;
			_hazeOff.Add( haze );
		}
		_retro = scene.GetAllComponents<Rendering.RetroFilter>().FirstOrDefault();
		if ( _retro.IsValid() )
		{
			_retroWas = (_retro.DepthFog, _retro.FogStart, _retro.FogEnd);
			_retro.DepthFog = true;
			_retro.FogStart = FogStart;
			_retro.FogEnd = FogEnd;
		}
	}

	private static void LeavePresentation()
	{
		foreach ( var haze in _hazeOff ) if ( haze.IsValid() ) haze.Enabled = true;
		_hazeOff.Clear();
		if ( _retro.IsValid() )
		{
			_retro.DepthFog = _retroWas.on;
			_retro.FogStart = _retroWas.start;
			_retro.FogEnd = _retroWas.end;
		}
		_retro = null;
	}

	/// <summary>The lava's brightness, a multiplier on every lava light (the user's pick, 2026-09-12: 3).</summary>
	[ConVar( "boss_lava_light" )] public static float LavaLightScale { get; set; } = 3f;
	/// <summary>The hell sun's brightness (a low, warm dusk sun). 0 by default: the user did not like it at all; the lava lights the stage.</summary>
	[ConVar( "boss_sun" )] public static float SunScale { get; set; } = 0f;
	/// <summary>The retro filter's depth fog while the arena stands (the user's find: 3000 to 6000 reads great here).</summary>
	[ConVar( "boss_fog_start" )] public static float FogStart { get; set; } = 3000f;
	[ConVar( "boss_fog_end" )] public static float FogEnd { get; set; } = 6000f;

	public const string ArenaMaterial = "shaders/realmsshaderhell.vmat";

	/// <summary>
	/// The stage lights itself: the dungeon scene has no sun and the toon shader has almost no ambient. Every arena
	/// piece's base material slot is swapped for the hell variant (a real ambient floor, warm, an ember rim; the
	/// outline slot stays), a low warm dusk sun throws long shadows, an orange shadowless point light sits over every
	/// lava mesh (a few big ones beat many small: the renderer drops lights past its per-tile budget), one enormous
	/// one over the lava sea the demo puts 27 m under the plates lights every cliff from below, embers drift off the
	/// lava, and a far dark-red haze stands in for the dungeon's fog.
	/// </summary>
	private static int Light( GameObject root )
	{
		var hell = Material.Load( ArenaMaterial );
		var n = 0;
		// a copy: the glow objects are parented under the same root, and adding to a list mid-enumeration throws
		foreach ( var child in root.GetAllObjects( true ).ToArray() )
		{
			if ( child == root ) continue;
			var renderer = child.Components.Get<ModelRenderer>();
			if ( renderer?.Model is null ) continue;
			if ( HellMaterials ) SwapBaseMaterial( renderer, hell );
			if ( !child.Name.Contains( "Lava", StringComparison.OrdinalIgnoreCase ) ) continue;
			// the outer tiles of a lava ring: lit by the inner ones, no light of their own (the renderer's per-tile light budget)
			if ( child.Tags.Has( "nolight" ) ) continue;
			var bounds = renderer.Model.Bounds.Transform( child.WorldTransform );
			var footprint = MathF.Max( bounds.Size.x, bounds.Size.y );
			var sea = footprint > 4000f;
			var top = new Vector3( bounds.Center.x, bounds.Center.y, bounds.Maxs.z );
			var go = new GameObject( root, true, child.Name + " glow" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			go.WorldPosition = top + Vector3.Up * (sea ? 300f : 120f);
			var light = go.Components.Create<PointLight>();
			light.Shadows = false;
			light.Radius = sea ? 9000f : Math.Clamp( footprint * 2.5f, 900f, 3000f );
			light.Attenuation = 1f;
			var brightness = (sea ? 2.5f : 1.6f) * LavaLightScale;
			var lava = go.Components.Create<LavaLight>();
			lava.BaseColor = new Color( 1f, 0.42f, 0.1f ) * brightness;
			lava.Phase = n * 1.7f;
			light.LightColor = lava.BaseColor;
			if ( !sea ) LavaLight.AddEmbers( go, bounds );
			n++;
		}
		// a hell dusk: low on the horizon for long shadows, warm, bright enough to read every silhouette
		if ( SunScale > 0f )
		{
			var sun = new GameObject( root, true, "hell sun" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			sun.WorldRotation = Rotation.From( 28f, 150f, 0f );
			var dir = sun.Components.Create<DirectionalLight>();
			dir.LightColor = new Color( 1f, 0.58f, 0.34f ) * 1.6f * SunScale;
			dir.Shadows = true;
			dir.SkyColor = new Color( 0.45f, 0.12f, 0.06f );
		}
		// the fog's replacement: nothing near, a dark red haze far out, so the far cliffs sink into ember-light
		var haze = root.Components.Create<GradientFog>();
		haze.Color = new Color( 0.24f, 0.05f, 0.02f );
		haze.StartDistance = 2500f;
		haze.EndDistance = 9000f;
		haze.FalloffExponent = 1.4f;
		haze.Height = 20000f;
		haze.VerticalFalloffExponent = 1f;
		return n;
	}

	/// <summary>Override the base material slot only (the one on the Realms atlas), never the outline shell's, so the toon line survives.</summary>
	public static void SwapBaseMaterial( ModelRenderer renderer, Material material )
	{
		if ( !renderer.IsValid() || material is null ) return;
		var mats = renderer.Materials;
		for ( var i = 0; i < mats.Count; i++ )
		{
			var path = mats.GetOriginal( i )?.ResourcePath ?? "";
			if ( path.Contains( "outline", StringComparison.OrdinalIgnoreCase ) ) continue;
			mats.SetOverride( i, material );
		}
	}

	private static Transform Marker( GameObject root, string name, Transform fallback )
	{
		var m = root.GetAllObjects( true ).FirstOrDefault( c => c != root && c.Name == name );
		if ( !m.IsValid() ) { Log.Warning( $"[Arena] no `{name}` marker in the stage; placed relative to the spawn" ); return fallback; }
		return m.WorldTransform;
	}

	public static void Clear()
	{
		if ( Networking.IsHost ) BossNet.Clear();
		// the death scene's slow motion never outlives the stage
		var scene = Game.ActiveScene;
		if ( scene.IsValid() ) scene.TimeScale = 1f;
		if ( Root.IsValid() )
		{
			if ( _ownsRoot ) Root.Destroy();
			LeavePresentation();
		}
		Root = null;
		_ownsRoot = false;
		NavReady = false;
		_navGeneration++;
	}

	/// <summary>Host: the three parts, dormant at the markers (sunk into the floor by the brain's dormant offsets).</summary>
	public static void SpawnBossHost( Scene scene )
	{
		if ( !Networking.IsHost || !Built ) return;
		BossNet.Clear();
		var skull = SpawnPart( SkullPrefab, "boss_skull", SkullMarker );
		var brain = skull.IsValid() ? skull.Components.Get<BossBrain>() : null;
		if ( brain.IsValid() ) brain.Configure( SkullMarker, HandLMarker, HandRMarker, FloorZ, ArenaCenter );
		skull?.NetworkSpawn( null );
		SpawnPart( HandLPrefab, "boss_hand_l", HandLMarker )?.NetworkSpawn( null );
		SpawnPart( HandRPrefab, "boss_hand_r", HandRMarker )?.NetworkSpawn( null );
	}

	private static GameObject SpawnPart( string prefab, string name, Transform at )
	{
		var go = GameObject.Clone( prefab, new CloneConfig { Transform = at, StartEnabled = true, Name = name } );
		if ( !go.IsValid() ) { Log.Error( $"[Arena] {prefab} did not clone" ); return null; }
		go.Flags |= GameObjectFlags.NotSaved;
		foreach ( var tag in new[] { "enemy", "npc", "boss" } )
			if ( !go.Tags.Has( tag ) ) go.Tags.Add( tag );
		return go;
	}

	// ------------------------------------------------------------------ the stage's hazards, every peer, its own player

	/// <summary>Standing in lava (a collider tagged `lava` under the feet): this much damage every `boss_lava_tick` seconds.</summary>
	[ConVar( "boss_lava_damage", ConVarFlags.Saved )] public static float LavaDamage { get; set; } = 2f;
	[ConVar( "boss_lava_tick", ConVarFlags.Saved )] public static float LavaTick { get; set; } = 0.2f;
	/// <summary>The body is kept out of the boss's hit shapes (0 turns the push-out off).</summary>
	[ConVar( "boss_body_pushout", ConVarFlags.Saved )] public static bool PushOut { get; set; } = true;

	private static float _nextLavaAt;
	private static bool _inLava;

	/// <summary>Every frame while the stage stands: the lava tick, and the push out of the boss's parts.</summary>
	internal static void TickHazards()
	{
		if ( !Built || !Game.IsPlaying ) return;
		var p = PlayerHealth.Local;
		if ( !p.IsValid() || p.IsProxy || p.IsDead ) return;
		var scene = p.Scene;
		var feet = p.FeetPosition;

		// lava: what the feet stand on (or sink into)
		var tr = scene.Trace.Ray( feet + Vector3.Up * 30f, feet + Vector3.Down * 50f ).WithoutTags( "player", "boss", "npc", "enemy", "debris", "spellfire" ).Run();
		var inLava = tr.Hit && tr.GameObject.IsValid() && tr.GameObject.Tags.Has( "lava" );
		// a won fight does not burn: the victory hold and the way home are not a place to die
		if ( inLava && BossBrain.Current is not { Phase: BossPhase.Dead } )
		{
			if ( !_inLava ) _nextLavaAt = Time.Now;
			if ( Time.Now >= _nextLavaAt )
			{
				_nextLavaAt = Time.Now + MathF.Max( 0.05f, LavaTick );
				p.Scald( LavaDamage, feet, "trap_lava" );
			}
		}
		_inLava = inLava;

		// the parts: the collision rules let them through bodies, so the body is pushed out of them here (never a
		// body in the fist: the grab places it inside the hand every frame, and pushing it out again made the hold shake)
		if ( !PushOut || p.Held ) return;
		var h = p.BodyHeight;
		var delta = Vector3.Zero;
		// the shapes are padded for the sword (BossAttack.HitForgive is that pad): the push-out uses the bone itself
		var shrink = BossAttack.HitForgive;
		// a part in its strike is not pushed against: the push-out kept the body a few units inside the padded shape
		// while the hit needs it inside the bone, so a palm thrust bulldozed the player along without ever landing
		var brain = BossBrain.Current;
		foreach ( var part in BossNet.Parts )
		{
			if ( !part.IsValid() || part.Dead ) continue;
			if ( brain.IsValid() && brain.IsStriking( part.Kind ) ) continue;
			delta += part.Resolve( feet + Vector3.Up * (h * 0.5f) + delta, 16f, shrink );
			delta += part.Resolve( feet + Vector3.Up * (h - 8f) + delta, 12f, shrink );
		}
		if ( delta.IsNearZeroLength ) return;
		if ( delta.Length > 90f ) delta = delta.Normal * 90f;
		p.Nudge( delta );
	}

	private static bool _autoStartRefused;

	/// <summary>Host, once per play session when `boss_arena_autostart` is set: the same as typing `boss_arena` the moment the player exists.</summary>
	internal static void TickAutoStart()
	{
		if ( !AutoStart || Built || !Networking.IsHost || !Game.IsPlaying ) return;
		// the run loop builds the arena itself at floor 13; a saved `boss_arena_autostart 1` must not hijack its scene
		if ( Run.RunManager.Instance.IsValid() )
		{
			if ( !_autoStartRefused ) Log.Info( "[Arena] boss_arena_autostart ignored: this scene has a RunManager (use run_start 13)" );
			_autoStartRefused = true;
			return;
		}
		var health = PlayerHealth.Local;
		if ( !health.IsValid() || !health.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ).IsValid() ) return;
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		BuildRpc( new Vector3( DevOriginX, 0f, 0f ) );
		SpawnBossHost( scene );
		_ = BakeNavMeshAsync( scene );
		TeleportLocalPlayer();
	}

	public static void TeleportLocalPlayer()
	{
		if ( !Built ) { Log.Warning( "[Arena] not built" ); return; }
		var health = PlayerHealth.Local;
		var controller = health.IsValid() ? health.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
		if ( !controller.IsValid() ) { Log.Warning( "[Arena] no local player" ); return; }
		controller.WorldPosition = Spawn.Position;
		if ( controller.Body.IsValid() ) controller.Body.Velocity = Vector3.Zero;
		var look = (SkullMarker.Position - Spawn.Position).WithZ( 0f );
		if ( !look.IsNearZeroLength ) controller.EyeAngles = Rotation.LookAt( look.Normal ).Angles();
	}
}

/// <summary>Drives the `boss_arena_autostart` convenience: a few frames into play, once the dungeon has spawned the player, the stage is built and the player moved into it.</summary>
public sealed class HellArenaSystem : GameObjectSystem
{
	public HellArenaSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 5, Tick, "HellArena" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		HellArena.TickAutoStart();
		HellArena.TickHazards();
	}
}
